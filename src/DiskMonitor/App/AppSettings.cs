using System.Text.Json;
using System.Text.Json.Serialization;
using DiskMonitor.Core.Events;
using DiskMonitor.Diagnostics;

namespace DiskMonitor.App;

/// <summary>How one severity tier notifies.</summary>
internal sealed class TierOptions
{
    /// <summary>Show a Windows toast notification.</summary>
    public bool Toast { get; set; } = true;

    /// <summary>Play the notification sound with the toast.</summary>
    public bool Sound { get; set; } = true;

    /// <summary>Keep the toast on screen until it is dismissed.</summary>
    public bool Sticky { get; set; }
}

internal sealed class AppSettings
{
    public const int UnsetPosition = int.MinValue;
    public static readonly int[] HistoryDayChoices = [7, 30, 90, 365];
    public static readonly int[] CoalesceMinuteChoices = [0, 5, 15, 60];
    public static readonly int[] HealthIntervalChoices = [5, 15, 30, 60];
    public static readonly int[] TemperatureChoices = [45, 50, 55, 60, 65, 70, 75];
    public static readonly int[] LowSpaceChoices = [0, 5, 10, 15, 20];
    public static readonly string[] RoutineScanChoices = ["Off", "Weekly", "Monthly"];
    public static readonly string[] ThemeChoices = ["System", "Light", "Dark"];

    // General
    public string Theme { get; set; } = "System";
    public bool StartMinimized { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool AutoCheckUpdates { get; set; } = true;

    // Alert tiers
    public TierOptions Critical { get; set; } = new() { Toast = true, Sound = true, Sticky = true };
    public TierOptions High { get; set; } = new() { Toast = true, Sound = true };
    public TierOptions Medium { get; set; } = new() { Toast = true, Sound = false };
    public TierOptions Low { get; set; } = new() { Toast = false, Sound = false };

    /// <summary>Minutes during which repeats of the same problem on the same device share one toast. 0 disables.</summary>
    public int CoalesceMinutes { get; set; } = 15;

    // Quiet hours: toasts go silently to the notification centre. Times are minutes after midnight.
    public bool QuietHoursEnabled { get; set; }
    public int QuietHoursStart { get; set; } = 22 * 60;
    public int QuietHoursEnd { get; set; } = 7 * 60;
    public bool QuietHoursAllowCritical { get; set; } = true;

    // Monitoring
    public int HistoryDays { get; set; } = 30;
    public bool IncludeChkdskEvents { get; set; } = true;
    public bool ReduceRemovableSeverity { get; set; } = true;
    public int HealthIntervalMinutes { get; set; } = 15;
    public int TemperatureWarningC { get; set; } = 60;

    /// <summary>Free-space percentage below which a volume raises a Low alert. 0 disables.</summary>
    public int LowSpacePercent { get; set; } = 5;

    /// <summary>Rule id to "Critical", "High", "Medium", "Low", or "Ignore".</summary>
    public Dictionary<string, string> RuleOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Proactive CHKDSK
    public bool AutoScanOnError { get; set; }
    public string RoutineScan { get; set; } = "Off";
    public bool NotifyCleanScan { get; set; }
    public bool ScanWithMoreResources { get; set; }

    // Window and view state
    public const int DefaultDrawerWidth = 440;
    public const int MinDrawerWidth = 360;
    public const int MaxDrawerWidth = 640;
    public static readonly string[] EventsGroupByChoices = ["Tier", "Drive", "None"];

    /// <summary>Backs the Events page's Repeats drop-down: true is "Combine repeats".</summary>
    public bool GroupSimilar { get; set; } = true;
    public string EventsGroupBy { get; set; } = "Tier";
    public int DrawerWidth { get; set; } = DefaultDrawerWidth;
    public string LastPage { get; set; } = "Overview";
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 800;
    public int WindowLeft { get; set; } = UnsetPosition;
    public int WindowTop { get; set; } = UnsetPosition;
    public bool WindowMaximized { get; set; }

    public TierOptions OptionsFor(Tier tier) => tier switch
    {
        Tier.Critical => Critical,
        Tier.High => High,
        Tier.Medium => Medium,
        _ => Low
    };

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile))
            {
                return new AppSettings();
            }

            AppSettings? settings = JsonSerializer.Deserialize(File.ReadAllText(AppPaths.SettingsFile), AppJsonContext.Default.AppSettings);
            return settings?.Sanitize() ?? new AppSettings();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Settings load", ex);
            return new AppSettings();
        }
    }

    public bool TrySave() => JsonFile.TryWrite(AppPaths.SettingsFile, this, AppJsonContext.Default.AppSettings, "Settings save");

    // Hand-edited or older files can carry values the UI cannot represent.
    internal AppSettings Sanitize()
    {
        Theme = ThemeChoices.Contains(Theme) ? Theme : ThemeChoices[0];
        Critical ??= new TierOptions { Sticky = true };
        High ??= new TierOptions();
        Medium ??= new TierOptions { Sound = false };
        Low ??= new TierOptions { Toast = false, Sound = false };
        HistoryDays = Nearest(HistoryDayChoices, HistoryDays);
        CoalesceMinutes = Nearest(CoalesceMinuteChoices, CoalesceMinutes);
        HealthIntervalMinutes = Nearest(HealthIntervalChoices, HealthIntervalMinutes);
        TemperatureWarningC = Nearest(TemperatureChoices, TemperatureWarningC);
        LowSpacePercent = Nearest(LowSpaceChoices, LowSpacePercent);
        RoutineScan = RoutineScanChoices.Contains(RoutineScan) ? RoutineScan : "Off";
        RuleOverrides = RuleOverrides is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(RuleOverrides, StringComparer.OrdinalIgnoreCase);
        QuietHoursStart = SnapToHalfHour(QuietHoursStart);
        QuietHoursEnd = SnapToHalfHour(QuietHoursEnd);
        EventsGroupBy = EventsGroupByChoices.FirstOrDefault(choice => choice.Equals(EventsGroupBy, StringComparison.OrdinalIgnoreCase)) ?? "Tier";
        DrawerWidth = Math.Clamp(DrawerWidth, MinDrawerWidth, MaxDrawerWidth);
        LastPage ??= "Overview";
        WindowWidth = Math.Clamp(WindowWidth, 400, 16384);
        WindowHeight = Math.Clamp(WindowHeight, 300, 16384);
        return this;
    }

    // Quiet hours are chosen in 30-minute steps; anything else is snapped into the day and onto a step.
    private static int SnapToHalfHour(int minutes)
    {
        int day = 24 * 60;
        int wrapped = ((minutes % day) + day) % day;
        return (int)Math.Round(wrapped / 30.0) * 30 % day;
    }

    private static int Nearest(int[] choices, int value) => choices.MinBy(choice => Math.Abs(choice - value));
}

/// <summary>Crash-safe JSON persistence: write a temporary file, then swap it into place.</summary>
internal static class JsonFile
{
    public static bool TryWrite<T>(string path, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo, string context)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string tempPath = path + ".tmp";
            using (FileStream stream = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, typeInfo);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, path, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            ErrorLog.Write(context, ex);
            return false;
        }
    }
}

// Source-generated metadata: no reflection warm-up on startup or save.
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(PersistedState))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
