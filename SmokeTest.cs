using System.Runtime.InteropServices;

namespace WindowsDesktopMover;

internal static class SmokeTest
{
    // An opt-in integration check: only our temporary test window is moved.
    internal static int Run(bool testKeyboard = true)
    {
        string report = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "smoke-test.txt");
        var lines = new List<string>();
        DesktopApi? api = null;
        int temporary = -1;
        try
        {
            api = new DesktopApi();
            lines.Add($"PASS: desktop helper loaded; {api.Count} desktop(s).");
            if (api.Count == 1)
            {
                api.CreateDesktop();
                temporary = api.Count - 1;
                if (temporary != 1) throw new Exception("Could not create exactly one temporary desktop.");
                lines.Add("PASS: temporary second desktop created for the move test.");
            }
            using (var hotkeys = new HotkeyWindow())
                lines.Add("PASS: handler for all 12 global shortcuts installed.");
            using (var hotkeys = new HotkeyWindow())
                lines.Add("PASS: shortcut handler removed and installed again.");
            using var window = new Form { Text = "Desktop Mover integration test", ShowInTaskbar = false, Width = 240, Height = 100 };
            window.Show();
            Application.DoEvents();
            int original = api.GetWindowDesktop(window.Handle);
            lines.Add($"PASS: test window found on desktop {original + 1}.");
            if (testKeyboard)
            using (var hotkeys = new HotkeyWindow())
            {
                int received = 0;
                hotkeys.Pressed += id =>
                {
                    received = id;
                    if (id == 12) api.MoveWindow(window.Handle, 1, false);
                };
                window.Activate();
                Native.SetForegroundWindow(window.Handle);
                if (Native.GetForegroundWindow() != window.Handle)
                    throw new Exception("Could not focus the temporary window for the keyboard test.");
                PressShortcut();
                var deadline = DateTime.UtcNow.AddSeconds(2);
                while (received == 0 && DateTime.UtcNow < deadline)
                {
                    Application.DoEvents();
                    Thread.Sleep(10);
                }
                if (received != 12)
                    throw new Exception($"Ctrl + Win + Shift + 2 did not deliver hotkey 12 (received {received}).");
                if (api.GetWindowDesktop(window.Handle) != 1)
                    throw new Exception("The keyboard shortcut did not move the test window to desktop 2.");
                lines.Add("PASS: actual Ctrl + Win + Shift + 2 keyboard input moved the test window to desktop 2.");
                api.MoveWindow(window.Handle, original, false);
            }
            if (api.Count > 1)
            {
                try
                {
                    api.MoveWindow(window.Handle, original == 0 ? 1 : 0, false);
                    lines.Add("PASS: test window moved to another desktop.");
                }
                finally { api.MoveWindow(window.Handle, original, false); }
                lines.Add("PASS: test window restored to its original desktop.");
            }
            else lines.Add("SKIP: moving needs a second existing desktop.");
            try { api.MoveWindow(window.Handle, api.Count, false); throw new Exception("Missing desktop was not rejected."); }
            catch (InvalidOperationException) { lines.Add("PASS: nonexistent destination rejected."); }
            CheckAutomaticDesktops(api, window, original, lines);
            File.WriteAllLines(report, lines);
            return 0;
        }
        catch (Exception error)
        {
            lines.Add($"FAIL: {error}");
            File.WriteAllLines(report, lines);
            return 1;
        }
        finally
        {
            try
            {
                if (temporary >= 0 && api != null)
                {
                    api.RemoveDesktop(temporary, 0);
                    File.AppendAllText(report, "PASS: temporary desktop removed.\n");
                }
            }
            finally { api?.Dispose(); }
        }
    }

    private static void CheckAutomaticDesktops(DesktopApi api, Form window, int original, List<string> lines)
    {
        int source = api.CreateDesktop();
        int destination = -1;
        using var blocker = new Form { Text = "Desktop Mover occupancy test", ShowInTaskbar = false, Width = 240, Height = 100 };
        try
        {
            api.MoveWindow(window.Handle, source, false);
            if (api.IsDesktopEmpty(source)) throw new Exception("An occupied desktop was reported empty.");
            destination = api.MoveWindow(window.Handle, source + 1, false, createMissing: true);
            if (destination != source + 1 || api.GetWindowDesktop(window.Handle) != destination)
                throw new Exception("Moving right did not create and move to the missing desktop.");
            if (!api.IsDesktopEmpty(source)) throw new Exception("The empty source desktop was not detected.");
            lines.Add("PASS: moving right creates the missing desktop; cleanup disabled retains the empty source.");

            blocker.Show();
            Application.DoEvents();
            api.MoveWindow(blocker.Handle, source, false);
            api.MoveWindow(window.Handle, source, false);
            int before = api.Count;
            api.MoveWindow(window.Handle, destination, false, closeEmpty: true);
            if (api.Count != before || api.GetWindowDesktop(blocker.Handle) != source)
                throw new Exception("Cleanup closed a desktop containing another window.");
            blocker.WindowState = FormWindowState.Minimized;
            Application.DoEvents();
            if (api.IsDesktopEmpty(source)) throw new Exception("A minimized window was ignored during occupancy detection.");
            lines.Add("PASS: another window prevents source cleanup, including when minimized.");

            blocker.Close();
            Application.DoEvents();
            api.MoveWindow(window.Handle, source, false);
            int final = api.MoveWindow(window.Handle, destination, false, closeEmpty: true);
            if (api.Count != before - 1 || final != source || api.GetWindowDesktop(window.Handle) != final)
                throw new Exception("Empty-source cleanup or destination renumbering failed.");
            destination = -1; // The new destination now occupies the source index.
            lines.Add("PASS: moving the last window closes its source and returns the renumbered destination.");
        }
        finally
        {
            blocker.Close();
            api.MoveWindow(window.Handle, original, false);
            if (destination >= 0 && destination < api.Count) api.RemoveDesktop(destination, original);
            if (source < api.Count) api.RemoveDesktop(source, original);
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)] internal uint Type;
        [FieldOffset(8)] internal ushort Key;
        [FieldOffset(12)] internal uint Flags;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    private static void PressShortcut()
    {
        ushort[] keys = [0x11, 0x5B, 0x10, 0x32]; // Ctrl, Win, Shift, 2
        Input[] input = keys.Select(key => new Input { Type = 1, Key = key })
            .Concat(keys.Reverse().Select(key => new Input { Type = 1, Key = key, Flags = 2 })).ToArray();
        if (SendInput((uint)input.Length, input, Marshal.SizeOf<Input>()) != input.Length)
        {
            // Release the modifiers even if Windows accepted only part of the input batch.
            var release = keys.Reverse().Select(key => new Input { Type = 1, Key = key, Flags = 2 }).ToArray();
            SendInput((uint)release.Length, release, Marshal.SizeOf<Input>());
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
    }
}
