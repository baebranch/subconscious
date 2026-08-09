using System.Collections;
using MauiIcons.Core;
using MauiIcons.Fluent;
using Microsoft.Maui.Graphics;
using GraphicsFont = Microsoft.Maui.Graphics.Font;
using Subconscious.Chat;

namespace Subconscious.Chat.Native;

/// <summary>A native-canvas chat transcript with retained text geometry and cross-message selection.</summary>
public sealed class NativeChatTranscriptView : ContentView
{
    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(
        nameof(ItemsSource), typeof(IEnumerable), typeof(NativeChatTranscriptView), default(IEnumerable),
        propertyChanged: static (bindable, _, _) => ((NativeChatTranscriptView)bindable).ReplaceObserver());

    public static readonly BindableProperty ThemeRevisionProperty = BindableProperty.Create(
        nameof(ThemeRevision), typeof(long), typeof(NativeChatTranscriptView), 0L,
        propertyChanged: static (bindable, _, _) => ((NativeChatTranscriptView)bindable).QueueInvalidation());

    public static readonly BindableProperty EmptyTextProperty = BindableProperty.Create(
        nameof(EmptyText), typeof(string), typeof(NativeChatTranscriptView), "Start a conversation.",
        propertyChanged: static (bindable, _, _) => ((NativeChatTranscriptView)bindable).QueueInvalidation());

    public static readonly BindableProperty MaximumBubbleWidthProperty = BindableProperty.Create(
        nameof(MaximumBubbleWidth), typeof(double), typeof(NativeChatTranscriptView), 525d,
        validateValue: static (_, value) => (double)value > 0 && double.IsFinite((double)value),
        propertyChanged: static (bindable, _, _) => ((NativeChatTranscriptView)bindable).QueueInvalidation());

    private readonly ScrollView _scrollView;
    private readonly Grid _surfaceLayer;
    private readonly GraphicsView _surface;
    private readonly AbsoluteLayout _iconLayer;
    private readonly TranscriptDrawable _drawable;
    private ItemsSourceObserver? _observer;
    private bool _invalidateQueued;
    private bool _isLoaded;
    private bool _isSelecting;
    private bool _nearBottom = true;
    private bool _followAfterLayout;
    private int _observedCount;
    private TextPosition? _anchor;
    private TextPosition? _focus;
    private TextPosition? _completedAnchor;
    private TextPosition? _completedFocus;
    private ActionHit? _pressedAction;
    private ActionHit? _hoveredAction;
    private float _pressedX;
    private float _pressedY;
#if WINDOWS
    private bool _isRightPointerPressed;
    private bool _isContextMenuInteraction;
    private Microsoft.UI.Xaml.FrameworkElement? _windowsSurface;
    private Microsoft.UI.Xaml.Input.PointerEventHandler? _pointerPressedHandler;
    private Microsoft.UI.Xaml.Input.PointerEventHandler? _pointerReleasedHandler;
    private Microsoft.UI.Xaml.Input.KeyboardAccelerator? _copyAccelerator;
    private Microsoft.UI.Xaml.Input.KeyboardAccelerator? _selectAllAccelerator;
    private Microsoft.UI.Xaml.Controls.MenuFlyout? _contextFlyout;
    private Microsoft.UI.Xaml.Controls.MenuFlyoutItem? _copyMenuItem;
    private Microsoft.UI.Xaml.Controls.MenuFlyoutItem? _selectAllMenuItem;
#endif

    public NativeChatTranscriptView()
    {
        _drawable = new TranscriptDrawable(this);
        _surface = new GraphicsView { Drawable = _drawable, HeightRequest = 1 };
        _surface.StartInteraction += OnStartInteraction;
        _surface.DragInteraction += OnDragInteraction;
        _surface.EndInteraction += OnEndInteraction;
        _surface.CancelInteraction += OnCancelInteraction;
        _surface.StartHoverInteraction += OnStartHoverInteraction;
        _surface.MoveHoverInteraction += OnMoveHoverInteraction;
        _surface.EndHoverInteraction += OnEndHoverInteraction;
        _surface.HandlerChanged += OnSurfaceHandlerChanged;

        _iconLayer = new AbsoluteLayout
        {
            InputTransparent = true,
            HeightRequest = 1,
        };
        _surfaceLayer = new Grid { HeightRequest = 1 };
        _surfaceLayer.Add(_surface);
        _surfaceLayer.Add(_iconLayer);

        _scrollView = new ScrollView { Orientation = ScrollOrientation.Vertical, Content = _surfaceLayer };
        _scrollView.Scrolled += OnScrolled;
        Content = _scrollView;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) => QueueInvalidation();
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public long ThemeRevision
    {
        get => (long)GetValue(ThemeRevisionProperty);
        set => SetValue(ThemeRevisionProperty, value);
    }

