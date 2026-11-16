using System.Globalization;
using DiskMonitor.Core;
using DiskMonitor.Core.Drives;
using DiskMonitor.Core.Events;

namespace DiskMonitor.App;

/// <summary>
/// Every user-facing string the app produces outside the rule catalog, exactly as the plans write
/// them. Strings with values are methods, so plurals and word order live in one place. A test fails
/// on any user-facing literal outside this file and <c>Core/Events/EventRules.cs</c>.
/// </summary>
internal static class Strings
{
    // ---------------------------------------------------------------------------------------------
    // Product and shared pieces
    // ---------------------------------------------------------------------------------------------

    public const string AppName = "Disk Monitor";
    public const string Separator = " · ";
    public const string Ellipsis = "…";
    public const string Dash = "—";

    public static readonly string[] SizeUnits = ["B", "KB", "MB", "GB", "TB", "PB"];
    public const string DegreesSuffix = " °C";

    public static string MonitorReadFailed(string log, string reason) => $"Could not read the {log} log: {reason}";

    public static string MonitorWatchFailed(string log, string reason) => $"Could not watch the {log} log: {reason}";

    public const string Today = "Today";
    public const string TodayLower = "today";
    public const string Yesterday = "Yesterday";
    public const string YesterdayLower = "yesterday";
    public const string DayMonthPattern = "d MMM";
    public const string DayMonthYearPattern = "d MMM yyyy";

    public static string DurationSeconds(int seconds) => $"{seconds} s";

    public static string DurationMinutes(int minutes) => $"{minutes} min";

    public static string DurationHours(int hours, int minutes) => minutes == 0 ? $"{hours} h" : $"{hours} h {minutes} min";

    public static string About(string duration) => "About " + duration;

    public static string AgeHours(long hours) => $"{hours} h";

    public static string AgeDays(long days) => Format.Number(days) + " days";

    public static string AgeYears(double years) => years.ToString("0.0", CultureInfo.CurrentCulture) + " years";

    public const string SwitchOn = "On";
    public const string SwitchOff = "Off";

    public const string TestEventMessage = "This is a test event raised with --simulate. It was not logged by Windows and disappears at the next history reload.";

    public static string Events(long count) => Format.Count(count, "event", "events");

    public static string Drives(long count) => Format.Count(count, "drive", "drives");

    // ---------------------------------------------------------------------------------------------
    // Tiers, kinds, health
    // ---------------------------------------------------------------------------------------------

    public static string TierName(Tier tier) => tier switch
    {
        Tier.Critical => "Critical",
        Tier.High => "High",
        Tier.Medium => "Medium",
        _ => "Low"
    };

    public static string KindName(ErrorKind kind) => kind switch
    {
        ErrorKind.Physical => "Physical",
        ErrorKind.Logical => "File system",
        ErrorKind.Health => "Drive health",
        ErrorKind.Capacity => "Capacity",
        _ => "Other"
    };

    public static string HealthName(HealthStatus health) => health switch
    {
        HealthStatus.Good => "Healthy",
        HealthStatus.Caution => "Caution",
        HealthStatus.Bad => "Failing",
        _ => "No health data"
    };

    // ---------------------------------------------------------------------------------------------
    // Drives and devices
    // ---------------------------------------------------------------------------------------------

    public static string BusName(BusType bus) => bus switch
    {
        BusType.Nvme => "NVMe",
        BusType.Sata => "SATA",
        BusType.Ata => "ATA",
        BusType.Usb => "USB",
        BusType.Sas => "SAS",
        BusType.Scsi => "SCSI",
        BusType.Raid => "RAID",
        BusType.Sd => "SD card",
        BusType.Mmc => "MMC",
        BusType.Iscsi => "iSCSI",
        BusType.Spaces => "Storage Space",
        BusType.Virtual or BusType.FileBackedVirtual => "Virtual",
        BusType.Ufs => "UFS",
        BusType.Ieee1394 => "FireWire",
        BusType.Fibre => "Fibre Channel",
        _ => "Disk"
    };

    public static string MediaName(MediaKind media, bool removable) => media switch
    {
        MediaKind.Ssd => "SSD",
        MediaKind.Hdd => "HDD",
        _ => removable ? "Removable" : ""
    };

    public static string DiskName(int number) => $"Disk {number}";

