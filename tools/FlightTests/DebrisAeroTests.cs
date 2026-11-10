using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Wreck;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// DEPARTED PARTS MUST FLY ON REAL FORCES (Ty 2026-11-10: "Make sure departing wings and canopies use real
/// aerodynamic[s] for movement same for the pilot when bailing out real forces leading to tumbles and flutter etc",
/// and then "They get lift and drag I want tumble and flutter").
///
/// The subject is one Raymer-mass wing panel — 1.6 m chord, 5 m span, 0.2 m thick, 97.6 kg — released into still air
/// at 150 kt, which is the case Ty described. Everything here is measured three ways, on the same panel, from the
/// same release, so the question "was it the drive that was missing or the damping that was in the way?" is answered
/// with numbers rather than an opinion:
///
///   <see cref="Air.Old"/>   what shipped: HydroBody's air share (per-axis |v_k|·v_k drag at the box centre, plus its
///                           tip-speed rotational damping), no normal force and no centre of pressure.
///   <see cref="Air.New"/>   what ships now: <see cref="DebrisAero"/> for the whole of the air, HydroBody for water only.
///   <see cref="Air.Both"/>  the new drive left on top of the old damping — the variant that would have been the
///                           tempting half-measure, kept here because it is the one that does NOT tumble.
///
/// The integration below is WreckSim's own, inlined so the three air models can be swapped; <see
/// cref="TheRealWreckSimAgreesWithTheHarness"/> pins the harness to the shipping WreckSim.
/// </summary>
public class DebrisAeroTests
{
    private readonly ITestOutputHelper _o;
    public DebrisAeroTests(ITestOutputHelper o) { _o = o; }

    public enum Air { Old, New, Both }

    /// <summary>150 kt true.</summary>
    public const double ReleaseMs = 77.2;
    public const double ChordM = 1.6, SpanM = 5.0, ThickM = 0.2;
    /// <summary>Release height: low enough that the density is sea-level-ish, high enough that nothing lands.</summary>
    public const double ReleaseUpM = 200.0;

    // ---- the panel ------------------------------------------------------------------------------------------------

    private static HydroElement Panel()
    {
        double box = ChordM * SpanM * ThickM;
        double envelope = WaterPhysics.AirfoilAreaFactor * box;              // an airfoil fills 0.685 of its box
        double mass = WaterPhysics.RaymerWingKgPerM2 * ChordM * SpanM;       // 12.2 kg/m² of exposed planform
        return new HydroElement("Panel", WreckAtom.WingLOuter, Vec3.Zero,
            new Vec3(0.5 * ChordM, 0.5 * SpanM, 0.5 * ThickM), envelope, mass, mass / WaterPhysics.RhoAluminium);
    }

    /// <summary>A piece holding one panel, released at <paramref name="attitude"/> with the world velocity
    /// <paramref name="worldVel"/> and the body rates <paramref name="rates"/>. Built through WreckSim.Add so it
    /// starts exactly where and how a real break-up would put it.</summary>
    private static (WreckSim Sim, WreckPiece Piece) Release(Quat attitude, Vec3 worldVel, Vec3 rates)
    {
        var sim = new WreckSim(new FlatWreckWorld(null, -1e6));
        var el = Panel();
        var ac = new RigidBodyState(new Vec3(0, 0, -ReleaseUpM), attitude,
            attitude.Conjugate().Rotate(worldVel), rates);
        WreckPiece p = sim.Add("Panel", AirframeComponent.WingLeft, new List<HydroElement> { el }, Vec3.Zero, ac);
        return (sim, p);
    }

    /// <summary>Panel flat, flying along its own chord: the flow is in its plane, α = 0. "Released edge-on".</summary>
    private static Quat EdgeOn => Quat.Identity;

    /// <summary>Panel on edge, the flow straight onto its plan face, α = 90°. "Released broadside". A 90° pitch
    /// about body y.</summary>
    private static Quat Broadside => new(0, System.Math.Sin(System.Math.PI / 4), 0, System.Math.Cos(System.Math.PI / 4));

    // ---- the harness ----------------------------------------------------------------------------------------------

