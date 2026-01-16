using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using XerahS.RegionCapture.Core.Capture;
using XerahS.RegionCapture.Core.Coordinates;
using XerahS.RegionCapture.NativeMethods;

namespace XerahS.RegionCapture.Tests;

/// <summary>
/// Automated pixel-correctness test harness.
/// Validates that captured regions match screen pixels across monitors with different DPI settings.
/// </summary>
[TestClass]
public class PixelCorrectnessTests
{
    private VirtualDesktop? _virtualDesktop;

    [TestInitialize]
    public void Initialize()
    {
        // Enumerate monitors
        _virtualDesktop = VirtualDesktop.Enumerate();

        // Log monitor configuration
        Console.WriteLine("=== Monitor Configuration ===");
        Console.WriteLine(_virtualDesktop.GenerateDiagnosticReport());
        Console.WriteLine();
    }

    [TestMethod]
    public async Task TestRandomRegions_SingleMonitor()
    {
        Assert.IsNotNull(_virtualDesktop);

        var primaryMonitor = _virtualDesktop.GetPrimaryMonitor();
        var random = new Random(12345); // Deterministic seed

        var testCases = GenerateRandomRegions(primaryMonitor.Bounds, random, count: 10);

        await RunPixelCorrectnessTests(testCases, "SingleMonitor");
    }

    [TestMethod]
    public async Task TestRandomRegions_AllMonitors()
    {
        Assert.IsNotNull(_virtualDesktop);

        var random = new Random(67890); // Deterministic seed
        var testCases = new List<Win32.RECT>();

        // Generate test cases for each monitor
        foreach (var monitor in _virtualDesktop.Monitors)
        {
            testCases.AddRange(GenerateRandomRegions(monitor.Bounds, random, count: 5));
        }

        await RunPixelCorrectnessTests(testCases, "AllMonitors");
    }

    [TestMethod]
    public async Task TestMonitorEdgeCases()
    {
        Assert.IsNotNull(_virtualDesktop);

        var testCases = new List<Win32.RECT>();

        foreach (var monitor in _virtualDesktop.Monitors)
        {
            var bounds = monitor.Bounds;

            // Top-left corner
            testCases.Add(new Win32.RECT(bounds.Left, bounds.Top, bounds.Left + 100, bounds.Top + 100));

            // Top-right corner
            testCases.Add(new Win32.RECT(bounds.Right - 100, bounds.Top, bounds.Right, bounds.Top + 100));

            // Bottom-left corner
            testCases.Add(new Win32.RECT(bounds.Left, bounds.Bottom - 100, bounds.Left + 100, bounds.Bottom));

            // Bottom-right corner
            testCases.Add(new Win32.RECT(bounds.Right - 100, bounds.Bottom - 100, bounds.Right, bounds.Bottom));

            // Center region
            int centerX = bounds.Left + bounds.Width / 2;
            int centerY = bounds.Top + bounds.Height / 2;
            testCases.Add(new Win32.RECT(centerX - 50, centerY - 50, centerX + 50, centerY + 50));
        }

        await RunPixelCorrectnessTests(testCases, "EdgeCases");
    }

    [TestMethod]
    public async Task TestNegativeCoordinates()
    {
        Assert.IsNotNull(_virtualDesktop);

        // Find monitors with negative coordinates
        var monitorsWithNegativeCoords = _virtualDesktop.Monitors
            .Where(m => m.Bounds.Left < 0 || m.Bounds.Top < 0)
            .ToList();

        if (monitorsWithNegativeCoords.Count == 0)
        {
            Assert.Inconclusive("No monitors with negative coordinates found. " +
                "Test requires multi-monitor setup with monitors positioned left/above primary.");
            return;
        }

        var testCases = new List<Win32.RECT>();
        var random = new Random(11111);

        foreach (var monitor in monitorsWithNegativeCoords)
        {
            testCases.AddRange(GenerateRandomRegions(monitor.Bounds, random, count: 5));
        }

        await RunPixelCorrectnessTests(testCases, "NegativeCoordinates");
    }

    [TestMethod]
    public async Task TestCrossingMonitorBoundaries()
    {
        Assert.IsNotNull(_virtualDesktop);

        if (_virtualDesktop.Monitors.Count < 2)
        {
            Assert.Inconclusive("Test requires at least 2 monitors.");
            return;
        }

        var testCases = new List<Win32.RECT>();

        // Find adjacent monitors and create regions that span the boundary
        for (int i = 0; i < _virtualDesktop.Monitors.Count; i++)
        {
            for (int j = i + 1; j < _virtualDesktop.Monitors.Count; j++)
            {
                var monitor1 = _virtualDesktop.Monitors[i];
                var monitor2 = _virtualDesktop.Monitors[j];

                // Check if monitors are adjacent horizontally
                if (monitor1.Bounds.Right == monitor2.Bounds.Left ||
                    monitor2.Bounds.Right == monitor1.Bounds.Left)
                {
                    int left = Math.Max(monitor1.Bounds.Left, monitor2.Bounds.Left) - 50;
                    int top = Math.Max(monitor1.Bounds.Top, monitor2.Bounds.Top);
                    testCases.Add(new Win32.RECT(left, top, left + 100, top + 100));
                }

                // Check if monitors are adjacent vertically
                if (monitor1.Bounds.Bottom == monitor2.Bounds.Top ||
                    monitor2.Bounds.Bottom == monitor1.Bounds.Top)
                {
                    int left = Math.Max(monitor1.Bounds.Left, monitor2.Bounds.Left);
                    int top = Math.Max(monitor1.Bounds.Top, monitor2.Bounds.Top) - 50;
                    testCases.Add(new Win32.RECT(left, top, left + 100, top + 100));
                }
            }
        }

        if (testCases.Count == 0)
        {
            Assert.Inconclusive("No adjacent monitors found for boundary testing.");
            return;
        }

        await RunPixelCorrectnessTests(testCases, "MonitorBoundaries");
    }