    public static string VolumeName(string letter, string label) => string.IsNullOrWhiteSpace(label) ? letter : $"{letter} {label}";

    public static string DeviceVolumeOnDisk(string letter, int disk) => $"{letter} · Disk {disk}";

    public static string DeviceVolumeOnNamedDisk(string letter, string name, int disk) => $"{letter} · {name} (Disk {disk})";

    public static string DeviceDiskMissing(int disk) => $"Disk {disk} (not connected)";

    public static string DeviceDisk(int disk, string name) => $"Disk {disk} · {name}";

    public static string DeviceDiskWithLetters(int disk, string name, string letters) => $"Disk {disk} · {name} ({letters})";

    public static string DeviceVolume(string letter) => $"Volume {letter}";

    // Health notes (subplan 06, section 3)
    public const string NoteFailurePredicted = "The drive predicts its own failure";
    public const string NoteSpareLow = "Spare capacity is below the drive's threshold";
    public const string NoteReliability = "The drive reports degraded reliability";
    public const string NoteReadOnly = "The drive has switched to read-only mode";
    public const string NoteBackupFailed = "The drive's memory backup has failed";
    public const string NoteTemperatureFlag = "The drive reports a temperature outside its limits";

    public static string NoteReallocated(long n) => n == 1 ? "1 reallocated sector" : $"{Format.Number(n)} reallocated sectors";

    public static string NotePending(long n) => n == 1 ? "1 sector pending reallocation" : $"{Format.Number(n)} sectors pending reallocation";

    public static string NoteUncorrectable(long n) => n == 1 ? "1 uncorrectable sector" : $"{Format.Number(n)} uncorrectable sectors";

    public static string NoteMediaErrors(long n) => n == 1 ? "1 media error" : $"{Format.Number(n)} media errors";

    public static string NoteLifeRemaining(int percent) => $"{percent}% of rated life remaining";

    public static string NoteHot(double temperature, int warning) =>
        $"Running at {Format.Temperature(temperature)}, at or above the {warning} °C warning level";

    // Health-check alert messages (the event message of a synthetic event)
    public const string CounterReallocated = "reallocated sectors";
    public const string CounterPending = "pending sectors";
    public const string CounterUncorrectable = "uncorrectable sectors";
    public const string CounterMediaErrors = "media errors";
    public const string CounterCrcErrors = "interface CRC errors";

    public static string AlertCounterRose(string counter, string disk, long previous, long current) =>
        $"{char.ToUpperInvariant(counter[0])}{counter[1..]} on {disk} rose from {Format.Number(previous)} to {Format.Number(current)}.";

    public static string AlertExistingPart(string counter, long value) => $"{counter}: {Format.Number(value)}";

    public static string AlertExisting(string disk, IEnumerable<string> parts) => $"{disk} already reports {string.Join(", ", parts)}.";

    public static string AlertPredict(string disk) => $"{disk} reports that it is about to fail.";

    public static string AlertNvme(string disk, IEnumerable<string> flags) => $"{disk}: {string.Join("; ", flags)}.";

    public const string FlagSpare = "spare capacity has fallen below the drive's threshold";
    public const string FlagReliability = "reliability is degraded";
    public const string FlagReadOnly = "the drive has switched to read-only mode";
    public const string FlagBackup = "the volatile memory backup has failed";

    public static string AlertWear(string disk, int life) => $"{disk} has {life}% of its rated life remaining.";

    public static string AlertHot(string disk, double temperature, int warning) =>
        $"{disk} is at {Format.Temperature(temperature)} (warning level {warning} °C).";

    public static string AlertDirty(string volume) => $"Volume {volume} is marked dirty. Windows will check it at the next restart.";

    public static string AlertLowSpace(string volume, long free, long total, double percent) =>
        $"Volume {volume} has {Format.Size(free)} free of {Format.Size(total)} ({Format.Percent(percent)}).";

    // ---------------------------------------------------------------------------------------------
    // Windows toasts
    // ---------------------------------------------------------------------------------------------

