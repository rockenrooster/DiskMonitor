using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using DiskMonitor.UI.Theming;

namespace DiskMonitor.Tests;

/// <summary>
/// Token values and contrast pairs from design/tokens.md (UI-01, UI-02), and icon geometry from
/// design/icons.md. The tables are copied into the test project (Design/) because docs/plans stays local;
/// when the plans are present, the copies must be identical to them.
/// </summary>
public sealed partial class DesignTableTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Design", name);

    [Theory]
    [InlineData("tokens.md")]
    [InlineData("icons.md")]
    public void FixtureMatchesThePlans(string name)
    {
        string plans = Path.Combine(TestSupport.RepositoryRoot(), "docs", "plans", "design", name);
        if (File.Exists(plans))
        {
            Assert.Equal(File.ReadAllText(plans).ReplaceLineEndings(), File.ReadAllText(Fixture(name)).ReplaceLineEndings());
        }
    }

    public static IEnumerable<object[]> ColourRows()
    {
        foreach (string line in File.ReadLines(Fixture("tokens.md")))
        {
            Match match = TokenRow().Match(line);
            if (match.Success)
            {
                yield return [match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value];
            }
        }
    }

    [Theory]
    [MemberData(nameof(ColourRows))]
    public void TokenValuesEqualTheTable(string token, string dark, string light)
    {
        PropertyInfo property = typeof(ThemeTokens).GetProperty(token, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new Xunit.Sdk.XunitException($"ThemeTokens has no {token}");
        Assert.Equal(Parse(dark), ((Color)property.GetValue(ThemeTokens.Dark)!).ToArgb());
        Assert.Equal(Parse(light), ((Color)property.GetValue(ThemeTokens.Light)!).ToArgb());
    }

    [Fact]
    public void EveryTokenOfTheTableIsCovered() => Assert.Equal(38, ColourRows().Count());

    public static IEnumerable<object[]> ContrastPairs()
    {
        string? theme = null;
        foreach (string line in File.ReadLines(Fixture("tokens.md")))
        {
            if (line.StartsWith("### Dark", StringComparison.Ordinal))
            {
                theme = "Dark";
            }
            else if (line.StartsWith("### Light", StringComparison.Ordinal))
            {
                theme = "Light";
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal) && !line.StartsWith("## Contrast", StringComparison.Ordinal))
            {
                theme = null;
            }

            Match match = ContrastRow().Match(line);
            if (theme is not null && match.Success)
            {
                yield return [theme, match.Groups[1].Value, match.Groups[2].Value, double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture)];
            }
        }
    }

    [Theory]
    [MemberData(nameof(ContrastPairs))]
    public void ContrastMeetsItsRequirement(string theme, string foreground, string background, double required)
    {
        ThemeTokens tokens = theme == "Dark" ? ThemeTokens.Dark : ThemeTokens.Light;
        double ratio = Contrast(Get(tokens, foreground), Get(tokens, background));
        Assert.True(ratio >= required, $"{theme} {foreground} on {background}: {ratio:0.00} < {required}");
    }

    [Fact]
    public void AllContrastPairsAreTested() => Assert.True(ContrastPairs().Count() >= 140);

    public static IEnumerable<object[]> Icons() => Enum.GetValues<IconName>().Select(icon => new object[] { icon.ToString() });

    [Theory]
    [MemberData(nameof(Icons))]
    public void EveryIconHasGeometryThatParses(string name)
    {
        IconName icon = Enum.Parse<IconName>(name);
        Assert.True(IconGeometry.Source.ContainsKey(icon));
        (System.Drawing.Drawing2D.GraphicsPath stroke, System.Drawing.Drawing2D.GraphicsPath? fill) = IconGeometry.Build(icon, 1.5f);
        Assert.True(stroke.PointCount > 0);
        RectangleF bounds = stroke.GetBounds();
        Assert.InRange(bounds.Left, -0.01f, 24.01f);
        Assert.InRange(bounds.Right, 0, 24.01f);
        stroke.Dispose();
        fill?.Dispose();
    }

    [Fact]
    public void EveryIconInTheUsageTableExists()
    {
        HashSet<string> keys = Enum.GetValues<IconName>().Select(IconGeometry.Key).ToHashSet();
        bool inUsage = false;
        List<string> missing = [];
        foreach (string line in File.ReadLines(Fixture("icons.md")))
        {
            if (line.StartsWith("### Where each icon is used", StringComparison.Ordinal))
            {
                inUsage = true;
                continue;
            }

            if (!inUsage || !line.StartsWith('|'))
            {
                continue;
            }

            string[] cells = line.Split('|');
            if (cells.Length < 3)
            {
                continue;
            }

            foreach (Match name in BacktickName().Matches(cells[2]))
            {
                if (!keys.Contains(name.Groups[1].Value))
                {
                    missing.Add(name.Groups[1].Value);
                }
            }
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void StatePathsParse()
    {
        foreach (StateIcon state in new[] { StateIcon.Skipped, StateIcon.Missing, StateIcon.Error })
        {
            (System.Drawing.Drawing2D.GraphicsPath stroke, System.Drawing.Drawing2D.GraphicsPath? fill) = StateShapes.Get(state, 1);
            Assert.True(stroke.PointCount > 0);
        }
    }

    private static Color Get(ThemeTokens tokens, string name) =>
        (Color)typeof(ThemeTokens).GetProperty(name, BindingFlags.Public | BindingFlags.Instance)!.GetValue(tokens)!;

    private static int Parse(string value)
    {
        value = value.Trim('`');
        if (value.StartsWith('#'))
        {
            int rgb = int.Parse(value[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF).ToArgb();
        }

        Match rgba = Rgba().Match(value);
        int alpha = (int)Math.Round(double.Parse(rgba.Groups[4].Value, CultureInfo.InvariantCulture) * 255, MidpointRounding.AwayFromZero);
        return Color.FromArgb(alpha, int.Parse(rgba.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(rgba.Groups[2].Value, CultureInfo.InvariantCulture), int.Parse(rgba.Groups[3].Value, CultureInfo.InvariantCulture)).ToArgb();
    }

    private static double Contrast(Color a, Color b)
    {
        double la = Luminance(a);
        double lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color c)
    {
        static double Channel(int v)
        {
            double x = v / 255.0;
            return x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    [GeneratedRegex(@"^\| (\w+) \| (`[^`]+`) \| (`[^`]+`) \|$")]
    private static partial Regex TokenRow();

    [GeneratedRegex(@"^\| (\w+) \| (\w+) \| [\d.]+ \| ([\d.]+) \| Pass \|$")]
    private static partial Regex ContrastRow();

    [GeneratedRegex(@"rgba\((\d+),(\d+),(\d+),([\d.]+)\)")]
    private static partial Regex Rgba();

    [GeneratedRegex(@"`([a-z-]+)`")]
    private static partial Regex BacktickName();
}
