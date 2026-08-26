using System;

namespace RsLoopTest
{
    internal sealed class PayloadGenerator
    {
        private readonly LoopDataOptions options;
        private readonly Random random;

        public PayloadGenerator(LoopDataOptions options)
            : this(options, new Random(unchecked(Environment.TickCount ^ Guid.NewGuid().GetHashCode())))
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
        }

        public byte[] CreateNext()
        {
            int length = options.FrameLength;
            if (options.RandomFrameLength)
            {
                int index = random.Next(LoopDataOptions.AllowedFrameLengths.Length);
                length = LoopDataOptions.AllowedFrameLengths[index];
            }

            byte[] payload = new byte[length];
            if (options.RandomContent)
            {
                random.NextBytes(payload);
                return payload;
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
            return payload;
        }
    }
}
