// =============================================================
//  GnwayController — 客户端（运行在你自己的电脑上）
//  通过命名管道向服务端 Agent 发送命令，控制云桌面内的程序
//
//  模式一：交互式 / 单命令 / 脚本文件
//    controller.exe [服务器IP]
//    controller.exe 192.168.1.105 windows
//    controller.exe 192.168.1.105  （进入交互式，输入 run 脚本路径）
//
//  模式二：发票自动化（--invoice）
//    controller.exe 192.168.1.105 --invoice
//    controller.exe 192.168.1.105 --invoice 发票管理 发票详情
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
            if (args.Length >= 1) _server = args[0];

            // ── 模式二：发票自动化 ──────────────────────────
            // controller.exe <IP> --invoice [列表窗口] [详情窗口]
            if (args.Length >= 2 && args[1] == "--invoice")
            {
                string winList   = args.Length >= 3 ? args[2] : "发票管理";
                string winDetail = args.Length >= 4 ? args[3] : "发票详情";
                InvoiceRunner.Run(_server, winList, winDetail, Send);
                return;
            }

            // ── 模式一A：单命令模式（适合脚本调用）──────────
            // controller.exe <IP> <命令>
            if (args.Length >= 2)
            {
                string result = Send(args[1]);
                Console.WriteLine(result);
                Environment.Exit(result.StartsWith("ERR") ? 1 : 0);
                return;
            }

            // ── 模式一B：交互式 ─────────────────────────────
            PrintHelp();

            while (true)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"\n[{_server}] > ");
                Console.ResetColor();

                string? line = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                if (line == "exit" || line == "quit") break;
                if (line == "help") { PrintHelp(); continue; }

                if (line.StartsWith("server "))
                {
                    _server = line.Substring(7).Trim();
                    Console.WriteLine($"已切换服务器: {_server}");
                    continue;
                }

                if (line.StartsWith("run "))
                {
                    RunScript(line.Substring(4).Trim());
                    continue;
                }

                // 发票自动化模式（交互式内也可触发）
                if (line.StartsWith("invoice"))
                {
                    var parts = line.Split(' ');
                    string winList   = parts.Length >= 2 ? parts[1] : "发票管理";
                    string winDetail = parts.Length >= 3 ? parts[2] : "发票详情";
                    InvoiceRunner.Run(_server, winList, winDetail, Send);
                    continue;
                }

                // 发送普通命令
                string res = Send(line);
                if (res.StartsWith("OK:"))
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(res.Substring(3));
                }
                else if (res.StartsWith("ERR:"))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("错误: " + res.Substring(4));
                }
                else
                {
                    Console.WriteLine(res);
                }
                Console.ResetColor();
            }
        }

        // ── 发送命令到服务端 Agent ─────────────────────────
        internal static string Send(string command, int timeoutMs = 12000)
        {
            try
            {
                using var client = new NamedPipeClientStream(
                    _server, PIPE_NAME, PipeDirection.InOut);

                client.Connect(timeoutMs);

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
                return "ERR:连接超时，请确认 agent.exe 已在服务器上运行";
            }
            catch (Exception ex)
            {
                return $"ERR:{ex.Message}";
            }
        }

        // ── 执行脚本文件 ───────────────────────────────────
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

                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

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
                    break; // 遇到错误停止
                }
                Console.ResetColor();
            }

            Console.WriteLine($"\n脚本完成: 成功 {ok} 条，失败 {err} 条");
        }

        static void PrintHelp()
        {
            Console.WriteLine(@"
╔══════════════════════════════════════════════════════╗
║           GnwayController 云联自动化客户端            ║
╚══════════════════════════════════════════════════════╝

【基本命令】
  windows                        列出服务端所有窗口
  tree|程序名[|层数]              查看控件树 (调试用)
  click|程序名|控件名             点击按钮
  click|程序名|控件名|父容器      在指定父容器内点击
  click|程序名|控件名||索引       点击第N个同名控件(从0起)
  input|程序名|控件名|文字        输入文字
  scroll|程序名|控件名|方向       滚动(up/down/left/right/top/bottom)
  scroll|程序名|控件名|down|large 大幅滚动(翻页)
  scrollto|程序名|容器名|目标名   滚动直到目标控件可见
  wait|程序名|弹窗标题            等待弹窗出现
  wait|程序名|弹窗标题|confirm    等待弹窗并点确定
  wait|程序名|弹窗标题|cancel|30  等待30秒后点取消
  gettext|程序名|控件名           获取控件文字
  exists|程序名|控件名            检查控件是否存在
  select|程序名|控件名|选项       下拉选择
  focus|程序名|控件名             设置焦点

【内置命令】
  server 192.168.1.100           切换到其他服务器
  run 脚本文件路径                执行脚本文件
  invoice [列表窗口] [详情窗口]   启动发票自动化处理
  help                           显示帮助
  exit / quit                    退出

【命令行直接调用（适合批处理/CI）】
  controller.exe 192.168.1.105 windows
  controller.exe 192.168.1.105 click|ERP系统|保存

【发票自动化模式】
  controller.exe 192.168.1.105 --invoice
  controller.exe 192.168.1.105 --invoice 发票管理 发票详情
");
        }
    }
}
