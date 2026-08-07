using ClipScribe.Core.Abstractions;
using ClipScribe.Core.Models;
using ClipScribe.Core.Services;

namespace ClipScribe.Tests;

public sealed class ClipboardCaptureServiceTests
{
    [Fact]
    public async Task CaptureCurrentClipboardAsync_SavesTextClipWithSourceApp()
    {
        var repo = new RecordingClipRepository();
        var reader = new StubClipboardReader("  hello world  ");
        var source = new StubForegroundWindowInfoProvider("notepad");

        var sut = new ClipboardCaptureService(
            repo,
            reader,
            source,
            new CaptureOptions(MaxHistoryItems: 100, Retention: null));

        var result = await sut.CaptureCurrentClipboardAsync();

        Assert.NotNull(result);
        Assert.NotNull(repo.LastSaved);
        Assert.Equal("hello world", repo.LastSaved!.Content);
        Assert.Equal("notepad", repo.LastSaved.SourceApp);
        Assert.Equal(ClipContentType.Text, repo.LastSaved.ContentType);
    }

    [Fact]
    public async Task CaptureCurrentClipboardAsync_DoesNothingForEmptyClipboard()
    {
        var repo = new RecordingClipRepository();
        var reader = new StubClipboardReader("   ");
        var source = new StubForegroundWindowInfoProvider("ignored");

        var sut = new ClipboardCaptureService(
            repo,
            reader,
            source,
            new CaptureOptions(MaxHistoryItems: 100, Retention: null));

        var result = await sut.CaptureCurrentClipboardAsync();

        Assert.Null(result);
        Assert.Null(repo.LastSaved);
    }

    [Fact]
    public async Task CaptureCurrentClipboardAsync_DoesNothingWhenClipboardViewerIgnoreFormatIsPresent()
    {
        var repo = new RecordingClipRepository();
        var reader = new StubClipboardReader("should not save", shouldIgnoreCurrentClipboard: true);
        var source = new StubForegroundWindowInfoProvider("notepad");

        var sut = new ClipboardCaptureService(
            repo,
            reader,
            source,
            new CaptureOptions(MaxHistoryItems: 100, Retention: null),
            () => PrivacySettings.Default);

        var result = await sut.CaptureCurrentClipboardAsync();

        Assert.Null(result);
        Assert.Null(repo.LastSaved);
    }

    [Fact]
    public async Task CaptureCurrentClipboardAsync_DoesNothingWhenSourceAppIsExcluded()
    {
        var repo = new RecordingClipRepository();
        var reader = new StubClipboardReader("copy text");
        var source = new StubForegroundWindowInfoProvider("Notepad");

        var sut = new ClipboardCaptureService(
            repo,
            reader,
            source,
            new CaptureOptions(MaxHistoryItems: 100, Retention: null),
            () => new PrivacySettings(
                RespectClipboardViewerIgnore: true,
                IgnoreSensitiveContent: true,
                ExcludedApps: new[] { "notepad", "KeePass" }));

        var result = await sut.CaptureCurrentClipboardAsync();

        Assert.Null(result);
        Assert.Null(repo.LastSaved);
    }

    [Fact]
    public async Task CaptureCurrentClipboardAsync_DoesNothingForSensitiveContent_WhenHeuristicEnabled()
    {
        var repo = new RecordingClipRepository();
        var reader = new StubClipboardReader("ghp_qwertyuiopasdfghjklzxcvbnm1234567890");
        var source = new StubForegroundWindowInfoProvider("powershell");

        var sut = new ClipboardCaptureService(
            repo,
            reader,
            source,
            new CaptureOptions(MaxHistoryItems: 100, Retention: null),
            () => new PrivacySettings(
                RespectClipboardViewerIgnore: true,
                IgnoreSensitiveContent: true,
                ExcludedApps: Array.Empty<string>()));

        var result = await sut.CaptureCurrentClipboardAsync();

        Assert.Null(result);
        Assert.Null(repo.LastSaved);
    }

    [Fact]
    public async Task CaptureCurrentClipboardAsync_SavesSensitiveLikeContent_WhenHeuristicDisabled()
    {
        var repo = new RecordingClipRepository();
        var reader = new StubClipboardReader("ghp_qwertyuiopasdfghjklzxcvbnm1234567890");
        var source = new StubForegroundWindowInfoProvider("powershell");

        var sut = new ClipboardCaptureService(
            repo,
            reader,
            source,
            new CaptureOptions(MaxHistoryItems: 100, Retention: null),
            () => new PrivacySettings(
                RespectClipboardViewerIgnore: true,
                IgnoreSensitiveContent: false,
                ExcludedApps: Array.Empty<string>()));

        var result = await sut.CaptureCurrentClipboardAsync();

        Assert.NotNull(result);
        Assert.NotNull(repo.LastSaved);
    }

    private sealed class RecordingClipRepository : IClipRepository
    {
        public NewClip? LastSaved { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<ClipSaveResult> SaveAsync(NewClip clip, CaptureOptions options, CancellationToken cancellationToken = default)
        {
            LastSaved = clip;
            return Task.FromResult(new ClipSaveResult(1, true));
        }

        public Task<long> CreateSnippetAsync(string name, string content, CancellationToken cancellationToken = default)
            => Task.FromResult(1L);

        public Task UpdateSnippetAsync(long clipId, string name, string content, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetPinnedAsync(long clipId, bool isPinned, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteClipAsync(long clipId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            LastSaved = null;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ClipRecord>> GetRecentAsync(int take, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ClipRecord>>(Array.Empty<ClipRecord>());

        public Task<IReadOnlyList<ClipRecord>> SearchAsync(
            string? query,
            int take,
            bool prioritizePinned = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ClipRecord>>(Array.Empty<ClipRecord>());

        public Task<int> GetCountAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(LastSaved is null ? 0 : 1);
    }

    private sealed class StubClipboardReader(string? value, bool shouldIgnoreCurrentClipboard = false) : IClipboardTextReader
    {
        public bool TryReadText(out string? text)
        {
            text = value;
            return value is not null;
        }

        public bool ShouldIgnoreCurrentClipboard() => shouldIgnoreCurrentClipboard;
    }

    private sealed class StubForegroundWindowInfoProvider(string? processName) : IForegroundWindowInfoProvider
    {
        public string? TryGetForegroundProcessName() => processName;
    }
}
