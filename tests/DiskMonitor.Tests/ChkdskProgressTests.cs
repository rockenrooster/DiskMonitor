using DiskMonitor.Core.Chkdsk;

namespace DiskMonitor.Tests;

/// <summary>
/// CHKDSK's console stream and progress as an online scan (<c>chkdsk C: /scan</c>) writes them, in the
/// shape recorded from a real run on Windows 11: the progress line is rewritten with bare carriage
/// returns and cleared with spaces, count lines end in CR CR LF, "Total" restarts after stage 1, and
/// a long phase late in stage 2 prints nothing. The numbers here are made up.
/// </summary>
public sealed class ChkdskProgressTests
{
    private static readonly string Clear = "\r" + new string(' ', 87) + "\r" + new string(' ', 20) + "\r";

    private static readonly string Stream =
        "The type of the file system is NTFS.\r\n" +
        "\r\nStage 1: Examining basic file system structure ...\r\n" +
        "Progress: 0 of 1000000 done; Stage:  0%; Total:  0%; ETA:   2:55:18    \r" +
        "Progress: 210000 of 1000000 done; Stage: 21%; Total:  7%; ETA:   0:01:48 ...\r" +
        "Progress: 1000000 of 1000000 done; Stage: 100%; Total: 35%; ETA:   0:01:08 .  " + Clear +
        "  1000000 file records processed.                                          \r\r\n" +
        "File verification completed.\r\n" +
        " Phase duration (File record verification): 35.64 seconds.\r\n" +
        "Progress: 1 of 20000 done; Stage:  0%; Total:  0%; ETA:   0:06:53 .. \r" +
        "Progress: 20000 of 20000 done; Stage: 100%; Total:  0%; ETA:   1:33:24    " + Clear +
        "  20000 large file records processed.                                      \r\r\n" +
        " Phase duration (Orphan file record recovery): 44.64 milliseconds.\r\n" +
        "\r\nStage 2: Examining file name linkage ...\r\n" +
        "Progress: 30000 of 1500000 done; Stage:  2%; Total:  0%; ETA:   1:32:03 ..\r" +
        "Progress: 50000 of 50000 done; Stage: 100%; Total:  1%; ETA:   1:09:40 . " + Clear +
        "  50000 reparse records processed.                                         \r\r\n" +
        "Progress: 1245000 of 1500000 done; Stage: 83%; Total:  2%; ETA:   1:40:49 .\r" +
        "  1500000 index entries processed.                                         \r\r\n" +
        "Index verification completed.\r\n" +
        " Phase duration (Index verification): 1.66 minutes.\r\n" +
        "Progress: 1 of 0 done; Stage: 99%; Total:  2%; ETA:   1:52:38 .  " + Clear +
        "  0 unindexed files scanned.                                               \r\r\n" +
        " Phase duration (Orphan reconnection): 2.58 minutes.\r\n" +
        "\r\nStage 3: Examining security descriptors ...\r\n" +
        "Security descriptor verification completed.\r\n" +
        "Progress: 90 of 90 done; Stage: 100%; Total:  2%; ETA:   2:22:49 .  " + Clear +
        "  210000 data files processed.                                             \r\r\n" +
        "\r\nWindows has scanned the file system and found no problems.\r\n" +
        "No further action is required.\r\n";

    [Theory]
    [InlineData(40)]
    [InlineData(1)]
    [InlineData(4096)]
    public void CountLinesStayInTheConsoleAndProgressDoesNot(int chunk)
    {
        List<string> lines = [];
        List<string> progress = [];
        ChkdskRunner.PumpOutput(new ChunkedReader(Stream, chunk), lines.Add, progress.Add);

        Assert.Contains("  1000000 file records processed.", lines);
        Assert.Contains("  20000 large file records processed.", lines);
        Assert.Contains("  1500000 index entries processed.", lines);
        Assert.Contains("  210000 data files processed.", lines);
        Assert.Contains("Stage 2: Examining file name linkage ...", lines);
        Assert.Equal("No further action is required.", lines[^1]);
        Assert.DoesNotContain(lines, line => line.TrimStart().StartsWith("Progress:", StringComparison.Ordinal));
        Assert.All(progress, text => Assert.StartsWith("Progress:", text));
        Assert.Equal(10, progress.Count);
    }