    public string EmptyText
    {
        get => (string)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    public double MaximumBubbleWidth
    {
        get => (double)GetValue(MaximumBubbleWidthProperty);
        set => SetValue(MaximumBubbleWidthProperty, value);
    }

    private IReadOnlyList<IChatTranscriptMessage> Messages =>
        _observer?.CurrentMessages ?? Array.Empty<IChatTranscriptMessage>();

    private void OnLoaded(object? sender, EventArgs e)
    {
        _isLoaded = true;
        EnsureObserver();
        AttachWindowsKeyboardHooks();
        QueueInvalidation();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        _isLoaded = false;
        DisposeObserver();
        DetachWindowsKeyboardHooks();
    }

    private void ReplaceObserver()
    {
        DisposeObserver();
        if (_isLoaded)
        {
            EnsureObserver();
        }
        QueueInvalidation();
    }

    private void EnsureObserver()
    {
        if (_observer is not null || ItemsSource is null)
        {
            return;
        }

        _observer = new ItemsSourceObserver(ItemsSource);
        _observer.Changed += OnItemsChanged;
        _observedCount = _observer.CurrentMessages.Count;
    }

    private void DisposeObserver()
    {
        if (_observer is null)
        {
            return;
        }

        _observer.Changed -= OnItemsChanged;
        _observer.Dispose();
        _observer = null;
        _observedCount = 0;
    }

    private void OnItemsChanged(object? sender, EventArgs e)
    {
        var count = _observer?.CurrentMessages.Count ?? 0;
        if (count > _observedCount && _nearBottom && !_isSelecting)
        {
            _followAfterLayout = true;
        }
        _observedCount = count;
        QueueInvalidation();
    }

    private void QueueInvalidation()
    {
        if (_invalidateQueued)
        {
            return;
        }

        _invalidateQueued = true;
        Dispatcher.Dispatch(() =>
        {
            _invalidateQueued = false;
            _drawable.InvalidateLayout();
            _surface.Invalidate();
        });
    }

    internal void ApplyMeasuredHeight(float height)
    {
        var requested = Math.Max(1, Math.Ceiling(height));
        if (Math.Abs(_surfaceLayer.HeightRequest - requested) < 0.5)
        {
            return;
        }

        Dispatcher.Dispatch(async () =>
        {
            _surface.HeightRequest = requested;
            _surfaceLayer.HeightRequest = requested;
            _iconLayer.HeightRequest = requested;
            if (_followAfterLayout && !_isSelecting)
            {
                _followAfterLayout = false;
                await _scrollView.ScrollToAsync(0, Math.Max(0, requested - _scrollView.Height), false);
                _nearBottom = true;
            }
        });
    }

    private void UpdateCopyIcons(IReadOnlyList<MessageLayout> messages, Color iconColor)
    {
        Dispatcher.Dispatch(() =>
        {
            _iconLayer.Children.Clear();
            foreach (var message in messages)
            {
                var icon = new Image
                {
                    InputTransparent = true,
                    Aspect = Aspect.Center,
                };
                icon.Icon(FluentIcons.Copy24).IconSize(15).IconColor(iconColor);
                AbsoluteLayout.SetLayoutBounds(icon, new Rect(
                    message.CopyBounds.X, message.CopyBounds.Y,
                    message.CopyBounds.Width, message.CopyBounds.Height));
                _iconLayer.Children.Add(icon);
            }
        });
    }

    private void OnScrolled(object? sender, ScrolledEventArgs e)
    {
        var extent = Math.Max(_surface.Height, _surface.HeightRequest);
        _nearBottom = e.ScrollY + _scrollView.Height >= extent - 80;
    }

    private void OnStartHoverInteraction(object? sender, TouchEventArgs e) => UpdateHoveredAction(e);

    private void OnMoveHoverInteraction(object? sender, TouchEventArgs e) => UpdateHoveredAction(e);

    private void OnEndHoverInteraction(object? sender, EventArgs e)
    {
        if (_hoveredAction is null)
        {
            return;
        }

        _hoveredAction = null;
        _surface.Invalidate();
    }

    private void UpdateHoveredAction(TouchEventArgs e)
    {
        var point = FirstTouch(e);
        var action = point is null ? null : _drawable.HitTestAction(point.Value);
        if (action?.Kind != ActionKind.CopyMessage)
        {
            action = null;
        }
        if (Equals(_hoveredAction, action))
        {
            return;
        }

        _hoveredAction = action;
        _surface.Invalidate();
    }

    private void OnStartInteraction(object? sender, TouchEventArgs e)
    {
#if WINDOWS
        if (_isRightPointerPressed || _isContextMenuInteraction)
        {
            return;
        }
#endif
        var point = FirstTouch(e);
        if (point is null)
        {
            return;
        }

        _pressedX = point.Value.X;
        _pressedY = point.Value.Y;
        _pressedAction = _drawable.HitTestAction(point.Value);
        if (_pressedAction is not null)
        {
            return;
        }

        var position = _drawable.HitTestText(point.Value);
        if (position is null)
        {
            ClearSelection();
            return;
        }

        FocusNativeSurface();
        // Do not discard the completed range here. WinUI may deliver GraphicsView's
        // StartInteraction before our native PointerPressed handler, so a right click can enter
        // this path before we know which mouse button was used. The native pointer handler clears
        // this cache for a real left click and restores it for a right click.
        _anchor = position;
        _focus = position;
        _isSelecting = true;
        _surface.Invalidate();
    }

    private void OnDragInteraction(object? sender, TouchEventArgs e)
    {
        var point = FirstTouch(e);
        if (point is null)
        {
            return;
        }

        if (_pressedAction is not null)
        {
            if (Distance(point.Value, new PointF(_pressedX, _pressedY)) > 12)
            {
                _pressedAction = null;
            }
            return;
        }

        if (!_isSelecting)
        {
            return;
        }

        var position = _drawable.HitTestText(point.Value);
        if (position is not null)
        {
            _focus = position;
            // Cache during the drag as well as on release. This makes right-click restoration
            // independent of whether WinUI reports the final left-button EndInteraction.
            RememberCompletedSelection();
            _surface.Invalidate();
        }
    }

    private async void OnEndInteraction(object? sender, TouchEventArgs e)
    {
#if WINDOWS
        if (_isContextMenuInteraction)
        {
            _pressedAction = null;
            _isSelecting = false;
            _surface.Invalidate();
            return;
        }
#endif
        var point = FirstTouch(e);
        if (_pressedAction is { } action && point is not null && action.Bounds.Contains(point.Value))
        {
            await ExecuteActionAsync(action);
        }
        _pressedAction = null;
        _isSelecting = false;
        RememberCompletedSelection();
        _surface.Invalidate();
    }

    private void OnCancelInteraction(object? sender, EventArgs e)
    {
        _pressedAction = null;
        _isSelecting = false;
        RememberCompletedSelection();
        _surface.Invalidate();
    }

    private static PointF? FirstTouch(TouchEventArgs e) => e.Touches.Any() ? e.Touches.First() : null;

    private static float Distance(PointF left, PointF right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        return MathF.Sqrt((dx * dx) + (dy * dy));
    }

    private async Task ExecuteActionAsync(ActionHit action)
    {
        switch (action.Kind)
        {
            case ActionKind.CopyMessage when action.MessageIndex >= 0 && action.MessageIndex < Messages.Count:
                await Clipboard.Default.SetTextAsync(Messages[action.MessageIndex].Content ?? string.Empty);
                break;
            case ActionKind.ToggleTool when action.MessageIndex >= 0 && action.MessageIndex < Messages.Count:
                var command = Messages[action.MessageIndex].ToggleToolExpandedCommand;
                if (command.CanExecute(null))
                {
                    command.Execute(null);
                }
                break;
        }
    }

    private bool HasSelection => _anchor is { } anchor && _focus is { } focus && anchor != focus;

    private void RememberCompletedSelection()
    {
        if (!HasSelection)
        {
            return;
        }

        _completedAnchor = _anchor;
        _completedFocus = _focus;
    }

    private void RestoreCompletedSelection()
    {
        if (_completedAnchor is null || _completedFocus is null)
        {
            return;
        }

        _anchor = _completedAnchor;
        _focus = _completedFocus;
    }

    private void ClearSelection()
    {
        _anchor = null;
        _focus = null;
        _completedAnchor = null;
        _completedFocus = null;
        _isSelecting = false;
        _surface.Invalidate();
    }

    private async Task CopySelectionAsync()
    {
        var text = _drawable.GetSelectedText(_anchor, _focus);
        if (!string.IsNullOrEmpty(text))
        {
            await Clipboard.Default.SetTextAsync(text);
        }
    }

    private void SelectAll()
    {
        var range = _drawable.GetDocumentRange();
        if (range is null)
        {
            return;
        }
        _anchor = range.Value.Start;
        _focus = range.Value.End;
        RememberCompletedSelection();
        _surface.Invalidate();
    }

    private void OnSurfaceHandlerChanged(object? sender, EventArgs e)
    {
        DetachWindowsKeyboardHooks();
        if (_isLoaded)
        {
            AttachWindowsKeyboardHooks();
        }
    }

    private void FocusNativeSurface()
    {
#if WINDOWS
        _windowsSurface?.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
#else
        _surface.Focus();
#endif
    }

    private void AttachWindowsKeyboardHooks()
    {
#if WINDOWS
        if (_windowsSurface is not null || _surface.Handler?.PlatformView is not Microsoft.UI.Xaml.FrameworkElement element)
        {
            return;
        }

        _windowsSurface = element;
        element.IsTabStop = true;
        _pointerPressedHandler = OnWindowsPointerPressed;
        _pointerReleasedHandler = OnWindowsPointerReleased;
        element.AddHandler(Microsoft.UI.Xaml.UIElement.PointerPressedEvent, _pointerPressedHandler, true);
        element.AddHandler(Microsoft.UI.Xaml.UIElement.PointerReleasedEvent, _pointerReleasedHandler, true);
        _copyAccelerator = new Microsoft.UI.Xaml.Input.KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.C,
            Modifiers = Windows.System.VirtualKeyModifiers.Control
        };
        _selectAllAccelerator = new Microsoft.UI.Xaml.Input.KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.A,
            Modifiers = Windows.System.VirtualKeyModifiers.Control
        };
        _copyAccelerator.Invoked += OnCopyAcceleratorInvoked;
        _selectAllAccelerator.Invoked += OnSelectAllAcceleratorInvoked;
        element.KeyboardAccelerators.Add(_copyAccelerator);
        element.KeyboardAccelerators.Add(_selectAllAccelerator);

