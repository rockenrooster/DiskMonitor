using DiskMonitor.App;
using DiskMonitor.Core.Drives;
using DiskMonitor.Core.Events;
using DiskMonitor.Diagnostics;
using DiskMonitor.UI.Pages;

namespace DiskMonitor.Tests;

/// <summary>DRV-01: the Drives page's view of the demo disks matches DriveScanner.Evaluate and subplan 06.</summary>
public sealed class DrivesViewModelTests
{
    private static readonly int Warning = new AppSettings().TemperatureWarningC;

    private static DriveSnapshot Demo() => DemoData.Drives(Warning, new DateTime(2026, 11, 16, 20, 20, 0, DateTimeKind.Utc));

    private static string[] Labels(PhysicalDisk disk) => DrivesPage.BuildTiles(disk, Warning).Select(t => t.Label).ToArray();

    [Fact]
    public void FourCardsInDiskNumberOrder()
    {
        DriveSnapshot snapshot = Demo();
        Assert.Equal([0, 1, 2, 3], snapshot.Disks.Select(d => d.Number));
        Assert.Equal([HealthStatus.Good, HealthStatus.Caution, HealthStatus.Good, HealthStatus.Unknown], snapshot.Disks.Select(d => d.Health));
    }

    [Fact]
    public void NvmeSsdShowsItsNvmeReadingsInTableOrder()
    {
        PhysicalDisk nvme = Demo().Disks[0];
        Assert.Equal(
            [Strings.TileTemperature, Strings.TileLife, Strings.TilePowerOn, Strings.TileCycles, Strings.TileWritten, Strings.TileRead,
             Strings.TileMedia, Strings.TileUnsafe, Strings.TileSpare],
            Labels(nvme));
        Assert.All(DrivesPage.BuildTiles(nvme, Warning), t => Assert.Null(t.Flag));
        Assert.Empty(nvme.HealthNotes);
    }

    [Fact]
    public void CautionHddHasTwoNotesAndTwoFlaggedTiles()
    {
        PhysicalDisk hdd = Demo().Disks[1];
        Assert.Equal([Strings.NoteReallocated(8), Strings.NotePending(2)], hdd.HealthNotes);
        List<DrivesPage.Tile> tiles = DrivesPage.BuildTiles(hdd, Warning);
        Assert.Equal(
            [Strings.TileTemperature, Strings.TilePowerOn, Strings.TileCycles, Strings.TileReallocated, Strings.TilePending, Strings.TileUncorrectable, Strings.TileCrc],
            tiles.Select(t => t.Label));
        Assert.Equal([Strings.TileReallocated, Strings.TilePending], tiles.Where(t => t.Flag == Tier.Medium).Select(t => t.Label));
    }

    [Fact]
    public void UsbDriveWithoutHealthDataHasNoTiles()
    {
        PhysicalDisk usb = Demo().Disks[3];
        Assert.False(usb.HealthSupported);
        Assert.Empty(DrivesPage.BuildTiles(usb, Warning));
    }

    [Fact]
    public void EveryTileHasATooltipAndAnUpperCaseLabel()
    {
        foreach (DrivesPage.Tile tile in Demo().Disks.SelectMany(d => DrivesPage.BuildTiles(d, Warning)))
        {
            Assert.False(string.IsNullOrWhiteSpace(tile.Tip));
            Assert.Equal(tile.Label.ToUpperInvariant(), tile.Label);
        }
    }

    [Fact]
    public void TemperatureAtTheWarningLevelIsFlagged()
    {
        PhysicalDisk hot = Demo().Disks[0] with { TemperatureC = Warning };
        Assert.Equal(Tier.Medium, DrivesPage.BuildTiles(hot, Warning)[0].Flag);
        PhysicalDisk cool = Demo().Disks[0] with { TemperatureC = Warning - 1 };
        Assert.Null(DrivesPage.BuildTiles(cool, Warning)[0].Flag);
    }

    [Fact]
    public void SpareBelowThresholdIsCritical()
    {
        PhysicalDisk worn = Demo().Disks[0] with { AvailableSparePercent = 5, SpareThresholdPercent = 10 };
        Assert.Equal(Tier.Critical, DrivesPage.BuildTiles(worn, Warning).Single(t => t.Label == Strings.TileSpare).Flag);
    }
}
