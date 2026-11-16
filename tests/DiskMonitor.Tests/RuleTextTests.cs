using DiskMonitor.Core.Events;

namespace DiskMonitor.Tests;

/// <summary>Every rule and the fallback explain what, why and what to do (CORE-09).</summary>
public sealed class RuleTextTests
{
    public static IEnumerable<object[]> AllRules() => RuleCatalog.All.Select(rule => new object[] { rule.Id });

    [Theory]
    [MemberData(nameof(AllRules))]
    public void EveryRuleHasAllFourTexts(string ruleId)
    {
        EventRule rule = RuleCatalog.Find(ruleId)!;
        AssertTexts(rule);
    }

    [Fact]
    public void TheFallbackHasAllFourTexts()
    {
        Classification? fallback = RuleCatalog.Classify(
            new RawEvent("System", "disk", 4242, 2, "text"),
            new Dictionary<string, string>());
        Assert.NotNull(fallback);
        AssertTexts(fallback.Value.Rule);
    }

    [Fact]
    public void WhyItMattersDefaultsByKind()
    {
        EventRule physical = RuleCatalog.Find("disk:7")!;
        Assert.StartsWith("A drive or connection", RuleCatalog.WhyItMatters(physical));
        EventRule repaired = RuleCatalog.Find("ntfs:130")!;
        Assert.NotNull(repaired.Why);
        Assert.Equal(repaired.Why, RuleCatalog.WhyItMatters(repaired));
    }

    private static void AssertTexts(EventRule rule)
    {
        Assert.False(string.IsNullOrWhiteSpace(rule.Title), $"{rule.Id} title");
        Assert.False(string.IsNullOrWhiteSpace(rule.Explanation), $"{rule.Id} explanation");
        Assert.False(string.IsNullOrWhiteSpace(RuleCatalog.WhyItMatters(rule)), $"{rule.Id} reason");
        Assert.False(string.IsNullOrWhiteSpace(rule.Advice), $"{rule.Id} advice");
        Assert.EndsWith(".", rule.Explanation.TrimEnd());
        Assert.EndsWith(".", rule.Advice.TrimEnd());
    }
}
