# -*- coding: utf-8 -*-
import re

filepath = 'Agent/Agent.cs'
with open(filepath, 'r', encoding='utf-8-sig') as f:
    code = f.read()

# 1. Replace EnumerateUIAVirtualButtons method
old_enum = '''        static void EnumerateUIAVirtualButtons(IntPtr parentHwnd, string parentMagicId, TextWriter writer, int depth)
        {
            try
            {
                var root = AutomationElement.FromHandle(parentHwnd);
                Console.WriteLine($"  [UIA扫描] 正在扫描 {parentMagicId} 的虚拟子控件...");
                
                // 先尝试 Descendants 深度搜索，再回退到 Children
                var allElements = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
                if (allElements.Count == 0)
                    allElements = root.FindAll(TreeScope.Children, Condition.TrueCondition);
                
                Console.WriteLine($"  [UIA扫描] 发现 {allElements.Count} 个UIA元素");
                int index = 1;
                foreach (AutomationElement el in allElements)
                {
                    var ct = el.Current.ControlType;
                    // 接受所有可交互的控件类型
                    bool isInteractive = (ct == ControlType.Button || ct == ControlType.MenuItem ||
                                          ct == ControlType.SplitButton || ct == ControlType.Custom ||
                                          ct == ControlType.ToolBar || ct == ControlType.Tab ||
                                          ct == ControlType.TabItem || ct == ControlType.Hyperlink ||
                                          ct == ControlType.Image);
                    if (!isInteractive) continue;
                    // 跳过工具栏容器本身
                    if (ct == ControlType.ToolBar) continue;
                    
                    string text = el.Current.Name ?? "";
                    string magicId = $"<UIA_{parentMagicId}_BTN{index}>";
                    bool enabled = el.Current.IsEnabled;
                    var rect = el.Current.BoundingRectangle;
                    int w = (int)rect.Width;
                    string displayRect = w > 0 ? $"[{(int)rect.Left},{(int)rect.Top} Width:{w}]" : "";
                    string typeName = ct == ControlType.Button ? "UIA_Button" :
                                     ct == ControlType.MenuItem ? "UIA_MenuItem" : $"UIA_{ct.ProgrammaticName.Replace(\\"ControlType.\\", \\"\\")}";
                    
                    writer.WriteLine($"{typeName}|{depth}|{magicId}|{text}|{displayRect}|{(enabled ? \\"1\\" : \\"0\\")}");
                    Console.WriteLine($"    [{index}] {typeName}: {text} {displayRect}");
                    index++;
                }
                if (index == 1) Console.WriteLine($"  [UIA扫描] 未发现可交互的虚拟按钮");
            }
            catch (Exception ex) { Console.WriteLine($"  [UIA扫描异常] {ex.Message}"); }
        }'''

