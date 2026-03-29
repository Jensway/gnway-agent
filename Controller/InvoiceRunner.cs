// =============================================================
//  InvoiceRunner — 发票自动化处理逻辑（客户端内置模块）
//  作为 Controller 的一部分运行，无需独立 exe
//  触发方式：
//    命令行：controller.exe <IP> --invoice [列表窗口] [详情窗口]
//    交互式：invoice [列表窗口] [详情窗口]
// =============================================================

using System;
using System.Threading;

namespace GnwayController
{
    static class InvoiceRunner
    {
        // 超时配置（秒）
        const int T_GENERATE = 60;   // 等"发票详情"窗口出现（生成时间不确定）
        const int T_SAVE     = 30;   // 等"审核"按钮可用（保存完成）
        const int T_AUDIT    = 30;   // 等"审核成功"弹窗
        const int T_JIUJI    = 10;   // 等"勾稽成功"弹窗（可能不出现）
        const int MAX_ROUNDS = 200;  // 最多循环次数

        // Send 委托：由 Controller 传入，复用同一条管道连接逻辑
        static Func<string, string>? _send;
        static string _winList   = "发票管理";
        static string _winDetail = "发票详情";

        public static void Run(
            string server,
            string winList,
            string winDetail,
            Func<string, string> sendFunc)
        {
            _send      = sendFunc;
            _winList   = winList;
            _winDetail = winDetail;

            Banner(server);

            // 连通性检查
            Log("检查 Agent 连通性...");
            string ping = Send("windows");
            if (ping.StartsWith("ERR"))
            {
                Err($"无法连接 Agent：{ping}");
                Err("请确认 agent.exe 已在服务器运行，且 IP 正确");
                return;
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
                    return;
                }

                if (!ProcessOne(round))
                {
                    Err("流程中断，请检查 ERP 界面状态");
                    return;
                }

                Console.WriteLine();
            }

            Err($"已达最大循环次数 {MAX_ROUNDS}，强制停止");
        }

        // ── 单次发票处理流程 ──────────────────────────────────
        static bool ProcessOne(int round)
        {
            // ① 选中第一条"未处理"行
            Log("① 选中「未处理」");
            string r = Send($"click|{_winList}|未处理");
            if (!Ok(r))
            {
                Log("  直接点击失败，改用键盘 ↓ 导航...");
                Send($"focus|{_winList}|{_winList}");
                Thread.Sleep(200);
                Send($"input|{_winList}|{_winList}|{{DOWN}}");
            }
            Thread.Sleep(400);

            // ② 点击"生成"
            Log("② 点击「生成」");
            r = Send($"click|{_winList}|生成");
            if (!Ok(r)) return Fail("生成", r);

            // ③ 等待"发票详情"窗口出现
            Log($"③ 等待「{_winDetail}」窗口（最多 {T_GENERATE}s）...");
            if (!WaitWindowAppear(_winDetail, T_GENERATE))
                return Fail($"等待{_winDetail}窗口", "超时");
            Log($"  ✓ 「{_winDetail}」已出现");
            Thread.Sleep(400);

            // ④ 点击"保存"
            Log("④ 点击「保存」");
            r = Send($"click|{_winDetail}|保存");
            if (!Ok(r)) return Fail("保存", r);

            // ⑤ 等待"审核"按钮可用（保存完成后激活）
            Log($"⑤ 等待「审核」按钮可用（最多 {T_SAVE}s）...");
            if (!WaitControlExists(_winDetail, "审核", T_SAVE))
                return Fail("等待审核按钮", "超时，保存可能未完成");
            Log("  ✓ 「审核」已可用");
            Thread.Sleep(300);

            // ⑥ 点击"审核"
            Log("⑥ 点击「审核」");
            r = Send($"click|{_winDetail}|审核");
            if (!Ok(r)) return Fail("审核", r);

            // ⑦ 等待"审核成功"弹窗（必须出现）
            Log($"⑦ 等待「审核成功」弹窗（最多 {T_AUDIT}s）...");
            if (!DismissPopup("审核成功", T_AUDIT, required: true))
                return Fail("审核成功弹窗", "超时，审核可能失败");
            Thread.Sleep(400);

            // ⑧ 等待"勾稽成功"弹窗（可能不出现，跳过也无妨）
            Log("⑧ 检查「勾稽成功」弹窗（可能不出现）...");
            DismissPopup("勾稽成功", T_JIUJI, required: false);
            Thread.Sleep(300);

            // ⑨ 点击"退出"回到列表
            Log("⑨ 点击「退出」");
            r = Send($"click|{_winDetail}|退出");
            if (!Ok(r)) r = Send($"click|{_winDetail}|关闭"); // 兜底
            Log($"  {(Ok(r) ? "✓" : "△")} {r}");

            Thread.Sleep(800);
            return true;
        }

        // ── 等待窗口出现 ─────────────────────────────────────
        static bool WaitWindowAppear(string title, int timeoutSec)
        {
            var deadline = DateTime.Now.AddSeconds(timeoutSec);
            while (DateTime.Now < deadline)
            {
                if (Send($"exists|{title}|{title}") == "OK:true") return true;
                Thread.Sleep(1000);
            }
            return false;
        }

        // ── 等待控件出现 ─────────────────────────────────────
        static bool WaitControlExists(string window, string control, int timeoutSec)
        {
            var deadline = DateTime.Now.AddSeconds(timeoutSec);
            while (DateTime.Now < deadline)
            {
                if (Send($"exists|{window}|{control}") == "OK:true") return true;
                Thread.Sleep(1000);
            }
            return false;
        }

        // ── 等待弹窗并点确定 ─────────────────────────────────
        static bool DismissPopup(string title, int timeoutSec, bool required)
        {
            string r = Send($"wait|{title}|{title}|confirm|{timeoutSec}");
            if (Ok(r)) { Log($"  ✓ 弹窗「{title}」已点确定"); return true; }
            Log($"  ○ 弹窗「{title}」未出现{(required ? "" : "（跳过）")}");
            return !required;
        }

        // ── 检查是否还有"未处理" ─────────────────────────────
        static bool HasUnprocessed() =>
            Send($"exists|{_winList}|未处理") == "OK:true";

        // ── 转发到 Controller.Send ───────────────────────────
        static string Send(string cmd) => _send!(cmd);
        static bool Ok(string r) => r.StartsWith("OK");

        static void Log(string msg) =>
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");

        static void Err(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✗ {msg}");
            Console.ResetColor();
        }

        static bool Fail(string step, string reason)
        {
            Err($"步骤「{step}」失败：{reason}");
            return false;
        }

        static void Banner(string server)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("╔══════════════════════════════════════════╗");
            Console.WriteLine("║     GnwayController — 发票自动化模式     ║");
            Console.WriteLine("╚══════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine($"  Agent 服务器 : {server}");
            Console.WriteLine($"  列表窗口     : {_winList}");
            Console.WriteLine($"  详情窗口     : {_winDetail}");
            Console.WriteLine(new string('─', 48));
            Console.WriteLine();
        }
    }
}
