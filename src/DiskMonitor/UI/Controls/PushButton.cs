using DiskMonitor.UI.Theming;

namespace DiskMonitor.UI.Controls;

internal enum ButtonKind
{
    /// <summary>The main action of a view: filled with the accent color.</summary>
    Primary,

    /// <summary>An ordinary action: outlined.</summary>
    Secondary,

    /// <summary>A low-emphasis action: no chrome until hovered.</summary>
    Ghost,

    /// <summary>A destructive action.</summary>
    Danger
}

/// <summary>A flat button with an optional leading glyph and clearly distinct hover, pressed, and focus states.</summary>
internal sealed class PushButton : InteractiveSurface
{
    private ButtonKind _kind = ButtonKind.Secondary;
    private string? _glyph;

    public PushButton()
    {
        Height = 32;
    }

    public ButtonKind Kind
    {
        get => _kind;
        set
        {
            _kind = value;
            Invalidate();
        }
    }

    /// <summary>Optional icon-font glyph drawn before the text.</summary>
    public string? Glyph
    {
        get => _glyph;
        set
        {
            _glyph = value;
            Invalidate();
        }
    }

    /// <summary>Width that fits the glyph and text with standard padding.</summary>
    public int PreferredWidth
    {
        get
        {
            int text = Draw.Measure(Text, F(FontRole.BodyBold)).Width;
            int glyph = _glyph is null ? 0 : S(16);
            int gap = _glyph is not null && text > 0 ? S(7) : 0;
            int padding = text > 0 ? S(14) : S(8);
            return glyph + gap + text + padding * 2;
        }
    }

    /// <summary>Sizes the button to its content at the standard height.</summary>
    public PushButton AutoSized()
    {
        Size = new Size(PreferredWidth, S(32));
        return this;
    }

    protected override void OnActivate() => OnClick(EventArgs.Empty);

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        Rectangle bounds = ClientRectangle;
        (Color fill, Color border, Color foreground) = ResolveColors();
        int radius = S(6);

        Draw.FillRounded(graphics, bounds, radius, fill);
        if (border != fill)
        {
            Draw.StrokeRounded(graphics, bounds, radius, border);
        }

        if (FocusVisible)
        {
            Draw.StrokeRounded(graphics, bounds, radius, _kind == ButtonKind.Primary ? P.Text : P.Accent, S(2));
        }

        Font font = F(FontRole.BodyBold);
        int textWidth = Draw.Measure(Text, font).Width;
        int glyphWidth = _glyph is null ? 0 : S(16);
        int gap = _glyph is not null && textWidth > 0 ? S(7) : 0;
        int available = bounds.Width - S(8) * 2;
        int content = Math.Min(available, glyphWidth + gap + textWidth);
        int x = bounds.X + (bounds.Width - content) / 2;

        if (_glyph is not null)
        {
            Draw.Glyph(graphics, _glyph, F(FontRole.Icon), new Rectangle(x, bounds.Y, glyphWidth, bounds.Height), foreground);
            x += glyphWidth + gap;
        }

        Draw.Text(graphics, Text, font, new Rectangle(x, bounds.Y, content - glyphWidth - gap, bounds.Height - 1), foreground);
    }

    private (Color Fill, Color Border, Color Foreground) ResolveColors()
    {
        Palette p = P;
        Color backdrop = Backdrop(p);
        if (!Enabled)
        {
            return _kind switch
            {
                ButtonKind.Ghost => (backdrop, backdrop, p.TextFaint),
                ButtonKind.Secondary => (backdrop, p.Border, p.TextFaint),
                _ => (p.Border, p.Border, p.TextFaint)
            };
        }

        switch (_kind)
        {
            case ButtonKind.Primary:
                Color accent = Pressed ? p.AccentPressed : Hovered ? p.AccentHover : p.Accent;
                return (accent, accent, Hovered && !Pressed && p.IsDark ? Color.FromArgb(8, 16, 32) : p.OnAccent);

            case ButtonKind.Danger:
                Color danger = Pressed ? Draw.Blend(p.Critical, Color.Black, 0.28) : Hovered ? Draw.Blend(p.Critical, Color.White, 0.22) : p.Critical;
                return (danger, danger, Color.White);

            case ButtonKind.Ghost:
                if (Pressed)
                {
                    return (p.SurfacePressed, p.BorderStrong, p.Text);
                }

                return Hovered ? (p.SurfaceHover, p.BorderStrong, p.Text) : (backdrop, backdrop, p.TextMuted);

            default:
                if (Pressed)
                {
                    return (p.SurfacePressed, p.Accent, p.Text);
                }

                return Hovered ? (p.SurfaceHover, p.BorderStrong, p.Text) : (p.SurfaceAlt, p.Border, p.Text);
        }
    }
}
