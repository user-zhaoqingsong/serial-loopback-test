using System;
using RsLoopTest;

internal static class LoopCoreTests
{
    private static int failures;

    private static void Main()
    {
        Run("HEX 空格格式", delegate
        {
            AssertBytes(new byte[] { 0x55, 0xAA, 0x00, 0xFF },
                PayloadCodec.Parse("55 AA 00 FF", true));
        });
        Run("HEX 连续格式", delegate
        {
            AssertBytes(new byte[] { 0x12, 0x34, 0xAB, 0xCD },
                PayloadCodec.Parse("1234ABCD", true));
        });
        Run("HEX 混合分隔符", delegate
        {
            AssertBytes(new byte[] { 0x01, 0x02, 0x03 },
                PayloadCodec.Parse("0x01,02-03", true));
        });
        Run("文本 UTF-8", delegate
        {
            AssertBytes(new byte[] { 0x41, 0xE7, 0x8E, 0xAF },
                PayloadCodec.Parse("A环", false));
        });
        Run("拒绝非法 HEX", delegate
        {
            bool thrown = false;
            try { PayloadCodec.Parse("55 AG", true); }
            catch (FormatException) { thrown = true; }
            Assert(thrown, "未拒绝非法 HEX");
        });
        Run("分片组帧", delegate
        {
            FrameBuffer buffer = new FrameBuffer(4);
            buffer.Append(new byte[] { 1, 2 }, 2);
            byte[] frame;
            Assert(!buffer.TryTakeFrame(out frame), "数据不足时不应成帧");
            buffer.Append(new byte[] { 3, 4, 5, 6, 7, 8 }, 6);
            Assert(buffer.TryTakeFrame(out frame), "第一帧未生成");
            AssertBytes(new byte[] { 1, 2, 3, 4 }, frame);
            Assert(buffer.TryTakeFrame(out frame), "第二帧未生成");
            AssertBytes(new byte[] { 5, 6, 7, 8 }, frame);
        });
        Run("9600 短帧自动放宽超时", delegate
        {
            int timeout = SerialTiming.CalculateEffectiveTimeoutMilliseconds(8, 9600, 2000);
            Assert(timeout >= 5000, "9600 baud 的安全余量不足");
        });
        Run("9600 长帧按传输时间延长", delegate
        {
            int shortFrame = SerialTiming.CalculateEffectiveTimeoutMilliseconds(8, 9600, 2000);
            int longFrame = SerialTiming.CalculateEffectiveTimeoutMilliseconds(2048, 9600, 2000);
            Assert(longFrame > shortFrame, "长帧超时没有随传输时间增加");
            Assert(longFrame >= 13000, "2048 字节往返超时余量不足");
        });
        Run("高速短帧尊重用户设置", delegate
        {
            int timeout = SerialTiming.CalculateEffectiveTimeoutMilliseconds(8, 115200, 2000);
            Assert(timeout == 2000, "高速短帧不应无故覆盖用户设置");
        });
        Run("更长用户设置优先", delegate
        {
            int timeout = SerialTiming.CalculateEffectiveTimeoutMilliseconds(8, 9600, 15000);
            Assert(timeout == 15000, "未保留用户设置的更长超时");
        });
        Run("五档帧长生成", delegate
        {
            foreach (int length in LoopDataOptions.AllowedFrameLengths)
            {
                PayloadGenerator generator = CreateGenerator(PayloadPattern.Incrementing, length, false, false, null, 1);
                Assert(generator.CreateNext().Length == length, "帧长生成错误：" + length);
            }
        });
        Run("预设交替内容", delegate
        {
            PayloadGenerator generator = CreateGenerator(PayloadPattern.Alternating55AA, 20, false, false, null, 1);
            byte[] payload = generator.CreateNext();
            for (int index = 0; index < payload.Length; index++)
            {
                Assert(payload[index] == (index % 2 == 0 ? (byte)0x55 : (byte)0xAA), "交替内容错误");
            }
        });
        Run("自定义内容循环填充", delegate
        {
            PayloadGenerator generator = CreateGenerator(PayloadPattern.CustomRepeat, 20, false, false,
                new byte[] { 1, 2, 3 }, 1);
            byte[] payload = generator.CreateNext();
            for (int index = 0; index < payload.Length; index++)
            {
                Assert(payload[index] == (byte)(index % 3 + 1), "自定义循环内容错误");
            }
        });
        Run("随机内容逐帧变化", delegate
        {
            PayloadGenerator generator = CreateGenerator(PayloadPattern.Fixed55, 20, true, false, null, 123);
            Assert(!PayloadCodec.AreEqual(generator.CreateNext(), generator.CreateNext()), "随机内容未变化");
        });
        Run("随机帧长限定五档", delegate
        {
            PayloadGenerator generator = CreateGenerator(PayloadPattern.Incrementing, 20, false, true, null, 456);
            for (int count = 0; count < 50; count++)
            {
                int actualLength = generator.CreateNext().Length;
                Assert(Array.IndexOf(LoopDataOptions.AllowedFrameLengths, actualLength) >= 0,
                    "随机帧长超出五档范围");
            }
        });
        Run("波特率范围边界", delegate
        {
            Assert(BaudRateOptions.Parse("300") == 300, "最低波特率解析失败");
            Assert(BaudRateOptions.Parse("3000000") == 3000000, "最高波特率解析失败");
            Assert(BaudRateOptions.Parse(" 115200 ") == 115200, "常用波特率解析失败");
        });
        Run("拒绝过低波特率", delegate
        {
            AssertBaudRejected("299");
        });
        Run("拒绝过高或非法波特率", delegate
        {
            AssertBaudRejected("3000001");
            AssertBaudRejected("9.6k");
        });
        Run("单端线路耗时小于双端往返", delegate
        {
            int single = SerialTiming.CalculateWireMilliseconds(100, 9600, 1);
            int dual = SerialTiming.CalculateWireMilliseconds(100, 9600, 2);
            Assert(single < dual, "单端自环线路耗时计算错误");
            Assert(dual == SerialTiming.CalculateRoundTripWireMilliseconds(100, 9600),
                "双端往返兼容计算错误");
        });

        if (failures > 0)
        {
            Console.Error.WriteLine("FAILED: " + failures);
            Environment.Exit(1);
        }
        Console.WriteLine("PASS: 19 tests");
    }

    private static PayloadGenerator CreateGenerator(PayloadPattern pattern, int length,
        bool randomContent, bool randomLength, byte[] customPattern, int seed)
    {
        LoopDataOptions options = new LoopDataOptions
        {
            Pattern = pattern,
            FrameLength = length,
            RandomContent = randomContent,
            RandomFrameLength = randomLength,
            CustomPattern = customPattern
        };
        return new PayloadGenerator(options, new Random(seed));
    }

    private static void AssertBaudRejected(string value)
    {
        bool rejected = false;
        try
        {
            BaudRateOptions.Parse(value);
        }
        catch (Exception)
        {
            rejected = true;
        }
        Assert(rejected, "未拒绝非法波特率：" + value);
    }

    private static void Run(string name, Action action)
    {
        try
        {
            action();
            Console.WriteLine("PASS " + name);
        }
        catch (Exception exception)
        {
            failures++;
            Console.Error.WriteLine("FAIL " + name + ": " + exception.Message);
        }
    }

    private static void AssertBytes(byte[] expected, byte[] actual)
    {
        Assert(PayloadCodec.AreEqual(expected, actual),
            "字节不一致。期望=" + PayloadCodec.ToHex(expected) + " 实际=" + PayloadCodec.ToHex(actual));
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
