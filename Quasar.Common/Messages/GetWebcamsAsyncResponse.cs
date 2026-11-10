using ProtoBuf;

namespace Quasar.Common.Messages
{
    [ProtoContract]
    public class GetWebcamsAsyncResponse : IMessage
    {
        [ProtoMember(1)]
        public string[] WebcamNames { get; set; }
    }
}
