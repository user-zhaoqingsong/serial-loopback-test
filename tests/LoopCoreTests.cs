using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using RsLoopTest;

internal static class LoopCoreTests
{
    private static int failures;
    private static int testCount;

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
            Assert(timeout == 2000, "高速短帧不应覆盖用户设置");
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
                PayloadGenerator generator = CreateGenerator(PayloadPattern.Incrementing,
                    length, false, false, null, 1u, 1);
                Assert(generator.CreateNext().Length == length, "帧长生成错误：" + length);
            }
        });
        Run("预设交替内容", delegate
        {
            PayloadGenerator generator = CreateGenerator(PayloadPattern.Alternating55AA,
                20, false, false, null, 1u, 1);
            byte[] payload = generator.CreateNext();
            for (int index = 0; index < payload.Length; index++)
                Assert(payload[index] == (index % 2 == 0 ? (byte)0x55 : (byte)0xAA),
                    "交替内容错误");
        });
        Run("自定义内容循环填充", delegate
        {
            PayloadGenerator generator = CreateGenerator(PayloadPattern.CustomRepeat,
                20, false, false, new byte[] { 1, 2, 3 }, 1u, 1);
            byte[] payload = generator.CreateNext();
            for (int index = 0; index < payload.Length; index++)
                Assert(payload[index] == (byte)(index % 3 + 1), "自定义循环内容错误");
        });
        Run("随机内容逐帧变化", delegate
        {
            PayloadGenerator generator = CreateGenerator(PayloadPattern.Fixed55,
                20, true, false, null, 123u, 123);
            Assert(!PayloadCodec.AreEqual(generator.CreateNext(), generator.CreateNext()),
                "随机内容未变化");
        });
        Run("随机内容按种子复现", delegate
        {
            PayloadGenerator first = CreateGenerator(PayloadPattern.Fixed55,
                40, true, false, null, 0x12345678u, 1);
            PayloadGenerator second = CreateGenerator(PayloadPattern.Fixed55,
                40, true, false, null, 0x12345678u, 999);
            AssertBytes(first.CreateNext(), second.CreateNext());
            AssertBytes(first.CreateNext(), second.CreateNext());
        });
        Run("随机帧长限定五档", delegate
        {
            PayloadGenerator generator = CreateGenerator(PayloadPattern.Incrementing,
                20, false, true, null, 1u, 456);
            for (int count = 0; count < 50; count++)
            {
                int actualLength = generator.CreateNext().Length;
                Assert(Array.IndexOf(LoopDataOptions.AllowedFrameLengths, actualLength) >= 0,
                    "随机帧长超出五档范围");
            }
        });
        Run("PRBS7 按种子复现", delegate { AssertPrbsReproducible(PayloadPattern.Prbs7); });
        Run("PRBS15 按种子复现", delegate { AssertPrbsReproducible(PayloadPattern.Prbs15); });
        Run("PRBS31 按种子复现", delegate { AssertPrbsReproducible(PayloadPattern.Prbs31); });
        Run("PRBS 记录每帧起始种子", delegate
        {
            PayloadGenerator generator = CreateGenerator(PayloadPattern.Prbs31,
                20, false, false, null, 0x12345678u, 1);
            GeneratedPayload first = generator.CreateNextFrame();
            GeneratedPayload second = generator.CreateNextFrame();
            Assert(first.StartSeed == 0x12345678u, "首帧种子记录错误");
            Assert(second.StartSeed != first.StartSeed, "逐帧种子没有推进");
        });
        Run("CRC32 标准校验向量", delegate
        {
            byte[] vector = Encoding.ASCII.GetBytes("123456789");
            Assert(Crc32.Compute(vector, 0, vector.Length) == 0xCBF43926u,
                "CRC32 与标准向量不一致");
        });
        Run("协议帧编解码往返", delegate
        {
            byte[] payload = BuildPayload(40);
            byte[] raw = LoopFrameCodec.Build(42u, PayloadPattern.Prbs15, 0x10203040u, payload);
            FrameParseBatch batch = new LoopFrameParser().Append(raw, raw.Length);
            Assert(batch.Frames.Count == 1, "未解析出完整协议帧");
            LoopFrame frame = batch.Frames[0];
            Assert(frame.Sequence == 42u && frame.PayloadLength == payload.Length,
                "序号或长度解析错误");
            Assert(frame.Pattern == PayloadPattern.Prbs15 && frame.Seed == 0x10203040u,
                "模式或种子解析错误");
            Assert(frame.FrameCrcValid, "有效帧 CRC 被误判");
            AssertBytes(payload, frame.Payload);
        });
        Run("协议帧支持任意分片", delegate
        {
            byte[] raw = LoopFrameCodec.Build(7u, PayloadPattern.Prbs7, 3u, BuildPayload(20));
            LoopFrameParser parser = new LoopFrameParser();
            for (int index = 0; index < raw.Length - 1; index++)
                Assert(parser.Append(new byte[] { raw[index] }, 1).Frames.Count == 0,
                    "不完整帧被提前解析");
            FrameParseBatch last = parser.Append(new byte[] { raw[raw.Length - 1] }, 1);
            Assert(last.Frames.Count == 1 && last.Frames[0].Sequence == 7u,
                "逐字节输入未能正确组帧");
        });
        Run("错位与坏帧头后重新同步", delegate
        {
            byte[] bad = LoopFrameCodec.Build(1u, PayloadPattern.Incrementing, 1u, BuildPayload(20));
            bad[6] ^= 0x01;
            byte[] good = LoopFrameCodec.Build(2u, PayloadPattern.Incrementing, 1u, BuildPayload(20));
            byte[] stream = Combine(new byte[] { 0x00, 0x52, 0x11, 0x22 }, bad, good);
            FrameParseBatch batch = new LoopFrameParser().Append(stream, stream.Length);
            Assert(batch.HeaderErrors >= 1, "未识别损坏的帧头");
            Assert(batch.DiscardedBytes > 0, "未统计重新同步丢弃字节");
            Assert(batch.Frames.Count == 1 && batch.Frames[0].Sequence == 2u,
                "错位后未恢复到下一有效帧");
        });
        Run("载荷损坏由整帧 CRC 检出", delegate
        {
            byte[] raw = LoopFrameCodec.Build(8u, PayloadPattern.Prbs31, 9u, BuildPayload(40));
            raw[LoopFrameCodec.HeaderLength + 5] ^= 0x05;
            FrameParseBatch batch = new LoopFrameParser().Append(raw, raw.Length);
            Assert(batch.Frames.Count == 1 && !batch.Frames[0].FrameCrcValid,
                "整帧 CRC 未检出载荷位错误");
        });
        Run("错误字节和错误位数统计", delegate
        {
            long differentBytes;
            long differentBits;
            SerialLoopController.CountDifferences(
                new byte[] { 0x00, 0xFF, 0xAA }, new byte[] { 0x01, 0x00, 0xAB },
                out differentBytes, out differentBits);
            Assert(differentBytes == 3 && differentBits == 10,
                "错误字节或错误位数计算不正确");
            SerialLoopController.CountDifferences(
                new byte[] { 0x00, 0x00 }, new byte[] { 0x00 },
                out differentBytes, out differentBits);
            Assert(differentBytes == 1 && differentBits == 8,
                "缺失字节应计为 8 个错误位");
        });
        Run("连续多帧一次解析", delegate
        {
            byte[] first = LoopFrameCodec.Build(100u, PayloadPattern.Prbs7, 1u, BuildPayload(20));
            byte[] second = LoopFrameCodec.Build(101u, PayloadPattern.Prbs7, 2u, BuildPayload(100));
            FrameParseBatch batch = new LoopFrameParser().Append(Combine(first, second),
                first.Length + second.Length);
            Assert(batch.Frames.Count == 2, "未一次解析所有在途帧");
            Assert(batch.Frames[0].Sequence == 100u && batch.Frames[1].Sequence == 101u,
                "连续帧顺序解析错误");
        });
        Run("TCP 与 UDP 端点格式解析", delegate
        {
            TransportSettings tcp = TransportSettings.Parse(TransportKind.TcpClient,
                null, "127.0.0.1:9001");
            Assert(tcp.Host == "127.0.0.1" && tcp.Port == 9001, "TCP 端点解析错误");
            TransportSettings udp = TransportSettings.Parse(TransportKind.Udp,
                null, "9000@127.0.0.1:9001");
            Assert(udp.LocalPort == 9000 && udp.Port == 9001, "UDP 端点解析错误");
        });
        Run("本机 TCP Client/Server 双向传输", delegate
        {
            int port = GetFreeTcpPort();
            LoopTransport server = new TcpServerLoopTransport("127.0.0.1", port);
            LoopTransport client = new TcpClientLoopTransport("127.0.0.1", port);
            ManualResetEvent received = new ManualResetEvent(false);
            byte[] actual = null;
            server.DataReceived += delegate(byte[] data, int count)
            {
                server.TryWrite(data, 0, count);
            };
            client.DataReceived += delegate(byte[] data, int count)
            {
                actual = CopyBytes(data, count);
                received.Set();
            };
            try
            {
                server.Open();
                client.Open();
                byte[] expected = BuildPayload(40);
                Assert(client.TryWrite(expected, 0, expected.Length), "TCP Client 发送失败");
                Assert(received.WaitOne(3000), "TCP 回传等待超时");
                AssertBytes(expected, actual);
            }
            finally
            {
                client.Dispose();
                server.Dispose();
                received.Dispose();
            }
        });
        Run("本机 UDP 双向传输", delegate
        {
            int portA = GetFreeUdpPort();
            int portB = GetFreeUdpPort();
            while (portB == portA) portB = GetFreeUdpPort();
            LoopTransport endpointA = new UdpLoopTransport(portA, "127.0.0.1", portB);
            LoopTransport endpointB = new UdpLoopTransport(portB, "127.0.0.1", portA);
            ManualResetEvent received = new ManualResetEvent(false);
            byte[] actual = null;
            endpointB.DataReceived += delegate(byte[] data, int count)
            {
                endpointB.TryWrite(data, 0, count);
            };
            endpointA.DataReceived += delegate(byte[] data, int count)
            {
                actual = CopyBytes(data, count);
                received.Set();
            };
            try
            {
                endpointB.Open();
                endpointA.Open();
                byte[] expected = BuildPayload(60);
                Assert(endpointA.TryWrite(expected, 0, expected.Length), "UDP 发送失败");
                Assert(received.WaitOne(3000), "UDP 回传等待超时");
                AssertBytes(expected, actual);
            }
            finally
            {
                endpointA.Dispose();
                endpointB.Dispose();
                received.Dispose();
            }
        });
        Run("控制器 TCP 多帧在途环回", delegate
        {
            int port = GetFreeTcpPort();
            TransportSettings endpointA = TransportSettings.Parse(TransportKind.TcpClient,
                null, "127.0.0.1:" + port);
            TransportSettings endpointB = TransportSettings.Parse(TransportKind.TcpServer,
                null, "127.0.0.1:" + port);
            AssertControllerNetworkLoop(endpointA, endpointB);
        });
        Run("控制器 UDP 多帧在途环回", delegate
        {
            int portA = GetFreeUdpPort();
            int portB = GetFreeUdpPort();
            while (portB == portA) portB = GetFreeUdpPort();
            TransportSettings endpointA = TransportSettings.Parse(TransportKind.Udp,
                null, portA + "@127.0.0.1:" + portB);
            TransportSettings endpointB = TransportSettings.Parse(TransportKind.Udp,
                null, portB + "@127.0.0.1:" + portA);
            AssertControllerNetworkLoop(endpointA, endpointB);
        });
        Run("控制器识别 UDP 乱序与重复帧", delegate
        {
            int localPort = GetFreeUdpPort();
            int peerPort = GetFreeUdpPort();
            while (peerPort == localPort) peerPort = GetFreeUdpPort();
            UdpClient peer = new UdpClient(peerPort);
            peer.Client.ReceiveTimeout = 3000;
            SerialLoopController controller = new SerialLoopController();
            try
            {
                TransportSettings endpoint = TransportSettings.Parse(TransportKind.Udp,
                    null, localPort + "@127.0.0.1:" + peerPort);
                controller.Start(endpoint, null, 115200, CreatePrbsOptions(), 2000,
                    LoopTestMode.SinglePortFullDuplex);
                IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                byte[] first = peer.Receive(ref sender);
                byte[] second = peer.Receive(ref sender);
                peer.Send(second, second.Length, new IPEndPoint(IPAddress.Loopback, localPort));
                peer.Send(first, first.Length, new IPEndPoint(IPAddress.Loopback, localPort));
                peer.Send(first, first.Length, new IPEndPoint(IPAddress.Loopback, localPort));

                LoopSnapshot snapshot = WaitForSnapshot(controller, delegate(LoopSnapshot value)
                {
                    return value.OutOfOrderFrames >= 1 && value.DuplicateFrames >= 1;
                }, 3000);
                Assert(snapshot.OutOfOrderFrames >= 1, "未统计乱序帧");
                Assert(snapshot.DuplicateFrames >= 1, "未统计重复帧");
                Assert(snapshot.AReceivedOk >= 2, "乱序到达的有效帧未继续校验");
            }
            finally
            {
                controller.Stop("用户停止");
                controller.Dispose();
                peer.Close();
            }
        });
        Run("丢帧后仍持续填充发送窗口", delegate
        {
            int localPort = GetFreeUdpPort();
            int unusedPort = GetFreeUdpPort();
            while (unusedPort == localPort) unusedPort = GetFreeUdpPort();
            SerialLoopController controller = new SerialLoopController();
            try
            {
                TransportSettings endpoint = TransportSettings.Parse(TransportKind.Udp,
                    null, localPort + "@127.0.0.1:" + unusedPort);
                controller.Start(endpoint, null, 115200, CreatePrbsOptions(), 100,
                    LoopTestMode.SinglePortFullDuplex);
                LoopSnapshot snapshot = WaitForSnapshot(controller, delegate(LoopSnapshot value)
                {
                    return value.LostFrames >= 32 && value.ASent > 32;
                }, 3000);
                Assert(snapshot.LostFrames >= 32, "未按超时统计丢帧");
                Assert(snapshot.ASent > snapshot.WindowSize,
                    "丢帧后发送线程没有继续填充窗口");
                Assert(snapshot.IsRunning, "丢帧不应停止测试");
            }
            finally
            {
                controller.Stop("用户停止");
                controller.Dispose();
            }
        });
        Run("波特率范围边界", delegate
        {
            Assert(BaudRateOptions.Parse("300") == 300, "最低波特率解析失败");
            Assert(BaudRateOptions.Parse("3000000") == 3000000, "最高波特率解析失败");
            Assert(BaudRateOptions.Parse(" 115200 ") == 115200, "常用波特率解析失败");
        });
        Run("拒绝越界或非法波特率", delegate
        {
            AssertBaudRejected("299");
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
        Console.WriteLine("PASS: " + testCount + " tests");
    }

    private static void AssertPrbsReproducible(PayloadPattern pattern)
    {
        PayloadGenerator first = CreateGenerator(pattern, 100, false, false,
            null, 0x13579BDFu, 1);
        PayloadGenerator second = CreateGenerator(pattern, 100, false, false,
            null, 0x13579BDFu, 2);
        byte[] firstFrame = first.CreateNext();
        byte[] secondFrame = second.CreateNext();
        AssertBytes(firstFrame, secondFrame);
        Assert(!AllZero(firstFrame), "PRBS 序列不应全零");
        AssertBytes(first.CreateNext(), second.CreateNext());
    }

    private static bool AllZero(byte[] data)
    {
        foreach (byte value in data) if (value != 0) return false;
        return true;
    }

    private static byte[] BuildPayload(int length)
    {
        byte[] payload = new byte[length];
        for (int index = 0; index < length; index++) payload[index] = (byte)(index * 17 + 3);
        return payload;
    }

    private static byte[] CopyBytes(byte[] data, int count)
    {
        byte[] result = new byte[count];
        Array.Copy(data, result, count);
        return result;
    }

    private static int GetFreeTcpPort()
    {
        TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static int GetFreeUdpPort()
    {
        UdpClient client = new UdpClient(0);
        int port = ((IPEndPoint)client.Client.LocalEndPoint).Port;
        client.Close();
        return port;
    }

    private static void AssertControllerNetworkLoop(TransportSettings endpointA,
        TransportSettings endpointB)
    {
        SerialLoopController controller = new SerialLoopController();
        LoopDataOptions options = CreatePrbsOptions();
        try
        {
            controller.Start(endpointA, endpointB, 115200, options, 2000,
                LoopTestMode.DualPortRelay);
            DateTime deadline = DateTime.UtcNow.AddSeconds(4);
            LoopSnapshot snapshot;
            do
            {
                Thread.Sleep(10);
                snapshot = controller.GetSnapshot();
            }
            while (snapshot.AReceivedOk < 40 && DateTime.UtcNow < deadline);
            Assert(snapshot.AReceivedOk >= 40, "控制器未完成足够的连续环回帧");
            Assert(snapshot.ASent > 1 && snapshot.WindowSize == 32,
                "网络模式未启用多帧在途窗口");
            Assert(snapshot.AReceivedError == 0 && snapshot.CrcErrors == 0 &&
                snapshot.LostFrames == 0 && snapshot.ErrorBytes == 0 && snapshot.ErrorBits == 0,
                "无损本机网络环回出现错误统计");
        }
        finally
        {
            controller.Stop("用户停止");
            controller.Dispose();
        }
    }

    private static LoopDataOptions CreatePrbsOptions()
    {
        return new LoopDataOptions
        {
            Pattern = PayloadPattern.Prbs15,
            FrameLength = 100,
            DataSeed = 0x2468ACE1u
        };
    }

    private static LoopSnapshot WaitForSnapshot(SerialLoopController controller,
        Predicate<LoopSnapshot> condition, int timeoutMilliseconds)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        LoopSnapshot snapshot;
        do
        {
            Thread.Sleep(10);
            snapshot = controller.GetSnapshot();
        }
        while (!condition(snapshot) && DateTime.UtcNow < deadline);
        return snapshot;
    }

    private static byte[] Combine(params byte[][] arrays)
    {
        int length = 0;
        foreach (byte[] array in arrays) length += array.Length;
        byte[] result = new byte[length];
        int offset = 0;
        foreach (byte[] array in arrays)
        {
            Array.Copy(array, 0, result, offset, array.Length);
            offset += array.Length;
        }
        return result;
    }

    private static PayloadGenerator CreateGenerator(PayloadPattern pattern, int length,
        bool randomContent, bool randomLength, byte[] customPattern, uint dataSeed, int randomSeed)
    {
        LoopDataOptions options = new LoopDataOptions
        {
            Pattern = pattern,
            FrameLength = length,
            RandomContent = randomContent,
            RandomFrameLength = randomLength,
            CustomPattern = customPattern,
            DataSeed = dataSeed
        };
        return new PayloadGenerator(options, new Random(randomSeed));
    }

    private static void AssertBaudRejected(string value)
    {
        bool rejected = false;
        try { BaudRateOptions.Parse(value); }
        catch (Exception) { rejected = true; }
        Assert(rejected, "未拒绝非法波特率：" + value);
    }

    private static void Run(string name, Action action)
    {
        testCount++;
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
            "字节不一致。期望=" + PayloadCodec.ToHex(expected) +
            " 实际=" + PayloadCodec.ToHex(actual));
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
