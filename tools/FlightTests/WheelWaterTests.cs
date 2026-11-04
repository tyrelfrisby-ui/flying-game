using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Wheels on water (Ty 2026-11-03): the J-3 on wheels flipped at once skimming a pond, and a gentle tyre touch in the
/// Extra stopped it dead. WheelHydro is now a finite, depth-dependent planing contact (see its header); these fly it.
///
/// BEFORE (the old model, support = static load·(V/V_p)² whatever the depth, plough drag ½ρV²·0.9 on the immersed
/// frontal area, tyre bottom a radius below the gear point), from the same light touch (0.5 m/s sink, trim elevator,
/// idle, 3 m of water): J-3 at 0.9 V_p (14.5 m/s) 14.5 → 1.8 m/s in one second, peak 4.3 g, nose over to −43° and a
/// prop strike; at 1.1 V_p (17.8 m/s) peak 5.1 g and over; the Extra at 0.9 V_p 5.6 g; the Cassutt at flying speed
/// (34.5 m/s) a prop strike, 13.9 g and the tail off. AFTER: the numbers each test prints.
/// </summary>
[Collection("WorldTerrainActive")]
public class WheelWaterTests
{
    private readonly ITestOutputHelper _out;
    public WheelWaterTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    public static IEnumerable<object[]> Wheeled()
    {
        foreach (string f in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData"), "*-like.json").OrderBy(x => x))
        {
            AircraftConfig c;
            try { c = AircraftConfigLoader.LoadFromFile(f); } catch { continue; }
            // Wheels only: not floats, not the hang glider's or paraglider's feet.
            if (c.Floats != null || c.Gear.Count == 0 || c.WeightShift != null || (c.Id ?? "").Contains("paraglider")) continue;
            if (c.Gear.Any(g => g.GearType == "float-keel" || g.GearType == "skid")) continue;
            yield return new object[] { Path.GetFileNameWithoutExtension(f) };
        }
    }

    /// <summary>A light touch at skimming speed — 1.15 × the slowest glide the type will trim at, 0.5 m/s sink, idle,
    /// trim elevator, deep water — for every wheeled type: it skims or skips, the deceleration is finite and modest
    /// (peak under 1 g over the first 2.5 s), and nothing breaks. The 737 is the one exception, checked on its own below.</summary>
    [Theory]
    [MemberData(nameof(Wheeled))]
    public void ALightTouchAtSpeedSkimsAndBreaksNothing(string id)
    {
        FloatHydro.FlatWaterOverride = 3.0;
        try
        {
            AircraftConfig cfg = Load(id);
            var r = WheelWaterTouch.Run(cfg, 0.5, 2.5);
            _out.WriteLine(r.ToString());
            Assert.True(r.TouchAt > 0, "the wheels never reached the water");
            if (id == "boeing-737-like")
            {
                // 200 kt: the tyres plane (V_p 129 kt), but a light touch still pitches the nose down onto the water,
                // and the nose belly meeting water at 100 m/s bursts (WaterSlam) — a real load failure. What must hold
                // is that it is not an instant stop.
                Assert.True(r.PeakDecelG < 2.0, $"peak {r.PeakDecelG:F2} g");
                Assert.True(r.SpeedAt1s > 0.95 * r.V, $"V {r.V:F1} -> {r.SpeedAt1s:F1} m/s in 1 s");
                return;
            }
            Assert.True(r.PeakDecelG < 1.0, $"peak deceleration {r.PeakDecelG:F2} g");
            Assert.True(r.SpeedAt1s > 0.9 * r.V, $"V {r.V:F1} -> {r.SpeedAt1s:F1} m/s in 1 s");
            Assert.True(r.Lost.Count == 0, "lost " + string.Join(",", r.Lost));
            Assert.Empty(r.Damage);
            Assert.False(r.WingsFailed);
            Assert.True(r.MinPitchDeg > -10, $"pitched to {r.MinPitchDeg:F1}");
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    /// <summary>The old instant stop, on the same J-3 touch just below V_p (14.5 m/s): before, 14.5 → 1.8 m/s in a
    /// second at 4.3 g. Now the water has to slow it at a finite rate.</summary>
    [Fact]
    public void JustBelowThePlaningSpeedATouchIsNotAnInstantStop()
    {
        FloatHydro.FlatWaterOverride = 3.0;
        try
        {
            AircraftConfig cfg = Load("j3-cub-like");
            GearConfig main = cfg.Gear.First(g => !g.IsTailwheel && Math.Abs(g.Pos[1]) > 0.3);
            double vp = WheelHydro.HydroplaneSpeedMs(WheelHydro.TirePressurePsi(cfg, main));
            var r = WheelWaterTouch.Run(cfg, 0.5, 1.0, speed: 0.9 * vp, holdPitchDeg: 8);
            _out.WriteLine($"V_p {vp:F1} m/s; " + r);
            Assert.True(r.SpeedAt1s > 0.6 * r.V, $"{r.V:F1} -> {r.SpeedAt1s:F1} m/s in 1 s");
            Assert.True(r.PeakDecelG < 2.0, $"peak {r.PeakDecelG:F2} g");
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    /// <summary>Ty's skim: a J-3 on its standard tyres at 42 kt, tail low (8° attitude held), with power to hold the
    /// speed — upright for 15 s on the water, with little deceleration.</summary>
    [Fact]
    public void AJ3SkimsAt42KnotsWithPowerAndTheTailLow()
    {
        FloatHydro.FlatWaterOverride = 3.0;
        try
        {
            AircraftConfig cfg = Load("j3-cub-like");
            var r = WheelWaterTouch.Run(cfg, 0.5, 15.0, speed: 42 * 0.514444, throttle01: 0.4, holdPitchDeg: 8);
            _out.WriteLine(r.ToString());
            Assert.True(r.NoseOverAt < 0, $"nosed over at {r.NoseOverAt:F1} s");
            Assert.True(r.TouchedFraction > 0.8, $"on the water {r.TouchedFraction:P0} of the time");
            Assert.True(r.PeakDecelG < 0.5, $"peak {r.PeakDecelG:F2} g");
            Assert.True(r.Lost.Count == 0 && r.Damage.Count == 0);
            Assert.True(r.MinPitchDeg > 0, $"pitch went to {r.MinPitchDeg:F1}");
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    /// <summary>Power off at 42 kt: it slows, goes below V_p and the tyres' dig-in speed, digs in and goes over — and
    /// holding the tail down (10° rather than 2°) delays the nose-over but cannot prevent it.</summary>
    [Fact]
    public void SlowingBelowThePlaningSpeedDigsInAndTheTailDownOnlyDelaysIt()
    {
        FloatHydro.FlatWaterOverride = 3.0;
        try
        {
            AircraftConfig cfg = Load("j3-cub-like");
            GearConfig main = cfg.Gear.First(g => !g.IsTailwheel && Math.Abs(g.Pos[1]) > 0.3);
            double vp = WheelHydro.HydroplaneSpeedMs(WheelHydro.TirePressurePsi(cfg, main));
            var flat = WheelWaterTouch.Run(cfg, 0.5, 15.0, speed: 42 * 0.514444, holdPitchDeg: 2);
            var tailDown = WheelWaterTouch.Run(cfg, 0.5, 15.0, speed: 42 * 0.514444, holdPitchDeg: 10);
            _out.WriteLine($"V_p {vp:F1} m/s, dig-in (full weight on the mains) {WheelHydro.DigInSpeedMs(cfg, main, cfg.Mass.MassKg * 9.81 / 2):F1} m/s");
            _out.WriteLine("2 deg:  " + flat);
            _out.WriteLine("10 deg: " + tailDown);
            Assert.True(flat.NoseOverAt > 2.0, $"2 deg: over at {flat.NoseOverAt:F2} s — too soon or never");
            Assert.True(tailDown.NoseOverAt > 0, "the tail held down cannot save it once well below V_p");
            Assert.True(flat.SpeedAtNoseOver < vp && tailDown.SpeedAtNoseOver < vp, "it goes over below the planing speed");
            Assert.True(tailDown.NoseOverAt > flat.NoseOverAt, "holding the tail down delays the nose-over");
            Assert.True(tailDown.SpeedAtNoseOver < flat.SpeedAtNoseOver, "and lets it slow further first");
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }
}

/// <summary>A light wheel touch on deep water: level-ish glide trim at a skimming speed, wheels 3 cm above the
/// surface, sinking at the given rate. Shared by the probe and the tests.</summary>
public static class WheelWaterTouch
{
    public sealed class Result
    {
        public string Id = "";
        public double NoseOverAt = -1, MeanDecelG, TouchedFraction, SpeedAtNoseOver; public double V, PeakDecelG, PeakPitchRateDps, MinSpeed, SpeedAt1s, MaxPitchDeg, MinPitchDeg, TouchAt = -1, PeakNz;
        public bool Converged;
        public List<string> Events = new();
        public List<AirframeComponent> Lost = new();
        public List<string> Damage = new();
        public bool WingsFailed;
        public override string ToString() =>
            $"{Id}: noseover {NoseOverAt:F2}s at {SpeedAtNoseOver:F1} m/s, touching {TouchedFraction:P0}; V {V:F1} m/s trim {Converged} touch {TouchAt:F2}s; peak decel {PeakDecelG:F2} g, nz {PeakNz:F2}, peak |q| {PeakPitchRateDps:F0} deg/s, pitch {MinPitchDeg:F1}..{MaxPitchDeg:F1}, V@1s {SpeedAt1s:F1}, Vmin {MinSpeed:F1}; lost [{string.Join(",", Lost)}] dmg [{string.Join("; ", Damage)}] wings {WingsFailed}";
    }

    /// <summary>The lowest speed from a ladder at which the glide trim converges at a sane angle of attack, times 1.15.</summary>
    public static (double v, TrimSolver.Result t) SkimSpeed(AircraftConfig cfg)
    {
        foreach (double v0 in new[] { 18.0, 20, 22, 24, 26, 28, 30, 33, 36, 40, 45, 50, 55, 60, 70, 80, 90, 100, 110 })
        {
            TrimSolver.Result t = TrimSolver.SolveGliderTrim(cfg, v0, 10, flapFraction: 0.0, idleProp: true);
            if (t.Converged && t.AlphaRad < 10 / 57.3 && t.AlphaRad > -2 / 57.3)
            {
                double v = v0 * 1.15;
                TrimSolver.Result t2 = TrimSolver.SolveGliderTrim(cfg, v, 10, flapFraction: 0.0, idleProp: true);
                return t2.Converged ? (v, t2) : (v0, t);
            }
        }
        return (40, TrimSolver.SolveGliderTrim(cfg, 40, 10, flapFraction: 0.0, idleProp: true));
    }

    public static Result Run(AircraftConfig cfg, double sinkMs, double secs, double? speed = null, double stick = double.NaN, double throttle01 = 0, Action<string>? log = null, double holdPitchDeg = double.NaN)
    {
        var res = new Result { Id = cfg.Id ?? "" };
        (double v, TrimSolver.Result trim) = SkimSpeed(cfg);
        if (speed.HasValue) { v = speed.Value; trim = TrimSolver.SolveGliderTrim(cfg, v, 10, flapFraction: 0.0, idleProp: true); }
        res.V = v; res.Converged = trim.Converged;
        double alpha = trim.Converged ? trim.AlphaRad : 4 / 57.3;
        double gam = Math.Asin(sinkMs / v);
        double th = alpha - gam;
        var q = new Quat(0, Math.Sin(th / 2), 0, Math.Cos(th / 2));
        Vec3 cg = cfg.Mass.CgVec();
        // Lowest point of any tyre, below the CG at this attitude.
        double low = double.NegativeInfinity;
        foreach (GearConfig g in cfg.Gear)
        {
            double z = q.Rotate(g.PosVec() - cg).Z;   // the gear point is the bottom of the tyre (the ground convention)
            low = Math.Max(low, z);
        }
        double water = FloatHydro.WaterSurfaceAt(0, 0) ?? 0;
        double z0 = water + low + 0.03;
        Vec3 vWorld = new(v * Math.Cos(gam), 0, v * Math.Sin(gam));
        var state = new RigidBodyState(new Vec3(0, 0, -z0), q, q.Conjugate().Rotate(vWorld), Vec3.Zero);
        double elev = trim.Converged ? trim.ElevatorRad : 0;
        var ac = new Aircraft(cfg, state, new ControlDeflections(0, elev, 0, 0, 1.0));
        double st = double.IsNaN(stick) ? Aircraft.StickForDeflection(elev, cfg.Controls.Elevator) : stick;
        var sim = new SimLoop(ac);
        ac.HardImpact += (c, p) => res.Events.Add($"t={Atmosphere.SimTimeSec:F2} impact {p} {c:F2} m/s water={ac.LastImpact.OnWater}");
        ac.ComponentLost += c => res.Events.Add($"t={Atmosphere.SimTimeSec:F2} LOST {c}");
        double t0 = Atmosphere.SimTimeSec;
        res.MinSpeed = v; res.MaxPitchDeg = -999; res.MinPitchDeg = 999;
        Vec3 vPrev = vWorld;
        const double win = 0.05;
        int touched = 0, windows = 0;
        for (double t = 0; t < secs; t += win)
        {
            if (!double.IsNaN(holdPitchDeg))
            {
                // A pilot holding an attitude: aft stick (negative) to raise the nose, damped by the pitch rate.
                Quat a0 = ac.State.Attitude; double p0 = Math.Asin(Math.Clamp(2 * (a0.W * a0.Y - a0.Z * a0.X), -1, 1)) * 57.3;
                st = Math.Clamp(-0.12 * (holdPitchDeg - p0) + 0.03 * ac.State.Rates.Y * 57.3, -1.0, 1.0);
            }
            sim.RunFor(win, new ControlInputs(0, st, 0, 1.0 - 2.0 * throttle01));
            RigidBodyState s = ac.State;
            Vec3 vw = s.Attitude.Rotate(s.Velocity);
            Vec3 hNow = new(vw.X, vw.Y, 0), hPrev = new(vPrev.X, vPrev.Y, 0);
            double decel = (hPrev.Length - hNow.Length) / win / 9.81;
            res.PeakDecelG = Math.Max(res.PeakDecelG, decel);
            res.PeakNz = Math.Max(res.PeakNz, ac.LoadFactorZ);
            res.PeakPitchRateDps = Math.Max(res.PeakPitchRateDps, Math.Abs(s.Rates.Y) * 57.3);
            res.MinSpeed = Math.Min(res.MinSpeed, vw.Length);
            if (Math.Abs(t + win - 1.0) < 1e-6) res.SpeedAt1s = vw.Length;
            Quat a = s.Attitude; double pitch = Math.Asin(Math.Clamp(2 * (a.W * a.Y - a.Z * a.X), -1, 1)) * 57.3;
            res.MaxPitchDeg = Math.Max(res.MaxPitchDeg, pitch); res.MinPitchDeg = Math.Min(res.MinPitchDeg, pitch);
            if (res.TouchAt < 0 && ac.AnyGearTouching) res.TouchAt = t + win;
            if (ac.AnyGearTouching) touched++;
            if (res.NoseOverAt < 0 && (pitch < -30 || ac.LostComponents.Contains(AirframeComponent.Propeller))) { res.NoseOverAt = t + win; res.SpeedAtNoseOver = vw.Length; }
            log?.Invoke($"t={t + win:F2} V {vw.Length:F1} decel {decel:F2} g h {-s.Position.Z:F2} pitch {pitch:F1} q {s.Rates.Y * 57.3:F0} nz {ac.LoadFactorZ:F2} gear {ac.AnyGearTouching}");
            vPrev = vw;
            windows++;
        }
        res.TouchedFraction = windows > 0 ? (double)touched / windows : 0;
        res.MeanDecelG = (v - res.MinSpeed) / secs / 9.81;
        res.Lost = ac.LostComponents.ToList();
        res.Damage = ac.Hydro.DamageLog.ToList();
        res.WingsFailed = ac.Structure.WingsFailed;
        return res;
    }
}

