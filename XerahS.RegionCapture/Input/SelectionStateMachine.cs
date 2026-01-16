using System;
using XerahS.RegionCapture.NativeMethods;

namespace XerahS.RegionCapture.Input;

/// <summary>
/// State machine for region selection interaction.
/// Manages transitions between hover, drag, select, and adjust states.
/// </summary>
internal sealed class SelectionStateMachine
{
    private SelectionState _state;
    private Win32.RECT? _selectionRect;
    private DetectedWindow? _hoveredWindow;
    private (int x, int y) _dragStartPoint;
    private (int x, int y) _currentPoint;
    private bool _isDragging;
    private readonly double _dragThreshold;

    public SelectionState State => _state;
    public Win32.RECT? SelectionRect => _selectionRect;
    public DetectedWindow? HoveredWindow => _hoveredWindow;
    public bool IsDragging => _isDragging;

    public event EventHandler<StateChangedEventArgs>? StateChanged;
    public event EventHandler<SelectionChangedEventArgs>? SelectionChanged;
    public event EventHandler<WindowHoverChangedEventArgs>? WindowHoverChanged;

    public SelectionStateMachine(double dragThreshold)
    {
        _state = SelectionState.Idle;
        _dragThreshold = dragThreshold;
    }

    /// <summary>
    /// Updates the hovered window based on cursor position.
    /// </summary>
    public void UpdateHoveredWindow(DetectedWindow? window)
    {
        if (_state != SelectionState.Idle && _state != SelectionState.HoverWindowCandidate)
            return;

        var previousWindow = _hoveredWindow;
        _hoveredWindow = window;

        if (window != null && _state == SelectionState.Idle)
        {
            ChangeState(SelectionState.HoverWindowCandidate);
        }
        else if (window == null && _state == SelectionState.HoverWindowCandidate)
        {
            ChangeState(SelectionState.Idle);
        }

        if (!AreSameWindow(previousWindow, window))
        {
            WindowHoverChanged?.Invoke(this, new WindowHoverChangedEventArgs(window));
        }
    }

    /// <summary>
    /// Handles pointer pressed event.
    /// </summary>
    public void OnPointerPressed(int physX, int physY)
    {
        _dragStartPoint = (physX, physY);
        _currentPoint = (physX, physY);
        _isDragging = false;

        // If hovering over a window, prepare for potential click-to-select
        // But don't commit yet - wait to see if user drags
        if (_state == SelectionState.HoverWindowCandidate && _hoveredWindow != null)
        {
            // Stay in HoverWindowCandidate state
            // Selection will be committed on pointer released if no drag occurs
        }
        else
        {
            // Start manual selection
            ChangeState(SelectionState.DragSelecting);
        }
    }

    /// <summary>
    /// Handles pointer moved event.
    /// </summary>
    public void OnPointerMoved(int physX, int physY)
    {
        _currentPoint = (physX, physY);

        if (_state == SelectionState.HoverWindowCandidate && !_isDragging)
        {
            // Check if drag threshold exceeded
            int dx = physX - _dragStartPoint.x;
            int dy = physY - _dragStartPoint.y;
            double distance = Math.Sqrt(dx * dx + dy * dy);

            if (distance > _dragThreshold)
            {
                // User is dragging, switch to manual selection
                _isDragging = true;
                _hoveredWindow = null;
                ChangeState(SelectionState.DragSelecting);
                UpdateSelection();
            }
        }
        else if (_state == SelectionState.DragSelecting)
        {
            _isDragging = true;
            UpdateSelection();
        }
        else if (_state == SelectionState.Selected || _state == SelectionState.Adjusting)
        {
            // Handle selection adjustment (resize/move)
            // This would involve hit testing against selection handles
            // For now, simplified implementation
        }
    }

