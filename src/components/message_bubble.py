import flet as ft

from components.gui_constants import *
from components.data_objects import Message


class MessageBubble(ft.Container):
  """ A class to represent a message bubble in the chat.
      A message block is a stack of individual messages and an display icon
  """
  def __init__(self, message: Message):
    super().__init__()
    self.vertical_alignment="start"
    self.bgcolor = sender_message_bubble_bg_colour
    self.margin=ft.margin.only(right=6)
    self.alignment=ft.alignment.center_left
    self.width=500
    self.padding=10
    self.border_radius=message_bubble_radius
    self.message = message

    self.controls=[
      ft.Text(value="This is a test message", color="Ff0000"),
      ft.Column(
        [
          ft.Text(self.message.user_name, weight="bold"), # Username of message sender
          ft.Text(
            self.message.text,
            selectable=True,
            color=sender_message_bubble_text_colour,
            size=14,
            weight=ft.FontWeight.W_400
          ), # Message
          ft.Text(self.message.datetime, size=10) # Message timestamp
        ],
        tight=True,
        spacing=4,
      )
    ]
    
  