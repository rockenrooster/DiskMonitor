using System.Diagnostics.Eventing.Reader;
using DiskMonitor.App;
using DiskMonitor.Core.Drives;
using DiskMonitor.Diagnostics;
using Microsoft.Win32;

namespace DiskMonitor.Core.Events;

/// <summary>
/// Reads disk problems from the Windows event logs: a history pass at startup, then push
/// subscriptions that deliver each new record the moment Windows writes it.
/// </summary>
internal sealed class EventLogMonitor : IDisposable
{
    private const string SystemLog = "System";
    private const string ApplicationLog = "Application";

    // Warnings and worse from any provider (filtered by provider in code), plus the informational
    // NTFS volume-state record, which carries repair requests as free text.
    private const string SystemFilter = "(Level=1 or Level=2 or Level=3) or (Provider[@Name='Microsoft-Windows-Ntfs'] and EventID=98)";
    private const string ApplicationFilter = "Provider[@Name='Chkdsk' or @Name='Microsoft-Windows-Chkdsk' or @Name='Wininit' or @Name='Microsoft-Windows-Wininit']";
    private const int MaxHistoryEvents = 40_000;
    private const int MaxMessageLength = 16_000;

    private readonly AppSettings _settings;
    private readonly EventStore _store;
    private readonly StateStore _state;
    private readonly Func<DriveSnapshot> _drives;
    private readonly Lock _gate = new();
    private readonly List<EventLogWatcher> _watchers = [];
    private HashSet<string> _systemProviders = RuleCatalog.BuildSystemProviderSet([]);
    private DateTime _startedUtc = DateTime.UtcNow;
    private int _loading;
    private bool _reloadRequested;
    private bool _disposed;

    public EventLogMonitor(AppSettings settings, EventStore store, StateStore state, Func<DriveSnapshot> drives)
    {
        _settings = settings;
        _store = store;
        _state = state;
        _drives = drives;
    }

    /// <summary>A new disk problem was written to a Windows log while the app was running.</summary>
    public event Action<DiskEvent>? EventArrived;

    /// <summary>Problems logged while the app was not running. The flag is true on the very first run.</summary>
    public event Action<IReadOnlyList<DiskEvent>, bool>? MissedEventsFound;

    public event Action? StatusChanged;

    /// <summary>True while at least one log subscription is active.</summary>
    public bool IsLive { get; private set; }
    public bool IsLoading => Volatile.Read(ref _loading) != 0;
    public string? LastError { get; private set; }
    public int WatchedProviderCount => _systemProviders.Count;

    /// <summary>Subscribes to the logs, then loads history. Call once, from a background thread.</summary>
    public void Start()
    {
        _systemProviders = RuleCatalog.BuildSystemProviderSet(DiscoverPortDrivers());
        _startedUtc = DateTime.UtcNow;
        StartWatcher(SystemLog, $"*[System[{SystemFilter}]]");
        StartWatcher(ApplicationLog, $"*[System[{ApplicationFilter}]]");
        lock (_gate)
        {
            IsLive = _watchers.Count > 0;
        }

        LoadHistory(reportMissed: true);
    }

    /// <summary>Re-reads history, for example after the history window or a rule's tier changed.</summary>
    public Task ReloadAsync() => Task.Run(() => LoadHistory(reportMissed: false));

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (EventLogWatcher watcher in _watchers)
            {
                try
                {
                    watcher.Enabled = false;
                    watcher.Dispose();
                }
                catch (Exception ex)
                {
                    ErrorLog.Write("Watcher dispose", ex);
                }
            }

