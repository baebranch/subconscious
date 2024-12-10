""" Simple echo example using the subconscious UI """
from time import sleep
from datetime import datetime
from src.subconscious import Subconscious

# Message id
msg_id = 0

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

def stream_response(response):
  """ Demo response streamer """
  global msg_id
  text = response["text"]
  message = response.copy()
  message["text"] = ""
  message["id"] = f"msg-{msg_id}"
  msg_id += 1
  for char in text:
    sleep(0.001) # Simulate a delay
    message["text"] = char
    echo.stream_response(message) # Echo the response

# Initialize and configure UI
echo = Subconscious()
echo.load_threads(conversation_threads) # Loads the conversation threads into the UI
echo.set_thread_history_loader(conversation_history_loader) # Sets the conversation history loader
echo.splash = False

def new_user_message(message):
  print(f"User message: {message}")
  sleep(2) # Simulate a delay
  msg = message
  msg["is_sender"] = False
  msg["created_at"] = datetime.now()
  msg["thread"] = "general"
  print(f"Echo message: {msg}")

  # echo.send_response(msg) # Echo the user message
  stream_response(msg) # Stream the response

echo.set_new_user_message_callback(new_user_message) # Called when a new user message is sent
# echo.send_response(response) # Called when a new receiver message is sent
echo.load(view="app")
