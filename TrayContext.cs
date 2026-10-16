using System.Text.Json;

namespace WindowsDesktopMover;

internal sealed class TrayContext : ApplicationContext
{
    private readonly DesktopApi desktops;
    private readonly HotkeyWindow hotkeys;
    private readonly NotifyIcon tray;
    private readonly Dictionary<string, Icon> icons = new();
    private readonly List<Image> iconPreviews = new();
    private readonly ToolStripMenuItem colorMenu;
    private string iconColor = "silver";
    private readonly ContextMenuStrip menu;
    private readonly ToolStripMenuItem follow;
    private readonly ToolStripMenuItem createMissing;
    private readonly ToolStripMenuItem closeEmpty;
    private readonly ToolStripMenuItem startWithWindows;
    private readonly ToolStripMenuItem status = new("Ready") { Enabled = false };
    private static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsDesktopMover", "settings.json");
    private static readonly (string Id, string Label)[] IconColors =
    [
        ("orange", "Orange"), ("azure", "Azure"), ("cyan-teal", "Cyan teal"),
        ("emerald", "Emerald"), ("violet", "Violet"), ("periwinkle", "Periwinkle"),
        ("coral", "Coral"), ("magenta", "Magenta"), ("amber", "Amber"), ("silver", "Silver")
    ];

