using System.Collections.Specialized;
using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;

namespace Subconscious.WYSIWYG;

/// <summary>Reusable closeable tab strip and one active editor/viewer surface.</summary>
public sealed class EditorWorkspaceView : ContentView
{
    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(
        nameof(ItemsSource), typeof(IEnumerable<IEditorDocument>), typeof(EditorWorkspaceView), null,
        propertyChanged: static (view, oldValue, newValue) => ((EditorWorkspaceView)view).OnItemsSourceChanged(oldValue, newValue));
    public static readonly BindableProperty SelectedDocumentProperty = BindableProperty.Create(
        nameof(SelectedDocument), typeof(IEditorDocument), typeof(EditorWorkspaceView), null,
        BindingMode.TwoWay, propertyChanged: static (view, _, _) => ((EditorWorkspaceView)view).LoadSelectedDocument());
    public static readonly BindableProperty CloseCommandProperty = BindableProperty.Create(
        nameof(CloseCommand), typeof(ICommand), typeof(EditorWorkspaceView));
    public static readonly BindableProperty SaveCommandProperty = BindableProperty.Create(
        nameof(SaveCommand), typeof(ICommand), typeof(EditorWorkspaceView));
    public ICommand? SaveCommand
    {
        get => (ICommand?)GetValue(SaveCommandProperty);
        set => SetValue(SaveCommandProperty, value);
    }
    public static readonly BindableProperty ThemeProperty = BindableProperty.Create(
        nameof(Theme), typeof(EditorTheme), typeof(EditorWorkspaceView), EditorTheme.Light,
        propertyChanged: static (view, _, _) => ((EditorWorkspaceView)view).ApplyTheme());

    private readonly HorizontalStackLayout _tabHost = new() { Spacing = 3 };
    private readonly NativeDocumentEditor _textEditor = new()
    {
        Kind = EditorDocumentKind.Text,
        FontSize = 13,
        Placeholder = "Start typing…",
    };
    private readonly CodeEditorView _codeEditor = new();
    private readonly MarkdownEditorView _markdownEditor = new();
    private readonly VerticalStackLayout _documentPlaceholder;
    private bool _updatingDocument;
    private INotifyCollectionChanged? _observableItems;

