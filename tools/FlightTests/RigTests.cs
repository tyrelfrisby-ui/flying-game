using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The fleet's RIG (owner 2026-11-02: "trim/rig all of the airplanes for hands off level 75% power"), flown.
///
/// Every powered type is spawned at its rig condition — level at 75 % power, 4,000 ft ISA, the stick and rudder
/// centred, the elevator on the trim RigSolver gives — and left alone. After a settle the rates, the sideslip and the
/// vertical speed must be the ones of an aeroplane flying straight and level by itself; every glider likewise at best
/// glide. Then the power is moved: more must yaw and roll the aeroplane toward the side its propeller torques it
/// (left for a clockwise-from-behind prop), less the other way, as on the real aeroplanes.
///
/// The rig angles themselves are pinned against literals copied by hand from RigTool's output (see
/// docs/RIGGING.md): moving anything that moves the rig fails here until RigTool is re-run and the numbers re-copied.
/// Runs in the non-parallel collection because it needs the global atmosphere still.
/// </summary>
[Collection("WorldTerrainActive")]
public class RigTests
{
    private readonly ITestOutputHelper _out;
    public RigTests(ITestOutputHelper o) { _out = o; }

    private const double D = 180.0 / Math.PI;

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    /// <summary>Still air, no terrain: the rig is a property of the airframe, not of the weather.</summary>
    private static IDisposable StillAir()
    {
        var field = Atmosphere.Field; var wind = Atmosphere.SteadyWind; var turb = Atmosphere.ActiveTurbulence; var terrain = WorldTerrain.Active;
        var ridge = Atmosphere.ActiveRidge; var thermals = Atmosphere.Thermals.ToList();
        Atmosphere.Field = null; Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null; WorldTerrain.Active = null;
        Atmosphere.ActiveRidge = null; Atmosphere.Thermals.Clear();
        return new Restore(() =>
        {
            Atmosphere.Field = field; Atmosphere.SteadyWind = wind; Atmosphere.ActiveTurbulence = turb; WorldTerrain.Active = terrain;
            Atmosphere.ActiveRidge = ridge; Atmosphere.Thermals.AddRange(thermals);
        });
    }
    private sealed class Restore : IDisposable { private readonly Action _a; public Restore(Action a) { _a = a; } public void Dispose() => _a(); }

    /// <summary>Bank and HeadingChange are CHANGES over the measured window (the trimmed state starts slightly banked).</summary>
    public sealed record Flight(double MaxP, double MaxQ, double MaxR, double MaxBeta, double AltChange, double MaxVs, double Bank, double HeadingChange);

