using System.Text.RegularExpressions;

namespace DiskMonitor.Core.Events;

/// <summary>How one kind of event is tiered and explained. Users can re-tier or ignore a rule by its id.</summary>
internal sealed record EventRule(
    string Id,
    string Source,
    Tier Tier,
    ErrorKind Kind,
    string Title,
    string Explanation,
    string Advice,
    bool SuggestScan = false,
    bool Ignore = false);

/// <summary>The fields of a log record that classification needs.</summary>
internal readonly record struct RawEvent(string Log, string Provider, int Id, int Level, string Message);

/// <summary>A matched rule plus the tier after the user's override, if any.</summary>
internal readonly record struct Classification(EventRule Rule, Tier Tier, bool Overridden);

internal static partial class RuleCatalog
{
    public const string IgnoreValue = "Ignore";
    private const string HealthSource = "Disk Monitor health check";

    private static readonly Dictionary<string, string> Families = new(StringComparer.OrdinalIgnoreCase)
    {
        ["disk"] = "disk",
        ["Ntfs"] = "ntfs",
        ["Microsoft-Windows-Ntfs"] = "ntfs",
        ["ReFS"] = "refs",
        ["Microsoft-Windows-ReFS"] = "refs",
        ["exFAT"] = "fat",
        ["Fastfat"] = "fat",
        ["volsnap"] = "volsnap",
        ["volmgr"] = "volmgr",
        ["partmgr"] = "partmgr",
        ["Microsoft-Windows-DiskDiagnostic"] = "diskdiag",
        ["Chkdsk"] = "chkdsk",
        ["Microsoft-Windows-Chkdsk"] = "chkdsk",
        ["Wininit"] = "wininit",
        ["Microsoft-Windows-Wininit"] = "wininit"
    };

    /// <summary>Storage port and miniport drivers that ship with Windows or are widely installed.</summary>
    private static readonly string[] KnownPortDrivers =
    [
        "storahci", "stornvme", "storvsc", "storufs", "sdstor", "UASPStor", "atapi", "nvme", "secnvme",
        "iaStorA", "iaStorAC", "iaStorAV", "iaStorAVC", "iaStorV", "iaStorVD", "iaVROC", "vmd",
        "amdsata", "amd_sata", "amdxata", "rcraid", "rcbottom", "nvraid", "nvstor",
        "LSI_SAS", "LSI_SAS2i", "LSI_SAS3i", "megasas", "megasas2i", "megasas35i", "ItSas35i", "percsas2i",
        "percsas3i", "HpSAMD", "SmartSAMD", "arcsas", "adp80xx", "3ware", "stexstor", "mvumis", "vsmraid",
        "VSTXRAID", "SiSRaid2", "SiSRaid4", "mpio", "sbp2port", "asstor64", "mvs91xx"
    ];

    private static readonly Dictionary<string, EventRule> Rules = BuildRules();

    /// <summary>Every built-in rule, in display order.</summary>
    public static IReadOnlyCollection<EventRule> All => Rules.Values;

    public static EventRule? Find(string id) => Rules.GetValueOrDefault(id);

