using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;

namespace RsLoopTest
{
    internal sealed class SerialLoopController : IDisposable
    {
        private readonly object syncRoot = new object();
        private SerialPort portA;
        private SerialPort portB;
        private FrameBuffer bufferA;
        private FrameBuffer bufferB;
        private byte[] expected;
        private LoopDataOptions dataOptions;
        private PayloadGenerator payloadGenerator;
        private Timer timeoutTimer;
        private Stopwatch elapsed = new Stopwatch();
        private Stopwatch roundTrip = new Stopwatch();
        private long aSent;
        private long aReceivedOk;
        private long aReceivedError;
        private long bReceivedOk;
        private long bReceivedError;
        private long bSent;
        private long totalBytes;
        private double totalRoundTripMilliseconds;
        private double lastRoundTripMilliseconds;
        private int timeoutMilliseconds;
        private int requestedTimeoutMilliseconds;
        private int baudRate;
        private LoopTestMode mode;
        private bool isRunning;
        private bool disposed;
        private string stopReason = "未启动";

        public event Action<string, bool> LogAvailable;
        public event Action<string> TestStopped;

        public void Start(string portAName, string portBName, int baudRate, LoopDataOptions options,
            int responseTimeoutMilliseconds, LoopTestMode mode)
        {
            if (string.IsNullOrWhiteSpace(portAName))
            {
                throw new ArgumentException("必须选择端口 A。", "portAName");
            }
            if (mode == LoopTestMode.DualPortRelay && string.IsNullOrWhiteSpace(portBName))
            {
                throw new ArgumentException("双端口模式必须选择端口 B。", "portBName");
            }
            if (mode == LoopTestMode.DualPortRelay &&
                string.Equals(portAName, portBName, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("端口 A 和端口 B 不能选择同一个串口。");
            }
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }
            options.Validate();

            int startingFrameLength = 0;
            int startingTimeout = 0;
            string startingFrameHex = string.Empty;

            try
            {
                lock (syncRoot)
                {
                    ThrowIfDisposed();
                    if (isRunning)
                    {
                        throw new InvalidOperationException("测试已在运行中。");
                    }

                    ResetCounters();
                    dataOptions = options.Clone();
                    payloadGenerator = new PayloadGenerator(dataOptions);
                    this.baudRate = baudRate;
                    this.mode = mode;
                    requestedTimeoutMilliseconds = responseTimeoutMilliseconds;
                    portA = CreatePort(portAName, baudRate);
                    portA.DataReceived += PortADataReceived;
                    if (mode == LoopTestMode.DualPortRelay)
                    {
                        portB = CreatePort(portBName, baudRate);
                        portB.DataReceived += PortBDataReceived;
                        portB.Open();
                    }
                    portA.Open();
                    if (portB != null)
                    {
                        portB.DiscardInBuffer();
                        portB.DiscardOutBuffer();
                    }
                    portA.DiscardInBuffer();
                    portA.DiscardOutBuffer();
                    isRunning = true;
                    stopReason = string.Empty;
                    elapsed.Restart();
                    timeoutTimer = new Timer(CheckTimeout, null, 200, 200);
                    BeginNextRound();
                    startingFrameLength = expected.Length;
                    startingTimeout = timeoutMilliseconds;
                    startingFrameHex = PayloadCodec.ToHex(expected);
                }
            }
            catch
            {
                SerialPort closingPortA;
                SerialPort closingPortB;
                lock (syncRoot)
                {
                    isRunning = false;
                    elapsed.Stop();
                    roundTrip.Stop();
                    DisposeTimer();
                    closingPortA = portA;
                    closingPortB = portB;
                    portA = null;
                    portB = null;
                }
                ClosePort(closingPortA, PortADataReceived);
                ClosePort(closingPortB, PortBDataReceived);
                throw;
            }

            int wirePasses = mode == LoopTestMode.SinglePortFullDuplex ? 1 : 2;
            int wireMilliseconds = SerialTiming.CalculateWireMilliseconds(startingFrameLength,
                baudRate, wirePasses);
            string modeName = mode == LoopTestMode.SinglePortFullDuplex
                ? "单端口全双工自环" : "双端口中继环回";
            EmitLog("测试已启动：" + modeName + "，" + baudRate + " baud，" + dataOptions.Describe() +
                "；首帧 " + startingFrameLength +
                " 字节，理论线路耗时约 " + wireMilliseconds + " ms，实际超时 " +
                startingTimeout + " ms。", false);
            EmitLog("A 端发送首帧：" + startingFrameHex, false);
        }

