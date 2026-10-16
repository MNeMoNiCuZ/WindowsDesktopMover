using System.Runtime.InteropServices;

namespace WindowsDesktopMover;

internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private delegate nint HookProc(int code, nint message, nint data);
    private readonly HookProc callback;
    private readonly nint hook;
    private readonly HashSet<uint> held = [];
    internal event Action<int>? Pressed;

    internal HotkeyWindow()
    {
        CreateHandle(new CreateParams { Caption = "Windows Desktop Mover hotkeys", Parent = new nint(-3) });
        callback = OnKeyboard;
        hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
        if (hook == 0)
        {
            int error = Marshal.GetLastWin32Error();
            DestroyHandle();
            throw new System.ComponentModel.Win32Exception(error, "Could not install the desktop shortcut handler.");
        }
    }

    private nint OnKeyboard(int code, nint message, nint data)
    {
        if (code >= 0)
        {
            uint key = unchecked((uint)Marshal.ReadInt32(data));
            bool down = (int)message is 0x100 or 0x104;
            bool up = (int)message is 0x101 or 0x105;
            if (up && held.Remove(key)) return 1;
            int id = key switch { 0x25 => 1, 0x27 => 2, 0x44 => 3, >= 0x31 and <= 0x39 => (int)key - 0x30 + 10, _ => 0 };
            if (down && id != 0 && IsDown(0x11) && IsDown(0x10) && (IsDown(0x5B) || IsDown(0x5C)) && !IsDown(0x12))
            {
                // Queue work: never call the desktop API inside a low-level hook.
                if (held.Add(key)) PostMessage(Handle, Native.WmHotkey, id, 0);
                return 1;
            }
        }
        return CallNextHookEx(hook, code, message, data);
    }

    private static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == Native.WmHotkey) Pressed?.Invoke(message.WParam.ToInt32());
        base.WndProc(ref message);
    }

    public void Dispose()
    {
        UnhookWindowsHookEx(hook);
        DestroyHandle();
        GC.KeepAlive(callback);
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint hwnd, int message, nint wParam, nint lParam);
}
