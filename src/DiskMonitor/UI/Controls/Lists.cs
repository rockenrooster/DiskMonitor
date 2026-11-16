using DiskMonitor.UI.Theming;

namespace DiskMonitor.UI.Controls;

/// <summary>Row painters for lists and tables (subplan 02, Lists and tables). Coordinates are device pixels.</summary>
internal static class RowPaint
{
    /// <summary>Row background for rest, hover and selected states, with the 3 px leading bar.</summary>
    public static void Background(Graphics g, RectangleF r, bool hover, bool selected, float barHeight, int dpi)
    {
        ThemeTokens t = ThemeService.Tokens;
        float s = dpi / 96f;
        if (selected)
        {
            g.FillRectangle(ThemeService.Resources.Brush(t.Selected), r);
            Draw.FillRounded(g, new RectangleF(r.X, r.Y + (r.Height - barHeight) / 2f, 3 * s, barHeight), 1.5f * s, t.Accent);
        }
        else if (hover)
        {
            g.FillRectangle(ThemeService.Resources.Brush(t.Hover), r);
            Draw.FillRounded(g, new RectangleF(r.X, r.Y + (r.Height - barHeight) / 2f, 3 * s, barHeight), 1.5f * s, t.IsHighContrast ? t.Accent : t.StrokeStrong);
        }
    }

    /// <summary>A 1 px Stroke divider at 60 % opacity along the bottom of a row.</summary>
    public static void Divider(Graphics g, float left, float right, float bottom, int dpi)
    {
        float s = dpi / 96f;
        float y = (float)Math.Floor(bottom - s) + 0.5f;
        Draw.Line(g, Color.FromArgb(153, ThemeService.Tokens.Stroke), s, left, y, right, y);
    }

    /// <summary>Group header: 32 px, Bg at 55 % opacity, chevron at +14, optional shape at +38, label at +62, count 8 px after.</summary>
    public static void GroupHeader(Graphics g, RectangleF r, bool collapsed, Level? level, IconName? icon, string label, string count, int dpi, bool focus = false)
    {
        ThemeTokens t = ThemeService.Tokens;
        float s = dpi / 96f;
        g.FillRectangle(ThemeService.Resources.Brush(Color.FromArgb(140, t.Bg)), r);
        Draw.Icon(g, collapsed ? IconName.ChevronRight : IconName.ChevronDown, r.X + 14 * s, r.Y + 8 * s, 16, dpi, t.Text2);
        float x = r.X + 38 * s;
        Color behind = Blend(t.Bg, t.Surface, 140);
        if (level is { } shown)
        {
            (Color shape, _, _) = t.LevelColors(shown);
            Draw.Severity(g, shown, x, r.Y + 8 * s, 16, dpi, shape, behind);
            x += 24 * s;
        }
        else if (icon is { } glyph)
        {
            Draw.Icon(g, glyph, x, r.Y + 8 * s, 16, dpi, t.Text2);
            x += 24 * s;
        }

        Font strong = ThemeService.Resources.Font(FontRole.BodyStrong, dpi);
        Draw.Text(g, label, strong, x, r.Y + 16 * s, t.Text);
        Draw.Text(g, count, ThemeService.Resources.Font(FontRole.Body, dpi), x + Draw.TextWidth(label, strong) + 8 * s, r.Y + 16 * s, t.Text3);
        if (focus)
        {
            Draw.FocusRing(g, RectangleF.Inflate(r, -3 * s, -3 * s), 4 * s, dpi);
        }
    }

    /// <summary>Table header: 32 px, Bg at 55 % opacity, CaptionStrong Text3 labels, 1 px Stroke below.</summary>
    public static void TableHeader(Graphics g, RectangleF r, IReadOnlyList<(string Label, float X, bool RightAligned)> columns, int dpi)
    {
        ThemeTokens t = ThemeService.Tokens;
        float s = dpi / 96f;
        g.FillRectangle(ThemeService.Resources.Brush(Color.FromArgb(140, t.Bg)), r);
        Font font = ThemeService.Resources.Font(FontRole.CaptionStrong, dpi);
        foreach ((string label, float x, bool right) in columns)
        {
            if (right)
            {
                Draw.TextRight(g, label, font, x, r.Y + 16 * s, t.Text3);
            }
            else
            {
                Draw.Text(g, label, font, x, r.Y + 16 * s, t.Text3);
            }
        }

        float line = (float)Math.Floor(r.Bottom - s) + 0.5f;
        Draw.Line(g, t.Stroke, s, r.X, line, r.Right, line);
    }

