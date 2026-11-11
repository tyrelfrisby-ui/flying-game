using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// HANDSOFF_DIV=1: WHAT GROWS, FROM A STATE TRIMMED TO 1e-13?
///
/// <see cref="RigTests.HandsOffLevelAtRigPower"/> was failing on five of thirty types — j3-cub, j3-cub-floats,
/// great-lakes, pitts-s2b, extra-300 — and nothing else in a fleet of thirty-five, with three other test classes
/// reporting the same five. That gate flies STICK FIXED (<see cref="ControlInputs"/>'s four-argument constructor
/// leaves every Free flag false), so the free-floating elevator was never in it whatever it does to the 2-33.
///
/// This probe does not assert. It prints the trajectory of the state itself, so the SHAPE of the growth can be
/// read: an exponential from a round-off seed is an eigenvalue, a step is a discontinuity in the model, and a
/// ramp that starts at t = 0 is a residual the trim did not actually zero.
/// HANDSOFF_DIV_IDS=&lt;id&gt;[,...] scopes it; the default is the five red types plus the C-172 and the Decathlon,
/// which pass, as controls.
///
/// WHAT IT FOUND. The five were exactly the five types with a FITTED BLADE (propulsion.bladePitch75Deg, or
/// constantSpeed with fineStopDeg) and nothing else; the thirty that pass are the thirty still on the old throttle
/// law, which is also why the C-172 is immune rather than merely lucky. <see cref="TheRigsMomentAgainstTheFlownOne"/>
/// is the measurement: the rig solves <see cref="PropModel.SteadyDrive"/> and the sim flies
/// <c>AircraftEngine.Drive</c>, and for a fitted type those were three different disagreements at once —
///   1. <c>AircraftEngine.SolvesShaft</c> gated on <c>!GovernorHasOil</c>, so a governing constant-speed type never
///      integrated its shaft and <see cref="PropModel.IsSolved"/> came out TRUE in the solve and FALSE in flight:
///      two different propellers AND two different fins, since that predicate also decides whether the fin stands in
///      the swirl. Pitts L −577 N·m / N +512, Extra L −418 / N +844, Great Lakes L −300 / N +664.
///   2. the shaft's seed was <c>max(IdleRpm, 500)</c>, so every spawn started the propeller at idle and spooled up —
///      the J-3 at 724 rpm against the rig's 2,129.
///   3. <see cref="PropModel.SteadyDrive"/> fed <see cref="PistonEngine"/> the DENSITY ratio where the parameter is
///      the PRESSURE ratio, and <see cref="RigSolver.SteadyForces"/> left <c>noseBody</c> at its default so the arm
///      the disc's in-plane force swings on was −cg instead of nose − cg.
/// With all three fixed the moment the flown model applies that the solve did not see is |L| ≤ 0.05 N·m, |M| ≤ 0.002
/// and |N| ≤ 0.04 on every one of the five — the C-172's own figure, where it had been hundreds.
///
/// AND A MEASUREMENT TRAP WORTH KEEPING: the "pitch" half of this was never pitch. <see cref="RigTests.Fly"/>
/// re-baselines bank at t = settle, so the Pitts — already at 49° of bank and turning by 20 s — reported "bank
/// −0.10°" with a q of 7.1 °/s that was only the pitch rate of a banked turn. All five failed LATERALLY. Trace the
/// state, not the summary.
/// </summary>
[Collection("WorldTerrainActive")]
public class HandsOffDivergenceProbe
{
    private readonly ITestOutputHelper _o;
    public HandsOffDivergenceProbe(ITestOutputHelper o) { _o = o; }
    private const double D = 180.0 / Math.PI;
    private double _every = 0.5;

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

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

    private static readonly string[] Default =
    {
        "pitts-s2b-like", "extra-300-like", "j3-cub-like", "j3-cub-floats-like", "great-lakes-like",
        "c172-like", "decathlon-8kcab-like",
    };

