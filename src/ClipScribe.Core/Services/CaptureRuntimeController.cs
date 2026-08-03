using ClipScribe.Core.Abstractions;
using ClipScribe.Core.Models;

namespace ClipScribe.Core.Services;

public sealed class CaptureRuntimeController : IDisposable
{
    private readonly IClipboardCaptureRuntime _runtime;
    private readonly ICaptureStateStore _stateStore;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private bool _initialized;
    private bool _disposed;

    public CaptureRuntimeController(IClipboardCaptureRuntime runtime, ICaptureStateStore stateStore)
    {
        _runtime = runtime;
        _stateStore = stateStore;
    }

    public bool IsPaused { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            var state = await _stateStore.LoadAsync(cancellationToken);
            IsPaused = state.IsPaused;
            if (!IsPaused)
            {
                await _runtime.StartAsync(cancellationToken);
            }

            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetPausedAsync(bool paused, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await InitializeAsync(cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsPaused == paused)
            {
                return;
            }

            if (paused)
            {
                _runtime.Stop();
            }
            else
            {
                await _runtime.StartAsync(cancellationToken);
            }

            IsPaused = paused;
            await _stateStore.SaveAsync(new CaptureRuntimeState(IsPaused), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task TogglePausedAsync(CancellationToken cancellationToken = default)
        => SetPausedAsync(!IsPaused, cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _runtime.Stop();
        _gate.Dispose();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(CaptureRuntimeController));
        }
    }
}
