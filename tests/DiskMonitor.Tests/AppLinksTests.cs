using System.Text;
using System.Text.RegularExpressions;
using DiskMonitor.Core.Notifications;

namespace DiskMonitor.Tests;

/// <summary>Links round-trip, and no link of any shape maps to an action (CORE-07, security invariant 5).</summary>
public sealed class AppLinksTests
{
    [Fact]
    public void EventLinkRoundTrips()
    {
        const string key = "System|12345|638123456789012345";
        LinkTarget target = AppLinks.Resolve(AppLinks.ForEvent(key));
        Assert.Equal(LinkKind.Event, target.Kind);
        Assert.Equal(key, target.EventKey);
    }

    [Theory]
    [InlineData("Overview")]
    [InlineData("Events")]
    [InlineData("Drives")]
    [InlineData("CheckDisk")]
    [InlineData("Settings")]
    public void PageLinkRoundTrips(string page)
    {
        LinkTarget target = AppLinks.Resolve(AppLinks.ForPage(page));
        Assert.Equal(LinkKind.Page, target.Kind);
        Assert.Equal(page, target.Page);
    }

    [Fact]
    public void ScanLinkOnlySelectsTheVolume()
    {
        LinkTarget target = AppLinks.Resolve(AppLinks.ForScan("D:"));
        Assert.Equal(LinkKind.ScanOffer, target.Kind);
        Assert.Equal("D:", target.Volume);
    }

    [Theory]
    [InlineData("diskmonitor:scan?volume=C", (int)LinkKind.ScanOffer)]
    [InlineData("diskmonitor:snooze?minutes=600", (int)LinkKind.Show)]
    [InlineData("diskmonitor:exit", (int)LinkKind.Show)]
    [InlineData("diskmonitor:open?page=..\\..\\x", (int)LinkKind.Show)]
    [InlineData("diskmonitor:open?page=settings", (int)LinkKind.Page)]
    [InlineData("diskmonitor:scan?volume=C:\\Windows\\System32", (int)LinkKind.Page)]
    [InlineData("diskmonitor:event", (int)LinkKind.Page)]
    [InlineData("diskmonitor:", (int)LinkKind.Show)]
    [InlineData("diskmonitor:%zz", (int)LinkKind.Show)]
    [InlineData("https://example.com", (int)LinkKind.Show)]
    [InlineData("", (int)LinkKind.Show)]
    [InlineData(null, (int)LinkKind.Show)]
    public void HostileLinksOnlyNavigate(string? link, int expected) =>
        Assert.Equal((LinkKind)expected, AppLinks.Resolve(link).Kind);

    [Fact]
    public void FiveThousandCharacterLinkShowsTheWindow() =>
        Assert.Equal(LinkKind.Show, AppLinks.Resolve("diskmonitor:open?page=" + new string('A', 5000)).Kind);

    [Fact]
    public void FuzzedLinksNeverMapToAnAction()
    {
        Random random = new(20261116);
        string[] seeds =
        [
            AppLinks.ForEvent("System|1|2"), AppLinks.ForPage("Events"), AppLinks.ForScan("C:"),
            "diskmonitor:snooze?minutes=60", "diskmonitor:exit", "diskmonitor:open?page=Settings&section=rules",
            "diskmonitor:scan?volume=D&mode=Fix&t=0123456789ABCDEF", "diskmonitor:settings?theme=Dark"
        ];
        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789:/?&=%._-\\|{}[]\"' \t\r\n\u00e9\u4e2d";
        Regex volume = new("^[A-Z]:$");

        for (int i = 0; i < 10_000; i++)
        {
            string link;
            if (i % 2 == 0)
            {
                StringBuilder builder = new(seeds[random.Next(seeds.Length)]);
                int mutations = random.Next(1, 6);
                for (int m = 0; m < mutations; m++)
                {
                    int position = random.Next(builder.Length + 1);
                    switch (random.Next(3))
                    {
                        case 0:
                            builder.Insert(position, alphabet[random.Next(alphabet.Length)]);
                            break;
                        case 1 when builder.Length > 0:
                            builder.Remove(Math.Min(position, builder.Length - 1), 1);
                            break;
                        default:
                            if (builder.Length > 0)
                            {
                                builder[Math.Min(position, builder.Length - 1)] = alphabet[random.Next(alphabet.Length)];
                            }

                            break;
                    }
                }

                link = builder.ToString();
            }
            else
            {
                int length = random.Next(0, 300);
                StringBuilder builder = new("diskmonitor:");
                for (int c = 0; c < length; c++)
                {
                    builder.Append(alphabet[random.Next(alphabet.Length)]);
                }

                link = builder.ToString();
            }

            LinkTarget target = AppLinks.Resolve(link);
            switch (target.Kind)
            {
                case LinkKind.Show:
                    Assert.Null(target.Page);
                    Assert.Null(target.Volume);
                    Assert.Null(target.EventKey);
                    break;
                case LinkKind.Page:
                    Assert.Contains(target.Page, AppLinks.PageKeys);
                    break;
                case LinkKind.Event:
                    Assert.False(string.IsNullOrWhiteSpace(target.EventKey));
                    Assert.True(target.EventKey!.Length <= 256);
                    break;
                case LinkKind.ScanOffer:
                    Assert.Matches(volume, target.Volume!);
                    break;
                default:
                    Assert.Fail($"Unexpected link kind {target.Kind} for {link}");
                    break;
            }
        }
    }
}
