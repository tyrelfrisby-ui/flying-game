using System.Globalization;
using System.Text;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.Aero.Wake;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The trailing-vortex wake (docs/WAKE-VORTEX.md, Ty 2026-11-05: "no lift no vortex, lots of AOA/lift lots of vortex";
/// "formation flying should realistically roll the following plane"; "the glider will box the wake"). Nothing here
/// asks the wake for a rolling moment: the follower is a whole Aircraft whose strips meet the air the leader's lines
/// move, and the roll is what its own wing does with that air.
///
/// Set WAKE_PLOT_DIR to have the runs write their curves as CSV (tools/wake/plot_wake_tests.py draws them).
/// </summary>
public class WakeVortexTests
{
    private readonly ITestOutputHelper _o;
    public WakeVortexTests(ITestOutputHelper o) { _o = o; }

    private const double AltM = 1000.0;
    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private static void Clean()
    {
        VortexWake.Clear();
        Atmosphere.SteadyWind = Vec3.Zero;
        Atmosphere.ActiveTurbulence = null;
        VortexWake.GroundOverride = (_, _) => 0.0;   // open sea level under everything: no terrain in these runs
    }

    private static void Csv(string name, string header, IEnumerable<string> rows)
    {
        string? dir = Environment.GetEnvironmentVariable("WAKE_PLOT_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder(header).Append('\n');
        foreach (string r in rows) sb.Append(r).Append('\n');
        File.WriteAllText(Path.Combine(dir, name), sb.ToString());
    }
    private static string F(double v) => v.ToString("G6", CultureInfo.InvariantCulture);

    /// <summary>A kinematic leader: straight and level along +x at <paramref name="v"/>, n = <paramref name="nz"/>.</summary>
    private static void FlyLeader(WakeSource src, ref Vec3 pos, double v, double nz, double weightN, double seconds, double dt)
    {
        for (double t = 0; t < seconds - 1e-9; t += dt)
        {
            pos += new Vec3(v * dt, 0, 0);
            src.ShedFromLoadFactor(dt, pos, Quat.Identity, new Vec3(v, 0, 0), nz, weightN, Atmosphere.DensityAt(pos));
            VortexWake.Advance(dt);
        }
    }

    // ------------------------------------------------------------------ lift makes the vortex

    [Fact]
    public void CirculationIsTheWingsOwnLift()
    {
        Clean();
        try
        {
            AircraftConfig c = Load("glider-2-33-like");
            TrimSolver.Result trim = TrimSolver.SolveGliderTrim(c, 22.0, AltM);
            Assert.True(trim.Converged);
            Quat att = new(0, Math.Sin(trim.ThetaRad / 2), 0, Math.Cos(trim.ThetaRad / 2));
            var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -AltM), att, att.Conjugate().Rotate(new Vec3(22.0 * Math.Cos(trim.ThetaRad - trim.AlphaRad), 0, 22.0 * Math.Sin(trim.ThetaRad - trim.AlphaRad))), Vec3.Zero),
                new ControlDeflections(0, trim.ElevatorRad, 0, 0));
            double span = AeroModel.WingSpan(c);
            ac.Wake = VortexWake.CreateSource("2-33", span);
            var loop = new SimLoop(ac);
            for (int i = 0; i < 400; i++) { ac.StepWithDeflectionTargets(new ControlDeflections(0, trim.ElevatorRad, 0, 0), SimLoop.DefaultFixedDtSec); VortexWake.Advance(SimLoop.DefaultFixedDtSec); }

            double v = ac.State.Velocity.Length, rho = Atmosphere.DensityAt(ac.State.Position);
            double w = ac.MassProperties.MassKg * Atmosphere.GravityMs2;
            double expected = w * Math.Cos(Math.Atan(1.0 / Math.Max(1.0, trim.GlideRatio))) / (rho * v * Math.PI / 4.0 * span);
            double got = ac.Wake.Head.Gamma;
            _o.WriteLine($"2-33: span {span:F2} m, m {ac.MassProperties.MassKg:F0} kg, V {v:F2} m/s, rho {rho:F4}; Γ {got:F3} m²/s, W cosγ/(ρVb0) {expected:F3}");
            Assert.InRange(got / expected, 0.95, 1.05);
        }
        finally { Clean(); VortexWake.GroundOverride = null; }
    }

    [Fact]
    public void NoLiftNoVortexAndMoreLiftMoreVortex()
    {
        Clean();
        try
        {
            const double v = 30.0, m = 900.0, span = 11.0;
            double w = m * Atmosphere.GravityMs2;
            var gammas = new List<string>();
            double G(double nz)
            {
                VortexWake.Clear(); VortexWake.GroundOverride = (_, _) => 0.0;
                WakeSource s = VortexWake.CreateSource($"n{nz}", span);
                Vec3 p = new(0, 0, -AltM);
                FlyLeader(s, ref p, v, nz, w, 4.0, 0.02);
                double probe = VortexWake.SampleVelocity(p + new Vec3(-60, 0.5 * WakeVortex.SpanFactor * span, 0)).Length;
                gammas.Add($"{F(nz)},{F(s.Head.Gamma)},{F(probe)}");
                return s.Head.Gamma;
            }
            // A pushover at exactly zero g (the whole aeroplane ballistic): nothing to shed.
            Assert.Equal(0.0, G(0.0), 9);
            var segs = new List<VortexSegment>();
            Assert.Equal(0, VortexWake.GetActiveSegments(segs));
            Assert.Equal(0.0, VortexWake.SampleVelocity(new Vec3(-60, 4.3, -AltM)).Length, 9);

            double g1 = G(1.0), g2 = G(2.0), g4 = G(4.0);
            // By hand: Γ = n·W/(ρ V π/4 b) = 8826/(1.1117 · 30 · 8.639) = 30.6 m²/s at n = 1 (ρ ISA 1000 m = 1.1117).
            Assert.InRange(g1, 30.4, 30.8);
            Assert.Equal(2.0, g2 / g1, 6);
            Assert.Equal(4.0, g4 / g1, 6);
            // Inverted (n = −1): the same strength, the other sense.
            Assert.Equal(g1, G(-1.0), 6);
            Csv("wake_gamma_vs_load.csv", "nz,gamma_m2s,vel_at_core_60m_ms", gammas);
            _o.WriteLine(string.Join("\n", gammas));
        }
        finally { Clean(); VortexWake.GroundOverride = null; }
    }

    [Fact]
    public void ZeroLiftTrackFromItsOwnAccelerationShedsNothing()
    {
        // A peer seen only as poses: a ballistic arc (a = g) has no lift; the same path held level has n = 1.
        Clean();
        try
        {
            WakeSource s = VortexWake.CreateSource("peer", 10.0);
            Vec3 p = new(0, 0, -AltM), vel = new(40, 0, -10);
            for (int i = 0; i < 150; i++)
            {
                vel += new Vec3(0, 0, Atmosphere.GravityMs2 * 0.02);
                p += vel * 0.02;
                s.ShedKinematic(0.02, p, Quat.Identity, vel, Vec3.Zero, 800.0, 1.11);
                VortexWake.Advance(0.02);
            }
            Assert.True(s.Head.Gamma < 0.05, $"ballistic arc shed Γ {s.Head.Gamma:F3}");
            WakeSource lvl = VortexWake.CreateSource("level", 10.0);
            Vec3 q = new(0, 500, -AltM), vl = new(40, 0, 0);
            for (int i = 0; i < 150; i++) { q += vl * 0.02; lvl.ShedKinematic(0.02, q, Quat.Identity, vl, Vec3.Zero, 800.0, 1.11); VortexWake.Advance(0.02); }
            double expect = 800.0 * Atmosphere.GravityMs2 / (1.11 * 40 * Math.PI / 4 * 10.0);
            Assert.InRange(lvl.Head.Gamma / expect, 0.98, 1.02);
        }
        finally { Clean(); VortexWake.GroundOverride = null; }
    }

    // ------------------------------------------------------------------ the follower rolls

    /// <summary>A 2-33 trimmed in a glide, placed with its CG on one of a leading 2-33's lines 4 s back (≈ 90 m, six
    /// spans), flies 0.8 s hands-off against the same aeroplane with the wake switched off. The right-hand line's
    /// circulation points aft, so it turns counter-clockwise seen from behind: up outboard, down inboard. A follower
    /// centred on it has its right wing in the upwash and its left in the downwash and must roll LEFT, and on the
    /// left-hand line RIGHT: in both cases the sense of the vortex's own rotation.</summary>
    [Theory]
    [InlineData(+1)]
    [InlineData(-1)]
    public void FollowerOnALineRollsWithTheVortex(int side)
    {
        Clean();
        try
        {
            AircraftConfig c = Load("glider-2-33-like");
            double span = AeroModel.WingSpan(c), v = 22.0;
            TrimSolver.Result trim = TrimSolver.SolveGliderTrim(c, v, AltM);
            double gam = trim.ThetaRad - trim.AlphaRad;   // flight-path angle (negative: descending)
            Quat att = new(0, Math.Sin(trim.ThetaRad / 2), 0, Math.Cos(trim.ThetaRad / 2));
            var trimD = new ControlDeflections(0, trim.ElevatorRad, 0, 0);

            WakeSource lead = VortexWake.CreateSource("leader", span);
            double weight = (c.Mass.MassKg) * Atmosphere.GravityMs2;
            Vec3 lp = new(0, 0, -AltM);
            FlyLeader(lead, ref lp, v, 1.0, weight, 8.0, 0.02);

            // Where the line is 4 s back: the station whose age is nearest 4 s.
            WakeStation st = lead.Station(0);
            for (int k = 0; k < lead.StationCount; k++) if (Math.Abs(lead.Station(k).AgeSec - 4.0) < Math.Abs(st.AgeSec - 4.0)) st = lead.Station(k);
            Vec3 core = side > 0 ? st.R : st.L;   // R = right wing's line (lift up, heading +x: g = +y)
            Assert.True(side > 0 ? core.Y > 0 : core.Y < 0);

            Aircraft Make(bool feels)
            {
                var a = new Aircraft(c, new RigidBodyState(core, att, att.Conjugate().Rotate(new Vec3(v * Math.Cos(gam), 0, -v * Math.Sin(gam))), Vec3.Zero), trimD)
                { FeelsWake = feels };
                return a;
            }
            Aircraft inWake = Make(true), clean = Make(false);
            Vec3 vortAtCg = VortexWake.SampleVorticity(core);
            var rows = new List<string>();
            double dt = SimLoop.DefaultFixedDtSec;
            for (int i = 0; i < 160; i++)
            {
                inWake.StepWithDeflectionTargets(trimD, dt);
                clean.StepWithDeflectionTargets(trimD, dt);
                lp += new Vec3(v * dt, 0, 0);
                lead.ShedFromLoadFactor(dt, lp, Quat.Identity, new Vec3(v, 0, 0), 1.0, weight, Atmosphere.DensityAt(lp));
                VortexWake.Advance(dt);
                (double rW, _, _) = FormationPilot.Euler(inWake.State.Attitude);
                (double rC, _, _) = FormationPilot.Euler(clean.State.Attitude);
                rows.Add($"{F((i + 1) * dt)},{F(rW * 180 / Math.PI)},{F(rC * 180 / Math.PI)},{F(inWake.State.Rates.X * 180 / Math.PI)}");
            }
            (double roll, _, _) = FormationPilot.Euler(inWake.State.Attitude);
            (double roll0, _, _) = FormationPilot.Euler(clean.State.Attitude);
            double dRollDeg = (roll - roll0) * 180 / Math.PI;
            double sense = Math.Sign(vortAtCg.X);   // the vortex's spin about the flight direction (+x)
            _o.WriteLine($"side {side}: Γ {st.Gamma:F2} m²/s rc {st.Rc:F2} m, ω·x̂ {vortAtCg.X:F2} 1/s; Δroll after 0.8 s {dRollDeg:F1}°, p {inWake.State.Rates.X * 57.3:F1} °/s");
            Csv($"wake_follower_roll_{(side > 0 ? "right" : "left")}_line.csv", "t_s,roll_in_wake_deg,roll_clean_deg,p_in_wake_degs", rows);
            Assert.Equal(-side, (int)sense);
            Assert.Equal(sense, Math.Sign(dRollDeg));
            // Strip theory by hand, for a follower of span b centred on a line far from its partner: the line's
            // w(y) = Γ/(2πy) gives a rolling moment q·a·c·Γ/(2πV)·b, the follower's own roll damping q·a·c·p·b³/(12V);
            // they balance at p = 6Γ/(π b²), whatever the speed, chord or lift slope. 5.2 °/s here (Γ 11.5, b 15.5):
            // a like-sized follower is rolled firmly, not thrown; one of half the span would be rolled four times as fast.
            // The slack covers the finite core, the partner line 12 m away, tip loss and the tail.
            double pExpect = 6.0 * st.Gamma / (Math.PI * span * span);
            _o.WriteLine($"  p {inWake.State.Rates.X:F4} rad/s vs strip theory 6Γ/(πb²) = {pExpect:F4}");
            Assert.InRange(Math.Abs(inWake.State.Rates.X) / pExpect, 0.65, 1.35);
        }
        finally { Clean(); VortexWake.GroundOverride = null; }
    }

    [Fact]
    public void FollowerBetweenTheLinesSinksAndDoesNotRoll()
    {
        Clean();
        try
        {
            AircraftConfig c = Load("glider-2-33-like");
            double span = AeroModel.WingSpan(c), v = 22.0;
            TrimSolver.Result trim = TrimSolver.SolveGliderTrim(c, v, AltM);
            double gam = trim.ThetaRad - trim.AlphaRad;
            Quat att = new(0, Math.Sin(trim.ThetaRad / 2), 0, Math.Cos(trim.ThetaRad / 2));
            var trimD = new ControlDeflections(0, trim.ElevatorRad, 0, 0);
            WakeSource lead = VortexWake.CreateSource("leader", span);
            Vec3 lp = new(0, 0, -AltM);
            FlyLeader(lead, ref lp, v, 1.0, c.Mass.MassKg * Atmosphere.GravityMs2, 8.0, 0.02);
            WakeStation st = lead.Station(10);   // 4 s back
            double wMid = VortexWake.SampleVelocity(st.Mid).Z, wExpect = 2.0 * st.Gamma / (Math.PI * st.B0);
            Aircraft Make(bool feels) => new(c, new RigidBodyState(st.Mid, att, att.Conjugate().Rotate(new Vec3(v * Math.Cos(gam), 0, -v * Math.Sin(gam))), Vec3.Zero), trimD) { FeelsWake = feels };
            Aircraft a = Make(true), b = Make(false);
            for (int i = 0; i < 200; i++) { a.StepWithDeflectionTargets(trimD, 0.005); b.StepWithDeflectionTargets(trimD, 0.005); }
            double sink = (a.State.Position.Z - b.State.Position.Z);
            (double r, _, _) = FormationPilot.Euler(a.State.Attitude);
            _o.WriteLine($"between the lines: extra sink {sink:F2} m in 1 s, roll {r * 57.3:F2}°");
            // Between the cores the air goes down at 2Γ/(π b₀) (both lines, b₀/2 away each): ≈ 0.6 m/s here. The
            // follower's own span (15.5 m) is wider than b₀ (12.2 m), so its tips sit in the upwash outboard of the
            // cores and the net loss of lift is a fraction of that.
            _o.WriteLine($"  downwash at the centre {wMid:F3} m/s vs 2Γ/(πb0) {wExpect:F3}");
            Assert.InRange(wMid / wExpect, 0.9, 1.05);
            Assert.True(sink > 0.1, $"extra sink {sink:F2} m");
            Assert.True(Math.Abs(r * 57.3) < 1.0);
        }
        finally { Clean(); VortexWake.GroundOverride = null; }
    }

    // ------------------------------------------------------------------ the wake itself

    /// <summary>
    /// A C172-sized pair (1043 kg, 11 m, 50 m/s) left behind and watched for two minutes, in calm air and in light
    /// turbulence. Descent is not written anywhere in the model: each line moves with what the other induces, so the
    /// first seconds must sink at w₀ = Γ₀/(2π b₀); then the pair must hold its circulation, link, and die — sooner in
    /// turbulence — and stop sinking as it dies.
    /// </summary>
    [Fact]
    public void PairDescendsAtItsOwnInductionThenDecays()
    {
        var rows = new List<string>();
        (double w0, double early, double demiseCalm, double tHalf, double sinkLate) Run(double eps, string tag)
        {
            Clean();
            VortexWake.EddyDissipationOverride = eps;
            const double v = 50.0, m = 1043.0, span = 11.0;
            WakeSource s = VortexWake.CreateSource(tag, span);
            Vec3 p = new(0, 0, -AltM);
            FlyLeader(s, ref p, v, 1.0, m * Atmosphere.GravityMs2, 20.0, 0.02);
            s.Detach();
            // Watch the station 10 s along (k counts from the newest, every 0.4 s): well away from both ends.
            int k = 25;
            WakeStation s0 = s.Station(k);
            double b0 = s0.B0, w0 = s0.Gamma0 / (2 * Math.PI * b0);
            double z0 = s0.Mid.Z, t0 = s0.AgeSec, early = double.NaN, tHalf = double.NaN, zPrev = z0, sinkLate = 0;
            for (int n = 0; n < 3000; n++)
            {
                VortexWake.Advance(0.05);
                if (s.StationCount <= k) break;
                WakeStation x = s.Station(k);
                if (double.IsNaN(early) && x.AgeSec - t0 >= 5.0) early = (x.Mid.Z - z0) / (x.AgeSec - t0);
                if (double.IsNaN(tHalf) && x.Gamma < 0.5 * x.Gamma0) tHalf = x.AgeSec;
                if (n % 20 == 0)
                    rows.Add($"{tag},{F(x.AgeSec)},{F(-x.Mid.Z)},{F(x.Gamma)},{F(x.Rc)},{F(x.R.Y - x.L.Y)}");
                sinkLate = (x.Mid.Z - zPrev) / 0.05; zPrev = x.Mid.Z;
            }
            _o.WriteLine($"{tag}: ε {eps:G2}, Γ0 {s0.Gamma0:F2}, w0 {w0:F3} m/s, t0 {s0.T0Sec:F1} s, demise {s0.DemiseAgeSec:F1} s; early sink {early:F3} m/s, Γ half at {tHalf:F1} s");
            return (w0, early, s0.DemiseAgeSec, tHalf, sinkLate);
        }
        try
        {
            var calm = Run(WakeVortex.CalmEddyDissipation, "calm");
            var turb = Run(1e-2, "turbulent");
            Csv("wake_decay_descent.csv", "case,age_s,alt_m,gamma_m2s,core_m,separation_m", rows);
            // Emergent descent within 10 % of Γ₀/(2π b₀) (finite window and a 5 s average are the slack).
            Assert.InRange(calm.early / calm.w0, 0.9, 1.1);
            Assert.InRange(turb.early / turb.w0, 0.9, 1.1);
            // Turbulence links the pair sooner.
            Assert.True(turb.tHalf < calm.tHalf, $"turbulent half-life {turb.tHalf:F1} s vs calm {calm.tHalf:F1} s");
            Assert.False(double.IsNaN(calm.tHalf), "calm pair never decayed in 150 s");
        }
        finally { Clean(); VortexWake.GroundOverride = null; }
    }

    [Fact]
    public void WindCarriesTheWake()
    {
        Clean();
        try
        {
            VortexWake.WindOverride = _ => new Vec3(0, 5, 0);   // 5 m/s from the west
            WakeSource s = VortexWake.CreateSource("x", 11.0);
            Vec3 p = new(0, 0, -AltM);
            FlyLeader(s, ref p, 50, 1.0, 10000, 4.0, 0.02);
            s.Detach();
            double y0 = s.Station(5).Mid.Y;
            for (int i = 0; i < 200; i++) VortexWake.Advance(0.05);
            Assert.InRange(s.Station(5).Mid.Y - y0, 49.0, 51.0);
        }
        finally { Clean(); VortexWake.GroundOverride = null; }
    }

    [Fact]
    public void NearTheGroundThePairSpreadsAndStopsSinking()
    {
        Clean();
        try
        {
            VortexWake.GroundOverride = (_, _) => AltM - 12.0;   // ground 12 m under the flight path (≈ 1.4 b₀)
            WakeSource s = VortexWake.CreateSource("x", 11.0);
            Vec3 p = new(0, 0, -AltM);
            FlyLeader(s, ref p, 50, 1.0, 1043 * Atmosphere.GravityMs2, 10.0, 0.02);
            s.Detach();
            double sep0 = s.Station(10).R.Y - s.Station(10).L.Y;
            for (int i = 0; i < 600; i++) VortexWake.Advance(0.05);
            WakeStation x = s.Station(10);
            double agl = -x.Mid.Z - (AltM - 12.0);
            _o.WriteLine($"ground: separation {sep0:F1} → {x.R.Y - x.L.Y:F1} m, height {agl:F1} m");
            Assert.True(x.R.Y - x.L.Y > sep0 * 1.3, "the pair should spread over the ground");
            Assert.True(agl > 0.0);
        }
        finally { Clean(); VortexWake.GroundOverride = null; }
    }

    // ------------------------------------------------------------------ the tow

    /// <summary>
    /// Boxing the wake. A Pawnee at tow power and 30 m/s, and the cross-section of air 61 m (2 s) behind it, where the
    /// 2-33 flies: what a whole 2-33 placed at each point there does in its first 0.25 s, against the same glider in
    /// clean air (roll acceleration, extra sink). The map is written to wake_tow_box.csv. Asserted: straight in the wake
    /// the glider sinks and feels the slipstream's push; a span to either side it rolls (away from the centre, outboard
    /// of a line), and a couple of metres above the wake (high tow) it is nearly clean.
    /// </summary>
    [Fact]
    public void TowedGliderFindsTheWakeBox()
    {
        Clean();
        try
        {
            AircraftConfig tugC = Load("pa25-pawnee-like"), gc = Load("glider-2-33-like");
            const double v = 30.0;
            double tugSpan = AeroModel.WingSpan(tugC), tugW = tugC.Mass.MassKg * Atmosphere.GravityMs2;
            (double du, double r, double sw) = Aircraft.TailSlipstream(tugC, 1.0, new Vec3(v, 0, 0), Vec3.Zero, Atmosphere.DensityAtAltitude(AltM));
            WakeSource tug = VortexWake.CreateSource("tug", tugSpan);
            Vec3 tp = new(0, 0, -AltM);
            double rho = Atmosphere.DensityAtAltitude(AltM);
            for (double t = 0; t < 6.0; t += 0.02)
            {
                tp += new Vec3(v * 0.02, 0, 0);
                tug.Shed(0.02, tp, Quat.Identity, new Vec3(v, 0, 0), new Vec3(0, 0, -tugW), rho, du, r, sw);
                VortexWake.Advance(0.02);
            }
            WakeStation two = tug.Station(5);   // 2 s back
            _o.WriteLine($"tug: span {tugSpan:F2} m, Γ {two.Gamma:F1} m²/s, slipstream ΔU {du:F1} m/s r {r:F2} m swirl {sw:F2}; 2 s back: ΔU {two.JetDu:F2} σ {two.JetR:F2}, wake {(-two.Mid.Z - AltM):F2} m vs tug path");

            TrimSolver.Result trim = TrimSolver.SolveGliderTrim(gc, v, AltM);
            Quat att = new(0, Math.Sin(trim.ThetaRad / 2), 0, Math.Cos(trim.ThetaRad / 2));
            double gam = trim.ThetaRad - trim.AlphaRad;
            var trimD = new ControlDeflections(0, trim.ElevatorRad, 0, 0);
            Vec3 vel = att.Conjugate().Rotate(new Vec3(v * Math.Cos(gam), 0, -v * Math.Sin(gam)));
            (double pdot, double sink, double push) Probe(Vec3 at)
            {
                var a = new Aircraft(gc, new RigidBodyState(at, att, vel, Vec3.Zero), trimD);
                var b = new Aircraft(gc, new RigidBodyState(at, att, vel, Vec3.Zero), trimD) { FeelsWake = false };
                for (int i = 0; i < 50; i++) { a.StepWithDeflectionTargets(trimD, 0.005); b.StepWithDeflectionTargets(trimD, 0.005); }
                return ((a.State.Rates.X - b.State.Rates.X) / 0.25 * 57.3, a.State.Position.Z - b.State.Position.Z, -(a.State.Position.X - b.State.Position.X));
            }
            var rows = new List<string>();
            Vec3 centre = two.Mid;
            var grid = new Dictionary<(int, int), (double pd, double sink, double pu)>();
            for (int iz = -6; iz <= 6; iz++)
                for (int iy = -12; iy <= 12; iy++)
                {
                    var r3 = Probe(centre + new Vec3(0, iy, iz));   // NED: iz < 0 is above the wake
                    grid[(iy, -iz)] = (r3.pdot, r3.sink, r3.push);
                    rows.Add($"{iy},{-iz},{F(r3.pdot)},{F(r3.sink)},{F(r3.push)}");
                }
            Csv("wake_tow_box.csv", "dy_m,dz_up_m,roll_accel_degs2,extra_sink_m,slowed_m", rows);

            // Full aileron in clean air, for scale.
            var ail = new Aircraft(gc, new RigidBodyState(centre + new Vec3(0, 200, 0), att, vel, Vec3.Zero), trimD) { FeelsWake = false };
            var full = new ControlDeflections(gc.Controls.Aileron.MaxDeflRad, trim.ElevatorRad, 0, 0);
            for (int i = 0; i < 50; i++) ail.StepWithDeflectionTargets(full, 0.005);
            double ailPdot = Math.Abs(ail.State.Rates.X) / 0.25 * 57.3;

            int peakL = 0, peakR = 0;
            for (int iy = 1; iy <= 12; iy++)
            {
                if (Math.Abs(grid[(iy, 0)].pd) > Math.Abs(grid[(peakR, 0)].pd)) peakR = iy;
                if (Math.Abs(grid[(-iy, 0)].pd) > Math.Abs(grid[(peakL, 0)].pd)) peakL = -iy;
            }
            double peak = Math.Max(Math.Abs(grid[(peakR, 0)].pd), Math.Abs(grid[(peakL, 0)].pd));
            var mid = grid[(0, 0)];
            var high = grid[(0, 6)];
            _o.WriteLine($"roll peaks at {peakL} m ({grid[(peakL, 0)].pd:F0} °/s²) and +{peakR} m ({grid[(peakR, 0)].pd:F0}); lines at ±{0.5 * two.B0:F2} m; full aileron {ailPdot:F0} °/s²");
            _o.WriteLine($"in the wake: sink {mid.sink:F3} m in 0.25 s, ṗ {mid.pd:F1}; 6 m above: sink {high.sink:F3}, ṗ {high.pd:F1}; 6 m above a line: ṗ {grid[(peakR, 6)].pd:F1}");
            // The box: the roll is strongest with the glider's CG on either line, in the line's sense (right line
            // counter-clockwise from behind → roll left), and the downwash between them sinks it.
            Assert.InRange(peakR, (int)Math.Floor(0.5 * two.B0) - 1, (int)Math.Ceiling(0.5 * two.B0) + 1);
            Assert.InRange(-peakL, (int)Math.Floor(0.5 * two.B0) - 1, (int)Math.Ceiling(0.5 * two.B0) + 1);
            Assert.True(grid[(peakR, 0)].pd < 0 && grid[(peakL, 0)].pd > 0);
            Assert.True(mid.sink > 0 && mid.sink >= grid[(10, 0)].sink && mid.sink >= grid[(-10, 0)].sink);
            // Strong enough to have to fly (on a line: ~80 % of what full aileron gives the 2-33, measured), and a few
            // metres clear — high tow above the wake, low tow below it — the roll is mostly gone.
            Assert.True(peak > 0.25 * ailPdot, $"peak {peak:F0} vs aileron {ailPdot:F0} °/s²");
            Assert.True(Math.Abs(grid[(peakR, 6)].pd) < 0.3 * peak && Math.Abs(grid[(peakL, -6)].pd) < 0.3 * peak);
            // The pair's downwash reaches further than its roll (a dipole's far field against the cores' own): 6 m up it is
            // still 40 % of the centre's, measured; the roll a line gives is down to under a quarter.
            Assert.True(high.sink < 0.6 * mid.sink);
        }
        finally { Clean(); VortexWake.GroundOverride = null; }
    }

    // ------------------------------------------------------------------ cost

    /// <summary>The frame budget with wakes on: four aeroplanes' full wakes (every station live), a 2-33 flying in one
    /// of them, the field advanced at 72 Hz. Prints the cost per sim-second of the follower's step and of the field,
    /// and holds the wake path to no garbage once warm (the Quest's collector turns garbage into stutter).</summary>
    [Fact]
    public void WakeCostAndGarbage()
    {
        Clean();
        try
        {
            AircraftConfig c = Load("glider-2-33-like");
            double span = AeroModel.WingSpan(c), v = 22.0;
            var leads = new List<(WakeSource s, Vec3 p)>();
            for (int i = 0; i < 4; i++) leads.Add((VortexWake.CreateSource($"l{i}", 11.0), new Vec3(0, i * 30.0, -AltM)));
            double dt = 1.0 / 72.0;
            for (int f = 0; f < 72 * 130; f++)   // 130 s: every buffer full
            {
                for (int i = 0; i < leads.Count; i++)
                {
                    var (s, p) = leads[i];
                    p += new Vec3(40 * dt, 0, 0);
                    s.ShedFromLoadFactor(dt, p, Quat.Identity, new Vec3(40, 0, 0), 1.0, 9000, 1.11);
                    leads[i] = (s, p);
                }
                VortexWake.Advance(dt);
            }
            var segs = new List<VortexSegment>();
            int nSeg = VortexWake.GetActiveSegments(segs);
            TrimSolver.Result trim = TrimSolver.SolveGliderTrim(c, v, AltM);
            Quat att = new(0, Math.Sin(trim.ThetaRad / 2), 0, Math.Cos(trim.ThetaRad / 2));
            var trimD = new ControlDeflections(0, trim.ElevatorRad, 0, 0);
            WakeStation st = leads[0].s.Station(20);
            var a = new Aircraft(c, new RigidBodyState(st.Mid + new Vec3(0, 2, 0), att, att.Conjugate().Rotate(new Vec3(v, 0, 0)), Vec3.Zero), trimD);
            a.Wake = VortexWake.CreateSource("follower", span);
            for (int i = 0; i < 400; i++) { a.StepWithDeflectionTargets(trimD, 0.005); if (i % 3 == 0) VortexWake.Advance(1.0 / 72); }
            var b = new Aircraft(c, new RigidBodyState(st.Mid + new Vec3(0, 2, 0), att, att.Conjugate().Rotate(new Vec3(v, 0, 0)), Vec3.Zero), trimD) { FeelsWake = false };
            for (int i = 0; i < 400; i++) b.StepWithDeflectionTargets(trimD, 0.005);

            long alloc0 = GC.GetAllocatedBytesForCurrentThread();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 200; i++) a.StepWithDeflectionTargets(trimD, 0.005);
            double msIn = sw.Elapsed.TotalMilliseconds;
            long allocIn = GC.GetAllocatedBytesForCurrentThread() - alloc0;
            sw.Restart();
            for (int i = 0; i < 200; i++) b.StepWithDeflectionTargets(trimD, 0.005);
            double msOut = sw.Elapsed.TotalMilliseconds;
            alloc0 = GC.GetAllocatedBytesForCurrentThread();
            sw.Restart();
            for (int i = 0; i < 72; i++) VortexWake.Advance(1.0 / 72);
            double msField = sw.Elapsed.TotalMilliseconds;
            long allocField = GC.GetAllocatedBytesForCurrentThread() - alloc0;
            _o.WriteLine($"{nSeg} live segments in 5 wakes; follower step {msIn:F1} ms per sim-second in the wake vs {msOut:F1} out of it (+{msIn - msOut:F1}); field {msField:F1} ms per second at 72 Hz; garbage {allocIn} B (follower, includes the aircraft's own), {allocField} B (field)");
            Assert.True(nSeg > 1500, $"only {nSeg} segments: the cost case is not the worst case");
            Assert.Equal(0, allocField);
        }
        finally { Clean(); VortexWake.GroundOverride = null; }
    }
}
