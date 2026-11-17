using DiskMonitor.Core.Drives;
using DiskMonitor.Core.Events;
using DiskMonitor.UI.Theming;

namespace DiskMonitor.UI.Controls;

/// <summary>Painters for badges and pills (subplan 02, Badges, pills, chips). Positions are device pixels.</summary>
internal static class Badges
{
    /// <summary>Width of a severity or health badge: 6, 12 px shape, 4, word, 8.</summary>
    public static int SeverityWidth(string word, int dpi)
    {
        float s = dpi / 96f;
        return (int)Math.Round(30 * s + Draw.TextWidth(word, ThemeService.Resources.Font(FontRole.CaptionStrong, dpi)));
    }

    /// <summary>Paints a severity badge (height 20, radius 10) with its top-left at (x, y); returns its width.</summary>
    public static int Severity(Graphics g, float x, float y, Level level, string word, int dpi)
    {
        ThemeTokens t = ThemeService.Tokens;
        float s = dpi / 96f;
        (Color text, _, Color pill) = t.LevelColors(level);
        int width = SeverityWidth(word, dpi);
        RectangleF box = new(x, y, width, 20 * s);
        Draw.FillRounded(g, box, 10 * s, pill);
        if (t.IsHighContrast)
        {
            Draw.StrokeRounded(g, box, 10 * s, t.Text, s);
        }

        if (level == Level.Neutral)
        {
            Draw.State(g, StateIcon.Skipped, x + 6 * s, y + 4 * s, 12, dpi, t.Text3);
        }
        else
        {
            Draw.Severity(g, level, x + 6 * s, y + 4 * s, 12, dpi, text, pill);
        }

        Draw.Text(g, word, ThemeService.Resources.Font(FontRole.CaptionStrong, dpi), x + 22 * s, y + 10 * s, text);
        return width;
    }

    public static int Tier(Graphics g, float x, float y, Tier tier, int dpi) =>
        Severity(g, x, y, tier.ToLevel(), App.Strings.TierName(tier), dpi);

    public static int Health(Graphics g, float x, float y, HealthStatus health, int dpi) =>
        Severity(g, x, y, health.ToLevel(), App.Strings.HealthName(health), dpi);

    public static int HealthWidth(HealthStatus health, int dpi) => SeverityWidth(App.Strings.HealthName(health), dpi);

    public static int PillWidth(string text, int dpi) =>
        (int)Math.Round(16 * dpi / 96f + Draw.TextWidth(text, ThemeService.Resources.Font(FontRole.CaptionStrong, dpi)));

    /// <summary>Paints a pill (height 20, radius 10, 8 px padding, CaptionStrong); returns its width.</summary>
    public static int Pill(Graphics g, float x, float y, string text, Level level, int dpi, bool centered = false)
    {
        ThemeTokens t = ThemeService.Tokens;
        float s = dpi / 96f;
        (Color textColor, _, Color fill) = t.LevelColors(level);
        int width = PillWidth(text, dpi);
        RectangleF box = new(x, y, width, 20 * s);
        Draw.FillRounded(g, box, 10 * s, fill);
        if (t.IsHighContrast)
        {
            Draw.StrokeRounded(g, box, 10 * s, t.Text, s);
        }

        Font font = ThemeService.Resources.Font(FontRole.CaptionStrong, dpi);
        if (centered)
        {
            Draw.TextCentered(g, text, font, x + width / 2f, y + 10 * s, textColor);
        }
        else
        {
            Draw.Text(g, text, font, x + 8 * s, y + 10 * s, textColor);
        }

        return width;
    }
}

/// <summary>
/// A filter chip: 28 px, radius 14, 12 px padding; an optional 14 px severity shape or 16 px icon, the
/// label (BodyStrong when on), an optional count in Text3. Toggles on click, Space and Enter.
/// </summary>
internal sealed class Chip : InteractiveKitControl
{
    private bool _on;
    private string? _count;
    private Level? _severity;
    private IconName? _icon;

    public Chip()
    {
        AccessibleRole = AccessibleRole.CheckButton;
        Backdrop = static t => t.Bg;
    }

