namespace ClipScribe.Core.Models;

public sealed record GlobalHotkeySettings(
    bool Ctrl = true,
    bool Shift = true,
    bool Alt = false,
    bool Win = false,
    string Key = "V")
{
    private static readonly HashSet<string> SupportedNamedKeys = new(StringComparer.Ordinal)
    {
        "TAB",
        "SPACE",
        "ENTER",
        "ESCAPE",
        "UP",
        "DOWN",
        "LEFT",
        "RIGHT",
        "INSERT",
        "DELETE",
        "HOME",
        "END",
        "PAGEUP",
        "PAGEDOWN"
    };

    public static GlobalHotkeySettings Default { get; } = new();

    public static GlobalHotkeySettings Normalize(GlobalHotkeySettings? value)
    {
        if (value is null)
        {
            return Default;
        }

        var normalizedKey = NormalizeKey(value.Key);

        return value with
        {
            Key = normalizedKey
        };
    }

    private static string NormalizeKey(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Default.Key;
        }

        var candidate = raw
            .Trim()
            .ToUpperInvariant()
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        if (candidate.Length == 1 && char.IsLetterOrDigit(candidate[0]))
        {
            return candidate;
        }

        if (candidate.Length >= 2 && candidate[0] == 'F' &&
            int.TryParse(candidate[1..], out var functionNumber) &&
            functionNumber is >= 1 and <= 24)
        {
            return $"F{functionNumber}";
        }

        if (SupportedNamedKeys.Contains(candidate))
        {
            return candidate;
        }

        return Default.Key;
    }
}
