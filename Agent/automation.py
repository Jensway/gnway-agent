import time
import win32gui
import pywinauto
from pywinauto import Desktop
from pywinauto.findwindows import find_windows, find_elements

def get_window(title_pattern, timeout=0):
    start = time.time()
    while True:
        try:
            # 放弃难以调试的内部正则 search，使用最朴素稳定的 substring 判断，完全 1:1 还原 C#
            desktop = Desktop(backend="uia")
            for w in desktop.windows(visible_only=False):
                # 获取该窗口的名称
                name = w.element_info.name or ""
                if title_pattern in name:
                    # 匹配成功！由于我们在使用 UIA，部分弹窗可能是 IsOffscreen 但肉眼可见的
                    w_wrap = w.wrapper_object()
                    if not w_wrap.is_offscreen:
                        hwnd = w.element_info.handle
                        app = pywinauto.Application(backend="uia").connect(handle=hwnd)
                        return app.window(handle=hwnd)
        except Exception:
            pass
            
        if time.time() - start >= timeout:
            return None
        time.sleep(0.3)

def find_control(window_wrapper, control_name, control_type=None, parent_name=None, index=0):
    """
    由于 C# 版本使用了 Win32 + UIA 双引擎降维枚举，为了保持高速，我们:
    1. 优先使用 window_wrapper.child_window(...) (pywinauto 自带高速查找)
    2. 如果找不到，降级到深度遍历
    """
    kwargs = {"auto_id" if control_name.isalnum() else "title": control_name}
    if control_type:
        kwargs["control_type"] = control_type
        
    # 如果指定了父级，先找到父级
    root = window_wrapper
    if parent_name:
        try:
            root = window_wrapper.child_window(title=parent_name).wrapper_object()
        except:
            # 兼容：有时候 parent_name 可能是 auto_id
            root = window_wrapper.child_window(auto_id=parent_name).wrapper_object()

    # 在 root 下寻找目标
    # child_window 是延迟计算的，直到 wrapper_object 被调用
    try:
        ctrls = root.descendants(**kwargs)
        if not ctrls:
            # 放大搜索范围尝试纯名称包含
            kwargs.pop("auto_id", None)
            kwargs.pop("title", None)
            import re
            safe_ctrl_name = re.escape(control_name)
            kwargs["title_re"] = f".*{safe_ctrl_name}.*"
            ctrls = root.descendants(**kwargs)
            
        if ctrls and len(ctrls) > index:
            return ctrls[index]
            
        raise Exception(f"控件未找到: [{control_name}]")
    except Exception as e:
        # Fallback 到慢速深度全扫描
        all_children = root.descendants()
        matches = []
        for c in all_children:
            n = c.element_info.name
            aid = c.element_info.automation_id
            ct = c.element_info.control_type
            
            if (n == control_name or aid == control_name) and (control_type is None or ct == control_type):
                matches.append(c)
                
        if len(matches) > index:
            return matches[index]
        raise Exception(f"控件未找到: [{control_name}]" + (f" (在 [{parent_name}] 内)" if parent_name else ""))

def write_pipe_stream(pipe, text):
    import win32file
    if pipe:
        win32file.WriteFile(pipe, (text + "\n").encode('utf-8'))

def process_command(cmd_line, pipe=None):
    parts = cmd_line.split('|')
    action = parts[0].strip().lower()

    try:
        if action == "windows":
            return list_all_windows()
        if action == "snapshot":
            return do_snapshot()

        if len(parts) < 2:
            return "ERR:参数不足（格式: 动作|程序名|...）"

        app_title = parts[1]

        if action == "tree":
            depth = int(parts[2]) if len(parts) > 2 else 4
            win = get_window(app_title)
            if not win: return f"ERR:找不到窗口: {app_title}"
            return dump_tree(win, depth)

        if action == "windowexists":
            return "OK:true" if get_window(app_title) else "OK:false"
            
        win = get_window(app_title)
        if not win:
            raise Exception(f"找不到窗口: {app_title}")

        if action == "listcontrols":
            do_listcontrols_stream(win, pipe)
            return None # 已经走流式输出了

        if action == "click":        return do_click(win, parts)
        if action == "input":        return do_input(win, parts)
        if action == "scroll":       return do_scroll(win, parts)
        if action == "scrollto":     return do_scrollto(win, parts)
        if action == "wait":         return do_wait(app_title, parts)
        if action == "gettext":      return do_gettext(win, parts)
        if action == "exists":       return do_exists(win, parts)
        if action == "select":       return do_select(win, parts)
        if action == "focus":        return do_focus(win, parts)
        if action == "isenabled":    return do_isenabled(win, parts)
        if action == "popupinfo":    return do_popupinfo(win, parts)
        if action == "gridrows":     return do_gridrows(win, parts)
        
        return f"ERR:未知动作 [{action}]"
    except Exception as e:
        return f"ERR:{str(e)}"

