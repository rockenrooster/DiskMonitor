using System.Runtime.InteropServices;
using DiskMonitor.Core;
using DiskMonitor.Core.Chkdsk;
using DiskMonitor.Core.Drives;
using DiskMonitor.Core.Events;
using DiskMonitor.Core.Notifications;
using DiskMonitor.Core.Updates;
using DiskMonitor.Diagnostics;
using DiskMonitor.UI;
using DiskMonitor.UI.Controls;
using DiskMonitor.UI.Pages;
using DiskMonitor.UI.Theming;
using Microsoft.Win32;

namespace DiskMonitor.App;

/// <summary>
/// The process is a tray application that sometimes has a window (subplan 03). The window is created
/// when asked for and disposed when closed, so the idle process holds only the monitoring services.
/// </summary>
internal sealed class TrayApp : ApplicationContext, IAppHost
{
    private static readonly TimeSpan FirstUpdateCheck = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromHours(24);

    private readonly AppServices _services;
    private readonly MessageSink _sink;
    private readonly NotifyIcon _tray;
    private readonly bool _dev;
    private readonly HashSet<string> _loggedVerbs = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Threading.Timer _updateTimer;
    private readonly IDisposable _ipc;
    private MainForm? _main;
    private string _lastPage;
    private volatile bool _windowActive;
    private bool _exiting;
    private int _trayDirty;
    private Icon? _trayIcon;
    private TrayBadge? _shownBadge;
    private bool _balloonOpensEvents;

