using DiskMonitor.UI.Theming;

namespace DiskMonitor.UI.Controls;

/// <summary>
/// A text box: 32 px, radius 4, a borderless native TextBox inside a painted frame. Hover shows a
/// StrokeStrong border, focus a 2 px Accent border. The placeholder (Text3) is painted by the frame.
/// </summary>
internal class KitTextBox : KitControl, IThemeAware
{
    private readonly TextBox _box;
    private bool _hovered;
    private bool _inset;
    private string _placeholder = "";

    public KitTextBox()
    {
        SetStyle(ControlStyles.Selectable, true);
        _box = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Margin = Padding.Empty,
            Visible = true
        };
        _box.TextChanged += (_, _) => { OnInnerTextChanged(); Invalidate(); };
        _box.GotFocus += (_, _) => Invalidate();
        _box.LostFocus += (_, _) => Invalidate();
        _box.MouseEnter += (_, _) => SetHover(true);
        _box.MouseLeave += (_, _) => SetHover(PointToClient(MousePosition) is var p && ClientRectangle.Contains(p));
        _box.KeyDown += (_, e) => OnInnerKeyDown(e);
        Controls.Add(_box);
        Cursor = Cursors.IBeam;
        AccessibleRole = AccessibleRole.Text;
        ApplyTheme();
    }

    public TextBox Inner => _box;

    /// <summary>Inside a card or dialog the box is filled with Bg instead of Surface.</summary>
    public bool Inset
    {
        get => _inset;
        set { _inset = value; ApplyTheme(); Invalidate(); }
    }

    public string Placeholder
    {
        get => _placeholder;
        set { _placeholder = value; AccessibleDescription = value; Invalidate(); }
    }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => _box.Text;
        set => _box.Text = value;
    }

    public bool ReadOnly
    {
        get => _box.ReadOnly;
        set { _box.ReadOnly = value; ApplyTheme(); }
    }

    /// <summary>Left edge of the text, from the frame's left edge.</summary>
    protected virtual int TextInset => S(10);

    protected virtual int TrailingSpace => S(10);

    protected bool HasFocus => _box.Focused || Preview.HasFlag(PreviewState.Focus);

    public void ApplyTheme()
    {
        Color fill = _inset ? T.Bg : T.Surface;
        _box.BackColor = fill;
        _box.ForeColor = Enabled ? T.Text : T.TextDisabled;
        ThemeService.ApplyNativeTheme(_box);
        Invalidate();
    }

    public new void Focus() => _box.Focus();

    protected virtual void OnInnerTextChanged()
    {
        base.OnTextChanged(EventArgs.Empty);
    }

    protected virtual void OnInnerKeyDown(KeyEventArgs e)
    {
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        _box.Focus();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        SetHover(true);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        SetHover(PointToClient(MousePosition) is var p && ClientRectangle.Contains(p) && _box.Bounds.Contains(p));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _box.Focus();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        ApplyTheme();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int left = TextInset;
        int height = _box.PreferredHeight;
        _box.SetBounds(left, (Height - height) / 2, Math.Max(1, Width - left - TrailingSpace), height);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _box.Font = F(FontRole.Body);
        ApplyTheme();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        _box.Font = F(FontRole.Body);
        base.OnDpiChangedAfterParent(e);
    }

    protected override void PaintContent(Graphics g)
    {
        bool focus = HasFocus;
        bool hover = (_hovered || Preview.HasFlag(PreviewState.Hover)) && Enabled;
        bool disabled = !Enabled || Preview.HasFlag(PreviewState.Disabled);
        PaintFrame(g, ClientRectangle, focus, hover, disabled, _inset, Dpi);
        bool showPlaceholder = _box.TextLength == 0 && !_box.Focused && _placeholder.Length > 0;
        _box.Visible = !showPlaceholder || Preview != PreviewState.None;
        if (showPlaceholder)
        {
            Draw.Text(g, _placeholder, F(FontRole.Body), TextInset, Height / 2f, T.Text3, Width - TextInset - TrailingSpace);
        }

        PaintAdornments(g, focus, disabled);
    }

    /// <summary>Paints icons inside the frame (search and clear icons).</summary>
    protected virtual void PaintAdornments(Graphics g, bool focus, bool disabled)
    {
    }

    /// <summary>
    /// Paints a text box with its text (or placeholder), as the harness and static surfaces need it:
    /// optional search icon, caret after the text when focused, and the clear icon.
    /// </summary>
    public static void RenderBox(Graphics g, Rectangle r, string text, string placeholder, bool focus, bool hover, bool disabled, bool inset, bool search, int dpi)
    {
        ThemeTokens t = ThemeService.Tokens;
        float s = dpi / 96f;
        PaintFrame(g, r, focus, hover, disabled, inset, dpi);
        float textX = r.X + (search ? 34 : 10) * s;
        float centerY = r.Y + r.Height / 2f;
        Font body = ThemeService.Resources.Font(FontRole.Body, dpi);
        if (search)
        {
            Draw.Icon(g, IconName.Search, r.X + 10 * s, r.Y + (r.Height - 16 * s) / 2f, 16, dpi, disabled ? t.TextDisabled : t.Text3);
        }

        if (text.Length > 0)
        {
            Draw.Text(g, text, body, textX, centerY, disabled ? t.TextDisabled : t.Text);
            if (focus)
            {
                float caret = (float)Math.Floor(textX + Draw.TextWidth(text, body) + 2 * s) + 0.5f;
                Draw.Line(g, t.Text, s, caret, centerY - 8 * s, caret, centerY + 8 * s);
            }

            if (search)
            {
                Draw.Icon(g, IconName.Close, r.Right - 26 * s, r.Y + (r.Height - 16 * s) / 2f, 16, dpi, t.Text2);
            }
        }
        else
        {
            Draw.Text(g, placeholder, body, textX, centerY, t.Text3);
        }
    }

    public static void PaintFrame(Graphics g, Rectangle r, bool focus, bool hover, bool disabled, bool inset, int dpi)
    {
        ThemeTokens t = ThemeService.Tokens;
        float s = dpi / 96f;
        Draw.FillRounded(g, r, 4 * s, inset ? t.Bg : t.Surface);
        if (focus && !disabled)
        {
            Draw.StrokeRounded(g, r, 4 * s, t.Accent, 2 * s);
        }
        else
        {
            Draw.StrokeRounded(g, r, 4 * s, t.IsHighContrast && hover ? t.Accent : hover && !disabled ? t.StrokeStrong : t.Stroke, t.IsHighContrast && hover ? 2 * s : s);
        }
    }

    private void SetHover(bool value)
    {
        if (_hovered != value)
        {
            _hovered = value;
            Invalidate();
        }
    }
}

