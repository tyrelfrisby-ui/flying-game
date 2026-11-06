using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The wakes over the tail (TailWake, owner 2026-11-02: "how much is the tail blanketing at very high aoa past 45 degrees
/// for example? it seems like too much"). Each wing and stabiliser section sheds its own wake from its own trailing edge /
/// separation point along the local flow, NACA Report 648 width and depth.
/// </summary>
public class TailWakeTests
{
    private readonly ITestOutputHelper _out;
    public TailWakeTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    /// <summary>Area-weighted q_local/q∞ of one surface at freestream angle of attack <paramref name="alphaDeg"/>, no rotation.</summary>
    public static double SurfaceQ(AircraftConfig cfg, IReadOnlyDictionary<string, AirfoilTable> tables, string surfaceId, double alphaDeg, double betaDeg = 0)
    {
        double a = alphaDeg * Math.PI / 180, b = betaDeg * Math.PI / 180;
        Vec3 v = new Vec3(30 * Math.Cos(a) * Math.Cos(b), 30 * Math.Sin(b), 30 * Math.Sin(a) * Math.Cos(b));
        double[] q = TailWake.StripQFactors(cfg, tables, v, Vec3.Zero);
        int idx = 0; double qa = 0, aa = 0;
        foreach (SurfaceConfig sf in cfg.Surfaces)
            foreach (StripConfig st in sf.Strips)
            {
                if (sf.Id == surfaceId) { qa += q[idx] * st.Area; aa += st.Area; }
                idx++;
            }
        return qa / aa;
    }

    [Fact]
    public void PrintTailQTable()
    {
        foreach (string id in new[] { "pitts-s2b-like", "extra-300-like", "c172-like", "great-lakes-like", "glider-2-33-like" })
        {
            var cfg = Load(id); var tables = Aircraft.BuildAirfoilTables(cfg);
            _out.WriteLine($"{id}: AoA | hStab | elevator | fin | rudder");
            foreach (double a in new[] { 0.0, 5, 10, 15, 20, 25, 30, 35, 45, 60, 75, -20, -45 })
                _out.WriteLine($"  {a,4:F0} | {SurfaceQ(cfg, tables, "hStab", a):F3} | {SurfaceQ(cfg, tables, "elevator", a):F3} | {SurfaceQ(cfg, tables, "vStab", a):F3} | {SurfaceQ(cfg, tables, "rudder-vstab", a):F3}");
        }
    }

    /// <summary>Strip-by-strip q factor of one surface (for the rudder, which the stab plane splits).</summary>
    private static double StripQ(AircraftConfig cfg, IReadOnlyDictionary<string, AirfoilTable> tables, string surfaceId, double z, double alphaDeg)
    {
        double a = alphaDeg * Math.PI / 180;
        double[] q = TailWake.StripQFactors(cfg, tables, new Vec3(30 * Math.Cos(a), 0, 30 * Math.Sin(a)), Vec3.Zero);
        int idx = 0;
        foreach (SurfaceConfig sf in cfg.Surfaces)
            foreach (StripConfig st in sf.Strips)
            {
                if (sf.Id == surfaceId && Math.Abs(st.Pos[2] - z) < 1e-3) return q[idx];
                idx++;
            }
        throw new InvalidOperationException($"no {surfaceId} strip at z {z}");
    }

    /// <summary>
    /// Ty's question, answered by geometry. The Pitts stab sits 0.6 m above the lower wing and 1.8 m behind its trailing edge
    /// (pitts-s2b-like.json strips), so the lower wing's wake, leaving along the flow, crosses the stab's height near
    /// α ≈ 18° and is well above it by 45°; the upper wing's wake leaves above the stab and goes away from it. Past 45° the
    /// stab and elevator fly in undisturbed air — the old single-wing band had them at 0.495 of free-stream q there.
    /// </summary>
    [Fact]
    public void PittsStabIsInCleanAirPast45Degrees()
    {
        var cfg = Load("pitts-s2b-like"); var tables = Aircraft.BuildAirfoilTables(cfg);
        foreach (double a in new[] { 45.0, 60, 75 })
        {
            Assert.InRange(SurfaceQ(cfg, tables, "hStab", a), 0.98, 1.0);
            Assert.InRange(SurfaceQ(cfg, tables, "elevator", a), 0.98, 1.0);
        }
        // Just past the stall the lower wing's wake is ON the stab: it is the band where the tail really is blanketed.
        Assert.InRange(SurfaceQ(cfg, tables, "hStab", 15), 0.4, 0.85);
        // Attached flight: the tail is clean (no flat η_t any more; the wake of a 0.01-c_d section is thin).
        Assert.InRange(SurfaceQ(cfg, tables, "hStab", 2), 0.97, 1.0);
    }

    /// <summary>
    /// The stab shields the rudder above it from below (NACA spin-tunnel tail-damping geometry), and the rudder below the stab
    /// — 31 % of the Pitts rudder on the Aviat three-view — is the part that stays in clean air in an upright spin. Inverted,
    /// the roles swap: the flow arrives from above and the stab's wake falls on the lower rudder.
    /// </summary>
    [Fact]
    public void PittsRudderBelowTheStabIsUnshieldedUpright()
    {
        var cfg = Load("pitts-s2b-like"); var tables = Aircraft.BuildAirfoilTables(cfg);
        Assert.InRange(StripQ(cfg, tables, "rudder-vstab", 0.089, 45), 0.98, 1.0);    // below the stab (z +0.089)
        Assert.InRange(StripQ(cfg, tables, "rudder-vstab", -0.198, 45), 0.0, 0.5);    // just above it, in its dead air
        Assert.InRange(StripQ(cfg, tables, "rudder-vstab", 0.089, -45), 0.0, 0.5);    // inverted: below the stab is downstream
        Assert.InRange(StripQ(cfg, tables, "rudder-vstab", -0.198, -45), 0.98, 1.0);
    }

    [Fact]
    public void NoWingNoWake()
    {
        var cfg = Load("c172-like"); var tables = Aircraft.BuildAirfoilTables(cfg);
        var mask = new bool[cfg.Surfaces.Count];
        for (int i = 0; i < mask.Length; i++) mask[i] = !cfg.Surfaces[i].Id.Contains("wing");
        // A stalled wing that has left the airframe sheds nothing over the tail: the tail's lift is that of a clean tail.
        double a = 20 * Math.PI / 180; Vec3 v = new(30 * Math.Cos(a), 0, 30 * Math.Sin(a));
        Vec3 fWingsOff = AeroModel.Compute(cfg, tables, v, Vec3.Zero, Vec3.Zero, 1.0, ControlDeflections.Neutral, surfaceMask: mask).Force;
        var tailOnly = TailOnlyClone(cfg);
        Vec3 fTail = AeroModel.Compute(tailOnly, Aircraft.BuildAirfoilTables(tailOnly), v, Vec3.Zero, Vec3.Zero, 1.0, ControlDeflections.Neutral).Force;
        Assert.True((fWingsOff - fTail).Length < 1e-6 * Math.Max(1, fTail.Length), $"{fWingsOff} vs {fTail}");
    }

    private static AircraftConfig TailOnlyClone(AircraftConfig cfg)
    {
        var c = AircraftConfigLoader.LoadFromJson(System.Text.Json.JsonSerializer.Serialize(cfg));
        c.Surfaces.RemoveAll(sf => sf.Id.Contains("wing"));
        return c;
    }

    /// <summary>One developed Pitts spin: stall, then full aft stick and full rudder held for 45 s, the last 20 s measured.</summary>
    public static (double alphaDeg, double pitchDeg, double secPerTurn, double ftPerTurn) PittsSpin(double rudder, double lever, bool noGyro = false)
    {
        AircraftConfig cfg = Load("pitts-s2b-like");
        if (noGyro) cfg.Propulsion!.PropInertia = 0;
        double v = ThermalSpawn.MinSinkSpeed(cfg, 2500);
        var t0 = TrimSolver.SolveGliderTrim(cfg, v, 2500, idleProp: true);
        var att = new Quat(0, Math.Sin(t0.ThetaRad / 2), 0, Math.Cos(t0.ThetaRad / 2));
        var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -2500), att, new Vec3(v * Math.Cos(t0.AlphaRad), 0, v * Math.Sin(t0.AlphaRad)), Vec3.Zero),
                              new ControlDeflections(0, t0.ElevatorRad, 0, 0));
        double stallDeg = StallAuthorityTests.StallDeg(cfg), h = SimLoop.DefaultFixedDtSec;
        static double Alpha(RigidBodyState s) { Vec3 air = s.Velocity - s.Attitude.Conjugate().Rotate(Atmosphere.WindAtPosition(s.Position)); return Math.Atan2(air.Z, air.X) * 180 / Math.PI; }
        for (double t = 0; t < 25; t += h) { ac.Step(new ControlInputs(0, -1, 0, 1.0), h); if (Alpha(ac.State) > stallDeg + 2) break; }
        var pro = new ControlInputs(0, -1, rudder, lever);
        double net = 0, lost = 0, sa = 0, sp = 0; int n = 0;
        for (double t = 0; t < 45; t += h)
        {
            double z0 = ac.State.Position.Z; ac.Step(pro, h); var s = ac.State;
            if (t >= 25)
            {
                Vec3 zB = s.Attitude.Conjugate().Rotate(new Vec3(0, 0, 1));
                sa += Alpha(s); sp += FormationPilot.Euler(s.Attitude).Item2 * 180 / Math.PI;
                net += (s.Rates.X * zB.X + s.Rates.Y * zB.Y + s.Rates.Z * zB.Z) * h; lost += s.Position.Z - z0; n++;
            }
            if (-s.Position.Z < 600) ac.State = new RigidBodyState(new Vec3(s.Position.X, s.Position.Y, s.Position.Z - 2500), s.Attitude, s.Velocity, s.Rates);
        }
        double turns = Math.Abs(net) / (2 * Math.PI);
        return (sa / n, sp / n, 20 / turns, lost * 3.2808 / turns);
    }

    /// <summary>
    /// The idle upright spin is a STEEP spin, both directions: AAIB G-ODDS / manufacturer data for the S-2A, 10 turns in
    /// 3,400 ft and 32 s, nose well down. Pinned: nose at least 50° below the horizon and α under 40°. NOT pinned: the turn
    /// time. Real is ~3.2 s/turn; this model gives 1.5–2.1 s/turn, because with a clean stab the spin's pitch balance
    /// (tail nose-down against the inertial pitch-up (I_zz − I_xx)·p·r) needs that rate — see AGENT-LOG 2026-11-02.
    /// </summary>
    [Theory]
    [InlineData(-1.0)]
    [InlineData(1.0)]
    public void PittsIdleSpinIsSteep(double rudder)
    {
        var (alpha, pitch, sPerTurn, _) = PittsSpin(rudder, 1.0);
        _out.WriteLine($"rudder {rudder}: alpha {alpha:F1} pitch {pitch:F1} {sPerTurn:F2} s/turn");
        Assert.InRange(alpha, 25.0, 40.0);
        Assert.True(pitch <= -50.0, $"nose only {-pitch:F0} deg down");
        Assert.InRange(sPerTurn, 1.0, 4.5);   // a spin, not a spiral or a stop
    }

    /// <summary>
    /// Partial power does not flatten the spin through the AIRFRAME: with the propeller's gyroscopic couple removed, a 40 %
    /// power spin either way stays below 60° α. (With it, PropInertia 3.5 kg·m² at the governed 2,500 rpm, the LEFT spin goes flat at α ≈ 90: the
    /// clockwise prop's precession pitches the nose up in a left yaw. That is a propeller-inertia question, lane/claude-inertia.)
    /// </summary>
    [Theory]
    [InlineData(-1.0)]
    [InlineData(1.0)]
    public void PartialPowerSpinIsNotFlatWithoutTheGyroCouple(double rudder)
    {
        var (alpha, pitch, sPerTurn, _) = PittsSpin(rudder, 0.2, noGyro: true);
        _out.WriteLine($"rudder {rudder}: alpha {alpha:F1} pitch {pitch:F1} {sPerTurn:F2} s/turn");
        Assert.True(alpha < 60.0, $"alpha {alpha:F0}");
    }
}
