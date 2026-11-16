using DiskMonitor.UI.Theming;

namespace DiskMonitor.UI.Controls;

/// <summary>Painters for cards, info bars, dialogs, toasts, empty states and skeletons (subplan 02).</summary>
internal static class Surfaces
{
    /// <summary>Card: Surface, 1 px Stroke, radius 8; title CaptionStrong Text3 at +16, centred 22 px below the top.</summary>
    public static void Card(Graphics g, RectangleF r, string? title, int dpi, bool hover = false, bool tile = false)
    {
        ThemeTokens t = ThemeService.Tokens;
        float s = dpi / 96f;
        Draw.FillRounded(g, r, 8 * s, hover && tile ? t.Hover : t.Surface);
        Draw.StrokeRounded(g, r, 8 * s, t.IsHighContrast && hover ? t.Accent : hover ? t.StrokeStrong : t.Stroke, t.IsHighContrast && hover ? 2 * s : s);
        if (!string.IsNullOrEmpty(title))
        {
            Draw.Text(g, title, ThemeService.Resources.Font(FontRole.CaptionStrong, dpi), r.X + 16 * s, r.Y + 22 * s, t.Text3, (int)(r.Width - 32 * s));
        }
    }

    /// <summary>
    /// Info bar: radius 4, the level's Pill fill, a 3 px Fill bar on the left, a 16 px shape at +14, text Body
    /// at +40, an optional action in BodyStrong Accent with a 1 px underline, 16 px from the right.
    /// Returns the action's bounds (empty when there is none).
    /// </summary>
    public static RectangleF InfoBar(Graphics g, RectangleF r, Level level, string text, string? action, int dpi, bool actionHover = false, float rightReserve = 0)
    {
        ThemeTokens t = ThemeService.Tokens;
        float s = dpi / 96f;
        (Color shape, Color fill, Color pill) = t.LevelColors(level == Level.Neutral ? Level.Low : level);
        Draw.FillRounded(g, r, 4 * s, pill);
        if (t.IsHighContrast)
        {
            Draw.StrokeRounded(g, r, 4 * s, t.Text, s);
        }

        Draw.FillRounded(g, new RectangleF(r.X, r.Y, 3 * s, r.Height), 1.5f * s, fill);
        Draw.Severity(g, level == Level.Neutral ? Level.Low : level, r.X + 14 * s, r.Y + (r.Height - 16 * s) / 2f, 16, dpi, shape, pill);
        float centerY = r.Y + r.Height / 2f;
        RectangleF actionBounds = RectangleF.Empty;
        float textRight = r.Right - 16 * s - rightReserve;
        if (!string.IsNullOrEmpty(action))
        {
            Font strong = ThemeService.Resources.Font(FontRole.BodyStrong, dpi);
            int width = Draw.TextWidth(action, strong);
            float x = r.Right - 16 * s - rightReserve - width;
            Draw.Text(g, action, strong, x, centerY, actionHover ? t.AccentHover : t.Accent);
            float underline = (float)Math.Floor(centerY + 9 * s) + 0.5f;
            Draw.Line(g, actionHover ? t.AccentHover : t.Accent, s, x, underline, x + width, underline);
            actionBounds = new RectangleF(x - 4 * s, r.Y, width + 8 * s, r.Height);
            textRight = x - 16 * s;
        }

        Draw.Text(g, text, ThemeService.Resources.Font(FontRole.Body, dpi), r.X + 40 * s, centerY, t.Text, (int)Math.Max(1, textRight - r.X - 40 * s));
        return actionBounds;
    }

    /// <summary>Dialog surface: Raised, Stroke, radius 8, shadow; title Subtitle centred 34 px below the top.</summary>
    public static void DialogSurface(Graphics g, Rectangle r, string title, int dpi, bool shadow = true)
    {
        ThemeTokens t = ThemeService.Tokens;
        float s = dpi / 96f;
        if (shadow)
        {
            Draw.Shadow(g, r, (int)Math.Round(8 * s), dpi);
            Draw.PrepareShapes(g);
        }

        Draw.FillRounded(g, r, 8 * s, t.Raised);
        Draw.StrokeRounded(g, r, 8 * s, t.Stroke, s);
        if (title.Length > 0)
        {
            Draw.Text(g, title, ThemeService.Resources.Font(FontRole.Subtitle, dpi), r.X + 24 * s, r.Y + 34 * s, t.Text, (int)(r.Width - 48 * s));
        }
    }

