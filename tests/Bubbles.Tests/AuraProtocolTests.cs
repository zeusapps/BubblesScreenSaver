using Bubbles.Keyboard;

namespace Bubbles.Tests;

/// <summary>The ASUS Aura packets, and the choice of which HID collection to send them to.
///
/// Unusually for this feature, the protocol itself has been confirmed on hardware: these exact
/// bytes were written to a G614/G615 keyboard and the keys did what they were told. What these
/// tests protect is that the bytes stay that way -- a padding length or a command byte quietly
/// changing is a keyboard that silently stops responding, with nothing anywhere to say why.</summary>
public class AuraProtocolTests
{
    [Fact]
    public void AColourIsThreePacketsInAFixedOrder()
    {
        var packets = AuraProtocol.Show(new KeyColor(0xC4, 0x30, 0x18));

        Assert.Equal(3, packets.Length);
        Assert.Equal(0xB3, packets[0][1]);   // the effect
        Assert.Equal(0xB5, packets[1][1]);   // SET
        Assert.Equal(0xB4, packets[2][1]);   // APPLY
    }

    [Fact]
    public void TheEffectCarriesTheColourWhereTheFirmwareLooksForIt()
    {
        var effect = AuraProtocol.Effect(new KeyColor(0xC4, 0x30, 0x18));

        Assert.Equal(AuraProtocol.MessageLength, effect.Length);
        Assert.Equal(0x5D, effect[0]);   // report id
        Assert.Equal(0xB3, effect[1]);   // set effect
        Assert.Equal(0x00, effect[2]);   // whole keyboard
        Assert.Equal(0x00, effect[3]);   // static
        Assert.Equal(0xC4, effect[4]);
        Assert.Equal(0x30, effect[5]);
        Assert.Equal(0x18, effect[6]);
    }

    [Fact]
    public void EveryPacketSharesTheOneReportId()
    {
        // A packet with the wrong report id is refused outright by this collection, which is at
        // least loud. Getting it right on two of three would not be.
        Assert.All(AuraProtocol.Show(KeyColor.Black), packet => Assert.Equal(0x5D, packet[0]));
    }

    [Fact]
    public void BlackIsAColourLikeAnyOther()
    {
        var effect = AuraProtocol.Effect(KeyColor.Black);

        Assert.Equal(0x00, effect[4]);
        Assert.Equal(0x00, effect[5]);
        Assert.Equal(0x00, effect[6]);
    }

    // ---- padding -------------------------------------------------------------------------

    [Fact]
    public void APacketIsPaddedToTheReportLengthTheDeviceDeclares()
    {
        var padded = AuraProtocol.Pad(AuraProtocol.Effect(new KeyColor(1, 2, 3)), 128);

        Assert.Equal(128, padded.Length);
        Assert.Equal(0x5D, padded[0]);
        Assert.Equal(1, padded[4]);

        // Everything past the message is zero, not left over from anything.
        Assert.All(padded[AuraProtocol.MessageLength..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void PaddingToTheExactLengthChangesNothing()
    {
        var packet = AuraProtocol.Effect(new KeyColor(1, 2, 3));

        Assert.Same(packet, AuraProtocol.Pad(packet, AuraProtocol.MessageLength));
    }

    [Fact]
    public void PaddingShorterThanThePacketIsRefused()
    {
        // Firmware drops a short write in silence, so producing one is worse than failing.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AuraProtocol.Pad(AuraProtocol.Effect(KeyColor.Black), 8));
    }

    // ---- picking the collection ----------------------------------------------------------

    private static HidCollection Collection(
        ushort usagePage, ushort usage, int outputLength = 128, ushort vendor = 0x0B05) =>
        new($"path-{usagePage:X4}-{usage:X4}", vendor, 0x19B6, usagePage, usage, outputLength);

    [Fact]
    public void TheLightingCollectionIsFoundAmongItsSiblings()
    {
        // The real device, as enumerated: ten collections behind one product id, several of
        // which accept writes and only one of which acts on them.
        var attached = new List<HidCollection>
        {
            Collection(0xFF89, 0x0010, outputLength: 0),
            Collection(0xFF89, 0xFF0F, outputLength: 0),
            Collection(0xFF82, 0x00CF, outputLength: 61),
            Collection(0xFF31, 0x0076, outputLength: 0),
            Collection(0xFF31, 0x0079, outputLength: 128),   // this one
            Collection(0x0059, 0x0001, outputLength: 0),
            Collection(0x0001, 0x0006, outputLength: 2),
        };

        var lighting = AuraKeyboard.Lighting(attached);

        Assert.NotNull(lighting);
        Assert.Equal(0xFF31, lighting!.UsagePage);
        Assert.Equal(0x0079, lighting.Usage);
        Assert.Equal(128, lighting.OutputReportLength);
    }

    [Fact]
    public void ANearMissOnUsageIsNotAccepted()
    {
        // 0x0076 is the sibling collection: same vendor, same usage page, takes a different
        // report id, and answers writes without doing anything.
        Assert.Null(AuraKeyboard.Lighting([Collection(0xFF31, 0x0076)]));
    }

    [Fact]
    public void AnotherVendorsKeyboardIsNotAssumedToSpeakAura()
    {
        Assert.Null(AuraKeyboard.Lighting([Collection(0xFF31, 0x0079, vendor: 0x1532)]));
    }

    [Fact]
    public void ACollectionTooSmallToCarryThePacketIsNotUsed()
    {
        Assert.Null(AuraKeyboard.Lighting([Collection(0xFF31, 0x0079, outputLength: 8)]));
    }

    [Fact]
    public void NoKeyboardAtAllIsNotAnError()
    {
        Assert.Null(AuraKeyboard.Lighting([]));
    }
}
