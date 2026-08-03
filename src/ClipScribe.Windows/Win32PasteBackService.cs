using System.Runtime.InteropServices;

namespace ClipScribe.Windows;

public sealed class Win32PasteBackService
{
    private const int SwRestore = 9;
    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyUp = 0x0002;

    private const ushort VkControl = 0x11;
    private const ushort VkV = 0x56;

    public bool TryPasteFromClipboard(IntPtr targetWindow)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (targetWindow == IntPtr.Zero || !IsWindow(targetWindow))
        {
            return false;
        }

        _ = ShowWindow(targetWindow, SwRestore);
        _ = SetForegroundWindow(targetWindow);

        Thread.Sleep(65);

        var inputs = new[]
        {
            CreateKeyInput(VkControl, keyUp: false),
            CreateKeyInput(VkV, keyUp: false),
            CreateKeyInput(VkV, keyUp: true),
            CreateKeyInput(VkControl, keyUp: true)
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        return sent == inputs.Length;
    }

    private static Input CreateKeyInput(ushort virtualKey, bool keyUp)
        => new()
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = virtualKey,
                    ScanCode = 0,
                    Flags = keyUp ? KeyeventfKeyUp : 0,
                    Time = 0,
                    ExtraInfo = IntPtr.Zero
                }
            }
        };

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, Input[] inputs, int sizeOfInputStructure);
}
