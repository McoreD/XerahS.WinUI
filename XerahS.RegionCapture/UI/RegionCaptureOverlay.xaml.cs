using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.System;
using WinRT.Interop;
using XerahS.RegionCapture.Core.Capture;
using XerahS.RegionCapture.Core.Coordinates;
using XerahS.RegionCapture.Input;
using XerahS.RegionCapture.NativeMethods;

using System.IO;

namespace XerahS.RegionCapture.UI;

public sealed partial class RegionCaptureOverlay : Window
{
    private readonly RegionCaptureOptions _options;
    private readonly TaskCompletionSource<RegionCaptureResult?> _completionSource;
    private readonly VirtualDesktop _virtualDesktop;
    private readonly CoordinateMapper _coordinateMapper;
    private readonly SelectionStateMachine _stateMachine;
    private readonly WindowDetector _windowDetector;
    private readonly DateTimeOffset _captureStartTime;

    private IntPtr _hwnd;
    private Timer? _windowDetectionTimer;
    private (int x, int y) _lastCursorPosition;
    private readonly string _logFile;

    public RegionCaptureOverlay(RegionCaptureOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();

        _completionSource = new TaskCompletionSource<RegionCaptureResult?>();
        _captureStartTime = DateTimeOffset.Now;

        _logFile = Path.Combine(Path.GetTempPath(), "RegionCapture_Debug.log");
        Log($"Constructor called. Log file: {_logFile}");

        // Initialize core systems
        _virtualDesktop = VirtualDesktop.Enumerate();
        _coordinateMapper = new CoordinateMapper(_virtualDesktop, _options.EnableDiagnostics);
        _stateMachine = new SelectionStateMachine(_options.DragThreshold);

        this.InitializeComponent();

        // Get window handle and configure as overlay
        _hwnd = WindowNative.GetWindowHandle(this);
        _windowDetector = new WindowDetector(_hwnd, _options.IncludeSystemWindows);

        ConfigureOverlayWindow();
        ApplyOptions();
        SubscribeToEvents();

        // Start window detection timer if enabled
        if (_options.EnableWindowDetection)
        {
            _windowDetectionTimer = new Timer(
                OnWindowDetectionTick,
                null,
                TimeSpan.FromMilliseconds(_options.WindowDetectionDebounceMs),
                TimeSpan.FromMilliseconds(_options.WindowDetectionDebounceMs));
        }
    }

    /// <summary>
    /// Shows the overlay and waits for capture completion.
    /// </summary>
    public async Task<RegionCaptureResult?> ShowAsync()
    {
        Log("ShowAsync called");
        // Freeze screen before showing
        await CaptureAndSetBackgroundAsync();

        this.Activate();
        Log("Window activated");
        return await _completionSource.Task;
    }

    /// <summary>
    /// Cancels the capture operation.
    /// </summary>
    public void Cancel()
    {
        _stateMachine.Cancel();
        CloseOverlay(null);
    }

    private void ConfigureOverlayWindow()
    {
        // Get AppWindow for advanced configuration
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        
        Log($"ConfigureOverlayWindow: AppWindow obtained. Presenter: {appWindow.Presenter?.GetType().Name}");

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(false, false);
            Log("ConfigureOverlayWindow: SetBorderAndTitleBar(false, false) called");
        }

        // Force ExtendsContentIntoTitleBar as fallback for removing caption
        this.ExtendsContentIntoTitleBar = true;

        // Position and size to cover entire virtual desktop
        var vdBounds = _virtualDesktop.VirtualBounds;
        
        Log($"ConfigureOverlayWindow: Moving to {vdBounds.Left},{vdBounds.Top} {vdBounds.Width}x{vdBounds.Height}");

        // Move and resize window to cover virtual desktop
        appWindow.MoveAndResize(new RectInt32(
            vdBounds.Left,
            vdBounds.Top,
            vdBounds.Width,
            vdBounds.Height));

