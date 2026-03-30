import win32gui
from pywinauto.uia_element_info import UIAElementInfo

def test():
    desktop = win32gui.GetDesktopWindow()
    hwnds = []
    
    def callback(hwnd, extra):
        hwnds.append(hwnd)
        return True
    win32gui.EnumChildWindows(desktop, callback, None)
    
    for hwnd in hwnds[:10]:
        try:
            w = UIAElementInfo(hwnd)
            print(f"HWND: {hwnd}, Type: {type(w.control_type)}, Val: {repr(w.control_type)}, Name: {repr(w.name)}")
        except Exception as e:
            print(f"HWND: {hwnd} Failed: {e}")

if __name__ == '__main__':
    test()
