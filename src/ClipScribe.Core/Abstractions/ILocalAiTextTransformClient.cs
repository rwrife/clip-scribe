using ClipScribe.Core.Models;

namespace ClipScribe.Core.Abstractions;

public interface ILocalAiTextTransformClient
{
    Task<string> TransformAsync(
        LocalAiSettings settings,
        string transformInstruction,
        string input,
        CancellationToken cancellationToken = default);
}
