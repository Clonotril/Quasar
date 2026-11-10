using ProtoBuf;

namespace Quasar.Common.Messages
{
    [ProtoContract]
    public class DoChatResponse : IMessage
    {
        [ProtoMember(1)]
        public string Message { get; set; }

        [ProtoMember(2)]
        public string SenderName { get; set; }
    }
}
