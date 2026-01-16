# XerahS Region Capture - Architecture Documentation

## Design Philosophy

The region capture system is built around three core principles:

1. **Coordinate Correctness**: All coordinate transformations are explicit, logged, and testable
2. **DPI Awareness**: Per-monitor DPI scaling is handled at every transform boundary
3. **Deterministic Behavior**: State machine ensures predictable interaction flow

---

## Coordinate Space Model

### The Problem

Multi-monitor, mixed-DPI environments introduce multiple coordinate spaces:

- **Virtual Desktop Physical Pixels**: Screen space with negative coordinates possible
- **Monitor-Local Physical Pixels**: 0,0 at top-left of each monitor
- **DIPs (Device-Independent Pixels)**: WinUI's logical coordinate system
- **Per-Monitor DIPs**: DIPs scaled per monitor's DPI setting

Mismatches between these spaces cause the most common capture bugs:
- Off-by-one pixel errors at monitor boundaries
- Scaling errors (capturing 1920x1080 results in 1280x720 bitmap)
- Offset errors (selection at X=100 captures pixels at X=150)

### The Solution: Canonical Space

**Canonical Coordinate Space**: Physical pixels in virtual desktop coordinates.

All internal calculations, capture operations, and result coordinates use this space exclusively.

```
Virtual Desktop Example (3 monitors):

Monitor 1: 1920x1080 @ 100% DPI  |  Monitor 2: 2560x1440 @ 150% DPI  |  Monitor 3: 1920x1080 @ 100% DPI
Position: (-1920, 0)              |  Position: (0, 0) [PRIMARY]        |  Position: (2560, 0)

Virtual Desktop Bounds: (-1920, 0) → (4480, 1440)
                        ↑                      ↑
                   Negative X            Positive extent

Canonical coordinate (500, 500) = 500 physical pixels from virtual origin
On Monitor 2 @ 150% DPI, this is ~333 DIPs
```

### Transform Pipeline

#### 1. Pointer Input → Physical Pixels

**Input**: WinUI `PointerRoutedEventArgs.Position` (in DIPs relative to overlay window)

**Steps**:
1. Get overlay window rect in physical pixels: `GetWindowRect(hwnd)`
2. Map DIP position to physical: `physX = overlayRect.Left + dipX`
3. Account for overlay positioning at virtual desktop origin

