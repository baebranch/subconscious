""" Langgraph implementation using the subconscious UI """
import sqlite3
import logging
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

from src.subconscious import Subconscious
from src.components.data_objects import HumanMessage

# DB connection and conversation config
conn = sqlite3.connect("./data/memory.db", check_same_thread=False)
memory = SqliteSaver(conn)
config = {"configurable": {"thread_id": "19"}}


class State(TypedDict):
  # Messages have the type "list". The `add_messages` function
  # in the annotation defines how this state key should be updated
  # (in this case, it appends messages to the list, rather than overwriting them)
  messages: Annotated[list, add_messages]


graph_builder = StateGraph(State)

class LLM:
  """ Language model manager for switching between different LLMs """

  model_config = {
    "Anthropic": {
      "chat": ChatAnthropic,
      "model": "claude-3-5-sonnet-20240620"
    },
    "OpenAI": {
      "chat": ChatOpenAI,
      "model": "gpt-3.5-turbo"
    },
    "Ollama": {
      "chat": ChatOllama,
      "model": "phi3"
    },
    "Google": {
      "chat": ChatGoogleGenerativeAI,
      "model": "gemini-1.5-pro"
    }
  }

  def __init__(self):
    """ Initialize the LLM model """

  def invoke(self, messages):
    return self.model.invoke(messages)
    
  def switch(self, e):
    """ Switch the model """
    # Update the settings
    self.settings["_general"]["llm"] = e.data
    print(f"Switching to {self.settings['_general']['llm']}")

    # Configure the new model
    if self.settings["_general"]["llm"] in self.model_config:
      self.model = self.model_config[self.settings["_general"]["llm"]]["chat"](
        api_key = self.settings[self.settings["_general"]["llm"]]["api_key"]["value"] if "api_key" in self.settings[self.settings["_general"]["llm"]] else None,
        model = self.model_config[self.settings["_general"]["llm"]]["model"]
      )

    return self.settings
  
  def set_settings(self, settings):
    """ Set the LLM settings and configure the initial model """
    self.settings = settings

    # Conifgure the model
    if self.settings["_general"]["llm"] in self.model_config:
      self.model = self.model_config[self.settings["_general"]["llm"]]["chat"](
        api_key = self.settings[self.settings["_general"]["llm"]]["api_key"]["value"] if "api_key" in self.settings[self.settings["_general"]["llm"]] else None,
        model = self.model_config[self.settings["_general"]["llm"]]["model"]
      )


llm = LLM()


def chatbot(state: State):
  return {"messages": [llm.invoke(state["messages"])]}


graph_builder.add_node("chatbot", chatbot)
graph_builder.add_edge(START, "chatbot")
graph_builder.add_edge("chatbot", END)
graph = graph_builder.compile(checkpointer=memory)


def stream_graph_updates(message: str):
  for event in graph.stream({"messages": [message]}, config=config):
    for value in event.values():
      with_ts = value['messages'][0]
      setattr(with_ts, "timestamp", datetime.now(timezone.utc).astimezone())
      graph.update_state(config, {"messages": [with_ts]})
      print("LLM:", value['messages'])
      echo.send_response(with_ts, config['configurable']['thread_id'])


# Demo conversation threads
conversation_threads = [
  {
    "id": "general",
    "title": "General",
    "description": "General thread for miscellaneous messages",
    "updated_at": datetime.now(),
  },
  {
    "id": "feedback",
    "title": "Feedback",
    "description": "Thread for feedback messages",
    "updated_at": datetime.now(),
  },
  {
    "id": "support",
    "title": "Support",
    "description": "Thread for support messages",
    "updated_at": datetime.now(),
  }
]

# Thread history loader
def conversation_history_loader(thread):
  """ History is rendered in the order received """
  messages = []
  count = 0
  history = graph.get_state_history(config)
  for event in history:
    print("EVENT: ", event.values['messages'])
    return event.values['messages']
  return messages


# Initialize and configure UI
echo = Subconscious()
echo.load_threads(conversation_threads) # Loads the conversation threads into the UI context view
echo.set_thread_history_loader(conversation_history_loader) # Sets the conversation history loader
echo.set_active_thread(config['configurable']['thread_id']) # Sets the active thread
llm.set_settings(echo.settings) # Sets the LLM settings
echo.set_llm_switcher(llm.switch) # Sets the LLM switcher
# echo.splash = False

def new_user_message(message):
  stream_graph_updates(message)

echo.set_new_user_message_callback(new_user_message) # Called when a new user message is sent
echo.load(view="app")