new_enum = '''        static void EnumerateUIAVirtualButtons(IntPtr parentHwnd, string parentMagicId, TextWriter writer, int depth)
        {
            // 策略1: Win32 原生工具栏消息 TB_BUTTONCOUNT
            int tbCount = (int)SendMessage(parentHwnd, TB_BUTTONCOUNT, IntPtr.Zero, IntPtr.Zero);
            if (tbCount > 0)
            {
                Console.WriteLine($"  [Win32工具栏] {parentMagicId} 发现 {tbCount} 个原生按钮");
                for (int i = 0; i < tbCount; i++)
                {
                    StringBuilder sbText = new StringBuilder(260);
                    SendMessage(parentHwnd, TB_GETBUTTONTEXTW, (IntPtr)i, sbText);
                    string text = sbText.ToString();
                    if (string.IsNullOrEmpty(text)) text = $"Button{i + 1}";
                    string magicId = $"<TB_{parentMagicId}_BTN{i + 1}>";
                    writer.WriteLine($"TB_Button|{depth}|{magicId}|{text}||1");
                    Console.WriteLine($"    [TB_{i + 1}] {text}");
                }
                return;
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
                        for (int i = 0; i < obtained; i++)
                        {
                            try
                            {
                                string name = "";
                                try { name = acc.get_accName(children[i]) ?? ""; } catch { }
                                string role = "";
                                try { role = acc.get_accRole(children[i])?.ToString() ?? ""; } catch { }
                                if (!string.IsNullOrWhiteSpace(name) || role.Contains("43") || role.Contains("push"))
                                {
                                    string magicId = $"<MSAA_{parentMagicId}_BTN{btnIdx}>";
                                    writer.WriteLine($"MSAA_Button|{depth}|{magicId}|{name}||1");
                                    Console.WriteLine($"    [MSAA_{btnIdx}] {name} role={role}");
                                    btnIdx++;
                                }
                            }
                            catch { }
                        }
                        if (btnIdx > 1) return;
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
                    var ct = el.Current.ControlType;
                    if (ct == ControlType.ToolBar || ct == ControlType.Pane || ct == ControlType.Window) continue;
                    string text = el.Current.Name ?? "";
                    string magicId = $"<UIA_{parentMagicId}_BTN{index}>";
                    bool enabled = el.Current.IsEnabled;
                    var rect = el.Current.BoundingRectangle;
                    int w = (int)rect.Width;
                    string displayRect = w > 0 ? $"[{(int)rect.Left},{(int)rect.Top} Width:{w}]" : "";
                    string typeName = $"UIA_{ct.ProgrammaticName.Replace(\\"ControlType.\\", \\"\\")}";
                    writer.WriteLine($"{typeName}|{depth}|{magicId}|{text}|{displayRect}|{(enabled ? \\"1\\" : \\"0\\")}");
                    Console.WriteLine($"    [{index}] {typeName}: {text}");
                    index++;
                }
                if (index == 1) Console.WriteLine($"  [UIA] 无虚拟按钮");
            }
            catch (Exception ex) { Console.WriteLine($"  [UIA异常] {ex.Message}"); }
        }'''

if old_enum in code:
    code = code.replace(old_enum, new_enum)
    print("EnumerateUIAVirtualButtons replaced OK")
else:
    print("ERROR: could not find EnumerateUIAVirtualButtons to replace")

# 2. Replace FindUiaVirtualControl to support TB_/MSAA_ prefixes
old_find = '''        static AutomationElement FindUiaVirtualControl(IntPtr window, string controlName)
        {
            var uiaMatch = System.Text.RegularExpressions.Regex.Match(controlName, @"<UIA_([A-Za-z0-9_]+?)(\\d+)_BTN(\\d+)>");'''

new_find = '''        static AutomationElement FindUiaVirtualControl(IntPtr window, string controlName)
        {
            var uiaMatch = System.Text.RegularExpressions.Regex.Match(controlName, @"<(?:UIA|MSAA|TB)_([A-Za-z0-9_]+?)(\\d+)_BTN(\\d+)>");'''

if old_find in code:
    code = code.replace(old_find, new_find)
    print("FindUiaVirtualControl regex updated OK")
else:
    print("ERROR: could not find FindUiaVirtualControl")

# 3. Add P/Invoke declarations before RECT struct
old_pinvoke_area = '''        const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        const uint MOUSEEVENTF_LEFTUP = 0x0004;

        struct RECT'''

new_pinvoke_area = '''        const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        const uint MOUSEEVENTF_LEFTUP = 0x0004;
        const uint TB_BUTTONCOUNT = 0x0418;
        const uint TB_GETBUTTONTEXTW = 0x044B;
        const uint OBJID_CLIENT = 0xFFFFFFFC;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, StringBuilder lParam);

        [DllImport("oleacc.dll")]
        static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint dwObjectId, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppvObject);

        [DllImport("oleacc.dll")]
        static extern int AccessibleChildren([MarshalAs(UnmanagedType.Interface)] object paccContainer, int iChildStart, int cChildren, [Out] object[] rgvarChildren, out int pcObtained);

        struct RECT'''

if old_pinvoke_area in code:
    code = code.replace(old_pinvoke_area, new_pinvoke_area)
    print("P/Invoke declarations added OK")
else:
    print("ERROR: could not find P/Invoke insertion point")

# 4. Fix the menu text
code = code.replace('Agent 本地懒人调试菜单', 'Agent 本地调试菜单')
print("Menu text fixed")

# Save with UTF-8 BOM
with open(filepath, 'w', encoding='utf-8-sig') as f:
    f.write(code)

print("All done!")
