namespace Bubbles.Keyboard;

/// <summary>The ASUS Aura laptop keyboard protocol, as bytes.
///
/// Three packets, in a fixed order: the effect, then SET, then APPLY. Nothing happens without
/// all three -- the effect alone is accepted and discarded, which is a discouraging way to
/// spend an afternoon.
///
/// The layout is the one asusctl's `rog-aura` crate uses, and it was confirmed against a
/// G614/G615 board (`0B05:19B6`) by writing these bytes and watching the keys: the blink
/// pattern, the colours, and the moment the keyboard was handed back all matched. That makes
/// this the one part of the feature that is verified on hardware rather than argued from a
/// document.
///
/// Packets are written as HID output reports to the vendor collection whose usage page is
/// 0xFF31 and usage 0x0079, padded to that collection's declared output report length. The
/// padding matters: firmware silently drops a short write, which is the same failure as
/// sending nothing at all and looks identical from here.</summary>
internal static class AuraProtocol
{
    /// <summary>Report id for the lighting commands. The keyboard exposes several vendor
    /// collections and each takes its own report id -- 0x5A commands belong to a different
    /// collection and are rejected outright by this one, which is at least an honest error.</summary>
    private const byte Report = 0x5D;

    private const byte CommandEffect = 0xB3;
    private const byte CommandApply = 0xB4;
    private const byte CommandSet = 0xB5;

    /// <summary>Built-in mode 0: one static colour across the board. The only mode this
    /// application wants -- the animation is ours, done by sending a series of static
    /// colours, rather than one of the keyboard's own effects running on its own clock.</summary>
    private const byte ModeStatic = 0x00;

    /// <summary>Every packet is this long before padding.</summary>
    public const int MessageLength = 17;

    /// <summary>The vendor collection that takes these commands.</summary>
    public const ushort UsagePage = 0xFF31;
    public const ushort Usage = 0x0079;

    public const ushort VendorId = 0x0B05;

    /// <summary>One colour, everywhere.</summary>
    public static byte[] Effect(KeyColor colour) =>
    [
        Report,
        CommandEffect,
        0x00,             // zone: whole keyboard
        ModeStatic,
        colour.R,
        colour.G,
        colour.B,
        0x00,             // speed: meaningless for a static colour
        0x00,             // direction: likewise
        0x00,
        0x00, 0x00, 0x00, // second colour, for the modes that interpolate
        0x00, 0x00, 0x00, 0x00,
    ];

    /// <summary>Commits the effect. Without this the keyboard keeps whatever it had.</summary>
    public static byte[] Set() => Command(CommandSet);

    /// <summary>Makes the committed effect take. Without this the change does not persist.</summary>
    public static byte[] Apply() => Command(CommandApply);

    /// <summary>The three packets for one colour, in the order they must be written.</summary>
    public static byte[][] Show(KeyColor colour) => [Effect(colour), Set(), Apply()];

    private static byte[] Command(byte command)
    {
        var packet = new byte[MessageLength];
        packet[0] = Report;
        packet[1] = command;
        return packet;
    }

    /// <summary>Pads a packet out to the report length the device declares.</summary>
    public static byte[] Pad(byte[] packet, int reportLength)
    {
        if (reportLength < packet.Length)
            throw new ArgumentOutOfRangeException(
                nameof(reportLength), reportLength, "shorter than the packet it must carry");

        if (reportLength == packet.Length) return packet;

        var padded = new byte[reportLength];
        packet.CopyTo(padded, 0);
        return padded;
    }
}
