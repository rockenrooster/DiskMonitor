using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DiskMonitor.Core.Drives;

/// <summary>
/// Raw storage queries. Every device is opened with no access rights, which is all these
/// control codes need, so nothing here requires elevation.
/// </summary>
internal static class StorageNative
{
    private const uint FileShareReadWrite = 0x3;
    private const uint OpenExisting = 3;

    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const uint IoctlStoragePredictFailure = 0x002D1100;
    private const uint IoctlDiskGetDriveGeometryEx = 0x000700A0;
    private const uint IoctlVolumeGetVolumeDiskExtents = 0x00560000;
    private const uint FsctlIsVolumeDirty = 0x00090078;

    private const int StorageDeviceProperty = 0;
    private const int StorageDeviceSeekPenaltyProperty = 7;
    private const int StorageDeviceProtocolSpecificProperty = 50;
    private const int StorageDeviceTemperatureProperty = 52;

    public const int ProtocolTypeAta = 2;
    public const int ProtocolTypeNvme = 3;
    private const int ProtocolDataTypeLogPage = 2; // NVMeDataTypeLogPage and AtaDataTypeLogPage share the value
    public const int NvmeLogPageHealthInfo = 2;
    public const int AtaLogDeviceStatistics = 4;
    public const int AtaStatisticsPageGeneral = 1;
    public const int AtaStatisticsPageTemperature = 5;
    public const int AtaStatisticsPageSolidState = 7;

    private const int LogPageLength = 512;
    private const int PropertyQueryHeaderLength = 8; // STORAGE_PROPERTY_QUERY up to AdditionalParameters
    private const int ProtocolDataLength = 40; // STORAGE_PROTOCOL_SPECIFIC_DATA
    private const int ProtocolDescriptorHeaderLength = 8; // STORAGE_PROTOCOL_DATA_DESCRIPTOR up to the protocol data
    private const int PredictFailureLength = 516; // STORAGE_PREDICT_FAILURE: flag + 512 vendor bytes

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        byte[]? inBuffer,
        int inBufferSize,
        byte[]? outBuffer,
        int outBufferSize,
        out int bytesReturned,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDevicePowerState(SafeFileHandle device, [MarshalAs(UnmanagedType.Bool)] out bool isOn);

