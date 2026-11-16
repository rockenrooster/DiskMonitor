using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text.RegularExpressions;
using DiskMonitor.Diagnostics;

namespace DiskMonitor.App;

/// <summary>Administrator rights: whether this process has them, and how to start a copy that does.</summary>
internal static partial class Elevation
{
    private const int ErrorCancelled = 1223; // the user declined the UAC prompt

    private static readonly Lazy<bool> ElevatedLazy = new(() =>
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    });

    private static readonly Lazy<string> SidLazy = new(() =>
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value ?? "";
    });

    public static bool IsElevated => ElevatedLazy.Value;

    /// <summary>SID of the user running this process.</summary>
    public static string CurrentUserSid => SidLazy.Value;

    public static bool IsValidSid(string? sid) => sid is not null && SidPattern().IsMatch(sid);

    /// <summary>
    /// Starts this executable with administrator rights, showing a UAC prompt.
    /// Returns null if the user declined.
    /// </summary>
    public static Process? StartSelfElevated(string arguments)
    {
        try
        {
            return Process.Start(new ProcessStartInfo(AppPaths.ExecutablePath, arguments)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return null;
        }
        catch (Win32Exception ex)
        {
            ErrorLog.Write("Elevate", ex);
            return null;
        }
    }

    /// <summary>Runs an elevated copy to completion. Returns its exit code, or null if declined or timed out.</summary>
    public static int? RunSelfElevated(string arguments, int timeoutMilliseconds = 60_000)
    {
        using Process? process = StartSelfElevated(arguments);
        if (process is null || !process.WaitForExit(timeoutMilliseconds))
        {
            return null;
        }

        return process.ExitCode;
    }

    [GeneratedRegex(@"^S-1-\d{1,3}(-\d{1,10}){1,14}$", RegexOptions.CultureInvariant)]
    private static partial Regex SidPattern();
}
