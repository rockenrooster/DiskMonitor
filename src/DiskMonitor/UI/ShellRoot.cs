using System.Text.RegularExpressions;
using DiskMonitor.App;
using DiskMonitor.Core;
using DiskMonitor.Core.Chkdsk;
using DiskMonitor.Core.Events;
using DiskMonitor.UI.Controls;
using DiskMonitor.UI.Pages;
using DiskMonitor.UI.Theming;

namespace DiskMonitor.UI;

/// <summary>
/// The window's client area: the navigation rail, the header with the page title and toolbar, the
/// scrolling content area and the status strip, laid out from subplan 02's shell geometry.
/// </summary>
internal sealed class ShellRoot : KitControl
{
    private readonly MainForm _form;
    private readonly AppServices _services;
    private readonly NavItemControl[] _nav;
    private readonly NavItemControl _update;
    private readonly NavItemControl _settings;
    private readonly PageViewport _viewport;
    private readonly KitButton _stop = new() { Text = Strings.ButtonStop, Compact = true, Variant = ButtonVariant.Subtle, Visible = false };
    private readonly List<Control> _toolbar = [];
    private PageBase? _page;
    private DrawerPanel? _drawer;
    private float _drawerShown;
    private long _drawerAnimationStart;
    private bool _drawerOpening;
    private bool _stripVisible;
    private bool _animating;
    private string _stripText = "";
    private int? _stripPercent;
    private bool _stripOpensCheckDisk;

    public ShellRoot(MainForm form, AppServices services)
    {
        _form = form;
        _services = services;
        Backdrop = static t => t.Bg;
        SetStyle(ControlStyles.ContainerControl, true);
        _nav =
        [
            new NavItemControl("Overview", Strings.PageOverview, IconName.Dashboard),
            new NavItemControl("Events", Strings.PageEvents, IconName.Issues),
            new NavItemControl("Drives", Strings.PageDrives, IconName.Drive),
            new NavItemControl("CheckDisk", Strings.PageCheckDisk, IconName.Scans)
        ];
        _update = new NavItemControl("Update", Strings.NavUpdateAvailable, IconName.Update) { Visible = false, AccentIcon = true };
        _settings = new NavItemControl("Settings", Strings.PageSettings, IconName.Settings);
        foreach (NavItemControl item in _nav.Append(_update).Append(_settings))
        {
            item.Click += OnNavClick;
            Controls.Add(item);
        }

        _viewport = new PageViewport();
        Controls.Add(_viewport);
        _stop.Backdrop = static t => t.Surface;
        _stop.Click += OnStopClick;
        Controls.Add(_stop);
        AccessibleRole = AccessibleRole.Client;
    }

    public bool StripVisible => _stripVisible;

    private bool Collapsed => Width < S(1100);

    private int RailWidth => S(Collapsed ? 56 : 200);

    public void SetPage(PageBase page)
    {
        foreach (Control old in _toolbar)
        {
            Controls.Remove(old);
        }

        _toolbar.Clear();
        _page = page;
        foreach (Control control in page.Toolbar)
        {
            if (control is KitControl kit)
            {
                kit.Backdrop = static t => t.Bg;
            }

            _toolbar.Add(control);
            Controls.Add(control);
        }

        _viewport.SetPage(page);
        LayoutShell();
        Invalidate();
    }

    public void LayoutShell()
    {
        PerformLayout();
        Invalidate();
    }

    public DrawerPanel? Drawer => _drawer;

    /// <summary>Shows a drawer at the right edge; it slides in over 120 ms (ease-out cubic).</summary>
    public void ShowDrawer(DrawerPanel drawer)
    {
        if (_drawer != drawer)
        {
            if (_drawer is not null)
            {
                Controls.Remove(_drawer);
            }

            _drawer = drawer;
            drawer.WidthChanging += OnDrawerResize;
            Controls.Add(drawer);
            drawer.BringToFront();
            _drawerShown = 0;
        }

        drawer.Visible = true;
        StartDrawerAnimation(opening: true);
    }

