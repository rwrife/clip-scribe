using ClipScribe.Core.Models;
using ClipScribe.Core.Utilities;
using ClipScribe.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
using System.Diagnostics;

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
    public async Task SaveAsync_DoesNotPrunePinnedClips()
    {
        var dbPath = CreateDatabasePath();
        var repo = new SqliteClipRepository(dbPath);

        await repo.SaveAsync(MakeClip("pinned", DateTimeOffset.UtcNow.AddSeconds(1), isPinned: true), new CaptureOptions(MaxHistoryItems: 2, Retention: null));
        await repo.SaveAsync(MakeClip("clip-1", DateTimeOffset.UtcNow.AddSeconds(2)), new CaptureOptions(MaxHistoryItems: 2, Retention: null));
        await repo.SaveAsync(MakeClip("clip-2", DateTimeOffset.UtcNow.AddSeconds(3)), new CaptureOptions(MaxHistoryItems: 2, Retention: null));
        await repo.SaveAsync(MakeClip("clip-3", DateTimeOffset.UtcNow.AddSeconds(4)), new CaptureOptions(MaxHistoryItems: 2, Retention: null));

        var results = await repo.SearchAsync(null, take: 20, prioritizePinned: true);

        Assert.Contains(results, x => x.Content == "pinned" && x.IsPinned);
        Assert.DoesNotContain(results, x => x.Content == "clip-1");
        Assert.Contains(results, x => x.Content == "clip-2");
        Assert.Contains(results, x => x.Content == "clip-3");
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

    [Fact]
    public async Task SnippetCrud_PersistsAndIsNeverPruned()
    {
        var dbPath = CreateDatabasePath();
        var repo = new SqliteClipRepository(dbPath);

        var snippetId = await repo.CreateSnippetAsync("Build command", "dotnet test");

        await repo.SaveAsync(MakeClip("clip-1", DateTimeOffset.UtcNow.AddSeconds(1)), new CaptureOptions(MaxHistoryItems: 1, Retention: TimeSpan.FromMilliseconds(1)));
        await Task.Delay(3);
        await repo.SaveAsync(MakeClip("clip-2", DateTimeOffset.UtcNow.AddSeconds(2)), new CaptureOptions(MaxHistoryItems: 1, Retention: TimeSpan.FromMilliseconds(1)));

        var rowsAfterPrune = await repo.SearchAsync(null, take: 20, prioritizePinned: true);
        var snippet = Assert.Single(rowsAfterPrune.Where(x => x.Id == snippetId));
        Assert.True(snippet.IsSnippet);
        Assert.True(snippet.IsPinned);
        Assert.Equal("Build command", snippet.SnippetName);

        await repo.UpdateSnippetAsync(snippetId, "Updated command", "dotnet build");

        var updated = Assert.Single((await repo.SearchAsync("Updated command", take: 20, prioritizePinned: true)).Where(x => x.Id == snippetId));
        Assert.Equal("dotnet build", updated.Content);
        Assert.Equal("Updated command", updated.SnippetName);

        await repo.DeleteClipAsync(snippetId);
        Assert.DoesNotContain(await repo.SearchAsync(null, take: 20, prioritizePinned: true), x => x.Id == snippetId);
    }

    [Fact]
    public async Task SetPinnedAsync_TogglesPinStateForRegularClips()
    {
        var dbPath = CreateDatabasePath();
        var repo = new SqliteClipRepository(dbPath);
        var options = new CaptureOptions(MaxHistoryItems: 100, Retention: null);

        var saved = await repo.SaveAsync(MakeClip("toggle-pin", DateTimeOffset.UtcNow), options);

        await repo.SetPinnedAsync(saved.ClipId, isPinned: true);
        var pinned = Assert.Single(await repo.SearchAsync("toggle-pin", take: 5, prioritizePinned: true));
        Assert.True(pinned.IsPinned);

        await repo.SetPinnedAsync(saved.ClipId, isPinned: false);
        var unpinned = Assert.Single(await repo.SearchAsync("toggle-pin", take: 5, prioritizePinned: true));
        Assert.False(unpinned.IsPinned);
    }

    [Fact]
    public async Task ClearAsync_RemovesAllClips()
    {
        var dbPath = CreateDatabasePath();
        var repo = new SqliteClipRepository(dbPath);
        var options = new CaptureOptions(MaxHistoryItems: 100, Retention: null);

        await repo.SaveAsync(MakeClip("one", DateTimeOffset.UtcNow), options);
        await repo.SaveAsync(MakeClip("two", DateTimeOffset.UtcNow.AddSeconds(1)), options);

        await repo.ClearAsync();

        Assert.Equal(0, await repo.GetCountAsync());
        Assert.Empty(await repo.GetRecentAsync(10));
    }

    [Fact]
    public async Task SearchAsync_ReturnsSubstringMatchesRanked()
    {
        var dbPath = CreateDatabasePath();
        var repo = new SqliteClipRepository(dbPath);
        var options = new CaptureOptions(MaxHistoryItems: 100, Retention: null);
        var baseline = DateTimeOffset.UtcNow;

        await repo.SaveAsync(MakeClip("prefix match example", baseline.AddSeconds(1)), options);
        await repo.SaveAsync(MakeClip("something in the middle match token", baseline.AddSeconds(2)), options);
        await repo.SaveAsync(MakeClip("match", baseline.AddSeconds(3)), options);

        var results = await repo.SearchAsync("match", take: 10);

        Assert.Equal(3, results.Count);
        Assert.Equal("match", results[0].Content);
        Assert.Contains(results, r => r.Content == "prefix match example");
        Assert.Contains(results, r => r.Content == "something in the middle match token");
    }

    [Fact]
    public async Task SearchAsync_EmptyQueryReturnsMostRecent()
    {
        var dbPath = CreateDatabasePath();
        var repo = new SqliteClipRepository(dbPath);
        var options = new CaptureOptions(MaxHistoryItems: 100, Retention: null);
        var baseline = DateTimeOffset.UtcNow;

        await repo.SaveAsync(MakeClip("older", baseline.AddSeconds(1)), options);
        await repo.SaveAsync(MakeClip("newer", baseline.AddSeconds(2)), options);

        var results = await repo.SearchAsync("   ", take: 10);

        Assert.Equal(new[] { "newer", "older" }, results.Select(x => x.Content));
    }

    [Fact]
    public async Task SearchAsync_ShortQueryUsesFuzzyFallback()
    {
        var dbPath = CreateDatabasePath();
        var repo = new SqliteClipRepository(dbPath);
        var options = new CaptureOptions(MaxHistoryItems: 100, Retention: null);

        await repo.SaveAsync(MakeClip("clipboard", DateTimeOffset.UtcNow), options);

        var results = await repo.SearchAsync("clp", take: 5);

        Assert.Contains(results, r => r.Content == "clipboard");
    }

    [Fact]
    public async Task SearchAsync_IndexStaysInSyncAfterDeleteAndPrune()
    {
        var dbPath = CreateDatabasePath();
        var repo = new SqliteClipRepository(dbPath);
        var baseline = DateTimeOffset.UtcNow;

        await repo.SaveAsync(MakeClip("keep me", baseline.AddSeconds(1)), new CaptureOptions(MaxHistoryItems: 10, Retention: null));
        await repo.SaveAsync(MakeClip("delete me", baseline.AddSeconds(2)), new CaptureOptions(MaxHistoryItems: 10, Retention: null));

        await DeleteClipByContentAsync(dbPath, "delete me");

        var deletedResults = await repo.SearchAsync("delete", take: 10);
        Assert.DoesNotContain(deletedResults, r => r.Content == "delete me");

        await repo.SaveAsync(MakeClip("newer 1", baseline.AddSeconds(3)), new CaptureOptions(MaxHistoryItems: 2, Retention: null));
        await repo.SaveAsync(MakeClip("newer 2", baseline.AddSeconds(4)), new CaptureOptions(MaxHistoryItems: 2, Retention: null));

        var prunedResults = await repo.SearchAsync("keep", take: 10);
        Assert.DoesNotContain(prunedResults, r => r.Content == "keep me");
    }

    [Fact]
    public async Task SearchAsync_TenThousandClipsReturnsQuickly()
    {
        var dbPath = CreateDatabasePath();
        var repo = new SqliteClipRepository(dbPath);
        await repo.InitializeAsync();

        await SeedDatabaseForPerfAsync(dbPath, clipCount: 10_000);

        var sw = Stopwatch.StartNew();
        var results = await repo.SearchAsync("needle-token", take: 20);
        sw.Stop();

        Assert.NotEmpty(results);
        Assert.True(sw.ElapsedMilliseconds < 400, $"Expected search under 400ms, got {sw.ElapsedMilliseconds}ms");
    }

    private static async Task DeleteClipByContentAsync(string dbPath, string content)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        await using var connection = new SqliteConnection(cs);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM clips WHERE content = $content;";
        cmd.Parameters.AddWithValue("$content", content);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SeedDatabaseForPerfAsync(string dbPath, int clipCount)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        await using var connection = new SqliteConnection(cs);
        await connection.OpenAsync();
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync();

        var insert = connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText =
            """
            INSERT INTO clips(content, content_hash, content_type, created_at_utc, source_app, is_pinned, copy_count)
            VALUES ($content, $content_hash, $content_type, $created_at, $source_app, $is_pinned, 1);
            """;

        var contentParam = insert.CreateParameter();
        contentParam.ParameterName = "$content";
        insert.Parameters.Add(contentParam);

        var hashParam = insert.CreateParameter();
        hashParam.ParameterName = "$content_hash";
        insert.Parameters.Add(hashParam);

        var typeParam = insert.CreateParameter();
        typeParam.ParameterName = "$content_type";
        typeParam.Value = (int)ClipContentType.Text;
        insert.Parameters.Add(typeParam);

        var createdAtParam = insert.CreateParameter();
        createdAtParam.ParameterName = "$created_at";
        insert.Parameters.Add(createdAtParam);

        var sourceAppParam = insert.CreateParameter();
        sourceAppParam.ParameterName = "$source_app";
        sourceAppParam.Value = "perf-test";
        insert.Parameters.Add(sourceAppParam);

        var pinnedParam = insert.CreateParameter();
        pinnedParam.ParameterName = "$is_pinned";
        insert.Parameters.Add(pinnedParam);

        var baseline = DateTimeOffset.UtcNow.AddMinutes(-10);

        for (var i = 0; i < clipCount; i++)
        {
            var content = i % 250 == 0
                ? $"needle-token payload {i}"
                : $"random clipboard text payload {i}";

            contentParam.Value = content;
            hashParam.Value = TextHasher.Sha256(content);
            createdAtParam.Value = baseline.AddMilliseconds(i).ToUnixTimeMilliseconds();
            pinnedParam.Value = i % 997 == 0 ? 1 : 0;

            await insert.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    private static NewClip MakeClip(string text, DateTimeOffset createdAt, bool isPinned = false)
        => new(
            Content: text,
            ContentHash: TextHasher.Sha256(text),
            ContentType: ClipContentType.Text,
            CreatedAtUtc: createdAt,
            SourceApp: "test",
            IsPinned: isPinned);

    private static string CreateDatabasePath()
    {
        var root = Path.Combine(Path.GetTempPath(), "clip-scribe-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return Path.Combine(root, "history.db");
    }
}
