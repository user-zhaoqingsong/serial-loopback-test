using System;

namespace RsLoopTest
{
    internal static class SerialTiming
    {
        // 8N1 每字节包含 1 个起始位、8 个数据位和 1 个停止位。
        private const int BitsPerByte = 10;

        public static int CalculateRoundTripWireMilliseconds(int frameLength, int baudRate)
        {
            return CalculateWireMilliseconds(frameLength, baudRate, 2);
        }

        public static int CalculateWireMilliseconds(int frameLength, int baudRate, int wirePasses)
        {
            if (frameLength <= 0)
            {
                throw new ArgumentOutOfRangeException("frameLength");
            }
            if (baudRate <= 0)
            {
                throw new ArgumentOutOfRangeException("baudRate");
            }
            if (wirePasses <= 0)
            {
                throw new ArgumentOutOfRangeException("wirePasses");
            }

            double milliseconds = (double)frameLength * BitsPerByte * wirePasses * 1000.0 / baudRate;
            return Math.Max(1, (int)Math.Ceiling(milliseconds));
        }

        public static int CalculateEffectiveTimeoutMilliseconds(int frameLength, int baudRate,
            int requestedTimeoutMilliseconds)
        {
            return CalculateEffectiveTimeoutMilliseconds(frameLength, baudRate,
                requestedTimeoutMilliseconds, 2);
        }

        public static int CalculateEffectiveTimeoutMilliseconds(int frameLength, int baudRate,
            int requestedTimeoutMilliseconds, int wirePasses)
        {
            if (requestedTimeoutMilliseconds <= 0)
            {
                throw new ArgumentOutOfRangeException("requestedTimeoutMilliseconds");
            }

            int wireMilliseconds = CalculateWireMilliseconds(frameLength, baudRate, wirePasses);
            int lowSpeedAllowance = baudRate <= 9600 ? 5000 : (baudRate <= 19200 ? 3000 : 1000);
            long adaptiveTimeout = (long)wireMilliseconds * 2L + lowSpeedAllowance;
            adaptiveTimeout = Math.Max(adaptiveTimeout, requestedTimeoutMilliseconds);
            return adaptiveTimeout > int.MaxValue ? int.MaxValue : (int)adaptiveTimeout;
        }
    }
}
