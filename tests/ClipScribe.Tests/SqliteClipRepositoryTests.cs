using ClipScribe.Core.Models;
using ClipScribe.Core.Utilities;
using ClipScribe.Infrastructure.Sqlite;

namespace ClipScribe.Tests;

public sealed class SqliteClipRepositoryTests
{
    [Fact]
    public async Task SaveAsync_DedupesConsecutiveIdenticalClips()
    {
        var dbPath = CreateDatabasePath();
        var repo = new SqliteClipRepository(dbPath);
        var options = new CaptureOptions(MaxHistoryItems: 100, Retention: null);

        var first = await repo.SaveAsync(MakeClip("hello", DateTimeOffset.UtcNow), options);
        var second = await repo.SaveAsync(MakeClip("hello", DateTimeOffset.UtcNow.AddSeconds(1)), options);

        Assert.True(first.InsertedNewRow);
        Assert.False(second.InsertedNewRow);

        var count = await repo.GetCountAsync();
        Assert.Equal(1, count);

        var row = (await repo.GetRecentAsync(1)).Single();
        Assert.Equal(2, row.CopyCount);
        Assert.Equal("hello", row.Content);
    }

    [Fact]
    public async Task SaveAsync_PersistsAcrossRepositoryInstances()
    {
        var dbPath = CreateDatabasePath();
        var options = new CaptureOptions(MaxHistoryItems: 100, Retention: null);

        var repo1 = new SqliteClipRepository(dbPath);
        await repo1.SaveAsync(MakeClip("persist me", DateTimeOffset.UtcNow), options);

        var repo2 = new SqliteClipRepository(dbPath);
        var count = await repo2.GetCountAsync();
        var rows = await repo2.GetRecentAsync(10);

        Assert.Equal(1, count);
        Assert.Equal("persist me", rows.Single().Content);
    }

    [Fact]
    public async Task SaveAsync_PrunesByMaxHistory()
    {
        var dbPath = CreateDatabasePath();
        var repo = new SqliteClipRepository(dbPath);
        var options = new CaptureOptions(MaxHistoryItems: 3, Retention: null);

        for (var i = 1; i <= 5; i++)
        {
            await repo.SaveAsync(MakeClip($"clip-{i}", DateTimeOffset.UtcNow.AddSeconds(i)), options);
        }

        Assert.Equal(3, await repo.GetCountAsync());
        var remaining = await repo.GetRecentAsync(10);

        Assert.Equal(new[] { "clip-5", "clip-4", "clip-3" }, remaining.Select(x => x.Content));
    }

    [Fact]
    public async Task SaveAsync_PrunesByRetention()
    {
        var dbPath = CreateDatabasePath();
        var repo = new SqliteClipRepository(dbPath);
        var options = new CaptureOptions(MaxHistoryItems: 100, Retention: TimeSpan.FromDays(2));

        await repo.SaveAsync(MakeClip("too-old", DateTimeOffset.UtcNow.AddDays(-5)), options);
        await repo.SaveAsync(MakeClip("recent", DateTimeOffset.UtcNow), options);

        var rows = await repo.GetRecentAsync(10);

        Assert.Single(rows);
        Assert.Equal("recent", rows[0].Content);
    }

    private static NewClip MakeClip(string text, DateTimeOffset createdAt)
        => new(
            Content: text,
            ContentHash: TextHasher.Sha256(text),
            ContentType: ClipContentType.Text,
            CreatedAtUtc: createdAt,
            SourceApp: "test",
            IsPinned: false);

    private static string CreateDatabasePath()
    {
        var root = Path.Combine(Path.GetTempPath(), "clip-scribe-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return Path.Combine(root, "history.db");
    }
}
