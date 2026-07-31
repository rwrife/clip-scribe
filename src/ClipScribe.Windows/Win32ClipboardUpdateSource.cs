using System.ComponentModel;
using System.Runtime.InteropServices;
using ClipScribe.Core.Abstractions;

namespace ClipScribe.Windows;

public sealed class Win32ClipboardUpdateSource : IClipboardUpdateSource
{
    private const int WmClipboardUpdate = 0x031D;
    private const int WmDestroy = 0x0002;
    private const int WmClose = 0x0010;

    private static readonly IntPtr HwndMessage = new(-3);

    private readonly ManualResetEventSlim _windowReady = new(false);
    private readonly object _sync = new();

    private Thread? _thread;
    private IntPtr _windowHandle;
    private bool _disposed;

    public event EventHandler? ClipboardUpdated;

    public void Start()
    {
        ThrowIfDisposed();

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Win32 clipboard listener only works on Windows.");
        }

        lock (_sync)
        {
            if (_thread is { IsAlive: true })
            {
                return;
            }

            _windowReady.Reset();
            _thread = new Thread(RunMessageLoop)
            {
                IsBackground = true,
                Name = "clip-scribe-clipboard-listener"
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        if (!_windowReady.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("Timed out starting the Win32 clipboard listener.");
        }

        if (_windowHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Win32 clipboard listener failed to create a hidden window.");
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (_thread is null)
            {
                return;
            }

            if (_windowHandle != IntPtr.Zero)
            {
                _ = PostMessage(_windowHandle, WmClose, IntPtr.Zero, IntPtr.Zero);
            }

            if (!_thread.Join(TimeSpan.FromSeconds(5)))
            {
                try
                {
                    _thread.Interrupt();
                }
                catch
                {
                    // ignored
                }
            }

            _thread = null;
            _windowHandle = IntPtr.Zero;
        }
    }

    private void RunMessageLoop()
    {
        var className = $"ClipScribeHiddenWindow_{Guid.NewGuid():N}";
        WndProc callback = WindowProc;

        var wc = new WndClass
        {
            lpszClassName = className,
            lpfnWndProc = callback
        };

        var atom = RegisterClassW(ref wc);
        if (atom == 0)
        {
            _windowReady.Set();
            return;
        }

        try
        {
            _windowHandle = CreateWindowExW(
                0,
                className,
                "clip-scribe-hidden",
                0,
                0,
                0,
                0,
                0,
                HwndMessage,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            if (_windowHandle == IntPtr.Zero)
            {
                _windowReady.Set();
                return;
            }

            if (!AddClipboardFormatListener(_windowHandle))
            {
                var error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error, "Failed to register AddClipboardFormatListener.");
            }

            _windowReady.Set();

            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                _ = TranslateMessage(ref msg);
                _ = DispatchMessage(ref msg);
            }
        }
        finally
        {
            if (_windowHandle != IntPtr.Zero)
            {
                _ = RemoveClipboardFormatListener(_windowHandle);
                _ = DestroyWindow(_windowHandle);
                _windowHandle = IntPtr.Zero;
            }

            _ = UnregisterClassW(className, IntPtr.Zero);
        }
    }

    private IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmClipboardUpdate)
        {
            ClipboardUpdated?.Invoke(this, EventArgs.Empty);
            return IntPtr.Zero;
        }

        if (msg == WmDestroy)
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }

        if (msg == WmClose)
        {
            _ = DestroyWindow(hwnd);
            return IntPtr.Zero;
        }

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(Win32ClipboardUpdateSource));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _windowReady.Dispose();
        _disposed = true;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClass
    {
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public Point pt;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassW([In] ref WndClass lpWndClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClassW(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage([In] ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage([In] ref Msg lpmsg);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
