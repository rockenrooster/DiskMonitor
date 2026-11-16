using DiskMonitor.UI.Theming;

namespace DiskMonitor.UI.Controls;

internal enum ButtonVariant
{
    Primary,
    Secondary,
    Subtle,
    Danger
}

/// <summary>How a button looks right now: the state columns of the components sheet.</summary>
internal readonly record struct ButtonLook(bool Hover, bool Pressed, bool Disabled, bool Focus);

/// <summary>
/// The kit button (subplan 02, Buttons): 32 px (compact 28), radius 4, 12 px padding, optional 16 px
/// icon, label, optional chevron; Primary, Secondary, Subtle and Danger. Without a label it is the
/// 32 × 32 icon button, whose tooltip is mandatory.
/// </summary>
internal sealed class KitButton : InteractiveKitControl
{
    private ButtonVariant _variant = ButtonVariant.Secondary;
    private IconName? _icon;
    private bool _chevron;
    private bool _compact;
    private bool _busy;
    private Color? _iconColor;

    public KitButton()
    {
        AccessibleRole = AccessibleRole.PushButton;
    }

    public ButtonVariant Variant
    {
        get => _variant;
        set { _variant = value; Invalidate(); }
    }

    public IconName? Icon
    {
        get => _icon;
        set { _icon = value; Relayout(); }
    }

    /// <summary>Overrides the icon colour (the rail's Update item draws its icon in Accent).</summary>
    public Color? IconColor
    {
        get => _iconColor;
        set { _iconColor = value; Invalidate(); }
    }

    public bool ShowChevron
    {
        get => _chevron;
        set { _chevron = value; Relayout(); }
    }

    public bool Compact
    {
        get => _compact;
        set { _compact = value; Relayout(); }
    }

    /// <summary>Busy: disabled, with a 16 px progress ring in place of the icon.</summary>
    public bool Busy
    {
        get => _busy;
        set
        {
            if (_busy == value)
            {
                return;
            }

            _busy = value;
            if (value)
            {
                KitAnimator.Add(OnAnimate);
            }
            else
            {
                KitAnimator.Remove(OnAnimate);
            }

            Relayout();
        }
    }

