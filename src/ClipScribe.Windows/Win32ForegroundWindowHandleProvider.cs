using System.Runtime.InteropServices;

namespace ClipScribe.Windows;

public sealed class Win32ForegroundWindowHandleProvider
{
    public IntPtr TryGetForegroundWindowHandle()
    {
        if (!OperatingSystem.IsWindows())
        {
            return IntPtr.Zero;
        }

        return GetForegroundWindow();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
