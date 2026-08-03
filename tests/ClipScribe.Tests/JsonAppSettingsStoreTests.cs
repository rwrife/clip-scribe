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
    public void EnsureExists_WritesDefaultConfig_AndLoadHotkeyReturnsDefault()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "clip-scribe-tests", Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(tempDirectory, "config.json");

        try
        {
            var store = new JsonAppSettingsStore(settingsPath);
            store.EnsureExists();

            Assert.True(File.Exists(settingsPath));

            var hotkey = store.LoadHotkey();
            Assert.Equal(GlobalHotkeySettings.Default, hotkey);
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
