using System.Runtime.InteropServices;

namespace DiskMonitor.Core.Notifications;

/// <summary>What Windows says about this app's permission to show toasts.</summary>
internal enum ToastSetting
{
    Enabled = 0,
    DisabledForApplication = 1,
    DisabledForUser = 2,
    DisabledByGroupPolicy = 3,
    DisabledByManifest = 4,
    Unknown = -1
}

/// <summary>
/// Shows Windows toast notifications by calling the WinRT notification interfaces through their
/// COM vtables. This avoids shipping the 25 MB Windows SDK projection for a handful of calls.
/// All methods return an HRESULT; negative means failure. Call from the UI thread.
/// </summary>
internal static unsafe class ToastInterop
{
    // Vtable slots: 0-2 IUnknown, 3-5 IInspectable, interface methods from 6.
    private const int SlotFirst = 6;
    private const int SlotCreateToastNotifierWithId = 7; // IToastNotificationManagerStatics
    private const int SlotNotifierSetting = 8; // IToastNotifier.get_Setting
    private const int SlotPutTag = 6; // IToastNotification2
    private const int SlotPutGroup = 8; // IToastNotification2
    private const int SlotPutSuppressPopup = 10; // IToastNotification2 (windows.ui.notifications.h, SDK 10.0.22621)

    private static readonly Guid ToastManagerStaticsId = new("50AC103F-D235-4598-BBEF-98FE4D1A3AD4");
    private static readonly Guid ToastFactoryId = new("04124B20-82C6-4229-B109-FD9ED4662B53");
    private static readonly Guid ToastNotification2Id = new("9DFB9FD1-143A-490E-90BF-B9FBA7132DE7");
    private static readonly Guid XmlDocumentId = new("F7F3A506-1E87-42D6-BCFB-B8C809FA5494");
    private static readonly Guid XmlDocumentIoId = new("6CD0E74E-EE65-4489-9EBF-CA43E87BA637");

