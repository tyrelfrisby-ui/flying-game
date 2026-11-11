using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>
/// THE WHEELS / SKIS SELECTOR, one shared cockpit part (Ty 2026-11-05: "when chosen a selector for 'wheels/skis' gets put in
/// the cockpit someplace"). Every aeroplane draws this same part from this one design — a placard plate with a lever in a
/// two-position gate and three annunciator lamps — and only WHERE it goes is per aeroplane: an anchor on the seat
/// (<see cref="StationConfig.SkiSelector"/>), or, where none is authored, a fixed offset from the pilot's eye. No aeroplane's
/// model or builder carries a selector of its own (WheelSkiHingeTests.TheSelectorIsOneSharedPart).
///
/// The lever is shape-coded like a landing-gear handle (14 CFR 23.781 codes the gear knob as a wheel): its knob is a small
/// ski. It shows the COMMAND — up is WHEELS (skis up), down is SKIS (skis down), the same sense as a gear handle — and the
/// lamps show where the skis ARE: green WHEELS, green SKIS, amber TRANSIT while the hydraulics run (<see cref="SkiSelector.Annunciation"/>).
/// Dimensions are a design choice, not a type's: Ty to approve from the renders.
/// </summary>
public static class SkiSelectorPart
{
    /// <summary>Plate: width, height, thickness (m).</summary>
    public const double PlateWidthM = 0.085, PlateHeightM = 0.110, PlateThicknessM = 0.004;
    /// <summary>Lever: from its pivot at the plate's centre to the knob.</summary>
    public const double LeverLengthM = 0.055, LeverDiameterM = 0.007;
    /// <summary>Knob: a little ski, length × width × thickness, its tip turned up.</summary>
    public const double KnobLengthM = 0.030, KnobWidthM = 0.012, KnobThicknessM = 0.006;
    /// <summary>Lever angle from the plate's normal (degrees, + = up): WHEELS up, SKIS down.</summary>
    public const double WheelsDeg = 35.0, SkisDeg = -35.0;
    /// <summary>Lamps: diameter, and their row along the plate's top (left to right as the pilot looks: WHEELS, TRANSIT, SKIS).</summary>
    public const double LampDiameterM = 0.011;
    public static readonly string[] LampNames = { "WHEELS", "TRANSIT", "SKIS" };

    /// <summary>
    /// Where the part goes when a seat authors no anchor, from the pilot's eye (body frame: +x forward, +y right, +z down):
    /// 0.50 m ahead, 0.22 m to the left and 0.32 m below, i.e. low on the left of the panel, under the left hand — where
    /// a gear handle or a hydraulic selector sits in a light aeroplane.
    /// </summary>
    public static readonly Vec3 DefaultOffsetFromEye = new(0.50, -0.22, 0.32);

    /// <summary>One mounting.</summary>
    public readonly struct Mount
    {
        public readonly string SeatId;
        /// <summary>The plate's centre, body frame.</summary>
        public readonly Vec3 Pos;
        /// <summary>The eye it faces (the plate's normal points at it).</summary>
        public readonly Vec3 Eye;
        public readonly bool Authored;
        public Mount(string seat, Vec3 pos, Vec3 eye, bool authored) { SeatId = seat; Pos = pos; Eye = eye; Authored = authored; }
    }

    /// <summary>Every mounting on this aeroplane: each seat that authors an anchor, else the pilot's (first) seat at the
    /// default offset — or, on a type with no seats authored yet (most of the fleet), the default offset from
    /// <paramref name="fallbackEye"/>, the cockpit view's own estimate of the pilot's eye. None when the option is not
    /// fitted.</summary>
    public static List<Mount> MountsFor(AircraftConfig c, Vec3? fallbackEye = null)
    {
        var list = new List<Mount>();
        if (!SkiGear.AnyFitted(c)) return list;
        List<StationConfig> seats = c.Loading?.Seats ?? new List<StationConfig>();
        foreach (StationConfig s in seats)
            if (s.SkiSelector is { Length: 3 } a)
                list.Add(new Mount(s.Id, new Vec3(a[0], a[1], a[2]), s.EyeVec(), true));
        if (list.Count == 0 && seats.Count > 0)
            list.Add(new Mount(seats[0].Id, seats[0].EyeVec() + DefaultOffsetFromEye, seats[0].EyeVec(), false));
        else if (list.Count == 0 && fallbackEye is Vec3 e)
            list.Add(new Mount("", e + DefaultOffsetFromEye, e, false));
        return list;
    }

    /// <summary>The lever's angle for the lever's own position.</summary>
    public static double LeverDeg(SkiSelector.Position p) => p == SkiSelector.Position.Skis ? SkisDeg : WheelsDeg;

    /// <summary>Which lamp is lit (index into <see cref="LampNames"/>).</summary>
    public static int LitLamp(SkiSelector.Light l) => l switch { SkiSelector.Light.Wheels => 0, SkiSelector.Light.Transit => 1, _ => 2 };
}