    /// <summary>Why the button is disabled; shown as its tooltip (never disable a button without one).</summary>
    public string? DisabledReason { get; set; }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => base.Text;
        set
        {
            base.Text = value;
            AccessibleName = value;
            Relayout();
        }
    }

    public int ButtonHeight => S(_compact ? 28 : 32);

    /// <summary>The width the anatomy asks for: content plus 12 px on each side, minimum 64 (32 without a label).</summary>
    public int PreferredWidth => MeasureWidth(Text, Icon is not null || Busy, ShowChevron, Variant, Compact, Dpi);

    public static int MeasureWidth(string text, bool hasIcon, bool chevron, ButtonVariant variant, bool compact, int dpi)
    {
        if (text.Length == 0)
        {
            return ThemeService.S(dpi, compact ? 28 : 32);
        }

        float s = dpi / 96f;
        Font font = ThemeService.Resources.Font(LabelRole(variant), dpi);
        float content = Draw.TextWidth(text, font) + (hasIcon ? 24 * s : 0) + (chevron ? 20 * s : 0);
        return Math.Max((int)Math.Round(content + 24 * s), ThemeService.S(dpi, 64));
    }

    public static FontRole LabelRole(ButtonVariant variant) => variant is ButtonVariant.Primary or ButtonVariant.Danger ? FontRole.BodyStrong : FontRole.Body;

    public void SizeToContent() => Size = new Size(PreferredWidth, ButtonHeight);

    protected override void OnMouseHover(EventArgs e)
    {
        string? previous = ToolTipText;
        if (!Enabled && DisabledReason is not null)
        {
            ToolTipText = DisabledReason;
        }
        else if (Text.Length == 0 && ToolTipText is null)
        {
            ToolTipText = AccessibleName;
        }

        base.OnMouseHover(e);
        ToolTipText = previous;
    }

    protected override void PaintContent(Graphics g)
    {
        ButtonLook look = new(ShowHover, ShowPressed, ShowDisabled || Busy, false);
        Render(g, ClientRectangle, Variant, Text, Busy ? null : Icon, ShowChevron, look, Dpi, Busy, KitAnimator.Now, IconColor);
    }

    /// <summary>Paints a button in a state; used by the control and by the render harness.</summary>
    public static void Render(Graphics g, Rectangle r, ButtonVariant variant, string text, IconName? icon, bool chevron, ButtonLook look, int dpi, bool busy = false, long now = 0, Color? iconColor = null)
    {
        ThemeTokens t = ThemeService.Tokens;
        float s = dpi / 96f;
        float radius = 4 * s;
        RectangleF box = r;
        Color content;
        if (variant is ButtonVariant.Primary or ButtonVariant.Danger)
        {
            bool danger = variant == ButtonVariant.Danger;
            Color fill = look.Disabled ? t.Hover
                : look.Pressed ? (danger ? t.DangerPressed : t.AccentPressed)
                : look.Hover ? (danger ? t.DangerHover : t.AccentHover)
                : (danger ? t.DangerFill : t.Accent);
            Color on = danger ? t.OnDanger : t.OnAccent;
            Draw.FillRounded(g, box, radius, fill);
            if (!look.Disabled && (look.Hover || look.Pressed))
            {
                Draw.StrokeRounded(g, box, radius, on, s);
            }

            if (t.IsHighContrast && !look.Disabled)
            {
                Draw.StrokeRounded(g, box, radius, look.Hover ? t.Accent : t.Text, look.Hover ? 2 * s : s);
            }

            content = look.Disabled ? t.TextDisabled : on;
        }
        else
        {
            bool subtle = variant == ButtonVariant.Subtle;
            Color fill = look.Disabled ? (subtle ? Color.Empty : t.Bg)
                : look.Pressed ? t.Pressed
                : look.Hover ? t.Hover
                : subtle ? Color.Empty : t.Surface;
            Color border = look.Disabled ? (subtle ? Color.Empty : t.Stroke)
                : look.Pressed ? t.Accent
                : look.Hover ? t.StrokeStrong
                : subtle ? Color.Empty : t.Stroke;
            if (t.IsHighContrast && look.Hover && !look.Disabled)
            {
                border = t.Accent;
            }

            if (!fill.IsEmpty)
            {
                Draw.FillRounded(g, box, radius, fill);
            }

            if (!border.IsEmpty)
            {
                Draw.StrokeRounded(g, box, radius, border, t.IsHighContrast && look.Hover ? 2 * s : s);
            }

            content = look.Disabled ? t.TextDisabled : t.Text;
        }

        int dy = look.Pressed && !look.Disabled ? (int)Math.Round(s) : 0;
        Font font = ThemeService.Resources.Font(LabelRole(variant), dpi);
        bool hasIcon = icon is not null || busy;
        float textWidth = text.Length == 0 ? 0 : Draw.TextWidth(text, font);
        float contentWidth = (hasIcon ? 16 * s : 0) + (hasIcon && text.Length > 0 ? 8 * s : 0) + textWidth + (chevron ? (text.Length > 0 ? 4 * s : 0) + 16 * s : 0);
        float x = r.X + (r.Width - contentWidth) / 2f;
        float centerY = r.Y + r.Height / 2f + dy;
        if (busy)
        {
            float phase = now % 1000 / 1000f * 360f;
            Draw.ProgressRing(g, x + 8 * s, centerY, 7 * s, 2 * s, phase - 60, t.Accent);
            x += 16 * s + (text.Length > 0 ? 8 * s : 0);
        }
        else if (icon is { } shown)
        {
            Draw.Icon(g, shown, x, r.Y + (r.Height - 16 * s) / 2f + dy, 16, dpi, iconColor is { } custom && !look.Disabled ? custom : content);
            x += 16 * s + (text.Length > 0 ? 8 * s : 0);
        }

        if (text.Length > 0)
        {
            Draw.Text(g, text, font, x, centerY, content);
            x += textWidth + 4 * s;
        }

        if (chevron)
        {
            Draw.Icon(g, IconName.ChevronDown, x, r.Y + (r.Height - 16 * s) / 2f + dy, 16, dpi, content);
        }

        if (look.Focus)
        {
            Draw.FocusRing(g, box, radius, dpi);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            KitAnimator.Remove(OnAnimate);
        }

        base.Dispose(disposing);
    }

    private void OnAnimate(long now) => Invalidate();

    private void Relayout()
    {
        Invalidate();
        Parent?.PerformLayout();
    }
}

/// <summary>
/// The split button: primary colours; the left part runs the action, the right 29 px part opens a menu.
/// A 1 px divider in OnAccent at 35 % opacity, inset 6 px top and bottom. Each part has its own hover.
/// </summary>
internal sealed class SplitButton : InteractiveKitControl
{
    private int _hoverPart = -1;
    private int _pressedPart = -1;
    private IconName? _icon = IconName.Play;

    public SplitButton()
    {
        AccessibleRole = AccessibleRole.SplitButton;
        SetStyle(ControlStyles.StandardClick, false);
    }

    /// <summary>Raised when the right part is clicked, or Down/Alt+Down is pressed.</summary>
    public event EventHandler? MenuRequested;

