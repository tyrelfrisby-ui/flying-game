namespace FlyingGame.Core;

/// <summary>
/// The SHAPE OF THE WATER BEHIND A PLANING FLOAT: how much of it is white, how deep the hollow aft of the step is,
/// and where the rooster tail stands. Everything here is derived from state <see cref="FloatHydro"/> already
/// computes — speed, beam, trim, wetted length ratio and the draft at the step — so none of it is a speed curve
/// fitted to a screenshot, and none of it is fed back into the forces.
///
/// WHY THIS EXISTS. Ty, 2026-11-11, from the cockpit of his own J-3 on floats: "There needs to be more white
/// spray/foam behind the floats at any speed above 10 mph ramping up with speed. The keel pushes down into the
/// water and creates a 'rooster tail' of spray that is not modeled it should be. At plow the water is
/// significantly displaced downward and a 'cavity' is created behind the airplane that is visible". Then: "Using
/// physics".
///
/// HIS 10 MPH IS A FROUDE NUMBER, and checking that was the first thing worth doing. The float's beam from its own
/// config is B = 0.5358 m (not the 0.76 m of an Edo 1320 — this is a Cub float), so the beam Froude number, which
/// Savitsky calls the speed coefficient Cv = V/√(gB), is
///
///     10 mph = 4.4704 m/s  →  Cv = 4.4704 / √(9.80665 × 0.5358) = 1.95
///
/// and Savitsky (1964) states his planing equations for Cv &gt; 2 — the speed at which a hard-chine hull stops
/// behaving as a displacement body, a stagnation line forms across the bottom and water starts being THROWN
/// forward and sideways as whisker spray instead of pushed aside. Ty's threshold and Savitsky's are the same
/// number to within 3 %, which is as close as a pilot's speed tape and a 1964 towing-tank paper are ever going to
/// get. So the onset below is a Cv, not a speed: a Beaver's 1.1 m beam puts the same Cv at 14.6 mph, and that is
/// automatically right rather than separately tuned.
///
/// THE HOLLOW AND THE ROOSTER ARE THE SAME CURVE, and it was already in this repository: Savitsky and Morabito
/// (2010), "Surface Wave Contours Associated with the Forebody Wake of Stepped Planing Hulls", is
/// <see cref="FloatHydro.WakeHeightBeams"/>, used today to decide where the afterbody meets the water. It gives
/// the height of the free surface behind the step ABOVE THE FOREBODY KEEL LINE EXTENDED AFT. The keel at the step
/// sits <c>StepDraftM</c> below the undisturbed surface, so relative to the undisturbed surface the water behind
/// the step is
///
///     z(x) = −stepDraft + B · WakeHeightBeams(x/B, λ, τ, Cv, β)
///
/// which is NEGATIVE at the step — a hollow exactly as deep as the keel is immersed, which is Ty's cavity, and it
/// is a cavity for the reason he gives: the keel pushed the water down and it has not come back up yet. It
/// crosses zero some distance aft (<see cref="CavityLengthM"/>, where the cavity closes) and then rises to a crest
/// (<see cref="CrestDistanceM"/>, <see cref="CrestHeightM"/>) — the rooster tail. Nothing here is new physics; it
/// is the same published curve read for what it says about the surface instead of about the afterbody.
///
/// WHAT THIS IS NOT. It is visual only. <c>FloatSplash</c>'s header says it "draws the existing physics spray
/// without feeding anything back to the simulation" and that line holds: nothing in this class is called from
/// <see cref="FloatHydro.Compute"/>, and no force, moment or drag term reads it.
/// </summary>
public static class PlaningWake
{
    /// <summary>Savitsky (1964) states the planing equations for Cv &gt; 2 — the planing onset, and Ty's 10 mph on
    /// this float's beam — and the hull is FULLY planing, with dynamic pressure carrying nearly all the load and
    /// buoyancy almost none of it, by about Cv = 3.5. White water ramps across that band: it starts where the
    /// stagnation line forms and is complete once the whole bottom aft of it is throwing spray. On the Cub float
    /// that is 10.3 mph to 18 mph, and the 15 mph plow Ty judged it at sits two thirds of the way up.</summary>
    public const double WhiteWaterOnsetCv = 2.0, WhiteWaterFullCv = 3.5;

