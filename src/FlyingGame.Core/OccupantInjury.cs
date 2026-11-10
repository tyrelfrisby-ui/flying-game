using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>How the pilot came out of it, worst first last: the parachute landing's grade, the crash's, and the two
/// together (<see cref="OccupantInjury.Worse"/>). The Bridge's one-liners are picked by it.</summary>
public enum LandingOutcome { Unhurt, Bruised, Injured, Dead }

/// <summary>
/// WHAT THE CRASH DID TO THE PILOT (Ty 2026-11-05 06:35: "Bailing out after a crash has the pilot surviving because the
/// comment is only looking at his impact and it is low. Make the comments apply to crashes too"). Until this the only
/// injury in the game was the parachute landing's (<see cref="ParachuteLanding"/>): a pilot who flew into a hillside at
/// 60 m/s, sat in the wreck and pressed BAIL OUT stepped off a stopped aeroplane, "landed" at 1 m/s and walked away.
///
/// The occupant is judged the way escape systems and crash-survivable seats are: by what his SEAT did, through the
/// body's own dynamics, and by whether the structure round him kept his space.
///
///  • The seat's acceleration is MEASURED, not estimated: the sim hands over the seat point's velocity change over each
///    step (CG velocity + ω × r, everything that changed it — contact, crush, a tree, the water, a part breaking off),
///    less gravity, in body axes. That is what an accelerometer bolted to the seat would read.
///  • It drives the Brinkley Dynamic Response Criterion (Brinkley &amp; Shaffer 1971; Brinkley 1985, the model
///    NASA-STD-3001 uses for crew landing loads): one damped spring-mass per body axis, x'' + 2ζω x' + ω² x = A(t),
///    DR = ω² x / g. Its natural frequencies are the body's (the spine's in z), so it is a low-pass filter with the
///    body's own overshoot: a 5 ms spike barely moves it, a 100 ms 20 g pulse moves it a lot — exactly why a crash that
///    stops in a crush stroke is survivable and the same speed into a wall is not. Restrained occupant (all of these
///    aeroplanes have a harness).
///  • β = √Σ (DR_i / DR_limit,i)², against the published Low / Moderate / High risk limits for the sign of each DR. β ≥ 1
///    at a level is that level's risk of injury: low ≈ the limit for a routine capsule landing, high ≈ a high chance of
///    serious injury (spinal fracture at DRz 18 is the ejection-seat design limit).
///  • Fatal: β_high ≥ <see cref="FatalBetaHigh"/>. Brinkley stops at "high risk"; the step past it is ours and is Q
///    for Ty (AGENT-LOG NEEDS YOU): 1.5 × the high limits is DRz 27 (well past spinal fracture, into the vertical
///    crash range where the aorta fails) and DRx −69 (a frontal deceleration beyond anything a belted occupant is
///    recorded surviving outside a rocket sled).
///  • Livable space: crash-survivability practice (the "C" of CREEP, the container) — if the structure folds into the
///    occupant, the loads do not matter. The crush the sim already keeps for the CABIN section
///    (<see cref="FuselageCrush.CabinCrushM"/>, the section that holds the seats in that model) is that fold. One crush stroke (<see cref="FuselageStructure.CrushTravelM"/>, the
///    stroke the structure is built to give) is used up → Injured; the full geometric fold
///    (<see cref="FuselageStructure.MaxGeometricCrushM"/>, where the model stops moving the struck point because there
///    is no airframe left between it and him) → Dead. Any cabin crush at all is at least Bruised.
///
/// Pure: no Unity. The Sim feeds it (Aircraft.Occupant); the Bridge reads it when the pilot leaves.
/// </summary>
public sealed class OccupantInjury
{
    /// <summary>Brinkley's per-axis natural frequency (rad/s) and damping ratio: x (chest-back), y (side), z (spine).</summary>
    public const double OmegaX = 62.8, ZetaX = 0.2;
    public const double OmegaY = 58.0, ZetaY = 0.09;
    public const double OmegaZ = 52.9, ZetaZ = 0.224;

