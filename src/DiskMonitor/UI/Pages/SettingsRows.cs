using DiskMonitor.App;
using DiskMonitor.UI.Controls;
using DiskMonitor.UI.Theming;

namespace DiskMonitor.UI.Pages;

/// <summary>A part at the right of a settings row: a control, or painted content that measures itself.</summary>
internal sealed class RowItem
{
    public Control? Control { get; init; }

    /// <summary>Paints at (x, centre y) and returns its width; called with a null Graphics to measure.</summary>
    public Func<Graphics?, float, float, float>? Painter { get; init; }

    /// <summary>When set, the control is centred on this x (a function of the card width) instead of flowing.</summary>
    public Func<int, int>? CenterX { get; init; }

    public Func<bool>? Shown { get; init; }

    public bool IsShown => Shown?.Invoke() ?? true;
}

/// <summary>
/// One settings row (subplan 08, section 1): label in Body, an optional helper in Caption Text3 beneath it,
/// and its controls right-aligned 16 px from the card edge, 8 apart. Rows are data, so layout and
/// painting are loops.
/// </summary>
internal sealed class SettingRow
{
    public required string Label { get; set; }

    public Func<string?>? Helper { get; init; }

    /// <summary>True when the helper reports a problem (drawn in HighText).</summary>
    public Func<bool>? HelperWarns { get; init; }

    public Func<bool>? Shown { get; init; }

    public List<RowItem> Items { get; init; } = [];

    /// <summary>Paints in place of the label, at (x, centre y): the Alerts tier badge.</summary>
    public Action<Graphics, float, float>? PaintLabel { get; init; }

    /// <summary>Paints the whole row instead of a label and helper: the Alerts column captions.</summary>
    public Action<Graphics, RectangleF>? PaintRow { get; init; }

    public int MinHeight { get; init; } = 56;

    public bool IsShown => Shown?.Invoke() ?? true;
}

/// <summary>A card of settings rows separated by 1 px Stroke lines.</summary>
internal sealed class RowsCard : Card
{
    private readonly IReadOnlyList<SettingRow> _rows;

    public RowsCard(IReadOnlyList<SettingRow> rows)
    {
        _rows = rows;
        Backdrop = static t => t.Bg;
        foreach (SettingRow row in rows)
        {
            foreach (RowItem item in row.Items)
            {
                if (item.Control is { } control)
                {
                    if (control is KitControl kit)
                    {
                        kit.Backdrop = static t => t.Surface;
                    }

                    if (control is DropDown drop)
                    {
                        drop.MarkChanged = false;
                    }

                    Controls.Add(control);
                }
            }
        }
    }

    public int Measure(int width)
    {
        int height = 0;
        foreach (SettingRow row in _rows)
        {
            if (row.IsShown)
            {
                height += RowHeight(row, width);
            }
        }

        return Math.Max(height, S(1));
    }

    /// <summary>The size a control takes in a row (switches 40 × 20; drop-downs as wide as their widest item).</summary>
    public Size ControlSize(Control control) => control switch
    {
        KitSwitch => new Size(S(40), S(20)),
        DropDown drop => new Size(DropWidth(drop), S(32)),
        SegmentedControl segmented => new Size(segmented.PreferredWidth, S(32)),
        KitButton button => new Size(button.PreferredWidth, button.ButtonHeight),
        _ => control.Size
    };

    private int DropWidth(DropDown drop)
    {
        Font body = F(FontRole.Body);
        int widest = drop.Items.Count == 0 ? S(40) : drop.Items.Max(item => Draw.TextWidth(drop.LabelFormat?.Invoke(item) ?? item, body));
        return widest + S(12 + 8 + 26);
    }

    private float ItemWidth(RowItem item) => item.Control is { } control ? ControlSize(control).Width : item.Painter?.Invoke(null, 0, 0) ?? 0;

    /// <summary>Left edge of the row's right-hand parts: the label and helper end 16 px before it.</summary>
    private float ItemsLeft(SettingRow row, int width)
    {
        float left = width - S(16);
        bool any = false;
        foreach (RowItem item in row.Items)
        {
            if (!item.IsShown)
            {
                continue;
            }

            if (item.CenterX is { } center)
            {
                float w = item.Control is { } control ? ControlSize(control).Width : 0;
                left = Math.Min(left, center(width) - w / 2f);
                continue;
            }

            left -= ItemWidth(item) + (any ? S(8) : 0);
            any = true;
        }

        return left;
    }

    private int TextWidth(SettingRow row, int width) => Math.Max(S(40), (int)(ItemsLeft(row, width) - S(16) - S(16)));

    private List<string> HelperLines(SettingRow row, int width) =>
        row.Helper?.Invoke() is { Length: > 0 } helper ? Draw.Wrap(helper, F(FontRole.Caption), TextWidth(row, width)) : [];

