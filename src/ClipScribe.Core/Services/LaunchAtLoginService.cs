using ClipScribe.Core.Abstractions;

namespace ClipScribe.Core.Services;

public sealed class LaunchAtLoginService
{
    private readonly string _appName;
    private readonly string _launchCommand;
    private readonly ILaunchAtLoginStore _store;

    public LaunchAtLoginService(string appName, string launchCommand, ILaunchAtLoginStore store)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appName);
        ArgumentException.ThrowIfNullOrWhiteSpace(launchCommand);

        _appName = appName;
        _launchCommand = launchCommand;
        _store = store;
    }

    public bool IsEnabled()
    {
        var existing = _store.GetValue(_appName);
        return string.Equals(existing, _launchCommand, StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            _store.SetValue(_appName, _launchCommand);
            return;
        }

        _store.RemoveValue(_appName);
    }
}
