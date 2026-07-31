using ClipScribe.Core.Abstractions;

namespace ClipScribe.Core.Services;

public sealed class NoopForegroundWindowInfoProvider : IForegroundWindowInfoProvider
{
    public string? TryGetForegroundProcessName() => null;
}
