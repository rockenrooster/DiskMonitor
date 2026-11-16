using System.Globalization;
using System.Text.RegularExpressions;
using DiskMonitor.Core.Drives;

namespace DiskMonitor.Core.Events;

/// <summary>The disk and volume an event message refers to, as far as they can be worked out.</summary>
internal readonly record struct ResolvedDevice(int? DiskNumber, string? Volume, string Text, bool RemovableMedia, bool ShadowCopy);

/// <summary>Turns the kernel device names in event messages into disk numbers and drive letters.</summary>
internal static partial class DeviceResolver
{
    public static ResolvedDevice Resolve(string message, DriveSnapshot drives)
    {
        int? diskNumber = null;
        string? letter = null;

        Match match = HarddiskPattern().Match(message);
        if (!match.Success)
        {
            match = DiskNumberPattern().Match(message);
        }

        if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed))
        {
            diskNumber = parsed;
        }

        match = VolumeGuidPattern().Match(message);
        if (match.Success)
        {
            letter = drives.FindVolumeByGuid(match.Value)?.Letter;
        }

        if (letter is null)
        {
            match = HarddiskVolumePattern().Match(message);
            if (match.Success)
            {
                letter = drives.FindVolumeByNtDevice(match.Value)?.Letter;
            }
        }

        if (letter is null)
        {
            match = LabelledLetterPattern().Match(message);
            if (!match.Success)
            {
                match = NamedLetterPattern().Match(message);
            }

            if (match.Success)
            {
                letter = match.Groups[1].Value.ToUpperInvariant() + ":";
            }
        }

        if (letter is null)
        {
            // A bare "X:" only counts when it is a volume that exists right now.
            foreach (Match candidate in BareLetterPattern().Matches(message))
            {
                string value = candidate.Groups[1].Value.ToUpperInvariant() + ":";
                if (drives.FindVolume(value) is not null)
                {
                    letter = value;
                    break;
                }
            }
        }

        VolumeInfo? volume = drives.FindVolume(letter);
        if (diskNumber is null && volume is { DiskNumbers.Count: 1 })
        {
            diskNumber = volume.DiskNumbers[0];
        }

        PhysicalDisk? disk = diskNumber is null ? null : drives.FindDisk(diskNumber.Value);
        bool shadowCopy = message.Contains("HarddiskVolumeShadowCopy", StringComparison.OrdinalIgnoreCase);
        return new ResolvedDevice(diskNumber, letter, Describe(diskNumber, disk, letter), disk?.RemovableMedia == true, shadowCopy);
    }

    /// <summary>Short device label shown beside an event, for example "C: · Samsung SSD 980 (Disk 0)".</summary>
    public static string Describe(int? diskNumber, PhysicalDisk? disk, string? letter)
    {
        if (letter is not null && diskNumber is { } number)
        {
            return disk is null
                ? App.Strings.DeviceVolumeOnDisk(letter, number)
                : App.Strings.DeviceVolumeOnNamedDisk(letter, disk.DisplayName, number);
        }

        if (diskNumber is { } only)
        {
            if (disk is null)
            {
                return App.Strings.DeviceDiskMissing(only);
            }

            string letters = disk.VolumeLetters;
            return letters.Length == 0
                ? App.Strings.DeviceDisk(only, disk.DisplayName)
                : App.Strings.DeviceDiskWithLetters(only, disk.DisplayName, letters);
        }

        return letter is null ? "" : App.Strings.DeviceVolume(letter);
    }

    [GeneratedRegex(@"\\Device\\Harddisk(\d+)\\DR\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HarddiskPattern();

    [GeneratedRegex(@"\bDisk (\d+)\b", RegexOptions.CultureInvariant)]
    private static partial Regex DiskNumberPattern();

    [GeneratedRegex(@"\\\\\?\\Volume\{[0-9a-fA-F-]{36}\}", RegexOptions.CultureInvariant)]
    private static partial Regex VolumeGuidPattern();

    [GeneratedRegex(@"\\Device\\HarddiskVolume\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HarddiskVolumePattern();

    [GeneratedRegex(@"\b(?:Volume name|VolumeId):\s*([A-Za-z]):", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LabelledLetterPattern();

    [GeneratedRegex(@"\b(?:volume|drive|on)\s+([A-Za-z]):(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedLetterPattern();

    [GeneratedRegex(@"(?<![A-Za-z0-9])([A-Za-z]):(?![A-Za-z0-9/])", RegexOptions.CultureInvariant)]
    private static partial Regex BareLetterPattern();
}