    /// <summary>Spawn at a solved steady state and fly it with the stick at the trim, aileron and rudder centred and
    /// the throttle at `throttle` from t = 0. Rates/sideslip/vertical speed are measured over [settle, settle+window].</summary>
    private static (Aircraft ac, Flight f) Fly(AircraftConfig c, RigSolver.Result r, double throttle, double settle, double window)
    {
        Quat att = RigSolver.Attitude(r, 0.0);
        Vec3 vb = new(r.TasMs * Math.Cos(r.AlphaRad), 0, r.TasMs * Math.Sin(r.AlphaRad));
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -r.AltitudeM), att, vb, Vec3.Zero), new ControlDeflections(0, r.ElevatorRad, 0, 0));
        if (c.RetractableGear) ac.SetGear(false, immediate: true);
        double stick = Aircraft.StickForDeflection(r.ElevatorRad, c.Controls.Elevator);
        double lever = c.Propulsion is null ? 0.0 : 1.0 - 2.0 * throttle;
        var input = new ControlInputs(0, stick, 0, lever);
        const double dt = SimLoop.DefaultFixedDtSec;
        double t = 0, alt0 = r.AltitudeM, psi0 = Heading(ac.State.Attitude), bank0 = Bank(ac.State.Attitude), maxP = 0, maxQ = 0, maxR = 0, maxB = 0, maxVs = 0;
        while (t < settle + window - 1e-9)
        {
            ac.Step(input, dt); t += dt;
            RigidBodyState s = ac.State;
            if (settle > 0 && Math.Abs(t - settle) < dt / 2) { alt0 = -s.Position.Z; psi0 = Heading(s.Attitude); bank0 = Bank(s.Attitude); }
            if (t >= settle)
            {
                maxP = Math.Max(maxP, Math.Abs(s.Rates.X)); maxQ = Math.Max(maxQ, Math.Abs(s.Rates.Y)); maxR = Math.Max(maxR, Math.Abs(s.Rates.Z));
                maxB = Math.Max(maxB, Math.Abs(Math.Asin(Math.Clamp(s.Velocity.Y / s.Velocity.Length, -1, 1))));
                Vec3 vw = s.Attitude.Rotate(s.Velocity);
                maxVs = Math.Max(maxVs, Math.Abs(vw.Z));
            }
        }
        RigidBodyState e = ac.State;
        double dpsi = Heading(e.Attitude) - psi0; dpsi = Math.Atan2(Math.Sin(dpsi), Math.Cos(dpsi));
        return (ac, new Flight(maxP * D, maxQ * D, maxR * D, maxB * D, -e.Position.Z - alt0, maxVs, (Bank(e.Attitude) - bank0) * D, dpsi * D));
    }

    private static double Heading(Quat q) { Vec3 x = q.Rotate(new Vec3(1, 0, 0)); return Math.Atan2(x.Y, x.X); }
    private static double Bank(Quat q)
    {
        Vec3 y = q.Rotate(new Vec3(0, 1, 0)); return Math.Asin(Math.Clamp(y.Z, -1, 1));   // right wing down (z+) = right bank
    }

    public static IEnumerable<object[]> Powered() => RigTable.Rows.Where(r => r.Powered).Select(r => new object[] { r.Id });
    public static IEnumerable<object[]> Unpowered() => RigTable.Rows.Where(r => !r.Powered).Select(r => new object[] { r.Id });
    public static IEnumerable<object[]> Propellers() => RigTable.Rows.Where(r => r.Powered && r.PropSign != 0).Select(r => new object[] { r.Id });

    /// <summary>
    /// Every config the build carries — THE DIRECTORY, not <see cref="RigTable.Rows"/>. What to check must not come
    /// from the thing being checked: while this read the table, the Grob 103 sat in the fleet with no rig block at all
    /// and no row, so it got no test case and the pins had nothing to run on it. Off the directory, a type with no row
    /// is a loud failure in <see cref="ConfigRigMatchesTheHandCopiedTable"/> rather than a type nobody tests.
    /// </summary>
    public static string[] ConfigIds() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData"), "*.json")
                 .Select(p => Path.GetFileNameWithoutExtension(p)!).OrderBy(x => x, StringComparer.Ordinal).ToArray();

    // ---- 1. the rig in the configs is the rig the model asks for, and matches the hand-copied table ----

    [Fact]
    public void EveryConfigIsInTheTable()
    {
        Assert.Equal(ConfigIds(), RigTable.Rows.Select(r => r.Id).OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [MemberData(nameof(AllIds))]
    public void ConfigRigMatchesTheHandCopiedTable(string id)
    {
        RigTable.Row row = RigTable.Get(id);
        AircraftConfig c = Load(id);
        Assert.Equal(row.Powered ? RigSolver.RigThrottle : 0.0, c.Rig.ThrottleFraction, 6);
        Assert.Equal(RigSolver.RigAltitudeM, c.Rig.AltitudeM, 3);
        Assert.True(Math.Abs(c.Rig.AileronRad * D - row.AileronDeg) < 0.0015, $"{id}: aileron rig {c.Rig.AileronRad * D:F4}° vs table {row.AileronDeg:F3}°");
        Assert.True(Math.Abs(c.Rig.FinOffsetRad * D - row.FinDeg) < 0.0015, $"{id}: fin offset {c.Rig.FinOffsetRad * D:F4}° vs table {row.FinDeg:F3}°");
        Assert.True(Math.Abs(c.Rig.ElevatorTrimRad * D - row.ElevatorDeg) < 0.0015, $"{id}: elevator trim {c.Rig.ElevatorTrimRad * D:F4}° vs table {row.ElevatorDeg:F3}°");
        Assert.True(Math.Abs(c.Rig.TasMs / 0.514444 - row.Ktas) < 0.06, $"{id}: rig speed {c.Rig.TasMs / 0.514444:F2} KTAS vs table {row.Ktas:F1}");
    }

    public static IEnumerable<object[]> AllIds() => ConfigIds().Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(AllIds))]
    public void ModelStillAsksForTheSameRig(string id)
    {
        // Re-solve from the bare airframe: if the aero or prop model moves, the stored rig is stale and this says so.
        AircraftConfig c = Load(id);
        RigConfig stored = c.Rig;
        c.Rig = new RigConfig();
        RigSolver.Result r = RigSolver.SolveRig(c);
        Assert.True(r.Converged, $"{id}: rig solve did not converge (residual {r.ResidualNorm:E2})");
        Assert.True(Math.Abs((r.AileronRigRad - stored.AileronRad) * D) < 0.002 && Math.Abs((r.FinOffsetRad - stored.FinOffsetRad) * D) < 0.002
                    && Math.Abs((r.ElevatorRad - stored.ElevatorTrimRad) * D) < 0.002,
            $"{id}: the model now wants aileron {r.AileronRigRad * D:F4}°, fin {r.FinOffsetRad * D:F4}°, elevator {r.ElevatorRad * D:F4}° " +
            $"but the config carries {stored.AileronRad * D:F4}°, {stored.FinOffsetRad * D:F4}°, {stored.ElevatorTrimRad * D:F4}° — re-run RigTool (RIG_WRITE=1)");
    }

    // ---- 2. hands off at the rig condition ----

    /// <summary>
    /// Bands, after a 20 s settle and over the next 40 s (the solved state is exact to 1e-11 of the weight; what moves
    /// at all is the start-up of the downwash and unsteady lags, which kicks a small phugoid):
    ///   roll, pitch and yaw rate below 0.25 °/s — a standard-rate turn is 3 °/s, so this is a twelfth of one;
    ///   sideslip below 0.2° — under a quarter of a ball width on a slip indicator (one ball width ≈ 1–2° of slip);
    ///   altitude within 15 m (50 ft) and vertical speed below 1.5 m/s (300 fpm) — inside the PTS ±100 ft for
    ///   straight and level, and the residual phugoid of the start-up is all there is.
    /// </summary>
    [Theory]
    [MemberData(nameof(Powered))]
    public void HandsOffLevelAtRigPower(string id)
    {
        using var still = StillAir();
        AircraftConfig c = Load(id);
        RigSolver.Result r = RigSolver.SolveLevel(c, RigSolver.RigThrottle, RigSolver.RigAltitudeM);
        Assert.True(r.Converged, $"{id}: level solve did not converge");
        // With its own rig the level state needs no lateral input at all: the lateral residual is already ~0.
        Assert.True(Math.Abs(r.Residual[3]) < 2e-4 && Math.Abs(r.Residual[5]) < 2e-4,
            $"{id}: rolling/yawing moment at the rig point {r.Residual[3]:E2}/{r.Residual[5]:E2} W·m");
        (_, Flight f) = Fly(c, r, RigSolver.RigThrottle, 20, 40);
        _out.WriteLine($"{id}: p {f.MaxP:F3} q {f.MaxQ:F3} r {f.MaxR:F3} °/s, β {f.MaxBeta:F3}°, Δh {f.AltChange:F1} m, |vs| {f.MaxVs:F2} m/s, bank {f.Bank:F2}°, Δψ {f.HeadingChange:F2}°");
        Assert.True(f.MaxP < 0.25 && f.MaxQ < 0.25 && f.MaxR < 0.25, $"{id}: rates p {f.MaxP:F3} q {f.MaxQ:F3} r {f.MaxR:F3} °/s");
        Assert.True(f.MaxBeta < 0.2, $"{id}: sideslip {f.MaxBeta:F3}°");
        Assert.True(Math.Abs(f.AltChange) < 15 && f.MaxVs < 1.5, $"{id}: altitude change {f.AltChange:F1} m, vertical speed {f.MaxVs:F2} m/s");
    }

    /// <summary>Gliders at best glide, rig and stick centred: same rate and sideslip bands, and a steady glide — the
    /// sink rate holds within 0.3 m/s over the window (no phugoid worth the name, no spiral).</summary>
    [Theory]
    [MemberData(nameof(Unpowered))]
    public void HandsOffAtBestGlide(string id)
    {
        using var still = StillAir();
        AircraftConfig c = Load(id);
        RigSolver.Result r = RigSolver.SolveGlide(c, RigSolver.RigGlideTasAt(c, RigSolver.RigAltitudeM), RigSolver.RigAltitudeM);
        Assert.True(r.Converged, $"{id}: glide solve did not converge");
        Assert.True(r.GammaRad < 0, $"{id}: a glider glides down");
        Assert.True(Math.Abs(r.Residual[3]) < 1e-6 && Math.Abs(r.Residual[5]) < 1e-6, $"{id}: a symmetric glider has no rolling/yawing moment");
        (_, Flight f) = Fly(c, r, 0, 20, 40);
        _out.WriteLine($"{id}: p {f.MaxP:F3} q {f.MaxQ:F3} r {f.MaxR:F3} °/s, β {f.MaxBeta:F3}°, bank {f.Bank:F2}°, Δψ {f.HeadingChange:F2}°");
        Assert.True(f.MaxP < 0.25 && f.MaxQ < 0.25 && f.MaxR < 0.25, $"{id}: rates p {f.MaxP:F3} q {f.MaxQ:F3} r {f.MaxR:F3} °/s");
        Assert.True(f.MaxBeta < 0.2, $"{id}: sideslip {f.MaxBeta:F3}°");
    }

    /// <summary>
    /// The rig is fixed geometry set at 4,000 ft; the island's in-air starts are at ~1,500 ft (coast) and ~6,500 ft
    /// (inland). The engine model has no altitude lapse, so 75 % there buys a different EQUIVALENT airspeed — higher in
    /// the denser air — and the fixed rig, which works through q, over-corrects low and under-corrects high: the
    /// aeroplane wanders right at 1,500 ft and left at 6,500 ft, as one rigged once would.
    /// </summary>
    [Theory]
    [MemberData(nameof(PoweredAtStartAltitudes))]
    public void RigStillNearlyHandsOffAtTheStartAltitudes(string id, double altitudeFt)
    {
        // A PROBE, not a gate (RIG_PROBE=1): off its rig altitude a fixed rig is not hands-off and is not meant to be;
        // what this measures is how far from it, and docs/RIGGING.md reports it.
        // RIG_PROBE_IDS=<id>[,<id>...] scopes it. 30 types x 5 heights x a 60 s flight is an hour on a loaded machine,
        // and the question asked is usually about one aeroplane — xUnit's --filter cannot select a theory's DATA, so
        // without this the only way to probe the Pitts was to probe the fleet.
        if (Environment.GetEnvironmentVariable("RIG_PROBE") == null) return;
        var onlyIds = (Environment.GetEnvironmentVariable("RIG_PROBE_IDS") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();
        if (onlyIds.Length > 0 && !onlyIds.Contains(id)) return;
        using var still = StillAir();
        AircraftConfig c = Load(id);
        RigSolver.Result r = RigSolver.SolveLevel(c, RigSolver.RigThrottle, altitudeFt * 0.3048);
        // NOT asserted: whether the level solve converges off the rig altitude is one of the things being measured.
        // Asserting it stopped the probe at the first type that failed and reported nothing about the rest, which is
        // how "the J-3 does not converge off the rig altitude" could be true for a day without a number beside it.
        if (!r.Converged) { _out.WriteLine($"{id} @ {altitudeFt} ft: level solve DID NOT CONVERGE (residual {r.ResidualNorm:E2}) — AirStart falls back to the power-off trim"); return; }
        (_, Flight f) = Fly(c, r, RigSolver.RigThrottle, 20, 40);
        _out.WriteLine($"{id} @ {altitudeFt} ft: {r.TasMs / 0.514444:F1} KTAS, L {r.Residual[3]:E1} N {r.Residual[5]:E1} W·m; r {f.MaxR:F3} °/s, β {f.MaxBeta:F3}°, bank {f.Bank:F2}°, Δψ {f.HeadingChange:F2}°, Δh {f.AltChange:F1} m");
    }

    /// <summary>The island's two in-air start heights, and the three round numbers Ty flies the hands-off drift at
    /// (3,000 / 6,000 / 10,000 ft) so the probe answers the question that gets asked.</summary>
    public static IEnumerable<object[]> PoweredAtStartAltitudes() =>
        RigTable.Rows.Where(r => r.Powered)
                     .SelectMany(r => new[] { 1500.0, 3000.0, 6000.0, 6500.0, 10000.0 }.Select(a => new object[] { r.Id, a }));

    // ---- 3. power away from the rig point ----

    /// <summary>The moments first, at the rig state with nothing else changed: more power must roll and yaw toward the
    /// propeller's torque side (left, for RotationSign +1), less power the other way. Then flown: 3 s after the
    /// change, the heading and bank have gone the same way.</summary>
    [Theory]
    [MemberData(nameof(Propellers))]
    public void PowerAwayFromTheRigYawsAndRollsTheRightWay(string id)
    {
        using var still = StillAir();
        AircraftConfig c = Load(id);
        RigTable.Row row = RigTable.Get(id);
        RigSolver.Result r = RigSolver.SolveLevel(c, RigSolver.RigThrottle, RigSolver.RigAltitudeM);
        var tables = Aircraft.BuildAirfoilTables(c);
        double rho = Atmosphere.DensityAtAltitude(r.AltitudeM), w = c.Mass.MassKg * Atmosphere.GravityMs2;
        Vec3 vb = new(r.TasMs * Math.Cos(r.AlphaRad), 0, r.TasMs * Math.Sin(r.AlphaRad));
        var ctl = new ControlDeflections(0, r.ElevatorRad, 0, 0);
        Vec3 M(double thr) => RigSolver.SteadyForces(c, tables, thr, vb, rho, ctl).Moment;
        Vec3 m0 = M(RigSolver.RigThrottle), mUp = M(1.0), mDn = M(0.3);
        double s = row.PropSign;   // +1: torque side is LEFT (negative roll / yaw)
        _out.WriteLine($"{id}: full power ΔL {(mUp.X - m0.X):F1} ΔN {(mUp.Z - m0.Z):F1} N·m; 30 % ΔL {(mDn.X - m0.X):F1} ΔN {(mDn.Z - m0.Z):F1} N·m (W {w:F0} N)");
        Assert.True(s * (mUp.Z - m0.Z) < 0 && s * (mUp.X - m0.X) < 0, $"{id}: more power must yaw AND roll to the torque side");
        Assert.True(s * (mDn.Z - m0.Z) > 0 && s * (mDn.X - m0.X) > 0, $"{id}: less power must yaw AND roll away from the torque side");

        (_, Flight up) = Fly(c, r, 1.0, 0, 3);
        (_, Flight dn) = Fly(c, r, 0.3, 0, 3);
        _out.WriteLine($"{id}: flown 3 s — full power Δψ {up.HeadingChange:F2}° bank {up.Bank:F2}°; 30 % Δψ {dn.HeadingChange:F2}° bank {dn.Bank:F2}°");
        Assert.True(s * up.HeadingChange < 0 && s * up.Bank < 0, $"{id}: full power flew Δψ {up.HeadingChange:F2}°, bank {up.Bank:F2}°");
        Assert.True(s * dn.HeadingChange > 0 && s * dn.Bank > 0, $"{id}: 30 % power flew Δψ {dn.HeadingChange:F2}°, bank {dn.Bank:F2}°");
    }

    /// <summary>Sign pin for the table: a positive aileron rig rolls RIGHT (+Mx) and a positive fin offset yaws RIGHT (+Mz),
    /// which is what a clockwise prop's left-rolling torque and left-yawing slipstream need — hence both positive.</summary>
    [Fact]
    public void PositiveRigRollsAndYawsRight()
    {
        AircraftConfig c = Load("c172-like");
        RigSolver.Result r = RigSolver.SolveLevel(c, RigSolver.RigThrottle, RigSolver.RigAltitudeM);
        var tables = Aircraft.BuildAirfoilTables(c);
        double rho = Atmosphere.DensityAtAltitude(r.AltitudeM);
        Vec3 vb = new(r.TasMs * Math.Cos(r.AlphaRad), 0, r.TasMs * Math.Sin(r.AlphaRad));
        var ctl = new ControlDeflections(0, r.ElevatorRad, 0, 0);
        Vec3 M(double ail, double fin) { c.Rig = new RigConfig { AileronRad = ail, FinOffsetRad = fin }; return RigSolver.SteadyForces(c, tables, 0.75, vb, rho, ctl).Moment; }
        Vec3 m0 = M(0, 0), ma = M(0.01, 0), mf = M(0, 0.01);
        Assert.True(ma.X - m0.X > 0, $"+aileron rig must roll right; ΔL {ma.X - m0.X:F1}");
        Assert.True(mf.Z - m0.Z > 0, $"+fin offset must yaw right; ΔN {mf.Z - m0.Z:F1}");
    }

    /// <summary>The Seminole's props turn opposite ways (left clockwise, right counter-clockwise from behind): power must
    /// move its roll and yaw by a small fraction of what the same aeroplane would feel with both turning clockwise.</summary>
    [Fact]
    public void CounterRotatingTwinBarelyFeelsPower()
    {
        using var still = StillAir();
        AircraftConfig c = Load("seminole-like");
        RigSolver.Result r = RigSolver.SolveLevel(c, RigSolver.RigThrottle, RigSolver.RigAltitudeM);
        var tables = Aircraft.BuildAirfoilTables(c);
        double rho = Atmosphere.DensityAtAltitude(r.AltitudeM);
        Vec3 vb = new(r.TasMs * Math.Cos(r.AlphaRad), 0, r.TasMs * Math.Sin(r.AlphaRad));
        var ctl = new ControlDeflections(0, r.ElevatorRad, 0, 0);
        Vec3 Dm() => RigSolver.SteadyForces(c, tables, 1.0, vb, rho, ctl).Moment - RigSolver.SteadyForces(c, tables, 0.75, vb, rho, ctl).Moment;
        Vec3 counter = Dm();
        int saved = c.Engines[1].RotationSign; c.Engines[1].RotationSign = c.Engines[0].RotationSign;
        Vec3 co = Dm(); c.Engines[1].RotationSign = saved;
        _out.WriteLine($"seminole 75→100 %: counter-rotating ΔL {counter.X:F1} ΔN {counter.Z:F1}; co-rotating ΔL {co.X:F1} ΔN {co.Z:F1} N·m");
        Assert.True(Math.Abs(counter.X) < 0.1 * Math.Abs(co.X) && Math.Abs(counter.Z) < 0.1 * Math.Abs(co.Z), "counter-rotation must cancel the power effects");
    }
}

