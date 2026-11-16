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

    private readonly AppSettings _settings;
    private readonly StateStore _state;
    private readonly EventStore _store;
    private readonly System.Threading.Timer _timer;
    private readonly Lock _scanGate = new();
    private volatile DriveSnapshot _current = DriveSnapshot.Empty;

    public DriveMonitor(AppSettings settings, StateStore state, EventStore store)
    {
        _settings = settings;
        _state = state;
        _store = store;
        _timer = new System.Threading.Timer(_ => SafeRefresh(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Raised after every scan, on a background thread.</summary>
    public event Action? Updated;

    /// <summary>A drive or volume condition got worse.</summary>
    public event Action<DiskEvent>? AlertRaised;

    public DriveSnapshot Current => _current;

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
            DriveSnapshot snapshot = DriveScanner.Scan(_settings.TemperatureWarningC, _current);
            _current = snapshot;
            DetectAlerts(snapshot);
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

        void Counter(long? value, long previous, Action<long> store, string ruleId, string noun, bool reportExisting)
        {
            if (value is not { } current)
            {
                return;
            }

            if (firstSight)
            {
                if (current > 0 && reportExisting)
                {
                    existing.Add($"{noun}: {current:N0}");
                }
            }
            else if (current > previous)
            {
                AddAlert(alerts, ruleId, $"{Capitalize(noun)} on {disk.DisplayName} rose from {previous:N0} to {current:N0}.", disk, null);
            }

            store(current);
        }

        Counter(disk.Reallocated, baseline.Reallocated, v => baseline.Reallocated = v, "smart:reallocated", "reallocated sectors", reportExisting: true);
        Counter(disk.Pending, baseline.Pending, v => baseline.Pending = v, "smart:pending", "pending sectors", reportExisting: true);
        Counter(disk.Uncorrectable, baseline.Uncorrectable, v => baseline.Uncorrectable = v, "smart:uncorrectable", "uncorrectable sectors", reportExisting: true);
        Counter(disk.MediaErrors, baseline.MediaErrors, v => baseline.MediaErrors = v, "nvme:media", "media errors", reportExisting: true);
        Counter(disk.CrcErrors, baseline.CrcErrors, v => baseline.CrcErrors = v, "smart:crc", "interface CRC errors", reportExisting: false);

        if (existing.Count > 0)
        {
            AddAlert(alerts, "smart:existing", $"{disk.DisplayName} already reports {string.Join(", ", existing)}.", disk, null);
        }

        bool predicted = disk.PredictFailure == true;
        if (predicted && !baseline.PredictFailure)
        {
            AddAlert(alerts, "smart:predict", $"{disk.DisplayName} reports that it is about to fail.", disk, null);
        }

        baseline.PredictFailure = predicted;

        int warning = (disk.NvmeCriticalWarning ?? 0) & 0x1D; // every flag except the temperature one
        int newFlags = warning & ~baseline.CriticalWarning;
        if (newFlags != 0)
        {
            AddAlert(alerts, "nvme:critical", $"{disk.DisplayName}: {string.Join("; ", DescribeWarningFlags(newFlags))}.", disk, null);
        }

        baseline.CriticalWarning = warning;

        if (disk.LifeRemainingPercent is { } life)
        {
            if (life <= 10 && !baseline.WearAlerted)
            {
                AddAlert(alerts, "health:wear", $"{disk.DisplayName} has {life}% of its rated life remaining.", disk, null);
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
                AddAlert(alerts, "health:temperature", $"{disk.DisplayName} is at {temperature:0} °C (warning level {threshold} °C).", disk, null);
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
            AddAlert(alerts, "volume:dirty", $"Volume {volume.DisplayName} is marked dirty. Windows will check it at the next restart.", disk, volume);
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
            AddAlert(
                alerts,
                "volume:space",
                $"Volume {volume.DisplayName} has {Format.Bytes(volume.FreeBytes)} free of {Format.Bytes(volume.TotalBytes)} ({volume.FreePercent:0.#}%).",
                disk,
                volume);
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

        DateTime now = DateTime.UtcNow;
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
            Advice = rule.Advice,
            Log = "Disk Monitor",
            Provider = "Health check",
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
            yield return "spare capacity has fallen below the drive's threshold";
        }

        if ((flags & 0x04) != 0)
        {
            yield return "reliability is degraded";
        }

        if ((flags & 0x08) != 0)
        {
            yield return "the drive has switched to read-only mode";
        }

        if ((flags & 0x10) != 0)
        {
            yield return "the volatile memory backup has failed";
        }
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