    /// <summary>
    /// Toast: 360 × 64, radius 8, Raised, Stroke, shadow; a 3 × 48 bar in the level's Fill; 16 px shape at
    /// (16, 14); title BodyStrong at +44 on y 22; text Caption Text2 on y 43.
    /// </summary>
    public static void ToastContent(Graphics g, Rectangle r, Level? level, string title, string? text, int dpi)
    {
        ThemeTokens t = ThemeService.Tokens;
        float s = dpi / 96f;
        if (level is { } shown)
        {
            (Color shape, Color fill, _) = t.LevelColors(shown);
            Draw.FillRounded(g, new RectangleF(r.X, r.Y + 8 * s, 3 * s, 48 * s), 1.5f * s, fill);
            Draw.Severity(g, shown, r.X + 16 * s, r.Y + 14 * s, 16, dpi, shape, t.Raised);
        }

        float x = r.X + (level is null ? 16 : 44) * s;
        float right = r.Right - 104 * s;
        Draw.Text(g, title, ThemeService.Resources.Font(FontRole.BodyStrong, dpi), x, r.Y + 22 * s, t.Text, (int)(right - x));
        if (!string.IsNullOrEmpty(text))
        {
            Draw.Text(g, text, ThemeService.Resources.Font(FontRole.Caption, dpi), x, r.Y + 43 * s, t.Text2, (int)(right - x));
        }
    }

    /// <summary>
    /// Empty state: a centred column with a 48 px (or 32 px) icon in Text3, the headline in Subtitle and one
    /// or two sentences in Body Text2. Returns the y below the text, where a button may go.
    /// </summary>
    public static float EmptyState(Graphics g, RectangleF area, IconName icon, int iconSize, string headline, string? text, int dpi, float? top = null)
    {
        ThemeTokens t = ThemeService.Tokens;
        float s = dpi / 96f;
        Font body = ThemeService.Resources.Font(FontRole.Body, dpi);
        List<string> lines = string.IsNullOrEmpty(text) ? [] : Draw.Wrap(text, body, (int)Math.Min(area.Width - 32 * s, 520 * s));
        float height = iconSize * s + 18 * s + 6 * s + lines.Count * 24 * s;
        float y = top ?? area.Y + (area.Height - height) / 2f;
        float cx = area.X + area.Width / 2f;
        Draw.Icon(g, icon, cx - iconSize * s / 2f, y, iconSize, dpi, t.Text3);
        y += iconSize * s + 18 * s;
        Draw.TextCentered(g, headline, ThemeService.Resources.Font(FontRole.Subtitle, dpi), cx, y, t.Text);
        y += 24 * s;
        foreach (string line in lines)
        {
            Draw.TextCentered(g, line, body, cx, y, t.Text2);
            y += 24 * s;
        }

        return y - 12 * s;
    }

    /// <summary>A skeleton row: a 16 px circle, a 10 px bar and an 8 px bar at 60 % opacity, in Hover.</summary>
    public static void SkeletonRow(Graphics g, float x, float y, float firstWidth, float secondWidth, int dpi)
    {
        ThemeTokens t = ThemeService.Tokens;
        float s = dpi / 96f;
        Draw.FillRounded(g, new RectangleF(x, y + 2 * s, 16 * s, 16 * s), 8 * s, t.Hover);
        Draw.FillRounded(g, new RectangleF(x + 28 * s, y, firstWidth, 10 * s), 4 * s, t.Hover);
        Draw.FillRounded(g, new RectangleF(x + 28 * s, y + 14 * s, secondWidth, 8 * s), 4 * s, Color.FromArgb(153, t.Hover));
    }
}

/// <summary>
/// A card container: Surface, Stroke, radius 8, 16 px padding, an upper-case title. A card that acts as
/// a button gets StrokeStrong on hover (stat tiles also the Hover fill) and is focusable.
/// </summary>
internal class Card : InteractiveKitControl
{
    private bool _clickable;

    public Card()
    {
        TabStop = false;
        SetStyle(ControlStyles.Selectable, false);
        AccessibleRole = AccessibleRole.Grouping;
    }

    public string? Title { get; set; }

    /// <summary>Stat tiles take the Hover fill on hover, not only the border.</summary>
    public bool IsTile { get; set; }

    public bool Clickable
    {
        get => _clickable;
        set
        {
            _clickable = value;
            TabStop = value;
            SetStyle(ControlStyles.Selectable, value);
            AccessibleRole = value ? AccessibleRole.PushButton : AccessibleRole.Grouping;
        }
    }

    public override bool FocusRingVisible => false;

    /// <summary>Colour children should use as their backdrop.</summary>
    public Color SurfaceColor => _clickable && ShowHover && IsTile ? T.Hover : T.Surface;

    protected override bool ActivatesOnKeys => _clickable;

    protected override void PaintContent(Graphics g)
    {
        Surfaces.Card(g, ClientRectangle, Title, Dpi, _clickable && ShowHover, IsTile);
        PaintCard(g);
        if (_clickable && ShowFocus)
        {
            Draw.FocusRing(g, RectangleF.Inflate(ClientRectangle, -Sf(3), -Sf(3)), Sf(5), Dpi);
        }
    }

    /// <summary>Paints the card's content over its surface.</summary>
    protected virtual void PaintCard(Graphics g)
    {
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        if (_clickable)
        {
            Cursor = Cursors.Default;
        }
    }
}

/// <summary>
/// An info bar control: the painter above plus a clickable action that can hold keyboard focus (Enter
/// runs it) and an optional dismiss button.
/// </summary>
internal sealed class InfoBarControl : InteractiveKitControl
{
    private RectangleF _action;
    private bool _actionHover;
    private bool _closeHover;

