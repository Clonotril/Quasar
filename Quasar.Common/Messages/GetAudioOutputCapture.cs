using ProtoBuf;

namespace Quasar.Common.Messages
{
    [ProtoContract]
    public class GetAudioOutputCapture : IMessage
    {
        [ProtoMember(1)]
        public bool CreateNew { get; set; }

        [ProtoMember(2)]
        public int DeviceIndex { get; set; }
    }
}
