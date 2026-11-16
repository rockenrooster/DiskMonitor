using DiskMonitor.Core;
using static DiskMonitor.Tests.TestSupport;

namespace DiskMonitor.Tests;

/// <summary>Every example in the Formats table of subplan 02, with a fixed "now" (CORE-12).</summary>
public sealed class FormatTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(412 * 1024, "412 KB")]
    [InlineData(31.2 * 1024 * 1024, "31.2 MB")]
    [InlineData(1.8 * 1024 * 1024 * 1024 * 1024, "1.8 TB")]
    [InlineData(1023.97 * 1024 * 1024, "1.0 GB")]
    public void SizeUsesBinaryUnitsWithOneDecimalFromMegabytes(double bytes, string expected) =>
        WithPlanCulture(() => Assert.Equal(expected, Format.Size(bytes)));

    [Fact]
    public void PercentTemperatureAndCount() => WithPlanCulture(() =>
    {
        Assert.Equal("97%", Format.Percent(97.4));
        Assert.Equal("41 °C", Format.Temperature(40.6));
        Assert.Equal("12,507", Format.Number(12_507));
        Assert.Equal("1 event", Format.Count(1, "event", "events"));
        Assert.Equal("12,507 events", Format.Count(12_507, "event", "events"));
        Assert.Equal("0 events", Format.Count(0, "event", "events"));
    });

    [Fact]
    public void RecentDateFollowsTheTable() => WithPlanCulture(() =>
    {
        DateTime now = NowUtc;
        Assert.Equal("Today 03:12", Format.RecentDate(LocalToUtc(2026, 11, 16, 3, 12), false, now));
        Assert.Equal("Yesterday 03:00", Format.RecentDate(LocalToUtc(2026, 11, 15, 3, 0), false, now));
        Assert.Equal("Wed 21:40", Format.RecentDate(LocalToUtc(2026, 11, 11, 21, 40), false, now));
        Assert.Equal("Tue 21:40", Format.RecentDate(LocalToUtc(2026, 11, 10, 21, 40), false, now));
        Assert.Equal("9 Nov 03:00", Format.RecentDate(LocalToUtc(2026, 11, 9, 3, 0), false, now));
        Assert.Equal("8 Nov 03:00", Format.RecentDate(LocalToUtc(2026, 11, 8, 3, 0), false, now));
        Assert.Equal("14 Mar 2025", Format.RecentDate(LocalToUtc(2025, 3, 14, 3, 0), false, now));
    });

    [Fact]
    public void RecentDateInASentenceIsLowerCase() => WithPlanCulture(() =>
    {
        DateTime now = NowUtc;
        Assert.Equal("today 03:12", Format.RecentDate(LocalToUtc(2026, 11, 16, 3, 12), true, now));
        Assert.Equal("yesterday 03:00", Format.RecentDate(LocalToUtc(2026, 11, 15, 3, 0), true, now));
        Assert.Equal("Wed 21:40", Format.RecentDate(LocalToUtc(2026, 11, 11, 21, 40), true, now));
    });

    [Fact]
    public void RecentDateNamesTheWeekdayForScheduledMoments() => WithPlanCulture(() =>
    {
        Assert.Equal("Thu 03:00", Format.RecentDate(LocalToUtc(2026, 11, 19, 3, 0), false, NowUtc));
        Assert.Equal("30 Nov 03:00", Format.RecentDate(LocalToUtc(2026, 11, 30, 3, 0), false, NowUtc));
    });

    [Fact]
    public void ExactTimeIsLongDateDotTimeWithSeconds() => WithPlanCulture(() =>
        Assert.Equal("Monday, 16 November 2026 · 14:02:37", Format.ExactTime(LocalToUtc(2026, 11, 16, 14, 2, 37))));

    [Theory]
    [InlineData(45, false, "45 s")]
    [InlineData(37 * 60, false, "37 min")]
    [InlineData(108 * 60, false, "1 h 48 min")]
    [InlineData(120 * 60, false, "2 h")]
    [InlineData(3 * 60, true, "About 3 min")]
    public void Duration(int seconds, bool estimate, string expected) =>
        WithPlanCulture(() => Assert.Equal(expected, Format.Duration(TimeSpan.FromSeconds(seconds), estimate)));

    [Theory]
    [InlineData(36, "36 h")]
    [InlineData(47, "47 h")]
    [InlineData(142 * 24, "142 days")]
    [InlineData(26_280, "3.0 years")]
    public void DriveAge(long hours, string expected) =>
        WithPlanCulture(() => Assert.Equal(expected, Format.DriveAge(hours)));

    [Fact]
    public void SingleLineCollapsesWhitespaceAndCuts()
    {
        Assert.Equal("a b c", Format.SingleLine("  a\r\n b\t c ", 20));
        Assert.Equal("abcd…", Format.SingleLine("abcdefgh", 5));
    }
}