    /// <summary>
    /// Navigation item: 40 px, radius 4; icon 16 px at +14, label Body at +42 (BodyStrong when selected);
    /// hover and selected states with the 3 × 16 bar; an optional badge pill 10 px from the right.
    /// </summary>
    public static void NavItem(Graphics g, RectangleF r, IconName icon, string? label, bool hover, bool selected, bool focus, int dpi, Color? iconColor = null)
    {
        ThemeTokens t = ThemeService.Tokens;
        float s = dpi / 96f;
        if (selected)
        {
            Draw.FillRounded(g, r, 4 * s, t.Selected);
            Draw.FillRounded(g, new RectangleF(r.X, r.Y + 12 * s, 3 * s, 16 * s), 1.5f * s, t.Accent);
        }
        else if (hover)
        {
            Draw.FillRounded(g, r, 4 * s, t.Hover);
            Draw.FillRounded(g, new RectangleF(r.X, r.Y + 12 * s, 3 * s, 16 * s), 1.5f * s, t.IsHighContrast ? t.Accent : t.StrokeStrong);
        }

        float iconX = label is null ? r.X + (r.Width - 16 * s) / 2f : r.X + 14 * s;
        Draw.Icon(g, icon, iconX, r.Y + 12 * s, 16, dpi, iconColor ?? (selected ? t.OnSelected : t.Text));
        if (label is not null)
        {
            Draw.Text(g, label, ThemeService.Resources.Font(selected ? FontRole.BodyStrong : FontRole.Body, dpi), r.X + 42 * s, r.Y + 20 * s, selected ? t.OnSelected : t.Text, (int)(r.Width - 50 * s));
        }

        if (focus)
        {
            Draw.FocusRing(g, r, 4 * s, dpi);
        }
    }

    /// <summary>Scrollbar thumb: 6 px, radius 3, StrokeStrong at 60 %, 3 px from the right; 10 px at full opacity on hover.</summary>
    public static void Scrollbar(Graphics g, RectangleF track, float viewport, float content, float offset, bool hover, int dpi)
    {
        if (content <= viewport || viewport <= 0)
        {
            return;
        }

        float s = dpi / 96f;
        float width = (hover ? 10 : 6) * s;
        float usable = track.Height - 8 * s;
        float length = Math.Max(32 * s, usable * viewport / content);
        float top = track.Y + 4 * s + (usable - length) * Math.Clamp(offset / (content - viewport), 0, 1);
        RectangleF thumb = new(track.Right - 3 * s - width, top, width, length);
        Color color = ThemeService.Tokens.StrokeStrong;
        Draw.FillRounded(g, thumb, width / 2f, hover ? color : Color.FromArgb(153, color));
    }

    /// <summary>The thumb's rectangle, for hit-testing a drag.</summary>
    public static RectangleF ScrollThumb(RectangleF track, float viewport, float content, float offset, bool hover, int dpi)
    {
        float s = dpi / 96f;
        float width = (hover ? 10 : 6) * s;
        float usable = track.Height - 8 * s;
        float length = Math.Max(32 * s, usable * viewport / Math.Max(content, 1));
        float top = track.Y + 4 * s + (usable - length) * Math.Clamp(offset / Math.Max(1, content - viewport), 0, 1);
        return new RectangleF(track.Right - 3 * s - width, top, width, length);
    }

    private static Color Blend(Color top, Color bottom, int alpha) => Color.FromArgb(
        (top.R * alpha + bottom.R * (255 - alpha)) / 255,
        (top.G * alpha + bottom.G * (255 - alpha)) / 255,
        (top.B * alpha + bottom.B * (255 - alpha)) / 255);
}

/// <summary>A row of a <see cref="VirtualList"/>. Concrete rows carry their data; the owner paints them.</summary>
internal abstract class ListRow
{
    /// <summary>Identity used to keep the selection, focus and scroll position across refreshes.</summary>
    public abstract object Key { get; }

