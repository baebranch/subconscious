import json
import flet as ft
from uuid import uuid4
from threading import Thread
from time import time, monotonic
from src.utilities import VERSION
from collections import defaultdict
from src.components.data_objects import HumanMessage

from src.utilities.filechange import FileChange
from src.components.data_objects import Message, ExpansionPanelSlug
from src.components.message_block import MessageBlock, MessageBubble


class MessageList(ft.ListView):
  def __init__(self, *args, **kwargs):
    super().__init__(*args, **kwargs)
    self.expand = 1
    self.width = 750
    self.spacing = 10
    self.loaded = False
    self.active = False
    self.auto_scroll = True
    self.last = monotonic()
    self.cache_extent = 1000
    self.velocity = monotonic()
    self.on_scroll_interval = 2000
    self.semantic_child_count = 10000
    self.padding = ft.padding.only(0, 0, 0, 115)


class MainWindow(ft.Container):
  """ The main window for the UI """
  def __init__(self, lang, settings, update_llms, llm_configured):
    super().__init__()
    self.l = lang
    self.padding = 0
    self.expand = True
    self.settings = settings
    self.active_thread = None
    self.last_ai_message = {}
    self.model_config_titles = {}
    self.update_llms = update_llms
    self.bgcolor = ft.colors.BACKGROUND
    self.llm_configured = llm_configured
    self.threads = defaultdict(MessageList)

    self.message_form = ft.TextField(
      hint_text=self.l.chatwindow.hint,
      autofocus=True,
      shift_enter=True,
      min_lines=1,
      max_lines=5,
      on_submit=self.send_message,
      border=ft.InputBorder.NONE,
      border_color=ft.colors.TRANSPARENT,
      border_radius=0,
      expand=True,
    )

    self.model_in_use = ft.Text("", size=12, color=ft.Colors.PRIMARY, text_align="center")

    self.message_controls = ft.Container(content=
        ft.IconButton(
          icon=ft.icons.SEND_ROUNDED,
          tooltip="Send message",
          style=ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=3)),
          on_click=self.send_message,
        ), padding=ft.padding.only(0, 0, 0, 4), margin=0
      )

    def handle_change(e: ft.ControlEvent, title, key, setting):
      def make_change(e, title, key, setting):
        if type(self.settings[title][key]['value']) == bool:
          self.settings[title][key]['value'] = json.loads(e.data)
        else:
          self.settings[title][key]['value'] = e.data
        return self.settings
      
      FileChange(make_change, e, title, key, setting)

    def render_settings(title, key, val):
      if title == "General": pass
      elif type(val['value']) == bool:
        return ft.Row([
          ft.Checkbox(
            label=val['label'],
            value=val['value'], on_change=lambda e: handle_change(e, title, key, val), shape=ft.RoundedRectangleBorder(radius=3),
            label_style=ft.TextStyle(size=15, overflow=ft.TextOverflow.CLIP), tooltip=val['label'], expand_loose=True
          ),
        ], spacing=0, wrap=True)
      elif type(val['value']) == str and "key" in key:
        return ft.Container(content=
          ft.Row([
            ft.Container(content=
              ft.Text(f"{key.replace('_', ' ').title()}  ", size=15),
              padding=ft.padding.only(0, 0, 0, 1),
            ),
            ft.Container(content=
              ft.TextField(
                value=val['value'], on_change=lambda e: handle_change(e, title, key, val),
                border=ft.InputBorder.NONE, border_color=ft.colors.TRANSPARENT,
                bgcolor=ft.colors.TRANSPARENT, border_radius=5, multiline=False, 
                clip_behavior=ft.ClipBehavior.HARD_EDGE, 
                content_padding=ft.padding.only(2, -14, 2, 2),
                dense=True, password=True
              ),
              border=ft.border.all(1, ft.colors.PRIMARY), border_radius=5,
              padding=ft.padding.only(0, 0, 0, 0), margin=ft.margin.all(0), bgcolor=ft.colors.BACKGROUND, expand=True,
              clip_behavior=ft.ClipBehavior.HARD_EDGE,
            ),

          ], spacing=0, expand=True),
          padding=ft.padding.only(10, 0, 20, 15),
        )
      elif type(val['value']) == str:
        return ft.Container(content=
          ft.Row([
            ft.Container(content=
              ft.Text(f"{key.replace('_', ' ').title()}  ", size=15),
              padding=ft.padding.only(0, 0, 0, 1),
            ),
            ft.Container(content=
              ft.TextField(
                value=val['value'], on_change=lambda e: handle_change(e, title, key, val),
                border=ft.InputBorder.NONE, border_color=ft.colors.TRANSPARENT,
                bgcolor=ft.colors.TRANSPARENT, border_radius=5, multiline=False, 
                clip_behavior=ft.ClipBehavior.HARD_EDGE, 
                content_padding=ft.padding.only(2, -14, 2, 2),
                dense=True
              ),
              border=ft.border.all(1, ft.colors.PRIMARY), border_radius=5,
              padding=ft.padding.only(0, 0, 0, 0), margin=ft.margin.all(0), bgcolor=ft.colors.BACKGROUND, expand=True,
              clip_behavior=ft.ClipBehavior.HARD_EDGE,
            ),

          ], spacing=0, expand=True),
          padding=ft.padding.only(10, 0, 20, 15),
        )

    # Models settings config
    self.models_config = ft.ExpansionPanelList(
      expand_icon_color=ft.colors.PRIMARY,
      elevation=8,
      divider_color=ft.colors.SECONDARY_CONTAINER,
      expanded_header_padding=ft.padding.all(0),
      controls=[
        self.new_llm_config(None, slug, **config)
        for slug,config in reversed(list(self.settings['_models'].items()))
      ]
    )
        
    # Default settings content
    self.default_settings = ft.ListView([
        # Settings Header
        ft.Container(
          content=ft.Row([
            ft.Icon(ft.icons.SETTINGS, size=20, color=ft.colors.PRIMARY),
            ft.Text("Settings", size=20, color=ft.colors.PRIMARY),
          ], alignment="center"),
          margin=ft.margin.only(30, 4, 30, 10),
        ),

        # General Settings Expansion Panel List
        ft.ExpansionPanelList(
          expand_icon_color=ft.colors.PRIMARY,
          elevation=8,
          divider_color=ft.colors.SECONDARY_CONTAINER,
          expanded_header_padding=ft.padding.all(0),
          controls=[
            # General settings panel
            ft.ExpansionPanel(
              header=ft.Container(
                content=ft.Text("General", size=18, color=ft.colors.PRIMARY),
                padding=ft.padding.only(10, 10, 10, 0)
              ),
              content=ft.Column([
                ft.Row([
                  ft.Checkbox(
                    label=self.settings['General']['tray']['label'],
                    value=self.settings['General']['tray']['value'], on_change=lambda e: handle_change(e, "General", "tray", self.settings['General']), shape=ft.RoundedRectangleBorder(radius=3),
                    label_style=ft.TextStyle(size=15, overflow=ft.TextOverflow.CLIP), tooltip=self.settings['General']['tray']['label'], expand_loose=True
                  ),
                ], spacing=0, wrap=True),
              ], alignment="start"),
              bgcolor=ft.colors.SURFACE_CONTAINER_HIGHEST,
              expanded=False, can_tap_header=True,
            )
          ]
        ),

        # Horizontal divider
        ft.Container(
          content=ft.Divider(height=1, color=ft.colors.SECONDARY_CONTAINER),
          padding=ft.padding.only(6, 0, 5, 0)
        ),

        # Models Sub-Header and Button
        ft.Container(content=
          ft.Row([
            ft.Container(
              content=ft.Row([
                ft.Text("Models", size=18, color=ft.colors.PRIMARY),
              ], expand=True),
              margin=ft.margin.only(10, 4, 30, 0), expand=True
            ),
            ft.TextButton(
              text="New Model Config",
              icon=ft.icons.ADD,
              style=ft.ButtonStyle(
                  shape=ft.RoundedRectangleBorder(radius=3),
                  padding=ft.padding.only(5,5,10,0),
                  alignment=ft.alignment.center,
                  text_style=ft.TextStyle(size=15, color=ft.colors.PRIMARY, weight=ft.FontWeight.NORMAL),
                  icon_size=18
                ),
              on_click=self.new_llm_config,
            )
          ], spacing=0),
          padding=ft.padding.only(0, 0, 0, 0),
        ),

        # Models Settings Expansion Panel List
        self.models_config,
      ], spacing=10, padding=ft.padding.only(0, 0, 0, 20))

    # Default message content for when there are no messages
    self.default = ft.Container(
      content=ft.Column([
      ft.Text("To get started introduce your self, say what you would like to do, ask about features or how sbuconscious can help by typing in the chatbox below...", size=20, color=ft.colors.GREY, text_align=ft.TextAlign.CENTER),
    ], alignment="center", horizontal_alignment="center", spacing=0, expand=True), expand=True, padding=ft.padding.only(0,0,0,150), alignment=ft.alignment.center)

    # Configuration required for LLM default message
    self.configure = ft.Container(
      content=ft.Column([
      ft.Text("To begin using this Chat Interface, a LLM API key or local source (Ollama) must be configured in the settings for one of the supported LLM providers.", size=20, color=ft.colors.GREY, text_align=ft.TextAlign.CENTER),
    ], alignment="center", horizontal_alignment="center", spacing=0, expand=True), expand=True, padding=ft.padding.only(0,0,0,150), alignment=ft.alignment.center)

    # About content
    self.about = ft.Column([
      ft.Image(src="./src/assets/logo.png", width=100, height=100),
      ft.Row([
        ft.Icon(ft.icons.INFO_OUTLINE, size=20, color=ft.colors.GREY),
        ft.Text("About", size=20, color=ft.colors.GREY),
      ], alignment="center"),
      ft.Text("Subconscious is a simple desktop open sourced UI for LLM and Agent interaction.", size=15, color=ft.colors.GREY, text_align=ft.TextAlign.CENTER),
      ft.Text(spans=[
        ft.TextSpan("Visit us at: "),
        ft.TextSpan(
            "subconscious.chat",
            ft.TextStyle(decoration=ft.TextDecoration.UNDERLINE),
            url="https://subconscious.chat/",
            on_enter=self.highlight_link,
            on_exit=self.unhighlight_link,
          ),
      ], size=15),
      ft.Text(spans=[
        ft.TextSpan(
            "License",
            ft.TextStyle(decoration=ft.TextDecoration.UNDERLINE),
            url="https://github.com/baebranch/subconscious/blob/main/LICENSE",
            on_enter=self.highlight_link,
            on_exit=self.unhighlight_link,
          ),
      ], size=15),
      ft.Text(f"Version:{VERSION}", size=15, color=ft.colors.GREY),
      ft.Text("© 2024 Subconscious", size=15, color=ft.colors.GREY),
    ], alignment="center", horizontal_alignment="center", spacing=0)

    self.content = self.default

  def show_banner(self, error):
    self.page.overlay.append(
      ft.Container(content=
        ft.Row([
          ft.Icon(ft.icons.ERROR_OUTLINE, size=25, color=ft.colors.RED),
          ft.Row([
            ft.Text(f"Error: {str(error)}", size=15, color=ft.colors.RED, width=350),
          ], width=350),
          ft.IconButton(ft.icons.CLOSE, on_click=self.close_banner, style=ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=3))),
        ], expand_loose=True, alignment="center", vertical_alignment="center", spacing=10),
        bgcolor=ft.Colors.AMBER_100,
        padding=ft.padding.only(20, 10, 20, 10),
        margin=ft.margin.only(0, 40, 0, 0)
      )
    )
    self.page.update()
  
  def close_banner(self, e):
    self.page.overlay.pop(0)
    self.page.update()

  def highlight_link(self, e):
    e.control.style.color = ft.Colors.BLUE
    e.control.update()
  
  def handle_model_change(self, e: ft.ControlEvent, slug, key):
    """ Handle changes to the model configurations """
    # Update the settings
    def make_change(e, slug, key):
      if key == 'delete':
        del self.settings['_models'][slug]
      else:
        self.settings['_models'][slug][key] = e.data
      return self.settings

    FileChange(make_change, e, slug, key)

    # Update titles
    if key in ['alias', 'model', 'provider']:
      if key == 'alias' and e.data == "":
        if self.settings['_models'][slug]['model'] == "" and self.settings['_models'][slug]['provider'] == "": 
          self.model_config_titles[slug].value = "Provider-Model"
        else:
          self.model_config_titles[slug].value = f"{self.settings['_models'][slug]['provider']}-{self.settings['_models'][slug]['model']}"
      elif key == 'alias':
        self.model_config_titles[slug].value = e.data
      elif self.settings['_models'][slug]['alias'] == "":
        if self.settings['_models'][slug]['model'] == "" and self.settings['_models'][slug]['provider'] == "": 
          self.model_config_titles[slug].value = "Provider-Model"
        else:
          self.model_config_titles[slug].value = f"{self.settings['_models'][slug]['provider']}-{self.settings['_models'][slug]['model']}"
      self.page.update()
    elif key == 'delete':
      for control in self.models_config.controls:
        if control.slug == slug:
          self.models_config.controls.remove(control)
      self.page.update()

    # Update LLM list
    self.update_llms()
  
  def new_llm_config(self, event, slug=None, provider=None, model=None, api_key=None, alias=None):
    """ Calls an external function to switch the LLM model """
    if not slug: slug = str(uuid4())
    if event:
      text = "Provider-Model"
    else:
      text = alias if alias else (f"{self.settings['_models'][slug]['provider']}-{self.settings['_models'][slug]['model']}" if self.settings['_models'][slug]['model'] or self.settings['_models'][slug]['provider'] else "Provider-Model")

    title = ft.Text(text, size=18, color=ft.colors.PRIMARY)
    self.model_config_titles[slug] = title
    panel = ExpansionPanelSlug(
      slug=slug,
      header=ft.Container(
        content=title,
        padding=ft.padding.only(10, 10, 10, 0)
      ),
      content=ft.Column([
        # Provider
        ft.Container(content=
          ft.Row([
            ft.Container(content=
              ft.Text("Provider  ", size=15),
              padding=ft.padding.only(10, 0, -1, 0),
            ),
            ft.Container(content=
              ft.Dropdown(
                value=provider if provider else None,
                width=250,
                border=None,
                hint_content=ft.Text("Select a provider", weight=ft.FontWeight.NORMAL), # This is the text that shows when no option is selected
                border_color=ft.colors.TRANSPARENT,
                content_padding=ft.padding.only(10,2,2,2),
                expand=True,
                dense=True,
                on_change=lambda e: self.handle_model_change(e, slug, "provider"),
                options=[
                  ft.dropdown.Option(key="OpenAI", content=ft.Text("OpenAI", weight=ft.FontWeight.NORMAL)),
                  ft.dropdown.Option(key="Anthropic", content=ft.Text("Anthropic", weight=ft.FontWeight.NORMAL)),
                  ft.dropdown.Option(key="Google", content=ft.Text("Google", weight=ft.FontWeight.NORMAL)),
                  ft.dropdown.Option(key="Ollama", content=ft.Text("Ollama", weight=ft.FontWeight.NORMAL)),
                  ft.dropdown.Option(key="Hugging Face", content=ft.Text("Hugging Face", weight=ft.FontWeight.NORMAL)),
                ],
              ), expand=True, border=ft.border.all(1, ft.colors.PRIMARY), border_radius=5,
            ), 
          ], alignment=ft.alignment.center, spacing=0, expand=True),
          padding=ft.padding.only(0, 10, 15, 0), expand=True
        ),

        # LLM Name Field
        ft.Container(content=
          ft.Row([
            ft.Container(content=
              ft.Text("Model     ", size=15),
              padding=ft.padding.only(0, 0, 0, 1),
            ),
            ft.Container(content=
              ft.TextField(
                value=model if model else None,
                on_change=lambda e: self.handle_model_change(e, slug, "model"),
                border=ft.InputBorder.NONE, border_color=ft.colors.TRANSPARENT,
                bgcolor=ft.colors.TRANSPARENT, border_radius=5, multiline=False, 
                clip_behavior=ft.ClipBehavior.HARD_EDGE, 
                content_padding=ft.padding.only(10, -14, 2, 2),
                dense=True, hint_text="Enter the LLM model name",
                hint_style=ft.TextStyle(color=ft.colors.SECONDARY, weight=ft.FontWeight.NORMAL),
              ),
              border=ft.border.all(1, ft.colors.PRIMARY), border_radius=5,
              padding=ft.padding.only(0, 0, 0, 0), margin=ft.margin.all(0), bgcolor=ft.colors.BACKGROUND, expand=True,
              clip_behavior=ft.ClipBehavior.HARD_EDGE,
            ),

          ], spacing=0, expand=True),
          padding=ft.padding.only(10, 0, 15, 0)
        ),

        # Api Key Field
        ft.Container(content=
          ft.Row([
            ft.Container(content=
              ft.Text("API Key   ", size=15),
              padding=ft.padding.only(0, 0, 0, 1),
            ),
            ft.Container(content=
              ft.TextField(
                value=api_key if api_key else None,
                on_change=lambda e: self.handle_model_change(e, slug, "api_key"),
                border=ft.InputBorder.NONE, border_color=ft.colors.TRANSPARENT,
                bgcolor=ft.colors.TRANSPARENT, border_radius=5, multiline=False, 
                clip_behavior=ft.ClipBehavior.HARD_EDGE, 
                content_padding=ft.padding.only(10, -14, 2, 2),
                dense=True, password=True, hint_text="Enter the LLM API key if required",
                hint_style=ft.TextStyle(color=ft.colors.SECONDARY, weight=ft.FontWeight.NORMAL)
              ),
              border=ft.border.all(1, ft.colors.PRIMARY), border_radius=5,
              padding=ft.padding.only(0, 0, 0, 0), margin=ft.margin.all(0), bgcolor=ft.colors.BACKGROUND, expand=True,
              clip_behavior=ft.ClipBehavior.HARD_EDGE,
            ),

          ], spacing=0, expand=True),
          padding=ft.padding.only(10, 0, 15, 0)
        ),

        # Alias Field
        ft.Container(content=
          ft.Stack([
            ft.Icon(ft.icons.MORE_HORIZ, size=25),
            ft.ExpansionTile(
              title=ft.Stack([], height=7),
              tile_padding=ft.padding.only(0, 0, 0, 0),
              min_tile_height=7,
              dense=True,
              visual_density=ft.VisualDensity.COMPACT,
              show_trailing_icon=False,
              clip_behavior=ft.ClipBehavior.ANTI_ALIAS,
              maintain_state=True,
              collapsed_text_color=ft.Colors.PRIMARY,
              text_color=ft.Colors.PRIMARY,
              expand=False,
              shape=ft.RoundedRectangleBorder(radius=0),
              controls=[
                # Alias field
                ft.Container(content=
                  ft.Row([
                    ft.Container(content=
                      ft.Text("Alias        ", size=15),
                      padding=ft.padding.only(0, 0, 0, 1),
                    ),
                    ft.Container(content=
                      ft.TextField(
                        value=alias if alias else None,
                        on_change=lambda e: self.handle_model_change(e, slug, "alias"),
                        border=ft.InputBorder.NONE, border_color=ft.colors.TRANSPARENT,
                        bgcolor=ft.colors.TRANSPARENT, border_radius=5, multiline=False, 
                        clip_behavior=ft.ClipBehavior.HARD_EDGE, 
                        content_padding=ft.padding.only(10, -14, 2, 2),
                        dense=True, hint_text="Alias for this model configuration",
                        hint_style=ft.TextStyle(color=ft.colors.SECONDARY, weight=ft.FontWeight.NORMAL),
                      ),
                      border=ft.border.all(1, ft.colors.PRIMARY), border_radius=5,
                      padding=ft.padding.only(0, 0, 0, 0), margin=ft.margin.all(0), bgcolor=ft.colors.BACKGROUND, expand=True,
                      clip_behavior=ft.ClipBehavior.HARD_EDGE,
                    ),
                  ], spacing=0, expand=True),
                  padding=ft.padding.only(11, 10, 15, 0)
                ),

                # Delete Button
                ft.Container(content=
                  ft.Row([
                    ft.Container(content=
                      ft.IconButton(
                        icon=ft.icons.DELETE,
                        tooltip="Delete model configuration",
                        style=ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=3)),
                        on_click=lambda e: self.handle_model_change(e, slug, "delete"),
                      )
                    ),
                  ], spacing=0, expand=True),
                  padding=ft.padding.only(10, 10, 15, 0)
                ),
              ],
            ),
          ], expand=True, alignment=ft.alignment.top_center,
          ), padding=ft.padding.only(0, 0, 0, 10)
        ),
      ], alignment=ft.alignment.center, spacing=10),
      bgcolor=ft.colors.SURFACE_CONTAINER_HIGHEST,
      expanded=True if event else False, can_tap_header=True,
    )

    if event:
      self.models_config.controls.insert(0, panel)
      def new_blank(slug):
        self.settings['_models'][slug] = {
          "provider": "",
          "model": "",
          "api_key": "",
          "alias": ""
        }
        self.model_config_titles[slug] = title
        return self.settings
      FileChange(new_blank, slug)
      self.page.update()
    else:
      return panel

  def unhighlight_link(self, e):
    e.control.style.color = None
    e.control.update()

  def chatwindow(self):
    # Determines if the chatbox is visible
    if self.llm_configured():
      self.message_form.visible = True
      self.message_controls.visible = True
    else:
      self.message_form.visible = False
      self.message_controls.visible = False

    # Returns the chat window content
    return ft.Stack([
        ft.Container(
          content=(self.threads[self.active_thread] if self.threads[self.active_thread].active else self.default) if self.llm_configured() else self.configure,
          padding=0,
          expand=True,
          alignment=ft.alignment.top_center
        ),
        ft.Row([
          ft.Stack([
            ft.Column([
              ft.Container(
                content=ft.Column([
                  ft.Container(
                    border_radius=ft.BorderRadius(5, 5, 5, 5),
                    padding=ft.padding.only(4, -15, 4, 0),
                    margin=ft.margin.only(0, 0, 0, 0),
                    bgcolor=ft.colors.SECONDARY_CONTAINER,
                    content=ft.Column(
                      [
                        self.message_form,
                        self.message_controls,
                      ],
                      alignment="center",
                      horizontal_alignment="end", spacing=0,
                    ),
                  ),
                  self.model_in_use,
                  ], alignment="end", horizontal_alignment=ft.CrossAxisAlignment.CENTER, spacing=0,
                ),
                width=500, 
                alignment=ft.alignment.bottom_center, 
                bgcolor=ft.colors.TRANSPARENT,
              )
            ], alignment="end", spacing=0),
            ], 
            alignment=ft.alignment.bottom_center,
            expand=True,
            clip_behavior=ft.ClipBehavior.ANTI_ALIAS,
          )
        ], expand=True, alignment=ft.alignment.bottom_center, spacing=0),
      ],
    )

  def send_message(self, e):
    """ Send the user message """
    # Prepare the message to be sent and prevent empty messages
    text = self.message_form.value.strip()
    if len(text) == 0:
      return
        
    # Update the chat window with the new user's message
    message = HumanMessage(content=text)
    self.message_form.value = ""

    # Add the message to the active thread and shows the thread if not already shown
    self.threads[self.active_thread].controls.append(MessageBubble(message))
    if not self.threads[self.active_thread].active:
      self.threads[self.active_thread].active = True
      self.content = self.chatwindow()
    self.page.update()
    
    # Call the new user message callback
    self.new_user_message_callback(message)
  
  def render_history(self, messages):
    """ messages: Loads the message history chat bubbles into the chat window 
    """
    message_list = MessageList()
    for message in messages:
      message_list.controls.append(MessageBubble(message))
      message_list.active = True
    return message_list
  
  def show_thread(self):
    """ Load thread history if not loaded and show the thread content """
    if not self.threads[self.active_thread].loaded:
      self.threads[self.active_thread] = self.render_history(
        self.load_thread_history(self.active_thread)
      )
      self.threads[self.active_thread].loaded = True
    self.content = self.chatwindow()

    # Update the page
    if self.page: self.page.update()

  def set_thread_history_loader(self, loader):
    self.load_thread_history = loader
  
  def set_new_user_message_callback(self, callback):
    self.new_user_message_callback = callback
  
  def send_response(self, message, thread_id=None):
    self.threads[thread_id].controls.append(MessageBubble(message))
    self.page.update()
  
  def stream_response(self, message, thread_id=None):
    if thread_id in self.last_ai_message and self.last_ai_message[thread_id].id == message.id:
      self.last_ai_message[thread_id].text += message.text
      self.threads[thread_id].controls[-1].message_content.value = self.last_ai_message[thread_id].text
      self.threads[thread_id].controls[-1].message_content.update()
      self.threads[thread_id].controls[-1].update()
    else:
      self.last_ai_message[thread_id] = message
      self.threads[thread_id].controls.append(MessageBubble(self.last_ai_message[thread_id]))
    self.page.update()

  def show_setting(self, setting_name=None):
    """ Show the settings content """
    self.content = self.default_settings
    self.page.update()
  
  def show_about(self):
    """ Show the about content """
    self.content = self.about
    self.page.update()
  
  def set_active_thread(self, thread_id):
    """ Set the active thread """
    self.active_thread = thread_id
    self.show_thread()
  
  def set_current_model(self, model):
    self.model_in_use.value = model
    if self.page: self.page.update()
    