    public sealed class Track
    {
        public readonly List<double> T = new(), Omega = new(), AlphaDeg = new();
        /// <summary>∫|ω| dt: every radian the piece turned, whichever way.</summary>
        public double TurnedRad;
        /// <summary>∫ω_span dt, signed: how far it went END OVER END about its own spanwise axis. A piece that
        /// rocks back and forth stays near zero here however much it turns; one that goes round accumulates.</summary>
        public double PitchedRad;
        /// <summary>How many times the piece's plane came all the way round (|PitchedRad| crossing a multiple of π —
        /// every half turn puts the other face into the wind, which is a tumble you can see).</summary>
        public int HalfTurns;
        public double PeakOmega, TipSpeedRatio, FinalSpeedMs;
        public double FinalOmega => Omega.Count > 0 ? Omega[^1] : 0;
        public double Revolutions => TurnedRad / (2 * System.Math.PI);
    }

    /// <summary>
    /// WreckSim's integration with the air model switchable: RK4 over gravity + hydrostatics (+ DebrisAero where the
    /// model has it), then HydroBody's implicit drag/added-mass correction (with or without its air share), with the
    /// same attitude-driven sub-stepping. No ground and no water, so contact and flooding do not enter.
    /// </summary>
    private Track Fly(Air air, Quat attitude, Vec3 worldVel, Vec3 rates, double seconds)
    {
        (WreckSim sim, WreckPiece p) = Release(attitude, worldVel, rates);
        RigidBodyState s = p.State;
        MassProperties mp = p.MassProps;
        double rho = Atmosphere.DensityAt(s.Position);
        Vec3 windWorld = Atmosphere.SteadyWindAt(s.Position);
        double dt = sim.Dt, nan = double.NaN;
        var track = new Track();
        bool useDebris = air != Air.Old, useHydroAir = air != Air.New;
        (Vec3, Vec3) Forces(RigidBodyState st)
        {
            Quat qi = st.Attitude.Conjugate();
            Vec3 f = qi.Rotate(new Vec3(0, 0, p.MassKg * WaterPhysics.G)), m = Vec3.Zero;
            if (useDebris)
            {
                (Vec3 fa, Vec3 ma) = DebrisAero.Forces(p.Elements, p.CgBody, st, rho, nan, qi.Rotate(windWorld));
                f += fa; m += ma;
            }
            return (f, m);
        }
        for (double t = 0; t < seconds; t += dt)
        {
            // WreckSim re-samples both at every step, so this does too.
            rho = Atmosphere.DensityAt(s.Position);
            windWorld = Atmosphere.SteadyWindAt(s.Position);
            int sub = System.Math.Clamp((int)System.Math.Ceiling(s.Rates.Length * dt / WreckSim.AttitudeStepRad), 1, WreckSim.MaxSubSteps);
            double hh = dt / sub;
            for (int i = 0; i < sub; i++)
            {
                WaterCoeffs b = HydroBody.Coefficients(p.Elements, p.CgBody, s, nan, useHydroAir);
                RigidBodyState after = RigidBody6DOF.IntegrateRK4(s, hh, mp, Forces);
                WaterCoeffs n = HydroBody.Coefficients(p.Elements, p.CgBody, after, nan, useHydroAir);
                s = HydroBody.Correct(s, after, p.MassKg, p.Inertia, b, n, hh);
                track.TurnedRad += s.Rates.Length * hh;
                double was = track.PitchedRad;
                track.PitchedRad += s.Rates.Y * hh;       // body y is the panel's spanwise axis: end-over-end
                if ((int)System.Math.Abs(track.PitchedRad / System.Math.PI) != (int)System.Math.Abs(was / System.Math.PI))
                    track.HalfTurns++;
            }
            double w = s.Rates.Length;
            track.T.Add(t + dt);
            track.Omega.Add(w);
            track.PeakOmega = System.Math.Max(track.PeakOmega, w);
            // α: the angle between the flight path and the panel's own plane.
            double vm = s.Velocity.Length;
            track.AlphaDeg.Add(vm > 1e-6 ? System.Math.Asin(System.Math.Clamp(System.Math.Abs(s.Velocity.Z) / vm, 0, 1)) * 57.2957795 : 0);
            if (!double.IsFinite(w) || !double.IsFinite(s.Position.Z)) break;
        }
        track.FinalSpeedMs = s.Velocity.Length;
        track.TipSpeedRatio = track.FinalSpeedMs > 1e-6 ? track.FinalOmega * 0.5 * ChordM / track.FinalSpeedMs : 0;
        return track;
    }

