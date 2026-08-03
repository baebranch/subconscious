using System.Collections.ObjectModel;

namespace Subconscious.Mobile;

public partial class MainPage : ContentPage
{
	/// <summary>Backs the CollectionView in MainPage.xaml. ObservableCollection raises
	/// CollectionChanged itself, so the UI updates on Add without any extra plumbing.</summary>
	public ObservableCollection<ChatMessage> Messages { get; } = new();

	public MainPage()
	{
		InitializeComponent();

		// MainPage acts as its own (minimal) view model for this dummy slice —
		// no separate class needed just to expose one collection.
		BindingContext = this;

		Messages.Add(new ChatMessage("Hey! Ask me anything and I'll echo it back for now.", isFromUser: false));
	}

	private async void OnSendClicked(object? sender, EventArgs e)
	{
		var text = MessageEditor.Text?.Trim();
		if (string.IsNullOrEmpty(text))
		{
			return;
		}

		MessageEditor.Text = string.Empty;

		Messages.Add(new ChatMessage(text, isFromUser: true));
		ScrollToLatest();

		var reply = await EchoAsync(text);

		Messages.Add(new ChatMessage(reply, isFromUser: false));
		ScrollToLatest();
	}

	/// <summary>
	/// Placeholder "bot" response. Swap this out for a real call into Subconscious.Engine
	/// once the mobile client is wired up to it — the rest of the chat UI won't need to change.
	/// </summary>
	private static async Task<string> EchoAsync(string message)
	{
		await Task.Delay(300); // small delay so it reads like a real round trip
		return $"Echo: {message}";
	}

	private void ScrollToLatest()
	{
		if (Messages.Count > 0)
		{
			MessagesView.ScrollTo(Messages[^1], position: ScrollToPosition.End, animate: true);
		}
	}
}