    public IEnumerable<IEditorDocument>? ItemsSource
    {
        get => (IEnumerable<IEditorDocument>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }
    public IEditorDocument? SelectedDocument
    {
        get => (IEditorDocument?)GetValue(SelectedDocumentProperty);
        set => SetValue(SelectedDocumentProperty, value);
    }
    public ICommand? CloseCommand
    {
        get => (ICommand?)GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }
    public EditorTheme Theme
    {
        get => (EditorTheme)GetValue(ThemeProperty);
        set => SetValue(ThemeProperty, value);
    }

    public EditorWorkspaceView()
    {
        _documentPlaceholder = new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center, Spacing = 8,
            Children =
            {
                new Label { Text = "Viewer required", FontSize = 16, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center },
                new Label { Text = "This document type is view-only and needs a host-provided binary viewer.", FontSize = 12, HorizontalTextAlignment = TextAlignment.Center },
            },
        };

        var tabs = new ScrollView { Orientation = ScrollOrientation.Horizontal, HeightRequest = 35, Content = _tabHost };
        var surfaces = new Grid();
        surfaces.Children.Add(_textEditor); surfaces.Children.Add(_codeEditor);
        surfaces.Children.Add(_markdownEditor); surfaces.Children.Add(_documentPlaceholder);
        Content = new Grid
        {
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) },
            RowSpacing = 0,
            Children = { tabs, surfaces },
        };
        Grid.SetRow(surfaces, 1);

        _textEditor.DocumentTextChanged += OnNativeEditorTextChanged;
        _codeEditor.DocumentTextChanged += OnNativeEditorTextChanged;
        _markdownEditor.DocumentTextChanged += OnNativeEditorTextChanged;
        _textEditor.SaveRequested += OnSaveRequested;
        _codeEditor.SaveRequested += OnSaveRequested;
        _markdownEditor.SaveRequested += OnSaveRequested;
        ApplyTheme();
        LoadSelectedDocument();
    }

    private void OnItemsSourceChanged(object? oldValue, object? newValue)
    {
        if (_observableItems is not null)
        {
            _observableItems.CollectionChanged -= OnCollectionChanged;
        }
        UnsubscribeDocuments(oldValue as IEnumerable<IEditorDocument>);
        _observableItems = newValue as INotifyCollectionChanged;
        if (_observableItems is not null)
        {
            _observableItems.CollectionChanged += OnCollectionChanged;
        }
        SubscribeDocuments(newValue as IEnumerable<IEditorDocument>);
        RebuildTabs();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        UnsubscribeDocuments(args.OldItems?.Cast<IEditorDocument>());
        SubscribeDocuments(args.NewItems?.Cast<IEditorDocument>());
        RebuildTabs();
    }

    private void SubscribeDocuments(IEnumerable<IEditorDocument>? documents)
    {
        if (documents is null) return;
        foreach (var document in documents) document.PropertyChanged += OnDocumentPropertyChanged;
    }

    private void UnsubscribeDocuments(IEnumerable<IEditorDocument>? documents)
    {
        if (documents is null) return;
        foreach (var document in documents) document.PropertyChanged -= OnDocumentPropertyChanged;
    }

    private void OnDocumentPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(IEditorDocument.IsDirty))
        {
            RebuildTabs();
        }
        if (!_updatingDocument && ReferenceEquals(sender, SelectedDocument)
            && args.PropertyName == nameof(IEditorDocument.Content))
        {
            LoadSelectedDocument();
        }
    }

    private void RebuildTabs()
    {
        _tabHost.Children.Clear();
        foreach (var document in ItemsSource ?? [])
        {
            _tabHost.Children.Add(CreateTab(document));
        }
    }

    private View CreateTab(IEditorDocument document)
    {
        var isActive = ReferenceEquals(document, SelectedDocument);
        var title = new Label { Text = document.DisplayName, FontSize = 12, MaxLines = 1,
            LineBreakMode = LineBreakMode.TailTruncation, MaximumWidthRequest = 175, VerticalTextAlignment = TextAlignment.Center };
        var dirty = new Label { Text = "●", FontSize = 8, IsVisible = document.IsDirty,
            TextColor = Theme.Accent, VerticalTextAlignment = TextAlignment.Center };
        var close = new Button { Text = "×", FontSize = 15, Padding = 0, WidthRequest = 22, HeightRequest = 22,
            MinimumWidthRequest = 22, MinimumHeightRequest = 22, BackgroundColor = Colors.Transparent,
            BorderWidth = 0, TextColor = Theme.MutedText };
        SemanticProperties.SetDescription(close, $"Close {document.DisplayName}");
        ToolTipProperties.SetText(close, $"Close {document.DisplayName}");
        close.Clicked += (_, _) =>
        {
            if (CloseCommand?.CanExecute(document) == true) CloseCommand.Execute(document);
        };

        var content = new HorizontalStackLayout
        {
            Spacing = 4,
            Padding = new Thickness(9, 3, 4, 3),
            Children = { title, dirty, close },
        };
        var tabSurface = new Grid
        {
            RowDefinitions = { new RowDefinition(isActive ? 3 : 0), new RowDefinition(GridLength.Star) },
            Children = { new BoxView { Color = Theme.Accent, IsVisible = isActive }, content },
        };
        Grid.SetRow(content, 1);

        // The active tab has a theme-accent top bar rather than a selected fill. It intentionally
        // has no bottom or outer stroke and overlaps the surface by one pixel, making it read as
        // a continuation of the active document instead of a separate floating button.
        var border = new Border
        {
            Content = tabSurface,
            Padding = 0,
            Margin = isActive ? new Thickness(0, 0, 0, -1) : new Thickness(0),
            StrokeThickness = isActive ? 0 : .5,
            Stroke = new SolidColorBrush(isActive ? Colors.Transparent : Theme.Divider),
            BackgroundColor = Theme.Surface,
            StrokeShape = new RoundRectangle { CornerRadius = 4 },
        };
        border.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => SelectedDocument = document) });
        return border;
    }

    private void LoadSelectedDocument()
    {
        RebuildTabs();
        var document = SelectedDocument;
        _textEditor.IsVisible = document?.Kind == EditorDocumentKind.Text;
        _codeEditor.IsVisible = document?.Kind == EditorDocumentKind.Code;
        _markdownEditor.IsVisible = document?.Kind == EditorDocumentKind.Markdown;
        _documentPlaceholder.IsVisible = document?.Kind == EditorDocumentKind.Document;

        if (document?.Kind == EditorDocumentKind.Text)
        {
            _textEditor.LoadDocument(document, Theme);
        }
        else
        {
            _textEditor.ClearDocument(Theme);
        }

        if (document?.Kind == EditorDocumentKind.Code) _codeEditor.LoadDocument(document, Theme);
        else _codeEditor.ClearDocument(Theme);
        if (document?.Kind == EditorDocumentKind.Markdown) _markdownEditor.LoadDocument(document, Theme);
        else _markdownEditor.ClearDocument(Theme);
    }

    private void OnSaveRequested(object? sender, EventArgs args)
    {
        if (SelectedDocument is null or { IsReadOnly: true }
            || SaveCommand?.CanExecute(null) != true)
        {
            return;
        }
        SaveCommand.Execute(null);
    }

    private void OnNativeEditorTextChanged(object? sender, EditorTextChangedEventArgs args)
    {
        var document = SelectedDocument;
        if (document is null || document.IsReadOnly || document.DocumentId != args.DocumentId)
        {
            return;
        }
        UpdateDocument(document, args.Text);
    }

    private void UpdateDocument(IEditorDocument document, string text)
    {
        if (document.Content == text) return;
        _updatingDocument = true;
        try { document.Content = text; }
        finally { _updatingDocument = false; }
    }

    private void ApplyTheme()
    {
        BackgroundColor = Theme.Surface;
        _textEditor.ApplyTheme(Theme);
        _documentPlaceholder.BackgroundColor = Theme.Surface;
        foreach (var label in _documentPlaceholder.Children.OfType<Label>()) label.TextColor = Theme.Text;
        _codeEditor.ApplyTheme(Theme);
        _markdownEditor.ApplyTheme(Theme);
        RebuildTabs();
    }
}
