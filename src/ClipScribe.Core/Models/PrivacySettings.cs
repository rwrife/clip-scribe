namespace ClipScribe.Core.Models;

public sealed record PrivacySettings(
    bool RespectClipboardViewerIgnore = true,
    bool IgnoreSensitiveContent = true,
    IReadOnlyList<string>? ExcludedApps = null)
{
    public static PrivacySettings Default { get; } = new(
        RespectClipboardViewerIgnore: true,
        IgnoreSensitiveContent: true,
        ExcludedApps: Array.Empty<string>());

    public static PrivacySettings Normalize(PrivacySettings? value)
    {
        if (value is null)
        {
            return Default;
        }

        var normalizedApps = (value.ExcludedApps ?? Array.Empty<string>())
            .Select(static app => (app ?? string.Empty).Trim())
            .Where(static app => !string.IsNullOrWhiteSpace(app))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static app => app, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new PrivacySettings(
            RespectClipboardViewerIgnore: value.RespectClipboardViewerIgnore,
            IgnoreSensitiveContent: value.IgnoreSensitiveContent,
            ExcludedApps: normalizedApps);
    }

    public bool IsSourceAppExcluded(string? sourceApp)
    {
        if (string.IsNullOrWhiteSpace(sourceApp))
        {
            return false;
        }

        var app = sourceApp.Trim();
        foreach (var excluded in ExcludedApps ?? Array.Empty<string>())
        {
            if (string.Equals(excluded, app, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
