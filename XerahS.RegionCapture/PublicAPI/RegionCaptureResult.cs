using System;
using Windows.Graphics.Imaging;

namespace XerahS.RegionCapture;

/// <summary>
/// Result of a region capture operation containing the captured image and metadata.
/// </summary>
public sealed class RegionCaptureResult : IDisposable
{
    /// <summary>
    /// The captured bitmap in BGRA8 format.
    /// </summary>
    public SoftwareBitmap Bitmap { get; }

    /// <summary>
    /// Selected region in virtual desktop physical pixels.
    /// Coordinates are in the global virtual desktop space and may include negative values.
    /// </summary>
    public CaptureRectangle Region { get; }

    /// <summary>
    /// Information about the monitor where the capture was primarily performed.
    /// If the selection spans multiple monitors, this is the monitor containing the top-left corner.
    /// </summary>
    public MonitorMetadata PrimaryMonitor { get; }

    /// <summary>
    /// Diagnostic information about the capture session.
    /// Useful for debugging coordinate mapping issues.
    /// </summary>
    public CaptureDiagnostics Diagnostics { get; }

    /// <summary>
    /// Whether the capture was from window auto-selection (true) or manual rectangle selection (false).
    /// </summary>
    public bool IsWindowCapture { get; }

    /// <summary>
    /// If IsWindowCapture is true, contains the window handle (HWND) that was captured.
    /// </summary>
    public IntPtr WindowHandle { get; }

    internal RegionCaptureResult(
        SoftwareBitmap bitmap,
        CaptureRectangle region,
        MonitorMetadata primaryMonitor,
        CaptureDiagnostics diagnostics,
        bool isWindowCapture = false,
        IntPtr windowHandle = default)
    {
        Bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
        Region = region;
        PrimaryMonitor = primaryMonitor ?? throw new ArgumentNullException(nameof(primaryMonitor));
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        IsWindowCapture = isWindowCapture;
        WindowHandle = windowHandle;
    }

    public void Dispose()
    {
        Bitmap?.Dispose();
    }
}

/// <summary>
/// Rectangle in physical pixels within the virtual desktop coordinate space.
/// </summary>
public readonly struct CaptureRectangle
{
    /// <summary>X coordinate in physical pixels (can be negative).</summary>
    public int X { get; }

    /// <summary>Y coordinate in physical pixels (can be negative).</summary>
    public int Y { get; }

    /// <summary>Width in physical pixels.</summary>
    public int Width { get; }

    /// <summary>Height in physical pixels.</summary>
    public int Height { get; }

    public CaptureRectangle(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public override string ToString() => $"({X}, {Y}) {Width}x{Height}";
}

/// <summary>
/// Metadata about a monitor involved in the capture.
/// </summary>
public sealed class MonitorMetadata
{
    /// <summary>Monitor handle (HMONITOR).</summary>
    public IntPtr Handle { get; }

    /// <summary>Monitor device name.</summary>
    public string DeviceName { get; }

    /// <summary>Monitor bounds in physical pixels.</summary>
    public CaptureRectangle Bounds { get; }

    /// <summary>DPI scale factor (1.0 = 96 DPI, 1.5 = 144 DPI, 2.0 = 192 DPI, etc.).</summary>
    public double ScaleFactor { get; }

    /// <summary>Effective DPI value.</summary>
    public uint DpiX { get; }

    /// <summary>Effective DPI value.</summary>
    public uint DpiY { get; }

    internal MonitorMetadata(IntPtr handle, string deviceName, CaptureRectangle bounds, double scaleFactor, uint dpiX, uint dpiY)
    {
        Handle = handle;
        DeviceName = deviceName ?? string.Empty;
        Bounds = bounds;
        ScaleFactor = scaleFactor;
        DpiX = dpiX;
        DpiY = dpiY;
    }

    public override string ToString() => $"{DeviceName} @ {ScaleFactor:P0} ({DpiX} DPI) {Bounds}";
}

/// <summary>
/// Diagnostic information captured during the capture session.
/// </summary>
public sealed class CaptureDiagnostics
{
    /// <summary>All monitors detected during the session.</summary>
    public MonitorMetadata[] AllMonitors { get; }

    /// <summary>Virtual desktop bounds in physical pixels.</summary>
    public CaptureRectangle VirtualDesktopBounds { get; }

    /// <summary>Timestamp when capture was initiated.</summary>
    public DateTimeOffset CaptureStartTime { get; }

    /// <summary>Timestamp when capture completed.</summary>
    public DateTimeOffset CaptureEndTime { get; }

    /// <summary>Full coordinate transformation log for debugging.</summary>
    public string TransformLog { get; }

    internal CaptureDiagnostics(
        MonitorMetadata[] allMonitors,
        CaptureRectangle virtualDesktopBounds,
        DateTimeOffset captureStartTime,
        DateTimeOffset captureEndTime,
        string transformLog)
    {
        AllMonitors = allMonitors ?? Array.Empty<MonitorMetadata>();
        VirtualDesktopBounds = virtualDesktopBounds;
        CaptureStartTime = captureStartTime;
        CaptureEndTime = captureEndTime;
        TransformLog = transformLog ?? string.Empty;
    }
}
