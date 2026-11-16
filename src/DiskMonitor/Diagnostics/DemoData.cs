using DiskMonitor.App;
using DiskMonitor.Core;
using DiskMonitor.Core.Drives;
using DiskMonitor.Core.Events;

namespace DiskMonitor.Diagnostics;

/// <summary>
/// Invented data for demo mode and the screen harness (subplan 09, part B): four drives and a week of
/// events built from the real rule catalog, so demo wording always matches real wording. The same seed
/// gives the same data; nothing here describes a real machine.
/// </summary>
internal static class DemoData
{
    private const long Gb = 1024L * 1024 * 1024;
    private const long Tb = 1024L * Gb;

    public static void Populate(AppServices services, int seed = 20261116)
    {
        DateTime now = Clock.UtcNow;
        DriveSnapshot drives = Drives(services.Settings.TemperatureWarningC, now);
        services.Drives.SetSnapshot(drives);

        Random random = new(seed);
        List<DiskEvent> events = [];
        long record = 48_210;

        DiskEvent Log(string ruleId, int eventId, string provider, int level, DateTime time, string message, int? disk, string? volume)
        {
            EventRule rule = RuleCatalog.Find(ruleId) ?? Fallback(ruleId, provider, eventId, level);
            record += random.Next(3, 40);
            PhysicalDisk? physical = disk is null ? null : drives.FindDisk(disk.Value);
            return new DiskEvent
            {
                Key = $"System|{record}|{time.Ticks}",
                TimeUtc = time,
                Tier = RuleCatalog.ResolveTier(rule, services.Settings.RuleOverrides, out _) ?? rule.Tier,
                Kind = rule.Kind,
                RuleId = rule.Id,
                Title = rule.Title,
                Message = message,
                Explanation = rule.Explanation,
                WhyItMatters = RuleCatalog.WhyItMatters(rule),
                Advice = rule.Advice,
                Log = "System",
                Provider = provider,
                EventId = eventId,
                RecordId = record,
                Level = level,
                DiskNumber = disk,
                Volume = volume,
                DeviceText = DeviceResolver.Describe(disk, physical, volume),
                SuggestScan = rule.SuggestScan && volume is not null
            };
        }

        DiskEvent Health(string ruleId, DateTime time, string message, int disk)
        {
            EventRule rule = RuleCatalog.Find(ruleId)!;
            PhysicalDisk? physical = drives.FindDisk(disk);
            return new DiskEvent
            {
                Key = $"health|{ruleId}|{physical?.Key}|{time.Ticks}",
                TimeUtc = time,
                Tier = rule.Tier,
                Kind = rule.Kind,
                RuleId = rule.Id,
                Title = rule.Title,
                Message = message,
                Explanation = rule.Explanation,
                WhyItMatters = RuleCatalog.WhyItMatters(rule),
                Advice = rule.Advice,
                Log = Core.Drives.DriveMonitor.HealthLog,
                Provider = Core.Drives.DriveMonitor.HealthProvider,
                DiskNumber = disk,
                DeviceText = DeviceResolver.Describe(disk, physical, null),
                Synthetic = true
            };
        }

        // disk:7, bad block on disk 1: 2 h, 1 day and 3 days ago.
        foreach (TimeSpan ago in new[] { TimeSpan.FromHours(2), TimeSpan.FromDays(1), TimeSpan.FromDays(3) })
        {
            events.Add(Log("disk:7", 7, "disk", 2, now - ago - TimeSpan.FromMinutes(random.Next(0, 50)), @"The device, \Device\Harddisk1\DR1, has a bad block.", 1, null));
        }

        // ntfs:55, corruption on D: a day ago.
        events.Add(Log("ntfs:55", 55, "Microsoft-Windows-Ntfs", 2, now - TimeSpan.FromDays(1) - TimeSpan.FromMinutes(22), "A corruption was discovered in the file system structure on volume D:.", 1, "D:"));

        // disk:51, 41 paging errors over the last five days.
        for (int i = 0; i < 41; i++)
        {
            DateTime time = now - TimeSpan.FromMinutes(random.Next(30, 5 * 24 * 60));
            events.Add(Log("disk:51", 51, "disk", 3, time, @"An error was detected on device \Device\Harddisk1\DR1 during a paging operation.", 1, null));
        }

        // smart:reallocated, a health check 5 h ago.
        PhysicalDisk wd = drives.FindDisk(1)!;
        events.Add(Health("smart:reallocated", now - TimeSpan.FromHours(5), Strings.AlertCounterRose(Strings.CounterReallocated, wd.DisplayName, 6, 8), 1));

        // disk:153, 7 retries over six days.
        for (int i = 0; i < 7; i++)
        {
            DateTime time = now - TimeSpan.FromMinutes(random.Next(60, 6 * 24 * 60));
            events.Add(Log("disk:153", 153, "disk", 3, time, $"The IO operation at logical block address 0x{random.Next(0x100000, 0x7FFFFFFF):x} for Disk 1 (PDO name: \\Device\\00000034) was retried.", 1, null));
        }

        // port:129, two resets four days ago, no device.
        for (int i = 0; i < 2; i++)
        {
            events.Add(Log("port:129", 129, "storahci", 3, now - TimeSpan.FromDays(4) - TimeSpan.FromMinutes(7 * i + 3), @"Reset to device, \Device\RaidPort0, was issued.", null, null));
        }

        // health:temperature on disk 2, six days ago.
        PhysicalDisk crucial = drives.FindDisk(2)!;
        events.Add(Health("health:temperature", now - TimeSpan.FromDays(6) - TimeSpan.FromHours(3), Strings.AlertHot(crucial.DisplayName, 61, 60), 2));

        // ntfs:130, repaired on D: a day ago.
        events.Add(Log("ntfs:130", 130, "Microsoft-Windows-Ntfs", 4, now - TimeSpan.FromDays(1) - TimeSpan.FromMinutes(5), "The file system structure on volume D: has now been repaired.", 1, "D:"));

        // volsnap:36, a fallback rule on C: nine days ago.
        events.Add(Log("volsnap:36", 36, "volsnap", 3, now - TimeSpan.FromDays(9) - TimeSpan.FromHours(2), "The shadow copies of volume C: were aborted because the shadow copy storage could not grow due to a user imposed limit.", 0, "C:"));

        services.Events.ReplaceLogEvents(events, DateTime.MinValue);
        services.Events.MarkRead(events.Where(e => now - e.TimeUtc > TimeSpan.FromDays(2)));

        services.State.Update(s =>
        {
            s.Scans["C:"] = new ScanRecord { TimeUtc = now - TimeSpan.FromDays(3), Mode = "Scan", ExitCode = 0, Clean = true, Summary = Strings.SummaryScanClean, Outcome = "Clean" };
            s.Scans["D:"] = new ScanRecord { TimeUtc = now - TimeSpan.FromHours(2), Mode = "Scan", ExitCode = 3, Clean = false, Summary = Strings.SummaryScanProblems, Outcome = "Problems" };
        });
    }

