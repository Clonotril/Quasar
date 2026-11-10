using Quasar.Common.Messages;
using Quasar.Common.Networking;
using Quasar.Server.Networking;
using System;
using System.Drawing;
using System.IO;

namespace Quasar.Server.Messages
{
    /// <summary>
    /// Handles messages for the interaction with the remote webcam (Async/DirectShow mode).
    /// </summary>
    public class RemoteWebcamAsyncHandler : MessageProcessorBase<Bitmap>, IDisposable
    {
        public bool IsStarted { get; set; }

        public bool HasReceivedFrame { get; private set; }

        private readonly object _syncLock = new object();

        private int _consecutiveEmptyFrames;

        public delegate void WebcamsChangedEventHandler(object sender, string[] webcamNames);

        public delegate void StatusChangedEventHandler(object sender, string statusText);

        public event WebcamsChangedEventHandler WebcamsChanged;

        public event StatusChangedEventHandler StatusChanged;

        private void OnWebcamsChanged(string[] webcamNames)
        {
            SynchronizationContext.Post(val =>
            {
                var handler = WebcamsChanged;
                handler?.Invoke(this, (string[])val);
            }, webcamNames);
        }

        private void OnStatusChanged(string statusText)
        {
            SynchronizationContext.Post(val =>
            {
                var handler = StatusChanged;
                handler?.Invoke(this, (string)val);
            }, statusText);
        }

        private readonly Client _client;

        public RemoteWebcamAsyncHandler(Client client) : base(true)
        {
            _client = client;
        }

        public override bool CanExecute(IMessage message) => message is GetWebcamsAsyncResponse ||
                                                              message is GetWebcamFrameAsyncResponse;

        public override bool CanExecuteFrom(ISender sender) => _client.Equals(sender);

        public override void Execute(ISender sender, IMessage message)
        {
            switch (message)
            {
                case GetWebcamsAsyncResponse w:
                    Execute(sender, w);
                    break;
                case GetWebcamFrameAsyncResponse f:
                    Execute(sender, f);
                    break;
            }
        }

        public void BeginReceiveFrames(int deviceIndex, int quality)
        {
            lock (_syncLock)
            {
                IsStarted = true;
                HasReceivedFrame = false;
                _consecutiveEmptyFrames = 0;
                _client.Send(new GetWebcamFrameAsync
                {
                    CreateNew = true,
                    DeviceIndex = deviceIndex,
                    Quality = quality
                });
            }
        }

        public void EndReceiveFrames()
        {
            lock (_syncLock)
            {
                IsStarted = false;
                _client.Send(new DoWebcamAsyncStop());
            }
        }

        public void RefreshWebcams()
        {
            _client.Send(new GetWebcamsAsync());
        }

        private void Execute(ISender client, GetWebcamsAsyncResponse message)
        {
            OnWebcamsChanged(message.WebcamNames ?? new string[0]);
        }

        private void Execute(ISender client, GetWebcamFrameAsyncResponse message)
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

                // Request next frame
                client.Send(new GetWebcamFrameAsync
                {
                    Quality = message.Quality,
                    DeviceIndex = message.DeviceIndex
                });
            }
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
                lock (_syncLock)
                {
                    IsStarted = false;
                }
            }
        }
    }
}
