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

    // ---- 1. the rig in the configs is the rig the model asks for, and matches the hand-copied table ----

    [Fact]
    public void EveryConfigIsInTheTable()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "TestData");
        var ids = Directory.GetFiles(dir, "*.json").Select(p => Path.GetFileNameWithoutExtension(p)).OrderBy(x => x).ToArray();
        Assert.Equal(ids, RigTable.Rows.Select(r => r.Id).OrderBy(x => x).ToArray());
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

    public static IEnumerable<object[]> AllIds() => RigTable.Rows.Select(r => new object[] { r.Id });

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
        if (Environment.GetEnvironmentVariable("RIG_PROBE") == null) return;
        using var still = StillAir();
        AircraftConfig c = Load(id);
        RigSolver.Result r = RigSolver.SolveLevel(c, RigSolver.RigThrottle, altitudeFt * 0.3048);
        Assert.True(r.Converged, $"{id}: level solve at {altitudeFt} ft did not converge");
        (_, Flight f) = Fly(c, r, RigSolver.RigThrottle, 20, 40);
        _out.WriteLine($"{id} @ {altitudeFt} ft: {r.TasMs / 0.514444:F1} KTAS, L {r.Residual[3]:E1} N {r.Residual[5]:E1} W·m; r {f.MaxR:F3} °/s, β {f.MaxBeta:F3}°, bank {f.Bank:F2}°, Δψ {f.HeadingChange:F2}°, Δh {f.AltChange:F1} m");
    }

    public static IEnumerable<object[]> PoweredAtStartAltitudes() =>
        RigTable.Rows.Where(r => r.Powered).SelectMany(r => new[] { new object[] { r.Id, 1500.0 }, new object[] { r.Id, 6500.0 } });

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
/// changed config has to be a deliberate edit in two places. RE-SOLVED 2026-11-03 on lane/claude-roll-only after the
/// aileron-section change (PlainFlap: every aileron is weaker per degree, so the rig needs 8–10 % more of it, the Pitts
/// 37 % more) and the biplane induction, stick fixed at controls neutral, WITHOUT lane/claude-pitts-spin's per-section tail
/// wakes (so the elevator trims barely move); these rows are that RigTool output transcribed. Degrees and knots. PropSign: +1 clockwise-from-behind
/// prop(s) with a net torque, 0 none (glider, jet, counter-rotating twin).
/// </summary>
public static class RigTable
{
    public sealed record Row(string Id, bool Powered, int PropSign, double AileronDeg, double FinDeg, double ElevatorDeg, double Ktas);

    public static readonly Row[] Rows =
    {
        //  id                         powered prop  aileron°  fin°     elevator°  KTAS
        new("aircam-amphib-like", true, 1, 0.2822, 0.2558, -0.5094, 97.07),
        new("aircam-like", true, 1, 0.2710, 0.2393, 0.1977, 99.42),
        new("boeing-737-like", true, 0, 0.0000, 0.0000, 3.8796, 350.22),
        new("c172-like", true, 1, 0.1457, 0.1234, 1.5696, 123.43),
        new("cassutt-f1-like", true, 1, 0.3207, 0.1151, 0.4542, 159.22),
        new("cirrus-sr22-like", true, 1, 0.2155, 0.0949, 2.0738, 162.51),
        new("dc3-like", true, 1, 0.1397, 0.0792, 0.4303, 152.52),
        new("decathlon-8kcab-like", true, 1, 0.2152, 0.2074, 2.0412, 107.77),
        new("dhc2-beaver-floats-like", true, 1, 0.2789, 0.2869, 0.5993, 114.50),
        new("dhc2-beaver-like", true, 1, 0.2404, 0.2420, 0.7176, 123.28),
        new("extra-300-like", true, 1, 0.2033, 0.1697, -1.0710, 160.01),
        new("f86-sabre-like", true, 0, 0.0000, 0.0000, 2.6314, 320.46),
        new("geebee-r2-like", true, 1, 0.7167, 0.3107, -2.0343, 188.13),
        new("glasair3-like", true, 1, 0.4163, 0.0930, 0.0793, 235.86),
        new("glider-2-33-like", false, 0, 0.0000, 0.0000, 0.7389, 41.93),
        new("glider-eb29r-like", false, 0, 0.0000, 0.0000, -3.2809, 62.45),
        new("glider-swift-s1-like", false, 0, 0.0000, 0.0000, -1.0783, 62.89),
        new("great-lakes-like", true, 1, 0.4426, 0.3627, 0.8661, 108.59),
        new("hang-glider-like", false, 0, 0.0000, 0.0000, -15.5066, 21.38),
        new("hughes-h4-like", true, 1, 0.0327, 0.0172, -0.3516, 150.73),
        new("j3-cub-floats-like", true, 1, 0.3611, 0.4589, -1.8821, 55.53),
        new("j3-cub-like", true, 1, 0.2510, 0.3274, -0.1014, 65.54),
        new("jn4-jenny-like", true, 1, 0.4507, 0.5907, 0.3258, 52.47),
        new("p51d-like", true, 1, 0.4746, 0.1274, -1.4291, 209.41),
        new("pa18-bush-like", true, 1, 0.2159, 0.2358, 0.7454, 92.55),
        new("pa18-cub-like", true, 1, 0.2029, 0.2206, 0.9640, 95.66),
        new("pa18-floats-like", true, 1, 0.2377, 0.2610, 0.2775, 88.32),
        new("pa25-pawnee-like", true, 1, 0.2240, 0.2114, 0.9820, 110.26),
        new("pa28-archer-like", true, 1, 0.2659, 0.1872, 0.5881, 108.88),
        new("paraglider-en-b-like", false, 0, 0.0000, 0.0000, 9.5338, 19.74),
        new("pitts-s2b-like", true, 1, 0.3740, 0.5174, -1.1736, 139.34),
        new("seminole-like", true, 0, -0.0032, -0.0001, 1.5883, 143.16),
        new("stearman-pt17-like", true, 1, 0.4737, 0.2727, -0.9230, 107.78),
        new("target-drone-like", true, 1, 0.3207, 0.1151, 0.4542, 159.22),
    };

    public static Row Get(string id) => Rows.Single(r => r.Id == id);
}

