using System.Text;
using System.Threading.Channels;
using Subconscious.Desktop.Engine;

namespace Subconscious.Terminal;

internal sealed class TerminalApp
{
    private const string SettingsClient = "terminal";
    private readonly EngineClient _client;
    private readonly TerminalSession _terminal;
    private readonly TerminalRenderer _renderer;
    private readonly bool _dev;
    private readonly Channel<UiEvent> _events = Channel.CreateUnbounded<UiEvent>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Composer _composer = new();
    private readonly StringBuilder _streaming = new();
    private readonly List<string> _history = [];
    private List<Workspace> _workspaces = [];
    private List<ThreadInfo> _threads = [];
    private List<ModelChoice> _models = [];
    private Workspace? _workspace;
    private ThreadInfo? _thread;
    private ModelChoice? _model;
    private TerminalTheme _theme = TerminalTheme.Default;
    private SelectionOverlay? _selection;
    private PendingApproval? _approval;
    private string? _activeTurnId;
    private string _status = "Starting…";
    private int _historyIndex;
    private bool _connected;
    private bool _logoCommitted;
    private bool _running = true;

    public TerminalApp(EngineClient client, TerminalSession terminal, bool dev)
    {
        _client = client;
        _terminal = terminal;
        _renderer = new TerminalRenderer(terminal);
        _dev = dev;
        _client.ConnectionStatusChanged += (_, value) => Publish(new ConnectionChanged(value));
        _client.ChatDelta += (_, value) => Publish(new DeltaReceived(value));
        _client.ChatDone += (_, value) => Publish(new TurnCompleted(value));
        _client.ChatError += (_, value) => Publish(new TurnFailed(value));
        _client.ChatCancelled += (_, value) => Publish(new TurnCancelled(value));
        _client.ToolApprovalRequested += (_, value) => Publish(new ApprovalRequested(value));
    }

    public async Task<int> RunAsync()
    {
        Render();
        _ = Task.Run(ReadInputLoop);
        if (_terminal.Interactive) _ = Task.Run(WatchSizeAsync);
        await InitializeAsync();
        Render();

        while (_running && !_shutdown.IsCancellationRequested)
        {
            var next = await _events.Reader.ReadAsync(_shutdown.Token);
            var coalesce = next is DeltaReceived;
            await ProcessAsync(next);
            if (coalesce) await Task.Delay(24, _shutdown.Token);
            while (_events.Reader.TryRead(out var queued)) await ProcessAsync(queued);
            if (_running) Render();
        }

        _shutdown.Cancel();
        _renderer.ClearLive();
        return 0;
    }