    /// <summary>Logical height: 32 (group header, summary), 36 (one-line) or 44 (two-line).</summary>
    public abstract int LogicalHeight { get; }

    /// <summary>Whether the row can be selected (group headers and summaries cannot).</summary>
    public virtual bool Selectable => true;

    /// <summary>Accessible name: tier, title, device, time for event rows.</summary>
    public abstract string AccessibleName { get; }
}

/// <summary>Per-row paint state handed to the owner's painter.</summary>
internal readonly record struct RowState(bool Hover, bool Selected, bool Focus, bool Pressed);

/// <summary>
/// A virtual list: only visible rows are drawn, row heights come from a fixed set, and scrolling allocates
/// nothing. Multi-select with Ctrl and Shift, Ctrl+A, Space; Enter activates; arrows, PgUp, PgDn, Home
/// and End move. The owner paints rows and answers hit tests for hover actions.
/// </summary>
internal sealed class VirtualList : KitControl
{
    private IReadOnlyList<ListRow> _rows = [];
    private int[] _offsets = [0];
    private readonly HashSet<object> _selected = [];
    private int _focus = -1;
    private int _anchor = -1;
    private int _hover = -1;
    private float _scroll;
    private bool _scrollHover;
    private bool _dragging;
    private float _dragStart;
    private float _dragScroll;
    private int _bottomPadding;
    private int _topPadding;

