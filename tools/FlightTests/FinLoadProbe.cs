using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE VERTICAL TAIL'S LIMIT LOAD, FLOWN. Ty 2026-11-11, verbatim:
///
///   "At va measure the force on the vertical tail with a yaw one way followed by an immediate yaw full deflection
///    the other way then add 15% and call that the limit load with a 2.0 ultimate strength before any fatigue"
///
/// IT IS A REAL CERTIFICATION MANOEUVRE, not an invention: the rudder-reversal case of the yaw manoeuvring
/// conditions (FAR 23.441 / 25.351), and the case that took the fin off AA587. So it is flown as the real thing
/// rather than as "deflect and measure", and the difference IS the case:
///
///   • ONE DEFLECTION from wings-level flight yaws the aeroplane, and the sideslip that builds works AGAINST the
///     rudder's own incidence on the fin — so the fin's force peaks early and then falls away as the aeroplane
///     weathercocks into its new steady slip.
///   • THE REVERSAL, applied while the sideslip is still at or near its overswing peak, puts the sideslip angle and
///     the opposite control deflection on the SAME side of the fin, so the two ADD. Strictly worse, and measuring a
///     single deflection would under-read the design case.
///
/// Both numbers are printed so the gap is visible rather than asserted, and both are authored: the reversal peak
/// plus 15 % is Ty's LIMIT LOAD (<see cref="LimitsConfig.FinLimitLoadN"/>), and the single-deflection peak at Va is
/// <see cref="LimitsConfig.FinOrdinaryLoadN"/>, the load of an ordinary rudder input.
///
/// AND THE THIRD CASE IS THE FATIGUE ENDPOINT, which is Ty's too — 2026-11-11, verbatim: the fin "fails at the
/// equivalent of full rudder at 75% va speed." So the endpoint is the single-deflection peak flown AGAIN at
/// <see cref="EndpointVaFraction"/> x Va, and it is MEASURED rather than scaled. The arithmetic guess — a fixed
/// deflection loads a surface in proportion to dynamic pressure, so 0.75 Va ought to give 0.5625 x the Va figure —
/// is only a guess until the aeroplane is flown at that speed, because the sideslip that builds at a given
/// deflection need not be speed-independent. <see cref="TheEndpointScalesWithDynamicPressure"/> is where the
/// measurement and the arithmetic are put side by side.
///
/// VA IS DERIVED WHERE IT IS NOT AUTHORED, by its definition: Va = Vs1 x sqrt(n_limit), the speed at which full
/// control deflection makes exactly the limit load factor (FAR 23.335(c)). Only glider-grob103-acro-like publishes
/// a vaMs today.
///
/// FINLOAD=1 prints the fleet table and the lines to author; otherwise the asserts alone run.
/// </summary>
[Collection("WorldTerrainActive")]
public class FinLoadProbe
{
    private readonly ITestOutputHelper _o;
    public FinLoadProbe(ITestOutputHelper o) { _o = o; }

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    /// <summary>Still air, no terrain: a structural limit is a property of the airframe, not of the weather.</summary>
    private static IDisposable StillAir()
    {
        var field = Atmosphere.Field; var wind = Atmosphere.SteadyWind; var turb = Atmosphere.ActiveTurbulence;
        var terrain = WorldTerrain.Active; var ridge = Atmosphere.ActiveRidge;
        var thermals = Atmosphere.Thermals.ToList();
        Atmosphere.Field = null; Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null;
        WorldTerrain.Active = null; Atmosphere.ActiveRidge = null; Atmosphere.Thermals.Clear();
        return new Restore(() =>
        {
            Atmosphere.Field = field; Atmosphere.SteadyWind = wind; Atmosphere.ActiveTurbulence = turb;
            WorldTerrain.Active = terrain; Atmosphere.ActiveRidge = ridge; Atmosphere.Thermals.AddRange(thermals);
        });
    }
    private sealed class Restore : IDisposable { private readonly Action _a; public Restore(Action a) { _a = a; } public void Dispose() => _a(); }

    /// <summary>The altitude the manoeuvre is flown at: low enough that the density correction is small, high enough
    /// that a glide at Va does not reach the ground inside the run.</summary>
    public const double ProbeAltitudeM = 900.0;

    /// <summary>Ty's 15 %: the margin over the measured reversal peak that makes it the LIMIT load.</summary>
    public const double LimitMargin = 1.15;

