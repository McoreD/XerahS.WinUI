using System;
using XerahS.RegionCapture.NativeMethods;

namespace XerahS.RegionCapture.Core.Coordinates;

/// <summary>
/// Represents a physical monitor with its DPI-aware properties.
/// All coordinates are in physical pixels.
/// </summary>
internal sealed class MonitorInfo
{
    /// <summary>Monitor handle (HMONITOR).</summary>
    public IntPtr Handle { get; }

    /// <summary>Device name (e.g., "\\.\DISPLAY1").</summary>
    public string DeviceName { get; }

    /// <summary>Monitor bounds in physical pixels (virtual desktop coordinates).</summary>
    public Win32.RECT Bounds { get; }

    /// <summary>Working area (excluding taskbar) in physical pixels.</summary>
    public Win32.RECT WorkArea { get; }

    /// <summary>Effective DPI X.</summary>
    public uint DpiX { get; }

    /// <summary>Effective DPI Y.</summary>
    public uint DpiY { get; }

    /// <summary>Scale factor relative to 96 DPI (1.0 = 100%, 1.5 = 150%, 2.0 = 200%).</summary>
    public double ScaleFactor => DpiX / 96.0;

    /// <summary>True if this is the primary monitor.</summary>
    public bool IsPrimary { get; }

    public MonitorInfo(IntPtr handle, string deviceName, Win32.RECT bounds, Win32.RECT workArea, uint dpiX, uint dpiY, bool isPrimary)
    {
        Handle = handle;
        DeviceName = deviceName ?? string.Empty;
        Bounds = bounds;
        WorkArea = workArea;
        DpiX = dpiX;
        DpiY = dpiY;
        IsPrimary = isPrimary;
    }

    /// <summary>
    /// Converts a point from DIPs to physical pixels for this monitor.
    /// </summary>
    public (int x, int y) DipToPhysical(double dipX, double dipY)
    {
        return (
            (int)Math.Round(dipX * ScaleFactor),
            (int)Math.Round(dipY * ScaleFactor)
        );
    }

    /// <summary>
    /// Converts a point from physical pixels to DIPs for this monitor.
    /// </summary>
    public (double x, double y) PhysicalToDip(int physX, int physY)
    {
        return (
            physX / ScaleFactor,
            physY / ScaleFactor
        );
    }

    /// <summary>
    /// Checks if a physical pixel point is within this monitor's bounds.
    /// </summary>
    public bool ContainsPhysicalPoint(int x, int y)
    {
        return x >= Bounds.Left && x < Bounds.Right &&
               y >= Bounds.Top && y < Bounds.Bottom;
    }

    public MonitorMetadata ToPublicMetadata()
    {
        return new MonitorMetadata(
            Handle,
            DeviceName,
            new CaptureRectangle(Bounds.Left, Bounds.Top, Bounds.Width, Bounds.Height),
            ScaleFactor,
            DpiX,
            DpiY
        );
    }

    public override string ToString() =>
        $"{DeviceName} @ {ScaleFactor:P0} ({DpiX} DPI) {Bounds} {(IsPrimary ? "[PRIMARY]" : "")}";
}
