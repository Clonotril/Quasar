using ProtoBuf;

namespace Quasar.Common.Messages
{
    [ProtoContract]
    public class GetAudioOutputDevicesResponse : IMessage
    {
        [ProtoMember(1)]
        public string[] DeviceNames { get; set; }

        [ProtoMember(2)]
        public string[] DeviceIds { get; set; }
    }
}
