import os
import json
import pystray
import logging
import flet as ft
from flet import Page
from PIL import Image
from time import sleep
from typing import Literal
from threading import Thread
from multiprocessing import Process

from src.lang import LangLoader
from src.components.gui import GUI
from src.utilities.dictobj import DictObj
from src.components.leftbar import Leftbar
from src.components.rightbar import Rightbar
from src.components.titlebar import TitleBar
from src.components.data_objects import Message
from src.components.main_window import MainWindow
from src.components.contextlist import ContextList


logger = logging.getLogger("subconscious")


class Subconscious:
  """ Setup the layout for the subcscious app UI """
  splash = True

  def __init__(self, title: str="Subconscious"):
    """ Should only call expand on containers """
    # Load ui settings
    self.title = title
    if os.path.exists("./data/settings.json"):
      f = open("./data/settings.json", "r")
      self.settings = json.load(f)
      f.close()
    else:
      logger.debug("Settings file not found, creating new settings file...")
      self.settings = {
        "_general": {
          "mode": "light", # light or dark
          "theme": "black", # color scheme
          "language": "en", # language
          "llm": "" # No default llm is configured
        },
        "General": {
          "tray": {
            "value": True,
            "label": "Show tray icon",
          }
        },
        "_models": [

        ],
      }
      f = open("./data/settings.json", "w")
      f.write(json.dumps(self.settings, indent=2))
      f.close()
    
    # Create settings object
    self.settings = DictObj(self.settings)

    # Initialize the UI
    self.lang = LangLoader(self.settings._general.language)
    self.__initialize_ui()
  
  # User methods
  def load(self, view: Literal['app', 'web']) -> None:
    """ Main function to initiate subconscious gui """
    def window_resized_handler(e):
      self.__titlebar.theme_changed()
      self.__leftbar.dynamic_height(e)

    def main(page: ft.Page):
      # Flet page config
      page.window.center()
      self.page = page
      page.padding = 0
      page.spacing = 0
      page.locale_configuration
      page.window.min_width = 500
      page.bgcolor = ft.colors.BACKGROUND
      page.window.min_height = 500
      page.window.width = 500
      page.window.frameless = False
      page.window.title_bar_hidden = True
      page.theme_mode = getattr(ft.ThemeMode, self.settings._general.mode.upper())
      if self.settings._general.theme == "white" and self.settings._general.mode == "dark":
        page.theme = ft.Theme(color_scheme=ft.ColorScheme(primary=ft.colors.WHITE, secondary=ft.colors.GREY, background=ft.colors.BLACK87, secondary_container=ft.colors.GREY_800, primary_container=ft.colors.GREY_800))
      elif self.settings._general.theme == "black" and self.settings._general.mode == "light":
        page.theme = ft.Theme(color_scheme=ft.ColorScheme(primary=ft.colors.BLACK, secondary=ft.colors.GREY, background=ft.colors.WHITE, secondary_container=ft.colors.GREY_300, primary_container=ft.colors.GREY_300))
      else:
        page.theme = ft.Theme(color_scheme_seed=self.settings._general.theme)
      page.title = self.title
      page.font = { "calibri": "https://fonts.googleapis.com/css2?family=Calibri:wght@400;700&display=swap" }
      page.on_resized = window_resized_handler
      # page.
      
      # Language config
      page.locale_configuration = ft.LocaleConfiguration(
        current_locale=ft.Locale("en", "UK"),
        supported_locales=[
          ft.Locale("en", "UK")
        ],
      )

      # Initialize the app, tray ion, splash screen
      if self.settings.General.tray.value:
        self.__initialize_tray_icon()

      if self.splash: page.overlay.append(self.splash_screen())
      page.add(self.__titlebar)
      page.add(self.__content)
      page.on_error = lambda e: logger.debug("Page error: %s", e.data)
      if self.splash:
        sleep(3) # Do some loading if needed
        page.overlay.pop(0)

      # Update the pages
      page.update()
      self.__mainwindow.page.update()
  
    # Load the subconscious app
    if view == 'web':
      ft.app(target=main, view=ft.AppView.FLET_APP_WEB, assets_dir="src/assets")
    elif view == 'app':
      ft.app(target=main, assets_dir="src/assets")

  def load_threads(self, threads):
    """ Load the threads into the context list """
    self.__contextlist.load_contexts(threads)
  
  def set_thread_history_loader(self, loader):
    """ Set the thread history loader"""
    self.__mainwindow.set_thread_history_loader(loader)
  
  def set_new_user_message_callback(self, callback):
    """ Set the new user message callback """
    self.__mainwindow.set_new_user_message_callback(callback)
  
  def set_llm_switcher(self, switcher):
    """ Set the LLM switcher """
    self.__switcher = switcher
  
  def set_active_thread(self, thread_id):
    """ Set the active thread """
    self.__mainwindow.set_active_thread(thread_id)
  
  def send_response(self, message, thread_id):
    """ Send a response message """
    self.__mainwindow.send_response(message, thread_id)
  
  def stream_response(self, message, thread_id):
    """ Stream a response message """
    self.__mainwindow.stream_response(message, thread_id)

  # Internal methods
  def __initialize_ui(self):
    """ Initialize the UI components """
    self.__titlebar = TitleBar(Page)
    self.__mainwindow = MainWindow(self.lang, self.settings, self.__update_llms, self.__llm_configured)
    self.__rightbar = Rightbar(Page, self.__mainwindow.show_about, self.settings, self.switch_llm)
    self.__contextlist = ContextList(self.lang, self.__mainwindow)
    self.__leftbar = Leftbar(Page, self.lang, self.__contextlist, self.__mainwindow, self.__titlebar.theme_changed, self.settings)
    self.__content = GUI(self.__leftbar, self.__mainwindow, self.__contextlist, self.__rightbar)

  def __update_llms(self):
    """ Update the LLMs menu """
    self.__rightbar.update_llms()
  
  def __llm_configured(self):
    """ Check if a LLM is configured """
    return self.__rightbar.llm_configured()
  
  def switch_llm(self, e):
    """ Calls an external function to switch the LLM model """
    return self.__switcher(e)

  def __initialize_tray_icon(self):
    self.page.window.prevent_close = True # Tray icon persistance
    self.page.window.on_event = self.__on_window_event

    self.__tray_icon = pystray.Icon(
      name="Subconscious",
      icon=Image.open("./src/assets/logo.png"),
      title="Subconscious",
      menu=pystray.Menu(
        pystray.MenuItem("Open Subconscious", self.__default_tray_option, default=True),
        pystray.MenuItem("Exit", self.__tray_exit)
      ),
      visible=True,
    )
    self.__tray_icon.run_detached()

  def __default_tray_option(self, icon, query):
    self.page.window.skip_task_bar = False
    self.page.window.minimized = False
    self.page.update()
  
  def __tray_exit(self, icon, query):
    icon.stop()
    self.page.window_destroy()
  
  def __on_window_event(self, e):
    if e.data == "close" and self.settings.General.tray.value:
      self.page.window.skip_task_bar = True
      self.page.window.minimized = True
      self.page.update()

  def splash_screen(self):
    """ Show the splash screen """
    return ft.Container(
      content=ft.Row([
        ft.Column([
          ft.Image(src="./src/assets/logo.png", width=100, height=100),
          ft.Text("Subconscious", size=25, color=ft.colors.PRIMARY),
        ], alignment="center", horizontal_alignment="center", spacing=0, expand=True),
      ], alignment="center", vertical_alignment="center", spacing=0, expand=True),
      bgcolor=ft.colors.BACKGROUND
    )

  def update_Settings(self):
    """ Update the settings file """
    f = open("./data/settings.json", "w")
    f.write(json.dumps(self.settings, indent=2))
    f.close()