    /// <summary>Closes the drawer; it slides out over 100 ms.</summary>
    public void HideDrawer(bool animate = true)
    {
        if (_drawer is null || !_drawer.Visible)
        {
            return;
        }

        if (!animate || !ThemeService.AnimationsEnabled)
        {
            FinishHide();
            return;
        }

        StartDrawerAnimation(opening: false);
    }

    private void StartDrawerAnimation(bool opening)
    {
        _drawerOpening = opening;
        if (!ThemeService.AnimationsEnabled)
        {
            _drawerShown = opening ? 1 : 0;
            if (!opening)
            {
                FinishHide();
            }

            PerformLayout();
            return;
        }

        _drawerAnimationStart = KitAnimator.Now;
        KitAnimator.Add(OnDrawerTick);
    }

    private void OnDrawerTick(long now)
    {
        float duration = _drawerOpening ? 120f : 100f;
        float t = Math.Clamp((now - _drawerAnimationStart) / duration, 0, 1);
        float eased = 1 - MathF.Pow(1 - t, 3);
        _drawerShown = _drawerOpening ? eased : 1 - eased;
        if (t >= 1)
        {
            KitAnimator.Remove(OnDrawerTick);
            if (!_drawerOpening)
            {
                FinishHide();
                return;
            }
        }

        PerformLayout();
    }

    private void FinishHide()
    {
        KitAnimator.Remove(OnDrawerTick);
        if (_drawer is not null)
        {
            _drawer.Visible = false;
            _drawerShown = 0;
        }

        PerformLayout();
        Invalidate();
    }

    private void OnDrawerResize(object? sender, EventArgs e) => PerformLayout();

    /// <summary>Badges, the scan indicator and the update item, from current state.</summary>
    public void UpdateRail()
    {
        string current = _form.CurrentPageKey;
        foreach (NavItemControl item in _nav.Append(_settings))
        {
            item.Selected = item.Key == current;
        }

        UnreadSummary unread = _services.Events.GetUnread();
        int count = unread.Critical + unread.High + unread.Medium;
        Tier? worst = unread.Critical > 0 ? Tier.Critical : unread.High > 0 ? Tier.High : unread.Medium > 0 ? Tier.Medium : null;
        _nav[1].SetBadge(count == 0 ? null : count > 999 ? Strings.BadgeOverflow : Format.Number(count), worst);
        _nav[3].Busy = _services.Chkdsk.IsRunning;
        bool update = _services.Updates.LastResult?.CanDownload == true;
        if (_update.Visible != update)
        {
            _update.Visible = update;
            PerformLayout();
        }

        Invalidate(new Rectangle(0, 0, RailWidth, Height));
    }

