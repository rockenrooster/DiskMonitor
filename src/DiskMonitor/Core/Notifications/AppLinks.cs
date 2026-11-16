using DiskMonitor.App;

namespace DiskMonitor.Core.Notifications;

/// <summary>A request delivered to the running app: a toast click, a second launch, or a command-line switch.</summary>
internal sealed record AppCommand(string Verb, IReadOnlyDictionary<string, string> Arguments)
{
    public const string Show = "show";
    public const string Open = "open";
    public const string Event = "event";
    public const string Scan = "scan";
    public const string Snooze = "snooze";
    public const string Exit = "exit";
    public const string None = "none";

    public string? this[string name] => Arguments.GetValueOrDefault(name);
}

/// <summary>Builds and parses the diskmonitor: links that toast buttons carry.</summary>
internal static class AppLinks
{
    private const string Prefix = AppPaths.ProtocolScheme + ":";

    public static string ForEvent(string key) => $"{Prefix}{AppCommand.Event}?key={Uri.EscapeDataString(key)}";

    public static string ForPage(string page) => $"{Prefix}{AppCommand.Open}?page={Uri.EscapeDataString(page)}";

    public static string ForScan(string volume, string token) =>
        $"{Prefix}{AppCommand.Scan}?volume={Uri.EscapeDataString(volume.TrimEnd(':'))}&t={token}";

    public static string ForSnooze(int minutes, string token) => $"{Prefix}{AppCommand.Snooze}?minutes={minutes}&t={token}";

    /// <summary>
    /// Parses a link. Links can arrive from anywhere (any program may open a diskmonitor: URL),
    /// so the result is untrusted input: callers must validate every argument.
    /// </summary>
    public static bool TryParse(string? text, out AppCommand command)
    {
        command = new AppCommand(AppCommand.Show, new Dictionary<string, string>());
        if (string.IsNullOrWhiteSpace(text) || !text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) || text.Length > 2048)
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
}
