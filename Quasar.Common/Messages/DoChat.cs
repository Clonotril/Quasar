using ProtoBuf;

namespace Quasar.Common.Messages
{
    [ProtoContract]
    public class DoChat : IMessage
    {
        [ProtoMember(1)]
        public string Message { get; set; }

        [ProtoMember(2)]
        public string SenderName { get; set; }
    }
}
