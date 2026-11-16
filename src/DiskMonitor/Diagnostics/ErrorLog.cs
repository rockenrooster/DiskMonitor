using DiskMonitor.App;

namespace DiskMonitor.Diagnostics;

/// <summary>Append-only diagnostic log. Never throws: logging must not take the monitor down.</summary>
internal static class ErrorLog
{
    private const long MaxBytes = 512 * 1024;
    private static readonly Lock Gate = new();

    public static string FilePath => Path.Combine(AppPaths.LogDirectory, "diskmonitor.log");

    public static void Write(string context, Exception exception) =>
        Write(context, $"{exception.GetType().Name}: {exception.Message}{Environment.NewLine}{exception.StackTrace}");

    public static void Write(string context, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.LogDirectory);
                FileInfo file = new(FilePath);
                if (file.Exists && file.Length > MaxBytes)
                {
                    string previous = FilePath + ".1";
                    File.Move(FilePath, previous, overwrite: true);
                }

                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{context}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Nothing sensible to do if the log itself cannot be written.
        }
    }
}
