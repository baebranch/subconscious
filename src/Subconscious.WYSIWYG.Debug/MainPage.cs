using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using Subconscious.WYSIWYG;

namespace Subconscious.WYSIWYG.Debug;

public sealed class MainPage : ContentPage
{
    private readonly List<DebugDocument> _fixtures = CreateFixtures();
    private readonly ObservableCollection<DebugDocument> _documents = [];
    private readonly EditorWorkspaceView _workspace = new();
    private readonly Label _status = new() { FontSize = 12, LineBreakMode = LineBreakMode.TailTruncation };
    private readonly Grid _header = new() { Padding = new Thickness(12, 8), ColumnSpacing = 8 };
    private EditorTheme _theme = EditorTheme.Light;

    public MainPage()
    {
        Title = "Standalone WYSIWYG test host";
        RestoreFixtures();
        _workspace.ItemsSource = _documents;
        _workspace.SelectedDocument = _documents[0];
        _workspace.CloseCommand = new Command(value => CloseDocument((DebugDocument)value));
        _header.ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new(), new()];
        var title = new Label { Text = "Standalone WYSIWYG test host — no Desktop / Engine", FontAttributes = FontAttributes.Bold, FontSize = 15 };
        _header.Add(title);
        var reset = MakeButton("Reset selected");
        reset.Clicked += (_, _) => ResetSelected();
        _header.Add(reset, 1);
        var theme = MakeButton("Toggle theme");
        theme.Clicked += (_, _) => ((App)Application.Current!).ToggleTheme();
        _header.Add(theme, 2);
        var picks = new HorizontalStackLayout { Padding = new Thickness(12, 0, 12, 8), Spacing = 6 };
        foreach (var document in _documents) picks.Add(MakePicker(document));
        var restore = MakeButton("Restore fixtures");
        restore.Clicked += (_, _) => { RestoreFixtures(); _workspace.SelectedDocument = _documents[0]; };
        picks.Add(restore);
        Content = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)], Children = { _header, picks, _workspace, _status } };
        Grid.SetRow(picks, 1); Grid.SetRow(_workspace, 2); Grid.SetRow(_status, 3);
        SetTheme(_theme);
        SetStatus("Ready: choose a fixture, edit it, and switch tabs repeatedly.");
    }

    public void SetTheme(EditorTheme theme)
    {
        _theme = theme;
        BackgroundColor = theme.Surface;
        _header.BackgroundColor = theme.Panel;
        _status.TextColor = theme.MutedText;
        _workspace.Theme = theme;
    }

    private Button MakeButton(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Padding = new Thickness(10, 5),
        CornerRadius = 4,
        BackgroundColor = _theme.Hover,
        TextColor = _theme.Text,
    };

    private Button MakePicker(DebugDocument document)
    {
        var button = MakeButton(document.DisplayName);
        button.Clicked += (_, _) => SelectDocument(document);
        return button;
    }

    private void SelectDocument(DebugDocument document)
    {
        if (!_documents.Contains(document)) return;
        _workspace.SelectedDocument = document;
        SetStatus($"Selected {document.DisplayName} ({document.Kind}).");
    }

    private void ResetSelected()
    {
        if (_workspace.SelectedDocument is not DebugDocument document) return;
        document.Reset();
        SetStatus($"Reset {document.DisplayName}.");
    }

    private void CloseDocument(DebugDocument document)
    {
        if (!_documents.Remove(document)) return;
        _workspace.SelectedDocument = _documents.FirstOrDefault();
        SetStatus($"Closed {document.DisplayName}. Use Restore fixtures to reopen it.");
    }

    private void RestoreFixtures()
    {
        foreach (var document in _fixtures) document.Reset();
        _documents.Clear();
        foreach (var document in _fixtures)
        {
            _documents.Add(document);
            document.PropertyChanged -= OnDocumentPropertyChanged;
            document.PropertyChanged += OnDocumentPropertyChanged;
        }
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is DebugDocument document && args.PropertyName is nameof(DebugDocument.Content) or nameof(DebugDocument.IsDirty))
        {
            SetStatus($"{document.DisplayName}: {(document.IsDirty ? "modified" : "clean")} ({document.Content.Length:n0} chars).");
        }
    }

    private void SetStatus(string text)
    {
        _status.Text = text;
        _status.Padding = new Thickness(12, 7);
        _status.BackgroundColor = _theme.Panel;
    }

    private static List<DebugDocument> CreateFixtures() =>
    [
        new("debug:calculator.cs", "calculator.cs", EditorDocumentKind.Code, "csharp", """
            namespace DebugFixtures;

            public static class Calculator
            {
                public static decimal Add(decimal left, decimal right) => left + right;
                public static decimal Multiply(decimal left, decimal right) => left * right;
            }
            """),
        new("debug:welcome.md", "welcome.md", EditorDocumentKind.Markdown, "markdown", """
            # Native Markdown

            This is **editable** Markdown with _native_ rich text.

            - Select text and use the toolbar.
            - Switch tabs while editing.
            """),
        new("debug:notes.txt", "notes.txt", EditorDocumentKind.Text, "text", "Use this plain-text fixture to compare native editing behavior."),
        new("debug:stress.cs", "stress.cs", EditorDocumentKind.Code, "csharp", CreateStressCode()),
    ];

    private static string CreateStressCode()
    {
        var source = new StringBuilder("namespace DebugFixtures;\n\npublic static class StressCode\n{\n");
        for (var index = 0; index < 250; index++)
        {
            source.Append("    public static int Method").Append(index).Append("(int value)\n    {\n")
                .Append("        // Syntax and folding stress fixture\n")
                .Append("        return value + ").Append(index).Append(";\n    }\n\n");
        }
        return source.Append('}').ToString();
    }
}