    public IconName? Icon
    {
        get => _icon;
        set { _icon = value; Invalidate(); }
    }

    public string? DisabledReason { get; set; }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => base.Text;
        set { base.Text = value; AccessibleName = value; Invalidate(); Parent?.PerformLayout(); }
    }

    public int PreferredWidth => MeasureLeft(Text, Dpi) + S(29);

    public static int MeasureLeft(string text, int dpi)
    {
        float s = dpi / 96f;
        return (int)Math.Round(12 * s + 16 * s + 8 * s + Draw.TextWidth(text, ThemeService.Resources.Font(FontRole.BodyStrong, dpi)) + 12 * s);
    }

    /// <summary>Screen rectangle of the whole button, for placing its menu below it.</summary>
    public Rectangle ScreenBounds => RectangleToScreen(ClientRectangle);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int part = !ClientRectangle.Contains(e.Location) ? -1 : e.X >= Width - S(29) ? 1 : 0;
        if (part != _hoverPart)
        {
            _hoverPart = part;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverPart = -1;
        _pressedPart = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && Enabled)
        {
            _pressedPart = e.X >= Width - S(29) ? 1 : 0;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        int pressed = _pressedPart;
        _pressedPart = -1;
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && Enabled && ClientRectangle.Contains(e.Location))
        {
            bool right = e.X >= Width - S(29);
            if (pressed == 1 && right)
            {
                MenuRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (pressed == 0 && !right)
            {
                OnClick(EventArgs.Empty);
            }
        }

        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Down && Enabled)
        {
            e.Handled = true;
            MenuRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.OnKeyDown(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData == Keys.Down || base.IsInputKey(keyData);

    protected override void OnMouseHover(EventArgs e)
    {
        string? previous = ToolTipText;
        if (!Enabled && DisabledReason is not null)
        {
            ToolTipText = DisabledReason;
        }

        base.OnMouseHover(e);
        ToolTipText = previous;
    }

    protected override void PaintContent(Graphics g)
    {
        bool forcedHover = Preview.HasFlag(PreviewState.Hover);
        bool forcedPressed = Preview.HasFlag(PreviewState.Pressed);
        int hover = forcedHover || forcedPressed ? 2 : ShowDisabled ? -1 : _hoverPart;
        int pressed = forcedPressed ? 2 : _pressedPart == _hoverPart ? _pressedPart : -1;
        Render(g, ClientRectangle, Text, Icon, hover, pressed, ShowDisabled, false, Dpi);
    }

    /// <summary>Paints the split button. Part 0 is the action, 1 the menu, 2 both (harness).</summary>
    public static void Render(Graphics g, Rectangle r, string text, IconName? icon, int hoverPart, int pressedPart, bool disabled, bool focus, int dpi)
    {
        ThemeTokens t = ThemeService.Tokens;
        float s = dpi / 96f;
        float radius = 4 * s;
        float split = r.Right - 29 * s;
        Color Fill(int part) => disabled ? t.Hover
            : pressedPart == part || pressedPart == 2 ? t.AccentPressed
            : hoverPart == part || hoverPart == 2 ? t.AccentHover
            : t.Accent;

        Draw.FillRounded(g, r, radius, Fill(0));
        if (Fill(1) != Fill(0))
        {
            Region clip = g.Clip;
            g.SetClip(new RectangleF(split, r.Y, r.Right - split, r.Height));
            Draw.FillRounded(g, r, radius, Fill(1));
            g.Clip = clip;
        }

        Color content = disabled ? t.TextDisabled : t.OnAccent;
        int dy0 = pressedPart is 0 or 2 && !disabled ? (int)Math.Round(s) : 0;
        int dy1 = pressedPart is 1 or 2 && !disabled ? (int)Math.Round(s) : 0;
        float x = r.X + 12 * s;
        if (icon is { } shown)
        {
            Draw.Icon(g, shown, x, r.Y + (r.Height - 16 * s) / 2f + dy0, 16, dpi, content);
        }

        Draw.Text(g, text, ThemeService.Resources.Font(FontRole.BodyStrong, dpi), x + 24 * s, r.Y + r.Height / 2f + dy0, content);
        float divider = (float)Math.Floor(split) + 0.5f;
        Draw.Line(g, Color.FromArgb(89, content), s, divider, r.Y + 6 * s, divider, r.Bottom - 6 * s);
        Draw.Icon(g, IconName.ChevronDown, (float)Math.Round(split + 6.5f * s), r.Y + (r.Height - 16 * s) / 2f + dy1, 16, dpi, content);
        if (focus)
        {
            Draw.FocusRing(g, r, radius, dpi);
        }
    }
}
