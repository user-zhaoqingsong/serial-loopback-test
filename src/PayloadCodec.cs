using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RsLoopTest
{
    internal static class PayloadCodec
    {
        public static byte[] Parse(string value, bool isHex)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new FormatException("预设数据不能为空。");
            }

            if (!isHex)
            {
                return new UTF8Encoding(false).GetBytes(value);
            }

            string normalized = value.Replace("0x", string.Empty)
                                     .Replace("0X", string.Empty)
                                     .Replace(",", " ")
                                     .Replace("-", " ")
                                     .Replace("\r", " ")
                                     .Replace("\n", " ")
                                     .Replace("\t", " ");
            string[] tokens = normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            List<byte> result = new List<byte>();

            foreach (string token in tokens)
            {
                if (token.Length % 2 != 0)
                {
                    throw new FormatException("HEX 数据必须由偶数个十六进制字符组成。");
                }

                for (int index = 0; index < token.Length; index += 2)
                {
                    byte parsed;
                    if (!byte.TryParse(token.Substring(index, 2), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out parsed))
                    {
                        throw new FormatException("HEX 数据包含非法字符：" + token);
                    }
                    result.Add(parsed);
                }
            }

            if (result.Count == 0)
            {
                throw new FormatException("预设数据不能为空。");
            }

            if (result.Count > 4096)
            {
                throw new FormatException("单帧数据不能超过 4096 字节。");
            }

            return result.ToArray();
        }

        public static string ToHex(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(data.Length * 3);
            for (int index = 0; index < data.Length; index++)
            {
                if (index > 0)
                {
                    builder.Append(' ');
                }
                builder.Append(data[index].ToString("X2", CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        public static bool AreEqual(byte[] expected, byte[] actual)
        {
            if (expected == null || actual == null || expected.Length != actual.Length)
            {
                return false;
            }

            for (int index = 0; index < expected.Length; index++)
            {
                if (expected[index] != actual[index])
                {
                    return false;
                }
            }
            return true;
        }
    }
}