/// <summary>
/// The search box: a text box with the search icon at +10, text at +34 and, while it has text, a clear
/// icon 10 px from the right. Filters after 200 ms without typing; Esc clears.
/// </summary>
internal sealed class SearchBox : KitTextBox
{
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 200 };

    public SearchBox()
    {
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            SearchChanged?.Invoke(this, EventArgs.Empty);
        };
        AccessibleRole = AccessibleRole.Text;
    }

    /// <summary>Raised 200 ms after the last keystroke, and at once when cleared.</summary>
    public event EventHandler? SearchChanged;

    /// <summary>Raised when Esc is pressed on an empty box (the owner may close it).</summary>
    public event EventHandler? Dismissed;

    protected override int TextInset => S(34);

    protected override int TrailingSpace => S(36);

    public void Clear()
    {
        Inner.Text = "";
        _debounce.Stop();
        SearchChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnInnerTextChanged()
    {
        base.OnInnerTextChanged();
        _debounce.Stop();
        _debounce.Start();
    }

    protected override void OnInnerKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            if (Inner.TextLength > 0)
            {
                Clear();
            }
            else
            {
                Dismissed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (Inner.TextLength > 0 && e.X >= Width - S(36))
        {
            Clear();
            Focus();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = Inner.TextLength > 0 && e.X >= Width - S(36) ? Cursors.Default : Cursors.IBeam;
    }

    protected override void PaintAdornments(Graphics g, bool focus, bool disabled)
    {
        Draw.Icon(g, IconName.Search, S(10), (Height - S(16)) / 2f, 16, Dpi, disabled ? T.TextDisabled : T.Text3);
        if (Inner.TextLength > 0 || Preview.HasFlag(PreviewState.Focus))
        {
            Draw.Icon(g, IconName.Close, Width - S(26), (Height - S(16)) / 2f, 16, Dpi, T.Text2);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _debounce.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// The drop-down: 32 px (compact 28), padding 12, Body label, a 16 px chevron whose left edge is 26 px from
/// the right; coloured as a secondary button. A value other than the default shows an Accent border.
/// </summary>
internal sealed class DropDown : InteractiveKitControl
{
    private readonly List<string> _items = [];
    private int _selected;
    private bool _open;
    private bool _compact;

    public DropDown()
    {
        AccessibleRole = AccessibleRole.ComboBox;
        Backdrop = static t => t.Bg;
    }

    public event EventHandler? SelectionChanged;

    public IReadOnlyList<string> Items => _items;

    public int DefaultIndex { get; set; }

    /// <summary>Accent border while the selection differs from <see cref="DefaultIndex"/> (filters); settings turn it off.</summary>
    public bool MarkChanged { get; set; } = true;

    /// <summary>Formats the label from the selected item, for example "Group: {value}"; null shows the item.</summary>
    public Func<string, string>? LabelFormat { get; set; }

    public bool Compact
    {
        get => _compact;
        set { _compact = value; Invalidate(); }
    }

    public bool Inset { get; set; }

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            int clamped = _items.Count == 0 ? 0 : Math.Clamp(value, 0, _items.Count - 1);
            if (clamped != _selected)
            {
                _selected = clamped;
                AccessibleName = Label;
                Invalidate();

                // The width follows the label.
                Parent?.PerformLayout();
            }
        }
    }

    public string Label => _items.Count == 0 ? "" : LabelFormat?.Invoke(_items[_selected]) ?? _items[_selected];

    public void SetItems(IEnumerable<string> items, int selected = 0)
    {
        _items.Clear();
        _items.AddRange(items);
        _selected = Math.Clamp(selected, 0, Math.Max(0, _items.Count - 1));
        AccessibleName = Label;
        Invalidate();
        Parent?.PerformLayout();
    }

    public int PreferredWidth(int minimum = 0) =>
        Math.Max(minimum, Draw.TextWidth(Label, F(FontRole.Body)) + S(12 + 8 + 26));

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        OpenList();
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Down && e.Alt)
        {
            e.Handled = true;
            OpenList();
            return;
        }

        if (e.KeyCode is Keys.Up or Keys.Down && _items.Count > 0)
        {
            e.Handled = true;
            Choose(Math.Clamp(_selected + (e.KeyCode == Keys.Down ? 1 : -1), 0, _items.Count - 1));
            return;
        }

        base.OnKeyDown(e);
    }

    public void OpenList()
    {
        if (_items.Count == 0 || _open)
        {
            return;
        }

        _open = true;
        Invalidate();
        DropDownList list = new(_items, _selected, Math.Max(Width, S(120)), ThemeService.Dpi(this), this);
        list.Chosen += (_, index) => Choose(index);
        list.FormClosed += (_, _) =>
        {
            _open = false;
            Invalidate();
            if (!IsDisposed)
            {
                Focus();
            }
        };
        Point below = PointToScreen(new Point(0, Height + S(4)));
        list.Open(below, FindForm());
    }

    private void Choose(int index)
    {
        if (index != _selected)
        {
            SelectedIndex = index;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void PaintContent(Graphics g)
    {
        bool changed = MarkChanged && _selected != DefaultIndex;
        Render(g, ClientRectangle, Label, ShowHover, _open || ShowPressed, ShowDisabled, false, changed, Inset, Dpi);
    }

    public static void Render(Graphics g, Rectangle r, string label, bool hover, bool open, bool disabled, bool focus, bool changed, bool inset, int dpi)
    {
        ThemeTokens t = ThemeService.Tokens;
        float s = dpi / 96f;
        Color fill = disabled ? t.Bg : open ? t.Pressed : hover ? t.Hover : inset ? t.Bg : t.Surface;
        Color border = disabled ? t.Stroke : open || changed ? t.Accent : hover ? t.StrokeStrong : t.Stroke;
        Draw.FillRounded(g, r, 4 * s, fill);
        Draw.StrokeRounded(g, r, 4 * s, border, t.IsHighContrast && hover ? 2 * s : s);
        Color text = disabled ? t.TextDisabled : t.Text;
        Draw.Text(g, label, ThemeService.Resources.Font(FontRole.Body, dpi), r.X + 12 * s, r.Y + r.Height / 2f, text, (int)(r.Width - 12 * s - 30 * s));
        Draw.Icon(g, IconName.ChevronDown, r.Right - 26 * s, r.Y + (r.Height - 16 * s) / 2f, 16, dpi, disabled ? t.TextDisabled : t.Text2);
        if (focus)
        {
            Draw.FocusRing(g, r, 4 * s, dpi);
        }
    }
}

/// <summary>
/// The drop-down list: opens 4 px below, Raised, Stroke, radius 8, shadow, 4 px padding, 32 px items.
/// The current item is Selected with an Accent bar, BodyStrong and a check. Type-ahead selects.
/// </summary>
internal sealed class DropDownList : LayeredPopup
{
    private readonly IReadOnlyList<string> _items;
    private readonly int _width;
    private readonly Control _owner;
    private int _current;
    private int _hover = -1;
    private string _typed = "";
    private long _typedAt;

    public DropDownList(IReadOnlyList<string> items, int current, int width, int dpi, Control owner)
    {
        _items = items;
        _current = current;
        _width = width;
        _owner = owner;
        PopupDpi = dpi;
    }

    public event EventHandler<int>? Chosen;

    protected override bool Activates => true;

    protected override Size SurfaceSize => new(_width, ThemeService.S(PopupDpi, 8) + _items.Count * ThemeService.S(PopupDpi, 32));

    public void Open(Point surfaceTopLeft, Form? owner)
    {
        Rectangle screen = Screen.FromPoint(surfaceTopLeft).WorkingArea;
        Size size = SurfaceSize;
        int y = surfaceTopLeft.Y + size.Height > screen.Bottom ? _owner.PointToScreen(Point.Empty).Y - size.Height - ThemeService.S(PopupDpi, 4) : surfaceTopLeft.Y;
        SurfaceLocation = new Point(surfaceTopLeft.X, y);
        Owner = owner;
        Show(owner);
        Activate();
    }

    public static void PaintList(Graphics g, Rectangle bounds, IReadOnlyList<string> items, int current, int hover, int dpi)
    {
        ThemeTokens t = ThemeService.Tokens;
        float s = dpi / 96f;
        for (int i = 0; i < items.Count; i++)
        {
            RectangleF item = new(bounds.X + 4 * s, bounds.Y + 4 * s + i * 32 * s, bounds.Width - 8 * s, 32 * s);
            bool selected = i == current;
            if (selected)
            {
                Draw.FillRounded(g, item, 4 * s, t.Selected);
                Draw.FillRounded(g, new RectangleF(item.X, item.Y + 8 * s, 3 * s, 16 * s), 1.5f * s, t.Accent);
                Draw.Icon(g, IconName.Check, bounds.Right - 30 * s, item.Y + 8 * s, 16, dpi, t.OnSelected);
            }
            else if (i == hover)
            {
                Draw.FillRounded(g, item, 4 * s, t.Hover);
                Draw.FillRounded(g, new RectangleF(item.X, item.Y + 8 * s, 3 * s, 16 * s), 1.5f * s, t.StrokeStrong);
            }

            Font font = ThemeService.Resources.Font(selected ? FontRole.BodyStrong : FontRole.Body, dpi);
            Draw.Text(g, items[i], font, item.X + 12 * s, item.Y + 16 * s, selected ? t.OnSelected : t.Text, (int)(item.Width - 46 * s));
        }
    }

    protected override void PaintSurface(Graphics g, Rectangle bounds) => PaintList(g, bounds, _items, _current, _hover, PopupDpi);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = IndexAt(e.Location);
        if (index != _hover)
        {
            _hover = index;
            Render();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        int index = IndexAt(e.Location);
        if (index >= 0)
        {
            Choose(index);
        }
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Escape:
                Close();
                break;
            case Keys.Enter:
            case Keys.Space:
                Choose(_hover >= 0 ? _hover : _current);
                break;
            case Keys.Up:
                _hover = Math.Max(0, (_hover >= 0 ? _hover : _current) - 1);
                Render();
                break;
            case Keys.Down:
                _hover = Math.Min(_items.Count - 1, (_hover >= 0 ? _hover : _current) + 1);
                Render();
                break;
            case Keys.Home:
                _hover = 0;
                Render();
                break;
            case Keys.End:
                _hover = _items.Count - 1;
                Render();
                break;
        }
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (char.IsControl(e.KeyChar))
        {
            return;
        }

        long now = KitAnimator.Now;
        _typed = now - _typedAt > 1000 ? e.KeyChar.ToString() : _typed + e.KeyChar;
        _typedAt = now;
        int match = -1;
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i].StartsWith(_typed, StringComparison.CurrentCultureIgnoreCase))
            {
                match = i;
                break;
            }
        }

        if (match >= 0)
        {
            _hover = match;
            Render();
        }
    }

    private int IndexAt(Point client)
    {
        Point p = ToSurface(client);
        float s = PopupDpi / 96f;
        int index = (int)Math.Floor((p.Y - 4 * s) / (32 * s));
        return p.X >= 0 && p.X < _width && index >= 0 && index < _items.Count ? index : -1;
    }

    private void Choose(int index)
    {
        _current = index;
        Chosen?.Invoke(this, index);
        Close();
    }
}

