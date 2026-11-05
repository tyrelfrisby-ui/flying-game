// The DATA CLASS is shared. The System.Text.Json loader below is headless-only: Unity has no System.Text.Json,
// and there the Bridge assembly reads the same file with Newtonsoft (UnityVrLensLoader) into this same class --
// the pattern AtmosphereFieldConfigLoader already follows.
namespace FlyingGame.Core.Vr;

/// <summary>
/// The headset: a Cardboard-style viewer profile plus the few numbers the renderer needs. Engine-free, so the
/// geometry it drives (<see cref="VrOptics"/>) is tested without a phone or a Unity scene.
///
/// The defaults are GENERIC GOOGLE CARDBOARD v2 (the 2015 "Cardboard I/O" viewer), as Google publishes them for
/// viewer manufacturers: screen-to-lens 39.3 mm, inter-lens 63.9 mm, tray-to-lens 35 mm, distortion
/// k1 0.33582564 / k2 0.55348791, 60° in each direction, bottom vertical alignment. The owner's brief quoted
/// 0.039 / 0.064 / 0.34 / 0.55 — the same figures rounded — and the full precision is used here.
/// Source: https://support.google.com/cardboard/manufacturers/answer/6321873
///
/// It is a PROFILE, not a measurement of Ty's own headset. A different viewer wants its own profile, which is
/// what the file is for.
/// </summary>
public sealed class VrLensConfig
{
    /// <summary>Centre-to-centre lens spacing, metres. NOT the same thing as the pilot's IPD: the lenses are
    /// fixed in the cardboard, the eyes are his.</summary>
    public double InterLensDistanceM { get; set; } = 0.0639;

    /// <summary>Lens to screen surface, metres — the focal geometry that sets how big the screen looks.</summary>
    public double ScreenToLensDistanceM { get; set; } = 0.0393;

    /// <summary>Bottom of the phone tray to the lens centres, metres. With <see cref="VerticalAlignment"/>
    /// BOTTOM this is what puts the lens centres at the right height on the screen.</summary>
    public double TrayToLensDistanceM { get; set; } = 0.035;

    /// <summary>BOTTOM (the v2 viewer), CENTER or TOP.</summary>
    public string VerticalAlignment { get; set; } = "BOTTOM";

    /// <summary>Radial distortion of the lens, r' = r(1 + k1·r² + k2·r⁴). Codex's shader undoes it.</summary>
    public double K1 { get; set; } = 0.33582564;
    public double K2 { get; set; } = 0.55348791;

    /// <summary>The lens's own field of view, degrees, LEFT/RIGHT/BOTTOM/TOP for the LEFT eye (the right eye
    /// mirrors it). The rendered FOV is the smaller of this and what the screen actually subtends.</summary>
    public double FovLeftDeg { get; set; } = 60.0;
    public double FovRightDeg { get; set; } = 60.0;
    public double FovBottomDeg { get; set; } = 60.0;
    public double FovTopDeg { get; set; } = 60.0;

    /// <summary>The PILOT's interpupillary distance, metres: what the two cameras are actually separated by.
    /// 63 mm is the adult mean. Ty should set his own — it is the one number that decides whether the stereo
    /// fuses comfortably.</summary>
    public double IpdM { get; set; } = 0.063;

    /// <summary>Eye render-target scale against the per-eye viewport. Below 1 trades sharpness for frame rate;
    /// 0.85 is the starting point here because a phone drawing the cockpit TWICE has to hold 60 fps, and a
    /// headset makes dropped frames far worse than soft pixels. Ty's to tune on the device.</summary>
    public double RenderScale { get; set; } = 0.85;

    public bool ChromaticAberration { get; set; } = true;
    /// <summary>Per-channel radial scale for the CA pass. Red slightly in, blue slightly out; green is 1 by
    /// definition. Codex tunes these against the real lens.</summary>
    public double CaRedScale { get; set; } = 0.994;
    public double CaBlueScale { get; set; } = 1.006;

    /// <summary>VR HUD: "off" hides it; "minimal" shows IAS kt, altitude ft MSL and true heading.
    /// Unknown values hide it. Rendered binocularly in the world before lens correction.</summary>
    public string HudMode { get; set; } = "minimal";

#if !UNITY_5_3_OR_NEWER
    private static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static VrLensConfig LoadFromJson(string json) =>
        System.Text.Json.JsonSerializer.Deserialize<VrLensConfig>(json, Options) ?? new VrLensConfig();

    public static VrLensConfig LoadFromFile(string path) => LoadFromJson(System.IO.File.ReadAllText(path));
#endif

    /// <summary>Clamped to what a headset can actually be: a profile with nonsense in it must not produce a
    /// NaN frustum on a pilot's face.</summary>
    public VrLensConfig Sane()
    {
        InterLensDistanceM = Clamp(InterLensDistanceM, 0.04, 0.09, 0.0639);
        ScreenToLensDistanceM = Clamp(ScreenToLensDistanceM, 0.02, 0.09, 0.0393);
        TrayToLensDistanceM = Clamp(TrayToLensDistanceM, 0.0, 0.09, 0.035);
        IpdM = Clamp(IpdM, MinIpdM, MaxIpdM, 0.063);
        RenderScale = Clamp(RenderScale, 0.5, 1.0, 0.85);
        foreach (var _ in new[] { 0 }) { }
        FovLeftDeg = Clamp(FovLeftDeg, 10, 85, 60);
        FovRightDeg = Clamp(FovRightDeg, 10, 85, 60);
        FovBottomDeg = Clamp(FovBottomDeg, 10, 85, 60);
        FovTopDeg = Clamp(FovTopDeg, 10, 85, 60);
        return this;
    }

    /// <summary>The slider's ends: 55–75 mm spans about the 1st to 99th percentile of adult IPD.</summary>
    public const double MinIpdM = 0.055, MaxIpdM = 0.075;

    private static double Clamp(double v, double lo, double hi, double fallback) =>
        double.IsFinite(v) && v > 0.0 ? System.Math.Clamp(v, lo, hi) : fallback;
}
