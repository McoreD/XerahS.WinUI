using Microsoft.UI;
using Windows.UI;

namespace XerahS.RegionCapture;

/// <summary>
/// Configuration options for region capture behavior and appearance.
/// </summary>
public sealed class RegionCaptureOptions
{
    /// <summary>
    /// Enable window auto-detection and highlight on hover.
    /// Default: true
    /// </summary>
    public bool EnableWindowDetection { get; set; } = true;

    /// <summary>
    /// Enable single-click window selection when hovering over a window.
    /// Requires EnableWindowDetection to be true.
    /// Default: true
    /// </summary>
    public bool EnableQuickWindowCapture { get; set; } = true;

    /// <summary>
    /// Show crosshair lines at cursor position.
    /// Default: true
    /// </summary>
    public bool ShowCrosshair { get; set; } = true;

    /// <summary>
    /// Show magnifier near cursor for precise selection.
    /// Default: false (performance consideration)
    /// </summary>
    public bool ShowMagnifier { get; set; } = false;

    /// <summary>
    /// Magnification level for the magnifier (2.0 = 2x zoom).
    /// Default: 3.0
    /// </summary>
    public double MagnifierZoom { get; set; } = 3.0;

    /// <summary>
    /// Show HUD with X/Y/W/H coordinates.
    /// Default: true
    /// </summary>
    public bool ShowCoordinateHUD { get; set; } = true;

    /// <summary>
    /// Opacity of the dimmed overlay (0.0 = transparent, 1.0 = opaque).
    /// Default: 0.5
    /// </summary>
    public double DimOpacity { get; set; } = 0.5;

    /// <summary>
    /// Color of the selection rectangle outline.
    /// Default: White
    /// </summary>
    public Color SelectionColor { get; set; } = Colors.White;

    /// <summary>
    /// Thickness of the selection rectangle outline in DIPs.
    /// Automatically scaled per monitor DPI.
    /// Default: 2.0
    /// </summary>
    public double SelectionStrokeThickness { get; set; } = 2.0;

    /// <summary>
    /// Enable snapping to monitor edges.
    /// Default: true
    /// </summary>
    public bool EnableEdgeSnapping { get; set; } = true;

    /// <summary>
    /// Snap threshold in physical pixels.
    /// Default: 10
    /// </summary>
    public int SnapThreshold { get; set; } = 10;

    /// <summary>
    /// Minimum selection size in physical pixels.
    /// Default: 10x10
    /// </summary>
    public int MinimumSelectionSize { get; set; } = 10;

    /// <summary>
    /// Drag threshold in DIPs before starting manual selection (prevents accidental drags during window click).
    /// Default: 5.0
    /// </summary>
    public double DragThreshold { get; set; } = 5.0;

    /// <summary>
    /// Debounce time for window detection in milliseconds (prevents flicker when moving between windows).
    /// Default: 50ms
    /// </summary>
    public int WindowDetectionDebounceMs { get; set; } = 50;

    /// <summary>
    /// Enable comprehensive diagnostic logging to output and result object.
    /// Default: true
    /// </summary>
    public bool EnableDiagnostics { get; set; } = true;

    /// <summary>
    /// Arrow key nudge amount in physical pixels (normal).
    /// Default: 1
    /// </summary>
    public int NudgeAmountSmall { get; set; } = 1;

    /// <summary>
    /// Arrow key nudge amount in physical pixels (with Shift modifier).
    /// Default: 10
    /// </summary>
    public int NudgeAmountLarge { get; set; } = 10;

    /// <summary>
    /// Include the taskbar and system UI in window auto-detection.
    /// Default: false
    /// </summary>
    public bool IncludeSystemWindows { get; set; } = false;

    /// <summary>
    /// Color of the window highlight border.
    /// Default: Cyan
    /// </summary>
    public Color WindowHighlightColor { get; set; } = Colors.Cyan;

    /// <summary>
    /// Validates the options and throws if any are invalid.
    /// </summary>
    internal void Validate()
    {
        if (DimOpacity < 0.0 || DimOpacity > 1.0)
            throw new System.ArgumentOutOfRangeException(nameof(DimOpacity), "Must be between 0.0 and 1.0");

        if (SelectionStrokeThickness < 0.1)
            throw new System.ArgumentOutOfRangeException(nameof(SelectionStrokeThickness), "Must be at least 0.1");

        if (MagnifierZoom < 1.0)
            throw new System.ArgumentOutOfRangeException(nameof(MagnifierZoom), "Must be at least 1.0");

        if (MinimumSelectionSize < 1)
            throw new System.ArgumentOutOfRangeException(nameof(MinimumSelectionSize), "Must be at least 1");

        if (SnapThreshold < 0)
            throw new System.ArgumentOutOfRangeException(nameof(SnapThreshold), "Must be non-negative");
    }
}
