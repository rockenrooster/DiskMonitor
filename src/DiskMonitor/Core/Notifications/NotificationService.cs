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

    /// <summary>Set for test toasts, which show whatever the snooze and quiet hours say.</summary>
    public bool IgnoreQuietHours { get; init; }
}

/// <summary>
/// Decides which events deserve a toast (tier options, snooze, quiet hours, repeat grouping, burst
/// limit) and shows them. Safe to call from any thread. Toast text never contains the raw event
/// message, file names or paths: toasts can appear on the lock screen.
/// </summary>
internal sealed class NotificationService
{
    private const int MaxToastsPerMinute = 4;
    private const int MinutesPerDay = 24 * 60;
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

    /// <summary>Raised on the caller's thread when the snooze starts or ends.</summary>
    public event Action? SnoozeChanged;

    /// <summary>False in demo, render and self-check runs, where nothing may reach the user's screen.</summary>
    public bool Enabled { get; set; } = true;

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

    public bool IsSnoozed => Clock.UtcNow < SnoozeUntilUtc;

    /// <summary>True while quiet hours are on and the local time is inside them.</summary>
    public bool IsQuietTime
    {
        get
        {
            if (!_settings.QuietHoursEnabled)
            {
                return false;
            }

            DateTime local = Clock.UtcNow.ToLocalTime();
            return IsWithinQuietHours(_settings.QuietHoursStart, _settings.QuietHoursEnd, local.Hour * 60 + local.Minute);
        }
    }

    /// <summary>
    /// Whether <paramref name="nowMinutes"/> (minutes after midnight) falls in the window from
    /// <paramref name="startMinutes"/> to <paramref name="endMinutes"/>. A window that crosses midnight
    /// works; a window whose start equals its end is empty (quiet hours off).
    /// </summary>
    public static bool IsWithinQuietHours(int startMinutes, int endMinutes, int nowMinutes)
    {
        int start = Mod(startMinutes);
        int end = Mod(endMinutes);
        int now = Mod(nowMinutes);
        if (start == end)
        {
            return false;
        }

        return start < end ? now >= start && now < end : now >= start || now < end;

        static int Mod(int value) => ((value % MinutesPerDay) + MinutesPerDay) % MinutesPerDay;
    }

    /// <summary>Silences toasts for a while; null resumes them. Events are still recorded.</summary>
    public void Snooze(TimeSpan? duration)
    {
        DateTime until = duration is null ? DateTime.MinValue : Clock.UtcNow + duration.Value;
        SnoozeUntil(until);
    }

    /// <summary>Silences toasts until a moment (UTC); <see cref="DateTime.MinValue"/> resumes them.</summary>
    public void SnoozeUntil(DateTime untilUtc)
    {
        lock (_gate)
        {
            _snoozeUntilUtc = untilUtc;
        }

        _state.Update(s => s.SnoozeUntilUtc = untilUtc);
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
            if (Clock.UtcNow < _snoozeUntilUtc || IsSuppressedLocked(item, now))
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
                Title = Strings.ToastBurstTitle,
                Line1 = Strings.ToastBurstLine1,
                Line2 = Strings.ToastBurstLine2,
                Attribution = Strings.AppName,
                LaunchUri = AppLinks.ForPage("Events"),
                Actions = [new ToastAction(Strings.ActionReviewEvents, AppLinks.ForPage("Events"))],
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
            .Select(p => Strings.ToastMissedPart(p.count, p.tier)));
        DiskEvent newest = missed.MaxBy(e => e.TimeUtc)!;