    private void Log(string what, Track t)
    {
        var line = new System.Text.StringBuilder($"  {what,-26}");
        for (int i = 0; i < t.T.Count; i += (int)System.Math.Max(1, t.T.Count / 10))
            line.Append($" {t.T[i]:F1}s:{t.Omega[i],6:F1}");
        _o.WriteLine(line.ToString());
        _o.WriteLine($"  {what,-26} peak |w| {t.PeakOmega:F1} rad/s, turned {t.Revolutions:F2} rev, end-over-end "
                     + $"{t.PitchedRad / (2 * System.Math.PI):+0.00;-0.00} rev in {t.HalfTurns} half turns, "
                     + $"final |w| {t.FinalOmega:F1} rad/s at {t.FinalSpeedMs:F1} m/s, tip-speed wc/2V {t.TipSpeedRatio:F2}");
    }

    /// <summary>
    /// THE REAL THING: a real type's real left wing, with the real elements AirframeFlooding hands a break-up —
    /// several boxes, so a real CG, real principal moments and real products of inertia — released at 150 kt with the
    /// rate a departure actually hands it. Printed, not asserted beyond "it must go round", because what each type's
    /// wing does is a measurement and not a target.
    /// </summary>
    [Theory]
    [InlineData("pitts-s2b-like", 0.0)]
    [InlineData("pitts-s2b-like", 5.0)]
    [InlineData("great-lakes-like", 5.0)]
    [InlineData("c172-like", 5.0)]
    public void ARealWingPanelFliesOnItsOwnGeometry(string id, double departurePitchRate)
    {
        AircraftConfig c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
        var plane = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -ReleaseUpM), Quat.Identity, Vec3.Zero, Vec3.Zero), ControlDeflections.Neutral);
        List<HydroElement> els = plane.Hydro.Detach(AirframeComponent.WingLeft);
        Assert.NotEmpty(els);
        var sim = new WreckSim(new FlatWreckWorld(null, -1e6));
        var at = new RigidBodyState(new Vec3(0, 0, -ReleaseUpM), Quat.Identity,
            new Vec3(ReleaseMs, 0, 0), new Vec3(0, departurePitchRate, 0));
        WreckPiece p = sim.Add("WingLeft", AirframeComponent.WingLeft, els, c.Mass.CgVec(), at);
        _o.WriteLine($"{id}: {els.Count} element(s), {p.MassKg:F1} kg, I = ({p.Inertia.X:F1}, {p.Inertia.Y:F1}, {p.Inertia.Z:F1}) kg.m2, "
                     + $"products ({p.Products.X:F2}, {p.Products.Y:F2}, {p.Products.Z:F2}), released with q = {departurePitchRate} rad/s");
        double pitched = 0, turned = 0, peak = 0;
        int half = 0;
        for (int i = 0; i < (int)(6.0 / sim.Dt); i++)
        {
            sim.StepOnce();
            Vec3 w = p.State.Rates;
            double was = pitched;
            pitched += w.Y * sim.Dt;
            turned += w.Length * sim.Dt;
            peak = System.Math.Max(peak, w.Length);
            if ((int)System.Math.Abs(pitched / System.Math.PI) != (int)System.Math.Abs(was / System.Math.PI)) half++;
            if (i % (int)(1.0 / sim.Dt) == 0)
                _o.WriteLine($"  t {i * sim.Dt:F1}s  |w| {w.Length,5:F1} rad/s  (p {w.X,6:F1} q {w.Y,6:F1} r {w.Z,6:F1})  "
                             + $"V {p.State.Velocity.Length,5:F1} m/s  end-over-end {pitched / (2 * System.Math.PI):+0.00;-0.00} rev");
        }
        _o.WriteLine($"  => turned {turned / (2 * System.Math.PI):F2} rev, end-over-end {pitched / (2 * System.Math.PI):+0.00;-0.00} rev "
                     + $"in {half} half turns, peak |w| {peak:F1} rad/s");
        Assert.True(turned > 0.5, $"{id} barely turned at all: {turned:F2} rad in 6 s");
    }

    // ---- the headline: does it tumble? ---------------------------------------------------------------------------

    /// <summary>
    /// A PANEL RELEASED EDGE-ON AT 150 KT TURNS, AND ONLY THE NEW AIR MAKES IT TURN. It leaves with NO rotation at
    /// all — rates exactly zero — so every radian it turns was made by the air: the plate's normal force acting ahead
    /// of the box centre, which for this deliberately symmetric single box is exactly where the CG is. Under what
    /// shipped before it stays where it was pointed, to the last decimal. Under the new drive left on top of the old
    /// damping it turns, but a third less. The air in one place is what gives the whole of it.
    ///
    /// A single symmetric box is the HARDEST case to tumble and is here for that reason: with the CG at the plan
    /// centroid, broadside is a stable trim with the force dead on the CG, and the barrier to going over is highest.
    /// It flutters about broadside instead (see the trace). A real wing — several boxes, so a CG that is not any
    /// box's centre and real products of inertia — goes over end over end:
    /// <see cref="ARealWingPanelFliesOnItsOwnGeometry"/>.
    /// </summary>
    [Fact]
    public void APanelReleasedEdgeOnTurnsFromZeroRateAndOnlyTheNewAirDoesIt()
    {
        var vel = new Vec3(ReleaseMs, 0, 0);
        Track old = Fly(Air.Old, EdgeOn, vel, Vec3.Zero, 3.0);
        Track both = Fly(Air.Both, EdgeOn, vel, Vec3.Zero, 3.0);
        Track now = Fly(Air.New, EdgeOn, vel, Vec3.Zero, 3.0);
        _o.WriteLine($"edge-on, released at {ReleaseMs:F0} m/s with w = 0, {ChordM}x{SpanM}x{ThickM} m panel, {Panel().MassKg:F0} kg:");
        Log("old (HydroBody air)", old);
        Log("new drive + old damping", both);
        Log("new (DebrisAero only)", now);

        // What shipped could not turn it at all: no normal force, no centre of pressure, nothing but damping.
        Assert.True(old.TurnedRad < 0.05, $"old turned {old.TurnedRad:F3} rad — it was not supposed to be able to");
        // It must turn hard, and still be turning at 3 s rather than having rung down.
        Assert.True(now.Revolutions > 1.0, $"new turned only {now.Revolutions:F2} rev");
        Assert.True(now.PeakOmega > 8.0, $"new peaked at {now.PeakOmega:F1} rad/s");
        Assert.True(now.FinalOmega > 0.5, $"new is down to {now.FinalOmega:F2} rad/s at 3 s — it has rung right down");
        // And the old damping, left on top, really does take a third of it away.
        Assert.True(both.TurnedRad < 0.8 * now.TurnedRad,
            $"double-counted air turned {both.Revolutions:F2} rev vs {now.Revolutions:F2}");
    }

    /// <summary>
    /// EDGE-ON AND BROADSIDE DO DIFFERENT THINGS, which is the whole point of a force that moves with the angle.
    /// Released broadside the panel STAYS broadside: the force is dead on the plan centroid by symmetry at α = 90°,
    /// so nothing turns it, and the quarter turn it does make is the flight path's, not its own — it keeps its face
    /// to the wind while the wind swings from horizontal to vertical as it stops flying and starts falling. Released
    /// edge-on the force is a quarter chord ahead of the CG at full strength, which is the most unstable attitude a
    /// plate has, and it goes.
    /// </summary>
    [Fact]
    public void BroadsideStaysBroadsideAndEdgeOnDoesNot()
    {
        var vel = new Vec3(ReleaseMs, 0, 0);
        Track edge = Fly(Air.New, EdgeOn, vel, Vec3.Zero, 3.0);
        Track flat = Fly(Air.New, Broadside, vel, Vec3.Zero, 3.0);
        _o.WriteLine("released with w = 0 at 150 kt:");
        Log("edge-on (alpha 0)", edge);
        Log("broadside (alpha 90)", flat);
        _o.WriteLine($"  alpha edge-on {edge.AlphaDeg[0]:F0} -> {edge.AlphaDeg[^1]:F0} deg; broadside {flat.AlphaDeg[0]:F0} -> {flat.AlphaDeg[^1]:F0} deg");
        Assert.True(flat.TurnedRad < 0.3 * edge.TurnedRad,
            $"broadside turned {flat.TurnedRad:F2} rad, edge-on {edge.TurnedRad:F2} — they must not behave alike");
        Assert.True(edge.TurnedRad > 2 * System.Math.PI, $"edge-on turned {edge.TurnedRad:F2} rad");
        // Broadside it keeps its face to the wind the whole way: that is a quarter turn of flight path, no more.
        Assert.True(flat.TurnedRad < 0.45 * 2 * System.Math.PI, $"broadside turned {flat.Revolutions:F2} rev");
        double aMin = double.MaxValue;
        foreach (double a in flat.AlphaDeg) aMin = System.Math.Min(aMin, a);
        _o.WriteLine($"  broadside never came below alpha {aMin:F0} deg");
        Assert.True(aMin > 60.0, $"broadside wandered to alpha {aMin:F0} deg");
    }

    /// <summary>
    /// THE RATE IS NOT AN INITIAL CONDITION. Released edge-on from rest the rate GROWS, and it grows from the
    /// aerodynamic moment: zero at the first step, climbing while α opens up. If a tumble rate were being imposed
    /// anywhere the first sample would already carry it.
    /// </summary>
    [Fact]
    public void TheAngularRateGrowsFromTheMomentInsteadOfStartingThere()
    {
        Track t = Fly(Air.New, EdgeOn, new Vec3(ReleaseMs, 0, 0), Vec3.Zero, 1.0);
        _o.WriteLine("edge-on from rest, first 20 steps (5 ms each):");
        for (int i = 0; i < 20 && i < t.Omega.Count; i++) _o.WriteLine($"  t {t.T[i] * 1000,5:F0} ms  |w| {t.Omega[i]:F4} rad/s  alpha {t.AlphaDeg[i]:F2} deg");
        Assert.True(t.Omega[0] < 0.5, $"first step is already at {t.Omega[0]:F2} rad/s");
        for (int i = 1; i < 12; i++)
            Assert.True(t.Omega[i] > t.Omega[i - 1], $"step {i}: |w| fell from {t.Omega[i - 1]:F4} to {t.Omega[i]:F4}");
        Assert.True(t.PeakOmega > 10.0 * t.Omega[0], $"peak {t.PeakOmega:F1} is not much more than the first step's {t.Omega[0]:F3}");
    }

    /// <summary>
    /// THE MECHANISM, MEASURED AGAINST THE TWO TEXTBOOK ANSWERS. This is the one number in the whole feature that
    /// decides whether a plate can tumble at all: dM/dω, the aerodynamic moment's response to a pitch rate about the
    /// panel's own spanwise axis. Differentiated numerically out of <see cref="DebrisAero"/> and compared with
    ///     attached (α = 0)    dM/dω = +k·|v|·c²/16   DRIVING — the classical quasi-steady thin-airfoil result for a
    ///                                                plate pivoting about mid-chord, whose aerodynamic centre is a
    ///                                                quarter chord ahead of its pivot (Theodorsen 1935);
    ///     broadside (α = 90)  dM/dω = −k·|v|·c²/6    DAMPING — a uniform chordwise pressure at its own second
    ///                                                moment c²/12, doubled because broadside the plate law
    ///                                                |v|·v_n IS v_n², so the force is quadratic in the rate there,
    /// with k = ½ρ·Cd·A. Neither was fitted; they come out of the node layout. The OLD air could only ever give the
    /// damping sign, at either end, which is why nothing could autorotate under it — printed here beside them, and
    /// note that it lands on the broadside figure (that much it had right) and gives essentially nothing at α = 0,
    /// where the whole question is decided.
    /// </summary>
    [Fact]
    public void ThePitchRateMomentMatchesTheTwoTextbookAnswers()
    {
        var half = new Vec3(0.5 * ChordM, 0.5 * SpanM, 0.5 * ThickM);
        double rho = 1.225, v = 50.0, dq = 1e-4;
        double k = 0.5 * rho * WaterPhysics.CdForFineness(ThickM / System.Math.Sqrt(ChordM * SpanM)) * ChordM * SpanM;
        foreach ((double deg, double factor, string what) in new[] { (0.0, 1.0 / 16.0, "driving"), (90.0, -1.0 / 6.0, "damping") })
        {
            double r = deg / 57.2957795;
            var vel = new Vec3(v * System.Math.Cos(r), 0, v * System.Math.Sin(r));
            double M(double q) => DebrisAero.Forces(Vec3.Zero, half, 1.0,
                new RigidBodyState(Vec3.Zero, Quat.Identity, vel, new Vec3(0, q, 0)), rho, Vec3.Zero).Moment.Y;
            double dMdq = (M(dq) - M(-dq)) / (2 * dq);
            double want = factor * k * v * ChordM * ChordM;
            // What the old air would have said about the same rate, for the record.
            var st = new RigidBodyState(Vec3.Zero, Quat.Identity, vel, new Vec3(0, dq, 0));
            double oldD = -HydroBody.Coefficients(new List<HydroElement> { Panel() }, Vec3.Zero, st, double.NaN, true).RotDamping.Y;
            _o.WriteLine($"  alpha {deg,3:F0} ({what,7}): dM/dw measured {dMdq,10:F1}, textbook {want,10:F1} N.m per rad/s; old air {oldD,10:F1}");
            Assert.InRange(dMdq, want - 0.03 * System.Math.Abs(want), want + 0.03 * System.Math.Abs(want));
            Assert.True(oldD <= 0.0, $"the old air is supposed to be damping-only, and read {oldD:F1}");
        }
    }

    /// <summary>
    /// WHAT A FREE SYMMETRIC PLATE ACTUALLY DOES, recorded rather than asserted into a shape. Released edge-on and
    /// followed for six seconds it snaps to broadside at 13 rad/s, overshoots, and then flutters about broadside at
    /// 0.5–3 rad/s — a falling card, not a propeller. The tip-speed ratio ωc/2V of a CONSTRAINED autorotating plate is
    /// 0.3–0.5 (Iversen, "Autorotating flat-plate wings", JFM 1979; Lugt, "Autorotation", Ann. Rev. Fluid Mech. 1983);
    /// a FREE plate does something else again, and whether it flutters or tumbles turns on its thickness ratio
    /// (Andersen, Pesavento &amp; Wang, "Unsteady aerodynamics of fluttering and tumbling plates", JFM 541, 2005, who
    /// get both out of one quasi-steady model). This one, at h/c = 0.125, comes down on the flutter side — I have not
    /// checked that against their phase diagram, only noted that both behaviours are real. Energy says why: the barrier
    /// to going over the top scales as ρ·c³·V² and the rotational energy at tip-speed ratio λ as σ·λ²·c²·V², so the
    /// ratio is 2σλ²/(ρ·c) — 1 at λ = 0.28 for this 1.6 m chord at 12.2 kg/m². Marginal, and it falls short.
    /// Smaller pieces (an aileron, a fairing) clear it easily, and so do real multi-box wings.
    /// </summary>
    [Fact]
    public void AFreeSymmetricPlateFluttersAboutBroadsideRatherThanAutorotating()
    {
        Track t = Fly(Air.New, EdgeOn, new Vec3(ReleaseMs, 0, 0), Vec3.Zero, 6.0);
        Log("edge-on, 6 s", t);
        Assert.True(t.Revolutions > 1.5, $"it hardly moved: {t.Revolutions:F2} rev");
        Assert.True(System.Math.Abs(t.PitchedRad) < 2 * System.Math.PI,
            $"it went {t.PitchedRad / (2 * System.Math.PI):F2} rev end over end — if a symmetric plate now tumbles, this comment is out of date");
        Assert.True(t.FinalOmega > 0.2, $"the flutter died: {t.FinalOmega:F2} rad/s at 6 s");
    }

    /// <summary>
    /// A PIECE THAT LEAVES A SPINNING AEROPLANE KEEPS ITS MOMENTUM AND THEN FLIES. Released broadside with the rate a
    /// spin would hand it (3 rad/s in roll), the old air damped it toward nothing in under a second; now the plate's
    /// own moment keeps it turning. Nothing was added to make this happen — it is the same force, read at four strips.
    /// </summary>
    [Fact]
    public void ADepartureRateIsSustainedInsteadOfDampedAway()
    {
        var vel = new Vec3(ReleaseMs, 0, 0);
        var spin = new Vec3(3.0, 0, 0);
        Track old = Fly(Air.Old, Broadside, vel, spin, 3.0);
        Track now = Fly(Air.New, Broadside, vel, spin, 3.0);
        _o.WriteLine("broadside, released with 3 rad/s of roll (what a spinning aeroplane hands a panel):");
        Log("old (HydroBody air)", old);
        Log("new (DebrisAero)", now);
        Assert.True(old.FinalOmega < 1.0, $"old kept {old.FinalOmega:F2} rad/s — the baseline is supposed to damp it");
        Assert.True(now.FinalOmega > 2.0 * old.FinalOmega, $"new kept {now.FinalOmega:F2}, old {old.FinalOmega:F2}");
    }

    // ---- the force law itself -------------------------------------------------------------------------------------

    /// <summary>
    /// THE PLATE'S NORMAL FORCE IS Cn = Cd_max·sin α ON ITS OWN AREA, and that is what comes out: the measured
    /// force divided by q·A tracks sin α over the whole range, within the few per cent the fineness blend allows at
    /// this thickness. The old law gave sin²α, which is where the factor of 1/sin α went missing.
    /// </summary>
    [Fact]
    public void TheNormalForceFollowsTheFlatPlateSinLaw()
    {
        var half = new Vec3(0.5 * ChordM, 0.5 * SpanM, 0.5 * ThickM);
        double rho = 1.225, v = 50.0, q = 0.5 * rho * v * v, a = ChordM * SpanM;
        _o.WriteLine("  alpha   Cn measured   Cd_max*sin(a)   old law (sin^2)");
        foreach (double deg in new[] { 5.0, 10.0, 20.0, 30.0, 45.0, 60.0, 90.0 })
        {
            double r = deg / 57.2957795;
            var s = new RigidBodyState(Vec3.Zero, Quat.Identity,
                new Vec3(v * System.Math.Cos(r), 0, v * System.Math.Sin(r)), Vec3.Zero);
            (Vec3 f, Vec3 m) = DebrisAero.Forces(Vec3.Zero, half, 1.0, s, rho, Vec3.Zero);
            double cn = System.Math.Abs(f.Z) / (q * a);
            double law = WaterPhysics.CdPlateNormal * System.Math.Sin(r);
            _o.WriteLine($"  {deg,5:F0}   {cn,11:F3}   {law,13:F3}   {WaterPhysics.CdPlateNormal * System.Math.Sin(r) * System.Math.Sin(r),15:F3}");
            Assert.InRange(cn, 0.88 * law, 1.12 * law);
            // And the force is off-centre everywhere but broadside: that is the moment that makes the tumble. The
            // arm is the CP walk itself, 0.25c·(1 − sin α), so it shrinks to nothing exactly at broadside.
            double arm = System.Math.Abs(m.Y) / System.Math.Max(1e-9, System.Math.Abs(f.Z));
            double want = DebrisAero.CpWalk * ChordM * (1.0 - System.Math.Sin(r));
            Assert.InRange(arm, want - 0.01 * ChordM, want + 0.01 * ChordM);
        }
    }

    /// <summary>
    /// THE CENTRE OF PRESSURE WALKS LIKE A FLAT PLATE'S. Quarter chord when the flow is nearly along the plate,
    /// mid-chord broadside, monotone in between — within 0.02c of the measured plate series (0.28c at 10°, 0.33 at
    /// 20°, 0.38 at 30°, 0.43 at 45°, 0.50 at 90°).
    /// </summary>
    [Fact]
    public void TheCentreOfPressureMatchesTheMeasuredPlateSeries()
    {
        var half = new Vec3(0.5 * ChordM, 0.5 * SpanM, 0.5 * ThickM);
        double rho = 1.225, v = 50.0;
        (double deg, double book)[] series = { (10, 0.28), (20, 0.33), (30, 0.38), (45, 0.43), (90, 0.50) };
        double last = 0;
        foreach ((double deg, double book) in series)
        {
            double r = deg / 57.2957795;
            var s = new RigidBodyState(Vec3.Zero, Quat.Identity,
                new Vec3(v * System.Math.Cos(r), 0, v * System.Math.Sin(r)), Vec3.Zero);
            (Vec3 f, Vec3 m) = DebrisAero.Forces(Vec3.Zero, half, 1.0, s, rho, Vec3.Zero);
            // Moving in +x, the leading edge is the +x one; x_cp is measured back from it.
            double ahead = System.Math.Abs(m.Y) / System.Math.Max(1e-9, System.Math.Abs(f.Z));
            double xcp = 0.5 - ahead / ChordM;
            _o.WriteLine($"  alpha {deg,3:F0}  x_cp/c {xcp:F3}  measured {book:F2}");
            Assert.InRange(xcp, book - 0.02, book + 0.02);
            Assert.True(xcp > last, $"x_cp went backwards at alpha {deg}");
            last = xcp;
        }
    }

    /// <summary>A piece that is not spinning and is not plate-like gets exactly the drag it always got: the box's
    /// own quadratic law, at its own centre. A fuselage section moving along its axis is the case, and this is why
    /// the chunky half of the wreck is untouched by all of the above.</summary>
    [Fact]
    public void AChunkyElementMovingFaceOnKeepsTheOldDragExactly()
    {
        var half = new Vec3(1.0, 0.5, 0.5);                 // a fuselage section: fineness 2.0 along x
        double rho = 1.225, v = 30.0;
        var s = new RigidBodyState(Vec3.Zero, Quat.Identity, new Vec3(v, 0, 0), Vec3.Zero);
        (Vec3 f, Vec3 m) = DebrisAero.Forces(Vec3.Zero, half, 1.0, s, rho, Vec3.Zero);
        double expect = -0.5 * rho * WaterPhysics.CdForFineness(2.0) * (4 * half.Y * half.Z) * v * v;
        _o.WriteLine($"  Fx {f.X:F2} N, old law {expect:F2} N, moment {m.Length:E2} N.m");
        Assert.Equal(expect, f.X, 6);
        Assert.True(m.Length < 1e-6, $"a face-on box must not be turned: {m.Length:E2} N.m");
    }

    /// <summary>Still air turns nothing and pushes nothing, whatever the attitude: no force appears out of the
    /// geometry on its own.</summary>
    [Fact]
    public void StillAirDoesNothing()
    {
        var s = new RigidBodyState(Vec3.Zero, new Quat(0.3, 0.4, 0.1, 0.86).Normalized(), Vec3.Zero, Vec3.Zero);
        (Vec3 f, Vec3 m) = DebrisAero.Forces(Vec3.Zero, new Vec3(0.8, 2.5, 0.1), 1.0, s, 1.225, Vec3.Zero);
        Assert.Equal(0.0, f.Length, 12);
        Assert.Equal(0.0, m.Length, 12);
    }

    /// <summary>A piece sitting still in a wind feels the wind: the velocity everywhere is the one THROUGH THE AIR,
    /// so a panel at rest in a 20 m/s wind gets the same force as one flying at 20 m/s into still air.</summary>
    [Fact]
    public void TheWindActsExactlyAsMotionThroughStillAirDoes()
    {
        var half = new Vec3(0.8, 2.5, 0.1);
        var moving = new RigidBodyState(Vec3.Zero, Quat.Identity, new Vec3(14, 0, 14), new Vec3(0.5, -0.3, 0.2));
        var still = new RigidBodyState(Vec3.Zero, Quat.Identity, Vec3.Zero, new Vec3(0.5, -0.3, 0.2));
        (Vec3 f1, Vec3 m1) = DebrisAero.Forces(Vec3.Zero, half, 1.0, moving, 1.225, Vec3.Zero);
        (Vec3 f2, Vec3 m2) = DebrisAero.Forces(Vec3.Zero, half, 1.0, still, 1.225, new Vec3(-14, 0, -14));
        Assert.Equal(f1.X, f2.X, 9); Assert.Equal(f1.Z, f2.Z, 9);
        Assert.Equal(m1.Y, m2.Y, 9);
    }

    /// <summary>The harness above is WreckSim's loop, so it had better agree with WreckSim.</summary>
    [Fact]
    public void TheRealWreckSimAgreesWithTheHarness()
    {
        (WreckSim sim, WreckPiece p) = Release(EdgeOn, new Vec3(ReleaseMs, 0, 0), Vec3.Zero);
        for (int i = 0; i < 200; i++) sim.StepOnce();
        Track t = Fly(Air.New, EdgeOn, new Vec3(ReleaseMs, 0, 0), Vec3.Zero, 200 * sim.Dt);
        _o.WriteLine($"  WreckSim |w| {p.State.Rates.Length:F3} rad/s, harness {t.FinalOmega:F3}");
        Assert.Equal(t.FinalOmega, p.State.Rates.Length, 9);
    }

    /// <summary>
    /// WHAT IT COSTS. DebrisAero is four Gauss strips × three faces per element, called once per RK4 stage, so four
    /// times a step; HydroBody's two calls per step are unchanged. Measured here as steps per second for one
    /// one-element piece and for a twenty-element wreck, against the 200 Hz the sim needs.
    /// </summary>
    [Fact]
    public void TheCostIsMeasured()
    {
        foreach (int n in new[] { 1, 20 })
        {
            var sim = new WreckSim(new FlatWreckWorld(null, -1e6));
            var els = new List<HydroElement>();
            for (int i = 0; i < n; i++)
            {
                HydroElement e = Panel();
                e.Centre = new Vec3(0.3 * i, 0, 0);
                els.Add(e);
            }
            var ac = new RigidBodyState(new Vec3(0, 0, -ReleaseUpM), Quat.Identity, new Vec3(ReleaseMs, 0, 0), Vec3.Zero);
            sim.Add("Piece", AirframeComponent.WingLeft, els, Vec3.Zero, ac);
            for (int i = 0; i < 200; i++) sim.StepOnce();          // warm, and get it tumbling (so the sub-stepping bites)
            var sw = System.Diagnostics.Stopwatch.StartNew();
            const int steps = 4000;
            for (int i = 0; i < steps; i++) sim.StepOnce();
            sw.Stop();
            double perStepUs = sw.Elapsed.TotalMilliseconds * 1000.0 / steps;
            _o.WriteLine($"  {n,2} element(s): {perStepUs:F1} us/step  =>  {perStepUs * 200 / 1000:F2} ms per second of sim at 200 Hz");
            Assert.True(perStepUs * 200 / 1000 < 100.0, $"{n} elements cost {perStepUs * 200 / 1000:F1} ms per sim second");
        }
    }
}
