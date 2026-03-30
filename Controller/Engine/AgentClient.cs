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
        private readonly string _server;
        private readonly string _pipeName;
        private readonly int    _timeoutMs;

        public AgentClient(string server,
                           string pipeName  = "GnwayAgentPipe",
                           int    timeoutMs = 12000)
        {
            _server    = server;
            _pipeName  = pipeName;
            _timeoutMs = timeoutMs;
        }

        /// <summary>发送命令，返回 Agent 回应（含 "OK:..." 或 "ERR:..."）</summary>
        public string Send(string command)
        {
            try
            {
                using var client = new NamedPipeClientStream(
                    _server, _pipeName, PipeDirection.InOut);

                client.Connect(_timeoutMs);

                var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
                var reader = new StreamReader(client, Encoding.UTF8);

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
