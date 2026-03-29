// =============================================================
//  GnwayAgent - 服务端 Agent
//  部署到云联服务器，通过命名管道接收命令，操作同 Session 内的程序
//  依赖：Windows 自带 .NET Framework 4.8 + UIAutomation
//  编译：GitHub Actions 自动编译，无需本地安装编译器
// =============================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace GnwayAgent
{
    class Agent
    {
        const string PIPE_NAME = "GnwayAgentPipe";

        static void Main(string[] args)
        {
            Console.WriteLine("=== GnwayAgent 服务端 ===");
            Console.WriteLine($"进程ID: {System.Diagnostics.Process.GetCurrentProcess().Id}");
            Console.WriteLine($"管道名称: {PIPE_NAME}");
            Console.WriteLine($"主机名称: {Dns.GetHostName()}");
            PrintLocalIPs();
            Console.WriteLine("等待指令中... (Ctrl+C 退出)\n");

            // 循环监听，每次处理完一个客户端连接后继续等待下一个
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PIPE_NAME,
                        PipeDirection.InOut,
                        1,                          // 同时只处理1个连接
                        PipeTransmissionMode.Message,
                        PipeOptions.None
                    );

                    Console.WriteLine("[等待] 客户端连接中...");
                    server.WaitForConnection();
                    Console.WriteLine("[连接] 客户端已连接");

                    var reader = new StreamReader(server, Encoding.UTF8);
                    var writer = new StreamWriter(server, Encoding.UTF8) { AutoFlush = true };

                    string? cmdLine = reader.ReadLine();
                    if (string.IsNullOrEmpty(cmdLine))
                    {
                        writer.WriteLine("ERR:空命令");
                        continue;
                    }

                    Console.WriteLine($"[收到] {cmdLine}");
                    string result = ProcessCommand(cmdLine);
                    writer.WriteLine(result);
                    Console.WriteLine($"[返回] {result}\n");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[错误] {ex.Message}");
                    Thread.Sleep(1000);
                }
            }
        }

        // =====================================================
        //  命令解析
        //  格式: 动作|参数1|参数2|...
        //  例如: click|ERP系统|保存
        //        input|ERP系统|用户名|admin
        //        scroll|ERP系统|数据列表|down
        //        wait|ERP系统|提示|confirm
        //        tree|ERP系统
        //        windows      (列出所有窗口)
        // =====================================================
        static string ProcessCommand(string cmdLine)
        {
            string[] parts = cmdLine.Split('|');
            string action = parts[0].ToLower().Trim();

            try
            {
                // 列出所有顶层窗口（调试用）
                if (action == "windows")
                    return ListAllWindows();

                if (parts.Length < 2)
                    return "ERR:参数不足（格式: 动作|程序名|...）";

                string appTitle = parts[1];

                // 打印控件树（调试用，不需要在服务器上装其他工具）
                if (action == "tree")
                {
                    int depth = parts.Length > 2 ? int.Parse(parts[2]) : 4;
                    var win = FindWindow(appTitle)
                        ?? throw new Exception($"找不到窗口: {appTitle}");
                    return DumpTree(win, depth);
                }

                // 以下动作都需要找到窗口
                var window = FindWindow(appTitle)
                    ?? throw new Exception($"找不到窗口: {appTitle}");

                return action switch
                {
                    "click"       => DoClick(window, parts),
                    "input"       => DoInput(window, parts),
                    "scroll"      => DoScroll(window, parts),
                    "scrollto"    => DoScrollTo(window, parts),
                    "wait"        => DoWait(appTitle, parts),
                    "gettext"     => DoGetText(window, parts),
                    "exists"      => DoExists(window, parts),
                    "select"      => DoSelect(window, parts),
                    "focus"       => DoFocus(window, parts),
                    _             => $"ERR:未知动作 [{action}]"
                };
            }
            catch (Exception ex)
            {
                return $"ERR:{ex.Message}";
            }
        }

        // =====================================================
        //  动作实现
        // =====================================================

        // ── 点击控件 ─────────────────────────────────────────
        // click|程序名|控件名[|父容器名][|索引]
        static string DoClick(AutomationElement window, string[] parts)
        {
            string controlName = parts[2];
            string? parent = parts.Length > 3 ? parts[3] : null;
            int index = parts.Length > 4 ? int.Parse(parts[4]) : 0;

            var ctrl = FindControl(window, controlName, null, parent, index);

            // 先尝试 InvokePattern（按钮）
            if (ctrl.TryGetCurrentPattern(InvokePattern.Pattern, out object? ip))
            {
                ((InvokePattern)ip).Invoke();
                return $"OK:已点击 [{controlName}]";
            }

            // 再尝试 SelectionItemPattern（单选/复选）
            if (ctrl.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object? sp))
            {
                ((SelectionItemPattern)sp).Select();
                return $"OK:已选中 [{controlName}]";
            }

            // 最后用鼠标点击（兜底）
            var rect = ctrl.Current.BoundingRectangle;
            var pt = new System.Drawing.Point(
                (int)(rect.Left + rect.Width / 2),
                (int)(rect.Top + rect.Height / 2)
            );
            System.Windows.Forms.Cursor.Position = pt;
            Thread.Sleep(100);
            SimulateClick(pt);
            return $"OK:已模拟点击 [{controlName}] 坐标({pt.X},{pt.Y})";
        }

        // ── 输入文字 ─────────────────────────────────────────
        // input|程序名|控件名|文字[|是否清空=true]
        static string DoInput(AutomationElement window, string[] parts)
        {
            string controlName = parts[2];
            string text = parts.Length > 3 ? parts[3] : "";
            bool clear = parts.Length > 4 ? parts[4] != "false" : true;

            var ctrl = FindControl(window, controlName);

            if (ctrl.TryGetCurrentPattern(ValuePattern.Pattern, out object? vp))
            {
                if (clear) ((ValuePattern)vp).SetValue("");
                ((ValuePattern)vp).SetValue(text);
                return $"OK:已输入 [{text}] → [{controlName}]";
            }

            // 如果控件不支持 ValuePattern，先 Focus 再键盘输入
            ctrl.SetFocus();
            Thread.Sleep(100);
            if (clear)
            {
                System.Windows.Forms.SendKeys.SendWait("^a"); // Ctrl+A 全选
                System.Windows.Forms.SendKeys.SendWait("{DELETE}");
            }
            System.Windows.Forms.SendKeys.SendWait(text);
            return $"OK:已键入 [{text}] → [{controlName}]";
        }

        // ── 滚动 ─────────────────────────────────────────────
        // scroll|程序名|控件名|方向[|幅度]
        // 方向: up/down/left/right/top/bottom
        // 幅度: small(默认)/large
        static string DoScroll(AutomationElement window, string[] parts)
        {
            string controlName = parts[2];
            string direction = parts.Length > 3 ? parts[3].ToLower() : "down";
            string amount = parts.Length > 4 ? parts[4].ToLower() : "small";

            var ctrl = FindControl(window, controlName);

            if (!ctrl.TryGetCurrentPattern(ScrollPattern.Pattern, out object? sp))
                return "ERR:该控件不支持滚动";

            var scroll = (ScrollPattern)sp;
            var inc = amount == "large" ? ScrollAmount.LargeIncrement
                                        : ScrollAmount.SmallIncrement;
            var dec = amount == "large" ? ScrollAmount.LargeDecrement
                                        : ScrollAmount.SmallDecrement;

            switch (direction)
            {
                case "down":   scroll.Scroll(ScrollAmount.NoAmount, inc); break;
                case "up":     scroll.Scroll(ScrollAmount.NoAmount, dec); break;
                case "right":  scroll.Scroll(inc, ScrollAmount.NoAmount); break;
                case "left":   scroll.Scroll(dec, ScrollAmount.NoAmount); break;
                case "top":    scroll.SetScrollPercent(ScrollPattern.NoScroll, 0); break;
                case "bottom": scroll.SetScrollPercent(ScrollPattern.NoScroll, 100); break;
            }

            double pos = scroll.Current.VerticalScrollPercent;
            return $"OK:已滚动 [{direction}]，当前位置 {pos:F1}%";
        }

        // ── 滚动直到某个控件可见 ──────────────────────────────
        // scrollto|程序名|滚动容器名|目标控件名
        static string DoScrollTo(AutomationElement window, string[] parts)
        {
            string containerName = parts[2];
            string targetName = parts[3];

            var container = FindControl(window, containerName);
            var target = FindControl(window, targetName);

            if (target.TryGetCurrentPattern(ScrollItemPattern.Pattern, out object? sp))
            {
                ((ScrollItemPattern)sp).ScrollIntoView();
                return $"OK:已滚动到 [{targetName}]";
            }

            return "ERR:目标控件不支持 ScrollIntoView";
        }

        // ── 等待弹窗 ─────────────────────────────────────────
        // wait|程序名|弹窗标题[|动作=confirm/cancel/none][|超时秒=15]
        static string DoWait(string appTitle, string[] parts)
        {
            string dialogTitle = parts[2];
            string dialogAction = parts.Length > 3 ? parts[3].ToLower() : "none";
            int timeout = parts.Length > 4 ? int.Parse(parts[4]) : 15;

            Console.WriteLine($"  等待弹窗: [{dialogTitle}] 最多{timeout}秒...");

            var dialog = WaitForWindow(dialogTitle, timeout)
                ?? throw new Exception($"等待弹窗超时: {dialogTitle}");

            string btnName = dialogAction switch
            {
                "confirm" => "确定",
                "cancel"  => "取消",
                "yes"     => "是",
                "no"      => "否",
                "close"   => "关闭",
                _         => ""
            };

            if (!string.IsNullOrEmpty(btnName))
            {
                Thread.Sleep(200); // 弹窗完全渲染后再点
                var btn = FindControl(dialog, btnName);
                ((InvokePattern)btn.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                return $"OK:弹窗 [{dialogTitle}] 已点击 [{btnName}]";
            }

            return $"OK:弹窗 [{dialogTitle}] 已出现";
        }

        // ── 获取控件文字 ─────────────────────────────────────
        // gettext|程序名|控件名
        static string DoGetText(AutomationElement window, string[] parts)
        {
            string controlName = parts[2];
            var ctrl = FindControl(window, controlName);

            if (ctrl.TryGetCurrentPattern(ValuePattern.Pattern, out object? vp))
                return $"OK:{((ValuePattern)vp).Current.Value}";

            if (ctrl.TryGetCurrentPattern(TextPattern.Pattern, out object? tp))
                return $"OK:{((TextPattern)tp).DocumentRange.GetText(-1)}";

            return $"OK:{ctrl.Current.Name}";
        }

        // ── 检查控件是否存在 ─────────────────────────────────
        // exists|程序名|控件名
        static string DoExists(AutomationElement window, string[] parts)
        {
            string controlName = parts[2];
            try
            {
                FindControl(window, controlName);
                return "OK:true";
            }
            catch
            {
                return "OK:false";
            }
        }

        // ── 下拉选择 ─────────────────────────────────────────
        // select|程序名|控件名|选项文字
        static string DoSelect(AutomationElement window, string[] parts)
        {
            string controlName = parts[2];
            string option = parts[3];

            var ctrl = FindControl(window, controlName);

            // 展开下拉
            if (ctrl.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out object? ep))
                ((ExpandCollapsePattern)ep).Expand();

            Thread.Sleep(200);

            // 找到选项并选中
            var item = ctrl.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, option))
                ?? throw new Exception($"下拉选项未找到: {option}");

            ((SelectionItemPattern)item.GetCurrentPattern(
                SelectionItemPattern.Pattern)).Select();

            return $"OK:已选择 [{option}] in [{controlName}]";
        }

        // ── 设置焦点 ─────────────────────────────────────────
        // focus|程序名|控件名
        static string DoFocus(AutomationElement window, string[] parts)
        {
            string controlName = parts[2];
            var ctrl = FindControl(window, controlName);
            ctrl.SetFocus();
            return $"OK:已聚焦 [{controlName}]";
        }

        // =====================================================
        //  工具函数
        // =====================================================

        // 打印本机所有 IPv4 地址
        static void PrintLocalIPs()
        {
            Console.WriteLine("本机 IP 地址（Controller 连接时使用）:");
            bool found = false;
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (UnicastIPAddressInformation addr in
                    nic.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    Console.WriteLine($"  [{nic.Name}] {addr.Address}");
                    found = true;
                }
            }
            if (!found)
                Console.WriteLine("  （未检测到局域网网卡，请确认网络连接）");
        }

        // 查找顶层窗口（模糊匹配标题）
        static AutomationElement? FindWindow(string titlePattern)
        {
            var all = AutomationElement.RootElement.FindAll(
                TreeScope.Children, Condition.TrueCondition);

            foreach (AutomationElement e in all)
                if (e.Current.Name.Contains(titlePattern)
                    && e.Current.IsOffscreen == false)
                    return e;

            return null;
        }

        // 等待指定标题的窗口出现
        static AutomationElement? WaitForWindow(string title, int timeoutSec)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < timeoutSec)
            {
                var w = FindWindow(title);
                if (w != null) return w;
                Thread.Sleep(300);
            }
            return null;
        }

        // 精确查找控件
        // controlName: 控件名称（Name 属性）或 AutomationId
        // controlType: 控件类型（null = 不限）
        // parentName:  在哪个父容器下查找（解决工具栏同名问题）
        // index:       同名控件的第几个（从0开始）
        static AutomationElement FindControl(
            AutomationElement root,
            string controlName,
            ControlType? controlType = null,
            string? parentName = null,
            int index = 0)
        {
            AutomationElement searchRoot = root;

            // 如果指定了父容器，先找父容器
            if (!string.IsNullOrEmpty(parentName))
            {
                var parentCond = new PropertyCondition(
                    AutomationElement.NameProperty, parentName);
                searchRoot = root.FindFirst(TreeScope.Descendants, parentCond)
                    ?? throw new Exception($"父容器未找到: {parentName}");
            }

            // 组合查找条件
            Condition nameCond = new OrCondition(
                new PropertyCondition(AutomationElement.NameProperty, controlName),
                new PropertyCondition(AutomationElement.AutomationIdProperty, controlName)
            );

            Condition finalCond = controlType != null
                ? (Condition)new AndCondition(nameCond,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, controlType))
                : nameCond;

            var results = searchRoot.FindAll(TreeScope.Descendants, finalCond);

            if (results.Count == 0)
                throw new Exception($"控件未找到: [{controlName}]" +
                    (parentName != null ? $" (在 [{parentName}] 内)" : ""));

            if (index >= results.Count)
                throw new Exception($"索引越界: [{controlName}] 共{results.Count}个，请求第{index}个");

            return results[index];
        }

        // 列出所有顶层窗口
        static string ListAllWindows()
        {
            var all = AutomationElement.RootElement.FindAll(
                TreeScope.Children, Condition.TrueCondition);

            var sb = new StringBuilder();
            sb.AppendLine("OK:当前所有顶层窗口:");
            foreach (AutomationElement e in all)
            {
                string name = e.Current.Name;
                string cls = e.Current.ClassName;
                if (!string.IsNullOrEmpty(name))
                    sb.AppendLine($"  [{cls}] {name}");
            }
            return sb.ToString().TrimEnd();
        }

        // 打印控件树（调试用）
        static string DumpTree(AutomationElement root, int maxDepth = 4)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"OK:控件树 [{root.Current.Name}]");
            DumpNode(root, 0, maxDepth, sb);
            return sb.ToString().TrimEnd();
        }

        static void DumpNode(AutomationElement el, int depth, int maxDepth, StringBuilder sb)
        {
            string indent = new string(' ', depth * 2);
            string type = el.Current.ControlType.ProgrammaticName
                            .Replace("ControlType.", "");
            string name = el.Current.Name;
            string aid = el.Current.AutomationId;

            sb.AppendLine($"{indent}[{type}] '{name}'" +
                (string.IsNullOrEmpty(aid) ? "" : $" (id={aid})"));

            if (depth >= maxDepth) return;

            var walker = TreeWalker.ControlViewWalker;
            var child = walker.GetFirstChild(el);
            while (child != null)
            {
                DumpNode(child, depth + 1, maxDepth, sb);
                child = walker.GetNextSibling(child);
            }
        }

        // 模拟鼠标左键点击（兜底方案）
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, int dwExtraInfo);
        const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        const uint MOUSEEVENTF_LEFTUP = 0x0004;

        static void SimulateClick(System.Drawing.Point pt)
        {
            mouse_event(MOUSEEVENTF_LEFTDOWN, pt.X, pt.Y, 0, 0);
            Thread.Sleep(50);
            mouse_event(MOUSEEVENTF_LEFTUP, pt.X, pt.Y, 0, 0);
        }
    }
}
