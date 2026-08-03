using ClipScribe.Core.Abstractions;
using ClipScribe.Core.Models;
using ClipScribe.Core.Services;

namespace ClipScribe.Tests;

public sealed class CaptureRuntimeControllerTests
{
    [Fact]
    public async Task InitializeAsync_StartsCaptureWhenNotPaused()
    {
        var runtime = new RecordingRuntime();
        var store = new InMemoryCaptureStateStore(new CaptureRuntimeState(IsPaused: false));
        var sut = new CaptureRuntimeController(runtime, store);

        await sut.InitializeAsync();

        Assert.False(sut.IsPaused);
        Assert.Equal(1, runtime.StartCalls);
        Assert.Equal(0, runtime.StopCalls);
    }

    [Fact]
    public async Task PauseState_PersistsAcrossRestarts()
    {
        var store = new InMemoryCaptureStateStore(new CaptureRuntimeState(IsPaused: false));

        var runtime1 = new RecordingRuntime();
        var controller1 = new CaptureRuntimeController(runtime1, store);
        await controller1.InitializeAsync();
        await controller1.SetPausedAsync(true);

        Assert.True(controller1.IsPaused);
        Assert.Equal(1, runtime1.StartCalls);
        Assert.Equal(1, runtime1.StopCalls);

        var runtime2 = new RecordingRuntime();
        var controller2 = new CaptureRuntimeController(runtime2, store);
        await controller2.InitializeAsync();

        Assert.True(controller2.IsPaused);
        Assert.Equal(0, runtime2.StartCalls);

        await controller2.SetPausedAsync(false);
        Assert.False(controller2.IsPaused);
        Assert.Equal(1, runtime2.StartCalls);
    }

    private sealed class RecordingRuntime : IClipboardCaptureRuntime
    {
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            StartCalls++;
            return Task.CompletedTask;
        }

        public void Stop() => StopCalls++;
    }

    private sealed class InMemoryCaptureStateStore : ICaptureStateStore
    {
        private CaptureRuntimeState _state;

        public InMemoryCaptureStateStore(CaptureRuntimeState initialState)
        {
            _state = initialState;
        }

        public Task<CaptureRuntimeState> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_state);

        public Task SaveAsync(CaptureRuntimeState state, CancellationToken cancellationToken = default)
        {
            _state = state;
            return Task.CompletedTask;
        }
    }
}
