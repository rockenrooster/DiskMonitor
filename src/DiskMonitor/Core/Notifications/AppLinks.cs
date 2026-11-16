using DiskMonitor.App;
using DiskMonitor.Core.Chkdsk;

namespace DiskMonitor.Core.Notifications;

/// <summary>A request delivered to the running app: a toast click, a second launch, or a command-line switch.</summary>
internal sealed record AppCommand(string Verb, IReadOnlyDictionary<string, string> Arguments)
{
    public const string Show = "show";
    public const string Open = "open";
    public const string Event = "event";
    public const string Scan = "scan";
    public const string Exit = "exit";

    public string? this[string name] => Arguments.GetValueOrDefault(name);
}

/// <summary>What a link asks the app to show. Every kind is a view; none of them acts.</summary>
internal enum LinkKind
{
    /// <summary>Bring the window forward on the page it was on.</summary>
    Show,

    /// <summary>A page, by key.</summary>
    Page,

    /// <summary>The Events page with one event's details open.</summary>
    Event,

    /// <summary>The Check Disk page with a volume selected and an offer to scan it. Nothing starts.</summary>
    ScanOffer
}

internal sealed record LinkTarget(LinkKind Kind, string? Page = null, string? EventKey = null, string? Volume = null)
{
    public static readonly LinkTarget ShowWindow = new(LinkKind.Show);
}

/// <summary>
/// Builds and parses the diskmonitor: links that toast buttons carry. A link selects a view and
/// never acts: any program can open such a link, so it must not start a scan, change a setting,
/// snooze, or name a file (MediaVerifier's rule; master plan, security invariant 5).
/// </summary>
internal static class AppLinks
{
    private const string Prefix = AppPaths.ProtocolScheme + ":";
    private const int MaxLinkLength = 2048;
    private const int MaxEventKeyLength = 256;

    /// <summary>Page keys a link may name. They are identifiers, not display text.</summary>
    public static readonly string[] PageKeys = ["Overview", "Events", "Drives", "CheckDisk", "Settings"];

    public static string ForEvent(string key) => $"{Prefix}{AppCommand.Event}?key={Uri.EscapeDataString(key)}";

    public static string ForPage(string page) => $"{Prefix}{AppCommand.Open}?page={Uri.EscapeDataString(page)}";

    public static string ForScan(string volume) =>
        $"{Prefix}{AppCommand.Scan}?volume={Uri.EscapeDataString(volume.TrimEnd(':'))}";

    /// <summary>
    /// Parses a link. Links can arrive from anywhere (any program may open a diskmonitor: URL),
    /// so the result is untrusted input: callers must validate every argument.
    /// </summary>
    public static bool TryParse(string? text, out AppCommand command)
    {
        command = new AppCommand(AppCommand.Show, new Dictionary<string, string>());
        if (string.IsNullOrWhiteSpace(text) || !text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) || text.Length > MaxLinkLength)
        {
            return false;
        }

        string body = text[Prefix.Length..].TrimStart('/');
        int query = body.IndexOf('?');
        string verb = (query < 0 ? body : body[..query]).Trim('/').ToLowerInvariant();
        Dictionary<string, string> arguments = new(StringComparer.OrdinalIgnoreCase);
        if (query >= 0)
        {
            foreach (string pair in body[(query + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = pair.IndexOf('=');
                if (equals <= 0)
                {
                    continue;
                }

                try
                {
                    arguments[Uri.UnescapeDataString(pair[..equals])] = Uri.UnescapeDataString(pair[(equals + 1)..]);
                }
                catch (UriFormatException)
                {
                    // A malformed escape: drop the pair.
                }
            }
        }

        if (verb.Length == 0)
        {
            verb = AppCommand.Show;
        }

        command = new AppCommand(verb, arguments);
        return true;
    }

    /// <summary>
    /// Turns any link text into the view it selects. Never throws. Unknown verbs, unknown pages,
    /// malformed or oversized links all end in <see cref="LinkTarget.ShowWindow"/>. A link can never
    /// exit the app: that needs the plain <c>exit</c> command of a second launch with <c>--exit</c>.
    /// </summary>
    public static LinkTarget Resolve(string? text)
    {
        try
        {
            if (!TryParse(text, out AppCommand command))
            {
                return LinkTarget.ShowWindow;
            }

            switch (command.Verb)
            {
                case AppCommand.Open:
                    string? page = PageKeys.FirstOrDefault(key => key.Equals(command["page"], StringComparison.OrdinalIgnoreCase));
                    return page is null ? LinkTarget.ShowWindow : new LinkTarget(LinkKind.Page, Page: page);

                case AppCommand.Event:
                    string? key = command["key"];
                    return string.IsNullOrWhiteSpace(key) || key.Length > MaxEventKeyLength
                        ? new LinkTarget(LinkKind.Page, Page: "Events")
                        : new LinkTarget(LinkKind.Event, EventKey: key);

                case AppCommand.Scan:
                    string? volume = ChkdskRunner.NormalizeVolume(command["volume"]);
                    return volume is null
                        ? new LinkTarget(LinkKind.Page, Page: "CheckDisk")
                        : new LinkTarget(LinkKind.ScanOffer, Volume: volume);

                default:
                    return LinkTarget.ShowWindow;
            }
        }
        catch (Exception)
        {
            return LinkTarget.ShowWindow;
        }
    }
}
