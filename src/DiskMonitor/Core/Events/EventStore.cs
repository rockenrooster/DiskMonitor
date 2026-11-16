using DiskMonitor.App;

namespace DiskMonitor.Core.Events;

/// <summary>A snapshot of which events are read.</summary>
internal sealed record ReadState(DateTime ReadUpToUtc, HashSet<string> ReadKeys);

/// <summary>Unread counts per tier at one moment.</summary>
internal readonly record struct UnreadSummary(int Critical, int High, int Medium, int Low)
{
    public int Total => Critical + High + Medium + Low;

    public Tier? Highest => Critical > 0 ? Tier.Critical : High > 0 ? Tier.High : Medium > 0 ? Tier.Medium : Low > 0 ? Tier.Low : null;

    public int For(Tier tier) => tier switch
    {
        Tier.Critical => Critical,
        Tier.High => High,
        Tier.Medium => Medium,
        _ => Low
    };
}

/// <summary>
/// The in-memory list of disk events, newest first, plus which of them the user has read.
/// Safe to call from any thread; <see cref="Changed"/> is raised on the calling thread.
/// </summary>
internal sealed class EventStore
{
    private const int MaxEvents = 50_000;
    private readonly StateStore _state;
    private readonly Lock _gate = new();
    private readonly HashSet<string> _keys = [];
    private readonly HashSet<string> _readKeys;
    private List<DiskEvent> _events = [];
    private DiskEvent[]? _snapshot;
    private DateTime _readUpToUtc;

    public EventStore(StateStore state)
    {
        _state = state;
        _readUpToUtc = state.Read(s => s.ReadUpToUtc);
        _readKeys = state.Read(s => new HashSet<string>(s.ReadKeys));
        foreach (DiskEvent healthEvent in state.Read(s => s.HealthEvents.ToArray()))
        {
            if (_keys.Add(healthEvent.Key))
            {
                _events.Add(healthEvent);
            }
        }

        _events.Sort(NewestFirst);
    }

    /// <summary>Raised after the list or the read state changes.</summary>
    public event Action? Changed;

    /// <summary>All events, newest first. The returned list is never mutated.</summary>
    public IReadOnlyList<DiskEvent> Snapshot()
    {
        lock (_gate)
        {
            return _snapshot ??= _events.ToArray();
        }
    }

    /// <summary>Swaps in a fresh read of the Windows logs, keeping the alerts Disk Monitor raised itself.</summary>
    public void ReplaceLogEvents(IEnumerable<DiskEvent> logEvents, DateTime oldestKeptUtc)
    {
        lock (_gate)
        {
            List<DiskEvent> merged = _events.Where(e => e.Synthetic && e.TimeUtc >= oldestKeptUtc).ToList();
            _keys.Clear();
            foreach (DiskEvent kept in merged)
            {
                _keys.Add(kept.Key);
            }

            foreach (DiskEvent logEvent in logEvents)
            {
                if (merged.Count >= MaxEvents)
                {
                    break;
                }

                if (_keys.Add(logEvent.Key))
                {
                    merged.Add(logEvent);
                }
            }

            merged.Sort(NewestFirst);
            _events = merged;
            _snapshot = null;
            _readKeys.IntersectWith(_keys);
        }

        _state.Update(s =>
        {
            s.HealthEvents.RemoveAll(e => e.TimeUtc < oldestKeptUtc);
            s.ReadKeys = SnapshotReadKeys();
        });
        Changed?.Invoke();
    }

    /// <summary>Adds one event. Returns false if it was already present.</summary>
    public bool Add(DiskEvent item)
    {
        lock (_gate)
        {
            if (!_keys.Add(item.Key))
            {
                return false;
            }

            int index = _events.BinarySearch(item, Comparer<DiskEvent>.Create(NewestFirst));
            _events.Insert(index < 0 ? ~index : index, item);
            if (_events.Count > MaxEvents)
            {
                DiskEvent oldest = _events[^1];
                _events.RemoveAt(_events.Count - 1);
                _keys.Remove(oldest.Key);
            }

            _snapshot = null;
        }

        if (item.Synthetic)
        {
            _state.Update(s => s.HealthEvents.Add(item));
        }

        Changed?.Invoke();
        return true;
    }

    public bool IsUnread(DiskEvent item)
    {
        lock (_gate)
        {
            return IsUnreadLocked(item);
        }
    }

    public UnreadSummary GetUnread()
    {
        int critical = 0, high = 0, medium = 0, low = 0;
        lock (_gate)
        {
            foreach (DiskEvent item in _events)
            {
                if (item.TimeUtc <= _readUpToUtc)
                {
                    break; // newest first: everything after this is read
                }

                if (_readKeys.Contains(item.Key))
                {
                    continue;
                }

                switch (item.Tier)
                {
                    case Tier.Critical: critical++; break;
                    case Tier.High: high++; break;
                    case Tier.Medium: medium++; break;
                    default: low++; break;
                }
            }
        }

        return new UnreadSummary(critical, high, medium, low);
    }

    public void MarkRead(IEnumerable<DiskEvent> items)
    {
        bool changed = false;
        lock (_gate)
        {
            foreach (DiskEvent item in items)
            {
                if (IsUnreadLocked(item))
                {
                    changed |= _readKeys.Add(item.Key);
                }
            }
        }

        if (changed)
        {
            _state.Update(s => s.ReadKeys = SnapshotReadKeys());
            Changed?.Invoke();
        }
    }

    public void MarkAllRead()
    {
        lock (_gate)
        {
            _readUpToUtc = DateTime.UtcNow;
            _readKeys.Clear();
        }

        _state.Update(s =>
        {
            s.ReadUpToUtc = _readUpToUtc;
            s.ReadKeys = [];
        });
        Changed?.Invoke();
    }

    /// <summary>A copy of the read state, so "Mark as read" can be undone exactly.</summary>
    public ReadState CaptureReadState()
    {
        lock (_gate)
        {
            return new ReadState(_readUpToUtc, new HashSet<string>(_readKeys));
        }
    }

    public void RestoreReadState(ReadState saved)
    {
        lock (_gate)
        {
            _readUpToUtc = saved.ReadUpToUtc;
            _readKeys.Clear();
            _readKeys.UnionWith(saved.ReadKeys);
        }

        _state.Update(s =>
        {
            s.ReadUpToUtc = saved.ReadUpToUtc;
            s.ReadKeys = SnapshotReadKeys();
        });
        Changed?.Invoke();
    }

    private bool IsUnreadLocked(DiskEvent item) => item.TimeUtc > _readUpToUtc && !_readKeys.Contains(item.Key);

    private HashSet<string> SnapshotReadKeys()
    {
        lock (_gate)
        {
            return new HashSet<string>(_readKeys);
        }
    }

    private static int NewestFirst(DiskEvent left, DiskEvent right)
    {
        int byTime = right.TimeUtc.CompareTo(left.TimeUtc);
        return byTime != 0 ? byTime : string.CompareOrdinal(right.Key, left.Key);
    }
}
