using Microsoft.Win32.SafeHandles;

namespace Bubbles.Keyboard;

/// <summary>An ASUS Aura keyboard, written to directly over HID.
///
/// Nothing else has to be installed or running for this to work, which is the whole reason it
/// is this and not a client for somebody's lighting server. It talks to the vendor collection
/// the firmware exposes for exactly this purpose.
///
/// **It only works while Windows owns the lighting.** With Dynamic Lighting switched off, the
/// vendor's own software holds the keyboard and these writes are accepted and thrown away --
/// no error, no clue, just a keyboard that does not change. That was the single most
/// expensive thing to discover about this feature, so the setting says it and the log says it.
///
/// Giving the keyboard back is releasing it. There is no way to ask this protocol what colour
/// the keyboard was before -- it only listens -- so nothing is recorded to restore *to*.
/// Closing the handle is enough: whatever owns the lighting reasserts itself within a moment,
/// which was confirmed by watching it happen. Process death closes handles too, so even a
/// crash mid-Emission hands the keyboard back.</summary>
internal sealed class AuraKeyboard : IKeyboardDevice
{
    private readonly Func<List<HidCollection>> _collections;

    private SafeFileHandle? _handle;
    private HidCollection? _device;

    public AuraKeyboard() : this(Hid.Collections)
    {
    }

    /// <param name="collections">How to find HID collections. Replaced in tests, so the choice
    /// of collection -- the part that has been wrong before -- can be exercised without a
    /// keyboard.</param>
    internal AuraKeyboard(Func<List<HidCollection>> collections) => _collections = collections;

    /// <summary>Picks the lighting collection out of everything attached.
    ///
    /// Matched on vendor, usage page and usage, never on order. One keyboard presents ten
    /// collections behind a single product id and several of them accept writes; only this one
    /// acts on them.</summary>
    internal static HidCollection? Lighting(IEnumerable<HidCollection> collections) =>
        collections.FirstOrDefault(c =>
            c.VendorId == AuraProtocol.VendorId &&
            c.UsagePage == AuraProtocol.UsagePage &&
            c.Usage == AuraProtocol.Usage &&
            c.OutputReportLength >= AuraProtocol.MessageLength);

    public KeyboardRecord? Open()
    {
        try
        {
            _device = Lighting(_collections());

            if (_device is null)
            {
                Diagnostics.Log("keyboard lighting: no ASUS Aura keyboard attached");
                return null;
            }

            _handle = Hid.Open(_device.Path);

            if (_handle.IsInvalid)
            {
                Diagnostics.Log($"keyboard lighting: {_device.ProductId:X4} refused to open");
                _handle.Dispose();
                _handle = null;
                _device = null;
                return null;
            }

            Diagnostics.Log($"keyboard lighting: using {_device.VendorId:X4}:{_device.ProductId:X4}, " +
                            $"{_device.OutputReportLength}-byte reports. If nothing lights up, " +
                            "Dynamic Lighting is off and the vendor's software still owns the keys.");

            return new KeyboardRecord
            {
                Key = $"{_device.VendorId:X4}:{_device.ProductId:X4}",
                Name = "ASUS Aura keyboard",
            };
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"keyboard lighting: no keyboard reached ({ex.GetType().Name}: {ex.Message})");
            Release();
            return null;
        }
    }

    public bool Show(KeyColor colour)
    {
        if (_handle is null || _device is null) return false;

        try
        {
            foreach (var packet in AuraProtocol.Show(colour))
            {
                if (Hid.Write(_handle, AuraProtocol.Pad(packet, _device.OutputReportLength))) continue;

                // A refused write means the device has gone, not that the colour was wrong.
                Diagnostics.Log("keyboard lighting: the keyboard stopped accepting writes");
                Release();
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"keyboard lighting: lost the keyboard ({ex.GetType().Name}: {ex.Message})");
            Release();
            return false;
        }
    }

    /// <summary>Black, which on this keyboard is the backlight off. There is no separate
    /// power-down command in this protocol -- static black is the whole of it.</summary>
    public bool GoDark() => Show(KeyColor.Black);

    /// <summary>Hands the keyboard back by letting go of it.
    ///
    /// Always succeeds, because there is nothing that can fail: no packet is sent and no state
    /// is asserted. What follows is whatever owns the lighting noticing it is free again.</summary>
    public bool Restore(KeyboardRecord record)
    {
        Release();
        return true;
    }

    public void Dispose() => Release();

    private void Release()
    {
        _handle?.Dispose();
        _handle = null;
        _device = null;
    }
}
