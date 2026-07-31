using ClipScribe.Core.Abstractions;
using ClipScribe.Core.Models;
using ClipScribe.Core.Utilities;

namespace ClipScribe.Core.Services;

public sealed class ClipboardCaptureService
{
    private readonly IClipRepository _repository;
    private readonly IClipboardTextReader _clipboardTextReader;
    private readonly IForegroundWindowInfoProvider _foregroundWindowInfoProvider;
    private readonly CaptureOptions _options;

    public ClipboardCaptureService(
        IClipRepository repository,
        IClipboardTextReader clipboardTextReader,
        IForegroundWindowInfoProvider foregroundWindowInfoProvider,
        CaptureOptions options)
    {
        _repository = repository;
        _clipboardTextReader = clipboardTextReader;
        _foregroundWindowInfoProvider = foregroundWindowInfoProvider;
        _options = options.Validate();
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => _repository.InitializeAsync(cancellationToken);

    public async Task<ClipSaveResult?> CaptureCurrentClipboardAsync(CancellationToken cancellationToken = default)
    {
        if (!_clipboardTextReader.TryReadText(out var text) || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = Normalize(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var clip = new NewClip(
            Content: normalized,
            ContentHash: TextHasher.Sha256(normalized),
            ContentType: ClipContentType.Text,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            SourceApp: _foregroundWindowInfoProvider.TryGetForegroundProcessName(),
            IsPinned: false);

        return await _repository.SaveAsync(clip, _options, cancellationToken);
    }

    private static string Normalize(string text)
        => text.Replace("\0", string.Empty).Trim();
}
