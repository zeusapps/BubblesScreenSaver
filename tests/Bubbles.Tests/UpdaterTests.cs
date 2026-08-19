using Bubbles.Session;

namespace Bubbles.Tests;

/// <summary>The updater downloads an executable and runs it, so the checksum lookup is the
/// one piece of it that must not be approximately right. A miss has to read as "no hash",
/// never as "some other file's hash".</summary>
public sealed class UpdaterTests
{
    private const string Listing = """
        3b1f8e0d5a6c4b2e9f7a1c3d5e7b9f1a3c5d7e9b1f3a5c7d9e1b3f5a7c9d1e3b  Bubbles.exe
        9e1b3f5a7c9d1e3b5f7a9c1d3e5b7f9a1c3d5e7b9f1a3c5d7e9b1f3a5c7d9e1b  SHA256SUMS.txt
        """;

    [Fact]
    public void A_named_file_gets_its_own_hash()
    {
        Assert.StartsWith("3b1f8e0d", Updater.ParseChecksum(Listing, "Bubbles.exe"));
        Assert.StartsWith("9e1b3f5a", Updater.ParseChecksum(Listing, "SHA256SUMS.txt"));
    }

    [Fact]
    public void A_file_that_is_not_listed_returns_nothing()
    {
        // Must be null rather than the first line, or the wrong binary verifies as correct.
        Assert.Null(Updater.ParseChecksum(Listing, "Bubbles.zip"));
    }

    [Fact]
    public void An_empty_listing_returns_nothing()
    {
        Assert.Null(Updater.ParseChecksum("", "Bubbles.exe"));
        Assert.Null(Updater.ParseChecksum("\n\n   \n", "Bubbles.exe"));
    }

    [Fact]
    public void The_binary_marker_that_sha256sum_writes_is_tolerated()
    {
        // GNU sha256sum marks binary mode with a leading asterisk on the filename.
        Assert.Equal("abc123", Updater.ParseChecksum("abc123 *Bubbles.exe", "Bubbles.exe"));
    }

    [Fact]
    public void A_path_in_the_listing_still_matches_the_bare_asset_name()
    {
        Assert.Equal("abc123", Updater.ParseChecksum("abc123  artifacts/Bubbles.exe", "Bubbles.exe"));
    }

    [Fact]
    public void Windows_line_endings_do_not_glue_the_hash_to_the_next_line()
    {
        var listing = "3b1f8e0d  Bubbles.exe\r\n9e1b3f5a  SHA256SUMS.txt\r\n";

        Assert.Equal("3b1f8e0d", Updater.ParseChecksum(listing, "Bubbles.exe"));
    }

    [Fact]
    public void Hashes_are_compared_in_one_case()
    {
        // GitHub's own tooling emits uppercase; SHA256.HashData is compared lowercased.
        Assert.Equal("3b1f8e0d", Updater.ParseChecksum("3B1F8E0D  Bubbles.exe", "Bubbles.exe"));
    }

    [Fact]
    public void A_malformed_line_is_skipped_rather_than_taken_as_a_hash()
    {
        var listing = "this-line-has-no-filename\n3b1f8e0d  Bubbles.exe";

        Assert.Equal("3b1f8e0d", Updater.ParseChecksum(listing, "Bubbles.exe"));
    }

    [Fact]
    public void The_running_version_is_always_known()
    {
        // Everything the updater decides is a comparison against this.
        Assert.NotNull(Updater.Current);
        Assert.True(Updater.Current >= new Version(1, 0, 0));
    }
}