    public const string ToastBurstTitle = "Disk events are arriving rapidly";
    public const string ToastBurstLine1 = "More disk problems are being logged than can be shown one by one.";
    public const string ToastBurstLine2 = "Open Disk Monitor to review them.";
    public const string ActionReviewEvents = "Review events";
    public const string ActionViewDetails = "View details";
    public const string ActionNotNow = "Not now";
    public const string ActionOpenCheckDisk = "Open Check Disk";
    public const string ActionOpenSettings = "Open Settings";
    public const string ActionOpenApp = "Open Disk Monitor";
    public const string AttributionCheckDisk = "Check Disk";
    public const string AttributionHealthCheck = "Disk Monitor · Health check";
    public const string AttributionTest = "Disk Monitor · Test";

    public static string ActionScanVolume(string volume) => $"Scan {volume}…";

    public static string ToastMissedTitle(int count, bool firstRun) => firstRun
        ? $"Found {Format.Count(count, "disk problem", "disk problems")} in recent history"
        : $"{Format.Count(count, "disk problem", "disk problems")} while Disk Monitor was not running";

    public static string ToastMissedPart(int count, Tier tier) => $"{Format.Number(count)} {TierName(tier).ToLowerInvariant()}";

    public static string ToastLatest(string title, string device) => device.Length > 0 ? $"Latest: {title} · {device}" : $"Latest: {title}";

    public static string ToastScanTitle(string volume, bool clean) => clean ? $"Scan of {volume} found no problems" : $"Scan of {volume} needs your attention";

    public static string ToastScanWantedTitle(string volume) => $"Scan {volume} for file system errors?";

    public const string ToastScanWantedLine2 = "The scan is read-only and needs administrator approval.";

    public static string ScanWantedReason(string title) => $"{title} was reported on this volume.";

    public const string ToastRoutineTitle = "Routine disk scan is due";
    public const string ToastRoutineLine1 = "Open Check Disk to scan your volumes for file system errors.";

    public static string ToastUpdateTitle(string version) => $"Disk Monitor {version} is available";

    public const string ToastUpdateLine1 = "Open Settings to download and install the update.";

    public static string ToastTestTitle(Tier tier) => $"{TierName(tier)}: Test notification";

    public const string ToastTestLine1 = "Disk 0 · Example drive (C:)";

    public static string ToastTestLine2(Tier tier) => $"This is how a {TierName(tier).ToLowerInvariant()} disk alert looks and sounds.";

    public static string ToastEventTitle(Tier tier, string title, int repeats) =>
        repeats > 0 ? $"{TierName(tier)}: {title} (+{Format.Number(repeats)} more)" : $"{TierName(tier)}: {title}";

    public static string AttributionLog(string log, string provider, int eventId) => $"{log} log · {provider} {eventId}";

    public static string ToastRejected(int hr) => $"Windows rejected the notification (0x{hr:X8}).";

    // ---------------------------------------------------------------------------------------------
    // CHKDSK
    // ---------------------------------------------------------------------------------------------

    public static string ModeName(Core.Chkdsk.ChkdskMode mode) => mode switch
    {
        Core.Chkdsk.ChkdskMode.Scan => "Scan",
        Core.Chkdsk.ChkdskMode.SpotFix => "Spot fix",
        Core.Chkdsk.ChkdskMode.Fix => "Repair",
        _ => "Repair and scan for bad sectors"
    };

    public const string ScanStarting = "Starting…";
    public const string ScanRunningText = "Running…";
    public const string ScanWaitingApproval = "Waiting for administrator approval…";
    public const string ScanCancelledSummary = "The scan was cancelled.";
    public const string SummaryScheduled = "The volume is in use, so the check is scheduled for the next restart.";
    public const string SummaryScanClean = "No problems found.";
    public const string SummaryScanProblems = "Problems were found. Review the output, then run a repair.";
    public const string SummaryRepairClean = "Completed. No errors were found.";
    public const string SummaryRepairFixed = "Completed. Errors were found and fixed.";
    public const string SummaryRepairCleanupSkipped = "Completed, but disk cleanup was skipped.";
    public const string SummaryRepairFailed = "CHKDSK could not finish the repair. Review the output.";
    public const string ErrorDeclined = "Administrator approval was declined, so nothing was changed.";
    public const string ErrorHelperStopped = "The scan helper stopped before CHKDSK finished.";
    public const string ErrorHelperNoStart = "The scan helper could not start. Administrator rights are required to run CHKDSK.";
    public const string ErrorHelperTimeout = "The scan helper did not respond in time.";
    public const string ErrorRefs = "ReFS volumes repair themselves online and cannot be checked with CHKDSK.";
    public const string ErrorSpotFixNtfs = "Spot fix is only available on NTFS volumes.";
    public const string ErrorNotDriveLetter = "That is not a drive letter.";
    public const string ErrorChkdskNoStart = "CHKDSK could not be started.";
    public const string ErrorRequestNotUnderstood = "The scan request was not understood.";
    public const string ErrorCheckNotSupported = "The requested check is not supported.";
    public const string ErrorCancelled = "Cancelled.";

