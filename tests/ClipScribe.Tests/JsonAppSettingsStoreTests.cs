using ClipScribe.Core.Models;
using ClipScribe.Core.Services;

namespace ClipScribe.Tests;

public sealed class JsonAppSettingsStoreTests
{
    [Fact]
    public void ParseHotkey_ReturnsDefault_WhenJsonIsMissingOrInvalid()
    {
        var fromEmpty = JsonAppSettingsStore.ParseHotkey(null);
        var fromInvalid = JsonAppSettingsStore.ParseHotkey("{ definitely-not-json }");

        Assert.Equal(GlobalHotkeySettings.Default, fromEmpty);
        Assert.Equal(GlobalHotkeySettings.Default, fromInvalid);
    }

    [Fact]
    public void ParseHotkey_NormalizesConfiguredHotkey()
    {
        var json =
            """
            {
              "hotkey": {
                "ctrl": false,
                "shift": true,
                "alt": true,
                "win": false,
                "key": " f12 "
              }
            }
            """;

        var hotkey = JsonAppSettingsStore.ParseHotkey(json);

        Assert.False(hotkey.Ctrl);
        Assert.True(hotkey.Shift);
        Assert.True(hotkey.Alt);
        Assert.False(hotkey.Win);
        Assert.Equal("F12", hotkey.Key);
    }

    [Fact]
    public void ParseHotkey_FallsBackToDefaultKey_WhenConfiguredKeyUnsupported()
    {
        var json =
            """
            {
              "hotkey": {
                "ctrl": true,
                "shift": false,
                "alt": false,
                "win": false,
                "key": "!!!"
              }
            }
            """;

        var hotkey = JsonAppSettingsStore.ParseHotkey(json);

        Assert.True(hotkey.Ctrl);
        Assert.False(hotkey.Shift);
        Assert.False(hotkey.Alt);
        Assert.False(hotkey.Win);
        Assert.Equal(GlobalHotkeySettings.Default.Key, hotkey.Key);
    }

    [Fact]
    public void ParseLocalAiSettings_ReturnsDefault_WhenJsonIsMissingOrInvalid()
    {
        var fromEmpty = JsonAppSettingsStore.ParseLocalAiSettings(null);
        var fromInvalid = JsonAppSettingsStore.ParseLocalAiSettings("{ definitely-not-json }");

        Assert.Equal(LocalAiSettings.Default, fromEmpty);
        Assert.Equal(LocalAiSettings.Default, fromInvalid);
    }

    [Fact]
    public void ParseLocalAiSettings_NormalizesConfiguredValues()
    {
        var json =
            """
            {
              "localAi": {
                "enabled": true,
                "endpoint": "  http://localhost:11434/v1  ",
                "model": "  llama3.2:3b  "
              }
            }
            """;

        var settings = JsonAppSettingsStore.ParseLocalAiSettings(json);

        Assert.True(settings.Enabled);
        Assert.Equal("http://localhost:11434/v1", settings.Endpoint);
        Assert.Equal("llama3.2:3b", settings.Model);
        Assert.True(settings.IsEnabledAndConfigured);
    }

    [Fact]
    public void ParsePrivacySettings_ReturnsDefault_WhenJsonIsMissingOrInvalid()
    {
        var fromEmpty = JsonAppSettingsStore.ParsePrivacySettings(null);
        var fromInvalid = JsonAppSettingsStore.ParsePrivacySettings("{ definitely-not-json }");

        Assert.Equal(PrivacySettings.Default.RespectClipboardViewerIgnore, fromEmpty.RespectClipboardViewerIgnore);
        Assert.Equal(PrivacySettings.Default.IgnoreSensitiveContent, fromEmpty.IgnoreSensitiveContent);
        Assert.Empty(fromEmpty.ExcludedApps ?? Array.Empty<string>());

        Assert.Equal(PrivacySettings.Default.RespectClipboardViewerIgnore, fromInvalid.RespectClipboardViewerIgnore);
        Assert.Equal(PrivacySettings.Default.IgnoreSensitiveContent, fromInvalid.IgnoreSensitiveContent);
        Assert.Empty(fromInvalid.ExcludedApps ?? Array.Empty<string>());
    }

    [Fact]
    public void ParsePrivacySettings_NormalizesExcludedApps()
    {
        var json =
            """
            {
              "privacy": {
                "respectClipboardViewerIgnore": true,
                "ignoreSensitiveContent": false,
                "excludedApps": ["  notepad  ", "KeePass", "NOTEPAD", ""]
              }
            }
            """;

        var privacy = JsonAppSettingsStore.ParsePrivacySettings(json);

        Assert.True(privacy.RespectClipboardViewerIgnore);
        Assert.False(privacy.IgnoreSensitiveContent);
        Assert.Equal(new[] { "KeePass", "notepad" }, privacy.ExcludedApps);
    }

    [Fact]
    public void SaveHotkey_PreservesExistingLocalAiAndPrivacySettings()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "clip-scribe-tests", Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(tempDirectory, "config.json");

        Directory.CreateDirectory(tempDirectory);
        File.WriteAllText(
            settingsPath,
            """
            {
              "hotkey": {
                "ctrl": true,
                "shift": true,
                "alt": false,
                "win": false,
                "key": "v"
              },
              "localAi": {
                "enabled": true,
                "endpoint": "http://localhost:11434",
                "model": "llama3.2:3b"
              },
              "privacy": {
                "respectClipboardViewerIgnore": false,
                "ignoreSensitiveContent": false,
                "excludedApps": ["notepad", "keepass"]
              }
            }
            """);

        try
        {
            var store = new JsonAppSettingsStore(settingsPath);
            store.SaveHotkey(new GlobalHotkeySettings(Ctrl: true, Shift: false, Alt: true, Win: false, Key: "f12"));

            var localAi = store.LoadLocalAiSettings();
            Assert.True(localAi.Enabled);
            Assert.Equal("http://localhost:11434", localAi.Endpoint);
            Assert.Equal("llama3.2:3b", localAi.Model);

            var privacy = store.LoadPrivacySettings();
            Assert.False(privacy.RespectClipboardViewerIgnore);
            Assert.False(privacy.IgnoreSensitiveContent);
            Assert.Equal(new[] { "keepass", "notepad" }, privacy.ExcludedApps);

            var hotkey = store.LoadHotkey();
            Assert.True(hotkey.Ctrl);
            Assert.False(hotkey.Shift);
            Assert.True(hotkey.Alt);
            Assert.Equal("F12", hotkey.Key);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void EnsureExists_WritesDefaultConfig_WithHotkeyLocalAiAndPrivacyDefaults()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "clip-scribe-tests", Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(tempDirectory, "config.json");

        try
        {
            var store = new JsonAppSettingsStore(settingsPath);
            store.EnsureExists();

            Assert.True(File.Exists(settingsPath));
            Assert.Equal(GlobalHotkeySettings.Default, store.LoadHotkey());
            Assert.Equal(LocalAiSettings.Default, store.LoadLocalAiSettings());

            var privacy = store.LoadPrivacySettings();
            Assert.Equal(PrivacySettings.Default.RespectClipboardViewerIgnore, privacy.RespectClipboardViewerIgnore);
            Assert.Equal(PrivacySettings.Default.IgnoreSensitiveContent, privacy.IgnoreSensitiveContent);
            Assert.Empty(privacy.ExcludedApps ?? Array.Empty<string>());
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }
}
