// =============================================================
//  GnwayAgent - 服务端 Agent (Native Win32 Edition)
//  部署到云联服务器，通过命名管道接收命令，操作同 Session 内的程序
//  基于 EnumChildWindows 实现极速无感知、精准透视的 VB6 提取
// =============================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace GnwayAgent
{
    class Agent
    {
        static void Main(string[] args)
        {
            int port = 19090;
            if (args.Length > 0 && int.TryParse(args[0], out int p)) port = p;

            Console.WriteLine("=== GnwayAgent 服务端 (Native Win32 极速版) ===");
            Console.WriteLine($"进程ID: {System.Diagnostics.Process.GetCurrentProcess().Id}");
            Console.WriteLine($"TCP 端口: {port} (可附加参数启动修改，如: Agent.exe 9090)");
            Console.WriteLine($"主机名称: {Dns.GetHostName()}");

            var tcpThread = new Thread(() =>
            {
                var listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
                while (true)
                {
                    try
                    {
                        using var client = listener.AcceptTcpClient();
                        using var stream = client.GetStream();
                        var remoteEP = client.Client.RemoteEndPoint?.ToString() ?? "未知IP";
                        Console.WriteLine($"\n[TCP连接] Controller ({remoteEP}) 已接入！");

                        var reader = new StreamReader(stream, Encoding.UTF8);
                        var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                        string? cmdLine = reader.ReadLine();
                        if (string.IsNullOrEmpty(cmdLine)) { writer.WriteLine("ERR:空命令"); continue; }

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
                        Console.WriteLine($"[TCP错误] {ex.Message}");
                        Thread.Sleep(500);
                    }
                }
            });
            tcpThread.IsBackground = true;
            tcpThread.Start();

            PrintMenu();

            while (true)
            {
                string? input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input)) continue;

                if (input.Trim().ToLower() == "m" || input.Trim().ToLower() == "menu")
                {
                    PrintMenu(); continue;
                }

                if (int.TryParse(input.Trim(), out int menuIndex))
                {
                    var windows = GetValidWindows();
                    if (menuIndex >= 1 && menuIndex <= windows.Count)
                    {
                        string target = windows[menuIndex - 1].Split('|')[0]; // only get handle
                        if (target == "") target = windows[menuIndex - 1]; // fallback

                        Console.WriteLine($"\n========== [拉取全部控件树] {target} ==========");
                        using var ms = new MemoryStream();
                        using var sw = new StreamWriter(ms, Encoding.UTF8) { AutoFlush = true };
                        ProcessCommand($"listcontrols|{target}", sw);
                        ms.Position = 0;
                        using var sr = new StreamReader(ms, Encoding.UTF8);
                        string fullOutput = sr.ReadToEnd();
                        Console.WriteLine(fullOutput);
                        try
                        {
                            File.WriteAllText("agent_dump.txt", fullOutput, Encoding.UTF8);
                            Console.WriteLine("\n[⭐ 提示] 控件树已保存到 agent_dump.txt");
                        } catch { }
                        Console.WriteLine("====================================================\n输入 'm' 刷新");
                    }
                    else Console.WriteLine(">>> 编号无效");
                    continue;
                }

                try
                {
                    using var ms = new MemoryStream();
                    using var sw = new StreamWriter(ms, Encoding.UTF8) { AutoFlush = true };
                    string? result = ProcessCommand(input, sw);
                    if (result != null) Console.WriteLine(result);
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
                catch (Exception ex) { Console.WriteLine($"[本地错误] {ex.Message}"); }
            }
        }

        static List<string> GetValidWindows()
        {
            var list = new List<string>();
            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd) || GetWindowTextLength(hWnd) == 0) return true;
                string title = GetWindowTextStr(hWnd);
                if (!string.IsNullOrWhiteSpace(title)) list.Add(title);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        static void PrintMenu()
        {
            Console.WriteLine("\n==== [Agent 本地懒人调试菜单] ====");
            var list = GetValidWindows();
            for (int i = 0; i < list.Count; i++) Console.WriteLine($" [{i + 1}] {list[i]}");
            Console.WriteLine("==================================");
            Console.Write("\n请输入数字或指令 (如: m): ");
        }

        static string? ProcessCommand(string cmdLine, TextWriter writer)
        {
            string[] parts = cmdLine.Split('|');
            string action = parts[0].ToLower().Trim();

            try
            {
                if (action == "windows") return "OK:\n" + string.Join("\n", GetValidWindows());
                
                if (action == "snapshot")
                {
                    var wins = GetValidWindows();
                    return "OK:" + string.Join("|||", wins);
                }

                if (parts.Length < 2) return "ERR:参数不足（格式: 动作|程序名|...）";
                string appTitle = parts[1];

                if (action == "windowexists") return FindWindowByTitle(appTitle) != IntPtr.Zero ? "OK:true" : "OK:false";

                IntPtr window = FindWindowByTitle(appTitle);
                if (window == IntPtr.Zero) throw new Exception($"找不到窗口: {appTitle}");

                if (action == "listcontrols" || action == "tree")
                {
                    DoListControlsStream(window, parts, writer);
                    return null;
                }

                return action switch
                {
                    "click"      => DoClick(window, parts),
                    "input"      => DoInput(window, parts),
                    "gettext"    => DoGetText(window, parts),
                    "select"     => DoSelect(window, parts),
                    "exists"     => DoExists(window, parts),
                    "isenabled"  => DoIsEnabled(window, parts),
                    "popupinfo"  => DoPopupInfo(window, parts),
                    "gridrows"   => DoGridRows(window, parts),
                    "gridselect" => DoGridSelect(window, parts),
                    "focus"      => DoFocus(window, parts),
                    "wait"       => DoWait(appTitle, parts),
                    _            => $"ERR:未知动作 [{action}]"
                };
            }
            catch (Exception ex) { return $"ERR:{ex.Message}"; }
        }

        // =====================================================
        //  Win32 极速操控逻辑
        // =====================================================

        static IntPtr FindWindowByTitle(string titlePattern)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd)) return true;
                string title = GetWindowTextStr(hWnd);
                if (title.Contains(titlePattern)) { found = hWnd; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        static IntPtr FindControl(IntPtr window, string controlName)
        {
            var match = Regex.Match(controlName, @"<([A-Za-z0-9_]+?)(\d+)>");
            if (match.Success)
            {
                string targetClass = match.Groups[1].Value.ToLower();
                int targetIndex = int.Parse(match.Groups[2].Value);
                int currentTypeCount = 0;
                IntPtr targetElement = IntPtr.Zero;

                bool WalkForMagicName(IntPtr currentRoot)
                {
                    IntPtr child = GetWindow(currentRoot, GW_CHILD);
                    while (child != IntPtr.Zero)
                    {
                        string cls = GetClassNameStr(child).ToLower();
                        if (cls == targetClass)
                        {
                            currentTypeCount++;
                            if (currentTypeCount == targetIndex)
                            {
                                targetElement = child;
                                return true;
                            }
                        }
                        if (WalkForMagicName(child)) return true;
                        child = GetWindow(child, GW_HWNDNEXT);
                    }
                    return false;
                }
                
                if (WalkForMagicName(window)) return targetElement;
                throw new Exception($"未能打捞出绝对坐标匹配的控件: {controlName}");
            }

            // Fallback: 模糊匹配树中所有的可用名称
            IntPtr fallbackMatch = IntPtr.Zero;
            bool FallbackWalk(IntPtr root)
            {
                IntPtr child = GetWindow(root, GW_CHILD);
                while (child != IntPtr.Zero)
                {
                    var text = GetWindowTextStr(child);
                    if (!string.IsNullOrWhiteSpace(text) && text == controlName)
                    {
                        fallbackMatch = child; return true;
                    }
                    if (FallbackWalk(child)) return true;
                    child = GetWindow(child, GW_HWNDNEXT);
                }
                return false;
            }
            if (FallbackWalk(window)) return fallbackMatch;

            throw new Exception($"未发现可用控件: [{controlName}]");
        }

        static string DoClick(IntPtr window, string[] parts)
        {
            string controlName = parts[2];
            IntPtr ctrl = FindControl(window, controlName);
            
            // 兜底模拟坐标点击
            GetWindowRect(ctrl, out RECT rect);
            System.Drawing.Point pt = new System.Drawing.Point(
                rect.Left + (rect.Right - rect.Left) / 2, 
                rect.Top + (rect.Bottom - rect.Top) / 2);
            System.Windows.Forms.Cursor.Position = pt;
            Thread.Sleep(50);
            mouse_event(MOUSEEVENTF_LEFTDOWN, pt.X, pt.Y, 0, 0);
            Thread.Sleep(50);
            mouse_event(MOUSEEVENTF_LEFTUP, pt.X, pt.Y, 0, 0);
            
            // 发送 BM_CLICK
            SendMessage(ctrl, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
            
            return $"OK:已原生点击 [{controlName}]";
        }

        static string DoInput(IntPtr window, string[] parts)
        {
            string controlName = parts[2];
            string text = parts.Length > 3 ? parts[3] : "";
            IntPtr ctrl = FindControl(window, controlName);

            // 直接 Win32 霸王硬上弓修改
            SendMessage(ctrl, WM_SETTEXT, IntPtr.Zero, text);
            
            // 兜底 Focus + 键盘输入 (对部分 VB6 拦截修改有用)
            SetFocus(ctrl);
            Thread.Sleep(100);
            System.Windows.Forms.SendKeys.SendWait("^a");
            System.Windows.Forms.SendKeys.SendWait("{DELETE}");
            System.Windows.Forms.SendKeys.SendWait(text);
            
            return $"OK:已覆写文本 [{text}] -> [{controlName}]";
        }

        static string DoGetText(IntPtr window, string[] parts)
        {
            IntPtr ctrl = FindControl(window, parts[2]);
            return $"OK:{GetWindowTextStr(ctrl)}";
        }

        static string DoSelect(IntPtr window, string[] parts)
        {
            IntPtr ctrl = FindControl(window, parts[2]);
            string option = parts[3];
            
            int index = (int)SendMessageString(ctrl, CB_FINDSTRINGEXACT, (IntPtr)(-1), option);
            if (index == CB_ERR) return $"ERR:未找到选项 {option}";
            SendMessage(ctrl, CB_SETCURSEL, (IntPtr)index, IntPtr.Zero);
            IntPtr parent = GetParent(ctrl);
            int ctrlId = GetWindowLong(ctrl, GWL_ID);
            SendMessage(parent, WM_COMMAND, (IntPtr)((CBN_SELCHANGE << 16) | ctrlId), ctrl);
            
            return $"OK:已选择 [{option}]";
        }

        static string DoExists(IntPtr window, string[] parts)
        {
            try { FindControl(window, parts[2]); return "OK:true"; }
            catch { return "OK:false"; }
        }

        static string DoIsEnabled(IntPtr window, string[] parts)
        {
            IntPtr ctrl = FindControl(window, parts[2]);
            return IsWindowEnabled(ctrl) ? "OK:true" : "OK:false";
        }

        static string DoWait(string appTitle, string[] parts)
        {
            string dialogTitle = parts[2];
            int timeout = parts.Length > 4 ? int.Parse(parts[4]) : 15;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            IntPtr dialog = IntPtr.Zero;
            while (sw.Elapsed.TotalSeconds < timeout)
            {
                dialog = FindWindowByTitle(dialogTitle);
                if (dialog != IntPtr.Zero) break;
                Thread.Sleep(300);
            }
            if (dialog == IntPtr.Zero) return $"ERR:等待弹窗超时 [{dialogTitle}]";
            return $"OK:弹窗出现";
        }

        static string DoFocus(IntPtr window, string[] parts)
        {
            IntPtr ctrl = FindControl(window, parts[2]);
            SetFocus(ctrl);
            return $"OK:已使控件获得焦点";
        }

        // =====================================================
        // UIA Fallback: 对于网格这种高度虚拟化无句柄的设计，借助 UIA 解析行列表
        // =====================================================
        static string DoGridRows(IntPtr window, string[] parts)
        {
            IntPtr grid = FindControl(window, parts[2]);
            int maxRows = parts.Length > 3 && int.TryParse(parts[3], out int m) ? m : 500;
            
            var el = AutomationElement.FromHandle(grid);
            var sb = new StringBuilder("OK:\n");
            var walker = TreeWalker.ControlViewWalker;
            var child = walker.GetFirstChild(el);
            int rowIdx = 0;
            
            while (child != null && rowIdx < maxRows)
            {
                var ct = child.Current.ControlType;
                if (ct == ControlType.DataItem || ct == ControlType.ListItem || ct == ControlType.TreeItem || ct == ControlType.Custom)
                {
                    var cols = new List<string>();
                    var cell = walker.GetFirstChild(child);
                    while (cell != null)
                    {
                        cols.Add(cell.TryGetCurrentPattern(ValuePattern.Pattern, out object? vp) 
                            ? ((ValuePattern)vp).Current.Value 
                            : (cell.Current.Name ?? ""));
                        cell = walker.GetNextSibling(cell);
                    }
                    if (cols.Count == 0) cols.Add(child.Current.Name ?? "");
                    sb.AppendLine(string.Join("\t", cols));
                    rowIdx++;
                }
                child = walker.GetNextSibling(child);
            }
            return sb.ToString().TrimEnd();
        }

        static string DoGridSelect(IntPtr window, string[] parts)
        {
            IntPtr grid = FindControl(window, parts[2]);
            int rowIndex = int.Parse(parts[3]);
            
            var el = AutomationElement.FromHandle(grid);
            var walker = TreeWalker.ControlViewWalker;
            var child = walker.GetFirstChild(el);
            int current = 0;
            
            while (child != null)
            {
                var ct = child.Current.ControlType;
                if (ct == ControlType.DataItem || ct == ControlType.ListItem || ct == ControlType.Custom)
                {
                    if (current == rowIndex)
                    {
                        if (child.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object? sp))
                        {
                            ((SelectionItemPattern)sp).Select();
                        }
                        else
                        {
                            child.SetFocus();
                            var rect = child.Current.BoundingRectangle;
                            var pt = new System.Drawing.Point((int)(rect.Left + 5), (int)(rect.Top + rect.Height / 2));
                            System.Windows.Forms.Cursor.Position = pt;
                            Thread.Sleep(60);
                            mouse_event(MOUSEEVENTF_LEFTDOWN, pt.X, pt.Y, 0, 0);
                            Thread.Sleep(50);
                            mouse_event(MOUSEEVENTF_LEFTUP, pt.X, pt.Y, 0, 0);
                        }
                        return $"OK:已选中第 {rowIndex} 行";
                    }
                    current++;
                }
                child = walker.GetNextSibling(child);
            }
            return $"ERR:行超出范围";
        }

        static string DoPopupInfo(IntPtr window, string[] parts)
        {
            IntPtr popup = FindControl(window, parts[2]);
            var texts = new List<string>();
            var btns = new List<string>();
            
            bool WalkForPop(IntPtr root)
            {
                IntPtr child = GetWindow(root, GW_CHILD);
                while (child != IntPtr.Zero)
                {
                    string cls = GetClassNameStr(child).ToLower();
                    string txt = GetWindowTextStr(child);
                    if (cls.Contains("button") || cls.Contains("command")) btns.Add(txt);
                    else if (cls.Contains("label") || cls.Contains("static")) texts.Add(txt);
                    WalkForPop(child);
                    child = GetWindow(child, GW_HWNDNEXT);
                }
                return false;
            }
            WalkForPop(popup);
            return $"OK:title={GetWindowTextStr(popup)}|body={string.Join(" ", texts)}|buttons={string.Join(",", btns)}";
        }

        // =====================================================
        //  Win32 物理世界透视树
        // =====================================================

        static void DoListControlsStream(IntPtr window, string[] parts, TextWriter writer)
        {
            writer.WriteLine("OK:");
            var counters = new Dictionary<string, int>();
            try { WalkWin32Tree(window, writer, 1, counters); }
            catch (Exception ex) { writer.WriteLine($"ERR:{ex.Message}"); }
        }

        static void WalkWin32Tree(IntPtr root, TextWriter writer, int depth, Dictionary<string, int> counters)
        {
            IntPtr child = GetWindow(root, GW_CHILD);
            while (child != IntPtr.Zero)
            {
                string cls = GetClassNameStr(child);
                string text = GetWindowTextStr(child);
                
                int count = counters.ContainsKey(cls.ToLower()) ? counters[cls.ToLower()] + 1 : 1;
                counters[cls.ToLower()] = count;
                string magicId = $"<{cls}{count}>";
                
                bool enabled = IsWindowEnabled(child);
                bool visible = IsWindowVisible(child);
                
                GetWindowRect(child, out RECT rect);
                int w = rect.Right - rect.Left;
                
                string display = string.IsNullOrWhiteSpace(text) ? magicId : $"{magicId} {text}";
                display += $" [类:{cls}]";
                if (!visible) display += " {隐}";
                if (visible && w > 0) display += $" [矩形:{rect.Left},{rect.Top} 宽:{w}]";
                
                string pad = new string('-', depth * 2) + " ";
                
                // 白噪音垃圾容器过滤 (仅限没有显示标题的透明/装饰类)
                bool isNoise = string.IsNullOrWhiteSpace(text) && (
                    cls.IndexOf("Timer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    cls.IndexOf("PictureBox", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    cls.IndexOf("UserControl", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    cls.IndexOf("DockWnd", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    cls.IndexOf("DynaBar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    cls.IndexOf("ScrollBar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    cls.IndexOf("MDIClient", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    cls.IndexOf("OleControl", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    cls.IndexOf("Embedding", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    cls.Equals("Static", StringComparison.OrdinalIgnoreCase)
                );

                if (!isNoise)
                {
                    writer.WriteLine($"{cls}|{pad}{display}|{(enabled ? "1" : "0")}");
                }
                
                WalkWin32Tree(child, writer, depth + 1, counters);
                child = GetWindow(child, GW_HWNDNEXT);
            }
        }

        // =====================================================
        //  P/Invoke
        // =====================================================

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool IsWindowEnabled(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam);
        [DllImport("user32.dll", EntryPoint = "SendMessage", CharSet = CharSet.Auto)] static extern IntPtr SendMessageString(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern IntPtr SetFocus(IntPtr hWnd);
        [DllImport("user32.dll")] static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, int dwExtraInfo);
        [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        const uint GW_CHILD = 5;
        const uint GW_HWNDNEXT = 2;
        const uint BM_CLICK = 0x00F5;
        const uint WM_SETTEXT = 0x000C;
        const uint WM_GETTEXT = 0x000D;
        const uint WM_GETTEXTLENGTH = 0x000E;
        const uint CB_SETCURSEL = 0x014E;
        const uint CB_FINDSTRINGEXACT = 0x0158;
        const int CB_ERR = -1;
        const uint WM_COMMAND = 0x0111;
        const int CBN_SELCHANGE = 1;
        const int GWL_ID = -12;
        const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        const uint MOUSEEVENTF_LEFTUP = 0x0004;

        struct RECT { public int Left, Top, Right, Bottom; }

        static string GetWindowTextStr(IntPtr hWnd)
        {
            int len = GetWindowTextLength(hWnd) + 1;
            StringBuilder sb = new StringBuilder(len);
            GetWindowText(hWnd, sb, len);
            return sb.ToString();
        }

        static string GetClassNameStr(IntPtr hWnd)
        {
            StringBuilder sb = new StringBuilder(256);
            GetClassName(hWnd, sb, 256);
            return sb.ToString();
        }
    }
}