    /// <summary>
    /// Handles pointer released event.
    /// </summary>
    public void OnPointerReleased(int physX, int physY)
    {
        _currentPoint = (physX, physY);

        if (_state == SelectionState.HoverWindowCandidate && !_isDragging && _hoveredWindow != null)
        {
            // Click-to-select window
            _selectionRect = _hoveredWindow.Bounds;
            ChangeState(SelectionState.Selected);
            SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(_selectionRect.Value, true, _hoveredWindow.Handle));
        }
        else if (_state == SelectionState.DragSelecting)
        {
            // Finalize manual selection
            UpdateSelection();

            if (_selectionRect.HasValue)
            {
                ChangeState(SelectionState.Selected);
                SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(_selectionRect.Value, false, IntPtr.Zero));
            }
            else
            {
                // Selection too small or invalid, return to idle
                ChangeState(SelectionState.Idle);
            }
        }

        _isDragging = false;
    }

    /// <summary>
    /// Confirms the selection and completes the capture.
    /// </summary>
    public void Confirm()
    {
        if (_state == SelectionState.Selected && _selectionRect.HasValue)
        {
            ChangeState(SelectionState.Confirmed);
        }
    }

    /// <summary>
    /// Cancels the selection and exits capture mode.
    /// </summary>
    public void Cancel()
    {
        ChangeState(SelectionState.Cancelled);
    }

    /// <summary>
    /// Nudges the selection by the specified amount.
    /// </summary>
    public void NudgeSelection(int dx, int dy)
    {
        if (_state != SelectionState.Selected || !_selectionRect.HasValue)
            return;

        var rect = _selectionRect.Value;
        _selectionRect = new Win32.RECT(
            rect.Left + dx,
            rect.Top + dy,
            rect.Right + dx,
            rect.Bottom + dy);

        SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(_selectionRect.Value, false, IntPtr.Zero));
    }

    /// <summary>
    /// Resizes the selection by the specified amount.
    /// </summary>
    public void ResizeSelection(int dw, int dh)
    {
        if (_state != SelectionState.Selected || !_selectionRect.HasValue)
            return;

        var rect = _selectionRect.Value;
        _selectionRect = new Win32.RECT(
            rect.Left,
            rect.Top,
            rect.Right + dw,
            rect.Bottom + dh);

        SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(_selectionRect.Value, false, IntPtr.Zero));
    }

    private void UpdateSelection()
    {
        int left = Math.Min(_dragStartPoint.x, _currentPoint.x);
        int top = Math.Min(_dragStartPoint.y, _currentPoint.y);
        int right = Math.Max(_dragStartPoint.x, _currentPoint.x);
        int bottom = Math.Max(_dragStartPoint.y, _currentPoint.y);

        var rect = new Win32.RECT(left, top, right, bottom);

        // Only update if dimensions are meaningful
        if (rect.Width > 0 && rect.Height > 0)
        {
            _selectionRect = rect;
            SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(rect, false, IntPtr.Zero));
        }
    }

    private void ChangeState(SelectionState newState)
    {
        var oldState = _state;
        _state = newState;

        StateChanged?.Invoke(this, new StateChangedEventArgs(oldState, newState));
    }

    private static bool AreSameWindow(DetectedWindow? a, DetectedWindow? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        return a.Handle == b.Handle;
    }
}

/// <summary>
/// Selection state enumeration.
/// </summary>
internal enum SelectionState
{
    /// <summary>No interaction, showing crosshair only.</summary>
    Idle,

    /// <summary>Cursor is hovering over an eligible window, showing highlight.</summary>
    HoverWindowCandidate,

    /// <summary>User is dragging to create a manual selection.</summary>
    DragSelecting,

    /// <summary>Selection is finalized and can be confirmed or adjusted.</summary>
    Selected,

    /// <summary>User is adjusting selection (resize/move).</summary>
    Adjusting,

    /// <summary>Selection confirmed, ready to capture.</summary>
    Confirmed,

    /// <summary>Capture cancelled.</summary>
    Cancelled
}

internal sealed class StateChangedEventArgs : EventArgs
{
    public SelectionState OldState { get; }
    public SelectionState NewState { get; }

    public StateChangedEventArgs(SelectionState oldState, SelectionState newState)
    {
        OldState = oldState;
        NewState = newState;
    }
}

internal sealed class SelectionChangedEventArgs : EventArgs
{
    public Win32.RECT Selection { get; }
    public bool IsWindowSelection { get; }
    public IntPtr WindowHandle { get; }

    public SelectionChangedEventArgs(Win32.RECT selection, bool isWindowSelection, IntPtr windowHandle)
    {
        Selection = selection;
        IsWindowSelection = isWindowSelection;
        WindowHandle = windowHandle;
    }
}

internal sealed class WindowHoverChangedEventArgs : EventArgs
{
    public DetectedWindow? Window { get; }

    public WindowHoverChangedEventArgs(DetectedWindow? window)
    {
        Window = window;
    }
}
