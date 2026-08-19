using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bubbles;

/// <summary>User-tweakable knobs, persisted to %APPDATA%\Bubbles\settings.json.</summary>
public sealed class Settings
{
    /// <summary>How many bubbles are alive at once.</summary>
    public int BubbleCount { get; set; } = 22;

    /// <summary>Bubble radius range, in device-independent pixels.</summary>
    public double MinRadius { get; set; } = 40;
    public double MaxRadius { get; set; } = 150;

    /// <summary>Average drift speed, in DIP per second.</summary>
    public double Speed { get; set; } = 42;

    /// <summary>0 = every bubble drifts at exactly Speed, 1 = wildly varied.</summary>
    public double SpeedVariance { get; set; } = 0.65;

    /// <summary>Master opacity of the whole overlay.</summary>
    public double Opacity { get; set; } = 0.85;

    /// <summary>Upward acceleration in DIP/s^2. 0 = pure bouncing, ~25 = they float up like real bubbles.</summary>
    public double Buoyancy { get; set; } = 0;

    /// <summary>How much the soap film jiggles, 0..1.</summary>
    public double Wobble { get; set; } = 0.045;

    /// <summary>Hide the mouse pointer while the overlay is up. A parked white arrow is
    /// burn-in too. Ignored in AlwaysOn, where you are actually using the machine.</summary>
    public bool HideCursor { get; set; } = true;

    /// <summary>False lets you pop bubbles with the mouse, but the overlay then eats every click. Keep true.</summary>
    public bool ClickThrough { get; set; } = true;

    /// <summary>Frames per second cap. 0 follows the compositor (usually your refresh rate).
    /// The Zone artifacts are drawn live rather than blitted, so 30 is the sensible default:
    /// the motion is slow enough that it reads identically and it halves the work.</summary>
    public int MaxFps { get; set; } = 30;

    // ---- idle behaviour -------------------------------------------------------------

    /// <summary>Seconds of no keyboard/mouse input before the bubbles fade in.
    /// This is measured by the app itself, so it still works when something
    /// (PowerToys Awake, a media player) has suppressed the Windows screensaver.</summary>
    public double IdleSeconds { get; set; } = 60;

    /// <summary>Seconds of no input before the screen goes fully black. 0 disables it.
    /// This is drawn, not a power state: no monitor or system power API is involved,
    /// and any keypress brings the desktop straight back. On OLED, black is unlit.</summary>
    public double BlackoutSeconds { get; set; } = 600;

    /// <summary>How dark the sheet behind the bubbles gets, 0..1.
    /// 0 shows your desktop untouched; higher is easier on an OLED panel.</summary>
    public double Dim { get; set; } = 0.55;

    /// <summary>Seconds the bubbles take to fade in once you go idle.</summary>
    public double FadeInSeconds { get; set; } = 2.0;

    /// <summary>Run the bubbles all the time instead of waiting for you to go idle.</summary>
    public bool AlwaysOn { get; set; } = false;

    /// <summary>Turn monitor backlights down during blackout. Drawing black is enough for
    /// OLED, where black is unlit; an LCD keeps glowing behind it. Offered to every monitor --
    /// whichever accept it get it, whatever panel they use. Restored exactly as found,
    /// including after a crash.</summary>
    public bool DimMonitorBacklight { get; set; } = true;

    /// <summary>Also ask monitors to enter standby during blackout. Off by default: minimum
    /// backlight is nearly as dark and cannot leave a monitor asleep if something goes wrong.
    /// Some monitors take a second to come back, and a few want their power button.</summary>
    public bool MonitorStandby { get; set; } = false;

    // ---- updates ----------------------------------------------------------------------

    /// <summary>Check GitHub for newer releases and download them in the background. The swap
    /// itself happens at the next launch, never while you are using the machine.</summary>
    public bool AutoUpdate { get; set; } = true;

    /// <summary>Hours between checks.</summary>
    public double UpdateCheckHours { get; set; } = 24;

    // ---- theme ----------------------------------------------------------------------

    /// <summary>Zone = S.T.A.L.K.E.R. artifacts. Soap = the original soap bubbles.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<OverlayTheme>))]
    public OverlayTheme Theme { get; set; } = OverlayTheme.Zone;

    /// <summary>Animate the artifacts -- morphing outlines, drifting particles, discharge.
    /// This is drawn live every few frames rather than blitted, and on a 2560x1600 desktop it
    /// measured around 60 points of one CPU core more than the frozen version. Set false to
    /// keep the same shapes as still sprites at roughly a tenth of the cost.</summary>
    public bool Animated { get; set; } = true;

    /// <summary>How close an artifact has to drift to the detector to be picked up, in DIP.
    /// Larger collects more often; 0 turns collection off and the field just drifts.</summary>
    public double CollectRadius { get; set; } = 60;

    /// <summary>Show the drifting Veres artifact detector. Zone theme only.</summary>
    public bool ShowDetector { get; set; } = true;

    /// <summary>Lightning across the sky during an Emission. Zone theme only, and only while
    /// the Emission itself is running -- nothing is drawn once the screen is black.</summary>
    public bool Lightning { get; set; } = true;

    /// <summary>Make the blackout an Emission -- red sky, agitated artifacts, shockwave --
    /// instead of a plain fade. Zone theme only; either way it ends at pure black.</summary>
    public bool Emission { get; set; } = true;

    [JsonIgnore]
    public static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Bubbles");

    [JsonIgnore]
    public static string FilePath => Path.Combine(Directory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), JsonOptions)?.Clamped()
                       ?? new Settings();
        }
        catch
        {
            // A hand-edited file with a typo shouldn't stop the bubbles.
        }

        var fresh = new Settings();
        fresh.Save();
        return fresh;
    }

    public void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
        }
    }

    public Settings Clamped()
    {
        BubbleCount = Math.Clamp(BubbleCount, 1, 400);
        MinRadius = Math.Clamp(MinRadius, 4, 1200);
        MaxRadius = Math.Clamp(MaxRadius, MinRadius, 1600);
        Speed = Math.Clamp(Speed, 0, 2000);
        SpeedVariance = Math.Clamp(SpeedVariance, 0, 1);
        Opacity = Math.Clamp(Opacity, 0.02, 1);
        Buoyancy = Math.Clamp(Buoyancy, -400, 400);
        Wobble = Math.Clamp(Wobble, 0, 0.5);
        MaxFps = Math.Clamp(MaxFps, 0, 480);
        IdleSeconds = Math.Clamp(IdleSeconds, 1, 86400);
        BlackoutSeconds = BlackoutSeconds <= 0 ? 0 : Math.Clamp(BlackoutSeconds, IdleSeconds, 86400);
        Dim = Math.Clamp(Dim, 0, 1);
        CollectRadius = Math.Clamp(CollectRadius, 0, 600);
        UpdateCheckHours = Math.Clamp(UpdateCheckHours, 1, 720);
        FadeInSeconds = Math.Clamp(FadeInSeconds, 0, 30);
        return this;
    }
}
