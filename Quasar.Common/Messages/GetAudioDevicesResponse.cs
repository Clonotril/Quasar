using ProtoBuf;

namespace Quasar.Common.Messages
{
    [ProtoContract]
    public class GetAudioDevicesResponse : IMessage
    {
        [ProtoMember(1)]
        public string[] DeviceNames { get; set; }
    }
}