    /// <summary>The four demo drives, each run through the real health rules.</summary>
    public static DriveSnapshot Drives(int temperatureWarningC, DateTime nowUtc)
    {
        VolumeInfo c = new() { Letter = "C:", Label = "Windows", FileSystem = "NTFS", TotalBytes = (long)(1.82 * Tb), FreeBytes = 612 * Gb, IsSystem = true, DiskNumbers = [0], NtDevice = @"\Device\HarddiskVolume3" };
        VolumeInfo d = new() { Letter = "D:", Label = "Media", FileSystem = "NTFS", TotalBytes = (long)(7.28 * Tb), FreeBytes = (long)(1.1 * Tb), DiskNumbers = [1], NtDevice = @"\Device\HarddiskVolume7" };
        VolumeInfo e = new() { Letter = "E:", Label = "Projects", FileSystem = "NTFS", TotalBytes = 931 * Gb, FreeBytes = 402 * Gb, DiskNumbers = [2], NtDevice = @"\Device\HarddiskVolume9" };
        VolumeInfo f = new() { Letter = "F:", Label = "Backup", FileSystem = "exFAT", TotalBytes = (long)(3.64 * Tb), FreeBytes = (long)(2.0 * Tb), Removable = false, DiskNumbers = [3], NtDevice = @"\Device\HarddiskVolume11" };

        PhysicalDisk samsung = new()
        {
            Number = 0, Model = "Samsung SSD 990 PRO 2TB", Serial = "DEMO-SERIAL-0000", Firmware = "4B2QJXD7", Bus = BusType.Nvme, Media = MediaKind.Ssd,
            SizeBytes = (long)(1.82 * Tb), HealthSupported = true, TemperatureC = 41, LifeRemainingPercent = 97, PowerOnHours = 3_412, PowerCycles = 811,
            BytesWritten = 21.4 * Tb, BytesRead = 18.2 * Tb, MediaErrors = 0, UnsafeShutdowns = 12, NvmeCriticalWarning = 0,
            AvailableSparePercent = 100, SpareThresholdPercent = 10, Volumes = [c]
        };
        PhysicalDisk wd = new()
        {
            Number = 1, Model = "WDC WD80EFZZ-68BTXN0", Serial = "DEMO-SERIAL-0001", Firmware = "81.00A81", Bus = BusType.Sata, Media = MediaKind.Hdd,
            SizeBytes = (long)(7.28 * Tb), HealthSupported = true, PredictFailure = false, TemperatureC = 38, PowerOnHours = 26_280, PowerCycles = 412,
            Reallocated = 8, Pending = 2, Uncorrectable = 0, CrcErrors = 0, Volumes = [d]
        };
        PhysicalDisk crucial = new()
        {
            Number = 2, Model = "Crucial MX500 1TB", Serial = "DEMO-SERIAL-0002", Firmware = "M3CR046", Bus = BusType.Sata, Media = MediaKind.Ssd,
            SizeBytes = 931 * Gb, HealthSupported = true, PredictFailure = false, TemperatureC = 33, LifeRemainingPercent = 82, PowerOnHours = 9_870, PowerCycles = 1_204,
            Reallocated = 0, Pending = 0, Uncorrectable = 0, CrcErrors = 0, Volumes = [e]
        };
        PhysicalDisk seagate = new()
        {
            Number = 3, Model = "Seagate Expansion HDD", Serial = "DEMO-SERIAL-0003", Firmware = "", Bus = BusType.Usb, Media = MediaKind.Hdd,
            SizeBytes = (long)(3.64 * Tb), HealthSupported = false, Volumes = [f]
        };

        PhysicalDisk[] disks = [samsung, wd, crucial, seagate];
        return new DriveSnapshot(nowUtc - TimeSpan.FromMinutes(12), disks.Select(disk => DriveScanner.Evaluate(disk, temperatureWarningC)).ToList(), [c, d, e, f]);
    }

