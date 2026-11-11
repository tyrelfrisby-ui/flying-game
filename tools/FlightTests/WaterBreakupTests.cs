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
/// The same localized break-up on WATER (Ty 2026-11-03): a gentle wheel touch skims and drags with the water's real
/// forces (WheelHydro's finite planing contact, lane/claude-wheel-hydroplane); the airframe comes apart only where a
/// load exceeds a part's strength — the water's force on a wing panel's station against that panel's moments
/// (Aircraft.RecordPanelWaterLoads → the panel law), the water's load on a joint against its ultimate
/// (AirframeFlooding.CheckJoints) — and only that part leaves, with v + ω × r, through the same LoseComponent that
/// conserves momentum on land. Flat water 3 m deep (FloatHydro.FlatWaterOverride, set and cleared in each test's own
/// scope: it is [ThreadStatic]).
/// </summary>
[Collection("WorldTerrainActive")]
public class WaterBreakupTests
{
    private readonly ITestOutputHelper _o;
    public WaterBreakupTests(ITestOutputHelper o) { _o = o; }

    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    /// <summary>At <paramref name="v"/> m/s, banked <paramref name="bankDeg"/> right, sinking <paramref name="sinkMs"/>, wings at
    /// the glide trim's attitude, the lowest tyre or hard point 3 cm above the water.</summary>
    private static Aircraft OverWater(AircraftConfig c, double v, double sinkMs, double bankDeg)
    {
        TrimSolver.Result t = TrimSolver.SolveGliderTrim(c, v, 10, flapFraction: 0.0, idleProp: true);
        double alpha = t.Converged ? t.AlphaRad : 4 / 57.3, gam = Math.Asin(sinkMs / v), th = alpha - gam, ph = bankDeg * Math.PI / 180;
        Quat q = Quat.Multiply(new Quat(0, 0, 0, 1), Quat.Multiply(new Quat(0, Math.Sin(th / 2), 0, Math.Cos(th / 2)), new Quat(Math.Sin(ph / 2), 0, 0, Math.Cos(ph / 2))));
        Vec3 cg = c.Mass.CgVec();
        double low = double.NegativeInfinity;
        foreach (GearConfig g in c.Gear) low = Math.Max(low, q.Rotate(g.PosVec() - cg).Z);
        foreach (ContactPoint p in AirframeContact.BuildPoints(c)) if (!p.PropDisc) low = Math.Max(low, q.Rotate(p.Body - cg).Z);
        double z0 = 3.0 + low + 0.03;
        Vec3 vw = new(v * Math.Cos(gam), 0, v * Math.Sin(gam));
        return new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -z0), q, q.Conjugate().Rotate(vw), Vec3.Zero),
            new ControlDeflections(0, t.Converged ? t.ElevatorRad : 0, 0, 0, 1.0));
    }

    private static double Speed(Aircraft ac) => ac.State.Attitude.Rotate(ac.State.Velocity).Length;

    // ---- (a) a gentle wheel touch skims ------------------------------------------------------------------------------

    [Theory]
    [InlineData("extra-300-like")]
    [InlineData("j3-cub-like")]
    public void AGentleWheelTouchOnWaterSkimsDecelaratesFinitelyAndStaysIntact(string id)
    {
        FloatHydro.FlatWaterOverride = 3.0;
        try
        {
            // 0.5 m/s of sink at 1.15 × the slowest glide trim (WheelWaterTouch), idle: a tyre brushing the water.
            WheelWaterTouch.Result r = WheelWaterTouch.Run(Load(id), 0.5, 3.0);
            _o.WriteLine(r.ToString());
            Assert.True(r.TouchAt > 0, "never touched");
            // Finite: under 1 g peak (a dead stop from ~25 m/s inside a step would be hundreds of g) and still 90 % of the
            // speed a second on; never below a third of it in three seconds.
            Assert.True(r.PeakDecelG < 1.0, $"peak {r.PeakDecelG:F2} g");
            Assert.True(r.SpeedAt1s > 0.9 * r.V, $"{r.V:F1} -> {r.SpeedAt1s:F1} m/s");
            Assert.True(r.MinSpeed > r.V / 3, $"min {r.MinSpeed:F1} of {r.V:F1}");
            Assert.Empty(r.Lost);
            Assert.Empty(r.Damage);
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    // ---- (b) a harder touch breaks only the overloaded part -----------------------------------------------------------

    [Fact]
    public void AWingDugIntoTheWaterLosesOnlyWhatWasOverloadedAndEverythingKeepsItsMomentum()
    {
        FloatHydro.FlatWaterOverride = 3.0;
        try
        {
            // The Extra at 40 m/s, banked 30° right, sinking 6 m/s: the right wing meets the water first, ahead of the wheels.
            // (Measured 2026-11-03: at 35 m/s, 3–5 m/s of sink and 30–45° of bank the tip panel only bends — 0.3–0.85 of its
            // ultimate — and nothing comes off, yet the water still brings it to ~1 m/s within a second: a wing element
            // knifing through water at 35 m/s has ½·ρ·C_d·A·v² ≈ ½·1000·0.4·0.36·35² ≈ 90 kN of drag. That is the
            // cartwheel, at the rate the water's forces allow, not a stop.)
            AircraftConfig c = Load("extra-300-like");
            Aircraft ac = OverWater(c, 40, 6.0, 30);
            var relay = new CrashRelay(ac, new FlatWreckWorld(3.0, 0.0));
            var checks = new List<string>();
            double worst = 0, worstL = 0, worstP = 0;
            Vec3 P = c.Mass.CgVec();
            var inputs = new ControlInputs(0, 0, 0, 1);
            for (int i = 0; i < 200; i++)
            {
                // Separations happen after the step's integration and nothing moves the airframe after them within the
                // step, so the books are: the mass before the step, the state after it, and the pieces born in it (they
                // stay where they were born until the wreck is advanced, which this test never does).
                MassProperties m0 = ac.MassProperties; Vec3 c0 = ac.CgShiftBody;
                int born = relay.Wreck.Pieces.Count;
                ac.Step(inputs, 0.005);
                if (relay.Wreck.Pieces.Count == born) continue;
                RigidBodyState s = ac.State;
                MassProperties m1 = ac.MassProperties; Vec3 c1 = ac.CgShiftBody;
                Vec3 pBefore = (s.Velocity + Vec3.Cross(s.Rates, c0)) * m0.MassKg;
                Vec3 lBefore = m0.Times(s.Rates) + Vec3.Cross(c0, s.Velocity) * m0.MassKg;
                Vec3 pAfter = (s.Velocity + Vec3.Cross(s.Rates, c1)) * m1.MassKg;
                Vec3 lAfter = m1.Times(s.Rates) + Vec3.Cross(c1, s.Velocity) * m1.MassKg;
                for (int k = born; k < relay.Wreck.Pieces.Count; k++)
                {
                    WreckPiece p = relay.Wreck.Pieces[k];
                    Vec3 r = p.CgBody - P, vb = s.Attitude.Conjugate().Rotate(p.WorldVelocity);
                    pAfter += vb * p.MassKg;
                    lAfter += p.MassProps.Times(p.State.Rates) + Vec3.Cross(r, vb) * p.MassKg;
                    worst = Math.Max(worst, (vb - (s.Velocity + Vec3.Cross(s.Rates, r))).Length);
                    checks.Add($"t {i * 0.005:F3}: {p.Name} {p.MassKg:F2} kg at r ({r.X:F2}, {r.Y:F2}, {r.Z:F2})");
                }
                double dp = (pAfter - pBefore).Length / pBefore.Length, dl = (lAfter - lBefore).Length / Math.Max(1.0, lBefore.Length);
                worstP = Math.Max(worstP, dp); worstL = Math.Max(worstL, dl);
                checks.Add($"  |Δp|/p {dp:E1}, |ΔL|/L {dl:E1}");
            }
            foreach (string l in checks) _o.WriteLine(l);
            _o.WriteLine($"lost [{string.Join(",", ac.LostComponents)}]; {string.Join("; ", ac.Hydro.DamageLog.Where(d => d.Contains("broken")))}");
            // The water's load on the right wing's outer joint (AirframeFlooding: 0.55 of the semispan, 11.7 kN·m ultimate
            // for the Extra at its +10 g × 2.0) is what goes; the left wing, the inner right wing and the tail stay. Anything
            // else lost went past its OWN strength, written in the damage log (the nose's mount on the axial deceleration:
            // 9 g × its 303 kg = 26.7 kN, 14 CFR 23.561) — and the prop, a blade in the water.
            Assert.True(ac.IsLost(AirframeComponent.WingRightOuter), "the right outer wing held");
            Assert.False(ac.IsLost(AirframeComponent.WingRight));
            Assert.False(ac.IsLost(AirframeComponent.WingLeft));
            Assert.False(ac.IsLost(AirframeComponent.WingLeftOuter));
            Assert.False(ac.IsLost(AirframeComponent.TailBoom));
            foreach (AirframeComponent comp in new[] { AirframeComponent.WingRightOuter, AirframeComponent.Nose })
                if (ac.IsLost(comp)) Assert.Contains(ac.Hydro.DamageLog, d => d.StartsWith(comp + " broken off in the water"));
            Assert.True(worst < 1e-9, $"a piece left at {worst:E1} m/s off v + ω×r");
            Assert.True(worstP < 1e-9, $"linear momentum off by {worstP:E1}");
            // Angular momentum is exact where the books close. The Extra's do not on its pitch axis: its wreck boxes, engine
            // well forward, already carry more pitch inertia than its config before anything breaks, and a separation
            // never ADDS inertia (Aircraft.ShedMass), so the piece's share of Iyy stays on the remnant — 4.6·10⁻⁴ of L,
            // logged where it happens. Every other axis, and every type whose boxes fit, is exact (TreeStrikeTests).
            bool floored = ac.Hydro.DamageLog.Any(d => d.Contains("not conserved"));
            Assert.True(worstL < (floored ? 1e-3 : 1e-6), $"angular momentum off by {worstL:E1}{(floored ? " (an inertia floor acted)" : "")}");
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    // ---- (c) water impacts never zero velocity non-physically --------------------------------------------------------

    [Theory]
    [InlineData("extra-300-like", 40.0, 4.0)]
    [InlineData("j3-cub-like", 25.0, 3.0)]
    public void AHardWheelsFirstArrivalOnWaterIsNotADeadStop(string id, double v, double sink)
    {
        FloatHydro.FlatWaterOverride = 3.0;
        try
        {
            Aircraft ac = OverWater(Load(id), v, sink, 0);
            double v0 = Speed(ac), worstStep = 0, prev = v0;
            var inputs = new ControlInputs(0, 0, 0, 1);
            for (int i = 0; i < 200; i++)
            {
                ac.Step(inputs, 0.005);
                double now = Speed(ac);
                Assert.False(double.IsNaN(now), $"NaN at step {i}");
                worstStep = Math.Max(worstStep, prev - now);
                prev = now;
            }
            _o.WriteLine($"{id}: {v0:F1} -> {Speed(ac):F1} m/s in 1 s, worst single step {worstStep:F3} m/s ({worstStep / 0.005 / 9.81:F1} g); lost [{string.Join(",", ac.LostComponents)}]; {string.Join("; ", ac.Hydro.DamageLog.Where(d => d.Contains("broken")))}");
            // The water may take a great deal — gear down at 4 m/s of sink the Extra noses over and its wings slam flat —
            // but only at the rate its forces allow. A dead stop is the whole speed in one 5 ms step (40 m/s: 800 g); the
            // bound is 2 m/s in a step, 40 g.
            //
            // THE 1.0 m/s FLOOR WAS RE-MEASURED AND NOT MOVED (Claude, 2026-11-10, lane/claude-vne-cam), and the reason
            // is worth keeping, because a re-band was authorised and turned out to be unnecessary. Merging
            // lane/claude-crash-cam-occupants — the fix that keeps the crash camera on the PILOT's section — was
            // reported on 2026-11-06 to drop the Extra's case from 1.45 to 0.90 m/s against this floor, and Ty's call on
            // 2026-11-10, offered the alternative of investigating the coupling first, was "Merge and re-band that
            // test". Re-measured on today's code, in this class alone and again inside the 16-test water filter, both
            // runs identical:
            //
            //     extra-300-like   base 1.6 m/s  ->  with the merge 2.1 m/s   (floor 1.0: passes either way)
            //     j3-cub-like      base 6.7 m/s  ->  with the merge 6.7 m/s   (unchanged, as is its damage log)
            //
            // So the 0.90 does not reproduce, and the merge now moves this case the RIGHT way. Both ends of the
            // 2026-11-06 comparison have moved under four days of wreck and water work — the window between that lane's
            // base 2b47338f and today holds staggered progressive wing failure (e2949511), the Extra's propeller put on
            // the station it was measured at rather than one guessed off the fuselage (1452c53a), and a thrown propeller
            // taking its weight with it (25a7a713), among others. WHICH of them moved it was NOT bisected: once the
            // merged number came out above the floor there was nothing left to re-band, and loosening the floor on a
            // stale measurement would have been the wrong trade — this is the one assertion that would catch the water
            // zeroing an aeroplane's velocity outright.
            //
            // WHY A "CAMERA CHANGE" MOVES A BREAK-UP SPEED AT ALL — because it is not a camera change, and no camera
            // touches the simulation. AirframeContact.FuselageStations is the SINGLE definition of where the fuselage is
            // cut into nose / cabin / tail cone, and two things read it: the crash camera, which follows the piece that
            // holds the seats, and AirframeHydroModel.Build, which sizes those same three pieces and places their cuts
            // and joints. Keeping the Extra's rear seat (station x -1.2801) inside the cabin moves its tail cut from
            // x -1.250 to x -1.780, shortening the tail cone by 0.53 m; a fuselage joint's axial ultimate is
            // WaterPhysics.EmergencyForwardG (17 g, 14 CFR 23.561) x g x the mass BEYOND it, so a tail cone of 52.0 kg
            // instead of 56.2 kg breaks at 8.7 kN instead of 9.4 kN — both printed in the damage log above, and
            // OccupantCabinBreakupTests prints the cut and the piece's mass. It lets go slightly earlier in the arrival
            // and leaves a shorter remnant in the water, which is why the speed at 1 s goes UP rather than down. Shared
            // geometry, read in one direction, NOT feedback from the view into the physics — the latter is what would
            // have made this a defect to stop for rather than a consequence to measure.
            Assert.True(worstStep < 2.0, $"{worstStep:F2} m/s lost in one step");
            Assert.True(Speed(ac) > 1.0, $"{Speed(ac):F2} m/s after 1 s");
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }
}