/// <summary>
/// The segmented control: container Surface (Bg when inset), Stroke, radius 4; segment width = label
/// + 28. The selected segment is a Selected rectangle inset 2 px with a 2 px Accent underline; its label
/// BodyStrong raised 1 px. Arrow keys move the selection.
/// </summary>
internal sealed class SegmentedControl : InteractiveKitControl
{
    private readonly List<string> _items = [];
    private int _selected;
    private int _hover = -1;

    public SegmentedControl()
    {
        AccessibleRole = AccessibleRole.PageTabList;
    }

    public event EventHandler? SelectionChanged;

    public bool Compact { get; set; }

    public bool Inset { get; set; }

    public IReadOnlyList<string> Items => _items;

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            int clamped = Math.Clamp(value, 0, Math.Max(0, _items.Count - 1));
            if (clamped != _selected)
            {
                _selected = clamped;
                AccessibleName = _items.Count > 0 ? _items[_selected] : "";
                Invalidate();
                Parent?.PerformLayout();
            }
        }
    }

    public void SetItems(IEnumerable<string> items, int selected = 0)
    {
        _items.Clear();
        _items.AddRange(items);
        _selected = Math.Clamp(selected, 0, Math.Max(0, _items.Count - 1));
        AccessibleName = _items.Count > 0 ? _items[_selected] : "";
        Invalidate();
    }

    public int PreferredWidth => Widths(_items, _selected, Dpi).Sum();

    public static int[] Widths(IReadOnlyList<string> items, int selected, int dpi)
    {
        int[] widths = new int[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            Font font = ThemeService.Resources.Font(i == selected ? FontRole.BodyStrong : FontRole.Body, dpi);
            widths[i] = (int)Math.Round(Draw.TextWidth(items[i], font) + 28 * dpi / 96f);
        }

        return widths;
    }

    protected override bool ActivatesOnKeys => false;

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Left or Keys.Right && _items.Count > 0)
        {
            e.Handled = true;
            Select(Math.Clamp(_selected + (e.KeyCode == Keys.Right ? 1 : -1), 0, _items.Count - 1));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = IndexAt(e.X);
        if (index != _hover)
        {
            _hover = index;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        int index = IndexAt(e.X);
        if (e.Button == MouseButtons.Left && index >= 0)
        {
            Select(index);
        }
    }

    private void Select(int index)
    {
        if (index != _selected)
        {
            SelectedIndex = index;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private int IndexAt(int x)
    {
        int[] widths = Widths(_items, _selected, Dpi);
        int left = 0;
        for (int i = 0; i < widths.Length; i++)
        {
            if (x >= left && x < left + widths[i])
            {
                return i;
            }

            left += widths[i];
        }

        return -1;
    }

    protected override void PaintContent(Graphics g) =>
        Render(g, ClientRectangle, _items, _selected, ShowDisabled ? -1 : _hover, Inset, false, Dpi);

    public static void Render(Graphics g, Rectangle r, IReadOnlyList<string> items, int selected, int hover, bool inset, bool focus, int dpi)
    {
        ThemeTokens t = ThemeService.Tokens;
        float s = dpi / 96f;
        Draw.FillRounded(g, r, 4 * s, inset ? t.Bg : t.Surface);
        Draw.StrokeRounded(g, r, 4 * s, t.Stroke, s);
        int[] widths = Widths(items, selected, dpi);
        float x = r.X;
        for (int i = 0; i < items.Count; i++)
        {
            float w = widths[i];
            float center = x + w / 2f;
            if (i == selected)
            {
                Draw.FillRounded(g, new RectangleF(x + 2 * s, r.Y + 2 * s, w - 4 * s, r.Height - 4 * s), 3 * s, t.Selected);
                Draw.FillRounded(g, new RectangleF(x + 10 * s, r.Bottom - 5 * s, w - 20 * s, 2 * s), s, t.Accent);
                Draw.TextCentered(g, items[i], ThemeService.Resources.Font(FontRole.BodyStrong, dpi), center, r.Y + r.Height / 2f - s, t.OnSelected);
            }
            else
            {
                if (i == hover)
                {
                    Draw.FillRounded(g, new RectangleF(x + 2 * s, r.Y + 2 * s, w - 4 * s, r.Height - 4 * s), 3 * s, t.Hover);
                }

                Draw.TextCentered(g, items[i], ThemeService.Resources.Font(FontRole.Body, dpi), center, r.Y + r.Height / 2f, t.Text2);
            }

            if (i > 0 && i - 1 != selected && i != selected)
            {
                float line = (float)Math.Floor(x) + 0.5f;
                Draw.Line(g, t.Stroke, s, line, r.Y + 8 * s, line, r.Bottom - 8 * s);
            }

            x += w;
        }

        if (focus)
        {
            Draw.FocusRing(g, r, 4 * s, dpi);
        }
    }
}

/// <summary>The switch: 40 × 20, radius 10, knob r 6 (r 7 on hover). Space toggles.</summary>
internal sealed class KitSwitch : InteractiveKitControl
{
    private bool _checked;

    public KitSwitch()
    {
        AccessibleRole = AccessibleRole.CheckButton;
        Size = new Size(40, 20);
    }

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked != value)
            {
                _checked = value;
                AccessibleDescription = value ? App.Strings.SwitchOn : App.Strings.SwitchOff;
                Invalidate();
            }
        }
    }

    /// <summary>Why the switch is disabled; shown as its tooltip.</summary>
    public string? DisabledReason { get; set; }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Checked = !Checked;
        CheckedChanged?.Invoke(this, EventArgs.Empty);
    }

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

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Size = new Size(S(40), S(20));
    }

    public override RectangleF FocusRingRect => new(0, (Height - S(20)) / 2, S(40), S(20));

    public override float FocusRingRadius => Sf(10);

    protected override void PaintContent(Graphics g) =>
        Render(g, new Rectangle(0, (Height - S(20)) / 2, S(40), S(20)), Checked, ShowHover, ShowDisabled, false, Dpi);

    public static void Render(Graphics g, Rectangle r, bool on, bool hover, bool disabled, bool focus, int dpi)
    {
        ThemeTokens t = ThemeService.Tokens;
        float s = dpi / 96f;
        float radius = 10 * s;
        if (on)
        {
            Draw.FillRounded(g, r, radius, disabled ? t.Hover : hover ? t.AccentHover : t.Accent);
        }
        else
        {
            Draw.FillRounded(g, r, radius, disabled ? t.Bg : hover ? t.Hover : t.Surface);
            Draw.StrokeRounded(g, r, radius, disabled ? t.Stroke : t.StrokeStrong, s);
        }

        if (t.IsHighContrast && hover)
        {
            Draw.StrokeRounded(g, r, radius, t.Accent, 2 * s);
        }

        float knob = (hover && !disabled ? 7 : 6) * s;
        float cx = r.X + (on ? 30 : 10) * s;
        Draw.Circle(g, disabled ? t.TextDisabled : on ? t.OnAccent : t.Text2, cx, r.Y + r.Height / 2f, knob);
        if (focus)
        {
            Draw.FocusRing(g, r, radius, dpi);
        }
    }
}

