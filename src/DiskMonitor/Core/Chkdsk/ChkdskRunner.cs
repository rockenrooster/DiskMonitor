using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DiskMonitor.Core.Chkdsk;

internal enum ChkdskMode
{
    /// <summary>Read-only check. On NTFS this is the online scan, which never takes the volume offline.</summary>
    Scan,

    /// <summary>Repairs only the problems a previous scan recorded. NTFS only.</summary>
    SpotFix,

    /// <summary>Full file system repair (chkdsk /f).</summary>
    Fix,

    /// <summary>Full repair plus a surface scan for bad sectors (chkdsk /r).</summary>
    FixAndRecover
}

internal static class ChkdskModeExtensions
{
    public static string Label(this ChkdskMode mode) => mode switch
    {
        ChkdskMode.Scan => "Scan",
        ChkdskMode.SpotFix => "Spot fix",
        ChkdskMode.Fix => "Repair",
        _ => "Repair and surface scan"
    };

    public static bool IsReadOnly(this ChkdskMode mode) => mode == ChkdskMode.Scan;
}

/// <summary>Raised for requests that cannot run at all, with a message fit to show the user.</summary>
internal sealed class ChkdskException(string message) : Exception(message);

/// <summary>Runs chkdsk.exe and reports its output line by line. Requires administrator rights.</summary>
internal static partial class ChkdskRunner
{
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetOEMCP();

    /// <summary>Normalizes "c", "C:", or "C:\" to "C:"; null if the text is not a drive letter.</summary>
    public static string? NormalizeVolume(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string trimmed = text.Trim().TrimEnd('\\').TrimEnd(':');
        return trimmed.Length == 1 && char.IsAsciiLetter(trimmed[0]) ? char.ToUpperInvariant(trimmed[0]) + ":" : null;
    }

    /// <summary>The chkdsk arguments for a request, validated against the volume's file system.</summary>
    public static string BuildArguments(string volume, ChkdskMode mode, bool moreResources, string fileSystem)
    {
        bool ntfs = fileSystem.Equals("NTFS", StringComparison.OrdinalIgnoreCase);
        if (fileSystem.Equals("ReFS", StringComparison.OrdinalIgnoreCase))
        {
            throw new ChkdskException("ReFS volumes repair themselves online and cannot be checked with CHKDSK.");
        }

        return mode switch
        {
            ChkdskMode.Scan when ntfs => moreResources ? $"{volume} /scan /perf" : $"{volume} /scan",
            ChkdskMode.Scan => volume, // without switches chkdsk checks FAT and exFAT read-only
            ChkdskMode.SpotFix when ntfs => $"{volume} /spotfix",
            ChkdskMode.SpotFix => throw new ChkdskException("Spot fix is only available on NTFS volumes."),
            ChkdskMode.Fix => $"{volume} /f",
            _ => $"{volume} /r"
        };
    }

    /// <summary>
    /// Runs chkdsk to completion and returns its exit code. Permanent output lines go to
    /// <paramref name="onLine"/>; the self-overwriting progress line goes to <paramref name="onProgress"/>.
    /// </summary>
    public static int Run(
        string volume,
        ChkdskMode mode,
        bool moreResources,
        Action<string> onStarted,
        Action<string> onLine,
        Action<string, int?> onProgress,
        CancellationToken cancellation)
    {
        string letter = NormalizeVolume(volume) ?? throw new ChkdskException("That is not a drive letter.");
        DriveInfo drive = new(letter + "\\");
        if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable) || !drive.IsReady)
        {
            throw new ChkdskException($"Volume {letter} is not available.");
        }

        string arguments = BuildArguments(letter, mode, moreResources, drive.DriveFormat);
        ProcessStartInfo startInfo = new(Path.Combine(Environment.SystemDirectory, "chkdsk.exe"), arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = ConsoleEncoding(),
            StandardErrorEncoding = ConsoleEncoding()
        };

        using Process process = Process.Start(startInfo) ?? throw new ChkdskException("CHKDSK could not be started.");
        using CancellationTokenRegistration killOnCancel = cancellation.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Already exited.
            }
        });

        onStarted("chkdsk " + arguments);
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                onLine(e.Data.TrimEnd());
            }
        };
        process.BeginErrorReadLine();

        // A repair on a volume in use asks one or two yes/no questions. Policy: never force a
        // dismount, always accept "check at the next restart". A volume that cannot be dismounted
        // (system or page file) is only asked the second question.
        try
        {
            if (!mode.IsReadOnly())
            {
                process.StandardInput.Write(CannotDismount(letter) ? "Y\r\n" : "N\r\nY\r\n");
            }

            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // chkdsk exited before reading its input.
        }

        PumpOutput(process.StandardOutput, onLine, onProgress);
        process.WaitForExit();
        cancellation.ThrowIfCancellationRequested();
        return process.ExitCode;
    }

    // chkdsk rewrites its progress line with a bare carriage return; real lines end with a line feed.
    private static void PumpOutput(StreamReader reader, Action<string> onLine, Action<string, int?> onProgress)
    {
        char[] buffer = new char[2048];
        StringBuilder current = new();
        bool pendingReturn = false;
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int index = 0; index < read; index++)
            {
                char c = buffer[index];
                if (pendingReturn)
                {
                    pendingReturn = false;
                    if (c == '\n')
                    {
                        EmitLine(current, onLine, onProgress);
                        continue;
                    }

                    EmitProgress(current, onProgress);
                }

                switch (c)
                {
                    case '\r':
                        pendingReturn = true;
                        break;
                    case '\n':
                        EmitLine(current, onLine, onProgress);
                        break;
                    default:
                        current.Append(c);
                        break;
                }
            }
        }

        if (current.Length > 0)
        {
            EmitLine(current, onLine, onProgress);
        }
    }

    private static void EmitLine(StringBuilder current, Action<string> onLine, Action<string, int?> onProgress)
    {
        string text = current.ToString().TrimEnd();
        current.Clear();
        if (IsProgress(text))
        {
            onProgress(text.Trim(), ParsePercent(text));
        }
        else
        {
            onLine(text);
        }
    }

    private static void EmitProgress(StringBuilder current, Action<string, int?> onProgress)
    {
        string text = current.ToString().Trim();
        current.Clear();
        if (text.Length > 0)
        {
            onProgress(text, ParsePercent(text));
        }
    }

    private static bool IsProgress(string text) =>
        text.StartsWith("Progress:", StringComparison.OrdinalIgnoreCase) || PercentCompletePattern().IsMatch(text);

    private static int? ParsePercent(string text)
    {
        Match match = TotalPattern().Match(text);
        if (!match.Success)
        {
            match = PercentCompletePattern().Match(text);
        }

        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int percent)
            ? Math.Clamp(percent, 0, 100)
            : null;
    }

    private static bool CannotDismount(string letter)
    {
        string systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "";
        if (systemRoot.StartsWith(letter, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            using RegistryKey? memory = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management");
            return memory?.GetValue("ExistingPageFiles") is string[] pageFiles
                && pageFiles.Any(file => file.Contains(letter + "\\", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    // chkdsk writes in the console's OEM code page when its output is redirected.
    private static Encoding ConsoleEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding((int)GetOEMCP());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Encoding.UTF8;
        }
    }

    [GeneratedRegex(@"Total:\s*(\d+)\s*%", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TotalPattern();

    [GeneratedRegex(@"(\d+) percent complete", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PercentCompletePattern();
}