    private static IntPtr _notifier;
    private static IntPtr _toastFactory;
    private static string? _notifierAppId;

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, in Guid interfaceId, out IntPtr factory);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoActivateInstance(IntPtr activatableClassId, out IntPtr instance);

    [DllImport("combase.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string? source, uint length, out IntPtr hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    /// <summary>
    /// Shows a toast built from <paramref name="xml"/>. A toast with the same tag and group replaces the
    /// earlier one. With <paramref name="suppressPopup"/> the toast goes straight to the notification
    /// centre without appearing on screen (quiet hours).
    /// </summary>
    public static int Show(string appId, string xml, string? tag, string? group, bool suppressPopup = false)
    {
        IntPtr document = IntPtr.Zero;
        IntPtr documentIo = IntPtr.Zero;
        IntPtr xmlDocument = IntPtr.Zero;
        IntPtr toast = IntPtr.Zero;
        IntPtr toast2 = IntPtr.Zero;
        try
        {
            int hr = EnsureNotifier(appId);
            if (hr < 0)
            {
                return hr;
            }

            hr = Activate("Windows.Data.Xml.Dom.XmlDocument", out document);
            if (hr < 0)
            {
                return hr;
            }

            hr = Marshal.QueryInterface(document, in XmlDocumentIoId, out documentIo);
            if (hr < 0)
            {
                return hr;
            }

            hr = CallWithString(documentIo, SlotFirst, xml); // IXmlDocumentIO.LoadXml
            if (hr < 0)
            {
                return hr;
            }

            hr = Marshal.QueryInterface(document, in XmlDocumentId, out xmlDocument);
            if (hr < 0)
            {
                return hr;
            }

            // IToastNotificationFactory.CreateToastNotification(IXmlDocument*, IToastNotification**)
            IntPtr created;
            hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Slot(_toastFactory, SlotFirst))(_toastFactory, xmlDocument, &created);
            if (hr < 0)
            {
                return hr;
            }

            toast = created;
            if ((tag is not null || group is not null || suppressPopup) && Marshal.QueryInterface(toast, in ToastNotification2Id, out toast2) >= 0)
            {
                if (tag is not null)
                {
                    _ = CallWithString(toast2, SlotPutTag, tag);
                }

                if (group is not null)
                {
                    _ = CallWithString(toast2, SlotPutGroup, group);
                }

                if (suppressPopup)
                {
                    // put_SuppressPopup(boolean); a WinRT boolean is one byte.
                    _ = ((delegate* unmanaged[Stdcall]<IntPtr, byte, int>)Slot(toast2, SlotPutSuppressPopup))(toast2, 1);
                }
            }

            // IToastNotifier.Show(IToastNotification*)
            return ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Slot(_notifier, SlotFirst))(_notifier, toast);
        }
        finally
        {
            Release(toast2);
            Release(toast);
            Release(xmlDocument);
            Release(documentIo);
            Release(document);
        }
    }

    /// <summary>Asks Windows whether this app may currently show toasts.</summary>
    public static ToastSetting QuerySetting(string appId)
    {
        if (EnsureNotifier(appId) < 0)
        {
            return ToastSetting.Unknown;
        }

        int setting;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Slot(_notifier, SlotNotifierSetting))(_notifier, &setting);
        return hr < 0 || !Enum.IsDefined((ToastSetting)setting) ? ToastSetting.Unknown : (ToastSetting)setting;
    }

    // The notifier and factory are process-wide and agile; they are created once and kept.
    private static int EnsureNotifier(string appId)
    {
        if (_notifier != IntPtr.Zero && _toastFactory != IntPtr.Zero && _notifierAppId == appId)
        {
            return 0;
        }

        IntPtr statics = IntPtr.Zero;
        IntPtr appIdString = IntPtr.Zero;
        try
        {
            int hr = GetFactory("Windows.UI.Notifications.ToastNotificationManager", in ToastManagerStaticsId, out statics);
            if (hr < 0)
            {
                return hr;
            }

            hr = WindowsCreateString(appId, (uint)appId.Length, out appIdString);
            if (hr < 0)
            {
                return hr;
            }

            IntPtr notifier;
            hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Slot(statics, SlotCreateToastNotifierWithId))(statics, appIdString, &notifier);
            if (hr < 0)
            {
                return hr;
            }

            hr = GetFactory("Windows.UI.Notifications.ToastNotification", in ToastFactoryId, out IntPtr factory);
            if (hr < 0)
            {
                Release(notifier);
                return hr;
            }

            Release(_notifier);
            Release(_toastFactory);
            _notifier = notifier;
            _toastFactory = factory;
            _notifierAppId = appId;
            return 0;
        }
        finally
        {
            if (appIdString != IntPtr.Zero)
            {
                _ = WindowsDeleteString(appIdString);
            }

            Release(statics);
        }
    }

    private static int GetFactory(string className, in Guid interfaceId, out IntPtr factory)
    {
        factory = IntPtr.Zero;
        int hr = WindowsCreateString(className, (uint)className.Length, out IntPtr classId);
        if (hr < 0)
        {
            return hr;
        }

        try
        {
            return RoGetActivationFactory(classId, in interfaceId, out factory);
        }
        finally
        {
            _ = WindowsDeleteString(classId);
        }
    }

    private static int Activate(string className, out IntPtr instance)
    {
        instance = IntPtr.Zero;
        int hr = WindowsCreateString(className, (uint)className.Length, out IntPtr classId);
        if (hr < 0)
        {
            return hr;
        }

        try
        {
            return RoActivateInstance(classId, out instance);
        }
        finally
        {
            _ = WindowsDeleteString(classId);
        }
    }

    // Calls a method whose only argument is an HSTRING, such as LoadXml or put_Tag.
    private static int CallWithString(IntPtr instance, int slot, string value)
    {
        int hr = WindowsCreateString(value, (uint)value.Length, out IntPtr text);
        if (hr < 0)
        {
            return hr;
        }

        try
        {
            return ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Slot(instance, slot))(instance, text);
        }
        finally
        {
            _ = WindowsDeleteString(text);
        }
    }

    private static void* Slot(IntPtr instance, int index) => (*(void***)instance)[index];

    private static void Release(IntPtr instance)
    {
        if (instance != IntPtr.Zero)
        {
            _ = Marshal.Release(instance);
        }
    }
}
