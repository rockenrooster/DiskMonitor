using System.Globalization;
using System.Text.RegularExpressions;

namespace DiskMonitor.Core.Chkdsk;

/// <summary>
/// How far a CHKDSK run has got, worked out from its output. CHKDSK's own "Total" figure restarts after
/// stage 1 of an online scan (35 % at the end of stage 1, then 0 to 2 % until the end) and its ETA then
/// climbs to hours, so neither is used. The percentage comes from the stage CHKDSK announces
/// ("Stage 2: ...") and the progress within it ("Stage: 41%"), each stage an equal share, and it never
/// goes back. FAT and exFAT report "N percent complete" instead, which is used as it is. The time left
/// is estimated from the time taken so far.
/// </summary>
internal sealed partial class ChkdskProgress(ChkdskMode mode)
{
    private const double MinimumForEstimate = 0.05;
    private static readonly TimeSpan MinimumElapsed = TimeSpan.FromSeconds(10);

    // An online scan and a repair have three stages; a repair that also looks for bad sectors has five.
    private readonly int _stages = mode == ChkdskMode.FixAndRecover ? 5 : 3;
    private int _stage;
    private long _stageTotal;
    private double _fraction;
    private DateTime? _firstUtc;

    /// <summary>The overall percentage, or null until CHKDSK reports a stage or a percentage.</summary>
    public int? Percent { get; private set; }

    /// <summary>A permanent output line; "Stage N:" starts stage N.</summary>
    public void OnLine(string line, DateTime nowUtc)
    {
        _firstUtc ??= nowUtc;
        if (Number(StageHeaderPattern().Match(line)) is { } stage && stage > _stage)
        {
            _stage = stage;
            _stageTotal = 0;
            Advance(StageStart(_stage));
        }
    }

    /// <summary>The self-overwriting progress line.</summary>
    public void OnProgress(string text, DateTime nowUtc)
    {
        _firstUtc ??= nowUtc;
        if (_stage > 0 && Number(StagePercentPattern().Match(text)) is { } withinStage)
        {
            // A stage also reports shorter side passes ("Progress: 188874 of 188874", the reparse points
            // during index verification) that reach 100 % long before the stage ends, and placeholders
            // ("0 of 0"). Only the stage's largest count is followed.
            Match counts = CountsPattern().Match(text);
            long total = counts.Success && long.TryParse(counts.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed) ? parsed : _stageTotal;
            if (counts.Success && total == 0 || total < _stageTotal)
            {
                return;
            }

            _stageTotal = total;
            Advance(StageStart(_stage) + withinStage / 100.0 / Math.Max(_stages, _stage));
        }
        else if (Number(PercentCompletePattern().Match(text)) is { } overall)
        {
            Advance(overall / 100.0);
        }
    }

    /// <summary>Time left at the pace so far; null until 5 % and 10 seconds are behind.</summary>
    public TimeSpan? TimeLeft(DateTime nowUtc)
    {
        if (_firstUtc is not { } first || _fraction < MinimumForEstimate || nowUtc - first < MinimumElapsed)
        {
            return null;
        }

        double elapsed = (nowUtc - first).TotalSeconds;
        return TimeSpan.FromSeconds(Math.Round(elapsed * (1 - _fraction) / _fraction));
    }

    /// <summary>True for FAT and exFAT's "N percent complete." progress text.</summary>
    internal static bool IsPercentComplete(string text) => PercentCompletePattern().IsMatch(text);

    private double StageStart(int stage) => (stage - 1) / (double)Math.Max(_stages, stage);

    private void Advance(double fraction)
    {
        _fraction = Math.Max(_fraction, Math.Clamp(fraction, 0, 1));
        Percent = (int)Math.Floor(_fraction * 100);
    }

    private static int? Number(Match match) =>
        match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            ? Math.Clamp(value, 0, 100)
            : null;

    [GeneratedRegex(@"^\s*Stage (\d+):", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StageHeaderPattern();

    [GeneratedRegex(@"Stage:\s*(\d+)\s*%", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StagePercentPattern();

    [GeneratedRegex(@"Progress:\s*\d+\s+of\s+(\d+)\s+done", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CountsPattern();

    [GeneratedRegex(@"(\d+) percent complete", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PercentCompletePattern();
}
