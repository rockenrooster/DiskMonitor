using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using DiskMonitor.App;
using DiskMonitor.Core.Notifications;
using DiskMonitor.Diagnostics;

namespace DiskMonitor.Core.Chkdsk;

internal sealed record ChkdskResult(
    string Volume,
    ChkdskMode Mode,
    int ExitCode,
    bool Clean,
    bool Cancelled,
    bool Automatic,
    string Summary,
    string? Error);

/// <summary>
/// Runs CHKDSK on behalf of the UI and the proactive scheduler, one volume at a time. When the app
/// is not elevated the work happens in an elevated helper process and its output is streamed back.
/// </summary>
internal sealed class ChkdskService
{
    private const int MaxOutputLines = 4000;
    private static readonly TimeSpan HelperConnectTimeout = TimeSpan.FromSeconds(90);

    private readonly AppSettings _settings;
    private readonly StateStore _state;
    private readonly NotificationService _notifier;
    private readonly SemaphoreSlim _queue = new(1, 1);
    private readonly Lock _gate = new();
    private readonly List<string> _output = [];
    private CancellationTokenSource? _cancel;
    private string? _runningVolume;
    private ChkdskMode _runningMode;
    private string _commandLine = "";
    private string _progressText = "";
    private int? _progressPercent;
    private ChkdskResult? _lastResult;

    public ChkdskService(AppSettings settings, StateStore state, NotificationService notifier)
    {
        _settings = settings;
        _state = state;
        _notifier = notifier;
    }

    /// <summary>Raised on a background thread whenever output, progress, or running state changes.</summary>
    public event Action? Changed;

    public event Action<ChkdskResult>? Completed;

    public bool IsRunning => Read(() => _runningVolume is not null);
    public string? RunningVolume => Read(() => _runningVolume);
    public ChkdskMode RunningMode => Read(() => _runningMode);
    public string CommandLine => Read(() => _commandLine);
    public string ProgressText => Read(() => _progressText);
    public int? ProgressPercent => Read(() => _progressPercent);
    public ChkdskResult? LastResult => Read(() => _lastResult);

    /// <summary>Output of the current or most recent run.</summary>
    public string[] GetOutput() => Read(() => _output.ToArray());

    /// <summary>True if this request can start without showing a UAC prompt.</summary>
    public bool CanRunWithoutPrompt(ChkdskMode mode) =>
        Elevation.IsElevated || (mode.IsReadOnly() && ScanTask.IsRegistered());

