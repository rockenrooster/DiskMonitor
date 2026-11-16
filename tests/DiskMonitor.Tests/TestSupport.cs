using System.Globalization;
using DiskMonitor.Core;

namespace DiskMonitor.Tests;

/// <summary>Shared fixtures: a fixed culture and the fixed "now" that demo mode and the harness use.</summary>
internal static class TestSupport
{
    /// <summary>Monday 16 November 2026, 14:20 local time (the moment MediaVerifier's mockups use).</summary>
    public static readonly DateTime NowLocal = new(2026, 11, 16, 14, 20, 0, DateTimeKind.Local);

    public static DateTime NowUtc => NowLocal.ToUniversalTime();

    public static DateTime LocalToUtc(int year, int month, int day, int hour, int minute, int second = 0) =>
        new DateTime(year, month, day, hour, minute, second, DateTimeKind.Local).ToUniversalTime();

    /// <summary>English with 24-hour times and a long date with the weekday, as in the plans' examples.</summary>
    public static CultureInfo PlanCulture()
    {
        CultureInfo culture = (CultureInfo)CultureInfo.GetCultureInfo("en-GB").Clone();
        culture.DateTimeFormat.LongDatePattern = "dddd, d MMMM yyyy";
        culture.DateTimeFormat.ShortTimePattern = "HH:mm";
        culture.DateTimeFormat.LongTimePattern = "HH:mm:ss";
        culture.NumberFormat.NumberGroupSeparator = ",";
        culture.NumberFormat.NumberDecimalSeparator = ".";
        return culture;
    }

    /// <summary>Runs <paramref name="action"/> with the plan culture and a pinned clock, then restores both.</summary>
    public static void WithPlanCulture(Action action)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = PlanCulture();
        try
        {
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    public static string RepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "DiskMonitor.sln")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        return directory ?? throw new InvalidOperationException("Repository root not found.");
    }
}
