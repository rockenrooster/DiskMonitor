using System.Diagnostics;
using System.Security;
using System.Text;
using DiskMonitor.App;
using DiskMonitor.Diagnostics;

namespace DiskMonitor.Core.Chkdsk;

/// <summary>
/// An on-demand scheduled task that starts the scan helper with administrator rights. Once it is
/// registered (one UAC prompt), read-only scans can run without prompting again. The task only
/// ever performs the read-only scan; repairs always go through a fresh UAC prompt.
/// </summary>
internal static class ScanTask
{
    public const string OperationSwitch = "--elevated-op";
    private const string RegisterOperation = "register-scan-task";
    private const string UnregisterOperation = "unregister-scan-task";
    private const string UnregisterAllOperation = "unregister-all-tasks";
    private const string Folder = @"\DiskMonitor";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(2);
    private static readonly Lock Gate = new();
    private static bool _registered;
    private static DateTime _checkedUtc;

    /// <summary>Whether the task exists for this user and points at this executable.</summary>
    public static bool IsRegistered(bool refresh = false)
    {
        lock (Gate)
        {
            if (!refresh && DateTime.UtcNow - _checkedUtc < CacheLifetime)
            {
                return _registered;
            }
        }

        (int exitCode, string output) = RunSchtasks($"/Query /TN \"{TaskName(Elevation.CurrentUserSid)}\" /XML");
        bool registered = exitCode == 0
            && output.Contains(SecurityElement.Escape(AppPaths.ExecutablePath) ?? AppPaths.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        lock (Gate)
        {
            _registered = registered;
            _checkedUtc = DateTime.UtcNow;
        }

        return registered;
    }

    /// <summary>Starts the task. The helper it launches connects back over the scan pipe.</summary>
    public static bool Start() => RunSchtasks($"/Run /TN \"{TaskName(Elevation.CurrentUserSid)}\"").exitCode == 0;

    /// <summary>Registers the task through an elevated copy of the app. Shows one UAC prompt.</summary>
    public static bool RequestRegister() => RunOperation(RegisterOperation);

    public static bool RequestUnregister() => RunOperation(UnregisterOperation);

    /// <summary>Entry point of the elevated copy started by <see cref="RequestRegister"/>. Returns a process exit code.</summary>
    public static int RunElevatedOperation(string[] args)
    {
        const int accessDenied = 5;
        const int invalidParameter = 87;
        if (!Elevation.IsElevated)
        {
            return accessDenied;
        }

        string operation = CommandLine.Value(args, OperationSwitch) ?? "";
        string? sid = CommandLine.Value(args, "--sid");
        switch (operation)
        {
            case RegisterOperation:
                if (!Elevation.IsValidSid(sid))
                {
                    return invalidParameter;
                }

                // A task that runs elevated must not point at a file a standard user can replace.
                if (!AppPaths.IsInstalledBuild)
                {
                    return accessDenied;
                }

                return Register(sid!);

            case UnregisterOperation:
                return Elevation.IsValidSid(sid)
                    ? RunSchtasks($"/Delete /TN \"{TaskName(sid!)}\" /F").exitCode
                    : invalidParameter;

            case UnregisterAllOperation:
                return UnregisterAll();

            default:
                return invalidParameter;
        }
    }

    private static string TaskName(string sid) => $@"{Folder}\ScanHelper-{sid}";

    private static bool RunOperation(string operation)
    {
        int? exitCode = Elevation.RunSelfElevated($"{OperationSwitch} {operation} --sid {Elevation.CurrentUserSid}");
        bool registered = IsRegistered(refresh: true);
        return exitCode == 0 && registered == (operation == RegisterOperation);
    }

    private static int Register(string sid)
    {
        string xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Author>Disk Monitor</Author>
                <Description>Lets Disk Monitor run read-only file system scans (chkdsk /scan) without an administrator prompt. Remove it from Disk Monitor's Check Disk page.</Description>
              </RegistrationInfo>
              <Triggers />
              <Principals>
                <Principal id="Author">
                  <UserId>{sid}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>Parallel</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT24H</ExecutionTimeLimit>
                <Priority>5</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{SecurityElement.Escape(AppPaths.ExecutablePath)}</Command>
                  <Arguments>{ChkdskHelper.HelperSwitch} --task --sid {sid}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;

        string path = Path.Combine(Path.GetTempPath(), $"diskmonitor-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(path, xml, Encoding.Unicode);
            return RunSchtasks($"/Create /TN \"{TaskName(sid)}\" /XML \"{path}\" /F").exitCode;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Register scan task", ex);
            return 1;
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A leftover temp file is harmless.
            }
        }
    }

    // Used by the uninstaller: removes the task of every user who enabled it.
    private static int UnregisterAll()
    {
        (int exitCode, string output) = RunSchtasks("/Query /FO CSV /NH");
        if (exitCode != 0)
        {
            return exitCode;
        }

        int result = 0;
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string name = line.Split(',')[0].Trim('"');
            if (name.StartsWith(Folder + @"\ScanHelper-", StringComparison.OrdinalIgnoreCase)
                && Elevation.IsValidSid(name[(Folder.Length + @"\ScanHelper-".Length)..]))
            {
                int deleted = RunSchtasks($"/Delete /TN \"{name}\" /F").exitCode;
                result = deleted != 0 ? deleted : result;
            }
        }

        return result;
    }

    private static (int exitCode, string output) RunSchtasks(string arguments)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process is null)
            {
                return (-1, "");
            }

            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();
            string output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(20_000))
            {
                process.Kill();
                return (-1, output);
            }

            return (process.ExitCode, output);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("schtasks", ex);
            return (-1, "");
        }
    }
}

/// <summary>Minimal command-line helpers shared by the app's process modes.</summary>
internal static class CommandLine
{
    public static bool Has(string[] args, string name) => args.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>The argument that follows <paramref name="name"/>, or null.</summary>
    public static string? Value(string[] args, string name)
    {
        int index = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
