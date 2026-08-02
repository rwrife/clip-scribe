namespace ClipScribe.Core.Abstractions;

public interface IClipboardCaptureRuntime
{
    Task StartAsync(CancellationToken cancellationToken = default);

    void Stop();
}
