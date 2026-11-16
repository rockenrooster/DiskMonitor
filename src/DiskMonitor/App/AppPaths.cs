namespace DiskMonitor.App;

internal static class AppPaths
{
    public const string AppName = "Disk Monitor";

    /// <summary>Identity Windows uses to group this app's toasts and taskbar button.</summary>
    public const string AppUserModelId = "Rockenrooster.DiskMonitor";

    /// <summary>URL scheme that toast buttons use to call back into the running app.</summary>
    public const string ProtocolScheme = "diskmonitor";

    private static readonly Lazy<string> DataDirectoryLazy = new(ResolveDataDirectory);

    public static string DataDirectory => DataDirectoryLazy.Value;
    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string StateFile => Path.Combine(DataDirectory, "state.json");
    public static string LogDirectory => Path.Combine(DataDirectory, "logs");
    public static string UpdateDirectory => Path.Combine(DataDirectory, "Updates");
    public static string ToastIconFile => Path.Combine(DataDirectory, "app-icon.png");

    public static string ExecutablePath => Environment.ProcessPath ?? Application.ExecutablePath;

    /// <summary>
    /// True when the executable sits under Program Files, where a standard user cannot replace it.
    /// Only such a copy may be registered to run elevated without a prompt.
    /// </summary>
    public static bool IsInstalledBuild
    {
        get
        {
            string exe = ExecutablePath;
            return IsUnder(exe, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))
                || IsUnder(exe, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86))
                || IsUnder(exe, Environment.GetEnvironmentVariable("ProgramW6432"));
        }
    }

    private static bool IsUnder(string path, string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        string prefix = root.TrimEnd('\\') + "\\";
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    // DISKMONITOR_DATA_DIR redirects settings and state, so previews and tests never touch real data.
    private static string ResolveDataDirectory()
    {
        string? overridePath = Environment.GetEnvironmentVariable("DISKMONITOR_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(overridePath);
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskMonitor");
    }
}