    [Fact]
    public void PercentFollowsTheStagesAndNeverGoesBack()
    {
        ChkdskProgress tracker = new(ChkdskMode.Scan);
        DateTime start = new(2026, 11, 16, 20, 0, 0, DateTimeKind.Utc);
        List<int> seen = [];
        Feed(tracker, start, seen);

        for (int i = 1; i < seen.Count; i++)
        {
            Assert.True(seen[i] >= seen[i - 1], $"The percentage went back from {seen[i - 1]} to {seen[i]}.");
        }

        Assert.Contains(33, seen);
        Assert.Contains(61, seen);
        Assert.DoesNotContain(seen, percent => percent is > 61 and < 66);
        Assert.Equal(100, seen[^1]);
    }

    [Fact]
    public void TimeLeftIsAnEstimateFromThePaceNotChkdsksEta()
    {
        ChkdskProgress tracker = new(ChkdskMode.Scan);
        DateTime start = new(2026, 11, 16, 20, 0, 0, DateTimeKind.Utc);
        tracker.OnLine("The type of the file system is NTFS.", start);
        Assert.Null(tracker.TimeLeft(start.AddSeconds(5)));

        tracker.OnLine("Stage 1: Examining basic file system structure ...", start.AddSeconds(1));
        tracker.OnProgress("Progress: 1000000 of 1000000 done; Stage: 100%; Total: 35%; ETA:   0:01:08", start.AddSeconds(40));
        tracker.OnLine("Stage 2: Examining file name linkage ...", start.AddSeconds(41));
        tracker.OnProgress("Progress: 1245000 of 1500000 done; Stage: 83%; Total:  2%; ETA:   1:40:49", start.AddSeconds(131));

        // 61 % after 131 s: about 84 s left at that pace, where CHKDSK said 1 h 40 min.
        TimeSpan left = Assert.IsType<TimeSpan>(tracker.TimeLeft(start.AddSeconds(131)));
        Assert.InRange(left.TotalSeconds, 70, 100);

        // During the silent phase the estimate grows with the time taken, but stays minutes, not hours.
        TimeSpan later = Assert.IsType<TimeSpan>(tracker.TimeLeft(start.AddSeconds(290)));
        Assert.InRange(later.TotalMinutes, 2, 5);
    }

    [Fact]
    public void FatReportsItsOwnPercentage()
    {
        ChkdskProgress tracker = new(ChkdskMode.Scan);
        DateTime now = new(2026, 11, 16, 20, 0, 0, DateTimeKind.Utc);
        tracker.OnLine("The type of the file system is exFAT.", now);
        Assert.Null(tracker.Percent);
        tracker.OnProgress("10 percent complete.", now.AddSeconds(2));
        Assert.Equal(10, tracker.Percent);
        tracker.OnProgress("8 percent complete.", now.AddSeconds(3));
        Assert.Equal(10, tracker.Percent);
    }

    [Fact]
    public void RepairWithBadSectorScanHasFiveStages()
    {
        ChkdskProgress tracker = new(ChkdskMode.FixAndRecover);
        DateTime now = new(2026, 11, 16, 20, 0, 0, DateTimeKind.Utc);
        tracker.OnLine("Stage 4: Looking for bad clusters in user file data ...", now);
        Assert.Equal(60, tracker.Percent);
    }

    private static void Feed(ChkdskProgress tracker, DateTime start, List<int> seen)
    {
        int second = 0;
        ChkdskRunner.PumpOutput(new ChunkedReader(Stream, 40), line => Record(() => tracker.OnLine(line, start.AddSeconds(second++))), text => Record(() => tracker.OnProgress(text, start.AddSeconds(second++))));

        void Record(Action step)
        {
            step();
            if (tracker.Percent is { } percent)
            {
                seen.Add(percent);
            }
        }
    }

    /// <summary>Hands out the text a few characters at a time, as CHKDSK's 40-byte writes arrive.</summary>
    private sealed class ChunkedReader(string text, int chunk) : TextReader
    {
        private int _position;

        public override int Read(char[] buffer, int index, int count)
        {
            int length = Math.Min(Math.Min(count, chunk), text.Length - _position);
            text.CopyTo(_position, buffer, index, length);
            _position += length;
            return length;
        }
    }
}
