using System.Runtime.InteropServices;
using DiskMonitor.App;
using DiskMonitor.Diagnostics;
using Microsoft.Win32;

namespace DiskMonitor.Core.Notifications;

/// <summary>
/// Per-user registration that lets an unpackaged app show toasts under its own name and receive
/// toast clicks: an AppUserModelID entry and a URL protocol, both under HKCU.
/// </summary>
internal static class ShellRegistration
{
    [DllImport("shell32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    /// <summary>Writes the registration if it is missing or points at another copy of the app.</summary>
    public static void Ensure(Action<string> writeIconPng)
    {
        try
        {
            _ = SetCurrentProcessExplicitAppUserModelID(AppPaths.AppUserModelId);

            string executable = AppPaths.ExecutablePath;
            string icon = AppPaths.ToastIconFile;
            if (!File.Exists(icon) || File.GetLastWriteTimeUtc(icon) < File.GetLastWriteTimeUtc(executable))
            {
                Directory.CreateDirectory(AppPaths.DataDirectory);
                writeIconPng(icon);
            }

            using (RegistryKey appId = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppPaths.AppUserModelId}"))
            {
                SetIfDifferent(appId, "DisplayName", AppPaths.AppName);
                SetIfDifferent(appId, "IconUri", icon);
            }

            using RegistryKey protocol = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{AppPaths.ProtocolScheme}");
            SetIfDifferent(protocol, "", $"URL:{AppPaths.AppName}");
            SetIfDifferent(protocol, "URL Protocol", "");
            using RegistryKey command = protocol.CreateSubKey(@"shell\open\command");
            SetIfDifferent(command, "", $"\"{executable}\" \"%1\"");
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Shell registration", ex);
        }
    }

    private static void SetIfDifferent(RegistryKey key, string name, string value)
    {
        if (key.GetValue(name) as string != value)
        {
            key.SetValue(name, value, RegistryValueKind.String);
        }
    }
}