    private async Task InitializeAsync()
    {
        try
        {
            _status = "Discovering engine…";
            Render();
            await _client.ConnectAsync(_dev);
            _connected = _client.IsConnected;
            _status = "Loading terminal settings…";
            Render();

            var settings = await _client.GetSettingsAsync(client: SettingsClient, cancellationToken: _shutdown.Token);
            _theme = new TerminalTheme(
                TerminalTheme.ParseMode(Setting(settings, "themeMode")),
                TerminalTheme.ParseAccent(Setting(settings, "themeAccent")));
            _renderer.SetTheme(_theme);
            _renderer.CommitLogo();
            _logoCommitted = true;

            _status = "Loading workspaces…";
            Render();
            _workspaces = (await _client.ListWorkspacesAsync(_shutdown.Token))
                .OrderBy(workspace => workspace.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (_workspaces.Count == 0)
            {
                _workspaces.Add(await _client.CreateWorkspaceAsync("Default", cancellationToken: _shutdown.Token));
            }

            var workspaceId = Setting(settings, "activeWorkspace");
            _workspace = _workspaces.FirstOrDefault(item => item.Uuid == workspaceId) ?? _workspaces[0];

            var catalog = await _client.ListModelsAsync(_shutdown.Token);
            var configured = await _client.ListModelConfigurationsAsync(_shutdown.Token);
            _models = catalog.Select(item => new ModelChoice(item.Id, $"{item.Name} · {item.Provider}"))
                .Concat(configured.Select(item => new ModelChoice(
                    item.Id,
                    $"{item.Alias ?? item.Model} · {item.Provider}")))
                .DistinctBy(item => item.Id)
                .ToList();
            var modelId = Setting(settings, "activeModel") ?? _workspace.DefaultModelId;
            _model = _models.FirstOrDefault(item => item.Id == modelId) ?? _models.FirstOrDefault();

            await LoadThreadsAsync(Setting(settings, "activeThread"));
            _status = StatusText();
        }
        catch (Exception exception)
        {
            _connected = false;
            _status = "Connection failed";
            if (!_logoCommitted)
            {
                _renderer.CommitLogo();
                _logoCommitted = true;
            }
            _renderer.CommitNotice($"Unable to reach the Subconscious engine: {exception.Message}", true);
        }
    }

    private async Task LoadThreadsAsync(string? preferredThreadId = null)
    {
        if (_workspace is null) return;
        _threads = (await _client.ListThreadsAsync(_workspace.Uuid, _shutdown.Token))
            .OrderByDescending(thread => thread.UpdatedAt).ToList();
        _thread = _threads.FirstOrDefault(item => item.Uuid == preferredThreadId) ?? _threads.FirstOrDefault();
        if (_thread is null)
        {
            _renderer.CommitSection("New conversation", _workspace.Name);
            return;
        }
        await CommitHistoryAsync(_thread);
    }

    private async Task CommitHistoryAsync(ThreadInfo thread)
    {
        _renderer.CommitSection(thread.Title ?? "Untitled thread", _workspace?.Name);
        var messages = (await _client.ListMessagesAsync(thread.Uuid, _shutdown.Token))
            .OrderBy(message => message.CreatedAt).ToList();
        foreach (var message in messages) _renderer.CommitMessage(message.Role, message.Content);
        if (messages.Count == 0) _renderer.CommitNotice("No messages yet.");
    }

    private async Task ProcessAsync(UiEvent message)
    {
        switch (message)
        {
            case KeyPressed key:
                await HandleKeyAsync(key.Key);
                break;
            case LineSubmitted line:
                await SubmitAsync(line.Text);
                break;
            case InputClosed:
                _running = false;
                break;
            case ConnectionChanged connection:
                _connected = connection.Connected;
                _status = connection.Connected ? StatusText() : "Disconnected — reconnecting…";
                break;
            case DeltaReceived delta when IsActive(delta.Value.TurnId):
                _streaming.Append(delta.Value.Delta);
                _renderer.AppendPlainDelta(delta.Value.Delta);
                break;
            case TurnCompleted done when IsActive(done.Value.TurnId):
                await CompleteTurnAsync(done.Value.ThreadUuid, cancelled: false);
                break;
            case TurnCancelled cancelled when IsActive(cancelled.Value.TurnId):
                await CompleteTurnAsync(cancelled.Value.ThreadUuid, cancelled: true);
                break;
            case TurnFailed failed when IsActive(failed.Value.TurnId):
                await FailTurnAsync(failed.Value.Error);
                break;
            case ApprovalRequested approval when IsActive(approval.Value.TurnId):
                _approval = new PendingApproval(approval.Value);
                _status = $"Approval required · {approval.Value.ToolName}";
                break;
            case TerminalResized:
                break;
        }
    }

    private async Task HandleKeyAsync(ConsoleKeyInfo key)
    {
        if (_approval is not null)
        {
            await HandleApprovalKeyAsync(key);
            return;
        }
        if (_selection is not null)
        {
            await HandleSelectionKeyAsync(key);
            return;
        }

        var control = key.Modifiers.HasFlag(ConsoleModifiers.Control);
        if (control && key.Key == ConsoleKey.C)
        {
            if (_activeTurnId is not null) await CancelActiveTurnAsync();
            else if (!_composer.IsEmpty) _composer.Clear();
            else _running = false;
            return;
        }
        if (control && key.Key == ConsoleKey.L)
        {
            _renderer.ClearScreen();
            _renderer.CommitLogo();
            return;
        }
        if (key.Key == ConsoleKey.Escape && _activeTurnId is not null)
        {
            await CancelActiveTurnAsync();
            return;
        }
        if (key.Key == ConsoleKey.UpArrow && !key.Modifiers.HasFlag(ConsoleModifiers.Shift))
        {
            RecallHistory(-1);
            return;
        }
        if (key.Key == ConsoleKey.DownArrow && !key.Modifiers.HasFlag(ConsoleModifiers.Shift))
        {
            RecallHistory(1);
            return;
        }
        if (key.Key == ConsoleKey.Tab)
        {
            CompleteCommand();
            return;
        }

        var action = _composer.Apply(key);
        if (action == ComposerAction.Submit) await SubmitAsync(_composer.Take());
        else if (action == ComposerAction.Changed) _historyIndex = _history.Count;
    }

    private async Task HandleApprovalKeyAsync(ConsoleKeyInfo key)
    {
        if (_approval is null) return;
        if (key.Key is ConsoleKey.LeftArrow or ConsoleKey.N)
        {
            _approval = _approval with { ApproveSelected = false };
            if (key.Key == ConsoleKey.N) await ResolveApprovalAsync(false);
        }
        else if (key.Key is ConsoleKey.RightArrow or ConsoleKey.Y)
        {
            _approval = _approval with { ApproveSelected = true };
            if (key.Key == ConsoleKey.Y) await ResolveApprovalAsync(true);
        }
        else if (key.Key == ConsoleKey.Enter)
        {
            await ResolveApprovalAsync(_approval.ApproveSelected);
        }
        else if (key.Key == ConsoleKey.Escape)
        {
            await ResolveApprovalAsync(false);
        }
    }

    private async Task ResolveApprovalAsync(bool approve)
    {
        var pending = _approval;
        if (pending is null) return;
        try
        {
            await _client.ResolveToolApprovalAsync(
                pending.Request.TurnId,
                pending.Request.ApprovalId,
                approve,
                _shutdown.Token);
            _renderer.CommitNotice($"{pending.Request.ToolName}: {(approve ? "approved" : "denied")}");
            _approval = null;
            _status = "Generating…";
        }
        catch (Exception exception)
        {
            _status = $"Approval failed: {exception.Message}";
        }
    }

    private async Task HandleSelectionKeyAsync(ConsoleKeyInfo key)
    {
        var overlay = _selection;
        if (overlay is null) return;
        if (key.Key == ConsoleKey.Escape) { _selection = null; return; }
        if (overlay.Items.Count == 0) return;
        if (key.Key == ConsoleKey.UpArrow)
        {
            overlay.SelectedIndex = (overlay.SelectedIndex - 1 + overlay.Items.Count) % overlay.Items.Count;
        }
        else if (key.Key == ConsoleKey.DownArrow)
        {
            overlay.SelectedIndex = (overlay.SelectedIndex + 1) % overlay.Items.Count;
        }
        else if (key.Key == ConsoleKey.Enter && overlay.Selected is { } selected)
        {
            try
            {
                await ApplySelectionAsync(overlay.Kind, selected.Id);
                _selection = null;
            }
            catch (Exception exception)
            {
                _status = $"Unable to select {overlay.Kind.ToString().ToLowerInvariant()}: {exception.Message}";
                _renderer.CommitNotice(_status, true);
            }
        }
    }

    private async Task ApplySelectionAsync(OverlayKind kind, string id)
    {
        if (kind == OverlayKind.Workspaces && _workspaces.FirstOrDefault(item => item.Uuid == id) is { } workspace)
        {
            _workspace = workspace;
            await SaveSettingAsync("activeWorkspace", workspace.Uuid);
            await LoadThreadsAsync();
        }
        else if (kind == OverlayKind.Threads && _threads.FirstOrDefault(item => item.Uuid == id) is { } thread)
        {
            _thread = thread;
            await SaveSettingAsync("activeThread", thread.Uuid);
            await CommitHistoryAsync(thread);
        }
        else if (kind == OverlayKind.Themes)
        {
            await ApplyThemeSelectionAsync(id);
        }
        else if (kind == OverlayKind.Models && _models.FirstOrDefault(item => item.Id == id) is { } model)
        {
            _model = model;
            await SaveSettingAsync("activeModel", model.Id);
            if (_thread is not null) _thread = await _client.UpdateThreadModelAsync(_thread.Uuid, model.Id, _shutdown.Token);
        }
        _status = StatusText();
    }

    private async Task SubmitAsync(string input)
    {
        var content = input.Trim();
        if (content.Length == 0) return;
        _history.Add(content);
        _historyIndex = _history.Count;

        if (content.StartsWith('/'))
        {
            await ExecuteCommandAsync(content);
            return;
        }
        if (_activeTurnId is not null)
        {
            _renderer.CommitNotice("A turn is already running. Press Esc or use /cancel first.");
            return;
        }
        if (!_connected || _workspace is null)
        {
            _renderer.CommitNotice("The engine is not connected.", true);
            return;
        }

        _renderer.CommitMessage("user", content);
        _streaming.Clear();
        _renderer.BeginPlainAssistant();
        _status = "Generating… · Esc cancels";
        try
        {
            _activeTurnId = await _client.SendChatAsync(
                _thread?.Uuid,
                content,
                _thread is null ? _workspace.Uuid : null,
                _model?.Id,
                _shutdown.Token);
        }
        catch (Exception exception)
        {
            _renderer.EndPlainAssistant();
            _renderer.CommitNotice($"Unable to send: {exception.Message}", true);
            _status = StatusText();
        }
    }

    private async Task ExecuteCommandAsync(string input)
    {
        var split = input.Split(' ', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var command = split[0].ToLowerInvariant();
        var argument = split.Length == 2 ? split[1] : null;
        switch (command)
        {
            case "/help":
                _renderer.CommitMessage("commands", HelpText);
                break;
            case "/quit":
            case "/exit":
                _running = false;
                break;
            case "/new":
                if (_activeTurnId is not null) { _renderer.CommitNotice("Cancel the active turn first."); break; }
                _thread = null;
                await SaveSettingAsync("activeThread", string.Empty);
                _renderer.CommitSection("New conversation", _workspace?.Name);
                _status = StatusText();
                break;
            case "/cancel":
                await CancelActiveTurnAsync();
                break;
            case "/clear":
                _renderer.ClearScreen();
                _renderer.CommitLogo();
                break;
            case "/status":
                _renderer.CommitNotice(StatusText());
                break;
            case "/threads":
                OpenSelection(OverlayKind.Threads, "Threads", _threads.Select(item => new SelectionItem(item.Uuid, item.Title ?? "Untitled thread")));
                break;
            case "/thread":
                await SelectByArgumentAsync(OverlayKind.Threads, argument);
                break;
            case "/workspaces":
                OpenSelection(OverlayKind.Workspaces, "Workspaces", _workspaces.Select(item => new SelectionItem(item.Uuid, item.Name)));
                break;
            case "/workspace":
                await SelectByArgumentAsync(OverlayKind.Workspaces, argument);
                break;
            case "/models":
                OpenSelection(OverlayKind.Models, "Models", _models.Select(item => new SelectionItem(item.Id, item.Label)));
                break;
            case "/model":
                await SelectByArgumentAsync(OverlayKind.Models, argument);
                break;
            case "/theme":
                await SelectThemeAsync(argument);
                break;
            default:
                _renderer.CommitNotice($"Unknown command '{command}'. Use /help.", true);
                break;
        }
    }

    private void OpenSelection(OverlayKind kind, string title, IEnumerable<SelectionItem> source)
    {
        var items = source.ToList();
        if (_terminal.Interactive)
        {
            _selection = new SelectionOverlay(kind, title, items);
            return;
        }
        var listing = items.Count == 0
            ? "(none)"
            : string.Join('\n', items.Select((item, index) => $"{index + 1}. {item.Label}"));
        _renderer.CommitMessage(title.ToLowerInvariant(), listing);
    }

    private async Task SelectByArgumentAsync(OverlayKind kind, string? argument)
    {
        var items = kind switch
        {
            OverlayKind.Workspaces => _workspaces.Select(item => new SelectionItem(item.Uuid, item.Name)).ToList(),
            OverlayKind.Threads => _threads.Select(item => new SelectionItem(item.Uuid, item.Title ?? "Untitled thread")).ToList(),
            OverlayKind.Models => _models.Select(item => new SelectionItem(item.Id, item.Label)).ToList(),
            OverlayKind.Themes => ThemeItems(),
            _ => [],
        };
        if (string.IsNullOrWhiteSpace(argument))
        {
            OpenSelection(kind, kind.ToString(), items);
            return;
        }
        SelectionItem? selected = null;
        if (int.TryParse(argument, out var number) && number > 0 && number <= items.Count)
        {
            selected = items[number - 1];
        }
        selected ??= items.FirstOrDefault(item => item.Id.Equals(argument, StringComparison.OrdinalIgnoreCase));
        selected ??= items.FirstOrDefault(item => item.Label.Contains(argument, StringComparison.OrdinalIgnoreCase));
        if (selected is null)
        {
            _renderer.CommitNotice($"No {kind.ToString().ToLowerInvariant()} matched '{argument}'.", true);
            return;
        }
        await ApplySelectionAsync(kind, selected.Id);
    }

    private async Task SelectThemeAsync(string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            OpenSelection(OverlayKind.Themes, $"Theme · {_theme.DisplayName}", ThemeItems());
            return;
        }

        var parts = argument.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        TerminalThemeMode mode;
        TerminalAccent accent;
        TerminalTheme? selected = null;

        if (parts.Length == 1)
        {
            if (TerminalTheme.TryParseMode(parts[0], out mode))
            {
                selected = _theme with { Mode = mode };
            }
            else if (TerminalTheme.TryParseAccent(parts[0], out accent))
            {
                selected = _theme with { Accent = accent };
            }
        }
        else if (parts.Length == 2)
        {
            if (parts[0].Equals("mode", StringComparison.OrdinalIgnoreCase)
                && TerminalTheme.TryParseMode(parts[1], out mode))
            {
                selected = _theme with { Mode = mode };
            }
            else if (parts[0].Equals("accent", StringComparison.OrdinalIgnoreCase)
                && TerminalTheme.TryParseAccent(parts[1], out accent))
            {
                selected = _theme with { Accent = accent };
            }
            else if (TerminalTheme.TryParseMode(parts[0], out mode)
                && TerminalTheme.TryParseAccent(parts[1], out accent))
            {
                selected = new TerminalTheme(mode, accent);
            }
        }

        if (selected is null)
        {
            _renderer.CommitNotice(
                "Usage: /theme [system|light|dark] [purple|blue|teal|green|yellow|orange|red|pink]",
                true);
            return;
        }

        await SetThemeAsync(selected);
    }

    private List<SelectionItem> ThemeItems()
    {
        var modes = Enum.GetValues<TerminalThemeMode>().Select(mode => new SelectionItem(
            $"mode:{mode.ToString().ToLowerInvariant()}",
            $"{(mode == _theme.Mode ? "✓" : " ")} Mode · {mode}"));
        var accents = Enum.GetValues<TerminalAccent>().Select(accent => new SelectionItem(
            $"accent:{accent.ToString().ToLowerInvariant()}",
            $"{(accent == _theme.Accent ? "✓" : " ")} Accent · {accent}"));
        return modes.Concat(accents).ToList();
    }

    private async Task ApplyThemeSelectionAsync(string id)
    {
        var parts = id.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            throw new InvalidOperationException($"Invalid theme choice '{id}'.");
        }

        if (parts[0].Equals("mode", StringComparison.OrdinalIgnoreCase)
            && TerminalTheme.TryParseMode(parts[1], out var mode))
        {
            await SetThemeAsync(_theme with { Mode = mode });
            return;
        }

        if (parts[0].Equals("accent", StringComparison.OrdinalIgnoreCase)
            && TerminalTheme.TryParseAccent(parts[1], out var accent))
        {
            await SetThemeAsync(_theme with { Accent = accent });
            return;
        }

        throw new InvalidOperationException($"Invalid theme choice '{id}'.");
    }

