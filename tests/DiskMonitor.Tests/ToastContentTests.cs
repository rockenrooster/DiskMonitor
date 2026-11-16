using System.Xml.Linq;
using DiskMonitor.App;
using DiskMonitor.Core.Events;
using DiskMonitor.Core.Notifications;

namespace DiskMonitor.Tests;

/// <summary>Toast XML is well formed, escapes markup, and never carries the event message (CORE-06).</summary>
public sealed class ToastContentTests
{
    private static DiskEvent SampleEvent(string message, string device = "D: · WDC WD80EFZZ (Disk 1)") => new()
    {
        Key = "System|42|638000000000000000",
        TimeUtc = DateTime.UtcNow,
        Tier = Tier.High,
        Kind = ErrorKind.Logical,
        RuleId = "ntfs:150",
        Title = "Bad clusters relocated",
        Message = message,
        Advice = "Back up this drive now, then check its health on the Drives page.",
        Log = "System",
        Provider = "Ntfs",
        EventId = 150,
        Volume = "D:",
        DeviceText = device,
        SuggestScan = true
    };

    [Fact]
    public void EventToastNeverContainsTheMessage()
    {
        DiskEvent item = SampleEvent(@"The file C:\Users\x\secret.docx could not be read. Volume: C:\Users\x");
        string xml = NotificationService.BuildXml(NotificationService.BuildEventToast(item, 0, new TierOptions()));
        Assert.DoesNotContain("secret", xml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"C:\Users", xml, StringComparison.OrdinalIgnoreCase);
        _ = XDocument.Parse(xml);
    }

    [Fact]
    public void EventToastWithoutDeviceNamesTheKind()
    {
        ToastRequest request = NotificationService.BuildEventToast(SampleEvent("anything", device: ""), 0, new TierOptions());
        Assert.Equal(Strings.KindName(ErrorKind.Logical), request.Line1);
    }

    [Fact]
    public void EventToastButtonsOnlyNavigate()
    {
        ToastRequest request = NotificationService.BuildEventToast(SampleEvent("x"), 3, new TierOptions());
        Assert.Contains("(+3 more)", request.Title);
        Assert.Equal(2, request.Actions.Count);
        foreach (ToastAction action in request.Actions)
        {
            Assert.NotEqual(LinkKind.Show, AppLinks.Resolve(action.Uri).Kind);
        }

        Assert.DoesNotContain(request.Actions, a => a.Label.Contains("Snooze", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(LinkKind.ScanOffer, AppLinks.Resolve(request.Actions[1].Uri).Kind);
    }

    [Fact]
    public void MarkupIsEscaped()
    {
        ToastRequest request = new()
        {
            Title = "<b>\"Disk\" & 'more'</b>\u0001",
            Line1 = "a < b > c",
            LaunchUri = "diskmonitor:open?page=Events&x=1",
            Actions = [new ToastAction("Go & see", "diskmonitor:open?page=Drives&y=\"2\"")]
        };
        XDocument document = XDocument.Parse(NotificationService.BuildXml(request));
        Assert.Equal("<b>\"Disk\" & 'more'</b>", document.Descendants("text").First().Value);
        Assert.Equal("diskmonitor:open?page=Events&x=1", document.Root!.Attribute("launch")!.Value);
    }

    [Fact]
    public void SilentAndStickyChangeTheXml()
    {
        ToastRequest request = new()
        {
            Title = "t",
            LaunchUri = "diskmonitor:show",
            Actions = [new ToastAction("Open", "diskmonitor:show")],
            Silent = true,
            Sticky = true
        };
        string xml = NotificationService.BuildXml(request);
        Assert.Contains("scenario=\"reminder\"", xml);
        Assert.Contains("<audio silent=\"true\"/>", xml);
    }
}
