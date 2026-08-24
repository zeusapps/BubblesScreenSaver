using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bubbles;

/// <summary>User-tweakable knobs, persisted to %APPDATA%\Bubbles\settings.json.</summary>
public sealed class Settings
{
    /// <summary>The meaning of this file, so that a setting whose interpretation changes can
    /// be converted once rather than silently reinterpreted.
    ///
    /// Deliberately left at zero by the property initializer rather than at
    /// <see cref="DensityVersion"/>: a settings.json written before the field existed has no
    /// such key, deserialization leaves the initializer value in place, and zero is what tells
    /// the two apart. A fresh install is stamped in <see cref="Load"/>, which is the only place
    /// that creates settings meant to be authoritative.</summary>
    public int SettingsVersion { get; set; }

    /// <summary>The version at which <see cref="BubbleCount"/> became a density.</summary>
    public const int DensityVersion = 1;

    /// <summary>How many bubbles are alive at once on a screen of <see cref="BaselineWidth"/> by
    /// <see cref="BaselineHeight"/> DIP. A desktop larger than that carries proportionally more,
    /// and a desktop of several monitors carries its share on each -- so this is a density, not
    /// a total, and connecting a monitor adds bubbles instead of thinning out the ones already
    /// on screen.
    ///
    /// The clamp still applies to the number written here rather than to the derived total; see
    /// <c>MonitorRegions.DerivedTotal</c>, which clamps that separately.</summary>
    public int BubbleCount { get; set; } = 22;

    /// <summary>The screen <see cref="BubbleCount"/> is quoted against, in DIP.
    ///
    /// DIP rather than physical pixels, because the regions this is compared against are already
    /// in DIP and because density should follow what the user sees: turning the scaling up makes
    /// everything on that screen bigger, so the screen holds fewer bubble-sized things and the
    /// count drops to match. Two physically identical monitors at different scale factors
    /// therefore get different counts, which is the intended reading rather than an oversight.</summary>
    public const double BaselineWidth = 1920;
    public const double BaselineHeight = 1080;

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
    /// burn-in too.</summary>
    public bool HideCursor { get; set; } = true;

    /// <summary>False lets you pop bubbles with the mouse, but the overlay then eats every click. Keep true.</summary>
    public bool ClickThrough { get; set; } = true;

    /// <summary>Frames per second cap. 0 follows the compositor (usually your refresh rate).
    /// The Zone artifacts are drawn live rather than blitted, so 30 is the sensible default:
    /// the motion is slow enough that it reads identically and it halves the work.</summary>
    public int MaxFps { get; set; } = 30;

    // ---- holding off ------------------------------------------------------------------

    /// <summary>Stay out of the way while the microphone is in use. Sitting on a call produces
    /// no keyboard or mouse input, so an idle timer alone concludes you have left.</summary>
    public bool PauseWhileMicrophoneInUse { get; set; } = true;

    /// <summary>Stay out of the way while the camera is in use.</summary>
    public bool PauseWhileCameraInUse { get; set; } = true;

    /// <summary>Stay out of the way while a full-screen or presenting application is running --
    /// a game, a slideshow, or anything that has asked Windows not to be interrupted.</summary>
    public bool PauseInFullScreen { get; set; } = true;

    /// <summary>Stay out of the way while sound is playing. Watching a video produces no
    /// keyboard or mouse input either, and a video in a window is not fullscreen at all. It is
    /// a proxy, and only a proxy: sound means somebody is listening, but silence does not mean
    /// nobody is watching. See PauseWhileMediaPlaying for the signal that does.</summary>
    public bool PauseWhileAudioPlaying { get; set; } = true;

    /// <summary>Ask Windows what is playing, rather than asking the loudspeaker.
    ///
    /// The media session records behind the taskbar's media flyout say whether a player is
    /// playing and whether it is video or music, neither of which depends on an audio track
    /// existing. Video holds everything off; music holds the artifacts off but still lets the
    /// screen reach black, because an album must not keep an OLED lit for three hours.</summary>
    public bool PauseWhileMediaPlaying { get; set; } = true;

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

