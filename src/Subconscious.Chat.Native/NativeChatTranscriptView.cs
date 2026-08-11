using System.Collections;
using System.Globalization;
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
    private readonly Dictionary<int, MauiIcon> _copyIcons = [];
    private readonly TranscriptDrawable _drawable;
    private readonly IDispatcherTimer _selectionScrollTimer;
    private readonly IDispatcherTimer _resizeInvalidationTimer;
    private readonly IDispatcherTimer _blockRefinementTimer;
    private ItemsSourceObserver? _observer;
    private Color? _copyIconColor;
    private bool _invalidateQueued;
    private int _heightUpdateVersion;
    private double _pendingMeasuredHeight = double.NaN;
    private double _lastObservedWidth = -1;
    private bool _isLoaded;
    private bool _isSelecting;
    private bool _selectionScrollPending;
    private bool _nearBottom = true;
    private bool _followAfterLayout;
    private int _observedCount;
    private PointF? _selectionPointer;
    private TextPosition? _anchor;
    private TextPosition? _focus;
    private TextPosition? _completedAnchor;
    private TextPosition? _completedFocus;
    private ActionHit? _pressedAction;
    private ActionHit? _hoveredAction;
    private float _pressedX;
    private float _pressedY;
#if WINDOWS
    private const int VirtualKeyLeftButton = 0x01;
    private bool _isPrimaryPointerPressed;
    private bool _isPrimaryPointerCaptured;
    private bool _isPrimaryMousePointer;
    private uint? _primaryPointerId;
    private bool _isRightPointerPressed;
    private bool _isContextMenuInteraction;
    private bool _isTextCursor;
    private Microsoft.UI.Input.InputCursor? _textCursor;
    private Microsoft.UI.Xaml.FrameworkElement? _windowsSurface;
    private Microsoft.UI.Xaml.Input.PointerEventHandler? _pointerPressedHandler;
    private Microsoft.UI.Xaml.Input.PointerEventHandler? _pointerMovedHandler;
    private Microsoft.UI.Xaml.Input.PointerEventHandler? _pointerReleasedHandler;
    private Microsoft.UI.Xaml.Input.PointerEventHandler? _pointerCaptureLostHandler;
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
        _selectionScrollTimer = Dispatcher.CreateTimer();
        _selectionScrollTimer.Interval = TimeSpan.FromMilliseconds(16);
        _selectionScrollTimer.Tick += OnSelectionScrollTick;
        _resizeInvalidationTimer = Dispatcher.CreateTimer();
        _resizeInvalidationTimer.Interval = TimeSpan.FromMilliseconds(32);
        _resizeInvalidationTimer.Tick += OnResizeInvalidationTick;
        _blockRefinementTimer = Dispatcher.CreateTimer();
        _blockRefinementTimer.Interval = TimeSpan.FromMilliseconds(16);
        _blockRefinementTimer.Tick += OnBlockRefinementTick;
        Content = _scrollView;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
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
        _resizeInvalidationTimer.Stop();
        _blockRefinementTimer.Stop();
        StopSelectionAutoScroll();
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

    private void OnSizeChanged(object? sender, EventArgs e)
    {
        var width = Width;
        if (width <= 0 || Math.Abs(width - _lastObservedWidth) <= 0.5)
        {
            return;
        }

        _lastObservedWidth = width;
        // Native window drags can raise several width notifications per frame. Reflow is an
        // O(transcript text) operation, so consolidate them without clearing cached projection
        // or intrinsic metrics; the last observed width is rendered on the next timer tick.
        if (!_resizeInvalidationTimer.IsRunning)
        {
            _resizeInvalidationTimer.Start();
        }
    }

    private void OnResizeInvalidationTick(object? sender, EventArgs e)
    {
        _resizeInvalidationTimer.Stop();
        _surface.Invalidate();
    }

    /// <summary>Scroll offset of the visible region inside the transcript canvas.</summary>
    internal float ViewportTop => (float)_scrollView.ScrollY;

    /// <summary>Height of the visible region, or 0 before the scroll view is measured.</summary>
    internal float ViewportHeight => (float)_scrollView.Height;

    /// <summary>Continues measuring deferred off-screen messages on later frames.</summary>
    internal void ScheduleBlockRefinement()
    {
        if (_isLoaded && !_blockRefinementTimer.IsRunning)
        {
            _blockRefinementTimer.Start();
        }
    }

    private void OnBlockRefinementTick(object? sender, EventArgs e)
    {
        _blockRefinementTimer.Stop();
        // Text measurement requires the platform canvas, so refinement stays on the UI thread
        // and is bounded per pass instead of being moved to a background thread.
        _drawable.MarkLayoutDirty();
        _surface.Invalidate();
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
        if (double.IsFinite(_pendingMeasuredHeight)
            && Math.Abs(_pendingMeasuredHeight - requested) < 0.5)
        {
            return;
        }

        _pendingMeasuredHeight = requested;
        var updateVersion = ++_heightUpdateVersion;
        if (Math.Abs(_surfaceLayer.HeightRequest - requested) < 0.5)
        {
            return;
        }

        Dispatcher.Dispatch(async () =>
        {
            // Rapid resize/expand operations may enqueue several heights. Only the newest layout
            // may update the visual tree; applying stale heights causes extra measure passes.
            if (updateVersion != _heightUpdateVersion)
            {
                return;
            }

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
        // Draw runs on the UI thread. Reconcile retained icon views immediately so their bounds
        // change in the same layout pass as the canvas instead of one dispatcher turn later.
        var colorChanged = _copyIconColor is null || !_copyIconColor.Equals(iconColor);
        _copyIconColor = iconColor;
        var activeIndexes = new HashSet<int>();

        foreach (var message in messages)
        {
            if (message.IsApproval || message.CopyBounds.Width <= 0)
            {
                continue;
            }

            activeIndexes.Add(message.MessageIndex);
            if (!_copyIcons.TryGetValue(message.MessageIndex, out var icon))
            {
                icon = new MauiIcon
                {
                    InputTransparent = true,
                };
                _copyIcons.Add(message.MessageIndex, icon);
                _iconLayer.Children.Add(icon);

                // Fluent icons need a realized native handler before their FontImageSource is
                // reliable. Configure after adding the retained Image, and reapply only if that
                // handler is recreated; resize still only moves the existing control.
                icon.HandlerChanged += (_, _) => ConfigureCopyIcon(icon, _copyIconColor ?? iconColor);
                ConfigureCopyIcon(icon, iconColor);
            }
            else if (colorChanged)
            {
                icon.IconColor(iconColor);
            }

            var bounds = new Rect(
                message.CopyBounds.X, message.CopyBounds.Y,
                message.CopyBounds.Width, message.CopyBounds.Height);
            var current = AbsoluteLayout.GetLayoutBounds(icon);
            if (Math.Abs(current.X - bounds.X) > 0.1
                || Math.Abs(current.Y - bounds.Y) > 0.1
                || Math.Abs(current.Width - bounds.Width) > 0.1
                || Math.Abs(current.Height - bounds.Height) > 0.1)
            {
                AbsoluteLayout.SetLayoutBounds(icon, bounds);
            }
        }

        foreach (var index in _copyIcons.Keys.Where(index => !activeIndexes.Contains(index)).ToArray())
        {
            var icon = _copyIcons[index];
            _iconLayer.Children.Remove(icon);
            _copyIcons.Remove(index);
        }
    }

    private static void ConfigureCopyIcon(MauiIcon icon, Color iconColor)
    {
        icon.Icon(FluentIcons.Copy24).IconSize(15).IconColor(iconColor);
    }

    private void OnScrolled(object? sender, ScrolledEventArgs e)
    {
        var extent = Math.Max(_surface.Height, _surface.HeightRequest);
        _nearBottom = e.ScrollY + _scrollView.Height >= extent - 80;

        // Only the visible band is painted, so scrolling must repaint. Messages that still hold
        // deferred geometry additionally need a layout pass before they can be drawn correctly.
        if (_drawable.HasPendingBlocks)
        {
            _drawable.MarkLayoutDirty();
        }
        _surface.Invalidate();
    }

    private void StartSelectionAutoScroll()
    {
        if (!_selectionScrollTimer.IsRunning)
        {
            _selectionScrollTimer.Start();
        }
    }

    private void StopSelectionAutoScroll()
    {
        _selectionScrollTimer.Stop();
        _selectionPointer = null;
        _selectionScrollPending = false;
    }

    private async void OnSelectionScrollTick(object? sender, EventArgs e)
    {
#if WINDOWS
        // WinUI/MAUI can stop reporting the drag at the window boundary. Poll the physical
        // mouse button so boundary cancellation never ends selection, while a release outside
        // the app still terminates the timer without requiring another pointer event.
        if (_isPrimaryPointerPressed && _isPrimaryMousePointer && !IsWindowsLeftButtonDown())
        {
            CompleteWindowsPrimaryDrag();
            return;
        }
#endif
        if (_selectionScrollPending || !_isSelecting || _selectionPointer is not { } pointer
            || _scrollView.Height <= 0)
        {
            return;
        }

        const float edgeBand = 42;
        const float maximumStep = 22;
        var viewportTop = (float)_scrollView.ScrollY;
        var viewportBottom = viewportTop + (float)_scrollView.Height;
        var delta = pointer.Y < viewportTop + edgeBand
            ? -maximumStep * Math.Clamp((viewportTop + edgeBand - pointer.Y) / edgeBand, 0, 1)
            : pointer.Y > viewportBottom - edgeBand
                ? maximumStep * Math.Clamp((pointer.Y - (viewportBottom - edgeBand)) / edgeBand, 0, 1)
                : 0;
        if (Math.Abs(delta) < 0.5f)
        {
            return;
        }

        var extent = Math.Max(_surface.Height, _surface.HeightRequest);
        var target = Math.Clamp(viewportTop + delta, 0, Math.Max(0, extent - _scrollView.Height));
        var applied = (float)(target - viewportTop);
        if (Math.Abs(applied) < 0.5f)
        {
            return;
        }

        _selectionScrollPending = true;
        try
        {
            await _scrollView.ScrollToAsync(0, target, false);
            var adjusted = new PointF(pointer.X, pointer.Y + applied);
            _selectionPointer = adjusted;
            var position = _drawable.HitTestText(adjusted);
            if (position is not null)
            {
                _focus = position;
                RememberCompletedSelection();
                _surface.Invalidate();
            }
        }
        finally
        {
            _selectionScrollPending = false;
        }
    }

    private void OnStartHoverInteraction(object? sender, TouchEventArgs e) => UpdateHoveredAction(e);

    private void OnMoveHoverInteraction(object? sender, TouchEventArgs e) => UpdateHoveredAction(e);

    private void OnEndHoverInteraction(object? sender, EventArgs e)
    {
        SetWindowsTextCursor(false);
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
        var hitAction = point is null ? null : _drawable.HitTestAction(point.Value);
        SetWindowsTextCursor(point is not null && hitAction is null
            && _drawable.IsPointOverText(point.Value));
        var action = hitAction?.Kind is ActionKind.CopyMessage or ActionKind.Approve or ActionKind.Deny
            ? hitAction
            : null;
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
        var extendSelection = IsShiftPressed() && _completedAnchor is not null && _completedFocus is not null;
        // WinUI may deliver GraphicsView's StartInteraction before our native PointerPressed
        // callback. Preserve the completed anchor for Shift+Click and right-click restoration.
        _anchor = extendSelection ? _completedAnchor : position;
        _focus = position;
        _isSelecting = true;
        _selectionPointer = point;
        StartSelectionAutoScroll();
        RememberCompletedSelection();
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

        UpdateSelectionAt(point.Value);
    }

    private void UpdateSelectionAt(PointF point)
    {
        if (!_isSelecting)
        {
            return;
        }

        _selectionPointer = point;
        var position = _drawable.HitTestText(point);
        if (position is not null && position != _focus)
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
            StopSelectionAutoScroll();
            _surface.Invalidate();
            return;
        }

        // GraphicsView can report EndInteraction when a captured mouse crosses the app
        // boundary. Only the physical left-button release is allowed to complete the drag.
        if (_isPrimaryPointerPressed && IsWindowsPrimaryPointerDown())
        {
            _pressedAction = null;
            RememberCompletedSelection();
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
        StopSelectionAutoScroll();
        RememberCompletedSelection();
        _surface.Invalidate();
    }

    private void OnCancelInteraction(object? sender, EventArgs e)
    {
#if WINDOWS
        // MAUI can cancel the GraphicsView interaction merely because the cursor crossed the
        // window boundary. Native pointer capture remains active, so preserve the range and let
        // the timer continue using the latest captured position until release/capture loss.
        if (_isPrimaryPointerPressed)
        {
            _pressedAction = null;
            RememberCompletedSelection();
            _surface.Invalidate();
            return;
        }
#endif
        _pressedAction = null;
        _isSelecting = false;
        StopSelectionAutoScroll();
        RememberCompletedSelection();
        _surface.Invalidate();
    }

    private static PointF? FirstTouch(TouchEventArgs e) => e.Touches.Any() ? e.Touches.First() : null;

    private static bool IsShiftPressed()
    {
#if WINDOWS
        var state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
            Windows.System.VirtualKey.Shift);
        return (state & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
#else
        return false;
#endif
    }

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
            case ActionKind.Approve when action.MessageIndex >= 0 && action.MessageIndex < Messages.Count
                && Messages[action.MessageIndex] is IChatApprovalRequest approval:
                if (approval.ApproveCommand.CanExecute(null))
                {
                    approval.ApproveCommand.Execute(null);
                }
                break;
            case ActionKind.Deny when action.MessageIndex >= 0 && action.MessageIndex < Messages.Count
                && Messages[action.MessageIndex] is IChatApprovalRequest denial:
                if (denial.DenyCommand.CanExecute(null))
                {
                    denial.DenyCommand.Execute(null);
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
        StopSelectionAutoScroll();
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

    private void SetWindowsTextCursor(bool useTextCursor)
    {
#if WINDOWS
        if (_windowsSurface is null || _isTextCursor == useTextCursor)
        {
            return;
        }

        try
        {
            var property = typeof(Microsoft.UI.Xaml.UIElement).GetProperty(
                "ProtectedCursor",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            _textCursor ??= Microsoft.UI.Input.InputSystemCursor.Create(
                Microsoft.UI.Input.InputSystemCursorShape.IBeam);
            property?.SetValue(_windowsSurface, useTextCursor ? _textCursor : null);
            _isTextCursor = useTextCursor;
        }
        catch (Exception)
        {
            // Cursor feedback must not interfere with selection.
        }
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
        _pointerMovedHandler = OnWindowsPointerMoved;
        _pointerReleasedHandler = OnWindowsPointerReleased;
        _pointerCaptureLostHandler = OnWindowsPointerCaptureLost;
        element.AddHandler(Microsoft.UI.Xaml.UIElement.PointerPressedEvent, _pointerPressedHandler, true);
        element.AddHandler(Microsoft.UI.Xaml.UIElement.PointerMovedEvent, _pointerMovedHandler, true);
        element.AddHandler(Microsoft.UI.Xaml.UIElement.PointerReleasedEvent, _pointerReleasedHandler, true);
        element.AddHandler(Microsoft.UI.Xaml.UIElement.PointerCaptureLostEvent, _pointerCaptureLostHandler, true);
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
        SetWindowsTextCursor(false);
        if (_windowsSurface is not null)
        {
            if (_pointerPressedHandler is not null)
            {
                _windowsSurface.RemoveHandler(Microsoft.UI.Xaml.UIElement.PointerPressedEvent, _pointerPressedHandler);
            }
            if (_pointerMovedHandler is not null)
            {
                _windowsSurface.RemoveHandler(Microsoft.UI.Xaml.UIElement.PointerMovedEvent, _pointerMovedHandler);
            }
            if (_pointerReleasedHandler is not null)
            {
                _windowsSurface.RemoveHandler(Microsoft.UI.Xaml.UIElement.PointerReleasedEvent, _pointerReleasedHandler);
            }
            if (_pointerCaptureLostHandler is not null)
            {
                _windowsSurface.RemoveHandler(Microsoft.UI.Xaml.UIElement.PointerCaptureLostEvent, _pointerCaptureLostHandler);
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
        _pointerMovedHandler = null;
        _pointerReleasedHandler = null;
        _pointerCaptureLostHandler = null;
        _isPrimaryPointerPressed = false;
        _isPrimaryPointerCaptured = false;
        _isPrimaryMousePointer = false;
        _primaryPointerId = null;
        _contextFlyout = null;
        _copyMenuItem = null;
        _selectAllMenuItem = null;
        _isRightPointerPressed = false;
        _isContextMenuInteraction = false;
        _isTextCursor = false;
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

        var point = e.GetCurrentPoint(_windowsSurface);
        _isRightPointerPressed = point.Properties.IsRightButtonPressed;
        _isContextMenuInteraction = _isRightPointerPressed;
        if (_isRightPointerPressed)
        {
            RestoreCompletedSelection();
            _surface.Invalidate();
            return;
        }

        if (point.Properties.IsLeftButtonPressed)
        {
            _primaryPointerId = e.Pointer.PointerId;
            _isPrimaryPointerPressed = true;
            _isPrimaryMousePointer = e.Pointer.PointerDeviceType
                == Microsoft.UI.Input.PointerDeviceType.Mouse;
            _isPrimaryPointerCaptured = _windowsSurface.CapturePointer(e.Pointer);
        }

        if (!IsShiftPressed())
        {
            // StartInteraction may run before or after this native callback. Preserve the
            // completed anchor for Shift+Click, otherwise begin a fresh left-click range.
            _completedAnchor = null;
            _completedFocus = null;
        }
    }

    private void OnWindowsPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_windowsSurface is null || !_isPrimaryPointerPressed
            || _primaryPointerId != e.Pointer.PointerId)
        {
            return;
        }

        var point = e.GetCurrentPoint(_windowsSurface);
        if (_isPrimaryMousePointer && !point.Properties.IsLeftButtonPressed
            && !IsWindowsLeftButtonDown())
        {
            CompleteWindowsPrimaryDrag();
            return;
        }

        UpdateSelectionAt(new PointF((float)point.Position.X, (float)point.Position.Y));
    }

    private void OnWindowsPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _isRightPointerPressed = false;
        if (_windowsSurface is null || !_isPrimaryPointerPressed
            || _primaryPointerId != e.Pointer.PointerId)
        {
            return;
        }

        // Clear the state before releasing capture: releasing it can synchronously raise
        // PointerCaptureLost, which must not cancel a selection that has already completed.
        _isPrimaryPointerCaptured = false;
        _windowsSurface.ReleasePointerCapture(e.Pointer);
        CompleteWindowsPrimaryDrag();
    }

    private void OnWindowsPointerCaptureLost(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_isPrimaryPointerCaptured || _primaryPointerId != e.Pointer.PointerId)
        {
            return;
        }

        _isPrimaryPointerCaptured = false;
        if (IsWindowsPrimaryPointerDown())
        {
            // Capture loss is not equivalent to release. Keep the last outside-edge coordinate
            // and timer alive; pointer moves resume when the cursor returns to the element.
            return;
        }

        CompleteWindowsPrimaryDrag();
    }

    private bool IsWindowsPrimaryPointerDown() =>
        _isPrimaryPointerPressed && (!_isPrimaryMousePointer || IsWindowsLeftButtonDown());

    private static bool IsWindowsLeftButtonDown() =>
        (GetAsyncKeyState(VirtualKeyLeftButton) & 0x8000) != 0;

    private void CompleteWindowsPrimaryDrag()
    {
        _isPrimaryPointerPressed = false;
        _isPrimaryPointerCaptured = false;
        _isPrimaryMousePointer = false;
        _primaryPointerId = null;
        _pressedAction = null;
        _isSelecting = false;
        StopSelectionAutoScroll();
        RememberCompletedSelection();
        _surface.Invalidate();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

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
        private const float ApprovalMinimumWidth = 360;
        private const float ApprovalButtonWidth = 78;
        private const float ApprovalButtonHeight = 30;
        private const float ApprovalButtonGap = 8;
        private const float DrawMargin = 320;
        private const float RebuildMargin = 240;
        private const int DeferredRebuildBudget = 6;
        private readonly NativeChatTranscriptView _owner = owner;
        private readonly List<ActionHit> _actions = [];
        private readonly Dictionary<int, CachedMetrics> _metrics = [];
        private readonly Dictionary<int, CachedBlock> _blocks = [];
        private readonly List<MessageLayout> _visible = [];
        private ChatTranscriptProjection? _projection;
        private TranscriptLayout _layout = TranscriptLayout.Empty;
        private bool _layoutDirty = true;
        private float _layoutWidth = -1;

        /// <summary>True when off-screen messages still hold estimated instead of measured geometry.</summary>
        public bool HasPendingBlocks { get; private set; }

        public void InvalidateLayout()
        {
            // Cached blocks and metrics are validated per message by content, width and state, so
            // a collection or theme change no longer discards geometry that is still accurate.
            _projection = null;
            _layoutDirty = true;
        }

        public void MarkLayoutDirty() => _layoutDirty = true;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var width = Math.Max(1, dirtyRect.Width);
            var colors = CanvasColors.Resolve();
            var viewportHeight = _owner.ViewportHeight;
            var hasViewport = viewportHeight > 0;
            var viewportTop = hasViewport ? _owner.ViewportTop : 0;
            var viewportBottom = hasViewport ? viewportTop + viewportHeight : float.MaxValue;

            if (_layoutDirty || Math.Abs(_layoutWidth - width) > 0.5f)
            {
                _layout = BuildLayout(canvas, width, viewportTop, viewportBottom);
                _layoutWidth = width;
                _layoutDirty = false;
                _owner.ApplyMeasuredHeight(_layout.Height);
                if (HasPendingBlocks)
                {
                    _owner.ScheduleBlockRefinement();
                }
            }

            canvas.FillColor = colors.Surface;
            canvas.FillRectangle(dirtyRect);

            if (_layout.Messages.Count == 0)
            {
                canvas.Font = GraphicsFont.Default;
                canvas.FontSize = BodyFontSize;
                canvas.FontColor = colors.SecondaryText;
                canvas.DrawString(_owner.EmptyText ?? string.Empty, 0, 20, width, 40,
                    HorizontalAlignment.Center, VerticalAlignment.Center);
                _owner.UpdateCopyIcons(Array.Empty<MessageLayout>(), colors.SecondaryText);
                return;
            }

            // Paint only the band around the viewport. Painting cost becomes independent of
            // transcript length while cached geometry keeps selection and hit testing intact.
            var drawTop = hasViewport ? viewportTop - DrawMargin : float.MinValue;
            var drawBottom = hasViewport ? viewportBottom + DrawMargin : float.MaxValue;
            _visible.Clear();
            foreach (var message in _layout.Messages)
            {
                var bubble = message.Bubble;
                if (bubble.Bottom < drawTop)
                {
                    continue;
                }
                if (bubble.Top > drawBottom)
                {
                    break;
                }

                _visible.Add(message);
                DrawMessage(canvas, message, colors);
            }

            _owner.UpdateCopyIcons(_visible, colors.SecondaryText);
        }

        private void DrawMessage(ICanvas canvas, MessageLayout message, CanvasColors colors)
        {
            var block = message.Block;
            // Cached geometry is stored relative to the bubble. Translating instead of recomputing
            // absolute rects is what makes a width-only or scroll-only pass cheap.
            canvas.SaveState();
            canvas.Translate(message.OffsetX, message.OffsetY);
            var bubbleBounds = new RectF(0, 0, block.Width, block.Height);
            var bubbleColor = message.IsUser ? colors.UserBubble : colors.AssistantBubble;
            canvas.FillColor = bubbleColor;
            canvas.FillRoundedRectangle(bubbleBounds, 6);

            if (message.IsApproval)
            {
                canvas.FillColor = message.ApprovalStatus switch
                {
                    ChatApprovalStatus.Approved => colors.Accent,
                    ChatApprovalStatus.Denied => colors.Error,
                    _ => colors.Accent,
                };
                canvas.FillRoundedRectangle(new RectF(0, 0, 4, block.Height), 2);
            }

            if (_owner._hoveredAction is { Kind: ActionKind.CopyMessage } hovered
                && hovered.MessageIndex == message.MessageIndex)
            {
                canvas.FillColor = Blend(bubbleColor, colors.PrimaryText, 0.12f);
                canvas.FillRoundedRectangle(block.CopyBounds, 4);
            }

            // Code surfaces must be behind the selection layer; otherwise a selected fenced
            // block remains selectable but its highlight is hidden by the code fill.
            foreach (var line in message.Lines)
            {
                var style = LineStyle(line);
                if ((style & (MarkdownTextStyle.Code | MarkdownTextStyle.CodeBlock)) != 0)
                {
                    canvas.FillColor = colors.CodeBackground;
                    canvas.FillRoundedRectangle(new RectF(
                        line.Bounds.X - 3, line.Bounds.Y - 1,
                        line.Bounds.Width + 6, line.Bounds.Height + 2), 3);
                }
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
                var style = LineStyle(line);
                var font = line.IsHeading ? GraphicsFont.DefaultBold : FontFor(style);
                var size = FontSizeFor(style, line.IsDetail);
                canvas.Font = font;
                canvas.FontSize = size;
                canvas.FontColor = line.IsHeading && line.IsDetail
                    ? colors.SecondaryText
                    : (style & MarkdownTextStyle.Quote) != 0
                        ? colors.SecondaryText
                        : colors.PrimaryText;
                // Line widths were measured during layout; reuse them instead of re-measuring
                // every visual line on each paint.
                var contentRight = block.Width - BubblePaddingX;
                var renderWidth = Math.Max(1,
                    Math.Min(line.Bounds.Width + 2, contentRight - line.Bounds.X));
                canvas.DrawString(line.Text,
                    line.Bounds.X, line.Bounds.Y,
                    renderWidth, line.Bounds.Height,
                    HorizontalAlignment.Left, VerticalAlignment.Top, TextFlow.ClipBounds);
            }

            canvas.Font = GraphicsFont.Default;
            canvas.FontSize = TimestampFontSize;
            canvas.FontColor = colors.SecondaryText;
            canvas.DrawString(message.Timestamp, block.TimestampBounds.X, block.TimestampBounds.Y,
                block.TimestampBounds.Width, block.TimestampBounds.Height,
                HorizontalAlignment.Right, VerticalAlignment.Center, TextFlow.ClipBounds);

            if (message.IsApproval)
            {
                DrawApprovalControls(canvas, message, colors);
                canvas.RestoreState();
                return;
            }

            if (message.IsTool)
            {
                DrawChevron(canvas, block.ToggleBounds, message.IsToolExpanded, colors.SecondaryText);
            }

            canvas.RestoreState();
        }

        private void DrawApprovalControls(ICanvas canvas, MessageLayout message, CanvasColors colors)
        {
            var block = message.Block;
            if (message.ApprovalStatus == ChatApprovalStatus.Pending)
            {
                var denyHovered = _owner._hoveredAction is
                    { Kind: ActionKind.Deny, MessageIndex: var denyIndex }
                    && denyIndex == message.MessageIndex;
                var approveHovered = _owner._hoveredAction is
                    { Kind: ActionKind.Approve, MessageIndex: var approveIndex }
                    && approveIndex == message.MessageIndex;

                DrawApprovalButton(canvas, block.DenyBounds, "Deny",
                    denyHovered ? Blend(colors.ErrorBackground, colors.Error, 0.16f) : colors.ErrorBackground,
                    colors.Error);
                DrawApprovalButton(canvas, block.ApproveBounds, "Allow",
                    approveHovered ? Blend(colors.Accent, Colors.White, 0.16f) : colors.Accent,
                    Colors.White);
                return;
            }

            var approved = message.ApprovalStatus == ChatApprovalStatus.Approved;
            DrawApprovalButton(canvas, block.ApproveBounds, approved ? "Allowed" : "Denied",
                approved ? Blend(colors.AssistantBubble, colors.Accent, 0.18f) : colors.ErrorBackground,
                approved ? colors.Accent : colors.Error);
        }

        private static void DrawApprovalButton(
            ICanvas canvas, RectF bounds, string text, Color background, Color foreground)
        {
            canvas.FillColor = background;
            canvas.FillRoundedRectangle(bounds, 5);
            canvas.Font = GraphicsFont.DefaultBold;
            canvas.FontSize = DetailFontSize;
            canvas.FontColor = foreground;
            canvas.DrawString(text, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                HorizontalAlignment.Center, VerticalAlignment.Center, TextFlow.ClipBounds);
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

        private TranscriptLayout BuildLayout(ICanvas canvas, float width,
            float viewportTop, float viewportBottom)
        {
            var projection = _projection ??= ChatTranscriptProjection.Capture(_owner.Messages);
            var snapshots = projection.Messages;
            var count = snapshots.Count;
            _actions.Clear();
            if (count == 0)
            {
                HasPendingBlocks = false;
                return TranscriptLayout.Empty;
            }

            var available = Math.Max(120, width - (OuterPadding * 2));
            var bubbleCap = (float)Math.Min(_owner.MaximumBubbleWidth, available);
            var widths = new float[count];
            var timestamps = new string[count];
            var blocks = new MessageBlock[count];
            var stale = new bool[count];
            var tops = new float[count];
            var y = OuterPadding;

            // Pass 1: resolve each bubble width from cached intrinsic metrics and reuse cached
            // geometry. A bubble already clamped to the maximum keeps its wrapped lines, so a
            // window resize usually re-wraps nothing at all.
            for (var index = 0; index < count; index++)
            {
                var snapshot = snapshots[index];
                var timestamp = NormalizeSingleLine(snapshot.Timestamp);
                timestamps[index] = timestamp;
                var bubbleWidth = ResolveBubbleWidth(canvas, snapshot, timestamp, bubbleCap);
                widths[index] = bubbleWidth;

                if (_blocks.TryGetValue(snapshot.SourceIndex, out var cached)
                    && cached.Matches(snapshot, bubbleWidth))
                {
                    blocks[index] = cached.Block;
                }
                else
                {
                    stale[index] = true;
                    blocks[index] = cached?.Block ?? CreateEstimatedBlock(snapshot, bubbleWidth);
                }

                tops[index] = y;
                y += blocks[index].Height + BubbleGap;
            }

            // Pass 2: re-wrap nearest-first. Anything inside or beside the viewport is always
            // measured; the remainder is bounded per pass and finished on later frames.
            HasPendingBlocks = false;
            var order = BuildRebuildOrder(stale, tops, blocks, viewportTop, viewportBottom);
            var budget = DeferredRebuildBudget;
            foreach (var index in order)
            {
                var top = tops[index];
                var bottom = top + blocks[index].Height;
                var near = bottom >= viewportTop - RebuildMargin && top <= viewportBottom + RebuildMargin;
                if (!near)
                {
                    if (budget <= 0)
                    {
                        HasPendingBlocks = true;
                        continue;
                    }
                    budget--;
                }

                var snapshot = snapshots[index];
                var block = BuildBlock(canvas, snapshot, widths[index], timestamps[index]);
                blocks[index] = block;
                _blocks[snapshot.SourceIndex] = new CachedBlock(
                    snapshot.CanonicalSelectableText, widths[index],
                    snapshot.IsToolExpanded, snapshot.ApprovalStatus, block);
            }

            // Pass 3: position the cached blocks. Only offsets change, so this stays linear and
            // allocation-light regardless of how much text each message holds.
            var messages = new List<MessageLayout>(count);
            y = OuterPadding;
            for (var index = 0; index < count; index++)
            {
                var snapshot = snapshots[index];
                var block = blocks[index];
                var offsetX = snapshot.IsUser ? width - OuterPadding - block.Width : OuterPadding;
                var layout = new MessageLayout(snapshot.SourceIndex, snapshot.IsUser, snapshot.IsTool,
                    snapshot.IsApproval, snapshot.IsToolExpanded, snapshot.ApprovalStatus,
                    timestamps[index], block, offsetX, y);
                messages.Add(layout);
                RegisterActions(layout);
                y += block.Height + BubbleGap;
            }

            PruneCaches(snapshots);
            return new TranscriptLayout(messages, Math.Max(60, y));
        }

        private static List<int> BuildRebuildOrder(bool[] stale, float[] tops,
            MessageBlock[] blocks, float viewportTop, float viewportBottom)
        {
            var order = new List<int>();
            for (var index = 0; index < stale.Length; index++)
            {
                if (stale[index])
                {
                    order.Add(index);
                }
            }

            if (order.Count > 1)
            {
                order.Sort((left, right) => ViewportDistance(tops[left], blocks[left].Height, viewportTop, viewportBottom)
                    .CompareTo(ViewportDistance(tops[right], blocks[right].Height, viewportTop, viewportBottom)));
            }
            return order;
        }

        private static float ViewportDistance(float top, float height, float viewportTop, float viewportBottom)
        {
            var bottom = top + height;
            if (bottom < viewportTop)
            {
                return viewportTop - bottom;
            }
            return top > viewportBottom ? top - viewportBottom : 0;
        }

        private void RegisterActions(MessageLayout message)
        {
            if (message.IsApproval)
            {
                if (message.ApprovalStatus == ChatApprovalStatus.Pending)
                {
                    _actions.Add(new ActionHit(ActionKind.Deny, message.MessageIndex, message.DenyBounds));
                    _actions.Add(new ActionHit(ActionKind.Approve, message.MessageIndex, message.ApproveBounds));
                }
                return;
            }

            if (message.IsTool)
            {
                // Register the broad header action first. Copy is registered last so reverse hit
                // testing gives the dedicated copy button precedence over the overlapping header.
                _actions.Add(new ActionHit(ActionKind.ToggleTool, message.MessageIndex, message.HeaderBounds));
            }

            _actions.Add(new ActionHit(ActionKind.CopyMessage, message.MessageIndex, message.CopyBounds));
        }

        private float ResolveBubbleWidth(ICanvas canvas, ChatMessageSnapshot snapshot,
            string timestamp, float bubbleCap)
        {
            if (!_metrics.TryGetValue(snapshot.SourceIndex, out var cached)
                || !cached.Matches(snapshot))
            {
                var visible = BuildVisibleText(snapshot);
                var naturalWidth = MeasureNaturalWidth(canvas, visible.Text, visible.DetailStart);
                var footerTimestampWidth = Measure(canvas, timestamp, TimestampFontSize) + 6;
                var toolNaturalWidth = snapshot.IsTool
                    ? MeasureToolNaturalWidth(canvas, snapshot, timestamp)
                    : 0;
                cached = new CachedMetrics(snapshot.CanonicalSelectableText, snapshot.IsToolExpanded,
                    new MessageIntrinsicMetrics(naturalWidth, footerTimestampWidth, toolNaturalWidth));
                _metrics[snapshot.SourceIndex] = cached;
            }

            var metrics = cached.Metrics;
            var footerBubbleWidth = metrics.FooterTimestampWidth + 6 + 24 + (BubblePaddingX * 2);
            var naturalBubbleWidth = Math.Max(
                Math.Max(metrics.NaturalWidth + (BubblePaddingX * 2), footerBubbleWidth), 105f);
            // Measure all tool details even while collapsed. The card fits its natural content
            // up to the cap and therefore keeps the same width when it is expanded.
            return snapshot.IsApproval
                ? Math.Clamp(Math.Max(naturalBubbleWidth, Math.Min(ApprovalMinimumWidth, bubbleCap)),
                    Math.Min(120, bubbleCap), bubbleCap)
                : snapshot.IsTool
                    ? Math.Clamp(metrics.ToolNaturalWidth, Math.Min(120, bubbleCap), bubbleCap)
                    : Math.Clamp(naturalBubbleWidth, Math.Min(120, bubbleCap), bubbleCap);
        }

        /// <summary>Builds one message's wrapped geometry relative to its own bubble origin.</summary>
        private static MessageBlock BuildBlock(ICanvas canvas, ChatMessageSnapshot snapshot,
            float bubbleWidth, string timestamp)
        {
            var visible = BuildVisibleText(snapshot);
            var contentX = BubblePaddingX;
            var contentWidth = bubbleWidth - (BubblePaddingX * 2);
            var lines = new List<TextLineLayout>();
            var lineY = BubblePaddingY;
            var headerBounds = RectF.Zero;
            RectF copyBounds;
            RectF toggleBounds;
            RectF timestampBounds;
            var approveBounds = RectF.Zero;
            var denyBounds = RectF.Zero;

            if (snapshot.IsApproval)
            {
                WrapText(canvas, visible.Text, 0, contentX, ref lineY, contentWidth, false, lines,
                    visible.Spans);
                if (lines.Count == 0)
                {
                    lines.Add(CreateLine(canvas, string.Empty, 0, contentX, lineY,
                        contentWidth, false, false));
                    lineY += LineHeight(canvas, BodyFontSize) + LineGap;
                }

                var actionY = lineY + 6;
                approveBounds = new RectF(
                    bubbleWidth - BubblePaddingX - ApprovalButtonWidth,
                    actionY, ApprovalButtonWidth, ApprovalButtonHeight);
                denyBounds = new RectF(
                    approveBounds.Left - ApprovalButtonGap - ApprovalButtonWidth,
                    actionY, ApprovalButtonWidth, ApprovalButtonHeight);
                copyBounds = RectF.Zero;
                toggleBounds = RectF.Zero;
                timestampBounds = new RectF(
                    contentX, actionY,
                    Math.Max(20, denyBounds.Left - ApprovalButtonGap - contentX),
                    ApprovalButtonHeight);
                lineY = actionY + ApprovalButtonHeight;
            }
            else if (snapshot.IsTool)
            {
                headerBounds = new RectF(0, 0, bubbleWidth, ToolHeaderHeight);
                var actionY = (ToolHeaderHeight - ToolActionSize) / 2;
                toggleBounds = new RectF(
                    bubbleWidth - BubblePaddingX - ToolActionSize,
                    actionY, ToolActionSize, ToolActionSize);
                copyBounds = new RectF(
                    toggleBounds.Left - ToolActionGap - ToolActionSize,
                    actionY, ToolActionSize, ToolActionSize);

                var measuredTimestamp = Measure(canvas, timestamp, TimestampFontSize) + 6;
                var timestampWidth = Math.Max(ToolActionSize, measuredTimestamp);
                var timestampRight = copyBounds.Left - ToolTimestampActionGap;
                var timestampLeft = Math.Max(contentX, timestampRight - timestampWidth);
                timestampBounds = new RectF(
                    timestampLeft, 0,
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
                var titleY = (ToolHeaderHeight - titleHeight) / 2;
                lines.Add(CreateLine(canvas, title, 0, contentX, titleY, titleWidth, false, false));

                lineY = ToolHeaderHeight;
                if (snapshot.IsToolExpanded && visible.DetailStart >= 0 && visible.DetailStart < visible.Text.Length)
                {
                    lineY += 4;
                    WrapText(canvas, visible.Text[visible.DetailStart..], visible.DetailStart,
                        contentX, ref lineY, contentWidth, true, lines);
                }
            }
            else
            {
                WrapText(canvas, visible.Text, 0, contentX, ref lineY, contentWidth, false, lines,
                    visible.Spans);
                if (lines.Count == 0)
                {
                    lines.Add(CreateLine(canvas, string.Empty, 0, contentX, lineY, contentWidth, false, false));
                    lineY += LineHeight(canvas, BodyFontSize) + LineGap;
                }

                var footerY = lineY + 3;
                copyBounds = new RectF(bubbleWidth - BubblePaddingX - 24, footerY, 24, 24);
                toggleBounds = RectF.Zero;
                timestampBounds = new RectF(
                    contentX, footerY,
                    Math.Max(20, copyBounds.Left - 6 - contentX), 24);
            }

            var hasExpandedDetails = snapshot.IsTool && snapshot.IsToolExpanded
                && visible.DetailStart >= 0 && visible.DetailStart < visible.Text.Length;
            var bottom = snapshot.IsApproval
                ? lineY + BubblePaddingY
                : snapshot.IsTool
                    ? hasExpandedDetails ? lineY + BubblePaddingY : ToolHeaderHeight
                    : copyBounds.Bottom + BubblePaddingY;
            var height = Math.Max(40, bottom);
            if (snapshot.IsTool && !snapshot.IsToolExpanded)
            {
                // Make every pixel of a collapsed tool card toggle the card.
                headerBounds = new RectF(0, 0, bubbleWidth, height);
            }

            return new MessageBlock(bubbleWidth, height, visible.Text, lines,
                headerBounds, timestampBounds, copyBounds, toggleBounds, approveBounds, denyBounds);
        }

        /// <summary>Cheap stand-in geometry so far off-screen messages cost no text shaping.</summary>
        private static MessageBlock CreateEstimatedBlock(ChatMessageSnapshot snapshot, float bubbleWidth)
        {
            var height = snapshot.IsTool && !snapshot.IsToolExpanded
                ? ToolHeaderHeight
                : EstimateHeight(snapshot, bubbleWidth - (BubblePaddingX * 2));
            return new MessageBlock(bubbleWidth, height, string.Empty,
                Array.Empty<TextLineLayout>(), RectF.Zero, RectF.Zero,
                RectF.Zero, RectF.Zero, RectF.Zero, RectF.Zero);
        }

        private static float EstimateHeight(ChatMessageSnapshot snapshot, float contentWidth)
        {
            var length = (snapshot.CanonicalSelectableText ?? string.Empty).Length;
            var charactersPerLine = Math.Max(18f, contentWidth / 6.6f);
            var lines = Math.Max(1f, MathF.Ceiling(length / charactersPerLine));
            return (BubblePaddingY * 2) + (lines * (BodyFontSize + LineGap + 4)) + 27;
        }

        private void PruneCaches(IReadOnlyList<ChatMessageSnapshot> snapshots)
        {
            if (_blocks.Count <= snapshots.Count + 32 && _metrics.Count <= snapshots.Count + 32)
            {
                return;
            }

            var live = new HashSet<int>();
            foreach (var snapshot in snapshots)
            {
                live.Add(snapshot.SourceIndex);
            }
            foreach (var key in _blocks.Keys.Where(key => !live.Contains(key)).ToArray())
            {
                _blocks.Remove(key);
            }
            foreach (var key in _metrics.Keys.Where(key => !live.Contains(key)).ToArray())
            {
                _metrics.Remove(key);
            }
        }

        private static float MeasureToolNaturalWidth(
            ICanvas canvas, ChatMessageSnapshot snapshot, string timestamp)
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

            return Math.Max(headerWidth, detailWidth + (BubblePaddingX * 2));
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
            if (snapshot.IsApproval)
            {
                return BuildApprovalVisibleText(snapshot);
            }

            if (!snapshot.IsTool)
            {
                return new VisibleText(
                    snapshot.ContentProjection.NativeText.Text,
                    -1,
                    snapshot.ContentProjection.NativeText.Spans);
            }

            var title = snapshot.ToolTitle ?? string.Empty;
            if (!snapshot.IsToolExpanded)
            {
                return new VisibleText(title, -1, Array.Empty<MarkdownTextSpan>());
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
                return new VisibleText(title, -1, Array.Empty<MarkdownTextSpan>());
            }

            return new VisibleText(
                title + "\n\n" + string.Join("\n\n", sections),
                title.Length + 2,
                Array.Empty<MarkdownTextSpan>());
        }

        private static VisibleText BuildApprovalVisibleText(ChatMessageSnapshot snapshot)
        {
            var sections = new List<(string Text, MarkdownTextStyle Style, int CodeStart)>();
            if (!string.IsNullOrWhiteSpace(snapshot.ApprovalTitle))
            {
                sections.Add((snapshot.ApprovalTitle, MarkdownTextStyle.Strong, -1));
            }
            if (!string.IsNullOrWhiteSpace(snapshot.ApprovalDescription))
            {
                sections.Add((snapshot.ApprovalDescription, MarkdownTextStyle.None, -1));
            }
            if (!string.IsNullOrWhiteSpace(snapshot.ApprovalOperation))
            {
                sections.Add(($"Operation: {snapshot.ApprovalOperation}", MarkdownTextStyle.Strong, -1));
            }
            if (!string.IsNullOrWhiteSpace(snapshot.ApprovalArguments))
            {
                const string argumentsLabel = "Arguments\n";
                sections.Add((argumentsLabel + NormalizeNewlines(snapshot.ApprovalArguments),
                    MarkdownTextStyle.None, argumentsLabel.Length));
            }

            var text = string.Join("\n\n", sections.Select(section => section.Text));
            var spans = new List<MarkdownTextSpan>();
            var offset = 0;
            foreach (var section in sections)
            {
                if (section.Style != MarkdownTextStyle.None)
                {
                    spans.Add(new MarkdownTextSpan(offset, section.Text.Length, section.Style));
                }
                if (section.CodeStart >= 0)
                {
                    const int labelLength = 9;
                    spans.Add(new MarkdownTextSpan(offset, labelLength, MarkdownTextStyle.Strong));
                    var codeLength = section.Text.Length - section.CodeStart;
                    if (codeLength > 0)
                    {
                        spans.Add(new MarkdownTextSpan(offset + section.CodeStart, codeLength,
                            MarkdownTextStyle.CodeBlock));
                    }
                }
                offset += section.Text.Length + 2;
            }

            return new VisibleText(text, -1, spans);
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
            float width, bool detail, List<TextLineLayout> output,
            IReadOnlyList<MarkdownTextSpan>? spans = null)
        {
            var position = 0;
            while (position <= text.Length)
            {
                var newline = text.IndexOf('\n', position);
                var end = newline < 0 ? text.Length : newline;
                var segment = text[position..end];
                if (segment.Length == 0)
                {
                    var line = CreateLine(canvas, string.Empty, baseOffset + position,
                        x, y, width, detail, false, spans);
                    output.Add(line);
                    y += line.Bounds.Height + LineGap;
                }
                else
                {
                    WrapSegment(canvas, segment, baseOffset + position, x, ref y,
                        width, detail, output, spans);
                }

                if (newline < 0)
                {
                    break;
                }
                position = newline + 1;
            }
        }

        private static void WrapSegment(ICanvas canvas, string segment, int baseOffset, float x, ref float y,
            float width, bool detail, List<TextLineLayout> output,
            IReadOnlyList<MarkdownTextSpan>? spans)
        {
            var starts = StringInfo.ParseCombiningCharacters(segment);
            var elementWidths = new float[starts.Length];
            var rawSegmentWidth = 0f;
            var sharedStyle = MarkdownTextStyle.None;
            if (starts.Length > 0)
            {
                sharedStyle = ResolveStyle(spans, baseOffset + starts[0]).Style;
            }

            for (var index = 0; index < starts.Length; index++)
            {
                var elementStart = starts[index];
                var elementLength = TextElementLength(segment, elementStart);
                var (style, _) = ResolveStyle(spans, baseOffset + elementStart);
                sharedStyle &= style;
                var elementWidth = MeasureTextElement(canvas, segment, elementStart, elementLength,
                    baseOffset + elementStart, detail, spans);
                elementWidths[index] = elementWidth;
                rawSegmentWidth += elementWidth;
            }

            // Wrapping and painting must use the same shaped-text scale. Summing independently
            // measured graphemes can substantially underestimate a complete line on Windows.
            var shapedSegmentWidth = Measure(canvas, segment,
                FontSizeFor(sharedStyle, detail), FontFor(sharedStyle));
            var widthScale = rawSegmentWidth > 0 ? shapedSegmentWidth / rawSegmentWidth : 1;
            if (!float.IsFinite(widthScale) || widthScale <= 0)
            {
                widthScale = 1;
            }

            var cursor = 0;
            while (cursor < starts.Length)
            {
                var fit = cursor;
                var lastBreak = -1;
                var measuredWidth = 0f;
                while (fit < starts.Length)
                {
                    var elementStart = starts[fit];
                    if (char.IsWhiteSpace(segment, elementStart))
                    {
                        lastBreak = fit + 1;
                    }
                    var elementWidth = elementWidths[fit] * widthScale;
                    if (fit > cursor && measuredWidth + elementWidth > width)
                    {
                        break;
                    }
                    measuredWidth += elementWidth;
                    fit++;
                }

                if (fit <= cursor)
                {
                    fit = cursor + 1;
                }
                else if (fit < starts.Length && lastBreak > cursor)
                {
                    fit = lastBreak;
                }

                var lineStart = starts[cursor];
                var lineEnd = fit < starts.Length ? starts[fit] : segment.Length;
                var lineText = segment[lineStart..lineEnd];
                var heading = detail && (lineText == "Input" || lineText == "Output");
                var line = CreateLine(canvas, lineText, baseOffset + lineStart,
                    x, y, width, detail, heading, spans, elementWidths, cursor);
                output.Add(line);
                y += line.Bounds.Height + LineGap;
                cursor = fit;
            }
        }

        private static TextLineLayout CreateLine(ICanvas canvas, string text, int startOffset,
            float x, float y, float maxWidth, bool detail, bool heading,
            IReadOnlyList<MarkdownTextSpan>? spans = null,
            IReadOnlyList<float>? measuredElementWidths = null,
            int measuredElementOffset = 0)
        {
            var starts = StringInfo.ParseCombiningCharacters(text);
            var pending = new List<(int Offset, int Length, MarkdownTextStyle Style, float Width)>(starts.Length);
            for (var index = 0; index < starts.Length; index++)
            {
                var elementStart = starts[index];
                var elementLength = StringInfo.GetNextTextElementLength(text.AsSpan(elementStart));
                var (style, _) = ResolveStyle(spans, startOffset + elementStart);
                if (heading)
                {
                    style |= MarkdownTextStyle.Strong;
                }

                var elementWidth = measuredElementWidths is not null
                    && measuredElementOffset + index < measuredElementWidths.Count
                        ? measuredElementWidths[measuredElementOffset + index]
                        : Measure(canvas, text.Substring(elementStart, elementLength),
                            FontSizeFor(style, detail), FontFor(style));
                pending.Add((startOffset + elementStart, elementLength, style,
                    Math.Max(0.5f, elementWidth)));
            }

            var baseHeight = LineHeight(canvas, detail ? DetailFontSize : BodyFontSize);
            var characters = new List<CharacterLayout>(pending.Count);
            var cursorX = x;
            foreach (var item in pending)
            {
                characters.Add(new CharacterLayout(item.Offset, item.Length,
                    new RectF(cursorX, y, item.Width, baseHeight), item.Style));
                cursorX += item.Width;
            }

            // Individual grapheme measurements (especially bold text on Windows) do not
            // necessarily add up to the width of the fully shaped line used by DrawString.
            // Normalize retained hit/selection geometry to that shaped width while preserving
            // each grapheme's relative advance. This keeps painting at one draw call per line.
            var geometryWidth = cursorX - x;
            var provisionalLine = new TextLineLayout(text, startOffset,
                new RectF(x, y, Math.Max(1, geometryWidth), baseHeight),
                detail, heading, characters);
            var renderStyle = LineStyle(provisionalLine);
            var renderFont = heading ? GraphicsFont.DefaultBold : FontFor(renderStyle);
            var renderSize = FontSizeFor(renderStyle, detail);
            var renderHeight = LineHeight(canvas, renderSize, renderFont);
            var shapedWidth = characters.Count == 0 ? 0 : Measure(canvas, text, renderSize, renderFont);
            var scale = geometryWidth > 0 ? shapedWidth / geometryWidth : 1;
            if (!float.IsFinite(scale) || scale <= 0)
            {
                scale = 1;
            }

            for (var index = 0; index < characters.Count; index++)
            {
                var character = characters[index];
                characters[index] = character with
                {
                    Bounds = new RectF(
                        x + ((character.Bounds.X - x) * scale), y,
                        character.Bounds.Width * scale, renderHeight)
                };
            }

            var measured = Math.Min(maxWidth, Math.Max(1, shapedWidth));
            return new TextLineLayout(text, startOffset,
                new RectF(x, y, measured, renderHeight), detail, heading, characters);
        }

        private static float MeasureTextElement(ICanvas canvas, string text, int elementStart, int elementLength,
            int absoluteOffset, bool detail, IReadOnlyList<MarkdownTextSpan>? spans)
        {
            var (style, _) = ResolveStyle(spans, absoluteOffset);
            // Inline code is painted as part of its containing line, so its wrapping geometry
            // must use the same body size as that single shaped draw call. Block code and
            // headings remain whole-line styles and retain their specific metrics.
            style &= ~MarkdownTextStyle.Code;
            return Measure(canvas, text.Substring(elementStart, elementLength),
                FontSizeFor(style, detail), FontFor(style));
        }

        private static int TextElementLength(string text, int start) =>
            StringInfo.GetNextTextElement(text, start).Length;

        private static (MarkdownTextStyle Style, string? Link) ResolveStyle(
            IReadOnlyList<MarkdownTextSpan>? spans, int offset)
        {
            var style = MarkdownTextStyle.None;
            string? link = null;
            if (spans is not null)
            {
                foreach (var span in spans)
                {
                    if (offset >= span.Start && offset < span.End)
                    {
                        style |= span.Style;
                        link ??= span.LinkTarget;
                    }
                }
            }
            return (style, link);
        }

        private static MarkdownTextStyle LineStyle(TextLineLayout line)
        {
            var style = line.IsHeading ? MarkdownTextStyle.Strong : MarkdownTextStyle.None;
            if (line.Characters.Count == 0)
            {
                return style;
            }

            // The canvas shapes each visual line in one call. Only preserve styles shared by
            // the entire line; applying a partial link or emphasis style to an entire sentence
            // would be misleading, while per-fragment drawing loses normal word shaping.
            var shared = line.Characters[0].Style;
            for (var index = 1; index < line.Characters.Count; index++)
            {
                shared &= line.Characters[index].Style;
            }

            const MarkdownTextStyle lineStyles = MarkdownTextStyle.Heading1
                | MarkdownTextStyle.Heading2
                | MarkdownTextStyle.Heading3
                | MarkdownTextStyle.Heading4
                | MarkdownTextStyle.Heading5
                | MarkdownTextStyle.Heading6
                | MarkdownTextStyle.Strong
                | MarkdownTextStyle.Code
                | MarkdownTextStyle.CodeBlock
                | MarkdownTextStyle.Quote
                | MarkdownTextStyle.TableHeader;
            return style | (shared & lineStyles);
        }

        private static GraphicsFont FontFor(MarkdownTextStyle style) =>
            (style & (MarkdownTextStyle.Strong | MarkdownTextStyle.TableHeader
                | MarkdownTextStyle.Heading1 | MarkdownTextStyle.Heading2 | MarkdownTextStyle.Heading3
                | MarkdownTextStyle.Heading4 | MarkdownTextStyle.Heading5 | MarkdownTextStyle.Heading6)) != 0
                ? GraphicsFont.DefaultBold
                : GraphicsFont.Default;

        private static float FontSizeFor(MarkdownTextStyle style, bool detail)
        {
            if (detail || (style & (MarkdownTextStyle.Code | MarkdownTextStyle.CodeBlock)) != 0)
            {
                return DetailFontSize;
            }
            if ((style & MarkdownTextStyle.Heading1) != 0) return 20;
            if ((style & MarkdownTextStyle.Heading2) != 0) return 18;
            if ((style & MarkdownTextStyle.Heading3) != 0) return 16;
            if ((style & MarkdownTextStyle.Heading4) != 0) return 15;
            if ((style & MarkdownTextStyle.Heading5) != 0) return 14;
            return BodyFontSize;
        }

        private static float Measure(ICanvas canvas, string text, float size, GraphicsFont? font = null) =>
            string.IsNullOrEmpty(text) ? 0 : canvas.GetStringSize(text, font ?? GraphicsFont.Default, size).Width;

        private static float LineHeight(ICanvas canvas, float size, GraphicsFont? font = null) =>
            Math.Max(size + 4, canvas.GetStringSize("Ag", font ?? GraphicsFont.Default, size).Height);

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
                CharacterLayout? first = null;
                CharacterLayout? last = null;
                foreach (var character in line.Characters)
                {
                    if (character.Offset >= selectionEnd || character.Offset + character.Length <= selectionStart)
                    {
                        continue;
                    }
                    first ??= character;
                    last = character;
                }
                if (first is not null && last is not null)
                {
                    yield return new RectF(first.Bounds.Left, line.Bounds.Top,
                        Math.Max(1, last.Bounds.Right - first.Bounds.Left), line.Bounds.Height);
                }
            }
        }

        public ActionHit? HitTestAction(PointF point)
        {
            for (var index = _actions.Count - 1; index >= 0; index--)
            {
                var action = _actions[index];
                if (!Contains(action.Bounds, point))
                {
                    continue;
                }

                // The broad tool-header action must not mask its retained title text. Title text
                // behaves like every other transcript line; surrounding header space still toggles.
                if (action.Kind == ActionKind.ToggleTool && IsPointOverText(point))
                {
                    continue;
                }
                return action;
            }
            return null;
        }

        public bool IsPointOverText(PointF point)
        {
            foreach (var message in _layout.Messages)
            {
                if (!Contains(message.Bubble, point))
                {
                    continue;
                }

                var local = message.ToLocal(point);
                foreach (var line in message.Lines)
                {
                    if (local.Y < line.Bounds.Top || local.Y > line.Bounds.Bottom)
                    {
                        continue;
                    }
                    foreach (var character in line.Characters)
                    {
                        if (Contains(new RectF(character.Bounds.X - 1, character.Bounds.Y,
                            character.Bounds.Width + 2, character.Bounds.Height), local))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        public TextPosition? HitTestText(PointF point)
        {
            if (_layout.Messages.Count == 0)
            {
                return null;
            }

            // Selection drags call this on every pointer move, so scan linearly instead of
            // allocating LINQ orderings across the whole transcript.
            var message = _layout.Messages[0];
            var messageDistance = VerticalDistance(message.Bubble, point.Y);
            for (var index = 1; index < _layout.Messages.Count; index++)
            {
                var candidate = _layout.Messages[index];
                var distance = VerticalDistance(candidate.Bubble, point.Y);
                if (distance < messageDistance)
                {
                    message = candidate;
                    messageDistance = distance;
                }
            }

            if (message.Lines.Count == 0)
            {
                return new TextPosition(message.MessageIndex, 0);
            }

            var local = message.ToLocal(point);
            var line = message.Lines[0];
            var lineDistance = VerticalDistance(line.Bounds, local.Y);
            for (var index = 1; index < message.Lines.Count; index++)
            {
                var candidate = message.Lines[index];
                var distance = VerticalDistance(candidate.Bounds, local.Y);
                if (distance < lineDistance)
                {
                    line = candidate;
                    lineDistance = distance;
                }
            }

            if (line.Characters.Count == 0)
            {
                return new TextPosition(message.MessageIndex, Math.Clamp(line.StartOffset, 0, message.VisibleText.Length));
            }
            if (local.X <= line.Characters[0].Bounds.Center.X)
            {
                return new TextPosition(message.MessageIndex, line.Characters[0].Offset);
            }
            foreach (var character in line.Characters)
            {
                if (local.X < character.Bounds.Center.X)
                {
                    return new TextPosition(message.MessageIndex, character.Offset);
                }
            }
            var last = line.Characters[^1];
            return new TextPosition(message.MessageIndex,
                Math.Min(message.VisibleText.Length, last.Offset + last.Length));
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
        ToggleTool,
        Approve,
        Deny
    }

    private sealed record ActionHit(ActionKind Kind, int MessageIndex, RectF Bounds);
    private sealed record VisibleText(
        string Text,
        int DetailStart,
        IReadOnlyList<MarkdownTextSpan> Spans);
    private sealed record CharacterLayout(
        int Offset,
        int Length,
        RectF Bounds,
        MarkdownTextStyle Style);
    private sealed record TextLineLayout(
        string Text,
        int StartOffset,
        RectF Bounds,
        bool IsDetail,
        bool IsHeading,
        IReadOnlyList<CharacterLayout> Characters);
    private sealed record MessageIntrinsicMetrics(
        float NaturalWidth,
        float FooterTimestampWidth,
        float ToolNaturalWidth);

    private sealed record CachedMetrics(
        string ContentKey,
        bool Expanded,
        MessageIntrinsicMetrics Metrics)
    {
        public bool Matches(ChatMessageSnapshot snapshot) =>
            Expanded == snapshot.IsToolExpanded
            && string.Equals(ContentKey, snapshot.CanonicalSelectableText, StringComparison.Ordinal);
    }

    /// <summary>One message's wrapped geometry, stored relative to its own bubble origin.</summary>
    private sealed record MessageBlock(
        float Width,
        float Height,
        string VisibleText,
        IReadOnlyList<TextLineLayout> Lines,
        RectF HeaderBounds,
        RectF TimestampBounds,
        RectF CopyBounds,
        RectF ToggleBounds,
        RectF ApproveBounds,
        RectF DenyBounds);

    private sealed record CachedBlock(
        string ContentKey,
        float BubbleWidth,
        bool Expanded,
        ChatApprovalStatus Status,
        MessageBlock Block)
    {
        public bool Matches(ChatMessageSnapshot snapshot, float bubbleWidth) =>
            Math.Abs(BubbleWidth - bubbleWidth) < 0.5f
            && Expanded == snapshot.IsToolExpanded
            && Status == snapshot.ApprovalStatus
            && string.Equals(ContentKey, snapshot.CanonicalSelectableText, StringComparison.Ordinal);
    }

    private sealed record MessageLayout(
        int MessageIndex,
        bool IsUser,
        bool IsTool,
        bool IsApproval,
        bool IsToolExpanded,
        ChatApprovalStatus ApprovalStatus,
        string Timestamp,
        MessageBlock Block,
        float OffsetX,
        float OffsetY)
    {
        public string VisibleText => Block.VisibleText;
        public IReadOnlyList<TextLineLayout> Lines => Block.Lines;
        public RectF Bubble => new(OffsetX, OffsetY, Block.Width, Block.Height);
        public RectF HeaderBounds => Absolute(Block.HeaderBounds);
        public RectF TimestampBounds => Absolute(Block.TimestampBounds);
        public RectF CopyBounds => Absolute(Block.CopyBounds);
        public RectF ToggleBounds => Absolute(Block.ToggleBounds);
        public RectF ApproveBounds => Absolute(Block.ApproveBounds);
        public RectF DenyBounds => Absolute(Block.DenyBounds);

        public PointF ToLocal(PointF point) => new(point.X - OffsetX, point.Y - OffsetY);

        private RectF Absolute(RectF local) => local.Width <= 0 && local.Height <= 0
            ? RectF.Zero
            : new RectF(local.X + OffsetX, local.Y + OffsetY, local.Width, local.Height);
    }
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
        Color Error,
        Color ErrorBackground,
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
            var error = ResolveColor("ErrorColor", dark ? "#FF8A80" : "#D9534F");
            var errorBackground = ResolveColor("ErrorBackgroundColor", dark ? "#4A2525" : "#FDECEA");
            var code = ResolveColor("HoverColor", dark ? "#383838" : "#EFEEF4");
            var selection = Color.FromRgba(accent.Red, accent.Green, accent.Blue, 0.30f);
            return new CanvasColors(surface, primary, secondary, user, assistant, accent,
                error, errorBackground, code, selection);
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
