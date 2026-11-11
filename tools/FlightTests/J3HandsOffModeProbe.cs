using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// J3_MODE=1: the J-3 is the last type red on the flown hands-off gate, and after the propeller double-drive fix it
/// is a clean measurement rather than a mismatch — the rig and the flown model agree at the rig point to |L| ≤ 0.005
/// N·m, |M| ≤ 0.001, |N| ≤ 0.005, better than the C-172's own figure, and the aeroplane holds that point exactly
/// (bank −0.001°, beta 0.000, every rate 0.000) before anything grows. So there is nothing left to blame and what is
/// left is an EIGENVALUE: something about this aeroplane is unstable, seeded by round-off.
///
/// <see cref="HandsOffDivergenceProbe.TraceWhatGrows"/> shows the shape. Held at the rig point, stick fixed, in still
/// air, the J-3's beta runs 0.000 → 0.001 → 0.003 → 0.007 → 0.016 → 0.035 → 0.058 at four-second samples: a clean
/// exponential doubling every ~4 s, growth rate about +0.17 /s, with p, q, r, V and alpha all growing with it and
/// then turning over. The C-172 at the same gate holds every number below 1e-3 for the full sixty seconds.
///
/// ALL SIX AXES MOVE TOGETHER, which a symmetric aeroplane cannot do: the longitudinal and lateral sets only couple
/// through an asymmetry, and this one has two of them in its rig — a fin offset of 2.557° (against the ~1.76° Ty
/// measured on his own aeroplane) and 0.105° of rigged aileron — plus a propeller that makes P-factor and swirl.
///
/// THE ABLATIONS BELOW ask which of those the mode needs, each re-rigged on its own config so the comparison is
/// between two aeroplanes that are each in trim, not between a rig and a different aeroplane. The one that matters
/// most: the five types that failed this gate were exactly the five with a FITTED BLADE, i.e. the five whose SHAFT
/// is an integrated state (<see cref="PropModel.IntegratesShaft"/>) rather than the old throttle law — so the first
/// question is whether the shaft is in the loop. Dropping bladePitch75Deg takes the shaft out of the state vector
/// and puts this aeroplane on the C-172's propeller path.
///
/// WHAT IT FOUND (2026-11-11).
///
/// 1. THE MODE NEEDS THE SOLVED DISC'S CROSS-FLOW TERMS, and nothing else does it. Growth rate of |beta| fitted in
///    the linear window (1e-4 to 0.05 deg), J-3, stick fixed, still air, at the rig point:
///      baseline                            +0.1695 /s   doubles in 4.1 s
///      PROP_NOINPLANE=1  (no in-plane force) −0.0026 /s   STABLE
///      PROP_NOPFACTOR=1  (no P-factor offset) +0.0102 /s   16x slower
///    A tractor disc sits ahead of the CG, so its cross-flow force yaws the aeroplane the way the sideslip already
///    points: it is DESTABILISING in yaw, which is real and correctly signed. On the J-3 it beats the fin. That also
///    says why it is this type: the disc's force follows the thrust it is making while the fin's restoring follows
///    q = ½ρV², and the J-3 is the slowest of the fitted-blade types (29 m/s at the rig) with the largest propeller
///    against the smallest fin. A real J-3 is directionally stable, so something here is too big — the disc's
///    cross-flow force, or the arm it swings on. The arm is worth looking at first: the rig's copy of it was wrong
///    until 2026-11-10 (RigSolver.SteadyForces left noseBody at its default, so the arm was −cg instead of
///    nose − cg) and the fix made the two sides AGREE, which does not make either one right.
/// 2. PFactorK AND SlipstreamK ARE DEAD ON THIS TYPE and zeroing them is bit-identical to baseline — the same note
///    PropModel already carries. Use PROP_NOINPLANE / PROP_NOPFACTOR. Anyone ablating the legacy coefficients on a
///    solved type is ablating nothing and will conclude the propeller is innocent.
/// 3. THE SHAFT IS IN THE LOOP but is not the driver: ten times the propeller inertia takes the growth from
///    +0.1695 to +0.1405 /s, and a tenth of it changes nothing (+0.1691).
/// 4. THE 10,000 ft RIG FAILURE IS A PERFORMANCE CEILING, NOT A SOLVER FAULT. The failure walks DOWN in altitude as
///    the throttle comes UP — 0.75 throttle fails from 6,000 ft, 0.85 reaches 8,000, and full throttle converges at
///    12,000 ft on alpha 3.9°. A 65 hp Cub at 75 % has no level-flight solution at 10,000 ft and the solver is right
///    to find none; the C-172 and the Pitts converge at every altitude because they have the power. See
///    <see cref="TheRigAcrossAltitude"/>. Two things ARE defects and neither is the iteration:
///      (a) with no solution to find, the solve returns a CLAMPED non-solution — V pinned at 28.00 m/s, alpha at
///          2.865°, elevator anywhere from 0 to +4.8° — rather than saying there is no level flight here;
///      (b) <c>Result.Converged</c> is TRUE at residuals of 7e-3 (sea level) and 1e-1 (an ablated config) while the
///          tolerance handed to Iterate is 1e-11, so Converged is not a statement about the residual. Read
///          ResidualNorm, never Converged.
///    NOT FIXED HERE, at instruction: the iteration moves every rig in the fleet and must not share a commit.
///
/// Powered types fly at the rig throttle; <c>ControlInputs.ThrottleLever</c> is INVERTED, so that is
/// <c>1 − 2·RigThrottle</c> and NOT 1.0, which would be idle.
/// </summary>
[Collection("WorldTerrainActive")]
public class J3HandsOffModeProbe
{
    private readonly ITestOutputHelper _o;
    public J3HandsOffModeProbe(ITestOutputHelper o) { _o = o; }
    private const double D = 180.0 / Math.PI;

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private static IDisposable StillAir()
    {
        var field = Atmosphere.Field; var wind = Atmosphere.SteadyWind; var turb = Atmosphere.ActiveTurbulence;
        var terrain = WorldTerrain.Active; var ridge = Atmosphere.ActiveRidge; var thermals = Atmosphere.Thermals.ToList();
        Atmosphere.Field = null; Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null;
        WorldTerrain.Active = null; Atmosphere.ActiveRidge = null; Atmosphere.Thermals.Clear();
        return new Restore(() =>
        {
            Atmosphere.Field = field; Atmosphere.SteadyWind = wind; Atmosphere.ActiveTurbulence = turb;
            WorldTerrain.Active = terrain; Atmosphere.ActiveRidge = ridge; Atmosphere.Thermals.AddRange(thermals);
        });
    }
    private sealed class Restore : IDisposable { private readonly Action _a; public Restore(Action a) { _a = a; } public void Dispose() => _a(); }

