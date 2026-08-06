using System.ComponentModel;
using System.Runtime.InteropServices;
using ClipScribe.Core.Abstractions;

namespace ClipScribe.Windows;

public sealed class Win32ClipboardTextReader : IClipboardTextReader
{
    private const uint CfUnicodeText = 13;

    private static readonly string[] ClipboardIgnoreFormatNames =
    [
        "CF_CLIPBOARD_VIEWER_IGNORE",
        "Clipboard Viewer Ignore",
        "ExcludeClipboardContentFromMonitorProcessing"
    ];

    public bool TryReadText(out string? text)
    {
        text = null;

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (!IsClipboardFormatAvailable(CfUnicodeText))
        {
            return false;
        }

        if (!OpenClipboard(IntPtr.Zero))
        {
            return false;
        }

        try
        {
            var handle = GetClipboardData(CfUnicodeText);
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            var ptr = GlobalLock(handle);
            if (ptr == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                text = Marshal.PtrToStringUni(ptr);
                return !string.IsNullOrEmpty(text);
            }
            finally
            {
                if (!GlobalUnlock(handle) && Marshal.GetLastWin32Error() != 0)
                {
                    _ = new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
        }
        finally
        {
            _ = CloseClipboard();
        }
    }

    public bool ShouldIgnoreCurrentClipboard()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        foreach (var formatName in ClipboardIgnoreFormatNames)
        {
            var format = RegisterClipboardFormat(formatName);
            if (format != 0 && IsClipboardFormatAvailable(format))
            {
                return true;
            }
        }

        return false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterClipboardFormat(string lpszFormat);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);
}
