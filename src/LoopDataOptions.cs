using System;

namespace RsLoopTest
{
    internal enum PayloadPattern
    {
        Incrementing,
        Fixed55,
        FixedAA,
        Alternating55AA,
        CustomRepeat,
        Prbs7,
        Prbs15,
        Prbs31
    }

    internal sealed class LoopDataOptions
    {
        public static readonly int[] AllowedFrameLengths = { 20, 40, 60, 80, 100 };

        public PayloadPattern Pattern { get; set; }
        public int FrameLength { get; set; }
        public bool RandomContent { get; set; }
        public bool RandomFrameLength { get; set; }
        public byte[] CustomPattern { get; set; }
        public uint DataSeed { get; set; }

        public void Validate()
        {
            bool lengthAllowed = false;
            foreach (int length in AllowedFrameLengths)
            {
                if (FrameLength == length)
                {
                    lengthAllowed = true;
                    break;
                }
            }

            if (!lengthAllowed)
            {
                throw new ArgumentException("帧长必须选择 20、40、60、80 或 100 字节。");
            }
            if (!RandomContent && Pattern == PayloadPattern.CustomRepeat &&
                (CustomPattern == null || CustomPattern.Length == 0))
            {
                throw new ArgumentException("自定义预设内容不能为空。");
            }
        }

        public LoopDataOptions Clone()
        {
            return new LoopDataOptions
            {
                Pattern = Pattern,
                FrameLength = FrameLength,
                RandomContent = RandomContent,
                RandomFrameLength = RandomFrameLength,
                CustomPattern = CustomPattern == null ? null : (byte[])CustomPattern.Clone(),
                DataSeed = DataSeed
            };
        }

        public string Describe()
        {
            string content = RandomContent ? "内容随机" : GetPatternName(Pattern);
            string length = RandomFrameLength ? "帧长随机（20/40/60/80/100）" : FrameLength + " 字节";
            return content + "，" + length;
        }

        private static string GetPatternName(PayloadPattern pattern)
        {
            switch (pattern)
            {
                case PayloadPattern.Fixed55:
                    return "全 55";
                case PayloadPattern.FixedAA:
                    return "全 AA";
                case PayloadPattern.Alternating55AA:
                    return "55/AA 交替";
                case PayloadPattern.CustomRepeat:
                    return "自定义 HEX 循环";
                case PayloadPattern.Prbs7:
                    return "PRBS7";
                case PayloadPattern.Prbs15:
                    return "PRBS15";
                case PayloadPattern.Prbs31:
                    return "PRBS31";
                default:
                    return "递增 00-FF";
            }
        }
    }
}
