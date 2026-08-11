namespace Subconscious.Desktop.ViewModels;

/// <summary>Bundled in-memory files for exercising each FileEditorView surface without a workspace.</summary>
internal static class FileEditorSamples
{
    private const string SampleWorkspaceId = "built-in-file-editor-samples";

    public static IReadOnlyList<FileEditorTab> CreateTabs() =>
    [
        new FileEditorTab(SampleWorkspaceId, -1, "welcome.md", "welcome.md", "# File editor samples\n\nMarkdown is presented as **rendered editable content** with a formatting toolbar.\n\n- Open `calculator.cs` to try numbered, highlighted, foldable code.\n- Open `notes.txt` for a plain text editor."),
        new FileEditorTab(SampleWorkspaceId, -1, "calculator.cs", "calculator.cs", "namespace Samples;\n\npublic static class Calculator\n{\n    public static int Add(int first, int second)\n    {\n        return first + second;\n    }\n\n    public static int Multiply(int first, int second)\n    {\n        return first * second;\n    }\n}"),
        new FileEditorTab(SampleWorkspaceId, -1, "settings.json", "settings.json", "{\n  \"editor\": {\n    \"wordWrap\": true,\n    \"showLineNumbers\": true\n  }\n}"),
        new FileEditorTab(SampleWorkspaceId, -1, "notes.txt", "notes.txt", "Plain text stays deliberately simple.\n\nIt is ideal for notes, logs, and configuration that does not need code features."),
    ];
}
