using System;

namespace RsLoopTest
{
    internal sealed class PayloadGenerator
    {
        private readonly LoopDataOptions options;
        private readonly Random random;
        private uint streamState;

        public PayloadGenerator(LoopDataOptions options)
            : this(options, new Random(unchecked((int)options.DataSeed)))
        {
        }

        internal PayloadGenerator(LoopDataOptions options, Random random)
        {
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }
            if (random == null)
            {
                throw new ArgumentNullException("random");
            }

            options.Validate();
            this.options = options.Clone();
            this.random = random;
            streamState = options.DataSeed == 0 ? 1u : options.DataSeed;
        }

        public byte[] CreateNext()
        {
            return CreateNextFrame().Data;
        }

        public GeneratedPayload CreateNextFrame()
        {
            int length = options.FrameLength;
            if (options.RandomFrameLength)
            {
                int index = random.Next(LoopDataOptions.AllowedFrameLengths.Length);
                length = LoopDataOptions.AllowedFrameLengths[index];
            }

            byte[] payload = new byte[length];
            uint startSeed = streamState;
            if (options.RandomContent)
            {
                for (int index = 0; index < payload.Length; index++)
                {
                    payload[index] = NextXorShiftByte();
                }
                return new GeneratedPayload { Data = payload, StartSeed = startSeed };
            }

            if (options.Pattern == PayloadPattern.Prbs7 ||
                options.Pattern == PayloadPattern.Prbs15 ||
                options.Pattern == PayloadPattern.Prbs31)
            {
                int order = options.Pattern == PayloadPattern.Prbs7 ? 7 :
                    (options.Pattern == PayloadPattern.Prbs15 ? 15 : 31);
                streamState = NormalizePrbsSeed(streamState, order);
                startSeed = streamState;
                for (int index = 0; index < payload.Length; index++)
                {
                    payload[index] = NextPrbsByte(order);
                }
                return new GeneratedPayload { Data = payload, StartSeed = startSeed };
            }

            for (int index = 0; index < payload.Length; index++)
            {
                switch (options.Pattern)
                {
                    case PayloadPattern.Fixed55:
                        payload[index] = 0x55;
                        break;
                    case PayloadPattern.FixedAA:
                        payload[index] = 0xAA;
                        break;
                    case PayloadPattern.Alternating55AA:
                        payload[index] = index % 2 == 0 ? (byte)0x55 : (byte)0xAA;
                        break;
                    case PayloadPattern.CustomRepeat:
                        payload[index] = options.CustomPattern[index % options.CustomPattern.Length];
                        break;
                    default:
                        payload[index] = (byte)(index & 0xFF);
                        break;
                }
            }
            return new GeneratedPayload { Data = payload, StartSeed = options.DataSeed };
        }

        private byte NextXorShiftByte()
        {
            uint value = streamState == 0 ? 0x6D2B79F5u : streamState;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            streamState = value;
            return (byte)value;
        }

        private byte NextPrbsByte(int order)
        {
            byte result = 0;
            for (int bitIndex = 0; bitIndex < 8; bitIndex++)
            {
                int secondTap = order == 31 ? 27 : order - 2;
                uint nextBit = ((streamState >> (order - 1)) ^
                    (streamState >> secondTap)) & 1u;
                uint mask = order == 31 ? 0x7FFFFFFFu : ((1u << order) - 1u);
                streamState = ((streamState << 1) | nextBit) & mask;
                result |= (byte)((streamState & 1u) << bitIndex);
            }
            return result;
        }

        private static uint NormalizePrbsSeed(uint value, int order)
        {
            uint mask = order == 31 ? 0x7FFFFFFFu : ((1u << order) - 1u);
            uint normalized = value & mask;
            return normalized == 0 ? 1u : normalized;
        }
    }
}
