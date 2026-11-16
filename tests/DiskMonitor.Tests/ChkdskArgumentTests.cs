using DiskMonitor.Core.Chkdsk;

namespace DiskMonitor.Tests;

/// <summary>CHKDSK arguments per file system and mode; ReFS refused; volume normalisation.</summary>
public sealed class ChkdskArgumentTests
{
    [Theory]
    [InlineData((int)ChkdskMode.Scan, false, "NTFS", "C: /scan")]
    [InlineData((int)ChkdskMode.Scan, true, "NTFS", "C: /scan /perf")]
    [InlineData((int)ChkdskMode.Scan, false, "FAT32", "C:")]
    [InlineData((int)ChkdskMode.Scan, true, "exFAT", "C:")]
    [InlineData((int)ChkdskMode.SpotFix, false, "NTFS", "C: /spotfix")]
    [InlineData((int)ChkdskMode.Fix, false, "NTFS", "C: /f")]
    [InlineData((int)ChkdskMode.Fix, false, "exFAT", "C: /f")]
    [InlineData((int)ChkdskMode.FixAndRecover, false, "NTFS", "C: /r")]
    public void ArgumentsByModeAndFileSystem(int mode, bool perf, string fileSystem, string expected) =>
        Assert.Equal(expected, ChkdskRunner.BuildArguments("C:", (ChkdskMode)mode, perf, fileSystem));

    [Theory]
    [InlineData((int)ChkdskMode.Scan)]
    [InlineData((int)ChkdskMode.Fix)]
    public void RefsIsRefused(int mode) =>
        Assert.Throws<ChkdskException>(() => ChkdskRunner.BuildArguments("D:", (ChkdskMode)mode, false, "ReFS"));

    [Fact]
    public void SpotFixNeedsNtfs() =>
        Assert.Throws<ChkdskException>(() => ChkdskRunner.BuildArguments("D:", ChkdskMode.SpotFix, false, "exFAT"));

    [Theory]
    [InlineData("c", "C:")]
    [InlineData("C:", "C:")]
    [InlineData("d:\\", "D:")]
    [InlineData("  e  ", "E:")]
    [InlineData("cd", null)]
    [InlineData("1:", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("C:\\Windows", null)]
    [InlineData("..\\x", null)]
    public void NormalizeVolume(string? input, string? expected) =>
        Assert.Equal(expected, ChkdskRunner.NormalizeVolume(input));

    [Fact]
    public void OnlyTheScanIsReadOnly()
    {
        Assert.True(ChkdskMode.Scan.IsReadOnly());
        Assert.False(ChkdskMode.SpotFix.IsReadOnly());
        Assert.False(ChkdskMode.Fix.IsReadOnly());
        Assert.False(ChkdskMode.FixAndRecover.IsReadOnly());
    }
}