    private int RowHeight(SettingRow row, int width) =>
        row.PaintRow is not null ? S(row.MinHeight) : Math.Max(S(row.MinHeight), S(20) + HelperLines(row, width).Count * S(16) + S(20));

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int y = 0;
        foreach (SettingRow row in _rows)
        {
            bool shown = row.IsShown;
            foreach (RowItem item in row.Items)
            {
                if (item.Control is { } control)
                {
                    control.Visible = shown && item.IsShown;
                }
            }

            if (!shown)
            {
                continue;
            }

            int height = RowHeight(row, Width);
            float x = Width - S(16);
            for (int i = row.Items.Count - 1; i >= 0; i--)
            {
                RowItem item = row.Items[i];
                if (!item.IsShown)
                {
                    continue;
                }

                if (item.CenterX is { } center)
                {
                    if (item.Control is { } centred)
                    {
                        Size size = ControlSize(centred);
                        centred.SetBounds(center(Width) - size.Width / 2, y + (height - size.Height) / 2, size.Width, size.Height);
                    }

                    continue;
                }

                x -= ItemWidth(item);
                if (item.Control is { } control)
                {
                    Size size = ControlSize(control);
                    control.SetBounds((int)x, y + (height - size.Height) / 2, size.Width, size.Height);
                }

                x -= S(8);
            }

            y += height;
        }
    }

    protected override void PaintCard(Graphics g)
    {
        ThemeTokens t = T;
        float s = Sc;
        Font body = F(FontRole.Body);
        Font caption = F(FontRole.Caption);
        int y = 0;
        bool first = true;
        foreach (SettingRow row in _rows)
        {
            if (!row.IsShown)
            {
                continue;
            }

            int height = RowHeight(row, Width);
            if (!first)
            {
                float line = (float)Math.Floor((double)y) + 0.5f;
                Draw.Line(g, t.Stroke, s, 16 * s, line, Width - 16 * s, line);
            }

            first = false;
            if (row.PaintRow is { } paintRow)
            {
                paintRow(g, new RectangleF(0, y, Width, height));
            }
            else
            {
                int textWidth = TextWidth(row, Width);
                List<string> help = HelperLines(row, Width);
                float top = y + (height - (S(20) + help.Count * S(16))) / 2f;
                if (row.PaintLabel is { } paintLabel)
                {
                    paintLabel(g, 16 * s, top + 10 * s);
                }
                else
                {
                    Draw.Text(g, row.Label, body, 16 * s, top + 10 * s, t.Text, textWidth);
                }

                Color helpColor = row.HelperWarns?.Invoke() == true ? t.HighText : t.Text3;
                for (int i = 0; i < help.Count; i++)
                {
                    Draw.Text(g, help[i], caption, 16 * s, top + (28 + 16 * i) * s, helpColor, textWidth);
                }
            }

            float x = Width - 16 * s;
            float cy = y + height / 2f;
            for (int i = row.Items.Count - 1; i >= 0; i--)
            {
                RowItem item = row.Items[i];
                if (!item.IsShown || item.CenterX is not null)
                {
                    continue;
                }

                float w = ItemWidth(item);
                x -= w;
                item.Painter?.Invoke(g, x, cy);
                x -= 8 * s;
            }

            y += height;
        }
    }
}

/// <summary>
/// One settings section: its name in Subtitle, an intro line in Body Text2, then its content (subplan 08,
/// section 1). Values are read in <see cref="Reload"/> (on activation and on a settings refresh).
/// </summary>
internal abstract class SettingsSectionView : KitControl
{
    protected SettingsSectionView(SettingsPage page, string key, string title, string? intro)
    {
        Page = page;
        Key = key;
        Title = title;
        Intro = intro;
        Backdrop = static t => t.Bg;
        SetStyle(ControlStyles.ContainerControl, true);
    }

    public SettingsPage Page { get; }

    public string Key { get; }

    public string Title { get; }

    public string? Intro { get; }

    protected AppServices Services => Page.AppServices;

    /// <summary>True while values are read into the controls, so their change handlers do nothing.</summary>
    protected bool Loading { get; private set; }

    /// <summary>Reads every value into its control.</summary>
    public abstract void Reload();

    /// <summary>Called when the section is shown, and when the page is activated.</summary>
    public virtual void Activate() => Reload();

    protected void Load(Action read)
    {
        Loading = true;
        try
        {
            read();
        }
        finally
        {
            Loading = false;
        }

        Invalidate(true);
    }

    /// <summary>Responds to a shell refresh; a settings change reloads the values.</summary>
    public virtual void OnRefresh(RefreshReason reasons)
    {
        if ((reasons & RefreshReason.Settings) != 0)
        {
            Reload();
        }
    }

    /// <summary>Lays the section out for a width and returns its height.</summary>
    public int Arrange(int width) => ArrangeContent(width, ContentTop(width));

    protected abstract int ArrangeContent(int width, int top);

    private List<string> IntroLines(int width) => Intro is null ? [] : Draw.Wrap(Intro, F(FontRole.Body), Math.Max(S(40), width));

    /// <summary>Where the content starts: 64 px down after a one-line intro, 40 without one.</summary>
    protected int ContentTop(int width)
    {
        int lines = IntroLines(width).Count;
        return lines == 0 ? S(40) : S(64) + (lines - 1) * S(20);
    }

