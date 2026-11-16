using System.Text.Json.Serialization;

namespace DiskMonitor.Core.Events;

/// <summary>Severity tier of a disk event. Ordered so a higher value is more urgent.</summary>
internal enum Tier
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

/// <summary>What kind of problem an event describes.</summary>
internal enum ErrorKind
{
    /// <summary>Hardware, media, cabling, or transport.</summary>
    Physical,

    /// <summary>File system structure.</summary>
    Logical,

    /// <summary>Drive self-reported health (S.M.A.R.T. / NVMe).</summary>
    Health,

    /// <summary>Free space.</summary>
    Capacity,

    Other
}

/// <summary>One disk problem, either read from a Windows event log or raised by a health check.</summary>
internal sealed record DiskEvent
{
    /// <summary>Stable identity, unique across logs and runs.</summary>
    public required string Key { get; init; }
    public required DateTime TimeUtc { get; init; }
    public required Tier Tier { get; init; }
    public required ErrorKind Kind { get; init; }

    /// <summary>The rule that classified this event; the unit users re-tier or mute.</summary>
    public required string RuleId { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }
    public string Explanation { get; init; } = "";

    /// <summary>Why the problem matters: from the rule, or the default for its kind.</summary>
    public string WhyItMatters { get; init; } = "";
    public string Advice { get; init; } = "";
    public string Log { get; init; } = "";
    public string Provider { get; init; } = "";
    public int EventId { get; init; }
    public long RecordId { get; init; }
    public int Level { get; init; }
    public int? DiskNumber { get; init; }

    /// <summary>Drive letter with colon, for example "C:".</summary>
    public string? Volume { get; init; }

    /// <summary>Human-readable device, for example "Disk 2 · Samsung SSD 870 (D:)".</summary>
    public string DeviceText { get; init; } = "";

    /// <summary>True when a file system scan of <see cref="Volume"/> is the sensible next step.</summary>
    public bool SuggestScan { get; init; }

    /// <summary>True for events Disk Monitor raised itself; these exist in no Windows log.</summary>
    public bool Synthetic { get; init; }

    /// <summary>Events with the same group key are one recurring problem on one device.</summary>
    [JsonIgnore]
    public string GroupKey => $"{RuleId}|{DiskNumber}|{Volume}";

    [JsonIgnore]
    public DateTime TimeLocal => TimeUtc.ToLocalTime();
}

internal static class TierExtensions
{
    public static readonly Tier[] Descending = [Tier.Critical, Tier.High, Tier.Medium, Tier.Low];

    public static readonly ErrorKind[] Kinds = [ErrorKind.Physical, ErrorKind.Logical, ErrorKind.Health, ErrorKind.Capacity, ErrorKind.Other];

    public static string Label(this Tier tier) => App.Strings.TierName(tier);

    public static Tier Lower(this Tier tier) => tier == Tier.Low ? Tier.Low : tier - 1;

    public static string Label(this ErrorKind kind) => App.Strings.KindName(kind);
}