        _copyMenuItem = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text = "Copy" };
        _selectAllMenuItem = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text = "Select all" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_copyMenuItem, "Copy");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_selectAllMenuItem, "Select all");
        _copyMenuItem.Click += OnCopyMenuItemClick;
        _selectAllMenuItem.Click += OnSelectAllMenuItemClick;

        _contextFlyout = new Microsoft.UI.Xaml.Controls.MenuFlyout();
        _contextFlyout.Items.Add(_copyMenuItem);
        _contextFlyout.Items.Add(_selectAllMenuItem);
        _contextFlyout.Opening += OnContextFlyoutOpening;
        element.ContextFlyout = _contextFlyout;
#endif
    }

    private void DetachWindowsKeyboardHooks()
    {
#if WINDOWS
        if (_windowsSurface is not null)
        {
            if (_pointerPressedHandler is not null)
            {
                _windowsSurface.RemoveHandler(Microsoft.UI.Xaml.UIElement.PointerPressedEvent, _pointerPressedHandler);
            }
            if (_pointerReleasedHandler is not null)
            {
                _windowsSurface.RemoveHandler(Microsoft.UI.Xaml.UIElement.PointerReleasedEvent, _pointerReleasedHandler);
            }
        }
        if (_copyAccelerator is not null)
        {
            _copyAccelerator.Invoked -= OnCopyAcceleratorInvoked;
            _windowsSurface?.KeyboardAccelerators.Remove(_copyAccelerator);
        }
        if (_selectAllAccelerator is not null)
        {
            _selectAllAccelerator.Invoked -= OnSelectAllAcceleratorInvoked;
            _windowsSurface?.KeyboardAccelerators.Remove(_selectAllAccelerator);
        }
        if (_contextFlyout is not null)
        {
            _contextFlyout.Opening -= OnContextFlyoutOpening;
            if (_windowsSurface is not null && ReferenceEquals(_windowsSurface.ContextFlyout, _contextFlyout))
            {
                _windowsSurface.ContextFlyout = null;
            }
        }
        if (_copyMenuItem is not null)
        {
            _copyMenuItem.Click -= OnCopyMenuItemClick;
        }
        if (_selectAllMenuItem is not null)
        {
            _selectAllMenuItem.Click -= OnSelectAllMenuItemClick;
        }

        _copyAccelerator = null;
        _selectAllAccelerator = null;
        _pointerPressedHandler = null;
        _pointerReleasedHandler = null;
        _contextFlyout = null;
        _copyMenuItem = null;
        _selectAllMenuItem = null;
        _isRightPointerPressed = false;
        _isContextMenuInteraction = false;
        _windowsSurface = null;
#endif
    }

