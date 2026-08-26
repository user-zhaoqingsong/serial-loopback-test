using System;
using System.Collections.Generic;

namespace RsLoopTest
{
    internal sealed class FrameBuffer
    {
        private readonly int frameLength;
        private readonly List<byte> bytes = new List<byte>();

        public FrameBuffer(int frameLength)
        {
            if (frameLength <= 0)
            {
                throw new ArgumentOutOfRangeException("frameLength");
            }
            this.frameLength = frameLength;
        }

        public void Append(byte[] input, int count)
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
                bytes.Add(input[index]);
            }
        }

        public bool TryTakeFrame(out byte[] frame)
        {
            if (bytes.Count < frameLength)
            {
                frame = null;
                return false;
            }

            frame = bytes.GetRange(0, frameLength).ToArray();
            bytes.RemoveRange(0, frameLength);
            return true;
        }

        public void Clear()
        {
            bytes.Clear();
        }
    }
}
