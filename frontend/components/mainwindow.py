import flet as ft

from frontend.components.gui_constants import *
from frontend.components.data_objects import Message
from frontend.components.message_block import MessageBlock, MessageBubble


class ChatBox(ft.Container):
  def __init__(self, chat):
    super().__init__()
    self.chat = chat
    self.spacing = 0
    self.padding = ft.padding.only(5, 5, 5, 5)

    self.controls = [
      ft.Text('This is a test context item')
    ]


class MainWindow(ft.Container):
  """ The main window for the UI """
  def __init__(self, chat, lang, llm):
    super().__init__()
    self.chat = chat
    self.llm = llm
    self.l = lang
    self.bgcolor = chat_bg_colour
    self.padding = 0
    self.expand = True
    self.threads = {}
    self.active_thread = None
    self.active_setting = None

    self.message_list = ft.ListView(
      # expand=True,
      spacing = 10,
      auto_scroll=False,
    )

    self.message_form = ft.TextField(
      hint_text=self.l.chatwindow.hint,
      autofocus=True,
      shift_enter=True,
      min_lines=1,
      max_lines=5,
      on_submit=self.send_message,
      filled=False,
      border=ft.InputBorder.NONE,
      border_color=ft.colors.TRANSPARENT,
      border_radius=0,
      expand=True,
    )

    self.chatwindow = ft.Column([
        ft.Container(
          content=self.message_list,
          padding=20,
          expand=True,
          bgcolor=chat_bg_colour,
        ),
        ft.Container(
          border=ft.border.only(top=ft.BorderSide(1, outline_colour)),
          padding=ft.padding.only(10, 0, 4, 0),
          content=ft.Row(
            [
              self.message_form,
              ft.Container(content=
              ft.IconButton(
                icon=ft.icons.SEND_ROUNDED,
                tooltip="Send message",
                # icon_color=controls_colour,
                style=ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=3)),
                on_click=self.send_message,
              ), padding=ft.padding.only(0, 0, 0, 4)
              )
            ], alignment="end", vertical_alignment="end", spacing=0,
          )
        ),
      ],
    )

    self.message_list.controls.append(MessageBubble(Message(user_name='Brian', text='Hello there')))
    self.message_list.controls.append(MessageBubble(Message(user_name='Brian', text='Hello there', is_sender=False)))

    # Default settings content
    self.default_settings = ft.Column([
      ft.Icon(ft.icons.SETTINGS, size=300, color=app_bg_colour),
    ], alignment="center", horizontal_alignment="center", spacing=0)

    # Default content
    self.default = ft.Column([
      ft.Icon(ft.icons.CHAT_OUTLINED, size=300, color=app_bg_colour),
      ft.Text("A desktop UI for LLM and Agent use.", size=20, color=ft.colors.GREY),
    ], alignment="center", horizontal_alignment="center", spacing=0)

    # self.content = self.default_settings
    # self.content = self.default
    self.content = self.chatwindow


  def send_message(self, e):
    # Prepare the message to be sent and prevent empty messages
    text = self.message_form.value.strip()
    if len(text) == 0:
      return
        
    # Update the chat window with the new user's message
    message = Message(user_name='Brian', text=text)
    self.message_form.value = ""
    self.message_list.controls.append(MessageBubble(message))
    self.page.update()
    
    # Get the response from the chatbot and update the chat window
    res = self.llm.send(message.text)
    self.message_list.controls.append(MessageBubble(Message(user_name='Subconscious', text=res, is_sender=False)))
    self.page.update()
  
  def load_history(self, context, oldest):
    """ context: specific context to load history for
        oldest: the oldest message in the current context message list
    """
  
  def show_thread(self, thread_id=None):
    """ Show the thread content """
    if thread_id is None:
      self.content = self.default
      return
    
    # Else show/load the thread content
    thread = self.threads.get(thread_id, None)
    # TODO: continue from here
    # Load messages
    # Add to a new list view
    # Set the content to the new list view
    # Update the page
  
  def show_setting(self, setting_name=None):
    """ Show the settings content """
    if setting_name is None:
      self.content = self.default_settings
      return
    
    # Else show/load the setting content
  
  

