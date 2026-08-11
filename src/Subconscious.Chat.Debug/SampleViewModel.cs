using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Subconscious.Chat.Debug;

public sealed class SampleViewModel : INotifyPropertyChanged
{
    private bool _loaded;
    private Task? _loadTask;
    private string? _loadError;
    private bool _isLoading;
    private long _themeRevision;
    private int _streamChunk;

    public ObservableCollection<SampleMessage> ItemsSource { get; } = [];
    public long ThemeRevision => _themeRevision;
    public string? LoadError
    {
        get => _loadError;
        private set
        {
            if (_loadError == value) return;
            _loadError = value;
            OnPropertyChanged();
        }
    }
    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading == value) return;
            _isLoading = value;
            OnPropertyChanged();
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;

    public Task LoadAsync()
    {
        if (_loaded)
        {
            return Task.CompletedTask;
        }

        return _loadTask ??= LoadCoreAsync();
    }

    public async Task ReloadAsync()
    {
        if (_loadTask is not null)
        {
            await _loadTask;
            return;
        }

        _loaded = false;
        ItemsSource.Clear();
        await LoadAsync();
    }

    private async Task LoadCoreAsync()
    {
        IsLoading = true;
        LoadError = null;
        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync("messages.json");
            var messages = await JsonSerializer.DeserializeAsync<List<SampleMessageData>>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            var loadedItems = messages.Select(message => new SampleMessage(
                message.Role ?? "assistant", message.Content ?? string.Empty, message.CreatedAt)).ToList();

            var approvalTime = messages.LastOrDefault()?.CreatedAt.AddSeconds(2) ?? DateTime.UtcNow;
            loadedItems.Add(new SampleApprovalRequest(
                "Tool approval required",
                "Allow Read Workspace File to run?",
                "Read a file from the active workspace",
                """
                {
                  "path": "src/Subconscious.Desktop/Views/ChatPanelView.xaml",
                  "encoding": "utf-8"
                }
                """,
                approvalTime));

            ItemsSource.Clear();
            foreach (var item in loadedItems)
            {
                ItemsSource.Add(item);
            }
            _loaded = true;
        }
        catch (Exception exception)
        {
            _loaded = false;
            LoadError = $"Couldn't load sample messages: {exception.Message}";
            Console.Error.WriteLine(LoadError);
        }
        finally
        {
            IsLoading = false;
            _loadTask = null;
        }
    }

    public void IncrementThemeRevision()
    {
        _themeRevision++;
        OnPropertyChanged(nameof(ThemeRevision));
    }

    public void AppendStreamText()
    {
        var message = ItemsSource.LastOrDefault(item => item.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase));
        if (message is not null)
        {
            message.Content += $"\n\nStream chunk {++_streamChunk}.";
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private sealed record SampleMessageData(string? Role, DateTime CreatedAt, string? Content);
}
