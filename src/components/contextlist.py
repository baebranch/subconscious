import flet as ft
from datetime import datetime, timezone


def on_hover(e):
  if e.data == "true":
    e.control.bgcolor = ft.colors.SECONDARY_CONTAINER
  else:
    e.control.bgcolor = receiver_message_bubble_bg_colour
  e.page.update()

class ContextItem(ft.Container):
  def __init__(self, chat):
    super().__init__()
    self.chat = chat
    self.spacing = 0
    self.padding = 10
    # self.height = 75
    self.on_hover = on_hover
    # self.on_click = 
    self.border_radius = 5
    self.bgcolor = receiver_message_bubble_bg_colour
    self.last_updated = datetime.now(timezone.utc).astimezone().isoformat()
    self.content = ft.Column([
      ft.Row([
        ft.Text('Chat Title', size=14, weight=ft.FontWeight.BOLD,
        ),
        ft.Text(self.last_updated, size=12),
      ]),
      ft.Row([
        ft.Text('This is a test chat snippet', color=ft.colors.WHITE, size=14, 
        ),
      ]),
    ], 
    spacing=5
    )


class SettingItem(ft.TextButton):
  def __init__(self, chat, toggle_func, setting):
    super().__init__()
    self.chat = chat
    self.style = ft.ButtonStyle(
      shape=ft.RoundedRectangleBorder(radius=5),
      padding=ft.padding.only(10, 10, 10, 10),
    )
    self.setting = setting
    self.on_click = self.highlight
    self.toggle_func = toggle_func

    self.content = ft.Column([
      ft.Row([
        ft.Text(setting['title'], size=14, weight=ft.FontWeight.W_500, color=ft.colors.WHITE, overflow=ft.TextOverflow.ELLIPSIS, expand=True),
      ], spacing=10),
    ], spacing=5)
  
  def highlight(self, e):
    self.toggle_func(self)
    self.style = ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=5), bgcolor=ft.colors.SECONDARY_CONTAINER)
    self.page.update()
  

class ThreadItem(ft.TextButton):
  def __init__(self, chat, toggle_func):
    super().__init__()
    self.chat = chat
    self.style = ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=5))
    self.last_updated = chat['updated_at'].astimezone()
    self.on_click = self.highlight
    self.toggle_func = toggle_func

    self.content = ft.Column([
      ft.Row([
        ft.Text(self.chat['title'], size=14, weight=ft.FontWeight.W_500, overflow=ft.TextOverflow.ELLIPSIS, tooltip=self.chat['title'], expand=True),
        ft.Text(self.render_time(), size=12, weight=ft.FontWeight.W_100, text_align=ft.TextAlign.RIGHT, tooltip=self.render_datetime_tooltip())
      ], spacing=10),
      ft.Text(self.chat['description'], size=14, weight=ft.FontWeight.W_100, max_lines=2, overflow=ft.TextOverflow.ELLIPSIS, tooltip=self.chat['description'])
    ], spacing=5)
  
  def highlight(self, e):
    self.toggle_func(self)
    self.style = ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=5), bgcolor=ft.colors.SECONDARY_CONTAINER)
    self.page.update()
  
  def render_time(self):
    """ Returns a human readable time string, adjusted for how long ago the message was sent. """
    now = datetime.now().astimezone()

    # Get the time difference
    diff = now - self.last_updated

    # If the message was sent today, return the time
    if diff.days == 0:
      return self.last_updated.strftime("%H:%M")
    # If the message was sent yesterday, return "Yesterday"
    elif diff.days == 1:
      return "Yesterday"
    # If the message was sent this week, return the day of the week (e.g. "Monday")
    elif diff.days < 7:
      return self.last_updated.strftime("%A")
    # Otherwise, return the date
    else:
      return self.last_updated.strftime("%d/%m/%Y")
  
  def render_datetime_tooltip(self):
    """ Returns a tooltip string for the message, showing the full date and time. """
    return self.last_updated.strftime("%d/%m/%Y %H:%M")
  

