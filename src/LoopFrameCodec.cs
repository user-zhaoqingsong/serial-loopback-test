using System;

namespace RsLoopTest
{
    internal static class LoopFrameCodec
    {
        public const byte ProtocolVersion = 1;
        public const int HeaderLength = 20;
        public const int TrailerLength = 4;
        public const int MaximumPayloadLength = 4096;
        public static readonly byte[] Magic = { 0x52, 0x53, 0x4C, 0x50 }; // RSLP

        public static byte[] Build(uint sequence, PayloadPattern pattern, uint seed, byte[] payload)
        {
            if (payload == null || payload.Length <= 0 || payload.Length > MaximumPayloadLength)
            {
                throw new ArgumentOutOfRangeException("payload");
            }

            byte[] frame = new byte[HeaderLength + payload.Length + TrailerLength];
            Array.Copy(Magic, 0, frame, 0, Magic.Length);
            frame[4] = ProtocolVersion;
            frame[5] = (byte)pattern;
            WriteUInt16(frame, 6, (ushort)payload.Length);
            WriteUInt32(frame, 8, sequence);
            WriteUInt32(frame, 12, seed);
            WriteUInt32(frame, 16, Crc32.Compute(frame, 0, 16));
            Array.Copy(payload, 0, frame, HeaderLength, payload.Length);
            WriteUInt32(frame, HeaderLength + payload.Length,
                Crc32.Compute(frame, 0, HeaderLength + payload.Length));
            return frame;
        }

        public static ushort ReadUInt16(byte[] data, int offset)
        {
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        public static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)(data[offset] |
                (data[offset + 1] << 8) |
                (data[offset + 2] << 16) |
                (data[offset + 3] << 24));
        }

        public static void WriteUInt16(byte[] data, int offset, ushort value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
        }

        public static void WriteUInt32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16);
            data[offset + 3] = (byte)(value >> 24);
        }
    }
}