    /// <summary>The status strip: shown only while a CHKDSK run is active or history is loading.</summary>
    public void UpdateStrip()
    {
        bool narrow = Collapsed;
        bool visible;
        _stripPercent = null;
        _stripOpensCheckDisk = false;
        bool stop = false;
        if (_services.Chkdsk.IsRunning && _services.Chkdsk.RunningVolume is { } volume)
        {
            visible = true;
            _stripOpensCheckDisk = true;
            if (_services.Chkdsk.IsWaitingForApproval)
            {
                _stripText = Strings.StripWaiting(volume);
            }
            else
            {
                _stripPercent = _services.Chkdsk.ProgressPercent;
                TimeSpan? left = null;
                Match eta = MainForm.EtaPattern().Match(_services.Chkdsk.ProgressText);
                if (eta.Success)
                {
                    left = new TimeSpan(int.Parse(eta.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                        int.Parse(eta.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
                        int.Parse(eta.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture));
                }

                bool repair = !_services.Chkdsk.RunningMode.IsReadOnly();
                _stripText = Strings.StripScanning(volume, _stripPercent, left, repair, narrow);
                stop = !repair;
            }
        }
        else if (_services.Monitor.IsLoading)
        {
            visible = true;
            _stripText = Strings.StripReading;
        }
        else
        {
            visible = false;
            _stripText = "";
        }

        _stop.Visible = visible && stop;
        if (visible != _stripVisible)
        {
            _stripVisible = visible;
            PerformLayout();
        }

        SetAnimating(visible);
        Invalidate();
    }

    private void SetAnimating(bool value)
    {
        if (value == _animating)
        {
            return;
        }

        _animating = value;
        if (value)
        {
            KitAnimator.Add(OnAnimate);
        }
        else
        {
            KitAnimator.Remove(OnAnimate);
        }
    }

    private void OnAnimate(long now)
    {
        if (_stripVisible)
        {
            Invalidate(new Rectangle(RailWidth, Height - S(36), Width - RailWidth, S(36)));
        }
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        bool collapsed = Collapsed;
        int rail = RailWidth;
        int itemWidth = S(collapsed ? 40 : 184);
        for (int i = 0; i < _nav.Length; i++)
        {
            _nav[i].Collapsed = collapsed;
            _nav[i].SetBounds(S(8), S(64) + i * S(44), itemWidth, S(40));
        }

        _settings.Collapsed = collapsed;
        _settings.SetBounds(S(8), Height - S(8) - S(40), itemWidth, S(40));
        _update.Collapsed = collapsed;
        _update.SetBounds(S(8), Height - S(8) - S(40) - S(44), itemWidth, S(40));

        int x = Width - S(24);
        foreach (Control control in _toolbar)
        {
            if (control is KitButton button)
            {
                button.SizeToContent();
            }

            control.SetBounds(x - control.Width, S(12), control.Width, S(32));
            x -= control.Width + S(8);
        }

        int stripTop = Height - S(36);
        int contentLeft = rail + S(24);
        int contentBottom = _stripVisible ? stripTop - S(16) : Height - S(24);
        int contentRight = Width - S(24);
        if (_drawer is { Visible: true })
        {
            int drawerWidth = S(_drawer.LogicalWidth);
            int drawerLeft = Width - (int)Math.Round(drawerWidth * _drawerShown);
            _drawer.SetBounds(drawerLeft, S(56), drawerWidth, Math.Max(1, (_stripVisible ? stripTop : Height) - S(56)));

            // A content area of 900 px or more narrows; a smaller one is overlaid.
            if (contentRight - contentLeft >= S(900))
            {
                contentRight = Math.Min(contentRight, drawerLeft - S(16));
            }
        }

        _viewport.SetBounds(contentLeft, S(72), Math.Max(1, Width - contentLeft - (Width - S(24) - contentRight)), Math.Max(1, contentBottom - S(72)));
        _viewport.ContentWidth = Math.Max(1, contentRight - contentLeft);
        _viewport.ArrangePage();
        _stop.SizeToContent();
        _stop.Location = new Point(Width - S(16) - _stop.Width, stripTop + (S(36) - _stop.Height) / 2);
    }

    protected override void PaintContent(Graphics g)
    {
        ThemeTokens t = T;
        int rail = RailWidth;
        float line = rail - Sf(0.5f);
        Draw.Line(g, t.Stroke, Sc, line, 0, line, Height);
        if (Collapsed)
        {
            IconArt.Paint(g, Sf(16), Sf(16), Sf(24));
        }
        else
        {
            IconArt.Paint(g, Sf(18), Sf(16), Sf(24));
            Draw.Text(g, Strings.AppName, F(FontRole.BodyStrong), Sf(50), Sf(28), t.Text);
        }

        Draw.PrepareShapes(g);
        if (_page is not null)
        {
            int toolbarLeft = _toolbar.Count == 0 ? Width - S(24) : _toolbar.Min(c => c.Left) - S(16);
            Draw.Text(g, _page.Title, F(FontRole.Title), rail + Sf(24), Sf(28), t.Text, Math.Max(1, toolbarLeft - rail - S(24)));
        }

        if (_stripVisible)
        {
            PaintStrip(g, rail);
        }
    }

    private void PaintStrip(Graphics g, int rail)
    {
        ThemeTokens t = T;
        float top = Height - Sf(36);
        RectangleF strip = new(rail, top, Width - rail, Sf(36));
        g.FillRectangle(ThemeService.Resources.Brush(t.Surface), strip);
        float y = (float)Math.Floor(top) + Sf(0.5f);
        Draw.Line(g, t.Stroke, Sc, rail, y, Width, y);
        long now = KitAnimator.Now;
        RectangleF progress = new(rail, top, Width - rail, Sf(2));
        if (_stripPercent is { } percent)
        {
            g.FillRectangle(ThemeService.Resources.Brush(t.Accent), progress.X, progress.Y, progress.Width * Math.Clamp(percent, 0, 100) / 100f, progress.Height);
        }
        else
        {
            float phase = now % 1500 / 1500f;
            float eased = phase < 0.5f ? 2 * phase * phase : 1 - MathF.Pow(-2 * phase + 2, 2) / 2;
            float segment = progress.Width * 0.4f;
            float x = progress.X - segment + eased * (progress.Width + segment);
            RectangleF bar = RectangleF.Intersect(new RectangleF(x, progress.Y, segment, progress.Height), progress);
            if (bar.Width > 0)
            {
                g.FillRectangle(ThemeService.Resources.Brush(t.Accent), bar);
            }
        }

        DataGraphics.Ring(g, rail + Sf(32), top + Sf(18), Sf(8), Dpi, now);
        int right = _stop.Visible ? _stop.Left - S(16) : Width - S(16);
        Draw.Text(g, _stripText, F(FontRole.Body), rail + Sf(48), top + Sf(18), t.Text, Math.Max(1, right - rail - S(48)));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_stripVisible && _stripOpensCheckDisk && e.Y >= Height - S(36) && e.X >= RailWidth)
        {
            _form.ShowPage("CheckDisk");
        }
    }

    private void OnNavClick(object? sender, EventArgs e)
    {
        if (sender is not NavItemControl item)
        {
            return;
        }

        if (item == _update)
        {
            _form.ShowSettings("updates");
        }
        else
        {
            _form.ShowPage(item.Key);
        }
    }

    private void OnStopClick(object? sender, EventArgs e)
    {
        KitDialog dialog = new KitDialog(Strings.StopDialogTitle, 480, cancelResult: 0)
            .Paragraph(Strings.StopDialogText)
            .Button(new DialogButton(Strings.StopDialogCancel, ButtonVariant.Secondary, 0))
            .Button(new DialogButton(Strings.StopDialogConfirm, ButtonVariant.Primary, 1, InitialFocus: true));
        if (dialog.ShowOver(_form) == 1)
        {
            _services.Chkdsk.Cancel();
        }

        dialog.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            KitAnimator.Remove(OnAnimate);
            KitAnimator.Remove(OnDrawerTick);
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// A navigation item: 40 px, radius 4; icon at +14 and label at +42, or the icon alone when the rail is
/// collapsed (the label becomes the tooltip). Optional badge pill, or a progress ring while busy.
/// </summary>
internal sealed class NavItemControl : InteractiveKitControl
{
    private readonly IconName _icon;
    private readonly string _label;
    private string? _badge;
    private Tier? _badgeTier;
    private bool _selected;
    private bool _busy;
    private bool _collapsed;

    public NavItemControl(string key, string label, IconName icon)
    {
        Key = key;
        _label = label;
        _icon = icon;
        Backdrop = static t => t.Bg;
        AccessibleRole = AccessibleRole.PageTab;
        AccessibleName = label;
    }

    public string Key { get; }

    public bool AccentIcon { get; init; }

    public bool Selected
    {
        get => _selected;
        set { if (_selected != value) { _selected = value; Invalidate(); } }
    }

    public bool Collapsed
    {
        get => _collapsed;
        set
        {
            if (_collapsed != value)
            {
                _collapsed = value;
                ToolTipText = value ? _label : null;
                Invalidate();
            }
        }
    }

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

            Invalidate();
        }
    }

    public void SetBadge(string? text, Tier? tier)
    {
        if (_badge != text || _badgeTier != tier)
        {
            _badge = text;
            _badgeTier = tier;
            AccessibleDescription = text;
            Invalidate();
        }
    }

    protected override void PaintContent(Graphics g)
    {
        ThemeTokens t = T;
        RectangleF r = ClientRectangle;
        RowPaint.NavItem(g, r, _icon, _collapsed ? null : _label, ShowHover && !_selected, _selected, false, Dpi, AccentIcon ? t.Accent : null);
        Color behind = _selected ? t.Selected : ShowHover ? t.Hover : t.Bg;
        if (_busy)
        {
            if (_collapsed)
            {
                DataGraphics.Ring(g, r.Width / 2f + Sf(8), Sf(12), Sf(5), Dpi, KitAnimator.Now);
            }
            else
            {
                DataGraphics.Ring(g, r.Right - Sf(10) - Sf(8), r.Height / 2f, Sf(8), Dpi, KitAnimator.Now);
            }
        }
        else if (_badge is not null && _badgeTier is { } tier)
        {
            if (_collapsed)
            {
                float cx = r.Width / 2f + Sf(8);
                float cy = Sf(12);
                Draw.Circle(g, behind, cx, cy, Sf(4.5f + 1.5f));
                Draw.Circle(g, t.TierFill(tier), cx, cy, Sf(4.5f));
            }
            else
            {
                int width = Badges.PillWidth(_badge, Dpi);
                Badges.Pill(g, r.Right - Sf(10) - width, (r.Height - Sf(20)) / 2f, _badge, tier.ToLevel(), Dpi, centered: true);
            }
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
}

/// <summary>
/// Hosts the current page within the content area and scrolls it vertically when it is taller; the
/// scrollbar sits in the 24 px margin to the right of the content.
/// </summary>
internal sealed class PageViewport : KitControl
{
    private PageBase? _page;
    private int _scroll;
    private int _contentHeight;
    private bool _barHover;
    private bool _dragging;
    private int _dragStart;
    private int _dragScroll;

    public PageViewport()
    {
        Backdrop = static t => t.Bg;
        SetStyle(ControlStyles.ContainerControl, true);
    }

    public int ContentWidth { get; set; }

    public void SetPage(PageBase page)
    {
        if (_page is not null && _page != page)
        {
            Controls.Remove(_page);
        }

        _page = page;
        _scroll = 0;
        if (!Controls.Contains(page))
        {
            Controls.Add(page);
        }

        ArrangePage();
    }

    public void ArrangePage()
    {
        if (_page is null || ContentWidth <= 0)
        {
            return;
        }

        _contentHeight = Math.Max(Height, _page.Arrange(ContentWidth, Height));
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _contentHeight - Height));
        _page.ViewportScroll = _scroll;
        _page.SetBounds(0, -_scroll, ContentWidth, _contentHeight);
        Invalidate();
    }

    public void ScrollBy(int delta)
    {
        int next = Math.Clamp(_scroll + delta, 0, Math.Max(0, _contentHeight - Height));
        if (next != _scroll)
        {
            _scroll = next;
            if (_page is not null)
            {
                _page.ViewportScroll = _scroll;
                _page.Top = -_scroll;
            }

            Invalidate();
        }
    }

    /// <summary>Scrolls so that a rectangle of the page (page coordinates) is visible.</summary>
    public void EnsureVisible(Rectangle pageRect)
    {
        if (pageRect.Top < _scroll)
        {
            ScrollBy(pageRect.Top - _scroll);
        }
        else if (pageRect.Bottom > _scroll + Height)
        {
            ScrollBy(pageRect.Bottom - _scroll - Height);
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ScrollBy(-e.Delta / 120 * S(48));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            int usable = Height - S(8);
            float thumb = Math.Max(S(32), usable * (float)Height / Math.Max(1, _contentHeight));
            float range = usable - thumb;
            if (range > 0)
            {
                ScrollBy(_dragScroll + (int)((e.Y - _dragStart) * (_contentHeight - Height) / range) - _scroll);
            }

            return;
        }

        bool hover = e.X >= ContentWidth;
        if (hover != _barHover)
        {
            _barHover = hover;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _barHover = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.X >= ContentWidth && _contentHeight > Height)
        {
            _dragging = true;
            _dragStart = e.Y;
            _dragScroll = _scroll;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
    }

    protected override void PaintContent(Graphics g) =>
        RowPaint.Scrollbar(g, new RectangleF(0, 0, Width, Height), Height, _contentHeight, _scroll, _barHover || _dragging, Dpi);
}
