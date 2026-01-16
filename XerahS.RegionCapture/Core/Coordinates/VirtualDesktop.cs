using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using XerahS.RegionCapture.NativeMethods;

namespace XerahS.RegionCapture.Core.Coordinates;

/// <summary>
/// Represents the virtual desktop spanning all monitors with coordinate mapping capabilities.
/// Canonical coordinate space: Physical pixels in virtual desktop coordinates.
/// </summary>
internal sealed class VirtualDesktop
{
    private readonly MonitorInfo[] _monitors;
    private readonly Win32.RECT _virtualBounds;

    public IReadOnlyList<MonitorInfo> Monitors => _monitors;
    public Win32.RECT VirtualBounds => _virtualBounds;

    private VirtualDesktop(MonitorInfo[] monitors)
    {
        _monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));

        if (_monitors.Length == 0)
        {
            throw new InvalidOperationException("No monitors detected.");
        }

        // Calculate virtual desktop bounding rectangle
        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = int.MinValue;
        int maxY = int.MinValue;

        foreach (var monitor in _monitors)
        {
            minX = Math.Min(minX, monitor.Bounds.Left);
            minY = Math.Min(minY, monitor.Bounds.Top);
            maxX = Math.Max(maxX, monitor.Bounds.Right);
            maxY = Math.Max(maxY, monitor.Bounds.Bottom);
        }

        _virtualBounds = new Win32.RECT(minX, minY, maxX, maxY);
    }

    /// <summary>
    /// Enumerates all monitors and creates a VirtualDesktop instance.
    /// </summary>
    public static VirtualDesktop Enumerate()
    {
        var monitors = new List<MonitorInfo>();

        Win32.MonitorEnumProc callback = (IntPtr hMonitor, IntPtr hdcMonitor, ref Win32.RECT lprcMonitor, IntPtr dwData) =>
        {
            var monitorInfo = Win32.MONITORINFOEX.Create();
            if (Win32.GetMonitorInfo(hMonitor, ref monitorInfo))
            {
                // Get DPI for this monitor
                uint dpiX = 96;
                uint dpiY = 96;
                Win32.GetDpiForMonitor(hMonitor, Win32.MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out dpiX, out dpiY);

                bool isPrimary = (monitorInfo.dwFlags & 0x00000001) != 0; // MONITORINFOF_PRIMARY

                var monitor = new MonitorInfo(
                    hMonitor,
                    monitorInfo.szDevice,
                    monitorInfo.rcMonitor,
                    monitorInfo.rcWork,
                    dpiX,
                    dpiY,
                    isPrimary
                );

                monitors.Add(monitor);
            }

            return true; // Continue enumeration
        };

        if (!Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
        {
            throw new InvalidOperationException("Failed to enumerate monitors.");
        }

        return new VirtualDesktop(monitors.ToArray());
    }

    /// <summary>
    /// Finds the monitor containing the specified physical pixel point.
    /// Returns null if the point is outside all monitors.
    /// </summary>
    public MonitorInfo? GetMonitorAtPhysicalPoint(int x, int y)
    {
        return _monitors.FirstOrDefault(m => m.ContainsPhysicalPoint(x, y));
    }

    /// <summary>
    /// Finds the monitor containing the specified physical pixel point.
    /// If not found, returns the nearest monitor.
    /// </summary>
    public MonitorInfo GetMonitorAtPhysicalPointOrNearest(int x, int y)
    {
        var monitor = GetMonitorAtPhysicalPoint(x, y);
        if (monitor != null)
        {
            return monitor;
        }

        // Use Win32 API to get nearest monitor
        var pt = new Win32.POINT(x, y);
        IntPtr hMonitor = Win32.MonitorFromPoint(pt, Win32.MONITOR_DEFAULTTONEAREST);

        return _monitors.FirstOrDefault(m => m.Handle == hMonitor) ?? _monitors[0];
    }

    /// <summary>
    /// Gets the primary monitor.
    /// </summary>
    public MonitorInfo GetPrimaryMonitor()
    {
        return _monitors.First(m => m.IsPrimary);
    }

    /// <summary>
    /// Normalizes a physical rectangle to ensure positive width/height and valid coordinates.
    /// Handles dragging from bottom-right to top-left.
    /// </summary>
    public Win32.RECT NormalizeRectangle(int x1, int y1, int x2, int y2)
    {
        int left = Math.Min(x1, x2);
        int top = Math.Min(y1, y2);
        int right = Math.Max(x1, x2);
        int bottom = Math.Max(y1, y2);

        return new Win32.RECT(left, top, right, bottom);
    }

    /// <summary>
    /// Clamps a physical rectangle to the virtual desktop bounds.
    /// </summary>
    public Win32.RECT ClampToVirtualDesktop(Win32.RECT rect)
    {
        int left = Math.Max(rect.Left, _virtualBounds.Left);
        int top = Math.Max(rect.Top, _virtualBounds.Top);
        int right = Math.Min(rect.Right, _virtualBounds.Right);
        int bottom = Math.Min(rect.Bottom, _virtualBounds.Bottom);

        // Ensure positive dimensions
        if (right <= left) right = left + 1;
        if (bottom <= top) bottom = top + 1;

        return new Win32.RECT(left, top, right, bottom);
    }

    /// <summary>
    /// Generates a diagnostic report of the monitor configuration.
    /// </summary>
    public string GenerateDiagnosticReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Virtual Desktop Configuration ===");
        sb.AppendLine($"Virtual Bounds: {_virtualBounds}");
        sb.AppendLine($"Monitor Count: {_monitors.Length}");
        sb.AppendLine();

        for (int i = 0; i < _monitors.Length; i++)
        {
            var m = _monitors[i];
            sb.AppendLine($"Monitor {i + 1}: {m}");
            sb.AppendLine($"  Work Area: {m.WorkArea}");
        }

        return sb.ToString();
    }

    public CaptureRectangle ToPublicRectangle(Win32.RECT rect)
    {
        return new CaptureRectangle(rect.Left, rect.Top, rect.Width, rect.Height);
    }
}
