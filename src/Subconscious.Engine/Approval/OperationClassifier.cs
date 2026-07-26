using System.Collections.Frozen;

namespace Subconscious.Engine.Approval;

/// <summary>
/// Classifies a tool name as a <see cref="OperationKind.Query"/> (read-only) or
/// <see cref="OperationKind.Mutation"/> (side-effecting) operation, driving human-in-the-loop
/// approval. Ported 1:1 from <c>tools/__init__.py</c>'s <c>classify_operation</c>: explicit
/// tool-name sets win; otherwise a read-oriented name prefix marks a query and everything
/// else defaults to mutation (approval-gated by default for unknown/new tools).
/// </summary>
public static class OperationClassifier
{
    // Explicit classification for every known built-in and desktop tool (tools/__init__.py
    // _MUTATION_TOOLS). Ported verbatim so behavior parity with the Python HITL gate holds
    // once the corresponding tool sets land in Phase 3.
    private static readonly FrozenSet<string> MutationTools = new[]
    {
        // todo
        "add_todo", "update_todo", "complete_todo", "delete_todo",
        // memory
        "remember", "forget", "forget_all",
        // notes
        "save_note", "delete_note",
        // contacts
        "add_contact", "update_contact", "delete_contact",
        // terminal
        "run_command", "run_terminal_command", "open_terminal_session",
        "run_in_session", "close_terminal_session",
        // settings
        "update_app_setting", "set_theme_mode",
        // images (write output files)
        "optimize_image", "convert_image", "batch_optimize_images",
        "batch_convert_image", "resize_image", "batch_resize_images",
        "images_to_pdf", "pdf_to_images",
        // filesystem
        "create_file", "move_to_trash", "replace_in_file",
        // clipboard
        "write_clipboard",
        // desktop automation (control the local machine's input devices)
        "move_mouse", "click_mouse", "double_click_mouse", "drag_mouse",
        "scroll_mouse", "type_text", "press_key", "press_hotkey",
    }.ToFrozenSet(StringComparer.Ordinal);

    // tools/__init__.py _QUERY_TOOLS
    private static readonly FrozenSet<string> QueryTools = new[]
    {
        // time
        "get_current_time", "get_current_date", "convert_timezone", "list_common_timezones",
        // calculator
        "calculate", "convert_units", "list_supported_units",
        // weather
        "get_weather", "get_forecast",
        // todo / memory / notes / contacts reads
        "list_todos", "recall", "list_memories", "list_notes", "get_note",
        "list_contacts", "find_contact",
        // web
        "fetch_page", "search_web", "check_connectivity", "speed_test",
        // knowledge retrieval (RAG / GraphRAG) — read-only
        "search_knowledge", "search_knowledge_graph",
        // terminal reads
        "get_env_var", "get_system_info",
        // settings reads
        "get_app_setting",
        // search / filesystem reads
        "search_fs", "read_file", "read_range", "search_in_file", "search_files",
        "list_directory", "get_file_info", "get_directory_tree", "find_symbol",
        // clipboard reads
        "read_clipboard",
        // desktop automation reads (screen inspection, no side effects)
        "get_screen_size", "get_mouse_position", "get_pixel_color",
        "capture_screenshot", "locate_on_screen",
    }.ToFrozenSet(StringComparer.Ordinal);

    // tools/__init__.py _QUERY_PREFIXES — name-prefix heuristic for tools not in the
    // explicit maps (e.g. user-added).
    private static readonly string[] QueryPrefixes =
    [
        "get_", "list_", "read_", "find_", "search_", "fetch_", "check_",
        "view_", "show_", "describe_", "lookup_", "recall",
    ];

    /// <summary>
    /// Return <see cref="OperationKind.Query"/> or <see cref="OperationKind.Mutation"/> for a
    /// tool name. Explicit classifications win; otherwise a read-oriented name prefix marks
    /// a query and everything else defaults to mutation (approval-gated).
    /// </summary>
    public static OperationKind Classify(string toolName)
    {
        if (MutationTools.Contains(toolName))
        {
            return OperationKind.Mutation;
        }
        if (QueryTools.Contains(toolName))
        {
            return OperationKind.Query;
        }
        foreach (var prefix in QueryPrefixes)
        {
            if (toolName.StartsWith(prefix, StringComparison.Ordinal))
            {
                return OperationKind.Query;
            }
        }
        return OperationKind.Mutation;
    }
}