    /// <summary>One risk level's DR limits for each axis and sign. +x eyeballs IN (the seat back pushes him forward),
    /// −x eyeballs OUT (a frontal deceleration, into the harness); ±y sideways; +z eyeballs DOWN (the seat pan pushes up,
    /// spinal compression), −z eyeballs up.</summary>
    public readonly struct Limits
    {
        public readonly double XPos, XNeg, YPos, YNeg, ZPos, ZNeg;
        public Limits(double xPos, double xNeg, double yPos, double yNeg, double zPos, double zNeg)
        { XPos = xPos; XNeg = xNeg; YPos = yPos; YNeg = yNeg; ZPos = zPos; ZNeg = zNeg; }

        /// <summary>β against these limits for the given DRs (each axis divided by the limit of its own sign).</summary>
        public double Beta(double drX, double drY, double drZ)
        {
            double bx = drX / (drX >= 0 ? XPos : XNeg), by = drY / (drY >= 0 ? YPos : YNeg), bz = drZ / (drZ >= 0 ? ZPos : ZNeg);
            return System.Math.Sqrt(bx * bx + by * by + bz * bz);
        }
    }

    /// <summary>Brinkley's restrained-occupant limits (as tabulated for NASA-STD-3001), magnitudes.</summary>
    public static readonly Limits LowRisk = new(35, 28, 14, 14, 13.1, 13.4);
    public static readonly Limits ModerateRisk = new(40, 35, 17, 17, 15.2, 16.5);
    public static readonly Limits HighRisk = new(46, 46, 22, 22, 18.0, 20.4);

    /// <summary>β against the HIGH limits at which the crash is fatal (see the class notes; a decision for Ty).</summary>
    public const double FatalBetaHigh = 1.5;

    /// <summary>The integrator's own step, s: the sim's 5 ms is ωΔt = 0.31 on x, too coarse for semi-implicit Euler to
    /// keep the overshoot right, so each sim step is cut into steps of at most this.</summary>
    public const double SubStepSec = 0.001;

    private const double G = 9.80665;

    // Per axis: displacement (m) and rate (m/s) of the body's spring.
    private double _x, _vx, _y, _vy, _z, _vz;
    private bool _settled;

    /// <summary>Largest β reached against each level so far.</summary>
    public double PeakBetaLow { get; private set; }
    public double PeakBetaModerate { get; private set; }
    public double PeakBetaHigh { get; private set; }
    /// <summary>The DRs at the moment of <see cref="PeakBetaHigh"/>.</summary>
    public double PeakDrX { get; private set; }
    public double PeakDrY { get; private set; }
    public double PeakDrZ { get; private set; }
    /// <summary>Largest seat acceleration seen (specific force, any direction), g: for the log, not the verdict.</summary>
    public double PeakSeatG { get; private set; }
    /// <summary>Most the cabin round the seat has been crushed, m.</summary>
    public double CockpitCrushM { get; private set; }

    /// <summary>
    /// One sim step of seat motion: <paramref name="specificForceBody"/> is the seat's acceleration less gravity over the
    /// step, body axes (x forward, y right, z DOWN), m/s² — (0, 0, −g) sitting still. Held constant over the step.
    /// </summary>
    public void Step(Vec3 specificForceBody, double dt)
    {
        if (!(dt > 0) || double.IsNaN(specificForceBody.X) || double.IsNaN(specificForceBody.Y) || double.IsNaN(specificForceBody.Z)) return;
        // Brinkley's inputs: +x when the seat pushes him forward (body +x), +y to the right, +z when it pushes him UP
        // (body −z, since body z is down).
        double ax = specificForceBody.X, ay = specificForceBody.Y, az = -specificForceBody.Z;
        PeakSeatG = System.Math.Max(PeakSeatG, specificForceBody.Length / G);
        if (!_settled)
        {
            // Start from rest in whatever he is sitting in (1 g on the ramp, the pull of a turn at an air start): the
            // spring is already compressed by it, not struck by it.
            _x = ax / (OmegaX * OmegaX); _y = ay / (OmegaY * OmegaY); _z = az / (OmegaZ * OmegaZ);
            _vx = _vy = _vz = 0;
            _settled = true;
        }
        int n = System.Math.Max(1, (int)System.Math.Ceiling(dt / SubStepSec));
        double h = dt / n;
        for (int i = 0; i < n; i++)
        {
            Axis(ref _x, ref _vx, ax, OmegaX, ZetaX, h);
            Axis(ref _y, ref _vy, ay, OmegaY, ZetaY, h);
            Axis(ref _z, ref _vz, az, OmegaZ, ZetaZ, h);
            Observe();
        }
    }

