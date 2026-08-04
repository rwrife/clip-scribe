using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using ClipScribe.Core.Models;

namespace ClipScribe.Windows;

public sealed class Win32GlobalHotkeyHost : IDisposable
{
    private const int WmHotkey = 0x0312;

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private static readonly IReadOnlyDictionary<string, uint> NamedVirtualKeys = new Dictionary<string, uint>(StringComparer.Ordinal)
    {
        ["TAB"] = 0x09,
        ["ENTER"] = 0x0D,
        ["ESCAPE"] = 0x1B,
        ["SPACE"] = 0x20,
        ["PAGEUP"] = 0x21,
        ["PAGEDOWN"] = 0x22,
        ["END"] = 0x23,
        ["HOME"] = 0x24,
        ["LEFT"] = 0x25,
        ["UP"] = 0x26,
        ["RIGHT"] = 0x27,
        ["DOWN"] = 0x28,
        ["INSERT"] = 0x2D,
        ["DELETE"] = 0x2E
    };

    private readonly Action _onPressed;
    private readonly int _hotkeyId;

    private bool _registered;
    private bool _disposed;

    public Win32GlobalHotkeyHost(Action onPressed)
    {
        ArgumentNullException.ThrowIfNull(onPressed);

        _onPressed = onPressed;
        _hotkeyId = Random.Shared.Next(0x1000, 0x7FFF);
        ComponentDispatcher.ThreadPreprocessMessage += OnThreadPreprocessMessage;
    }

    public void Register(GlobalHotkeySettings settings)
    {
        ThrowIfDisposed();

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Global hotkeys are only available on Windows.");
        }

        var normalized = GlobalHotkeySettings.Normalize(settings);
        var modifiers = BuildModifierFlags(normalized) | ModNoRepeat;
        var virtualKey = ResolveVirtualKey(normalized.Key);

        Unregister();

        if (!RegisterHotKey(IntPtr.Zero, _hotkeyId, modifiers, virtualKey))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"Failed to register hotkey {DescribeHotkey(normalized)}.");
        }

        _registered = true;
    }

    public void Unregister()
    {
        if (!_registered)
        {
            return;
        }

        _ = UnregisterHotKey(IntPtr.Zero, _hotkeyId);
        _registered = false;
    }

    private void OnThreadPreprocessMessage(ref MSG msg, ref bool handled)
    {
        if (handled || msg.message != WmHotkey)
        {
            return;
        }

        if ((int)msg.wParam != _hotkeyId)
        {
            return;
        }

        handled = true;
        _onPressed();
    }

    private static uint BuildModifierFlags(GlobalHotkeySettings settings)
    {
        var flags = 0u;

        if (settings.Alt)
        {
            flags |= ModAlt;
        }

        if (settings.Ctrl)
        {
            flags |= ModControl;
        }

        if (settings.Shift)
        {
            flags |= ModShift;
        }

        if (settings.Win)
        {
            flags |= ModWin;
        }

        return flags;
    }

    private static uint ResolveVirtualKey(string normalizedKey)
    {
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            return 0x56; // V
        }

        if (normalizedKey.Length == 1)
        {
            var ch = normalizedKey[0];
            if (char.IsLetterOrDigit(ch))
            {
                return ch;
            }
        }

        if (normalizedKey.Length >= 2 && normalizedKey[0] == 'F' &&
            int.TryParse(normalizedKey[1..], out var functionKey) &&
            functionKey is >= 1 and <= 24)
        {
            return (uint)(0x6F + functionKey);
        }

        if (NamedVirtualKeys.TryGetValue(normalizedKey, out var namedVirtualKey))
        {
            return namedVirtualKey;
        }

        return 0x56; // V fallback
    }

    private static string DescribeHotkey(GlobalHotkeySettings hotkey)
    {
        var parts = new List<string>();
        if (hotkey.Ctrl)
        {
            parts.Add("Ctrl");
        }

        if (hotkey.Shift)
        {
            parts.Add("Shift");
        }

        if (hotkey.Alt)
        {
            parts.Add("Alt");
        }

        if (hotkey.Win)
        {
            parts.Add("Win");
        }

        parts.Add(hotkey.Key);
        return string.Join("+", parts);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(Win32GlobalHotkeyHost));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Unregister();
        ComponentDispatcher.ThreadPreprocessMessage -= OnThreadPreprocessMessage;
        _disposed = true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
