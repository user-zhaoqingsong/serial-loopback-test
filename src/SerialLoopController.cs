using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace RsLoopTest
{
    internal sealed class SerialLoopController : IDisposable
    {
        private sealed class PendingFrame
        {
            public uint Sequence;
            public PayloadPattern Pattern;
            public uint Seed;
            public byte[] Payload;
            public byte[] RawBytes;
            public long SentTimestamp;
            public int TimeoutMilliseconds;
        }

        private readonly object syncRoot = new object();
        private readonly AutoResetEvent senderSignal = new AutoResetEvent(false);
        private readonly Stopwatch elapsed = new Stopwatch();
        private readonly Dictionary<uint, PendingFrame> pending = new Dictionary<uint, PendingFrame>();
        private readonly HashSet<uint> finalizedAhead = new HashSet<uint>();
        private readonly HashSet<uint> recentReceived = new HashSet<uint>();
        private readonly Queue<uint> recentReceivedOrder = new Queue<uint>();
        private LoopTransport transportA;
        private LoopTransport transportB;
        private LoopFrameParser parserA;
        private LoopFrameParser parserB;
        private LoopDataOptions dataOptions;
        private PayloadGenerator payloadGenerator;
        private Thread senderThread;
        private LoopTestMode mode;
        private int baudRate;
        private int requestedTimeoutMilliseconds;
        private int windowSize;
        private uint nextSequence;
        private uint nextExpectedSequence;
        private long aSent;
        private long aReceivedOk;
        private long aReceivedError;
        private long bReceivedOk;
        private long bReceivedError;
        private long bSent;
        private long totalBytes;
        private long crcErrors;
        private long headerErrors;
        private long resynchronizedBytes;
        private long lostFrames;
        private long duplicateFrames;
        private long outOfOrderFrames;
        private long errorBytes;
        private long errorBits;
        private long latencySamples;
        private double totalRoundTripMilliseconds;
        private double lastRoundTripMilliseconds;
        private bool isRunning;
        private bool disposed;
        private string stopReason = "未启动";

        public event Action<string, bool> LogAvailable;
        public event Action<string> TestStopped;

        public void Start(TransportSettings endpointA, TransportSettings endpointB,
            int baudRate, LoopDataOptions options,
            int responseTimeoutMilliseconds, LoopTestMode mode)
        {
            ValidateStartArguments(endpointA, endpointB, options, mode);
            try
            {
                lock (syncRoot)
                {
                    ThrowIfDisposed();
                    if (isRunning)
                    {
                        throw new InvalidOperationException("测试已在运行中。");
                    }

                    ResetState();
                    dataOptions = options.Clone();
                    payloadGenerator = new PayloadGenerator(dataOptions);
                    this.baudRate = baudRate;
                    this.mode = mode;
                    requestedTimeoutMilliseconds = responseTimeoutMilliseconds;
                    bool allNetwork = !endpointA.IsSerial &&
                        (mode == LoopTestMode.SinglePortFullDuplex || !endpointB.IsSerial);
                    windowSize = allNetwork ? 32 : CalculateWindowSize(baudRate);
                    parserA = new LoopFrameParser();
                    parserB = mode == LoopTestMode.DualPortRelay ? new LoopFrameParser() : null;

                    transportA = LoopTransportFactory.Create(endpointA, baudRate);
                    AttachTransport(transportA, true);
                    if (mode == LoopTestMode.DualPortRelay)
                    {
                        transportB = LoopTransportFactory.Create(endpointB, baudRate);
                        AttachTransport(transportB, false);
                    }
                    OpenTransports();
                    ClearTransportBuffers();

                    isRunning = true;
                    stopReason = string.Empty;
                    elapsed.Restart();
                    senderThread = new Thread(SenderLoop);
                    senderThread.IsBackground = true;
                    senderThread.Name = "SerialLoopSender";
                    senderThread.Start();
                }
            }
            catch
            {
                CleanupAfterStartFailure();
                throw;
            }

            string modeName = mode == LoopTestMode.SinglePortFullDuplex
                ? "单端点全双工/自环" : "双端点中继环回";
            string endpoints = mode == LoopTestMode.SinglePortFullDuplex
                ? endpointA.Describe() : endpointA.Describe() + " ⇄ " + endpointB.Describe();
            EmitLog("测试已启动：" + modeName + "，" + endpoints +
                (endpointA.IsSerial || (endpointB != null && endpointB.IsSerial)
                    ? "，串口 " + baudRate + " baud" : string.Empty) + "，" +
                dataOptions.Describe() + "，初始种子 0x" + dataOptions.DataSeed.ToString("X8") +
                "，在途窗口 " + windowSize + " 帧。", false);
            EmitLog("协议：RSLP v1，序号 + 长度 + 帧起始种子 + 头部 CRC32 + 整帧 CRC32。", false);
        }

        public void Stop(string reason)
        {
            bool shouldNotify;
            LoopTransport closingTransportA;
            LoopTransport closingTransportB;
            Thread closingSender;
            lock (syncRoot)
            {
                shouldNotify = isRunning;
                isRunning = false;
                stopReason = string.IsNullOrWhiteSpace(reason) ? "用户停止" : reason;
                elapsed.Stop();
                closingTransportA = transportA;
                closingTransportB = transportB;
                closingSender = senderThread;
                transportA = null;
                transportB = null;
                senderThread = null;
                senderSignal.Set();
            }

            CloseTransport(closingTransportA, true);
            CloseTransport(closingTransportB, false);
            JoinSender(closingSender);

            if (shouldNotify)
            {
                EmitLog("测试停止：" + stopReason, stopReason != "用户停止");
                Action<string> handler = TestStopped;
                if (handler != null)
                {
                    handler(stopReason);
                }
            }
        }

        public LoopSnapshot GetSnapshot()
        {
            lock (syncRoot)
            {
                return new LoopSnapshot
                {
                    IsRunning = isRunning,
                    Mode = mode,
                    ASent = aSent,
                    AReceivedOk = aReceivedOk,
                    AReceivedError = aReceivedError,
                    BReceivedOk = bReceivedOk,
                    BReceivedError = bReceivedError,
                    BSent = bSent,
                    TotalBytes = totalBytes,
                    CrcErrors = crcErrors,
                    HeaderErrors = headerErrors,
                    ResynchronizedBytes = resynchronizedBytes,
                    LostFrames = lostFrames,
                    DuplicateFrames = duplicateFrames,
                    OutOfOrderFrames = outOfOrderFrames,
                    ErrorBytes = errorBytes,
                    ErrorBits = errorBits,
                    InFlightFrames = pending.Count,
                    WindowSize = windowSize,
                    Elapsed = elapsed.Elapsed,
                    LastRoundTripMilliseconds = lastRoundTripMilliseconds,
                    AverageRoundTripMilliseconds = latencySamples == 0
                        ? 0.0 : totalRoundTripMilliseconds / latencySamples,
                    StopReason = stopReason
                };
            }
        }

        private static void ValidateStartArguments(TransportSettings endpointA,
            TransportSettings endpointB,
            LoopDataOptions options, LoopTestMode mode)
        {
            if (endpointA == null) throw new ArgumentNullException("endpointA");
            endpointA.Validate();
            if (mode == LoopTestMode.DualPortRelay && endpointB == null)
                throw new ArgumentException("双端口模式必须配置端点 B。");
            if (endpointB != null) endpointB.Validate();
            if (mode == LoopTestMode.DualPortRelay && endpointA.IsSerial && endpointB.IsSerial &&
                string.Equals(endpointA.SerialPortName, endpointB.SerialPortName,
                    StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("端口 A 和端口 B 不能选择同一个串口。");
            if (options == null) throw new ArgumentNullException("options");
            options.Validate();
        }

        private static int CalculateWindowSize(int rate)
        {
            if (rate <= 1200) return 2;
            if (rate <= 9600) return 4;
            if (rate <= 115200) return 8;
            if (rate <= 921600) return 16;
            return 32;
        }

        private void SenderLoop()
        {
            while (true)
            {
                PendingFrame frameToSend = null;
                LoopTransport sendingTransport = null;
                try
                {
                    lock (syncRoot)
                    {
                        if (!isRunning) return;
                        ExpireTimedOutFrames();
                        if (pending.Count < windowSize && transportA != null && transportA.IsReady)
                        {
                            GeneratedPayload generated = payloadGenerator.CreateNextFrame();
                            uint sequence = nextSequence++;
                            byte[] raw = LoopFrameCodec.Build(sequence, dataOptions.Pattern,
                                generated.StartSeed, generated.Data);
                            int serialPasses = (transportA.IsSerial ? 1 : 0) +
                                (mode == LoopTestMode.DualPortRelay && transportB != null &&
                                    transportB.IsSerial ? 1 : 0);
                            int timeout = requestedTimeoutMilliseconds;
                            if (serialPasses > 0)
                            {
                                int wireMilliseconds = SerialTiming.CalculateWireMilliseconds(
                                    raw.Length, baudRate, serialPasses);
                                timeout = SerialTiming.CalculateEffectiveTimeoutMilliseconds(
                                    raw.Length, baudRate, requestedTimeoutMilliseconds, serialPasses) +
                                    wireMilliseconds * windowSize;
                            }
                            frameToSend = new PendingFrame
                            {
                                Sequence = sequence,
                                Pattern = dataOptions.Pattern,
                                Seed = generated.StartSeed,
                                Payload = generated.Data,
                                RawBytes = raw,
                                SentTimestamp = Stopwatch.GetTimestamp(),
                                TimeoutMilliseconds = timeout
                            };
                            pending.Add(sequence, frameToSend);
                            aSent++;
                            sendingTransport = transportA;
                        }
                    }

                    if (frameToSend != null)
                    {
                        if (sendingTransport == null || !sendingTransport.TryWrite(
                            frameToSend.RawBytes, 0, frameToSend.RawBytes.Length))
                        {
                            lock (syncRoot)
                            {
                                pending.Remove(frameToSend.Sequence);
                                aSent--;
                                MarkFinalized(frameToSend.Sequence);
                            }
                            senderSignal.WaitOne(20);
                            continue;
                        }
                        if (frameToSend.Sequence == 0)
                        {
                            EmitLog("首帧：序号 0，载荷 " + frameToSend.Payload.Length +
                                " 字节，帧种子 0x" + frameToSend.Seed.ToString("X8") +
                                "，线路帧 " + frameToSend.RawBytes.Length + " 字节。", false);
                        }
                        continue;
                    }
                    senderSignal.WaitOne(20);
                }
                catch (Exception exception)
                {
                    FailAsync("发送线程失败：" + exception.Message);
                    return;
                }
            }
        }

        private void ExpireTimedOutFrames()
        {
            if (pending.Count == 0) return;
            long now = Stopwatch.GetTimestamp();
            List<uint> expired = new List<uint>();
            foreach (KeyValuePair<uint, PendingFrame> item in pending)
            {
                if (ElapsedMilliseconds(item.Value.SentTimestamp, now) > item.Value.TimeoutMilliseconds)
                    expired.Add(item.Key);
            }
            foreach (uint sequence in expired)
            {
                PendingFrame frame = pending[sequence];
                pending.Remove(sequence);
                lostFrames++;
                aReceivedError++;
                MarkFinalized(sequence);
                EmitSampledError("序号 " + sequence + " 超时，判定丢帧（" +
                    frame.TimeoutMilliseconds + " ms），测试继续。", lostFrames);
            }
        }

        private void TransportADataReceived(byte[] data, int count)
        {
            ReceiveData(data, count, true);
        }

        private void TransportBDataReceived(byte[] data, int count)
        {
            ReceiveData(data, count, false);
        }

        private void ReceiveData(byte[] incoming, int count, bool isPortA)
        {
            try
            {
                lock (syncRoot)
                {
                    LoopFrameParser parser = isPortA ? parserA : parserB;
                    if (!isRunning || incoming == null || count <= 0 || parser == null) return;
                    totalBytes += count;
                    FrameParseBatch batch = parser.Append(incoming, count);
                    headerErrors += batch.HeaderErrors;
                    resynchronizedBytes += batch.DiscardedBytes;
                    if (batch.HeaderErrors > 0)
                    {
                        EmitSampledError((isPortA ? "A" : "B") + " 端检测到 " +
                            batch.HeaderErrors + " 个无效帧头并重新同步。", headerErrors);
                    }
                    foreach (LoopFrame frame in batch.Frames)
                    {
                        if (isPortA) ProcessAFrame(frame); else ProcessBFrame(frame);
                    }
                }
            }
            catch (Exception exception)
            {
                FailAsync((isPortA ? "A" : "B") + " 端串口读取失败：" + exception.Message);
            }
        }

        private void ProcessBFrame(LoopFrame frame)
        {
            PendingFrame expectedFrame;
            bool hasExpected = pending.TryGetValue(frame.Sequence, out expectedFrame);
            bool payloadMatches = hasExpected && PayloadCodec.AreEqual(expectedFrame.Payload, frame.Payload);
            bool metadataMatches = hasExpected && expectedFrame.Pattern == frame.Pattern &&
                expectedFrame.Seed == frame.Seed;
            bool correct = frame.FrameCrcValid && payloadMatches && metadataMatches;
            if (!frame.FrameCrcValid) crcErrors++;
            if (correct) bReceivedOk++;
            else
            {
                bReceivedError++;
                EmitSampledError("B 端帧校验失败，序号 " + frame.Sequence + "，CRC=" +
                    (frame.FrameCrcValid ? "正确" : "错误") + "，仍原样回传。", bReceivedError);
            }
            if (transportB != null && transportB.TryWrite(frame.RawBytes, 0, frame.RawBytes.Length))
                bSent++;
            else
                EmitSampledError("B 端当前未连接，序号 " + frame.Sequence + " 未能回传。",
                    bReceivedError + 1);
        }

        private void ProcessAFrame(LoopFrame frame)
        {
            PendingFrame expectedFrame;
            if (!pending.TryGetValue(frame.Sequence, out expectedFrame))
            {
                if (recentReceived.Contains(frame.Sequence))
                {
                    duplicateFrames++;
                    EmitSampledError("收到重复帧，序号 " + frame.Sequence + "。", duplicateFrames);
                }
                else
                {
                    outOfOrderFrames++;
                    aReceivedError++;
                    EmitSampledError("收到不在当前窗口内的帧，序号 " + frame.Sequence + "。",
                        outOfOrderFrames);
                    RememberReceived(frame.Sequence);
                }
                if (!frame.FrameCrcValid) crcErrors++;
                return;
            }

            pending.Remove(frame.Sequence);
            if (frame.Sequence != nextExpectedSequence) outOfOrderFrames++;
            long now = Stopwatch.GetTimestamp();
            lastRoundTripMilliseconds = ElapsedMilliseconds(expectedFrame.SentTimestamp, now);
            totalRoundTripMilliseconds += lastRoundTripMilliseconds;
            latencySamples++;

            long frameErrorBytes;
            long frameErrorBits;
            CountDifferences(expectedFrame.Payload, frame.Payload, out frameErrorBytes, out frameErrorBits);
            errorBytes += frameErrorBytes;
            errorBits += frameErrorBits;
            bool metadataMatches = expectedFrame.Pattern == frame.Pattern &&
                expectedFrame.Seed == frame.Seed && expectedFrame.Payload.Length == frame.PayloadLength;
            bool correct = frame.FrameCrcValid && metadataMatches && frameErrorBytes == 0;
            if (!frame.FrameCrcValid) crcErrors++;
            if (correct) aReceivedOk++;
            else
            {
                aReceivedError++;
                EmitSampledError("A 端帧校验失败，序号 " + frame.Sequence + "，CRC=" +
                    (frame.FrameCrcValid ? "正确" : "错误") + "，错误字节 " +
                    frameErrorBytes + "，错误位 " + frameErrorBits + "。", aReceivedError);
            }
            MarkFinalized(frame.Sequence);
            RememberReceived(frame.Sequence);
            senderSignal.Set();
        }

        private void MarkFinalized(uint sequence)
        {
            finalizedAhead.Add(sequence);
            while (finalizedAhead.Remove(nextExpectedSequence)) nextExpectedSequence++;
        }

        private void RememberReceived(uint sequence)
        {
            if (recentReceived.Add(sequence))
            {
                recentReceivedOrder.Enqueue(sequence);
                while (recentReceivedOrder.Count > 4096)
                    recentReceived.Remove(recentReceivedOrder.Dequeue());
            }
        }

        internal static void CountDifferences(byte[] expected, byte[] actual,
            out long differentBytes, out long differentBits)
        {
            differentBytes = 0;
            differentBits = 0;
            int commonLength = Math.Min(expected.Length, actual.Length);
            for (int index = 0; index < commonLength; index++)
            {
                byte difference = (byte)(expected[index] ^ actual[index]);
                if (difference != 0)
                {
                    differentBytes++;
                    differentBits += CountSetBits(difference);
                }
            }
            for (int index = commonLength; index < expected.Length; index++)
            {
                differentBytes++;
                differentBits += 8;
            }
            for (int index = commonLength; index < actual.Length; index++)
            {
                differentBytes++;
                differentBits += 8;
            }
        }

        private static int CountSetBits(byte value)
        {
            int count = 0;
            while (value != 0)
            {
                value &= (byte)(value - 1);
                count++;
            }
            return count;
        }

        private static double ElapsedMilliseconds(long start, long end)
        {
            return (end - start) * 1000.0 / Stopwatch.Frequency;
        }

        private void AttachTransport(LoopTransport transport, bool isEndpointA)
        {
            if (isEndpointA)
            {
                transport.DataReceived += TransportADataReceived;
                transport.Faulted += TransportAFaulted;
                transport.StatusChanged += TransportAStatusChanged;
            }
            else
            {
                transport.DataReceived += TransportBDataReceived;
                transport.Faulted += TransportBFaulted;
                transport.StatusChanged += TransportBStatusChanged;
            }
        }

        private void OpenTransports()
        {
            if (transportA != null && transportA.IsServer) transportA.Open();
            if (transportB != null && transportB.IsServer) transportB.Open();
            if (transportB != null && !transportB.IsServer) transportB.Open();
            if (transportA != null && !transportA.IsServer) transportA.Open();
        }

        private void ClearTransportBuffers()
        {
            if (transportB != null) transportB.ClearBuffers();
            if (transportA != null) transportA.ClearBuffers();
        }

        private void TransportAFaulted(Exception exception)
        {
            FailAsync("端点 A 读取失败：" + exception.Message);
        }

        private void TransportBFaulted(Exception exception)
        {
            FailAsync("端点 B 读取失败：" + exception.Message);
        }

        private void TransportAStatusChanged(string message)
        {
            EmitLog("端点 A：" + message, false);
            senderSignal.Set();
        }

        private void TransportBStatusChanged(string message)
        {
            EmitLog("端点 B：" + message, false);
        }

        private void EmitSampledError(string message, long errorNumber)
        {
            long total = aReceivedError + bReceivedError + headerErrors + lostFrames;
            if (total <= 20 || errorNumber % 100 == 0) EmitLog(message, true);
        }

        private void FailAsync(string reason)
        {
            bool queueStop = false;
            lock (syncRoot)
            {
                if (isRunning)
                {
                    isRunning = false;
                    stopReason = reason;
                    senderSignal.Set();
                    queueStop = true;
                }
            }
            if (!queueStop) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                LoopTransport closingTransportA;
                LoopTransport closingTransportB;
                Thread closingSender;
                lock (syncRoot)
                {
                    elapsed.Stop();
                    closingTransportA = transportA;
                    closingTransportB = transportB;
                    closingSender = senderThread;
                    transportA = null;
                    transportB = null;
                    senderThread = null;
                }
                CloseTransport(closingTransportA, true);
                CloseTransport(closingTransportB, false);
                JoinSender(closingSender);
                EmitLog("测试异常停止：" + reason, true);
                Action<string> handler = TestStopped;
                if (handler != null) handler(reason);
            });
        }

        private void CleanupAfterStartFailure()
        {
            LoopTransport closingTransportA;
            LoopTransport closingTransportB;
            Thread closingSender;
            lock (syncRoot)
            {
                isRunning = false;
                elapsed.Stop();
                closingTransportA = transportA;
                closingTransportB = transportB;
                closingSender = senderThread;
                transportA = null;
                transportB = null;
                senderThread = null;
                senderSignal.Set();
            }
            CloseTransport(closingTransportA, true);
            CloseTransport(closingTransportB, false);
            JoinSender(closingSender);
        }

        private void ResetState()
        {
            aSent = aReceivedOk = aReceivedError = 0;
            bReceivedOk = bReceivedError = bSent = 0;
            totalBytes = crcErrors = headerErrors = resynchronizedBytes = 0;
            lostFrames = duplicateFrames = outOfOrderFrames = 0;
            errorBytes = errorBits = latencySamples = 0;
            totalRoundTripMilliseconds = lastRoundTripMilliseconds = 0.0;
            nextSequence = nextExpectedSequence = 0;
            pending.Clear();
            finalizedAhead.Clear();
            recentReceived.Clear();
            recentReceivedOrder.Clear();
            elapsed.Reset();
        }

        private void CloseTransport(LoopTransport transport, bool isEndpointA)
        {
            if (transport == null) return;
            try
            {
                if (isEndpointA)
                {
                    transport.DataReceived -= TransportADataReceived;
                    transport.Faulted -= TransportAFaulted;
                    transport.StatusChanged -= TransportAStatusChanged;
                }
                else
                {
                    transport.DataReceived -= TransportBDataReceived;
                    transport.Faulted -= TransportBFaulted;
                    transport.StatusChanged -= TransportBStatusChanged;
                }
            }
            catch { }
            finally { transport.Dispose(); }
        }

        private static void JoinSender(Thread thread)
        {
            if (thread != null && thread != Thread.CurrentThread && thread.IsAlive) thread.Join(2000);
        }

        private void EmitLog(string message, bool isError)
        {
            Action<string, bool> handler = LogAvailable;
            if (handler != null) handler(message, isError);
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException("SerialLoopController");
        }

        public void Dispose()
        {
            if (disposed) return;
            Stop("窗口关闭");
            senderSignal.Dispose();
            disposed = true;
        }
    }
}