    /// <summary>Provider names whose System-log events are worth classifying.</summary>
    public static HashSet<string> BuildSystemProviderSet(IEnumerable<string> discoveredPortDrivers)
    {
        HashSet<string> providers = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string provider, string family) in Families)
        {
            if (family is not ("chkdsk" or "wininit"))
            {
                providers.Add(provider);
            }
        }

        providers.UnionWith(KnownPortDrivers);
        providers.UnionWith(discoveredPortDrivers);
        return providers;
    }

    /// <summary>
    /// Maps a log record to a rule. Returns null when the record is not a disk problem, is covered
    /// by an ignored rule, or was muted by the user.
    /// </summary>
    public static Classification? Classify(RawEvent raw, IReadOnlyDictionary<string, string> overrides)
    {
        EventRule? rule = Match(raw);
        if (rule is null || ResolveTier(rule, overrides, out bool overridden) is not { } tier)
        {
            return null;
        }

        return new Classification(rule, tier, overridden);
    }

    /// <summary>The tier a rule currently maps to, or null when the rule is ignored.</summary>
    public static Tier? ResolveTier(EventRule rule, IReadOnlyDictionary<string, string> overrides, out bool overridden)
    {
        overridden = false;
        if (overrides.TryGetValue(rule.Id, out string? choice))
        {
            if (choice.Equals(IgnoreValue, StringComparison.OrdinalIgnoreCase))
            {
                overridden = true;
                return null;
            }

            if (Enum.TryParse(choice, ignoreCase: true, out Tier chosen) && Enum.IsDefined(chosen))
            {
                overridden = true;
                return chosen;
            }
        }

        return rule.Ignore ? null : rule.Tier;
    }

    private static EventRule? Match(RawEvent raw)
    {
        string family = Families.GetValueOrDefault(raw.Provider) ?? "port";
        switch (family)
        {
            case "chkdsk":
            case "wininit":
                return MatchChkdskResult(raw, family);
            case "ntfs" when raw.Id == 98:
                return MatchNtfsVolumeState(raw);
        }

        if (Rules.TryGetValue($"{family}:{raw.Id}", out EventRule? rule))
        {
            return rule;
        }

        return BuildFallback(raw, family);
    }

    // Event 98 carries the volume's repair state as free text: healthy, or one of three repair needs.
    private static EventRule? MatchNtfsVolumeState(RawEvent raw)
    {
        string text = raw.Message;
        if (text.Contains("is healthy", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (text.Contains("Full Chkdsk", StringComparison.OrdinalIgnoreCase))
        {
            return Rules["ntfs:98-full"];
        }

        if (text.Contains("Spot Fix", StringComparison.OrdinalIgnoreCase)
            || text.Contains("taken offline", StringComparison.OrdinalIgnoreCase))
        {
            return Rules["ntfs:98-spotfix"];
        }

        if (text.Contains("Online Scan", StringComparison.OrdinalIgnoreCase))
        {
            return Rules["ntfs:98-scan"];
        }

        // Unrecognised wording (for example a localized Windows): trust the record's own level.
        return raw.Level is >= 1 and <= 3 ? Rules["ntfs:98-scan"] : null;
    }

    // CHKDSK logs its whole console output as one informational record; only the outcome matters.
    private static EventRule? MatchChkdskResult(RawEvent raw, string family)
    {
        if (family == "wininit" && raw.Id != 1001)
        {
            return null;
        }

        string text = raw.Message;
        Match badSectors = BadSectorsPattern().Match(text);
        if (badSectors.Success && long.TryParse(badSectors.Groups[1].Value, out long kilobytes) && kilobytes > 0)
        {
            return Rules["chkdsk:bad-sectors"];
        }

        if (text.Contains("found no problems", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (text.Contains("made corrections to the file system", StringComparison.OrdinalIgnoreCase)
            || text.Contains("made repairs to the file system", StringComparison.OrdinalIgnoreCase))
        {
            return Rules["chkdsk:fixed"];
        }

        if (text.Contains("found problems", StringComparison.OrdinalIgnoreCase)
            || text.Contains("found errors", StringComparison.OrdinalIgnoreCase)
            || text.Contains("errors found", StringComparison.OrdinalIgnoreCase)
            || text.Contains("queued for offline repair", StringComparison.OrdinalIgnoreCase)
            || text.Contains("cannot continue in read-only mode", StringComparison.OrdinalIgnoreCase)
            || text.Contains("unrecoverable error", StringComparison.OrdinalIgnoreCase))
        {
            return Rules["chkdsk:problems"];
        }

        return null;
    }

    // Unlisted events from watched providers still surface, tiered by the record's own level.
    private static EventRule? BuildFallback(RawEvent raw, string family)
    {
        Tier? byLevel = raw.Level switch
        {
            1 => Tier.Critical,
            2 => Tier.High,
            3 => Tier.Medium,
            _ => null
        };
        if (byLevel is null)
        {
            return null;
        }

        (string name, Tier cap, ErrorKind kind) = family switch
        {
            "disk" => ("Disk", Tier.Critical, ErrorKind.Physical),
            "ntfs" => ("NTFS", Tier.Critical, ErrorKind.Logical),
            "refs" => ("ReFS", Tier.Critical, ErrorKind.Logical),
            "fat" => ("FAT/exFAT", Tier.High, ErrorKind.Logical),
            "volsnap" => ("Shadow copy", Tier.Low, ErrorKind.Other),
            "volmgr" => ("Volume manager", Tier.Low, ErrorKind.Other),
            "partmgr" => ("Partition manager", Tier.Low, ErrorKind.Other),
            "diskdiag" => ("Disk diagnostic", Tier.Critical, ErrorKind.Health),
            _ => ("Storage controller", Tier.High, ErrorKind.Physical)
        };

        Tier tier = byLevel.Value > cap ? cap : byLevel.Value;
        return new EventRule(
            $"{family}:{raw.Id}",
            $"{raw.Provider} · Event {raw.Id}",
            tier,
            kind,
            $"{name} event {raw.Id}",
            "Windows logged this event for a storage component. Disk Monitor has no specific guidance for it, so its tier follows the severity Windows gave it.",
            "Read the message below. If it keeps recurring, check the health of the drive involved.");
    }

    [GeneratedRegex(@"(\d+) KB in bad sectors", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BadSectorsPattern();

    private static Dictionary<string, EventRule> BuildRules()
    {
        Dictionary<string, EventRule> rules = new(StringComparer.OrdinalIgnoreCase);

        void Add(string id, string source, Tier tier, ErrorKind kind, string title, string explanation, string advice, bool scan = false, bool ignore = false) =>
            rules[id] = new EventRule(id, source, tier, kind, title, explanation, advice, scan, ignore);

        const string backup = "Back up this drive now, then check its health on the Drives page. Plan to replace it if this keeps happening.";
        const string cabling = "Reseat or replace the data and power cables, try another port, and update the storage controller driver. If it continues, back up the drive and check its health.";

        // --- disk class driver: physical media and transport -------------------------------------
        Add("disk:7", "disk · Event 7", Tier.Critical, ErrorKind.Physical, "Bad block on disk",
            "Windows could not read or write a sector on this disk. Bad blocks usually mean the media is wearing out or damaged.",
            backup);
        Add("disk:11", "disk · Event 11", Tier.High, ErrorKind.Physical, "Disk controller error",
            "The storage controller reported an error while talking to this disk. Typical causes are a loose or damaged cable, unstable power, or a failing drive or port.",
            cabling);
        Add("disk:15", "disk · Event 15", Tier.Medium, ErrorKind.Physical, "Disk not ready",
            "The disk was not ready when Windows tried to use it. This is common for external drives waking from sleep and more worrying for internal ones.",
            "If this repeats on an internal drive, check its cables and power, then its health.");
        Add("disk:51", "disk · Event 51", Tier.High, ErrorKind.Physical, "I/O error during paging operation",
            "Windows hit an error reading or writing this disk during paging or cached file I/O. It usually points to a failing drive, a bad cable, or a device that was unplugged while in use.",
            "Back up the drive and check its cabling and health. For removable drives, always use Safely Remove before unplugging.");
        Add("disk:52", "disk · Event 52", Tier.Critical, ErrorKind.Health, "Drive failure predicted",
            "The drive's own S.M.A.R.T. diagnostics predict that it will fail.",
            "Back up your data immediately and replace the drive.");
        Add("disk:153", "disk · Event 153", Tier.Medium, ErrorKind.Physical, "I/O request retried",
            "A read or write to this disk timed out and was retried. An occasional retry is harmless; frequent retries point to cabling, power, or drive problems.",
            "Check cables and power. If retries continue, review the drive's health and back it up.");
        Add("disk:154", "disk · Event 154", Tier.Critical, ErrorKind.Physical, "I/O failed with a hardware error",
            "A read or write failed because the disk itself reported a hardware error.",
            backup);
        Add("disk:157", "disk · Event 157", Tier.Medium, ErrorKind.Physical, "Disk removed unexpectedly",
            "The disk disappeared without being safely removed. For an internal disk this suggests a loose connection, a power problem, or drive failure.",
            "Check the connection and power. Scan the disk's volumes afterwards, since an unexpected removal can damage the file system.");
        Add("disk:158", "disk · Event 158", Tier.Low, ErrorKind.Other, "Duplicate disk identifiers",
            "Two disks report the same identifiers, which usually happens with cloned disks or some multi-path setups.",
            "If the disks are clones, give one a new signature. Otherwise no action is needed.");
        Add("disk:9", "disk · Event 9", Tier.Medium, ErrorKind.Physical, "Disk timed out",
            "The disk did not respond within the time Windows allows.",
            cabling);
        Add("disk:5", "disk · Event 5", Tier.High, ErrorKind.Physical, "Parity error on disk",
            "Data was corrupted in transit between the disk and the controller.",
            cabling);
        Add("disk:32", "disk · Event 32", Tier.Low, ErrorKind.Other, "Write cache enabled",
            "The disk's write cache is on, so a sudden power loss could lose recently written data.",
            "No action is needed on a system with reliable power. Use a UPS for important data.");

        // --- storage port and miniport drivers ----------------------------------------------------
        Add("port:129", "Storage controller · Event 129", Tier.Medium, ErrorKind.Physical, "Storage device was reset",
            "Windows reset a storage device after it stopped responding. Repeated resets cause freezes and can precede drive failure.",
            "Update storage drivers and firmware, check cables and power, and consider disabling link power management if resets continue.");
        Add("port:9", "Storage controller · Event 9", Tier.Medium, ErrorKind.Physical, "Storage device timed out",
            "A storage device did not respond within the time Windows allows.",
            cabling);
        Add("port:11", "Storage controller · Event 11", Tier.High, ErrorKind.Physical, "Storage controller error",
            "The storage controller driver detected an error on one of its ports.",
            cabling);
        Add("port:5", "Storage controller · Event 5", Tier.High, ErrorKind.Physical, "Parity error on storage bus",
            "Data was corrupted in transit between a storage device and its controller.",
            cabling);
        Add("port:15", "Storage controller · Event 15", Tier.Medium, ErrorKind.Physical, "Storage device not ready",
            "A storage device was not ready when Windows tried to use it.",
            "If this repeats on an internal drive, check its cables and power, then its health.");

        // --- NTFS: file system structure ----------------------------------------------------------
        Add("ntfs:55", "Ntfs · Event 55", Tier.Critical, ErrorKind.Logical, "File system corruption detected",
            "NTFS found corruption in the file system structure of this volume.",
            "Run a scan on the volume. If corruption returns after a repair, check the health of the underlying drive.", scan: true);
        Add("ntfs:98-full", "Ntfs · Event 98", Tier.High, ErrorKind.Logical, "Volume needs a full CHKDSK",
            "Windows found file system problems that can only be repaired while the volume is offline.",
            "Run a repair (chkdsk /f) on the volume. For the system drive it runs at the next restart.", scan: true);
        Add("ntfs:98-spotfix", "Ntfs · Event 98", Tier.High, ErrorKind.Logical, "Volume needs an offline spot fix",
            "Windows found file system problems and queued a short offline repair for this volume.",
            "Restart to let Windows repair the volume, or run a spot fix from the Check Disk page.", scan: true);
        Add("ntfs:98-scan", "Ntfs · Event 98", Tier.Medium, ErrorKind.Logical, "Volume needs an online scan",
            "Windows suspects file system problems on this volume and wants to scan it.",
            "Run a scan on the volume from the Check Disk page.", scan: true);
        Add("ntfs:130", "Ntfs · Event 130", Tier.Low, ErrorKind.Logical, "File system repaired",
            "NTFS repaired a problem in the file system structure of this volume on its own.",
            "No action is needed. Repeated repairs on one volume are a reason to check the drive's health.");
        Add("ntfs:131", "Ntfs · Event 131", Tier.Critical, ErrorKind.Logical, "File system cannot be corrected",
            "NTFS found a problem in this volume's file system structure that it could not repair while the volume is in use.",
            "Back up the volume, then run a repair (chkdsk /f) from the Check Disk page.", scan: true);
        Add("ntfs:132", "Ntfs · Event 132", Tier.Medium, ErrorKind.Logical, "Many file system repairs",
            "NTFS made so many repairs in a short time that it stopped logging them individually.",
            "Run a scan on the affected volumes and check the health of their drive.", scan: true);
        Add("ntfs:137", "Ntfs · Event 137", Tier.High, ErrorKind.Logical, "Transaction manager failed to start",
            "The NTFS transaction resource manager on this volume hit an error it could not recover from. This often follows a drive that disconnected or lost power.",
            "Check the drive's connection, then scan the volume.", scan: true);
        Add("ntfs:140", "Ntfs · Event 140", Tier.High, ErrorKind.Logical, "Failed to flush the transaction log",
            "Windows could not write file system log data to this volume, so recent changes may be lost or corrupted. This usually means the drive disconnected or is failing.",
            "Check the drive's connection and health, then scan the volume.", scan: true);
        Add("ntfs:57", "Ntfs · Event 57", Tier.High, ErrorKind.Logical, "Failed to flush the transaction log",
            "Windows could not write file system log data to this volume, so recent changes may be lost or corrupted. This usually means the drive disconnected or is failing.",
            "Check the drive's connection and health, then scan the volume.", scan: true);
        Add("ntfs:50", "Ntfs · Event 50", Tier.High, ErrorKind.Logical, "Delayed write failed",
            "Windows lost data it was still writing to a file because the device stopped accepting writes.",
            "Check the drive's connection and health. Files that were being saved at that moment may be incomplete.", scan: true);
        Add("ntfs:7", "Ntfs · Event 7", Tier.High, ErrorKind.Logical, "Torn write detected",
            "NTFS found a partially written structure on this volume, typically after power loss or a drive that drops writes.",
            "Scan the volume and check the drive's health and power.", scan: true);
        Add("ntfs:150", "Ntfs · Event 150", Tier.High, ErrorKind.Physical, "Bad clusters relocated",
            "A read or write failed and NTFS moved the data elsewhere, marking the original clusters as bad. This may indicate a failing disk.",
            backup);
        Add("ntfs:162", "Ntfs · Event 162", Tier.Critical, ErrorKind.Physical, "Data read does not match data written",
            "Data read back from the drive differs from what was written. That is silent corruption, caused by the drive, its cable or controller, or faulty memory.",
            "Back up the drive now. Check its health and cabling, and test the system memory.");
        Add("ntfs:163", "Ntfs · Event 163", Tier.Medium, ErrorKind.Logical, "MFT bitmap inconsistency",
            "The NTFS master file table bitmap on this volume does not match the table it describes.",
            "Run a scan on the volume.", scan: true);
        Add("ntfs:210", "Ntfs · Event 210", Tier.Low, ErrorKind.Logical, "Thin provisioning mapping repaired",
            "NTFS fixed a mapping problem on a thinly provisioned volume.",
            "No action is needed.");
        Add("ntfs:211", "Ntfs · Event 211", Tier.High, ErrorKind.Logical, "Thin provisioning repair failed",
            "NTFS could not fix a mapping problem on a thinly provisioned volume, possibly because the pool is out of space.",
            "Free up or add capacity to the storage pool, then scan the volume.", scan: true);

        // --- ReFS ---------------------------------------------------------------------------------
        Add("refs:130", "ReFS · Event 130", Tier.Low, ErrorKind.Logical, "File system repaired",
            "ReFS repaired a problem in the file system structure of this volume.", "No action is needed.");
        Add("refs:131", "ReFS · Event 131", Tier.Critical, ErrorKind.Logical, "File system cannot be corrected",
            "ReFS found a problem in this volume's structure that it could not repair.",
            "Back up the volume and check the health of the drives behind it.");
        Add("refs:132", "ReFS · Event 132", Tier.Low, ErrorKind.Logical, "Checksum error corrected",
            "ReFS detected corrupted data and repaired it from a good copy.",
            "No action is needed, but repeated corrections suggest a drive is degrading.");
        Add("refs:133", "ReFS · Event 133", Tier.Critical, ErrorKind.Physical, "Uncorrectable checksum error",
            "ReFS detected corrupted data and had no good copy to repair it from. The file named in the message is damaged.",
            "Restore the file from a backup and check the health of the drives behind this volume.");
        Add("refs:134", "ReFS · Event 134", Tier.High, ErrorKind.Physical, "Metadata write failed",
            "ReFS could not write file system metadata to the drive behind this volume.", backup);
        Add("refs:135", "ReFS · Event 135", Tier.High, ErrorKind.Logical, "ReFS volume failed to mount",
            "The volume is formatted as ReFS but could not be mounted.",
            "Check that every drive behind the volume is connected and healthy.");
        Add("refs:137", "ReFS · Event 137", Tier.High, ErrorKind.Logical, "Log replay failed",
            "ReFS could not replay its log on this volume, so recent changes may be lost.", backup);
        Add("refs:138", "ReFS · Event 138", Tier.High, ErrorKind.Logical, "Log could not be opened",
            "ReFS could not open its log on this volume.", backup);
        Add("refs:139", "ReFS · Event 139", Tier.Medium, ErrorKind.Logical, "Global metadata corruption repaired",
            "ReFS repaired corrupted global metadata on this volume. Some space may have been leaked.",
            "Check the health of the drives behind this volume.");
        Add("refs:140", "ReFS · Event 140", Tier.Critical, ErrorKind.Logical, "Global metadata corruption",
            "ReFS found corrupted global metadata on this volume and could not repair it.", backup);
        Add("refs:513", "ReFS · Event 513", Tier.High, ErrorKind.Logical, "Corrupt file removed",
            "ReFS found a corrupt file and removed it from the volume.",
            "Restore the file named in the message from a backup.");
        Add("refs:514", "ReFS · Event 514", Tier.High, ErrorKind.Logical, "Corrupt file",
            "ReFS found a corrupt file and may not have been able to remove it.",
            "Restore the file named in the message from a backup.");
        Add("refs:515", "ReFS · Event 515", Tier.High, ErrorKind.Logical, "Corrupt folder removed",
            "ReFS found a corrupt folder and removed its contents from the volume.",
            "Restore the folder named in the message from a backup.");
        Add("refs:516", "ReFS · Event 516", Tier.High, ErrorKind.Logical, "Corrupt folder",
            "ReFS found a corrupt folder and may not have been able to remove its contents.",
            "Restore the folder named in the message from a backup.");
        Add("refs:519", "ReFS · Event 519", Tier.Medium, ErrorKind.Logical, "Metadata corruption detected",
            "ReFS detected corruption in file system metadata.", "Check the health of the drives behind this volume.");
        Add("refs:520", "ReFS · Event 520", Tier.High, ErrorKind.Logical, "Metadata corruption",
            "ReFS detected corruption in file system metadata that it could not repair.", backup);
        Add("refs:521", "ReFS · Event 521", Tier.Medium, ErrorKind.Logical, "Self-healing disabled",
            "ReFS detected metadata corruption on this volume and turned off its self-healing features.",
            "Back up the volume and check the health of the drives behind it.");
        foreach (int id in new[] { 136, 517, 518, 522, 524 })
        {
            Add($"refs:{id}", $"ReFS · Event {id}", Tier.Low, ErrorKind.Other, "ReFS configuration notice",
                "ReFS reported a configuration condition on this volume rather than damage.",
                "Read the message below to see whether the configuration is intended.");
        }

        // --- volume shadow copy, volume and partition managers -------------------------------------
        Add("volsnap:14", "volsnap · Event 14", Tier.High, ErrorKind.Physical, "Shadow copies aborted by I/O failure",
            "Windows lost the shadow copies (restore points) of a volume because reads or writes to a drive failed.",
            "Check the health and connection of the drive that holds the volume and its shadow copy storage.");
        Add("volsnap:61", "volsnap · Event 61", Tier.High, ErrorKind.Physical, "Bad sector during volume revert",
            "Restoring a volume from a shadow copy hit a bad sector.",
            "Verify the data on the volume and check the drive's health.");
        Add("volmgr:162", "volmgr · Event 162", Tier.Low, ErrorKind.Other, "Crash dump written",
            "Windows finished writing a crash dump file. This records a system crash, not a disk fault.",
            "No disk action is needed.", ignore: true);
        foreach (int id in new[] { 45, 46, 49, 161 })
        {
            Add($"volmgr:{id}", $"volmgr · Event {id}", Tier.Low, ErrorKind.Other, "Crash dump configuration problem",
                "Windows could not set up or write a crash dump file. This affects crash diagnostics, not your data.",
                "Check that the page file is enabled and that the system drive has free space.");
        }

        foreach (int id in new[] { 58, 59 })
        {
            Add($"partmgr:{id}", $"partmgr · Event {id}", Tier.Low, ErrorKind.Other, "Disk signature collision",
                "Two disks have the same signature, which usually happens with cloned disks. Windows keeps one of them offline.",
                "Bring the disk online in Disk Management to give it a new signature.");
        }

        Add("diskdiag:1", "Windows Disk Diagnostic · Event 1", Tier.Critical, ErrorKind.Health, "S.M.A.R.T. fault detected by Windows",
            "Windows Disk Diagnostic detected that this drive reports a S.M.A.R.T. fault. The drive may fail soon.",
            "Back up your data immediately and replace the drive.");

        // --- CHKDSK results ------------------------------------------------------------------------
        Add("chkdsk:problems", "Chkdsk result", Tier.High, ErrorKind.Logical, "CHKDSK found file system problems",
            "A file system check finished and reported problems on this volume.",
            "Run a repair on the volume from the Check Disk page. For the system drive it runs at the next restart.", scan: true);
        Add("chkdsk:bad-sectors", "Chkdsk result", Tier.High, ErrorKind.Physical, "CHKDSK reports bad sectors",
            "A file system check finished and the volume has clusters marked as bad sectors.",
            backup);
        Add("chkdsk:fixed", "Chkdsk result", Tier.Low, ErrorKind.Logical, "CHKDSK repaired the file system",
            "A file system check found problems on this volume and repaired them.",
            "No action is needed. Repeated repairs on one volume are a reason to check the drive's health.");

        // --- raised by Disk Monitor's own health checks ---------------------------------------------
        Add("smart:predict", HealthSource, Tier.Critical, ErrorKind.Health, "Drive failure predicted",
            "The drive's S.M.A.R.T. self-assessment reports that it is about to fail.",
            "Back up your data immediately and replace the drive.");
        Add("smart:reallocated", HealthSource, Tier.High, ErrorKind.Health, "Reallocated sectors increased",
            "The drive replaced damaged sectors with spares. A growing count means the media is deteriorating.",
            backup);
        Add("smart:pending", HealthSource, Tier.High, ErrorKind.Health, "Pending sectors increased",
            "The drive has sectors it could not read reliably and is waiting to remap. Data in those sectors may already be lost.",
            backup);
        Add("smart:uncorrectable", HealthSource, Tier.High, ErrorKind.Health, "Uncorrectable sectors increased",
            "The drive found sectors whose data it could not recover.",
            backup);
        Add("smart:crc", HealthSource, Tier.Medium, ErrorKind.Physical, "Interface CRC errors increased",
            "Data was corrupted between the drive and the controller and had to be resent. The usual cause is a poor SATA cable or connector.",
            "Reseat or replace the drive's data cable.");
        Add("smart:existing", HealthSource, Tier.Medium, ErrorKind.Health, "Drive reports earlier damage",
            "This drive already had reallocated, pending, or uncorrectable sectors when Disk Monitor first checked it.",
            "Back up the drive and watch this count. Replace the drive if it grows.");
        Add("nvme:critical", HealthSource, Tier.Critical, ErrorKind.Health, "NVMe drive reports a critical warning",
            "The drive raised one of its critical warning flags: spare capacity exhausted, reliability degraded, read-only mode, or backup power failure.",
            "Back up your data immediately and replace the drive.");
        Add("nvme:media", HealthSource, Tier.High, ErrorKind.Health, "NVMe media errors increased",
            "The drive detected data it could not recover from its flash memory.",
            backup);
        Add("health:wear", HealthSource, Tier.Medium, ErrorKind.Health, "SSD is nearly worn out",
            "The drive has used almost all of its rated write endurance.",
            "Plan to replace the drive and keep its backups current.");
        Add("health:temperature", HealthSource, Tier.Medium, ErrorKind.Health, "Drive is running hot",
            "The drive's temperature is above the warning threshold. Sustained heat shortens drive life and can cause throttling or errors.",
            "Improve airflow around the drive or add a heatsink. The threshold is adjustable in Settings.");
        Add("volume:dirty", HealthSource, Tier.Medium, ErrorKind.Logical, "Volume is marked dirty",
            "Windows flagged this volume as possibly inconsistent and will check it at the next restart.",
            "Restart when convenient to let Windows check the volume, or run a scan now.", scan: true);
        Add("volume:space", HealthSource, Tier.Low, ErrorKind.Capacity, "Volume is low on free space",
            "Free space on this volume fell below the warning threshold.",
            "Free up space or move data to another drive. The threshold is adjustable in Settings.");

        return rules;
    }
}
