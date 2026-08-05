using System.ComponentModel;
using Subconscious.Desktop.ViewModels;

namespace Subconscious.Desktop.Views;

/// <summary>
/// Hosts the three panels and the two dividers between them.
///
/// MAUI has no built-in GridSplitter, so the dividers are driven by
/// <see cref="PointerGestureRecognizer"/>: pressing one records the pointer's X and the panel's
/// current width, each move applies the difference, and releasing persists the result.
///
/// Pointer events rather than a <see cref="PanGestureRecognizer"/> because pan doesn't deliver
/// mouse drags on WinUI (verified: nothing fires for a mouse press-move-release on the divider),
/// and pointer events are the better fit for a desktop-only app anyway - they give absolute
/// positions in whatever coordinate space is asked for.
/// </summary>
public partial class MainPage : ContentPage
{
    /// <summary>Fixed width of the persistent left navigation rail, including its 1px divider.</summary>
    private const double SidebarWidth = 41;

    /// <summary>Divider width. Also the grab area — wide enough to hit with a mouse, narrow
    /// enough to still read as a divider line.</summary>
    private const double SplitterThickness = 6;

    /// <summary>The center (utility) panel never shrinks below this, whatever the dividers do.</summary>
    private const double MinCenterPanelWidth = 320;

    /// <summary>Which divider, if any, the pointer is currently dragging.</summary>
    private enum Divider
    {
        None,
        Chat,
        Context,
    }

    private readonly MainViewModel _viewModel;
    private Divider _dragging;
    private double _dragStartPointerX;
    private double _dragStartWidth;
    private bool _engineStarted;

    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        BindingContext = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Apply the persisted widths before the first frame so the window never flashes the
        // defaults baked into the XAML.
        ApplyPanelWidths();

        SplitterCursor.ApplyResizeCursor(ChatSplitter);
        SplitterCursor.ApplyResizeCursor(ContextSplitter);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (_engineStarted)
        {
            return;
        }
        _engineStarted = true;