        Show(new ToastRequest
        {
            Title = Strings.ToastMissedTitle(missed.Count, firstRun),
            Line1 = breakdown,
            Line2 = Strings.ToastLatest(newest.Title, newest.DeviceText),
            Attribution = Strings.AppName,
            LaunchUri = AppLinks.ForPage("Events"),
            Actions = [new ToastAction(Strings.ActionReviewEvents, AppLinks.ForPage("Events"))],
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
            Title = Strings.ToastScanTitle(volume, clean),
            Line1 = summary,
            Attribution = Strings.AttributionCheckDisk,
            LaunchUri = AppLinks.ForPage("CheckDisk"),
            Actions = [new ToastAction(Strings.ActionOpenCheckDisk, AppLinks.ForPage("CheckDisk"))],
            Silent = clean || !_settings.High.Sound,
            Tag = "scan-" + volume.TrimEnd(':'),
            Group = "scans",
            Tier = clean ? null : Tier.High
        });
    }

    /// <summary>Offers a scan when one is wanted but cannot run without an administrator prompt.</summary>
    public void NotifyScanWanted(string volume, string reason)
    {
        if (IsSnoozed)
        {
            return;
        }

        Show(new ToastRequest
        {
            Title = Strings.ToastScanWantedTitle(volume),
            Line1 = reason,
            Line2 = Strings.ToastScanWantedLine2,
            Attribution = Strings.AttributionCheckDisk,
            LaunchUri = AppLinks.ForScan(volume),
            Actions =
            [
                new ToastAction(Strings.ActionScanVolume(volume), AppLinks.ForScan(volume)),
                new ToastAction(Strings.ActionNotNow, "", Dismiss: true)
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
            Title = Strings.ToastRoutineTitle,
            Line1 = Strings.ToastRoutineLine1,
            Attribution = Strings.AttributionCheckDisk,
            LaunchUri = AppLinks.ForPage("CheckDisk"),
            Actions = [new ToastAction(Strings.ActionOpenCheckDisk, AppLinks.ForPage("CheckDisk"))],
            Silent = true,
            Tag = "routine",
            Group = "scans"
        });
    }

    public void NotifyUpdateAvailable(string version)
    {
        Show(new ToastRequest
        {
            Title = Strings.ToastUpdateTitle(version),
            Line1 = Strings.ToastUpdateLine1,
            Attribution = Strings.AppName,
            LaunchUri = AppLinks.ForPage("Settings"),
            Actions = [new ToastAction(Strings.ActionOpenSettings, AppLinks.ForPage("Settings"))],
            Silent = true,
            Tag = "update",
            Group = "app"
        });
    }

    /// <summary>Shows a sample toast for a tier, ignoring snooze and quiet hours, so its options can be tried out.</summary>
    public void SendTest(Tier tier)
    {
        TierOptions options = _settings.OptionsFor(tier);
        Show(new ToastRequest
        {
            Title = Strings.ToastTestTitle(tier),
            Line1 = Strings.ToastTestLine1,
            Line2 = Strings.ToastTestLine2(tier),
            Attribution = Strings.AttributionTest,
            LaunchUri = AppLinks.ForPage("Settings"),
            Actions = [new ToastAction(Strings.ActionOpenApp, AppLinks.ForPage("Overview"))],
            Silent = !options.Sound,
            Sticky = options.Sticky,
            Tag = "test",
            Group = "app",
            Tier = tier,
            IgnoreQuietHours = true
        });
    }

    public ToastSetting QueryWindowsSetting() => Enabled ? ToastInterop.QuerySetting(AppPaths.AppUserModelId) : ToastSetting.Unknown;

    /// <summary>
    /// The toast for one event. Line 1 names the device (or the kind of problem), line 2 says what to
    /// do. The event message never appears: it can hold file names and paths.
    /// </summary>
    internal static ToastRequest BuildEventToast(DiskEvent item, int repeats, TierOptions options)
    {
        List<ToastAction> actions = [new(Strings.ActionViewDetails, AppLinks.ForEvent(item.Key))];
        if (item.SuggestScan && item.Volume is not null)
        {
            actions.Add(new ToastAction(Strings.ActionScanVolume(item.Volume), AppLinks.ForScan(item.Volume)));
        }

        return new ToastRequest
        {
            Title = Strings.ToastEventTitle(item.Tier, item.Title, repeats),
            Line1 = item.DeviceText.Length > 0 ? item.DeviceText : item.Kind.Label(),
            Line2 = Format.SingleLine(item.Advice, 180),
            Attribution = item.Synthetic ? Strings.AttributionHealthCheck : Strings.AttributionLog(item.Log, item.Provider, item.EventId),
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

        // During quiet hours a toast lands silently in the notification centre without popping up.
        // A Critical one still pops up when the user allows it. Balloons always pop up, so they are skipped.
        bool quiet = !request.IgnoreQuietHours && IsQuietTime
            && !(request.Tier == Tier.Critical && _settings.QuietHoursAllowCritical);
        if (quiet)
        {
            request = request with { Silent = true, Sticky = false };
        }

        _postToUi(() =>
        {
            try
            {
                int hr = ToastInterop.Show(AppPaths.AppUserModelId, BuildXml(request), request.Tag, request.Group, suppressPopup: quiet);
                if (hr >= 0)
                {
                    LastError = null;
                    return;
                }

                LastError = Strings.ToastRejected(hr);
                ErrorLog.Write("Toast", LastError);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                ErrorLog.Write("Toast", ex);
            }

            if (!quiet)
            {
                string body = string.Join(Environment.NewLine, new[] { request.Line1, request.Line2 }.Where(line => line.Length > 0));
                _ = BalloonFallback?.Invoke(request.Title, body, request.Tier);
            }
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