        // Set DPI awareness
        Win32.SetProcessDpiAwareness(Win32.PROCESS_DPI_AWARENESS.PROCESS_PER_MONITOR_DPI_AWARE);
    }

    private async Task CaptureAndSetBackgroundAsync()
    {
        try
        {
            Log("CaptureAndSetBackgroundAsync started");
            // Capture the entire virtual desktop
            var bounds = _virtualDesktop.VirtualBounds;
            Log($"Capturing region: {bounds.Left},{bounds.Top} {bounds.Width}x{bounds.Height}");

            var softwareBitmap = await ScreenCaptureEngine.CaptureRegionAsync(bounds);
            Log($"Capture engine returned bitmap: {softwareBitmap.PixelWidth}x{softwareBitmap.PixelHeight}");

            // Convert to SoftwareBitmapSource
            var source = new Microsoft.UI.Xaml.Media.Imaging.SoftwareBitmapSource();
            await source.SetBitmapAsync(softwareBitmap);

            BackgroundImage.Source = source;
            Log("BackgroundImage source set successfully");
        }
        catch (Exception ex)
        {
            Log($"Failed to capture background: {ex}");
            System.Diagnostics.Debug.WriteLine($"Failed to capture background: {ex}");
        }
    }

    private void Log(string message)
    {
        try
        {
            var msg = $"{DateTime.Now:HH:mm:ss.fff}: {message}";
            File.AppendAllText(_logFile, msg + Environment.NewLine);
            Console.WriteLine(msg); // Output to console for dotnet run capture
        }
        catch { }
    }

    private void ApplyOptions()
    {
        // Apply dim opacity
        DimOverlay.Opacity = _options.DimOpacity;

        // Apply selection color
        SelectionRect.Stroke = new Microsoft.UI.Xaml.Media.SolidColorBrush(_options.SelectionColor);
        SelectionRect.StrokeThickness = _options.SelectionStrokeThickness;

        WindowHighlightRect.Stroke = new Microsoft.UI.Xaml.Media.SolidColorBrush(_options.WindowHighlightColor);

        // Show/hide crosshair
        CrosshairHorizontal.Visibility = _options.ShowCrosshair ? Visibility.Visible : Visibility.Collapsed;
        CrosshairVertical.Visibility = _options.ShowCrosshair ? Visibility.Visible : Visibility.Collapsed;

        // Show/hide HUD
        CoordinateHUD.Visibility = _options.ShowCoordinateHUD ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SubscribeToEvents()
    {
        // Pointer events
        RootGrid.PointerPressed += OnPointerPressed;
        RootGrid.PointerMoved += OnPointerMoved;
        RootGrid.PointerReleased += OnPointerReleased;

        // Keyboard events
        RootGrid.KeyDown += OnKeyDown;

        // State machine events
        _stateMachine.StateChanged += OnStateChanged;
        _stateMachine.SelectionChanged += OnSelectionChanged;
        _stateMachine.WindowHoverChanged += OnWindowHoverChanged;

        // Window activated event to hide help
        this.Activated += (s, e) =>
        {
            HelpOverlay.Visibility = Visibility.Collapsed;
        };
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(RootGrid);

        // Right click = cancel
        if (point.Properties.IsRightButtonPressed)
        {
            _stateMachine.Cancel();
            return;
        }

        if (point.Properties.IsLeftButtonPressed)
        {
            var (physX, physY) = _coordinateMapper.OverlayDipToPhysical(this, point.Position.X, point.Position.Y);
            _stateMachine.OnPointerPressed(physX, physY);
        }
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        Log("OnDoubleTapped fired");
        _stateMachine.Confirm();
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(RootGrid);
        var (physX, physY) = _coordinateMapper.OverlayDipToPhysical(this, point.Position.X, point.Position.Y);

        _lastCursorPosition = (physX, physY);

        // Update crosshair
        if (_options.ShowCrosshair)
        {
            UpdateCrosshair(point.Position.X, point.Position.Y);
        }

        // Update state machine
        if (point.Properties.IsLeftButtonPressed)
        {
            _stateMachine.OnPointerMoved(physX, physY);
        }
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(RootGrid);
        var (physX, physY) = _coordinateMapper.OverlayDipToPhysical(this, point.Position.X, point.Position.Y);

        _stateMachine.OnPointerReleased(physX, physY);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape:
                _stateMachine.Cancel();
                break;

            case VirtualKey.Enter:
                _stateMachine.Confirm();
                break;

            case VirtualKey.Left:
                {
                    int amount = IsShiftPressed() ? -_options.NudgeAmountLarge : -_options.NudgeAmountSmall;
                    _stateMachine.NudgeSelection(amount, 0);
                    break;
                }

            case VirtualKey.Right:
                {
                    int amount = IsShiftPressed() ? _options.NudgeAmountLarge : _options.NudgeAmountSmall;
                    _stateMachine.NudgeSelection(amount, 0);
                    break;
                }

            case VirtualKey.Up:
                {
                    int amount = IsShiftPressed() ? -_options.NudgeAmountLarge : -_options.NudgeAmountSmall;
                    _stateMachine.NudgeSelection(0, amount);
                    break;
                }

            case VirtualKey.Down:
                {
                    int amount = IsShiftPressed() ? _options.NudgeAmountLarge : _options.NudgeAmountSmall;
                    _stateMachine.NudgeSelection(0, amount);
                    break;
                }
        }
    }

    private void OnStateChanged(object? sender, StateChangedEventArgs e)
    {
        if (e.NewState == SelectionState.Confirmed)
        {
            PerformCaptureAsync();
        }
        else if (e.NewState == SelectionState.Cancelled)
        {
            CloseOverlay(null);
        }
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateSelectionVisuals(e.Selection, e.IsWindowSelection);
    }

    private void OnWindowHoverChanged(object? sender, WindowHoverChangedEventArgs e)
    {
        if (e.Window != null)
        {
            ShowWindowHighlight(e.Window.Bounds);
        }
        else
        {
            HideWindowHighlight();
        }
    }

    private void OnWindowDetectionTick(object? state)
    {
        if (_stateMachine.State != SelectionState.Idle && _stateMachine.State != SelectionState.HoverWindowCandidate)
            return;

        if (!_options.EnableWindowDetection)
            return;

        // Detect window at cursor position
        var (physX, physY) = _lastCursorPosition;
        var detectedWindow = _windowDetector.DetectWindowAtPoint(physX, physY);

        // Update state machine on UI thread
        DispatcherQueue.TryEnqueue(() =>
        {
            _stateMachine.UpdateHoveredWindow(detectedWindow);
        });
    }

    private void UpdateCrosshair(double dipX, double dipY)
    {
        var vdBounds = _virtualDesktop.VirtualBounds;
        var (vdDipWidth, vdDipHeight) = (vdBounds.Width, vdBounds.Height);

        CrosshairHorizontal.X1 = 0;
        CrosshairHorizontal.Y1 = dipY;
        CrosshairHorizontal.X2 = vdDipWidth;
        CrosshairHorizontal.Y2 = dipY;

        CrosshairVertical.X1 = dipX;
        CrosshairVertical.Y1 = 0;
        CrosshairVertical.X2 = dipX;
        CrosshairVertical.Y2 = vdDipHeight;
    }

    private void ShowWindowHighlight(Win32.RECT physBounds)
    {
        var (dipX, dipY) = _coordinateMapper.PhysicalToOverlayDip(this, physBounds.Left, physBounds.Top);

        WindowHighlightRect.Visibility = Visibility.Visible;
        Microsoft.UI.Xaml.Controls.Canvas.SetLeft(WindowHighlightRect, dipX);
        Microsoft.UI.Xaml.Controls.Canvas.SetTop(WindowHighlightRect, dipY);
        WindowHighlightRect.Width = physBounds.Width;
        WindowHighlightRect.Height = physBounds.Height;
    }

    private void HideWindowHighlight()
    {
        WindowHighlightRect.Visibility = Visibility.Collapsed;
    }

    private void UpdateSelectionVisuals(Win32.RECT physSelection, bool isWindowSelection)
    {
        // Apply snapping if enabled
        if (_options.EnableEdgeSnapping && !isWindowSelection)
        {
            physSelection = _coordinateMapper.ApplyEdgeSnapping(physSelection, _options.SnapThreshold);
        }

        // Ensure minimum size
        if (physSelection.Width < _options.MinimumSelectionSize)
        {
            physSelection = new Win32.RECT(
                physSelection.Left,
                physSelection.Top,
                physSelection.Left + _options.MinimumSelectionSize,
                physSelection.Bottom);
        }

        if (physSelection.Height < _options.MinimumSelectionSize)
        {
            physSelection = new Win32.RECT(
                physSelection.Left,
                physSelection.Top,
                physSelection.Right,
                physSelection.Top + _options.MinimumSelectionSize);
        }

        // Convert to overlay DIPs
        var (dipX, dipY) = _coordinateMapper.PhysicalToOverlayDip(this, physSelection.Left, physSelection.Top);

        // Update selection rectangle
        SelectionRect.Visibility = Visibility.Visible;
        Microsoft.UI.Xaml.Controls.Canvas.SetLeft(SelectionRect, dipX);
        Microsoft.UI.Xaml.Controls.Canvas.SetTop(SelectionRect, dipY);
        SelectionRect.Width = physSelection.Width;
        SelectionRect.Height = physSelection.Height;

        // Update HUD
        if (_options.ShowCoordinateHUD)
        {
            var monitor = _virtualDesktop.GetMonitorAtPhysicalPointOrNearest(physSelection.Left, physSelection.Top);
            HUDText.Text = $"X: {physSelection.Left}, Y: {physSelection.Top}, W: {physSelection.Width}, H: {physSelection.Height}";
            HUDMonitorText.Text = $"{monitor.DeviceName} @ {monitor.ScaleFactor:P0} ({monitor.DpiX} DPI)";
        }

        // Hide window highlight when manual selection is active
        if (!isWindowSelection)
        {
            HideWindowHighlight();
        }
    }

    private async void PerformCaptureAsync()
    {
        if (!_stateMachine.SelectionRect.HasValue)
        {
            CloseOverlay(null);
            return;
        }

        var physSelection = _stateMachine.SelectionRect.Value;

        // Clamp to virtual desktop bounds
        physSelection = _virtualDesktop.ClampToVirtualDesktop(physSelection);

        try
        {
            // Hide overlay during capture to avoid capturing ourselves
            RootGrid.Visibility = Visibility.Collapsed;

            // Small delay to ensure overlay is hidden
            await Task.Delay(50);

            // Capture the region
            SoftwareBitmap bitmap = await ScreenCaptureEngine.CaptureRegionAsync(physSelection);

            // Build diagnostics
            var diagnostics = BuildDiagnostics();

            // Get primary monitor for the selection
            var primaryMonitor = _virtualDesktop.GetMonitorAtPhysicalPointOrNearest(physSelection.Left, physSelection.Top);

            // Determine if this was a window capture
            bool isWindowCapture = false;
            IntPtr windowHandle = IntPtr.Zero;

            if (_stateMachine.HoveredWindow != null &&
                _stateMachine.HoveredWindow.Bounds.Left == physSelection.Left &&
                _stateMachine.HoveredWindow.Bounds.Top == physSelection.Top &&
                _stateMachine.HoveredWindow.Bounds.Width == physSelection.Width &&
                _stateMachine.HoveredWindow.Bounds.Height == physSelection.Height)
            {
                isWindowCapture = true;
                windowHandle = _stateMachine.HoveredWindow.Handle;
            }

            // Create result
            var result = new RegionCaptureResult(
                bitmap,
                _virtualDesktop.ToPublicRectangle(physSelection),
                primaryMonitor.ToPublicMetadata(),
                diagnostics,
                isWindowCapture,
                windowHandle);

            CloseOverlay(result);
        }
        catch (Exception ex)
        {
            // TODO: Better error handling
            System.Diagnostics.Debug.WriteLine($"Capture failed: {ex}");
            CloseOverlay(null);
        }
    }

    private CaptureDiagnostics BuildDiagnostics()
    {
        var allMonitors = new MonitorMetadata[_virtualDesktop.Monitors.Count];
        for (int i = 0; i < _virtualDesktop.Monitors.Count; i++)
        {
            allMonitors[i] = _virtualDesktop.Monitors[i].ToPublicMetadata();
        }

        return new CaptureDiagnostics(
            allMonitors,
            _virtualDesktop.ToPublicRectangle(_virtualDesktop.VirtualBounds),
            _captureStartTime,
            DateTimeOffset.Now,
            _coordinateMapper.GetTransformLog());
    }

    private void CloseOverlay(RegionCaptureResult? result)
    {
        // Stop window detection
        _windowDetectionTimer?.Dispose();
        _windowDetectionTimer = null;

        // Complete the task
        _completionSource.TrySetResult(result);

        // Close window
        this.Close();
    }

    private bool IsShiftPressed()
    {
        var state = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        return (state & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
    }
}
