using System.IO;

using Bubbles.Zone;

namespace Bubbles.Tests;

/// <summary>The executable's own icon, which is what Windows search, the Start Menu, Alt-Tab
/// and the taskbar all show.
///
/// The .ico container is hand-written -- there is no managed writer for one -- so the parts
/// that would fail silently are checked here. A malformed directory does not throw: Windows
/// simply shows the generic default, which is indistinguishable from having no icon at all,
/// which is the state this came from.</summary>
public sealed class AppIconTests
{
    private static byte[] Icon()
    {
        byte[] bytes = [];
        Sta.Run(() =>
        {
            using var stream = new MemoryStream();
            BubbleArt.WriteIcon(stream);
            bytes = stream.ToArray();
        });

        return bytes;
    }

    [Fact]
    public void The_icon_carries_more_than_one_size()
    {
        var bytes = Icon();

        Assert.Equal(0, BitConverter.ToUInt16(bytes, 0));  // reserved
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 2));  // an icon, not a cursor

        var frames = BitConverter.ToUInt16(bytes, 4);

        Assert.True(frames > 1, $"only {frames} size(s); Windows picks a different one per surface");
    }

    /// <summary>Every entry has to point at real data inside the file. An offset past the end,
    /// or a length running off it, is the shape a mistake in the directory arithmetic takes --
    /// and 256 is stored as 0, because the field is one byte wide.</summary>
    [Fact]
    public void Every_entry_points_at_a_png_inside_the_file()
    {
        var bytes = Icon();
        var frames = BitConverter.ToUInt16(bytes, 4);

        var sizes = new List<int>();

        for (var i = 0; i < frames; i++)
        {
            var entry = 6 + (i * 16);
            var width = bytes[entry] == 0 ? 256 : bytes[entry];
            var length = BitConverter.ToInt32(bytes, entry + 8);
            var offset = BitConverter.ToInt32(bytes, entry + 12);

            Assert.InRange(offset, 6 + (frames * 16), bytes.Length);
            Assert.InRange(offset + length, offset, bytes.Length);

            // PNG-compressed frames, which is what keeps a 256-pixel size affordable.
            Assert.Equal<byte[]>([0x89, 0x50, 0x4E, 0x47], bytes[offset..(offset + 4)]);

            Assert.Equal(bytes[entry], bytes[entry + 1]);   // square
            Assert.Equal(32, BitConverter.ToUInt16(bytes, entry + 6));

            sizes.Add(width);
        }

        // The sizes Windows actually asks for, and no duplicates.
        Assert.Equal(sizes.Distinct().Count(), sizes.Count);
        Assert.Contains(16, sizes);
        Assert.Contains(32, sizes);
        Assert.Contains(256, sizes);
    }
}