    public VirtualList()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        Backdrop = static t => t.Surface;
        AccessibleRole = AccessibleRole.List;
    }

    /// <summary>Paints one row: graphics, row, bounds (device pixels), state.</summary>
    public Action<Graphics, ListRow, Rectangle, RowState>? RowPainter { get; set; }

    /// <summary>Hit test for actions inside a row (point relative to the row); returns an action id or null.</summary>
    public Func<ListRow, Point, Rectangle, string?>? RowHitTest { get; set; }

    public event EventHandler? SelectionChanged;

    public event EventHandler<ListRow>? RowActivated;

    public event EventHandler<(ListRow Row, string Action)>? RowAction;

    public event EventHandler<(ListRow? Row, Point Screen)>? ContextMenuRequested;

    /// <summary>Raised when Esc is pressed with nothing selected, so the owner can close what is open.</summary>
    public event EventHandler? EscapePressed;

    /// <summary>Raised when the visible range or the scroll position changes.</summary>
    public event EventHandler? Scrolled;

    public IReadOnlyList<ListRow> Rows => _rows;

    public int FocusIndex => _focus;

    public int HoverIndex => _hover;

    public float ScrollOffset => _scroll;

    /// <summary>Content height in device pixels.</summary>
    public int ContentHeight => _offsets[^1] + _topPadding + _bottomPadding;

    /// <summary>Extra space at the bottom (64 px while the selection bar shows).</summary>
    public int BottomPadding
    {
        get => _bottomPadding;
        set { _bottomPadding = value; ClampScroll(); Invalidate(); }
    }

    /// <summary>Space reserved at the top (the new-events pill).</summary>
    public int TopPadding
    {
        get => _topPadding;
        set { _topPadding = value; ClampScroll(); Invalidate(); }
    }

    /// <summary>When true, the list shows skeleton rows instead of its (empty) content.</summary>
    public bool Loading { get; set; }

    public IReadOnlyCollection<object> SelectedKeys => _selected;

    public IEnumerable<ListRow> SelectedRows
    {
        get
        {
            foreach (ListRow row in _rows)
            {
                if (_selected.Contains(row.Key))
                {
                    yield return row;
                }
            }
        }
    }

    public ListRow? FocusedRow => _focus >= 0 && _focus < _rows.Count ? _rows[_focus] : null;

    /// <summary>Replaces the rows, keeping the selection, focus and scroll position by identity.</summary>
    public void SetRows(IReadOnlyList<ListRow> rows)
    {
        object? focusKey = FocusedRow?.Key;
        object? anchorKey = _anchor >= 0 && _anchor < _rows.Count ? _rows[_anchor].Key : null;
        object? topKey = null;
        float topDelta = 0;
        int top = IndexAt(_scroll);
        if (top >= 0 && top < _rows.Count)
        {
            topKey = _rows[top].Key;
            topDelta = _scroll - _offsets[top];
        }

        _rows = rows;
        _offsets = new int[rows.Count + 1];
        int dpi = Dpi;
        for (int i = 0; i < rows.Count; i++)
        {
            _offsets[i + 1] = _offsets[i] + ThemeService.S(dpi, rows[i].LogicalHeight);
        }

        HashSet<object> keys = new(rows.Count);
        foreach (ListRow row in rows)
        {
            keys.Add(row.Key);
        }

        bool selectionChanged = _selected.RemoveWhere(key => !keys.Contains(key)) > 0;
        _focus = focusKey is null ? -1 : IndexOfKey(focusKey);
        _anchor = anchorKey is null ? -1 : IndexOfKey(anchorKey);
        if (topKey is not null && IndexOfKey(topKey) is var newTop && newTop >= 0)
        {
            _scroll = _offsets[newTop] + topDelta;
        }

        _hover = -1;
        ClampScroll();
        Invalidate();
        if (selectionChanged)
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public int IndexOfKey(object key)
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            if (Equals(_rows[i].Key, key))
            {
                return i;
            }
        }

        return -1;
    }

    public void ClearSelection()
    {
        if (_selected.Count > 0)
        {
            _selected.Clear();
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Selects one row only, focuses it and scrolls it into view.</summary>
    public void SelectOnly(int index)
    {
        if (index < 0 || index >= _rows.Count)
        {
            return;
        }

        _selected.Clear();
        if (_rows[index].Selectable)
        {
            _selected.Add(_rows[index].Key);
        }

        _focus = index;
        _anchor = index;
        EnsureVisible(index);
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool IsSelected(ListRow row) => _selected.Contains(row.Key);

    public void ScrollTo(float offset)
    {
        float previous = _scroll;
        _scroll = offset;
        ClampScroll();
        if (previous != _scroll)
        {
            Invalidate();
            Scrolled?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ScrollBy(float delta) => ScrollTo(_scroll + delta);

    public void EnsureVisible(int index)
    {
        if (index < 0 || index >= _rows.Count)
        {
            return;
        }

        float top = _offsets[index] + _topPadding;
        float bottom = _offsets[index + 1] + _topPadding;
        if (top < _scroll)
        {
            ScrollTo(top);
        }
        else if (bottom > _scroll + Height)
        {
            ScrollTo(bottom - Height);
        }
    }

    /// <summary>Index of the row at a content offset (binary search); -1 when there is none.</summary>
    public int IndexAt(float contentY)
    {
        float y = contentY - _topPadding;
        if (_rows.Count == 0 || y < 0 || y >= _offsets[^1])
        {
            return -1;
        }

        int low = 0;
        int high = _rows.Count - 1;
        while (low < high)
        {
            int mid = (low + high + 1) >> 1;
            if (_offsets[mid] <= y)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return low;
    }

    /// <summary>The row's bounds in client coordinates.</summary>
    public Rectangle RowBounds(int index) =>
        new(0, (int)(_offsets[index] + _topPadding - _scroll), Width, _offsets[index + 1] - _offsets[index]);

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ClampScroll();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        SetRows(_rows);
    }

    protected override void PaintContent(Graphics g)
    {
        if (Loading && _rows.Count == 0)
        {
            float y = Sf(14);
            for (int i = 0; y < Height; i++)
            {
                Surfaces.SkeletonRow(g, Sf(16), y, Sf(150 + (i * 37 % 90)), Sf(220 + (i * 53 % 120)), Dpi);
                y += Sf(44);
            }

            return;
        }

        int first = Math.Max(0, IndexAt(_scroll));
        Action<Graphics, ListRow, Rectangle, RowState>? painter = RowPainter;
        bool focused = Focused && KeyboardCues.ShowFocus;
        for (int i = first; i < _rows.Count; i++)
        {
            Rectangle bounds = RowBounds(i);
            if (bounds.Top >= Height)
            {
                break;
            }

            ListRow row = _rows[i];
            RowState state = new(i == _hover, _selected.Contains(row.Key), focused && i == _focus, false);
            painter?.Invoke(g, row, bounds, state);
        }

        RowPaint.Scrollbar(g, new RectangleF(0, 0, Width, Height), Height, ContentHeight, _scroll, _scrollHover || _dragging, Dpi);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ScrollBy(-e.Delta / 120f * S(44) * 3);
        UpdateHover(e.Location);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            float usable = Height - Sf(8);
            RectangleF thumb = RowPaint.ScrollThumb(new RectangleF(0, 0, Width, Height), Height, ContentHeight, _scroll, true, Dpi);
            float scrollable = ContentHeight - Height;
            float trackRange = usable - thumb.Height;
            if (trackRange > 0)
            {
                ScrollTo(_dragScroll + (e.Y - _dragStart) * scrollable / trackRange);
            }

            return;
        }

        bool overBar = e.X >= Width - S(16) && ContentHeight > Height;
        if (overBar != _scrollHover)
        {
            _scrollHover = overBar;
            Invalidate();
        }

        UpdateHover(e.Location);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _scrollHover = false;
        if (_hover != -1)
        {
            _hover = -1;
            Invalidate();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (_scrollHover && e.Button == MouseButtons.Left)
        {
            RectangleF thumb = RowPaint.ScrollThumb(new RectangleF(0, 0, Width, Height), Height, ContentHeight, _scroll, true, Dpi);
            if (thumb.Contains(e.Location))
            {
                _dragging = true;
                _dragStart = e.Y;
                _dragScroll = _scroll;
            }
            else
            {
                ScrollBy(e.Y < thumb.Top ? -Height : Height);
            }

            return;
        }

        int index = IndexAt(e.Y + _scroll);
        if (index < 0)
        {
            if (e.Button == MouseButtons.Left && (ModifierKeys & (Keys.Control | Keys.Shift)) == 0)
            {
                ClearSelection();
            }

            return;
        }

        ListRow row = _rows[index];
        Rectangle bounds = RowBounds(index);
        string? action = RowHitTest?.Invoke(row, new Point(e.X - bounds.X, e.Y - bounds.Y), bounds);
        if (e.Button == MouseButtons.Left && action is not null)
        {
            _focus = index;
            RowAction?.Invoke(this, (row, action));
            return;
        }

        if (e.Button == MouseButtons.Right)
        {
            if (!_selected.Contains(row.Key))
            {
                SelectOnly(index);
            }

            ContextMenuRequested?.Invoke(this, (row, PointToScreen(e.Location)));
            return;
        }

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (!row.Selectable)
        {
            _focus = index;
            RowActivated?.Invoke(this, row);
            Invalidate();
            return;
        }

        if ((ModifierKeys & Keys.Control) != 0)
        {
            if (!_selected.Remove(row.Key))
            {
                _selected.Add(row.Key);
            }

            _focus = index;
            _anchor = index;
        }
        else if ((ModifierKeys & Keys.Shift) != 0 && _anchor >= 0)
        {
            SelectRange(_anchor, index);
            _focus = index;
        }
        else
        {
            _selected.Clear();
            _selected.Add(row.Key);
            _focus = index;
            _anchor = index;
        }

        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        int index = IndexAt(e.Y + _scroll);
        if (index >= 0 && _rows[index].Selectable && e.Button == MouseButtons.Left)
        {
            RowActivated?.Invoke(this, _rows[index]);
        }
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End or Keys.Space or Keys.Enter or Keys.Escape
        || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_rows.Count == 0)
        {
            if (e.KeyCode == Keys.Escape)
            {
                EscapePressed?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        int pageRows = Math.Max(1, Height / S(44));
        int target = e.KeyCode switch
        {
            Keys.Up => _focus - 1,
            Keys.Down => _focus + 1,
            Keys.PageUp => _focus - pageRows,
            Keys.PageDown => _focus + pageRows,
            Keys.Home => 0,
            Keys.End => _rows.Count - 1,
            _ => int.MinValue
        };

        if (target != int.MinValue)
        {
            e.Handled = true;
            target = Math.Clamp(target, 0, _rows.Count - 1);
            if (e.Shift)
            {
                if (_anchor < 0)
                {
                    _anchor = Math.Max(0, _focus);
                }

                SelectRange(_anchor, target);
                _focus = target;
                EnsureVisible(target);
                Invalidate();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                _focus = target;
                _anchor = target;
                EnsureVisible(target);
                Invalidate();
                FocusMoved?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        switch (e.KeyCode)
        {
            case Keys.Space when FocusedRow is { } row:
                e.Handled = true;
                e.SuppressKeyPress = true;
                if (!row.Selectable)
                {
                    RowActivated?.Invoke(this, row);
                    break;
                }

                if (!_selected.Remove(row.Key))
                {
                    _selected.Add(row.Key);
                }

                _anchor = _focus;
                Invalidate();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                break;
            case Keys.Enter when FocusedRow is { } row:
                e.Handled = true;
                e.SuppressKeyPress = true;
                RowActivated?.Invoke(this, row);
                break;
            case Keys.A when e.Control:
                e.Handled = true;
                _selected.Clear();
                foreach (ListRow each in _rows)
                {
                    if (each.Selectable)
                    {
                        _selected.Add(each.Key);
                    }
                }

                Invalidate();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                break;
            case Keys.Escape:
                e.Handled = true;
                EscapePressed?.Invoke(this, EventArgs.Empty);
                break;
            case Keys.Apps:
                e.Handled = true;
                if (FocusedRow is { } focusedRow)
                {
                    Rectangle bounds = RowBounds(_focus);
                    ContextMenuRequested?.Invoke(this, (focusedRow, PointToScreen(new Point(bounds.X + S(44), bounds.Bottom))));
                }

                break;
        }
    }

    /// <summary>Raised when the keyboard focus moves without changing the selection.</summary>
    public event EventHandler? FocusMoved;

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        if (_focus < 0 && _rows.Count > 0)
        {
            _focus = IndexAt(_scroll) is var top && top >= 0 ? top : 0;
        }

        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new ListAccessibility(this);

    private void SelectRange(int from, int to)
    {
        _selected.Clear();
        int low = Math.Min(from, to);
        int high = Math.Max(from, to);
        for (int i = low; i <= high; i++)
        {
            if (_rows[i].Selectable)
            {
                _selected.Add(_rows[i].Key);
            }
        }
    }

    private void UpdateHover(Point location)
    {
        int index = _scrollHover ? -1 : IndexAt(location.Y + _scroll);
        if (index != _hover)
        {
            _hover = index;
            Invalidate();
        }
    }

    private void ClampScroll()
    {
        float max = Math.Max(0, ContentHeight - Height);
        _scroll = Math.Clamp(_scroll, 0, max);
    }

    /// <summary>Exposes the visible rows to UI Automation with their name, role and selection state.</summary>
    private sealed class ListAccessibility(VirtualList list) : ControlAccessibleObject(list)
    {
        public override AccessibleRole Role => AccessibleRole.List;

        public override int GetChildCount()
        {
            (int first, int last) = Visible();
            return last < first ? 0 : last - first + 1;
        }

        public override AccessibleObject? GetChild(int index)
        {
            (int first, _) = Visible();
            int row = first + index;
            return row >= 0 && row < list._rows.Count ? new RowAccessibility(list, row, this) : null;
        }

        private (int First, int Last) Visible()
        {
            int first = Math.Max(0, list.IndexAt(list._scroll));
            int last = list.IndexAt(list._scroll + list.Height - 1);
            if (last < 0)
            {
                last = list._rows.Count - 1;
            }

            return (first, last);
        }
    }

    private sealed class RowAccessibility(VirtualList list, int index, AccessibleObject parent) : AccessibleObject
    {
        public override string? Name
        {
            get => list._rows[index].AccessibleName;
            set { }
        }

        public override AccessibleRole Role => list._rows[index].Selectable ? AccessibleRole.ListItem : AccessibleRole.Grouping;

        public override AccessibleStates State
        {
            get
            {
                AccessibleStates state = AccessibleStates.Selectable | AccessibleStates.Focusable;
                if (list._selected.Contains(list._rows[index].Key))
                {
                    state |= AccessibleStates.Selected;
                }

                if (index == list._focus && list.Focused)
                {
                    state |= AccessibleStates.Focused;
                }

                return state;
            }
        }

        public override AccessibleObject Parent => parent;

        public override Rectangle Bounds => list.RectangleToScreen(list.RowBounds(index));
    }
}
