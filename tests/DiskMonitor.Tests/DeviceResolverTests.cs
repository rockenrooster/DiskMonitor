using DiskMonitor.Core.Drives;
using DiskMonitor.Core.Events;

namespace DiskMonitor.Tests;

/// <summary>Device names in event messages resolve to disk numbers and letters.</summary>
public sealed class DeviceResolverTests
{
    private static readonly DriveSnapshot Drives = new(
        DateTime.UtcNow,
        [
            new PhysicalDisk { Number = 0, Model = "Samsung SSD 990 PRO 2TB", Bus = BusType.Nvme },
            new PhysicalDisk { Number = 1, Model = "WDC WD80EFZZ-68BTXN0", Bus = BusType.Sata },
            new PhysicalDisk { Number = 3, Model = "Card Reader", Bus = BusType.Usb, RemovableMedia = true }
        ],
        [
            new VolumeInfo { Letter = "C:", Label = "Windows", FileSystem = "NTFS", NtDevice = @"\Device\HarddiskVolume3", GuidPath = @"\\?\Volume{11111111-2222-3333-4444-555555555555}\", DiskNumbers = [0] },
            new VolumeInfo { Letter = "D:", Label = "Media", FileSystem = "NTFS", NtDevice = @"\Device\HarddiskVolume7", GuidPath = @"\\?\Volume{aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee}\", DiskNumbers = [1] },
            new VolumeInfo { Letter = "F:", Label = "Card", FileSystem = "exFAT", NtDevice = @"\Device\HarddiskVolume9", DiskNumbers = [3] }
        ]);

    [Fact]
    public void HarddiskDrPath()
    {
        ResolvedDevice device = DeviceResolver.Resolve(@"The device, \Device\Harddisk1\DR1, has a bad block.", Drives);
        Assert.Equal(1, device.DiskNumber);
        Assert.Null(device.Volume);
        Assert.Equal("Disk 1 · WDC WD80EFZZ-68BTXN0", device.Text);
    }

    [Fact]
    public void VolumeGuid()
    {
        ResolvedDevice device = DeviceResolver.Resolve(@"Volume \\?\Volume{aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee} has errors.", Drives);
        Assert.Equal("D:", device.Volume);
        Assert.Equal(1, device.DiskNumber);
    }

    [Fact]
    public void HarddiskVolumeName()
    {
        ResolvedDevice device = DeviceResolver.Resolve(@"A corruption was discovered on \Device\HarddiskVolume3.", Drives);
        Assert.Equal("C:", device.Volume);
        Assert.Equal(0, device.DiskNumber);
        Assert.Equal("C: · Samsung SSD 990 PRO 2TB (Disk 0)", device.Text);
    }

    [Theory]
    [InlineData("Volume name: d: is damaged", "D:")]
    [InlineData("The file system on drive E: could not be repaired.", "E:")]
    [InlineData("Checking file system on C:", "C:")]
    public void LabelledAndNamedLetters(string message, string expected) =>
        Assert.Equal(expected, DeviceResolver.Resolve(message, Drives).Volume);

    [Fact]
    public void BareLetterOnlyCountsWhenTheVolumeExists()
    {
        Assert.Equal("D:", DeviceResolver.Resolve("Error at D: during write", Drives).Volume);
        Assert.Null(DeviceResolver.Resolve("Error at Q: during write", Drives).Volume);
        Assert.Null(DeviceResolver.Resolve("Time 12:30 reported", Drives).Volume);
    }

    [Fact]
    public void ShadowCopiesAndRemovableMediaAreFlagged()
    {
        Assert.True(DeviceResolver.Resolve(@"\Device\HarddiskVolumeShadowCopy12 is inconsistent", Drives).ShadowCopy);
        Assert.True(DeviceResolver.Resolve(@"\Device\Harddisk3\DR3 was removed", Drives).RemovableMedia);
    }

    [Fact]
    public void MissingDiskSaysNotConnected() =>
        Assert.Equal("Disk 5 (not connected)", DeviceResolver.Resolve(@"\Device\Harddisk5\DR5 error", Drives).Text);
}