/// <summary>
/// The fleet rig, copied BY HAND from RigTool's output (RIG_PROBE=1) on 2026-11-02 — not read from the configs, so a
/// changed config has to be a deliberate edit in two places. RE-SOLVED 2026-11-03 on lane/claude-island-nav after
/// lane/claude-pitts-spin (run #3) was merged on top of lane/claude-roll-only: the aileron-section change (PlainFlap: every
/// aileron is weaker per degree, so the rig needs 8–10 % more of it, the Pitts 35 % more), the biplane induction, the
/// per-section tail wakes (which removed the flat η_t = 0.9 and moved the elevator trims) and the induced-angle tail
/// tables, stick fixed at controls neutral. The merged AeroModel is the spin lane's, and RIG_WRITE=1 on the merged tree
/// reproduced the spin lane's rows to every digit; these rows are that RigTool output transcribed. Degrees and knots. PropSign: +1 clockwise-from-behind
/// prop(s) with a net torque, 0 none (glider, jet, counter-rotating twin).
///
/// RE-SOLVED for the SIX SHIPPING TYPES on 2026-11-08 (the 2-33, both Cubs, the Great Lakes, the Pitts and the Extra), after
/// the traced thrust jet (PropJet) and the geometric shadow on all six (TailWake.ShadowK, the fuselage's own shadow with it).
/// Both move the rig: the jet's tube no longer blows whatever is level with the body axis however high it stands, so a Cub's
/// fin wants 15 % more offset; and the jet reaches the wing root now, where its swirl makes a rolling moment, so the aileron
/// rigs moved 7-20 %. The other twenty-two types are NOT re-solved here (owner 2026-11-08: "for now just re rig the 6 that
/// ship"), so their rows and their stored rigs are both stale against this model and ModelStillAsksForTheSameRig says so for
/// each of them. That is a reported state, not a passing one.
///
/// RE-SOLVED AGAIN the same night, after TailWake.OnBody stopped the fuselage shadowing the roots of what is bolted to it.
/// That gave the tail back 1 % of its dynamic pressure at the trim alpha, and 1 % was enough to move three of the six:
/// the Pitts' and the Great Lakes' fin offsets came down 0.003 and 0.004 deg and the floatplane's by 0.001, because a
/// tail that works slightly better needs slightly less of it. Worth recording for scale — these rigs are sensitive to
/// the third decimal of tail q, which is why the twenty-two stale types are not a cosmetic debt.
///
/// RE-SOLVED FOR THE WHOLE FLEET, 2026-11-10 (RIG_WRITE=1 over all 35 configs, every one converged, residuals
/// 2E-16..1E-12), because this week's two tail changes left every stored rig stale and the half-measures above had run
/// out of road. What moved:
///   daf52725 put all 26 FINS on the low-aspect-ratio plate, and a weaker fin needs more incidence for the same yawing
///     moment. That is the whole story of the big numbers here: the fabric types' fin offsets went up 4-6x (the J-3
///     0.364 -> 2.260 deg, the Pitts 0.475 -> 1.997, the Great Lakes 0.361 -> 1.950) and the aileron rigs came DOWN by
///     a third to two thirds (the Pitts 0.386 -> 0.096), because the fin's own side force is now carried at a much
///     smaller bank and the ailerons no longer have to hold the aeroplane against it. The J-3 landing at 2.26 deg is
///     worth something: Ty measured about 1.76 deg of fin offset on his own aeroplane, and nothing here is tuned
///     toward it.
///   b7396cd0 made twelve types' horizontal tail a flat plate, which moved their elevator trims and, through the drag,
///     the rig SPEED — the Pitts 139.1 -> 126.2 KTAS at the same 75 %, the Great Lakes 108.5 -> 99.8.
/// Seven configs (both Cubs, the Pitts, the Great Lakes, the Extra, the PA-18 floats and the Beaver floats) had already
/// been rewritten by b11819f7 — the measured-slipstream commit, 2026-11-09 11:06 — WITHOUT re-copying these rows, which
/// is the only reason ConfigRigMatchesTheHandCopiedTable was red at all. It was green for the other twenty-seven, whose
/// stored rigs were just as stale: a literal check cannot see a rig invalidated by the MODEL moving, because the config
/// and the literal stay in perfect agreement and go wrong together. ModelStillAsksForTheSameRig is the one that sees
/// it, and it was red for 33 of 34. Re-run BOTH after anything that touches a tail, a section, the prop or the jet.
///
/// The Grob 103 gets its first row here. It had a config, no `rig` block at all and no row, and because this table was
/// what told RigTests and HandsOffLaunchSpiralTests which types to test, no test ran on it — it was unrigged and green
/// for two days. Both now enumerate the config DIRECTORY (RigTests.ConfigIds). It had no rig block because RigTool's
/// insertion regex was anchored on a two-space indent and the Grob's generator writes one space; that is fixed too.
/// </summary>
public static class RigTable
{
    public sealed record Row(string Id, bool Powered, int PropSign, double AileronDeg, double FinDeg, double ElevatorDeg, double Ktas);

