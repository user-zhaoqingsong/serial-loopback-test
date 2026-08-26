namespace RsLoopTest
{
    internal sealed class LoopFrame
    {
        public byte Version { get; set; }
        public PayloadPattern Pattern { get; set; }
        public int PayloadLength { get; set; }
        public uint Sequence { get; set; }
        public uint Seed { get; set; }
        public bool FrameCrcValid { get; set; }
        public byte[] Payload { get; set; }
        public byte[] RawBytes { get; set; }
    }

    internal sealed class FrameParseBatch
    {
        public FrameParseBatch()
        {
            Frames = new System.Collections.Generic.List<LoopFrame>();
        }

        public System.Collections.Generic.List<LoopFrame> Frames { get; private set; }
        public long DiscardedBytes { get; set; }
        public long HeaderErrors { get; set; }
    }
}
