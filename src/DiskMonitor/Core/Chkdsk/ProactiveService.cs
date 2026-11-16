using DiskMonitor.App;
using DiskMonitor.Core.Drives;
using DiskMonitor.Core.Events;
using DiskMonitor.Core.Notifications;
using DiskMonitor.Diagnostics;

namespace DiskMonitor.Core.Chkdsk;

/// <summary>
/// Proactive CHKDSK: scans a volume when a file system error is reported on it, and scans every
/// fixed volume on a routine schedule. Only the read-only scan ever runs automatically. When a
/// scan would need a UAC prompt, a toast asks first instead of prompting out of nowhere.
/// </summary>
internal sealed class ProactiveService : IDisposable
{
    private static readonly TimeSpan AutoScanCooldown = TimeSpan.FromHours(24);
    private static readonly TimeSpan PromptInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    private readonly AppSettings _settings;
    private readonly StateStore _state;
    private readonly ChkdskService _chkdsk;
    private readonly NotificationService _notifier;
    private readonly Func<DriveSnapshot> _drives;
    private readonly System.Threading.Timer _timer;
    private int _routineRunning;

    public ProactiveService(AppSettings settings, StateStore state, ChkdskService chkdsk, NotificationService notifier, Func<DriveSnapshot> drives)
    {
        _settings = settings;
        _state = state;
        _chkdsk = chkdsk;
        _notifier = notifier;
        _drives = drives;
        _timer = new System.Threading.Timer(_ => CheckRoutine(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start() => _timer.Change(FirstCheckDelay, CheckInterval);

    public void Dispose() => _timer.Dispose();

    /// <summary>When the next routine scan is due, or null if routine scans are off.</summary>
    public DateTime? NextRoutineUtc
    {
        get
        {
            if (RoutineInterval is not { } interval)
            {
                return null;
            }

            DateTime last = _state.Read(s => s.LastRoutineScanUtc);
            return last == default ? DateTime.UtcNow : last + interval;
        }
    }

    /// <summary>Called for every new event: starts or offers a scan after a file system error.</summary>
    public void OnEvent(DiskEvent item)
    {
        if (!_settings.AutoScanOnError
            || !item.SuggestScan
            || item.Volume is null
            || item.RuleId.StartsWith("chkdsk:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        VolumeInfo? volume = _drives().FindVolume(item.Volume);
        if (volume is null || !volume.SupportsOnlineScan)
        {
            return;
        }

        string letter = volume.Letter;
        DateTime now = DateTime.UtcNow;
        bool due = false;
        _state.Update(s =>
        {
            if (!s.AutoScanUtc.TryGetValue(letter, out DateTime last) || now - last >= AutoScanCooldown)
            {
                s.AutoScanUtc[letter] = now;
                due = true;
            }
        });
        if (!due)
        {
            return;
        }

        if (_chkdsk.CanRunWithoutPrompt(ChkdskMode.Scan))
        {
            _ = _chkdsk.RunAsync(letter, ChkdskMode.Scan, automatic: true, ScanTrigger.FileSystemError);
        }
        else
        {
            _notifier.NotifyScanWanted(letter, Strings.ScanWantedReason(item.Title));
        }
    }

    /// <summary>Scans every fixed volume that supports the online scan, one after another.</summary>
    public async Task RunRoutineAsync(bool automatic)
    {
        if (Interlocked.Exchange(ref _routineRunning, 1) != 0)
        {
            return;
        }

        try
        {
            _state.Update(s => s.LastRoutineScanUtc = DateTime.UtcNow);
            foreach (VolumeInfo volume in _drives().Volumes.Where(v => v.SupportsOnlineScan && !v.Removable))
            {
                ChkdskResult result = await _chkdsk.RunAsync(volume.Letter, ChkdskMode.Scan, automatic, automatic ? ScanTrigger.Routine : ScanTrigger.User).ConfigureAwait(false);
                if (result.Cancelled || result.Error is not null)
                {
                    break; // the user stopped it or declined approval: do not prompt once per volume
                }
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Routine scan", ex);
        }
        finally
        {
            Volatile.Write(ref _routineRunning, 0);
        }
    }

    private TimeSpan? RoutineInterval => _settings.RoutineScan switch
    {
        "Weekly" => TimeSpan.FromDays(7),
        "Monthly" => TimeSpan.FromDays(30),
        _ => null
    };

    private void CheckRoutine()
    {
        try
        {
            if (RoutineInterval is not { } interval || _chkdsk.IsRunning)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            (DateTime lastScan, DateTime lastPrompt) = _state.Read(s => (s.LastRoutineScanUtc, s.LastRoutinePromptUtc));
            if (now - lastScan < interval)
            {
                return;
            }

            // A scan reads the whole file system; leave it for when the machine is plugged in.
            if (SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline)
            {
                return;
            }

            if (_chkdsk.CanRunWithoutPrompt(ChkdskMode.Scan))
            {
                _ = RunRoutineAsync(automatic: true);
            }
            else if (now - lastPrompt >= PromptInterval)
            {
                _state.Update(s => s.LastRoutinePromptUtc = now);
                _notifier.NotifyRoutineScanDue();
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Routine check", ex);
        }
    }
}
