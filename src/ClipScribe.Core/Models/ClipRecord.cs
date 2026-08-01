namespace ClipScribe.Core.Models;

public sealed record ClipRecord(
    long Id,
    string Content,
    string ContentHash,
    ClipContentType ContentType,
    DateTimeOffset CreatedAtUtc,
    string? SourceApp,
    bool IsPinned,
    int CopyCount);
