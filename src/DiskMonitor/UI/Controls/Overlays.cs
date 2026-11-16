using DiskMonitor.UI.Theming;

namespace DiskMonitor.UI.Controls;

/// <summary>One menu entry. A null text is a separator.</summary>
internal sealed record MenuEntry(string? Text, IconName? Icon = null, string? Shortcut = null, Action? Run = null)
{
    public bool Enabled { get; init; } = true;
    public bool Checked { get; init; }
    public bool Strong { get; init; }
    public IReadOnlyList<MenuEntry>? Submenu { get; init; }

    /// <summary>Shown when the pointer rests on the item (for example why a disabled item is disabled).</summary>
    public string? Tooltip { get; init; }

    public static readonly MenuEntry Separator = new((string?)null);

    public bool IsSeparator => Text is null;
}

/// <summary>
/// The kit menu: Raised, Stroke, radius 8, shadow, 4 px padding, 280 px wide; 32 px items with the icon at
/// +16, the label at +44 and the shortcut in Caption Text3 16 px from the right; 9 px separators.
/// </summary>
internal sealed class KitMenu : LayeredPopup
{
    private readonly IReadOnlyList<MenuEntry> _entries;
    private readonly KitMenu? _parentMenu;
    private int _hover = -1;
    private KitMenu? _child;
    private bool _closingAll;

    public KitMenu(IReadOnlyList<MenuEntry> entries, int dpi, KitMenu? parent = null)
    {
        _entries = entries;
        _parentMenu = parent;
        PopupDpi = dpi;
    }

    protected override bool Activates => true;

    protected override bool TopMostPopup => true;

    protected override Size SurfaceSize => Measure(_entries, PopupDpi);

    public static Size Measure(IReadOnlyList<MenuEntry> entries, int dpi)
    {
        float s = dpi / 96f;
        float height = 8 * s + entries.Sum(e => e.IsSeparator ? 9 * s : 32 * s);
        return new Size((int)Math.Round(280 * s), (int)Math.Round(height));
    }

    /// <summary>Shows a menu at a screen point (its top-left), kept on screen.</summary>
    public static KitMenu ShowAt(IReadOnlyList<MenuEntry> entries, Point screen, int dpi, Form? owner, bool above = false)
    {
        KitMenu menu = new(entries, dpi);
        menu.Place(screen, above);
        menu.Owner = owner;
        menu.Show(owner);
        menu.Activate();
        return menu;
    }

    private void Place(Point screen, bool above)
    {
        Size size = SurfaceSize;
        Rectangle area = Screen.FromPoint(screen).WorkingArea;
        int x = Math.Clamp(screen.X, area.Left, Math.Max(area.Left, area.Right - size.Width));
        int y = above ? screen.Y - size.Height : screen.Y;
        if (y + size.Height > area.Bottom)
        {
            y = Math.Max(area.Top, screen.Y - size.Height);
        }

        SurfaceLocation = new Point(x, Math.Max(area.Top, y));
    }

    public static void PaintMenu(Graphics g, Rectangle bounds, IReadOnlyList<MenuEntry> entries, int hover, int dpi)
    {
        ThemeTokens t = ThemeService.Tokens;
        float s = dpi / 96f;
        float y = bounds.Y + 4 * s;
        Font body = ThemeService.Resources.Font(FontRole.Body, dpi);
        Font strong = ThemeService.Resources.Font(FontRole.BodyStrong, dpi);
        Font caption = ThemeService.Resources.Font(FontRole.Caption, dpi);
        for (int i = 0; i < entries.Count; i++)
        {
            MenuEntry entry = entries[i];
            if (entry.IsSeparator)
            {
                float line = (float)Math.Floor(y + 4 * s) + 0.5f;
                Draw.Line(g, t.Stroke, s, bounds.X + 12 * s, line, bounds.Right - 12 * s, line);
                y += 9 * s;
                continue;
            }

            RectangleF item = new(bounds.X + 4 * s, y, bounds.Width - 8 * s, 32 * s);
            if (i == hover && entry.Enabled)
            {
                Draw.FillRounded(g, item, 4 * s, t.Hover);
                Draw.FillRounded(g, new RectangleF(item.X, item.Y + 8 * s, 3 * s, 16 * s), 1.5f * s, t.IsHighContrast ? t.Accent : t.StrokeStrong);
            }

            Color text = entry.Enabled ? t.Text : t.TextDisabled;
            float centerY = y + 16 * s;
            if (entry.Checked)
            {
                Draw.Icon(g, IconName.Check, bounds.X + 16 * s, centerY - 8 * s, 16, dpi, text);
            }
            else if (entry.Icon is { } icon)
            {
                Draw.Icon(g, icon, bounds.X + 16 * s, centerY - 8 * s, 16, dpi, entry.Enabled ? t.Text2 : t.TextDisabled);
            }

            float right = bounds.Right - 16 * s;
            if (entry.Submenu is not null)
            {
                Draw.Icon(g, IconName.ChevronRight, right - 16 * s, centerY - 8 * s, 16, dpi, text);
                right -= 24 * s;
            }
            else if (!string.IsNullOrEmpty(entry.Shortcut))
            {
                int width = Draw.TextWidth(entry.Shortcut, caption);
                Draw.Text(g, entry.Shortcut, caption, right - width, centerY, entry.Enabled ? t.Text3 : t.TextDisabled);
                right -= width + 12 * s;
            }

            Draw.Text(g, entry.Text!, entry.Strong ? strong : body, bounds.X + 44 * s, centerY, text, (int)(right - bounds.X - 44 * s));
            y += 32 * s;
        }
    }