    internal TrayContext()
    {
        desktops = new DesktopApi();
        try { hotkeys = new HotkeyWindow(); }
        catch { desktops.Dispose(); throw; }

        var settings = LoadSettings();
        iconColor = IconColors.Any(color => color.Id == settings.IconColor) ? settings.IconColor : "silver";
        follow = new ToolStripMenuItem("Follow the moved window") { CheckOnClick = true, Checked = settings.Follow };
        createMissing = new ToolStripMenuItem("Create a desktop when moving right past the last") { CheckOnClick = true, Checked = settings.CreateMissing };
        closeEmpty = new ToolStripMenuItem("Close the source desktop when its last window moves") { CheckOnClick = true, Checked = settings.CloseEmpty };
        follow.CheckedChanged += (_, _) => SaveSettings();
        createMissing.CheckedChanged += (_, _) => SaveSettings();
        closeEmpty.CheckedChanged += (_, _) => SaveSettings();
        startWithWindows = new ToolStripMenuItem("Start with Windows");
        startWithWindows.Click += (_, _) => ToggleStartWithWindows();
        menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Desktop Mover") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        colorMenu = new ToolStripMenuItem("Color");
        ((ToolStripDropDownMenu)colorMenu.DropDown).ShowCheckMargin = true;
        foreach (var (id, label) in IconColors)
        {
            using var stream = typeof(TrayContext).Assembly.GetManifestResourceStream($"WindowsDesktopMover.Icons.{id}.ico")
                ?? throw new InvalidOperationException($"Missing icon: {id}");
            var icon = new Icon(stream, SystemInformation.SmallIconSize);
            icons.Add(id, icon);
            var preview = icon.ToBitmap();
            iconPreviews.Add(preview);
            var item = new ToolStripMenuItem(label, preview) { Tag = id, Checked = id == iconColor };
            item.Click += (_, _) => SelectIconColor(id);
            colorMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(colorMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(startWithWindows);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(follow);
        menu.Items.Add(createMissing);
        menu.Items.Add(closeEmpty);
        menu.Items.Add(status);
        menu.Items.Add("Last result", null, (_, _) => MessageBox.Show(status.Text, "Desktop Mover"));
        menu.Items.Add("Shortcut guide", null, (_, _) => ShowGuide());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        tray = new NotifyIcon { Text = "Desktop Mover — Ctrl + Win + Shift", Icon = icons[iconColor], ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowGuide();
        hotkeys.Pressed += MoveActiveWindow;
        menu.Opening += (_, _) => RefreshStartWithWindows();
        RefreshStartWithWindows();
    }

    private void RefreshStartWithWindows()
    {
        try
        {
            startWithWindows.Checked = StartupRegistration.IsEnabled();
            startWithWindows.Enabled = true;
            startWithWindows.ToolTipText = "";
        }
        catch (Exception error)
        {
            startWithWindows.Enabled = false;
            startWithWindows.ToolTipText = $"Could not read the Windows startup setting: {error.Message}";
            status.Text = startWithWindows.ToolTipText;
        }
    }

    private void ToggleStartWithWindows()
    {
        try
        {
            bool enabled = !startWithWindows.Checked;
            StartupRegistration.SetEnabled(enabled);
            startWithWindows.Checked = enabled;
        }
        catch (Exception error)
        {
            Notify($"Could not change the Windows startup setting: {error.Message}", ToolTipIcon.Error);
        }
    }

    private void SelectIconColor(string color)
    {
        if (color == iconColor) return;
        tray.Icon = icons[color];
        iconColor = color;
        foreach (ToolStripMenuItem item in colorMenu.DropDownItems)
            item.Checked = (string?)item.Tag == color;
        SaveSettings();
    }

    private void MoveActiveWindow(int shortcut)
    {
        try
        {
            if (shortcut == 3)
            {
                int created = desktops.CreateDesktop();
                status.Text = $"Created desktop {created + 1}";
                return;
            }
            nint hwnd = Native.GetForegroundWindow();
            hwnd = Native.GetAncestor(hwnd, 2); // GA_ROOT: retain the active app's top-level window.
            if (hwnd == 0 || hwnd == Native.GetShellWindow()) return;
            Native.GetWindowThreadProcessId(hwnd, out uint processId);
            if (processId == Environment.ProcessId) return;

            int current = desktops.GetWindowDesktop(hwnd);
            int target = shortcut switch { 1 => current - 1, 2 => current + 1, _ => shortcut - 11 };
            if (shortcut == 1 && target < 0) { Notify("Already on the first desktop."); return; }
            if (shortcut == 2 && target >= desktops.Count && !createMissing.Checked) { Notify("Already on the last desktop."); return; }
            if (target == current) return;
            MoveWindow(hwnd, target, shortcut == 2 && createMissing.Checked);
        }
        catch (Exception error) { Notify(error.Message, ToolTipIcon.Warning); }
    }

    private void MoveWindow(nint hwnd, int target, bool create)
    {
        try
        {
            target = desktops.MoveWindow(hwnd, target, follow.Checked, create, closeEmpty.Checked);
            status.Text = $"Moved window to desktop {target + 1}";
        }
        catch (Exception error) { Notify(error.Message, ToolTipIcon.Warning); }
    }

    private void Notify(string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        status.Text = text;
        tray.ShowBalloonTip(4000, "Desktop Mover", text, icon);
    }
    private static void ShowGuide() => MessageBox.Show(
        "Move the active window:\n\nCtrl + Win + Shift + Left / Right → adjacent desktop\nCtrl + Win + Shift + 1–9 → desktop 1–9 (top-row numbers)\n\nCtrl + Win + Shift + D → create a new desktop\n\nThe tray menu controls automatic desktop creation and closing empty source desktops. Both are enabled by default.\nEnable ‘Follow the moved window’ in the tray menu to switch with it.\n\nOnly the active window moves; other windows from the same app stay put.\n\nDesktop helper: VirtualDesktopAccessor by Jari Pennanen (MIT).",
        "Desktop Mover shortcuts", MessageBoxButtons.OK, MessageBoxIcon.Information);

    private static Settings LoadSettings()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath)) ?? new Settings(); }
        catch { return new Settings(); }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new Settings { Follow = follow.Checked, CreateMissing = createMissing.Checked, CloseEmpty = closeEmpty.Checked, IconColor = iconColor }));
        }
        catch (Exception error) { Notify($"Could not save the setting: {error.Message}", ToolTipIcon.Warning); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            tray.Visible = false;
            tray.Dispose();
            menu.Dispose();
            foreach (var preview in iconPreviews) preview.Dispose();
            foreach (var icon in icons.Values) icon.Dispose();
            hotkeys.Dispose();
            desktops.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed record Settings
    {
        public bool Follow { get; init; }
        public bool CreateMissing { get; init; } = true;
        public bool CloseEmpty { get; init; } = true;
        public string IconColor { get; init; } = "silver";
    }
}
