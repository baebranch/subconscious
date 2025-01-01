import sys
import requests
import subprocess
import flet as ft
from types import SimpleNamespace

from src.utilities import VERSION
from src.utilities.filechange import FileChange


class NavIcon(ft.Container):
  def __init__(self, show_window, icon, tooltip, badge=None):
    super().__init__()
    self.content=ft.IconButton(
      icon,
      on_click=lambda _: show_window(),
      tooltip=tooltip,
      padding=0,
      style=ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=3)),
      badge = badge
    )
    self.padding = ft.padding.only(4, 0, 4, 4)


class LLMItem(ft.PopupMenuItem):
  def __init__(self, name, switch_llm, slug, scm):
    super().__init__()
    self.switch_llm = switch_llm
    self.name = name
    self.scm = scm
    self.content = ft.Row(
      controls=[
        ft.Text(name),
      ],
    )
    self.data = slug
    self.on_click = self.update_settings
  
  def update_settings(self, event):
    event.data = self.data
    self.scm(self.name)
    FileChange(self.switch_llm, event)


class Rightbar(ft.Column):
  def __init__(self, page, show_window, settings, switch_llm, safe_exit, set_current_model):
    super().__init__()
    self.p = page
    self.width = 48
    self.spacing = 0
    self.alignment = "end"
    self.release_data = {}
    self.settings = settings
    self.safe_exit = safe_exit
    self.scm = set_current_model
    self.switch_llm = switch_llm
    self.dummy_event = SimpleNamespace(data=None)
    self._button_padding = ft.padding.only(4, 0, 4, 4)

    self.top_nav = ft.Column([
        NavIcon(show_window, ft.icons.INFO_OUTLINE, "About"),
      ], expand=4, spacing=0)
    
    self.llm_menu = ft.PopupMenuButton(
      icon=None, shape=ft.RoundedRectangleBorder(radius=3), # Has no effect
      width=80, height=80, left=-20, top=-20, splash_radius=35,
      icon_size=0, tooltip="LLM",
      items=[
        LLMItem(name=self.llm_config_name(config), switch_llm=self.switch_llm, slug=slug, scm=self.scm)
        for slug,config in reversed(list(self.settings['_models'].items()))
        if config['model'] and config['provider']
      ],
    )

    self.llm_menu_container = ft.Container(
      content=ft.Row([
        ft.Container(
          content=ft.Stack([
              ft.Image(
                src="./src/assets/ai_sparkle.svg",
                width=30, height=30,
                top=5, left=5,
                color=ft.colors.PRIMARY
              ),
              self.llm_menu,
            ], clip_behavior=ft.ClipBehavior.HARD_EDGE,
            width=40, height=40
          ),
          padding=ft.padding.only(0,0,0,0),
          shape=ft.RoundedRectangleBorder(radius=3),
        )
      ], height=40, width=40, spacing=0),
      padding=ft.padding.only(0,0,0,0),
      margin=ft.margin.only(4,0,4,4),
      border_radius=ft.BorderRadius(3,3,3,3),
      clip_behavior=ft.ClipBehavior.HARD_EDGE,
      visible=True if len(self.llm_menu.items) > 0 else False,
    )

    # Update modal
    self.confirm_update_modal = ft.AlertDialog(
      modal=True,
      title=ft.Text("Update Available"),
      content=ft.Text("An update is available. Would you like to install it?"),
      data="Update",
      actions=[
        ft.TextButton("No", on_click=self.handle_close, data=False),
        ft.TextButton("Yes", on_click=self.handle_close, data=True),
      ],
      actions_alignment=ft.MainAxisAlignment.END,
    )

    self.controls = [
      # Top navigation
      self.top_nav,

      # LLM selection menu
      self.llm_menu_container,
    ]

    if self.settings["_general"]["llm"]:
      self.scm(self.llm_config_name(self.settings['_models'][self.settings["_general"]["llm"]]))
    self.check_for_updates()

  def update_llms(self):
    # Check if a LLM is configured
    configured = self.llm_configured()

    # Update the LLM menu and make the button visible
    self.llm_menu.items = [
      LLMItem(name=self.llm_config_name(config), switch_llm=self.switch_llm, slug=slug, scm=self.scm)
      for slug,config in reversed(list(self.settings['_models'].items()))
      if config['model'] and config['provider']
    ]
    self.llm_menu_container.visible = True if len(self.llm_menu.items) > 0 else False

    # Automatically configure an LLM once configured in settings
    if not configured and self.llm_configured():
      def set_llm():
        self.settings["_general"]["llm"] = self.llm_menu.items[0].data
        return self.settings
      
      # Switch the LLM and update the settings
      if self.settings["_general"]["llm"]:
        self.scm(self.llm_config_name(self.settings['_models'][self.settings["_general"]["llm"]]))
      self.dummy_event.data = self.llm_menu.items[0].data
      self.switch_llm(self.dummy_event)
      FileChange(set_llm)
    elif configured and not self.llm_configured():
      def unset_llm():
        self.settings["_general"]["llm"] = ""
        return self.settings

      # Switch the LLM and update the settings & UI
      self.scm("")
      self.dummy_event.data = ""
      self.switch_llm(self.dummy_event)
      FileChange(unset_llm)
    
    # Update the LLM in use text
    # Trigger page update
    self.page.update()
  
  def llm_configured(self):
    return self.llm_menu_container.visible
  
  def llm_config_name(self, config):
    if config['alias']:
      return config['alias']
    elif config['model'] or config['provider']:
      return f"{config['provider']}-{config['model']}"
    else:
      return "Provider-Model"
  
  def check_for_updates(self):
    """ Check for updates """
    try:
      response = requests.get("https://api.github.com/repos/baebranch/subconscious/releases/latest")
      response.raise_for_status()
      self.release_data = response.json()

      if self.release_data['tag_name'] != VERSION:
        self.top_nav.controls.append(
          NavIcon(
            lambda: self.page.open(self.confirm_update_modal),
            ft.icons.SYSTEM_UPDATE_ALT,
            "Update available",
            ft.Badge(small_size=10, offset=ft.Offset(-5, 2), alignment=ft.alignment.top_right)
          )
        )
    except Exception as e:
      print(f"Error checking for updates: {e}")
  
  def handle_close(self, event):
    """ Handle the close event of the update modal """
    self.page.close(self.confirm_update_modal)
    update = event.control.data
    if update: self.start_installer_and_exit()
  
  def start_installer_and_exit(self):
    """ Start the installer and exit the current program """
    try:
      subprocess.Popen(
        ["installer.exe"],
        creationflags=subprocess.DETACHED_PROCESS
      )
      self.safe_exit()
    except Exception as e:
      print(f"Error starting installer: {e}")
