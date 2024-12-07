import flet as ft
from threading import Thread
from time import time, monotonic

from src.components.data_objects import Message
from src.components.message_block import MessageBlock, MessageBubble


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
  def __init__(self, lang):
    super().__init__()
    self.l = lang
    self.padding = 0
    # self.padding = ft.Padding(50, 0, 50, 0)
    self.threads = {}
    self.expand = True
    self.message_list = None
    self.active_thread = None
    self.active_setting = None
    self.bgcolor = ft.colors.BACKGROUND

    self.message_form = ft.TextField(
      hint_text=self.l.chatwindow.hint,
      autofocus=True,
      shift_enter=True,
      min_lines=1,
      max_lines=5,
      on_submit=self.send_message,
      # filled=False,
      border=ft.InputBorder.NONE,
      # bgcolor=ft.colors.RED,
      border_color=ft.colors.TRANSPARENT,
      border_radius=0,
      expand=True,
      # height=40
    )


    # self.message_list.controls.append(MessageBubble(Message(user_name='Brian', text='Hello there')))
    # self.message_list.controls.append(MessageBubble(Message(user_name='Brian', text='Hello there', is_sender=False)))

    # Default settings content
    self.default_settings = ft.Column([
      ft.Icon(ft.icons.SETTINGS, size=300, color=ft.colors.GREY),
      ft.Text("Settings", size=20, color=ft.colors.GREY),
    ], alignment="center", horizontal_alignment="center", spacing=0)

    # Default content
    self.default = ft.Column([
      ft.Icon(ft.icons.CHAT_OUTLINED, size=300, color=ft.colors.GREY),
      ft.Text("A desktop UI for LLM and Agent use", size=20, color=ft.colors.GREY),
    ], alignment="center", horizontal_alignment="center", spacing=0)

    self.content = self.default
    # self.content = self.default_settings
    # self.content = self.chatwindow

  def chatwindow(self):
    return ft.Stack([
        ft.Container(
          content=self.message_list,
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
                  ft.Container(content=
                    ft.IconButton(
                      icon=ft.icons.SEND_ROUNDED,
                      tooltip="Send message",
                      style=ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=3)),
                      on_click=self.send_message,
                    ), padding=ft.padding.only(0, 0, 0, 4), margin=0
                  )
                ],
                alignment="center",
                horizontal_alignment="end", spacing=0,
                # expand=True
              ),
              
            )
            ],alignment="end", horizontal_alignment=ft.CrossAxisAlignment.CENTER, spacing=0,
            # height=200
            # expand_loose=True,
            ),
          
            
            
            # height=200,
            # expand_loose=True,
            width=500, 
            alignment=ft.alignment.bottom_center, 
            # expand=True,
            bgcolor=ft.colors.RED,
            )
          ], alignment="end", spacing=0),
        ], 
        alignment=ft.alignment.bottom_center,
        expand=True,
        # width=500,
        # height=500,
        clip_behavior=ft.ClipBehavior.ANTI_ALIAS,
        # expand_loose=True
        )
        ], expand=True, alignment=ft.alignment.bottom_center, spacing=0  ),
      ],
    )

  def send_message(self, e):
    # Prepare the message to be sent and prevent empty messages
    text = self.message_form.value.strip()
    if len(text) == 0:
      return
        
    # Update the chat window with the new user's message
    message = Message(user_name='User', text=text)
    self.message_form.value = ""
    self.message_list.controls.append(MessageBubble(message))
    # self.page.update()
    
    # Get the response from the chatbot and update the chat window
    # res = self.llm.send(message.text)
    
    self.page.update()
    
    # Call the new user message callback
    self.new_user_message_callback(message.to_dict())
  
  def render_history(self, messages):
    """ context: Renders the specific context to load history for
        oldest: the oldest message in the current context message list
    """
    class Scroller(ft.ListView):
      def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.on_scroll_interval = 1000
        self.on_scroll = self.on_scroll_handler
        self.expand = 1
        self.spacing = 10
        self.padding = 0
        # self.padding = ft.padding.only(50, 20, 50, 20)
        self.cache_extent = 1000
        self.semantic_child_count = 10000

        self.auto_scroll = True
        self.last = monotonic()
        self.velocity = monotonic()

      def on_scroll_handler(self, _):
        # print(_)
        # print(_.data)
        # print(dir(_))
        # print(f'Scr')
        # print(dir(self))
        if _.event_type == 'start':
          # print('Start: ')
          self.velocity = monotonic()
        if _.event_type == 'user':
          # print('User')
          # print(_)
          # print(_.data)
          now = monotonic()
          # print(self.last)
          # print(now - self.last)
          # self.last = now
          # print(now - self.velocity)
          self.velocity = now
          
        
        # if _.event_type != 'user':
        #   print(_)
        #   print(_.data)

          
        # self.scroll_to(offset=_.)
        # elif _.event_type == 'reverse':
      
    message_list = Scroller()

    # message_list = ft.ListView(
    #     spacing = 10,
    #     expand=1,
    #     cache_extent=1000,  # May need to dynamically adjust as more messages are loaded
    #     first_item_prototype=True,
    #     auto_scroll=True,
    #     # on_scroll_interval=50000,
    #     semantic_child_count=10000,
    #     on_scroll=on_scroll
    #   )
    # message_list = ft.Column(
    #     height=200,
    #     # width=float("inf"),
    #     width=200,
    #     scroll=ft.ScrollMode.ALWAYS,
    #     spacing = 10,
    #     # scroll=ft.ScrollMode.ALWAYS,
    #     # expand = 4,
    #     # tight=True,
    #     # scroll=True,
    #     # tight=True,
    #     # expand=True,

    #     # auto_scroll=True,
    #     # on_scroll_interval=1000,
    #     # semantic_child_count=10000
    #   )
    
    # Load ai/user messages in the appropriate bubble
    for message in messages:
      message_list.controls.append(MessageBubble(message))
    
    return message_list
  
  def show_thread(self, thread_id=None):
    """ Show the thread content """
    # Show default or current thread content
    # if thread_id is None:
    #   if self.active_thread is None:
    #     self.content = self.default
    #     return
    #   elif self.active_thread:
    #     self.content = self.chatwindow()

    # # Else show/load the thread content
    # elif thread_id:
    #   self.active_thread = thread_id
    
    #   # Check if the thread is already loaded
    #   thread = self.threads.get(self.active_thread, None)

    #   if thread:
    #     # Show the thread content
    #     self.message_list = thread
    #     self.content = self.chatwindow()
    #   else:
    #     # Load the thread and render content
    #     messages = self.load_thread_history(self.active_thread)
    #     self.threads[self.active_thread] = self.render_history(messages)
    #     self.message_list = self.threads[self.active_thread]
    #     self.content = self.chatwindow()
    # Load the thread and render content
    messages = self.load_thread_history(self.active_thread)
    self.threads[self.active_thread] = self.render_history(messages)
    self.message_list = self.threads[self.active_thread]
    self.content = self.chatwindow()

    self.page.update()

    # TODO: continue from here
    # Load messages
    # Add to a new list view
    # Set the content to the new list view
    # Update the page
  def set_thread_history_loader(self, loader):
    self.load_thread_history = loader
  
  def set_new_user_message_callback(self, callback):
    self.new_user_message_callback = callback
  
  def send_response(self, message):
    self.message_list.controls.append(MessageBubble(message))
    self.page.update()

  def show_setting(self, setting_name=None):
    """ Show the settings content """
    if setting_name is None:
      self.content = self.default_settings
      return
    