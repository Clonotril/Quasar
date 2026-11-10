using ProtoBuf;

namespace Quasar.Common.Messages
{
    [ProtoContract]
    public class GetWebcamFrameResponse : IMessage
    {
        [ProtoMember(1)]
        public byte[] Image { get; set; }

        [ProtoMember(2)]
        public int DeviceIndex { get; set; }

        [ProtoMember(3)]
        public int Quality { get; set; }

        [ProtoMember(4)]
        public int Width { get; set; }

        [ProtoMember(5)]
        public int Height { get; set; }
    }
}
