using System;
using System.Collections.Generic;

namespace RsLoopTest
{
    internal sealed class LoopFrameParser
    {
        private readonly List<byte> buffer = new List<byte>();

        public FrameParseBatch Append(byte[] input, int count)
        {
            if (input == null)
            {
                throw new ArgumentNullException("input");
            }
            if (count < 0 || count > input.Length)
            {
                throw new ArgumentOutOfRangeException("count");
            }

            for (int index = 0; index < count; index++)
            {
                buffer.Add(input[index]);
            }

            FrameParseBatch batch = new FrameParseBatch();
            ParseAvailable(batch);
            return batch;
        }

        public void Clear()
        {
            buffer.Clear();
        }

        private void ParseAvailable(FrameParseBatch batch)
        {
            while (true)
            {
                int magicIndex = FindMagic();
                if (magicIndex < 0)
                {
                    int keep = MatchingMagicPrefixAtEnd();
                    int discard = buffer.Count - keep;
                    if (discard > 0)
                    {
                        buffer.RemoveRange(0, discard);
                        batch.DiscardedBytes += discard;
                    }
                    return;
                }

                if (magicIndex > 0)
                {
                    buffer.RemoveRange(0, magicIndex);
                    batch.DiscardedBytes += magicIndex;
                }

                if (buffer.Count < LoopFrameCodec.HeaderLength)
                {
                    return;
                }

                byte[] header = buffer.GetRange(0, LoopFrameCodec.HeaderLength).ToArray();
                int payloadLength = LoopFrameCodec.ReadUInt16(header, 6);
                uint expectedHeaderCrc = LoopFrameCodec.ReadUInt32(header, 16);
                bool headerValid = header[4] == LoopFrameCodec.ProtocolVersion &&
                    payloadLength > 0 && payloadLength <= LoopFrameCodec.MaximumPayloadLength &&
                    Crc32.Compute(header, 0, 16) == expectedHeaderCrc;

                if (!headerValid)
                {
                    buffer.RemoveAt(0);
                    batch.DiscardedBytes++;
                    batch.HeaderErrors++;
                    continue;
                }

                int frameLength = LoopFrameCodec.HeaderLength + payloadLength +
                    LoopFrameCodec.TrailerLength;
                if (buffer.Count < frameLength)
                {
                    return;
                }

                byte[] raw = buffer.GetRange(0, frameLength).ToArray();
                buffer.RemoveRange(0, frameLength);
                byte[] payload = new byte[payloadLength];
                Array.Copy(raw, LoopFrameCodec.HeaderLength, payload, 0, payloadLength);
                uint expectedFrameCrc = LoopFrameCodec.ReadUInt32(raw,
                    LoopFrameCodec.HeaderLength + payloadLength);

                batch.Frames.Add(new LoopFrame
                {
                    Version = raw[4],
                    Pattern = (PayloadPattern)raw[5],
                    PayloadLength = payloadLength,
                    Sequence = LoopFrameCodec.ReadUInt32(raw, 8),
                    Seed = LoopFrameCodec.ReadUInt32(raw, 12),
                    FrameCrcValid = Crc32.Compute(raw, 0,
                        LoopFrameCodec.HeaderLength + payloadLength) == expectedFrameCrc,
                    Payload = payload,
                    RawBytes = raw
                });
            }
        }

        private int FindMagic()
        {
            for (int index = 0; index <= buffer.Count - LoopFrameCodec.Magic.Length; index++)
            {
                bool matches = true;
                for (int magicIndex = 0; magicIndex < LoopFrameCodec.Magic.Length; magicIndex++)
                {
                    if (buffer[index + magicIndex] != LoopFrameCodec.Magic[magicIndex])
                    {
                        matches = false;
                        break;
                    }
                }
                if (matches)
                {
                    return index;
                }
            }
            return -1;
        }

        private int MatchingMagicPrefixAtEnd()
        {
            int maximum = Math.Min(buffer.Count, LoopFrameCodec.Magic.Length - 1);
            for (int length = maximum; length > 0; length--)
            {
                bool matches = true;
                for (int index = 0; index < length; index++)
                {
                    if (buffer[buffer.Count - length + index] != LoopFrameCodec.Magic[index])
                    {
                        matches = false;
                        break;
                    }
                }
                if (matches)
                {
                    return length;
                }
            }
            return 0;
        }
    }
}
