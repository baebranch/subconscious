# Roadmap

The current feature roadmap and development history for the Subconscious Chat UI is as follows:

## Current Sprint Goals

This first iteration should allow for the demonstration of the application stack.
Develop a Simple clean GUI for Subconscious chat interface.

~~General UI/UX~~

- ~~Splash screen~~
- ~~Chat window, shows message history~~
- ~~A single thread for the initial release~~
- ~~A text input box for user input~~
- ~~A settings button and screen for configuring the LLMs~~
- ~~Sidebar popup for switching between LLMs~~
- ~~An about screen for the application~~
- ~~Tray icon for the application to allow background operation~~

Implement local storage for the chat history

- ~~Save chat history to a local file~~
- ~~Load chat history from a local file~~

~~Implement some popular LLM APIs~~

- ~~OpenAI~~
- ~~Gemini~~
- ~~Ollama~~
- ~~Claude~~

The application Should be easy to install for non-technical users.

- ~~Create a simple installer for the application~~
- ~~Create a simple update process for the application~~
- ~~Use github pages for hosting the documentation/site~~

Cool features to add:

- In the background of the chat window, a live feed of the LLM's thought process
  - The option would be known as "view subconscious" in the settings

## Next Sprint Goals

- Truncation of the chat history for performance
- Semantic search for the chat history
- Adding knowledge graph as part of the RAG process
- New message notifications
- What's new screen on update
- Save the window size on resize
-Implement tools for LLM to interact with the user
  - Mail API (read, send, delete, unsubscribe, summarize)
  - File organization tool
  - Computer interaction tool
  - Calendar tool
  - Simple directory RAG system
- A voice input button option and accompanying output and animation
- Use mkdocs for documentation
- A copy button for LLM responses
- If no api key is configured, hide the llm selection button
- On deletion of an API key for an active LLM, another configured LLM should be auto-selected
- On clicking the lower area of the chatbox, the chatbox should be focused
- Scroll position memory for chat history and settings panel (scroll_to method)