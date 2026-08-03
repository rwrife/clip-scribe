using ClipScribe.Core.Abstractions;
using ClipScribe.Core.Services;

namespace ClipScribe.Tests;

public sealed class LaunchAtLoginServiceTests
{
    [Fact]
    public void SetEnabled_True_WritesStartupEntry()
    {
        var store = new InMemoryLaunchAtLoginStore();
        var sut = new LaunchAtLoginService("clip-scribe", "\"C:\\clip-scribe\\clip-scribe.exe\"", store);

        sut.SetEnabled(true);

        Assert.True(sut.IsEnabled());
        Assert.Equal("\"C:\\clip-scribe\\clip-scribe.exe\"", store.GetValue("clip-scribe"));
    }

    [Fact]
    public void SetEnabled_False_RemovesStartupEntry()
    {
        var store = new InMemoryLaunchAtLoginStore();
        var sut = new LaunchAtLoginService("clip-scribe", "\"C:\\clip-scribe\\clip-scribe.exe\"", store);

        sut.SetEnabled(true);
        sut.SetEnabled(false);

        Assert.False(sut.IsEnabled());
        Assert.Null(store.GetValue("clip-scribe"));
    }

    private sealed class InMemoryLaunchAtLoginStore : ILaunchAtLoginStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

        public string? GetValue(string appName)
            => _values.TryGetValue(appName, out var value) ? value : null;

        public void SetValue(string appName, string command)
            => _values[appName] = command;

        public void RemoveValue(string appName)
            => _values.Remove(appName);
    }
}
