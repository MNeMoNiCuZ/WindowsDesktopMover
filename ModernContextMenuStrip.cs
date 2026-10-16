using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WindowsDesktopMover;

internal sealed class ModernContextMenuStrip : ContextMenuStrip
{
    private readonly Font menuFont = new("Segoe UI", 9.5f);
    private MenuPalette palette = MenuPalette.Dark;

    internal ModernContextMenuStrip()
    {
        ShowImageMargin = false;
        ShowCheckMargin = true;
        Font = menuFont;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        ApplyTheme();
    }

    protected override void OnOpening(CancelEventArgs e)
    {
        ApplyTheme();
        base.OnOpening(e);
    }

    internal void ApplyTheme()
    {
        object? preference = null;
        try
        {
            preference = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", null);
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // An unavailable or unrecognized preference uses the dark palette.
        }
        ApplyPalette(SystemInformation.HighContrast ? MenuPalette.HighContrast : MenuPalette.FromPreference(preference));
    }

    internal void ApplyPalette(MenuPalette value)
    {
        palette = value;
        var renderer = new ModernMenuRenderer(palette);
        StyleMenu(this, renderer);
    }

    private void StyleMenu(ToolStripDropDown dropDown, ToolStripRenderer renderer)
    {
        int Scale(int value) => (int)Math.Round(value * dropDown.DeviceDpi / 96f);
        dropDown.Renderer = renderer;
        dropDown.Font = menuFont;
        dropDown.BackColor = palette.Background;
        dropDown.ForeColor = palette.Text;
        dropDown.Padding = new Padding(Scale(5));
        foreach (ToolStripItem item in dropDown.Items)
        {
            if (item is ToolStripSeparator)
            {
                item.AutoSize = false;
                item.Height = Scale(1);
                item.Margin = new Padding(0, Scale(5), 0, Scale(5));
            }
            else
            {
                // DropDownMenu owns horizontal spacing and uses the item bounds
                // to anchor submenus. Extra horizontal padding creates a gap.
                item.Padding = new Padding(0, Scale(7), 0, Scale(7));
            }
            if (item is ToolStripMenuItem { HasDropDownItems: true } child)
            {
                StyleMenu(child.DropDown, renderer);
                // Submenus create their native windows lazily.
                child.DropDown.HandleCreated -= OnDropDownHandleCreated;
                child.DropDown.HandleCreated += OnDropDownHandleCreated;
            }
        }
        if (dropDown.IsHandleCreated) ApplyWindowTheme(dropDown.Handle);
        dropDown.Invalidate(true);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyWindowTheme(Handle);
    }

    private void OnDropDownHandleCreated(object? sender, EventArgs e)
    {
        if (sender is ToolStripDropDown dropDown) ApplyWindowTheme(dropDown.Handle);
    }

    private void ApplyWindowTheme(nint handle)
    {
        int dark = palette.IsDark ? 1 : 0;
        int corners = 3; // DWMWCP_ROUNDSMALL: Windows 11 popup corners.
        _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        _ = DwmSetWindowAttribute(handle, 33, ref corners, sizeof(int));
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke((Action)(() => { if (!IsDisposed) ApplyTheme(); }));
        }
        catch (InvalidOperationException) { } // The menu may be closing on the UI thread.
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        base.Dispose(disposing);
        if (disposing) menuFont.Dispose();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}

