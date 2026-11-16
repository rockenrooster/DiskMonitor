using System.Drawing.Drawing2D;
using DiskMonitor.Core.Drives;
using DiskMonitor.Core.Events;

namespace DiskMonitor.UI.Theming;

/// <summary>State icons of design/icons.md: 1.5 px strokes in Text3.</summary>
internal enum StateIcon
{
    Pending,
    Skipped,
    Missing,
    Error
}

/// <summary>
/// Painting helpers. Coordinates are device pixels. Shapes use anti-aliasing with a half-pixel offset,
/// so a 1 px line at x + 0.5 covers exactly one pixel column, as in the design SVGs.
/// </summary>
internal static class Draw
{
    public const TextFormatFlags TextFlags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine
        | TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping;

    private static ThemeResources R => ThemeService.Resources;

    public static void PrepareShapes(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;
    }

    public static float Scale(int dpi) => dpi / 96f;

    public static void FillRect(Graphics g, RectangleF rect, Color color) => g.FillRectangle(R.Brush(color), rect);

    public static void FillRounded(Graphics g, RectangleF rect, float radius, Color color)
    {
        if (color.A == 0 || rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        if (radius <= 0.01f)
        {
            g.FillRectangle(R.Brush(color), rect);
            return;
        }

        GraphicsPath path = R.Rounded(rect.Width, rect.Height, radius);
        g.TranslateTransform(rect.X, rect.Y);
        g.FillPath(R.Brush(color), path);
        g.TranslateTransform(-rect.X, -rect.Y);
    }

    /// <summary>A border of <paramref name="width"/> device pixels drawn inside the element's bounds.</summary>
    public static void StrokeRounded(Graphics g, RectangleF rect, float radius, Color color, float width)
    {
        if (color.A == 0 || width <= 0)
        {
            return;
        }

        float half = width / 2;
        RectangleF inner = new(rect.X + half, rect.Y + half, rect.Width - width, rect.Height - width);
        float r = Math.Max(0, radius - half);
        GraphicsPath path = R.Rounded(inner.Width, inner.Height, r);
        g.TranslateTransform(inner.X, inner.Y);
        g.DrawPath(R.Pen(color, width), path);
        g.TranslateTransform(-inner.X, -inner.Y);
    }

    /// <summary>Fill plus a 1 px (scaled) inside border.</summary>
    public static void Box(Graphics g, RectangleF rect, float radius, Color fill, Color border, int dpi)
    {
        FillRounded(g, rect, radius, fill);
        StrokeRounded(g, rect, radius, border, Scale(dpi));
    }

    /// <summary>The keyboard focus ring: 2 px Accent, 1 px away from the control (outer edge 3 px out).</summary>
    public static void FocusRing(Graphics g, RectangleF control, float controlRadius, int dpi)
    {
        float s = Scale(dpi);
        RectangleF ring = RectangleF.Inflate(control, 3 * s, 3 * s);
        StrokeRounded(g, ring, controlRadius + 3 * s, ThemeService.Tokens.Accent, 2 * s);
    }

    public static void Line(Graphics g, Color color, float width, float x1, float y1, float x2, float y2, bool round = false) =>
        g.DrawLine(R.Pen(color, width, round), x1, y1, x2, y2);

    public static void Circle(Graphics g, Color color, float cx, float cy, float r) =>
        g.FillEllipse(R.Brush(color), cx - r, cy - r, 2 * r, 2 * r);

    public static void Ring(Graphics g, Color color, float width, float cx, float cy, float r) =>
        g.DrawEllipse(R.Pen(color, width), cx - r, cy - r, 2 * r, 2 * r);

    // --- text --------------------------------------------------------------------------------------

    public static Size Measure(string text, Font font) =>
        text.Length == 0 ? Size.Empty : TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue), TextFlags & ~TextFormatFlags.EndEllipsis);

    public static int TextWidth(string text, Font font) => Measure(text, font).Width;

    /// <summary>Draws one line of text whose vertical centre is <paramref name="centerY"/>, starting at <paramref name="x"/>.</summary>
    public static void Text(Graphics g, string text, Font font, float x, float centerY, Color color, int maxWidth = int.MaxValue)
    {
        if (text.Length == 0)
        {
            return;
        }

        (int baseline, float central) = R.TextMetrics(font);
        int top = (int)Math.Floor(centerY + central + 0.5f) - baseline;
        Rectangle bounds = new((int)Math.Round(x), top, Math.Max(1, maxWidth == int.MaxValue ? 100_000 : maxWidth), font.Height + 2);
        TextRenderer.DrawText(g, text, font, bounds, color, TextFlags);
    }

    /// <summary>Text centred horizontally on <paramref name="centerX"/>.</summary>
    public static void TextCentered(Graphics g, string text, Font font, float centerX, float centerY, Color color)
    {
        int width = TextWidth(text, font);
        Text(g, text, font, centerX - width / 2f, centerY, color);
    }

    /// <summary>Text ending at <paramref name="rightX"/>.</summary>
    public static void TextRight(Graphics g, string text, Font font, float rightX, float centerY, Color color)
    {
        int width = TextWidth(text, font);
        Text(g, text, font, rightX - width, centerY, color);
    }

    /// <summary>Wrapped text in a box; returns the height used.</summary>
    public static int TextWrapped(Graphics g, string text, Font font, Rectangle bounds, Color color, int lineHeight)
    {
        List<string> lines = Wrap(text, font, bounds.Width);
        int y = bounds.Y;
        foreach (string line in lines)
        {
            Text(g, line, font, bounds.X, y + lineHeight / 2f, color, bounds.Width);
            y += lineHeight;
        }

        return lines.Count * lineHeight;
    }

    /// <summary>Breaks text into lines that fit <paramref name="width"/>, at spaces.</summary>
    public static List<string> Wrap(string text, Font font, int width)
    {
        List<string> lines = [];
        foreach (string paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            string[] words = paragraph.Split(' ');
            string current = "";
            foreach (string word in words)
            {
                string candidate = current.Length == 0 ? word : current + " " + word;
                if (current.Length > 0 && TextWidth(candidate, font) > width)
                {
                    lines.Add(current);
                    current = word;
                }
                else
                {
                    current = candidate;
                }
            }

            lines.Add(current);
        }

        return lines;
    }

    // --- icons -------------------------------------------------------------------------------------

    /// <summary>Stroke width of an icon on the 16 px grid, by drawn size (design/icons.md).</summary>
    public static float IconStroke(int logicalSize) => logicalSize switch
    {
        >= 48 => 2.5f,
        >= 32 => 2.2f,
        >= 20 => 1.75f,
        _ => 1.5f
    };

    /// <summary>Draws an icon of <paramref name="logicalSize"/> pixels with its top-left at (x, y).</summary>
    public static void Icon(Graphics g, IconName icon, float x, float y, int logicalSize, int dpi, Color color)
    {
        float s = Scale(dpi);
        float scale = logicalSize * s / 16f;
        (GraphicsPath stroke, GraphicsPath? fill) = R.Icon(icon, scale);
        g.TranslateTransform(x, y);
        g.DrawPath(R.Pen(color, IconStroke(logicalSize) * s, round: true), stroke);
        if (fill is not null)
        {
            g.FillPath(R.Brush(color), fill);
        }

        g.TranslateTransform(-x, -y);
    }

    /// <summary>A severity shape: filled in <paramref name="color"/>, its glyph knocked out in <paramref name="backdrop"/>.</summary>
    public static void Severity(Graphics g, Level level, float x, float y, int logicalSize, int dpi, Color color, Color backdrop) =>
        SeverityShapes.Draw(g, level, x, y, logicalSize * Scale(dpi) / 16f, color, backdrop);

    public static void Severity(Graphics g, Tier tier, float x, float y, int logicalSize, int dpi, Color backdrop) =>
        Severity(g, tier.ToLevel(), x, y, logicalSize, dpi, ThemeService.Tokens.TierText(tier), backdrop);

    /// <summary>The health shape: OK, Medium or Critical shape, or the skipped state icon for no data.</summary>
    public static void Health(Graphics g, HealthStatus health, float x, float y, int logicalSize, int dpi, Color backdrop)
    {
        if (health == HealthStatus.Unknown)
        {
            State(g, StateIcon.Skipped, x, y, logicalSize, dpi, ThemeService.Tokens.Text3);
            return;
        }

        Severity(g, health.ToLevel(), x, y, logicalSize, dpi, ThemeService.Tokens.HealthColors(health).Text, backdrop);
    }

    public static void State(Graphics g, StateIcon state, float x, float y, int logicalSize, int dpi, Color color)
    {
        float s = Scale(dpi);
        float scale = logicalSize * s / 16f;
        float width = 1.5f * scale;
        g.TranslateTransform(x, y);
        if (state == StateIcon.Pending)
        {
            // SVG's round caps extend each 2.2 dash by half the stroke at both ends; GDI+ draws its round
            // dash caps inside the dash, so the visible lengths are passed instead (3.7 on, 0.8 off).
            using Pen dashed = new(color, width) { DashCap = DashCap.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
            dashed.DashPattern = [3.7f / 1.5f, 0.8f / 1.5f];
            float r = 5.75f * scale;
            g.DrawEllipse(dashed, 8 * scale - r, 8 * scale - r, 2 * r, 2 * r);
        }
        else
        {
            (GraphicsPath stroke, GraphicsPath? fill) = StateShapes.Get(state, scale);
            g.DrawPath(R.Pen(color, width, round: true), stroke);
            if (fill is not null)
            {
                g.FillPath(R.Brush(color), fill);
            }
        }

        g.TranslateTransform(-x, -y);
    }

    // --- elevation ---------------------------------------------------------------------------------

    /// <summary>Paints the cached shadow of a rounded rectangle (offset 6 px down, blur 10 px).</summary>
    public static void Shadow(Graphics g, Rectangle rect, int radius, int dpi)
    {
        if (ThemeService.Tokens.Shadow.A == 0 || rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        (Bitmap bitmap, int margin) = R.Shadow(rect.Width, rect.Height, radius, dpi);
        PixelOffsetMode previous = g.PixelOffsetMode;
        InterpolationMode interpolation = g.InterpolationMode;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.DrawImageUnscaled(bitmap, rect.X - margin, rect.Y - margin);
        g.PixelOffsetMode = previous;
        g.InterpolationMode = interpolation;
    }

    /// <summary>A progress ring: a 259° arc (72 % of the circle) from <paramref name="startDegrees"/> (0 = 3 o'clock), clockwise.</summary>
    public static void ProgressRing(Graphics g, float cx, float cy, float radius, float width, float startDegrees, Color color)
    {
        RectangleF box = new(cx - radius, cy - radius, 2 * radius, 2 * radius);
        g.DrawArc(R.Pen(color, width, round: true), box, startDegrees, 259.2f);
    }
}

/// <summary>Cached severity shapes on the 16 px grid (design/icons.md, severity shapes).</summary>
internal static class SeverityShapes
{
    private static readonly Dictionary<(Level, float), Shape> Cache = [];
    private static readonly Lock Gate = new();

    public static void Draw(Graphics g, Level level, float x, float y, float scale, Color color, Color backdrop)
    {
        Shape shape = Get(level, scale);
        ThemeResources r = ThemeService.Resources;
        g.TranslateTransform(x, y);
        g.FillPath(r.Brush(color), shape.Body);
        if (shape.BodyStroke > 0)
        {
            g.DrawPath(r.Pen(color, shape.BodyStroke, round: true), shape.Body);
        }

        g.DrawPath(r.Pen(backdrop, 1.7f * scale, round: true), shape.Glyph);
        if (shape.Dot is not null)
        {
            g.FillPath(r.Brush(backdrop), shape.Dot);
        }

        g.TranslateTransform(-x, -y);
    }

    private static Shape Get(Level level, float scale)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue((level, scale), out Shape? shape))
            {
                return shape;
            }

            GraphicsPath body = new();
            GraphicsPath glyph = new();
            GraphicsPath? dot = null;
            float stroke = 0;

            PointF P(float px, float py) => new(px * scale, py * scale);
            void Dot(float cx, float cy, float rr)
            {
                dot = new GraphicsPath();
                dot.AddEllipse((cx - rr) * scale, (cy - rr) * scale, 2 * rr * scale, 2 * rr * scale);
            }

            switch (level)
            {
                case Level.Critical:
                    body.AddPolygon([P(5, 1.5f), P(11, 1.5f), P(14.5f, 5), P(14.5f, 11), P(11, 14.5f), P(5, 14.5f), P(1.5f, 11), P(1.5f, 5)]);
                    stroke = 1f * scale;
                    glyph.AddLine(P(5.75f, 5.75f), P(10.25f, 10.25f));
                    glyph.StartFigure();
                    glyph.AddLine(P(10.25f, 5.75f), P(5.75f, 10.25f));
                    break;
                case Level.High:
                    body.AddPolygon([P(8, 2.25f), P(14.5f, 13.5f), P(1.5f, 13.5f)]);
                    stroke = 1.5f * scale;
                    glyph.AddLine(P(8, 6.25f), P(8, 9.5f));
                    Dot(8, 11.6f, 0.95f);
                    break;
                case Level.Medium:
                    body.AddPolygon([P(8, 1.5f), P(14.5f, 8), P(8, 14.5f), P(1.5f, 8)]);
                    stroke = 1.2f * scale;
                    glyph.AddLine(P(8, 4.9f), P(8, 8.6f));
                    Dot(8, 10.9f, 0.95f);
                    break;
                case Level.Ok:
                    body.AddEllipse((8 - 6.75f) * scale, (8 - 6.75f) * scale, 13.5f * scale, 13.5f * scale);
                    glyph.AddLines([P(4.9f, 8.2f), P(7.2f, 10.4f), P(11.1f, 5.9f)]);
                    break;
                default:
                    body.AddEllipse((8 - 6.75f) * scale, (8 - 6.75f) * scale, 13.5f * scale, 13.5f * scale);
                    glyph.AddLine(P(8, 7.4f), P(8, 11.2f));
                    Dot(8, 5, 0.95f);
                    break;
            }

            shape = new Shape(body, glyph, dot, stroke);
            Cache[(level, scale)] = shape;
            return shape;
        }
    }

    private sealed record Shape(GraphicsPath Body, GraphicsPath Glyph, GraphicsPath? Dot, float BodyStroke);
}

