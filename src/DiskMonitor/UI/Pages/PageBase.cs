using DiskMonitor.App;
using DiskMonitor.Core.Events;
using DiskMonitor.UI.Controls;
using DiskMonitor.UI.Theming;

namespace DiskMonitor.UI.Pages;

/// <summary>What changed since a page last refreshed. Pages rebuild only what the bits name.</summary>
[Flags]
internal enum RefreshReason
{
    None = 0,
    Events = 1,
    Drives = 2,
    Monitor = 4,
    Chkdsk = 8,
    Updates = 16,
    Settings = 32,
    Snooze = 64,
    All = 127
}

/// <summary>Filters the Events page can be opened with.</summary>
internal sealed record EventFilter(IReadOnlySet<Tier>? Tiers = null, int? DiskNumber = null, bool UnreadOnly = false);

/// <summary>An in-app toast: a result of something the user did while watching.</summary>
internal sealed record InAppToast(Level? Level, string Title, string? Text = null, string? ActionLabel = null, Action? Action = null);

/// <summary>Why Check Disk was opened with a volume: the time Windows reported damage on it, when known.</summary>
internal sealed record ScanOfferReason(DateTime? ReportedUtc);

/// <summary>Navigation arguments handed to <see cref="PageBase.Navigate"/>.</summary>
internal sealed record EventKey(string Key);

internal sealed record DiskTarget(int DiskNumber);

internal sealed record ScanOffer(string? Volume, ScanOfferReason? Reason);

internal sealed record SettingsSection(string? Section, bool StartUpdateCheck = false);

/// <summary>Navigation the pages ask the shell for. Every entry point only selects a view.</summary>
internal interface INavigator
{
    void ShowPage(string key);

    void ShowEvent(string eventKey);

    void ShowEvents(EventFilter filter);

    void ShowDrive(int diskNumber);

    void ShowCheckDisk(string? volume, ScanOfferReason? reason);

    void ShowSettings(string? section);

    void ShowToast(InAppToast toast);

    void ExitForUpdate();

    /// <summary>Registers the undo of the last action whose toast offered it (Ctrl+Z).</summary>
    void SetUndo(Action? undo);

    /// <summary>Shows a detail drawer at the window's right edge, below the header (it slides in).</summary>
    void ShowDrawer(DrawerPanel drawer);

    /// <summary>Closes the drawer (it slides out).</summary>
    void HideDrawer();

    /// <summary>The window that owns dialogs and menus.</summary>
    Form Window { get; }
}

/// <summary>
/// A detail drawer: Surface, a 1 px Stroke line on its left and top edges, resizable by dragging its left
/// edge between 360 and 640 px (the width is remembered by its owner).
/// </summary>
internal abstract class DrawerPanel : KitControl
{
    private bool _resizing;
    private int _resizeStartX;
    private int _resizeStartWidth;

    protected DrawerPanel()
    {
        Backdrop = static t => t.Surface;
        SetStyle(ControlStyles.ContainerControl, true);
    }

    /// <summary>Logical width (360 to 640).</summary>
    public int LogicalWidth { get; set; } = 440;

    /// <summary>Raised when a resize drag ends, with the new logical width.</summary>
    public event EventHandler<int>? WidthCommitted;

    /// <summary>Raised during a resize drag, so the shell lays out again.</summary>
    public event EventHandler? WidthChanging;

    protected override void PaintContent(Graphics g)
    {
        float line = Sf(0.5f);
        Draw.Line(g, T.Stroke, Sc, line, 0, line, Height);
        Draw.Line(g, T.Stroke, Sc, 0, line, Width, line);
        PaintDrawer(g);
    }

