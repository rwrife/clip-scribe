using ClipScribe.Core.Abstractions;

namespace ClipScribe.Core.Services;

public sealed class ClipboardCaptureEngine : IDisposable
{
    private readonly IClipboardUpdateSource _clipboardUpdateSource;
    private readonly ClipboardCaptureService _captureService;
    private readonly Action<Exception>? _onError;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();

    private bool _started;
    private bool _disposed;

    public ClipboardCaptureEngine(
        IClipboardUpdateSource clipboardUpdateSource,
        ClipboardCaptureService captureService,
        Action<Exception>? onError = null)
    {
        _clipboardUpdateSource = clipboardUpdateSource;
        _captureService = captureService;
        _onError = onError;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_started)
        {
            return;
        }

        await _captureService.InitializeAsync(cancellationToken);
        _clipboardUpdateSource.ClipboardUpdated += OnClipboardUpdated;
        _clipboardUpdateSource.Start();
        _started = true;
    }

    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        _clipboardUpdateSource.ClipboardUpdated -= OnClipboardUpdated;
        _clipboardUpdateSource.Stop();
        _started = false;
    }

    private async void OnClipboardUpdated(object? sender, EventArgs e)
    {
        try
        {
            await _gate.WaitAsync(_shutdown.Token).ConfigureAwait(false);
            try
            {
                await _captureService.CaptureCurrentClipboardAsync(_shutdown.Token).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
        catch (Exception ex)
        {
            _onError?.Invoke(ex);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ClipboardCaptureEngine));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _shutdown.Cancel();
        _shutdown.Dispose();
        _gate.Dispose();
        _clipboardUpdateSource.Dispose();
        _disposed = true;
    }
}
