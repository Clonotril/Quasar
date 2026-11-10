using Quasar.Common.Messages;
using Quasar.Server.Helper;
using Quasar.Server.Messages;
using Quasar.Server.Networking;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Quasar.Server.Forms
{
    public partial class FrmRemoteChat : Form
    {
        /// <summary>
        /// The client which can be used for the remote chat.
        /// </summary>
        private readonly Client _connectClient;

        /// <summary>
        /// The message handler for handling the communication with the client.
        /// </summary>
        private readonly RemoteChatHandler _remoteChatHandler;

        /// <summary>
        /// Holds the opened remote chat form for each client.
        /// </summary>
        private static readonly Dictionary<Client, FrmRemoteChat> OpenedForms = new Dictionary<Client, FrmRemoteChat>();

        /// <summary>
        /// Creates a new remote chat form for the client or gets the current open form, if there exists one already.
        /// </summary>
        /// <param name="client">The client used for the remote chat form.</param>
        /// <returns>
        /// Returns a new remote chat form for the client if there is none currently open, otherwise returns the existing one.
        /// </returns>
        public static FrmRemoteChat CreateNewOrGetExisting(Client client)
        {
            if (OpenedForms.ContainsKey(client))
            {
                return OpenedForms[client];
            }
            FrmRemoteChat f = new FrmRemoteChat(client);
            f.Disposed += (sender, args) => OpenedForms.Remove(client);
            OpenedForms.Add(client, f);
            return f;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FrmRemoteChat"/> class using the given client.
        /// </summary>
        /// <param name="client">The client used for the remote chat form.</param>
        public FrmRemoteChat(Client client)
        {
            _connectClient = client;
            _remoteChatHandler = new RemoteChatHandler(client);

            RegisterMessageHandler();
            InitializeComponent();
        }

        /// <summary>
        /// Called whenever a client disconnects.
        /// </summary>
        private void ClientDisconnected(Client client, bool connected)
        {
            if (!connected)
            {
                this.Invoke((MethodInvoker)this.Close);
            }
        }

        /// <summary>
        /// Registers the remote chat message handler for client communication.
        /// </summary>
        private void RegisterMessageHandler()
        {
            _connectClient.ClientState += ClientDisconnected;
            _remoteChatHandler.ProgressChanged += ChatMessageReceived;
            MessageHandler.Register(_remoteChatHandler);
        }

        /// <summary>
        /// Unregisters the remote chat message handler.
        /// </summary>
        private void UnregisterMessageHandler()
        {
            MessageHandler.Unregister(_remoteChatHandler);
            _remoteChatHandler.ProgressChanged -= ChatMessageReceived;
            _connectClient.ClientState -= ClientDisconnected;
        }

        /// <summary>
        /// Called whenever a chat message is received from the client.
        /// </summary>
        private void ChatMessageReceived(object sender, string value)
        {
            // value format: "SenderName|Message"
            string senderName = "Client";
            string message = value;

            int separatorIndex = value.IndexOf('|');
            if (separatorIndex > 0)
            {
                senderName = value.Substring(0, separatorIndex);
                message = value.Substring(separatorIndex + 1);
            }

            AppendMessage(senderName, message, Color.DarkGreen);
        }

        /// <summary>
        /// Appends a message to the chat log. Thread-safe.
        /// </summary>
        private void AppendMessage(string sender, string message, Color senderColor)
        {
            if (rtxtChat.InvokeRequired)
            {
                rtxtChat.Invoke((MethodInvoker)(() => AppendMessage(sender, message, senderColor)));
                return;
            }

            rtxtChat.SelectionStart = rtxtChat.TextLength;
            rtxtChat.SelectionLength = 0;

            // Sender name in bold
            rtxtChat.SelectionFont = new Font(rtxtChat.Font, FontStyle.Bold);
            rtxtChat.SelectionColor = senderColor;
            rtxtChat.AppendText(sender + ": ");

            // Message text in normal
            rtxtChat.SelectionFont = new Font(rtxtChat.Font, FontStyle.Regular);
            rtxtChat.SelectionColor = Color.Black;
            rtxtChat.AppendText(message + Environment.NewLine);

            rtxtChat.ScrollToCaret();
        }

        private void FrmRemoteChat_Load(object sender, EventArgs e)
        {
            this.Text = WindowHelper.GetWindowTitle("Remote Chat", _connectClient);
        }

        private void FrmRemoteChat_FormClosing(object sender, FormClosingEventArgs e)
        {
            UnregisterMessageHandler();
            _remoteChatHandler.Dispose();
        }

        private void btnSend_Click(object sender, EventArgs e)
        {
            SendMessage();
        }

        private void txtInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                SendMessage();
            }
        }

        private void SendMessage()
        {
            string text = txtInput.Text.Trim();
            if (string.IsNullOrEmpty(text))
                return;

            _remoteChatHandler.SendChatMessage(text, "Server");
            AppendMessage("Me", text, Color.Blue);
            txtInput.Clear();
            txtInput.Focus();
        }
    }
}
