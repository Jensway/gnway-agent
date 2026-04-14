// =============================================================
//  GnwayAgent - 鏈嶅姟绔?Agent (Native Win32 Edition)
//  閮ㄧ讲鍒颁簯鑱旀湇鍔″櫒锛岄€氳繃鍛藉悕绠￠亾鎺ユ敹鍛戒护锛屾搷浣滃悓 Session 鍐呯殑绋嬪簭
//  鍩轰簬 EnumChildWindows 瀹炵幇鏋侀€熸棤鎰熺煡銆佺簿鍑嗛€忚鐨?VB6 鎻愬彇
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
                        var remoteEP = client.Client.RemoteEndPoint?.ToString() ?? "鏈煡IP";
                        Console.WriteLine($"\n[TCP连接] Controller ({remoteEP}) 已接入！");

                        var reader = new StreamReader(stream, Encoding.UTF8);
                        var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                        string? cmdLine = reader.ReadLine();
                        if (string.IsNullOrEmpty(cmdLine)) { writer.WriteLine("ERR:empty_command"); continue; }

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
                            Console.WriteLine("\n[⭐提示] 控件树已保存到 agent_dump.txt");
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
                        Console.WriteLine("\n[info] Result dumped to agent_dump.txt");
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
            Console.WriteLine("\n==== [Agent 本地调试菜单] ====");
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

                if (parts.Length < 2) return "ERR:missing_params_format_action|window_name...";
                string appTitle = parts[1];

                if (action == "windowexists") return FindWindowByTitle(appTitle) != IntPtr.Zero ? "OK:true" : "OK:false";

                var matchingWindows = FindAllWindowsByTitle(appTitle);
                if (matchingWindows.Count == 0) throw new Exception($"Window not found: {appTitle}");

                if (action == "listcontrols" || action == "tree")
                {
                    DoListControlsStream(matchingWindows[0], parts, writer);
                    return null;
                }
                
                if (action == "treehash")
                {
                    return DoTreeHash(matchingWindows[0], parts);
                }

                if (action == "wait") return DoWait(appTitle, parts);

                string? lastError = null;
                foreach (var win in matchingWindows)
                {
                    try
                    {
                        string result = action switch
                        {
                            "click"      => DoClick(win, parts),
                            "input"      => DoInput(win, parts),
                            "sendkeys"   => DoSendKeys(win, parts),
                            "gettext"    => DoGetText(win, parts),
                            "select"     => DoSelect(win, parts),
                            "exists"     => DoExists(win, parts),
                            "isenabled"  => DoIsEnabled(win, parts),
                            "popupinfo"  => DoPopupInfo(win, parts),
                            "gridrows"   => DoGridRows(win, parts),
                            "gridselect" => DoGridSelect(win, parts),
                            "focus"      => DoFocus(win, parts),
                            _            => $"ERR:Unknown action [{action}]"
                        };

                        // 如果操作明确返回了 OK:false (例如 exists 未找到控件)，也要继续尝试下一个窗口
                        if (result != null && !result.StartsWith("ERR:") && result != "OK:false")
                            return result;
                        
                        // 对于 OK:false 或 ERR，记录下来但不立即返回，以便尝试其它候选窗口
                        if (result != null) lastError = result;
                    }
                    catch (Exception ex)
                    {
                        lastError = $"ERR:{ex.Message}";
                    }
                }
                return lastError ?? "ERR:Action failed on all matching windows";
            }
            catch (Exception ex) { return $"ERR:{ex.Message}"; }
        }

        // =====================================================
        //  Win32 鏋侀€熸搷鎺ч€昏緫
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

        static List<IntPtr> FindAllWindowsByTitle(string titlePattern)
        {
            var found = new List<IntPtr>();
            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd)) return true;
                string title = GetWindowTextStr(hWnd);
                if (title.Contains(titlePattern)) { found.Add(hWnd); }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        static string DoTreeHash(IntPtr window, string[] parts)
        {
            int hash = 0;
            try
            {
                WalkWin32TreeForHash(window, ref hash);
                return $"OK:{Math.Abs(hash)}";
            }
            catch (Exception ex) { return $"ERR:{ex.Message}"; }
        }

        static void WalkWin32TreeForHash(IntPtr root, ref int hash)
        {
            IntPtr child = GetWindow(root, GW_CHILD);
            while (child != IntPtr.Zero)
            {
                hash = unchecked(hash * 31 + GetClassNameStr(child).GetHashCode());
                string txt = GetWindowTextStr(child);
                if (!string.IsNullOrEmpty(txt)) hash = unchecked(hash * 31 + txt.GetHashCode());
                GetWindowRect(child, out RECT rect);
                hash += (rect.Right - rect.Left); // Use width to mix hash
                WalkWin32TreeForHash(child, ref hash);
                child = GetWindow(child, GW_HWNDNEXT);
            }
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
                throw new Exception($"Failed to find control by absolute cord: {controlName}");
            }

            // Fallback: 妯＄硦鍖归厤鏍戜腑鎵€鏈夌殑鍙敤鍚嶇О
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

            throw new Exception($"Control not found: [{controlName}]");
        }
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        class RadarForm : System.Windows.Forms.Form
        {
            protected override System.Windows.Forms.CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    // WS_EX_LAYERED (0x80000) | WS_EX_TRANSPARENT (0x20)
                    cp.ExStyle |= 0x80020;
                    return cp;
                }
            }
        }

        static void ShowClickHighlight(System.Drawing.Rectangle rect)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;
            var t = new Thread(() => {
                try {
                    int size = 120; 
                    int cx = rect.X + rect.Width / 2;
                    int cy = rect.Y + rect.Height / 2;
                    
                    var f = new RadarForm {
                        FormBorderStyle = System.Windows.Forms.FormBorderStyle.None,
                        BackColor = System.Drawing.Color.Magenta,
                        TransparencyKey = System.Drawing.Color.Magenta,
                        TopMost = true,
                        ShowInTaskbar = false,
                        StartPosition = System.Windows.Forms.FormStartPosition.Manual,
                        AutoScaleMode = System.Windows.Forms.AutoScaleMode.None,
                        Bounds = new System.Drawing.Rectangle(cx - size / 2, cy - size / 2, size, size)
                    };
                    
                    int tickCount = 0;
                    f.Paint += (s, e) => {
                        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        // Avoid clearing to Magenta repeatedly as it can cause flickering
                        // We rely on the initial TransparencyKey and BackColor setup
                        
                        // 1. 中心圆心点 (黄色小点)
                        if (tickCount % 6 < 4) // 控制高频闪烁节奏
                        {
                            using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.Yellow))
                                e.Graphics.FillEllipse(brush, size / 2 - 4, size / 2 - 4, 8, 8);
                            using (var pen = new System.Drawing.Pen(System.Drawing.Color.Red, 2))
                                e.Graphics.DrawEllipse(pen, size / 2 - 8, size / 2 - 8, 16, 16);
                        }
                        
                        // 2. 扩散的雷达波纹 (更粗，不使用抗锯齿可能更好避免 Magenta halo，这里改用稍浅的颜色)
                        int radius = 4 + tickCount * 4;
                        if (radius < size / 2 - 2)
                        {
                            using (var pen = new System.Drawing.Pen(System.Drawing.Color.Red, 4))
                                e.Graphics.DrawEllipse(pen, size / 2 - radius, size / 2 - radius, radius * 2, radius * 2);
                        }

                        int radius2 = radius - 24;
                        if (radius2 > 4 && radius2 < size / 2 - 2)
                        {
                            using (var pen = new System.Drawing.Pen(System.Drawing.Color.DarkOrange, 3))
                                e.Graphics.DrawEllipse(pen, size / 2 - radius2, size / 2 - radius2, radius2 * 2, radius2 * 2);
                        }
                    };
                    
                    var tmr = new System.Windows.Forms.Timer { Interval = 30 };
                    tmr.Tick += (s, e) => { 
                        tickCount++; 
                        if (tickCount >= 25) { tmr.Stop(); f.Close(); } 
                        else {
                            SetWindowPos(f.Handle, HWND_TOPMOST, 0, 0, 0, 0, 0x0002 | 0x0001 | 0x0010 /* SWP_NOACTIVATE */);
                            f.Invalidate(); 
                        } 
                    };
                    
                    f.Load += (s, e) => {
                        SetWindowPos(f.Handle, HWND_TOPMOST, 0, 0, 0, 0, 0x0002 | 0x0001 | 0x0010);
                        tmr.Start();
                    };
                    
                    System.Windows.Forms.Application.Run(f);
                } catch { } 
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
        }

        static string DoClick(IntPtr window, string[] parts)
        {
            string controlName = parts[2];
            if (controlName.Contains("<UIA_") || controlName.Contains("<TB_") || controlName.Contains("<MSAA_"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(controlName, @"<(?:UIA|MSAA|TB)_([A-Za-z0-9_]+?)(\d+)_BTN(\d+)>");
                if (!match.Success) return "ERR:无效的虚拟按键格式";
                string parentMagic = $"<{match.Groups[1].Value}{match.Groups[2].Value}>";
                int btnIndex = int.Parse(match.Groups[3].Value);
                IntPtr parentHwnd = FindControl(window, parentMagic);
                if (parentHwnd == IntPtr.Zero) return $"ERR:找不到宿主工具栏 {parentMagic}";

                // TB_ 前缀: 读取目标进程 RECT 后执行物理坐标点击
                if (controlName.Contains("<TB_"))
                {
                    int targetSystemIdx = -1;
                    int validIdx = 0;
                    int tbCount = (int)SendMessage(parentHwnd, TB_BUTTONCOUNT, IntPtr.Zero, IntPtr.Zero);
                    for (int i = 0; i < tbCount; i++)
                    {
                        RECT r = GetToolbarButtonRect(parentHwnd, i);
                        if ((r.Right - r.Left) > 0)
                        {
                            validIdx++;
                            if (validIdx == btnIndex)
                            {
                                targetSystemIdx = i;
                                break;
                            }
                        }
                    }

                    if (targetSystemIdx == -1) return $"ERR:在当前工具栏中找不到第 {btnIndex} 个有效按键";

                    int idx = targetSystemIdx;
                    RECT btnRect = GetToolbarButtonRect(parentHwnd, idx);
                    if (btnRect.Right > btnRect.Left) // 有效的包围盒
                    {
                        System.Drawing.Point tbPt = new System.Drawing.Point(
                            btnRect.Left + (btnRect.Right - btnRect.Left) / 2,
                            btnRect.Top + (btnRect.Bottom - btnRect.Top) / 2
                        );
                        ClientToScreen(parentHwnd, ref tbPt);

                        System.Drawing.Point lt = new System.Drawing.Point(btnRect.Left, btnRect.Top);
                        System.Drawing.Point rb = new System.Drawing.Point(btnRect.Right, btnRect.Bottom);
                        ClientToScreen(parentHwnd, ref lt);
                        ClientToScreen(parentHwnd, ref rb);
                        ShowClickHighlight(new System.Drawing.Rectangle(lt.X, lt.Y, rb.X - lt.X, rb.Y - lt.Y));

                        System.Windows.Forms.Cursor.Position = tbPt; Thread.Sleep(50);
                        mouse_event(MOUSEEVENTF_LEFTDOWN, tbPt.X, tbPt.Y, 0, 0); Thread.Sleep(50);
                        mouse_event(MOUSEEVENTF_LEFTUP, tbPt.X, tbPt.Y, 0, 0);
                        return $"OK:已物理点击工具栏按钮 [{controlName}] (内部索引:{idx})";
                    }
                    else
                    {
                        // 兜底发消息 (通常此项只会有动画效果，不会触发回调)
                        SendMessage(parentHwnd, TB_PRESSBUTTON, (IntPtr)idx, (IntPtr)1);
                        Thread.Sleep(100);
                        SendMessage(parentHwnd, TB_PRESSBUTTON, (IntPtr)idx, (IntPtr)0);
                        return $"OK:跨进程注入失败，已执行兜底压键 [{controlName}]";
                    }
                }

                // MSAA_ 前缀: 用 IAccessible.accDoDefaultAction
                if (controlName.Contains("<MSAA_"))
                {
                    try
                    {
                        Guid iidAcc = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");
                        int hr = AccessibleObjectFromWindow(parentHwnd, OBJID_CLIENT, ref iidAcc, out object accObj);
                        if (hr == 0 && accObj is Accessibility.IAccessible acc)
                        {
                            object[] children = new object[acc.accChildCount];
                            AccessibleChildren(acc, 0, acc.accChildCount, children, out int obtained);
                            int curIdx = 1;
                            for (int i = 0; i < obtained; i++)
                            {
                                string name = "";
                                try { name = acc.get_accName(children[i]) ?? ""; } catch { }
                                string role = "";
                                try { role = acc.get_accRole(children[i])?.ToString() ?? ""; } catch { }
                                
                                int px = 0, py = 0, pw = 0, ph = 0;
                                try { acc.accLocation(out px, out py, out pw, out ph, children[i]); } catch { }

                                bool matchesRole = !string.IsNullOrWhiteSpace(name) || role.Contains("43") || role.Contains("push");
                                if (matchesRole && pw > 0 && ph > 0)
                                {
                                    if (curIdx == btnIndex)
                                    {
                                        acc.accDoDefaultAction(children[i]);
                                        return $"OK:已MSAA点击 [{controlName}] ({name})";
                                    }
                                    curIdx++;
                                }
                            }
                        }
                        return $"ERR:MSAA未找到第 {btnIndex} 个按钮";
                    }
                    catch (Exception ex) { return $"ERR:MSAA点击失败 {ex.Message}"; }
                }

                // UIA_ 前缀: 用 InvokePattern 或坐标点击
                try
                {
                    var el = FindUiaVirtualControl(window, controlName);
                    if (!el.Current.IsEnabled) return "ERR:Control disabled";
                    try {
                        ((InvokePattern)el.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                        return $"OK:已原生虚拟点击 [{controlName}]";
                    } catch {
                        var uiaRect = el.Current.BoundingRectangle;
                        ShowClickHighlight(new System.Drawing.Rectangle((int)uiaRect.Left, (int)uiaRect.Top, (int)uiaRect.Width, (int)uiaRect.Height));

                        System.Drawing.Point uiaPt = new System.Drawing.Point((int)(uiaRect.Left + uiaRect.Width/2), (int)(uiaRect.Top + uiaRect.Height/2));
                        System.Windows.Forms.Cursor.Position = uiaPt; Thread.Sleep(50);
                        mouse_event(MOUSEEVENTF_LEFTDOWN, uiaPt.X, uiaPt.Y, 0, 0); Thread.Sleep(50);
                        mouse_event(MOUSEEVENTF_LEFTUP, uiaPt.X, uiaPt.Y, 0, 0);
                        return $"OK:已虚拟坐标点击 [{controlName}]";
                    }
                }
                catch (Exception ex) { return $"ERR:{ex.Message}"; }
            }
            
            IntPtr ctrl = FindControl(window, controlName);
            
            // 强制将窗口拉回前台并激活，突破系统的 Foreground Lock 限制
            ForceForegroundWindow(window);
            Thread.Sleep(100);

            // 鍏滃簳妯℃嫙鍧愭爣鐐瑰嚮
            GetWindowRect(ctrl, out RECT rect);
            System.Drawing.Point pt;

            if (parts.Length > 3 && !string.IsNullOrWhiteSpace(parts[3]) && parts[3].Contains(","))
            {
                var coords = parts[3].Split(',');
                if (coords.Length == 2 && int.TryParse(coords[0], out int x) && int.TryParse(coords[1], out int y))
                {
                    pt = new System.Drawing.Point(rect.Left + x, rect.Top + y);
                }
                else
                {
                    pt = new System.Drawing.Point(
                        rect.Left + (rect.Right - rect.Left) / 2, 
                        rect.Top + (rect.Bottom - rect.Top) / 2);
                }
            }
            else
            {
                pt = new System.Drawing.Point(
                    rect.Left + (rect.Right - rect.Left) / 2, 
                    rect.Top + (rect.Bottom - rect.Top) / 2);
            }

            System.Windows.Forms.Cursor.Position = pt;

            int w = rect.Right - rect.Left;
            int h = rect.Bottom - rect.Top;
            ShowClickHighlight(new System.Drawing.Rectangle(rect.Left, rect.Top, w, h));

            Thread.Sleep(50);
            mouse_event(MOUSEEVENTF_LEFTDOWN, pt.X, pt.Y, 0, 0);
            Thread.Sleep(50);
            mouse_event(MOUSEEVENTF_LEFTUP, pt.X, pt.Y, 0, 0);
            
            string coordMsg = parts.Length > 3 && !string.IsNullOrWhiteSpace(parts[3]) ? $"({parts[3]})" : "";
            return $"OK:Clicked [{controlName}]{coordMsg}";
        }

        static string DoInput(IntPtr window, string[] parts)
        {
            string controlName = parts[2];
            string text = parts.Length > 3 ? parts[3] : "";
            IntPtr ctrl = FindControl(window, controlName);

            // 鐩存帴 Win32 闇哥帇纭笂寮撲慨鏀?            SendMessage(ctrl, WM_SETTEXT, IntPtr.Zero, text);
            
            // 鍏滃簳 Focus + 閿洏杈撳叆 (瀵归儴鍒?VB6 鎷︽埅淇敼鏈夌敤)
            SetFocus(ctrl);
            Thread.Sleep(100);
            System.Windows.Forms.SendKeys.SendWait("^a");
            System.Windows.Forms.SendKeys.SendWait("{DELETE}");
            System.Windows.Forms.SendKeys.SendWait(text);
            
            return $"OK:InputText [{text}] -> [{controlName}]";
        }

        static string DoSendKeys(IntPtr window, string[] parts)
        {
            string controlName = parts[2];
            string keys = parts.Length > 3 ? parts[3] : "";
            IntPtr ctrl = FindControl(window, controlName);
            SetFocus(ctrl);
            Thread.Sleep(100);
            if (!string.IsNullOrEmpty(keys))
            {
                System.Windows.Forms.SendKeys.SendWait(keys);
            }
            return $"OK:SentKeys [{keys}] -> [{controlName}]";
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
            if (index == CB_ERR) return $"ERR:OptionNotFound {option}";
            SendMessage(ctrl, CB_SETCURSEL, (IntPtr)index, IntPtr.Zero);
            IntPtr parent = GetParent(ctrl);
            int ctrlId = GetWindowLong(ctrl, GWL_ID);
            SendMessage(parent, WM_COMMAND, (IntPtr)((CBN_SELCHANGE << 16) | ctrlId), ctrl);
            
            return $"OK:Selected [{option}]";
        }
        static string DoExists(IntPtr window, string[] parts)
        {
            try
            {
                string ctrl = parts[2];
                if (ctrl.Contains("<UIA_") || ctrl.Contains("<TB_") || ctrl.Contains("<MSAA_"))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(ctrl, @"<(?:UIA|MSAA|TB)_([A-Za-z0-9_]+?)(\d+)_BTN(\d+)>");
                    if (match.Success)
                    {
                        string parentMagic = $"<{match.Groups[1].Value}{match.Groups[2].Value}>";
                        IntPtr parentHwnd = FindControl(window, parentMagic);
                        if (parentHwnd != IntPtr.Zero) return "OK:true";
                    }
                    return "OK:false";
                }
                
                FindControl(window, ctrl); 
                return "OK:true";
            }
            catch { return "OK:false"; }
        }

        static string DoIsEnabled(IntPtr window, string[] parts)
        {
            try
            {
                string ctrl = parts[2];
                if (ctrl.Contains("<UIA_") || ctrl.Contains("<TB_") || ctrl.Contains("<MSAA_"))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(ctrl, @"<(?:UIA|MSAA|TB)_([A-Za-z0-9_]+?)(\d+)_BTN(\d+)>");
                    if (match.Success)
                    {
                        string parentMagic = $"<{match.Groups[1].Value}{match.Groups[2].Value}>";
                        IntPtr parentHwnd = FindControl(window, parentMagic);
                        return IsWindowEnabled(parentHwnd) ? "OK:true" : "OK:false";
                    }
                    return "OK:false";
                }

                IntPtr hwnd = FindControl(window, ctrl);
                return IsWindowEnabled(hwnd) ? "OK:true" : "OK:false";
            }
            catch { return "OK:false"; }
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
            if (dialog == IntPtr.Zero) return $"ERR:TimeoutWaitWindow [{dialogTitle}]";
            return $"OK:WindowAppeared";
        }

        static string DoFocus(IntPtr window, string[] parts)
        {
            IntPtr ctrl = FindControl(window, parts[2]);
            SetFocus(ctrl);
            return $"OK:Focused";
        }

        // =====================================================
        // UIA Fallback: 瀵逛簬缃戞牸杩欑楂樺害铏氭嫙鍖栨棤鍙ユ焺鐨勮璁★紝鍊熷姪 UIA 瑙ｆ瀽琛屽垪琛?        // =====================================================
        static string DoGridRows(IntPtr window, string[] parts)
        {
            IntPtr grid = FindControl(window, parts[2]);
            int maxRows = parts.Length > 3 && int.TryParse(parts[3], out int m) ? m : 500;
            
            ForceForegroundWindow(window);
            Thread.Sleep(150);

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
                    bool isSelected = false;
                    try {
                        if (child.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object? sp)) {
                            isSelected = ((SelectionItemPattern)sp).Current.IsSelected;
                        } else {
                            isSelected = child.Current.HasKeyboardFocus;
                        }
                    } catch { }

                    var cols = new List<string>();
                    cols.Add(isSelected ? "[SELECTED]" : "[UNSELECTED]");

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
                            ShowClickHighlight(new System.Drawing.Rectangle((int)rect.Left, (int)rect.Top, (int)rect.Width, (int)rect.Height));

                            var pt = new System.Drawing.Point((int)(rect.Left + 5), (int)(rect.Top + rect.Height / 2));
                            System.Windows.Forms.Cursor.Position = pt;
                            Thread.Sleep(60);
                            mouse_event(MOUSEEVENTF_LEFTDOWN, pt.X, pt.Y, 0, 0);
                            Thread.Sleep(50);
                            mouse_event(MOUSEEVENTF_LEFTUP, pt.X, pt.Y, 0, 0);
                        }
                        return $"OK:selected_row_{rowIndex}";
                    }
                    current++;
                }
                child = walker.GetNextSibling(child);
            }
            return $"ERR:row_out_of_bounds";
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
        //  Win32 鐗╃悊涓栫晫閫忚鏍?        // =====================================================

        static void DoListControlsStream(IntPtr window, string[] parts, TextWriter writer)
        {
            writer.WriteLine("OK:");
            var counters = new Dictionary<string, int>();
            try { WalkWin32Tree(window, writer, 1, counters); }
            catch (Exception ex) { writer.WriteLine($"ERR:{ex.Message}"); }
        }
        static void EnumerateUIAVirtualButtons(IntPtr parentHwnd, string parentMagicId, TextWriter writer, int depth)
        {
            // 策略1: Win32 原生工具栏消息 TB_BUTTONCOUNT
            int tbCount = (int)SendMessage(parentHwnd, TB_BUTTONCOUNT, IntPtr.Zero, IntPtr.Zero);
            if (tbCount > 0)
            {
                Console.WriteLine($"  [Win32工具栏] {parentMagicId} 发现 {tbCount} 个原生按钮");
                int validFound = 0;
                for (int i = 0; i < tbCount; i++)
                {
                    RECT rect = GetToolbarButtonRect(parentHwnd, i);
                    int w = rect.Right - rect.Left;
                    if (w <= 0) continue; // 跳过不可见或作为分隔符的工具栏按钮

                    validFound++;

                    StringBuilder sbText = new StringBuilder(260);
                    SendMessage(parentHwnd, TB_GETBUTTONTEXTW, (IntPtr)i, sbText);
                    string text = sbText.ToString();
                    if (string.IsNullOrEmpty(text)) text = $"Btn{validFound}";
                    
                    string magicId = $"<TB_{parentMagicId}_BTN{validFound}>";
                    string displayRect = $"[{rect.Left},{rect.Top} Width:{w}]";
                    writer.WriteLine($"TB_Button|{depth}|{magicId}|{text}|{displayRect}|1");
                    Console.WriteLine($"    [TB_{validFound}] {text}");
                }
                if (validFound > 0) return; // 如果真正找到了有效的、可见的原生按钮，就直接返回，否则向下走 MSAA/UIA
            }

            // 策略2: MSAA/IAccessible
            try
            {
                Guid iidAcc = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");
                int hr = AccessibleObjectFromWindow(parentHwnd, OBJID_CLIENT, ref iidAcc, out object accObj);
                if (hr == 0 && accObj is Accessibility.IAccessible acc)
                {
                    int childCount = acc.accChildCount;
                    Console.WriteLine($"  [MSAA] {parentMagicId} 发现 {childCount} 个子元素");
                    if (childCount > 0)
                    {
                        object[] children = new object[childCount];
                        AccessibleChildren(acc, 0, childCount, children, out int obtained);
                        int btnIdx = 1;
                        int validMsaaCount = 0;
                        for (int i = 0; i < obtained; i++)
                        {
                            try
                            {
                                int px = 0, py = 0, pw = 0, ph = 0;
                                try { acc.accLocation(out px, out py, out pw, out ph, children[i]); } catch { }
                                if (pw <= 0 || ph <= 0) 
                                {
                                    // 虽然我们跳过打印，但为了保证 MSAA 的序号点击一致，必须保证 idx 也是这里跳过的
                                    // 实际 DoClick 时 MSAA 的 idx 是独立计数的，但在现在的逻辑中两边规则必须镜像
                                    // 只要两边都是：满足 is interactive 就 idx++，那就能一致。
                                }

                                string name = "";
                                try { name = acc.get_accName(children[i]) ?? ""; } catch { }
                                string role = "";
                                try { role = acc.get_accRole(children[i])?.ToString() ?? ""; } catch { }
                                
                                bool matchesRole = !string.IsNullOrWhiteSpace(name) || role.Contains("43") || role.Contains("push");
                                
                                if (matchesRole && pw > 0 && ph > 0)
                                {
                                    string magicId = $"<MSAA_{parentMagicId}_BTN{btnIdx}>";
                                    string displayRect = $"[{px},{py} Width:{pw}]";
                                    writer.WriteLine($"MSAA_Button|{depth}|{magicId}|{name}|{displayRect}|1");
                                    Console.WriteLine($"    [MSAA_{btnIdx}] {name} role={role}");
                                    validMsaaCount++;
                                    btnIdx++; // 无论是否隐藏，只要符合查找Role特征，就消耗一个索引。这样与 DoClick 的逻辑完全一致
                                }
                            }
                            catch { }
                        }
                        if (validMsaaCount > 0) return;
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine($"  [MSAA异常] {ex.Message}"); }

            // 策略3: UIA 深度扫描
            try
            {
                var root = AutomationElement.FromHandle(parentHwnd);
                Console.WriteLine($"  [UIA] 扫描 {parentMagicId}...");
                var allElements = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
                if (allElements.Count == 0) allElements = root.FindAll(TreeScope.Children, Condition.TrueCondition);
                Console.WriteLine($"  [UIA] 发现 {allElements.Count} 个元素");
                int index = 1;
                foreach (AutomationElement el in allElements)
                {
                    if (!IsValidUiaButton(el)) continue;
                    
                    var ct = el.Current.ControlType;
                    string text = el.Current.Name ?? "";
                    string magicId = $"<UIA_{parentMagicId}_BTN{index}>";
                    bool enabled = el.Current.IsEnabled;
                    var rect = el.Current.BoundingRectangle;
                    int w = (int)rect.Width;
                    string displayRect = $"[{(int)rect.Left},{(int)rect.Top} Width:{w}]";
                    string typeName = $"UIA_{ct.ProgrammaticName.Replace("ControlType.", "")}";
                    writer.WriteLine($"{typeName}|{depth}|{magicId}|{text}|{displayRect}|{(enabled ? "1" : "0")}");
                    Console.WriteLine($"    [{index}] {typeName}: {text}");
                    index++;
                }
                if (index == 1) Console.WriteLine($"  [UIA] 无虚拟按钮");
            }
            catch (Exception ex) { Console.WriteLine($"  [UIA异常] {ex.Message}"); }
        }

        static bool IsValidUiaButton(AutomationElement el)
        {
            var ct = el.Current.ControlType;
            if (ct == ControlType.ToolBar || ct == ControlType.Pane || ct == ControlType.Window) return false;
            
            bool isInteractive = (ct == ControlType.Button || ct == ControlType.MenuItem ||
                                  ct == ControlType.SplitButton || ct == ControlType.Custom ||
                                  ct == ControlType.Tab || ct == ControlType.TabItem ||
                                  ct == ControlType.Hyperlink || ct == ControlType.Image ||
                                  ct == ControlType.ListItem || ct == ControlType.TreeItem);
            if (!isInteractive) return false;

            try
            {
                if (el.Current.IsOffscreen) return false;
                var rect = el.Current.BoundingRectangle;
                if (rect.Width <= 0 || rect.Height <= 0) return false;
            }
            catch { return false; }

            return true;
        }

        static AutomationElement FindUiaVirtualControl(IntPtr window, string controlName)
        {
            var uiaMatch = System.Text.RegularExpressions.Regex.Match(controlName, @"<(?:UIA|MSAA|TB)_([A-Za-z0-9_]+?)(\d+)_BTN(\d+)>");
            if (!uiaMatch.Success) throw new Exception("无效的虚拟按键格式");
            
            string parentMagic = $"<{uiaMatch.Groups[1].Value}{uiaMatch.Groups[2].Value}>";
            int btnIndex = int.Parse(uiaMatch.Groups[3].Value);
            
            IntPtr parentHwnd = FindControl(window, parentMagic);
            if (parentHwnd == IntPtr.Zero) throw new Exception($"找不到宿主工具栏: {parentMagic}");
            
            var root = AutomationElement.FromHandle(parentHwnd);
            var allElements = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            if (allElements.Count == 0)
                allElements = root.FindAll(TreeScope.Children, Condition.TrueCondition);
            int curIdx = 1;
            foreach (AutomationElement el in allElements)
            {
                if (!IsValidUiaButton(el)) continue;
                if (curIdx == btnIndex) return el;
                curIdx++;
            }
            throw new Exception($"在工具栏中未发现第 {btnIndex} 个按钮");
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
                
                string displayRect = visible && w > 0 ? $"[{rect.Left},{rect.Top} Width:{w}]" : (visible ? "" : "{Hidden}");
                
                // 鐧藉櫔闊冲瀮鍦惧鍣ㄨ繃婊?(浠呴檺娌℃湁鏄剧ず鏍囬鐨勯€忔槑/瑁呴グ绫?
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
                    writer.WriteLine($"{cls}|{depth}|{magicId}|{text}|{displayRect}|{(enabled ? "1" : "0")}");
                }
                if (cls.IndexOf("toolbar", StringComparison.OrdinalIgnoreCase) >= 0 || 
                    cls.IndexOf("msvb_lib_toolbar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    cls.IndexOf("activebar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    cls.IndexOf("dockwnd", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    try { EnumerateUIAVirtualButtons(child, magicId.Trim('<', '>'), writer, depth + 1); } catch { }
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
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr ProcessId);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hWnd);

        static void ForceForegroundWindow(IntPtr hWnd)
        {
            IntPtr fgWnd = GetForegroundWindow();
            if (fgWnd == hWnd) return;

            uint fgThread = GetWindowThreadProcessId(fgWnd, IntPtr.Zero);
            uint myThread = GetCurrentThreadId();
            uint targetThread = GetWindowThreadProcessId(hWnd, IntPtr.Zero);

            if (fgThread != myThread) AttachThreadInput(myThread, fgThread, true);
            if (targetThread != myThread) AttachThreadInput(myThread, targetThread, true);

            ShowWindow(hWnd, 9);
            BringWindowToTop(hWnd);
            SetForegroundWindow(hWnd);

            if (fgThread != myThread) AttachThreadInput(myThread, fgThread, false);
            if (targetThread != myThread) AttachThreadInput(myThread, targetThread, false);
        }

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
        const uint TB_BUTTONCOUNT = 0x0418;
        const uint TB_GETBUTTONTEXTW = 0x044B;
        const uint TB_PRESSBUTTON = 0x0403;
        const uint OBJID_CLIENT = 0xFFFFFFFC;
        
        const uint PROCESS_VM_OPERATION = 0x0008;
        const uint PROCESS_VM_READ = 0x0010;
        const uint PROCESS_VM_WRITE = 0x0020;
        const uint MEM_RESERVE = 0x2000;
        const uint MEM_COMMIT = 0x1000;
        const uint PAGE_READWRITE = 0x04;
        const uint MEM_RELEASE = 0x8000;
        const uint TB_GETITEMRECT = 0x041D;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, StringBuilder lParam);

        [DllImport("user32.dll")]
        static extern bool ClientToScreen(IntPtr hWnd, ref System.Drawing.Point lpPoint);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, out RECT lpBuffer, uint nSize, out IntPtr lpNumberOfBytesRead);
        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);

        static RECT GetToolbarButtonRect(IntPtr hwnd, int index)
        {
            GetWindowThreadProcessId(hwnd, out int pid);
            IntPtr hProcess = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, pid);
            if (hProcess == IntPtr.Zero) return new RECT();
            
            IntPtr allocatedAddress = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)Marshal.SizeOf(typeof(RECT)), MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
            if (allocatedAddress == IntPtr.Zero) 
            {
                CloseHandle(hProcess);
                return new RECT();
            }

            try
            {
                int res = (int)SendMessage(hwnd, TB_GETITEMRECT, (IntPtr)index, allocatedAddress);
                if (res != 0)
                {
                    ReadProcessMemory(hProcess, allocatedAddress, out RECT rect, (uint)Marshal.SizeOf(typeof(RECT)), out _);
                    return rect;
                }
            }
            finally
            {
                VirtualFreeEx(hProcess, allocatedAddress, 0, MEM_RELEASE);
                CloseHandle(hProcess);
            }
            return new RECT();
        }

        [DllImport("oleacc.dll")]
        static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint dwObjectId, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppvObject);

        [DllImport("oleacc.dll")]
        static extern int AccessibleChildren([MarshalAs(UnmanagedType.Interface)] object paccContainer, int iChildStart, int cChildren, [Out] object[] rgvarChildren, out int pcObtained);

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