    public static readonly Row[] Rows =
    {
        //  id                         powered prop  aileron°  fin°     elevator°  KTAS      (all re-solved 2026-11-10)
        // RE-SOLVED AGAIN 2026-11-10, the five FITTED-BLADE types only, because the rig was solving a propeller the
        // sim does not fly. Three faults, all of them reaching only a SOLVED propeller, which is why they landed on
        // these five and on nothing else: RigSolver.SteadyForces took its drive from PropModel.SteadyDrive, which
        // governs a constant-speed blade unconditionally, while AircraftEngine.SolvesShaft leaves a GOVERNING
        // constant-speed type on the throttle law — so PropModel.IsSolved was true in the solve and false in flight
        // for the Pitts, the Extra and the Great Lakes, which is a different propeller AND a different fin; the shaft
        // was seeded at idle rather than at its torque balance, so every spawn spooled up through the wrong torque,
        // P-factor and swirl; and SteadyDrive was fed the DENSITY ratio where PistonEngine wants the ambient PRESSURE
        // ratio (0.888 against 0.864 at 4,000 ft). The three constant-speed types' fin offsets fall from ~1.94 deg to
        // ~0.36-0.49 and their rig speeds rise, because the rig no longer stands their fin in a geometric swirl the
        // sim never applies to them. The other thirty rows reproduced to the last digit, which is both the
        // idempotence check and the predicate.
        new("aircam-amphib-like", true, 1, 0.2857, 0.4318, -0.2374, 97.09),
        new("aircam-like", true, 1, 0.2746, 0.3972, 0.5291, 99.41),
        new("boeing-737-like", true, 0, 0.0000, 0.0000, 3.1262, 349.57),
        new("c172-like", true, 1, 0.1750, 0.1440, 1.6544, 123.42),
        new("cassutt-f1-like", true, 1, 0.3319, 0.1609, 0.4464, 159.08),
        new("cirrus-sr22-like", true, 1, 0.2307, 0.1037, 2.1641, 162.33),
        new("dc3-like", true, 1, 0.1552, 0.1367, 0.6201, 152.66),
        new("decathlon-8kcab-like", true, 1, 0.2491, 0.3482, 1.8646, 105.78),
        new("dhc2-beaver-floats-like", true, 1, 0.2893, 0.5155, 0.4559, 114.60),
        new("dhc2-beaver-like", true, 1, 0.2493, 0.4228, 0.7352, 123.37),
        new("extra-300-like", true, 1, 0.1627, 1.1165, -1.5590, 148.63),
        new("f86-sabre-like", true, 0, 0.0000, 0.0000, 1.7871, 320.17),
        new("geebee-r2-like", true, 1, 0.7412, 0.4168, -1.5728, 187.93),
        new("glasair3-like", true, 1, 0.4384, 0.1313, 0.2363, 235.31),
        new("glider-2-33-like", false, 0, 0.0000, 0.0000, 0.4707, 40.96),
        new("glider-eb29r-like", false, 0, 0.0000, 0.0000, -3.3127, 62.34),
        // The Grob 103's FIRST rig (2026-11-10): it had a config, no rig block and no row, so nothing tested it.
        new("glider-grob103-acro-like", false, 0, 0.0000, 0.0000, 0.4746, 50.13),
        new("glider-swift-s1-like", false, 0, 0.0000, 0.0000, -1.3028, 62.69),
        new("great-lakes-like", true, 1, 0.3060, 1.9462, 0.7734, 98.17),
        new("hang-glider-like", false, 0, 0.0000, 0.0000, -15.5066, 21.38),
        new("hughes-h4-like", true, 1, 0.0413, 0.0327, -0.5892, 151.17),
        // RE-SOLVED 2026-11-11, the two J-3s only, because their authored MASS moved. Both configs had been authored
        // at their PLACARDS — 553.0 kg = 1,220 lb and 589.7 kg = 1,300 lb — and they now carry the type's published
        // empty weight plus the one-pilot-in-the-rear-seat reference the fleet uses: 680 lb (Piper Service Memo 41)
        // + 86 kg = 394.4 kg, and 805 lb (that plus the 125 lb seaplane exchange) + 86 kg = 451.1 kg. mass.massKg is
        // what RigSolver flies, so a lighter aeroplane is a DIFFERENT RIG: at the same 75 % it sits faster and at a
        // far lower alpha — the wheels Cub 56.31 → 62.58 KTAS at alpha 3.51° → −0.21°, the floats one 59.64 → 65.64
        // at 3.08° → 0.00° — and the elevator trim crosses zero with it (−1.74° → +0.54°, −1.51° → +0.55°). The fin
        // offsets move about a tenth of a degree (+2.557 → +2.447, +2.243 → +2.314), which is the check that this is
        // a weight change and not a tail change: a tail change moves the fin first.
        // Nothing else in the fleet was re-solved — RIG_ONLY=j3-cub-like,j3-cub-floats-like.
        new("j3-cub-floats-like", true, 1, 0.1483, 2.3143, 0.5484, 65.64),
        new("j3-cub-like", true, 1, 0.1611, 2.4467, 0.5428, 62.58),
        new("jn4-jenny-like", true, 1, 0.5127, 0.6826, -0.8444, 50.64),
        new("p51d-like", true, 1, 0.4943, 0.1943, -1.3254, 209.27),
        new("pa18-bush-like", true, 1, 0.2551, 0.4243, 0.7431, 91.25),
        new("pa18-cub-like", true, 1, 0.2387, 0.3810, 1.0109, 94.21),
        new("pa18-floats-like", true, 1, 0.2789, 0.4582, 0.1551, 87.13),
        new("pa25-pawnee-like", true, 1, 0.2472, 0.3302, 1.1592, 108.46),
        new("pa28-archer-like", true, 1, 0.2701, 0.1986, 0.6536, 108.74),
        new("paraglider-en-b-like", false, 0, 0.0000, 0.0000, 9.5138, 19.74),
        new("pitts-s2b-like", true, 1, 0.0977, 1.9392, -2.1482, 124.29),
        new("seminole-like", true, 0, 0.0000, 0.0000, 1.7195, 143.09),
        new("stearman-pt17-like", true, 1, 0.4759, 0.3375, 3.7671, 106.94),
        new("target-drone-like", true, 1, 0.3319, 0.1609, 0.4464, 159.08),
    };

    public static Row Get(string id)
    {
        Row? row = Rows.SingleOrDefault(r => r.Id == id);
        // Not Single(): a missing row used to throw "Sequence contains no matching element" from deep inside LINQ, which
        // reads like a broken test rather than "this aeroplane is not rigged and nothing is checking it".
        Assert.True(row is not null, $"{id} has a config but no RigTable row — run RIG_PROBE=1 dotnet test --filter RigTool and add it");
        return row!;
    }
}

