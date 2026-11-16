using DiskMonitor.App;
using DiskMonitor.Core.Events;
using DiskMonitor.Diagnostics;

namespace DiskMonitor.Core.Drives;

/// <summary>
/// Polls drive health on a timer and raises an alert when a drive's own counters get worse.
/// Counters are compared with the last values seen, so a long-standing condition alerts once.
/// </summary>
internal sealed class DriveMonitor : IDisposable
{
    private const int TemperatureHysteresisC = 5;
    private const double LowSpaceHysteresisPercent = 2;
    private const long SmallestVolumeForSpaceAlerts = 8L * 1024 * 1024 * 1024;

    /// <summary>Log and provider names of health-check events; they name the source, they are not UI text.</summary>
    public const string HealthLog = "Disk Monitor";
    public const string HealthProvider = "Health check";

    private readonly AppSettings _settings;
    private readonly StateStore _state;
    private readonly EventStore _store;
    private readonly System.Threading.Timer _timer;
    private readonly Lock _scanGate = new();
    private volatile DriveSnapshot _current = DriveSnapshot.Empty;
    private volatile bool _refreshing;

    public DriveMonitor(AppSettings settings, StateStore state, EventStore store)
    {
        _settings = settings;
        _state = state;
        _store = store;
        _timer = new System.Threading.Timer(_ => SafeRefresh(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Raised when a scan starts and after every scan, on a background thread.</summary>
    public event Action? Updated;

    /// <summary>A drive or volume condition got worse.</summary>
    public event Action<DiskEvent>? AlertRaised;

    public DriveSnapshot Current => _current;

    /// <summary>True while a drive scan runs, so the Drives page can show that it is checking.</summary>
    public bool IsRefreshing => _refreshing;

    /// <summary>Replaces the snapshot without scanning. Used by demo mode.</summary>
    public void SetSnapshot(DriveSnapshot snapshot)
    {
        _current = snapshot;
        Updated?.Invoke();
    }

    /// <summary>Starts periodic polling. The first scan is expected to have been run already.</summary>
    public void StartTimer() => Reschedule();

    /// <summary>Applies a changed polling interval or threshold.</summary>
    public void ApplySettings()
    {
        Reschedule();
        Task.Run(SafeRefresh);
    }

    public Task RefreshAsync() => Task.Run(SafeRefresh);

    /// <summary>Scans now, on the calling thread. Concurrent callers wait and share the result.</summary>
    public DriveSnapshot Refresh()
    {
        lock (_scanGate)
        {
            _refreshing = true;
            Updated?.Invoke();
            try
            {
                DriveSnapshot snapshot = DriveScanner.Scan(_settings.TemperatureWarningC, _current);
                _current = snapshot;
                DetectAlerts(snapshot);
            }
            finally
            {
                _refreshing = false;
            }
        }

        Updated?.Invoke();
        return _current;
    }

    public void Dispose() => _timer.Dispose();

    private void Reschedule()
    {
        TimeSpan interval = TimeSpan.FromMinutes(Math.Max(1, _settings.HealthIntervalMinutes));
        _timer.Change(interval, interval);
    }

    private void SafeRefresh()
    {
        try
        {
            Refresh();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Drive scan", ex);
        }
    }

    private void DetectAlerts(DriveSnapshot snapshot)
    {
        List<DiskEvent> alerts = [];
        _state.Update(state =>
        {
            foreach (PhysicalDisk disk in snapshot.Disks)
            {
                if (disk.HealthSupported && !disk.Asleep)
                {
                    CheckDisk(disk, state, alerts);
                }
            }

            foreach (VolumeInfo volume in snapshot.Volumes)
            {
                CheckVolume(volume, snapshot, state, alerts);
            }
        });

        foreach (DiskEvent alert in alerts)
        {
            if (_store.Add(alert))
            {
                AlertRaised?.Invoke(alert);
            }
        }
    }

    private void CheckDisk(PhysicalDisk disk, PersistedState state, List<DiskEvent> alerts)
    {
        bool firstSight = !state.Disks.TryGetValue(disk.Key, out DiskBaseline? baseline);
        baseline ??= state.Disks[disk.Key] = new DiskBaseline();
        List<string> existing = [];

        void Counter(long? value, long previous, Action<long> store, string ruleId, string counter, bool reportExisting)
        {
            if (value is not { } current)
            {
                return;
            }

            if (firstSight)
            {
                if (current > 0 && reportExisting)
                {
                    existing.Add(Strings.AlertExistingPart(counter, current));
                }
            }
            else if (current > previous)
            {
                AddAlert(alerts, ruleId, Strings.AlertCounterRose(counter, disk.DisplayName, previous, current), disk, null);
            }

            store(current);
        }

        Counter(disk.Reallocated, baseline.Reallocated, v => baseline.Reallocated = v, "smart:reallocated", Strings.CounterReallocated, reportExisting: true);
        Counter(disk.Pending, baseline.Pending, v => baseline.Pending = v, "smart:pending", Strings.CounterPending, reportExisting: true);
        Counter(disk.Uncorrectable, baseline.Uncorrectable, v => baseline.Uncorrectable = v, "smart:uncorrectable", Strings.CounterUncorrectable, reportExisting: true);
        Counter(disk.MediaErrors, baseline.MediaErrors, v => baseline.MediaErrors = v, "nvme:media", Strings.CounterMediaErrors, reportExisting: true);
        Counter(disk.CrcErrors, baseline.CrcErrors, v => baseline.CrcErrors = v, "smart:crc", Strings.CounterCrcErrors, reportExisting: false);

        if (existing.Count > 0)
        {
            AddAlert(alerts, "smart:existing", Strings.AlertExisting(disk.DisplayName, existing), disk, null);
        }

        bool predicted = disk.PredictFailure == true;
        if (predicted && !baseline.PredictFailure)
        {
            AddAlert(alerts, "smart:predict", Strings.AlertPredict(disk.DisplayName), disk, null);
        }

        baseline.PredictFailure = predicted;

        int warning = (disk.NvmeCriticalWarning ?? 0) & 0x1D; // every flag except the temperature one
        int newFlags = warning & ~baseline.CriticalWarning;
        if (newFlags != 0)
        {
            AddAlert(alerts, "nvme:critical", Strings.AlertNvme(disk.DisplayName, DescribeWarningFlags(newFlags)), disk, null);
        }

        baseline.CriticalWarning = warning;

        if (disk.LifeRemainingPercent is { } life)
        {
            if (life <= 10 && !baseline.WearAlerted)
            {
                AddAlert(alerts, "health:wear", Strings.AlertWear(disk.DisplayName, life), disk, null);
                baseline.WearAlerted = true;
            }
            else if (life > 10)
            {
                baseline.WearAlerted = false;
            }
        }

        if (disk.TemperatureC is { } temperature)
        {
            int threshold = _settings.TemperatureWarningC;
            if (temperature >= threshold && !baseline.HotAlerted)
            {
                AddAlert(alerts, "health:temperature", Strings.AlertHot(disk.DisplayName, temperature, threshold), disk, null);
                baseline.HotAlerted = true;
            }
            else if (temperature <= threshold - TemperatureHysteresisC)
            {
                baseline.HotAlerted = false;
            }
        }
    }

    private void CheckVolume(VolumeInfo volume, DriveSnapshot snapshot, PersistedState state, List<DiskEvent> alerts)
    {
        string key = volume.GuidPath.Length > 0 ? volume.GuidPath : volume.Letter;
        if (!state.Volumes.TryGetValue(key, out VolumeBaseline? baseline))
        {
            baseline = state.Volumes[key] = new VolumeBaseline();
        }

        PhysicalDisk? disk = volume.DiskNumbers.Count == 1 ? snapshot.FindDisk(volume.DiskNumbers[0]) : null;

        if (volume.Dirty == true && !baseline.DirtyAlerted)
        {
            AddAlert(alerts, "volume:dirty", Strings.AlertDirty(volume.DisplayName), disk, volume);
            baseline.DirtyAlerted = true;
        }
        else if (volume.Dirty == false)
        {
            baseline.DirtyAlerted = false;
        }

        int threshold = _settings.LowSpacePercent;
        bool eligible = threshold > 0 && !volume.Removable && volume.TotalBytes >= SmallestVolumeForSpaceAlerts;
        if (eligible && volume.FreePercent < threshold && !baseline.LowSpaceAlerted)
        {
            AddAlert(alerts, "volume:space", Strings.AlertLowSpace(volume.DisplayName, volume.FreeBytes, volume.TotalBytes, volume.FreePercent), disk, volume);
            baseline.LowSpaceAlerted = true;
        }
        else if (!eligible || volume.FreePercent >= threshold + LowSpaceHysteresisPercent)
        {
            baseline.LowSpaceAlerted = false;
        }
    }

    private void AddAlert(List<DiskEvent> alerts, string ruleId, string message, PhysicalDisk? disk, VolumeInfo? volume)
    {
        if (RuleCatalog.Find(ruleId) is not { } rule
            || RuleCatalog.ResolveTier(rule, _settings.RuleOverrides, out _) is not { } tier)
        {
            return;
        }

        DateTime now = Clock.UtcNow;
        int? diskNumber = disk?.Number;
        alerts.Add(new DiskEvent
        {
            Key = $"health|{ruleId}|{disk?.Key ?? volume?.Letter}|{now.Ticks}",
            TimeUtc = now,
            Tier = tier,
            Kind = rule.Kind,
            RuleId = rule.Id,
            Title = rule.Title,
            Message = message,
            Explanation = rule.Explanation,
            WhyItMatters = RuleCatalog.WhyItMatters(rule),
            Advice = rule.Advice,
            Log = HealthLog,
            Provider = HealthProvider,
            DiskNumber = diskNumber,
            Volume = volume?.Letter,
            DeviceText = DeviceResolver.Describe(diskNumber, disk, volume?.Letter),
            SuggestScan = rule.SuggestScan && volume is not null,
            Synthetic = true
        });
    }

    private static IEnumerable<string> DescribeWarningFlags(int flags)
    {
        if ((flags & 0x01) != 0)
        {
            yield return Strings.FlagSpare;
        }

        if ((flags & 0x04) != 0)
        {
            yield return Strings.FlagReliability;
        }

        if ((flags & 0x08) != 0)
        {
            yield return Strings.FlagReadOnly;
        }

        if ((flags & 0x10) != 0)
        {
            yield return Strings.FlagBackup;
        }
    }
}