    /// <summary>CHKDSK's first lines for a read-only scan of the demo Windows volume (invented figures).</summary>
    private static readonly string[] ScanOutputStart =
    [
        "The type of the file system is NTFS.",
        "Volume label is Windows.",
        "",
        "Stage 1: Examining basic file system structure ...",
        "  1249856 file records processed.",
        "File verification completed.",
        " Phase duration (File record verification): 7.81 seconds.",
        "  8412 large file records processed.",
        " Phase duration (Orphan file record recovery): 0.00 milliseconds.",
        "  0 bad file records processed.",
        " Phase duration (Bad file record checking): 1.12 milliseconds.",
        "",
        "Stage 2: Examining file name linkage ...",
        "  2241 reparse records processed.",
        "  1612904 index entries processed.",
        "Index verification completed.",
        " Phase duration (Index verification): 21.40 seconds."
    ];

    private static readonly string[] ScanOutputClean =
    [
        .. ScanOutputStart,
        "",
        "Stage 3: Examining security descriptors ...",
        "Security descriptor verification completed.",
        " Phase duration (Security descriptor verification): 1.05 seconds.",
        "  186524 data files processed.",
        "CHKDSK is verifying Usn Journal...",
        "  41247176 USN bytes processed.",
        "Usn Journal verification completed.",
        "",
        "Windows has scanned the file system and found no problems.",
        "No further action is required."
    ];

    private static readonly string[] ScanOutputProblems =
    [
        "The type of the file system is NTFS.",
        "Volume label is Media.",
        "",
        "Stage 1: Examining basic file system structure ...",
        "  402118 file records processed.",
        "File verification completed.",
        "",
        "Stage 2: Examining file name linkage ...",
        "Found corrupt index entry for file 0x2b1f in index $I30 of directory 0x5 \"\\\".",
        "  508214 index entries processed.",
        "Index verification completed.",
        "",
        "Stage 3: Examining security descriptors ...",
        "Security descriptor verification completed.",
        "",
        "Windows has scanned the file system and found problems.",
        "Run CHKDSK with the /spotfix option to fix them."
    ];

    private static readonly string[] ScanOutputStopped =
    [
        "The type of the file system is NTFS.",
        "Volume label is Projects.",
        "",
        "Stage 1: Examining basic file system structure ..."
    ];

    /// <summary>A read-only scan of C: in progress, started four minutes ago (Check Disk screens).</summary>
    public static void ScanRunning(AppServices services) =>
        services.Chkdsk.LoadPreview("C: /scan", ScanOutputStart, 41, null,
            "Progress: 512340 of 1249856 done; Stage: 41%; Total: 41%; ETA:   0:03:12", Clock.UtcNow - TimeSpan.FromMinutes(4));

