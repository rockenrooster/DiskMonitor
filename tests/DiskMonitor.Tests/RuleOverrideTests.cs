using DiskMonitor.Core.Events;
using DiskMonitor.UI.Pages;

namespace DiskMonitor.Tests;

/// <summary>SET-05: what the Rules section writes for a switch and tier, and what it shows for a value.</summary>
public sealed class RuleOverrideTests
{
    private static readonly Tier[] Tiers = [Tier.Critical, Tier.High, Tier.Medium, Tier.Low];

    [Fact]
    public void DefaultsWriteNothing()
    {
        Assert.Null(RulesSection.OverrideFor(true, Tier.High, true, Tier.High));
        Assert.Null(RulesSection.OverrideFor(false, Tier.Low, false, Tier.Low));
        Assert.Null(RulesSection.OverrideFor(false, Tier.Low, false, Tier.Critical));
    }

    [Fact]
    public void ChangesWriteTheTierOrIgnore()
    {
        Assert.Equal("Low", RulesSection.OverrideFor(true, Tier.Medium, true, Tier.Low));
        Assert.Equal(RuleCatalog.IgnoreValue, RulesSection.OverrideFor(true, Tier.Medium, false, Tier.Medium));
        Assert.Equal(RuleCatalog.IgnoreValue, RulesSection.OverrideFor(true, Tier.Medium, false, Tier.Low));

        // A rule that is off by default needs its tier written even when it equals the built-in one.
        Assert.Equal("Low", RulesSection.OverrideFor(false, Tier.Low, true, Tier.Low));
        Assert.Equal("High", RulesSection.OverrideFor(false, Tier.Low, true, Tier.High));
    }

    [Fact]
    public void EveryChoiceRoundTripsThroughTheStoredValue()
    {
        foreach (bool defaultOn in new[] { true, false })
        {
            foreach (Tier defaultTier in Tiers)
            {
                foreach (bool on in new[] { true, false })
                {
                    foreach (Tier tier in Tiers)
                    {
                        string? value = RulesSection.OverrideFor(defaultOn, defaultTier, on, tier);
                        (bool shownOn, Tier shownTier) = RulesSection.StateFor(defaultOn, defaultTier, value);
                        Assert.Equal(on, shownOn);
                        if (on)
                        {
                            Assert.Equal(tier, shownTier);
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void StoredValuesMeanTheSameToTheClassifier()
    {
        EventRule rule = RuleCatalog.Find("disk:153") ?? throw new InvalidOperationException("disk:153 missing");
        foreach (bool on in new[] { true, false })
        {
            foreach (Tier tier in Tiers)
            {
                string? value = RulesSection.OverrideFor(!rule.Ignore, rule.Tier, on, tier);
                Dictionary<string, string> overrides = new(StringComparer.OrdinalIgnoreCase);
                if (value is not null)
                {
                    overrides[rule.Id] = value;
                }

                Assert.Equal(on ? tier : null, RuleCatalog.ResolveTier(rule, overrides, out _));
            }
        }
    }

    [Fact]
    public void UnknownValuesShowTheDefault()
    {
        Assert.Equal((true, Tier.High), RulesSection.StateFor(true, Tier.High, "Severe"));
        Assert.Equal((false, Tier.Low), RulesSection.StateFor(false, Tier.Low, "7"));
        Assert.Equal((false, Tier.High), RulesSection.StateFor(true, Tier.High, "ignore"));
    }
}
