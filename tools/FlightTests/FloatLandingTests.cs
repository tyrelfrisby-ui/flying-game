using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Float landings (Ty 2026-11-03: the J-3 on floats landed on the water and broke apart at once).
///
/// THE CAUSE. FloatHydro's forebody planing lift is Savitsky's, with τ the trim of the bottom; it took τ from the
/// straight keel line just ahead of the step (0.57°) and gave NO lift at τ ≤ 0.1°, however far the water had climbed
/// up the swept bow. A touchdown flat or a little nose-low at speed therefore had nothing but buoyancy at the bow: the
/// floats knifed under (step draft 0.61 m on a 0.50 m deep float), the aeroplane pitched to −10°, the engine went into
/// the water at 32 m/s and the 9 g engine-mount check (AirframeFlooding.CheckJoints) correctly tore the nose off —
/// "Nose broken off in the water: 12.4 kN along the fuselage against 12.1" (32 m/s, 2 m/s sink, −2° pitch, deep sea).
/// The joint rule, the hard points and the crash relay were all doing their jobs on a wrong input. The planing τ is
/// now the wetted bottom's below zero: it moves to the chord to where the keel meets the water over the first
/// degree of negative trim (every positive-trim state, the validated ones, unchanged).
///
/// THE THRESHOLD. A float's ultimate water load, 14 CFR 23.527 (Aircraft.FloatUltimateWaterLoadN): for the J-3 floats
/// n_w = 4.12 (V_S0 38 kt, β 18.8°, half of 1,301 lb per float), 17.9 kN ultimate per float. Measured (26 m/s, +5°,
/// idle): 3.0 m/s sink (590 fpm) peaks at 15 kN — no damage; 3.5 m/s (690 fpm) splits both floats' bottoms. The design
/// descent velocity of 14 CFR 23.473 for this wing loading is 2.2 m/s (433 fpm).
/// </summary>
[Collection("WorldTerrainActive")]
public class FloatLandingTests
{
    private readonly ITestOutputHelper _out;
    public FloatLandingTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private static double Pitch(RigidBodyState s) { Quat q = s.Attitude; return Math.Asin(Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1)) * 57.3; }

    private sealed class Landing
    {
        public Aircraft Ac = null!;
        public double MaxRoll, StepSeconds, TouchAt = -1, DisplacementAt = -1, PeakNz, MaxPitch = -99, MinPitch = 99;
        public bool Airborne;
        public string Summary => $"touch {TouchAt:F1}s, on the step {StepSeconds:F1}s, displacement from {DisplacementAt:F1}s, " +
            $"pitch {MinPitch:F1}..{MaxPitch:F1}, |roll| {MaxRoll:F1}, nz {PeakNz:F2}, float peak {Ac.FloatPeakWaterLoadN[0] / 1000:F1}/{Ac.FloatPeakWaterLoadN[1] / 1000:F1} kN " +
            $"(ult {Ac.FloatUltimateWaterLoadN / 1000:F1}); lost [{string.Join(",", Ac.LostComponents)}] dmg [{string.Join("; ", Ac.Hydro.DamageLog)}]";
    }

    /// <summary>Touch down at <paramref name="v"/> m/s, sinking at <paramref name="sink"/>, at <paramref name="pitchDeg"/>,
    /// power idle, a little back stick held and the wings kept level, on flat water at 0; fly <paramref name="secs"/> s.</summary>
    private static Landing Land(AircraftConfig cfg, double v, double sink, double pitchDeg, double secs)
    {
        double th = pitchDeg / 57.2958, gam = Math.Asin(sink / v);
        var q = new Quat(0, Math.Sin(th / 2), 0, Math.Cos(th / 2));
        // Start with the lowest point of the floats (at this attitude) 5 cm above the water.
        double z0 = cfg.Floats!.KeelZ + 0.05;
        RigidBodyState At(double z) => new(new Vec3(0, 0, -z), q, q.Conjugate().Rotate(new Vec3(v * Math.Cos(gam), 0, v * Math.Sin(gam))), Vec3.Zero);
        for (int i = 0; i < 100; i++) { FloatHydro.Compute(cfg, At(z0 - 0.05), 0); if (!FloatHydro.Floats[0].Wet && !FloatHydro.Floats[1].Wet) break; z0 += 0.02; }
        var state = At(z0);
        var l = new Landing { Ac = new Aircraft(cfg, state, new ControlDeflections(0, 0, 0, 0, 1.0)) };
        var sim = new SimLoop(l.Ac);
        for (double t = 0; t < secs; t += 0.01)
        {
            // The pilot keeps the wings level (a float landing on one float is a different, harder landing).
            Quat a = l.Ac.State.Attitude;
            double roll = Math.Atan2(2 * (a.W * a.X + a.Y * a.Z), 1 - 2 * (a.X * a.X + a.Y * a.Y)) * 57.3;
            double ail = Math.Clamp(-0.08 * roll - 0.03 * l.Ac.State.Rates.X * 57.3, -1, 1);
            sim.RunFor(0.01, new ControlInputs(ail, -0.3, 0, 1.0));
            if (l.TouchAt >= 0) l.MaxRoll = Math.Max(l.MaxRoll, Math.Abs(roll));
            RigidBodyState s = l.Ac.State;
            FloatHydro.Report rep = FloatHydro.LastReport;
            bool wet = rep.DraftAtStepM > 0 || rep.BuoyancyN > 0;
            if (l.TouchAt < 0 && wet) l.TouchAt = t;
            if (l.TouchAt < 0) continue;
            l.PeakNz = Math.Max(l.PeakNz, l.Ac.LoadFactorZ);
            l.MaxPitch = Math.Max(l.MaxPitch, Pitch(s)); l.MinPitch = Math.Min(l.MinPitch, Pitch(s));
            double speed = s.Velocity.Length;
            if (wet && rep.VentilatedFraction > 0.5 && rep.PlaningLiftN > rep.BuoyancyN) l.StepSeconds += 0.01;
            if (l.DisplacementAt < 0 && l.StepSeconds > 0 && speed < 4.0 && rep.PlaningLiftN < rep.BuoyancyN) l.DisplacementAt = t;
            if (t > l.TouchAt + 3 && !wet && -s.Position.Z > cfg.Floats.KeelZ + 2.0) l.Airborne = true;
        }
        return l;
    }

    /// <summary>
    /// The speed a type is PUT ON THE WATER at: the fastest power-off glide at which it has already reached its
    /// landing angle of attack. A float landing is flown by attitude — the aeroplane is held off until the floats are
    /// at the angle the step wants and it settles on — so the touchdown speed is a consequence of that attitude and of
    /// the type's own wing, not a number you can write down once.
    ///
    /// IT USED TO BE A LITERAL AND THE LITERAL ROTTED. ANormalJ3FloatLandingGoesOnTheStepThenOffItUndamaged touched
    /// down at a hard-coded 26.8 m/s (60 mph), which was 1.37 Vso when it was written — a fast but perfectly normal
    /// arrival. On 2026-11-11 the J-3's authored mass went 589.67 → 451.142 kg (its empty weight corrected to Piper
    /// Service Memo 41 plus the float exchange), Vso went 19.55 → 17.10 m/s with it, and the same 26.8 became
    /// 1.57 Vso. MEASURED on the sweep that followed: at 1.57 Vso the aeroplane will not stay on the water at all —
    /// it touches, flies straight off again on a wing that is still making lift, zooms to 17 m, stalls, and arrives
    /// back nose-down 19° at 20 m/s for 8 g and a propeller in the lake. The float was never the problem; nothing it
    /// did was wrong. 1.11/1.17/1.23/1.29 Vso all touch, run 9–12 s on the step, come off into displacement and settle
    /// at 0.256 m with nz ≤ 3.0 and no damage; 1.37 balloons to 7 m; 1.40 loses the propeller.
    ///
    /// EveryFloatTypeLandsNormallyWithoutDamage already derived its touchdown speed this way and so never rotted. This
    /// is that construction, shared. For the J-3 today it gives 21.0 m/s — 47 mph, 1.23 Vso.
    /// </summary>
    private static (double V, double AlphaDeg) TouchdownSpeed(AircraftConfig cfg, double landingAlphaDeg = 7.0)
    {
        double v = 60, alpha = 0;
        for (double vv = 60; vv > 12; vv -= 0.5)
        {
            TrimSolver.Result t = TrimSolver.SolveGliderTrim(cfg, vv, 10, flapFraction: 0.0, idleProp: true);
            if (!t.Converged) continue;
            v = vv; alpha = t.AlphaRad * 57.3;
            if (alpha >= landingAlphaDeg) break;
        }
        return (v, alpha);
    }

    /// <summary>A normal float landing in the J-3: on the step at its own landing attitude (<see cref="TouchdownSpeed"/>:
    /// 47 mph today), 100–300 fpm, slightly nose-up, power idle. No damage of any
    /// kind; it runs on the step, then comes off it into displacement as it slows, and ends afloat at its resting draft.</summary>
    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void ANormalJ3FloatLandingGoesOnTheStepThenOffItUndamaged(double sinkMs)
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig cfg = Load("j3-cub-floats-like");
            (double v, _) = TouchdownSpeed(cfg);
            double vso = FlyingGame.Sim.Practice.PracticeScenario.EstimateVso(cfg, 0.0);
            // Guard the derivation itself: a float landing is flown between the stall and about 1.3 Vso. If this ever
            // walks outside that band again the scenario is wrong before any float force is.
            Assert.InRange(v / vso, 1.05, 1.30);
            Landing l = Land(cfg, v, sinkMs, 5.0, 40.0);
            _out.WriteLine($"{sinkMs * 196.85:F0} fpm at {v:F1} m/s ({v / 0.44704:F0} mph, {v / vso:F2} Vso): " + l.Summary);
            Assert.True(l.TouchAt >= 0, "never touched the water");
            Assert.Empty(l.Ac.LostComponents);
            Assert.Empty(l.Ac.Hydro.DamageLog);
            Assert.False(l.Ac.Structure.WingsFailed);
            Assert.True(l.StepSeconds > 1.0, $"on the step only {l.StepSeconds:F1} s");
            Assert.True(l.DisplacementAt > 0, "never came off the step into displacement");
            Assert.True(l.MinPitch > -5, $"pitched to {l.MinPitch:F1}");
            Assert.InRange(FloatHydro.LastReport.DraftAtStepM, 0.18, 0.35);
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    /// <summary>The landing that broke it: 32 m/s (72 mph), 2 m/s sink, 2° nose-down — fast and flat. Before, the floats
    /// went under and the nose came off ("12.4 kN along the fuselage against 12.1"). Now the bow planes: nothing breaks.</summary>
    [Fact]
    public void AFastFlatTouchdownPlanesInsteadOfDivingUnder()
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig cfg = Load("j3-cub-floats-like");
            Landing l = Land(cfg, 32.0, 2.0, -2.0, 6.0);
            _out.WriteLine(l.Summary);
            Assert.Empty(l.Ac.LostComponents);
            Assert.DoesNotContain(l.Ac.Hydro.DamageLog, d => d.Contains("Nose"));
            Assert.True(l.MinPitch > -8, $"pitched down to {l.MinPitch:F1}: the bow dug in (it went to −10 and on)");
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    /// <summary>The hard-landing threshold (14 CFR 23.527 ultimate water load, see the class): at the design descent
    /// velocity of 23.473 (2.2 m/s) nothing; at 5 m/s (~1,000 fpm, more than twice it) the float bottoms split — damage,
    /// and the aeroplane is still in one piece.</summary>
    [Fact]
    public void AHardLandingSurvivesTheDesignSinkAndSplitsAFloatWellBeyondIt()
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig cfg = Load("j3-cub-floats-like");
            Landing design = Land(cfg, 26.0, 2.2, 5.0, 6.0);
            Landing hard = Land(cfg, 26.0, 5.0, 5.0, 6.0);
            _out.WriteLine($"n_w {design.Ac.FloatLimitWaterLoadFactor:F2}, ultimate {design.Ac.FloatUltimateWaterLoadN / 1000:F1} kN per float");
            _out.WriteLine("2.2 m/s: " + design.Summary);
            _out.WriteLine("5.0 m/s: " + hard.Summary);
            Assert.InRange(design.Ac.FloatLimitWaterLoadFactor, 3.5, 4.7);
            Assert.Empty(design.Ac.Hydro.DamageLog);
            Assert.Empty(design.Ac.LostComponents);
            Assert.True(design.Ac.FloatPeakWaterLoadN.Max() < design.Ac.FloatUltimateWaterLoadN);
            Assert.Contains(hard.Ac.Hydro.DamageLog, d => d.Contains("bottom split"));
            Assert.DoesNotContain(AirframeComponent.Nose, hard.Ac.LostComponents);
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    /// <summary>
    /// BOTH SIDES OF 23.527 ARE AT THE DESIGN GROSS — the W and the V_S0 (Aircraft.DesignGrossVsoMs). A float is
    /// certificated once; it does not get weaker because the aeroplane is light today, and that is as true of the
    /// stall speed in n_w as it is of the weight beside it.
    ///
    /// The regression this pins: on 2026-11-11 the J-3's authored mass went 589.67 → 451.142 kg (the aeroplane as
    /// flown, not its placard). The W in n_w was moved to the placard the same day; the V_S0 was not, and went on
    /// reading PracticeScenario.EstimateVso, which trims at the as-flown mass — 33.24 kt where the certificated
    /// 1,300 lb stall is 38.0. n_w goes as V_S0², so the float came out 3.139 / 13.62 kN against the 4.12 / 17.9 kN
    /// this class derives by hand from A-691, 24 % weak, and split both bottoms on a 98 fpm touchdown.
    ///
    /// So the measurement is: loading the SAME type lighter must not change its float's strength at all.
    /// </summary>
    [Fact]
    public void AFloatsUltimateLoadIsItsCertificatesAndDoesNotFollowTodaysWeight()
    {
        AircraftConfig heavy = Load("j3-cub-floats-like");
        var atRest = new RigidBodyState(new Vec3(0, 0, -(heavy.Floats!.KeelZ - 0.25)), new Quat(0, 0, 0, 1), Vec3.Zero, Vec3.Zero);
        double designKg = heavy.Loading!.MaxTakeoffMassKg;
        var full = new Aircraft(heavy, atRest, ControlDeflections.Neutral);

        // The same aeroplane flown 100 kg lighter still (one pilot out, say). Nothing structural may move.
        AircraftConfig light = Load("j3-cub-floats-like");
        light.Mass.MassKg -= 100.0;
        var lean = new Aircraft(light, atRest, ControlDeflections.Neutral);

        double spread = Math.Abs(lean.FloatUltimateWaterLoadN - full.FloatUltimateWaterLoadN) / full.FloatUltimateWaterLoadN;
        _out.WriteLine($"design gross {designKg:F2} kg; as flown {heavy.Mass.MassKg:F3} and {light.Mass.MassKg:F3} kg -> "
                       + $"n_w {full.FloatLimitWaterLoadFactor:F4} / {lean.FloatLimitWaterLoadFactor:F4}, "
                       + $"ultimate {full.FloatUltimateWaterLoadN / 1000:F3} / {lean.FloatUltimateWaterLoadN / 1000:F3} kN per float ({spread:P2} apart)");
        // Not bit-equal, and the residue is NOT a weight dependence: PracticeScenario.EstimateVso walks a 0.5 m/s
        // grid and returns a grid point, so the two trims land on neighbouring rungs (17.1 and 15.1 m/s) before
        // being scaled to the design gross, and 0.5 m/s at this speed is 3 % of a stall that n_w squares. What is
        // left after the scaling is 0.1 %. Before the scaling existed the gap was the whole 23.5 % of the mass.
        Assert.True(spread < 0.02, $"the float's strength moved {spread:P2} when only today's loading changed");

        // And the figure itself is the certificate's, not the as-flown one: 23.527 closed form at the design gross,
        // C1 0.012, W half of 1,300 lb per float, β 18.8°, with the type's OWN sim stall scaled to that weight.
        double vKt = FlyingGame.Sim.Practice.PracticeScenario.EstimateVso(heavy, 0.0)
                     * Math.Sqrt(designKg / heavy.Mass.MassKg) * 1.943844;
        double wLb = designKg * 2.20462 / 2.0;
        double nw = 0.012 * vKt * vKt / (Math.Pow(Math.Tan(18.8 * Math.PI / 180), 2.0 / 3.0) * Math.Pow(wLb, 1.0 / 3.0));
        _out.WriteLine($"by hand: V_S0 at the design gross {vKt:F2} kt, W {wLb:F1} lb/float -> n_w {nw:F3}, ultimate {1.5 * nw * designKg * 9.81 / 2 / 1000:F2} kN");
        Assert.Equal(nw, full.FloatLimitWaterLoadFactor, 6);
        // The hand figure in this class's own summary: V_S0 38 kt, n_w 4.12, 17.9 kN ultimate per float.
        Assert.InRange(vKt, 37.0, 39.0);
        Assert.InRange(full.FloatUltimateWaterLoadN / 1000.0, 17.0, 18.5);
    }

    /// <summary>A step taxi at 30–40 mph: from rest, full power until 35 mph, then power to hold it at a 7° attitude.
    /// It stays on the step — no porpoising worth the name, never airborne, nothing damaged.</summary>
    [Fact]
    public void AStepTaxiAt35MphIsStable()
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig cfg = Load("j3-cub-floats-like");
            var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -(cfg.Floats!.KeelZ - 0.25)), new Quat(0, 0, 0, 1), Vec3.Zero, Vec3.Zero), ControlDeflections.Neutral);
            var sim = new SimLoop(ac);
            sim.RunFor(5.0, new ControlInputs(0, 0, 0, 1.0));
            const double target = 35 * 0.44704;
            double thr = 1.0, onStep = 0, total = 0, minP = 99, maxP = -99, minDraft = 99;
            bool reached = false;
            for (double t = 0; t < 60 && total < 20; t += 0.01)
            {
                double v = ac.State.Velocity.Length;
                if (!reached && v >= target) reached = true;
                if (reached) thr = Math.Clamp(0.45 + 0.15 * (target - v), 0.0, 1.0);
                // Held at a 7° attitude, as a step taxi is flown: below about 4° this float porpoises at these speeds
                // (the validated lower-limit porpoise, HydroJ3Tests.PorpoisesAtTooLowATrim…).
                Quat a = ac.State.Attitude; double pNow = Math.Asin(Math.Clamp(2 * (a.W * a.Y - a.Z * a.X), -1, 1)) * 57.3;
                double st = reached ? Math.Clamp(-0.12 * (7.0 - pNow) + 0.03 * ac.State.Rates.Y * 57.3, -1, 1) : -0.15;
                sim.RunFor(0.01, new ControlInputs(0, st, 0, 1.0 - 2.0 * thr));
                if (!reached) continue;
                total += 0.01;
                FloatHydro.Report rep = FloatHydro.LastReport;
                if (rep.VentilatedFraction > 0.5 && rep.PlaningLiftN > rep.BuoyancyN) onStep += 0.01;
                if (total > 3) { double p = Pitch(ac.State); minP = Math.Min(minP, p); maxP = Math.Max(maxP, p); minDraft = Math.Min(minDraft, rep.DraftAtStepM); }
            }
            _out.WriteLine($"reached {target:F1} m/s: {reached}; on the step {onStep:F1} of {total:F1} s; pitch {minP:F1}..{maxP:F1} after the first 3 s; " +
                           $"least step draft {minDraft:F3} m; V now {ac.State.Velocity.Length:F1}; dmg [{string.Join("; ", ac.Hydro.DamageLog)}]");
            Assert.True(reached, "never got up to step-taxi speed");
            Assert.True(onStep > 0.8 * total, $"on the step only {onStep:F1} of {total:F1} s");
            Assert.True(maxP - minP < 6.0, $"porpoising: pitch {minP:F1}..{maxP:F1}");
            Assert.True(minDraft > 0, "left the water");
            Assert.Empty(ac.LostComponents);
            Assert.Empty(ac.Hydro.DamageLog);
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    public static IEnumerable<object[]> FloatTypes()
    {
        foreach (string f in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData"), "*-like.json").OrderBy(x => x))
        {
            AircraftConfig c;
            try { c = AircraftConfigLoader.LoadFromFile(f); } catch { continue; }
            if (c.Floats != null) yield return new object[] { Path.GetFileNameWithoutExtension(f) };
        }
    }

    /// <summary>Every float type: a normal landing, slow and nose-up, 200 fpm, idle — nothing breaks or splits.</summary>
    [Theory]
    [MemberData(nameof(FloatTypes))]
    public void EveryFloatTypeLandsNormallyWithoutDamage(string id)
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig cfg = Load(id);
            // Touchdown speed: the fastest power-off glide at which the type is already at 7° angle of attack — slow,
            // nose up, the way a float landing is flown — and that attitude. (TouchdownSpeed: the same derivation the
            // J-3 landing above now uses, because a written-down speed goes stale the moment a weight moves.)
            (double v, double alpha) = TouchdownSpeed(cfg);
            Landing l = Land(cfg, v, 1.0, Math.Min(alpha, 8), 25.0);
            _out.WriteLine($"{id}: touchdown {v:F1} m/s at {Math.Min(alpha, 8):F1} deg; " + l.Summary);
            Assert.True(l.TouchAt >= 0, "never touched the water");
            Assert.Empty(l.Ac.LostComponents);
            Assert.Empty(l.Ac.Hydro.DamageLog);
            Assert.False(l.Ac.Structure.WingsFailed);
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }
}
