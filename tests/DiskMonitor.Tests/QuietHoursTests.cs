using DiskMonitor.Core.Notifications;

namespace DiskMonitor.Tests;

/// <summary>Quiet-hour windows within a day, across midnight, and start equal to end (CORE-08).</summary>
public sealed class QuietHoursTests
{
    [Theory]
    [InlineData(60, 120, 59, false)]
    [InlineData(60, 120, 60, true)]
    [InlineData(60, 120, 119, true)]
    [InlineData(60, 120, 120, false)]
    public void WindowInsideOneDay(int start, int end, int now, bool expected) =>
        Assert.Equal(expected, NotificationService.IsWithinQuietHours(start, end, now));

    [Theory]
    [InlineData(1320, 420, 1319, false)]
    [InlineData(1320, 420, 1320, true)]
    [InlineData(1320, 420, 1439, true)]
    [InlineData(1320, 420, 0, true)]
    [InlineData(1320, 420, 419, true)]
    [InlineData(1320, 420, 420, false)]
    [InlineData(1320, 420, 720, false)]
    public void WindowAcrossMidnight(int start, int end, int now, bool expected) =>
        Assert.Equal(expected, NotificationService.IsWithinQuietHours(start, end, now));

    [Theory]
    [InlineData(0)]
    [InlineData(600)]
    [InlineData(1439)]
    public void StartEqualToEndIsOff(int now) =>
        Assert.False(NotificationService.IsWithinQuietHours(480, 480, now));
}
