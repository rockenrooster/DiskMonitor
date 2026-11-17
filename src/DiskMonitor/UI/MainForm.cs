using System.Text.RegularExpressions;
using DiskMonitor.App;
using DiskMonitor.Core;
using DiskMonitor.Core.Chkdsk;
using DiskMonitor.Core.Events;
using DiskMonitor.Diagnostics;
using DiskMonitor.UI.Controls;
using DiskMonitor.UI.Pages;
using DiskMonitor.UI.Theming;

namespace DiskMonitor.UI;

/// <summary>What the shell needs from its host: the tray app, or the render harness.</summary>
internal interface IAppHost
{
    void ShowMainWindow(Action<MainForm>? then = null);

    void ExitApplication();
}

/// <summary>
/// The main window: navigation rail, header with the page's toolbar, a scrolling content area and the
/// status strip (subplans 02 and 03). Created when asked for and disposed when closed; it never polls.
/// </summary>
internal sealed partial class MainForm : Form, INavigator
{
    public static readonly string[] PageOrder = ["Overview", "Events", "Drives", "CheckDisk"];

    private readonly AppServices _services;
    private readonly IAppHost _host;
    private readonly ShellRoot _root;
    private readonly Dictionary<string, PageBase> _pages = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 100 };
    private readonly List<ToastPopup> _toasts = [];
    private int _pendingReasons;
    private PageBase? _current;
    private FormWindowState _restoreState = FormWindowState.Normal;
    private Action? _undo;
    private bool _boundsRestored;

    public MainForm(AppServices services, IAppHost host, string initialPage)
    {
        _services = services;
        _host = host;
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;
        StartPosition = FormStartPosition.Manual;
        Text = Strings.WindowTitle(services.Updates.CurrentVersion.ToString());
        Icon = LoadIcon();
        BackColor = ThemeService.Tokens.Bg;
        _root = new ShellRoot(this, services) { Dock = DockStyle.Fill };
        Controls.Add(_root);
        _refreshTimer.Tick += OnRefreshTimer;

        services.Events.Changed += OnEventsChanged;
        services.Drives.Updated += OnDrivesUpdated;
        services.Monitor.StatusChanged += OnMonitorChanged;
        services.Chkdsk.Changed += OnChkdskChanged;
        services.Chkdsk.Completed += OnChkdskCompleted;
        services.Updates.StatusChanged += OnUpdatesChanged;
        services.Notifier.SnoozeChanged += OnSnoozeChanged;
        services.SettingsChanged += OnSettingsChanged;
        services.WatchedScanFinished += OnWatchedScanFinished;
        ThemeService.ThemeChanged += OnThemeChanged;

        ShowPage(initialPage);
    }

    /// <summary>Page factory; pages are created on first visit.</summary>
    public static Func<AppServices, INavigator, string, PageBase>? PageFactory { get; set; }

    public string CurrentPageKey => _current?.Key ?? "Overview";

    public FormWindowState RestoreState => _restoreState;

    /// <summary>The control that fills the client area; the render harness captures it.</summary>
    public KitControl Root => _root;

    public PageBase? CurrentPage => _current;

    internal int Dpi => ThemeService.DpiOverride ?? DeviceDpi;

    internal int S(int value) => ThemeService.S(Dpi, value);

    // --- navigation ---------------------------------------------------------------------------------

    public void ShowPage(string key)
    {
        string resolved = PageOrder.Concat(["Settings"]).FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? "Overview";
        if (_current?.Key == resolved)
        {
            return;
        }

        _current?.Deactivate();
        _root.HideDrawer(animate: false);
        if (!_pages.TryGetValue(resolved, out PageBase? page))
        {
            page = PageFactory?.Invoke(_services, this, resolved) ?? new PlaceholderPage(_services, this, resolved, PageTitle(resolved));
            page.LayoutRequested += (_, _) => _root.LayoutShell();
            _pages[resolved] = page;
        }

        _current = page;
        _root.SetPage(page);
        page.Activate();
        page.RefreshData(RefreshReason.All);
        _root.UpdateRail();
    }

    public void ShowEvent(string eventKey) => Go("Events", new EventKey(eventKey));

    public void ShowEvents(EventFilter filter) => Go("Events", filter);

    public void ShowDrive(int diskNumber) => Go("Drives", new DiskTarget(diskNumber));

    public void ShowCheckDisk(string? volume, ScanOfferReason? reason) => Go("CheckDisk", new ScanOffer(volume, reason));

    public void ShowSettings(string? section) => Go("Settings", new SettingsSection(section));

    public void ShowSettingsAndCheck() => Go("Settings", new SettingsSection("updates", StartUpdateCheck: true));

    public void ShowToast(InAppToast toast)
    {
        if (!IsHandleCreated)
        {
            return;
        }

        ToastPopup popup = new(toast.Level, toast.Title, toast.Text, toast.ActionLabel, toast.Action, Dpi) { Owner = this };
        popup.Finished += (_, _) =>
        {
            _toasts.Remove(popup);
            PlaceToasts();
        };
        _toasts.Insert(0, popup);
        while (_toasts.Count > 3)
        {
            ToastPopup oldest = _toasts[^1];
            _toasts.RemoveAt(_toasts.Count - 1);
            oldest.Close();
        }

        PlaceToasts();
        popup.Show(this);
        popup.Start();
    }

    public void SetUndo(Action? undo) => _undo = undo;

    public void ShowDrawer(DrawerPanel drawer) => _root.ShowDrawer(drawer);

    public void HideDrawer() => _root.HideDrawer();

    public Form Window => this;

    public void ExitForUpdate() => _host.ExitApplication();

    private void Go(string page, object argument)
    {
        ShowPage(page);
        _current?.Navigate(argument);
    }

    private void PlaceToasts()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        Rectangle client = RectangleToScreen(ClientRectangle);
        int bottom = client.Bottom - S(16) - (_root.StripVisible ? S(36) : 0);
        foreach (ToastPopup toast in _toasts)
        {
            Point location = new(client.Right - S(16) - S(360), bottom - S(64));
            toast.SurfaceLocation = location;
            if (toast.Visible)
            {
                toast.Render();
            }

            bottom -= S(64) + S(8);
        }
    }

    public static string PageTitle(string key) => key switch
    {
        "Events" => Strings.PageEvents,
        "Drives" => Strings.PageDrives,
        "CheckDisk" => Strings.PageCheckDisk,
        "Settings" => Strings.PageSettings,
        _ => Strings.PageOverview
    };

    // --- window --------------------------------------------------------------------------------------

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ThemeService.ApplyWindowChrome(Handle);
        MinimumSize = SizeFromClientSize(new Size(S(960), S(600)));
        if (!_boundsRestored)
        {
            _boundsRestored = true;
            RestoreWindowBounds();
        }

        // A change that arrived before the window had a handle could not start the refresh; without this
        // the pending reasons stayed set and no later change refreshed the window until it was reopened.
        if (Interlocked.CompareExchange(ref _pendingReasons, 0, 0) != 0)
        {
            _refreshTimer.Start();
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState is FormWindowState.Normal or FormWindowState.Maximized)
        {
            _restoreState = WindowState;
        }

        PlaceToasts();
    }

    protected override void OnMove(EventArgs e)
    {
        base.OnMove(e);
        PlaceToasts();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        MinimumSize = SizeFromClientSize(new Size(S(960), S(600)));
        _root.LayoutShell();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        SaveWindowBounds();
        _current?.Deactivate();
        foreach (ToastPopup toast in _toasts.ToArray())
        {
            toast.Close();
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.D1:
                ShowPage("Overview");
                return true;
            case Keys.Control | Keys.D2:
                ShowPage("Events");
                return true;
            case Keys.Control | Keys.D3:
                ShowPage("Drives");
                return true;
            case Keys.Control | Keys.D4:
                ShowPage("CheckDisk");
                return true;
            case Keys.Control | Keys.Oemcomma:
                ShowPage("Settings");
                return true;
            case Keys.F5:
                _ = _services.Monitor.ReloadAsync();
                _ = _services.Drives.RefreshAsync();
                return true;
            case Keys.Control | Keys.F:
                return _current?.FocusSearch() ?? false;
            case Keys.OemQuestion when ActiveControl is not TextBox && FocusedTextBox() is null:
                return _current?.FocusSearch() ?? false;
            case Keys.Control | Keys.Z when FocusedTextBox() is null:
                Action? undo = _undo;
                _undo = null;
                undo?.Invoke();
                return undo is not null;
            case Keys.Escape:
                return _current?.HandleEscape() ?? false;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private TextBox? FocusedTextBox()
    {
        Control? control = ActiveControl;
        while (control is ContainerControl { ActiveControl: { } inner })
        {
            control = inner;
        }

        return control as TextBox;
    }

    private void RestoreWindowBounds()
    {
        // Saved bounds are device pixels of the whole window; the first run uses the default client size.
        AppSettings settings = _services.Settings;
        Size size = new(Math.Max(S(960), settings.WindowWidth), Math.Max(S(600), settings.WindowHeight));
        if (settings.WindowLeft != AppSettings.UnsetPosition && settings.WindowTop != AppSettings.UnsetPosition)
        {
            Rectangle bounds = new(settings.WindowLeft, settings.WindowTop, size.Width, size.Height);
            if (IsTitleBarOnScreen(bounds))
            {
                Bounds = bounds;
                if (settings.WindowMaximized)
                {
                    WindowState = FormWindowState.Maximized;
                }

                return;
            }
        }

        Size defaultClient = new(S(1280), S(800));
        Size outer = SizeFromClientSize(defaultClient);
        Rectangle area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1040);
        outer = new Size(Math.Min(outer.Width, area.Width), Math.Min(outer.Height, area.Height));
        Bounds = new Rectangle(area.Left + (area.Width - outer.Width) / 2, area.Top + (area.Height - outer.Height) / 2, outer.Width, outer.Height);
    }

    private void SaveWindowBounds()
    {
        AppSettings settings = _services.Settings;
        Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        settings.WindowLeft = bounds.Left;
        settings.WindowTop = bounds.Top;
        settings.WindowWidth = bounds.Width;
        settings.WindowHeight = bounds.Height;
        settings.WindowMaximized = WindowState == FormWindowState.Maximized || (WindowState == FormWindowState.Minimized && _restoreState == FormWindowState.Maximized);
        settings.LastPage = CurrentPageKey;
        if (!_services.DemoMode)
        {
            _ = settings.TrySave();
        }
    }

    // The window is restored only if a usable part of its title bar is on some screen.
    private static bool IsTitleBarOnScreen(Rectangle bounds)
    {
        Rectangle titleBar = new(bounds.Left + 40, bounds.Top, Math.Max(1, bounds.Width - 160), 32);
        return Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(titleBar));
    }

    private static Icon? LoadIcon()
    {
        try
        {
            using Stream? stream = typeof(MainForm).Assembly.GetManifestResourceStream("DiskMonitor.ico");
            return stream is null ? null : new Icon(stream);
        }
        catch (ArgumentException ex)
        {
            ErrorLog.Write("Window icon", ex);
            return null;
        }
    }

    // --- refresh plumbing (no polling) ---------------------------------------------------------------

    private void OnEventsChanged() => Queue(RefreshReason.Events);

    private void OnDrivesUpdated() => Queue(RefreshReason.Drives);

    private void OnMonitorChanged() => Queue(RefreshReason.Monitor);

    private void OnChkdskChanged() => Queue(RefreshReason.Chkdsk);

    private void OnChkdskCompleted(ChkdskResult result) => Queue(RefreshReason.Chkdsk);

    private void OnUpdatesChanged(object? sender, EventArgs e) => Queue(RefreshReason.Updates);

    private void OnSnoozeChanged() => Queue(RefreshReason.Snooze);

    private void OnSettingsChanged() => Queue(RefreshReason.Settings);

    private void Queue(RefreshReason reason)
    {
        int previous = Interlocked.Or(ref _pendingReasons, (int)reason);
        if (previous == 0 && IsHandleCreated && !IsDisposed)
        {
            try
            {
                BeginInvoke(() => _refreshTimer.Start());
            }
            catch (InvalidOperationException)
            {
                // The window is closing.
            }
        }
    }

    private void OnRefreshTimer(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        RefreshReason reasons = (RefreshReason)Interlocked.Exchange(ref _pendingReasons, 0);
        if (reasons == RefreshReason.None)
        {
            return;
        }

        _root.UpdateRail();
        _root.UpdateStrip();
        _current?.RefreshData(reasons);
    }

    private void OnWatchedScanFinished(ChkdskResult result)
    {
        if (!IsDisposed && ContainsFocus)
        {
            (Level level, string text) = CheckDiskResultText(result);
            ShowToast(new InAppToast(level, Strings.ScanFinishedToast(result.Volume), text));
        }
    }

    /// <summary>The level and text of a finished run, as the Check Disk result bars word it.</summary>
    public static (Level Level, string Text) CheckDiskResultText(ChkdskResult result) => result.Outcome switch
    {
        ChkdskOutcome.Clean => (Level.Ok, Strings.ResultClean(result.Volume)),
        ChkdskOutcome.Problems => (Level.High, Strings.ResultProblems(result.Volume)),
        ChkdskOutcome.Fixed => (Level.Ok, Strings.ResultFixed(result.Volume)),
        ChkdskOutcome.Scheduled => (Level.Low, Strings.ResultScheduled(result.Volume)),
        ChkdskOutcome.Cancelled => (Level.Medium, Strings.ResultCancelled(result.Volume)),
        ChkdskOutcome.Declined => (Level.Medium, Strings.ErrorDeclined),
        _ => (Level.Critical, Strings.ResultFailed(result.Volume, result.Error ?? result.Summary))
    };

    private void OnThemeChanged()
    {
        if (IsDisposed)
        {
            return;
        }

        ThemeService.ApplyWindowChrome(Handle);
        BackColor = ThemeService.Tokens.Bg;
        ApplyThemeRecursive(this);
        Invalidate(true);
    }

    private static void ApplyThemeRecursive(Control control)
    {
        foreach (Control child in control.Controls)
        {
            if (child is IThemeAware aware)
            {
                aware.ApplyTheme();
            }

            ApplyThemeRecursive(child);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _services.Events.Changed -= OnEventsChanged;
            _services.Drives.Updated -= OnDrivesUpdated;
            _services.Monitor.StatusChanged -= OnMonitorChanged;
            _services.Chkdsk.Changed -= OnChkdskChanged;
            _services.Chkdsk.Completed -= OnChkdskCompleted;
            _services.Updates.StatusChanged -= OnUpdatesChanged;
            _services.Notifier.SnoozeChanged -= OnSnoozeChanged;
            _services.SettingsChanged -= OnSettingsChanged;
            _services.WatchedScanFinished -= OnWatchedScanFinished;
            ThemeService.ThemeChanged -= OnThemeChanged;
            _refreshTimer.Dispose();
            foreach (PageBase page in _pages.Values)
            {
                page.Dispose();
            }

            _pages.Clear();
        }

        base.Dispose(disposing);
    }

    [GeneratedRegex(@"ETA:\s*(\d+):(\d\d):(\d\d)", RegexOptions.CultureInvariant)]
    internal static partial Regex EtaPattern();

    /// <summary>The time left from CHKDSK's progress line (<c>ETA: h:mm:ss</c>), or null when it has none.</summary>
    internal static TimeSpan? TimeLeft(string progressText)
    {
        Match eta = EtaPattern().Match(progressText);
        if (!eta.Success)
        {
            return null;
        }

        System.Globalization.CultureInfo invariant = System.Globalization.CultureInfo.InvariantCulture;
        return new TimeSpan(int.Parse(eta.Groups[1].Value, invariant), int.Parse(eta.Groups[2].Value, invariant), int.Parse(eta.Groups[3].Value, invariant));
    }
}