#if WINDOWS
    private void OnWindowsPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_windowsSurface is null)
        {
            return;
        }

        _isRightPointerPressed = e.GetCurrentPoint(_windowsSurface).Properties.IsRightButtonPressed;
        _isContextMenuInteraction = _isRightPointerPressed;
        if (_isRightPointerPressed)
        {
            RestoreCompletedSelection();
            _surface.Invalidate();
        }
        else
        {
            // StartInteraction may run before or after this native callback. Clearing only here
            // distinguishes an intentional left-click replacement from a right-click flyout.
            _completedAnchor = null;
            _completedFocus = null;
        }
    }

    private void OnWindowsPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _isRightPointerPressed = false;
    }

    private void OnContextFlyoutOpening(object? sender, object e)
    {
        RestoreCompletedSelection();
        _isRightPointerPressed = false;
        _isContextMenuInteraction = false;
        _surface.Invalidate();
        if (_copyMenuItem is not null)
        {
            _copyMenuItem.IsEnabled = !string.IsNullOrEmpty(_drawable.GetSelectedText(_anchor, _focus));
        }
        if (_selectAllMenuItem is not null)
        {
            _selectAllMenuItem.IsEnabled = _drawable.GetDocumentRange() is not null;
        }
    }

    private async void OnCopyMenuItemClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await CopySelectionAsync();
    }

    private void OnSelectAllMenuItemClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        SelectAll();
    }

    private async void OnCopyAcceleratorInvoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        if (HasSelection)
        {
            await CopySelectionAsync();
            args.Handled = true;
        }
    }

    private void OnSelectAllAcceleratorInvoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        SelectAll();
        args.Handled = true;
    }
