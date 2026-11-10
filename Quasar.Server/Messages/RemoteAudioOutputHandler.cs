using Quasar.Common.Messages;
using Quasar.Common.Networking;
using Quasar.Server.Networking;
using System;

namespace Quasar.Server.Messages
{
    /// <summary>
    /// Handles messages for the interaction with the remote audio output (system sound / speaker loopback).
    /// </summary>
    public class RemoteAudioOutputHandler : MessageProcessorBase<byte[]>, IDisposable
    {
        /// <summary>
        /// States if the client is currently streaming audio output.
        /// </summary>
        public bool IsStarted { get; set; }

        /// <summary>
        /// The current audio sample rate.
        /// </summary>
        public int SampleRate { get; private set; }

        /// <summary>
        /// The current number of audio channels.
        /// </summary>
        public int Channels { get; private set; }

        /// <summary>
        /// The current bits per sample.
        /// </summary>
        public int BitsPerSample { get; private set; }

        /// <summary>
        /// Used in lock statements to synchronize access between UI thread and thread pool.
        /// </summary>
        private readonly object _syncLock = new object();

        /// <summary>
        /// Represents the method that will handle audio output device changes.
        /// </summary>
        /// <param name="sender">The message processor which raised the event.</param>
        /// <param name="deviceNames">All currently available audio output devices.</param>
        public delegate void AudioOutputDevicesChangedEventHandler(object sender, string[] deviceNames);

        /// <summary>
        /// Raised when the list of audio output devices has been received.
        /// </summary>
        public event AudioOutputDevicesChangedEventHandler AudioOutputDevicesChanged;

        /// <summary>
        /// Reports changed audio output devices.
        /// </summary>
        private void OnAudioOutputDevicesChanged(string[] deviceNames)
        {
            SynchronizationContext.Post(val =>
            {
                var handler = AudioOutputDevicesChanged;
                handler?.Invoke(this, (string[])val);
            }, deviceNames);
        }

        /// <summary>
        /// The client which is associated with this handler.
        /// </summary>
        private readonly Client _client;

        /// <summary>
        /// Initializes a new instance of the <see cref="RemoteAudioOutputHandler"/> class.
        /// </summary>
        /// <param name="client">The associated client.</param>
        public RemoteAudioOutputHandler(Client client) : base(true)
        {
            _client = client;
        }

        /// <inheritdoc />
        public override bool CanExecute(IMessage message) => message is GetAudioOutputDevicesResponse ||
                                                              message is GetAudioOutputCaptureResponse;

        /// <inheritdoc />
        public override bool CanExecuteFrom(ISender sender) => _client.Equals(sender);

        /// <inheritdoc />
        public override void Execute(ISender sender, IMessage message)
        {
            switch (message)
            {
                case GetAudioOutputDevicesResponse d:
                    Execute(sender, d);
                    break;
                case GetAudioOutputCaptureResponse a:
                    Execute(sender, a);
                    break;
            }
        }

        /// <summary>
        /// Begins receiving audio output from the client.
        /// </summary>
        /// <param name="deviceIndex">The audio output device index.</param>
        public void BeginReceiveAudio(int deviceIndex)
        {
            lock (_syncLock)
            {
                IsStarted = true;
                _client.Send(new GetAudioOutputCapture
                {
                    CreateNew = true,
                    DeviceIndex = deviceIndex
                });
            }
        }

        /// <summary>
        /// Ends receiving audio output from the client.
        /// </summary>
        public void EndReceiveAudio()
        {
            lock (_syncLock)
            {
                IsStarted = false;
                _client.Send(new DoAudioOutputCaptureStop());
            }
        }

        /// <summary>
        /// Refreshes the available audio output devices of the client.
        /// </summary>
        public void RefreshAudioOutputDevices()
        {
            _client.Send(new GetAudioOutputDevices());
        }

        private void Execute(ISender client, GetAudioOutputDevicesResponse message)
        {
            OnAudioOutputDevicesChanged(message.DeviceNames ?? new string[0]);
        }

        private void Execute(ISender client, GetAudioOutputCaptureResponse message)
        {
            lock (_syncLock)
            {
                if (!IsStarted)
                    return;

                if (message.Audio != null && message.Audio.Length > 0)
                {
                    SampleRate = message.SampleRate;
                    Channels = message.Channels;
                    BitsPerSample = message.BitsPerSample;

                    OnReport(message.Audio);
                }
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