    private static void Axis(ref double x, ref double v, double a, double w, double zeta, double h)
    {
        v += (a - 2 * zeta * w * v - w * w * x) * h;   // semi-implicit Euler: rate first, then position
        x += v * h;
    }

    private void Observe()
    {
        double drX = OmegaX * OmegaX * _x / G, drY = OmegaY * OmegaY * _y / G, drZ = OmegaZ * OmegaZ * _z / G;
        PeakBetaLow = System.Math.Max(PeakBetaLow, LowRisk.Beta(drX, drY, drZ));
        PeakBetaModerate = System.Math.Max(PeakBetaModerate, ModerateRisk.Beta(drX, drY, drZ));
        double bh = HighRisk.Beta(drX, drY, drZ);
        if (bh > PeakBetaHigh) { PeakBetaHigh = bh; PeakDrX = drX; PeakDrY = drY; PeakDrZ = drZ; }
    }

    /// <summary>The cabin has been crushed to <paramref name="crushM"/> in all (it only grows).</summary>
    public void ObserveCrush(double crushM)
    {
        if (crushM > CockpitCrushM) CockpitCrushM = crushM;
    }

    /// <summary>The pilot's state now, from the worse of the loads and the space.</summary>
    public LandingOutcome Level => Worse(FromLoads(PeakBetaLow, PeakBetaModerate, PeakBetaHigh), FromCrush(CockpitCrushM));

    public static LandingOutcome FromLoads(double betaLow, double betaModerate, double betaHigh) =>
        betaHigh >= FatalBetaHigh ? LandingOutcome.Dead
        : betaModerate >= 1.0 ? LandingOutcome.Injured
        : betaLow >= 1.0 ? LandingOutcome.Bruised
        : LandingOutcome.Unhurt;

    public static LandingOutcome FromCrush(double crushM) =>
        crushM >= FuselageStructure.MaxGeometricCrushM ? LandingOutcome.Dead
        : crushM >= FuselageStructure.CrushTravelM ? LandingOutcome.Injured
        : crushM > 0 ? LandingOutcome.Bruised
        : LandingOutcome.Unhurt;

    public static LandingOutcome Worse(LandingOutcome a, LandingOutcome b) => (LandingOutcome)System.Math.Max((int)a, (int)b);

    /// <summary>One line for the log: the verdict and what drove it.</summary>
    public string Describe() =>
        $"{Level}: β low {PeakBetaLow:F2} / moderate {PeakBetaModerate:F2} / high {PeakBetaHigh:F2} " +
        $"(DR x {PeakDrX:F1} y {PeakDrY:F1} z {PeakDrZ:F1}), seat peak {PeakSeatG:F1} g, cockpit crush {CockpitCrushM * 100:F0} cm";
}

/// <summary>
/// The parachute landing's own grade (owner 2026-09-09), moved here from the Bridge's PilotPhrases unchanged so the
/// combined verdict can be tested: impact speed along the surface normal, water softening the same hit.
/// </summary>
public static class ParachuteLanding
{
    /// <summary>Impact-speed thresholds (m/s) on land; a canopy landing is ~5 m/s, a good PLF walks away from 6.</summary>
    public const double BruisedMs = 6, InjuredMs = 9, DeadMs = 14;
    /// <summary>Water: the same outcome needs this much more speed.</summary>
    public const double WaterSoftening = 1.6;

    public static LandingOutcome Outcome(double maxImpactMs, bool water)
    {
        double v = water ? maxImpactMs / WaterSoftening : maxImpactMs;
        return v < BruisedMs ? LandingOutcome.Unhurt : v < InjuredMs ? LandingOutcome.Bruised : v < DeadMs ? LandingOutcome.Injured : LandingOutcome.Dead;
    }
}