**Implementation**: [`CoordinateMapper.OverlayDipToPhysical()`](XerahS.RegionCapture/Core/Coordinates/CoordinateMapper.cs#L34)

**Logged**: Yes (all intermediate values)

#### 2. Physical Pixels → Overlay DIPs (for rendering)

**Purpose**: Convert selection rectangle back to DIPs for rendering on overlay canvas

**Steps**:
1. Get overlay window rect in physical pixels
2. Map physical to DIP: `dipX = physX - overlayRect.Left`

**Implementation**: [`CoordinateMapper.PhysicalToOverlayDip()`](XerahS.RegionCapture/Core/Coordinates/CoordinateMapper.cs#L75)

#### 3. Physical Rectangle → Capture Coordinates

**Purpose**: Prepare rectangle for `BitBlt` or Windows.Graphics.Capture API

**Implementation**: Direct pass-through (both use physical pixels)

**Validation**: Test harness verifies captured bitmap dimensions match physical rectangle

---

## Component Architecture

### Layer 1: Public API

**Entry Point**: `RegionCaptureManager`

- Thread-safe singleton or instance-based usage
- Marshals capture request to UI thread via `DispatcherQueue`
- Returns `RegionCaptureResult` with bitmap + metadata

**Contract**:
```csharp
Task<RegionCaptureResult?> CaptureRegionAsync(RegionCaptureOptions? options = null)
```

**Lifetime**:
- Create once, reuse for multiple captures
- Ensures only one capture active at a time
- Disposes overlay automatically on completion/cancellation

### Layer 2: Core Subsystems

#### VirtualDesktop

**Responsibility**: Monitor enumeration and layout modeling

**Key Methods**:
- `Enumerate()`: Enumerates all monitors via `EnumDisplayMonitors`
- `GetMonitorAtPhysicalPoint()`: Hit-test for monitor containment
- `ClampToVirtualDesktop()`: Constrain rectangle to valid bounds

**DPI Handling**:
- Queries `GetDpiForMonitor()` for each monitor
- Stores effective DPI (96, 120, 144, 192, etc.)
- Calculates scale factor: `dpi / 96.0`

#### CoordinateMapper

**Responsibility**: Transform between coordinate spaces + diagnostic logging

**Transform Methods**:
- `OverlayDipToPhysical()`: Pointer events → canonical space
- `PhysicalToOverlayDip()`: Selection → rendering
- `ApplyEdgeSnapping()`: Snap to monitor edges

**Logging**:
- All transforms logged with timestamps
- Input/output coordinates logged
- Monitor at position logged
- Accessible via `GetTransformLog()`

#### ScreenCaptureEngine

**Responsibility**: Pixel-perfect screen readback

**Implementation**: GDI+ BitBlt

**Flow**:
1. Get screen DC: `GetDC(NULL)`
2. Create compatible DC and bitmap
3. `BitBlt(hdcMem, 0, 0, width, height, hdcScreen, rect.Left, rect.Top, SRCCOPY)`
4. Convert to `SoftwareBitmap` (BGRA8 format)

**Why BitBlt**:
- Direct physical pixel access (no DPI scaling applied)
- Synchronous (no async coordination needed)
- Works with negative coordinates (virtual desktop aware)

**Alternative Considered**: Windows.Graphics.Capture
- Pros: GPU-accelerated, modern API, composition-aware
- Cons: Async complexity, requires window handle or display item
- Future: Add as optional backend

### Layer 3: Input & State Management

#### SelectionStateMachine

**States**:
```
Idle → HoverWindowCandidate → DragSelecting → Selected → Confirmed
                            ↘                 ↗
                              (drag override)

Any state → Cancelled (Esc or right-click)
```

**Events**:
- `StateChanged`: State transitions
- `SelectionChanged`: Rectangle updated
- `WindowHoverChanged`: Hovered window changed

**Key Logic**:
- **Drag Threshold**: Prevents accidental drag when clicking window
- **Window Click vs Drag**: If `distance < threshold` on release → window select; else → manual
- **Debouncing**: Window detection has configurable debounce to prevent flicker

#### WindowDetector

**Responsibility**: Identify capture-worthy windows under cursor

**Filter Logic**:
1. `WindowFromPoint()` → Get top window at cursor
2. `GetAncestor(GA_ROOTOWNER)` → Get root window (not child control)
3. Eligibility checks:
   - Visible (`IsWindowVisible()`)
   - Not cloaked (`DWMWA_CLOAKED` == false)
   - Not child window (`WS_CHILD` not set)
   - Not tool window (unless `WS_EX_APPWINDOW`)
   - Not tiny (<50x50 px, likely tooltip)
   - Not system UI (taskbar, notification area, etc.) unless `IncludeSystemWindows`

**Bounds Extraction**:
- Try `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` first (accurate including shadow)
- Fallback to `GetWindowRect()` if DWM not available

### Layer 4: UI

#### RegionCaptureOverlay

**Window Configuration**:
- Borderless (`SetBorderAndTitleBar(false, false)`)
- Topmost (`IsAlwaysOnTop = true`)
- Positioned at virtual desktop origin
- Sized to cover entire virtual desktop

**Rendering**:
- Dimmed overlay (`Rectangle` with semi-transparent black)
- Selection rectangle (dashed stroke, customizable color)
- Window highlight (dashed stroke, different color)
- Crosshair lines (horizontal + vertical through cursor)
- Coordinate HUD (live X/Y/W/H + monitor info)

**Input Handling**:
- Pointer events route through state machine
- Keyboard events handle nudge, confirm, cancel
- Timer-based window detection (non-blocking)

**DPI Awareness**:
- Overlay window reports physical pixel size
- Pointer events are in DIPs relative to overlay
- Transformations applied via `CoordinateMapper`

---

## State Machine Deep Dive

### State: Idle

**Entry**: Initial state or after clearing previous selection

**Visual**: Crosshair only, dim overlay

**Transitions**:
- Window under cursor detected → `HoverWindowCandidate`
- Left mouse down (no window) → `DragSelecting`
- Esc → `Cancelled`

### State: HoverWindowCandidate

**Entry**: Window detected under cursor

**Visual**: Window highlight border rendered, crosshair

**Transitions**:
- Cursor leaves window → `Idle`
- Left mouse down → Prepare for click-select (stay in this state)
- Drag beyond threshold → `DragSelecting`
- Release without drag → `Selected` (with window bounds)
- Esc → `Cancelled`

**Key Logic**:
```csharp
OnPointerPressed:
    _dragStartPoint = currentPosition
    // Wait to see if user drags

OnPointerMoved:
    distance = sqrt(dx² + dy²)
    if distance > threshold:
        → DragSelecting (manual override)

OnPointerReleased:
    if !_isDragging:
        SelectionRect = HoveredWindow.Bounds
        → Selected
```

### State: DragSelecting

**Entry**: User dragging to create manual selection

**Visual**: Growing rectangle from drag start to cursor, dim overlay, crosshair

**Transitions**:
- Release → `Selected` (if rectangle valid)
- Release → `Idle` (if rectangle too small)
- Esc → `Cancelled`

**Rectangle Update**: On every `PointerMoved`:
```csharp
left = min(startX, currentX)
top = min(startY, currentY)
right = max(startX, currentX)
bottom = max(startY, currentY)
```

### State: Selected

**Entry**: Rectangle finalized (from window click or manual drag)

**Visual**: Selection rectangle, resize handles, HUD with coordinates

**Affordances**:
- Arrow keys: Nudge by 1px (Shift: 10px)
- Enter: → `Confirmed`
- Esc: → `Cancelled`
- (Future: Grab handles to resize)

**Snapping**: If `EnableEdgeSnapping`, apply snap on each nudge/resize

### State: Confirmed

**Entry**: User pressed Enter

**Action**: Execute capture

**Flow**:
1. Hide overlay (avoid capturing self)
2. Delay 50ms (ensure overlay hidden)
3. `ScreenCaptureEngine.CaptureRegionAsync(selectionRect)`
4. Build `RegionCaptureResult`
5. Close overlay
6. Return result via `TaskCompletionSource`

### State: Cancelled

**Entry**: Esc pressed or right-click

**Action**: Close overlay, return `null` to caller

---

## DPI Handling Details

### Per-Monitor DPI Awareness Configuration

**Manifest** (`app.manifest`):
```xml
<dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
```

**Code** (fallback):
```csharp
Win32.SetProcessDpiAwareness(PROCESS_DPI_AWARENESS.PROCESS_PER_MONITOR_DPI_AWARE);
```

### Monitor DPI Enumeration

**API**: `GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, &dpiX, &dpiY)`

**Effective DPI**: The DPI value Windows uses for scaling (respects user override)

**Common Values**:
- 96 DPI = 100% scale
- 120 DPI = 125% scale
- 144 DPI = 150% scale
- 192 DPI = 200% scale
- Custom: 105, 115, 175, 225, etc.

### Overlay Window DPI Handling

**Challenge**: Overlay spans multiple monitors with different DPIs

**Solution**:
- Overlay positioned in physical pixels (`AppWindow.MoveAndResize()` uses physical)
- Pointer events from WinUI are in DIPs relative to window
- Transform uses monitor-at-position to get correct scale factor

**Edge Case**: Pointer moving across monitor boundary
- Continuous recalculation of monitor at cursor position
- Window highlight may "jump" slightly due to DPI change (acceptable UX trade-off)

### Rendering DPI Consistency

**Goal**: Visual elements (strokes, handles) appear same physical size across monitors

**Implementation**:
- `SelectionStrokeThickness` specified in DIPs (e.g., 2.0)
- WinUI automatically scales for rendering monitor
- Result: 2 DIP stroke is ~2px @ 100%, ~3px @ 150%, ~4px @ 200%

---

## Testing Strategy

### Automated Tests

**Framework**: MSTest

**Test Categories**:

1. **Single Monitor Tests**: Validate basic correctness
   - Random regions on primary monitor
   - Edge cases (corners, center)

2. **Multi-Monitor Tests**: Validate DPI handling
   - Random regions on all monitors
   - Mixed-DPI scenarios (100% + 150%)

3. **Coordinate Edge Cases**:
   - Negative coordinates (monitors left/above primary)
   - Monitor boundaries (selections crossing edges)

4. **Pixel Verification**:
   - Capture region → verify bitmap dimensions match
   - Sample pixels → verify non-zero (not completely black = failed capture)

**Determinism**:
- Seeded random number generator
- Reproducible test cases
- Failure reports saved to temp directory

### Manual Test Checklist

See [README.md - Manual Testing Checklist](README.md#manual-testing-checklist)

### Diagnostics Output

**Transform Log Example**:
```
[12:34:56.789] OverlayDipToPhysical: Input DIP (1234.50, 567.80)
[12:34:56.790]   Overlay window rect (physical): (-1920,0)-(3840,1080)
[12:34:56.791]   Monitor at position: \\.\DISPLAY1 @ 100% (96 DPI)
[12:34:56.791]   Final physical: (-686, 568)
```

**Usage**:
```csharp
var result = await captureManager.CaptureRegionAsync(new RegionCaptureOptions { EnableDiagnostics = true });
Console.WriteLine(result.Diagnostics.TransformLog);
```

---

## Performance Considerations

### Bottlenecks

1. **BitBlt**: Synchronous, CPU-based
   - Large captures (4K @ 200% = 7680x4320) can take 50-100ms
   - Mitigation: Run on background thread (already implemented)

2. **Window Detection**: Polling at 50ms interval
   - Negligible CPU usage (~1-2% on modern systems)
   - Could use Win32 mouse hook for real-time, but security/stability concerns

3. **Rendering**: WinUI composition on UI thread
   - Crosshair/HUD updates on every pointer move
   - Mitigation: Consider throttling to 60 FPS if needed

### Optimizations Applied

- ✅ Capture runs on background thread (doesn't block UI)
- ✅ Window detection is debounced (prevents flicker + reduces work)
- ✅ Diagnostic logging is optional (disable in production)
- ✅ Bitmap conversion is zero-copy where possible
- ✅ No redundant full-screen captures (only capture selected region)

### Future Optimizations

- [ ] Windows.Graphics.Capture API (GPU-accelerated)
- [ ] Render throttling (cap pointer move updates to 60 FPS)
- [ ] Magnifier texture caching (avoid re-capture on every move)

---

## Extension Points

### Custom Capture Backends

**Interface** (future):
```csharp
public interface IScreenCaptureEngine
{
    Task<SoftwareBitmap> CaptureRegionAsync(Win32.RECT physicalRect);
}
```

**Implementations**:
- `GdiBitBltCaptureEngine` (current)
- `WindowsGraphicsCaptureCaptureEngine` (future)
- `DesktopDuplicationCaptureEngine` (future)

### Custom Renderers

**Current**: XAML-based overlay

**Future**: Direct2D/Composition API for lower latency

**Benefits**:
- Faster rendering updates
- More effects (blur, animations)
- Lower memory footprint

### Custom Window Filters

**Current**: Hardcoded `IsEligibleWindow()` logic

**Future**: Plugin-based filter system
```csharp
public interface IWindowFilter
{
    bool IsEligible(IntPtr hwnd);
}
```

---

## Known Issues & Mitigations

### Issue 1: Overlay flicker on some systems

**Symptom**: Brief flash when overlay appears

**Cause**: Window composition timing

**Mitigation**: Ensure overlay is fully rendered before showing
- Future: Use `CompositionTarget.Rendering` event for precise timing

### Issue 2: Window highlight slight offset on legacy apps

**Symptom**: Highlight border doesn't perfectly match window edge

**Cause**: Legacy apps without DWM extended frame, `GetWindowRect` less accurate

**Impact**: Cosmetic only, capture is still correct

**Mitigation**: Document as known limitation for legacy apps

### Issue 3: Negative coordinate confusion

**Symptom**: User reports "selection moved" when monitor is left of primary

**Cause**: Users expect positive coordinates, negative is unintuitive

**Mitigation**:
- Clear documentation
- HUD shows actual coordinates for transparency
- Diagnostics log explains layout

---

## Maintenance Guidelines

### Adding New Features

1. **Coordinate Transforms**: Add to `CoordinateMapper`, log all steps
2. **State Transitions**: Update `SelectionStateMachine`, document in state diagram
3. **UI Elements**: Add to XAML, update rendering in code-behind
4. **Tests**: Add automated test cases in `PixelCorrectnessTests`

### Debugging Coordinate Issues

1. Enable diagnostics: `options.EnableDiagnostics = true`
2. Capture test region with known coordinates
3. Check `result.Diagnostics.TransformLog`
4. Compare expected vs actual at each transform step
5. Verify monitor DPI values match system settings

### Updating for New Windows Versions

- Monitor Win32 API deprecation notices
- Test on Windows Insider builds
- Validate DPI handling with new scaling options
- Check for new `DWMWINDOWATTRIBUTE` values

---

## References

### Windows APIs

- [High DPI Desktop Application Development on Windows](https://docs.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows)
- [EnumDisplayMonitors](https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaymonitors)
- [GetDpiForMonitor](https://docs.microsoft.com/en-us/windows/win32/api/shellscalingapi/nf-shellscalingapi-getdpiformonitor)
- [DwmGetWindowAttribute](https://docs.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmgetwindowattribute)

### WinUI 3

- [Windows App SDK](https://docs.microsoft.com/en-us/windows/apps/windows-app-sdk/)
- [WinUI 3 Overview](https://docs.microsoft.com/en-us/windows/apps/winui/winui3/)
- [Per-Monitor DPI Awareness in WinUI](https://docs.microsoft.com/en-us/windows/apps/desktop/modernize/apply-windows-themes#per-monitor-dpi-awareness)

### Best Practices

- [Writing DPI-Aware Desktop and Win32 Applications](https://docs.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows#writing-dpi-aware-desktop-and-win32-applications)
- [Virtual Screen Coordinates](https://docs.microsoft.com/en-us/windows/win32/gdi/the-virtual-screen)

---

**Document Version**: 1.0
**Last Updated**: 2026-01-16
**Author**: ShareX Team / XerahS.WinUI Contributors
