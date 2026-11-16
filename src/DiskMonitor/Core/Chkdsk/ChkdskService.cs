using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using DiskMonitor.App;
using DiskMonitor.Core.Notifications;
using DiskMonitor.Diagnostics;

namespace DiskMonitor.Core.Chkdsk;

/// <summary>How a CHKDSK run ended, as the Check Disk page and the scan records report it.</summary>
internal enum ChkdskOutcome
{
    /// <summary>No problems: a clean scan, or a repair that found nothing to fix.</summary>
    Clean,

    /// <summary>A read-only scan found problems that a repair can fix.</summary>
    Problems,

    /// <summary>A repair found errors and fixed them.</summary>
    Fixed,

    /// <summary>The volume was in use, so the work runs at the next restart.</summary>
    Scheduled,

    Cancelled,

    /// <summary>The administrator prompt was declined; nothing ran.</summary>
    Declined,

    /// <summary>CHKDSK could not run, or could not finish.</summary>
    Failed
}

/// <summary>What started a run; shown in the run's sub-line on the Check Disk page.</summary>
internal enum ScanTrigger
{
    User,
    FileSystemError,
    Routine
}

internal sealed record ChkdskResult(
    string Volume,
    ChkdskMode Mode,
    int ExitCode,
    bool Clean,
    bool Cancelled,
    bool Automatic,
    string Summary,
    string? Error)
{
    public ChkdskOutcome Outcome { get; init; }
    public ScanTrigger Trigger { get; init; }
    public DateTime StartedUtc { get; init; }
}

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
    private ScanTrigger _runningTrigger;
    private DateTime _startedUtc;
    private bool _waitingForApproval;
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

    /// <summary>Raised on a background thread whenever output, progress, or running state changes. Throttle in the UI.</summary>
    public event Action? Changed;

    public event Action<ChkdskResult>? Completed;

    public bool IsRunning => Read(() => _runningVolume is not null);
    public string? RunningVolume => Read(() => _runningVolume);
    public ChkdskMode RunningMode => Read(() => _runningMode);
    public ScanTrigger RunningTrigger => Read(() => _runningTrigger);
    public DateTime RunStartedUtc => Read(() => _startedUtc);

    /// <summary>True while the run waits for the user to answer the administrator prompt.</summary>
    public bool IsWaitingForApproval => Read(() => _waitingForApproval);
    public string CommandLine => Read(() => _commandLine);
    public string ProgressText => Read(() => _progressText);
    public int? ProgressPercent => Read(() => _progressPercent);
    public ChkdskResult? LastResult => Read(() => _lastResult);

    /// <summary>Output of the current or most recent run.</summary>
    public string[] GetOutput() => Read(() => _output.ToArray());

    /// <summary>True if this request can start without showing a UAC prompt. Can block: never call from paint or layout.</summary>
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

    /// <summary>
    /// Queues a run and completes when it finishes. Never throws; failures are in the result.
    /// The work always starts on a pool thread: the helper launch and the UAC prompt must never
    /// block the caller, which is usually the UI thread.
    /// </summary>
    public Task<ChkdskResult> RunAsync(string volume, ChkdskMode mode, bool automatic, ScanTrigger trigger = ScanTrigger.User)
    {
        if (automatic && trigger == ScanTrigger.User)
        {
            trigger = ScanTrigger.Routine;
        }

        return Task.Run(() => RunCoreAsync(volume, mode, automatic, trigger));
    }

    /// <summary>
    /// Shows a run in the UI without running anything: a run in progress when <paramref name="result"/>
    /// is null, otherwise a finished one. Used by the screen harness only.
    /// </summary>
    internal void LoadPreview(string commandLine, string[] output, int? percent, ChkdskResult? result, string progressText = "", DateTime? startedUtc = null)
    {
        lock (_gate)
        {
            _commandLine = commandLine;
            _output.Clear();
            _output.AddRange(output);
            _progressPercent = percent;
            _progressText = progressText;
            _startedUtc = startedUtc ?? Clock.UtcNow;
            _waitingForApproval = false;
            if (result is null)
            {
                string[] parts = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                _runningVolume = parts.Select(ChkdskRunner.NormalizeVolume).FirstOrDefault(v => v is not null) ?? "C:";
                _runningMode = ModeFromArguments(commandLine);
                _runningTrigger = ScanTrigger.User;
            }
            else
            {
                _runningVolume = null;
                _lastResult = result;
            }
        }

        Changed?.Invoke();
    }

    private async Task<ChkdskResult> RunCoreAsync(string volume, ChkdskMode mode, bool automatic, ScanTrigger trigger)
    {
        string letter = ChkdskRunner.NormalizeVolume(volume) ?? volume;
        await _queue.WaitAsync().ConfigureAwait(false);
        using CancellationTokenSource cancel = new();
        NotificationService.Suppression mute = _notifier.SuppressRule("chkdsk:", letter, TimeSpan.FromHours(24));
        DateTime startedUtc = Clock.UtcNow;
        ChkdskResult result;
        try
        {
            lock (_gate)
            {
                _cancel = cancel;
                _runningVolume = letter;
                _runningMode = mode;
                _runningTrigger = trigger;
                _startedUtc = startedUtc;
                _waitingForApproval = false;
                _commandLine = "";
                _progressText = Strings.ScanStarting;
                _progressPercent = null;
                _output.Clear();
            }

            Changed?.Invoke();

            int exitCode = -1;
            string? error = null;
            bool cancelled = false;
            bool declined = false;
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
                declined = ex.Declined;
            }
            catch (Exception ex)
            {
                ErrorLog.Write("CHKDSK", ex);
                error = ex.Message;
            }

            string[] output = GetOutput();
            bool scheduled = WasScheduled(output);
            ChkdskOutcome outcome = error is not null ? (declined ? ChkdskOutcome.Declined : ChkdskOutcome.Failed)
                : cancelled ? ChkdskOutcome.Cancelled
                : scheduled ? ChkdskOutcome.Scheduled
                : mode.IsReadOnly() ? (exitCode == 0 ? ChkdskOutcome.Clean : ChkdskOutcome.Problems)
                : exitCode switch { 0 or 2 => ChkdskOutcome.Clean, 1 => ChkdskOutcome.Fixed, _ => ChkdskOutcome.Failed };
            bool clean = outcome is ChkdskOutcome.Clean;
            string summary = error ?? (cancelled ? Strings.ScanCancelledSummary : Summarize(mode, exitCode, scheduled));
            result = new ChkdskResult(letter, mode, exitCode, clean, cancelled, automatic, summary, error)
            {
                Outcome = outcome,
                Trigger = trigger,
                StartedUtc = startedUtc
            };

            // A declined prompt or a cancelled scan changed nothing, so the last real result stays.
            if (outcome is not (ChkdskOutcome.Cancelled or ChkdskOutcome.Declined))
            {
                _state.Update(s => s.Scans[letter] = new ScanRecord
                {
                    TimeUtc = Clock.UtcNow,
                    Mode = mode.ToString(),
                    ExitCode = exitCode,
                    Clean = clean || outcome == ChkdskOutcome.Fixed,
                    Automatic = automatic,
                    Summary = summary,
                    Outcome = outcome.ToString()
                });
            }
        }
        finally
        {
            lock (_gate)
            {
                _cancel = null;
                _runningVolume = null;
                _waitingForApproval = false;
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
            lock (_gate)
            {
                _waitingForApproval = true;
            }

            OnProgress(Strings.ScanWaitingApproval, null);
            string arguments = $"{ChkdskHelper.HelperSwitch} --sid {sid} --volume {volume} --mode {mode}" + (moreResources ? " --perf" : "");
            launcher = Elevation.StartSelfElevated(arguments)
                ?? throw new ChkdskException(Strings.ErrorDeclined, declined: true);
        }

        using (launcher)
        {
            try
            {
                await WaitForHelperAsync(server, launcher, cancellation).ConfigureAwait(false);
            }
            finally
            {
                lock (_gate)
                {
                    _waitingForApproval = false;
                }
            }

            Changed?.Invoke();
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
        throw new ChkdskException(Strings.ErrorHelperStopped);
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
                        throw new ChkdskException(Strings.ErrorHelperNoStart);
                    }
                }
            }

            await connect.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new ChkdskException(Strings.ErrorHelperTimeout);
        }
    }

    private void OnStarted(string commandLine)
    {
        lock (_gate)
        {
            _commandLine = commandLine;
            _progressText = Strings.ScanRunningText;
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

    private static ChkdskMode ModeFromArguments(string commandLine) =>
        commandLine.Contains("/spotfix", StringComparison.OrdinalIgnoreCase) ? ChkdskMode.SpotFix
        : commandLine.Contains("/r", StringComparison.OrdinalIgnoreCase) ? ChkdskMode.FixAndRecover
        : commandLine.Contains("/f", StringComparison.OrdinalIgnoreCase) ? ChkdskMode.Fix
        : ChkdskMode.Scan;

    private static bool WasScheduled(string[] output) =>
        output.Any(line => line.Contains("next time the system restarts", StringComparison.OrdinalIgnoreCase)
            && line.Contains("will be checked", StringComparison.OrdinalIgnoreCase));

    private static string Summarize(ChkdskMode mode, int exitCode, bool scheduled)
    {
        if (scheduled)
        {
            return Strings.SummaryScheduled;
        }

        if (mode.IsReadOnly())
        {
            return exitCode == 0 ? Strings.SummaryScanClean : Strings.SummaryScanProblems;
        }

        return exitCode switch
        {
            0 => Strings.SummaryRepairClean,
            1 => Strings.SummaryRepairFixed,
            2 => Strings.SummaryRepairCleanupSkipped,
            _ => Strings.SummaryRepairFailed
        };
    }
}
