using System.Runtime.InteropServices;

namespace ClipScribe.Windows;

public sealed class Win32PasteBackService
{
    private const int SwRestore = 9;
    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyUp = 0x0002;
    private const uint KeyeventfUnicode = 0x0004;

    private const ushort VkControl = 0x11;
    private const ushort VkV = 0x56;

    public bool TryPasteFromClipboard(IntPtr targetWindow)
    {
        if (!CanUseTargetWindow(targetWindow))
        {
            return false;
        }

        FocusTargetWindow(targetWindow);

        var inputs = new[]
        {
            CreateVirtualKeyInput(VkControl, keyUp: false),
            CreateVirtualKeyInput(VkV, keyUp: false),
            CreateVirtualKeyInput(VkV, keyUp: true),
            CreateVirtualKeyInput(VkControl, keyUp: true)
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        return sent == inputs.Length;
    }

    public bool TryTypeText(IntPtr targetWindow, string text)
    {
        if (!CanUseTargetWindow(targetWindow) || string.IsNullOrEmpty(text))
        {
            return false;
        }

        FocusTargetWindow(targetWindow);

        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);

        var inputs = new List<Input>(normalized.Length * 2);
        foreach (var ch in normalized)
        {
            var toSend = ch == '\n' ? '\r' : ch;
            inputs.Add(CreateUnicodeInput(toSend, keyUp: false));
            inputs.Add(CreateUnicodeInput(toSend, keyUp: true));
        }

        if (inputs.Count == 0)
        {
            return false;
        }

        var sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>());
        return sent == inputs.Count;
    }

    private static bool CanUseTargetWindow(IntPtr targetWindow)
        => OperatingSystem.IsWindows() && targetWindow != IntPtr.Zero && IsWindow(targetWindow);

    private static void FocusTargetWindow(IntPtr targetWindow)
    {
        _ = ShowWindow(targetWindow, SwRestore);
        _ = SetForegroundWindow(targetWindow);
        Thread.Sleep(65);
    }

    private static Input CreateVirtualKeyInput(ushort virtualKey, bool keyUp)
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

    private static Input CreateUnicodeInput(char character, bool keyUp)
        => new()
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = 0,
                    ScanCode = character,
                    Flags = KeyeventfUnicode | (keyUp ? KeyeventfKeyUp : 0),
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
