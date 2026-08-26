using System;
using System.Globalization;

namespace RsLoopTest
{
    internal static class BaudRateOptions
    {
        public const int Minimum = 300;
        public const int Maximum = 3000000;

        public static readonly int[] CommonRates =
        {
            300, 600, 1200, 2400, 4800, 9600, 14400, 19200, 38400, 57600,
            115200, 128000, 230400, 256000, 460800, 500000, 576000, 921600,
            1000000, 1500000, 2000000, 3000000
        };

        public static int Parse(string value)
        {
            int baudRate;
            if (string.IsNullOrWhiteSpace(value) ||
                !int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out baudRate))
            {
                throw new FormatException("波特率必须是 300 到 3000000 之间的整数。");
            }
            if (baudRate < Minimum || baudRate > Maximum)
            {
                throw new ArgumentOutOfRangeException("value",
                    "波特率必须在 300 到 3000000 之间。");
            }
            return baudRate;
        }
    }
}
