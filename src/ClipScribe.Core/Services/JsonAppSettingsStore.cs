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

        SaveHotkey(GlobalHotkeySettings.Default);
    }

    public GlobalHotkeySettings LoadHotkey()
    {
        if (!File.Exists(_settingsPath))
        {
            return GlobalHotkeySettings.Default;
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            return ParseHotkey(json);
        }
        catch
        {
            return GlobalHotkeySettings.Default;
        }
    }

    public void SaveHotkey(GlobalHotkeySettings hotkey)
    {
        var normalized = GlobalHotkeySettings.Normalize(hotkey);
        var payload = new SettingsDocument
        {
            Hotkey = normalized
        };

        var parent = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var json = JsonSerializer.Serialize(payload, WriteOptions);
        File.WriteAllText(_settingsPath, json);
    }

    public static GlobalHotkeySettings ParseHotkey(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return GlobalHotkeySettings.Default;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<SettingsDocument>(json, ReadOptions);
            return GlobalHotkeySettings.Normalize(payload?.Hotkey);
        }
        catch (JsonException)
        {
            return GlobalHotkeySettings.Default;
        }
    }

    private sealed class SettingsDocument
    {
        public GlobalHotkeySettings? Hotkey { get; init; }
    }
}
