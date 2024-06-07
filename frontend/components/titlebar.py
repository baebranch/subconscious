import flet as ft

from frontend.components.gui_constants import *


class TitleBar(ft.Row):
  def __init__(self):
    super().__init__()
    # TODO: Add no-connection icon
    self.spacing = 0
    self.height = 40
    self.res_max = ft.Image(src="icons/maximize_light.svg", width=12, height=12)
    self.controls = [
      ft.WindowDragArea(
        ft.Container(
          ft.Text(
            "Subconscious"
          ),
          bgcolor=app_bg_colour,
          padding=ft.padding.only(10, 10, 10, 10),
        ),
        expand=True,
      ),
      ft.FilledButton(
        content=ft.Row([ft.Image(src="icons/minimize_light.svg", width=12, height=12)]),
        on_click=lambda _: self.minimize(_),
        style=ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=0), bgcolor=app_bg_colour, padding=ft.padding.only(14, 14, 14, 14)),
        width=40, height=40, tooltip="Minimize"
      ),
      ft.FilledButton(
        content=ft.Row([
          self.res_max,
        ]),
        on_click=lambda _: self.toggle(_),
        style=ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=0), bgcolor=app_bg_colour, padding=ft.padding.only(14, 14, 14, 14)),
        width=40, height=40, tooltip="Maximize"
      ),
      ft.FilledButton(
        content=ft.Row([ft.Image(src="icons/close_light.svg", width=12, height=12)]),
        on_click=lambda _: self.page.window_close(),
        style=ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=0), bgcolor=app_bg_colour, padding=ft.padding.only(14, 14, 14, 14), overlay_color=ft.colors.RED),
        width=40, height=40, tooltip="Close"
      ),
    ]
  
  def minimize(self, _):
    """ Minimize the window """
    self.page.window_minimized = True
    self.page.update()
  
  def toggle(self, _):
    """ Toggle between maximized and window mode """
    if self.page.window_maximized:
      self.page.window_maximized = False
      self.res_max.src = "icons/maximize_light.svg"
      self.res_max.tooltip = "Maximize"
    else:
      self.page.window_maximized = True
      self.res_max.src = "icons/restore_light.svg"
      self.res_max.tooltip = "Restore"
    self.page.update()
