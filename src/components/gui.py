import flet as ft


class GUI(ft.Container):
  def __init__(self, sidebar, mainwindow, contextlist, rightbar):
    super().__init__()
    # Components
    self.contextlist = contextlist

    self.padding = 0
    self.bgcolor = ft.colors.BACKGROUND
    # self.bgcolor = ft.colors.SECONDARY_CONTAINER
    self.expand = True
    self.content = ft.Row(
      [
        sidebar,
        ft.Container(
          ft.Row([
            # contextlist,
            # self.divider() if contextlist else None,
            mainwindow
          ], spacing=0),
          padding=0,
          # border=ft.border.only(top=ft.BorderSide(1, ft.colors.PRIMARY), left=ft.BorderSide(1, ft.colors.PRIMARY)),
          bgcolor=self.bgcolor,
          expand=True
        ),
        rightbar,
      ],
      spacing=0,
    )
  
  def divider(self):
    def move_vertical_divider(e: ft.DragUpdateEvent):
      if (e.delta_x > 0 and self.contextlist.content.width < 500) or (e.delta_x < 0 and self.contextlist.content.width > 200):
        self.contextlist.content.width += e.delta_x
        self.contextlist.content.update()

    def show_draggable_cursor(e: ft.HoverEvent):
      e.control.mouse_cursor = ft.MouseCursor.RESIZE_LEFT_RIGHT
      e.control.update()

    return ft.GestureDetector(
      content=ft.VerticalDivider(width=4, color=ft.colors.PRIMARY),
      drag_interval=10,
      on_pan_update=move_vertical_divider,
      on_hover=show_draggable_cursor,
    )
