using System.Text.Json;
using DiskMonitor.App;

namespace DiskMonitor.Tests;

/// <summary>Sanitising clamps every field; unknown JSON properties are ignored.</summary>
public sealed class SettingsTests
{
    [Fact]
    public void SanitizeClampsEveryField()
    {
        AppSettings settings = new()
        {
            Theme = "Neon",
            HistoryDays = 45,
            CoalesceMinutes = 7,
            HealthIntervalMinutes = 1000,
            TemperatureWarningC = 12,
            LowSpacePercent = 3,
            RoutineScan = "Daily",
            QuietHoursStart = 1500,
            QuietHoursEnd = -50,
            EventsGroupBy = "drive",
            DrawerWidth = 9999,
            WindowWidth = 10,
            WindowHeight = 100_000,
            Critical = null!,
            RuleOverrides = null!
        };

        settings.Sanitize();
        Assert.Equal("System", settings.Theme);
        Assert.Equal(30, settings.HistoryDays);
        Assert.Equal(5, settings.CoalesceMinutes);
        Assert.Equal(60, settings.HealthIntervalMinutes);
        Assert.Equal(45, settings.TemperatureWarningC);
        Assert.Equal(5, settings.LowSpacePercent);
        Assert.Equal("Off", settings.RoutineScan);
        Assert.Equal(60, settings.QuietHoursStart);
        Assert.Equal(1380, settings.QuietHoursEnd);
        Assert.Equal("Drive", settings.EventsGroupBy);
        Assert.Equal(640, settings.DrawerWidth);
        Assert.Equal(400, settings.WindowWidth);
        Assert.Equal(16384, settings.WindowHeight);
        Assert.NotNull(settings.Critical);
        Assert.NotNull(settings.RuleOverrides);
    }

    [Fact]
    public void DefaultsMatchThePlans()
    {
        AppSettings settings = new();
        Assert.Equal(1280, settings.WindowWidth);
        Assert.Equal(800, settings.WindowHeight);
        Assert.Equal(440, settings.DrawerWidth);
        Assert.Equal("Tier", settings.EventsGroupBy);
        Assert.False(settings.QuietHoursEnabled);
        Assert.Equal(1320, settings.QuietHoursStart);
        Assert.Equal(420, settings.QuietHoursEnd);
        Assert.True(settings.QuietHoursAllowCritical);
        Assert.True(settings.Critical.Toast && settings.Critical.Sound && settings.Critical.Sticky);
        Assert.True(settings.High.Toast && settings.High.Sound && !settings.High.Sticky);
        Assert.True(settings.Medium.Toast && !settings.Medium.Sound && !settings.Medium.Sticky);
        Assert.False(settings.Low.Toast || settings.Low.Sound || settings.Low.Sticky);
    }

    [Fact]
    public void UnknownJsonPropertiesAreIgnored()
    {
        const string json = """{ "Theme": "Dark", "NoSuchSetting": 42, "Nested": { "x": [1, 2] }, "HistoryDays": 90 }""";
        AppSettings settings = JsonSerializer.Deserialize(json, AppJsonContext.Default.AppSettings)!.Sanitize();
        Assert.Equal("Dark", settings.Theme);
        Assert.Equal(90, settings.HistoryDays);
    }

    [Fact]
    public void RuleOverridesAreCaseInsensitiveAfterLoad()
    {
        const string json = """{ "RuleOverrides": { "DISK:7": "Low" } }""";
        AppSettings settings = JsonSerializer.Deserialize(json, AppJsonContext.Default.AppSettings)!.Sanitize();
        Assert.Equal("Low", settings.RuleOverrides["disk:7"]);
    }
}