    protected abstract void PaintDrawer(Graphics g);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_resizing)
        {
            int delta = (_resizeStartX - Cursor.Position.X) * 96 / Math.Max(96, Dpi);
            int next = Math.Clamp(_resizeStartWidth + delta, AppSettings.MinDrawerWidth, AppSettings.MaxDrawerWidth);
            if (next != LogicalWidth)
            {
                LogicalWidth = next;
                WidthChanging?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        Cursor = e.X < S(5) ? Cursors.SizeWE : Cursors.Default;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && e.X < S(5))
        {
            _resizing = true;
            _resizeStartX = Cursor.Position.X;
            _resizeStartWidth = LogicalWidth;
            Capture = true;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_resizing)
        {
            _resizing = false;
            Capture = false;
            WidthCommitted?.Invoke(this, LogicalWidth);
        }
    }
}

/// <summary>
/// A page: created on first visit, kept until the window closes. The shell gives it the content area and
/// places its toolbar in the header. Painting never reads services; <see cref="RefreshData"/> does.
/// </summary>
internal abstract class PageBase : KitControl
{
    protected PageBase(AppServices services, INavigator navigator)
    {
        Services = services;
        Navigator = navigator;
        Backdrop = static t => t.Bg;
        SetStyle(ControlStyles.ContainerControl, true);
    }

    protected AppServices Services { get; }

    protected INavigator Navigator { get; }

    public abstract string Key { get; }

    public abstract string Title { get; }

    /// <summary>Controls the page creates for the shell header, right to left in reading order.</summary>
    public virtual IReadOnlyList<Control> Toolbar => [];

    /// <summary>Raised when the page needs the shell to lay it out again (its height changed).</summary>
    public event EventHandler? LayoutRequested;

    /// <summary>Lays children out within the content area; returns the content height.</summary>
    public abstract int Arrange(int width, int viewportHeight);

    public virtual void Activate()
    {
    }

    /// <summary>The page is hidden: stop every timer.</summary>
    public virtual void Deactivate()
    {
    }

    public virtual void RefreshData(RefreshReason reasons)
    {
    }

    /// <summary>Focuses the page's search or filter box (Ctrl+F or /); false when it has none.</summary>
    public virtual bool FocusSearch() => false;

    /// <summary>Handles Esc at page level; false lets the shell do nothing further.</summary>
    public virtual bool HandleEscape() => false;

    /// <summary>
    /// Applies a navigation argument after the page is shown: an <see cref="EventKey"/>, an
    /// <see cref="EventFilter"/>, a <see cref="DiskTarget"/>, a <see cref="ScanOffer"/> or a <see cref="SettingsSection"/>.
    /// </summary>
    public virtual void Navigate(object argument)
    {
    }

    /// <summary>The scroll offset the viewport shows; pages that keep an element in view use it.</summary>
    public int ViewportScroll { get; internal set; }

    protected void RequestLayout() => LayoutRequested?.Invoke(this, EventArgs.Empty);

    private bool _forwardingLayout;

    /// <summary>
    /// A kit control whose size follows its content (a chip's count, a drop-down's label, a button's text)
    /// asks its parent to lay out again with <c>Parent.PerformLayout()</c>, which names no affected control.
    /// A page is arranged by the shell, which knows the viewport, so that request goes to the shell; without
    /// it the control kept its old width until the page was shown again, and its text overflowed or was cut.
    /// Layouts that arranging causes (a child's bounds or visibility) name the child and are not forwarded.
    /// </summary>
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (e.AffectedControl is null && !_forwardingLayout)
        {
            _forwardingLayout = true;
            try
            {
                RequestLayout();
            }
            finally
            {
                _forwardingLayout = false;
            }
        }
    }
}

/// <summary>A page that is not built yet: shows its title and a line saying so.</summary>
internal sealed class PlaceholderPage(AppServices services, INavigator navigator, string key, string title) : PageBase(services, navigator)
{
    public override string Key { get; } = key;

    public override string Title { get; } = title;

    public override int Arrange(int width, int viewportHeight) => viewportHeight;

    protected override void PaintContent(Graphics g) =>
        Draw.Text(g, Strings.PlaceholderPage, F(FontRole.Body), 0, Sf(16), T.Text2);
}