    public static string ErrorVolumeUnavailable(string letter) => $"Volume {letter} is not available.";

    // ---------------------------------------------------------------------------------------------
    // Shell (subplan 03)
    // ---------------------------------------------------------------------------------------------

    public static string WindowTitle(string version) => $"Disk Monitor {version}";

    public const string ErrorDialogTitle = "Something went wrong";
    public const string ErrorDialogText = "Disk Monitor will keep running. Details were saved to the log.";
    public const string ErrorDialogOpenLog = "Open log";
    public const string ButtonClose = "Close";

    public const string PageOverview = "Overview";
    public const string PageEvents = "Events";
    public const string PageDrives = "Drives";
    public const string PageCheckDisk = "Check Disk";
    public const string PageSettings = "Settings";
    public const string NavUpdateAvailable = "Update available";
    public const string BadgeOverflow = "999+";

    public static string TraySnoozed(DateTime untilUtc) => $"Disk Monitor · notifications snoozed until {SnoozeTime(untilUtc)}";

    public static string TrayUnread(int total, int critical) =>
        critical > 0 ? $"Disk Monitor · {Format.Number(total)} unread ({Format.Number(critical)} critical)" : $"Disk Monitor · {Format.Number(total)} unread";

    public const string TrayFailing = "Disk Monitor · a drive is failing";
    public const string TrayAttention = "Disk Monitor · a drive needs attention";
    public const string TrayLimited = "Disk Monitor · monitoring is limited";
    public const string TrayAllClear = "Disk Monitor · all clear";

    public const string MenuOpen = "Open Disk Monitor";

    public static string MenuUnread(int total, int critical) =>
        critical > 0 ? $"{Format.Count(total, "unread alert", "unread alerts")} ({Format.Number(critical)} critical)" : Format.Count(total, "unread alert", "unread alerts");

    public const string MenuNoUnread = "No unread alerts";
    public const string MenuMarkAllRead = "Mark all as read";
    public const string MenuSnooze = "Snooze notifications";
    public const string SnoozeHour = "For 1 hour";
    public const string SnoozeFourHours = "For 4 hours";
    public const string SnoozeTomorrow = "Until tomorrow 08:00";
    public const string MenuResume = "Resume notifications";
    public const string MenuCheckDisk = "Check Disk…";

    public static string MenuInstall(string version) => $"Install version {version}…";

    public const string MenuCheckUpdates = "Check for updates";
    public const string MenuExit = "Exit Disk Monitor";

    public const string TrayHintTitle = "Disk Monitor is still running";
    public const string TrayHintText = "It keeps watching your drives from the notification area. Right-click the icon to exit.";

    public static string StripScanning(string volume, int? percent, TimeSpan? left, bool repair, bool narrow)
    {
        string verb = repair ? $"Repairing {volume}" : $"Scanning {volume}";
        if (percent is null)
        {
            return verb;
        }

        string text = $"{verb} · {percent}%";
        return narrow || left is null ? text : $"{text} · about {Format.Duration(left.Value, estimate: false)} left";
    }

    public static string StripWaiting(string volume) => $"Waiting for administrator approval to scan {volume}…";

    public const string StripReading = "Reading event history…";
    public const string ButtonStop = "Stop";
    public const string StopDialogTitle = "Stop this scan?";
    public const string StopDialogText = "Nothing on the disk was changed. You can scan again at any time.";
    public const string StopDialogConfirm = "Stop scan";
    public const string StopDialogCancel = "Keep scanning";
    public const string PlaceholderPage = "This page is being built.";

    // Results of a CHKDSK run (subplan 07, result info bars). Volumes include the colon, as in "C:".
    public static string ScanFinishedToast(string volume) => $"Scan of {volume} finished";

    public static string ResultClean(string volume) => $"{volume} no problems found.";

    public static string ResultProblems(string volume) => $"{volume} problems found. A repair can fix them.";

