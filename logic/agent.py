import wikipedia
from langchain_community.llms import Ollama
from langchain_openai import OpenAIEmbeddings
from langchain.memory import ChatMessageHistory # In memory message store
from langchain_community.vectorstores import FAISS
from langchain_core.output_parsers import StrOutputParser
from langchain_community.document_loaders import WebBaseLoader
from langchain_text_splitters import RecursiveCharacterTextSplitter
from langchain_core.prompts import ChatPromptTemplate, MessagesPlaceholder
from langchain.chains.combine_documents import create_stuff_documents_chain
from langchain_community.chat_message_histories import SQLChatMessageHistory
from langchain.chains import create_history_aware_retriever, create_retrieval_chain

from db.models import *


class ChatDataManager:
  def __init__(self):
    """ Loads the workspaces and chats sessions in order of last updated """
    self.workspaces = session.scalars(select(Workspace).order_by(Workspace.updated_at.desc())).all()
    self.chats = session.scalars(select(Chat).order_by(Chat.updated_at.desc())).all()
    self.settings = session.scalars(select(Settings)).all()
  
  def load_history(self, session_id):
    """ Load the chat history for a specific session """
    return session.scalars(select(Chat).filter_by(id=session_id)).first()



class LLMAgent:
  def __init__(self):
    self.message_chain =  SQLChatMessageHistory(
      session_id="general",
      connection_string="sqlite:///data/chat/chat_history.db",
    )


    self.llm = Ollama(model="phi3")
    self.prompt = ChatPromptTemplate.from_messages(
      [
        (
          "system",
          "You are a helpful assistant. Answer all questions to the best of your ability.",
        ),
        MessagesPlaceholder(variable_name="messages"),
      ]
    )

    self.chain = self.prompt | self.llm

  def send(self, input):
    self.message_chain.add_user_message(input)
    res = self.chain.invoke({"messages": self.message_chain.messages})
    self.message_chain.add_ai_message(res)
    return res
