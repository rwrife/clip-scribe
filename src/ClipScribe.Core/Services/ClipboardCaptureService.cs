using ClipScribe.Core.Abstractions;
using ClipScribe.Core.Models;
using ClipScribe.Core.Utilities;

namespace ClipScribe.Core.Services;

public sealed class ClipboardCaptureService
{
    private static readonly string[] SensitiveSourceAppMarkers =
    [
        "1password",
        "bitwarden",
        "keepass",
        "lastpass",
        "dashlane",
        "enpass",
        "nordpass",
        "protonpass"
    ];

    private static readonly string[] SensitiveTextMarkers =
    [
        "BEGIN PRIVATE KEY",
        "OPENSSH PRIVATE KEY",
        "PRIVATE KEY-----",
        "ghp_",
        "gho_",
        "github_pat_",
        "xoxp-",
        "xoxb-",
        "AKIA"
    ];

    private readonly IClipRepository _repository;
    private readonly IClipboardTextReader _clipboardTextReader;
    private readonly IForegroundWindowInfoProvider _foregroundWindowInfoProvider;
    private readonly CaptureOptions _options;
    private readonly Func<PrivacySettings>? _privacySettingsProvider;

    public ClipboardCaptureService(
        IClipRepository repository,
        IClipboardTextReader clipboardTextReader,
        IForegroundWindowInfoProvider foregroundWindowInfoProvider,
        CaptureOptions options,
        Func<PrivacySettings>? privacySettingsProvider = null)
    {
        _repository = repository;
        _clipboardTextReader = clipboardTextReader;
        _foregroundWindowInfoProvider = foregroundWindowInfoProvider;
        _options = options.Validate();
        _privacySettingsProvider = privacySettingsProvider;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => _repository.InitializeAsync(cancellationToken);

    public async Task<ClipSaveResult?> CaptureCurrentClipboardAsync(CancellationToken cancellationToken = default)
    {
        var sourceApp = _foregroundWindowInfoProvider.TryGetForegroundProcessName();
        var privacy = _privacySettingsProvider is null
            ? PrivacySettings.Default
            : PrivacySettings.Normalize(_privacySettingsProvider());

        if (privacy.RespectClipboardViewerIgnore && _clipboardTextReader.ShouldIgnoreCurrentClipboard())
        {
            return null;
        }

        if (privacy.IsSourceAppExcluded(sourceApp))
        {
            return null;
        }

        if (!_clipboardTextReader.TryReadText(out var text) || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = Normalize(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (privacy.IgnoreSensitiveContent && LooksSensitive(normalized, sourceApp))
        {
            return null;
        }

        var clip = new NewClip(
            Content: normalized,
            ContentHash: TextHasher.Sha256(normalized),
            ContentType: ClipContentType.Text,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            SourceApp: sourceApp,
            IsPinned: false);

        return await _repository.SaveAsync(clip, _options, cancellationToken);
    }

    private static string Normalize(string text)
        => text.Replace("\0", string.Empty).Trim();

    private static bool LooksSensitive(string text, string? sourceApp)
    {
        if (LooksLikeSensitiveSourceApp(sourceApp))
        {
            return true;
        }

        if (ContainsSensitiveMarker(text))
        {
            return true;
        }

        return LooksLikeSecretToken(text);
    }

    private static bool LooksLikeSensitiveSourceApp(string? sourceApp)
    {
        if (string.IsNullOrWhiteSpace(sourceApp))
        {
            return false;
        }

        return SensitiveSourceAppMarkers.Any(marker =>
            sourceApp.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsSensitiveMarker(string text)
    {
        foreach (var marker in SensitiveTextMarkers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeSecretToken(string text)
    {
        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (text.Length is < 20 or > 2048)
        {
            return false;
        }

        if (text.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var hasUpper = false;
        var hasLower = false;
        var hasDigit = false;
        var hasSymbol = false;
        var allowedChars = 0;

        foreach (var c in text)
        {
            if (char.IsUpper(c))
            {
                hasUpper = true;
            }
            else if (char.IsLower(c))
            {
                hasLower = true;
            }
            else if (char.IsDigit(c))
            {
                hasDigit = true;
            }
            else
            {
                hasSymbol = true;
            }

            if (char.IsLetterOrDigit(c) || c is '-' or '_' or '=' or '+' or '/')
            {
                allowedChars++;
            }
        }

        var classCount =
            (hasUpper ? 1 : 0) +
            (hasLower ? 1 : 0) +
            (hasDigit ? 1 : 0) +
            (hasSymbol ? 1 : 0);

        if (classCount < 3 && text.Length < 40)
        {
            return false;
        }

        return allowedChars >= (int)(text.Length * 0.85);
    }
}
