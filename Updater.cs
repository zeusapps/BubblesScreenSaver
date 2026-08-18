using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Threading;

namespace Bubbles;

/// <summary>Keeps the app current from its GitHub releases.
///
/// Deliberately unhurried: it checks in the background, verifies what it downloaded against
/// the checksum published with the release, and then *stages* the new binary. Nothing is
/// swapped while you are using the machine -- the exchange happens at the next launch, or when
/// you ask for it from the tray. An app that restarts itself out from under someone is exactly
/// the sort of surprise this project has already learned not to inflict.</summary>
public sealed class Updater : IDisposable
{
    private const string Repo = "zeusapps/BubblesScreenSaver";
    private const string AssetName = "Bubbles.exe";
    private const string ChecksumAsset = "SHA256SUMS.txt";

    // Declared before the client, because building the User-Agent needs it: static field
    // initialisers run in declaration order, and an empty version makes the header invalid.
    public static Version Current { get; } =
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    private static readonly HttpClient Http = CreateClient();

    private readonly DispatcherTimer _timer;
    private Settings _settings;
    private bool _busy;

    /// <summary>Set when a swap has been made and the caller should relaunch after shutdown.</summary>
    public static bool RestartWanted { get; private set; }

    /// <summary>Version sitting in the staging directory, if any is newer than what is running.</summary>
    public Version? Staged { get; private set; }

    /// <summary>Raised when a staged update appears, so the tray can offer it.</summary>
    public event Action? StateChanged;

    private static string StagingDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bubbles", "update");

    private static string StagedBinary => Path.Combine(StagingDirectory, AssetName);
    private static string StagedVersionFile => Path.Combine(StagingDirectory, "version.txt");

