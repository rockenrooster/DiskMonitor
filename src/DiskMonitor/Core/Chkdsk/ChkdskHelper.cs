using System.IO.Pipes;
using System.Text;
using DiskMonitor.App;
using DiskMonitor.Diagnostics;

namespace DiskMonitor.Core.Chkdsk;

/// <summary>
/// The elevated side of a CHKDSK run. The unelevated app starts this process (through UAC or the
/// scan task), and it streams chkdsk's output back over a named pipe the app is listening on.
///
/// Wire format, one message per line:
///   app to helper   JOB|volume|perf    (task launches only)   CANCEL
///   helper to app   S|command line     O|output line     P|percent|progress text
///                   X|exit code        E|error message
/// </summary>
internal static class ChkdskHelper
{
    public const string HelperSwitch = "--chkdsk-helper";
    private const int ConnectTimeoutMilliseconds = 15_000;

    public static string PipeName(string sid) => $"DiskMonitor.Chkdsk.{sid}";

    /// <summary>Process entry point. Returns the process exit code.</summary>
    public static int Run(string[] args)
    {
        const int accessDenied = 5;
        const int invalidParameter = 87;
        if (!Elevation.IsElevated)
        {
            return accessDenied;
        }

        string? sid = CommandLine.Value(args, "--sid");
        if (!Elevation.IsValidSid(sid))
        {
            return invalidParameter;
        }

        bool fromTask = CommandLine.Has(args, "--task");
        try
        {
            using NamedPipeClientStream pipe = new(".", PipeName(sid!), PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect(ConnectTimeoutMilliseconds);
            using StreamReader reader = new(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            using StreamWriter writer = new(pipe, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true) { AutoFlush = true };
            Lock writeGate = new();

            void Send(string line)
            {
                lock (writeGate)
                {
                    writer.WriteLine(line.ReplaceLineEndings(" "));
                }
            }

            string? volume;
            ChkdskMode mode;
            bool moreResources;
            if (fromTask)
            {
                // The task's command line is fixed, so the request arrives over the pipe. Whoever
                // owns the pipe is unverified: accept the read-only scan and nothing else.
                string[] job = (reader.ReadLine() ?? "").Split('|');
                if (job.Length < 3 || job[0] != "JOB")
                {
                    Send("E|" + Strings.ErrorRequestNotUnderstood);
                    return invalidParameter;
                }

                volume = ChkdskRunner.NormalizeVolume(job[1]);
                mode = ChkdskMode.Scan;
                moreResources = job[2] == "1";
            }
            else
            {
                // Launched through UAC: the request is the command line the user approved.
                volume = ChkdskRunner.NormalizeVolume(CommandLine.Value(args, "--volume"));
                moreResources = CommandLine.Has(args, "--perf");
                if (!Enum.TryParse(CommandLine.Value(args, "--mode"), ignoreCase: true, out mode) || !Enum.IsDefined(mode))
                {
                    Send("E|" + Strings.ErrorCheckNotSupported);
                    return invalidParameter;
                }
            }

            if (volume is null)
            {
                Send("E|" + Strings.ErrorNotDriveLetter);
                return invalidParameter;
            }

            using CancellationTokenSource cancel = new();
            _ = Task.Run(() =>
            {
                // The app asks to stop by sending CANCEL, or by going away (end of stream).
                try
                {
                    while (reader.ReadLine() is { } line)
                    {
                        if (line == "CANCEL")
                        {
                            break;
                        }
                    }
                }
                catch (IOException)
                {
                    // Pipe broken: treated like end of stream.
                }

                try
                {
                    cancel.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // The run already finished.
                }
            });

            try
            {
                int exitCode = ChkdskRunner.Run(
                    volume,
                    mode,
                    moreResources,
                    commandLine => Send("S|" + commandLine),
                    line => Send("O|" + line),
                    (text, percent) => Send($"P|{percent?.ToString() ?? ""}|{text}"),
                    cancel.Token);
                Send("X|" + exitCode);
                return 0;
            }
            catch (OperationCanceledException)
            {
                TrySend(Send, "E|" + Strings.ErrorCancelled);
                return 1;
            }
            catch (ChkdskException ex)
            {
                TrySend(Send, "E|" + ex.Message);
                return 1;
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Scan helper", ex);
            return 1;
        }
    }

    private static void TrySend(Action<string> send, string line)
    {
        try
        {
            send(line);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The app is gone; there is nobody to tell.
        }
    }
}