    /// <summary>Turn monitor backlights down during blackout. Drawing black is enough for
    /// OLED, where black is unlit; an LCD keeps glowing behind it. Offered to every monitor --
    /// whichever accept it get it, whatever panel they use. Restored exactly as found,
    /// including after a crash.</summary>
    public bool DimMonitorBacklight { get; set; } = true;

    /// <summary>Switch HDR off during blackout, so the backlight can actually be dimmed.
    ///
    /// While HDR is on, Windows owns the luminance pipeline and the monitor's DDC/CI channel
    /// is dead: brightness commands are accepted and discarded. Turning it off makes them work
    /// again -- but it is a display mode change, so expect a second of black and a re-sync at
    /// each end, and full-screen applications may not enjoy it. HDR is restored on wake, and
    /// on the next run if this one ends badly.</summary>
    public bool DisableHdrDuringBlackout { get; set; } = true;

    /// <summary>Carry an Emission onto the keyboard backlight, over HID.
    ///
    /// The sky's red rises on the keys through the buildup, the wavefront flares them white,
    /// lightning flashes them, and the blackout takes them dark. Whatever the keyboard was
    /// doing before is put back on waking, and on the next run if this one ends badly.
    ///
    /// Off by default. It needs an ASUS Aura keyboard and it needs Windows to own the lighting:
    /// with Dynamic Lighting switched off, the vendor's own software holds the keys and every
    /// write is accepted and thrown away, silently. On any other machine nothing happens at all,
    /// beyond a line in the log.</summary>
    public bool KeyboardLighting { get; set; } = false;

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
    /// the Emission itself is running -- nothing is drawn once the screen is black.
    ///
    /// Also gates the ambient strikes of the stormy weather state, which is the same sky doing
    /// the same thing more quietly. Somebody who turned lightning off does not want it back
    /// because the weather changed.</summary>
    public bool Lightning { get; set; } = true;

    /// <summary>Ambient weather -- fog, rain, and rain with lightning -- drifting through while
    /// the artifacts are on screen, changing about once a minute. Zone theme only, and nothing is
    /// drawn once the screen is black.</summary>
    public bool Weather { get; set; } = true;

    /// <summary>Make the blackout an Emission -- red sky, agitated artifacts, shockwave --
    /// instead of a plain fade. Zone theme only; either way it ends at pure black.</summary>
    public bool Emission { get; set; } = true;

    /// <summary>Lock the session once the screen has gone black, so coming back needs a PIN,
    /// password or Windows Hello. Off by default: locking somebody's machine is not something
    /// to start doing to them unasked.
    ///
    /// This is Windows' own lock, not a prompt of ours -- see <see cref="Interop.SessionLock"/>
    /// for why that distinction is the whole point. It fires only once the screen has actually
    /// reached black, never while an Emission is still playing, so interrupting the animation
    /// leaves you where you were.</summary>
    public bool LockAfterBlackout { get; set; } = false;

    [JsonIgnore]
    public static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Bubbles");

    [JsonIgnore]
    public static string FilePath => Path.Combine(Directory, "settings.json");

    internal static readonly JsonSerializerOptions JsonOptions = new()
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

        // Stamped, so the density conversion never runs against defaults that were already
        // written in the new meaning.
        var fresh = new Settings { SettingsVersion = DensityVersion };
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

    /// <summary>Whether <see cref="BubbleCount"/> still holds a total written under the old
    /// meaning and has yet to be converted to a density.
    ///
    /// Answered here but acted on where the monitor regions are known, because the conversion
    /// needs the real layout in field coordinates and nothing at load time has it.</summary>
    [JsonIgnore]
    public bool NeedsDensityMigration => SettingsVersion < DensityVersion;