    /// <summary>Stops the current run. Only read-only scans are safe to interrupt.</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            if (_runningVolume is not null && _runningMode.IsReadOnly())
            {
                _cancel?.Cancel();
            }
        }
    }

    /// <summary>Queues a run and completes when it finishes. Never throws; failures are in the result.</summary>
    public async Task<ChkdskResult> RunAsync(string volume, ChkdskMode mode, bool automatic)
    {
        string letter = ChkdskRunner.NormalizeVolume(volume) ?? volume;
        await _queue.WaitAsync().ConfigureAwait(false);
        using CancellationTokenSource cancel = new();
        NotificationService.Suppression mute = _notifier.SuppressRule("chkdsk:", letter, TimeSpan.FromHours(24));
        ChkdskResult result;
        try
        {
            lock (_gate)
            {
                _cancel = cancel;
                _runningVolume = letter;
                _runningMode = mode;
                _commandLine = "";
                _progressText = "Starting…";
                _progressPercent = null;
                _output.Clear();
            }

            Changed?.Invoke();

            int exitCode = -1;
            string? error = null;
            bool cancelled = false;
            try
            {
                bool moreResources = _settings.ScanWithMoreResources;
                exitCode = Elevation.IsElevated
                    ? await Task.Run(() => ChkdskRunner.Run(letter, mode, moreResources, OnStarted, AddLine, OnProgress, cancel.Token)).ConfigureAwait(false)
                    : await RunThroughHelperAsync(letter, mode, moreResources, cancel.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            catch (ChkdskException ex)
            {
                error = ex.Message;
            }
            catch (Exception ex)
            {
                ErrorLog.Write("CHKDSK", ex);
                error = ex.Message;
            }

            string[] output = GetOutput();
            bool clean = error is null && !cancelled && exitCode == 0 && !WasScheduled(output);
            string summary = error ?? (cancelled ? "The scan was cancelled." : Summarize(mode, exitCode, output));
            result = new ChkdskResult(letter, mode, exitCode, clean, cancelled, automatic, summary, error);

            if (error is null && !cancelled)
            {
                _state.Update(s => s.Scans[letter] = new ScanRecord
                {
                    TimeUtc = DateTime.UtcNow,
                    Mode = mode.ToString(),
                    ExitCode = exitCode,
                    Clean = clean,
                    Automatic = automatic,
                    Summary = summary
                });
            }
        }
        finally
        {
            lock (_gate)
            {
                _cancel = null;
                _runningVolume = null;
                _progressText = "";
                _progressPercent = null;
            }

            // The run's own result is reported directly; give the matching log record time to arrive.
            _notifier.Shorten(mute, TimeSpan.FromMinutes(2));
            _queue.Release();
        }

        lock (_gate)
        {
            _lastResult = result;
        }

        Changed?.Invoke();
        Completed?.Invoke(result);
        return result;
    }

    private async Task<int> RunThroughHelperAsync(string volume, ChkdskMode mode, bool moreResources, CancellationToken cancellation)
    {
        string sid = Elevation.CurrentUserSid;
        await using NamedPipeServerStream server = NamedPipeServerStreamAcl.Create(
            ChkdskHelper.PipeName(sid),
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            Ipc.CreateUserPipeSecurity());

        // Prefer the registered task (no prompt). It only carries read-only scans.
        bool throughTask = mode.IsReadOnly() && ScanTask.IsRegistered() && ScanTask.Start();
        Process? launcher = null;
        if (!throughTask)
        {
            OnProgress("Waiting for administrator approval…", null);
            string arguments = $"{ChkdskHelper.HelperSwitch} --sid {sid} --volume {volume} --mode {mode}" + (moreResources ? " --perf" : "");
            launcher = Elevation.StartSelfElevated(arguments)
                ?? throw new ChkdskException("Administrator approval was declined, so nothing was changed.");
        }

        using (launcher)
        {
            await WaitForHelperAsync(server, launcher, cancellation).ConfigureAwait(false);
            using StreamReader reader = new(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            await using StreamWriter writer = new(server, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true) { AutoFlush = true };
            if (throughTask)
            {
                await writer.WriteLineAsync($"JOB|{volume}|{(moreResources ? 1 : 0)}").ConfigureAwait(false);
            }

            await using CancellationTokenRegistration onCancel = cancellation.Register(() =>
            {
                try
                {
                    writer.WriteLine("CANCEL");
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
                {
                    // The helper already left.
                }
            });

            while (await reader.ReadLineAsync(CancellationToken.None).ConfigureAwait(false) is { } line)
            {
                if (line.Length < 2 || line[1] != '|')
                {
                    continue;
                }

                string payload = line[2..];
                switch (line[0])
                {
                    case 'S':
                        OnStarted(payload);
                        break;
                    case 'O':
                        AddLine(payload);
                        break;
                    case 'P':
                        int split = payload.IndexOf('|');
                        if (split >= 0)
                        {
                            OnProgress(payload[(split + 1)..], int.TryParse(payload[..split], out int percent) ? percent : null);
                        }

                        break;
                    case 'X':
                        cancellation.ThrowIfCancellationRequested();
                        return int.TryParse(payload, out int exitCode) ? exitCode : -1;
                    case 'E':
                        cancellation.ThrowIfCancellationRequested();
                        throw new ChkdskException(payload);
                }
            }
        }

        cancellation.ThrowIfCancellationRequested();
        throw new ChkdskException("The scan helper stopped before CHKDSK finished.");
    }

    // The helper may take a while to appear: the user has to answer the UAC prompt first.
    private static async Task WaitForHelperAsync(NamedPipeServerStream server, Process? launcher, CancellationToken cancellation)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(HelperConnectTimeout);
        Task connect = server.WaitForConnectionAsync(timeout.Token);
        try
        {
            while (!connect.IsCompleted)
            {
                await Task.WhenAny(connect, Task.Delay(400, CancellationToken.None)).ConfigureAwait(false);
                if (!connect.IsCompleted && launcher is { HasExited: true })
                {
                    // Give a helper that connected in its last moments the benefit of the doubt.
                    await Task.WhenAny(connect, Task.Delay(1500, CancellationToken.None)).ConfigureAwait(false);
                    if (!connect.IsCompleted)
                    {
                        timeout.Cancel();
                        throw new ChkdskException("The scan helper could not start. Administrator rights are required to run CHKDSK.");
                    }
                }
            }

            await connect.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new ChkdskException("The scan helper did not respond in time.");
        }
    }

    private void OnStarted(string commandLine)
    {
        lock (_gate)
        {
            _commandLine = commandLine;
            _progressText = "Running…";
        }

        Changed?.Invoke();
    }

    private void AddLine(string line)
    {
        lock (_gate)
        {
            // chkdsk separates stages with blank lines; keep single ones, drop runs of them.
            if (line.Length == 0 && (_output.Count == 0 || _output[^1].Length == 0))
            {
                return;
            }

            if (_output.Count >= MaxOutputLines)
            {
                _output.RemoveRange(0, MaxOutputLines / 4);
            }

            _output.Add(line);
        }

        Changed?.Invoke();
    }

    private void OnProgress(string text, int? percent)
    {
        lock (_gate)
        {
            _progressText = text;
            _progressPercent = percent ?? _progressPercent;
        }

        Changed?.Invoke();
    }

    private T Read<T>(Func<T> reader)
    {
        lock (_gate)
        {
            return reader();
        }
    }

    private static bool WasScheduled(string[] output) =>
        output.Any(line => line.Contains("next time the system restarts", StringComparison.OrdinalIgnoreCase)
            && line.Contains("will be checked", StringComparison.OrdinalIgnoreCase));

    private static string Summarize(ChkdskMode mode, int exitCode, string[] output)
    {
        if (WasScheduled(output))
        {
            return "The volume is in use, so the check is scheduled for the next restart.";
        }

        if (mode.IsReadOnly())
        {
            return exitCode == 0
                ? "No problems found."
                : "Problems were found. Review the output, then run a repair.";
        }

        return exitCode switch
        {
            0 => "Completed. No errors were found.",
            1 => "Completed. Errors were found and fixed.",
            2 => "Completed, but disk cleanup was skipped.",
            _ => "CHKDSK could not finish the repair. Review the output."
        };
    }
}
