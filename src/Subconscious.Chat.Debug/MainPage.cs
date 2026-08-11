using Subconscious.Chat.Native;
// using Subconscious.Chat.Web;

namespace Subconscious.Chat.Debug;

public sealed class MainPage : ContentPage
{
    private readonly SampleViewModel _viewModel;

    public MainPage(RendererKind rendererKind, SampleViewModel viewModel)
    {
        _viewModel = viewModel;
        BindingContext = viewModel;
        Title = $"{rendererKind} chat renderer";
        SetDynamicResource(BackgroundColorProperty, "SurfaceColor");

        var title = new Label
        {
            Text = $"Renderer: {rendererKind} — Fixture: messages.json", FontAttributes = FontAttributes.Bold,
            FontSize = 14, VerticalTextAlignment = TextAlignment.Center,
        };
        title.SetDynamicResource(Label.TextColorProperty, "PrimaryTextColor");
        var loadError = new Label { FontSize = 11, LineBreakMode = LineBreakMode.TailTruncation };
        loadError.SetDynamicResource(Label.TextColorProperty, "ErrorColor");
        loadError.SetBinding(Label.TextProperty, nameof(SampleViewModel.LoadError));
        var titleStack = new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center };
        titleStack.Add(title);
        titleStack.Add(loadError);

        var reloadButton = MakeButton("Reload messages");
        reloadButton.Clicked += async (_, _) => await _viewModel.ReloadAsync();

        var themeButton = MakeButton("Toggle theme");
        themeButton.Clicked += (_, _) =>
        {
            ((App)Application.Current!).ToggleTheme();
            _viewModel.IncrementThemeRevision();
        };
        var appendButton = MakeButton("Append stream text");
        appendButton.Clicked += (_, _) => _viewModel.AppendStreamText();

        var header = new Grid
        {
            Padding = new Thickness(12, 8), ColumnSpacing = 8,
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new(), new(), new()],
        };
        header.SetDynamicResource(BackgroundColorProperty, "PanelBackgroundColor");
        header.Add(titleStack, 0);
        header.Add(reloadButton, 1);
        header.Add(themeButton, 2);
        header.Add(appendButton, 3);

        var renderer = CreateRenderer(rendererKind);
        var layout = new Grid
        {
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)],
        };
        layout.Add(header);
        layout.Add(renderer, 0, 1);
        Content = layout;
        Loaded += async (_, _) => await _viewModel.LoadAsync();
    }

    private static Button MakeButton(string text)
    {
        var button = new Button
        {
            Text = text, FontSize = 12, Padding = new Thickness(12, 6),
            CornerRadius = 5, VerticalOptions = LayoutOptions.Center,
        };
        button.SetDynamicResource(BackgroundColorProperty, "AccentColor");
        button.SetDynamicResource(Button.TextColorProperty, "SurfaceColor");
        return button;
    }

    private static View CreateRenderer(RendererKind rendererKind)
    {
        // if (rendererKind == RendererKind.Web)
        // {
        //     var renderer = new WebChatTranscriptView
        //     {
        //         HorizontalOptions = LayoutOptions.Fill,
        //         VerticalOptions = LayoutOptions.Fill,
        //     };
        //     renderer.SetBinding(WebChatTranscriptView.ItemsSourceProperty, nameof(SampleViewModel.ItemsSource));
        //     renderer.SetBinding(WebChatTranscriptView.ThemeRevisionProperty, nameof(SampleViewModel.ThemeRevision));
        //     return renderer;
        // }

        var nativeRenderer = new NativeChatTranscriptView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
        };
        nativeRenderer.SetBinding(NativeChatTranscriptView.ItemsSourceProperty, nameof(SampleViewModel.ItemsSource));
        nativeRenderer.SetBinding(NativeChatTranscriptView.ThemeRevisionProperty, nameof(SampleViewModel.ThemeRevision));
        return nativeRenderer;
    }
}
