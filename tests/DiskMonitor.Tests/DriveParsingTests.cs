using DiskMonitor.Core.Drives;

namespace DiskMonitor.Tests;

/// <summary>Model strings, S.M.A.R.T. tables, ATA statistics, and the health status rules.</summary>
public sealed class DriveParsingTests
{
    [Theory]
    [InlineData("WDC WD80", "EDAZ-11TA3A0", (int)BusType.Sata, "WDC WD80EDAZ-11TA3A0")]
    [InlineData("ATA     ", "Samsung SSD 870 EVO 1TB", (int)BusType.Sata, "Samsung SSD 870 EVO 1TB")]
    [InlineData("NVMe", "Samsung SSD 990 PRO 2TB", (int)BusType.Nvme, "Samsung SSD 990 PRO 2TB")]
    [InlineData("", "INTEL  SSDPEKNU020TZ", (int)BusType.Nvme, "INTEL SSDPEKNU020TZ")]
    [InlineData("Seagate", "Expansion HDD", (int)BusType.Usb, "Seagate Expansion HDD")]
    [InlineData("Seagate", "Seagate Expansion", (int)BusType.Usb, "Seagate Expansion")]
    [InlineData("Vendor", "", (int)BusType.Usb, "Vendor")]
    public void ComposeModel(string vendor, string product, int bus, string expected) =>
        Assert.Equal(expected, StorageNative.ComposeModel(vendor, product, (BusType)bus));

    private static byte[] SmartTable(params (byte id, byte current, long raw)[] entries)
    {
        byte[] data = new byte[512];
        for (int slot = 0; slot < entries.Length; slot++)
        {
            int offset = 2 + slot * 12;
            data[offset] = entries[slot].id;
            data[offset + 3] = entries[slot].current;
            data[offset + 4] = entries[slot].current;
            for (int b = 0; b < 6; b++)
            {
                data[offset + 5 + b] = (byte)(entries[slot].raw >> (8 * b));
            }
        }

        return data;
    }

    [Fact]
    public void ParsesARealLookingTable()
    {
        byte[] table = SmartTable((0x05, 100, 8), (0x09, 70, 26_280), (0x0C, 100, 811), (0xC2, 62, 38), (0xC5, 100, 2));
        List<SmartAttribute> attributes = DriveScanner.ParseSmartAttributes(table);
        Assert.Equal(5, attributes.Count);
        Assert.Equal(8, attributes.Single(a => a.Id == 0x05).Raw);
        Assert.Equal(26_280, attributes.Single(a => a.Id == 0x09).Raw);
    }

    [Fact]
    public void RejectsGarbageAndRepeatedIds()
    {
        Assert.Empty(DriveScanner.ParseSmartAttributes(new byte[10]));
        Assert.Empty(DriveScanner.ParseSmartAttributes(SmartTable((0x01, 1, 1), (0x02, 1, 1))));
        Assert.Empty(DriveScanner.ParseSmartAttributes(SmartTable((0x05, 100, 1), (0x09, 1, 1), (0x05, 100, 2), (0x0C, 1, 1))));
        byte[] random = new byte[512];
        new Random(7).NextBytes(random);
        random[2] = 0x33;
        Assert.True(DriveScanner.ParseSmartAttributes(random).Count is 0 or >= 4);
    }

    [Fact]
    public void AtaStatisticNeedsSupportedAndValidFlags()
    {
        byte[] page = new byte[512];
        BitConverter.GetBytes((3UL << 62) | 41UL).CopyTo(page, 8);
        Assert.Equal(41, DriveScanner.ParseAtaStatistic(page, 8));
        BitConverter.GetBytes((1UL << 63) | 41UL).CopyTo(page, 8);
        Assert.Null(DriveScanner.ParseAtaStatistic(page, 8));
    }

    private static PhysicalDisk Healthy() => new() { Number = 1, Model = "Test", HealthSupported = true, TemperatureC = 38 };

    [Fact]
    public void StatusRules()
    {
        Assert.Equal(HealthStatus.Good, DriveScanner.Evaluate(Healthy(), 60).Health);
        Assert.Equal(HealthStatus.Unknown, DriveScanner.Evaluate(Healthy() with { HealthSupported = false }, 60).Health);
        Assert.Equal(HealthStatus.Bad, DriveScanner.Evaluate(Healthy() with { PredictFailure = true }, 60).Health);
        Assert.Equal(HealthStatus.Bad, DriveScanner.Evaluate(Healthy() with { NvmeCriticalWarning = 0x01 }, 60).Health);
        Assert.Equal(HealthStatus.Caution, DriveScanner.Evaluate(Healthy() with { NvmeCriticalWarning = 0x02 }, 60).Health);
        Assert.Equal(HealthStatus.Caution, DriveScanner.Evaluate(Healthy() with { LifeRemainingPercent = 10 }, 60).Health);
        Assert.Equal(HealthStatus.Good, DriveScanner.Evaluate(Healthy() with { LifeRemainingPercent = 11 }, 60).Health);
        Assert.Equal(HealthStatus.Caution, DriveScanner.Evaluate(Healthy() with { TemperatureC = 60 }, 60).Health);
        Assert.Equal(HealthStatus.Good, DriveScanner.Evaluate(Healthy() with { CrcErrors = 500 }, 60).Health);
    }

    [Fact]
    public void NotesUseThePlanWording()
    {
        PhysicalDisk disk = DriveScanner.Evaluate(Healthy() with { Reallocated = 8, Pending = 2, Uncorrectable = 1 }, 60);
        Assert.Equal(HealthStatus.Caution, disk.Health);
        Assert.Equal(["8 reallocated sectors", "2 sectors pending reallocation", "1 uncorrectable sector"], disk.HealthNotes);
        Assert.Equal(["Running at 61 °C, at or above the 60 °C warning level"],
            DriveScanner.Evaluate(Healthy() with { TemperatureC = 61 }, 60).HealthNotes);
    }
}
