using System;

namespace RsLoopTest
{
    internal enum TransportKind
    {
        Serial,
        TcpClient,
        TcpServer,
        Udp
    }

    internal sealed class TransportSettings
    {
        public TransportKind Kind { get; set; }
        public string SerialPortName { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public int LocalPort { get; set; }

        public bool IsSerial { get { return Kind == TransportKind.Serial; } }
        public bool IsServer { get { return Kind == TransportKind.TcpServer; } }

        public void Validate()
        {
            if (Kind == TransportKind.Serial)
            {
                if (string.IsNullOrWhiteSpace(SerialPortName))
                    throw new ArgumentException("串口模式必须选择 COM 端口。");
                return;
            }
            if (string.IsNullOrWhiteSpace(Host))
                throw new ArgumentException("网络端点的主机地址不能为空。");
            ValidatePort(Port, "远端/监听端口");
            if (Kind == TransportKind.Udp) ValidatePort(LocalPort, "UDP 本地端口");
        }

        public string Describe()
        {
            switch (Kind)
            {
                case TransportKind.TcpClient:
                    return "TCP Client " + FormatHostPort(Host, Port);
                case TransportKind.TcpServer:
                    return "TCP Server " + FormatHostPort(Host, Port);
                case TransportKind.Udp:
                    return "UDP 本地 :" + LocalPort + " → " + FormatHostPort(Host, Port);
                default:
                    return SerialPortName;
            }
        }

        public static TransportSettings Parse(TransportKind kind, string serialPortName,
            string endpointText)
        {
            TransportSettings settings = new TransportSettings { Kind = kind };
            if (kind == TransportKind.Serial)
            {
                settings.SerialPortName = serialPortName;
            }
            else if (kind == TransportKind.Udp)
            {
                string[] parts = (endpointText ?? string.Empty).Split('@');
                if (parts.Length != 2)
                    throw new FormatException("UDP 格式应为：本地端口@远端主机:远端端口。");
                int localPort;
                if (!int.TryParse(parts[0].Trim(), out localPort))
                    throw new FormatException("UDP 本地端口无效。");
                settings.LocalPort = localPort;
                string host;
                int port;
                ParseHostPort(parts[1], out host, out port);
                settings.Host = host;
                settings.Port = port;
            }
            else
            {
                string host;
                int port;
                ParseHostPort(endpointText, out host, out port);
                settings.Host = host;
                settings.Port = port;
            }
            settings.Validate();
            return settings;
        }

        private static void ParseHostPort(string text, out string host, out int port)
        {
            string value = (text ?? string.Empty).Trim();
            int separator;
            if (value.StartsWith("[", StringComparison.Ordinal))
            {
                int closeBracket = value.IndexOf(']');
                separator = closeBracket >= 0 && closeBracket + 1 < value.Length &&
                    value[closeBracket + 1] == ':' ? closeBracket + 1 : -1;
                host = closeBracket > 0 ? value.Substring(1, closeBracket - 1) : string.Empty;
            }
            else
            {
                separator = value.LastIndexOf(':');
                host = separator > 0 ? value.Substring(0, separator).Trim() : string.Empty;
            }
            int parsedPort;
            if (separator < 0 || !int.TryParse(value.Substring(separator + 1), out parsedPort))
                throw new FormatException("网络端点格式应为：主机:端口。");
            port = parsedPort;
        }

        private static string FormatHostPort(string host, int port)
        {
            return host != null && host.IndexOf(':') >= 0
                ? "[" + host + "]:" + port : host + ":" + port;
        }

        private static void ValidatePort(int value, string name)
        {
            if (value < 1 || value > 65535)
                throw new ArgumentOutOfRangeException(name, name + "必须在 1–65535 之间。");
        }
    }
}
