using System.Globalization;

namespace DiskMonitor.Core;

/// <summary>Human-readable formatting shared by the UI and notifications.</summary>
internal static class Format
{
    private static readonly string[] ByteUnits = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string Bytes(double bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        int unit = 0;
        while (bytes >= 1024 && unit < ByteUnits.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }

        string number = bytes >= 100 || unit == 0 ? bytes.ToString("0", CultureInfo.CurrentCulture) : bytes.ToString("0.#", CultureInfo.CurrentCulture);
        return $"{number} {ByteUnits[unit]}";
    }

    /// <summary>"just now", "5 min ago", "3 h ago", "yesterday", then a date.</summary>
    public static string Ago(DateTime timeUtc, DateTime? nowUtc = null)
    {
        TimeSpan span = (nowUtc ?? DateTime.UtcNow) - timeUtc;
        if (span < TimeSpan.FromSeconds(60))
        {
            return "just now";
        }

        if (span < TimeSpan.FromMinutes(60))
        {
            return $"{(int)span.TotalMinutes} min ago";
        }

        if (span < TimeSpan.FromHours(24))
        {
            return $"{(int)span.TotalHours} h ago";
        }

        if (span < TimeSpan.FromDays(2))
        {
            return "yesterday";
        }

        if (span < TimeSpan.FromDays(14))
        {
            return $"{(int)span.TotalDays} days ago";
        }

        DateTime local = timeUtc.ToLocalTime();
        return local.Year == DateTime.Now.Year
            ? local.ToString("MMM d", CultureInfo.CurrentCulture)
            : local.ToString("MMM d, yyyy", CultureInfo.CurrentCulture);
    }

    public static string DateTimeLong(DateTime timeUtc) =>
        timeUtc.ToLocalTime().ToString("ddd, MMM d yyyy · h:mm:ss tt", CultureInfo.CurrentCulture);

    public static string DateTimeShort(DateTime timeUtc)
    {
        DateTime local = timeUtc.ToLocalTime();
        return local.Date == DateTime.Now.Date
            ? local.ToString("h:mm tt", CultureInfo.CurrentCulture)
            : local.ToString("MMM d · h:mm tt", CultureInfo.CurrentCulture);
    }

    public static string Hours(long hours)
    {
        if (hours < 48)
        {
            return $"{hours:N0} h";
        }

        double days = hours / 24d;
        return days < 730 ? $"{days:N0} days" : $"{days / 365.25:0.0} years";
    }

    public static string Count(long value, string singular, string? plural = null) =>
        value == 1 ? $"1 {singular}" : $"{value:N0} {plural ?? singular + "s"}";

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
