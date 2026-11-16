using System.Drawing.Drawing2D;
using DiskMonitor.Core.Events;
using DiskMonitor.UI.Theming;

namespace DiskMonitor.UI.Controls;

/// <summary>Painters for the data graphics of subplan 02. Coordinates are device pixels.</summary>
internal static class DataGraphics
{
    /// <summary>Progress bar: radius half the height, track Stroke, fill Accent; a non-zero value is never narrower than the height.</summary>
    public static void ProgressBar(Graphics g, RectangleF r, double? fraction, long now, Color? fill = null)
    {
        ThemeTokens t = ThemeService.Tokens;
        float radius = r.Height / 2f;
        Draw.FillRounded(g, r, radius, t.Stroke);
        Color color = fill ?? t.Accent;
        if (fraction is { } value)
        {
            if (value > 0)
            {
                float width = Math.Max(r.Height, (float)(r.Width * Math.Clamp(value, 0, 1)));
                Draw.FillRounded(g, new RectangleF(r.X, r.Y, width, r.Height), radius, color);
            }
        }
        else
        {
            // Indeterminate: a segment 40 % of the track travels left to right in 1.5 s, ease-in-out.
            float phase = now % 1500 / 1500f;
            float eased = phase < 0.5f ? 2 * phase * phase : 1 - MathF.Pow(-2 * phase + 2, 2) / 2;
            float segment = r.Width * 0.4f;
            float x = r.X - segment + eased * (r.Width + segment);
            RectangleF bar = RectangleF.Intersect(new RectangleF(x, r.Y, segment, r.Height), r);
            if (bar.Width > 0)
            {
                Draw.FillRounded(g, bar, radius, color);
            }
        }
    }

    /// <summary>
    /// Progress ring of outer radius <paramref name="outerRadius"/> (8, or 10 beside a Subtitle): a 2 px arc of
    /// 72 % of the circle, rotating clockwise once per second.
    /// </summary>
    public static void Ring(Graphics g, float cx, float cy, float outerRadius, int dpi, long now, Color? color = null)
    {
        float s = dpi / 96f;
        float angle = now % 1000 / 1000f * 360f - 60f;
        Draw.ProgressRing(g, cx, cy, outerRadius - s, 2 * s, angle, color ?? ThemeService.Tokens.Accent);
    }

    /// <summary>
    /// Severity bar: one segment per tier with a count (Critical first), widths proportional with a 4 px
    /// minimum, 2 px gaps, radius 2. Returns each segment's rectangle for hit-testing and tooltips.
    /// </summary>
    public static List<(Tier Tier, RectangleF Bounds)> SeverityBar(Graphics? g, RectangleF r, IReadOnlyDictionary<Tier, int> counts, int dpi)
    {
        ThemeTokens t = ThemeService.Tokens;
        float s = dpi / 96f;
        List<(Tier, RectangleF)> segments = [];
        Tier[] present = TierExtensions.Descending.Where(tier => counts.GetValueOrDefault(tier) > 0).ToArray();
        if (present.Length == 0)
        {
            return segments;
        }

        float gaps = (present.Length - 1) * 2 * s;
        float available = Math.Max(0, r.Width - gaps);
        float minimum = 4 * s;
        int total = present.Sum(tier => counts[tier]);
        float[] widths = present.Select(tier => available * counts[tier] / total).ToArray();

        // Lift small segments to the minimum, taking the difference from the widest.
        for (int i = 0; i < widths.Length; i++)
        {
            if (widths[i] < minimum)
            {
                float lift = minimum - widths[i];
                widths[i] = minimum;
                int widest = Array.IndexOf(widths, widths.Max());
                widths[widest] = Math.Max(minimum, widths[widest] - lift);
            }
        }

        float x = r.X;
        for (int i = 0; i < present.Length; i++)
        {
            RectangleF segment = new(x, r.Y, widths[i], r.Height);
            if (g is not null)
            {
                Draw.FillRounded(g, segment, 2 * s, t.TierFill(present[i]));
            }

            segments.Add((present[i], segment));
            x += widths[i] + 2 * s;
        }

        return segments;
    }

    /// <summary>Donut: outer radius 40, thickness 10 (compact 22 and 6); track Stroke; value arc from 12 o'clock, round ends.</summary>
    public static void Donut(Graphics g, float cx, float cy, float outerRadius, float thickness, double fraction, Color color)
    {
        ThemeTokens t = ThemeService.Tokens;
        float r = outerRadius - thickness / 2f;
        RectangleF box = new(cx - r, cy - r, 2 * r, 2 * r);
        g.DrawEllipse(ThemeService.Resources.Pen(t.Stroke, thickness), box);
        double clamped = Math.Clamp(fraction, 0, 1);
        if (clamped >= 0.999)
        {
            g.DrawEllipse(ThemeService.Resources.Pen(color, thickness), box);
        }
        else if (clamped > 0)
        {
            g.DrawArc(ThemeService.Resources.Pen(color, thickness, round: true), box, -90, (float)(clamped * 360));
        }
    }

