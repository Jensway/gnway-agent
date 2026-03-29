// =============================================================
//  InvoiceRunner — 发票自动化处理客户端
//  通过命名管道调用服务端 Agent，循环处理"未处理"发票
//  直到列表中全部变为"已生成"
//  编译：dotnet build / GitHub Actions
// =============================================================

using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace GnwayInvoiceRunner
{
    class InvoiceRunner
    {
        // ── 配置区（可按实际窗口名修改） ─────────────────────
        const string PIPE_NAME = "GnwayAgentPipe";

        static string _server  = ".";           // Agent IP，命令行第1参数覆盖
        static string _winList = "发票管理";    // 列表窗口标题（模糊匹配）
        static string _winDetail = "发票详情";  // 详情窗口标题（模糊匹配）

        // 超时（秒）
        const int T_GENERATE = 60;   // 等"发票详情"窗口出现
        const int T_SAVE     = 30;   // 等"审核"按钮可用（保存完成）
        const int T_AUDIT    = 30;   // 等"审核成功"弹窗
        const int T_JIUJI    = 10;   // 等"勾稽成功"弹窗（可能不出现）
        const int MAX_ROUNDS = 200;  // 最多循环次数，防死循环
        // ─────────────────────────────────────────────────────

        static void Main(string[] args)
        {
            if (args.Length >= 1) _server   = args[0];
            if (args.Length >= 2) _winList  = args[1];
            if (args.Length >= 3) _winDetail = args[2];

            Banner();

            // 连通性检查
            Log("检查 Agent 连通性...");
            string ping = Send("windows");
            if (ping.StartsWith("ERR"))
            {
                Error($"无法连接 Agent：{ping}");
                Error("请确认 agent.exe 已在服务器运行，且 IP 正确");
                Environment.Exit(1);
            }
            Log("✓ Agent 连接正常\n");

            // ── 主循环 ──────────────────────────────────────
            for (int round = 1; round <= MAX_ROUNDS; round++)
            {
                Log($"【第 {round} 轮】检查是否还有「未处理」...");

                if (!HasUnprocessed())
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Log("✅ 全部已生成，任务完成！");
                    Console.ResetColor();
                    break;
                }

                if (!RunOneCycle(round))
                {
                    Error("流程中断，请检查 ERP 界面状态");
                    Environment.Exit(1);
                }

                Console.WriteLine();
            }

            Console.WriteLine("\n按任意键退出...");
            Console.ReadKey();
        }

        // ── 单次发票处理流程 ──────────────────────────────────
        static bool RunOneCycle(int round)
        {
            // ① 选中第一条"未处理"行
            Log("① 选中「未处理」");
            string r = Send($"click|{_winList}|未处理");
            if (!Ok(r))
            {
                // 备用：把焦点给列表，用 ↓ 键导航
                Log("  直接点击失败，改用键盘 ↓ 导航...");
                Send($"focus|{_winList}|{_winList}");
                Thread.Sleep(200);
                // 发 ↓ 键（SendKeys 格式）
                Send($"input|{_winList}|{_winList}|{{DOWN}}");
            }
            Thread.Sleep(400);

            // ② 点击"生成"按钮
            Log("② 点击「生成」");
            r = Send($"click|{_winList}|生成");
            if (!Ok(r)) return Fail("生成", r);

            // ③ 等待"发票详情"窗口出现
            Log($"③ 等待「{_winDetail}」窗口（最多 {T_GENERATE}s）...");
            if (!WaitWindowAppear(_winDetail, T_GENERATE))
                return Fail("等待发票详情窗口", "超时");
            Log($"  ✓ 「{_winDetail}」已出现");
            Thread.Sleep(400);

            // ④ 点击"保存"
            Log("④ 点击「保存」");
            r = Send($"click|{_winDetail}|保存");
            if (!Ok(r)) return Fail("保存", r);

            // ⑤ 等待"审核"按钮出现（保存完成后才激活）
            Log($"⑤ 等待「审核」按钮可用（最多 {T_SAVE}s）...");
            if (!WaitControlExists(_winDetail, "审核", T_SAVE))
                return Fail("等待审核按钮", "超时，可能保存未完成");
            Log("  ✓ 「审核」已可用");
            Thread.Sleep(300);

            // ⑥ 点击"审核"
            Log("⑥ 点击「审核」");
            r = Send($"click|{_winDetail}|审核");
            if (!Ok(r)) return Fail("审核", r);

            // ⑦ 等待"审核成功"弹窗并点确定（必须出现）
            Log($"⑦ 等待「审核成功」弹窗（最多 {T_AUDIT}s）...");
            if (!DismissPopup("审核成功", T_AUDIT, required: true))
                return Fail("审核成功弹窗", "超时，审核可能失败");
            Thread.Sleep(400);

            // ⑧ 等待"勾稽成功"弹窗（可能不出现，跳过也没关系）
            Log("⑧ 检查「勾稽成功」弹窗（可能不出现）...");
            DismissPopup("勾稽成功", T_JIUJI, required: false);
            Thread.Sleep(300);

            // ⑨ 点击"退出"回到列表
            Log("⑨ 点击「退出」");
            r = Send($"click|{_winDetail}|退出");
            if (!Ok(r))
            {
                // 有些界面叫"关闭"
                r = Send($"click|{_winDetail}|关闭");
            }
            Log($"  {(Ok(r) ? "✓" : "△")} {r}");

            Thread.Sleep(800); // 等列表窗口完全恢复
            return true;
        }

        // ── 辅助：等待窗口出现 ───────────────────────────────
        static bool WaitWindowAppear(string title, int timeoutSec)
        {
            var deadline = DateTime.Now.AddSeconds(timeoutSec);
            while (DateTime.Now < deadline)
            {
                // 用 exists 检测窗口是否可访问
                string r = Send($"exists|{title}|{title}");
                if (r == "OK:true") return true;
                Thread.Sleep(1000);
            }
            return false;
        }

        // ── 辅助：等待控件出现 ───────────────────────────────
        static bool WaitControlExists(string window, string control, int timeoutSec)
        {
            var deadline = DateTime.Now.AddSeconds(timeoutSec);
            while (DateTime.Now < deadline)
            {
                string r = Send($"exists|{window}|{control}");
                if (r == "OK:true") return true;
                Thread.Sleep(1000);
            }
            return false;
        }

        // ── 辅助：等待弹窗并点确定 ──────────────────────────
        static bool DismissPopup(string title, int timeoutSec, bool required)
        {
            string r = Send($"wait|{title}|{title}|confirm|{timeoutSec}");
            if (Ok(r))
            {
                Log($"  ✓ 弹窗「{title}」已点确定");
                return true;
            }
            Log($"  ○ 弹窗「{title}」未出现{(required ? "" : "（跳过）")}");
            return !required; // 非必须弹窗返回 true，必须弹窗返回 false
        }

        // ── 辅助：检查是否还有"未处理"条目 ─────────────────
        static bool HasUnprocessed()
        {
            string r = Send($"exists|{_winList}|未处理");
            return r == "OK:true";
        }

        // ── 核心：发送命令到 Agent ───────────────────────────
        static string Send(string command, int timeoutMs = 12000)
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
                return "ERR:连接超时，请确认 agent.exe 正在运行";
            }
            catch (Exception ex)
            {
                return $"ERR:{ex.Message}";
            }
        }

        // ── 日志工具 ─────────────────────────────────────────
        static void Log(string msg)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
        }

        static void Error(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✗ {msg}");
            Console.ResetColor();
        }

        static bool Fail(string step, string reason)
        {
            Error($"步骤「{step}」失败：{reason}");
            return false;
        }

        static bool Ok(string result) => result.StartsWith("OK");

        static void Banner()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("╔══════════════════════════════════════════╗");
            Console.WriteLine("║       GnwayInvoiceRunner — 发票自动化    ║");
            Console.WriteLine("╚══════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine($"  Agent 服务器: {_server}");
            Console.WriteLine($"  列表窗口:     {_winList}");
            Console.WriteLine($"  详情窗口:     {_winDetail}");
            Console.WriteLine();
            Console.WriteLine("用法: invoice-runner.exe [服务器IP] [列表窗口名] [详情窗口名]");
            Console.WriteLine("示例: invoice-runner.exe 192.168.1.105 发票管理 发票详情");
            Console.WriteLine(new string('─', 50));
            Console.WriteLine();
        }
    }
}
