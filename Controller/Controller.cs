// =============================================================
//  GnwayController - 本地控制端
//  运行在你自己的电脑上，通过命名管道向服务端 Agent 发送命令
//  支持：交互式命令行 / 脚本批量执行 / 管道调用
// =============================================================

using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace GnwayController
{
    class Controller
    {
        const string PIPE_NAME = "GnwayAgentPipe";
        static string _server = "."; // 默认本机；远程改为服务器IP

        static void Main(string[] args)
        {
            // ── 命令行参数模式（适合在脚本里调用）─────────
            // controller.exe 服务器IP 动作|程序名|控件名|...
            if (args.Length >= 2)
            {
                _server = args[0];
                string result = Send(args[1]);
                Console.WriteLine(result);
                Environment.Exit(result.StartsWith("ERR") ? 1 : 0);
                return;
            }

            // ── 交互式模式 ───────────────────────────────
            if (args.Length == 1) _server = args[0];

            PrintHelp();

            while (true)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"\n[{_server}] > ");
                Console.ResetColor();

                string? line = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                // 内置命令
                if (line == "exit" || line == "quit") break;
                if (line == "help") { PrintHelp(); continue; }
                if (line.StartsWith("server "))
                {
                    _server = line.Substring(7).Trim();
                    Console.WriteLine($"已切换服务器: {_server}");
                    continue;
                }

                // 执行脚本文件
                if (line.StartsWith("run "))
                {
                    RunScript(line.Substring(4).Trim());
                    continue;
                }

                // 发送命令
                string result = Send(line);
                
                // 友好显示结果
                if (result.StartsWith("OK:"))
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(result.Substring(3));
                }
                else if (result.StartsWith("ERR:"))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("错误: " + result.Substring(4));
                }
                else
                {
                    Console.WriteLine(result);
                }
                Console.ResetColor();
            }
        }

        // ── 发送命令到服务端 ──────────────────────────────
        static string Send(string command, int timeoutMs = 10000)
        {
            try
            {
                using var client = new NamedPipeClientStream(
                    _server, PIPE_NAME, PipeDirection.InOut);

                client.Connect(timeoutMs);

                var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
                var reader = new StreamReader(client, Encoding.UTF8);

                writer.WriteLine(command);

                // 读取返回（可能是多行，如控件树）
                var sb = new StringBuilder();
                string? line;
                while ((line = reader.ReadLine()) != null)
                    sb.AppendLine(line);

                return sb.ToString().TrimEnd();
            }
            catch (TimeoutException)
            {
                return "ERR:连接超时，请确认 agent.exe 已在服务器上运行";
            }
            catch (Exception ex)
            {
                return $"ERR:{ex.Message}";
            }
        }

        // ── 执行脚本文件 ──────────────────────────────────
        // 脚本格式：每行一条命令；# 开头为注释；sleep N 等待N毫秒
        static void RunScript(string scriptPath)
        {
            if (!File.Exists(scriptPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"脚本文件不存在: {scriptPath}");
                Console.ResetColor();
                return;
            }

            Console.WriteLine($"执行脚本: {scriptPath}");
            int lineNo = 0, ok = 0, err = 0;

            foreach (string rawLine in File.ReadAllLines(scriptPath))
            {
                lineNo++;
                string line = rawLine.Trim();

                if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                    continue; // 跳过空行和注释

                if (line.StartsWith("sleep "))
                {
                    int ms = int.Parse(line.Substring(6).Trim());
                    Console.WriteLine($"  [等待] {ms}ms...");
                    Thread.Sleep(ms);
                    continue;
                }

                Console.Write($"  [L{lineNo}] {line} → ");
                string result = Send(line);

                if (result.StartsWith("OK:"))
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("✓ " + result.Substring(3));
                    ok++;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("✗ " + result);
                    err++;
                    // 遇到错误停止执行
                    break;
                }
                Console.ResetColor();
            }

            Console.WriteLine($"\n脚本完成: 成功 {ok} 条，失败 {err} 条");
        }

        static void PrintHelp()
        {
            Console.WriteLine(@"
╔════════════════════════════════════════════════════╗
║           GnwayController 云联自动化控制端          ║
╚════════════════════════════════════════════════════╝

【基本命令】
  windows                        列出服务端所有窗口
  tree|程序名[|层数]              查看控件树 (调试用)
  click|程序名|控件名             点击按钮
  click|程序名|控件名|父容器      在指定父容器内点击 (解决同名问题)
  click|程序名|控件名||索引       点击第N个同名控件 (从0开始)
  input|程序名|控件名|文字        输入文字
  scroll|程序名|控件名|方向       滚动 (up/down/left/right/top/bottom)
  scroll|程序名|控件名|down|large 大幅滚动 (翻页)
  scrollto|程序名|容器名|目标名   滚动直到目标控件可见
  wait|程序名|弹窗标题            等待弹窗出现
  wait|程序名|弹窗标题|confirm    等待弹窗并点确定
  wait|程序名|弹窗标题|cancel|30  等待30秒，点取消
  gettext|程序名|控件名           获取控件文字
  exists|程序名|控件名            检查控件是否存在
  select|程序名|控件名|选项       下拉选择

【内置命令】
  server 192.168.1.100           切换到其他服务器
  run 脚本文件路径                执行脚本文件
  help                           显示帮助
  exit                           退出

【脚本文件格式】
  # 这是注释
  sleep 1000                     等待1秒
  tree|ERP系统                   查看控件树
  click|ERP系统|保存
  wait|ERP系统|保存成功|confirm
");
        }
    }
}
