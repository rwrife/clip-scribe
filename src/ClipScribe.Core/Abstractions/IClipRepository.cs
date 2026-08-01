using ClipScribe.Core.Models;

namespace ClipScribe.Core.Abstractions;

public interface IClipRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<ClipSaveResult> SaveAsync(NewClip clip, CaptureOptions options, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClipRecord>> GetRecentAsync(int take, CancellationToken cancellationToken = default);

    Task<int> GetCountAsync(CancellationToken cancellationToken = default);
}
