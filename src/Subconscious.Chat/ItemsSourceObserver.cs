using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Subconscious.Chat;

/// <summary>
/// Observes an enumerable transcript, including collection changes and changes to each message.
/// </summary>
public sealed class ItemsSourceObserver : IDisposable
{
    private readonly HashSet<IChatTranscriptMessage> _subscribedMessages =
        new(ReferenceEqualityComparer.Instance);
    private IEnumerable _itemsSource;
    private INotifyCollectionChanged? _observableCollection;
    private IReadOnlyList<IChatTranscriptMessage> _messages = Array.Empty<IChatTranscriptMessage>();
    private bool _isDisposed;

    public ItemsSourceObserver(IEnumerable itemsSource)
    {
        _itemsSource = itemsSource ?? throw new ArgumentNullException(nameof(itemsSource));
        SubscribeToSource();
        RebuildMessages();
    }

    public event EventHandler? Changed;

    /// <summary>The currently projected messages, in ItemsSource order.</summary>
    public IReadOnlyList<IChatTranscriptMessage> CurrentMessages => _messages;

    public IReadOnlyList<IChatTranscriptMessage> Messages => CurrentMessages;

    public void SetItemsSource(IEnumerable itemsSource)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(itemsSource);
        if (ReferenceEquals(_itemsSource, itemsSource))
        {
            return;
        }

        UnsubscribeFromSource();
        _itemsSource = itemsSource;
        SubscribeToSource();
        RebuildMessages();
        OnChanged();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        UnsubscribeFromSource();
        GC.SuppressFinalize(this);
    }

    private void SubscribeToSource()
    {
        _observableCollection = _itemsSource as INotifyCollectionChanged;
        if (_observableCollection is not null)
        {
            _observableCollection.CollectionChanged += OnCollectionChanged;
        }
    }

    private void RebuildMessages()
    {
        UnsubscribeFromMessages();

        var messages = new List<IChatTranscriptMessage>();
        foreach (var item in _itemsSource)
        {
            if (item is IChatTranscriptMessage message)
            {
                messages.Add(message);
                if (_subscribedMessages.Add(message))
                {
                    message.PropertyChanged += OnMessagePropertyChanged;
                }
            }
        }

        _messages = messages.AsReadOnly();
    }

    private void UnsubscribeFromSource()
    {
        if (_observableCollection is not null)
        {
            _observableCollection.CollectionChanged -= OnCollectionChanged;
            _observableCollection = null;
        }

        UnsubscribeFromMessages();
    }

    private void UnsubscribeFromMessages()
    {
        foreach (var message in _subscribedMessages)
        {
            message.PropertyChanged -= OnMessagePropertyChanged;
        }

        _subscribedMessages.Clear();
        _messages = Array.Empty<IChatTranscriptMessage>();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (_isDisposed)
        {
            return;
        }

        // Rebuilding makes Add, Remove, Replace, Move, and Reset follow one consistent path.
        RebuildMessages();
        OnChanged();
    }

    private void OnMessagePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!_isDisposed)
        {
            OnChanged();
        }
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