    public TrayApp(string initialCommand, bool dev)
    {
        _dev = dev;
        _services = new AppServices(Post, demoMode: false);
        ThemeService.Apply(_services.Settings.Theme);
        _sink = new MessageSink(this);
        _ = _sink.Handle;
        ShellRegistration.Ensure(path => IconArt.SavePng(path, 256));
        StartupManager.Refresh(_services.Settings.StartMinimized);
        _lastPage = _services.Settings.LastPage;

        _tray = new NotifyIcon { Visible = false };
        _tray.MouseUp += OnTrayMouseUp;
        _tray.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) { ShowMainWindow(); } };
        _tray.BalloonTipClicked += (_, _) =>
        {
            if (_balloonOpensEvents)
            {
                ShowMainWindow(form => form.ShowPage("Events"));
            }
        };
        UpdateTray();
        _tray.Visible = true;
        _services.Notifier.BalloonFallback = ShowBalloon;
        _services.IsWindowActive = () => _windowActive;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        _services.Events.Changed += MarkTrayDirty;
        _services.Drives.Updated += MarkTrayDirty;
        _services.Monitor.StatusChanged += MarkTrayDirty;
        _services.Notifier.SnoozeChanged += MarkTrayDirty;
        _services.Updates.StatusChanged += OnUpdatesChanged;

        _ipc = Ipc.Listen(message => Post(() => HandleCommand(message)));
        _services.Start();
        _updateTimer = new System.Threading.Timer(_ => OnUpdateTimer(), null, FirstUpdateCheck, UpdateInterval);
        HandleCommand(initialCommand);
    }

    /// <summary>Queues work on the UI thread; silently drops it once shutdown has begun.</summary>
    public void Post(Action action)
    {
        try
        {
            if (!_sink.IsDisposed && _sink.IsHandleCreated)
            {
                _sink.BeginInvoke(action);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // Shutting down.
        }
    }

    public void ShowMainWindow(Action<MainForm>? then = null)
    {
        if (_exiting)
        {
            return;
        }

        if (_main is null || _main.IsDisposed)
        {
            _main = new MainForm(_services, this, _lastPage);
            _main.Activated += (_, _) => _windowActive = true;
            _main.Deactivate += (_, _) => _windowActive = false;
            _main.FormClosed += OnMainClosed;
        }

        if (!_main.Visible)
        {
            _main.Show();
        }

        if (_main.WindowState == FormWindowState.Minimized)
        {
            _main.WindowState = _main.RestoreState;
        }

        _main.Activate();
        then?.Invoke(_main);
    }

    public void ExitApplication()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        try
        {
            _main?.Close();
            _tray.Visible = false;
            _tray.Dispose();
            _trayIcon?.Dispose();
            _ipc.Dispose();
            _updateTimer.Dispose();
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _services.Dispose();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Exit", ex);
        }
        finally
        {
            _sink.Dispose();
            ExitThread();
        }
    }

    /// <summary>Handles a command from the first launch, a second launch or a toast. Never throws.</summary>
    public void HandleCommand(string raw)
    {
        try
        {
            string command = (raw ?? "").Trim();
            if (command.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                ExitApplication();
                return;
            }

            if (command.Equals("tray", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (command.StartsWith("simulate ", StringComparison.OrdinalIgnoreCase))
            {
                if (_dev)
                {
                    _services.InjectTestEvent(command["simulate ".Length..].Trim());
                }

                return;
            }

            if (!command.StartsWith(AppPaths.ProtocolScheme + ":", StringComparison.OrdinalIgnoreCase))
            {
                if (command.Length > 0 && !command.Equals("show", StringComparison.OrdinalIgnoreCase))
                {
                    LogUnknown(command);
                }

                ShowMainWindow();
                return;
            }

            LinkTarget target = AppLinks.Resolve(command);
            switch (target.Kind)
            {
                case LinkKind.Page:
                    ShowMainWindow(form => form.ShowPage(target.Page!));
                    break;
                case LinkKind.Event:
                    ShowMainWindow(form => form.ShowEvent(target.EventKey!));
                    break;
                case LinkKind.ScanOffer:
                    string volume = target.Volume!;
                    if (_services.Drives.Current.FindVolume(volume) is not null)
                    {
                        ShowMainWindow(form => form.ShowCheckDisk(volume, new ScanOfferReason(LatestScanReason(volume))));
                    }
                    else
                    {
                        ShowMainWindow(form => form.ShowPage("CheckDisk"));
                    }

                    break;
                default:
                    if (AppLinks.TryParse(command, out AppCommand parsed) && parsed.Verb != AppCommand.Show)
                    {
                        LogUnknown(parsed.Verb);
                    }

                    ShowMainWindow();
                    break;
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Command", ex);
            try
            {
                ShowMainWindow();
            }
            catch (Exception inner)
            {
                ErrorLog.Write("Command fallback", inner);
            }
        }
    }

    private DateTime? LatestScanReason(string volume)
    {
        foreach (DiskEvent item in _services.Events.Snapshot())
        {
            if (item.SuggestScan && string.Equals(item.Volume, volume, StringComparison.OrdinalIgnoreCase))
            {
                return item.TimeUtc;
            }
        }

        return null;
    }

    private void LogUnknown(string verb)
    {
        string key = verb.Length > 40 ? verb[..40] : verb;
        if (_loggedVerbs.Count < 50 && _loggedVerbs.Add(key))
        {
            ErrorLog.Write("Command", "Unknown command: " + key);
        }
    }

    private void OnMainClosed(object? sender, FormClosedEventArgs e)
    {
        if (_main is not null)
        {
            _lastPage = _main.CurrentPageKey;
        }

        _main = null;
        _windowActive = false;
        if (_exiting)
        {
            return;
        }

        if (!_services.Settings.CloseToTray)
        {
            ExitApplication();
            return;
        }

        if (!_services.State.Read(s => s.TrayHintShown))
        {
            _balloonOpensEvents = false;
            _tray.ShowBalloonTip(8000, Strings.TrayHintTitle, Strings.TrayHintText, ToolTipIcon.Info);
            _services.State.Update(s => s.TrayHintShown = true);
        }

        // Give the memory of the closed window back.
        GC.Collect();
    }

    // --- tray icon ------------------------------------------------------------------------------------

    private void MarkTrayDirty()
    {
        if (Interlocked.Exchange(ref _trayDirty, 1) == 0)
        {
            Post(() =>
            {
                System.Windows.Forms.Timer once = new() { Interval = 250 };
                once.Tick += (_, _) =>
                {
                    once.Dispose();
                    Interlocked.Exchange(ref _trayDirty, 0);
                    UpdateTray();
                };
                once.Start();
            });
        }
    }

    private void OnUpdatesChanged(object? sender, EventArgs e) => MarkTrayDirty();

    private void UpdateTray()
    {
        if (_exiting)
        {
            return;
        }

        (TrayBadge badge, string tooltip) = TrayState();
        string text = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        if (_tray.Text != text)
        {
            _tray.Text = text;
        }

        if (_shownBadge != badge || _trayIcon is null)
        {
            Icon next = IconArt.CreateTrayIcon(SystemInformation.SmallIconSize.Width, badge);
            Icon? previous = _trayIcon;
            _tray.Icon = next;
            _trayIcon = next;
            _shownBadge = badge;
            previous?.Dispose();
        }
    }

    /// <summary>The first matching row of subplan 03's tray table.</summary>
    private (TrayBadge Badge, string Tooltip) TrayState()
    {
        if (_services.Notifier.IsSnoozed)
        {
            return (TrayBadge.Snoozed, Strings.TraySnoozed(_services.Notifier.SnoozeUntilUtc));
        }

        UnreadSummary unread = _services.Events.GetUnread();
        if (unread.Highest is { } highest)
        {
            TrayBadge badge = highest switch
            {
                Tier.Critical => TrayBadge.Critical,
                Tier.High => TrayBadge.High,
                Tier.Medium => TrayBadge.Medium,
                _ => TrayBadge.Low
            };
            return (badge, Strings.TrayUnread(unread.Total, unread.Critical));
        }

        HealthStatus worst = _services.Drives.Current.Disks.Count == 0 ? HealthStatus.Unknown : _services.Drives.Current.Disks.Max(d => d.Health);
        if (worst == HealthStatus.Bad)
        {
            return (TrayBadge.Critical, Strings.TrayFailing);
        }

        if (worst == HealthStatus.Caution)
        {
            return (TrayBadge.Medium, Strings.TrayAttention);
        }

        if (_services.Monitor.LastError is not null)
        {
            return (TrayBadge.High, Strings.TrayLimited);
        }

        return (TrayBadge.Ok, Strings.TrayAllClear);
    }

    private void OnTrayMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            ShowMainWindow();
        }
        else if (e.Button == MouseButtons.Right)
        {
            ShowTrayMenu();
        }
    }

    private void ShowTrayMenu()
    {
        UnreadSummary unread = _services.Events.GetUnread();
        bool snoozed = _services.Notifier.IsSnoozed;
        UpdateCheckResult? update = _services.Updates.LastResult;
        DateTime tomorrow = Clock.UtcNow.ToLocalTime().Date.AddDays(1).AddHours(8);
        List<MenuEntry> entries =
        [
            new(Strings.MenuOpen, IconName.Dashboard, null, () => ShowMainWindow()) { Strong = true },
            MenuEntry.Separator,
            new(unread.Total > 0 ? Strings.MenuUnread(unread.Total, unread.Critical) : Strings.MenuNoUnread) { Enabled = false },
            new(Strings.MenuMarkAllRead, IconName.Check, null, () => _services.Events.MarkAllRead()) { Enabled = unread.Total > 0 },
            MenuEntry.Separator,
            new(Strings.MenuSnooze, IconName.BellOff)
            {
                Submenu =
                [
                    new(Strings.SnoozeHour, null, null, () => _services.Notifier.Snooze(TimeSpan.FromHours(1))),
                    new(Strings.SnoozeFourHours, null, null, () => _services.Notifier.Snooze(TimeSpan.FromHours(4))),
                    new(Strings.SnoozeTomorrow, null, null, () => _services.Notifier.SnoozeUntil(tomorrow.ToUniversalTime())),
                    MenuEntry.Separator,
                    new(Strings.MenuResume, null, null, () => _services.Notifier.Snooze(null)) { Enabled = snoozed }
                ]
            },
            new(Strings.MenuCheckDisk, IconName.Scans, null, () => ShowMainWindow(form => form.ShowPage("CheckDisk"))),
            MenuEntry.Separator,
            update?.CanDownload == true
                ? new(Strings.MenuInstall(update.LatestVersionText.TrimStart('v', 'V')), IconName.Download, null, () => ShowMainWindow(form => form.ShowSettings("updates")))
                : new(Strings.MenuCheckUpdates, IconName.Update, null, () => ShowMainWindow(form => form.ShowSettingsAndCheck())),
            MenuEntry.Separator,
            new(Strings.MenuExit, IconName.Close, null, ExitApplication)
        ];

        Point cursor = Cursor.Position;
        int dpi = DpiAt(cursor);
        KitMenu menu = KitMenu.ShowAt(entries, cursor, dpi, null, above: true);
        _ = SetForegroundWindow(menu.Handle);
    }

    private bool ShowBalloon(string title, string body, Tier? tier)
    {
        if (_exiting)
        {
            return false;
        }

        _balloonOpensEvents = true;
        ToolTipIcon icon = tier is Tier.Critical or Tier.High ? ToolTipIcon.Error : tier == Tier.Medium ? ToolTipIcon.Warning : ToolTipIcon.Info;
        _tray.ShowBalloonTip(8000, title, body.Length == 0 ? " " : body, icon);
        return true;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Post(() =>
    {
        _shownBadge = null;
        UpdateTray();
    });

    // --- updates --------------------------------------------------------------------------------------

    private void OnUpdateTimer()
    {
        if (_exiting || !_services.Settings.AutoCheckUpdates)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                UpdateCheckResult result = await _services.Updates.CheckAsync().ConfigureAwait(false);
                string version = result.LatestVersionText;
                if (result.CanDownload && _services.State.Read(s => s.UpdateNoticeVersion) != version)
                {
                    _services.State.Update(s => s.UpdateNoticeVersion = version);
                    _services.Notifier.NotifyUpdateAvailable(version.TrimStart('v', 'V'));
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Update check", ex);
            }
        });
    }

    private static int DpiAt(Point point)
    {
        try
        {
            IntPtr monitor = MonitorFromPoint(new NativePoint(point.X, point.Y), 2);
            return GetDpiForMonitor(monitor, 0, out uint x, out _) == 0 ? (int)x : 96;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return 96;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint(int x, int y)
    {
        public readonly int X = x;
        public readonly int Y = y;
    }

    [DllImport("user32.dll", EntryPoint = "MonitorFromPoint")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("shcore.dll", EntryPoint = "GetDpiForMonitor")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    /// <summary>
    /// A window that is never shown: it gives the process a UI-thread handle for BeginInvoke and a
    /// top-level window for session messages, so sign-out and the installer can close the app.
    /// </summary>
    private sealed class MessageSink : Form
    {
        private const int WmQueryEndSession = 0x0011;
        private const int WmEndSession = 0x0016;
        private readonly TrayApp _owner;

        public MessageSink(TrayApp owner)
        {
            _owner = owner;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
            Size = new Size(1, 1);
        }

        protected override void SetVisibleCore(bool value) => base.SetVisibleCore(false);

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmQueryEndSession)
            {
                m.Result = 1;
                return;
            }

            if (m.Msg == WmEndSession && m.WParam != IntPtr.Zero)
            {
                _owner.ExitApplication();
                return;
            }

            base.WndProc(ref m);
        }
    }
}