def list_all_windows():
    desktop = Desktop(backend="uia")
    windows = desktop.windows()
    res = ["OK:当前所有顶层窗口:"]
    for w in windows:
        if w.element_info.name:
            res.append(f"  [{w.element_info.class_name}] {w.element_info.name}")
    return "\n".join(res)

def do_snapshot():
    desktop = Desktop(backend="uia")
    names = [w.element_info.name for w in desktop.windows() if w.element_info.name]
    return "OK:" + "|||".join(names)

def dump_tree(window, depth):
    # Pywinauto 的 print_control_identifiers 默认输出到 stdio，我们需要捕获它
    import io, sys
    old_stdout = sys.stdout
    sys.stdout = my_stdout = io.StringIO()
    try:
        window.print_control_identifiers(depth=depth)
    finally:
        sys.stdout = old_stdout
    return f"OK:控件树 [{window.element_info.name}]\n" + my_stdout.getvalue().strip()

def do_click(window, parts):
    control_name = parts[2]
    parentName = parts[3] if len(parts) > 3 and parts[3] else None
    index = int(parts[4]) if len(parts) > 4 else 0
    
    ctrl = find_control(window, control_name, parent_name=parentName, index=index)
    try:
        ctrl.invoke() # InvokePattern
        return f"OK:已点击 [{control_name}]"
    except:
        try:
            ctrl.select() # SelectionItemPattern
            return f"OK:已选中 [{control_name}]"
        except:
            # Fallback 到鼠标点击中心点
            rect = ctrl.rectangle()
            x, y = rect.left + rect.width() // 2, rect.top + rect.height() // 2
            pywinauto.mouse.click(button='left', coords=(x, y))
            return f"OK:已模拟点击 [{control_name}] 坐标({x},{y})"

def do_input(window, parts):
    control_name = parts[2]
    text = parts[3] if len(parts) > 3 else ""
    clear = parts[4].lower() != "false" if len(parts) > 4 else True
    
    ctrl = find_control(window, control_name)
    try:
        # 尝试 ValuePattern
        if clear:
            ctrl.set_edit_text("")
        ctrl.set_edit_text(text)
        return f"OK:已输入 [{text}] → [{control_name}]"
    except:
        # Fallback 到 SendKeys
        ctrl.set_focus()
        time.sleep(0.1)
        if clear:
            pywinauto.keyboard.send_keys("^a{VK_DELETE}")
        pywinauto.keyboard.send_keys(text, with_spaces=True)
        return f"OK:已键入 [{text}] → [{control_name}]"

def do_gettext(window, parts):
    control_name = parts[2]
    ctrl = find_control(window, control_name)
    try:
        val = ctrl.get_value()
        if val: return f"OK:{val}"
    except:
        pass
    try:
        return f"OK:{ctrl.window_text()}"
    except:
        return f"OK:{ctrl.element_info.name}"

def do_exists(window, parts):
    control_name = parts[2]
    try:
        find_control(window, control_name)
        return "OK:true"
    except:
        return "OK:false"

def do_focus(window, parts):
    control_name = parts[2]
    ctrl = find_control(window, control_name)
    ctrl.set_focus()
    return f"OK:已聚焦 [{control_name}]"

def do_isenabled(window, parts):
    control_name = parts[2]
    ctrl = find_control(window, control_name)
    return "OK:true" if ctrl.is_enabled() else "OK:false"

def do_wait(app_title, parts):
    dialog_title = parts[2]
    dialog_action = parts[3].lower() if len(parts) > 3 else "none"
    timeout = int(parts[4]) if len(parts) > 4 else 15
    
    dialog = get_window(dialog_title, timeout=timeout)
    if not dialog:
        raise Exception(f"等待弹窗超时: {dialog_title}")
        
    btn_map = {
        "confirm": "确定", "cancel": "取消", "yes": "是", 
        "no": "否", "close": "关闭"
    }
    btn_name = btn_map.get(dialog_action, "")
    
    if btn_name:
        time.sleep(0.2)
        btn = find_control(dialog, btn_name)
        btn.invoke()
        return f"OK:弹窗 [{dialog_title}] 已点击 [{btn_name}]"
        
    return f"OK:弹窗 [{dialog_title}] 已出现"

def do_select(window, parts):
    control_name = parts[2]
    option = parts[3]
    ctrl = find_control(window, control_name)
    try:
        ctrl.expand()
    except:
        pass
    time.sleep(0.2)
    # 查找子选项并选中
    item = find_control(ctrl, option)
    item.select()
    return f"OK:已选择 [{option}] in [{control_name}]"

