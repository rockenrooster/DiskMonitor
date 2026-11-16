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
