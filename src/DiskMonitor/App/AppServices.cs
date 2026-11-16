using DiskMonitor.Core;
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
    private readonly Action<Action> _postToUi;
    private bool _started;

    public AppServices(Action<Action> postToUi, bool demoMode)
    {
        _postToUi = postToUi;
        DemoMode = demoMode;
        Settings = AppSettings.Load();
        State = StateStore.Load(persist: !demoMode);
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

    /// <summary>Set by the UI: true while the main window is in front of the user. Read on background threads.</summary>
    public Func<bool>? IsWindowActive { get; set; }

    /// <summary>Raised on the UI thread when a scan the user is watching finishes, so the shell can show an in-app toast.</summary>
    public event Action<ChkdskResult>? WatchedScanFinished;

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

    /// <summary>Persists settings and tells the UI. Call on the UI thread. Demo mode never writes.</summary>
    public void SaveSettings()
    {
        if (!DemoMode)
        {
            _ = Settings.TrySave();
        }

        SettingsChanged?.Invoke();
    }

    /// <summary>Pushes a realistic sample event through the real pipeline. For development only (<c>--dev</c>).</summary>
    public void InjectTestEvent(string kind)
    {
        string ruleId = kind.ToLowerInvariant() switch
        {
            "critical" => "disk:7",
            "high" => "disk:51",
            "medium" => "disk:153",
            "low" => "ntfs:130",
            "logical" => "ntfs:55",
            _ => ""
        };
        if (RuleCatalog.Find(ruleId) is not { } rule)
        {
            return;
        }

        DriveSnapshot drives = Drives.Current;
        bool logical = ruleId == "ntfs:55";
        VolumeInfo? volume = logical ? drives.Volumes.FirstOrDefault(v => v.IsSystem) ?? drives.Volumes.FirstOrDefault() : null;
        PhysicalDisk? disk = volume is { DiskNumbers.Count: > 0 } ? drives.FindDisk(volume.DiskNumbers[0]) : drives.Disks.FirstOrDefault();
        DateTime now = Clock.UtcNow;
        DiskEvent item = new()
        {
            Key = "test|" + now.Ticks,
            TimeUtc = now,
            Tier = RuleCatalog.ResolveTier(rule, Settings.RuleOverrides, out _) ?? rule.Tier,
            Kind = rule.Kind,
            RuleId = rule.Id,
            Title = rule.Title,
            Message = Strings.TestEventMessage,
            Explanation = rule.Explanation,
            WhyItMatters = RuleCatalog.WhyItMatters(rule),
            Advice = rule.Advice,
            Log = TestLog,
            Provider = rule.Source,
            EventId = 0,
            DiskNumber = disk?.Number,
            Volume = volume?.Letter,
            DeviceText = DeviceResolver.Describe(disk?.Number, disk, volume?.Letter),
            SuggestScan = logical && volume is not null,
            Synthetic = false
        };

        // Such an event disappears at the next history reload, which is the intent.
        if (Events.Add(item))
        {
            OnNewEvent(item);
        }
    }

    /// <summary>Log name of injected test events; it names the source, it is not UI text.</summary>
    public const string TestLog = "Test";

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

        // Scans the user is watching need no Windows toast; the shell shows an in-app one instead.
        // Background ones report problems, and clean results on request.
        bool watching = !result.Automatic && IsWindowActive?.Invoke() == true;
        if (watching)
        {
            _postToUi(() => WatchedScanFinished?.Invoke(result));
        }
        else if (!result.Clean || !result.Automatic || Settings.NotifyCleanScan)
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
