namespace ClipScribe.Core.Abstractions;

public interface IClipboardUpdateSource : IDisposable
{
    event EventHandler? ClipboardUpdated;

    void Start();

    void Stop();
}
