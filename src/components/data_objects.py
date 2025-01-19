""" Message data class """
import flet as ft
from time import time
from datetime import datetime, timezone
from dataclasses import dataclass, field
from langchain_core.messages import AIMessage
from langchain_core.messages import HumanMessage as HM


@dataclass
class Message:
  """ A class to represent message data in the chat screen """
  text: str
  id: str = field(default=None)
  icon: str = field(default=None)
  images: list = field(default=None)
  is_sender: bool = field(default=True)
  thread: str = field(default="general")
  user_name: str = field(default="User")
  created_at: datetime = field(default_factory=lambda: datetime.now(timezone.utc).astimezone())

  def __getitem__(self, item):
    if isinstance(item, str) and hasattr(self, item):
      return getattr(self, item)
    raise KeyError(f"Key {item} not found in {self.__class__.__name__}")

  def to_dict(self):
    return {
      "id": self.id,
      "text": self.text,
      "icon": self.icon,
      "images": self.images,
      "is_sender": self.is_sender,
      "thread": self.thread,
      "user_name": self.user_name,
      "created_at": self.created_at.isoformat(),
    }


class SubMessage(AIMessage):
  """ Creates a time-stamped message object """
  def __init__(self, *args, **kwargs):
    super().__init__(*args, **kwargs)
    self.timestamp = datetime.now(timezone.utc).astimezone()


class HumanMessage(HM):
  """ Creates a time-stamped message object """
  def __init__(self, *args, **kwargs):
    super().__init__(*args, **kwargs)
    self.timestamp = datetime.now(timezone.utc).astimezone()

class ExpansionPanelSlug(ft.ExpansionPanel):
  def __init__(self, slug, *args, **kwargs):
    super().__init__(*args, **kwargs)
    self.slug = slug
