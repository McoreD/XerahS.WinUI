# XerahS.WinUI - Region Capture

> **Robust multi-monitor, mixed-DPI region capture for WinUI 3**

A production-ready WinUI 3 library providing pixel-perfect region capture with comprehensive support for:
- **Multi-monitor setups** with negative coordinates
- **Per-monitor DPI awareness** (100%, 125%, 150%, 200%, custom scaling)
- **Window auto-detection** with single-click capture
- **Manual rectangle selection** with visual guidance
- **Power-user affordances** (keyboard shortcuts, nudging, snapping)

Built specifically to solve coordinate-space mismatches and DPI-related capture errors that plague screenshot tools in mixed-DPI environments.

---

## Features

### Core Capabilities

✅ **Pixel-Accurate Capture**
- Canonical coordinate space: physical pixels in virtual desktop coordinates
- Comprehensive coordinate transformation logging for debugging
- Automated test harness validates correctness across DPI configurations

✅ **Multi-Monitor Support**
- Handles negative virtual desktop coordinates (monitors left/above primary)
- Correct operation when dragging selection across monitor boundaries
- Per-monitor DPI scaling applied accurately

✅ **Window Auto-Detection**
- Hover over any window to highlight its bounds
- Single-click to capture window (or drag to override with manual selection)
- Filters out tooltips, system UI, and transient windows
- Uses DWM extended frame bounds for accurate window edges

✅ **Professional UX**
- Full-screen dimmed overlay across all monitors
- Dashed selection rectangle with customizable color and thickness
- Optional crosshair for precision
- Real-time HUD showing X/Y/W/H and monitor DPI info
- Resize handles (when selected)

✅ **Power-User Features**
- Arrow keys: nudge selection by 1px (Shift: 10px)
- Enter: confirm | Esc: cancel | Right-click: cancel/exit
- Edge snapping to monitor bounds (configurable threshold)
- Minimum selection size enforcement

---

## Quick Start

### 1. Installation

Add the `XerahS.RegionCapture` project to your solution and reference it:

```xml
<ItemGroup>
  <ProjectReference Include="..\XerahS.RegionCapture\XerahS.RegionCapture.csproj" />
</ItemGroup>
```

### 2. Configure DPI Awareness

Ensure your app manifest includes per-monitor DPI awareness:

```xml
<application xmlns="urn:schemas-microsoft-com:asm.v3">
  <windowsSettings>
    <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
  </windowsSettings>
</application>
```

### 3. Basic Usage

```csharp
using XerahS.RegionCapture;

// Create manager (once, typically during app initialization)
var captureManager = new RegionCaptureManager();

// Start capture (e.g., from a hotkey handler or button click)
var result = await captureManager.CaptureRegionAsync();

if (result != null)
{
    // Access the captured bitmap
    SoftwareBitmap bitmap = result.Bitmap;

    // Get capture metadata
    Console.WriteLine($"Region: {result.Region}");
    Console.WriteLine($"Monitor: {result.PrimaryMonitor}");
    Console.WriteLine($"Window capture: {result.IsWindowCapture}");

    // Save to file, upload, or process further
    await SaveBitmapAsync(bitmap, "screenshot.png");

    // Clean up
    result.Dispose();
}
else
{
    // User cancelled
}
```

### 4. Advanced Configuration

```csharp
var options = new RegionCaptureOptions
{
    // Window detection
    EnableWindowDetection = true,
    EnableQuickWindowCapture = true,
    WindowDetectionDebounceMs = 50,

    // Visual guidance
    ShowCrosshair = true,
    ShowCoordinateHUD = true,
    ShowMagnifier = false, // Enable for pixel-perfect alignment
    MagnifierZoom = 3.0,

    // Appearance
    DimOpacity = 0.5,
    SelectionColor = Colors.White,
    WindowHighlightColor = Colors.Cyan,
    SelectionStrokeThickness = 2.0,

    // Snapping
    EnableEdgeSnapping = true,
    SnapThreshold = 10, // pixels

    // Interaction
    DragThreshold = 5.0, // DIPs - prevents accidental drag when clicking window
    MinimumSelectionSize = 10, // pixels
    NudgeAmountSmall = 1, // Arrow key nudge
    NudgeAmountLarge = 10, // Shift+Arrow nudge

    // System windows
    IncludeSystemWindows = false, // Exclude taskbar, etc.

    // Diagnostics
    EnableDiagnostics = true // Logs coordinate transforms for debugging
};

var result = await captureManager.CaptureRegionAsync(options);
```