    [DllImport("kernel32.dll", EntryPoint = "QueryDosDeviceW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDevice(string deviceName, [Out] char[] targetPath, uint maxCharacters);

    [DllImport("kernel32.dll", EntryPoint = "GetVolumeNameForVolumeMountPointW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPoint(string mountPoint, [Out] char[] volumeName, uint bufferLength);

    /// <summary>Opens a physical disk or volume for control queries only. The handle may be invalid.</summary>
    public static SafeFileHandle OpenDevice(string path) =>
        CreateFile(path, 0, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

    public static SafeFileHandle OpenPhysicalDisk(int number) => OpenDevice($@"\\.\PhysicalDrive{number}");

    /// <summary>Opens a volume by drive letter ("C:").</summary>
    public static SafeFileHandle OpenVolume(string letter) => OpenDevice($@"\\.\{letter}");

    /// <summary>
    /// Opens a volume with read access, which file system controls such as the dirty-bit query
    /// require. Windows grants this to administrators only, so the handle is invalid otherwise.
    /// </summary>
    public static SafeFileHandle OpenVolumeForRead(string letter)
    {
        const uint genericRead = 0x80000000;
        return CreateFile($@"\\.\{letter}", genericRead, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
    }

    /// <summary>False only when Windows reports the device as powered down; unknown counts as on.</summary>
    public static bool IsPoweredOn(SafeFileHandle device) => !GetDevicePowerState(device, out bool isOn) || isOn;

    public static string QueryNtDeviceName(string letter)
    {
        char[] buffer = new char[512];
        uint length = QueryDosDevice(letter, buffer, (uint)buffer.Length);
        if (length == 0)
        {
            return "";
        }

        int end = Array.IndexOf(buffer, '\0');
        return new string(buffer, 0, end < 0 ? (int)length : end);
    }

    public static string QueryVolumeGuidPath(string letter)
    {
        char[] buffer = new char[64];
        if (!GetVolumeNameForVolumeMountPoint(letter + "\\", buffer, (uint)buffer.Length))
        {
            return "";
        }

        int end = Array.IndexOf(buffer, '\0');
        return new string(buffer, 0, end < 0 ? buffer.Length : end);
    }

    /// <summary>Disk numbers a volume occupies; more than one for spanned or mirrored volumes.</summary>
    public static int[] QueryVolumeDiskNumbers(SafeFileHandle volume)
    {
        const int extentLength = 24; // DISK_EXTENT
        const int headerLength = 8; // NumberOfDiskExtents plus alignment padding
        byte[] output = new byte[headerLength + extentLength * 32];
        if (!DeviceIoControl(volume, IoctlVolumeGetVolumeDiskExtents, null, 0, output, output.Length, out int returned, IntPtr.Zero)
            || returned < headerLength)
        {
            return [];
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(output);
        count = Math.Clamp(count, 0, (returned - headerLength) / extentLength);
        int[] numbers = new int[count];
        for (int index = 0; index < count; index++)
        {
            numbers[index] = BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(headerLength + index * extentLength));
        }

        return numbers.Distinct().ToArray();
    }

    /// <summary>Whether the volume's dirty bit is set. Null when the file system does not say.</summary>
    public static bool? QueryVolumeDirty(SafeFileHandle volume)
    {
        const uint volumeIsDirty = 0x1;
        byte[] output = new byte[4];
        if (!DeviceIoControl(volume, FsctlIsVolumeDirty, null, 0, output, output.Length, out int returned, IntPtr.Zero)
            || returned < output.Length)
        {
            return null;
        }

        return (BinaryPrimitives.ReadUInt32LittleEndian(output) & volumeIsDirty) != 0;
    }

    /// <summary>Identity strings and bus type from STORAGE_DEVICE_DESCRIPTOR.</summary>
    public static DeviceIdentity? QueryIdentity(SafeFileHandle device)
    {
        const int removableMediaOffset = 10;
        const int vendorOffsetField = 12;
        const int productOffsetField = 16;
        const int revisionOffsetField = 20;
        const int serialOffsetField = 24;
        const int busTypeOffset = 28;

        byte[]? descriptor = QueryProperty(device, StorageDeviceProperty, 1024, out int returned);
        if (descriptor is null || returned < 36)
        {
            return null;
        }

        string vendor = ReadAnsiString(descriptor, returned, BinaryPrimitives.ReadInt32LittleEndian(descriptor.AsSpan(vendorOffsetField)));
        string product = ReadAnsiString(descriptor, returned, BinaryPrimitives.ReadInt32LittleEndian(descriptor.AsSpan(productOffsetField)));
        string revision = ReadAnsiString(descriptor, returned, BinaryPrimitives.ReadInt32LittleEndian(descriptor.AsSpan(revisionOffsetField)));
        string serial = ReadAnsiString(descriptor, returned, BinaryPrimitives.ReadInt32LittleEndian(descriptor.AsSpan(serialOffsetField)));
        int bus = BinaryPrimitives.ReadInt32LittleEndian(descriptor.AsSpan(busTypeOffset));
        BusType busType = Enum.IsDefined((BusType)bus) ? (BusType)bus : BusType.Unknown;

        return new DeviceIdentity(
            ComposeModel(vendor, product, busType),
            serial.Trim().TrimEnd('.'), // NVMe serials arrive with a trailing period
            revision.Trim(),
            busType,
            descriptor[removableMediaOffset] != 0);
    }

    /// <summary>Capacity in bytes, or 0 when the device has no media.</summary>
    public static long QuerySizeBytes(SafeFileHandle device)
    {
        const int diskSizeOffset = 24; // DISK_GEOMETRY_EX.DiskSize
        byte[] output = new byte[64];
        return DeviceIoControl(device, IoctlDiskGetDriveGeometryEx, null, 0, output, output.Length, out int returned, IntPtr.Zero)
            && returned >= diskSizeOffset + sizeof(long)
            ? BinaryPrimitives.ReadInt64LittleEndian(output.AsSpan(diskSizeOffset))
            : 0;
    }

    /// <summary>True for spinning media, false for solid state, null when the driver does not answer.</summary>
    public static bool? QueryIncursSeekPenalty(SafeFileHandle device)
    {
        const int incursSeekPenaltyOffset = 8; // DEVICE_SEEK_PENALTY_DESCRIPTOR.IncursSeekPenalty
        byte[]? descriptor = QueryProperty(device, StorageDeviceSeekPenaltyProperty, 12, out int returned);
        return descriptor is null || returned <= incursSeekPenaltyOffset ? null : descriptor[incursSeekPenaltyOffset] != 0;
    }

    public static double? QueryTemperatureCelsius(SafeFileHandle device)
    {
        const int infoCountOffset = 12; // STORAGE_TEMPERATURE_DATA_DESCRIPTOR.InfoCount
        const int firstTemperatureOffset = 26; // TemperatureInfo[0].Temperature, in Celsius
        byte[]? descriptor = QueryProperty(device, StorageDeviceTemperatureProperty, 128, out int returned);
        if (descriptor is null
            || returned < firstTemperatureOffset + sizeof(short)
            || BinaryPrimitives.ReadUInt16LittleEndian(descriptor.AsSpan(infoCountOffset)) == 0)
        {
            return null;
        }

        short celsius = BinaryPrimitives.ReadInt16LittleEndian(descriptor.AsSpan(firstTemperatureOffset));
        return celsius is > -50 and < 150 ? celsius : null;
    }

    /// <summary>
    /// The drive's own failure prediction. For ATA drives the vendor bytes are the S.M.A.R.T.
    /// attribute table. Null when the drive or its bridge does not support the query.
    /// </summary>
    public static PredictFailureResult? QueryPredictFailure(SafeFileHandle device)
    {
        byte[] output = new byte[PredictFailureLength];
        if (!DeviceIoControl(device, IoctlStoragePredictFailure, null, 0, output, output.Length, out int returned, IntPtr.Zero)
            || returned < sizeof(uint))
        {
            return null;
        }

        bool predicted = BinaryPrimitives.ReadUInt32LittleEndian(output) != 0;
        byte[] vendor = returned >= PredictFailureLength ? output.AsSpan(sizeof(uint), 512).ToArray() : [];
        return new PredictFailureResult(predicted, vendor);
    }

    /// <summary>
    /// Reads one 512-byte protocol log page (NVMe log page or ATA log sub-page). The request is a
    /// STORAGE_PROPERTY_QUERY whose additional parameters are a STORAGE_PROTOCOL_SPECIFIC_DATA;
    /// the reply reuses the buffer as a data descriptor.
    /// </summary>
    public static byte[]? QueryLogPage(SafeFileHandle device, int protocolType, int logPage, int subPage)
    {
        const int requestDataOffset = PropertyQueryHeaderLength;
        const int replyDataOffsetField = ProtocolDescriptorHeaderLength + 16; // ProtocolDataOffset
        byte[] buffer = new byte[PropertyQueryHeaderLength + ProtocolDataLength + LogPageLength];
        Span<byte> request = buffer.AsSpan(requestDataOffset);
        BinaryPrimitives.WriteInt32LittleEndian(buffer, StorageDeviceProtocolSpecificProperty);
        BinaryPrimitives.WriteInt32LittleEndian(request, protocolType);
        BinaryPrimitives.WriteInt32LittleEndian(request[4..], ProtocolDataTypeLogPage);
        BinaryPrimitives.WriteInt32LittleEndian(request[8..], logPage);
        BinaryPrimitives.WriteInt32LittleEndian(request[12..], subPage);
        BinaryPrimitives.WriteInt32LittleEndian(request[16..], ProtocolDataLength);
        BinaryPrimitives.WriteInt32LittleEndian(request[20..], LogPageLength);
        if (!DeviceIoControl(device, IoctlStorageQueryProperty, buffer, buffer.Length, buffer, buffer.Length, out int returned, IntPtr.Zero))
        {
            return null;
        }

        int dataOffset = ProtocolDescriptorHeaderLength + BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(replyDataOffsetField));
        return dataOffset >= ProtocolDescriptorHeaderLength && dataOffset + LogPageLength <= Math.Min(returned, buffer.Length)
            ? buffer.AsSpan(dataOffset, LogPageLength).ToArray()
            : null;
    }

    private static byte[]? QueryProperty(SafeFileHandle device, int propertyId, int outputLength, out int returned)
    {
        byte[] input = new byte[PropertyQueryHeaderLength + sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(input, propertyId);
        byte[] output = new byte[outputLength];
        return DeviceIoControl(device, IoctlStorageQueryProperty, input, input.Length, output, output.Length, out returned, IntPtr.Zero) && returned > 0
            ? output
            : null;
    }

    private static string ReadAnsiString(byte[] buffer, int valid, int offset)
    {
        if (offset <= 0 || offset >= valid)
        {
            return "";
        }

        int end = offset;
        while (end < valid && buffer[end] != 0)
        {
            end++;
        }

        // Padding is kept: for ATA drives it decides whether the vendor and product fields join with a space.
        return Encoding.ASCII.GetString(buffer, offset, end - offset);
    }

    internal static string ComposeModel(string vendor, string product, BusType bus)
    {
        string vendorName = vendor.Trim();
        string productName = product.Trim().TrimEnd('_');

        // SATA and NVMe drives report a placeholder vendor ("ATA", "NVMe") or none.
        if (vendorName.Length == 0
            || vendorName.Equals("ATA", StringComparison.OrdinalIgnoreCase)
            || vendorName.Equals("NVMe", StringComparison.OrdinalIgnoreCase)
            || productName.StartsWith(vendorName, StringComparison.OrdinalIgnoreCase))
        {
            return CollapseSpaces(productName);
        }

        if (productName.Length == 0)
        {
            return CollapseSpaces(vendorName);
        }

        // Some ATA paths split one model string across the two fields ("WDC WD80" + "EDAZ-11TA3A0"),
        // so the pieces are rejoined exactly as they were. Other buses carry two separate names.
        string joined = bus is BusType.Sata or BusType.Ata
            ? vendor.TrimStart() + productName
            : $"{vendorName} {productName}";
        return CollapseSpaces(joined);
    }

    private static string CollapseSpaces(string text)
    {
        StringBuilder result = new(text.Length);
        foreach (char c in text.Trim())
        {
            if (c != ' ' || (result.Length > 0 && result[^1] != ' '))
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }
}

internal sealed record DeviceIdentity(string Model, string Serial, string Firmware, BusType Bus, bool RemovableMedia);

internal sealed record PredictFailureResult(bool Predicted, byte[] VendorData);