    private async Task SetThemeAsync(TerminalTheme theme)
    {
        _theme = theme;
        _renderer.SetTheme(theme);
        if (_client.IsRestConnected)
        {
            await _client.UpdateSettingsAsync([
                new AppStateSetting { Key = "themeMode", Value = theme.ModeValue, Tag = "ui", Client = SettingsClient },
                new AppStateSetting { Key = "themeAccent", Value = theme.AccentValue, Tag = "ui", Client = SettingsClient },
            ], _shutdown.Token);
        }
        _status = StatusText();
        _renderer.CommitNotice($"Theme: {theme.DisplayName}");
    }

    private async Task CancelActiveTurnAsync()
    {
        if (_activeTurnId is null)
        {
            _renderer.CommitNotice("No turn is running.");
            return;
        }
        try
        {
            await _client.CancelChatAsync(_activeTurnId, _shutdown.Token);
            _status = "Cancelling…";
        }
        catch (Exception exception)
        {
            _renderer.CommitNotice($"Cancel failed: {exception.Message}", true);
        }
    }

    private async Task CompleteTurnAsync(string threadUuid, bool cancelled)
    {
        _renderer.EndPlainAssistant();
        if (_terminal.Interactive && _streaming.Length > 0)
        {
            _renderer.CommitMessage("assistant", _streaming.ToString());
        }
        if (cancelled) _renderer.CommitNotice("Turn cancelled.");
        _activeTurnId = null;
        _approval = null;
        _streaming.Clear();

        if (_workspace is not null && !string.IsNullOrWhiteSpace(threadUuid))
        {
            _threads = (await _client.ListThreadsAsync(_workspace.Uuid, _shutdown.Token))
                .OrderByDescending(thread => thread.UpdatedAt).ToList();
            _thread = _threads.FirstOrDefault(item => item.Uuid == threadUuid) ?? _thread;
            if (_thread is not null) await SaveSettingAsync("activeThread", _thread.Uuid);
        }
        _status = StatusText();
    }

