using System.Diagnostics;
using System.Runtime.InteropServices;
using ClipScribe.Core.Abstractions;

namespace ClipScribe.Windows;

public sealed class Win32ForegroundWindowInfoProvider : IForegroundWindowInfoProvider
{
    public string? TryGetForegroundProcessName()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        _ = GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
        {
            return null;
        }

        try
        {
            return Process.GetProcessById((int)pid).ProcessName;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