    /// <summary>Sparkline: a 1.5 px Accent line, the area beneath at 14 % opacity, a dot r 2.5 on the last point.</summary>
    public static void Sparkline(Graphics g, RectangleF r, IReadOnlyList<double> values, int dpi)
    {
        if (values.Count < 2)
        {
            return;
        }

        ThemeTokens t = ThemeService.Tokens;
        float s = dpi / 96f;
        double max = Math.Max(1e-9, values.Max());
        PointF[] points = new PointF[values.Count];
        for (int i = 0; i < values.Count; i++)
        {
            float x = r.X + r.Width * i / (values.Count - 1);
            float y = r.Bottom - (float)(values[i] / max) * (r.Height - 3 * s) - 0.5f * s;
            points[i] = new PointF(x, y);
        }

        using GraphicsPath area = new();
        area.AddLines(points);
        area.AddLine(points[^1], new PointF(r.Right, r.Bottom));
        area.AddLine(new PointF(r.Right, r.Bottom), new PointF(r.X, r.Bottom));
        area.CloseFigure();
        g.FillPath(ThemeService.Resources.Brush(Color.FromArgb(36, t.Accent)), area);
        g.DrawLines(ThemeService.Resources.Pen(t.Accent, 1.5f * s, round: true), points);
        Draw.Circle(g, t.Accent, points[^1].X, points[^1].Y, 2.5f * s);
    }

    /// <summary>One timeline entry: a circle r 4 on a 1 px Stroke line, text Body, time Caption Text3 right-aligned.</summary>
    public static void TimelineEntry(Graphics g, float lineX, float centerY, float textX, float rightX, string text, string time, bool first, bool last, float pitch, int dpi, Color? textColor = null)
    {
        ThemeTokens t = ThemeService.Tokens;
        float s = dpi / 96f;
        float line = (float)Math.Floor(lineX) + 0.5f;
        if (!first)
        {
            Draw.Line(g, t.Stroke, s, line, centerY - pitch / 2f, line, centerY);
        }

        if (!last)
        {
            Draw.Line(g, t.Stroke, s, line, centerY, line, centerY + pitch / 2f);
        }

        Draw.Circle(g, t.Surface, line, centerY, 4 * s);
        Draw.Ring(g, t.StrokeStrong, 1.5f * s, line, centerY, 4 * s - 0.75f * s);
        Font caption = ThemeService.Resources.Font(FontRole.Caption, dpi);
        int timeWidth = Draw.TextWidth(time, caption);
        Draw.Text(g, text, ThemeService.Resources.Font(FontRole.Body, dpi), textX, centerY, textColor ?? t.Text, (int)(rightX - textX - timeWidth - 8 * s));
        Draw.Text(g, time, caption, rightX - timeWidth, centerY, t.Text3);
    }

    /// <summary>
    /// Metric tile, 128 × 52, radius 4, Bg inside a card: label CaptionStrong Text3 at +12 on y 16, value
    /// BodyStrong at +12 on y 36. An out-of-range value gets the tier's 12 px shape and moves to +28.
    /// </summary>
    public static void MetricTile(Graphics g, RectangleF r, string label, string value, Tier? flagged, int dpi, bool hover = false)
    {
        ThemeTokens t = ThemeService.Tokens;
        float s = dpi / 96f;
        Draw.FillRounded(g, r, 4 * s, hover ? t.Hover : t.Bg);
        if (t.IsHighContrast)
        {
            Draw.StrokeRounded(g, r, 4 * s, t.Text, s);
        }

        Draw.Text(g, label, ThemeService.Resources.Font(FontRole.CaptionStrong, dpi), r.X + 12 * s, r.Y + 16 * s, t.Text3, (int)(r.Width - 16 * s));
        Font strong = ThemeService.Resources.Font(FontRole.BodyStrong, dpi);
        if (flagged is { } tier)
        {
            Draw.Severity(g, tier, r.X + 12 * s, r.Y + 30 * s, 12, dpi, hover ? t.Hover : t.Bg);
            Draw.Text(g, value, strong, r.X + 28 * s, r.Y + 36 * s, t.TierText(tier), (int)(r.Width - 32 * s));
        }
        else
        {
            Draw.Text(g, value, strong, r.X + 12 * s, r.Y + 36 * s, t.Text, (int)(r.Width - 16 * s));
        }
    }

    /// <summary>
    /// Usage bar: progress-bar anatomy, track Stroke; fill StrokeStrong normally, MedFill at 90 % used or more,
    /// HighFill below the low-space threshold. Free space is always written beside it.
    /// </summary>
    public static void UsageBar(Graphics g, RectangleF r, double usedFraction, double freePercent, int lowSpacePercent)
    {
        ThemeTokens t = ThemeService.Tokens;
        Color fill = lowSpacePercent > 0 && freePercent < lowSpacePercent ? t.HighFill
            : usedFraction >= 0.9 ? t.MedFill
            : t.StrokeStrong;
        ProgressBar(g, r, usedFraction, 0, fill);
    }
}

/// <summary>An animated progress ring control (radius 8, or 10 beside a Subtitle).</summary>
internal sealed class ProgressRingControl : KitControl
{
    private bool _running;

    public int OuterRadius { get; set; } = 8;

    public bool Running
    {
        get => _running;
        set
        {
            if (_running == value)
            {
                return;
            }

            _running = value;
            if (value && Visible)
            {
                KitAnimator.Add(OnTick);
            }
            else
            {
                KitAnimator.Remove(OnTick);
            }

            Invalidate();
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible && _running)
        {
            KitAnimator.Add(OnTick);
        }
        else
        {
            KitAnimator.Remove(OnTick);
        }
    }

    protected override void PaintContent(Graphics g) =>
        DataGraphics.Ring(g, Width / 2f, Height / 2f, Sf(OuterRadius), Dpi, KitAnimator.Now);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            KitAnimator.Remove(OnTick);
        }

        base.Dispose(disposing);
    }

    private void OnTick(long now) => Invalidate();
}
