""" Langgraph implementation using the subconscious UI """
import sqlite3
import logging
import win32gui
import win32con
from time import sleep, time
from typing import Annotated
import pydantic.deprecated.decorator
from datetime import datetime, timezone
from typing_extensions import TypedDict
from langchain_openai import ChatOpenAI
from langchain_ollama import ChatOllama
from langchain_anthropic import ChatAnthropic
from langgraph.graph.message import add_messages
from langgraph.checkpoint.sqlite import SqliteSaver
from langchain_google_genai import ChatGoogleGenerativeAI
from langgraph.graph import StateGraph, START, END, MessagesState
from langchain_huggingface import ChatHuggingFace, HuggingFaceEndpoint

from subconscious import Subconscious


# Logging config
logger = logging.getLogger("subconscious")
logger.setLevel(logging.DEBUG)


class State(TypedDict):
  """ State graph and state definition """
  messages: Annotated[list, add_messages]


class Runner:
  """ Manages the LLM, loading data, switching between models and the state graph """
  model_config = {
    "Anthropic": ChatAnthropic,
    "OpenAI": ChatOpenAI,
    "Ollama": ChatOllama,
    "Google": ChatGoogleGenerativeAI,
    "Hugging Face": lambda api_key, model: ChatHuggingFace(
      llm=HuggingFaceEndpoint(huggingfacehub_api_token=api_key, repo_id=model)
    )
  }
  graph_builder = StateGraph(State)
  # show_banner = None

  def __init__(self):
    # DB connection and conversation config
    self.conn = sqlite3.connect("./data/memory.db", check_same_thread=False)
    self.memory = SqliteSaver(self.conn)
    self.config = {"configurable": {"thread_id": "general"}}

  def invoke(self, messages):
    return self.model.invoke(messages)
    
  def switch(self, e):
    """ Switch the model """
    # Update the settings
    self.settings["_general"]["llm"] = e.data
    logger.info(f"Switching to {e.data}")

    # Configure the new model
    try:
      if e.data:
        self.model = self.model_config[self.settings["_models"][e.data]["provider"]](
          api_key = self.settings["_models"][e.data]["api_key"],
          model = self.settings["_models"][e.data]["model"]
        )
    except Exception as e:
      self.banner_error(e)
      logger.error(f"Error switching model: {e}")

    return self.settings
  
  def set_settings(self, settings):
    """ Set the LLM settings and configure the initial saved model """
    self.settings = settings

    # Conifgure the model
    if self.settings["_general"]["llm"]:
      try:
        self.model = self.model_config[self.settings["_models"][self.settings["_general"]["llm"]]["provider"]](
          api_key = self.settings["_models"][self.settings["_general"]["llm"]]["api_key"],
          model = self.settings["_models"][self.settings["_general"]["llm"]]["model"]
        )
      except Exception as e:
        # self.banner_error(e)
        logger.error(f"Error configuring model: {e}")

  def chatbot(self, state: State):
    return {"messages": [self.invoke(state["messages"])]}

  def compile_graph(self):
    """ Compile the state graph """
    self.graph_builder.add_node("chatbot", self.chatbot)
    self.graph_builder.add_edge(START, "chatbot")
    self.graph_builder.add_edge("chatbot", END)
    self.graph = self.graph_builder.compile(checkpointer=self.memory)

  def stream_graph_updates(self, message: str):
    try:
      for event in self.graph.stream({"messages": [message]}, config=self.config):
        for value in event.values():
          with_ts = value['messages'][0]
          setattr(with_ts, "timestamp", datetime.now(timezone.utc).astimezone())
          self.graph.update_state(self.config, {"messages": [with_ts]})
          logger.info("LLM:", value['messages'])
          echo.send_response(with_ts, self.config['configurable']['thread_id'])
    except Exception as e:
      self.banner_error(e)
      logger.error(f"Error streaming updates: {e}")

  def conversation_history_loader(self, thread):
    """ History is rendered in the order received """
    messages = []
    count = 0
    history = self.graph.get_state_history(self.config)
    for event in history:
      logger.info("EVENT: ", event.values['messages'])
      return event.values['messages']
    return messages
  
  def banner_error(self, e):
    self.show_banner(e)
  
  def set_banner(self, banner):
    self.show_banner = banner
  
  def set_app_icon(self, icon_path = "assets\\favicon.ico"):
    """ Override the default icon used by flet, the config that replaces it is broken """
    hwnd = win32gui.GetForegroundWindow()
    hicon = win32gui.LoadImage(None, icon_path, win32con.IMAGE_ICON, 0, 0, win32con.LR_LOADFROMFILE | win32con.LR_DEFAULTSIZE)
    win32gui.SendMessage(hwnd, win32con.WM_SETICON, win32con.ICON_SMALL, hicon)
    win32gui.SendMessage(hwnd, win32con.WM_SETICON, win32con.ICON_BIG, hicon)


# Initialize and configure UI
echo = Subconscious()

# Initialize the LLM runner and configure
llm = Runner()
llm.set_app_icon()
llm.compile_graph()
llm.set_banner(echo.show_banner) # Sets the banner


echo.set_thread_history_loader(llm.conversation_history_loader) # Sets the conversation history loader
echo.set_active_thread(llm.config['configurable']['thread_id']) # Sets the active thread
llm.set_settings(echo.settings) # Sets the LLM settings
echo.set_llm_switcher(llm.switch) # Sets the LLM switcher
echo.splash = True
echo.set_new_user_message_callback(llm.stream_graph_updates) # Called when a new user message is sent
echo.load(view="app")
