using System;
using System.IO;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace RsLoopTest
{
    internal abstract class LoopTransport : IDisposable
    {
        public event Action<byte[], int> DataReceived;
        public event Action<Exception> Faulted;
        public event Action<string> StatusChanged;

        public abstract bool IsReady { get; }
        public abstract bool IsSerial { get; }
        public abstract bool IsServer { get; }
        public abstract string Description { get; }
        public abstract void Open();
        public abstract bool TryWrite(byte[] data, int offset, int count);
        public abstract void ClearBuffers();
        public abstract void Close();

        protected void RaiseData(byte[] data, int count)
        {
            Action<byte[], int> handler = DataReceived;
            if (handler != null) handler(data, count);
        }

        protected void RaiseFault(Exception exception)
        {
            Action<Exception> handler = Faulted;
            if (handler != null) handler(exception);
        }

        protected void RaiseStatus(string message)
        {
            Action<string> handler = StatusChanged;
            if (handler != null) handler(message);
        }

        public void Dispose()
        {
            Close();
        }
    }

    internal static class LoopTransportFactory
    {
        public static LoopTransport Create(TransportSettings settings, int baudRate)
        {
            switch (settings.Kind)
            {
                case TransportKind.TcpClient:
                    return new TcpClientLoopTransport(settings.Host, settings.Port);
                case TransportKind.TcpServer:
                    return new TcpServerLoopTransport(settings.Host, settings.Port);
                case TransportKind.Udp:
                    return new UdpLoopTransport(settings.LocalPort, settings.Host, settings.Port);
                default:
                    return new SerialPortLoopTransport(settings.SerialPortName, baudRate);
            }
        }
    }

    internal sealed class SerialPortLoopTransport : LoopTransport
    {
        private readonly SerialPort port;

        public SerialPortLoopTransport(string portName, int baudRate)
        {
            port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                ReadTimeout = 1000,
                WriteTimeout = 3000,
                DtrEnable = false,
                RtsEnable = false,
                ReceivedBytesThreshold = 1
            };
            port.DataReceived += PortDataReceived;
        }

        public override bool IsReady { get { return port.IsOpen; } }
        public override bool IsSerial { get { return true; } }
        public override bool IsServer { get { return false; } }
        public override string Description { get { return port.PortName; } }
        public override void Open() { port.Open(); }

        public override bool TryWrite(byte[] data, int offset, int count)
        {
            if (!port.IsOpen) return false;
            port.Write(data, offset, count);
            return true;
        }

        public override void ClearBuffers()
        {
            if (!port.IsOpen) return;
            port.DiscardInBuffer();
            port.DiscardOutBuffer();
        }

        public override void Close()
        {
            try
            {
                port.DataReceived -= PortDataReceived;
                if (port.IsOpen) port.Close();
            }
            catch { }
            finally { port.Dispose(); }
        }

        private void PortDataReceived(object sender, SerialDataReceivedEventArgs eventArgs)
        {
            try
            {
                int available = port.BytesToRead;
                if (available <= 0) return;
                byte[] data = new byte[available];
                int read = port.Read(data, 0, data.Length);
                if (read > 0) RaiseData(data, read);
            }
            catch (Exception exception) { RaiseFault(exception); }
        }
    }

    internal sealed class TcpClientLoopTransport : LoopTransport
    {
        private readonly string host;
        private readonly int port;
        private readonly object writeLock = new object();
        private TcpClient client;
        private Thread receiveThread;
        private volatile bool active;

        public TcpClientLoopTransport(string host, int port)
        {
            this.host = host;
            this.port = port;
        }

        public override bool IsReady { get { return active && client != null && client.Connected; } }
        public override bool IsSerial { get { return false; } }
        public override bool IsServer { get { return false; } }
        public override string Description { get { return "TCP Client " + host + ":" + port; } }

        public override void Open()
        {
            client = new TcpClient();
            client.NoDelay = true;
            client.SendTimeout = 3000;
            IAsyncResult connecting = client.BeginConnect(host, port, null, null);
            WaitHandle waitHandle = connecting.AsyncWaitHandle;
            bool connected = waitHandle.WaitOne(5000);
            waitHandle.Close();
            if (!connected)
            {
                client.Close();
                throw new TimeoutException("TCP Client 连接超时：" + host + ":" + port);
            }
            client.EndConnect(connecting);
            active = true;
            receiveThread = StartBackground(ReceiveLoop, "TcpClientReceiver");
        }

        public override bool TryWrite(byte[] data, int offset, int count)
        {
            if (!IsReady) return false;
            lock (writeLock)
            {
                NetworkStream stream = client.GetStream();
                stream.Write(data, offset, count);
            }
            return true;
        }

        public override void ClearBuffers() { }

        public override void Close()
        {
            active = false;
            TcpClient closing = client;
            client = null;
            if (closing != null) try { closing.Close(); } catch { }
            Join(receiveThread);
            receiveThread = null;
        }

        private void ReceiveLoop()
        {
            try
            {
                NetworkStream stream = client.GetStream();
                byte[] buffer = new byte[8192];
                while (active)
                {
                    int read = stream.Read(buffer, 0, buffer.Length);
                    if (read <= 0) throw new IOException("TCP 连接已由对端关闭。");
                    byte[] data = new byte[read];
                    Array.Copy(buffer, data, read);
                    RaiseData(data, read);
                }
            }
            catch (Exception exception) { if (active) RaiseFault(exception); }
        }

        private static Thread StartBackground(ThreadStart action, string name)
        {
            Thread thread = new Thread(action) { IsBackground = true, Name = name };
            thread.Start();
            return thread;
        }

        private static void Join(Thread thread)
        {
            if (thread != null && thread != Thread.CurrentThread && thread.IsAlive) thread.Join(1500);
        }
    }

    internal sealed class TcpServerLoopTransport : LoopTransport
    {
        private readonly string host;
        private readonly int port;
        private readonly object clientLock = new object();
        private TcpListener listener;
        private TcpClient client;
        private Thread receiveThread;
        private volatile bool active;

        public TcpServerLoopTransport(string host, int port)
        {
            this.host = host;
            this.port = port;
        }

        public override bool IsReady { get { return active && client != null && client.Connected; } }
        public override bool IsSerial { get { return false; } }
        public override bool IsServer { get { return true; } }
        public override string Description { get { return "TCP Server " + host + ":" + port; } }

        public override void Open()
        {
            IPAddress address = ResolveBindAddress(host);
            listener = new TcpListener(address, port);
            listener.Start();
            active = true;
            receiveThread = new Thread(AcceptAndReceiveLoop)
            {
                IsBackground = true,
                Name = "TcpServerReceiver"
            };
            receiveThread.Start();
            RaiseStatus("正在监听 " + host + ":" + port + "，等待 TCP 客户端连接。");
        }

        public override bool TryWrite(byte[] data, int offset, int count)
        {
            lock (clientLock)
            {
                if (!IsReady) return false;
                client.GetStream().Write(data, offset, count);
                return true;
            }
        }

        public override void ClearBuffers() { }

        public override void Close()
        {
            active = false;
            if (listener != null) try { listener.Stop(); } catch { }
            lock (clientLock)
            {
                if (client != null) try { client.Close(); } catch { }
                client = null;
            }
            Join(receiveThread);
            receiveThread = null;
            listener = null;
        }

        private void AcceptAndReceiveLoop()
        {
            try
            {
                while (active)
                {
                    TcpClient accepted = listener.AcceptTcpClient();
                    accepted.NoDelay = true;
                    accepted.SendTimeout = 3000;
                    lock (clientLock) client = accepted;
                    RaiseStatus("TCP 客户端已连接：" + accepted.Client.RemoteEndPoint + "。");
                    try
                    {
                        NetworkStream stream = accepted.GetStream();
                        byte[] buffer = new byte[8192];
                        while (active)
                        {
                            int read = stream.Read(buffer, 0, buffer.Length);
                            if (read <= 0) break;
                            byte[] data = new byte[read];
                            Array.Copy(buffer, data, read);
                            RaiseData(data, read);
                        }
                    }
                    finally
                    {
                        lock (clientLock)
                        {
                            if (ReferenceEquals(client, accepted)) client = null;
                        }
                        try { accepted.Close(); } catch { }
                    }
                    if (active) RaiseStatus("TCP 客户端已断开，继续等待新连接。");
                }
            }
            catch (Exception exception) { if (active) RaiseFault(exception); }
        }

        private static IPAddress ResolveBindAddress(string value)
        {
            if (value == "0.0.0.0" || value == "*") return IPAddress.Any;
            if (value == "::") return IPAddress.IPv6Any;
            IPAddress address;
            if (IPAddress.TryParse(value, out address)) return address;
            IPAddress[] addresses = Dns.GetHostAddresses(value);
            if (addresses.Length == 0) throw new ArgumentException("无法解析监听地址：" + value);
            return addresses[0];
        }

        private static void Join(Thread thread)
        {
            if (thread != null && thread != Thread.CurrentThread && thread.IsAlive) thread.Join(1500);
        }
    }

    internal sealed class UdpLoopTransport : LoopTransport
    {
        private readonly int localPort;
        private readonly string remoteHost;
        private readonly int remotePort;
        private UdpClient client;
        private IPEndPoint remoteEndPoint;
        private Thread receiveThread;
        private volatile bool active;

        public UdpLoopTransport(int localPort, string remoteHost, int remotePort)
        {
            this.localPort = localPort;
            this.remoteHost = remoteHost;
            this.remotePort = remotePort;
        }

        public override bool IsReady { get { return active && client != null; } }
        public override bool IsSerial { get { return false; } }
        public override bool IsServer { get { return false; } }
        public override string Description
        {
            get { return "UDP :" + localPort + " → " + remoteHost + ":" + remotePort; }
        }

        public override void Open()
        {
            IPAddress[] addresses = Dns.GetHostAddresses(remoteHost);
            if (addresses.Length == 0) throw new ArgumentException("无法解析 UDP 地址：" + remoteHost);
            IPAddress address = addresses[0];
            remoteEndPoint = new IPEndPoint(address, remotePort);
            client = new UdpClient(address.AddressFamily);
            IPAddress localAddress = address.AddressFamily == AddressFamily.InterNetworkV6
                ? IPAddress.IPv6Any : IPAddress.Any;
            client.Client.Bind(new IPEndPoint(localAddress, localPort));
            DisableWindowsUdpConnectionReset(client.Client);
            active = true;
            receiveThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "UdpReceiver" };
            receiveThread.Start();
        }

        public override bool TryWrite(byte[] data, int offset, int count)
        {
            if (!IsReady) return false;
            byte[] datagram = data;
            if (offset != 0 || count != data.Length)
            {
                datagram = new byte[count];
                Array.Copy(data, offset, datagram, 0, count);
            }
            client.Send(datagram, datagram.Length, remoteEndPoint);
            return true;
        }

        public override void ClearBuffers() { }

        public override void Close()
        {
            active = false;
            UdpClient closing = client;
            client = null;
            if (closing != null) try { closing.Close(); } catch { }
            Join(receiveThread);
            receiveThread = null;
        }

        private void ReceiveLoop()
        {
            try
            {
                IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                while (active)
                {
                    byte[] data = client.Receive(ref sender);
                    if (data != null && data.Length > 0) RaiseData(data, data.Length);
                }
            }
            catch (Exception exception) { if (active) RaiseFault(exception); }
        }

        private static void Join(Thread thread)
        {
            if (thread != null && thread != Thread.CurrentThread && thread.IsAlive) thread.Join(1500);
        }

        private static void DisableWindowsUdpConnectionReset(Socket socket)
        {
            // Windows 默认会把 ICMP Port Unreachable 转成 WSAECONNRESET。
            // 环回测试应将无响应交给帧超时统计，而不是终止整个 UDP 端点。
            const int SioUdpConnectionReset = unchecked((int)0x9800000C);
            try
            {
                socket.IOControl((IOControlCode)SioUdpConnectionReset,
                    new byte[] { 0, 0, 0, 0 }, null);
            }
            catch (SocketException) { }
            catch (NotSupportedException) { }
        }
    }
}
