using System.Text.Json;
using ClipScribe.Core.Models;

namespace ClipScribe.Core.Services;

public sealed class JsonAppSettingsStore
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public JsonAppSettingsStore(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        _settingsPath = settingsPath;
    }

    public void EnsureExists()
    {
        if (File.Exists(_settingsPath))
        {
            return;
        }

        WriteSettingsDocument(new SettingsDocument
        {
            Hotkey = GlobalHotkeySettings.Default,
            LocalAi = LocalAiSettings.Default,
            Privacy = PrivacySettings.Default
        });
    }

    public GlobalHotkeySettings LoadHotkey()
    {
        var doc = ReadSettingsDocument();
        return GlobalHotkeySettings.Normalize(doc?.Hotkey);
    }

    public LocalAiSettings LoadLocalAiSettings()
    {
        var doc = ReadSettingsDocument();
        return LocalAiSettings.Normalize(doc?.LocalAi);
    }

    public PrivacySettings LoadPrivacySettings()
    {
        var doc = ReadSettingsDocument();
        return PrivacySettings.Normalize(doc?.Privacy);
    }

    public void SaveHotkey(GlobalHotkeySettings hotkey)
    {
        var existing = ReadSettingsDocument();

        var payload = new SettingsDocument
        {
            Hotkey = GlobalHotkeySettings.Normalize(hotkey),
            LocalAi = LocalAiSettings.Normalize(existing?.LocalAi),
            Privacy = PrivacySettings.Normalize(existing?.Privacy)
        };

        WriteSettingsDocument(payload);
    }

    public static GlobalHotkeySettings ParseHotkey(string? json)
    {
        var payload = ParseSettingsDocument(json);
        return GlobalHotkeySettings.Normalize(payload?.Hotkey);
    }

    public static LocalAiSettings ParseLocalAiSettings(string? json)
    {
        var payload = ParseSettingsDocument(json);
        return LocalAiSettings.Normalize(payload?.LocalAi);
    }

    public static PrivacySettings ParsePrivacySettings(string? json)
    {
        var payload = ParseSettingsDocument(json);
        return PrivacySettings.Normalize(payload?.Privacy);
    }

    private SettingsDocument? ReadSettingsDocument()
    {
        if (!File.Exists(_settingsPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            return ParseSettingsDocument(json);
        }
        catch
        {
            return null;
        }
    }

    private static SettingsDocument? ParseSettingsDocument(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SettingsDocument>(json, ReadOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void WriteSettingsDocument(SettingsDocument payload)
    {
        var parent = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var json = JsonSerializer.Serialize(payload, WriteOptions);
        File.WriteAllText(_settingsPath, json);
    }

    private sealed class SettingsDocument
    {
        public GlobalHotkeySettings? Hotkey { get; init; }

        public LocalAiSettings? LocalAi { get; init; }

        public PrivacySettings? Privacy { get; init; }
    }
}