    /// <summary>TY'S FATIGUE ENDPOINT, 2026-11-11: the fin "fails at the equivalent of full rudder at 75% va
    /// speed." The fraction of Va the endpoint case is flown at.</summary>
    public const double EndpointVaFraction = 0.75;

    /// <summary>The arithmetic the measurement is checked against: at a fixed deflection a surface's load goes as
    /// dynamic pressure, which goes as V squared, so 0.75 Va should give 0.5625 x the Va load. Mirrored in
    /// <see cref="FlyingGame.Core.Aero.Flutter.FinEndpointOverOrdinary"/>, which is what the sim actually uses.</summary>
    public const double EndpointDynamicPressureRatio = EndpointVaFraction * EndpointVaFraction;

    /// <summary>The endpoint load for a type, flown: full rudder from trimmed flight at 0.75 Va, peak fin side
    /// force. The same single-deflection case as <see cref="LimitsConfig.FinOrdinaryLoadN"/>, at the lower
    /// speed — which is what "the equivalent of full rudder at 75% va speed" is.</summary>
    public static FinRun FlyEndpoint(AircraftConfig c, double vaMs, int firstSign = 1)
        => Fly(c, vaMs * EndpointVaFraction, firstSign);

    /// <summary>Does this type have a vertical tail with strips on it? Nothing else can be measured.</summary>
    public static bool HasFin(AircraftConfig c)
    {
        foreach (SurfaceConfig sf in c.Surfaces)
        {
            bool vertical = sf.Id.Contains("vstab", StringComparison.OrdinalIgnoreCase)
                            || sf.Id.Contains("vertical", StringComparison.OrdinalIgnoreCase);
            if (vertical && sf.Strips.Count > 0) return true;
        }
        return false;
    }

    /// <summary>
    /// VA BY ITS DEFINITION: Vs1 x sqrt(n_limit) — the speed at which full control deflection produces exactly the
    /// limit load factor. That is not an approximation of Va, it is what Va IS (FAR 23.335(c)).
    ///
    /// Vs1 is the model's own clean stall speed (<see cref="FlyingGame.Sim.Practice.PracticeScenario.EstimateVso"/> at flaps 0),
    /// taken at sea level so the answer is an INDICATED speed, which is what a manoeuvring speed is. Capped at Vne:
    /// a type whose derived Va runs past its own never-exceed speed cannot fly the case above Vne, and the cap is
    /// the honest reading. The table flags any that cap.
    /// </summary>
    public static double DerivedVaMs(AircraftConfig c)
    {
        double n = c.Limits?.GMax ?? 0.0;
        if (n <= 0.0) return 0.0;
        double vs1 = FlyingGame.Sim.Practice.PracticeScenario.EstimateVso(c, 0.0, 0.0);
        if (!(vs1 > 1.0)) return 0.0;
        double va = vs1 * Math.Sqrt(n);
        double vne = c.Limits?.VneMs ?? 0.0;
        return vne > 1.0 ? Math.Min(va, vne) : va;
    }

    /// <summary>Va for the type: the authored one where there is one, else the derived one.</summary>
    public static double VaFor(AircraftConfig c) => c.Limits is { VaMs: > 1.0 } l ? l.VaMs : DerivedVaMs(c);

    public sealed class FinRun
    {
        public double VaMs, TasMs, SingleN, SingleBetaDeg, SingleAtSec;
        public double ReversalN, ReversalBetaDeg, ReversalAtSec, OverswingBetaDeg, ReversedAtSec;
        public bool Converged;
    }

