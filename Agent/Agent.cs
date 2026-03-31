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

            Console.WriteLine("=== GnwayAgent 鏈嶅姟绔?(Native Win32 鏋侀€熺増) ===");
            Console.WriteLine($"杩涚▼ID: {System.Diagnostics.Process.GetCurrentProcess().Id}");
            Console.WriteLine($"TCP 绔彛: {port} (鍙檮鍔犲弬鏁板惎鍔ㄤ慨鏀癸紝濡? Agent.exe 9090)");
            Console.WriteLine($"涓绘満鍚嶇О: {Dns.GetHostName()}");

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
                        Console.WriteLine($"\n[TCP杩炴帴] Controller ({remoteEP}) 宸叉帴鍏ワ紒");

                        var reader = new StreamReader(stream, Encoding.UTF8);
                        var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                        string? cmdLine = reader.ReadLine();
                        if (string.IsNullOrEmpty(cmdLine)) { writer.WriteLine("ERR:empty_command"); continue; }

                        Console.WriteLine($"[鏀跺埌缃戠粶鎸囦护] {cmdLine}");
                        string? result = ProcessCommand(cmdLine, writer);
                        
                        if (result != null)
                        {
                            writer.WriteLine(result);
                            Console.WriteLine($"[缃戠粶杩斿洖] {result}");
                        }
                        else
                        {
                            Console.WriteLine($"[缃戠粶杩斿洖] <娴佸紡杈撳嚭瀹屾瘯>");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[TCP閿欒] {ex.Message}");
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

                        Console.WriteLine($"\n========== [鎷夊彇鍏ㄩ儴鎺т欢鏍慮 {target} ==========");
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
                            Console.WriteLine("\n[猸?鎻愮ず] 鎺т欢鏍戝凡淇濆瓨鍒?agent_dump.txt");
                        } catch { }
                        Console.WriteLine("====================================================\n杈撳叆 'm' 鍒锋柊");
                    }
                    else Console.WriteLine(">>> 缂栧彿鏃犳晥");
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
                catch (Exception ex) { Console.WriteLine($"[鏈湴閿欒] {ex.Message}"); }
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
            Console.WriteLine("\n==== [Agent 鏈湴鎳掍汉璋冭瘯鑿滃崟] ====");
            var list = GetValidWindows();
            for (int i = 0; i < list.Count; i++) Console.WriteLine($" [{i + 1}] {list[i]}");
            Console.WriteLine("==================================");
            Console.Write("\n璇疯緭鍏ユ暟瀛楁垨鎸囦护 (濡? m): ");
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

                IntPtr window = FindWindowByTitle(appTitle);
                if (window == IntPtr.Zero) throw new Exception($"鎵句笉鍒扮獥鍙? {appTitle}");

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
                    _            => $"ERR:鏈煡鍔ㄤ綔 [{action}]"
                };
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
                throw new Exception($"鏈兘鎵撴崬鍑虹粷瀵瑰潗鏍囧尮閰嶇殑鎺т欢: {controlName}");
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

            throw new Exception($"鏈彂鐜板彲鐢ㄦ帶浠? [{controlName}]");
        }
        static string DoClick(IntPtr window, string[] parts)
        {
            string controlName = parts[2];
            if (controlName.Contains("<UIA_"))
            {
                var el = FindUiaVirtualControl(window, controlName);
                if (!el.Current.IsEnabled) return "ERR:控件不可用";
                try {
                    ((InvokePattern)el.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                    return $"OK:已原生虚拟点击 [{controlName}]";
                } catch {
                    var rect = el.Current.BoundingRectangle;
                    System.Drawing.Point pt = new System.Drawing.Point((int)(rect.Left + rect.Width/2), (int)(rect.Top + rect.Height/2));
                    System.Windows.Forms.Cursor.Position = pt; Thread.Sleep(50);
                    mouse_event(MOUSEEVENTF_LEFTDOWN, pt.X, pt.Y, 0, 0); Thread.Sleep(50);
                    mouse_event(MOUSEEVENTF_LEFTUP, pt.X, pt.Y, 0, 0);
                    return $"OK:已虚拟坐标点击 [{controlName}]";
                }
            }
            
            IntPtr ctrl = FindControl(window, controlName);
            
            // 鍏滃簳妯℃嫙鍧愭爣鐐瑰嚮
            GetWindowRect(ctrl, out RECT rect);
            System.Drawing.Point pt = new System.Drawing.Point(
                rect.Left + (rect.Right - rect.Left) / 2, 
                rect.Top + (rect.Bottom - rect.Top) / 2);
            System.Windows.Forms.Cursor.Position = pt;
            Thread.Sleep(50);
            mouse_event(MOUSEEVENTF_LEFTDOWN, pt.X, pt.Y, 0, 0);
            Thread.Sleep(50);
            mouse_event(MOUSEEVENTF_LEFTUP, pt.X, pt.Y, 0, 0);
            
            // 鍙戦€?BM_CLICK
            SendMessage(ctrl, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
            
            return $"OK:宸插師鐢熺偣鍑?[{controlName}]";
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
            
            return $"OK:宸茶鍐欐枃鏈?[{text}] -> [{controlName}]";
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
            if (index == CB_ERR) return $"ERR:鏈壘鍒伴€夐」 {option}";
            SendMessage(ctrl, CB_SETCURSEL, (IntPtr)index, IntPtr.Zero);
            IntPtr parent = GetParent(ctrl);
            int ctrlId = GetWindowLong(ctrl, GWL_ID);
            SendMessage(parent, WM_COMMAND, (IntPtr)((CBN_SELCHANGE << 16) | ctrlId), ctrl);
            
            return $"OK:宸查€夋嫨 [{option}]";
        }
        static string DoExists(IntPtr window, string[] parts)
        {
            if (parts[2].Contains("<UIA_")) { try { FindUiaVirtualControl(window, parts[2]); return "OK:true"; } catch { return "OK:false"; } }
            try { FindControl(window, parts[2]); return "OK:true"; }
            catch { return "OK:false"; }
        }
        static string DoIsEnabled(IntPtr window, string[] parts)
        {
            if (parts[2].Contains("<UIA_")) { return FindUiaVirtualControl(window, parts[2]).Current.IsEnabled ? "OK:true" : "OK:false"; }
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
            if (dialog == IntPtr.Zero) return $"ERR:绛夊緟寮圭獥瓒呮椂 [{dialogTitle}]";
            return $"OK:寮圭獥鍑虹幇";
        }

        static string DoFocus(IntPtr window, string[] parts)
        {
            IntPtr ctrl = FindControl(window, parts[2]);
            SetFocus(ctrl);
            return $"OK:宸蹭娇鎺т欢鑾峰緱鐒︾偣";
        }

        // =====================================================
        // UIA Fallback: 瀵逛簬缃戞牸杩欑楂樺害铏氭嫙鍖栨棤鍙ユ焺鐨勮璁★紝鍊熷姪 UIA 瑙ｆ瀽琛屽垪琛?        // =====================================================
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
            try
            {
                var root = AutomationElement.FromHandle(parentHwnd);
                var children = root.FindAll(TreeScope.Children, Condition.TrueCondition);
                int index = 1;
                foreach (AutomationElement el in children)
                {
                    if (el.Current.ControlType != ControlType.Button && el.Current.ControlType != ControlType.MenuItem) continue;
                    
                    string text = el.Current.Name ?? "";
                    string magicId = $"<UIA_{parentMagicId}_BTN{index}>";
                    bool enabled = el.Current.IsEnabled;
                    var rect = el.Current.BoundingRectangle;
                    int w = (int)rect.Width;
                    string displayRect = w > 0 ? $"[{(int)rect.Left},{(int)rect.Top}宽:{w}]" : "";
                    
                    writer.WriteLine($"UIA_Button|{depth}|{magicId}|{text}|{displayRect}|{(enabled ? "1" : "0")}");
                    index++;
                }
            }
            catch { }
        }

        static AutomationElement FindUiaVirtualControl(IntPtr window, string controlName)
        {
            var uiaMatch = System.Text.RegularExpressions.Regex.Match(controlName, @"<UIA_([A-Za-z0-9_]+?)(\d+)_BTN(\d+)>");
            if (!uiaMatch.Success) throw new Exception("无效的虚拟按键格式");
            
            string parentMagic = $"<{uiaMatch.Groups[1].Value}{uiaMatch.Groups[2].Value}>";
            int btnIndex = int.Parse(uiaMatch.Groups[3].Value);
            
            IntPtr parentHwnd = FindControl(window, parentMagic);
            if (parentHwnd == IntPtr.Zero) throw new Exception($"找不到宿主工具栏: {parentMagic}");
            
            var root = AutomationElement.FromHandle(parentHwnd);
            var children = root.FindAll(TreeScope.Children, Condition.TrueCondition);
            int curIdx = 1;
            foreach (AutomationElement el in children)
            {
                if (el.Current.ControlType != ControlType.Button && el.Current.ControlType != ControlType.MenuItem) continue;
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
                
                string displayRect = visible && w > 0 ? $"[{rect.Left},{rect.Top}瀹?{w}]" : (visible ? "" : "{闅恾");
                
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
                if (cls.IndexOf("toolbar", StringComparison.OrdinalIgnoreCase) >= 0 || cls.IndexOf("msvb_lib_toolbar", StringComparison.OrdinalIgnoreCase) >= 0)
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

