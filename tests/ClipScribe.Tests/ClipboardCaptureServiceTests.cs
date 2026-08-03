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

    private sealed class StubClipboardReader(string? value) : IClipboardTextReader
    {
        public bool TryReadText(out string? text)
        {
            text = value;
            return value is not null;
        }
    }

    private sealed class StubForegroundWindowInfoProvider(string? processName) : IForegroundWindowInfoProvider
    {
        public string? TryGetForegroundProcessName() => processName;
    }
}
