using System;

namespace RsLoopTest
{
    internal static class Crc32
    {
        private const uint Polynomial = 0xEDB88320u;
        private static readonly uint[] Table = BuildTable();

        public static uint Compute(byte[] data, int offset, int count)
        {
            if (data == null)
            {
                throw new ArgumentNullException("data");
            }
            if (offset < 0 || count < 0 || offset + count > data.Length)
            {
                throw new ArgumentOutOfRangeException("offset");
            }

            uint crc = 0xFFFFFFFFu;
            for (int index = offset; index < offset + count; index++)
            {
                crc = Table[(crc ^ data[index]) & 0xFF] ^ (crc >> 8);
            }
            return crc ^ 0xFFFFFFFFu;
        }

        private static uint[] BuildTable()
        {
            uint[] table = new uint[256];
            for (uint value = 0; value < table.Length; value++)
            {
                uint entry = value;
                for (int bit = 0; bit < 8; bit++)
                {
                    entry = (entry & 1) != 0 ? Polynomial ^ (entry >> 1) : entry >> 1;
                }
                table[value] = entry;
            }
            return table;
        }
    }
}
