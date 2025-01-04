import flet as ft
import ctypes
import win32gui
import win32con

def set_app_icon(hwnd, icon_path):
  hicon = win32gui.LoadImage(None, icon_path, win32con.IMAGE_ICON, 0, 0, win32con.LR_LOADFROMFILE | win32con.LR_DEFAULTSIZE)
  win32gui.SendMessage(hwnd, win32con.WM_SETICON, win32con.ICON_SMALL, hicon)
  win32gui.SendMessage(hwnd, win32con.WM_SETICON, win32con.ICON_BIG, hicon)

def main(page: ft.Page):
  # Set the AppUserModelID for the taskbar icon
  myappid = u'mycompany.myproduct.subproduct.version'  # Arbitrary string
  ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID(myappid)

  # Set custom icon for the window and taskbar
  icon_path = 'favicon.ico'  # Replace with the path to your custom icon
  hwnd = win32gui.GetForegroundWindow()
  set_app_icon(hwnd, icon_path)

  # Flet UI code here
  page.add(ft.Text("Hello, Flet!"))

ft.app(target=main)

# import flet as ft
# import ctypes
# import win32gui
# import win32con

# def set_app_icon(hwnd, icon_path):
#     hicon = win32gui.LoadImage(None, icon_path, win32con.IMAGE_ICON, 0, 0, win32con.LR_LOADFROMFILE | win32con.LR_DEFAULTSIZE)
#     win32gui.SendMessage(hwnd, win32con.WM_SETICON, win32con.ICON_SMALL, hicon)
#     win32gui.SendMessage(hwnd, win32con.WM_SETICON, win32con.ICON_BIG, hicon)

# def main(page: ft.Page):
#     # Set the AppUserModelID for the taskbar icon
#     myappid = u'mycompany.myproduct.subproduct.version'  # Arbitrary string
#     ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID(myappid)

#     # Set custom icon for the window and taskbar
#     icon_path = 'path_to_your_custom_icon.ico'  # Replace with the path to your custom icon
#     hwnd = page.window.native_window_handle
#     set_app_icon(hwnd, icon_path)

#     # Flet UI code here
#     page.add(ft.Text("Hello, Flet!"))

# ft.app(target=main)
