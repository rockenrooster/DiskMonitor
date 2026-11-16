using System.Text.Json;
using DiskMonitor.Core.Events;
using DiskMonitor.Diagnostics;

namespace DiskMonitor.App;

/// <summary>Last known health counters of one drive, so only changes raise alerts.</summary>
internal sealed class DiskBaseline
{
    public long Reallocated { get; set; }
    public long Pending { get; set; }
    public long Uncorrectable { get; set; }
    public long CrcErrors { get; set; }
    public long MediaErrors { get; set; }
    public bool PredictFailure { get; set; }
    public int CriticalWarning { get; set; }
    public bool HotAlerted { get; set; }
    public bool WearAlerted { get; set; }
}

internal sealed class VolumeBaseline
{
    public bool LowSpaceAlerted { get; set; }
    public bool DirtyAlerted { get; set; }
}

/// <summary>Outcome of the most recent CHKDSK run on a volume.</summary>
internal sealed class ScanRecord
{
    public DateTime TimeUtc { get; set; }
    public string Mode { get; set; } = "";
    public int ExitCode { get; set; }
    public bool Clean { get; set; }
    public bool Automatic { get; set; }
    public string Summary { get; set; } = "";

    /// <summary>A <see cref="Core.Chkdsk.ChkdskOutcome"/> name; empty in records written before it existed.</summary>
    public string Outcome { get; set; } = "";
}

/// <summary>Everything Disk Monitor remembers between runs that is not a user setting.</summary>
internal sealed class PersistedState
{
    /// <summary>Events at or before this moment count as read.</summary>
    public DateTime ReadUpToUtc { get; set; }

    /// <summary>Individually read events newer than <see cref="ReadUpToUtc"/>.</summary>
    public HashSet<string> ReadKeys { get; set; } = [];

    /// <summary>Time of the newest event already processed; later ones were missed while not running.</summary>
    public DateTime LastSeenUtc { get; set; }

    /// <summary>Alerts raised by Disk Monitor's own checks. They exist in no Windows log.</summary>
    public List<DiskEvent> HealthEvents { get; set; } = [];
    public Dictionary<string, DiskBaseline> Disks { get; set; } = [];
    public Dictionary<string, VolumeBaseline> Volumes { get; set; } = [];
    public Dictionary<string, ScanRecord> Scans { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, DateTime> AutoScanUtc { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime LastRoutineScanUtc { get; set; }
    public DateTime LastRoutinePromptUtc { get; set; }
    public DateTime SnoozeUntilUtc { get; set; }
    public string? UpdateNoticeVersion { get; set; }
    public bool StartupHintDismissed { get; set; }

    /// <summary>The one-time "still running in the notification area" balloon has been shown.</summary>
    public bool TrayHintShown { get; set; }
}

/// <summary>Thread-safe owner of <see cref="PersistedState"/> that saves shortly after each change.</summary>
internal sealed class StateStore : IDisposable
{
    private const int SaveDelayMilliseconds = 1500;
    private readonly Lock _gate = new();
    private readonly PersistedState _state;
    private readonly System.Threading.Timer _saveTimer;
    private readonly bool _persist;
    private bool _dirty;

    private StateStore(PersistedState state, bool persist)
    {
        _state = state;
        _persist = persist;
        _saveTimer = new System.Threading.Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Loads the saved state. With <paramref name="persist"/> false nothing is ever written back,
    /// for diagnostics and previews that must leave the real state untouched.
    /// </summary>
    public static StateStore Load(bool persist = true)
    {
        PersistedState? state = null;
        try
        {
            if (File.Exists(AppPaths.StateFile))
            {
                state = JsonSerializer.Deserialize(File.ReadAllText(AppPaths.StateFile), AppJsonContext.Default.PersistedState);
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("State load", ex);
        }

        state ??= new PersistedState();
        state.ReadKeys ??= [];
        state.HealthEvents ??= [];
        state.Disks ??= [];
        state.Volumes ??= [];
        state.Scans = new Dictionary<string, ScanRecord>(state.Scans ?? [], StringComparer.OrdinalIgnoreCase);
        state.AutoScanUtc = new Dictionary<string, DateTime>(state.AutoScanUtc ?? [], StringComparer.OrdinalIgnoreCase);
        return new StateStore(state, persist);
    }

    public T Read<T>(Func<PersistedState, T> reader)
    {
        lock (_gate)
        {
            return reader(_state);
        }
    }

    public void Update(Action<PersistedState> change)
    {
        lock (_gate)
        {
            change(_state);
            _dirty = _persist;
        }

        if (_persist)
        {
            _saveTimer.Change(SaveDelayMilliseconds, Timeout.Infinite);
        }
    }

    public void Flush()
    {
        lock (_gate)
        {
            if (!_dirty || !_persist)
            {
                return;
            }

            if (JsonFile.TryWrite(AppPaths.StateFile, _state, AppJsonContext.Default.PersistedState, "State save"))
            {
                _dirty = false;
            }
        }
    }

    public void Dispose()
    {
        _saveTimer.Dispose();
        Flush();
    }
}
