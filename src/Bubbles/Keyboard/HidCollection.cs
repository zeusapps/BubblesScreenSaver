using System.Runtime.InteropServices;

using Microsoft.Win32.SafeHandles;

namespace Bubbles.Keyboard;

/// <summary>One HID collection: where it lives, what it says it is, and how big a report it
/// wants.</summary>
/// <param name="Path">The device interface path, which is what CreateFile takes.</param>
/// <param name="OutputReportLength">Including the report id byte. A write must be exactly this
/// long or the firmware discards it without a word.</param>
internal sealed record HidCollection(
    string Path,
    ushort VendorId,
    ushort ProductId,
    ushort UsagePage,
    ushort Usage,
    int OutputReportLength);

/// <summary>Finding and writing to HID collections, by hand.
///
/// A device is not one thing. The keyboard this was written for presents ten collections
/// behind one product id -- a keyboard, a touchpad, consumer controls, two vendor collections
/// that take lighting commands, and a standards-track lighting collection Windows itself
/// drives. They differ in usage page, in report id and in report length, and picking the wrong
/// one produces writes that succeed and do nothing.
///
/// So collections are matched on what they declare, never on their position in the list.</summary>
internal static class Hid
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint ShareReadWrite = 0x00000003;
    private const uint OpenExisting = 3;

    private const int DigcfPresent = 0x02;
    private const int DigcfDeviceInterface = 0x10;

    private const int HidpStatusSuccess = 0x00110000;

    /// <summary>Every HID collection currently attached, with what it declares about itself.
    ///
    /// Opened with no access rights at all, which is enough to read the capabilities and is
    /// deliberately less than enough to disturb anything. A collection that will not open is
    /// skipped rather than fatal: the keyboard and mouse collections are owned by Windows and
    /// refusing us is the correct behaviour, not an error.</summary>
    public static List<HidCollection> Collections()
    {
        var found = new List<HidCollection>();

        Native.HidD_GetHidGuid(out var hidGuid);

        var set = Native.SetupDiGetClassDevs(
            ref hidGuid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);

        if (set == new IntPtr(-1)) return found;

        try
        {
            for (var index = 0; ; index++)
            {
                var interfaceData = new Native.SP_DEVICE_INTERFACE_DATA();
                interfaceData.cbSize = Marshal.SizeOf(interfaceData);

                if (!Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, index, ref interfaceData))
                    break;

                var path = DetailPath(set, ref interfaceData);
                if (path is null) continue;

                var collection = Describe(path);
                if (collection is not null) found.Add(collection);
            }
        }
        finally
        {
            Native.SetupDiDestroyDeviceInfoList(set);
        }

        return found;
    }

    private static string? DetailPath(IntPtr set, ref Native.SP_DEVICE_INTERFACE_DATA interfaceData)
    {
        var required = 0;
        Native.SetupDiGetDeviceInterfaceDetail(set, ref interfaceData, IntPtr.Zero, 0, ref required, IntPtr.Zero);

        if (required <= 0) return null;

        var buffer = Marshal.AllocHGlobal(required);

        try
        {
            // cbSize of the detail struct, which is 8 on 64-bit and 6 on 32-bit -- and is the
            // size of the *struct*, not of the buffer, which is a well-known way to get
            // ERROR_INVALID_USER_BUFFER out of this call.
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);

            if (!Native.SetupDiGetDeviceInterfaceDetail(
                    set, ref interfaceData, buffer, required, ref required, IntPtr.Zero))
                return null;

            return Marshal.PtrToStringUni(IntPtr.Add(buffer, 4));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static HidCollection? Describe(string path)
    {
        using var handle = Native.CreateFile(
            path, 0, ShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

        if (handle.IsInvalid) return null;

        var attributes = new Native.HIDD_ATTRIBUTES();
        attributes.Size = Marshal.SizeOf(attributes);

        if (!Native.HidD_GetAttributes(handle, ref attributes)) return null;

        if (!Native.HidD_GetPreparsedData(handle, out var preparsed)) return null;

        try
        {
            if (Native.HidP_GetCaps(preparsed, out var caps) != HidpStatusSuccess) return null;

            return new HidCollection(
                path,
                attributes.VendorID,
                attributes.ProductID,
                caps.UsagePage,
                caps.Usage,
                caps.OutputReportByteLength);
        }
        finally
        {
            Native.HidD_FreePreparsedData(preparsed);
        }
    }

    /// <summary>Opens a collection for writing. Shared, because the lighting is not ours
    /// exclusively -- Windows and the vendor's own software both hold it too, and asking for
    /// exclusive access would fail rather than win the argument.</summary>
    public static SafeFileHandle Open(string path) => Native.CreateFile(
        path, GenericRead | GenericWrite, ShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

    /// <summary>Writes one report.</summary>
    public static bool Write(SafeFileHandle handle, byte[] report) =>
        Native.WriteFile(handle, report, report.Length, out var written, IntPtr.Zero)
        && written == report.Length;

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")]
        public static extern void HidD_GetHidGuid(out Guid guid);

        [DllImport("hid.dll")]
        public static extern bool HidD_GetAttributes(SafeFileHandle handle, ref HIDD_ATTRIBUTES attributes);

        [DllImport("hid.dll")]
        public static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr preparsed);

        [DllImport("hid.dll")]
        public static extern bool HidD_FreePreparsedData(IntPtr preparsed);

        [DllImport("hid.dll")]
        public static extern int HidP_GetCaps(IntPtr preparsed, out HIDP_CAPS caps);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SetupDiGetClassDevs(
            ref Guid guid, IntPtr enumerator, IntPtr window, int flags);

        [DllImport("setupapi.dll")]
        public static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr set, IntPtr deviceInfo, ref Guid guid, int index, ref SP_DEVICE_INTERFACE_DATA data);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size,
            ref int required, IntPtr deviceInfoData);

        [DllImport("setupapi.dll")]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFile(
            string name, uint access, uint share, IntPtr security,
            uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(
            SafeFileHandle handle, byte[] buffer, int length, out int written, IntPtr overlapped);
    }
}
