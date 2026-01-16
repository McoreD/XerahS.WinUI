using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using WinRT;
using XerahS.RegionCapture.NativeMethods;

namespace XerahS.RegionCapture.Core.Capture;

/// <summary>
/// Screen capture engine using GDI+ BitBlt for pixel-perfect capture.
/// Coordinates are in physical pixels (virtual desktop space).
/// </summary>
internal sealed class ScreenCaptureEngine
{
    /// <summary>
    /// Captures a region of the screen in physical pixel coordinates.
    /// </summary>
    /// <param name="rect">Region to capture in physical pixels (virtual desktop coordinates).</param>
    /// <returns>SoftwareBitmap containing the captured image in BGRA8 format.</returns>
    public static async Task<SoftwareBitmap> CaptureRegionAsync(Win32.RECT rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            throw new ArgumentException("Capture region must have positive dimensions.", nameof(rect));

        return await Task.Run(() => CaptureRegion(rect));
    }

    private static SoftwareBitmap CaptureRegion(Win32.RECT rect)
    {
        IntPtr hdcScreen = IntPtr.Zero;
        IntPtr hdcMem = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;

        try
        {
            // Get screen DC
            hdcScreen = Win32.GetDC(IntPtr.Zero);
            if (hdcScreen == IntPtr.Zero)
                throw new InvalidOperationException("Failed to get screen DC.");

            // Create compatible DC
            hdcMem = Win32.CreateCompatibleDC(hdcScreen);
            if (hdcMem == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create compatible DC.");

            // Create compatible bitmap
            hBitmap = Win32.CreateCompatibleBitmap(hdcScreen, rect.Width, rect.Height);
            if (hBitmap == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create compatible bitmap.");

            // Select bitmap into memory DC
            IntPtr hOld = Win32.SelectObject(hdcMem, hBitmap);

            // Copy screen pixels to bitmap
            // BitBlt uses screen coordinates directly (physical pixels in virtual desktop space)
            bool success = Win32.BitBlt(
                hdcMem,
                0, 0,
                rect.Width, rect.Height,
                hdcScreen,
                rect.Left, rect.Top,
                Win32.SRCCOPY);

            if (!success)
                throw new InvalidOperationException("BitBlt failed.");

            // Convert to SoftwareBitmap
            var softwareBitmap = ConvertToSoftwareBitmap(hBitmap, rect.Width, rect.Height);

            // Restore old object
            Win32.SelectObject(hdcMem, hOld);

            return softwareBitmap;
        }
        finally
        {
            // Cleanup
            if (hBitmap != IntPtr.Zero)
                Win32.DeleteObject(hBitmap);

            if (hdcMem != IntPtr.Zero)
                Win32.DeleteDC(hdcMem);

            if (hdcScreen != IntPtr.Zero)
                Win32.ReleaseDC(IntPtr.Zero, hdcScreen);
        }
    }

    private static SoftwareBitmap ConvertToSoftwareBitmap(IntPtr hBitmap, int width, int height)
    {
        // Get BITMAP structure
        BITMAP bmp = new BITMAP();
        int size = Marshal.SizeOf<BITMAP>();

        if (GetObject(hBitmap, size, ref bmp) == 0)
            throw new InvalidOperationException("Failed to get BITMAP structure.");

        // Create SoftwareBitmap
        var softwareBitmap = new SoftwareBitmap(
            BitmapPixelFormat.Bgra8,
            width,
            height,
            BitmapAlphaMode.Premultiplied);

        // Get bitmap bits
        using (var buffer = softwareBitmap.LockBuffer(BitmapBufferAccessMode.Write))
        using (var reference = buffer.CreateReference())
        {
            unsafe
            {
                byte* dataInBytes;
                uint capacity;
                reference.As<IMemoryBufferByteAccess>().GetBuffer(out dataInBytes, out capacity);

                // Copy pixel data
                // GDI bitmap is in BGR format (32-bit), we need BGRA8
                int stride = width * 4;
                IntPtr bits = Marshal.AllocHGlobal(stride * height);

                try
                {
                    BITMAPINFO bi = new BITMAPINFO();
                    bi.biSize = Marshal.SizeOf<BITMAPINFOHEADER>();
                    bi.biWidth = width;
                    bi.biHeight = -height; // Top-down DIB
                    bi.biPlanes = 1;
                    bi.biBitCount = 32;
                    bi.biCompression = 0; // BI_RGB

                    IntPtr hdc = Win32.GetDC(IntPtr.Zero);
                    try
                    {
                        int result = GetDIBits(hdc, hBitmap, 0, (uint)height, bits, ref bi, 0);
                        if (result == 0)
                            throw new InvalidOperationException("GetDIBits failed.");

                        // Copy to SoftwareBitmap buffer
                        byte* src = (byte*)bits.ToPointer();
                        byte* dst = dataInBytes;

                        for (int i = 0; i < stride * height; i += 4)
                        {
                            // BGR -> BGRA (add alpha channel)
                            dst[i] = src[i];         // B
                            dst[i + 1] = src[i + 1]; // G
                            dst[i + 2] = src[i + 2]; // R
                            dst[i + 3] = 255;        // A (opaque)
                        }
                    }
                    finally
                    {
                        Win32.ReleaseDC(IntPtr.Zero, hdc);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(bits);
                }
            }
        }

        return softwareBitmap;
    }

    #region Additional P/Invoke

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr hObject, int nSize, ref BITMAP bmp);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hBitmap, uint start, uint cLines, IntPtr lpvBits, ref BITMAPINFO lpbmi, uint usage);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }

    #endregion
}
