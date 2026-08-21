using System.Runtime.InteropServices;

namespace Bubbles.Interop;

/// <summary>Whether sound is actually coming out of the machine.
///
/// Watching a video produces no keyboard or mouse input, so the idle timer concludes you have
/// left -- the same failure as sitting on a call, and the reason the screensaver used to arrive
/// partway through a film.
///
/// The obvious signal is the one Windows itself uses: a video player asks for
/// ES_DISPLAY_REQUIRED and the display stays awake. That is useless here, because this machine
/// runs PowerToys Awake, which holds exactly that request permanently -- which is why this app
/// measures idleness independently in the first place. Fullscreen detection does not cover it
/// either: a video in a window is not fullscreen at all, and even fullscreen is reported only
/// intermittently for a browser.
///
/// So it asks the audio endpoint what its peak output level is. Sound playing means somebody
/// is listening.</summary>
internal static class AudioActivity
{
    private static readonly Guid DeviceEnumeratorClass = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid MeterInformationId = new("C02216F6-8C67-4B5B-9D00-D008E73E0064");

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        // Declared purely to hold the vtable slot before GetDefaultAudioEndpoint.
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);

        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid id, int contextFlags, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object? instance);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
    }

    private const int Render = 0;      // eRender: output rather than input
    private const int Multimedia = 1;  // eMultimedia: the endpoint films and music go to
    private const int AllContexts = 0x17;

    // Reacquired on failure, which is what a device change looks like from here.
    private static IAudioMeterInformation? _meter;

    /// <summary>Peak output level, 0..1, or null if it cannot be read. Null is not silence --
    /// there may be no sound device at all -- so the caller decides what that means.</summary>
    public static float? Peak()
    {
        try
        {
            _meter ??= Acquire();
            if (_meter is null) return null;

            if (_meter.GetPeakValue(out var peak) == 0) return peak;

            // Stale handle, most likely the default endpoint changed underneath us.
            _meter = null;
            return null;
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"could not read the audio level: {ex.Message}");
            _meter = null;
            return null;
        }
    }

    private static IAudioMeterInformation? Acquire()
    {
        var type = Type.GetTypeFromCLSID(DeviceEnumeratorClass);
        if (type is null) return null;

        if (Activator.CreateInstance(type) is not IMMDeviceEnumerator enumerator) return null;

        if (enumerator.GetDefaultAudioEndpoint(Render, Multimedia, out var device) != 0 ||
            device is null)
        {
            return null;
        }

        var meterId = MeterInformationId;

        return device.Activate(ref meterId, AllContexts, IntPtr.Zero, out var instance) == 0
            ? instance as IAudioMeterInformation
            : null;
    }
}