        // Fire-and-forget: the panels render immediately with "Connecting…" status; initialization
        // then loads the engine-backed panel configuration once the local API is reachable.
        _ = InitializeEngineBackedStateAsync();
    }

    private Task InitializeEngineBackedStateAsync() =>
        _viewModel.InitializeEngineBackedStateAsync(MauiProgram.DevMode);

    // ── Divider drags ─────────────────────────────────────────────────────────

    private void OnChatSplitterPressed(object? sender, PointerEventArgs e) => BeginDrag(Divider.Chat, e);

    private void OnContextSplitterPressed(object? sender, PointerEventArgs e) => BeginDrag(Divider.Context, e);

    private void OnChatSplitterPointerEntered(object? sender, PointerEventArgs e) =>
        SetSplitterLineColor(ChatDividerLine, "AccentColor");

    private void OnChatSplitterPointerExited(object? sender, PointerEventArgs e) =>
        SetSplitterLineColor(ChatDividerLine, "DividerColor");

    private void OnContextSplitterPointerEntered(object? sender, PointerEventArgs e) =>
        SetSplitterLineColor(ContextDividerLine, "AccentColor");

    private void OnContextSplitterPointerExited(object? sender, PointerEventArgs e) =>
        SetSplitterLineColor(ContextDividerLine, "DividerColor");

    private static void SetSplitterLineColor(BoxView dividerLine, string resourceKey) =>
        dividerLine.SetDynamicResource(BoxView.ColorProperty, resourceKey);

    /// <summary>Returns the non-main panel whose width a physical divider controls. Each of the
    /// two side panels is assigned exactly one divider in every supported panel order.</summary>
    private PanelKind GetControlledPanel(Divider divider, PanelKind[]? order = null)
    {
        order ??= PanelConfigurationCatalog.OrderFor(_viewModel.PanelConfiguration);
        return divider switch
        {
            Divider.Context => order[0] == PanelKind.Main ? order[1] : order[0],
            Divider.Chat => order[2] == PanelKind.Main ? order[1] : order[2],
            _ => throw new InvalidOperationException("A non-divider cannot control a panel."),
        };
    }

    private static Grid SplitterFor(Divider divider, Grid contextSplitter, Grid chatSplitter) =>
        divider == Divider.Context ? contextSplitter : chatSplitter;

    private Grid SplitterFor(Divider divider) => SplitterFor(divider, ContextSplitter, ChatSplitter);

    private static void UpdateSplitterAccessibility(Grid splitter, PanelKind panel)
    {
        var panelName = panel.ToString().ToLowerInvariant();
        SemanticProperties.SetDescription(splitter, $"Resize {panelName} panel");
        ToolTipProperties.SetText(splitter, $"Drag to resize {panelName} panel · double-click to reset");
    }

    private bool IsPanelLeftOfDivider(Divider divider, PanelKind panel)
    {
        var order = PanelConfigurationCatalog.OrderFor(_viewModel.PanelConfiguration);
        var panelIndex = Array.IndexOf(order, panel);
        var dividerAfterIndex = divider == Divider.Context ? 0 : 1;
        return panelIndex <= dividerAfterIndex;
    }

    private double WidthOf(PanelKind panel) => panel switch
    {
        PanelKind.Chat => _viewModel.ChatPanelWidth,
        PanelKind.Context => _viewModel.ContextPanelWidth,
        _ => throw new InvalidOperationException("The main panel has flexible width."),
    };

    private void SetPanelWidth(PanelKind panel, double requested)
    {
        if (panel == PanelKind.Chat)
        {
            SetChatPanelWidth(requested);
        }
        else if (panel == PanelKind.Context)
        {
            SetContextPanelWidth(requested);
        }
    }

    private void BeginDrag(Divider divider, PointerEventArgs e)
    {
        if (e.GetPosition(PanelsGrid) is not { } position)
        {
            return;
        }

        _dragging = divider;
        _dragStartPointerX = position.X;
        _dragStartWidth = WidthOf(GetControlledPanel(divider));

        // Keeps the moves coming to the divider even when the pointer outruns its 6px width.
        PointerCapture.Capture(e, sender: SplitterFor(divider));
    }

    private void OnPanelsPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging == Divider.None || e.GetPosition(PanelsGrid) is not { } position)
        {
            return;
        }

        var panel = GetControlledPanel(_dragging);
        var delta = position.X - _dragStartPointerX;
        var direction = IsPanelLeftOfDivider(_dragging, panel) ? 1 : -1;
        SetPanelWidth(panel, _dragStartWidth + direction * delta);
    }

    private void OnPanelsPointerReleased(object? sender, PointerEventArgs e)
    {
        if (_dragging != Divider.None)
        {
            PointerCapture.Release(e, SplitterFor(_dragging));
        }

        EndDrag();
    }

    private void OnPanelsPointerExited(object? sender, PointerEventArgs e)
    {
        if (_dragging != Divider.None)
        {
            // A capture can be interrupted by leaving the application window. Release it before
            // ending the drag so the next divider press starts from a clean pointer state.
            PointerCapture.Release(e, SplitterFor(_dragging));
        }

        EndDrag();
    }

    /// <summary>Ends the drag and writes the result out. Widths are only persisted here, not on
    /// every pointer move.</summary>
    private void EndDrag()
    {
        if (_dragging == Divider.None)
        {
            return;
        }

        _dragging = Divider.None;
        _viewModel.SaveLayout();
    }

    /// <summary>Window got smaller — re-run the clamps so the panels give space back to the
    /// center instead of squeezing it out of existence.</summary>
    private void OnPanelsGridSizeChanged(object? sender, EventArgs e)
    {
        SetContextPanelWidth(_viewModel.ContextPanelWidth);
        SetChatPanelWidth(_viewModel.ChatPanelWidth);
    }

    private void SetChatPanelWidth(double requested)
    {
        var cap = SpaceForSidePanels() - _viewModel.EffectiveContextPanelWidth;
        _viewModel.ChatPanelWidth = Math.Min(requested, Math.Max(MainViewModel.MinChatPanelWidth, cap));
    }

    private void SetContextPanelWidth(double requested)
    {
        var cap = SpaceForSidePanels() - _viewModel.ChatPanelWidth;
        _viewModel.ContextPanelWidth = Math.Min(requested, Math.Max(MainViewModel.MinContextPanelWidth, cap));
    }

    /// <summary>How much width the two side panels may share between them. Returns
    /// <see cref="double.MaxValue"/> before the first layout pass, when the window size isn't
    /// known yet — the view model's own absolute limits still apply.</summary>
    private double SpaceForSidePanels()
    {
        var available = PanelsGrid.Width > 0 ? PanelsGrid.Width : Width;
        if (available <= 0)
        {
            return double.MaxValue;
        }

        var dividers = SplitterThickness * (_viewModel.IsContextPanelOpen ? 2 : 1);
        return available - SidebarWidth - MinCenterPanelWidth - dividers;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Opening a persisted context panel can otherwise apply its old width before the current
        // window has had a chance to reserve the main panel's minimum. Clamp the two fixed-width
        // panels before remeasuring whichever position the selected configuration assigns them.
        if (e.PropertyName == nameof(MainViewModel.IsContextPanelOpen) && _viewModel.IsContextPanelOpen)
        {
            SetContextPanelWidth(_viewModel.ContextPanelWidth);
            SetChatPanelWidth(_viewModel.ChatPanelWidth);
        }

        if (e.PropertyName is nameof(MainViewModel.ChatPanelWidth)
            or nameof(MainViewModel.ContextPanelWidth)
            or nameof(MainViewModel.IsContextPanelOpen)
            or nameof(MainViewModel.PanelConfiguration))
        {
            ApplyPanelWidths();
        }
    }

    /// <summary>Assigns each panel to its configured grid slot and applies its saved width. The
    /// main panel is the flexible column; chat and context retain independently persisted widths
    /// wherever the selected arrangement places them.</summary>
    private void ApplyPanelWidths()
    {
        var open = _viewModel.IsContextPanelOpen;
        var order = PanelConfigurationCatalog.OrderFor(_viewModel.PanelConfiguration);

        for (var index = 0; index < order.Length; index++)
        {
            var column = 1 + index * 2;
            var panel = order[index];
            PanelsGrid.ColumnDefinitions[column].Width = panel switch
            {
                PanelKind.Chat => new GridLength(_viewModel.ChatPanelWidth),
                PanelKind.Context => new GridLength(open ? _viewModel.ContextPanelWidth : 0),
                PanelKind.Main => GridLength.Star,
                _ => GridLength.Star,
            };

            switch (panel)
            {
                case PanelKind.Chat:
                    Grid.SetColumn(ChatPanel, column);
                    break;
                case PanelKind.Context:
                    Grid.SetColumn(ContextPanel, column);
                    break;
                case PanelKind.Main:
                    Grid.SetColumn(MainPanel, column);
                    break;
            }
        }

        var firstControlledPanel = GetControlledPanel(Divider.Context, order);
        var secondControlledPanel = GetControlledPanel(Divider.Chat, order);
        var firstSplitterVisible = open || firstControlledPanel != PanelKind.Context;
        var secondSplitterVisible = open || secondControlledPanel != PanelKind.Context;

        PanelsGrid.ColumnDefinitions[2].Width = new GridLength(firstSplitterVisible ? SplitterThickness : 0);
        PanelsGrid.ColumnDefinitions[4].Width = new GridLength(secondSplitterVisible ? SplitterThickness : 0);
        ContextSplitter.IsVisible = firstSplitterVisible;
        ChatSplitter.IsVisible = secondSplitterVisible;
        UpdateSplitterAccessibility(ContextSplitter, firstControlledPanel);
        UpdateSplitterAccessibility(ChatSplitter, secondControlledPanel);

        // Changing ColumnDefinition.Width does move the splitters immediately, but WinUI does not
        // always schedule a measure for descendant panel trees while a pointer stream is active.
        PanelsGrid.InvalidateMeasure();
    }
}
