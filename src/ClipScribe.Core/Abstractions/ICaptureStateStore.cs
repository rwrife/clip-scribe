using ClipScribe.Core.Models;

namespace ClipScribe.Core.Abstractions;

public interface ICaptureStateStore
{
    Task<CaptureRuntimeState> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(CaptureRuntimeState state, CancellationToken cancellationToken = default);
}