---

## Architecture

### Coordinate Space Model

**Canonical Space**: Physical pixels in virtual desktop coordinates.

All internal calculations and capture operations use this space. The library handles:
- **DIP → Physical** conversion (from WinUI pointer events)
- **Physical → DIP** conversion (for rendering on overlay)
- **Per-monitor DPI** scaling (1.0 = 96 DPI, 1.5 = 144 DPI, 2.0 = 192 DPI, etc.)
- **Negative coordinates** (monitors positioned left/above primary)

### Component Overview

```
PublicAPI/
├── RegionCaptureManager.cs      ← Entry point
├── RegionCaptureResult.cs       ← Return value with bitmap + metadata
└── RegionCaptureOptions.cs      ← Configuration

Core/
├── Coordinates/
│   ├── VirtualDesktop.cs        ← Monitor enumeration & layout
│   ├── MonitorInfo.cs           ← Per-monitor DPI & bounds
│   └── CoordinateMapper.cs      ← Transform pipeline + logging
├── Capture/
│   └── ScreenCaptureEngine.cs   ← GDI+ BitBlt capture

Input/
├── SelectionStateMachine.cs     ← Idle → Hover → Drag → Selected → Confirm/Cancel
└── WindowDetector.cs            ← Window-under-cursor detection & filtering

UI/
├── RegionCaptureOverlay.xaml    ← Full-screen overlay UI
└── RegionCaptureOverlay.xaml.cs ← Input handling & rendering

NativeMethods/
└── Win32.cs                     ← P/Invoke for DPI, monitors, window APIs
```

### State Machine

```
    ┌────────┐
    │  Idle  │ ← No interaction, crosshair only
    └────┬───┘
         │ Window under cursor?
         ▼
┌────────────────────┐
│ HoverWindowCandidate│ ← Highlight window border
└────┬───────────────┘
     │ Left click (no drag) → Select window bounds → [Selected]
     │ Drag beyond threshold → Manual selection
     ▼
┌──────────────┐
│ DragSelecting│ ← User drawing rectangle
└────┬─────────┘
     │ Release
     ▼
┌──────────┐
│ Selected │ ← Can nudge/resize with keyboard
└────┬─────┘
     │ Enter
     ▼
┌───────────┐
│ Confirmed │ → Capture & return
└───────────┘

At any point: Esc → [Cancelled] → Exit
```

---

## Testing

### Automated Pixel-Correctness Tests

Run the test harness to validate capture accuracy:

```bash
dotnet test XerahS.RegionCapture.Tests --logger "console;verbosity=detailed"
```

**Test Coverage**:
- ✅ Random regions on primary monitor
- ✅ Random regions on all monitors (mixed DPI)
- ✅ Monitor edge cases (corners, center)
- ✅ Negative coordinates (monitors left/above primary)
- ✅ Crossing monitor boundaries

**Failure Artifacts**: On failure, saves detailed report to `%TEMP%\RegionCapture_FailureReport_*.txt` including:
- Monitor configuration dump
- Failed region coordinates
- Transform logs

### Manual Testing Checklist

1. **Single monitor (100% DPI)**: Capture random regions → verify pixel-perfect
2. **Single monitor (150% DPI)**: Same as above
3. **Dual monitor (100% + 150%)**: Capture on each monitor separately
4. **Dual monitor**: Drag selection across boundary
5. **Monitor left of primary (negative X)**: Capture on left monitor
6. **Monitor above primary (negative Y)**: Capture on top monitor
7. **Window auto-select**: Hover → highlight → click → verify window bounds captured
8. **Drag override**: Hover window → drag → verify manual selection wins
9. **Keyboard nudge**: Select → arrow keys → verify 1px/10px movement
10. **Edge snapping**: Drag near monitor edge → verify snap