    /// <summary>Copies every persisted value onto another instance.
    ///
    /// By reflection rather than a written-out list, because a settings class gains properties
    /// and a hand-maintained copy would silently stop carrying the newest one -- which would
    /// show up as a single setting that cancelling the settings window failed to put back.
    /// Read/write instance properties are exactly the persisted set: the computed ones have no
    /// setter and the paths are static.</summary>
    public void CopyTo(Settings other)
    {
        foreach (var property in typeof(Settings).GetProperties())
            if (property is { CanRead: true, CanWrite: true })
                property.SetValue(other, property.GetValue(this));
    }

    /// <summary>The legal range of every clamped setting, in one place.
    ///
    /// Named rather than written into <see cref="Clamped"/> as literals because the settings
    /// window offers these same ranges to its controls. A slider whose maximum disagreed with
    /// the clamp would appear to accept a value and then have it moved somewhere else, which
    /// reads as the application losing the setting rather than refusing it.</summary>
    public static class Range
    {
        public const int BubbleCountMin = 1, BubbleCountMax = 400;
        public const double MinRadiusMin = 4, MinRadiusMax = 1200;
        public const double MaxRadiusMax = 1600;
        public const double SpeedMin = 0, SpeedMax = 2000;
        public const double SpeedVarianceMin = 0, SpeedVarianceMax = 1;
        public const double OpacityMin = 0.02, OpacityMax = 1;
        public const double BuoyancyMin = -400, BuoyancyMax = 400;
        public const double WobbleMin = 0, WobbleMax = 0.5;
        public const int MaxFpsMin = 0, MaxFpsMax = 480;
        public const double IdleSecondsMin = 1, IdleSecondsMax = 86400;
        public const double BlackoutSecondsMax = 86400;
        public const double DimMin = 0, DimMax = 1;
        public const double CollectRadiusMin = 0, CollectRadiusMax = 600;
        public const double UpdateCheckHoursMin = 1, UpdateCheckHoursMax = 720;
        public const double FadeInSecondsMin = 0, FadeInSecondsMax = 30;
    }

    public Settings Clamped()
    {
        BubbleCount = Math.Clamp(BubbleCount, Range.BubbleCountMin, Range.BubbleCountMax);
        MinRadius = Math.Clamp(MinRadius, Range.MinRadiusMin, Range.MinRadiusMax);

        // Floored at MinRadius, not at a constant: a maximum below the minimum describes no
        // radius at all.
        MaxRadius = Math.Clamp(MaxRadius, MinRadius, Range.MaxRadiusMax);
        Speed = Math.Clamp(Speed, Range.SpeedMin, Range.SpeedMax);
        SpeedVariance = Math.Clamp(SpeedVariance, Range.SpeedVarianceMin, Range.SpeedVarianceMax);
        Opacity = Math.Clamp(Opacity, Range.OpacityMin, Range.OpacityMax);
        Buoyancy = Math.Clamp(Buoyancy, Range.BuoyancyMin, Range.BuoyancyMax);
        Wobble = Math.Clamp(Wobble, Range.WobbleMin, Range.WobbleMax);
        MaxFps = Math.Clamp(MaxFps, Range.MaxFpsMin, Range.MaxFpsMax);
        IdleSeconds = Math.Clamp(IdleSeconds, Range.IdleSecondsMin, Range.IdleSecondsMax);

        // Zero means never, and is preserved as such. Any other value is measured from when the
        // screensaver starts, so it cannot precede it -- which is why the settings window labels
        // this delay as time after the artifacts appear rather than after the last keypress.
        BlackoutSeconds = BlackoutSeconds <= 0
            ? 0
            : Math.Clamp(BlackoutSeconds, IdleSeconds, Range.BlackoutSecondsMax);
        Dim = Math.Clamp(Dim, Range.DimMin, Range.DimMax);
        CollectRadius = Math.Clamp(CollectRadius, Range.CollectRadiusMin, Range.CollectRadiusMax);
        UpdateCheckHours = Math.Clamp(
            UpdateCheckHours, Range.UpdateCheckHoursMin, Range.UpdateCheckHoursMax);
        FadeInSeconds = Math.Clamp(FadeInSeconds, Range.FadeInSecondsMin, Range.FadeInSecondsMax);
        return this;
    }
}