    public InfoBarControl()
    {
        AccessibleRole = AccessibleRole.Alert;
        TabStop = false;
    }

    public override bool FocusRingVisible => false;

    public Level Level { get; set; } = Level.Low;

    public string? ActionText { get; set; }

    public bool Dismissible { get; set; }

    public event EventHandler? ActionClicked;

    public event EventHandler? Dismissed;

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => base.Text;
        set { base.Text = value; AccessibleName = value; Invalidate(); }
    }

    public void Configure(Level level, string text, string? action = null, bool dismissible = false)
    {
        Level = level;
        Text = text;
        ActionText = action;
        Dismissible = dismissible;
        TabStop = action is not null;
        SetStyle(ControlStyles.Selectable, action is not null);
        Invalidate();
    }

    protected override void OnClick(EventArgs e)
    {
        Point p = PointToClient(MousePosition);
        if (Dismissible && p.X >= Width - S(44) && MouseButtons == MouseButtons.None && ClientRectangle.Contains(p))
        {
            Dismissed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (ActionText is not null && (_action.Contains(p) || !ClientRectangle.Contains(p)))
        {
            ActionClicked?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space && ActionText is not null)
        {
            e.Handled = true;
            ActionClicked?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (e.KeyCode == Keys.Escape && Dismissible)
        {
            e.Handled = true;
            Dismissed?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool action = _action.Contains(e.Location);
        bool close = Dismissible && e.X >= Width - S(44);
        if (action != _actionHover || close != _closeHover)
        {
            _actionHover = action;
            _closeHover = close;
            Cursor = action ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _actionHover = false;
        _closeHover = false;
        Invalidate();
    }

    protected override void PaintContent(Graphics g)
    {
        float reserve = Dismissible ? S(36) : 0;
        _action = Surfaces.InfoBar(g, ClientRectangle, Level, Text, ActionText, Dpi, _actionHover || (ShowFocus && ActionText is not null), reserve);
        if (Dismissible)
        {
            Rectangle close = new(Width - S(44), (Height - S(28)) / 2, S(28), S(28));
            KitButton.Render(g, close, ButtonVariant.Subtle, "", IconName.Close, false, new ButtonLook(_closeHover, false, false, false), Dpi);
        }

        if (ShowFocus && ActionText is not null && !_action.IsEmpty)
        {
            Draw.FocusRing(g, RectangleF.Inflate(_action, 0, -Sf(8)), Sf(4), Dpi);
        }
    }
}

/// <summary>Empty state as a control, for lists and pages: the painter plus an optional primary button.</summary>
internal sealed class EmptyStateControl : KitControl
{
    private readonly KitButton _button = new() { Variant = ButtonVariant.Primary, Visible = false };

    public EmptyStateControl()
    {
        Controls.Add(_button);
        _button.Click += (_, _) => ButtonClicked?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? ButtonClicked;

    public IconName Icon { get; set; } = IconName.Check;

    public int IconSize { get; set; } = 48;

    public string Headline { get; set; } = "";

    public string? Message { get; set; }

    /// <summary>Shows a 48 px progress ring instead of the icon (the Drives page before its first scan).</summary>
    public bool Loading { get; set; }

    public string? ButtonText
    {
        get => _button.Visible ? _button.Text : null;
        set
        {
            _button.Visible = value is not null;
            _button.Text = value ?? "";
            PerformLayout();
        }
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_button.Visible)
        {
            _button.SizeToContent();
            float bottom = MeasureBottom();
            _button.Location = new Point((Width - _button.Width) / 2, (int)(bottom + S(16)));
        }
    }

    private float MeasureBottom()
    {
        float s = Sc;
        Font body = F(FontRole.Body);
        int lines = string.IsNullOrEmpty(Message) ? 0 : Draw.Wrap(Message, body, (int)Math.Min(Width - 32 * s, 520 * s)).Count;
        float height = IconSize * s + 24 * s + lines * 24 * s + (_button.Visible ? 48 * s : 0);
        float top = (Height - height) / 2f;
        return top + IconSize * s + 18 * s + 24 * s * (lines + 1) - 12 * s;
    }

    protected override void PaintContent(Graphics g)
    {
        float s = Sc;
        Font body = F(FontRole.Body);
        int lines = string.IsNullOrEmpty(Message) ? 0 : Draw.Wrap(Message, body, (int)Math.Min(Width - 32 * s, 520 * s)).Count;
        float height = IconSize * s + 24 * s + lines * 24 * s + (_button.Visible ? 48 * s : 0);
        float top = (Height - height) / 2f;
        if (Loading)
        {
            DataGraphics.Ring(g, Width / 2f, top + IconSize * s / 2f, IconSize * s / 2f, Dpi, KitAnimator.Now);
            float y = top + IconSize * s + 18 * s;
            Draw.TextCentered(g, Headline, F(FontRole.Subtitle), Width / 2f, y, T.Text);
            return;
        }

        Surfaces.EmptyState(g, ClientRectangle, Icon, IconSize, Headline, Message, Dpi, top);
    }
}