    public Updater(Settings settings)
    {
        _settings = settings;

        _timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            // First look shortly after launch, then on the configured cadence.
            Interval = TimeSpan.FromMinutes(3),
        };
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = TimeSpan.FromHours(Math.Clamp(_settings.UpdateCheckHours, 1, 720));
            await CheckAsync(manual: false);
        };
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        // GitHub rejects requests without one.
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Bubbles/{Current} (+https://github.com/{Repo})");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public void Apply(Settings settings) => _settings = settings;

    /// <summary>Whether this deployment can safely replace itself.
    ///
    /// Releases are published as a single self-contained file. A build from source is
    /// framework-dependent and keeps its assembly, deps.json and runtimeconfig.json beside the
    /// launcher -- dropping a self-contained binary into that folder leaves those sidecars
    /// behind, and the new binary dies on startup with a fatal runtime error. So a source build
    /// reports that an update exists and otherwise leaves well alone.</summary>
    public static bool CanSelfUpdate
    {
        get
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return false;

            var directory = Path.GetDirectoryName(exe);
            if (string.IsNullOrEmpty(directory)) return false;

            var sidecar = Path.Combine(directory, Path.GetFileNameWithoutExtension(exe) + ".dll");
            return !File.Exists(sidecar);
        }
    }

    public void Start()
    {
        Staged = ReadStagedVersion();
        if (Staged is not null) StateChanged?.Invoke();

        if (_settings.AutoUpdate) _timer.Start();
    }

    /// <summary>Looks for a newer release and stages it. Failures are silent by design --
    /// a screensaver has no business interrupting anyone because GitHub was unreachable.</summary>
    public async Task<string?> CheckAsync(bool manual)
    {
        if (_busy) return null;
        _busy = true;

        try
        {
            using var response = await Http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest");
            if (!response.IsSuccessStatusCode)
                return manual ? $"GitHub returned {(int)response.StatusCode}" : null;

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;

            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest))
                return manual ? $"could not read version from tag '{tag}'" : null;

            Diagnostics.Log($"update check: running {Current}, latest {latest}");

            if (latest <= Current)
                return manual ? $"up to date (v{Current})" : null;

            if (!CanSelfUpdate)
            {
                Diagnostics.Log("update available but this is a source build; not staging");
                return $"v{latest} is available -- this build runs from source, so update with git";
            }

            if (Staged is not null && Staged >= latest)
                return manual ? $"v{Staged} already staged -- restart to apply" : null;

            var binaryUrl = AssetUrl(root, AssetName);
            var checksumUrl = AssetUrl(root, ChecksumAsset);
            if (binaryUrl is null || checksumUrl is null)
                return manual ? "release is missing its assets" : null;

            var expected = ParseChecksum(await Http.GetStringAsync(checksumUrl), AssetName);
            if (expected is null)
                return manual ? "release has no checksum for the binary" : null;

            var payload = await Http.GetByteArrayAsync(binaryUrl);
            var actual = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

            if (actual != expected)
            {
                Diagnostics.Log($"update rejected: checksum {actual} != {expected}");
                return manual ? "download failed verification and was discarded" : null;
            }

            Directory.CreateDirectory(StagingDirectory);

            // Write beside the target and move into place, so a staged binary is never partial.
            var temp = StagedBinary + ".partial";
            await File.WriteAllBytesAsync(temp, payload);
            File.Move(temp, StagedBinary, overwrite: true);
            await File.WriteAllTextAsync(StagedVersionFile, latest.ToString());

            Staged = latest;
            Diagnostics.Log($"update staged: v{latest} ({payload.Length} bytes, verified)");
            StateChanged?.Invoke();

            return $"v{latest} downloaded -- restart to apply";
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"update check failed: {ex.Message}");
            return manual ? $"update check failed: {ex.Message}" : null;
        }
        finally
        {
            _busy = false;
        }
    }

    private static string? AssetUrl(JsonElement release, string name)
    {
        if (!release.TryGetProperty("assets", out var assets)) return null;

        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.TryGetProperty("name", out var assetName) &&
                string.Equals(assetName.GetString(), name, StringComparison.OrdinalIgnoreCase))
            {
                return asset.TryGetProperty("browser_download_url", out var url) ? url.GetString() : null;
            }
        }

        return null;
    }

    /// <summary>Pulls one hash out of a `sha256  filename` listing.</summary>
    private static string? ParseChecksum(string listing, string name)
    {
        foreach (var line in listing.Split('\n'))
        {
            var parts = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 &&
                string.Equals(Path.GetFileName(parts[^1]), name, StringComparison.OrdinalIgnoreCase))
            {
                return parts[0].ToLowerInvariant();
            }
        }

        return null;
    }

    private static Version? ReadStagedVersion()
    {
        try
        {
            if (!File.Exists(StagedBinary) || !File.Exists(StagedVersionFile)) return null;

            var text = File.ReadAllText(StagedVersionFile).Trim();
            return Version.TryParse(text, out var version) && version > Current ? version : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Swaps the staged binary in. Does *not* start anything -- the caller relaunches
    /// once the single-instance mutex has been released, otherwise the new process would find
    /// the old one still holding it and quietly exit.</summary>
    public bool SwapIn()
    {
        if (Staged is null || !CanSelfUpdate) return false;

        var current = Environment.ProcessPath;
        if (string.IsNullOrEmpty(current)) return false;

        var backup = current + ".old";

        try
        {
            if (File.Exists(backup)) File.Delete(backup);

            // Windows allows a running executable to be renamed, just not overwritten.
            File.Move(current, backup);

            try
            {
                File.Copy(StagedBinary, current);
            }
            catch
            {
                File.Move(backup, current);   // put it back rather than leave nothing behind
                throw;
            }

            File.Delete(StagedBinary);
            File.Delete(StagedVersionFile);

            Diagnostics.Log($"update applied: now v{Staged}");
            Staged = null;
            RestartWanted = true;
            return true;
        }
        catch (Exception ex)
        {
            // Most likely the app lives somewhere it cannot write to, such as Program Files.
            Diagnostics.Log($"update could not be applied: {ex.Message}");
            return false;
        }
    }

    /// <summary>Removes the previous binary left behind by an earlier swap.</summary>
    public static void SweepOldBinaries()
    {
        try
        {
            var current = Environment.ProcessPath;
            if (string.IsNullOrEmpty(current)) return;

            var backup = current + ".old";
            if (File.Exists(backup)) File.Delete(backup);
        }
        catch
        {
            // It is only litter; it will go on the next run.
        }
    }

    /// <summary>Relaunches the freshly swapped binary. Call once the mutex is gone.</summary>
    public static void RelaunchIfSwapped()
    {
        if (!RestartWanted) return;

        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"relaunch failed: {ex.Message}");
        }
    }

    public void Dispose() => _timer.Stop();
}