def do_scroll(window, parts):
    # pywinauto 中直接提供 scroll API 不是很统一，我们可以用原生 wheel 机制或者 UIA 的 ScrollPattern
    control_name = parts[2]
    direction = parts[3].lower() if len(parts) > 3 else "down"
    amount = parts[4].lower() if len(parts) > 4 else "small"
    
    ctrl = find_control(window, control_name)
    if not hasattr(ctrl, 'scroll'):
        # Fallback to wheel
        rect = ctrl.rectangle()
        x, y = rect.left + rect.width()//2, rect.top + rect.height()//2
        clicks = -1 if direction in ['down', 'right'] else 1
        clicks *= (3 if amount == 'large' else 1)
        pywinauto.mouse.scroll(coords=(x, y), wheel_dist=clicks)
        return f"OK:已模拟鼠标滚轮滚动 [{direction}]"
        
    # 如果支持原生 scroll
    try:
        v_action = "line_down" if direction == "down" else ("line_up" if direction == "up" else None)
        h_action = "line_right" if direction == "right" else ("line_left" if direction == "left" else None)
        if direction == "top": ctrl.set_scroll_percent(vertical_percent=0)
        if direction == "bottom": ctrl.set_scroll_percent(vertical_percent=100)
        if v_action: ctrl.scroll(direction=v_action, amount=amount)
        if h_action: ctrl.scroll(direction=h_action, amount=amount)
        return f"OK:已滚动 [{direction}]"
    except Exception as e:
        return f"ERR:滚动失败: {str(e)}"

def do_scrollto(window, parts):
    container_name = parts[2]
    target_name = parts[3]
    
    # 查找本身会触发 element 显示
    target = find_control(window, target_name)
    try:
        # 尝试 UIA 原生 scroll_into_view
        target.set_focus() 
        return f"OK:已滚动并聚焦到 [{target_name}]"
    except Exception as e:
        return f"ERR:目标控件不支持或聚焦失败: {str(e)}"

def do_popupinfo(window, parts):
    texts = []
    buttons = []
    
    def walk(ctrl):
        ct = ctrl.element_info.control_type
        name = ctrl.element_info.name or ""
        
        if ct in ["Text", "Custom"] and name.strip():
            texts.append(name.strip())
        if ct == "Button" and name.strip():
            buttons.append(name.strip())
            
        for child in ctrl.children():
            walk(child)
            
    walk(window)
    title = window.element_info.name or ""
    if title in texts: texts.remove(title)
    
    body = " ".join([t for t in texts if t])
    btns = ",".join([b for b in buttons if b])
    return f"OK:title={title}|body={body}|buttons={btns}"

def do_gridrows(window, parts):
    control_name = parts[2]
    max_rows = int(parts[3]) if len(parts) > 3 else 500
    
    grid = find_control(window, control_name)
    lines = ["OK:"]
    row_idx = 0
    
    for child in grid.children():
        if row_idx >= max_rows: break
        ct = child.element_info.control_type
        if ct in ["DataItem", "ListItem", "TreeItem"]:
            cols = []
            for cell in child.children():
                try: cols.append(cell.window_text() or cell.element_info.name or "")
                except: cols.append(cell.element_info.name or "")
            if not cols:
                cols.append(child.element_info.name or "")
            lines.append("\t".join(cols))
            row_idx += 1
            
    return "\n".join(lines)

def do_listcontrols_stream(window, pipe):
    write_pipe_stream(pipe, "OK:")
    
    def is_key_control(ct, name):
        if ct in ["Button", "CheckBox", "RadioButton", "Edit", "ComboBox", "List", "DataGrid", "Table", "Tree", "ScrollBar", "TabItem", "MenuItem", "Slider", "Spinner", "Thumb"]:
            return True
        if ct in ["Text", "Document"]: return bool(name and name.strip())
        return bool(name and name.strip()) # Containers with names
        
    try:
        # 使用底层 win32 枚举极大加速。直接使用 EnumChildWindows 平铺句柄
        def callback(hwnd, extra):
            try:
                # 转为 pywinauto 对象
                w = pywinauto.controls.uiawallet.UIAElementInfo(hwnd)
                ct = w.control_type
                name = w.name or ""
                enabled = 1 if w.enabled else 0
                if is_key_control(ct, name):
                    write_pipe_stream(pipe, f"{ct}|{name}|{enabled}")
            except:
                pass
            return True
            
        import win32gui
        win32gui.EnumChildWindows(window.handle, callback, None)
    except Exception as e:
        write_pipe_stream(pipe, f"ERR:获取控件树异常: {str(e)}")