    public static string ResultFixed(string volume) => $"{volume} errors were found and fixed.";

    public static string ResultScheduled(string volume) => $"{volume} the repair runs at the next restart.";

    public static string ResultCancelled(string volume) => $"{volume} the scan was stopped. Nothing was changed.";

    public static string ResultFailed(string volume, string reason) => $"{volume} the scan could not run. {reason}";

    /// <summary>Time of day for a snooze end: the time, with the weekday when it is not today.</summary>
    public static string SnoozeTime(DateTime untilUtc)
    {
        DateTime local = untilUtc.ToLocalTime();
        DateTime now = Clock.UtcNow.ToLocalTime();
        string time = local.ToString("t", CultureInfo.CurrentCulture);
        return local.Date == now.Date ? time : Format.RecentDate(untilUtc, inSentence: true);
    }

    // ---------------------------------------------------------------------------------------------
    // Overview (subplan 04)
    // ---------------------------------------------------------------------------------------------

    public const string OverviewRefreshTip = "Reload events and drive health";

    public static string InfoLimited(string error) => $"Monitoring is limited: {error}";

    public static string InfoSnoozed(DateTime untilUtc) => $"Notifications are snoozed until {SnoozeTime(untilUtc)}.";

    public const string InfoResume = "Resume";

    public static string InfoReadingHistory(int days) => $"Reading the last {days} days of disk events…";

    public const string CardDrives = "DRIVES";
    public const string CardEvents = "EVENTS";
    public const string CardCheckDisk = "CHECK DISK";
    public const string CardByDrive = "BY DRIVE";
    public const string CardNextSteps = "RECOMMENDED NEXT STEPS";
    public const string CardRecentEvents = "RECENT EVENTS";

    public static string DrivesOfHealthy(int ok, int total) => $"{ok} of {total}";

    public const string DrivesHealthySuffix = " healthy";

    public static string DrivesNeedAttention(int n) => n == 1 ? "1 drive needs attention" : $"{n} drives need attention";

    public static string DrivesNoHealthData(int n) => n == 1 ? "1 drive reports no health data" : $"{n} drives report no health data";

    public const string DrivesNoneReport = "No drive reports its health";
    public const string EventsUnreadSuffix = " unread";
    public const string EventsNoNewProblems = "No new problems";

    public static string EventsLatest(string title, DateTime utc) => $"Latest: {title}{Separator}{Format.RecentDate(utc, inSentence: false)}";

    public static string EventsInDays(int n, int days) => $"{Events(n)} in {days} days";

    public const string CheckDiskLast = "Last";
    public const string CheckDiskNext = "Next";

    public static string CheckDiskLastValue(string volume, bool clean, DateTime utc) =>
        $"{volume} {(clean ? "clean" : "problems found")}{Separator}{Format.RecentDate(utc, inSentence: false)}";

    public const string CheckDiskNeverScanned = "No volume has been scanned yet";

    public static string CheckDiskNextValue(DateTime utc) => $"{Format.RecentDate(utc, inSentence: false)}{Separator}routine scan";

    public const string CheckDiskNoRoutine = "No routine scans";

    public static string TilePlus24(int n) => n == 0 ? "None in 24 h" : $"+{Format.Number(n)} in 24 h";

    public static string DriveRowName(string name, string letters) => letters.Length == 0 ? name : $"{name} ({letters})";

    public static string DriveRowCount(int n) => n == 0 ? "No events" : Events(n);

    public static string SegmentTip(Tier tier, int count) => $"{TierName(tier)}: {Format.Number(count)}";

    public const string NothingToDo = "Nothing needs doing right now.";

    public static string RecFailingTitle(string name) => $"{name} reports that it is failing";

    public const string RecFailingDetail = "Back up its data now";
    public const string RecOpenDrive = "Open drive";

    public static string RecCriticalTitle(int n) => n == 1 ? "1 critical event needs attention" : $"{Format.Number(n)} critical events need attention";

    public static string RecLatest(string title, string device) => device.Length > 0 ? $"Latest: {title}{Separator}{device}" : $"Latest: {title}";

    public const string RecReview = "Review";

    public static string RecDamageTitle(string volume) => $"Windows reported file system damage on {volume}";

    public const string RecDamageDetail = "A read-only scan shows what CHKDSK would repair";