/// <summary>Cached state icons (skipped, missing, error); pending is drawn dashed.</summary>
internal static class StateShapes
{
    private static readonly Dictionary<(StateIcon, float), (GraphicsPath, GraphicsPath?)> Cache = [];
    private static readonly Lock Gate = new();

    public static readonly IReadOnlyDictionary<StateIcon, string> Source = new Dictionary<StateIcon, string>
    {
        [StateIcon.Skipped] = "C 8 8 5.75|L 5.25 8 10.75 8",
        [StateIcon.Missing] = "C 8 8 5.75|S M6.4 6.5 C6.4 5.5 7.1 4.9 8 4.9 C8.9 4.9 9.6 5.5 9.6 6.4 C9.6 7.5 8 7.6 8 8.9|F 8 11 0.5",
        [StateIcon.Error] = "R 2.5 2.5 11 11 2|L 8 5.25 8 8.75|F 8 10.9 0.5"
    };

    public static (GraphicsPath Stroke, GraphicsPath? Fill) Get(StateIcon state, float scale)
    {
        lock (Gate)
        {
            if (!Cache.TryGetValue((state, scale), out (GraphicsPath, GraphicsPath?) paths))
            {
                paths = IconGeometry.Build(Source[state], scale);
                Cache[(state, scale)] = paths;
            }

            return paths;
        }
    }
}