---

## Diagnostics

### Enable Logging

```csharp
var options = new RegionCaptureOptions
{
    EnableDiagnostics = true
};

var result = await captureManager.CaptureRegionAsync(options);

if (result != null)
{
    // Full coordinate transformation log
    Console.WriteLine(result.Diagnostics.TransformLog);

    // Monitor configuration at capture time
    foreach (var monitor in result.Diagnostics.AllMonitors)
    {
        Console.WriteLine(monitor);
    }

    // Virtual desktop bounds
    Console.WriteLine($"Virtual Desktop: {result.Diagnostics.VirtualDesktopBounds}");
}
```

### Sample Transform Log

```
=== Coordinate Transformation Log ===
=== Virtual Desktop Configuration ===
Virtual Bounds: (-1920,0)-(3840,1080) [5760x1080]
Monitor Count: 3

Monitor 1: \\.\DISPLAY1 @ 100% (96 DPI) (-1920,0)-(0,1080) [1920x1080] [PRIMARY]
  Work Area: (-1920,0)-(0,1040)
Monitor 2: \\.\DISPLAY2 @ 150% (144 DPI) (0,0)-(2560,1440) [2560x1440]
  Work Area: (0,40)-(2560,1400)
Monitor 3: \\.\DISPLAY3 @ 200% (192 DPI) (2560,0)-(3840,1080) [1280x1080]
  Work Area: (2560,0)-(3840,1040)

[12:34:56.789] OverlayDipToPhysical: Input DIP (1234.50, 567.80)
[12:34:56.790]   Overlay window rect (physical): (-1920,0)-(3840,1080)
[12:34:56.790]   Virtual desktop bounds: (-1920,0)-(3840,1080)
[12:34:56.791]   Estimated physical (direct): (-686, 568)
[12:34:56.791]   Monitor at position: \\.\DISPLAY1 @ 100% (96 DPI)
[12:34:56.791]   Final physical: (-686, 568)
...
```

---

## Known Limitations & Trade-offs

### Capture Method: GDI+ BitBlt

**Chosen**: GDI+ `BitBlt` for simplicity and reliability.

**Alternatives Considered**:
- **Windows.Graphics.Capture API**: Modern, GPU-accelerated, composition-based. Requires more setup and async handling. Future enhancement candidate.
- **Desktop Duplication API**: Lowest latency, designed for real-time. Overkill for single-shot capture and requires DirectX setup.

**Trade-off**: BitBlt is synchronous and CPU-based but guarantees pixel-perfect results and works on all Windows 10+ versions without additional dependencies.

### Overlay Window Model: Single Borderless Window

**Chosen**: One overlay window spanning the entire virtual desktop.

**Alternative**: One overlay per monitor.

**Trade-off**: Single window is simpler for input handling and rendering but requires careful DPI-aware positioning. Per-monitor overlays would handle DPI transitions more naturally but complicate input coordination.

### Window Detection: Polling Timer

**Chosen**: Timer-based polling (50ms default) to detect window under cursor.

**Alternative**: Win32 mouse hook for real-time detection.

**Trade-off**: Polling is safer (no global hooks) and sufficient for UX (<1 frame latency). Hooks would be more responsive but introduce security/stability risks.

---

## API Reference

### RegionCaptureManager

```csharp
public sealed class RegionCaptureManager : IDisposable
{
    public RegionCaptureManager(DispatcherQueue? dispatcherQueue = null);

    // Start capture (shows overlay, waits for user selection)
    public Task<RegionCaptureResult?> CaptureRegionAsync(RegionCaptureOptions? options = null);

    // Cancel active capture
    public void CancelCapture();

    // Check if capture is active
    public bool IsCapturing { get; }
}
```

