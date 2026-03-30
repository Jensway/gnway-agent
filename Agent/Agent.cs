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
        static bool UseUiaEngine = false; // 双发引擎切换开关

        static void Main(string[] args)
        {
            Console.WriteLine("=== GnwayAgent 服务端 ===");
            Console.WriteLine($"进程ID: {System.Diagnostics.Process.GetCurrentProcess().Id}");
            Console.WriteLine($"管道名称: {PIPE_NAME}");
            Console.WriteLine($"主机名称: {Dns.GetHostName()}");
            // 将网络管道监听放入独立后台线程，防止阻塞本地控制台输入
            var pipeThread = new Thread(() =>
            {
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

                        // 不再打印阻塞提示，保证本地控制台干净
                        server.WaitForConnection();
                        Console.WriteLine("\n[网络连接] Controller 客服端已从外部接入管道！");

                        var reader = new StreamReader(server, Encoding.UTF8);
                        var writer = new StreamWriter(server, Encoding.UTF8) { AutoFlush = true };

                        string? cmdLine = reader.ReadLine();
                        if (string.IsNullOrEmpty(cmdLine))
                        {
                            writer.WriteLine("ERR:空命令");
                            continue;
                        }

                        Console.WriteLine($"[收到网络指令] {cmdLine}");
                        string? result = ProcessCommand(cmdLine, writer);
                        if (result != null)
                        {
                            writer.WriteLine(result);
                            Console.WriteLine($"[网络返回] {result}");
                        }
                        else
                        {
                            Console.WriteLine($"[网络返回] <流式输出完毕>");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[网络管道错误] {ex.Message}");
                        Thread.Sleep(500);
                    }
                }
            });
            pipeThread.IsBackground = true;
            pipeThread.Start();

            // 首次启动时打印懒人交互菜单
            PrintMenu();

            // 主线程：本地控制台调试入口
            while (true)
            {
                string? input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input)) continue;

                if (input.Trim().ToLower() == "m" || input.Trim().ToLower() == "menu")
                {
                    PrintMenu();
                    continue;
                }

                // 如果用户直接输入了一个纯数字，我们认为是窗口编号选择
                if (int.TryParse(input.Trim(), out int menuIndex))
                {
                    var windows = GetValidWindows();
                    if (menuIndex >= 1 && menuIndex <= windows.Count)
                    {
                        string target = windows[menuIndex - 1];
                        Console.WriteLine($"\n========== [拉取全部控件树] {target} ==========");
                        
                        using var ms = new MemoryStream();
                        using var sw = new StreamWriter(ms, Encoding.UTF8) { AutoFlush = true };
                        ProcessCommand($"listcontrols|{target}", sw);
                        
                        ms.Position = 0;
                        using var sr = new StreamReader(ms, Encoding.UTF8);
                        string fullOutput = sr.ReadToEnd();
                        
                        Console.WriteLine(fullOutput);
                        
                        // 写入文本文件以便用户自由复制
                        try
                        {
                            File.WriteAllText("agent_dump.txt", fullOutput, Encoding.UTF8);
                            Console.WriteLine("\n[⭐ 重要提示] 上述完整的控件树已自动保存到本程序同目录下的 agent_dump.txt 文件中！请直接打开它全选复制内容！");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"\n[提示] 写入 txt 失败: {ex.Message}");
                        }

                        Console.WriteLine("====================================================\n输入 'm' 刷新窗口列表，或按 's' 切换引擎。");
                    }
                    else
                    {
                        Console.WriteLine(">>> 编号无效，请对照上方列表重新输入，或按 'm' 重新唤出列表。");
                    }
                    continue;
                }

                if (input.Trim().ToLower() == "s")
                {
                    UseUiaEngine = !UseUiaEngine;
                    Console.WriteLine($"\n[引擎已切换] 当前扫描引擎: {(UseUiaEngine ? "UIA 全量深度扫描引擎 (防漏/最全面但稍慢)" : "Win32 原生平铺引擎 (极速防卡死)")}");
                    PrintMenu();
                    continue;
                }

                // 其他手敲的原始指令
                Console.WriteLine($"\n--- [本地手动调试] 开始执行 {input} ---");
                try
                {
                    using var ms = new MemoryStream();
                    using var sw = new StreamWriter(ms, Encoding.UTF8) { AutoFlush = true };
                    string? result = ProcessCommand(input, sw);
                    
                    if (result != null)
                    {
                        Console.WriteLine(result);
                    }
                    else 
                    {
                        ms.Position = 0;
                        using var sr = new StreamReader(ms, Encoding.UTF8);
                        string output = sr.ReadToEnd();
                        Console.WriteLine(output);
                        File.WriteAllText("agent_dump.txt", output, Encoding.UTF8);
                        Console.WriteLine("\n[⭐ 提示] 结果已导出至 agent_dump.txt 方便复制！");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[本地错误] {ex.Message}");
                }
                Console.WriteLine("------------------------------------\n");
            }
        }

        static System.Collections.Generic.List<string> GetValidWindows()
        {
            var all = AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition);
            var list = new System.Collections.Generic.List<string>();
            foreach (AutomationElement e in all)
            {
                if (!string.IsNullOrEmpty(e.Current.Name))
                {
                    list.Add(e.Current.Name);
                }
            }
            return list;
        }

        static void PrintMenu()
        {
            PrintLocalIPs();
            Console.WriteLine("\n==== [Agent 本地懒人调试菜单] ====");
            var list = GetValidWindows();
            for (int i = 0; i < list.Count; i++)
            {
                Console.WriteLine($" [{i + 1}] {list[i]}");
            }
            Console.WriteLine("==================================");
            Console.WriteLine("请直接输入上方窗口前对应的【数字】（如 1 或 2）并按回车。");
            Console.WriteLine("Agent 将直接打印该窗口的所有底层 Win32 控件结构。");
            Console.WriteLine($"若窗口未显示，请按 'm' 键刷新列表。目前引擎: [{(UseUiaEngine ? "UIA全量" : "Win32极速")}] (按 's' 切换)");
            Console.WriteLine("网络客户端 NamedPipe 也仍在后台静默监听，随时可连接。");
            Console.Write("\n请输入数字或指令 (s切换引擎/m刷新): ");
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
        static string? ProcessCommand(string cmdLine, TextWriter writer)
        {
            string[] parts = cmdLine.Split('|');
            string action = parts[0].ToLower().Trim();

            try
            {
                // 列出所有顶层窗口（调试用）
                if (action == "windows")
                    return ListAllWindows();

                // snapshot：机器可读的窗口列表，用于状态感知
                if (action == "snapshot")
                    return DoSnapshot();

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

                // windowexists：不需要窗口存在，专门检查窗口是否存在
                if (action == "windowexists")
                    return FindWindow(appTitle) != null ? "OK:true" : "OK:false";

                // 以下动作都需要找到窗口
                var window = FindWindow(appTitle)
                    ?? throw new Exception($"找不到窗口: {appTitle}");

                if (action == "listcontrols")
                {
                    DoListControlsStream(window, parts, writer);
                    return null;
                }

                return action switch
                {
                    "click"        => DoClick(window, parts),
                    "input"        => DoInput(window, parts),
                    "scroll"       => DoScroll(window, parts),
                    "scrollto"     => DoScrollTo(window, parts),
                    "wait"         => DoWait(appTitle, parts),
                    "gettext"      => DoGetText(window, parts),
                    "exists"       => DoExists(window, parts),
                    "select"       => DoSelect(window, parts),
                    "focus"        => DoFocus(window, parts),
                    "isenabled"    => DoIsEnabled(window, parts),
                    "popupinfo"    => DoPopupInfo(window, parts),
                    "gridrows"     => DoGridRows(window, parts),
                    "gridselect"   => DoGridSelect(window, parts),
                    _              => $"ERR:未知动作 [{action}]"
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
            var searchRoots = new System.Collections.Generic.List<AutomationElement>();
            
            // 如果指定了父容器，先尝试用 Win32 HWND 瞬间遍历查父容器
            if (!string.IsNullOrEmpty(parentName))
            {
                var parentHwnds = new System.Collections.Generic.List<IntPtr>();
                try
                {
                    EnumChildWindows((IntPtr)root.Current.NativeWindowHandle, (hWnd, lParam) =>
                    {
                        parentHwnds.Add(hWnd);
                        return true;
                    }, IntPtr.Zero);
                }
                catch { }

                foreach (var ph in parentHwnds)
                {
                    try
                    {
                        var pe = AutomationElement.FromHandle(ph);
                        if (pe.Current.Name == parentName)
                        {
                            searchRoots.Add(pe);
                        }
                    }
                    catch { }
                }

                // 极端情况：父容器无句柄 (WPF/虚拟)，启用 UIA Fallback
                if (searchRoots.Count == 0)
                {
                    var parentCond = new PropertyCondition(AutomationElement.NameProperty, parentName);
                    var p = root.FindFirst(TreeScope.Descendants, parentCond);
                    if (p != null) searchRoots.Add(p);
                    else throw new Exception($"父容器未找到: {parentName}");
                }
            }
            else
            {
                searchRoots.Add(root);
            }

            var matches = new System.Collections.Generic.List<AutomationElement>();

            // 核心：在确定的 searchRoots 范围内使用高速 Win32 HWND 枚举找目标
            foreach (var sRoot in searchRoots)
            {
                var hwnds = new System.Collections.Generic.List<IntPtr>();
                try
                {
                    EnumChildWindows((IntPtr)sRoot.Current.NativeWindowHandle, (hWnd, lParam) =>
                    {
                        hwnds.Add(hWnd);
                        return true;
                    }, IntPtr.Zero);
                }
                catch { }

                foreach (var hWnd in hwnds)
                {
                    try
                    {
                        var sbText = new StringBuilder(256);
                        GetWindowText(hWnd, sbText, sbText.Capacity);
                        string win32Name = sbText.ToString();

                        // 高速原生过滤：如果原生的 Win32 文字正好等同于寻找的名字，直接命中！（完全绕过所有其余句柄的 UIA 转化）
                        if (!string.IsNullOrEmpty(win32Name) && win32Name == controlName)
                        {
                            var el = AutomationElement.FromHandle(hWnd);
                            if (controlType == null || el.Current.ControlType == controlType)
                            {
                                matches.Add(el);
                                continue;
                            }
                        }

                        // 如果原生名字查不到（可能是虚拟绘制的），再降级使用 UIA 匹配 (仍需耗费较多时间，但大部分常规按钮会被上方高速通道截获)
                        var uiaEl = AutomationElement.FromHandle(hWnd);
                        bool nameMatch = (uiaEl.Current.Name == controlName || uiaEl.Current.AutomationId == controlName);
                        bool typeMatch = controlType == null || uiaEl.Current.ControlType == controlType;
                        
                        if (nameMatch && typeMatch)
                        {
                            if (!matches.Contains(uiaEl)) matches.Add(uiaEl);
                        }
                    }
                    catch { }
                }

                if (matches.Count > index) return matches[index];
            }

            // 万一真的是纯虚拟 UI，Win32 枚不到，最终保底使用 UIA 的 Descendants (可能会卡)
            Condition nameCond = new OrCondition(
                new PropertyCondition(AutomationElement.NameProperty, controlName),
                new PropertyCondition(AutomationElement.AutomationIdProperty, controlName)
            );

            Condition finalCond = controlType != null
                ? (Condition)new AndCondition(nameCond,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, controlType))
                : nameCond;

            foreach (var sRoot in searchRoots)
            {
                try {
                    var results = sRoot.FindAll(TreeScope.Descendants, finalCond);
                    foreach (AutomationElement r in results) matches.Add(r);
                } catch { }
            }

            if (matches.Count == 0)
                throw new Exception($"控件未找到: [{controlName}]" + (parentName != null ? $" (在 [{parentName}] 内)" : ""));

            if (index >= matches.Count)
                throw new Exception($"索引越界: [{controlName}] 共{matches.Count}个，请求第{index}个");

            return matches[index];
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

            var walker = TreeWalker.RawViewWalker;
            var child = walker.GetFirstChild(el);
            while (child != null)
            {
                DumpNode(child, depth + 1, maxDepth, sb);
                child = walker.GetNextSibling(child);
            }
        }

        // ── 检查控件是否启用 ──────────────────────────────
        // isenabled|程序名|控件名
        static string DoIsEnabled(AutomationElement window, string[] parts)
        {
            string controlName = parts[2];
            var ctrl = FindControl(window, controlName);
            return ctrl.Current.IsEnabled ? "OK:true" : "OK:false";
        }

        // ── 读取弹窗完整信息（标题+正文+按钮）──────────────
        // popupinfo|程序名  →  OK:title=xxx|body=xxx|buttons=确定,取消
        static string DoPopupInfo(AutomationElement window, string[] parts)
        {
            var texts   = new System.Collections.Generic.List<string>();
            var buttons = new System.Collections.Generic.List<string>();
            CollectPopupContent(window, texts, buttons);

            // 去重并过滤标题自身（标题已在 title= 字段）
            string title = window.Current.Name;
            texts.Remove(title);

            string body = string.Join(" ", texts.FindAll(t => !string.IsNullOrWhiteSpace(t)));
            string btns = string.Join(",", buttons.FindAll(b => !string.IsNullOrWhiteSpace(b)));
            return $"OK:title={title}|body={body}|buttons={btns}";
        }

        static void CollectPopupContent(
            AutomationElement el,
            System.Collections.Generic.List<string> texts,
            System.Collections.Generic.List<string> buttons)
        {
            var type = el.Current.ControlType;
            string name = el.Current.Name ?? "";

            if ((type == ControlType.Text || type == ControlType.Custom)
                && !string.IsNullOrWhiteSpace(name))
                texts.Add(name.Trim());

            if (type == ControlType.Button && !string.IsNullOrWhiteSpace(name))
                buttons.Add(name.Trim());

            var walker = TreeWalker.RawViewWalker;
            var child  = walker.GetFirstChild(el);
            while (child != null)
            {
                CollectPopupContent(child, texts, buttons);
                child = walker.GetNextSibling(child);
            }
        }

        // ── 快照：返回所有可见窗口名（|||分隔）──────────────
        // snapshot  →  OK:窗口A|||窗口B|||窗口C
        static string DoSnapshot()
        {
            var all = AutomationElement.RootElement.FindAll(
                TreeScope.Children, Condition.TrueCondition);

            var names = new System.Collections.Generic.List<string>();
            foreach (AutomationElement e in all)
            {
                string n = e.Current.Name ?? "";
                if (!string.IsNullOrEmpty(n) && !e.Current.IsOffscreen)
                    names.Add(n);
            }
            return "OK:" + string.Join("|||", names);
        }

        // 模拟鼠标左键点击（兜底方案）
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, int dwExtraInfo);
        const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        const uint MOUSEEVENTF_LEFTUP   = 0x0004;

        static void SimulateClick(System.Drawing.Point pt)
        {
            mouse_event(MOUSEEVENTF_LEFTDOWN, pt.X, pt.Y, 0, 0);
            Thread.Sleep(50);
            mouse_event(MOUSEEVENTF_LEFTUP, pt.X, pt.Y, 0, 0);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        static extern bool EnumChildWindows(IntPtr hwndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        static extern bool IsWindowEnabled(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll", ExactSpelling = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        public static extern IntPtr GetParent(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        static extern bool IsWindowVisible(IntPtr hWnd);

        static void DoListControlsStream(AutomationElement window, string[] parts, TextWriter writer)
        {
            writer.WriteLine("OK:"); // 流式标识首行
            try
            {
                if (UseUiaEngine)
                {
                    WalkUiaTree(window, writer);
                    return;
                }

                // == Win32 极速平铺引擎 ==
                var hwnds = new System.Collections.Generic.List<IntPtr>();
                IntPtr rootNative = (IntPtr)window.Current.NativeWindowHandle;

                EnumChildWindows(rootNative, (hWnd, lParam) =>
                {
                    hwnds.Add(hWnd);
                    return true;
                }, IntPtr.Zero);

                int GetDepth(IntPtr h)
                {
                    int d = 0;
                    IntPtr p = GetParent(h);
                    while (p != IntPtr.Zero && p != rootNative && d < 20)
                    {
                        d++;
                        p = GetParent(p);
                    }
                    return d + 1;
                }

                // 完全放弃 UIA 转换，直接使用 Win32 API 瞬间提纯全屏文本与类型 (极速 0ms，杜绝任何假死报错)
                foreach (var hWnd in hwnds)
                {
                    try
                    {
                        var sbClass = new StringBuilder(256);
                        GetClassName(hWnd, sbClass, sbClass.Capacity);
                        string cls = sbClass.ToString();

                        var sbText = new StringBuilder(256);
                        GetWindowText(hWnd, sbText, sbText.Capacity);
                        string name = sbText.ToString();

                        bool enabled = IsWindowEnabled(hWnd);
                        bool visible = IsWindowVisible(hWnd);

                        string type = "Custom";
                        string cl = cls.ToLower();
                        if (cl.Contains("button") || cl.Contains("btn")) type = "Button";
                        else if (cl.Contains("checkbox") || cl.Contains("check")) type = "CheckBox";
                        else if (cl.Contains("radio")) type = "RadioButton";
                        else if (cl.Contains("edit")) type = "Edit";
                        else if (cl.Contains("combo")) type = "ComboBox";
                        else if (cl.Contains("listview") || cl.Contains("listbox") || cl.Contains("list")) type = "List";
                        else if (cl.Contains("grid") || cl.Contains("table") || cl.Contains("stringgrid")) type = "DataGrid";
                        else if (cl.Contains("tree")) type = "Tree";
                        else if (cl.Contains("scrollbar")) type = "ScrollBar";
                        else if (cl.Contains("tab")) type = "TabItem";
                        else if (cl.Contains("menu")) type = "MenuItem";
                        else if (cl.Contains("static") || cl.Contains("label") || cl.Contains("text")) type = "Text";
                        else if (cl.Contains("slider") || cl.Contains("trackbar")) type = "Slider";
                        else if (cl.Contains("spin") || cl.Contains("updown")) type = "Spinner";

                        int depth = GetDepth(hWnd);
                        string pad = new string('-', depth * 2) + " ";

                        string displayName = string.IsNullOrWhiteSpace(name) ? "[无文字]" : name;
                        displayName = $"{pad}{displayName} [类:{cls}]";
                        if (!visible) displayName += " {隐}";

                        writer.WriteLine($"{type}|{displayName}|{(enabled ? "1" : "0")}");
                    }
                    catch { } // 忽略安全异常
                }
            }
            catch (Exception ex)
            {
                writer.WriteLine($"ERR:获取控件树异常: {ex.Message}");
            }
        }

        static void WalkUiaTree(AutomationElement el, TextWriter writer, int depth = 1)
        {
            var walker = TreeWalker.ControlViewWalker;
            var child = walker.GetFirstChild(el);
            while (child != null)
            {
                try
                {
                    string type = child.Current.ControlType.ProgrammaticName.Replace("ControlType.", "");
                    string name = child.Current.Name ?? "";
                    string aid  = child.Current.AutomationId ?? "";
                    bool enabled = child.Current.IsEnabled;
                    bool visible = !child.Current.IsOffscreen;

                    string display = string.IsNullOrWhiteSpace(name) ? "[无名]" : name;
                    if (!string.IsNullOrWhiteSpace(aid) && aid != name)
                        display += $" (ID:{aid})";
                    if (!visible)
                        display += " {不可见}";

                    string pad = new string('-', depth * 2) + " ";
                    writer.WriteLine($"{type}|{pad}{display}|{(enabled ? "1" : "0")}");
                    
                    // 递归步进遍历，生成树状分支
                    WalkUiaTree(child, writer, depth + 1);
                }
                catch { } // 忽略中途失效的组件

                child = walker.GetNextSibling(child);
            }
        }



        // ── gridrows|窗口名|控件名[|最大行数] ─────────────────
        // 返回: OK: 后每行是制表符分隔的单元格文字
        static string DoGridRows(AutomationElement window, string[] parts)
        {
            string controlName = parts[2];
            int maxRows = parts.Length > 3 && int.TryParse(parts[3], out int m) ? m : 500;

            var grid   = FindControl(window, controlName);
            var sb     = new System.Text.StringBuilder("OK:");
            var walker = TreeWalker.ControlViewWalker;
            var child  = walker.GetFirstChild(grid);
            int rowIdx = 0;

            while (child != null && rowIdx < maxRows)
            {
                var ct = child.Current.ControlType;
                if (ct == ControlType.DataItem ||
                    ct == ControlType.ListItem  ||
                    ct == ControlType.TreeItem)
                {
                    var cols  = new System.Collections.Generic.List<string>();
                    var cell  = walker.GetFirstChild(child);
                    while (cell != null)
                    {
                        string txt = GetCellText(cell);
                        cols.Add(txt);
                        cell = walker.GetNextSibling(cell);
                    }
                    // 若没有子单元格，直接取行 Name
                    if (cols.Count == 0)
                        cols.Add(child.Current.Name ?? "");
                    sb.AppendLine(string.Join("\t", cols));
                    rowIdx++;
                }
                child = walker.GetNextSibling(child);
            }
            return sb.ToString().TrimEnd();
        }

        static string GetCellText(AutomationElement el)
        {
            if (el.TryGetCurrentPattern(ValuePattern.Pattern, out object? vp))
                return ((ValuePattern)vp).Current.Value ?? "";
            return el.Current.Name ?? "";
        }

        // ── gridselect|窗口名|控件名|行索引 ───────────────────
        // 按 0-based 行索引选中该行
        static string DoGridSelect(AutomationElement window, string[] parts)
        {
            string controlName = parts[2];
            if (!int.TryParse(parts[3], out int rowIndex))
                return "ERR:行索引必须是整数";

            var grid   = FindControl(window, controlName);
            var walker = TreeWalker.ControlViewWalker;
            var child  = walker.GetFirstChild(grid);
            int current = 0, total = 0;

            while (child != null)
            {
                var ct = child.Current.ControlType;
                if (ct == ControlType.DataItem ||
                    ct == ControlType.ListItem  ||
                    ct == ControlType.TreeItem)
                {
                    if (current == rowIndex)
                    {
                        // 优先 SelectionItemPattern
                        if (child.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object? sp))
                            ((SelectionItemPattern)sp).Select();
                        else
                        {
                            child.SetFocus();
                            var rect = child.Current.BoundingRectangle;
                            var pt   = new System.Drawing.Point(
                                (int)(rect.Left + 5),
                                (int)(rect.Top  + rect.Height / 2));
                            System.Windows.Forms.Cursor.Position = pt;
                            Thread.Sleep(60);
                            SimulateClick(pt);
                        }
                        return $"OK:已选第{rowIndex}行";
                    }
                    current++;
                    total++;
                }
                child = walker.GetNextSibling(child);
            }
            return $"ERR:行索引{rowIndex}超出范围（共{total}行）";
        }
    }
}
