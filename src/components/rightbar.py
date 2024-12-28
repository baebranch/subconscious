import flet as ft
from types import SimpleNamespace

from src.utilities.filechange import FileChange


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


class LLMItem(ft.PopupMenuItem):
  def __init__(self, name, switch_llm):
    super().__init__()
    self.switch_llm = switch_llm
    self.content = ft.Row(
      controls=[
        ft.Icon(
          name=ft.icons.COLOR_LENS_OUTLINED,
        ),
        ft.Text(name),
      ],
    )
    self.data = name
    self.on_click = self.update_settings
  
  def update_settings(self, event):
    event.data = self.data
    FileChange(self.switch_llm, event)


class Rightbar(ft.Column):
  def __init__(self, page, show_window, settings, switch_llm):
    super().__init__()
    self.p = page
    self.width = 48
    self.padding = 0
    self.spacing = 0
    self.dummy_event = SimpleNamespace(data=None)
    self.alignment = "end"
    self.settings = settings
    self.switch_llm = switch_llm
    self._button_padding = ft.padding.only(4, 0, 4, 4)

    self.top_nav = ft.Column([
        NavIcon(show_window, ft.icons.INFO_OUTLINE, "About"),
      ], expand=4, spacing=0)
    
    self.llm_menu = ft.PopupMenuButton(
      icon=None, shape=ft.RoundedRectangleBorder(radius=3), # Has no effect
      width=80, height=80, left=-20, top=-20, splash_radius=35,
      icon_size=0, tooltip="LLM",
      items=[
        LLMItem(name=provider, switch_llm=self.switch_llm)
        for provider in ["OpenAI", "Anthropic", "Google", "Ollama"]
        if self.settings[provider]["_type"] == "remote" and self.settings[provider]['api_key']['value'] != "" or
          self.settings[provider]["_type"] == "local" and self.settings[provider]['enabled']['value'] == True
      ],
    )

    self.llm_menu_container = ft.Container(
      content=ft.Row([
        ft.Container(
          content=ft.Stack([
              ft.Image(
                src="./src/assets/icons/ai_sparkle.svg",
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

    self.controls = [
      # Top navigation
      self.top_nav,

      # LLM selection menu
      self.llm_menu_container,
    ]

  def update_llms(self):
    # Check if a LLM is configured
    configured = self.llm_configured()

    # Update the LLM menu and make the button visible
    self.llm_menu.items = [
      LLMItem(name=provider, switch_llm=self.switch_llm)
      for provider in ["OpenAI", "Anthropic", "Google", "Ollama"]
      if self.settings[provider]["_type"] == "remote" and self.settings[provider]['api_key']['value'] != "" or
        self.settings[provider]["_type"] == "local" and self.settings[provider]['enabled']['value'] == True
    ]
    self.llm_menu_container.visible = True if len(self.llm_menu.items) > 0 else False

    # If an llm was configured where before there was none, set the new llm as the active one
    if not configured and self.llm_configured():
      def set_llm():
        self.settings["_general"]["llm"] = self.llm_menu.items[0].data
        return self.settings
      
      # Switch the LLM and update the settings
      self.dummy_event.data = self.llm_menu.items[0].data
      self.switch_llm(self.dummy_event)
      FileChange(set_llm)
    elif configured and not self.llm_configured():
      def unset_llm():
        self.settings["_general"]["llm"] = ""
        return self.settings

      # Switch the LLM and update the settings
      self.dummy_event.data = ""
      self.switch_llm(self.dummy_event)
      FileChange(unset_llm)

    # Trigger page update
    self.page.update()
  
  def llm_configured(self):
    return self.llm_menu_container.visible
