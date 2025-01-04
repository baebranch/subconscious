import flet as ft


class TitleBar(ft.Row):
  def __init__(self, page, title=None, buttons=None):
    super().__init__()
    # TODO: Add no-connection icon
    self.spacing = 0
    self.height = 40
    self.theme_init(page)
    self.res_max_button = ft.FilledButton(
        content=ft.Row([self.res_max]),
        on_click=lambda _: self.toggle(_),
        style=ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=0), bgcolor=ft.colors.BACKGROUND, padding=ft.padding.only(14, 14, 14, 14), overlay_color=ft.colors.SECONDARY_CONTAINER), 
        width=40, height=40, tooltip="Maximize"
      )
    self.controls = [
      ft.WindowDragArea(
        ft.Container(content=
          ft.Row([
            ft.Image(
              src="./assets/icon.png",
              width=20, height=20,
              color=ft.colors.PRIMARY,
            ),
            ft.Container(content=
              ft.Text(
                "Subconscious" if title is None else title,
                size=13, color=ft.colors.PRIMARY
              ),
              padding=ft.padding.only(14, -4, 0, 0)
            ),
          ], alignment="left", spacing=0, expand=True),
          bgcolor=ft.colors.BACKGROUND,
          padding=ft.padding.only(14, 10, 10, 10),
        ),
        expand=True,
      ),
      ft.FilledButton(
        content=ft.Row([self.mini]),
        on_click=lambda _: self.minimize(_),
        style=ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=0), bgcolor=ft.colors.BACKGROUND, padding=ft.padding.only(14, 14, 14, 14), overlay_color=ft.colors.SECONDARY_CONTAINER),
        width=40, height=40, tooltip="Minimize"
      ),
      self.res_max_button,
      ft.FilledButton(
        content=ft.Row([self.close]),
        on_click=lambda _: self.page.window_close(),
        style=ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=0), bgcolor=ft.colors.BACKGROUND, padding=ft.padding.only(14, 14, 14, 14), overlay_color=ft.colors.RED),
        width=40, height=40, tooltip="Close"
      ),
    ]
  
  def minimize(self, _):
    """ Minimize the window """
    self.page.window_minimized = True
    self.page.update()
  
  def theme_changed(self):
    """ Change the theme of the title bar buttons """
    if self.page.theme_mode == ft.ThemeMode.LIGHT:
      self.close.src = "./assets/close_dark.svg"
      self.mini.src = "./assets/minimize_dark.svg"
      if self.page.window.maximized:
        self.res_max.src = "./assets/restore_dark.svg"
      else:
        self.res_max.src = "./assets/maximize_dark.svg"
    else:
      self.close.src = "./assets/close_light.svg"
      self.mini.src = "./assets/minimize_light.svg"
      if self.page.window.maximized:
        self.res_max.src = "./assets/restore_light.svg"
      else:
        self.res_max.src = "./assets/maximize_light.svg"
    self.page.update()
  
  def theme_init(self, page):
    """ Change the theme of the title bar buttons """
    if page.theme_mode == ft.ThemeMode.LIGHT:
      self.close = ft.Image(src="./assets/close_dark.svg", width=12, height=12)
      self.mini = ft.Image(src="./assets/minimize_dark.svg", width=12, height=12)
      if page.window_maximized:
        self.res_max = ft.Image(src="./assets/restore_dark.svg", width=12, height=12)
      else:
        self.res_max = ft.Image(src="./assets/maximize_dark.svg", width=12, height=12)
    else:
      self.close = ft.Image(src="./assets/close_light.svg", width=12, height=12)
      self.mini = ft.Image(src="./assets/minimize_light.svg", width=12, height=12)
      if page.window_maximized:
        self.res_max = ft.Image(src="./assets/restore_light.svg", width=12, height=12)
      else:
        self.res_max = ft.Image(src="./assets/maximize_light.svg", width=12, height=12)
  
  def toggle(self, _):
    """ Toggle between maximized and window mode """
    if self.page.window.maximized:
      self.res_max_button.tooltip = "Maximize"
      self.page.window.maximized = False
      if self.page.theme_mode == ft.ThemeMode.LIGHT:
        self.res_max.src = "./assets/maximize_dark.svg"
      else:
        self.res_max.src = "./assets/maximize_light.svg"
    else:
      self.res_max_button.tooltip = "Restore"
      self.page.window.maximized = True
      if self.page.theme_mode == ft.ThemeMode.LIGHT:
        self.res_max.src = "./assets/restore_dark.svg"
      else:
        self.res_max.src = "./assets/restore_light.svg"
    self.page.update()
  