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
from langchain_huggingface import ChatHuggingFace
from langgraph.checkpoint.sqlite import SqliteSaver
from langchain_google_genai import ChatGoogleGenerativeAI
from langgraph.graph import StateGraph, START, END, MessagesState

from src.subconscious import Subconscious
from src.components.data_objects import HumanMessage


# DB connection and conversation config
conn = sqlite3.connect("./data/memory.db", check_same_thread=False)
memory = SqliteSaver(conn)
config = {"configurable": {"thread_id": "19"}}


# State graph and state definition
class State(TypedDict):
  messages: Annotated[list, add_messages]

graph_builder = StateGraph(State)


class LLM:
  """ Language model manager for switching between different Models """
  model_config = {
    "Anthropic": ChatAnthropic,
    "OpenAI": ChatOpenAI,
    "Ollama": ChatOllama,
    "Google": ChatGoogleGenerativeAI,
    "Hugging Face": ChatHuggingFace,
  }

  def invoke(self, messages):
    return self.model.invoke(messages)
    
  def switch(self, e):
    """ Switch the model """
    # Update the settings
    self.settings["_general"]["llm"] = e.data
    print(f"Switching to {e.data}")

    # Configure the new model
    try:
      self.model = self.model_config[self.settings["_models"][e.data]["provider"]](
        api_key = self.settings["_models"][e.data]["api_key"],
        model = self.settings["_models"][e.data]["model"]
      )
    except Exception as e:
      print(f"Error switching model: {e}")

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
        print(f"Error configuring model: {e}")


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
echo.set_thread_history_loader(conversation_history_loader) # Sets the conversation history loader
echo.set_active_thread(config['configurable']['thread_id']) # Sets the active thread
llm.set_settings(echo.settings) # Sets the LLM settings
echo.set_llm_switcher(llm.switch) # Sets the LLM switcher
echo.splash = False

def new_user_message(message):
  stream_graph_updates(message)

echo.set_new_user_message_callback(new_user_message) # Called when a new user message is sent
echo.load(view="app")
