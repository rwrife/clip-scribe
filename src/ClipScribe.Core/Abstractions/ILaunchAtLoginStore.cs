namespace ClipScribe.Core.Abstractions;

public interface ILaunchAtLoginStore
{
    string? GetValue(string appName);

    void SetValue(string appName, string command);

    void RemoveValue(string appName);
}
