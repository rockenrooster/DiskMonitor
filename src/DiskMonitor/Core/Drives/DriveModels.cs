namespace DiskMonitor.Core.Drives;

/// <summary>Values of STORAGE_BUS_TYPE.</summary>
internal enum BusType
{
    Unknown = 0,
    Scsi = 1,
    Atapi = 2,
    Ata = 3,
    Ieee1394 = 4,
    Ssa = 5,
    Fibre = 6,
    Usb = 7,
    Raid = 8,
    Iscsi = 9,
    Sas = 10,
    Sata = 11,
    Sd = 12,
    Mmc = 13,
    Virtual = 14,
    FileBackedVirtual = 15,
    Spaces = 16,
    Nvme = 17,
    Scm = 18,
    Ufs = 19
}

internal enum MediaKind
{
    Unknown,
    Hdd,
    Ssd
}

internal enum HealthStatus
{
    /// <summary>The drive does not report health data through this connection.</summary>
    Unknown,
    Good,

    /// <summary>The drive works but shows signs of wear or damage.</summary>
    Caution,

    /// <summary>The drive reports that it is failing.</summary>
    Bad
}

/// <summary>One row of an ATA S.M.A.R.T. attribute table.</summary>
internal sealed record SmartAttribute(byte Id, byte Current, byte Worst, long Raw);

internal sealed record VolumeInfo
{
    /// <summary>Drive letter with colon, for example "C:".</summary>
    public required string Letter { get; init; }
    public string Label { get; init; } = "";
    public string FileSystem { get; init; } = "";
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }
    public bool Removable { get; init; }
    public bool IsSystem { get; init; }

    /// <summary>Whether Windows has flagged the volume for a check at restart; null when it cannot be read.</summary>
    public bool? Dirty { get; init; }

    /// <summary>Kernel device name, for example \Device\HarddiskVolume3.</summary>
    public string NtDevice { get; init; } = "";

    /// <summary>Volume GUID path, for example \\?\Volume{...}\.</summary>
    public string GuidPath { get; init; } = "";
    public IReadOnlyList<int> DiskNumbers { get; init; } = [];

    public double UsedFraction => TotalBytes <= 0 ? 0 : Math.Clamp(1 - (double)FreeBytes / TotalBytes, 0, 1);
    public double FreePercent => TotalBytes <= 0 ? 100 : 100.0 * FreeBytes / TotalBytes;

    /// <summary>Only NTFS supports the online, read-only scan (chkdsk /scan).</summary>
    public bool SupportsOnlineScan => FileSystem.Equals("NTFS", StringComparison.OrdinalIgnoreCase);

    /// <summary>ReFS repairs itself online and rejects CHKDSK.</summary>
    public bool SupportsChkdsk => !FileSystem.Equals("ReFS", StringComparison.OrdinalIgnoreCase) && FileSystem.Length > 0;

    public string DisplayName => App.Strings.VolumeName(Letter, Label);
}

internal sealed record PhysicalDisk
{
    public required int Number { get; init; }
    public string Model { get; init; } = "";
    public string Serial { get; init; } = "";
    public string Firmware { get; init; } = "";
    public BusType Bus { get; init; }

    /// <summary>True for card readers and flash drives, whose media can be pulled out of the device.</summary>
    public bool RemovableMedia { get; init; }
    public long SizeBytes { get; init; }
    public MediaKind Media { get; init; }

    /// <summary>The drive was spun down, so health was not refreshed (reading it would wake the drive).</summary>
    public bool Asleep { get; init; }

    /// <summary>The drive answered at least one health query.</summary>
    public bool HealthSupported { get; init; }
    public bool? PredictFailure { get; init; }
    public double? TemperatureC { get; init; }
    public int? LifeRemainingPercent { get; init; }
    public long? PowerOnHours { get; init; }
    public long? PowerCycles { get; init; }
    public long? Reallocated { get; init; }
    public long? Pending { get; init; }
    public long? Uncorrectable { get; init; }
    public long? CrcErrors { get; init; }
    public long? MediaErrors { get; init; }
    public long? UnsafeShutdowns { get; init; }
    public double? BytesWritten { get; init; }
    public double? BytesRead { get; init; }
    public byte? NvmeCriticalWarning { get; init; }
    public int? AvailableSparePercent { get; init; }
    public int? SpareThresholdPercent { get; init; }
    public IReadOnlyList<SmartAttribute> Attributes { get; init; } = [];
    public HealthStatus Health { get; init; }
    public IReadOnlyList<string> HealthNotes { get; init; } = [];
    public IReadOnlyList<VolumeInfo> Volumes { get; init; } = [];

    /// <summary>Identity that survives re-numbering, used to track a drive's counters over time.</summary>
    public string Key => string.IsNullOrWhiteSpace(Serial) ? $"{Model}|{SizeBytes}" : $"{Model}|{Serial}";

    public string DisplayName => string.IsNullOrWhiteSpace(Model) ? App.Strings.DiskName(Number) : Model;

    public string BusLabel => App.Strings.BusName(Bus);

    public string MediaLabel => App.Strings.MediaName(Media, RemovableMedia);

    public string VolumeLetters => string.Join(", ", Volumes.Select(v => v.Letter));
}

/// <summary>Everything known about the machine's drives at one moment.</summary>
internal sealed record DriveSnapshot(DateTime TakenUtc, IReadOnlyList<PhysicalDisk> Disks, IReadOnlyList<VolumeInfo> Volumes)
{
    public static readonly DriveSnapshot Empty = new(DateTime.MinValue, [], []);

    public PhysicalDisk? FindDisk(int number) => Disks.FirstOrDefault(d => d.Number == number);

    public VolumeInfo? FindVolume(string? letter) =>
        string.IsNullOrEmpty(letter) ? null : Volumes.FirstOrDefault(v => v.Letter.Equals(letter, StringComparison.OrdinalIgnoreCase));

    public VolumeInfo? FindVolumeByNtDevice(string device) =>
        Volumes.FirstOrDefault(v => v.NtDevice.Equals(device, StringComparison.OrdinalIgnoreCase));

    public VolumeInfo? FindVolumeByGuid(string guidPath)
    {
        string wanted = guidPath.TrimEnd('\\');
        return Volumes.FirstOrDefault(v => v.GuidPath.TrimEnd('\\').Equals(wanted, StringComparison.OrdinalIgnoreCase));
    }

    public HealthStatus WorstHealth => Disks.Count == 0 ? HealthStatus.Unknown : Disks.Max(d => d.Health);
}
