import flet as ft


class NavIcon(ft.Container):
  def __init__(self, show_window, icon, tooltip):
    super().__init__()
    self.content=ft.IconButton(
      icon,
      on_click=lambda _: show_window(),
      tooltip=tooltip,
      padding=0,
      style=ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=3))
    )
    self.padding = ft.padding.only(4, 0, 4, 4)


class Rightbar(ft.Column):
  def __init__(self, page, show_window):
    super().__init__()
    self.p = page
    self.padding = 0
    self.width = 48
    self.spacing = 0
    self.alignment = "end"
    self._button_padding = ft.padding.only(4, 0, 4, 4)

    self.top_nav = ft.Column([
        NavIcon(show_window, ft.icons.INFO_OUTLINE, "About"),
      ], expand=4, spacing=0)

    self.controls = [
      # Top navigation
      self.top_nav,
    ]
