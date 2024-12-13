""" Langgraph implementation using the subconscious UI """
from time import sleep
from datetime import datetime
from src.subconscious import Subconscious

from typing import Annotated
from typing_extensions import TypedDict
from langchain_openai import ChatOpenAI
from langchain_ollama import ChatOllama
from langchain_anthropic import ChatAnthropic
from langgraph.graph.message import add_messages
from langgraph.graph import StateGraph, START, END
from langchain_google_genai import ChatGoogleGenerativeAI


msg_id = 0


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
    # self.model = model

  def invoke(self, messages):
    return self.model.invoke(messages)
    
  def switch(self, e):
    """ Switch the model """
    # Update the settings
    self.settings["_general"]["llm"] = e.data
    print(f"Switching to {self.settings['_general']['llm']}")

    # Configure the new model
    self.model = self.model_config[self.settings["_general"]["llm"]]["chat"](
      api_key = self.settings[self.settings["_general"]["llm"]]["api_key"]["value"] if "api_key" in self.settings[self.settings["_general"]["llm"]] else None,
      model = self.model_config[self.settings["_general"]["llm"]]["model"]
    )
  
  def set_settings(self, settings):
    """ Set the LLM settings and configure the initial model """
    self.settings = settings

    # Conifgure the model
    self.model = self.model_config[self.settings["_general"]["llm"]]["chat"](
      api_key = self.settings[self.settings["_general"]["llm"]]["api_key"]["value"] if "api_key" in self.settings[self.settings["_general"]["llm"]] else None,
      model = self.model_config[self.settings["_general"]["llm"]]["model"]
    )


llm = LLM()
# llm = ChatAnthropic(model="claude-3-5-sonnet-20240620")


def chatbot(state: State):
  return {"messages": [llm.invoke(state["messages"])]}


graph_builder.add_node("chatbot", chatbot)
graph_builder.add_edge(START, "chatbot")
graph_builder.add_edge("chatbot", END)
graph = graph_builder.compile()

def message_template():
  return {
      "id": None,
      "thread": None,
      "text": "",
      "is_sender": False,
      "created_at": datetime.now(),
    }


def stream_graph_updates(user_input: str):
  global msg_id
  msg_id += 1 
  llm_input = message_template()
  llm_input['id'] = msg_id
  for event in graph.stream({"messages": [("user", user_input)]}):
    for value in event.values():
      llm_input['text'] = value["messages"][-1].content
      echo.stream_response(llm_input)
      print("LLM:", value["messages"][-1].content)


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
  return [] # Debug
  return [
    {
      "id": 1,
      "thread_id": "general",
      "text": "Hello",
      "is_sender": True,
      "created_at": datetime.now(),
    },
    {
      "id": 2,
      "thread_id": "general",
      "text": "Hi",
      "is_sender": False,
      "created_at": datetime.now(),
    },
    {
      "id": 3,
      "thread_id": "general",
      "text": "How are you?",
      "is_sender": True,
      "created_at": datetime.now(),
    },
    {
      "id": 4,
      "thread_id": "general",
      "text": "I'm good, thanks",
      "is_sender": False,
      "created_at": datetime.now(),
    },
    {
      "id": 5,
      "thread_id": "general",
      "text": "What are you up to?",
      "is_sender": True,
      "created_at": datetime.now(),
    },
    {
      "id": 6,
      "thread_id": "general",
      "text": "Testing the echo example",
      "is_sender": False,
      "created_at": datetime.now(),
    }
  ]

# Initialize and configure UI
echo = Subconscious()
echo.load_threads(conversation_threads) # Loads the conversation threads into the UI
echo.set_thread_history_loader(conversation_history_loader) # Sets the conversation history loader
llm.set_settings(echo.settings) # Sets the LLM settings
echo.set_llm_switcher(llm.switch) # Sets the LLM switcher
echo.splash = False

def new_user_message(message):
  # stream_graph_updates(message)
  stream_graph_updates(message['text'])

echo.set_new_user_message_callback(new_user_message) # Called when a new user message is sent
# echo.send_response(response) # Called when a new receiver message is sent
echo.load(view="app")