/// <summary>The check box: 18 × 18, radius 3; label Body 10 px to the right, clickable.</summary>
internal sealed class KitCheckBox : InteractiveKitControl
{
    private bool _checked;

    public KitCheckBox()
    {
        AccessibleRole = AccessibleRole.CheckButton;
    }

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set { if (_checked != value) { _checked = value; Invalidate(); } }
    }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => base.Text;
        set { base.Text = value; AccessibleName = value; Invalidate(); }
    }

    public int PreferredWidth => S(18) + (Text.Length > 0 ? S(10) + Draw.TextWidth(Text, F(FontRole.Body)) : 0);

    public override RectangleF FocusRingRect => new(0, (Height - S(18)) / 2, S(18), S(18));

    public override float FocusRingRadius => Sf(3);

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Checked = !Checked;
        CheckedChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void PaintContent(Graphics g)
    {
        Rectangle box = new(0, (Height - S(18)) / 2, S(18), S(18));
        Render(g, box, Checked, ShowHover, ShowDisabled, false, Dpi);
        if (Text.Length > 0)
        {
            Draw.Text(g, Text, F(FontRole.Body), S(28), Height / 2f, ShowDisabled ? T.TextDisabled : T.Text);
        }
    }

    public static void Render(Graphics g, Rectangle r, bool on, bool hover, bool disabled, bool focus, int dpi)
    {
        ThemeTokens t = ThemeService.Tokens;
        float s = dpi / 96f;
        if (on)
        {
            Draw.FillRounded(g, r, 3 * s, disabled ? t.Hover : hover ? t.AccentHover : t.Accent);
            PointF[] check = [new(r.X + 4.5f * s, r.Y + 9.5f * s), new(r.X + 7.75f * s, r.Y + 12.5f * s), new(r.X + 13.5f * s, r.Y + 5.75f * s)];
            g.DrawLines(ThemeService.Resources.Pen(disabled ? t.TextDisabled : t.OnAccent, 2 * s, round: true), check);
        }
        else
        {
            Draw.FillRounded(g, r, 3 * s, disabled ? t.Bg : hover ? t.Hover : t.Surface);
            Draw.StrokeRounded(g, r, 3 * s, disabled ? t.Stroke : t.StrokeStrong, s);
        }

        if (focus)
        {
            Draw.FocusRing(g, r, 3 * s, dpi);
        }
    }
}

/// <summary>A radio button (18 px), as on MediaVerifier's sheets. Disk Monitor's screens do not use it.</summary>
internal static class Radio
{
    public static void Render(Graphics g, float cx, float cy, bool on, bool hover, int dpi)
    {
        ThemeTokens t = ThemeService.Tokens;
        float s = dpi / 96f;
        if (on)
        {
            Draw.Circle(g, hover ? t.AccentHover : t.Accent, cx, cy, 9 * s);
            Draw.Circle(g, t.OnAccent, cx, cy, 4 * s);
        }
        else
        {
            Draw.Circle(g, hover ? t.Hover : t.Surface, cx, cy, 9 * s);
            Draw.Ring(g, t.StrokeStrong, s, cx, cy, 8.5f * s);
        }
    }
}
