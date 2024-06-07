import json
import pystray
import flet as ft
from PIL import Image
from time import sleep
from flet import Page, Text

from lang import LangLoader
from frontend.components.gui_constants import *
from frontend.components.sidebar import Sidebar
from frontend.components.titlebar import TitleBar
from frontend.components.chat_data import ChatData
from frontend.components.mainwindow import MainWindow
from frontend.components.contextlist import ContextList
from logic.agent import LLMAgent, ChatDataManager, session, select, Settings


class Subconscious(ft.Container):
  """ Setup the layout for the subcscious app UI """
  def __init__(self, page, chat):
    super().__init__(self)
    """ Should only call expand on containers """
    self.chat = ChatDataManager()
    self.llm = LLMAgent()
    self.p = page

    # Load user settings
    # Set locale
    locale = session.scalars(select(Settings).filter_by(key='locale')).first()
    if locale:
      self.p.locale_configuration.current_locale = ft.Locale(locale.value['lang'], locale.value['loc'])
      self.lang = LangLoader(self.initialize_ui, locale.value['lang'])
    else:
      self.lang = LangLoader(self.initialize_ui, "en")
    
    # Tray Icon
    tray_icon = session.scalars(select(Settings).filter_by(key='tray_icon')).first()
    self.show_tray = tray_icon.value['show']
    if self.show_tray:
      self.initialize_tray_icon()

    # Initialize the UI
    self.initialize_ui()

  def initialize_ui(self):
    """ Initialize the UI components """
    # UI components
    self.mainwindow = MainWindow(self.chat, self.lang, self.llm)
    self.contextlist = ContextList(self.chat, self.lang, self.mainwindow)
    self.sidebar = Sidebar(self.p, self.chat, self.lang, self.contextlist, self.mainwindow)
    self.spacing = 0
    self.padding = 0
    self.bgcolor = app_bg_colour
    self.expand = True 

    # Layout
    self.content = ft.Row(
      [
        self.sidebar,
        ft.Container(
          ft.Row([
            self.contextlist,
            ft.VerticalDivider(width=1, color=app_bg_colour),
            self.mainwindow
          ], spacing=0),
          padding=0, border_radius=ft.BorderRadius(10, 0, 0, 0),
          border=ft.border.only(top=ft.BorderSide(1, outline_colour), left=ft.BorderSide(1, outline_colour)),bgcolor=app_bg_colour, expand=True
        )
      ],
      spacing=0,
    )
  
  def default_tray_option(self, icon, query):
    self.p.window_skip_task_bar = False
    self.p.window_minimized = False
    self.p.update()

  def initialize_tray_icon(self):
    self.p.window_prevent_close = True # Tray icon persistance
    self.p.on_window_event = self.on_window_event

    self.tray_icon = pystray.Icon(
      name="Subconscious",
      icon=Image.open("./frontend/assets/icons/flet.png"),
      title="Subconscious",
      menu=pystray.Menu(
        pystray.MenuItem("Open Subconscious", self.default_tray_option, default=True),
        pystray.MenuItem("Exit", self.tray_exit)
      ),
      visible=True,
    )
    self.tray_icon.run_detached()
  
  def tray_exit(self, icon, query):
    icon.stop()
    self.p.window_destroy()
  
  def on_window_event(self, e):
    if e.data == "close" and self.show_tray:
      self.p.window_skip_task_bar = True
      self.p.window_minimized = True
      self.p.update()


def main(page: Page):
  """ Main function to initiate subconscious gui """
  # Flet page config
  page.padding = 0
  page.spacing = 0
  page.locale_configuration
  page.window_min_width = 513
  page.bgcolor = app_bg_colour
  page.window_min_height = 508
  page.window_frameless = False
  page.window_title_bar_hidden = True
  page.theme_mode = ft.ThemeMode.DARK
  page.theme = ft.Theme(color_scheme_seed=app_bg_colour)
  page.title = "Subconscious LLM Agent & Chat"
  page.font = { "calibri": "https://fonts.googleapis.com/css2?family=Calibri:wght@400;700&display=swap" }
  
  # Language config
  page.locale_configuration = ft.LocaleConfiguration(
    current_locale=ft.Locale("en", "UK"),
    supported_locales=[
      ft.Locale("en", "UK"),
      ft.Locale("zh", "CN"),
      ft.Locale("es", "ES"),
      # ft.Locale("en", "US"),
      # ft.Locale("fr", "FR"),
      # ft.Locale("de", "DE"),
      # ft.Locale("it", "IT"),
      # ft.Locale("pt", "PT"),
      # ft.Locale("nl", "NL"),
      # ft.Locale("pl", "PL"),
      # ft.Locale("ru", "RU"),
      # ft.Locale("ja", "JP"),
      # ft.Locale("ko", "KR"),
    ],
  )

  app = Subconscious(page=page, chat=LLMAgent())
  page.add(TitleBar())
  page.add(app)

  page.on_error = lambda e: print("Page error:", e.data)
  
ft.app(target=main)
