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

    // ---- the aerated keel track: the WHITE water, and it is not spray ------------------------------------------
    //
    // Ty, 2026-11-11, naming the mechanism: "The spray is directly behind and in line with the floats keels and
    // widens at a narrower rate than the wake does it is the mixing of air and water together as the keels rub
    // along the water surface forming bubbles (white)".
    //
    // That is AIR ENTRAINMENT at a surface-piercing contact line, and it is a third thing, distinct from the two
    // this file and FloatHydro already model:
    //
    //   chine / whisker spray   thrown from the spray root, OUTWARD and FORWARD, droplets in air
    //                           (FloatHydro.SprayRoots, Savitsky 1964 — not touched here)
    //   the AERATED KEEL TRACK  bubbles dragged under at the keel, straight ASTERN, aerated WATER
    //                           (this block — it is white because it is full of air, not because it is spray)
    //   the Kelvin wake         the gravity-wave pattern, half-angle asin(1/3) = 19.47 deg
    //                           (the solver's height field, and the foam arms in Water.shader)
    //
    // Conflating the second with the third is exactly what a single fitted "1.6 beams wide" band did. The three
    // have three different widths and three different spreading rates, and the ratio between two of them is
    // testable — see <see cref="TrackHalfAngleRad"/> against <see cref="KelvinHalfAngleRad"/>.

    /// <summary>Kelvin's half-angle, asin(1/3) = 19.47 deg. Set by the dispersion of deep-water gravity waves and
    /// so independent of speed — which is precisely why it is the right thing to measure the bubble track against:
    /// one angle is fixed by wave physics, the other by turbulence, and they are not the same number.</summary>
    public static double KelvinHalfAngleRad => Math.Asin(1.0 / 3.0);

    /// <summary>Visual spreading rate dδ/dx of a PLANE TURBULENT MIXING LAYER with one stream at rest (Brown and
    /// Roshko 1974, J. Fluid Mech. 64, the velocity-ratio-1 limit, which is the case here: the water next to the
    /// keel is dragged along with it and the water outside is still). The TRACK's half-width grows at half of
    /// this, because δ is the whole layer.</summary>
    public const double MixingLayerGrowth = 0.17;

    /// <summary>How fast the track's half-width grows with distance astern, m per m: 0.085.</summary>
    public static double TrackSpreadPerM => 0.5 * MixingLayerGrowth;

    /// <summary>The track's own half-angle, 4.86 deg — a quarter of Kelvin's. This is Ty's "widens at a narrower
    /// rate than the wake does", and it comes out of turbulence rather than out of a fitted constant.</summary>
    public static double TrackHalfAngleRad => Math.Atan(TrackSpreadPerM);

    /// <summary>
    /// Half-width of the aerated track where it leaves the keel, m: the float's OWN wetted half-width, not a
    /// multiple of the beam. A V bottom of deadrise β immersed to a keel depth d is wetted out to d/tan β, and no
    /// further than the chine, so the contact line that drags the air under is at
    /// <c>min(d/tan β, B/2)</c>. On the Cub float (B = 0.5358 m, β = 18.8°) both the 15 mph plow (d = 0.171 m)
    /// and the 40 mph planing run (d = 0.149 m) are chine-limited, so the track starts exactly one beam wide —
    /// which is the answer, not a coincidence: a hull on its step is wetted to the chine by definition.
    /// </summary>
    public static double TrackHalfWidthM(double stepDraftM, double beamM, double deadriseDeg)
    {
        if (!(stepDraftM > 0) || !(beamM > 0)) return 0.0;
        double tan = Math.Tan(Math.Clamp(deadriseDeg, 1.0, 60.0) * Math.PI / 180.0);
        return Math.Min(stepDraftM / tan, 0.5 * beamM);
    }

    /// <summary>Terminal rise speed of the bubbles the keel entrains, m/s. Millimetre bubbles in water sit on the
    /// plateau of the rise-velocity curve where speed is nearly independent of size — about 0.25 m/s over roughly
    /// 0.7 to 5 mm (Clift, Grace and Weber, <i>Bubbles, Drops and Particles</i>; the same number Clausen and Wüest
    /// use for lake plumes). This is why a ship's white wake persists for hundreds of metres and a Cub's for a few:
    /// the physics is the same and only the speed and the entrainment depth differ.</summary>
    public const double BubbleRiseMs = 0.25;

    /// <summary>
    /// How far astern the white water lasts, m. The bubbles were dragged under to the keel's own immersion, and
    /// they are gone when they have risen back out: that takes <c>d / v_rise</c> seconds, during which the
    /// aeroplane has run <c>V·d / v_rise</c> further on. So the track is longer the faster you go AND the deeper
    /// the keel is in — 4.6 m at the 15 mph plow, 10.6 m at 40 mph on the step — and it is zero the moment the
    /// float leaves the water. Nothing here is fitted; it is a rise time against a ground speed.
    /// </summary>
    public static double AeratedLengthM(double speedMs, double stepDraftM) =>
        speedMs > 0 && stepDraftM > 0 ? speedMs * stepDraftM / BubbleRiseMs : 0.0;

    /// <summary>How much of the entrained column is still down there at <paramref name="xAftM"/> astern, 0..1.
    /// The bubbles rise at a constant speed, so the aerated column's depth falls LINEARLY and the whiteness with
    /// it — it does not decay exponentially, and it ends, at <see cref="AeratedLengthM"/>.</summary>
    public static double AeratedFraction01(double xAftM, double speedMs, double stepDraftM)
    {
        double length = AeratedLengthM(speedMs, stepDraftM);
        if (!(length > 0) || xAftM < 0) return 0.0;
        return Math.Clamp(1.0 - xAftM / length, 0.0, 1.0);
    }

    /// <summary>The track's half-width at <paramref name="xAftM"/> astern, m.</summary>
    public static double TrackHalfWidthAtM(double xAftM, double stepDraftM, double beamM, double deadriseDeg) =>
        TrackHalfWidthM(stepDraftM, beamM, deadriseDeg) + Math.Max(0, xAftM) * TrackSpreadPerM;

    /// <summary>Everything the water shader and the spray need for one float, in one call, so the caller does not
    /// repeat the trim/beam/draft arithmetic five times.</summary>
    public readonly struct Shape
    {
        public readonly double Cv, WhiteWater01, CavityDepthM, CavityLengthM, CrestDistanceM, CrestHeightM, CrestRiseMs;
        /// <summary>The aerated keel track: its half-width where it leaves the keel, how fast that half-width grows
        /// with distance astern, and how far astern the bubbles last.</summary>
        public readonly double TrackHalfWidthM, TrackSpreadPerM, AeratedLengthM;
        public Shape(double cv, double white, double depth, double length, double crestX, double crestZ, double rise,
                     double trackHalfWidth, double trackSpread, double aerated)
        {
            Cv = cv; WhiteWater01 = white; CavityDepthM = depth; CavityLengthM = length;
            CrestDistanceM = crestX; CrestHeightM = crestZ; CrestRiseMs = rise;
            TrackHalfWidthM = trackHalfWidth; TrackSpreadPerM = trackSpread; AeratedLengthM = aerated;
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
            CrestRiseSpeedMs(speedMs, stepDraftM, beamM, lambda, trimDeg, cv, deadriseDeg),
            TrackHalfWidthM(stepDraftM, beamM, deadriseDeg),
            TrackSpreadPerM,
            AeratedLengthM(speedMs, stepDraftM));
    }
}