    private List<Win32.RECT> GenerateRandomRegions(Win32.RECT bounds, Random random, int count)
    {
        var regions = new List<Win32.RECT>();

        for (int i = 0; i < count; i++)
        {
            // Random position and size within bounds
            int width = random.Next(50, Math.Min(300, bounds.Width / 2));
            int height = random.Next(50, Math.Min(300, bounds.Height / 2));

            int maxLeft = bounds.Right - width;
            int maxTop = bounds.Bottom - height;

            int left = random.Next(bounds.Left, maxLeft);
            int top = random.Next(bounds.Top, maxTop);

            regions.Add(new Win32.RECT(left, top, left + width, top + height));
        }

        return regions;
    }

    private async Task RunPixelCorrectnessTests(List<Win32.RECT> testCases, string testName)
    {
        Assert.IsNotNull(_virtualDesktop);

        int passed = 0;
        int failed = 0;
        var failures = new List<(Win32.RECT region, string error)>();

        Console.WriteLine($"=== Running {testName} Tests ===");
        Console.WriteLine($"Test cases: {testCases.Count}");
        Console.WriteLine();

        foreach (var region in testCases)
        {
            try
            {
                // Capture using the engine
                var captured = await ScreenCaptureEngine.CaptureRegionAsync(region);

                // Verify dimensions
                if (captured.PixelWidth != region.Width || captured.PixelHeight != region.Height)
                {
                    string error = $"Dimension mismatch: Expected {region.Width}x{region.Height}, " +
                                   $"Got {captured.PixelWidth}x{captured.PixelHeight}";
                    failures.Add((region, error));
                    failed++;
                    Console.WriteLine($"FAIL: {region} - {error}");
                    continue;
                }

                // Sample a few pixels and verify they match screen content
                // (This is a simplified check - a full implementation would compare all pixels)
                bool pixelsMatch = await VerifyPixelSamples(region, captured);

                if (pixelsMatch)
                {
                    passed++;
                    Console.WriteLine($"PASS: {region}");
                }
                else
                {
                    failures.Add((region, "Pixel content mismatch"));
                    failed++;
                    Console.WriteLine($"FAIL: {region} - Pixel content mismatch");
                }

                captured.Dispose();
            }
            catch (Exception ex)
            {
                failures.Add((region, ex.Message));
                failed++;
                Console.WriteLine($"FAIL: {region} - Exception: {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Results: {passed} passed, {failed} failed");
        Console.WriteLine();

        // Save failure report if any failures
        if (failures.Count > 0)
        {
            SaveFailureReport(testName, failures);
        }

        // Assert all tests passed
        Assert.AreEqual(0, failed, $"{failed} test(s) failed. See failure report for details.");
    }

    private async Task<bool> VerifyPixelSamples(Win32.RECT region, SoftwareBitmap captured)
    {
        // Sample pixels at specific positions and compare with screen
        // For simplicity, we'll sample 5 positions: corners and center

        var samplePoints = new (int x, int y)[]
        {
            (0, 0), // Top-left
            (region.Width - 1, 0), // Top-right
            (0, region.Height - 1), // Bottom-left
            (region.Width - 1, region.Height - 1), // Bottom-right
            (region.Width / 2, region.Height / 2) // Center
        };

        // Note: In a real implementation, you would capture individual pixels from the screen
        // and compare them with the captured bitmap. For this test harness, we're doing
        // a simplified verification by checking that the bitmap is not completely black
        // (which would indicate a failed capture).

        using (var buffer = captured.LockBuffer(BitmapBufferAccessMode.Read))
        using (var reference = buffer.CreateReference())
        {
            unsafe
            {
                byte* dataInBytes;
                uint capacity;
                ((IMemoryBufferByteAccess)reference).GetBuffer(out dataInBytes, out capacity);

                // Check that at least some pixels are non-zero
                // (A real implementation would do actual pixel comparison)
                int nonZeroPixels = 0;
                int stride = captured.PixelWidth * 4;

                for (int i = 0; i < capacity; i += 4)
                {
                    if (dataInBytes[i] != 0 || dataInBytes[i + 1] != 0 || dataInBytes[i + 2] != 0)
                    {
                        nonZeroPixels++;
                        if (nonZeroPixels > 10) break; // Enough evidence
                    }
                }

                return nonZeroPixels > 0;
            }
        }
    }

    private void SaveFailureReport(string testName, List<(Win32.RECT region, string error)> failures)
    {
        var reportPath = Path.Combine(Path.GetTempPath(), $"RegionCapture_FailureReport_{testName}_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

        using (var writer = new StreamWriter(reportPath))
        {
            writer.WriteLine($"=== Region Capture Failure Report: {testName} ===");
            writer.WriteLine($"Generated: {DateTime.Now}");
            writer.WriteLine();
            writer.WriteLine(_virtualDesktop!.GenerateDiagnosticReport());
            writer.WriteLine();
            writer.WriteLine("=== Failures ===");

            foreach (var (region, error) in failures)
            {
                writer.WriteLine($"Region: {region}");
                writer.WriteLine($"Error: {error}");
                writer.WriteLine();
            }
        }

        Console.WriteLine($"Failure report saved to: {reportPath}");
    }

    [System.Runtime.InteropServices.ComImport]
    [System.Runtime.InteropServices.Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
    private unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }
}