    public static string RecScanButton(string volume) => $"Scan {volume}…";

    public static string RecHighTitle(int n) => n == 1 ? "1 high-severity event to review" : $"{Format.Number(n)} high-severity events to review";

    public static string RecCautionTitle(string name, string note) => $"{name}: {note}";

    public const string RecCautionDetail = "Keep its backups current";
    public const string RecBlockedTitle = "Windows is blocking Disk Monitor's notifications";
    public const string RecBlockedDetail = "Alerts cannot reach you until they are allowed";
    public const string RecOpenSettings = "Open settings";
    public const string RecStartupTitle = "Disk Monitor does not start with Windows";
    public const string RecStartupDetail = "It only watches while it runs";
    public const string RecTurnOn = "Turn on";
    public const string RecStartupToast = "Disk Monitor now starts with Windows";
    public const string RecNoScanTitle = "No volume has been scanned in 30 days";
    public const string RecNoScanDetail = "Routine scans can do this on their own";
    public const string RecCheckDisk = "Check Disk";

    public static string RecentPerDay(int days) => $"Events per day, last {days} days";

    public static string RecentNone(int days) => $"No events in {days} days.";

    public const string Undo = "Undo";

    // ---------------------------------------------------------------------------------------------
    // Events (subplan 05)
    // ---------------------------------------------------------------------------------------------

    public const string EventsRefreshTip = "Reload from the Windows logs";
    public const string EventsExport = "Export";
    public const string EventsMarkAllRead = "Mark all as read";
    public const string EventsNothingUnread = "Nothing is unread";
    public const string ChipAll = "All";
    public const string DropDrive = "Drive";
    public const string DropAllDrives = "All drives";

    public static string DropDisk(int disk, string model) => model.Length > 0 ? $"Disk {disk} · {model}" : $"Disk {disk}";

    public const string DropShow = "Show";
    public const string ShowAll = "All events";
    public const string ShowUnread = "Unread only";
    public const string DropRange = "Range";
    public const string Range24 = "Last 24 hours";
    public const string Range7 = "Last 7 days";
    public const string Range30 = "Last 30 days";
    public const string RangeAll = "All loaded";
    public const string DropRepeats = "Repeats";
    public const string RepeatsCombine = "Combine repeats";
    public const string RepeatsEach = "Show each event";
    public const string GroupBy = "Group by";
    public const string GroupTier = "Tier";
    public const string GroupDrive = "Drive";
    public const string GroupNone = "None";

    public static string GroupLabel(string value) => $"Group: {value}";

    public const string DropKind = "Kind";
    public const string KindAll = "All kinds";

    public static string ShowingOf(int shown, int total) => $"Showing {Format.Number(shown)} of {Format.Number(total)}";

    public const string ClearFilters = "Clear filters";
    public const string SearchEvents = "Search events";
    public const string NoDrive = "No drive";

    public static string RowDevice(string device, string source, int eventId) => $"{device}{Separator}{source} {eventId}";

    public static string RowHealthCheck(string device) => device.Length > 0 ? $"{device}{Separator}Health check" : "Health check";

    public static string RowCount(int n) => $"×{Format.Number(n)}";

    public const string RowDetails = "Details";
    public const string MoreActions = "More actions";

    public static string NewEventsPill(int n) => $"{Format.Count(n, "new event", "new events")}{Separator}Show";

    public static string SelectedCount(int n) => $"{Format.Number(n)} selected";

    public const string MarkAsRead = "Mark as read";
    public const string Copy = "Copy";
    public const string EmptyNoEventsTitle = "No disk problems found";

    public static string EmptyNoEventsText(int days) => $"Monitoring is live. Nothing was logged in the last {days} days.";

    public const string EmptyFilteredTitle = "Nothing matches these filters";
    public const string DrawerClose = "Close (Esc)";
    public const string OpenInEventViewer = "Open in Event Viewer";
    public const string WhatThisMeans = "What this means";
    public const string WhyItMattersLabel = "Why it matters";
    public const string WhatYouCanDo = "What you can do";
    public const string EventMessage = "Event message";

    public static string HistoryRow(int n, DateTime sinceUtc) => $"History{Separator}{n} times since {Format.RecentDate(sinceUtc, inSentence: true)}";

