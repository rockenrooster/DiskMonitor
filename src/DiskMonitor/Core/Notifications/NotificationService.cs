using System.Security.Cryptography;
using System.Text;
using DiskMonitor.App;
using DiskMonitor.Core.Events;
using DiskMonitor.Diagnostics;

namespace DiskMonitor.Core.Notifications;

/// <summary>A toast button. A dismiss button closes the toast without waking the app.</summary>
internal sealed record ToastAction(string Label, string Uri, bool Dismiss = false);

internal sealed record ToastRequest
{
    public required string Title { get; init; }
    public string Line1 { get; init; } = "";
    public string Line2 { get; init; } = "";
    public string Attribution { get; init; } = "";
    public required string LaunchUri { get; init; }
    public IReadOnlyList<ToastAction> Actions { get; init; } = [];
    public bool Silent { get; init; }
    public bool Sticky { get; init; }
    public string? Tag { get; init; }
    public string Group { get; init; } = "events";
    public Tier? Tier { get; init; }
}

/// <summary>
/// Decides which events deserve a toast (tier options, snooze, repeat grouping, burst limit)
/// and shows them. Safe to call from any thread.
/// </summary>
internal sealed class NotificationService
{
    private const int MaxToastsPerMinute = 4;
    private static readonly TimeSpan BurstNoticeInterval = TimeSpan.FromMinutes(5);

    private readonly AppSettings _settings;
    private readonly StateStore _state;
    private readonly Action<Action> _postToUi;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, RepeatEntry> _recent = [];
    private readonly Queue<DateTime> _shownTimes = new();
    private readonly List<Suppression> _suppressions = [];
    private DateTime _lastBurstNoticeUtc;
    private DateTime _snoozeUntilUtc;

    public NotificationService(AppSettings settings, StateStore state, Action<Action> postToUi)
    {
        _settings = settings;
        _state = state;
        _postToUi = postToUi;
        _snoozeUntilUtc = state.Read(s => s.SnoozeUntilUtc);
    }

    public event Action? SnoozeChanged;

