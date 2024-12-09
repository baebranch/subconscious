import flet as ft
from math import pi
import flet.canvas as cv

from src.components.data_objects import Message


class MessageBubble(ft.Row):
  """ A class to represent a message bubble in the chat.
      A message bubble is a single message displaying text
  """
  def __init__(self, message: Message):
    super().__init__()
    self.expand = True
    self.controls = [
      ft.Container(
        ft.Stack([

          # Corner pointer for the message bubble
          self.sender_message_pointer() if message['is_sender'] else self.receiver_message_pointer(),

          # Message bubble
          self.generate_bubble(message),
          
        ], expand_loose=True),
        expand=True,
        alignment=ft.alignment.top_right if message['is_sender'] else ft.alignment.top_left,
      )
    ]
  
  def generate_bubble(self, message: dict):
    return ft.Container(
      bgcolor=ft.colors.PRIMARY_CONTAINER if message['is_sender'] else ft.colors.SURFACE_CONTAINER_HIGHEST,
      border_radius = ft.BorderRadius(5, 5, 5, 5),
      padding = ft.padding.only(10, 5, 10, 5),
      content = ft.Column([
        ft.Container(content=ft.Column([
            ft.Text(message['text'], selectable=True, ),
          ], spacing=0), padding=ft.padding.only(0, 0, 20, 0)),

        ft.Container(content=ft.Column([
            ft.Text(message['created_at'].strftime("%H:%M"), size=12),
          ],spacing=0)),
      ], spacing=0, horizontal_alignment="end"),

      margin=ft.margin.only(right=7) if message['is_sender'] else ft.margin.only(left=7),
    )
  
  def receiver_message_pointer(self):
    return ft.Container(
      border_radius=ft.BorderRadius(2, 2, 2, 2),
      width=20,
      height=15,
      left=0,
      alignment=ft.alignment.top_left,
      content = ft.Stack([
        ft.Container(
          bgcolor=ft.colors.SURFACE_CONTAINER_HIGHEST,
          width=20,
          height=15,
          border_radius=ft.BorderRadius(2, 2, 2, 2),
          rotate=pi/2,
          offset=ft.Offset(0.525, -0.1)
        ),
        ft.Container(
          bgcolor=ft.colors.SURFACE_CONTAINER_HIGHEST,
          width=20,
          height=15,
          border_radius=ft.BorderRadius(2, 2, 2, 2),
          rotate=pi/4,
          offset=ft.Offset(0.075, -0.3),
        ),
      ])
    )

  def sender_message_pointer(self):
    return ft.Container(
      border_radius=ft.BorderRadius(2, 2, 2, 2),
      width=20,
      height=15,
      right=0,
      alignment=ft.alignment.top_right,
      content = ft.Stack([
        ft.Container(
          bgcolor=ft.colors.PRIMARY_CONTAINER,
          width=20,
          height=15,
          border_radius=ft.BorderRadius(2, 2, 2, 2),
          rotate=pi/2,
          offset=ft.Offset(-0.525, -0.1)
        ),
        ft.Container(
          bgcolor=ft.colors.PRIMARY_CONTAINER,
          width=20,
          height=15,
          border_radius=ft.BorderRadius(2, 2, 2, 2),
          rotate=3*(pi/4),
          offset=ft.Offset(-0.075, -0.3),
        ),
      ])
    )


class MessageBlock(ft.Container):
  """ A class to represent a message block in the chat.
      A message block is a stack of individual messages and an display icon
  """
  def __init__(self, message: dict):
    super().__init__()
    self.vertical_alignment="start"
    self.bgcolor=ft.colors.RED
    # self.expand=True
    self.content=ft.Row(
        [
          ft.Text(message.user_name, weight="bold"),
          ft.Text(message.text, selectable=True),
        ],
        # tight=True,
        spacing=5,
      )
      # ft.CircleAvatar(
      #   content=ft.Text(self.get_initials(message.user_name)),
      #   color=ft.colors.WHITE,
      #   bgcolor=self.get_avatar_color(message.user_name),
      # ),
  
  def get_initials(self, user_name: str):
    return user_name[:1].capitalize()

  def get_avatar_color(self, user_name: str):
    colors_lookup = [
      ft.colors.AMBER,
      ft.colors.BLUE,
      ft.colors.BROWN,
      ft.colors.CYAN,
      ft.colors.GREEN,
      ft.colors.INDIGO,
      ft.colors.LIME,
      ft.colors.ORANGE,
      ft.colors.PINK,
      ft.colors.PURPLE,
      ft.colors.RED,
      ft.colors.TEAL,
      ft.colors.YELLOW,
    ]

    return colors_lookup[hash(user_name) % len(colors_lookup)]
