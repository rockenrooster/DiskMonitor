using DiskMonitor.UI.Theming;

namespace DiskMonitor.UI.Controls;

/// <summary>Palette-matched context menus, used by dropdowns, the tray icon, and right-click menus.</summary>
internal static class Menus
{
    public static ContextMenuStrip Create(int dpi, bool checkMargin = false) => new()
    {
        Renderer = new MenuRenderer(dpi),
        Font = Theme.Font(dpi, FontRole.Body),
        ShowImageMargin = false,
        ShowCheckMargin = checkMargin,
        ShowItemToolTips = false,
        Padding = new Padding(Theme.S(dpi, 4)),
        DropShadowEnabled = true
    };

    public static ContextMenuStrip Create(Control owner, bool checkMargin = false) => Create(owner.DeviceDpi, checkMargin);

    /// <summary>Adds a command item with comfortable touch-friendly padding.</summary>
    public static ToolStripMenuItem Add(ToolStripItemCollection items, string text, Action onClick, bool enabled = true, bool isChecked = false)
    {
        ToolStripMenuItem item = new(text)
        {
            Enabled = enabled,
            Checked = isChecked,
            Padding = new Padding(4, 5, 4, 5)
        };
        item.Click += (_, _) => onClick();
        items.Add(item);
        return item;
    }

    public static ToolStripMenuItem AddSubmenu(ToolStripItemCollection items, string text, bool checkMargin = false)
    {
        ToolStripMenuItem item = new(text) { Padding = new Padding(4, 5, 4, 5) };
        if (item.DropDown is ToolStripDropDownMenu dropDown)
        {
            dropDown.ShowImageMargin = false;
            dropDown.ShowCheckMargin = checkMargin;
        }

        items.Add(item);
        return item;
    }

    private sealed class MenuRenderer(int dpi) : ToolStripProfessionalRenderer
    {
        private static Palette P => Theme.Colors;

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using SolidBrush brush = new(P.Surface);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            // No shaded gutter: checks are drawn straight onto the menu surface.
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using Pen pen = new(P.BorderStrong);
            Rectangle bounds = new(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            e.Graphics.DrawRectangle(pen, bounds);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected && !e.Item.Pressed)
            {
                return;
            }

            Rectangle bounds = new(2, 1, e.Item.Width - 4, e.Item.Height - 2);
            Color fill = !e.Item.Enabled ? P.Surface : e.Item.Pressed && e.Item is ToolStripMenuItem { HasDropDownItems: false } ? P.SurfacePressed : P.SurfaceHover;
            Draw.FillRounded(e.Graphics, bounds, Theme.S(dpi, 4), fill);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? P.Text : P.TextFaint;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item is { Enabled: true } ? P.TextMuted : P.TextFaint;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle bounds = e.ImageRectangle;
            bounds.Inflate(2, 2);
            Draw.Glyph(e.Graphics, Glyphs.Check, Theme.Font(dpi, FontRole.IconSmall), bounds, P.Accent);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using Pen pen = new(P.Border);
            int y = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
        }
    }
}