    public event EventHandler? Toggled;

    public bool On
    {
        get => _on || Preview.HasFlag(PreviewState.Selected);
        set
        {
            if (_on != value)
            {
                _on = value;
                Invalidate();
                Parent?.PerformLayout();
            }
        }
    }

    /// <summary>Optional count after the label, for example "325".</summary>
    public string? Count
    {
        get => _count;
        set
        {
            if (_count != value)
            {
                _count = value;
                Invalidate();
                Parent?.PerformLayout();
            }
        }
    }

    public Level? Severity
    {
        get => _severity;
        set { _severity = value; Invalidate(); }
    }

    public IconName? Icon
    {
        get => _icon;
        set { _icon = value; Invalidate(); }
    }

    /// <summary>When false a click only raises <see cref="Toggled"/>; the owner decides the new state.</summary>
    public bool AutoToggle { get; set; } = true;

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => base.Text;
        set
        {
            if (base.Text != (value ?? ""))
            {
                base.Text = value;
                AccessibleName = value;
                Invalidate();
                Parent?.PerformLayout();
            }
        }
    }

    public int PreferredWidth => Measure(Text, Count, Severity is not null || Icon is not null, On, Dpi);

    public static int Measure(string text, string? count, bool hasSlot, bool on, int dpi)
    {
        float s = dpi / 96f;
        float width = 24 * s + (hasSlot ? 20 * s : 0)
            + Draw.TextWidth(text, ThemeService.Resources.Font(on ? FontRole.BodyStrong : FontRole.Body, dpi))
            + (string.IsNullOrEmpty(count) ? 0 : 6 * s + Draw.TextWidth(count, ThemeService.Resources.Font(FontRole.Body, dpi)));
        return (int)Math.Round(width);
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (AutoToggle)
        {
            On = !On;
        }

        Toggled?.Invoke(this, EventArgs.Empty);
    }

    protected override void PaintContent(Graphics g) =>
        Render(g, ClientRectangle, Text, Count, Severity, Icon, On, ShowHover, false, ShowDisabled, Dpi);

    public override float FocusRingRadius => Height / 2f;

    public static void Render(Graphics g, Rectangle r, string text, string? count, Level? severity, IconName? icon, bool on, bool hover, bool focus, bool disabled, int dpi)
    {
        ThemeTokens t = ThemeService.Tokens;
        float s = dpi / 96f;
        float radius = r.Height / 2f;
        Color fill = on ? t.Selected : hover && !disabled ? t.Hover : t.Surface;
        Color border = on ? t.Accent : hover && !disabled ? t.StrokeStrong : t.Stroke;
        Draw.FillRounded(g, r, radius, fill);
        Draw.StrokeRounded(g, r, radius, t.IsHighContrast && hover ? t.Accent : border, t.IsHighContrast && hover ? 2 * s : s);

        float x = r.X + 12 * s;
        float centerY = r.Y + r.Height / 2f;
        if (severity is { } level)
        {
            (Color shape, _, _) = t.LevelColors(level);
            Draw.Severity(g, level, x, centerY - 7 * s, 14, dpi, disabled ? t.TextDisabled : shape, fill);
            x += 20 * s;
        }
        else if (icon is { } shown)
        {
            Draw.Icon(g, shown, x - s, centerY - 8 * s, 16, dpi, disabled ? t.TextDisabled : t.Text2);
            x += 20 * s;
        }

        Color textColor = disabled ? t.TextDisabled : on ? t.OnSelected : t.Text;
        Font font = ThemeService.Resources.Font(on ? FontRole.BodyStrong : FontRole.Body, dpi);
        Draw.Text(g, text, font, x, centerY, textColor);
        if (!string.IsNullOrEmpty(count))
        {
            x += Draw.TextWidth(text, font) + 6 * s;
            Draw.Text(g, count, ThemeService.Resources.Font(FontRole.Body, dpi), x, centerY, disabled ? t.TextDisabled : on && t.IsHighContrast ? t.OnSelected : t.Text3);
        }

        if (focus)
        {
            Draw.FocusRing(g, r, radius, dpi);
        }
    }
}