        public void Stop(string reason)
        {
            bool shouldNotify;
            SerialPort closingPortA;
            SerialPort closingPortB;
            lock (syncRoot)
            {
                shouldNotify = isRunning;
                isRunning = false;
                stopReason = string.IsNullOrWhiteSpace(reason) ? "用户停止" : reason;
                elapsed.Stop();
                roundTrip.Stop();
                DisposeTimer();
                closingPortA = portA;
                closingPortB = portB;
                portA = null;
                portB = null;
            }

            ClosePort(closingPortA, PortADataReceived);
            ClosePort(closingPortB, PortBDataReceived);

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
                    Elapsed = elapsed.Elapsed,
                    LastRoundTripMilliseconds = lastRoundTripMilliseconds,
                    AverageRoundTripMilliseconds = aReceivedOk == 0
                        ? 0.0 : totalRoundTripMilliseconds / aReceivedOk,
                    StopReason = stopReason
                };
            }
        }

        private static SerialPort CreatePort(string portName, int baudRate)
        {
            return new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                ReadTimeout = 1000,
                WriteTimeout = 1000,
                DtrEnable = false,
                RtsEnable = false,
                ReceivedBytesThreshold = 1
            };
        }

        private void PortADataReceived(object sender, SerialDataReceivedEventArgs eventArgs)
        {
            ReadAvailable(sender as SerialPort, true, ProcessAFrame, "A");
        }

        private void PortBDataReceived(object sender, SerialDataReceivedEventArgs eventArgs)
        {
            ReadAvailable(sender as SerialPort, false, ProcessBFrame, "B");
        }

        private void ReadAvailable(SerialPort port, bool isPortA, Action processFrame, string side)
        {
            try
            {
                lock (syncRoot)
                {
                    SerialPort currentPort = isPortA ? portA : portB;
                    FrameBuffer currentBuffer = isPortA ? bufferA : bufferB;
                    if (!isRunning || port == null || !ReferenceEquals(port, currentPort) ||
                        currentBuffer == null || !port.IsOpen)
                    {
                        return;
                    }

                    int available = port.BytesToRead;
                    if (available <= 0)
                    {
                        return;
                    }

                    byte[] incoming = new byte[available];
                    int read = port.Read(incoming, 0, incoming.Length);
                    currentBuffer.Append(incoming, read);
                    totalBytes += read;
                    processFrame();
                }
            }
            catch (Exception exception)
            {
                FailAsync(side + " 端串口读取失败：" + exception.Message);
            }
        }

        private void ProcessBFrame()
        {
            byte[] frame;
            if (!isRunning || !bufferB.TryTakeFrame(out frame))
            {
                return;
            }

            if (PayloadCodec.AreEqual(expected, frame))
            {
                bReceivedOk++;
            }
            else
            {
                bReceivedError++;
                EmitValidationError("B", frame, bReceivedError);
            }

            // 无论校验是否正确，B 都回传实际收到的数据。
            portB.Write(frame, 0, frame.Length);
            bSent++;
        }

        private void ProcessAFrame()
        {
            byte[] frame;
            if (!isRunning || !bufferA.TryTakeFrame(out frame))
            {
                return;
            }

            roundTrip.Stop();
            lastRoundTripMilliseconds = roundTrip.Elapsed.TotalMilliseconds;

            if (PayloadCodec.AreEqual(expected, frame))
            {
                aReceivedOk++;
                totalRoundTripMilliseconds += lastRoundTripMilliseconds;
            }
            else
            {
                aReceivedError++;
                EmitValidationError("A", frame, aReceivedError);
            }

            // 校验结果只影响统计，不再阻断下一轮发送。
            BeginNextRound();
        }

        private void BeginNextRound()
        {
            expected = payloadGenerator.CreateNext();
            bufferA = new FrameBuffer(expected.Length);
            bufferB = mode == LoopTestMode.DualPortRelay ? new FrameBuffer(expected.Length) : null;
            int wirePasses = mode == LoopTestMode.SinglePortFullDuplex ? 1 : 2;
            timeoutMilliseconds = SerialTiming.CalculateEffectiveTimeoutMilliseconds(
                expected.Length, baudRate, requestedTimeoutMilliseconds, wirePasses);
            portA.Write(expected, 0, expected.Length);
            aSent++;
            roundTrip.Restart();
        }

        private void CheckTimeout(object state)
        {
            try
            {
                lock (syncRoot)
                {
                    if (!isRunning || !roundTrip.IsRunning)
                    {
                        return;
                    }

                    if (roundTrip.ElapsedMilliseconds > timeoutMilliseconds)
                    {
                        aReceivedError++;
                        EmitLog("本轮回传超时，已跳过并继续（阈值 " + timeoutMilliseconds +
                            " ms，帧长 " + expected.Length + " 字节，A 错误累计 " +
                            aReceivedError + "）。", true);
                        DiscardPendingInput();
                        BeginNextRound();
                    }
                }
            }
            catch (Exception exception)
            {
                FailAsync("超时恢复时串口操作失败：" + exception.Message);
            }
        }

        private void DiscardPendingInput()
        {
            bufferA.Clear();
            if (bufferB != null)
            {
                bufferB.Clear();
            }
            if (portA != null && portA.IsOpen)
            {
                portA.DiscardInBuffer();
            }
            if (portB != null && portB.IsOpen)
            {
                portB.DiscardInBuffer();
            }
        }

        private void EmitValidationError(string side, byte[] actual, long sideErrorCount)
        {
            long totalErrors = aReceivedError + bReceivedError;
            if (totalErrors <= 10 || totalErrors % 100 == 0)
            {
                EmitLog(side + " 端校验失败（该端累计 " + sideErrorCount + "），收到：" +
                    PayloadCodec.ToHex(actual) + "；预期：" + PayloadCodec.ToHex(expected) +
                    "。测试继续运行。", true);
            }
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
                    queueStop = true;
                }
            }

            if (queueStop)
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    SerialPort closingPortA;
                    SerialPort closingPortB;
                    lock (syncRoot)
                    {
                        elapsed.Stop();
                        roundTrip.Stop();
                        DisposeTimer();
                        closingPortA = portA;
                        closingPortB = portB;
                        portA = null;
                        portB = null;
                    }
                    ClosePort(closingPortA, PortADataReceived);
                    ClosePort(closingPortB, PortBDataReceived);
                    EmitLog("测试异常停止：" + reason, true);
                    Action<string> handler = TestStopped;
                    if (handler != null)
                    {
                        handler(reason);
                    }
                });
            }
        }

        private void ResetCounters()
        {
            aSent = 0;
            aReceivedOk = 0;
            aReceivedError = 0;
            bReceivedOk = 0;
            bReceivedError = 0;
            bSent = 0;
            totalBytes = 0;
            totalRoundTripMilliseconds = 0.0;
            lastRoundTripMilliseconds = 0.0;
            elapsed.Reset();
            roundTrip.Reset();
        }

        private static void ClosePort(SerialPort port, SerialDataReceivedEventHandler handler)
        {
            if (port == null)
            {
                return;
            }

            try
            {
                port.DataReceived -= handler;
                if (port.IsOpen)
                {
                    port.Close();
                }
            }
            catch
            {
            }
            finally
            {
                port.Dispose();
            }
        }

        private void DisposeTimer()
        {
            Timer timer = timeoutTimer;
            timeoutTimer = null;
            if (timer != null)
            {
                timer.Dispose();
            }
        }

        private void EmitLog(string message, bool isError)
        {
            Action<string, bool> handler = LogAvailable;
            if (handler != null)
            {
                handler(message, isError);
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException("SerialLoopController");
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            Stop("窗口关闭");
            disposed = true;
        }
    }
}