    /// <summary>The growth rate of the lateral state, fitted where it is still linear.</summary>
    private sealed class Mode
    {
        public double RatePerSec;       // d ln|state| / dt, fitted over the linear window
        public double TBeta01;          // seconds to reach |beta| = 0.1 deg
        public double MaxR, MaxBeta, MaxQ, MaxBank;
        public bool RigConverged;
        public double RigResidual, FinOffsetDeg, AileronRigDeg, ShaftRpm;
    }

    private Mode Run(string id, double seconds, Action<AircraftConfig>? tweak = null, bool trace = false)
    {
        AircraftConfig c = Load(id);
        tweak?.Invoke(c);
        RigSolver.Result r = RigSolver.SolveLevel(c, RigSolver.RigThrottle, RigSolver.RigAltitudeM);
        var m = new Mode
        {
            RigConverged = r.Converged, RigResidual = r.ResidualNorm,
            FinOffsetDeg = r.FinOffsetRad * D, AileronRigDeg = r.AileronRigRad * D,
        };
        Quat att = RigSolver.Attitude(r, 0.0);
        Vec3 vb = new(r.TasMs * Math.Cos(r.AlphaRad), 0, r.TasMs * Math.Sin(r.AlphaRad));
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -r.AltitudeM), att, vb, Vec3.Zero),
                              new ControlDeflections(0, r.ElevatorRad, 0, 0));
        if (c.RetractableGear) ac.SetGear(false, immediate: true);
        double stick = Aircraft.StickForDeflection(r.ElevatorRad, c.Controls.Elevator);
        double lever = 1.0 - 2.0 * RigSolver.RigThrottle;   // INVERTED lever: this is the rig throttle, not idle
        var input = new ControlInputs(0, stick, 0, lever);
        const double dt = SimLoop.DefaultFixedDtSec;
        var pts = new List<(double t, double y)>();
        if (trace) _o.WriteLine("     t      beta        p        q        r      bank    shaftRpm");
        for (double t = 0; t < seconds; t += dt)
        {
            ac.Step(input, dt);
            RigidBodyState st = ac.State;
            double v = st.Velocity.Length;
            double beta = Math.Asin(Math.Clamp(st.Velocity.Y / Math.Max(1e-9, v), -1, 1)) * D;
            Vec3 yb = st.Attitude.Rotate(new Vec3(0, 1, 0));
            double bank = Math.Asin(Math.Clamp(yb.Z, -1, 1)) * D;
            m.MaxBeta = Math.Max(m.MaxBeta, Math.Abs(beta));
            m.MaxR = Math.Max(m.MaxR, Math.Abs(st.Rates.Z * D));
            m.MaxQ = Math.Max(m.MaxQ, Math.Abs(st.Rates.Y * D));
            m.MaxBank = Math.Max(m.MaxBank, Math.Abs(bank));
            m.ShaftRpm = ac.Drive.ShaftRpm;
            if (m.TBeta01 == 0 && Math.Abs(beta) >= 0.1) m.TBeta01 = t;
            // THE FIT WINDOW IS WHERE THE MODE IS STILL LINEAR: |beta| between 1e-4 and 0.05 deg. Below that it is
            // round-off, above it the aeroplane has departed and the rate is no longer the eigenvalue.
            double y = Math.Abs(beta);
            if (y > 1e-4 && y < 0.05) pts.Add((t, Math.Log(y)));
            if (trace && (int)Math.Round(t / dt) % (int)Math.Round(2.0 / dt) == 0)
                _o.WriteLine($"{t,6:F1} {beta,9:F4} {st.Rates.X * D,8:F4} {st.Rates.Y * D,8:F4} {st.Rates.Z * D,8:F4} "
                    + $"{bank,9:F4} {ac.Drive.ShaftRpm,10:F1}");
        }
        if (pts.Count > 20)
        {
            double sx = 0, sy = 0, sxx = 0, sxy = 0; int n = pts.Count;
            foreach ((double x, double yy) in pts) { sx += x; sy += yy; sxx += x * x; sxy += x * yy; }
            double den = n * sxx - sx * sx;
            if (Math.Abs(den) > 1e-12) m.RatePerSec = (n * sxy - sx * sy) / den;
        }
        return m;
    }

    private void Line(string label, Mode m)
        => _o.WriteLine($"  {label,-40} growth {m.RatePerSec,7:F4} /s  (double {(m.RatePerSec > 1e-6 ? (0.693 / m.RatePerSec).ToString("F1") : "-"),6} s)  "
            + $"beta 0.1deg at {(m.TBeta01 > 0 ? m.TBeta01.ToString("F1") : ">win"),5} s  max beta {m.MaxBeta,6:F3} r {m.MaxR,6:F3} q {m.MaxQ,6:F3} bank {m.MaxBank,6:F3}  "
            + $"rig conv {m.RigConverged} |res| {m.RigResidual:E1} fin {m.FinOffsetDeg,6:F3} ail {m.AileronRigDeg,6:F3} rpm {m.ShaftRpm,7:F1}");

    [Fact]
    public void WhatTheModeNeeds()
    {
        if (Environment.GetEnvironmentVariable("J3_MODE") == null) return;
        string[] ids = (Environment.GetEnvironmentVariable("J3_MODE_IDS") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();
        if (ids.Length == 0) ids = new[] { "j3-cub-like", "c172-like" };
        double sec = double.TryParse(Environment.GetEnvironmentVariable("J3_MODE_SEC"), out double s) ? s : 60.0;
        using var still = StillAir();
        foreach (string id in ids)
        {
            _o.WriteLine($"=== {id}");
            Line("baseline", Run(id, sec));
            // IS THE SHAFT IN THE LOOP? Dropping the fitted blade puts this type on the old throttle law, so the
            // shaft stops being an integrated state — the C-172's propeller path, and the thirty types that pass.
            Line("no fitted blade (shaft not a state)", Run(id, sec, c => c.Propulsion!.BladePitch75Deg = 0));
            Line("prop inertia x10 (slow shaft)", Run(id, sec, c => c.Propulsion!.PropInertia *= 10.0));
            Line("prop inertia /10 (fast shaft)", Run(id, sec, c => c.Propulsion!.PropInertia *= 0.1));
            // THE TWO ASYMMETRIES THE PROPELLER MAKES, and the two built into the rig.
            Line("pFactorK = 0", Run(id, sec, c => c.Propulsion!.PFactorK = 0));
            Line("slipstreamK = 0", Run(id, sec, c => c.Propulsion!.SlipstreamK = 0));
            Line("pFactor + slipstream = 0", Run(id, sec, c => { c.Propulsion!.PFactorK = 0; c.Propulsion.SlipstreamK = 0; }));
            Line("no propeller asymmetry at all", Run(id, sec, c =>
            {
                c.Propulsion!.PFactorK = 0; c.Propulsion.SlipstreamK = 0; c.Propulsion.RotationSign = 0;
            }));
        }
    }

    /// <summary>J3_MODE_TRACE=1: the state frame by frame, so the shape is read rather than summarised.</summary>
    [Fact]
    public void TraceIt()
    {
        if (Environment.GetEnvironmentVariable("J3_MODE_TRACE") == null) return;
        string id = Environment.GetEnvironmentVariable("J3_MODE_IDS") ?? "j3-cub-like";
        double sec = double.TryParse(Environment.GetEnvironmentVariable("J3_MODE_SEC"), out double s) ? s : 60.0;
        using var still = StillAir();
        _o.WriteLine($"=== {id} baseline");
        Line("baseline", Run(id, sec, null, trace: true));
    }

    /// <summary>
    /// J3_RIG_ALT=1: the rig solve across altitude. Reported separately and NOT fixed here, because the fix is in
    /// <see cref="RigSolver"/>'s iteration and that would move every rig in the fleet: the J-3's solve does not
    /// converge at 10,000 ft, returning a residual of 2.93E-01 of the weight, which is not a solution at all but a
    /// scan that failed to bracket or step clamps that stalled.
    /// </summary>
    [Fact]
    public void TheRigAcrossAltitude()
    {
        if (Environment.GetEnvironmentVariable("J3_RIG_ALT") == null) return;
        string[] ids = (Environment.GetEnvironmentVariable("J3_MODE_IDS") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();
        if (ids.Length == 0) ids = new[] { "j3-cub-like", "j3-cub-floats-like", "c172-like", "pitts-s2b-like" };
        using var still = StillAir();
        foreach (string id in ids)
        {
            AircraftConfig c = Load(id);
            _o.WriteLine($"=== {id}");
            foreach (double altFt in new[] { 0, 1000, 2000, 4000, 6000, 8000, 10000, 12000 })
            {
                double alt = altFt * 0.3048;
                RigSolver.Result r = RigSolver.SolveLevel(c, RigSolver.RigThrottle, alt);
                _o.WriteLine($"  {altFt,6} ft: converged {r.Converged,5}  |res| {r.ResidualNorm:E2}  V {r.TasMs,6:F2}  "
                    + $"alpha {r.AlphaRad * D,7:F3}  ele {r.ElevatorRad * D,7:F3}  fin {r.FinOffsetRad * D,7:F3}  ail {r.AileronRigRad * D,7:F3}");
            }
            // AND THE SAME ALTITUDE AT MORE POWER. If the failures above are the solver, more throttle will not help;
            // if they are a CEILING, they will walk down in altitude as the throttle comes up.
            foreach (double thr in new[] { 0.75, 0.85, 1.0 })
                foreach (double altFt in new[] { 6000, 8000, 10000, 12000 })
                {
                    RigSolver.Result q = RigSolver.SolveLevel(c, thr, altFt * 0.3048);
                    _o.WriteLine($"  throttle {thr:F2} at {altFt,6} ft: converged {q.Converged,5}  |res| {q.ResidualNorm:E2}  "
                        + $"V {q.TasMs,6:F2}  alpha {q.AlphaRad * D,7:F3}");
                }
        }
    }
}
