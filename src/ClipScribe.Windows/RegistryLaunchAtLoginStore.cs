using Microsoft.Win32;
using ClipScribe.Core.Abstractions;

namespace ClipScribe.Windows;

public sealed class RegistryLaunchAtLoginStore : ILaunchAtLoginStore
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? GetValue(string appName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(appName) as string;
    }

    public void SetValue(string appName, string command)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Registry startup entries are only supported on Windows.");
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Unable to open startup registry key.");
        key.SetValue(appName, command, RegistryValueKind.String);
    }

    public void RemoveValue(string appName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(appName, throwOnMissingValue: false);
    }
}