    protected override void PaintSurface(Graphics g, Rectangle bounds) => PaintMenu(g, bounds, _entries, _hover, PopupDpi);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = IndexAt(e.Location);
        if (index != _hover)
        {
            _hover = index;
            KitTooltip.Hide(this);
            ResetMouseEventArgs();
            Render();
            if (index >= 0 && _entries[index].Submenu is not null)
            {
                OpenSubmenu(index);
            }
        }
    }

    protected override void OnMouseHover(EventArgs e)
    {
        base.OnMouseHover(e);
        if (_hover >= 0 && _entries[_hover].Tooltip is { } tooltip)
        {
            KitTooltip.Show(this, tooltip);
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        KitTooltip.Hide(this);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        KitTooltip.Hide(this);
        base.OnFormClosed(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        int index = IndexAt(e.Location);
        if (index >= 0)
        {
            Invoke(index);
        }
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        // A submenu taking activation must not close its parent.
        BeginInvoke(() =>
        {
            if (!IsDisposed && !ContainsActiveMenu())
            {
                CloseAll();
            }
        });
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Escape:
                if (_parentMenu is not null)
                {
                    Close();
                    _parentMenu.Activate();
                }
                else
                {
                    CloseAll();
                }

                break;
            case Keys.Left when _parentMenu is not null:
                Close();
                _parentMenu.Activate();
                break;
            case Keys.Up:
                MoveHover(-1);
                break;
            case Keys.Down:
                MoveHover(1);
                break;
            case Keys.Right when _hover >= 0 && _entries[_hover].Submenu is not null:
                OpenSubmenu(_hover);
                _child?.Activate();
                break;
            case Keys.Enter:
            case Keys.Space:
                if (_hover >= 0)
                {
                    Invoke(_hover);
                }

                break;
        }
    }

    private void MoveHover(int direction)
    {
        if (_entries.Count == 0)
        {
            return;
        }

        int index = _hover;
        for (int step = 0; step < _entries.Count; step++)
        {
            index = (index + direction + _entries.Count) % _entries.Count;
            if (!_entries[index].IsSeparator && _entries[index].Enabled)
            {
                break;
            }
        }

        _hover = index;
        Render();
    }

    private void Invoke(int index)
    {
        MenuEntry entry = _entries[index];
        if (!entry.Enabled || entry.IsSeparator)
        {
            return;
        }

        if (entry.Submenu is not null)
        {
            OpenSubmenu(index);
            _child?.Activate();
            return;
        }

        CloseAll();
        entry.Run?.Invoke();
    }

    private void OpenSubmenu(int index)
    {
        _child?.Close();
        float s = PopupDpi / 96f;
        float y = 4 * s;
        for (int i = 0; i < index; i++)
        {
            y += _entries[i].IsSeparator ? 9 * s : 32 * s;
        }

        KitMenu child = new(_entries[index].Submenu!, PopupDpi, this);
        Point topRight = new(SurfaceLocation.X + SurfaceSize.Width - (int)(4 * s), SurfaceLocation.Y + (int)(y - 4 * s));
        child.Place(topRight, above: false);
        child.Owner = Owner;
        child.Show(Owner);
        _child = child;
        child.FormClosed += (_, _) => { if (_child == child) { _child = null; } };
    }

    private bool ContainsActiveMenu()
    {
        Form? active = ActiveForm;
        for (KitMenu? menu = _child; menu is not null; menu = menu._child)
        {
            if (menu == active)
            {
                return true;
            }
        }

        return active == this;
    }

    private void CloseAll()
    {
        if (_closingAll)
        {
            return;
        }

        _closingAll = true;
        _child?.CloseAll();
        Close();
        _parentMenu?.CloseAll();
    }

    private int IndexAt(Point client)
    {
        Point p = ToSurface(client);
        float s = PopupDpi / 96f;
        if (p.X < 0 || p.X >= SurfaceSize.Width)
        {
            return -1;
        }

        float y = 4 * s;
        for (int i = 0; i < _entries.Count; i++)
        {
            float height = _entries[i].IsSeparator ? 9 * s : 32 * s;
            if (p.Y >= y && p.Y < y + height)
            {
                return _entries[i].IsSeparator ? -1 : i;
            }

            y += height;
        }

        return -1;
    }
}

