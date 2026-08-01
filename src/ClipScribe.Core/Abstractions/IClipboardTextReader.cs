namespace ClipScribe.Core.Abstractions;

public interface IClipboardTextReader
{
    bool TryReadText(out string? text);
}