    /// <summary>
    /// FLY IT. Trimmed at Va, wings level, power off — a steady glide converges at exactly Va on every type and
    /// nothing in 23.441 is about thrust. Full rudder one way; then, at the overswing peak of the sideslip, full
    /// opposite rudder. The fin's own lateral force (<see cref="Aircraft.FinSideLoadN"/>) is sampled every step and
    /// the two peaks are reported separately, before and after the reversal is commanded.
    /// </summary>
    public static FinRun Fly(AircraftConfig c, double vaMs, int firstSign = 1)
    {
        double alt = ProbeAltitudeM;
        double rho = Atmosphere.DensityAtAltitude(alt);
        double tas = vaMs * Math.Sqrt(Atmosphere.SeaLevelDensityKgM3 / Math.Max(1e-6, rho));
        RigSolver.Result r = RigSolver.SolveGlide(c, tas, alt);
        Quat att = RigSolver.Attitude(r, 0.0);
        var vb = new Vec3(tas * Math.Cos(r.AlphaRad), 0, tas * Math.Sin(r.AlphaRad));
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -alt), att, vb, Vec3.Zero), new ControlDeflections(0, r.ElevatorRad, 0, 0));
        if (c.RetractableGear) ac.SetGear(false, immediate: true);
        double stick = Aircraft.StickForDeflection(r.ElevatorRad, c.Controls.Elevator);
        // ThrottleLever is INVERTED (+1 = closed): the case is flown power off.
        double lever = c.Propulsion is null ? 0.0 : 1.0;
        const double dt = SimLoop.DefaultFixedDtSec;

        // A second hands-off first, so the trim residual is not read as part of the manoeuvre.
        double t = 0;
        for (; t < 1.0; t += dt) ac.Step(new ControlInputs(0, stick, 0, lever), dt);

        // THE SIDESLIP IS MEASURED FROM WHERE THE AEROPLANE WAS, not from zero. A rigged type does not glide at
        // beta = 0 — the Great Lakes sits at a couple of degrees of its own — and reading the absolute angle made
        // the overswing detector fire on the FIRST STEP there, reversing before the rudder had moved and reporting
        // a 120 N "single deflection" against a 1,967 N reversal. The input's own excursion is what 23.441 means.
        RigidBodyState s0 = ac.State;
        double beta0 = Math.Asin(Math.Clamp(s0.Velocity.Y / Math.Max(1e-6, s0.Velocity.Length), -1, 1)) * 180.0 / Math.PI;

        var run = new FinRun { VaMs = vaMs, TasMs = tas, Converged = r.Converged };
        double betaPeak = 0, lastAbsBeta = 0;
        bool reversed = false;
        double tEnd = t + 8.0;
        for (; t < tEnd; t += dt)
        {
            int sign = reversed ? -firstSign : firstSign;
            ac.Step(new ControlInputs(0, stick, sign, lever), dt);
            RigidBodyState s = ac.State;
            double beta = Math.Asin(Math.Clamp(s.Velocity.Y / Math.Max(1e-6, s.Velocity.Length), -1, 1)) * 180.0 / Math.PI - beta0;
            double f = Math.Abs(ac.FinSideLoadN);
            if (!reversed)
            {
                if (f > run.SingleN) { run.SingleN = f; run.SingleBetaDeg = beta; run.SingleAtSec = t - 1.0; }
                double ab = Math.Abs(beta);
                if (ab > betaPeak) betaPeak = ab;
                // THE OVERSWING PEAK: the sideslip stops growing. That instant is when the reversal is worst,
                // because the slip angle and the opposite deflection then add on the same side of the fin.
                // Not before 0.3 s: the rudder is on an actuator and has not reached its stop before then, so an
                // earlier "peak" is the trim settling, not the manoeuvre.
                else if (ab < lastAbsBeta && betaPeak > 1.0 && t - 1.0 > 0.3) { reversed = true; run.ReversedAtSec = t - 1.0; }
                lastAbsBeta = ab;
                // A type whose yaw is so damped it never overswings still gets its reversal, at 3 s and settled.
                if (!reversed && t - 1.0 > 3.0) { reversed = true; run.ReversedAtSec = t - 1.0; }
            }
            else if (f > run.ReversalN) { run.ReversalN = f; run.ReversalBetaDeg = beta; run.ReversalAtSec = t - 1.0; }
            if (double.IsNaN(s.Velocity.Length) || -s.Position.Z < 50.0) break;
        }
        run.OverswingBetaDeg = betaPeak;
        return run;
    }

    public static IEnumerable<string> FleetIds()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "TestData");
        foreach (string f in Directory.GetFiles(dir, "*.json").OrderBy(x => x))
        {
            string id = Path.GetFileNameWithoutExtension(f);
            AircraftConfig c;
            try { c = Load(id); } catch { continue; }
            if (c.Surfaces.Count == 0 || !HasFin(c) || c.WeightShift is not null) continue;
            yield return id;
        }
    }

    /// <summary>THE DELIVERABLE: the fleet's two numbers, the gap between them, and the lines to author.</summary>
    [Fact]
    public void FleetFinLoads()
    {
        if (Environment.GetEnvironmentVariable("FINLOAD") != "1") return;
        using IDisposable _ = StillAir();
        _o.WriteLine("THE VERTICAL TAIL'S LIMIT LOAD, by Ty's rudder-reversal manoeuvre at Va (FAR 23.441).");
        _o.WriteLine("Va is DERIVED where it is not authored: Va = Vs1 * sqrt(gMax), capped at Vne.");
        _o.WriteLine("SINGLE = peak fin side force on one full deflection. REVERSAL = peak after the opposite stop");
        _o.WriteLine("at the sideslip's overswing peak. LIMIT = reversal * 1.15 (Ty). Ultimate = 2 x limit (Ty).");
        _o.WriteLine("ENDP = the same single deflection flown at 0.75 Va: TY'S FATIGUE ENDPOINT, 2026-11-11.");
        _o.WriteLine("q = endp/single, which pure dynamic-pressure scaling would make 0.5625.\n");
        _o.WriteLine($"{"type",-30} {"Va",6} {"Vs1",6} {"single N",9} {"revers N",9} {"gap",5} {"limit N",9} {"endp N",9} {"q",6} {"beta_os",7} {"rev@s",6}");
        var patch = new List<string>();
        var ratios = new List<double>();
        foreach (string id in FleetIds())
        {
            AircraftConfig c = Load(id);
            double va = VaFor(c);
            if (va <= 1.0) { _o.WriteLine($"{id,-30}   (no gMax or no stall solution: skipped)"); continue; }
            double vs1 = FlyingGame.Sim.Practice.PracticeScenario.EstimateVso(c, 0.0, 0.0);
            FinRun r, e75;
            try { r = Fly(c, va); e75 = FlyEndpoint(c, va); }
            catch (Exception e) { _o.WriteLine($"{id,-30}   FAILED: {e.GetType().Name}: {e.Message}"); continue; }
            double limit = r.ReversalN * LimitMargin;
            double gap = r.SingleN > 1.0 ? r.ReversalN / r.SingleN : double.NaN;
            double q = r.SingleN > 1.0 ? e75.SingleN / r.SingleN : double.NaN;
            if (!double.IsNaN(q)) ratios.Add(q);
            bool capped = (c.Limits?.VneMs ?? 0) > 1.0 && Math.Abs(va - c.Limits!.VneMs) < 1e-6 && c.Limits.VaMs <= 1.0;
            _o.WriteLine($"{id,-30} {va,6:F1} {vs1,6:F1} {r.SingleN,9:F0} {r.ReversalN,9:F0} {gap,5:F2} {limit,9:F0} {e75.SingleN,9:F0} {q,6:F4} {r.OverswingBetaDeg,7:F1} {r.ReversedAtSec,6:F2}"
                         + (c.Limits is { VaMs: > 1.0 } ? "  Va AUTHORED" : capped ? "  Va CAPPED AT VNE" : ""));
            patch.Add($"{id}\t{va:F4}\t{limit:F1}\t{r.SingleN:F1}\t{e75.SingleN:F1}\t{q:F4}");
        }
        _o.WriteLine("\n---- id, vaMs, finLimitLoadN, finOrdinaryLoadN, endpoint(0.75Va) N, q ----");
        foreach (string p in patch) _o.WriteLine(p);
        if (ratios.Count > 0)
        {
            ratios.Sort();
            double mean = ratios.Sum() / ratios.Count;
            _o.WriteLine($"\nq across {ratios.Count} types: min {ratios[0]:F4}  median {ratios[ratios.Count / 2]:F4}  "
                         + $"mean {mean:F4}  max {ratios[^1]:F4}   (V^2 says {EndpointDynamicPressureRatio:F4})");
            _o.WriteLine($"worst departure from {EndpointDynamicPressureRatio:F4}: "
                         + $"{Math.Max(Math.Abs(ratios[0] - EndpointDynamicPressureRatio), Math.Abs(ratios[^1] - EndpointDynamicPressureRatio)) / EndpointDynamicPressureRatio * 100.0:F1} %");
        }
    }

    /// <summary>
    /// THE CLAIM THE WHOLE CASE RESTS ON, as a test rather than a sentence: the reversal is worse than the first
    /// input. If it stops being true the manoeuvre has stopped being the design case, and every authored limit is
    /// then measuring the wrong thing.
    /// </summary>
    [Fact]
    public void TheReversalIsWorseThanTheFirstInput()
    {
        using IDisposable _ = StillAir();
        AircraftConfig c = Load("pitts-s2b-like");
        FinRun r = Fly(c, VaFor(c));
        Assert.True(r.SingleN > 100.0, $"single-deflection fin load {r.SingleN:F0} N is too small to be a measurement");
        Assert.True(r.ReversalN > r.SingleN,
            $"the reversal ({r.ReversalN:F0} N) must exceed the single deflection ({r.SingleN:F0} N) — that is why 23.441 is a reversal case");
    }

    /// <summary>
    /// THE JUSTIFICATION FOR DERIVING THE ENDPOINT INSTEAD OF AUTHORING IT, kept as a flown measurement rather than
    /// as a paragraph. Ty's endpoint is a full rudder deflection at 0.75 Va; the sim gets there by multiplying the
    /// measured Va load by <see cref="Flutter.FinEndpointOverOrdinary"/> = 0.5625, which is only legitimate if the
    /// fin's load at a fixed deflection really does go as dynamic pressure. So it is flown, on the two types Ty
    /// flies and on a slow one and a fast one, and the measured ratio is checked against 0.5625.
    ///
    /// THE FLEET RUN (2026-11-11, FINLOAD=1, all 33 measured types) gave 0.5503 .. 0.6082, mean 0.5697, median
    /// 0.5691: 28 inside 3 %, 31 inside 5 %. The band here is 10 % so it holds the claim without pinning the
    /// scatter of a transient peak, and the J-3 is deliberately included because it is the worst of the fleet at
    /// +8 % — the slowest Va, so its 0.75 Va is nearest the stall. If this test ever fails, the dynamics have
    /// stopped being speed-independent and the endpoint must go back to being authored per type.
    /// </summary>
    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("extra-300-like")]
    [InlineData("j3-cub-like")]
    [InlineData("p51d-like")]
    public void TheEndpointScalesWithDynamicPressure(string id)
    {
        using IDisposable _ = StillAir();
        AircraftConfig c = Load(id);
        double va = VaFor(c);
        double atVa = Fly(c, va).SingleN, at75 = FlyEndpoint(c, va).SingleN;
        Assert.True(atVa > 100.0, $"{id}: the Va single-deflection load {atVa:F0} N is too small to be a measurement");
        Assert.True(at75 < atVa, $"{id}: the same deflection at 0.75 Va ({at75:F0} N) must load the fin LESS than at Va ({atVa:F0} N)");
        double q = at75 / atVa;
        Assert.InRange(q, Flutter.FinEndpointOverOrdinary * 0.90, Flutter.FinEndpointOverOrdinary * 1.10);
    }

    /// <summary>Va derives to a speed an aeroplane could fly: above its own clean stall, at or below its Vne.</summary>
    [Fact]
    public void DerivedVaIsBetweenTheStallAndVne()
    {
        using IDisposable _ = StillAir();
        foreach (string id in new[] { "pitts-s2b-like", "extra-300-like", "j3-cub-like", "c172-like", "great-lakes-like" })
        {
            AircraftConfig c = Load(id);
            double vs1 = FlyingGame.Sim.Practice.PracticeScenario.EstimateVso(c, 0.0, 0.0), va = DerivedVaMs(c);
            Assert.True(va > vs1, $"{id}: Va {va:F1} must be above Vs1 {vs1:F1}");
            Assert.True(va <= (c.Limits?.VneMs ?? 1e9) + 1e-6, $"{id}: Va {va:F1} must not exceed Vne");
        }
    }

    /// <summary>The fin carries a REAL per-step load, not a flutter special case: full rudder at speed must show up
    /// on <see cref="Aircraft.FinSideLoadN"/> and in the sign the pedal implies.</summary>
    [Fact]
    public void TheFinLoadSignalIsRealAndSigned()
    {
        using IDisposable _ = StillAir();
        AircraftConfig c = Load("pitts-s2b-like");
        double va = VaFor(c);
        FinRun right = Fly(c, va, firstSign: 1);
        FinRun left = Fly(c, va, firstSign: -1);
        Assert.True(right.SingleN > 100.0 && left.SingleN > 100.0, "full rudder at Va must load the fin");
        // Mirror images to within the propeller-less glide's own asymmetry (rigging, a biplane's offsets).
        Assert.InRange(left.SingleN / right.SingleN, 0.75, 1.33);
    }
}
