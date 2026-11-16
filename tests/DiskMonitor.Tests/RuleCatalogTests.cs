using DiskMonitor.Core.Events;

namespace DiskMonitor.Tests;

/// <summary>The classification table: catalog rules, NTFS 98, CHKDSK outcomes, fallback caps, overrides.</summary>
public sealed class RuleCatalogTests
{
    private static readonly Dictionary<string, string> NoOverrides = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> ProviderForFamily = new()
    {
        ["disk"] = "disk",
        ["ntfs"] = "Microsoft-Windows-Ntfs",
        ["refs"] = "Microsoft-Windows-ReFS",
        ["fat"] = "exFAT",
        ["volsnap"] = "volsnap",
        ["volmgr"] = "volmgr",
        ["partmgr"] = "partmgr",
        ["diskdiag"] = "Microsoft-Windows-DiskDiagnostic",
        ["port"] = "storahci"
    };

    public static IEnumerable<object[]> LogRules() => RuleCatalog.All
        .Where(rule => int.TryParse(rule.Id[(rule.Id.IndexOf(':') + 1)..], out _))
        .Where(rule => ProviderForFamily.ContainsKey(rule.Id[..rule.Id.IndexOf(':')]))
        .Select(rule => new object[] { rule.Id });

    [Theory]
    [MemberData(nameof(LogRules))]
    public void EveryLogRuleMatchesASampleRecord(string ruleId)
    {
        EventRule rule = RuleCatalog.Find(ruleId)!;
        string family = ruleId[..ruleId.IndexOf(':')];
        int id = int.Parse(ruleId[(ruleId.IndexOf(':') + 1)..]);
        Classification? result = RuleCatalog.Classify(new RawEvent("System", ProviderForFamily[family], id, 2, "sample"), NoOverrides);
        if (rule.Ignore)
        {
            Assert.Null(result);
            return;
        }

        Assert.NotNull(result);
        Assert.Equal(ruleId, result.Value.Rule.Id);
        Assert.Equal(rule.Tier, result.Value.Tier);
        Assert.False(result.Value.Overridden);
    }

    [Theory]
    [InlineData("The file system structure on volume C: is healthy.", null)]
    [InlineData("Volume C: needs to be taken offline to perform a Full Chkdsk.", "ntfs:98-full")]
    [InlineData("A corruption was found. Run Spot Fix to repair it.", "ntfs:98-spotfix")]
    [InlineData("Volume D: needs to be taken offline for a short time.", "ntfs:98-spotfix")]
    [InlineData("An Online Scan should be run on volume E:.", "ntfs:98-scan")]
    public void NtfsVolumeStateIsSplitByText(string message, string? expected)
    {
        Classification? result = RuleCatalog.Classify(new RawEvent("System", "Microsoft-Windows-Ntfs", 98, 4, message), NoOverrides);
        Assert.Equal(expected, result?.Rule.Id);
    }

    [Theory]
    [InlineData("Windows has scanned the file system and found no problems. 0 KB in bad sectors.", null)]
    [InlineData("Windows has made corrections to the file system.", "chkdsk:fixed")]
    [InlineData("Windows has scanned the file system and found problems.", "chkdsk:problems")]
    [InlineData("Errors found. CHKDSK cannot continue in read-only mode.", "chkdsk:problems")]
    [InlineData("found no problems. 128 KB in bad sectors.", "chkdsk:bad-sectors")]
    [InlineData("Some unrelated informational text.", null)]
    public void ChkdskResultsReportOnlyProblems(string message, string? expected)
    {
        Classification? result = RuleCatalog.Classify(new RawEvent("Application", "Chkdsk", 26226, 4, message), NoOverrides);
        Assert.Equal(expected, result?.Rule.Id);
    }

    [Fact]
    public void WininitOnlyCountsEvent1001()
    {
        Assert.Null(RuleCatalog.Classify(new RawEvent("Application", "Wininit", 1002, 4, "found problems"), NoOverrides));
        Assert.Equal("chkdsk:problems", RuleCatalog.Classify(new RawEvent("Application", "Wininit", 1001, 4, "found problems"), NoOverrides)?.Rule.Id);
    }

    [Theory]
    [InlineData("disk", 999, 1, (int)Tier.Critical)]
    [InlineData("disk", 999, 3, (int)Tier.Medium)]
    [InlineData("volsnap", 999, 1, (int)Tier.Low)]
    [InlineData("volmgr", 999, 2, (int)Tier.Low)]
    [InlineData("partmgr", 999, 3, (int)Tier.Low)]
    [InlineData("exFAT", 999, 1, (int)Tier.High)]
    [InlineData("storahci", 999, 1, (int)Tier.High)]
    [InlineData("SomeVendorRaid", 999, 2, (int)Tier.High)]
    public void FallbackFollowsLevelAndFamilyCap(string provider, int id, int level, int expectedTier)
    {
        Classification? result = RuleCatalog.Classify(new RawEvent("System", provider, id, level, "text"), NoOverrides);
        Assert.Equal((Tier)expectedTier, result?.Tier);
    }

    [Fact]
    public void FallbackIgnoresInformationalRecords() =>
        Assert.Null(RuleCatalog.Classify(new RawEvent("System", "disk", 999, 4, "text"), NoOverrides));

    [Fact]
    public void OverridesReTierAndIgnore()
    {
        RawEvent badBlock = new("System", "disk", 7, 2, "bad block");
        Dictionary<string, string> low = new(StringComparer.OrdinalIgnoreCase) { ["disk:7"] = "Low" };
        Classification? moved = RuleCatalog.Classify(badBlock, low);
        Assert.Equal(Tier.Low, moved?.Tier);
        Assert.True(moved?.Overridden);

        Dictionary<string, string> ignore = new(StringComparer.OrdinalIgnoreCase) { ["disk:7"] = RuleCatalog.IgnoreValue };
        Assert.Null(RuleCatalog.Classify(badBlock, ignore));

        Dictionary<string, string> bringBack = new(StringComparer.OrdinalIgnoreCase) { ["volmgr:162"] = "Medium" };
        Assert.Equal(Tier.Medium, RuleCatalog.Classify(new RawEvent("System", "volmgr", 162, 4, "dump"), bringBack)?.Tier);
    }

    [Fact]
    public void ProviderSetCoversFamiliesAndDiscoveredDrivers()
    {
        HashSet<string> providers = RuleCatalog.BuildSystemProviderSet(["MyVendorSas"]);
        Assert.Contains("disk", providers);
        Assert.Contains("Microsoft-Windows-Ntfs", providers);
        Assert.Contains("stornvme", providers);
        Assert.Contains("MyVendorSas", providers);
        Assert.DoesNotContain("Chkdsk", providers);
    }
}
