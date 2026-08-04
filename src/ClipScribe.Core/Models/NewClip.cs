namespace ClipScribe.Core.Models;

public sealed record NewClip(
    string Content,
    string ContentHash,
    ClipContentType ContentType,
    DateTimeOffset CreatedAtUtc,
    string? SourceApp,
    bool IsPinned = false,
    bool IsSnippet = false,
    string? SnippetName = null);