    private Task FailTurnAsync(string error)
    {
        _renderer.EndPlainAssistant();
        if (_terminal.Interactive && _streaming.Length > 0) _renderer.CommitMessage("assistant", _streaming.ToString());
        _renderer.CommitNotice($"Turn failed: {error}", true);
        _activeTurnId = null;
        _approval = null;
        _streaming.Clear();
        _status = StatusText();
        return Task.CompletedTask;
    }

    private void RecallHistory(int direction)
    {
        if (_history.Count == 0) return;
        _historyIndex = Math.Clamp(_historyIndex + direction, 0, _history.Count);
        _composer.Replace(_historyIndex == _history.Count ? string.Empty : _history[_historyIndex]);
    }

    private void CompleteCommand()
    {
        var text = _composer.Text;
        if (!text.StartsWith('/') || text.Contains(' ')) return;
        var matches = Commands.Where(command => command.StartsWith(text, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 1) _composer.Replace(matches[0] + " ");
    }

    private async Task SaveSettingAsync(string key, string value)
    {
        if (!_client.IsRestConnected) return;
        await _client.UpdateSettingsAsync([
            new AppStateSetting { Key = key, Value = value, Tag = "ui", Client = SettingsClient },
        ], _shutdown.Token);
    }

    private void Render() => _renderer.Render(new TerminalView(
        _status,
        _streaming.ToString(),
        _composer.Text,
        _composer.Caret,
        _activeTurnId is not null,
        _selection,
        _approval));

    private string StatusText()
    {
        var connection = _connected ? "connected" : "connecting";
        var workspace = _workspace?.Name ?? "no workspace";
        var thread = _thread?.Title ?? "new conversation";
        var model = _model?.Label ?? "default model";
        return $"{connection} · {workspace} · {thread} · {model} · {_theme.DisplayName} · /help";
    }

    private bool IsActive(string? turnId) =>
        _activeTurnId is not null && string.Equals(_activeTurnId, turnId, StringComparison.Ordinal);

    private void Publish(UiEvent message) => _events.Writer.TryWrite(message);

    private void ReadInputLoop()
    {
        try
        {
            if (_terminal.Interactive)
            {
                while (!_shutdown.IsCancellationRequested) Publish(new KeyPressed(Console.ReadKey(intercept: true)));
            }
            else
            {
                string? line;
                while (!_shutdown.IsCancellationRequested && (line = Console.ReadLine()) is not null)
                {
                    Publish(new LineSubmitted(line));
                }
                Publish(new InputClosed());
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            Publish(new InputClosed());
        }
    }

    private async Task WatchSizeAsync()
    {
        var width = _terminal.Width;
        var height = _terminal.Height;
        while (!_shutdown.IsCancellationRequested)
        {
            await Task.Delay(150, _shutdown.Token);
            var nextWidth = _terminal.Width;
            var nextHeight = _terminal.Height;
            if (nextWidth == width && nextHeight == height) continue;
            width = nextWidth;
            height = nextHeight;
            Publish(new TerminalResized(width, height));
        }
    }

    private static string? Setting(IEnumerable<AppStateSetting> settings, string key) =>
        settings.LastOrDefault(item => item.Key == key)?.Value is { Length: > 0 } value ? value : null;

    private static readonly string[] Commands =
    [
        "/help", "/new", "/threads", "/thread", "/workspaces", "/workspace",
        "/models", "/model", "/theme", "/cancel", "/clear", "/status", "/quit", "/exit",
    ];

    private const string HelpText = """
        /new                         Start a draft conversation
        /threads                     Open the thread picker
        /thread <n|name>             Switch thread
        /workspaces                  Open the workspace picker
        /workspace <n|name>          Switch workspace
        /models                      Open the model picker
        /model <n|id>                Select a model
        /theme                       Open the terminal theme picker
        /theme <mode> [accent]       Set system/light/dark and optional accent
        /theme mode|accent <value>   Set one terminal theme option
        /cancel                      Cancel the active turn
        /clear                       Clear the visible screen
        /status                      Show current selections
        /quit, /exit                 Exit

        Accents: purple, blue, teal, green, yellow, orange, red, pink
        Terminal theme settings are independent from the Desktop theme.

        Enter sends · Shift+Enter or Ctrl+Enter inserts a line
        Esc cancels · Ctrl+C clears/cancels/exits · Ctrl+L clears
        Up/Down recalls history · Tab completes slash commands
        """;
}