### RegionCaptureResult

```csharp
public sealed class RegionCaptureResult : IDisposable
{
    public SoftwareBitmap Bitmap { get; }                   // BGRA8 format
    public CaptureRectangle Region { get; }                 // Physical pixels
    public MonitorMetadata PrimaryMonitor { get; }          // Monitor containing top-left
    public CaptureDiagnostics Diagnostics { get; }          // Logs & metadata
    public bool IsWindowCapture { get; }                    // Auto-select vs manual
    public IntPtr WindowHandle { get; }                     // HWND if window capture
}
```

### RegionCaptureOptions

See [Advanced Configuration](#4-advanced-configuration) above for full property list.

---

## Integration Patterns

### Hotkey Integration (Global)

```csharp
// Register global hotkey (e.g., using Windows.System.GlobalSystemMediaTransportControlsSessionManager or Win32 RegisterHotKey)
// Then call capture manager:

private async void OnCaptureHotkey()
{
    var result = await _captureManager.CaptureRegionAsync();
    if (result != null)
    {
        await ProcessCaptureAsync(result);
        result.Dispose();
    }
}
```

### Menu/Button Integration

```csharp
private async void CaptureButton_Click(object sender, RoutedEventArgs e)
{
    var result = await _captureManager.CaptureRegionAsync();
    // ... handle result
}
```

### Saving to File

```csharp
private async Task SaveBitmapAsync(SoftwareBitmap bitmap, string filePath)
{
    var file = await StorageFile.GetFileFromPathAsync(filePath);

    using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
    {
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();
    }
}
```

---

## Troubleshooting

### Issue: Captured region is offset

**Cause**: Coordinate transformation error, likely DPI mismatch.

**Fix**:
1. Enable diagnostics: `options.EnableDiagnostics = true`
2. Check `result.Diagnostics.TransformLog` for coordinate conversions
3. Verify monitor DPI reported vs actual (Settings → Display → Scale)
4. Run automated tests: `dotnet test XerahS.RegionCapture.Tests`

### Issue: Window highlight doesn't match window bounds

**Cause**: DWM extended frame bounds not available, falling back to `GetWindowRect`.

**Fix**: Some windows (especially legacy apps) may not support DWM extended frames. This is a known limitation. Manual selection will still work correctly.

### Issue: Overlay doesn't cover all monitors

**Cause**: Virtual desktop bounds calculation error or window positioning failure.

**Fix**:
1. Check `VirtualDesktop.GenerateDiagnosticReport()` output
2. Verify monitors are detected correctly
3. Ensure app manifest has per-monitor DPI awareness enabled

### Issue: Capture is slow

**Cause**: Large capture region or high-resolution monitors.

**Optimization**:
- Disable magnifier: `options.ShowMagnifier = false`
- Reduce debounce: `options.WindowDetectionDebounceMs = 30`
- Future: Switch to Windows.Graphics.Capture API for GPU acceleration

---

## Roadmap

- [ ] **Windows.Graphics.Capture API** support (optional, GPU-accelerated)
- [ ] **Magnifier** implementation (zoomed view near cursor)
- [ ] **Annotation tools** (draw on overlay before capture)
- [ ] **Screen recording** mode (capture to video)
- [ ] **OCR integration** (extract text from selection)
- [ ] **Upload integration** (Imgur, custom endpoints)

---

## Contributing

This is a reference implementation designed for the ShareX/XerahS ecosystem. Contributions welcome:

1. Fork the repository
2. Create a feature branch
3. Add tests for new functionality
4. Ensure all tests pass: `dotnet test`
5. Submit a pull request

---

## License

MIT License - See LICENSE file for details.

---

## Credits

Built by the ShareX Team for XerahS.WinUI.

Inspired by best-in-class screenshot tools and the need for pixel-perfect capture in modern mixed-DPI Windows environments.

**Special Thanks**: Windows App SDK team for WinUI 3 and per-monitor DPI APIs.