class ContextList(ft.Container):
  def __init__(self, lang, mainwindow):
    super().__init__()
    self.l = lang
    self.max_width = 500
    self.min_width = 200
    self.active_thread = None
    self.active_setting = None
    self.mainwindow = mainwindow
    self.bgcolor = ft.colors.BACKGROUND
    self.padding = ft.padding.only(15, 10, 15, 10)
    self.border_radius = ft.BorderRadius(10, 0, 0, 0)
    
    self.context_list = ft.ListView(
      spacing=10,
      auto_scroll=True,
    )

    # Add the context items to the list view
    # for context in self.chat.chats:
    #   self.context_list.controls.append(ThreadItem(context, self.thread_toggle))

    self.threads = ft.Column([
        ft.Row([
          ft.Text(self.l.contextlist.title, size=20, weight=ft.FontWeight.BOLD),
          ft.Container(content=ft.IconButton(ft.icons.ADD_COMMENT_OUTLINED, on_click=self.new_chat, style=ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=3)), padding=0, width=34, height=34), padding=0),
        ]),
        self.context_list,
      ],
      spacing = 10,
      width=380,
    )

    # Add the settings items to the list view
    self.settings = ft.Column([
        ft.Row([
          ft.Text(self.l.contextlist.settings, size=20, weight=ft.FontWeight.BOLD),
        ]),
        # SettingItem(self.chat, self.setting_toggle, {"title": "Models", "main": "models"}),
        # SettingItem(self.chat, self.setting_toggle, {"title": "Workspaces", "main": "workspaces"}),
        ft.Checkbox(
          label="Show Tray Icon",
          # value=list(filter(lambda x: x.key == 'tray_icon', self.chat.settings))[0].value.get('show'),
          on_change=self.toggle_tray_icon,
          shape=ft.RoundedRectangleBorder(radius=3)
        ),
      ],
      spacing = 10,
      width=380,
    )

    # Add the settings items to the list view
    self.basic = ft.Column([
        # ft.Row([
        #   ft.Text(self.l.contextlist.settings, size=20, weight=ft.FontWeight.BOLD),
        # ]),
        # # SettingItem(self.chat, self.setting_toggle, {"title": "Models", "main": "models"}),
        # # SettingItem(self.chat, self.setting_toggle, {"title": "Workspaces", "main": "workspaces"}),
        # ft.Checkbox(
        #   label="Show Tray Icon",
        #   # value=list(filter(lambda x: x.key == 'tray_icon', self.chat.settings))[0].value.get('show'),
        #   on_change=self.toggle_tray_icon,
        #   shape=ft.RoundedRectangleBorder(radius=3)
        # ),
      ],
      spacing = 10,
      width=380,
    )

    self.content = self.basic
    # self.content = ft.Column([], spacing=0, width=0)

  def load_contexts(self, chats):
    """ Load the context items into the context list """
    self.context_list.controls = []
    for chat in chats:
      self.context_list.controls.append(ThreadItem(chat, self.thread_toggle))

  def new_chat(self, e):
    print('New chat')
  
  def show_threads(self, e):
    self.content = self.threads
    self.mainwindow.show_thread()
    # self.page.update()
  
  def show_settings(self, e):
    self.content = self.settings
    self.mainwindow.show_setting()
    # self.page.update()
  
  def toggle_tray_icon(self, e):
    """ Update the settings for the tray icon """
    tray = session.scalars(select(Settings).filter_by(key='tray_icon')).first()
    tray.value = {'show': e.control.value}
    session.commit()
    self.page.update()
  
  def thread_toggle(self, button):
    """ Thread toggle fuction """
    if self.active_thread:
      self.active_thread.style = ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=5))
    self.active_thread = button
    self.mainwindow.show_thread(self.active_thread.chat['id'])
    self.page.update()

  def setting_toggle(self, button):
    """ Setting toggle function """
    if self.active_setting:
      self.active_setting.style = ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=5))
    self.active_setting = button
    getattr(self.mainwindow, self.active_setting.setting['main'])()
    self.active_setting.setting
    self.page.update()
  
