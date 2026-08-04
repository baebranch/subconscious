namespace Subconscious.Desktop.ViewModels;

/// <summary>The three movable desktop content panels.</summary>
public enum PanelKind
{
    Chat,
    Context,
    Main,
}

/// <summary>The six complete left-to-right arrangements of the desktop panels.</summary>
public enum PanelConfiguration
{
    ContextChatMain,
    ChatContextMain,
    ContextMainChat,
    ChatMainContext,
    MainContextChat,
    MainChatContext,
}

public sealed record PanelConfigurationOption(PanelConfiguration Value, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public static class PanelConfigurationCatalog
{
    public static IReadOnlyList<PanelConfigurationOption> Options { get; } =
    [
        new(PanelConfiguration.ContextChatMain, "Context · Chat · Main"),
        new(PanelConfiguration.ChatContextMain, "Chat · Context · Main"),
        new(PanelConfiguration.ContextMainChat, "Context · Main · Chat"),
        new(PanelConfiguration.ChatMainContext, "Chat · Main · Context"),
        new(PanelConfiguration.MainContextChat, "Main · Context · Chat"),
        new(PanelConfiguration.MainChatContext, "Main · Chat · Context"),
    ];

    public static PanelConfigurationOption OptionFor(PanelConfiguration configuration) =>
        Options.First(option => option.Value == configuration);

    public static PanelKind[] OrderFor(PanelConfiguration configuration) => configuration switch
    {
        PanelConfiguration.ContextChatMain => [PanelKind.Context, PanelKind.Chat, PanelKind.Main],
        PanelConfiguration.ChatContextMain => [PanelKind.Chat, PanelKind.Context, PanelKind.Main],
        PanelConfiguration.ContextMainChat => [PanelKind.Context, PanelKind.Main, PanelKind.Chat],
        PanelConfiguration.ChatMainContext => [PanelKind.Chat, PanelKind.Main, PanelKind.Context],
        PanelConfiguration.MainContextChat => [PanelKind.Main, PanelKind.Context, PanelKind.Chat],
        PanelConfiguration.MainChatContext => [PanelKind.Main, PanelKind.Chat, PanelKind.Context],
        _ => [PanelKind.Context, PanelKind.Chat, PanelKind.Main],
    };
}
