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
    /// <summary>Fixed width of the persistent left navigation rail.</summary>
    private const double SidebarWidth = 40;

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

        // Fire-and-forget: the panels render immediately with "Connecting…" status; InitializeAsync
        // updates them once the engine handshake completes.
        _ = _viewModel.Chat.InitializeAsync(MauiProgram.DevMode);
    }

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

    private void BeginDrag(Divider divider, PointerEventArgs e)
    {
        if (e.GetPosition(PanelsGrid) is not { } position)
        {
            return;
        }

        _dragging = divider;
        _dragStartPointerX = position.X;
        _dragStartWidth = divider == Divider.Chat
            ? _viewModel.ChatPanelWidth
            : _viewModel.ContextPanelWidth;

        // Keeps the moves coming to the divider even when the pointer outruns its 6px width.
        PointerCapture.Capture(e, sender: divider == Divider.Chat ? ChatSplitter : ContextSplitter);
    }

    private void OnPanelsPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging == Divider.None || e.GetPosition(PanelsGrid) is not { } position)
        {
            return;
        }

        var delta = position.X - _dragStartPointerX;

        if (_dragging == Divider.Chat)
        {
            SetChatPanelWidth(_dragStartWidth + delta);
        }
        else
        {
            // The context panel now sits to the left of its divider, so dragging right widens it.
            SetContextPanelWidth(_dragStartWidth + delta);
        }
    }

    private void OnPanelsPointerReleased(object? sender, PointerEventArgs e)
    {
        if (_dragging != Divider.None)
        {
            PointerCapture.Release(e, _dragging == Divider.Chat ? ChatSplitter : ContextSplitter);
        }

        EndDrag();
    }

    private void OnPanelsPointerExited(object? sender, PointerEventArgs e)
    {
        if (_dragging != Divider.None)
        {
            // A capture can be interrupted by leaving the application window. Release it before
            // ending the drag so the next divider press starts from a clean pointer state.
            PointerCapture.Release(e, _dragging == Divider.Chat ? ChatSplitter : ContextSplitter);
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
        // window has had a chance to reserve the center panel's minimum. Clamp the right panel
        // first, then the chat panel, matching the SizeChanged ordering.
        if (e.PropertyName == nameof(MainViewModel.IsContextPanelOpen) && _viewModel.IsContextPanelOpen)
        {
            SetContextPanelWidth(_viewModel.ContextPanelWidth);
            SetChatPanelWidth(_viewModel.ChatPanelWidth);
        }

        if (e.PropertyName is nameof(MainViewModel.ChatPanelWidth)
            or nameof(MainViewModel.ContextPanelWidth)
            or nameof(MainViewModel.IsContextPanelOpen))
        {
            ApplyPanelWidths();
        }
    }

    /// <summary>Pushes the view model's widths into the grid. Column widths are set from code
    /// rather than bound: <c>ColumnDefinition</c> isn't part of the visual tree, so it never
    /// inherits a BindingContext.</summary>
    private void ApplyPanelWidths()
    {
        var open = _viewModel.IsContextPanelOpen;

        PanelsGrid.ColumnDefinitions[1].Width = new GridLength(_viewModel.EffectiveContextPanelWidth);
        PanelsGrid.ColumnDefinitions[2].Width = new GridLength(open ? SplitterThickness : 0);
        PanelsGrid.ColumnDefinitions[3].Width = new GridLength(_viewModel.ChatPanelWidth);
        PanelsGrid.ColumnDefinitions[4].Width = new GridLength(SplitterThickness);

        ContextSplitter.IsVisible = open;

        // Changing ColumnDefinition.Width does move the splitter immediately, but WinUI does not
        // always schedule a measure for the descendant panel trees while a pointer stream is
        // active. Mark the owning grid dirty after every complete width update so chat, main, and
        // context content reflows in the drag's next render pass instead of waiting for another
        // UI interaction.
        PanelsGrid.InvalidateMeasure();
    }
}