    public const string ShortcutEnter = "Enter";
    public const string ShortcutCtrlEnter = "Ctrl+Enter";
    public const string ShortcutCtrlI = "Ctrl+I";
    public const string ShortcutCtrlC = "Ctrl+C";
    public const string TechnicalDetails = "Technical details";
    public const string TechSource = "Source";
    public const string TechEventId = "Event ID";
    public const string TechLog = "Log";
    public const string TechLevel = "Level";
    public const string TechRecordId = "Record ID";
    public const string TechRule = "Rule";
    public const string CopyDetails = "Copy details";
    public const string Ignore = "Ignore";
    public const string ChangeTier = "Change tier";
    public const string IgnoreThisKind = "Ignore this kind of event";
    public const string ShowOnlyThisDrive = "Show only this drive";

    public static string UseDefaultTier(Tier tier) => $"Use default ({TierName(tier)})";

    public static string LevelName(int level) => level switch
    {
        1 => "Critical",
        2 => "Error",
        3 => "Warning",
        4 or 0 => "Information",
        _ => "Verbose"
    };

    public static string ToastIgnored(string title) => $"Ignored \"{title}\"";

    public static string ToastIgnoredText(int n) => $"{Events(n)} hidden. Settings › Rules can bring them back.";

    public static string ToastMoved(string title, Tier tier) => $"Moved \"{title}\" to {TierName(tier)}";

    public static string ToastMovedDefault(string title) => $"Moved \"{title}\" back to its default tier";

    public static string ToastMovedText(int n) => $"{Events(n)} changed tier.";

    public static string ToastMarkedRead(int n) => $"Marked {Events(n)} as read";

    public const string ToastCopied = "Copied";

    public static string ToastExported(int n) => $"Exported {Events(n)}";

    public const string OpenFolder = "Open folder";
    public const string ToastExportFailed = "Export failed";

    public static string ToastExportFailedText(string reason) => $"{reason} Choose another folder.";

    public const string ExportFilter = "CSV files (*.csv)|*.csv";

    public static string ExportFileName(DateTime localNow) => $"DiskMonitor-events-{localNow:yyyyMMdd-HHmm}.csv";

    public static string CopyHeader(Tier tier, string title) => $"{TierName(tier)}: {title}";

    public static string CopyDevice(string device) => $"Device: {device}";

    public static string CopySource(string source, int eventId, string log) => $"Source: {source} event {eventId} ({log} log)";

    public static string CopyOccurrences(int n) => $"Occurrences: {Format.Number(n)}";

    public static string CopyLabelled(string label, string text) => $"{label}: {text}";

    public static readonly string[] CsvColumns = ["Time", "Tier", "Kind", "Title", "Device", "Volume", "Disk", "Log", "Source", "EventId", "RecordId", "Rule", "Message"];

    // ---------------------------------------------------------------------------------------------
    // Updates (subplan 08, Updates)
    // ---------------------------------------------------------------------------------------------

    public const string UpdateNeverChecked = "Update status has not been checked.";
    public const string UpdateChecking = "Checking…";
    public const string UpdateUpToDate = "Disk Monitor is up to date.";
    public const string UpdateUnverified = "Update found, but it could not be verified. Nothing was installed.";
    public const string UpdateNoRelease = "No release has been published yet.";
    public const string UpdateTimedOut = "The request timed out.";
    public const string UpdateEmptyResponse = "GitHub returned an empty release response.";
    public const string UpdateLatestNone = "No release";
    public const string UpdateLatestUnknown = "Unknown";
    public const string UpdateCheckFirst = "Check for updates first.";
    public const string UpdateNothingToDownload = "No downloadable update is available.";
    public const string UpdateNoInstaller = "No signed installer is available.";
    public const string UpdateFileMissing = "The downloaded update file was not found.";
    public const string UpdateMetadataIncomplete = "The update information is incomplete.";
    public const string UpdateSizeMismatch = "The downloaded update does not match its signed size. Nothing was installed.";
    public const string UpdateHashMismatch = "The downloaded update does not match its signed checksum. Nothing was installed.";

    public static string UpdateAvailable(string version) => $"Version {version} is available.";

    public static string UpdateDownloading(double percent) => $"Downloading {Format.Percent(percent)}";

    public static string UpdateError(string reason) => $"Could not check for updates. {reason}";

    public static string UpdateBadTag(string tag) => $"The latest release tag {tag} is not a version.";
}
