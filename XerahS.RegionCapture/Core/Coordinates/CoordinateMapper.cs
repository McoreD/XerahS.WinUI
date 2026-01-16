using Microsoft.UI.Xaml;
using System;
using System.Text;
using Windows.Graphics;
using XerahS.RegionCapture.NativeMethods;

namespace XerahS.RegionCapture.Core.Coordinates;

/// <summary>
/// Handles coordinate transformations between different coordinate spaces.
/// Canonical space: Physical pixels in virtual desktop coordinates.
/// </summary>
internal sealed class CoordinateMapper
{
    private readonly VirtualDesktop _virtualDesktop;
    private readonly StringBuilder _transformLog;
    private readonly bool _enableLogging;

    public VirtualDesktop VirtualDesktop => _virtualDesktop;

    public CoordinateMapper(VirtualDesktop virtualDesktop, bool enableLogging = true)
    {
        _virtualDesktop = virtualDesktop ?? throw new ArgumentNullException(nameof(virtualDesktop));
        _enableLogging = enableLogging;
        _transformLog = enableLogging ? new StringBuilder() : new StringBuilder(0);

        if (_enableLogging)
        {
            _transformLog.AppendLine("=== Coordinate Transformation Log ===");
            _transformLog.AppendLine(_virtualDesktop.GenerateDiagnosticReport());
            _transformLog.AppendLine();
        }
    }

    /// <summary>
    /// Converts WinUI pointer position (in DIPs relative to overlay window) to physical pixels in virtual desktop coordinates.
    /// This is the critical transform for accurate capture.
    /// </summary>
    /// <param name="overlayWindow">The overlay window receiving pointer events.</param>
    /// <param name="dipX">Pointer X in DIPs (relative to overlay window).</param>
    /// <param name="dipY">Pointer Y in DIPs (relative to overlay window).</param>
    /// <returns>Coordinates in physical pixels (virtual desktop space).</returns>
    public (int physX, int physY) OverlayDipToPhysical(Window overlayWindow, double dipX, double dipY)
    {
        if (overlayWindow == null)
            throw new ArgumentNullException(nameof(overlayWindow));

        Log($"OverlayDipToPhysical: Input DIP ({dipX:F2}, {dipY:F2})");

        // Get the overlay window's position in screen coordinates (DIPs)
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(overlayWindow);

        // The overlay is positioned at virtual desktop origin in physical pixels
        // We need to account for the DPI scale of the monitor under the pointer

        // First, estimate which monitor based on DIP coordinates
        // Since overlay spans entire virtual desktop, we need to map DIP position to physical

        // Get overlay window bounds (should match virtual desktop in physical pixels)
        Win32.GetWindowRect(hwnd, out var overlayRect);

        Log($"  Overlay window rect (physical): {overlayRect}");
        Log($"  Virtual desktop bounds: {_virtualDesktop.VirtualBounds}");

        // Calculate physical position by mapping DIP position proportionally
        // across the overlay window's physical extent
        double dipWidth = overlayRect.Width;
        double dipHeight = overlayRect.Height;

        // But wait - the overlay window reports its size in physical pixels already
        // So we need to find which monitor the DIP point maps to

        // Strategy: Estimate physical position, then get monitor at that position for accurate DPI
        int estimatedPhysX = overlayRect.Left + (int)Math.Round(dipX);
        int estimatedPhysY = overlayRect.Top + (int)Math.Round(dipY);

        Log($"  Estimated physical (direct): ({estimatedPhysX}, {estimatedPhysY})");

        // Get monitor at this estimated position
        var monitor = _virtualDesktop.GetMonitorAtPhysicalPointOrNearest(estimatedPhysX, estimatedPhysY);

        Log($"  Monitor at position: {monitor}");

        // Now, convert the DIP coordinates relative to the monitor's bounds
        // The overlay window's client area in DIPs needs to be known

        // Actually, for a borderless window spanning the entire virtual desktop,
        // the window's client coordinates in DIPs directly correspond to physical pixels
        // when we account for per-monitor DPI scaling.

        // Simplified approach: Use direct mapping since overlay is borderless and positioned at virtual origin
        // The pointer position in DIPs from the overlay window IS the position in physical pixels
        // when the overlay is configured correctly (which we'll ensure in the overlay implementation)

        int physX = overlayRect.Left + (int)Math.Round(dipX);
        int physY = overlayRect.Top + (int)Math.Round(dipY);

        Log($"  Final physical: ({physX}, {physY})");

        return (physX, physY);
    }

    /// <summary>
    /// Converts physical pixel coordinates to DIPs for rendering on the overlay.
    /// </summary>
    public (double dipX, double dipY) PhysicalToOverlayDip(Window overlayWindow, int physX, int physY)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(overlayWindow);
        Win32.GetWindowRect(hwnd, out var overlayRect);

        double dipX = physX - overlayRect.Left;
        double dipY = physY - overlayRect.Top;

        Log($"PhysicalToOverlayDip: ({physX}, {physY}) -> DIP ({dipX:F2}, {dipY:F2})");

        return (dipX, dipY);
    }

    /// <summary>
    /// Converts a physical rectangle to a PointInt32 and SizeInt32 for capture APIs.
    /// </summary>
    public (PointInt32 position, SizeInt32 size) PhysicalRectToCapture(Win32.RECT physRect)
    {
        Log($"PhysicalRectToCapture: {physRect}");

        var position = new PointInt32(physRect.Left, physRect.Top);
        var size = new SizeInt32(physRect.Width, physRect.Height);

        Log($"  -> Position: ({position.X}, {position.Y}), Size: ({size.Width}, {size.Height})");

        return (position, size);
    }

    /// <summary>
    /// Applies snapping to monitor edges if the rectangle is within threshold.
    /// </summary>
    public Win32.RECT ApplyEdgeSnapping(Win32.RECT rect, int threshold)
    {
        if (threshold <= 0)
            return rect;

        int left = rect.Left;
        int top = rect.Top;
        int right = rect.Right;
        int bottom = rect.Bottom;

        bool snapped = false;

        // Find monitors that overlap with the rectangle
        foreach (var monitor in _virtualDesktop.Monitors)
        {
            // Snap left edge
            if (Math.Abs(left - monitor.Bounds.Left) <= threshold)
            {
                left = monitor.Bounds.Left;
                snapped = true;
            }

            // Snap top edge
            if (Math.Abs(top - monitor.Bounds.Top) <= threshold)
            {
                top = monitor.Bounds.Top;
                snapped = true;
            }

            // Snap right edge
            if (Math.Abs(right - monitor.Bounds.Right) <= threshold)
            {
                right = monitor.Bounds.Right;
                snapped = true;
            }

            // Snap bottom edge
            if (Math.Abs(bottom - monitor.Bounds.Bottom) <= threshold)
            {
                bottom = monitor.Bounds.Bottom;
                snapped = true;
            }
        }

        if (snapped)
        {
            var snappedRect = new Win32.RECT(left, top, right, bottom);
            Log($"ApplyEdgeSnapping: {rect} -> {snappedRect} (threshold: {threshold}px)");
            return snappedRect;
        }

        return rect;
    }

    /// <summary>
    /// Gets the complete transformation log for diagnostics.
    /// </summary>
    public string GetTransformLog()
    {
        return _transformLog.ToString();
    }

    private void Log(string message)
    {
        if (_enableLogging)
        {
            _transformLog.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
        }
    }
}