    /// <summary>False in previews and self-checks, where nothing may reach the user's screen.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Random per-run value embedded in the buttons of this run's toasts. A link that carries it
    /// came from a toast this process showed, so it may trigger an action rather than just navigate.
    /// </summary>
    public string SessionToken { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    /// <summary>Used when Windows rejects a toast: title, body, tier. Returns true if shown.</summary>
    public Func<string, string, Tier?, bool>? BalloonFallback { get; set; }

    public string? LastError { get; private set; }

    public DateTime SnoozeUntilUtc
    {
        get
        {
            lock (_gate)
            {
                return _snoozeUntilUtc;
            }
        }
    }

    public bool IsSnoozed => DateTime.UtcNow < SnoozeUntilUtc;

    /// <summary>Silences toasts for a while; null resumes them. Events are still recorded.</summary>
    public void Snooze(TimeSpan? duration)
    {
        DateTime until = duration is null ? DateTime.MinValue : DateTime.UtcNow + duration.Value;
        lock (_gate)
        {
            _snoozeUntilUtc = until;
        }

        _state.Update(s => s.SnoozeUntilUtc = until);
        SnoozeChanged?.Invoke();
    }

    /// <summary>Holds back toasts for matching events, used while a CHKDSK run reports its own result.</summary>
    public Suppression SuppressRule(string rulePrefix, string? volume, TimeSpan duration)
    {
        Suppression suppression = new(rulePrefix, volume) { UntilUtc = DateTime.UtcNow + duration };
        lock (_gate)
        {
            _suppressions.Add(suppression);
        }

        return suppression;
    }

    /// <summary>Ends a suppression after <paramref name="remaining"/> instead of its original duration.</summary>
    public void Shorten(Suppression suppression, TimeSpan remaining)
    {
        lock (_gate)
        {
            suppression.UntilUtc = DateTime.UtcNow + remaining;
        }
    }

    public void NotifyEvent(DiskEvent item)
    {
        TierOptions options = _settings.OptionsFor(item.Tier);
        if (!options.Toast)
        {
            return;
        }

        int repeats = 0;
        bool burst = false;
        lock (_gate)
        {
            DateTime now = DateTime.UtcNow;
            if (now < _snoozeUntilUtc || IsSuppressedLocked(item, now))
            {
                return;
            }

            if (_recent.TryGetValue(item.GroupKey, out RepeatEntry? entry))
            {
                int window = _settings.CoalesceMinutes;
                if (window > 0 && now - entry.LastShownUtc < TimeSpan.FromMinutes(window))
                {
                    entry.Suppressed++;
                    return;
                }

                repeats = entry.Suppressed;
                entry.Suppressed = 0;
                entry.LastShownUtc = now;
            }
            else
            {
                if (_recent.Count > 512)
                {
                    foreach (string stale in _recent.Where(p => now - p.Value.LastShownUtc > TimeSpan.FromHours(6)).Select(p => p.Key).ToList())
                    {
                        _recent.Remove(stale);
                    }
                }

                _recent[item.GroupKey] = new RepeatEntry { LastShownUtc = now };
            }

            while (_shownTimes.Count > 0 && now - _shownTimes.Peek() > TimeSpan.FromMinutes(1))
            {
                _shownTimes.Dequeue();
            }

            if (_shownTimes.Count >= MaxToastsPerMinute)
            {
                if (now - _lastBurstNoticeUtc < BurstNoticeInterval)
                {
                    return;
                }

                _lastBurstNoticeUtc = now;
                burst = true;
            }
            else
            {
                _shownTimes.Enqueue(now);
            }
        }

        if (burst)
        {
            Show(new ToastRequest
            {
                Title = "Disk events are arriving rapidly",
                Line1 = "More disk problems are being logged than can be shown one by one.",
                Line2 = "Open Disk Monitor to review them.",
                Attribution = AppPaths.AppName,
                LaunchUri = AppLinks.ForPage("Events"),
                Actions = [new ToastAction("Review events", AppLinks.ForPage("Events"))],
                Silent = true,
                Tag = "burst",
                Tier = item.Tier
            });
            return;
        }

        Show(BuildEventToast(item, repeats, options));
    }

    /// <summary>One summary toast for problems logged while the app was not running.</summary>
    public void NotifyMissed(IReadOnlyList<DiskEvent> missed, bool firstRun)
    {
        if (missed.Count == 0)
        {
            return;
        }

        Tier highest = missed.Max(e => e.Tier);
        TierOptions options = _settings.OptionsFor(highest);
        if (!options.Toast || IsSnoozed)
        {
            return;
        }

        string breakdown = string.Join(", ", TierExtensions.Descending
            .Select(tier => (tier, count: missed.Count(e => e.Tier == tier)))
            .Where(p => p.count > 0)
            .Select(p => $"{p.count:N0} {p.tier.Label().ToLowerInvariant()}"));
        DiskEvent newest = missed.MaxBy(e => e.TimeUtc)!;
        string latest = newest.DeviceText.Length > 0 ? $"Latest: {newest.Title} · {newest.DeviceText}" : $"Latest: {newest.Title}";

        Show(new ToastRequest
        {
            Title = firstRun
                ? $"Found {Format.Count(missed.Count, "disk problem")} in recent history"
                : $"{Format.Count(missed.Count, "disk problem")} while Disk Monitor was not running",
            Line1 = breakdown,
            Line2 = latest,
            Attribution = AppPaths.AppName,
            LaunchUri = AppLinks.ForPage("Events"),
            Actions = [new ToastAction("Review events", AppLinks.ForPage("Events"))],
            Silent = !options.Sound,
            Sticky = options.Sticky,
            Tag = "missed",
            Tier = highest
        });
    }

    public void NotifyScanResult(string volume, bool clean, string summary)
    {
        if (IsSnoozed)
        {
            return;
        }

        Show(new ToastRequest
        {
            Title = clean ? $"Scan of {volume} found no problems" : $"Scan of {volume} needs your attention",
            Line1 = summary,
            Attribution = "Check Disk",
            LaunchUri = AppLinks.ForPage("CheckDisk"),
            Actions = [new ToastAction("Open Check Disk", AppLinks.ForPage("CheckDisk"))],
            Silent = clean || !_settings.High.Sound,
            Tag = "scan-" + volume.TrimEnd(':'),
            Group = "scans",
            Tier = clean ? null : Tier.High
        });
    }

    /// <summary>Asks for approval when a scan is wanted but cannot run without an administrator prompt.</summary>
    public void NotifyScanWanted(string volume, string reason)
    {
        if (IsSnoozed)
        {
            return;
        }

        Show(new ToastRequest
        {
            Title = $"Scan {volume} for file system errors?",
            Line1 = reason,
            Line2 = "The scan is read-only and needs administrator approval.",
            Attribution = "Check Disk",
            LaunchUri = AppLinks.ForPage("CheckDisk"),
            Actions =
            [
                new ToastAction($"Scan {volume}", AppLinks.ForScan(volume, SessionToken)),
                new ToastAction("Not now", "", Dismiss: true)
            ],
            Silent = true,
            Tag = "scan-ask-" + volume.TrimEnd(':'),
            Group = "scans"
        });
    }

    public void NotifyRoutineScanDue()
    {
        if (IsSnoozed)
        {
            return;
        }

        Show(new ToastRequest
        {
            Title = "Routine disk scan is due",
            Line1 = "Open Check Disk to scan your volumes for file system errors.",
            Attribution = "Check Disk",
            LaunchUri = AppLinks.ForPage("CheckDisk"),
            Actions = [new ToastAction("Open Check Disk", AppLinks.ForPage("CheckDisk"))],
            Silent = true,
            Tag = "routine",
            Group = "scans"
        });
    }

    public void NotifyUpdateAvailable(string version)
    {
        Show(new ToastRequest
        {
            Title = $"Disk Monitor {version} is available",
            Line1 = "Open Settings to download and install the update.",
            Attribution = AppPaths.AppName,
            LaunchUri = AppLinks.ForPage("Settings"),
            Actions = [new ToastAction("Open Settings", AppLinks.ForPage("Settings"))],
            Silent = true,
            Tag = "update",
            Group = "app"
        });
    }

    /// <summary>Shows a sample toast for a tier, ignoring snooze, so its options can be tried out.</summary>
    public void SendTest(Tier tier)
    {
        TierOptions options = _settings.OptionsFor(tier);
        Show(new ToastRequest
        {
            Title = $"{tier.Label()}: Test notification",
            Line1 = "Disk 0 · Example drive (C:)",
            Line2 = $"This is how a {tier.Label().ToLowerInvariant()} disk alert looks and sounds.",
            Attribution = "Disk Monitor · Test",
            LaunchUri = AppLinks.ForPage("Settings"),
            Actions = [new ToastAction("Open Disk Monitor", AppLinks.ForPage("Overview"))],
            Silent = !options.Sound,
            Sticky = options.Sticky,
            Tag = "test",
            Group = "app",
            Tier = tier
        });
    }

    public ToastSetting QueryWindowsSetting() => Enabled ? ToastInterop.QuerySetting(AppPaths.AppUserModelId) : ToastSetting.Unknown;

    private ToastRequest BuildEventToast(DiskEvent item, int repeats, TierOptions options)
    {
        string title = $"{item.Tier.Label()}: {item.Title}";
        if (repeats > 0)
        {
            title += $" (+{repeats:N0} more)";
        }

        List<ToastAction> actions = [new("View details", AppLinks.ForEvent(item.Key))];
        if (item.SuggestScan && item.Volume is not null)
        {
            actions.Add(new ToastAction($"Scan {item.Volume}", AppLinks.ForScan(item.Volume, SessionToken)));
        }

        actions.Add(new ToastAction("Snooze 1 hour", AppLinks.ForSnooze(60, SessionToken)));

        bool hasDevice = item.DeviceText.Length > 0;
        return new ToastRequest
        {
            Title = title,
            Line1 = hasDevice ? item.DeviceText : Format.SingleLine(item.Message, 160),
            Line2 = item.Advice.Length > 0 ? Format.SingleLine(item.Advice, 180) : (hasDevice ? Format.SingleLine(item.Message, 160) : ""),
            Attribution = item.Synthetic ? "Disk Monitor · Health check" : $"{item.Log} log · {item.Provider} {item.EventId}",
            LaunchUri = AppLinks.ForEvent(item.Key),
            Actions = actions,
            Silent = !options.Sound,
            Sticky = options.Sticky,
            Tag = HashTag(item.GroupKey),
            Tier = item.Tier
        };
    }

    private void Show(ToastRequest request)
    {
        if (!Enabled)
        {
            return;
        }

        _postToUi(() =>
        {
            try
            {
                int hr = ToastInterop.Show(AppPaths.AppUserModelId, BuildXml(request), request.Tag, request.Group);
                if (hr >= 0)
                {
                    LastError = null;
                    return;
                }

                LastError = $"Windows rejected the notification (0x{hr:X8}).";
                ErrorLog.Write("Toast", LastError);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                ErrorLog.Write("Toast", ex);
            }

            string body = string.Join(Environment.NewLine, new[] { request.Line1, request.Line2 }.Where(line => line.Length > 0));
            _ = BalloonFallback?.Invoke(request.Title, body, request.Tier);
        });
    }

    private bool IsSuppressedLocked(DiskEvent item, DateTime now)
    {
        _suppressions.RemoveAll(s => s.UntilUtc <= now);
        foreach (Suppression suppression in _suppressions)
        {
            if (item.RuleId.StartsWith(suppression.RulePrefix, StringComparison.OrdinalIgnoreCase)
                && (suppression.Volume is null || item.Volume is null || suppression.Volume.Equals(item.Volume, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    internal static string BuildXml(ToastRequest request)
    {
        StringBuilder xml = new(1024);
        xml.Append("<toast activationType=\"protocol\" launch=\"").Append(Escape(request.LaunchUri)).Append('"');
        if (request.Sticky && request.Actions.Count > 0)
        {
            xml.Append(" scenario=\"reminder\"");
        }

        xml.Append("><visual><binding template=\"ToastGeneric\">");
        xml.Append("<text>").Append(Escape(request.Title)).Append("</text>");
        if (request.Line1.Length > 0)
        {
            xml.Append("<text>").Append(Escape(request.Line1)).Append("</text>");
        }

        if (request.Line2.Length > 0)
        {
            xml.Append("<text>").Append(Escape(request.Line2)).Append("</text>");
        }

        if (request.Attribution.Length > 0)
        {
            xml.Append("<text placement=\"attribution\">").Append(Escape(request.Attribution)).Append("</text>");
        }

        xml.Append("</binding></visual>");
        if (request.Actions.Count > 0)
        {
            xml.Append("<actions>");
            foreach (ToastAction action in request.Actions.Take(5))
            {
                xml.Append("<action content=\"").Append(Escape(action.Label));
                if (action.Dismiss)
                {
                    xml.Append("\" activationType=\"system\" arguments=\"dismiss\"/>");
                }
                else
                {
                    xml.Append("\" activationType=\"protocol\" arguments=\"").Append(Escape(action.Uri)).Append("\"/>");
                }
            }

            xml.Append("</actions>");
        }

        xml.Append(request.Silent ? "<audio silent=\"true\"/>" : "<audio src=\"ms-winsoundevent:Notification.Default\"/>");
        xml.Append("</toast>");
        return xml.ToString();
    }

    private static string Escape(string text)
    {
        StringBuilder escaped = new(text.Length + 16);
        foreach (char c in text)
        {
            switch (c)
            {
                case '&': escaped.Append("&amp;"); break;
                case '<': escaped.Append("&lt;"); break;
                case '>': escaped.Append("&gt;"); break;
                case '"': escaped.Append("&quot;"); break;
                case '\'': escaped.Append("&apos;"); break;
                default:
                    // Control characters other than tab and line breaks are not legal in XML 1.0.
                    if (c >= ' ' || c is '\t' or '\n' or '\r')
                    {
                        escaped.Append(c);
                    }

                    break;
            }
        }

        return escaped.ToString();
    }

    private static string HashTag(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)), 0, 8);

    private sealed class RepeatEntry
    {
        public DateTime LastShownUtc { get; set; }
        public int Suppressed { get; set; }
    }

    /// <summary>A temporary mute for one rule family on one volume.</summary>
    internal sealed class Suppression(string rulePrefix, string? volume)
    {
        public string RulePrefix { get; } = rulePrefix;
        public string? Volume { get; } = volume;
        public DateTime UntilUtc { get; set; }
    }
}