    /// <summary>HANDSOFF_BUDGET=1: the ROLLING and YAWING moment the rig solver believes in at its own solution,
    /// against the one the flown model produces at the identical state, with the flown model's own per-contributor
    /// breakdown beside it. The rig is only an equilibrium of the aeroplane if the two agree.</summary>
    [Fact]
    public void TheRigsMomentAgainstTheFlownOne()
    {
        if (Environment.GetEnvironmentVariable("HANDSOFF_BUDGET") == null) return;
        string[] ids = (Environment.GetEnvironmentVariable("HANDSOFF_DIV_IDS") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();
        if (ids.Length == 0) ids = Default;
        using var still = StillAir();
        foreach (string id in ids)
        {
            AircraftConfig c = Load(id);
            RigSolver.Result r = c.Propulsion is null
                ? RigSolver.SolveGlide(c, RigSolver.RigGlideTasAt(c, RigSolver.RigAltitudeM), RigSolver.RigAltitudeM)
                : RigSolver.SolveLevel(c, RigSolver.RigThrottle, RigSolver.RigAltitudeM);
            double rho = Atmosphere.DensityAtAltitude(r.AltitudeM);
            double thr = c.Propulsion is null ? 0.0 : RigSolver.RigThrottle;
            Vec3 vb = new(r.TasMs * Math.Cos(r.AlphaRad), 0, r.TasMs * Math.Sin(r.AlphaRad));
            var ctl = new ControlDeflections(0, r.ElevatorRad, 0, 0);
            (Vec3 sf, Vec3 sm) = RigSolver.SteadyForces(c, Aircraft.BuildAirfoilTables(c), thr, vb, rho, ctl);
            Quat att = RigSolver.Attitude(r, 0.0);
            var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -r.AltitudeM), att, vb, Vec3.Zero), ctl);
            if (c.RetractableGear) ac.SetGear(false, immediate: true);
            // DELIBERATELY NOT read through ForceSample / ForcesAtCurrentState. Summing the samples as every
            // consumer does — Cross(pos - cg, force) + moment — gave the C-172 L +423 N.m at a state it holds
            // perfectly still, which is the propeller's moment being charged twice (fixed on the lane in 01cba1c5,
            // and the same shape of lie as the phantom "tailflow" pitch term on 11-09). The moment below is read
            // back out of the RIGID BODY instead, through the inertia tensor, so it is the moment the equations of
            // motion actually integrated and cannot be a bookkeeping artefact.
            _o.WriteLine($"=== {id}: rig |res| {r.ResidualNorm:E2}, V {r.TasMs:F2}, alpha {r.AlphaRad * D:F3} deg");
            _o.WriteLine($"    rig  SteadyForces   L {sm.X,12:F3}  M {sm.Y,12:F3}  N {sm.Z,12:F3} N.m   (Fy {sf.Y,10:F3} N)");
            // What the FLOWN model actually produced, read back out of the rigid body: one step from rest at the rig
            // state, so the rates it has at the end are the moment the model applied, through the inertia tensor.
            MassProperties mp = ac.MassProperties;
            ac.Step(new ControlInputs(0, Aircraft.StickForDeflection(r.ElevatorRad, c.Controls.Elevator), 0,
                                      c.Propulsion is null ? 0.0 : 1.0 - 2.0 * RigSolver.RigThrottle), SimLoop.DefaultFixedDtSec);
            Vec3 w = ac.State.Rates * (1.0 / SimLoop.DefaultFixedDtSec);
            Vec3 flown = new(mp.Ixx * w.X + mp.Ixz * w.Z, mp.Iyy * w.Y, mp.Izz * w.Z + mp.Ixz * w.X);
            _o.WriteLine($"    flown  step 1       L {flown.X,12:F3}  M {flown.Y,12:F3}  N {flown.Z,12:F3} N.m");
            _o.WriteLine($"    DIFFERENCE          L {flown.X - sm.X,12:F3}  M {flown.Y - sm.Y,12:F3}  N {flown.Z - sm.Z,12:F3} N.m");
            // Where the rig's own call and the flown call differ in their ARGUMENTS, one at a time: a fresh
            // StripFlowState (what a spawned Aircraft carries) is the only one that can change a moment in still air.
            PropModel.PropDrive drive = PropModel.SteadyFlownDrive(c.Propulsion, thr, vb.X, rho);
            bool solved = c.Propulsion is not null && PropModel.IsSolved(c.Propulsion, drive);
            var prof = new BladeElement.SwirlProfile();
            (double du, double rr, double swirl) = Aircraft.TailSlipstream(c, thr, vb, Vec3.Zero, rho, drive, false, prof);
            var tables = Aircraft.BuildAirfoilTables(c);
            int n = c.Surfaces.Sum(x => x.Strips.Count);
            var fresh = new StripFlowState(); fresh.EnsureSize(n);
            (Vec3 _, Vec3 mNull) = AeroModel.Compute(c, tables, vb, Vec3.Zero, Vec3.Zero, rho, ctl, -1.0, null, du, rr, null, null, null, double.PositiveInfinity, swirl, default, solved, prof);
            (Vec3 _, Vec3 mFlow) = AeroModel.Compute(c, tables, vb, Vec3.Zero, Vec3.Zero, rho, ctl, -1.0, fresh, du, rr, null, null, Vec3.Zero, double.PositiveInfinity, swirl, default, solved, prof);
            _o.WriteLine($"    aero flowState null L {mNull.X,12:F3}  M {mNull.Y,12:F3}  N {mNull.Z,12:F3}");
            _o.WriteLine($"    aero flowState new  L {mFlow.X,12:F3}  M {mFlow.Y,12:F3}  N {mFlow.Z,12:F3}");
            // THE PROPELLER EACH SIDE BELIEVES IN. The rig solves PropModel.SteadyDrive; the sim flies
            // AircraftEngine.Drive.
            if (c.Propulsion is not null)
            {
                PropModel.PropDrive fd = ac.Drive;
                _o.WriteLine($"    rig   drive: shaft {drive.ShaftRpm,8:F1} rpm, blade {drive.BladePitchDeg,6:F2} deg, scale {drive.PowerScale:F3}, solved {PropModel.IsSolved(c.Propulsion, drive)}");
                _o.WriteLine($"    flown drive: shaft {fd.ShaftRpm,8:F1} rpm, blade {fd.BladePitchDeg,6:F2} deg, scale {fd.PowerScale:F3}, solved {PropModel.IsSolved(c.Propulsion, fd)}");
            }
        }
    }

    [Fact]
    public void TraceWhatGrows()
    {
        if (Environment.GetEnvironmentVariable("HANDSOFF_DIV") == null) return;
        string[] ids = (Environment.GetEnvironmentVariable("HANDSOFF_DIV_IDS") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();
        if (ids.Length == 0) ids = Default;
        double dur = double.TryParse(Environment.GetEnvironmentVariable("HANDSOFF_DIV_SEC"), out double s) ? s : 60.0;
        _every = double.TryParse(Environment.GetEnvironmentVariable("HANDSOFF_DIV_EVERY"), out double e) ? e : 0.5;
        bool free = Environment.GetEnvironmentVariable("HANDSOFF_DIV_FREE") == "1";
        using var still = StillAir();
        foreach (string id in ids) Trace(id, dur, free);
    }

    private void Trace(string id, double dur, bool free)
    {
        AircraftConfig c = Load(id);
        RigSolver.Result r = c.Propulsion is null
            ? RigSolver.SolveGlide(c, RigSolver.RigGlideTasAt(c, RigSolver.RigAltitudeM), RigSolver.RigAltitudeM)
            : RigSolver.SolveLevel(c, RigSolver.RigThrottle, RigSolver.RigAltitudeM);
        _o.WriteLine($"=== {id} stick {(free ? "FREE" : "FIXED")}: rig converged {r.Converged}, |residual| {r.ResidualNorm:E2}, "
            + $"V {r.TasMs:F2} m/s, alpha {r.AlphaRad * D:F3} deg, theta {r.ThetaRad * D:F3} deg, elevator {r.ElevatorRad * D:F3} deg");
        Quat att = RigSolver.Attitude(r, 0.0);
        Vec3 vb = new(r.TasMs * Math.Cos(r.AlphaRad), 0, r.TasMs * Math.Sin(r.AlphaRad));
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -r.AltitudeM), att, vb, Vec3.Zero), new ControlDeflections(0, r.ElevatorRad, 0, 0));
        if (c.RetractableGear) ac.SetGear(false, immediate: true);
        double stick = Aircraft.StickForDeflection(r.ElevatorRad, c.Controls.Elevator);
        double lever = c.Propulsion is null ? 0.0 : 1.0 - 2.0 * RigSolver.RigThrottle;
        var input = free
            ? new ControlInputs(0, stick, 0, lever, true, true, true)
            : new ControlInputs(0, stick, 0, lever);
        const double dt = SimLoop.DefaultFixedDtSec;
        _o.WriteLine("    t     V     alpha    beta     p       q       r      bank   pitch   head    dAlt    ele     ail     rud    shaftRpm  blade");
        for (double t = 0; t < dur + 1e-9; t += dt)
        {
            if ((int)Math.Round(t / dt) % Math.Max(1, (int)Math.Round(_every / dt)) == 0)
            {
                RigidBodyState st = ac.State;
                double v = st.Velocity.Length;
                double alpha = Math.Atan2(st.Velocity.Z, st.Velocity.X) * D;
                double beta = Math.Asin(Math.Clamp(st.Velocity.Y / Math.Max(1e-9, v), -1, 1)) * D;
                Vec3 yb = st.Attitude.Rotate(new Vec3(0, 1, 0));
                Vec3 xb = st.Attitude.Rotate(new Vec3(1, 0, 0));
                double bank = Math.Asin(Math.Clamp(yb.Z, -1, 1)) * D;
                double pitch = Math.Asin(Math.Clamp(-xb.Z, -1, 1)) * D;
                double head = Math.Atan2(xb.Y, xb.X) * D;
                ControlDeflections d = ac.CurrentDeflections;
                _o.WriteLine($"{t,6:F1} {v,6:F2} {alpha,7:F3} {beta,7:F3} {st.Rates.X * D,7:F3} {st.Rates.Y * D,7:F3} {st.Rates.Z * D,7:F3} "
                    + $"{bank,7:F3} {pitch,7:F3} {head,7:F3} {-st.Position.Z - r.AltitudeM,8:F2} {d.ElevatorRad * D,7:F3} {d.AileronRad * D,7:F3} {d.RudderRad * D,7:F3} {ac.Drive.ShaftRpm,9:F1} {ac.Drive.BladePitchDeg,6:F2}");
            }
            ac.Step(input, dt);
        }
    }
}
