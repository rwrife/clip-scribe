namespace ClipScribe.Core.Abstractions;

public interface IForegroundWindowInfoProvider
{
    string? TryGetForegroundProcessName();
}