    protected override void PaintContent(Graphics g)
    {
        float s = Sc;
        Draw.Text(g, Title, F(FontRole.Subtitle), 0, 15 * s, T.Text, Width);
        List<string> lines = IntroLines(Width);
        for (int i = 0; i < lines.Count; i++)
        {
            Draw.Text(g, lines[i], F(FontRole.Body), 0, (41 + 20 * i) * s, T.Text2, Width);
        }
    }

    /// <summary>Opens a folder, file or address with the shell (subplan 08: <c>UseShellExecute = true</c>).</summary>
    protected static void OpenWithShell(string target)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            Diagnostics.ErrorLog.Write("Open " + Path.GetFileName(target), ex);
        }
    }
}

/// <summary>The section list (subplan 08, section 1): 168 px wide, one 36 px item per section.</summary>
internal sealed class SectionList : InteractiveKitControl
{
    private readonly SettingsPage _page;
    private int _hover = -1;

    public SectionList(SettingsPage page)
    {
        _page = page;
        AccessibleRole = AccessibleRole.List;
        AccessibleName = Strings.PageSettings;
    }

    public override bool FocusRingVisible => false;

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int count = SettingsPage.SectionKeys.Length;
        int index = Array.IndexOf(SettingsPage.SectionKeys, _page.CurrentSection);
        int next = e.KeyCode switch
        {
            Keys.Up => Math.Max(0, index - 1),
            Keys.Down => Math.Min(count - 1, index + 1),
            Keys.Home => 0,
            Keys.End => count - 1,
            _ => -1
        };
        if (next >= 0)
        {
            e.Handled = true;
            _page.ShowSection(SettingsPage.SectionKeys[next]);
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = e.Y / S(36);
        index = index >= 0 && index < SettingsPage.SectionKeys.Length ? index : -1;
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

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        int index = PointToClient(MousePosition).Y / S(36);
        if (index >= 0 && index < SettingsPage.SectionKeys.Length)
        {
            _page.ShowSection(SettingsPage.SectionKeys[index]);
        }
    }

    protected override void PaintContent(Graphics g)
    {
        ThemeTokens t = T;
        float s = Sc;
        string current = _page.CurrentSection;
        for (int i = 0; i < SettingsPage.SectionKeys.Length; i++)
        {
            string key = SettingsPage.SectionKeys[i];
            RectangleF r = new(0, i * 36 * s, Width, 36 * s);
            bool selected = key == current;
            if (selected)
            {
                Draw.FillRounded(g, r, 4 * s, t.Selected);
                Draw.FillRounded(g, new RectangleF(r.X, r.Y + 10 * s, 3 * s, 16 * s), 1.5f * s, t.Accent);
            }
            else if (i == _hover)
            {
                Draw.FillRounded(g, r, 4 * s, t.Hover);
                Draw.FillRounded(g, new RectangleF(r.X, r.Y + 10 * s, 3 * s, 16 * s), 1.5f * s, t.IsHighContrast ? t.Accent : t.StrokeStrong);
            }

            string label = SettingsPage.SectionTitle(key);
            Font font = F(selected ? FontRole.BodyStrong : FontRole.Body);
            Draw.Text(g, label, font, 16 * s, r.Y + 18 * s, selected ? t.OnSelected : t.Text, (int)(r.Width - 32 * s));
            if (key == "updates" && _page.UpdateReady)
            {
                Draw.Circle(g, selected && t.IsHighContrast ? t.OnSelected : t.Accent, 16 * s + Draw.TextWidth(label, font) + 8 * s, r.Y + 18 * s, 3 * s);
            }

            if (selected && ShowFocus)
            {
                Draw.FocusRing(g, r, 4 * s, Dpi);
            }
        }
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new SectionListAccessibility(this);

    private sealed class SectionListAccessibility(SectionList list) : ControlAccessibleObject(list)
    {
        public override int GetChildCount() => SettingsPage.SectionKeys.Length;

        public override AccessibleObject? GetChild(int index) =>
            index >= 0 && index < SettingsPage.SectionKeys.Length ? new ItemAccessibility(list, index, this) : null;
    }

    private sealed class ItemAccessibility(SectionList list, int index, AccessibleObject parent) : AccessibleObject
    {
        public override string? Name
        {
            get => SettingsPage.SectionTitle(SettingsPage.SectionKeys[index]);
            set { }
        }

        public override AccessibleRole Role => AccessibleRole.ListItem;

        public override AccessibleObject Parent => parent;

        public override AccessibleStates State =>
            SettingsPage.SectionKeys[index] == list._page.CurrentSection ? AccessibleStates.Selected | AccessibleStates.Selectable : AccessibleStates.Selectable;

        public override Rectangle Bounds => list.RectangleToScreen(new Rectangle(0, index * list.S(36), list.Width, list.S(36)));

        public override void DoDefaultAction() => list._page.ShowSection(SettingsPage.SectionKeys[index]);
    }
}
