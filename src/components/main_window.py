import json
import flet as ft
from threading import Thread
from time import time, monotonic
from collections import defaultdict
from src.components.data_objects import HumanMessage

from src.utilities.filechange import FileChange
from src.components.data_objects import Message
from src.components.message_block import MessageBlock, MessageBubble


class MessageList(ft.ListView):
  def __init__(self, *args, **kwargs):
    super().__init__(*args, **kwargs)
    self.expand = 1
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

      # Update LLM list
      self.update_llms()


    def render_settings(title, key, val):
      if type(val['value']) == bool:
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
        
    # Default settings content
    self.default_settings = ft.Column([
      # Settings Header
      ft.Container(
        content=ft.Row([
          ft.Icon(ft.icons.SETTINGS, size=20, color=ft.colors.PRIMARY),
          ft.Text("Settings", size=20, color=ft.colors.PRIMARY),
        ], alignment="center"),
        margin=ft.margin.only(30, 4, 30, 10),
      ),

      # Settings Expansion
      ft.ExpansionPanelList(
        expand_icon_color=ft.colors.PRIMARY,
        elevation=8,
        divider_color=ft.colors.SECONDARY_CONTAINER,
        expanded_header_padding=ft.padding.all(0),
        controls=[
          ft.ExpansionPanel(
            header=ft.Container(
              content=ft.Text(title.capitalize(), size=18, color=ft.colors.PRIMARY),
              padding=ft.padding.only(10, 10, 10, 0)
            ),
            content=ft.Column([
              render_settings(title, key, val)
              for key,val in values.items() if key[0] != "_"
            ], alignment="start"),
            bgcolor=ft.colors.SURFACE_CONTAINER_HIGHEST,
            expanded=False, can_tap_header=True,
          )
          for title,values in self.settings.items() if title[0] != "_"
        ]
      )
    ], alignment="start")

    # Default message content for when there are no messages
    self.default = ft.Container(
      content=ft.Column([
      ft.Text("To get started introduce your self, say what you would like to do, ask about features or how sbuconscious can help by typing in the chatbox below...", size=20, color=ft.colors.GREY, text_align=ft.TextAlign.CENTER),
    ], alignment="center", horizontal_alignment="center", spacing=0, expand=True), expand=True, padding=ft.padding.only(0,0,0,150), alignment=ft.alignment.center)

    # Configuration required for LLM default message
    self.configure = ft.Container(
      content=ft.Column([
      ft.Text("To begin using this Chat Interface, an LLM API key or Source (Ollama) must be configured in the settings for one of the supported LLMs.", size=20, color=ft.colors.GREY, text_align=ft.TextAlign.CENTER),
    ], alignment="center", horizontal_alignment="center", spacing=0, expand=True), expand=True, padding=ft.padding.only(0,0,0,150), alignment=ft.alignment.center)

    # About content
    self.about = ft.Column([
      ft.Image(src="./src/assets/icons/logo.png", width=100, height=100),
      ft.Row([
        ft.Icon(ft.icons.INFO_OUTLINE, size=20, color=ft.colors.GREY),
        ft.Text("About", size=20, color=ft.colors.GREY),
      ], alignment="center"),
      ft.Text("Subconscious is a simple desktop open sourced GUI for LLM and Agent use.", size=15, color=ft.colors.GREY, text_align=ft.TextAlign.CENTER),
      ft.Text(spans=[
        ft.TextSpan("Visit us at: "),
        ft.TextSpan(
            "subconscious.chat",
            ft.TextStyle(decoration=ft.TextDecoration.UNDERLINE),
            url="https://subconscious.chat/terms",
            on_enter=self.highlight_link,
            on_exit=self.unhighlight_link,
          ),
      ], size=15),
      ft.Text(spans=[
        ft.TextSpan(
            "Terms of Use",
            ft.TextStyle(decoration=ft.TextDecoration.UNDERLINE),
            url="https://subconscious.chat/terms",
            on_enter=self.highlight_link,
            on_exit=self.unhighlight_link,
          ),
      ], size=15),
      ft.Text(spans=[
        ft.TextSpan(
            "Privacy Policy",
            ft.TextStyle(decoration=ft.TextDecoration.UNDERLINE),
            url="https://subconscious.chat/privacy",
            on_enter=self.highlight_link,
            on_exit=self.unhighlight_link,
          ),
      ], size=15),
      ft.Text("Version: 0.1.0", size=15, color=ft.colors.GREY),
      ft.Text("© 2024 Subconscious", size=15, color=ft.colors.GREY),
    ], alignment="center", horizontal_alignment="center", spacing=0)

    self.content = self.default

  def highlight_link(self, e):
    e.control.style.color = ft.Colors.BLUE
    e.control.update()

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
        ),
        ft.Row([
          ft.Stack([
            ft.Column([
              ft.Container(
                content=ft.Column([
                  ft.Container(
                    border_radius=ft.BorderRadius(5, 5, 5, 5),
                    padding=ft.padding.only(4, -15, 4, 0),
                    margin=ft.margin.only(0, 0, 0, 15),
                    bgcolor=ft.colors.SECONDARY_CONTAINER,
                    content=ft.Column(
                      [
                        self.message_form,
                        self.message_controls
                      ],
                      alignment="center",
                      horizontal_alignment="end", spacing=0,
                    ),
                  )
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
        ], expand=True, alignment=ft.alignment.bottom_center, spacing=0  ),
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
    