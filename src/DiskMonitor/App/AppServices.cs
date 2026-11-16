using DiskMonitor.Core.Chkdsk;
using DiskMonitor.Core.Drives;
using DiskMonitor.Core.Events;
using DiskMonitor.Core.Notifications;
using DiskMonitor.Core.Updates;
using DiskMonitor.Diagnostics;
using Microsoft.Win32;

namespace DiskMonitor.App;

/// <summary>Owns every long-lived service and wires them together. One instance per process.</summary>
internal sealed class AppServices : IDisposable
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(10);
    private readonly System.Threading.Timer _heartbeat;
    private bool _started;

    public AppServices(Action<Action> postToUi, bool demoMode)
    {
        DemoMode = demoMode;
        Settings = AppSettings.Load();
        State = StateStore.Load();
        Events = new EventStore(State);
        Drives = new DriveMonitor(Settings, State, Events);
        Notifier = new NotificationService(Settings, State, postToUi) { Enabled = !demoMode };
        Monitor = new EventLogMonitor(Settings, Events, State, () => Drives.Current);
        Chkdsk = new ChkdskService(Settings, State, Notifier);
        Proactive = new ProactiveService(Settings, State, Chkdsk, Notifier, () => Drives.Current);
        Updates = new UpdateService();
        _heartbeat = new System.Threading.Timer(_ => Monitor.MarkSeen(DateTime.UtcNow), null, Timeout.Infinite, Timeout.Infinite);

        Monitor.EventArrived += OnNewEvent;
        Monitor.MissedEventsFound += (missed, firstRun) => Notifier.NotifyMissed(missed, firstRun);
        Drives.AlertRaised += OnNewEvent;
        Chkdsk.Completed += OnScanCompleted;
    }

    /// <summary>Raised on the UI thread after any setting is saved.</summary>
    public event Action? SettingsChanged;

    public bool DemoMode { get; }
    public AppSettings Settings { get; }
    public StateStore State { get; }
    public EventStore Events { get; }
    public DriveMonitor Drives { get; }
    public EventLogMonitor Monitor { get; }
    public NotificationService Notifier { get; }
    public ChkdskService Chkdsk { get; }
    public ProactiveService Proactive { get; }
    public UpdateService Updates { get; }

    /// <summary>Set by the UI: true while the main window is in front of the user.</summary>
    public Func<bool>? IsWindowActive { get; set; }

    /// <summary>Begins monitoring in the background: drives first, so events can be matched to them.</summary>
    public void Start()
    {
        if (_started || DemoMode)
        {
            return;
        }

        _started = true;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _ = Task.Run(() =>
        {
            try
            {
                Drives.Refresh();
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Initial drive scan", ex);
            }

            Drives.StartTimer();
            Monitor.Start();
            Proactive.Start();
            _heartbeat.Change(HeartbeatInterval, HeartbeatInterval);
        });
    }

    /// <summary>Persists settings and tells the UI. Call on the UI thread.</summary>
    public void SaveSettings()
    {
        _ = Settings.TrySave();
        SettingsChanged?.Invoke();
    }

    /// <summary>Saves, then re-reads history because what counts as an event (or its tier) changed.</summary>
    public void SaveAndReloadEvents()
    {
        SaveSettings();
        if (!DemoMode)
        {
            _ = Monitor.ReloadAsync();
        }
    }

    /// <summary>Saves, then re-evaluates drive health with the new thresholds and interval.</summary>
    public void SaveAndRefreshDrives()
    {
        SaveSettings();
        if (!DemoMode)
        {
            Drives.ApplySettings();
        }
    }

    /// <summary>Re-tiers or mutes a rule; null restores its built-in tier.</summary>
    public void SetRuleOverride(string ruleId, string? value)
    {
        // Copy on write: background threads read the dictionary while classifying events.
        Dictionary<string, string> overrides = new(Settings.RuleOverrides, StringComparer.OrdinalIgnoreCase);
        if (value is null)
        {
            overrides.Remove(ruleId);
        }
        else
        {
            overrides[ruleId] = value;
        }

        Settings.RuleOverrides = overrides;
        SaveAndReloadEvents();
    }

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _heartbeat.Dispose();
        Proactive.Dispose();
        Monitor.Dispose();
        Drives.Dispose();
        Updates.Dispose();
        if (!DemoMode)
        {
            Monitor.MarkSeen(DateTime.UtcNow);
            _ = Settings.TrySave();
        }

        State.Dispose();
    }

    private void OnNewEvent(DiskEvent item)
    {
        Notifier.NotifyEvent(item);
        Proactive.OnEvent(item);
    }

    private void OnScanCompleted(ChkdskResult result)
    {
        _ = Drives.RefreshAsync(); // a repair can clear the dirty bit
        if (result.Cancelled || result.Error is not null)
        {
            return;
        }

        // Scans the user is watching need no toast; background ones report problems, and clean results on request.
        bool watching = !result.Automatic && IsWindowActive?.Invoke() == true;
        if (!watching && (!result.Clean || !result.Automatic || Settings.NotifyCleanScan))
        {
            Notifier.NotifyScanResult(result.Volume, result.Clean, result.Summary);
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            _ = Task.Delay(TimeSpan.FromSeconds(20)).ContinueWith(_ => Drives.RefreshAsync(), TaskScheduler.Default);
        }
    }
}
