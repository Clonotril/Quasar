using ProtoBuf;

namespace Quasar.Common.Messages
{
    [ProtoContract]
    public class GetWebcamFrameAsync : IMessage
    {
        [ProtoMember(1)]
        public bool CreateNew { get; set; }

        [ProtoMember(2)]
        public int DeviceIndex { get; set; }

        [ProtoMember(3)]
        public int Quality { get; set; }
    }
}