internal sealed record MenuPalette(
    Color Background, Color Text, Color Muted, Color Hover, Color SelectedText, Color Border, Color Separator, bool IsDark)
{
    internal static MenuPalette Dark { get; } = new(
        Color.FromArgb(43, 43, 43), Color.FromArgb(245, 245, 245), Color.FromArgb(164, 164, 164),
        Color.FromArgb(61, 61, 61), Color.FromArgb(245, 245, 245), Color.FromArgb(71, 71, 71), Color.FromArgb(65, 65, 65), true);

    internal static MenuPalette Light { get; } = new(
        Color.FromArgb(249, 249, 249), Color.FromArgb(32, 32, 32), Color.FromArgb(110, 110, 110),
        Color.FromArgb(235, 235, 235), Color.FromArgb(32, 32, 32), Color.FromArgb(211, 211, 211), Color.FromArgb(224, 224, 224), false);

    internal static MenuPalette HighContrast => new(
        SystemColors.Menu, SystemColors.MenuText, SystemColors.GrayText, SystemColors.Highlight,
        SystemColors.HighlightText, SystemColors.WindowText, SystemColors.GrayText, SystemColors.Menu.GetBrightness() < 0.5f);

    internal static MenuPalette FromPreference(object? preference) => preference is int value && value == 1 ? Light : Dark;
}

internal sealed class ModernMenuRenderer(MenuPalette palette) : ToolStripRenderer
{
    private static float Scale(ToolStrip? strip, float value) => value * (strip?.DeviceDpi ?? 96) / 96f;

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(palette.Background);

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        var state = e.Graphics.Save();
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var outline = Rounded(new RectangleF(0.5f, 0.5f, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), Scale(e.ToolStrip, 4));
        using var pen = new Pen(palette.Border);
        e.Graphics.DrawPath(pen, outline);
        e.Graphics.Restore(state);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Enabled || !e.Item.Selected) return;
        var state = e.Graphics.Save();
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float inset = Scale(e.ToolStrip, 5);
        using var shape = Rounded(new RectangleF(inset, 1, e.Item.Width - 2 * inset, e.Item.Height - 2), Scale(e.ToolStrip, 4));
        using var brush = new SolidBrush(palette.Hover);
        e.Graphics.FillPath(brush, shape);
        e.Graphics.Restore(state);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        Color foreground = !e.Item.Enabled ? palette.Muted : e.Item.Selected ? palette.SelectedText : palette.Text;
        var bounds = e.TextRectangle;
        bounds.Y = 0;
        bounds.Height = e.Item.Height;
        TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, bounds, foreground,
            e.TextFormat | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
    {
        if (e.Image is not { } image) return;
        var bounds = e.ImageRectangle;
        bounds.Y = (e.Item.Height - bounds.Height) / 2;
        if (e.Item.Enabled) e.Graphics.DrawImage(image, bounds);
        else ControlPaint.DrawImageDisabled(e.Graphics, image, bounds.X, bounds.Y, palette.Background);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var state = e.Graphics.Save();
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float unit = Scale(e.ToolStrip, 1);
        float x = e.ImageRectangle.Left + e.ImageRectangle.Width / 2f;
        float y = e.Item.Height / 2f;
        using var pen = new Pen(!e.Item.Enabled ? palette.Muted : e.Item.Selected ? palette.SelectedText : palette.Text, 1.6f * unit)
        { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        e.Graphics.DrawLines(pen, new PointF[] { new(x - 4 * unit, y), new(x - unit, y + 3 * unit), new(x + 5 * unit, y - 3 * unit) });
        e.Graphics.Restore(state);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        if (e.Item is not { } item) return;
        var state = e.Graphics.Save();
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float unit = Scale(item.Owner, 1);
        float x = e.ArrowRectangle.Left + e.ArrowRectangle.Width / 2f;
        float y = item.Height / 2f;
        float direction = e.Direction == ArrowDirection.Left ? -1 : 1;
        using var pen = new Pen(!item.Enabled ? palette.Muted : item.Selected ? palette.SelectedText : palette.Text, 1.3f * unit)
        { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        e.Graphics.DrawLines(pen, new PointF[] { new(x - direction * unit, y - 3 * unit), new(x + direction * 2 * unit, y), new(x - direction * unit, y + 3 * unit) });
        e.Graphics.Restore(state);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(palette.Separator);
        float inset = Scale(e.ToolStrip, 8);
        e.Graphics.DrawLine(pen, inset, e.Item.Height / 2f, e.Item.Width - inset, e.Item.Height / 2f);
    }

    private static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        float diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