    /// <summary>A finished run this session, with the outcome's output and result (Check Disk screens).</summary>
    public static void ScanFinished(AppServices services, Core.Chkdsk.ChkdskOutcome outcome)
    {
        DateTime started = Clock.UtcNow - TimeSpan.FromMinutes(9);
        (string volume, string[] output, int exitCode, string summary, string? error) = outcome switch
        {
            Core.Chkdsk.ChkdskOutcome.Clean => ("C:", ScanOutputClean, 0, Strings.SummaryScanClean, (string?)null),
            Core.Chkdsk.ChkdskOutcome.Problems => ("D:", ScanOutputProblems, 3, Strings.SummaryScanProblems, null),
            _ => ("E:", ScanOutputStopped, -1, "", Strings.ErrorHelperStopped)
        };
        Core.Chkdsk.ChkdskResult result = new(volume, Core.Chkdsk.ChkdskMode.Scan, exitCode, outcome == Core.Chkdsk.ChkdskOutcome.Clean, false, false, summary, error)
        {
            Outcome = outcome,
            Trigger = Core.Chkdsk.ScanTrigger.User,
            StartedUtc = started
        };
        services.Chkdsk.LoadPreview(volume + " /scan", output, null, result, startedUtc: started);
    }

    /// <summary>A newer signed release is available (Settings, Updates screen).</summary>
    public static void UpdateAvailable(AppServices services)
    {
        Version current = services.Updates.CurrentVersion;
        Version latest = new(current.Major, current.Minor + 1, 0);
        services.Updates.LoadPreview(new Core.Updates.UpdateCheckResult(current, latest, "v" + latest.ToString(3), true, true, true,
            Core.Updates.UpdateService.ReleasesUrl, "DiskMonitor-Setup.exe", null, 48_000_000, null, Strings.UpdateAvailable(latest.ToString(3))));
    }

    /// <summary>
    /// Replaces the history with <paramref name="count"/> events spread over a year, each with its own
    /// message and device text, for the memory budget with a full store (subplan 09, part D).
    /// </summary>
    public static void ManyEvents(AppServices services, int count, int seed = 50_000)
    {
        DriveSnapshot drives = services.Drives.Current;
        EventRule[] rules = RuleCatalog.All.Where(r => !r.Ignore).ToArray();
        Random random = new(seed);
        DateTime now = Clock.UtcNow;
        List<DiskEvent> events = new(count);
        for (int i = 0; i < count; i++)
        {
            EventRule rule = rules[random.Next(rules.Length)];
            int disk = random.Next(0, 4);
            PhysicalDisk? physical = drives.FindDisk(disk);
            string? volume = physical?.Volumes.FirstOrDefault()?.Letter;
            DateTime time = now - TimeSpan.FromSeconds(random.Next(0, 365 * 24 * 3600));
            long record = 1_000_000 + i;
            events.Add(new DiskEvent
            {
                Key = $"System|{record}|{time.Ticks}",
                TimeUtc = time,
                Tier = rule.Tier,
                Kind = rule.Kind,
                RuleId = rule.Id,
                Title = rule.Title,
                Message = $"The IO operation at logical block address 0x{random.Next():x} for Disk {disk} (PDO name: \\Device\\{random.Next(0x20, 0x60):X8}) was retried. Record {record}.",
                Explanation = rule.Explanation,
                WhyItMatters = RuleCatalog.WhyItMatters(rule),
                Advice = rule.Advice,
                Log = "System",
                Provider = rule.Id.Split(':')[0],
                EventId = random.Next(1, 600),
                RecordId = record,
                Level = 2,
                DiskNumber = disk,
                Volume = volume,
                DeviceText = DeviceResolver.Describe(disk, physical, volume),
                SuggestScan = false
            });
        }

        services.Events.ReplaceLogEvents(events, DateTime.MinValue);
    }

    /// <summary>Adds a ReFS volume, which the Check Disk table shows as inert.</summary>
    public static void AddRefsVolume(AppServices services)
    {
        DriveSnapshot current = services.Drives.Current;
        VolumeInfo g = new() { Letter = "G:", Label = "Archive", FileSystem = "ReFS", TotalBytes = 2 * Tb, FreeBytes = Tb, DiskNumbers = [3], NtDevice = @"\Device\HarddiskVolume13" };
        services.Drives.SetSnapshot(current with { Volumes = [.. current.Volumes, g] });
    }

    private static EventRule Fallback(string ruleId, string provider, int eventId, int level) =>
        RuleCatalog.Classify(new RawEvent("System", provider, eventId, level, ""), new Dictionary<string, string>())?.Rule
        ?? throw new InvalidOperationException(ruleId);
}
