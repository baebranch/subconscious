namespace Subconscious.Mobile;

/// <summary>
/// A single chat bubble. Immutable — once a message is sent or received it doesn't change.
/// </summary>
public sealed class ChatMessage
{
	public string Text { get; }

	/// <summary>True if this bubble was typed by the user (right-aligned); false for the bot's replies (left-aligned).</summary>
	public bool IsFromUser { get; }

	public ChatMessage(string text, bool isFromUser)
	{
		Text = text;
		IsFromUser = isFromUser;
	}
}
