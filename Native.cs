using System.Runtime.InteropServices;

namespace WindowsDesktopMover;

internal static class Native
{
    internal delegate bool EnumWindowProc(nint hwnd, nint parameter);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowProc callback, nint parameter);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowTextLength(nint hwnd);
    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(nint hwnd, System.Text.StringBuilder name, int capacity);
    internal const int WmHotkey = 0x0312;
    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    internal static extern nint GetShellWindow();
    [DllImport("user32.dll")]
    internal static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint hwnd);
}

internal sealed class DesktopApi : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Query();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int WindowQuery(nint hwnd);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Move(nint hwnd, int desktop);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Switch(int desktop);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Remove(int desktop, int fallback);

    private readonly nint library;
    private readonly Query count;
    private readonly WindowQuery windowDesktop;
    private readonly WindowQuery pinnedWindow;
    private readonly WindowQuery pinnedApp;
    private readonly Move move;
    private readonly Switch switchDesktop;
    private readonly Query create;
    private readonly Remove remove;

    internal DesktopApi()
    {
        int build = Environment.OSVersion.Version.Build;
        if (build < 22621)
            throw new NotSupportedException("This version supports Windows 11 22H2 and newer (x64). Windows 10 and older Windows 11 builds need a different desktop helper.");
        string variant = build >= 26100 ? "win11-24h2" : "win11-23h2";
        library = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "native", variant, "VirtualDesktopAccessor.dll"));
        try
        {
            count = Export<Query>("GetDesktopCount");
            windowDesktop = Export<WindowQuery>("GetWindowDesktopNumber");
            pinnedWindow = Export<WindowQuery>("IsPinnedWindow");
            pinnedApp = Export<WindowQuery>("IsPinnedApp");
            move = Export<Move>("MoveWindowToDesktopNumber");
            switchDesktop = Export<Switch>("GoToDesktopNumber");
            create = Export<Query>("CreateDesktop");
            remove = Export<Remove>("RemoveDesktop");
            _ = Count;
        }
        catch { NativeLibrary.Free(library); throw; }
    }

    private T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
    internal int Count => Check(count(), "read the virtual desktops");
    internal int GetWindowDesktop(nint hwnd) => Check(windowDesktop(hwnd), "find this window's desktop");
    internal int CreateDesktop()
    {
        int previous = Count;
        Check(create(), "create a desktop");
        if (Count != previous + 1) throw new InvalidOperationException("Windows did not create the desktop.");
        return previous;
    }
    internal void RemoveDesktop(int desktop, int fallback) => Check(remove(desktop, fallback), "close the empty desktop");

    internal int MoveWindow(nint hwnd, int target, bool follow, bool createMissing = false, bool closeEmpty = false)
    {
        // Some desktop apps have no AppUserModelID, so the app-pin query returns -1.
        // Only a positive pin result blocks moving; verify the actual move below.
        if (pinnedWindow(hwnd) == 1 || pinnedApp(hwnd) == 1)
            throw new InvalidOperationException("This window or app is shown on all desktops. Turn that off in Task View before moving it.");
        int source = GetWindowDesktop(hwnd);
        int desktopCount = Count;
        if (createMissing && target == desktopCount && target == source + 1) CreateDesktop();
        if (target < 0 || target >= Count)
            throw new InvalidOperationException($"Desktop {target + 1} does not exist. Create it with Win + Ctrl + Shift + D first.");
        if (target == source) return target;
        Check(move(hwnd, target), "move the window");
        if (GetWindowDesktop(hwnd) != target)
            throw new InvalidOperationException("Windows did not move the window. For an administrator app, try running Desktop Mover as administrator too.");
        if (follow)
        {
            Check(switchDesktop(target), "switch to the destination desktop");
            Native.SetForegroundWindow(hwnd);
        }
        if (closeEmpty && Count > 1 && IsDesktopEmpty(source))
        {
            RemoveDesktop(source, target);
            if (source < target) target--;
        }
        return target;
    }

    internal bool IsDesktopEmpty(int desktop)
    {
        bool empty = true;
        bool uncertain = false;
        bool enumerated = Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd) || hwnd == Native.GetShellWindow()) return true;
            long style = Native.GetWindowLongPtr(hwnd, -20).ToInt64(); // GWL_EXSTYLE
            if ((style & (0x80 | 0x08000000)) != 0) return true; // Tool/helper windows, or non-activating overlays.
            // App-cloaked windows are hidden by their app. Shell-cloaked windows may
            // simply belong to another desktop and must still be counted.
            if (Native.DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int)) == 0 && (cloaked & 1) != 0) return true;
            var className = new System.Text.StringBuilder(256);
            if (Native.GetClassName(hwnd, className, className.Capacity) == 0)
            {
                if (Native.IsWindow(hwnd) && Native.GetWindowTextLength(hwnd) > 0) uncertain = true;
                return true;
            }
            if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
            // Pinned windows belong to every desktop, not to this source desktop.
            if (pinnedWindow(hwnd) == 1 || pinnedApp(hwnd) == 1) return true;
            int number = windowDesktop(hwnd);
            if (number < 0)
            {
                // Shell helpers without an application view have no desktop assignment.
                // They must not keep every desktop open.
                return true;
            }
            else if (number == desktop) empty = false;
            return true;
        }, 0);
        return enumerated && empty && !uncertain;
    }

    private static int Check(int result, string action)
    {
        if (result < 0)
            throw new InvalidOperationException($"Could not {action}. The window may not support moving, or the desktop helper may be incompatible with this Windows update.");
        return result;
    }
    public void Dispose() => NativeLibrary.Free(library);
}
