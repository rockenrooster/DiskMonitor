using System.Buffers.Binary;
using DiskMonitor.Diagnostics;
using Microsoft.Win32.SafeHandles;

namespace DiskMonitor.Core.Drives;

/// <summary>Enumerates physical disks and volumes and reads each drive's self-reported health.</summary>
internal static class DriveScanner
{
    private const int MaxDiskNumber = 64;

    private const byte AttrReallocated = 0x05;
    private const byte AttrPowerOnHours = 0x09;
    private const byte AttrPowerCycles = 0x0C;
    private const byte AttrAirflowTemperature = 0xBE;
    private const byte AttrTemperature = 0xC2;
    private const byte AttrPending = 0xC5;
    private const byte AttrUncorrectable = 0xC6;
    private const byte AttrCrcErrors = 0xC7;

    // Attributes whose normalized value counts down from 100 as an SSD wears, most trustworthy first.
    private static readonly byte[] WearAttributes = [0xE7, 0xB1, 0xCA, 0xA9];

    public static DriveSnapshot Scan(int temperatureWarningC, DriveSnapshot previous)
    {
        List<VolumeInfo> volumes = ScanVolumes();
        List<PhysicalDisk> disks = [];
        for (int number = 0; number < MaxDiskNumber; number++)
        {
            try
            {
                PhysicalDisk? disk = ScanDisk(number, temperatureWarningC, previous.FindDisk(number));
                if (disk is not null)
                {
                    disks.Add(disk with { Volumes = volumes.Where(v => v.DiskNumbers.Contains(number)).ToList() });
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Write($"Scan disk {number}", ex);
            }
        }

        return new DriveSnapshot(DateTime.UtcNow, disks, volumes);
    }

    /// <summary>Applies the health rules to a disk's raw readings.</summary>
    public static PhysicalDisk Evaluate(PhysicalDisk disk, int temperatureWarningC)
    {
        List<string> notes = [];
        HealthStatus status = disk.HealthSupported ? HealthStatus.Good : HealthStatus.Unknown;

        void Raise(HealthStatus level, string note)
        {
            if (level > status)
            {
                status = level;
            }

            notes.Add(note);
        }

        if (disk.PredictFailure == true)
        {
            Raise(HealthStatus.Bad, "The drive predicts its own failure (S.M.A.R.T.)");
        }

        if (disk.NvmeCriticalWarning is { } warning)
        {
            if ((warning & 0x01) != 0)
            {
                Raise(HealthStatus.Bad, "Spare capacity is below the drive's threshold");
            }

            if ((warning & 0x04) != 0)
            {
                Raise(HealthStatus.Bad, "The drive reports degraded reliability");
            }

            if ((warning & 0x08) != 0)
            {
                Raise(HealthStatus.Bad, "The drive has switched to read-only mode");
            }

            if ((warning & 0x10) != 0)
            {
                Raise(HealthStatus.Bad, "The drive's volatile memory backup has failed");
            }

            if ((warning & 0x02) != 0)
            {
                Raise(HealthStatus.Caution, "Temperature is outside the drive's own limits");
            }
        }

        if (disk.Reallocated > 0)
        {
            Raise(HealthStatus.Caution, Count(disk.Reallocated.Value, "reallocated sector"));
        }

        if (disk.Pending > 0)
        {
            Raise(HealthStatus.Caution, Count(disk.Pending.Value, "sector") + " pending reallocation");
        }

        if (disk.Uncorrectable > 0)
        {
            Raise(HealthStatus.Caution, Count(disk.Uncorrectable.Value, "uncorrectable sector"));
        }

        if (disk.MediaErrors > 0)
        {
            Raise(HealthStatus.Caution, Count(disk.MediaErrors.Value, "media error"));
        }

        if (disk.LifeRemainingPercent is <= 10)
        {
            Raise(HealthStatus.Caution, $"{disk.LifeRemainingPercent}% of rated life remaining");
        }

        if (disk.TemperatureC is { } temperature && temperature >= temperatureWarningC)
        {
            Raise(HealthStatus.Caution, $"Temperature {temperature:0} °C is at or above the {temperatureWarningC} °C warning level");
        }

        return disk with { Health = status, HealthNotes = notes };
    }

    /// <summary>Decodes the 30 fixed slots of an ATA S.M.A.R.T. attribute table; empty if the bytes are not one.</summary>
    internal static List<SmartAttribute> ParseSmartAttributes(ReadOnlySpan<byte> data)
    {
        const int tableOffset = 2; // after the revision number
        const int slotLength = 12;
        const int slotCount = 30;
        List<SmartAttribute> attributes = [];
        if (data.Length < tableOffset + slotLength * slotCount)
        {
            return attributes;
        }

        HashSet<byte> seen = [];
        for (int slot = 0; slot < slotCount; slot++)
        {
            ReadOnlySpan<byte> entry = data.Slice(tableOffset + slot * slotLength, slotLength);
            byte id = entry[0];
            if (id == 0)
            {
                continue;
            }

            if (!seen.Add(id))
            {
                return []; // a real table never repeats an attribute
            }

            long raw = 0;
            for (int index = 5; index >= 0; index--)
            {
                raw = (raw << 8) | entry[5 + index];
            }

            attributes.Add(new SmartAttribute(id, entry[3], entry[4], raw));
        }

        // SCSI and USB bridges answer the same query with unrelated bytes; require a plausible table.
        bool plausible = attributes.Count >= 4
            && attributes.Any(a => a.Id is AttrPowerOnHours or AttrPowerCycles or AttrReallocated or AttrTemperature);
        return plausible ? attributes : [];
    }

    private static PhysicalDisk? ScanDisk(int number, int temperatureWarningC, PhysicalDisk? previous)
    {
        using SafeFileHandle device = StorageNative.OpenPhysicalDisk(number);
        if (device.IsInvalid)
        {
            return null;
        }

        DeviceIdentity identity = StorageNative.QueryIdentity(device)
            ?? new DeviceIdentity("", "", "", BusType.Unknown, RemovableMedia: false);
        long size = StorageNative.QuerySizeBytes(device);
        if (size <= 0 && identity.RemovableMedia)
        {
            return null; // an empty card reader slot
        }

        bool synthetic = identity.Bus is BusType.Virtual or BusType.FileBackedVirtual or BusType.Spaces;
        PhysicalDisk disk = new()
        {
            Number = number,
            Model = identity.Model,
            Serial = identity.Serial,
            Firmware = identity.Firmware,
            Bus = identity.Bus,
            RemovableMedia = identity.RemovableMedia,
            SizeBytes = size,
            Media = ResolveMedia(device, identity, synthetic)
        };

        if (synthetic)
        {
            return Evaluate(disk, temperatureWarningC);
        }

        // Health queries wake a spun-down drive, so a sleeping drive keeps its last readings.
        if (!StorageNative.IsPoweredOn(device))
        {
            return previous is not null && previous.Key == disk.Key
                ? previous with { Number = number, Asleep = true }
                : Evaluate(disk with { Asleep = true }, temperatureWarningC);
        }

        disk = identity.Bus == BusType.Nvme ? ReadNvmeHealth(device, disk) : ReadAtaHealth(device, disk);
        return Evaluate(disk, temperatureWarningC);
    }

    private static MediaKind ResolveMedia(SafeFileHandle device, DeviceIdentity identity, bool synthetic)
    {
        if (identity.Bus == BusType.Nvme)
        {
            return MediaKind.Ssd;
        }

        if (synthetic || identity.RemovableMedia)
        {
            return MediaKind.Unknown;
        }

        return StorageNative.QueryIncursSeekPenalty(device) switch
        {
            true => MediaKind.Hdd,
            false => MediaKind.Ssd,
            null => MediaKind.Unknown
        };
    }

    private static PhysicalDisk ReadNvmeHealth(SafeFileHandle device, PhysicalDisk disk)
    {
        byte[]? log = StorageNative.QueryLogPage(device, StorageNative.ProtocolTypeNvme, StorageNative.NvmeLogPageHealthInfo, 0);
        if (log is null || IsBlank(log))
        {
            double? fallbackTemperature = StorageNative.QueryTemperatureCelsius(device);
            bool? predicted = StorageNative.QueryPredictFailure(device)?.Predicted;
            return disk with
            {
                TemperatureC = fallbackTemperature,
                PredictFailure = predicted,
                HealthSupported = fallbackTemperature is not null || predicted is not null
            };
        }

        // NVME_HEALTH_INFO_LOG; the 128-bit counters never exceed 64 bits in practice.
        int kelvin = BinaryPrimitives.ReadUInt16LittleEndian(log.AsSpan(1));
        int percentageUsed = log[5]; // may exceed 100 once the rated endurance is passed
        return disk with
        {
            HealthSupported = true,
            NvmeCriticalWarning = log[0],
            TemperatureC = kelvin == 0 ? null : kelvin - 273,
            AvailableSparePercent = log[3],
            SpareThresholdPercent = log[4],
            LifeRemainingPercent = Math.Clamp(100 - percentageUsed, 0, 100),
            BytesRead = BinaryPrimitives.ReadUInt64LittleEndian(log.AsSpan(32)) * 512000d,
            BytesWritten = BinaryPrimitives.ReadUInt64LittleEndian(log.AsSpan(48)) * 512000d,
            PowerCycles = ClampToLong(BinaryPrimitives.ReadUInt64LittleEndian(log.AsSpan(112))),
            PowerOnHours = ClampToLong(BinaryPrimitives.ReadUInt64LittleEndian(log.AsSpan(128))),
            UnsafeShutdowns = ClampToLong(BinaryPrimitives.ReadUInt64LittleEndian(log.AsSpan(144))),
            MediaErrors = ClampToLong(BinaryPrimitives.ReadUInt64LittleEndian(log.AsSpan(160)))
        };
    }

    private static PhysicalDisk ReadAtaHealth(SafeFileHandle device, PhysicalDisk disk)
    {
        PredictFailureResult? prediction = StorageNative.QueryPredictFailure(device);
        List<SmartAttribute> attributes = prediction is null ? [] : ParseSmartAttributes(prediction.VendorData);
        double? temperature = StorageNative.QueryTemperatureCelsius(device);
        bool ata = disk.Bus is BusType.Sata or BusType.Ata;

        long? Counter(byte id)
        {
            SmartAttribute? attribute = attributes.FirstOrDefault(a => a.Id == id);
            return attribute is null ? null : attribute.Raw & 0xFFFFFFFF;
        }

        if (temperature is null)
        {
            long? raw = Counter(AttrTemperature) ?? Counter(AttrAirflowTemperature);
            if (raw is not null && (raw.Value & 0xFF) is > 0 and < 120)
            {
                temperature = raw.Value & 0xFF;
            }
        }

        if (temperature is null && ata
            && StorageNative.QueryLogPage(device, StorageNative.ProtocolTypeAta, StorageNative.AtaLogDeviceStatistics, StorageNative.AtaStatisticsPageTemperature) is { } temperaturePage
            && ParseAtaStatistic(temperaturePage, 8) is { } statistic)
        {
            temperature = (sbyte)statistic;
        }

        int? life = null;
        if (disk.Media != MediaKind.Hdd)
        {
            if (ata
                && StorageNative.QueryLogPage(device, StorageNative.ProtocolTypeAta, StorageNative.AtaLogDeviceStatistics, StorageNative.AtaStatisticsPageSolidState) is { } endurancePage
                && ParseAtaStatistic(endurancePage, 8) is { } percentageUsed)
            {
                life = Math.Clamp(100 - percentageUsed, 0, 100);
            }
            else
            {
                foreach (byte id in WearAttributes)
                {
                    SmartAttribute? wear = attributes.FirstOrDefault(a => a.Id == id);
                    if (wear is { Current: > 0 and <= 100 })
                    {
                        life = wear.Current;
                        break;
                    }
                }
            }
        }

        long? powerOnHours = Counter(AttrPowerOnHours);
        return disk with
        {
            HealthSupported = prediction is not null || attributes.Count > 0 || temperature is not null,
            PredictFailure = prediction?.Predicted,
            Attributes = attributes,
            TemperatureC = temperature,
            LifeRemainingPercent = life,
            PowerOnHours = powerOnHours is > 1_000_000 ? null : powerOnHours, // some drives pack minutes into the raw value
            PowerCycles = Counter(AttrPowerCycles),
            Reallocated = Counter(AttrReallocated),
            Pending = Counter(AttrPending),
            Uncorrectable = Counter(AttrUncorrectable),
            CrcErrors = Counter(AttrCrcErrors)
        };
    }

    /// <summary>One statistic from an ATA Device Statistics page; null unless flagged supported and valid.</summary>
    internal static int? ParseAtaStatistic(ReadOnlySpan<byte> page, int offset)
    {
        ulong value = BinaryPrimitives.ReadUInt64LittleEndian(page[offset..]);
        return value >> 62 == 3 ? (int)(value & 0xFF) : null;
    }

    private static List<VolumeInfo> ScanVolumes()
    {
        List<VolumeInfo> volumes = [];
        string systemRoot = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "";
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Enumerate volumes", ex);
            return volumes;
        }

        foreach (DriveInfo drive in drives)
        {
            try
            {
                if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
                {
                    continue;
                }

                string letter = drive.Name.TrimEnd('\\');
                if (drive.DriveType == DriveType.Removable && letter is "A:" or "B:")
                {
                    continue; // probing a floppy drive is slow and noisy
                }

                if (!drive.IsReady)
                {
                    continue;
                }

                int[] diskNumbers = [];
                bool? dirty = null;
                using (SafeFileHandle handle = StorageNative.OpenVolume(letter))
                {
                    if (!handle.IsInvalid)
                    {
                        diskNumbers = StorageNative.QueryVolumeDiskNumbers(handle);
                    }
                }

                // The dirty bit is readable by administrators only; unelevated it stays unknown.
                if (App.Elevation.IsElevated)
                {
                    using SafeFileHandle readable = StorageNative.OpenVolumeForRead(letter);
                    if (!readable.IsInvalid)
                    {
                        dirty = StorageNative.QueryVolumeDirty(readable);
                    }
                }

                volumes.Add(new VolumeInfo
                {
                    Letter = letter,
                    Label = drive.VolumeLabel ?? "",
                    FileSystem = drive.DriveFormat ?? "",
                    TotalBytes = drive.TotalSize,
                    FreeBytes = drive.TotalFreeSpace,
                    Removable = drive.DriveType == DriveType.Removable,
                    IsSystem = letter.Equals(systemRoot, StringComparison.OrdinalIgnoreCase),
                    Dirty = dirty,
                    NtDevice = StorageNative.QueryNtDeviceName(letter),
                    GuidPath = StorageNative.QueryVolumeGuidPath(letter),
                    DiskNumbers = diskNumbers
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The volume went away or is locked mid-scan; it will be picked up next time.
            }
        }

        return volumes;
    }

    private static bool IsBlank(ReadOnlySpan<byte> log) => log[..200].IndexOfAnyExcept((byte)0) < 0;

    private static long ClampToLong(ulong value) => value > long.MaxValue ? long.MaxValue : (long)value;

    private static string Count(long value, string noun) => value == 1 ? $"1 {noun}" : $"{value:N0} {noun}s";
}
