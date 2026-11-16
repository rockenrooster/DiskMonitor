using System.Drawing.Drawing2D;

namespace DiskMonitor.UI.Theming;

/// <summary>Icon-font glyphs (Segoe Fluent Icons, with the same code points in Segoe MDL2 Assets).</summary>
internal static class Glyphs
{
    public const string Home = "";
    public const string Health = "";
    public const string Warning = "";
    public const string Error = "";
    public const string ErrorBadge = "";
    public const string Info = "";
    public const string Completed = "";
    public const string Check = "";
    public const string HardDrive = "";
    public const string Diagnostic = "";
    public const string Repair = "";
    public const string Settings = "";
    public const string Search = "";
    public const string Clear = "";
    public const string Refresh = "";
    public const string Copy = "";
    public const string OpenExternal = "";
    public const string Save = "";
    public const string Download = "";
    public const string More = "";
    public const string ChevronRight = "";
    public const string ChevronDown = "";
    public const string ChevronUp = "";
    public const string Play = "";
    public const string Stop = "";
    public const string Shield = "";
    public const string Clock = "";
    public const string Bell = "";
    public const string QuietHours = "";
    public const string Filter = "";
    public const string Folder = "";
    public const string Mute = "";
    public const string List = "";
    public const string Group = "";
    public const string Unknown = "";
    public const string Accept = "";
    public const string Usb = "";
    public const string Sync = "";
}

/// <summary>Painting primitives shared by every custom control.</summary>
internal static class Draw
{
    public const TextFormatFlags SingleLine =
        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

    public const TextFormatFlags Centered =
        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

    public const TextFormatFlags RightAligned =
        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

    public const TextFormatFlags Wrapped =
        TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl;

    public static Color Blend(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            255,
            (int)Math.Round(from.R + (to.R - from.R) * amount),
            (int)Math.Round(from.G + (to.G - from.G) * amount),
            (int)Math.Round(from.B + (to.B - from.B) * amount));
    }

    public static void Text(Graphics graphics, string? text, Font font, Rectangle bounds, Color color, TextFormatFlags flags = SingleLine)
    {
        if (!string.IsNullOrEmpty(text) && bounds.Width > 0 && bounds.Height > 0)
        {
            TextRenderer.DrawText(graphics, text, font, bounds, color, flags);
        }
    }

    /// <summary>Draws an icon-font glyph centered in <paramref name="bounds"/>.</summary>
    public static void Glyph(Graphics graphics, string glyph, Font iconFont, Rectangle bounds, Color color) =>
        TextRenderer.DrawText(graphics, glyph, iconFont, bounds, color, Centered | TextFormatFlags.NoClipping);

    public static Size Measure(string? text, Font font) =>
        string.IsNullOrEmpty(text)
            ? Size.Empty
            : TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);

    /// <summary>Height of <paramref name="text"/> when word-wrapped to <paramref name="width"/>.</summary>
    public static int MeasureWrapped(string? text, Font font, int width) =>
        string.IsNullOrEmpty(text) || width <= 0
            ? 0
            : TextRenderer.MeasureText(text, font, new Size(width, int.MaxValue), Wrapped).Height;

    public static GraphicsPath RoundedPath(RectangleF bounds, float radius)
    {
        GraphicsPath path = new();
        float diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (diameter <= 0.5f)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRounded(Graphics graphics, Rectangle bounds, int radius, Color fill)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        SmoothingMode previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using GraphicsPath path = RoundedPath(new RectangleF(bounds.X - 0.5f, bounds.Y - 0.5f, bounds.Width, bounds.Height), radius);
        using SolidBrush brush = new(fill);
        graphics.FillPath(brush, path);
        graphics.SmoothingMode = previous;
    }

    public static void StrokeRounded(Graphics graphics, Rectangle bounds, int radius, Color color, float width = 1f)
    {
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return;
        }

        SmoothingMode previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float inset = width / 2f;
        using GraphicsPath path = RoundedPath(
            new RectangleF(bounds.X + inset - 0.5f, bounds.Y + inset - 0.5f, bounds.Width - width, bounds.Height - width),
            Math.Max(0, radius - inset));
        using Pen pen = new(color, width);
        graphics.DrawPath(pen, path);
        graphics.SmoothingMode = previous;
    }

    /// <summary>A filled, bordered rounded rectangle: the basic card.</summary>
    public static void Card(Graphics graphics, Rectangle bounds, int radius, Color fill, Color border)
    {
        FillRounded(graphics, bounds, radius, fill);
        StrokeRounded(graphics, bounds, radius, border);
    }

    public static void FillCircle(Graphics graphics, Rectangle bounds, Color fill)
    {
        SmoothingMode previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using SolidBrush brush = new(fill);
        graphics.FillEllipse(brush, bounds.X - 0.5f, bounds.Y - 0.5f, bounds.Width, bounds.Height);
        graphics.SmoothingMode = previous;
    }

    /// <summary>A rounded label with tinted background, sized to its text. Returns the width used.</summary>
    public static int Pill(Graphics graphics, string text, Font font, Point location, int height, int padding, Color color, Color over)
    {
        int width = Measure(text, font).Width + padding * 2;
        Rectangle bounds = new(location.X, location.Y, width, height);
        FillRounded(graphics, bounds, height / 2, Blend(over, color, 0.18));
        Text(graphics, text, font, bounds, color, Centered);
        return width;
    }

    /// <summary>A horizontal meter: a track with a filled portion.</summary>
    public static void Bar(Graphics graphics, Rectangle bounds, double fraction, Color track, Color fill)
    {
        FillRounded(graphics, bounds, bounds.Height / 2, track);
        int filled = (int)Math.Round(bounds.Width * Math.Clamp(fraction, 0, 1));
        if (filled > 0)
        {
            FillRounded(graphics, new Rectangle(bounds.X, bounds.Y, Math.Max(filled, bounds.Height), bounds.Height), bounds.Height / 2, fill);
        }
    }
}
