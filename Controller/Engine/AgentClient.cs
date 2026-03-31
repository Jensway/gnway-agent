// ============================================================
//  AgentClient.cs — 与服务端 Agent 通信（命名管道）
// ============================================================

using System;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace GnwayController.Engine
{
    public class AgentClient
    {
        private readonly string _serverIp;
        private readonly int    _port;
        private readonly int    _timeoutMs;

        public AgentClient(string serverIp, int port = 19090, int timeoutMs = 12000)
        {
            if (serverIp.Contains(":"))
            {
                var parts = serverIp.Split(':');
                _serverIp = parts[0];
                if (int.TryParse(parts[1], out int p)) port = p;
            }
            else
            {
                _serverIp = serverIp == "." || string.IsNullOrWhiteSpace(serverIp) ? "127.0.0.1" : serverIp;
            }
            
            _port      = port;
            _timeoutMs = timeoutMs;
        }

        /// <summary>发送命令，返回 Agent 回应（含 "OK:..." 或 "ERR:..."）</summary>
        public string Send(string command)
        {
            try
            {
                using var client = new System.Net.Sockets.TcpClient();
                // 设置超时
                client.ReceiveTimeout = _timeoutMs;
                client.SendTimeout = _timeoutMs;
                
                var connectTask = client.ConnectAsync(_serverIp, _port);
                if (!connectTask.Wait(_timeoutMs))
                {
                    return "ERR:连接超时，请确认 agent.exe 在服务器上运行且端口畅通";
                }

                using var stream = client.GetStream();
                var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
                var reader = new StreamReader(stream, Encoding.UTF8);

                writer.WriteLine(command);

                var sb = new StringBuilder();
                string? line;
                while ((line = reader.ReadLine()) != null)
                    sb.AppendLine(line);

                return sb.ToString().TrimEnd();
            }
            catch (TimeoutException)
            {
                return "ERR:连接超时，请确认 agent.exe 在服务器上运行";
            }
            catch (Exception ex)
            {
                return $"ERR:{ex.Message}";
            }
        }

        public bool IsOk(string result)   => result.StartsWith("OK");
        public bool IsTrue(string result) => result.TrimEnd() == "OK:true";
    }
}
