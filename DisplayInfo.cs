using System.Runtime.InteropServices;

namespace Bubbles;

/// <summary>Reads what Windows knows about each connected display: its real name, how it is
/// connected, and whether HDR is on.
///
/// This is diagnostics, not behaviour. Nothing here decides what the app does -- see
/// <see cref="MonitorBacklight"/> for why panel technology is deliberately not consulted. It
/// exists because "my monitor ignores the backlight command" is nearly always explained by one
/// of these facts, and guessing is worse than looking.</summary>
internal static class DisplayInfo
{
    private const uint QueryOnlyActivePaths = 2;
    private const uint GetTargetName = 2;
    private const uint GetAdvancedColorInfo = 9;
    private const uint SetAdvancedColorState = 10;
    private const uint InternalDisplay = 0x80000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint Low;
        public int High;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoHeader
    {
        public uint Type;
        public uint Size;
        public Luid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdvancedColorInfo
    {
        public DeviceInfoHeader Header;

        /// <summary>Bit 0 supported, bit 1 enabled, bit 2 wide colour enforced,
        /// bit 3 forced off.</summary>
        public uint Value;

        public uint ColorEncoding;
        public uint BitsPerColorChannel;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TargetDeviceName
    {
        public DeviceInfoHeader Header;
        public uint Flags;
        public uint OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string FriendlyName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DevicePath;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathSourceInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathTargetInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public Rational RefreshRate;
        public uint ScanLineOrdering;
        public int TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathInfo
    {
        public PathSourceInfo Source;
        public PathTargetInfo Target;
        public uint Flags;
    }

    // The mode union is only ever passed through, so it just has to be the right size.
    [StructLayout(LayoutKind.Explicit, Size = 48)]
    private struct ModeUnion
    {
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ModeInfo
    {
        public uint InfoType;
        public uint Id;
        public Luid AdapterId;
        public ModeUnion Mode;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] PathInfo[] paths,
        ref uint modeCount, [Out] ModeInfo[] modes, IntPtr topology);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref TargetDeviceName request);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref AdvancedColorInfo request);

    [StructLayout(LayoutKind.Sequential)]
    private struct SetAdvancedColor
    {
        public DeviceInfoHeader Header;

        /// <summary>Bit 0 enables advanced colour; the rest is reserved.</summary>
        public uint Value;
    }

    [DllImport("user32.dll")]
    private static extern int DisplayConfigSetDeviceInfo(ref SetAdvancedColor request);

    /// <summary>One display, identified well enough to come back to later.
    /// <paramref name="Internal"/> marks the built-in panel, which has no DDC/CI backlight to
    /// reach and therefore nothing to gain from having its HDR switched off.</summary>
    public sealed record Target(uint AdapterLow, int AdapterHigh, uint Id, string Name, bool Internal = false);

    /// <summary>Every active display that currently has HDR switched on.</summary>
    public static List<Target> WithHdrEnabled()
    {
        var found = new List<Target>();

        ForEachTarget((adapter, id, name, technology) =>
        {
            // Named flags, because `info.Value` on a nullable struct means something else
            // entirely and reads as if it were this field.
            if (ColourFlags(adapter, id) is not { } flags) return;

            var supported = (flags & 1) != 0;
            var enabled = (flags & 2) != 0;

            if (supported && enabled)
                found.Add(new Target(adapter.Low, adapter.High, id, name, technology == InternalDisplay));
        });

        return found;
    }

    /// <summary>Whether HDR is on for this display right now, or null if it cannot be asked --
    /// which is what a disconnected display looks like.</summary>
    public static bool? HdrEnabled(Target target)
    {
        var adapter = new Luid { Low = target.AdapterLow, High = target.AdapterHigh };
        return ColourFlags(adapter, target.Id) is { } flags ? (flags & 2) != 0 : null;
    }

    /// <summary>Every active display that can do HDR, whatever state it is in.</summary>
    public static List<Target> AllTargets()
    {
        var found = new List<Target>();

        ForEachTarget((adapter, id, name, technology) =>
        {
            if (ColourFlags(adapter, id) is { } flags && (flags & 1) != 0)
                found.Add(new Target(adapter.Low, adapter.High, id, name, technology == InternalDisplay));
        });

        return found;
    }

    /// <summary>Switches HDR on or off for one display. Returns whether the display agreed --
    /// this is a mode change, and it does not always take.</summary>
    public static bool SetHdr(Target target, bool enabled)
    {
        var request = new SetAdvancedColor
        {
            Header = new DeviceInfoHeader
            {
                Type = SetAdvancedColorState,
                Size = (uint)Marshal.SizeOf<SetAdvancedColor>(),
                AdapterId = new Luid { Low = target.AdapterLow, High = target.AdapterHigh },
                Id = target.Id,
            },
            Value = enabled ? 1u : 0u,
        };

        var result = DisplayConfigSetDeviceInfo(ref request);
        if (result != 0) Diagnostics.Log($"HDR {(enabled ? "on" : "off")} for {target.Name} failed: {result}");
        return result == 0;
    }

    private static uint? ColourFlags(Luid adapter, uint id)
    {
        var request = new AdvancedColorInfo
        {
            Header = new DeviceInfoHeader
            {
                Type = GetAdvancedColorInfo,
                Size = (uint)Marshal.SizeOf<AdvancedColorInfo>(),
                AdapterId = adapter,
                Id = id,
            },
        };

        return DisplayConfigGetDeviceInfo(ref request) == 0 ? request.Value : null;
    }

    /// <summary>Walks the active display paths, handing each one its adapter, id and name.</summary>
    private static void ForEachTarget(Action<Luid, uint, string, uint> body)
    {
        try
        {
            if (GetDisplayConfigBufferSizes(QueryOnlyActivePaths, out var pathCount, out var modeCount) != 0)
                return;

            var paths = new PathInfo[pathCount];
            var modes = new ModeInfo[modeCount];

            if (QueryDisplayConfig(QueryOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
                return;

            for (var i = 0; i < pathCount; i++)
            {
                var target = paths[i].Target;

                var name = new TargetDeviceName
                {
                    Header = new DeviceInfoHeader
                    {
                        Type = GetTargetName,
                        Size = (uint)Marshal.SizeOf<TargetDeviceName>(),
                        AdapterId = target.AdapterId,
                        Id = target.Id,
                    },
                };

                var friendly = DisplayConfigGetDeviceInfo(ref name) == 0 && !string.IsNullOrWhiteSpace(name.FriendlyName)
                    ? name.FriendlyName.Trim()
                    : "display";

                body(target.AdapterId, target.Id, friendly, target.OutputTechnology);
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"display walk failed: {ex.Message}");
        }
    }

    private static string Technology(uint value) => value switch
    {
        0 => "other",
        1 => "VGA",
        4 => "component",
        5 => "DVI",
        6 => "HDMI",
        7 => "LVDS",
        10 => "DisplayPort",
        11 => "DisplayPort (embedded)",
        12 => "UDI",
        13 => "UDI (embedded)",
        15 => "Miracast",
        0x80000000 => "internal",
        _ => $"technology {value}",
    };

    /// <summary>One line per active display.</summary>
    public static List<string> Describe()
    {
        var lines = new List<string>();

        try
        {
            if (GetDisplayConfigBufferSizes(QueryOnlyActivePaths, out var pathCount, out var modeCount) != 0)
                return new List<string> { "  (could not read the display configuration)" };

            var paths = new PathInfo[pathCount];
            var modes = new ModeInfo[modeCount];

            if (QueryDisplayConfig(QueryOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
                return new List<string> { "  (could not query the display configuration)" };

            for (var i = 0; i < pathCount; i++)
            {
                var target = paths[i].Target;

                var name = new TargetDeviceName
                {
                    Header = new DeviceInfoHeader
                    {
                        Type = GetTargetName,
                        Size = (uint)Marshal.SizeOf<TargetDeviceName>(),
                        AdapterId = target.AdapterId,
                        Id = target.Id,
                    },
                };

                var friendly = DisplayConfigGetDeviceInfo(ref name) == 0 && !string.IsNullOrWhiteSpace(name.FriendlyName)
                    ? name.FriendlyName.Trim()
                    : "unnamed display";

                var colour = new AdvancedColorInfo
                {
                    Header = new DeviceInfoHeader
                    {
                        Type = GetAdvancedColorInfo,
                        Size = (uint)Marshal.SizeOf<AdvancedColorInfo>(),
                        AdapterId = target.AdapterId,
                        Id = target.Id,
                    },
                };

                var hdr = "unknown";
                if (DisplayConfigGetDeviceInfo(ref colour) == 0)
                {
                    var supported = (colour.Value & 1) != 0;
                    var enabled = (colour.Value & 2) != 0;
                    hdr = supported ? (enabled ? "HDR ON" : "HDR available, off") : "no HDR";
                }

                lines.Add($"  {friendly}  [{Technology(target.OutputTechnology)}]  {hdr}");
            }
        }
        catch (Exception ex)
        {
            lines.Add($"  (display configuration unavailable: {ex.Message})");
        }

        return lines;
    }
}
