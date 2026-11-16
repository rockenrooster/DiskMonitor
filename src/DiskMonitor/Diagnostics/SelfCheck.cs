using System.Runtime.InteropServices;
using DiskMonitor.App;
using DiskMonitor.Core;
using DiskMonitor.Core.Chkdsk;
using DiskMonitor.Core.Drives;
using DiskMonitor.Core.Events;
using DiskMonitor.Core.Notifications;

namespace DiskMonitor.Diagnostics;

/// <summary>
/// "DiskMonitor.exe --self-check": prints what the monitor can see on this machine and exits.
/// Changes nothing and shows no notifications. Exit code 0 means every check passed.
/// </summary>
internal static class SelfCheck
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    public static int Run(string[] args)
    {
        // A GUI executable has no console of its own; borrow the one it was started from.
        _ = AttachConsole(AttachParentProcess);
        TextWriter output = Console.Out;
        bool verbose = CommandLine.Has(args, "--verbose");
        int failures = 0;

        void Check(string name, bool ok, string detail)
        {
            output.WriteLine($"[{(ok ? " OK " : "FAIL")}] {name}: {detail}");
            if (!ok)
            {
                failures++;
            }
        }

        output.WriteLine($"Disk Monitor self-check · v{typeof(SelfCheck).Assembly.GetName().Version} · elevated={Elevation.IsElevated} · installed={AppPaths.IsInstalledBuild}");
        output.WriteLine($"Data directory: {AppPaths.DataDirectory}");

        AppSettings settings = AppSettings.Load();
        if (int.TryParse(CommandLine.Value(args, "--days"), out int days) && days > 0)
        {
            settings.HistoryDays = days; // look further back than the saved setting, for this run only
        }

        using StateStore state = StateStore.Load(persist: false);
        EventStore store = new(state);

        DriveSnapshot drives = DriveSnapshot.Empty;
        try
        {
            drives = DriveScanner.Scan(settings.TemperatureWarningC, DriveSnapshot.Empty);
            Check("Drives", drives.Disks.Count > 0, $"{drives.Disks.Count} physical, {drives.Volumes.Count} volumes");
            foreach (PhysicalDisk disk in drives.Disks)
            {
                string serial = disk.Serial.Length > 4 ? "…" + disk.Serial[^4..] : disk.Serial;
                output.WriteLine(
                    $"       Disk {disk.Number}: {disk.DisplayName} | {disk.BusLabel} {disk.MediaLabel} | {Format.Size(disk.SizeBytes)} | sn {serial} | fw {disk.Firmware}" +
                    $" | health {disk.Health}{(disk.Asleep ? " (asleep)" : "")} | temp {disk.TemperatureC?.ToString("0") ?? "-"} C | life {disk.LifeRemainingPercent?.ToString() ?? "-"}%" +
                    $" | poh {disk.PowerOnHours?.ToString() ?? "-"} | realloc {disk.Reallocated?.ToString() ?? "-"} pend {disk.Pending?.ToString() ?? "-"} unc {disk.Uncorrectable?.ToString() ?? "-"}" +
                    $" crc {disk.CrcErrors?.ToString() ?? "-"} media {disk.MediaErrors?.ToString() ?? "-"} | smart attrs {disk.Attributes.Count} | predict {disk.PredictFailure?.ToString() ?? "-"}" +
                    $" | removable {disk.RemovableMedia} | volumes [{disk.VolumeLetters}]");
                foreach (string note in disk.HealthNotes)
                {
                    output.WriteLine($"         note: {note}");
                }
            }

            foreach (VolumeInfo volume in drives.Volumes)
            {
                output.WriteLine(
                    $"       {volume.Letter} '{volume.Label}' {volume.FileSystem} | {Format.Size(volume.FreeBytes)} free of {Format.Size(volume.TotalBytes)}" +
                    $" | dirty {volume.Dirty?.ToString() ?? "unknown"} | disks [{string.Join(",", volume.DiskNumbers)}] | {volume.NtDevice} | system {volume.IsSystem} | removable {volume.Removable}");
            }
        }
        catch (Exception ex)
        {
            Check("Drives", false, ex.Message);
        }

        try
        {
            DriveSnapshot snapshot = drives;
            using EventLogMonitor monitor = new(settings, store, state, () => snapshot);
            List<DiskEvent> missed = [];
            monitor.MissedEventsFound += (events, _) => missed.AddRange(events);
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            monitor.Start();
            watch.Stop();
            IReadOnlyList<DiskEvent> events = store.Snapshot();
            Check("Event logs", monitor.IsLive && monitor.LastError is null,
                $"live={monitor.IsLive}, {monitor.WatchedProviderCount} providers, {events.Count} events in {settings.HistoryDays} days, loaded in {watch.ElapsedMilliseconds} ms" +
                (monitor.LastError is null ? "" : $", error: {monitor.LastError}"));
            foreach (Tier tier in TierExtensions.Descending)
            {
                output.WriteLine($"       {tier.Label(),-8} {events.Count(e => e.Tier == tier)}");
            }

            foreach (IGrouping<string, DiskEvent> group in events.GroupBy(e => e.GroupKey).OrderByDescending(g => g.Count()).Take(verbose ? 40 : 12))
            {
                DiskEvent sample = group.First();
                output.WriteLine($"       {group.Count(),5}x [{sample.Tier.Label(),-8}] {sample.RuleId,-16} {sample.Title} | {sample.DeviceText} | scan={sample.SuggestScan}");
                if (verbose)
                {
                    output.WriteLine($"              {Format.SingleLine(sample.Message, 200)}");
                }
            }
        }
        catch (Exception ex)
        {
            Check("Event logs", false, ex.Message);
        }

        try
        {
            ToastRequest request = new()
            {
                Title = "Critical: Bad block on disk",
                Line1 = "Disk 0 · \"Example\" <drive> & co (C:)",
                Line2 = "Back up this drive now.",
                Attribution = "System log · disk 7",
                LaunchUri = AppLinks.ForEvent("System|1|2"),
                Actions = [new ToastAction("View details", AppLinks.ForEvent("System|1|2")), new ToastAction("Not now", "", Dismiss: true)],
                Sticky = true
            };
            string xml = NotificationService.BuildXml(request);
            System.Xml.XmlDocument document = new();
            document.LoadXml(xml);
            bool roundTrip = AppLinks.TryParse(AppLinks.ForEvent("System|1|2"), out AppCommand command)
                && command.Verb == AppCommand.Event && command["key"] == "System|1|2";
            Check("Toast content", document.DocumentElement?.Name == "toast" && roundTrip, $"{xml.Length} characters of well-formed XML, links round-trip");
        }
        catch (Exception ex)
        {
            Check("Toast content", false, ex.Message);
        }

        Check("Startup entry", true, StartupManager.IsEnabled() ? "enabled" : "not enabled");
        Check("Prompt-free scans", true, ScanTask.IsRegistered() ? "task registered" : "not registered (scans will ask for approval)");

        output.WriteLine(failures == 0 ? "Self-check passed." : $"Self-check found {failures} problem(s).");
        output.Flush();
        return failures == 0 ? 0 : 1;
    }
}
