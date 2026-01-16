using Microsoft.UI.Dispatching;
using System;
using System.Threading.Tasks;
using XerahS.RegionCapture.Core;
using XerahS.RegionCapture.UI;

namespace XerahS.RegionCapture;

/// <summary>
/// Public API for initiating region capture with multi-monitor and mixed-DPI support.
/// Thread-safe and designed to be called from hotkey handlers or menu actions.
/// </summary>
public sealed class RegionCaptureManager : IDisposable
{
    private readonly DispatcherQueue _dispatcherQueue;
    private RegionCaptureOverlay? _activeOverlay;
    private bool _isCapturing;
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of RegionCaptureManager.
    /// </summary>
    /// <param name="dispatcherQueue">Optional DispatcherQueue for UI thread marshalling. If null, uses current thread's queue.</param>
    public RegionCaptureManager(DispatcherQueue? dispatcherQueue = null)
    {
        _dispatcherQueue = dispatcherQueue ?? DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("RegionCaptureManager must be created on a thread with a DispatcherQueue (UI thread).");
    }

    /// <summary>
    /// Starts region capture mode asynchronously.
    /// Shows full-screen overlay across all monitors and waits for user selection.
    /// </summary>
    /// <param name="options">Configuration options for the capture session.</param>
    /// <returns>Task that completes with the capture result, or null if cancelled.</returns>
    /// <exception cref="InvalidOperationException">Thrown if capture is already in progress.</exception>
    public Task<RegionCaptureResult?> CaptureRegionAsync(RegionCaptureOptions? options = null)
    {
        lock (_lock)
        {
            if (_isCapturing)
            {
                throw new InvalidOperationException("Region capture is already in progress. Call CancelCapture() first.");
            }
            _isCapturing = true;
        }

        options ??= new RegionCaptureOptions();

        var tcs = new TaskCompletionSource<RegionCaptureResult?>();

        _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.High, async () =>
        {
            try
            {
                _activeOverlay = new RegionCaptureOverlay(options);
                var result = await _activeOverlay.ShowAsync();

                lock (_lock)
                {
                    _isCapturing = false;
                    _activeOverlay = null;
                }

                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    _isCapturing = false;
                    _activeOverlay = null;
                }
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }

    /// <summary>
    /// Cancels an active region capture session.
    /// Safe to call even if no capture is in progress.
    /// </summary>
    public void CancelCapture()
    {
        lock (_lock)
        {
            if (!_isCapturing || _activeOverlay == null)
            {
                return;
            }
        }

        _dispatcherQueue.TryEnqueue(() =>
        {
            _activeOverlay?.Cancel();
        });
    }

    /// <summary>
    /// Gets whether a capture session is currently active.
    /// </summary>
    public bool IsCapturing
    {
        get
        {
            lock (_lock)
            {
                return _isCapturing;
            }
        }
    }

    public void Dispose()
    {
        CancelCapture();
    }
}
