using ClipScribe.Core.Abstractions;
using ClipScribe.Core.Models;
using Microsoft.Data.Sqlite;

namespace ClipScribe.Infrastructure.Sqlite;

public sealed class SqliteClipRepository : IClipRepository
{
    private readonly string _databasePath;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initGate = new(1, 1);

    private bool _initialized;

    public SqliteClipRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = databasePath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            var parent = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await ExecuteNonQueryAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken);
            await ExecuteNonQueryAsync(connection, "PRAGMA synchronous=NORMAL;", cancellationToken);

            var schema = """
                CREATE TABLE IF NOT EXISTS clips (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    content TEXT NOT NULL,
                    content_hash TEXT NOT NULL,
                    content_type INTEGER NOT NULL,
                    created_at_utc INTEGER NOT NULL,
                    source_app TEXT NULL,
                    is_pinned INTEGER NOT NULL DEFAULT 0,
                    copy_count INTEGER NOT NULL DEFAULT 1
                );

                CREATE INDEX IF NOT EXISTS idx_clips_created_at_utc ON clips(created_at_utc DESC);
                CREATE INDEX IF NOT EXISTS idx_clips_hash ON clips(content_hash);

                CREATE VIRTUAL TABLE IF NOT EXISTS clips_fts USING fts5(
                    content,
                    content='clips',
                    content_rowid='id'
                );

                CREATE TRIGGER IF NOT EXISTS clips_ai AFTER INSERT ON clips BEGIN
                    INSERT INTO clips_fts(rowid, content) VALUES (new.id, new.content);
                END;

                CREATE TRIGGER IF NOT EXISTS clips_ad AFTER DELETE ON clips BEGIN
                    INSERT INTO clips_fts(clips_fts, rowid, content) VALUES('delete', old.id, old.content);
                END;

                CREATE TRIGGER IF NOT EXISTS clips_au AFTER UPDATE ON clips BEGIN
                    INSERT INTO clips_fts(clips_fts, rowid, content) VALUES('delete', old.id, old.content);
                    INSERT INTO clips_fts(rowid, content) VALUES (new.id, new.content);
                END;
                """;

            await ExecuteNonQueryAsync(connection, schema, cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initGate.Release();
        }
    }

    public async Task<ClipSaveResult> SaveAsync(
        NewClip clip,
        CaptureOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        await InitializeAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var latest = await GetLatestClipMetadataAsync(connection, transaction, cancellationToken);
        var timestamp = clip.CreatedAtUtc.ToUnixTimeMilliseconds();

        long clipId;
        bool inserted;

        if (latest is not null && latest.Value.ContentHash == clip.ContentHash)
        {
            clipId = latest.Value.Id;
            inserted = false;

            var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText =
                """
                UPDATE clips
                SET created_at_utc = $created_at,
                    source_app = $source_app,
                    copy_count = copy_count + 1
                WHERE id = $id;
                """;
            update.Parameters.AddWithValue("$created_at", timestamp);
            update.Parameters.AddWithValue("$source_app", (object?)clip.SourceApp ?? DBNull.Value);
            update.Parameters.AddWithValue("$id", clipId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            inserted = true;
            var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO clips(content, content_hash, content_type, created_at_utc, source_app, is_pinned, copy_count)
                VALUES ($content, $content_hash, $content_type, $created_at, $source_app, $is_pinned, 1);
                SELECT last_insert_rowid();
                """;
            insert.Parameters.AddWithValue("$content", clip.Content);
            insert.Parameters.AddWithValue("$content_hash", clip.ContentHash);
            insert.Parameters.AddWithValue("$content_type", (int)clip.ContentType);
            insert.Parameters.AddWithValue("$created_at", timestamp);
            insert.Parameters.AddWithValue("$source_app", (object?)clip.SourceApp ?? DBNull.Value);
            insert.Parameters.AddWithValue("$is_pinned", clip.IsPinned ? 1 : 0);

            clipId = (long)(await insert.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("Failed to insert clip."));
        }

        await ApplyPruningAsync(connection, transaction, options, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ClipSaveResult(clipId, inserted);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM clips;";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ClipRecord>> GetRecentAsync(int take, CancellationToken cancellationToken = default)
    {
        if (take <= 0)
        {
            return Array.Empty<ClipRecord>();
        }

        await InitializeAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await QueryRecentAsync(connection, take, prioritizePinned: false, cancellationToken);
    }

    public async Task<IReadOnlyList<ClipRecord>> SearchAsync(
        string? query,
        int take,
        bool prioritizePinned = false,
        CancellationToken cancellationToken = default)
    {
        if (take <= 0)
        {
            return Array.Empty<ClipRecord>();
        }

        await InitializeAsync(cancellationToken);

        var normalizedQuery = query?.Trim() ?? string.Empty;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return await QueryRecentAsync(connection, take, prioritizePinned, cancellationToken);
        }

        var loweredQuery = normalizedQuery.ToLowerInvariant();
        var containsPattern = $"%{EscapeLike(loweredQuery)}%";
        var prefixPattern = $"{EscapeLike(loweredQuery)}%";
        var ftsQuery = BuildFtsPrefixQuery(normalizedQuery);

        var cmd = connection.CreateCommand();
        cmd.CommandText =
            string.IsNullOrWhiteSpace(ftsQuery)
                ? """
                  SELECT c.id, c.content, c.content_hash, c.content_type, c.created_at_utc, c.source_app, c.is_pinned, c.copy_count
                  FROM clips c
                  WHERE lower(c.content) LIKE $contains ESCAPE '\'
                  ORDER BY
                      CASE WHEN $prioritize_pinned = 1 THEN c.is_pinned ELSE 0 END DESC,
                      CASE
                          WHEN lower(c.content) = $exact THEN 4
                          WHEN lower(c.content) LIKE $prefix ESCAPE '\' THEN 3
                          ELSE 1
                      END DESC,
                      c.created_at_utc DESC,
                      c.id DESC
                  LIMIT $take;
                  """
                : """
                  WITH fts_matches AS (
                      SELECT c.id AS id, bm25(clips_fts) AS fts_rank
                      FROM clips_fts
                      JOIN clips c ON c.id = clips_fts.rowid
                      WHERE clips_fts MATCH $fts_query
                  )
                  SELECT c.id, c.content, c.content_hash, c.content_type, c.created_at_utc, c.source_app, c.is_pinned, c.copy_count,
                         fm.fts_rank
                  FROM clips c
                  LEFT JOIN fts_matches fm ON fm.id = c.id
                  WHERE fm.id IS NOT NULL
                     OR lower(c.content) LIKE $contains ESCAPE '\'
                  ORDER BY
                      CASE WHEN $prioritize_pinned = 1 THEN c.is_pinned ELSE 0 END DESC,
                      CASE
                          WHEN lower(c.content) = $exact THEN 5
                          WHEN lower(c.content) LIKE $prefix ESCAPE '\' THEN 4
                          WHEN fm.id IS NOT NULL THEN 3
                          ELSE 1
                      END DESC,
                      CASE WHEN fm.fts_rank IS NULL THEN 1 ELSE 0 END ASC,
                      fm.fts_rank ASC,
                      c.created_at_utc DESC,
                      c.id DESC
                  LIMIT $take;
                  """;

        cmd.Parameters.AddWithValue("$contains", containsPattern);
        cmd.Parameters.AddWithValue("$prefix", prefixPattern);
        cmd.Parameters.AddWithValue("$exact", loweredQuery);
        cmd.Parameters.AddWithValue("$take", take);
        cmd.Parameters.AddWithValue("$prioritize_pinned", prioritizePinned ? 1 : 0);

        if (!string.IsNullOrWhiteSpace(ftsQuery))
        {
            cmd.Parameters.AddWithValue("$fts_query", ftsQuery);
        }

        var results = new List<ClipRecord>(capacity: take);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(MapClipRecord(reader));
        }

        if (normalizedQuery.Length <= 3 && results.Count < take)
        {
            var existingIds = results.Select(x => x.Id).ToHashSet();
            var fallbackWindow = Math.Max(200, take * 20);
            var recent = await QueryRecentAsync(connection, fallbackWindow, prioritizePinned, cancellationToken);

            var fuzzyMatches = recent
                .Where(x => !existingIds.Contains(x.Id))
                .Select(x => new { Record = x, Score = ComputeFuzzyScore(loweredQuery, x.Content) })
                .Where(x => x.Score >= 0.55)
                .OrderByDescending(x => prioritizePinned && x.Record.IsPinned)
                .ThenByDescending(x => x.Score)
                .ThenByDescending(x => x.Record.CreatedAtUtc)
                .Take(take - results.Count)
                .Select(x => x.Record);

            results.AddRange(fuzzyMatches);
        }

        return results;
    }

    public async Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM clips;";
        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value);
    }

    private SqliteConnection CreateConnection() => new(_connectionString);

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<(long Id, string ContentHash)?> GetLatestClipMetadataAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText =
            """
            SELECT id, content_hash
            FROM clips
            ORDER BY created_at_utc DESC, id DESC
            LIMIT 1;
            """;

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return (reader.GetInt64(0), reader.GetString(1));
    }

    private static async Task<IReadOnlyList<ClipRecord>> QueryRecentAsync(
        SqliteConnection connection,
        int take,
        bool prioritizePinned,
        CancellationToken cancellationToken)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT id, content, content_hash, content_type, created_at_utc, source_app, is_pinned, copy_count
            FROM clips
            ORDER BY
                CASE WHEN $prioritize_pinned = 1 THEN is_pinned ELSE 0 END DESC,
                created_at_utc DESC,
                id DESC
            LIMIT $take;
            """;
        cmd.Parameters.AddWithValue("$take", take);
        cmd.Parameters.AddWithValue("$prioritize_pinned", prioritizePinned ? 1 : 0);

        var items = new List<ClipRecord>(capacity: take);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(MapClipRecord(reader));
        }

        return items;
    }

    private static ClipRecord MapClipRecord(SqliteDataReader reader)
        => new(
            Id: reader.GetInt64(0),
            Content: reader.GetString(1),
            ContentHash: reader.GetString(2),
            ContentType: (ClipContentType)reader.GetInt32(3),
            CreatedAtUtc: DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(4)),
            SourceApp: reader.IsDBNull(5) ? null : reader.GetString(5),
            IsPinned: reader.GetInt32(6) == 1,
            CopyCount: reader.GetInt32(7));

    private static string EscapeLike(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static string? BuildFtsPrefixQuery(string query)
    {
        var tokens = query
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => new string(token.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant())
            .Where(token => token.Length >= 2)
            .Distinct(StringComparer.Ordinal)
            .Take(6)
            .Select(token => $"\"{token}\"*")
            .ToArray();

        if (tokens.Length == 0)
        {
            return null;
        }

        return string.Join(" AND ", tokens);
    }

    private static double ComputeFuzzyScore(string query, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return 0;
        }

        var text = content.ToLowerInvariant();

        var containsIndex = text.IndexOf(query, StringComparison.Ordinal);
        if (containsIndex >= 0)
        {
            return 1.0 + (1d / (containsIndex + 1));
        }

        var queryIndex = 0;
        var bestContiguous = 0;
        var currentContiguous = 0;
        var matched = 0;

        for (var i = 0; i < text.Length && queryIndex < query.Length; i++)
        {
            if (text[i] == query[queryIndex])
            {
                matched++;
                currentContiguous++;
                bestContiguous = Math.Max(bestContiguous, currentContiguous);
                queryIndex++;
            }
            else
            {
                currentContiguous = 0;
            }
        }

        if (matched == 0)
        {
            return 0;
        }

        var coverage = (double)matched / query.Length;
        var contiguousBonus = (double)bestContiguous / query.Length;

        return (coverage * 0.7) + (contiguousBonus * 0.3);
    }

    private static async Task ApplyPruningAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CaptureOptions options,
        CancellationToken cancellationToken)
    {
        if (options.Retention is { } retention)
        {
            var retentionCutoff = DateTimeOffset.UtcNow.Subtract(retention).ToUnixTimeMilliseconds();
            var retentionDelete = connection.CreateCommand();
            retentionDelete.Transaction = transaction;
            retentionDelete.CommandText = "DELETE FROM clips WHERE created_at_utc < $cutoff;";
            retentionDelete.Parameters.AddWithValue("$cutoff", retentionCutoff);
            await retentionDelete.ExecuteNonQueryAsync(cancellationToken);
        }

        var sizeDelete = connection.CreateCommand();
        sizeDelete.Transaction = transaction;
        sizeDelete.CommandText =
            """
            DELETE FROM clips
            WHERE id IN (
                SELECT id FROM clips
                ORDER BY created_at_utc DESC, id DESC
                LIMIT -1 OFFSET $max_history
            );
            """;
        sizeDelete.Parameters.AddWithValue("$max_history", options.MaxHistoryItems);
        await sizeDelete.ExecuteNonQueryAsync(cancellationToken);
    }
}