#endif

    private (TextPosition Start, TextPosition End)? Selection =>
        _anchor is { } anchor && _focus is { } focus && anchor != focus
            ? TextPosition.Order(anchor, focus)
            : null;

    private sealed class TranscriptDrawable(NativeChatTranscriptView owner) : IDrawable
    {
        private const float OuterPadding = 12;
        private const float BubblePaddingX = 10;
        private const float BubblePaddingY = 8;
        private const float BubbleGap = 8;
        private const float BodyFontSize = 13;
        private const float DetailFontSize = 12;
        private const float TimestampFontSize = 11;
        private const float LineGap = 3;
        private const float ToolHeaderHeight = 40;
        private const float ToolActionSize = 24;
        private const float ToolActionGap = 4;
        private const float ToolTitleTimestampGap = 10;
        private const float ToolTimestampActionGap = 8;
        private readonly NativeChatTranscriptView _owner = owner;
        private readonly List<ActionHit> _actions = [];
        private TranscriptLayout _layout = TranscriptLayout.Empty;
        private bool _layoutDirty = true;
        private float _layoutWidth = -1;

        public void InvalidateLayout() => _layoutDirty = true;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var width = Math.Max(1, dirtyRect.Width);
            var colors = CanvasColors.Resolve();
            if (_layoutDirty || Math.Abs(_layoutWidth - width) > 0.5f)
            {
                _layout = BuildLayout(canvas, width);
                _layoutWidth = width;
                _layoutDirty = false;
                _owner.ApplyMeasuredHeight(_layout.Height);
                _owner.UpdateCopyIcons(_layout.Messages, colors.SecondaryText);
            }

            canvas.FillColor = colors.Surface;
            canvas.FillRectangle(dirtyRect);
            _actions.Clear();

            if (_layout.Messages.Count == 0)
            {
                canvas.Font = GraphicsFont.Default;
                canvas.FontSize = BodyFontSize;
                canvas.FontColor = colors.SecondaryText;
                canvas.DrawString(_owner.EmptyText ?? string.Empty, 0, 20, width, 40,
                    HorizontalAlignment.Center, VerticalAlignment.Center);
                return;
            }

            foreach (var message in _layout.Messages)
            {
                DrawMessage(canvas, message, colors);
            }
        }

        private void DrawMessage(ICanvas canvas, MessageLayout message, CanvasColors colors)
        {
            var bubbleColor = message.IsUser ? colors.UserBubble : colors.AssistantBubble;
            canvas.FillColor = bubbleColor;
            canvas.FillRoundedRectangle(message.Bubble, 6);

            if (_owner._hoveredAction is { Kind: ActionKind.CopyMessage } hovered
                && hovered.MessageIndex == message.MessageIndex)
            {
                canvas.FillColor = Blend(bubbleColor, colors.PrimaryText, 0.12f);
                canvas.FillRoundedRectangle(message.CopyBounds, 4);
            }

            var selection = _owner.Selection;
            if (selection is not null)
            {
                canvas.FillColor = colors.Selection;
                foreach (var rect in SelectionRects(message, selection.Value.Start, selection.Value.End))
                {
                    canvas.FillRoundedRectangle(rect, 2);
                }
            }

            foreach (var line in message.Lines)
            {
                canvas.Font = line.IsHeading ? GraphicsFont.DefaultBold : GraphicsFont.Default;
                canvas.FontSize = line.IsDetail ? DetailFontSize : BodyFontSize;
                canvas.FontColor = line.IsHeading && line.IsDetail
                    ? colors.SecondaryText
                    : colors.PrimaryText;
                canvas.DrawString(line.Text, line.Bounds.X, line.Bounds.Y, line.Bounds.Width, line.Bounds.Height,
                    HorizontalAlignment.Left, VerticalAlignment.Top, TextFlow.ClipBounds);
            }

            canvas.Font = GraphicsFont.Default;
            canvas.FontSize = TimestampFontSize;
            canvas.FontColor = colors.SecondaryText;
            canvas.DrawString(message.Timestamp, message.TimestampBounds.X, message.TimestampBounds.Y,
                message.TimestampBounds.Width, message.TimestampBounds.Height,
                HorizontalAlignment.Right, VerticalAlignment.Center, TextFlow.ClipBounds);

            if (message.IsTool)
            {
                DrawChevron(canvas, message.ToggleBounds, message.IsToolExpanded, colors.SecondaryText);
                // Register the broad header action first. Copy is registered last so reverse hit
                // testing gives the dedicated copy button precedence over the overlapping header.
                _actions.Add(new ActionHit(ActionKind.ToggleTool, message.MessageIndex, message.HeaderBounds));
            }

            _actions.Add(new ActionHit(ActionKind.CopyMessage, message.MessageIndex, message.CopyBounds));
        }

        private static Color Blend(Color background, Color foreground, float foregroundAmount)
        {
            var amount = Math.Clamp(foregroundAmount, 0, 1);
            return Color.FromRgba(
                background.Red + ((foreground.Red - background.Red) * amount),
                background.Green + ((foreground.Green - background.Green) * amount),
                background.Blue + ((foreground.Blue - background.Blue) * amount),
                background.Alpha);
        }

        private static void DrawChevron(ICanvas canvas, RectF bounds, bool expanded, Color color)
        {
            canvas.StrokeColor = color;
            canvas.StrokeSize = 1.5f;
            var cx = bounds.Center.X;
            var cy = bounds.Center.Y;
            if (expanded)
            {
                canvas.DrawLine(cx - 4, cy - 2, cx, cy + 2);
                canvas.DrawLine(cx, cy + 2, cx + 4, cy - 2);
            }
            else
            {
                canvas.DrawLine(cx - 2, cy - 4, cx + 2, cy);
                canvas.DrawLine(cx + 2, cy, cx - 2, cy + 4);
            }
        }

        private TranscriptLayout BuildLayout(ICanvas canvas, float width)
        {
            var projection = ChatTranscriptProjection.Capture(_owner.Messages);
            var messages = new List<MessageLayout>(projection.Messages.Count);
            var available = Math.Max(120, width - (OuterPadding * 2));
            var bubbleCap = (float)Math.Min(_owner.MaximumBubbleWidth, available);
            var y = OuterPadding;

            foreach (var snapshot in projection.Messages)
            {
                var visible = BuildVisibleText(snapshot);
                var timestamp = NormalizeSingleLine(snapshot.Timestamp);
                var naturalWidth = MeasureNaturalWidth(canvas, visible.Text, visible.DetailStart);
                var naturalBubbleWidth = Math.Max(naturalWidth + (BubblePaddingX * 2), 105f);
                // Measure all tool details even while collapsed. The card fits its natural content
                // up to the cap and therefore keeps the same width when it is expanded.
                var bubbleWidth = snapshot.IsTool
                    ? MeasureToolBubbleWidth(canvas, snapshot, timestamp, bubbleCap)
                    : Math.Clamp(naturalBubbleWidth, Math.Min(120, bubbleCap), bubbleCap);
                var bubbleX = snapshot.IsUser ? width - OuterPadding - bubbleWidth : OuterPadding;
                var contentX = bubbleX + BubblePaddingX;
                var contentWidth = bubbleWidth - (BubblePaddingX * 2);
                var lines = new List<TextLineLayout>();
                var lineY = y + BubblePaddingY;
                var headerBounds = RectF.Zero;
                RectF copyBounds;
                RectF toggleBounds;
                RectF timestampBounds;

                if (snapshot.IsTool)
                {
                    headerBounds = new RectF(bubbleX, y, bubbleWidth, ToolHeaderHeight);
                    var actionY = y + ((ToolHeaderHeight - ToolActionSize) / 2);
                    toggleBounds = new RectF(
                        bubbleX + bubbleWidth - BubblePaddingX - ToolActionSize,
                        actionY, ToolActionSize, ToolActionSize);
                    copyBounds = new RectF(
                        toggleBounds.Left - ToolActionGap - ToolActionSize,
                        actionY, ToolActionSize, ToolActionSize);

                    var measuredTimestamp = Measure(canvas, timestamp, TimestampFontSize) + 6;
                    var timestampWidth = Math.Max(ToolActionSize, measuredTimestamp);
                    var timestampRight = copyBounds.Left - ToolTimestampActionGap;
                    var timestampLeft = Math.Max(contentX, timestampRight - timestampWidth);
                    timestampBounds = new RectF(
                        timestampLeft, y,
                        Math.Max(0, timestampRight - timestampLeft), ToolHeaderHeight);

                    var titleRight = timestampBounds.Left - ToolTitleTimestampGap;
                    var titleWidth = Math.Max(16, titleRight - contentX);
                    var originalTitle = snapshot.ToolTitle ?? string.Empty;
                    var title = FitEllipsis(canvas, NormalizeSingleLine(originalTitle), titleWidth, BodyFontSize);
                    visible = visible with
                    {
                        Text = title + visible.Text[originalTitle.Length..],
                        DetailStart = visible.DetailStart < 0
                            ? -1
                            : visible.DetailStart - originalTitle.Length + title.Length
                    };
                    var titleHeight = LineHeight(canvas, BodyFontSize);
                    var titleY = y + ((ToolHeaderHeight - titleHeight) / 2);
                    lines.Add(CreateLine(canvas, title, 0, contentX, titleY, titleWidth, false, true));

                    lineY = y + ToolHeaderHeight;
                    if (snapshot.IsToolExpanded && visible.DetailStart >= 0 && visible.DetailStart < visible.Text.Length)
                    {
                        lineY += 4;
                        WrapText(canvas, visible.Text[visible.DetailStart..], visible.DetailStart,
                            contentX, ref lineY, contentWidth, true, lines);
                    }
                }
                else
                {
                    WrapText(canvas, visible.Text, 0, contentX, ref lineY, contentWidth, false, lines);
                    if (lines.Count == 0)
                    {
                        lines.Add(CreateLine(canvas, string.Empty, 0, contentX, lineY, contentWidth, false, false));
                        lineY += LineHeight(canvas, BodyFontSize) + LineGap;
                    }

                    var footerY = lineY + 3;
                    copyBounds = new RectF(
                        bubbleX + bubbleWidth - BubblePaddingX - 24,
                        footerY, 24, 24);
                    toggleBounds = RectF.Zero;
                    timestampBounds = new RectF(
                        contentX, footerY,
                        Math.Max(20, copyBounds.Left - 6 - contentX), 24);
                }

                var hasExpandedDetails = snapshot.IsTool && snapshot.IsToolExpanded
                    && visible.DetailStart >= 0 && visible.DetailStart < visible.Text.Length;
                var bottom = snapshot.IsTool
                    ? hasExpandedDetails ? lineY + BubblePaddingY : y + ToolHeaderHeight
                    : copyBounds.Bottom + BubblePaddingY;
                var bubble = new RectF(bubbleX, y, bubbleWidth, Math.Max(40, bottom - y));
                if (snapshot.IsTool && !snapshot.IsToolExpanded)
                {
                    // Make every pixel of a collapsed tool card toggle the card.
                    headerBounds = bubble;
                }

                messages.Add(new MessageLayout(snapshot.SourceIndex, snapshot.IsUser, snapshot.IsTool,
                    snapshot.IsToolExpanded, timestamp, visible.Text, bubble, lines,
                    headerBounds, timestampBounds, copyBounds, toggleBounds));
                y = bubble.Bottom + BubbleGap;
            }

            return new TranscriptLayout(messages, Math.Max(60, y));
        }

        private static float MeasureToolBubbleWidth(
            ICanvas canvas, ChatMessageSnapshot snapshot, string timestamp, float bubbleCap)
        {
            var titleWidth = Measure(canvas, NormalizeSingleLine(snapshot.ToolTitle), BodyFontSize) + 8;
            var timestampWidth = Math.Max(
                ToolActionSize, Measure(canvas, timestamp, TimestampFontSize) + 6);
            var headerWidth = (BubblePaddingX * 2)
                + titleWidth
                + ToolTitleTimestampGap
                + timestampWidth
                + ToolTimestampActionGap
                + ToolActionSize
                + ToolActionGap
                + ToolActionSize;

            var detailWidth = 0f;
            if (!string.IsNullOrEmpty(snapshot.ToolInput))
            {
                detailWidth = Math.Max(detailWidth, Measure(canvas, "Input", DetailFontSize));
                detailWidth = Math.Max(detailWidth, MeasureMultilineWidth(canvas, snapshot.ToolInput, DetailFontSize));
            }
            if (!string.IsNullOrEmpty(snapshot.ToolOutput))
            {
                detailWidth = Math.Max(detailWidth, Measure(canvas, "Output", DetailFontSize));
                detailWidth = Math.Max(detailWidth, MeasureMultilineWidth(canvas, snapshot.ToolOutput, DetailFontSize));
            }

            var desiredWidth = Math.Max(headerWidth, detailWidth + (BubblePaddingX * 2));
            return Math.Clamp(desiredWidth, Math.Min(120, bubbleCap), bubbleCap);
        }

        private static float MeasureMultilineWidth(ICanvas canvas, string value, float size)
        {
            var widest = 0f;
            foreach (var line in NormalizeNewlines(value).Split('\n'))
            {
                widest = Math.Max(widest, Measure(canvas, line, size));
            }
            return widest;
        }

        private static VisibleText BuildVisibleText(ChatMessageSnapshot snapshot)
        {
            if (!snapshot.IsTool)
            {
                return new VisibleText(NormalizeNewlines(snapshot.ContentProjection.PlainText), -1);
            }

            var title = snapshot.ToolTitle ?? string.Empty;
            if (!snapshot.IsToolExpanded)
            {
                return new VisibleText(title, -1);
            }

            var sections = new List<string>();
            if (!string.IsNullOrEmpty(snapshot.ToolInput))
            {
                sections.Add($"Input\n{NormalizeNewlines(snapshot.ToolInput)}");
            }
            if (!string.IsNullOrEmpty(snapshot.ToolOutput))
            {
                sections.Add($"Output\n{NormalizeNewlines(snapshot.ToolOutput)}");
            }
            if (sections.Count == 0)
            {
                return new VisibleText(title, -1);
            }

            return new VisibleText(title + "\n\n" + string.Join("\n\n", sections), title.Length + 2);
        }

        private static string NormalizeSingleLine(string? value) => (value ?? string.Empty)
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ');

        private static string NormalizeNewlines(string? value) => (value ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        private static float MeasureNaturalWidth(ICanvas canvas, string text, int detailStart)
        {
            var max = 0f;
            var offset = 0;
            foreach (var line in text.Split('\n'))
            {
                var detail = detailStart >= 0 && offset >= detailStart;
                var size = detail ? DetailFontSize : BodyFontSize;
                max = Math.Max(max, Measure(canvas, line, size));
                offset += line.Length + 1;
            }
            return Math.Min(max, 505);
        }

        private static string FitEllipsis(ICanvas canvas, string value, float width, float size)
        {
            value ??= string.Empty;
            if (Measure(canvas, value, size) <= width)
            {
                return value;
            }
            const string ellipsis = "…";
            var length = value.Length;
            while (length > 0 && Measure(canvas, value[..length] + ellipsis, size) > width)
            {
                length--;
            }
            return value[..length] + ellipsis;
        }

        private static void WrapText(ICanvas canvas, string text, int baseOffset, float x, ref float y,
            float width, bool detail, List<TextLineLayout> output)
        {
            var position = 0;
            while (position <= text.Length)
            {
                var newline = text.IndexOf('\n', position);
                var end = newline < 0 ? text.Length : newline;
                var segment = text[position..end];
                if (segment.Length == 0)
                {
                    var height = LineHeight(canvas, detail ? DetailFontSize : BodyFontSize);
                    output.Add(CreateLine(canvas, string.Empty, baseOffset + position, x, y, width, detail, false));
                    y += height + LineGap;
                }
                else
                {
                    WrapSegment(canvas, segment, baseOffset + position, x, ref y, width, detail, output);
                }

                if (newline < 0)
                {
                    break;
                }
                position = newline + 1;
            }
        }

        private static void WrapSegment(ICanvas canvas, string segment, int baseOffset, float x, ref float y,
            float width, bool detail, List<TextLineLayout> output)
        {
            var cursor = 0;
            while (cursor < segment.Length)
            {
                var fit = cursor;
                var lastBreak = -1;
                while (fit < segment.Length)
                {
                    fit++;
                    if (char.IsWhiteSpace(segment[fit - 1]))
                    {
                        lastBreak = fit;
                    }
                    if (Measure(canvas, segment[cursor..fit], detail ? DetailFontSize : BodyFontSize) > width)
                    {
                        fit--;
                        break;
                    }
                }

                if (fit <= cursor)
                {
                    fit = cursor + 1;
                }
                else if (fit < segment.Length && lastBreak > cursor)
                {
                    fit = lastBreak;
                }

                var lineText = segment[cursor..fit];
                var heading = detail && (lineText == "Input" || lineText == "Output");
                output.Add(CreateLine(canvas, lineText, baseOffset + cursor, x, y, width, detail, heading));
                y += LineHeight(canvas, detail ? DetailFontSize : BodyFontSize) + LineGap;
                cursor = fit;
            }
        }

        private static TextLineLayout CreateLine(ICanvas canvas, string text, int startOffset,
            float x, float y, float maxWidth, bool detail, bool heading)
        {
            var size = detail ? DetailFontSize : BodyFontSize;
            var height = LineHeight(canvas, size);
            var measured = Math.Min(maxWidth, Math.Max(1, Measure(canvas, text, size)));
            var characters = new List<CharacterLayout>(text.Length);
            var previous = 0f;
            for (var index = 0; index < text.Length; index++)
            {
                var current = Math.Min(maxWidth, Measure(canvas, text[..(index + 1)], size));
                characters.Add(new CharacterLayout(startOffset + index,
                    new RectF(x + previous, y, Math.Max(0.5f, current - previous), height)));
                previous = current;
            }

            return new TextLineLayout(text, startOffset,
                new RectF(x, y, measured, height), detail, heading, characters);
        }

        private static float Measure(ICanvas canvas, string text, float size) =>
            string.IsNullOrEmpty(text) ? 0 : canvas.GetStringSize(text, GraphicsFont.Default, size).Width;

        private static float LineHeight(ICanvas canvas, float size) =>
            Math.Max(size + 4, canvas.GetStringSize("Ag", GraphicsFont.Default, size).Height);

        private static IEnumerable<RectF> SelectionRects(MessageLayout message,
            TextPosition start, TextPosition end)
        {
            if (message.MessageIndex < start.MessageIndex || message.MessageIndex > end.MessageIndex)
            {
                yield break;
            }

            var selectionStart = message.MessageIndex == start.MessageIndex ? start.Offset : 0;
            var selectionEnd = message.MessageIndex == end.MessageIndex ? end.Offset : message.VisibleText.Length;
            foreach (var line in message.Lines)
            {
                var selected = line.Characters
                    .Where(character => character.Offset >= selectionStart && character.Offset < selectionEnd)
                    .ToArray();
                if (selected.Length == 0)
                {
                    continue;
                }
                var left = selected[0].Bounds.Left;
                var right = selected[^1].Bounds.Right;
                yield return new RectF(left, line.Bounds.Top, Math.Max(1, right - left), line.Bounds.Height);
            }
        }

        public ActionHit? HitTestAction(PointF point)
        {
            for (var index = _actions.Count - 1; index >= 0; index--)
            {
                if (Contains(_actions[index].Bounds, point))
                {
                    return _actions[index];
                }
            }
            return null;
        }

        public TextPosition? HitTestText(PointF point)
        {
            if (_layout.Messages.Count == 0)
            {
                return null;
            }

            var message = _layout.Messages
                .OrderBy(candidate => VerticalDistance(candidate.Bubble, point.Y))
                .First();
            if (message.Lines.Count == 0)
            {
                return new TextPosition(message.MessageIndex, 0);
            }

            var line = message.Lines
                .OrderBy(candidate => VerticalDistance(candidate.Bounds, point.Y))
                .First();
            if (line.Characters.Count == 0)
            {
                return new TextPosition(message.MessageIndex, Math.Clamp(line.StartOffset, 0, message.VisibleText.Length));
            }
            if (point.X <= line.Characters[0].Bounds.Center.X)
            {
                return new TextPosition(message.MessageIndex, line.Characters[0].Offset);
            }
            foreach (var character in line.Characters)
            {
                if (point.X < character.Bounds.Center.X)
                {
                    return new TextPosition(message.MessageIndex, character.Offset);
                }
            }
            var last = line.Characters[^1];
            return new TextPosition(message.MessageIndex,
                Math.Min(message.VisibleText.Length, last.Offset + 1));
        }

        public string GetSelectedText(TextPosition? anchor, TextPosition? focus)
        {
            if (anchor is null || focus is null || anchor == focus)
            {
                return string.Empty;
            }

            var (start, end) = TextPosition.Order(anchor.Value, focus.Value);
            var parts = new List<string>();
            for (var index = start.MessageIndex; index <= end.MessageIndex && index < _layout.Messages.Count; index++)
            {
                var message = _layout.Messages[index];
                var from = index == start.MessageIndex ? Math.Clamp(start.Offset, 0, message.VisibleText.Length) : 0;
                var to = index == end.MessageIndex ? Math.Clamp(end.Offset, 0, message.VisibleText.Length) : message.VisibleText.Length;
                if (to > from)
                {
                    parts.Add(message.VisibleText[from..to]);
                }
            }
            return string.Join(Environment.NewLine + Environment.NewLine, parts);
        }

        public (TextPosition Start, TextPosition End)? GetDocumentRange()
        {
            var first = _layout.Messages.FirstOrDefault(message => message.VisibleText.Length > 0);
            var last = _layout.Messages.LastOrDefault(message => message.VisibleText.Length > 0);
            return first is null || last is null
                ? null
                : (new TextPosition(first.MessageIndex, 0),
                    new TextPosition(last.MessageIndex, last.VisibleText.Length));
        }

        private static bool Contains(RectF rect, PointF point) =>
            point.X >= rect.Left && point.X <= rect.Right && point.Y >= rect.Top && point.Y <= rect.Bottom;

        private static float VerticalDistance(RectF rect, float y) => y < rect.Top
            ? rect.Top - y
            : y > rect.Bottom ? y - rect.Bottom : 0;
    }

    private readonly record struct TextPosition(int MessageIndex, int Offset)
    {
        public static (TextPosition Start, TextPosition End) Order(TextPosition left, TextPosition right) =>
            Compare(left, right) <= 0 ? (left, right) : (right, left);

        private static int Compare(TextPosition left, TextPosition right)
        {
            var message = left.MessageIndex.CompareTo(right.MessageIndex);
            return message != 0 ? message : left.Offset.CompareTo(right.Offset);
        }
    }

    private enum ActionKind
    {
        CopyMessage,
        ToggleTool
    }

    private sealed record ActionHit(ActionKind Kind, int MessageIndex, RectF Bounds);
    private sealed record VisibleText(string Text, int DetailStart);
    private sealed record CharacterLayout(int Offset, RectF Bounds);
    private sealed record TextLineLayout(
        string Text,
        int StartOffset,
        RectF Bounds,
        bool IsDetail,
        bool IsHeading,
        IReadOnlyList<CharacterLayout> Characters);
    private sealed record MessageLayout(
        int MessageIndex,
        bool IsUser,
        bool IsTool,
        bool IsToolExpanded,
        string Timestamp,
        string VisibleText,
        RectF Bubble,
        IReadOnlyList<TextLineLayout> Lines,
        RectF HeaderBounds,
        RectF TimestampBounds,
        RectF CopyBounds,
        RectF ToggleBounds);
    private sealed record TranscriptLayout(IReadOnlyList<MessageLayout> Messages, float Height)
    {
        public static TranscriptLayout Empty { get; } = new(Array.Empty<MessageLayout>(), 1);
    }

    private sealed record CanvasColors(
        Color Surface,
        Color PrimaryText,
        Color SecondaryText,
        Color UserBubble,
        Color AssistantBubble,
        Color Accent,
        Color CodeBackground,
        Color Selection)
    {
        public static CanvasColors Resolve()
        {
            var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
            var surface = ResolveColor("SurfaceColor", dark ? "#2C2C2C" : "#FFFFFF");
            var primary = ResolveColor("PrimaryTextColor", dark ? "#F5F5F5" : "#1F1B2E");
            var secondary = ResolveColor("SecondaryTextColor", dark ? "#C4C4C4" : "#8A8698");
            var user = ResolveColor("UserBubbleColor", dark ? "#332B45" : "#EEEBFB");
            var assistant = ResolveColor("AssistantBubbleColor", dark ? "#333333" : "#F2F2F5");
            var accent = ResolveColor("AccentColor", "#673AB7");
            var code = ResolveColor("HoverColor", dark ? "#383838" : "#EFEEF4");
            var selection = Color.FromRgba(accent.Red, accent.Green, accent.Blue, 0.30f);
            return new CanvasColors(surface, primary, secondary, user, assistant, accent, code, selection);
        }

        private static Color ResolveColor(string key, string fallback)
        {
            if (Application.Current?.Resources.TryGetValue(key, out var value) == true)
            {
                if (value is Color color)
                {
                    return color;
                }
                if (value is SolidColorBrush brush)
                {
                    return brush.Color;
                }
            }
            return Color.FromArgb(fallback);
        }
    }
}