/// <summary>
/// In-app toast host: toasts 360 × 64 at the owner window's bottom-right (above the status strip),
/// stacked upward with 8 px gaps, at most three; each fades in over 150 ms, stays 5 s (paused while
/// hovered) and fades out over 150 ms.
/// </summary>
internal sealed class ToastPopup : LayeredPopup
{
    private readonly Level? _level;
    private readonly string _title;
    private readonly string? _text;
    private readonly string? _actionLabel;
    private readonly Action? _action;
    private long _shownAt;
    private long _fadeOutAt = long.MaxValue;
    private bool _hovered;
    private int _hoverPart = -1;

    public ToastPopup(Level? level, string title, string? text, string? actionLabel, Action? action, int dpi)
    {
        _level = level;
        _title = title;
        _text = text;
        _actionLabel = actionLabel;
        _action = action;
        PopupDpi = dpi;
        PopupOpacity = 0;
    }

    public event EventHandler? Finished;

    protected override Size SurfaceSize => new(ThemeService.S(PopupDpi, 360), ThemeService.S(PopupDpi, 64));

    public void Start()
    {
        _shownAt = KitAnimator.Now;
        if (!ThemeService.AnimationsEnabled)
        {
            PopupOpacity = 255;
        }

        KitAnimator.Add(Tick);
    }

    public void Dismiss()
    {
        if (_fadeOutAt == long.MaxValue)
        {
            _fadeOutAt = KitAnimator.Now;
        }
    }

    public static void PaintToast(Graphics g, Rectangle bounds, Level? level, string title, string? text, string? actionLabel, int hoverPart, int dpi)
    {
        float s = dpi / 96f;
        Rectangle close = new(bounds.Right - (int)Math.Round(40 * s), bounds.Y + (int)Math.Round(18 * s), (int)Math.Round(28 * s), (int)Math.Round(28 * s));
        Rectangle? action = null;
        if (!string.IsNullOrEmpty(actionLabel))
        {
            int width = KitButton.MeasureWidth(actionLabel, false, false, ButtonVariant.Secondary, true, dpi);
            action = new Rectangle(close.X - (int)Math.Round(4 * s) - width, close.Y, width, close.Height);
        }

        // The text ends 12 px before the action (or the close button), so a long action never covers it.
        float textRight = Math.Min(bounds.Right - 104 * s, (action?.X ?? close.X) - 12 * s);
        Surfaces.ToastContent(g, bounds, level, title, text, dpi, textRight);
        KitButton.Render(g, close, ButtonVariant.Subtle, "", IconName.Close, false, new ButtonLook(hoverPart == 1, false, false, false), dpi);
        if (action is { } shown)
        {
            KitButton.Render(g, shown, ButtonVariant.Secondary, actionLabel!, null, false, new ButtonLook(hoverPart == 0, false, false, false), dpi);
        }
    }

    protected override void PaintSurface(Graphics g, Rectangle bounds) => PaintToast(g, bounds, _level, _title, _text, _actionLabel, _hoverPart, PopupDpi);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _hovered = true;
        int part = PartAt(e.Location);
        if (part != _hoverPart)
        {
            _hoverPart = part;
            Render();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        _hoverPart = -1;
        _shownAt = KitAnimator.Now - 150;
        Render();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        int part = PartAt(e.Location);
        if (part == 1)
        {
            Dismiss();
        }
        else if (part == 0)
        {
            _action?.Invoke();
            Dismiss();
        }
    }

    private int PartAt(Point client)
    {
        Point p = ToSurface(client);
        float s = PopupDpi / 96f;
        Size size = SurfaceSize;
        Rectangle close = new(size.Width - (int)Math.Round(40 * s), (int)Math.Round(18 * s), (int)Math.Round(28 * s), (int)Math.Round(28 * s));
        if (close.Contains(p))
        {
            return 1;
        }

        if (!string.IsNullOrEmpty(_actionLabel))
        {
            int width = KitButton.MeasureWidth(_actionLabel, false, false, ButtonVariant.Secondary, true, PopupDpi);
            Rectangle action = new(close.X - (int)Math.Round(4 * s) - width, close.Y, width, close.Height);
            if (action.Contains(p))
            {
                return 0;
            }
        }

        return -1;
    }

    private void Tick(long now)
    {
        bool animate = ThemeService.AnimationsEnabled;
        if (now >= _fadeOutAt)
        {
            double t = animate ? (now - _fadeOutAt) / 150.0 : 1;
            if (t >= 1)
            {
                KitAnimator.Remove(Tick);
                Close();
                Finished?.Invoke(this, EventArgs.Empty);
                return;
            }

            PopupOpacity = (byte)(255 * (1 - t));
            return;
        }

        if (_hovered)
        {
            _shownAt = now - 150;
        }

        double age = now - _shownAt;
        if (age < 150 && animate)
        {
            PopupOpacity = (byte)(255 * age / 150);
        }
        else if (PopupOpacity != 255)
        {
            PopupOpacity = 255;
        }

        if (age >= 150 + 5000)
        {
            _fadeOutAt = now;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            KitAnimator.Remove(Tick);
        }

        base.Dispose(disposing);
    }
}
