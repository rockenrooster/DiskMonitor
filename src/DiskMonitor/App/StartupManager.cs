using DiskMonitor.Diagnostics;
using Microsoft.Win32;

namespace DiskMonitor.App;

/// <summary>
/// "Start with Windows" through the per-user Run key. The registry is the source of truth, so the
/// toggle stays honest when the user changes it in Task Manager or the installer sets it.
/// </summary>
internal static class StartupManager
{
    public const string TraySwitch = "--tray";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "DiskMonitor";

    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? run = Registry.CurrentUser.OpenSubKey(RunKey);
            if (run?.GetValue(ValueName) is not string command || !command.Contains(AppPaths.ExecutablePath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Task Manager records "disabled" as an odd first byte here without touching the Run value.
            using RegistryKey? approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            return approved?.GetValue(ValueName) is not byte[] { Length: > 0 } state || (state[0] & 1) == 0;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Startup query", ex);
            return false;
        }
    }

    public static bool SetEnabled(bool enabled, bool startMinimized)
    {
        try
        {
            using RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                run.SetValue(ValueName, BuildCommand(startMinimized), RegistryValueKind.String);
                using RegistryKey? approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
                approved?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            else
            {
                run.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Startup change", ex);
            return false;
        }
    }

    /// <summary>Rewrites the command if startup is on, after the app moved or "start minimized" changed.</summary>
    public static void Refresh(bool startMinimized)
    {
        try
        {
            using RegistryKey? run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (run?.GetValue(ValueName) is not string command)
            {
                return;
            }

            // Only adopt an entry that already points here: a second, portable copy must not hijack it.
            string wanted = BuildCommand(startMinimized);
            if (command != wanted && command.Contains(AppPaths.ExecutablePath, StringComparison.OrdinalIgnoreCase))
            {
                run.SetValue(ValueName, wanted, RegistryValueKind.String);
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Startup refresh", ex);
        }
    }

    private static string BuildCommand(bool startMinimized) =>
        startMinimized ? $"\"{AppPaths.ExecutablePath}\" {TraySwitch}" : $"\"{AppPaths.ExecutablePath}\"";
}
