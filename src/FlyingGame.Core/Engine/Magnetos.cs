namespace FlyingGame.Core.Engine;

/// <summary>
/// The two magnetos of a piston engine, as numbers (Ty 2026-11-05, Great Lakes: "remove the ignition switch and add TWO
/// MAG switches"). Each magneto fires one of the two plugs in every cylinder. With both, the charge is lit from two sides
/// and burns in time; with one, the flame front has twice as far to go, the pressure peak comes late and the cylinder
/// makes a little less work — the "mag drop" every run-up checks. With neither there is no spark and the engine stops at
/// once, fuel or no fuel; switched back on with the prop still turning, it fires again on the next stroke.
///
/// <see cref="PowerFactor"/> is normalised to BOTH, so an aeroplane flown with both mags on makes exactly the power it
/// always made: this adds the switches, it does not re-tune the fleet. Pure maths; the switches live in the aircraft.
/// </summary>
public static class Magnetos
{
    /// <summary>(E) Shaft power on one magneto as a fraction of both: a 4–6 % loss is the usual figure for single-plug
    /// running at cruise (the run-up's 50–175 rpm drop at 1,800 rpm on a fixed-pitch prop). Not a measured number for
    /// this engine.</summary>
    public const double SingleMagPowerFactor = 0.95;

    /// <summary>At least one magneto is live: there is a spark.</summary>
    public static bool Fires(bool left, bool right) => left || right;

    /// <summary>Shaft power relative to both magnetos: 1 with both, <see cref="SingleMagPowerFactor"/> with one, 0 with none.</summary>
    public static double PowerFactor(bool left, bool right) =>
        left && right ? 1.0 : left || right ? SingleMagPowerFactor : 0.0;

    /// <summary>The ignition key position the two switches amount to (ModelRig's <c>magneto</c>: 0 OFF, 1 R, 2 L, 3 BOTH).</summary>
    public static double KeyPosition(bool left, bool right) => left && right ? 3.0 : left ? 2.0 : right ? 1.0 : 0.0;
}
