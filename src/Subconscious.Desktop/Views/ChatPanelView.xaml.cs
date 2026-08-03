using System.Collections.Specialized;
using Subconscious.Desktop.ViewModels;

namespace Subconscious.Desktop.Views;

/// <summary>The left-hand chat panel. Native CollectionView items bind directly to messages, so
/// regular MAUI theme resources and platform scrollbars update without a WebView refresh bridge.</summary>
public partial class ChatPanelView : ContentView
{
    private ChatViewModel? _chat;

    public ChatPanelView()
    {
        InitializeComponent();
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        DetachChat();

        if (BindingContext is ChatViewModel chat)
        {
            _chat = chat;
            chat.Messages.CollectionChanged += OnMessagesCollectionChanged;
        }
    }

    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems?.OfType<MessageViewModel>().LastOrDefault() is not { } newest)
        {
            return;
        }

        // Keep an active conversation following new user/assistant bubbles without rebuilding
        // the visual tree or affecting native scrollbar theming.
        Dispatcher.Dispatch(() => MessagesView.ScrollTo(newest, position: ScrollToPosition.End, animate: false));
    }

    private async void OnCopyMessageClicked(object? sender, EventArgs e)
    {
        if (sender is ImageButton { BindingContext: MessageViewModel message })
        {
            await Clipboard.Default.SetTextAsync(message.Content);
        }
    }

    private void DetachChat()
    {
        if (_chat is not null)
        {
            _chat.Messages.CollectionChanged -= OnMessagesCollectionChanged;
            _chat = null;
        }
    }
}