            _watchers.Clear();
            IsLive = false;
        }
    }

    /// <summary>Classifies one record. Exposed for the self-check.</summary>
    internal DiskEvent? Convert(EventRecord record, string log, DriveSnapshot drives)
    {
        string provider = record.ProviderName ?? "";
        if (log == SystemLog && !_systemProviders.Contains(provider))
        {
            return null;
        }

        string message = Describe(record);
        RawEvent raw = new(log, provider, record.Id, record.Level ?? 0, message);
        if (RuleCatalog.Classify(raw, _settings.RuleOverrides) is not { } classification)
        {
            return null;
        }

        ResolvedDevice device = DeviceResolver.Resolve(message, drives);
        Tier tier = classification.Tier;
        if (!classification.Overridden)
        {
            // Snapshot inconsistencies and pulled memory cards look like real faults but rarely are.
            if (device.ShadowCopy || (_settings.ReduceRemovableSeverity && device.RemovableMedia))
            {
                tier = tier.Lower();
            }
        }

        EventRule rule = classification.Rule;
        DateTime timeUtc = (record.TimeCreated ?? DateTime.Now).ToUniversalTime();
        long recordId = record.RecordId ?? 0;
        return new DiskEvent
        {
            Key = $"{log}|{recordId}|{timeUtc.Ticks}",
            TimeUtc = timeUtc,
            Tier = tier,
            Kind = rule.Kind,
            RuleId = rule.Id,
            Title = rule.Title,
            Message = message,
            Explanation = rule.Explanation,
            Advice = rule.Advice,
            Log = log,
            Provider = provider,
            EventId = record.Id,
            RecordId = recordId,
            Level = record.Level ?? 0,
            DiskNumber = device.DiskNumber,
            Volume = device.Volume,
            DeviceText = device.Text,
            SuggestScan = rule.SuggestScan && device.Volume is not null
        };
    }

    private void LoadHistory(bool reportMissed)
    {
        if (Interlocked.Exchange(ref _loading, 1) != 0)
        {
            _reloadRequested = true; // a load is running; it will go round again when it finishes
            return;
        }

        try
        {
            do
            {
                _reloadRequested = false;
                StatusChanged?.Invoke();
                int days = _settings.HistoryDays;
                DateTime oldestUtc = DateTime.UtcNow.AddDays(-days);
                long windowMilliseconds = (long)TimeSpan.FromDays(days).TotalMilliseconds;
                string window = $"TimeCreated[timediff(@SystemTime) <= {windowMilliseconds}]";
                DriveSnapshot drives = _drives();
                List<DiskEvent> events = [];
                LastError = null;
                ReadLog(SystemLog, $"*[System[({SystemFilter}) and {window}]]", drives, events);
                if (_settings.IncludeChkdskEvents)
                {
                    ReadLog(ApplicationLog, $"*[System[{ApplicationFilter} and {window}]]", drives, events);
                }

                _store.ReplaceLogEvents(events, oldestUtc);

                if (reportMissed)
                {
                    reportMissed = false;
                    DateTime lastSeenUtc = _state.Read(s => s.LastSeenUtc);
                    List<DiskEvent> missed = events.Where(e => e.TimeUtc > lastSeenUtc && e.TimeUtc < _startedUtc).ToList();
                    if (missed.Count > 0)
                    {
                        MissedEventsFound?.Invoke(missed, lastSeenUtc == default);
                    }
                }

                MarkSeen(DateTime.UtcNow);
            }
            while (_reloadRequested);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            ErrorLog.Write("History load", ex);
        }
        finally
        {
            Volatile.Write(ref _loading, 0);
            StatusChanged?.Invoke();
        }
    }

    private void ReadLog(string log, string xpath, DriveSnapshot drives, List<DiskEvent> into)
    {
        try
        {
            EventLogQuery query = new(log, PathType.LogName, xpath) { ReverseDirection = true };
            using EventLogReader reader = new(query);
            while (into.Count < MaxHistoryEvents && reader.ReadEvent() is { } record)
            {
                using (record)
                {
                    try
                    {
                        if (Convert(record, log, drives) is { } item)
                        {
                            into.Add(item);
                        }
                    }
                    catch (EventLogException)
                    {
                        // One unreadable record must not hide the rest of the log.
                    }
                }
            }
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException or InvalidOperationException)
        {
            LastError = $"Could not read the {log} log: {ex.Message}";
            ErrorLog.Write($"Read {log} log", ex);
        }
    }

    private void StartWatcher(string log, string xpath)
    {
        try
        {
            EventLogWatcher watcher = new(new EventLogQuery(log, PathType.LogName, xpath));
            watcher.EventRecordWritten += (_, e) => OnRecordWritten(log, e);
            watcher.Enabled = true;
            lock (_gate)
            {
                if (_disposed)
                {
                    watcher.Dispose();
                    return;
                }

                _watchers.Add(watcher);
            }
        }
        catch (Exception ex)
        {
            LastError = $"Could not watch the {log} log: {ex.Message}";
            ErrorLog.Write($"Watch {log} log", ex);
        }
    }

    private void OnRecordWritten(string log, EventRecordWrittenEventArgs e)
    {
        if (e.EventRecord is null)
        {
            if (e.EventException is { } failure)
            {
                LastError = failure.Message;
                ErrorLog.Write($"{log} subscription", failure);
                StatusChanged?.Invoke();
            }

            return;
        }

        using EventRecord record = e.EventRecord;
        try
        {
            if (log == ApplicationLog && !_settings.IncludeChkdskEvents)
            {
                return;
            }

            if (Convert(record, log, _drives()) is not { } item || !_store.Add(item))
            {
                return;
            }

            MarkSeen(item.TimeUtc);
            EventArrived?.Invoke(item);
        }
        catch (Exception ex)
        {
            ErrorLog.Write($"{log} record", ex);
        }
    }

    /// <summary>Records that everything up to <paramref name="timeUtc"/> has been processed.</summary>
    public void MarkSeen(DateTime timeUtc) => _state.Update(s =>
    {
        if (timeUtc > s.LastSeenUtc)
        {
            s.LastSeenUtc = timeUtc;
        }
    });

    private static string Describe(EventRecord record)
    {
        string? text = null;
        try
        {
            text = record.FormatDescription();
        }
        catch (EventLogException)
        {
            // The provider's message file is missing; fall back to the raw insertion values.
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            try
            {
                text = string.Join(" ", record.Properties.Select(p => p.Value?.ToString()).Where(v => !string.IsNullOrWhiteSpace(v)));
            }
            catch (EventLogException)
            {
                text = "";
            }
        }

        text = text.Trim();
        return text.Length > MaxMessageLength ? text[..MaxMessageLength] + "…" : text;
    }

    // Storage miniports register in the "SCSI Miniport" load group, whoever their vendor is.
    private static List<string> DiscoverPortDrivers()
    {
        List<string> drivers = [];
        try
        {
            using RegistryKey? services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (services is null)
            {
                return drivers;
            }

            foreach (string name in services.GetSubKeyNames())
            {
                try
                {
                    using RegistryKey? service = services.OpenSubKey(name);
                    if (service?.GetValue("Group") is string group && group.Equals("SCSI Miniport", StringComparison.OrdinalIgnoreCase))
                    {
                        drivers.Add(name);
                    }
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
                {
                    // Some service keys are locked down; they are not storage drivers.
                }
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Discover storage drivers", ex);
        }

        return drivers;
    }
}
