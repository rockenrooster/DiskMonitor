using System.Globalization;
using DiskMonitor.App;

namespace DiskMonitor.Core;

/// <summary>
/// The app's notion of "now". Demo mode, the screen harness and tests pin it so every render and
/// every formatted date is the same from run to run.
/// </summary>
internal static class Clock
{
    private static long _pinnedTicks;

    public static DateTime UtcNow
    {
        get
        {
            long ticks = Interlocked.Read(ref _pinnedTicks);
            return ticks == 0 ? DateTime.UtcNow : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    public static bool IsPinned => Interlocked.Read(ref _pinnedTicks) != 0;

    /// <summary>Pins "now" to a UTC moment; null returns to the real clock.</summary>
    public static void Pin(DateTime? utc) =>
        Interlocked.Exchange(ref _pinnedTicks, utc is { } value ? value.ToUniversalTime().Ticks : 0);
}

/// <summary>Human-readable formatting shared by the UI and notifications (subplan 02, Formats).</summary>
internal static class Format
{
    /// <summary>Binary units, one decimal from MB up, none for bytes and KB: "412 KB", "31.2 MB", "1.8 TB".</summary>
    public static string Size(double bytes)
    {
        if (double.IsNaN(bytes) || bytes < 0)
        {
            bytes = 0;
        }

        int unit = 0;
        while (bytes >= 1024 && unit < Strings.SizeUnits.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }

        // Rounding can carry into the next unit ("1024.0 KB"); move up instead.
        if (unit >= 2 && Math.Round(bytes, 1) >= 1024 && unit < Strings.SizeUnits.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }
        else if (unit < 2 && Math.Round(bytes) >= 1024 && unit < Strings.SizeUnits.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }

        string number = unit < 2
            ? Math.Round(bytes).ToString("0", CultureInfo.CurrentCulture)
            : bytes.ToString("0.0", CultureInfo.CurrentCulture);
        return number + " " + Strings.SizeUnits[unit];
    }

    /// <summary>Whole percent: "97%".</summary>
    public static string Percent(double value) => Math.Round(value).ToString("0", CultureInfo.CurrentCulture) + "%";

    /// <summary>Whole degrees: "41 °C".</summary>
    public static string Temperature(double celsius) => Math.Round(celsius).ToString("0", CultureInfo.CurrentCulture) + Strings.DegreesSuffix;

    /// <summary>Culture grouping: "12,507".</summary>
    public static string Number(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>Current culture short time: "14:02".</summary>
    public static string TimeOfDay(DateTime utc) => utc.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);

    /// <summary>
    /// "Today 03:12", "Yesterday 03:00", the weekday for the last six days ("Mon 21:40"), then
    /// "8 Nov 03:00", with the year instead of the time when not the current year ("14 Mar 2026").
    /// In a sentence "Today" and "Yesterday" are written in lower case.
    /// </summary>
    public static string RecentDate(DateTime utc, bool inSentence, DateTime? nowUtc = null)
    {
        DateTime local = utc.ToLocalTime();
        DateTime now = (nowUtc ?? Clock.UtcNow).ToLocalTime();
        int daysAgo = (now.Date - local.Date).Days;
        string time = local.ToString("t", CultureInfo.CurrentCulture);
        if (daysAgo == 0)
        {
            return (inSentence ? Strings.TodayLower : Strings.Today) + " " + time;
        }

        if (daysAgo == 1)
        {
            return (inSentence ? Strings.YesterdayLower : Strings.Yesterday) + " " + time;
        }

        // The weekday names the last six days, and the next six for scheduled moments.
        if (daysAgo is > 1 and <= 6 or < 0 and >= -6)
        {
            return local.ToString("ddd", CultureInfo.CurrentCulture) + " " + time;
        }

        return local.Year == now.Year
            ? local.ToString(Strings.DayMonthPattern, CultureInfo.CurrentCulture) + " " + time
            : local.ToString(Strings.DayMonthYearPattern, CultureInfo.CurrentCulture);
    }

    /// <summary>Culture long date, a middle dot, then the time with seconds: "Monday, 16 November 2026 · 14:02:37".</summary>
    public static string ExactTime(DateTime utc)
    {
        DateTime local = utc.ToLocalTime();
        return local.ToString("D", CultureInfo.CurrentCulture) + Strings.Separator + local.ToString("T", CultureInfo.CurrentCulture);
    }

    /// <summary>"45 s", "37 min", "1 h 48 min"; an estimate reads "About 3 min".</summary>
    public static string Duration(TimeSpan span, bool estimate)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        string text;
        if (span.TotalSeconds < 60)
        {
            text = Strings.DurationSeconds((int)Math.Round(span.TotalSeconds));
        }
        else if (span.TotalMinutes < 60)
        {
            text = Strings.DurationMinutes((int)Math.Round(span.TotalMinutes));
        }
        else
        {
            int hours = (int)span.TotalHours;
            int minutes = (int)Math.Round(span.TotalMinutes - hours * 60);
            if (minutes == 60)
            {
                hours++;
                minutes = 0;
            }

            text = Strings.DurationHours(hours, minutes);
        }

        return estimate ? Strings.About(text) : text;
    }

    /// <summary>Under 48 h "36 h"; under two years "142 days"; otherwise "3.0 years".</summary>
    public static string DriveAge(long hours)
    {
        if (hours < 48)
        {
            return Strings.AgeHours(Math.Max(0, hours));
        }

        double days = hours / 24d;
        return days < 730
            ? Strings.AgeDays((long)Math.Floor(days))
            : Strings.AgeYears(days / 365.25);
    }

    /// <summary>"1 event", "12,507 events".</summary>
    public static string Count(long value, string singular, string plural) =>
        value == 1 ? "1 " + singular : Number(value) + " " + plural;

    /// <summary>Collapses whitespace and trims to a single line of at most <paramref name="max"/> characters.</summary>
    public static string SingleLine(string text, int max)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        Span<char> buffer = text.Length <= 512 ? stackalloc char[text.Length] : new char[text.Length];
        int length = 0;
        bool pendingSpace = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                pendingSpace = length > 0;
                continue;
            }

            if (pendingSpace)
            {
                buffer[length++] = ' ';
                pendingSpace = false;
            }

            buffer[length++] = c;
        }

        string result = new(buffer[..length]);
        return result.Length <= max ? result : result[..Math.Max(0, max - 1)].TrimEnd() + "…";
    }
}