    /// <summary>Savitsky's speed coefficient, which is the beam Froude number: Cv = V/√(gB).</summary>
    public static double SpeedCoefficient(double speedMs, double beamM) =>
        beamM > 0 && speedMs > 0 ? speedMs / Math.Sqrt(Atmosphere.GravityMs2 * beamM) : 0.0;

    /// <summary>The speed, m/s, at which a float of this beam reaches a given speed coefficient. 4.58 m/s
    /// (10.3 mph) for the Cub float's 0.5358 m beam at Cv = 2 — Ty's "above 10 mph".</summary>
    public static double SpeedForCv(double cv, double beamM) => cv * Math.Sqrt(Atmosphere.GravityMs2 * beamM);

    /// <summary>How much of the wake behind the float is white water, 0..1. Zero below the planing onset — a
    /// displacement taxi parts the water and leaves it clear, which is why a 3 kt idle must not grow a rooster
    /// tail — and one once the whole bottom aft of the stagnation line is throwing spray.</summary>
    public static double WhiteWater01(double cv) =>
        SeaState.Smoothstep(WhiteWaterOnsetCv, WhiteWaterFullCv, cv);

    // ---- the wake surface behind the step ---------------------------------------------------------------------

    /// <summary>
    /// Height of the free surface behind the step, m, measured from the UNDISTURBED water level and positive up,
    /// at <paramref name="xAftM"/> metres aft of the step. Negative inside the cavity. This is
    /// <see cref="FloatHydro.WakeHeightBeams"/> (Savitsky and Morabito 2010) referred to the undisturbed surface
    /// instead of to the extended keel line.
    /// </summary>
    public static double SurfaceM(double xAftM, double stepDraftM, double beamM,
                                  double lambda, double trimDeg, double cv, double deadriseDeg)
    {
        if (!(beamM > 0) || xAftM < 0) return 0.0;
        return -stepDraftM + beamM * FloatHydro.WakeHeightBeams(xAftM / beamM, lambda, trimDeg, cv, deadriseDeg);
    }

    /// <summary>How far aft of the step the cavity closes, m: where the wake has risen back to the undisturbed
    /// surface. Zero when the keel is not immersed at the step, or when the wake never gets back up (it always
    /// does above the onset, because the crest height grows with trim and the draft does not).</summary>
    public static double CavityLengthM(double stepDraftM, double beamM,
                                       double lambda, double trimDeg, double cv, double deadriseDeg)
    {
        if (!(stepDraftM > 0) || !(beamM > 0) || !(cv > 0)) return 0.0;
        double crest = CrestWakeHeightBeams(lambda, trimDeg, deadriseDeg) * beamM;
        if (!(crest > stepDraftM)) return CrestDistanceM(beamM, cv);      // never closes: the whole wake is hollow
        // WakeHeightBeams is crest·sin(arg) with arg = (π/Cv)(x/3B)^1.5, so inverting it is exact rather than a
        // search: sin(arg) = draft/crest, and x = 3B·(arg·Cv/π)^(2/3).
        double arg = Math.Asin(Math.Clamp(stepDraftM / crest, 0, 1));
        return 3.0 * beamM * Math.Pow(arg * cv / Math.PI, 2.0 / 3.0);
    }

    /// <summary>The deepest point of the cavity and where it is, m. The deepest point is AT the step — the keel
    /// holds the water down and the wake only rises from there — so this is the step draft itself, reported as a
    /// depth so a caller does not have to know the sign convention.</summary>
    public static double CavityDepthM(double stepDraftM) => Math.Max(0.0, stepDraftM);

    /// <summary>Where the wake crest — the rooster tail — stands, m aft of the step. The crest is where Savitsky
    /// and Morabito's sine reaches π/2: x = 3B·(Cv/2)^(2/3), so it moves aft as the square-ish root of speed. At
    /// Cv = 2.9 (15 mph on a Cub float) it is 2.1 m astern; at Cv = 7.8 (40 mph) it is 4.0 m.</summary>
    public static double CrestDistanceM(double beamM, double cv) =>
        beamM > 0 && cv > 0 ? 3.0 * beamM * Math.Pow(cv / 2.0, 2.0 / 3.0) : 0.0;

