using ProtoBuf;

namespace Quasar.Common.Messages
{
    [ProtoContract]
    public class GetAudioCaptureResponse : IMessage
    {
        [ProtoMember(1)]
        public byte[] Audio { get; set; }

        [ProtoMember(2)]
        public int SampleRate { get; set; }

        [ProtoMember(3)]
        public int Channels { get; set; }

        [ProtoMember(4)]
        public int BitsPerSample { get; set; }

        [ProtoMember(5)]
        public int DeviceIndex { get; set; }
    }
}
