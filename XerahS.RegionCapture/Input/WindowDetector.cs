using System;
using System.Text;
using XerahS.RegionCapture.NativeMethods;

namespace XerahS.RegionCapture.Input;

/// <summary>
/// Detects windows under the cursor for auto-selection with window highlight.
/// Filters out non-relevant windows (tooltips, overlays, system UI, etc.).
/// </summary>
internal sealed class WindowDetector
{
    private readonly IntPtr _overlayHwnd;
    private readonly bool _includeSystemWindows;

    public WindowDetector(IntPtr overlayHwnd, bool includeSystemWindows)
    {
        _overlayHwnd = overlayHwnd;
        _includeSystemWindows = includeSystemWindows;
    }

    /// <summary>
    /// Detects the topmost eligible window at the specified physical pixel coordinates.
    /// Returns null if no eligible window is found.
    /// </summary>
    public DetectedWindow? DetectWindowAtPoint(int physX, int physY)
    {
        var point = new Win32.POINT(physX, physY);
        IntPtr hwnd = Win32.WindowFromPoint(point);

        if (hwnd == IntPtr.Zero)
            return null;

        // Get the root window (not a child control)
        IntPtr rootHwnd = Win32.GetAncestor(hwnd, Win32.GA_ROOTOWNER);
        if (rootHwnd != IntPtr.Zero)
            hwnd = rootHwnd;

        // Filter out our own overlay
        if (hwnd == _overlayHwnd)
            return null;

        // Check if window is eligible for capture
        if (!IsEligibleWindow(hwnd))
            return null;

        // Get window bounds - try DWM extended frame first for better accuracy
        Win32.RECT bounds;
        int hr = Win32.DwmGetWindowAttribute(
            hwnd,
            Win32.DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS,
            out bounds,
            System.Runtime.InteropServices.Marshal.SizeOf<Win32.RECT>());

        if (hr != 0)
        {
            // Fallback to GetWindowRect
            if (!Win32.GetWindowRect(hwnd, out bounds))
                return null;
        }

        // Get window text and class name for identification
        string title = GetWindowText(hwnd);
        string className = GetWindowClassName(hwnd);

        return new DetectedWindow(hwnd, bounds, title, className);
    }

    private bool IsEligibleWindow(IntPtr hwnd)
    {
        // Must be visible
        if (!Win32.IsWindowVisible(hwnd))
            return false;

        // Check for cloaked windows (e.g., windows on other virtual desktops)
        if (Win32.DwmGetWindowAttribute(
                hwnd,
                Win32.DWMWINDOWATTRIBUTE.DWMWA_CLOAKED,
                out bool isCloaked,
                sizeof(int)) == 0 && isCloaked)
        {
            return false;
        }

        int style = Win32.GetWindowLong(hwnd, Win32.GWL_STYLE);
        int exStyle = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);

        // Filter out child windows
        if ((style & Win32.WS_CHILD) != 0)
            return false;

        // Filter out tool windows unless they have WS_EX_APPWINDOW
        bool isToolWindow = (exStyle & Win32.WS_EX_TOOLWINDOW) != 0;
        bool isAppWindow = (exStyle & Win32.WS_EX_APPWINDOW) != 0;

        if (isToolWindow && !isAppWindow)
            return false;

        // Filter out windows with WS_EX_NOACTIVATE (e.g., tooltips, toasts)
        if ((exStyle & Win32.WS_EX_NOACTIVATE) != 0)
            return false;

        // Get window rect and filter tiny windows (likely tooltips or indicators)
        if (Win32.GetWindowRect(hwnd, out var rect))
        {
            if (rect.Width < 50 || rect.Height < 50)
                return false;
        }

        // Filter system UI unless explicitly included
        if (!_includeSystemWindows)
        {
            string className = GetWindowClassName(hwnd);

            string[] systemClassNames = new[]
            {
                "Shell_TrayWnd",        // Taskbar
                "Shell_SecondaryTrayWnd", // Secondary taskbar on multi-monitor
                "NotifyIconOverflowWindow", // Notification area overflow
                "Windows.UI.Core.CoreWindow", // Some system UI
                "ApplicationFrameWindow", // Sometimes UWP system windows
                "ForegroundStaging",     // System staging window
            };

            foreach (var sysClass in systemClassNames)
            {
                if (className.Equals(sysClass, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            // Check for windows with empty titles and suspect class names (often system UI)
            string title = GetWindowText(hwnd);
            if (string.IsNullOrWhiteSpace(title))
            {
                // Additional filtering for blank-title windows
                if (className.StartsWith("Windows.UI", StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }

        return true;
    }

    private string GetWindowText(IntPtr hwnd)
    {
        int length = Win32.GetWindowTextLength(hwnd);
        if (length == 0)
            return string.Empty;

        var sb = new StringBuilder(length + 1);
        Win32.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private string GetWindowClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        Win32.GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }
}

/// <summary>
/// Represents a detected window eligible for capture.
/// </summary>
internal sealed class DetectedWindow
{
    public IntPtr Handle { get; }
    public Win32.RECT Bounds { get; }
    public string Title { get; }
    public string ClassName { get; }

    public DetectedWindow(IntPtr handle, Win32.RECT bounds, string title, string className)
    {
        Handle = handle;
        Bounds = bounds;
        Title = title ?? string.Empty;
        ClassName = className ?? string.Empty;
    }

    public override string ToString() =>
        $"{Title} [{ClassName}] {Bounds} (HWND: 0x{Handle.ToInt64():X})";
}