    /// <summary>The crest's height above the EXTENDED KEEL LINE, in beams: Savitsky and Morabito's amplitude with
    /// the sine at 1.</summary>
    public static double CrestWakeHeightBeams(double lambda, double trimDeg, double deadriseDeg)
    {
        // Savitsky and Morabito's amplitude has a constant term, so it is non-zero even at zero trim: that is the
        // wake of a hull that is IN the water at no trim, and it is nonsense for one that is not in the water at
        // all. A float out of the water has no wetted length, and no wetted length is the test.
        if (!(lambda > 0)) return 0.0;
        double a = deadriseDeg >= 20 ? 2.0 : 1.5 + 0.5 * Math.Clamp((deadriseDeg - 10) / 10, 0, 1);
        return 0.17 * (a + 0.03 * lambda * Math.Pow(Math.Max(trimDeg, 0), 1.5));
    }

    /// <summary>The rooster tail's height above the undisturbed water, m. Never negative: where the crest does not
    /// reach the surface there is no rooster tail, only a long hollow.</summary>
    public static double CrestHeightM(double stepDraftM, double beamM, double lambda, double trimDeg, double deadriseDeg) =>
        Math.Max(0.0, beamM * CrestWakeHeightBeams(lambda, trimDeg, deadriseDeg) - stepDraftM);

    /// <summary>
    /// How fast the water is going UP as it climbs out of the cavity on to the crest, m/s — what throws the
    /// rooster tail's spray clear of the surface. The surface is a fixed shape in the hull's frame, so a water
    /// particle crossing it at the hull's speed rises at V·dz/dx; this is that slope taken across the climb, from
    /// the bottom of the cavity to the crest. A ballistic plume from it reaches w²/2g.
    ///
    /// For the Cub float at 15 mph plowing (Cv 2.9, τ 8.2°, λ 1.83, step draft 0.171 m) that is a 0.29 m climb
    /// over 2.1 m at 6.7 m/s: w = 0.94 m/s, and a plume about 4 cm high. A J-3 on floats does not throw a
    /// hydroplane's rooster, and saying so with a number is better than drawing one that is not there.
    /// </summary>
    public static double CrestRiseSpeedMs(double speedMs, double stepDraftM, double beamM,
                                          double lambda, double trimDeg, double cv, double deadriseDeg)
    {
        double x = CrestDistanceM(beamM, cv);
        if (!(x > 0) || !(speedMs > 0)) return 0.0;
        double rise = beamM * CrestWakeHeightBeams(lambda, trimDeg, deadriseDeg);
        return speedMs * rise / x;
    }

    /// <summary>Everything the water shader and the spray need for one float, in one call, so the caller does not
    /// repeat the trim/beam/draft arithmetic five times.</summary>
    public readonly struct Shape
    {
        public readonly double Cv, WhiteWater01, CavityDepthM, CavityLengthM, CrestDistanceM, CrestHeightM, CrestRiseMs;
        public Shape(double cv, double white, double depth, double length, double crestX, double crestZ, double rise)
        {
            Cv = cv; WhiteWater01 = white; CavityDepthM = depth; CavityLengthM = length;
            CrestDistanceM = crestX; CrestHeightM = crestZ; CrestRiseMs = rise;
        }
    }

    /// <summary>The whole wake shape for one float, from the state <see cref="FloatHydro"/> last reported for it.</summary>
    public static Shape Of(double speedMs, double beamM, double stepDraftM,
                           double lambda, double trimDeg, double deadriseDeg)
    {
        double cv = SpeedCoefficient(speedMs, beamM);
        double white = WhiteWater01(cv);
        return new Shape(
            cv, white,
            CavityDepthM(stepDraftM),
            CavityLengthM(stepDraftM, beamM, lambda, trimDeg, cv, deadriseDeg),
            CrestDistanceM(beamM, cv),
            CrestHeightM(stepDraftM, beamM, lambda, trimDeg, deadriseDeg),
            CrestRiseSpeedMs(speedMs, stepDraftM, beamM, lambda, trimDeg, cv, deadriseDeg));
    }
}
