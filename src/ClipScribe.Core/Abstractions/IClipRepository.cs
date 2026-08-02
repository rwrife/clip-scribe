using ClipScribe.Core.Models;

namespace ClipScribe.Core.Abstractions;

public interface IClipRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<ClipSaveResult> SaveAsync(NewClip clip, CaptureOptions options, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClipRecord>> GetRecentAsync(int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClipRecord>> SearchAsync(
        string? query,
        int take,
        bool prioritizePinned = false,
        CancellationToken cancellationToken = default);

    Task<int> GetCountAsync(CancellationToken cancellationToken = default);
}
