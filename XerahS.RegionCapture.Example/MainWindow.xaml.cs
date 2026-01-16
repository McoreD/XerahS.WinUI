using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;
using WinRT.Interop;

namespace XerahS.RegionCapture.Example;

public sealed partial class MainWindow : Window
{
    private RegionCaptureManager? _captureManager;
    private RegionCaptureResult? _lastResult;

    public MainWindow()
    {
        this.InitializeComponent();
        this.Title = "XerahS Region Capture - Example";

        // Initialize capture manager
        _captureManager = new RegionCaptureManager();

        // Register global hotkey (Ctrl+Shift+R)
        RootGrid.KeyDown += OnWindowKeyDown;
    }

    private void OnWindowKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Check for Ctrl+Shift+R
        var ctrlState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        var shiftState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);

        bool ctrlPressed = (ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
        bool shiftPressed = (shiftState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

        if (ctrlPressed && shiftPressed && e.Key == VirtualKey.R)
        {
            StartCaptureAsync();
        }
    }

    private void StartCaptureButton_Click(object sender, RoutedEventArgs e)
    {
        StartCaptureAsync();
    }

    private async void StartCaptureAsync()
    {
        if (_captureManager == null)
            return;

        // Update status
        StatusText.Text = "Starting region capture...";
        ResultsContainer.Visibility = Visibility.Collapsed;

        // Configure options based on UI
        var options = new RegionCaptureOptions
        {
            EnableWindowDetection = WindowDetectionCheckBox.IsChecked ?? true,
            ShowCrosshair = ShowCrosshairCheckBox.IsChecked ?? true,
            ShowCoordinateHUD = ShowHUDCheckBox.IsChecked ?? true,
            EnableEdgeSnapping = EdgeSnappingCheckBox.IsChecked ?? true,
            EnableDiagnostics = true
        };

        try
        {
            // Start capture
            var result = await _captureManager.CaptureRegionAsync(options);

            if (result != null)
            {
                _lastResult = result;
                DisplayResult(result);
            }
            else
            {
                StatusText.Text = "Capture was cancelled.";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Capture failed: {ex.Message}";
        }
    }

    private async void DisplayResult(RegionCaptureResult result)
    {
        // Build result text
        var sb = new StringBuilder();
        sb.AppendLine($"Capture Type: {(result.IsWindowCapture ? "Window Auto-Select" : "Manual Rectangle")}");
        sb.AppendLine($"Region: {result.Region}");
        sb.AppendLine($"Bitmap Size: {result.Bitmap.PixelWidth}x{result.Bitmap.PixelHeight}");
        sb.AppendLine($"Primary Monitor: {result.PrimaryMonitor}");
        sb.AppendLine($"Capture Duration: {(result.Diagnostics.CaptureEndTime - result.Diagnostics.CaptureStartTime).TotalMilliseconds:F0}ms");
        sb.AppendLine();
        sb.AppendLine("Virtual Desktop Configuration:");
        sb.AppendLine($"  Bounds: {result.Diagnostics.VirtualDesktopBounds}");
        sb.AppendLine($"  Monitors: {result.Diagnostics.AllMonitors.Length}");

        for (int i = 0; i < result.Diagnostics.AllMonitors.Length; i++)
        {
            var mon = result.Diagnostics.AllMonitors[i];
            sb.AppendLine($"    [{i + 1}] {mon}");
        }

        if (result.IsWindowCapture)
        {
            sb.AppendLine();
            sb.AppendLine($"Window Handle: 0x{result.WindowHandle.ToInt64():X}");
        }

        ResultText.Text = sb.ToString();

        // Display image
        try
        {
            var bitmap = new WriteableBitmap(result.Bitmap.PixelWidth, result.Bitmap.PixelHeight);

            result.Bitmap.CopyToBuffer(bitmap.PixelBuffer);

            ResultImage.Source = bitmap;
        }
        catch (Exception ex)
        {
            sb.AppendLine();
            sb.AppendLine($"Failed to display image: {ex.Message}");
            ResultText.Text = sb.ToString();
        }

        // Show results
        ResultsContainer.Visibility = Visibility.Visible;
        SaveButton.Visibility = Visibility.Visible;
        StatusText.Text = "Capture completed successfully!";

        // Log diagnostics to debug output
        System.Diagnostics.Debug.WriteLine("=== CAPTURE DIAGNOSTICS ===");
        System.Diagnostics.Debug.WriteLine(result.Diagnostics.TransformLog);
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastResult == null)
            return;

        try
        {
            // Create file picker
            var savePicker = new FileSavePicker();
            savePicker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            savePicker.FileTypeChoices.Add("PNG Image", new[] { ".png" });
            savePicker.FileTypeChoices.Add("JPEG Image", new[] { ".jpg", ".jpeg" });
            savePicker.SuggestedFileName = $"Capture_{DateTime.Now:yyyyMMdd_HHmmss}";

            // Initialize with window handle
            var hwnd = WindowNative.GetWindowHandle(this);
            InitializeWithWindow.Initialize(savePicker, hwnd);

            // Show picker
            StorageFile file = await savePicker.PickSaveFileAsync();

            if (file != null)
            {
                using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite))
                {
                    BitmapEncoder encoder;

                    if (file.FileType.ToLowerInvariant() == ".png")
                    {
                        encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                    }
                    else
                    {
                        encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream);
                    }

                    encoder.SetSoftwareBitmap(_lastResult.Bitmap);
                    await encoder.FlushAsync();
                }

                StatusText.Text = $"Image saved to: {file.Path}";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to save image: {ex.Message}";
        }
    }
}
