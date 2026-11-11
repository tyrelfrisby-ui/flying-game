using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE PITTS ON THE GROUND (ABLATE=1; opt-in, nothing runs in the normal suite). The apparatus behind the
/// 2026-11-11 finding, kept so the numbers can be re-measured rather than re-argued. Four symptoms were put
/// together as one: the two failing landing rollouts, the failing swerve recovery, and a takeoff roll 25 % under
/// the published figure. They are TWO causes, not one and not four.
///
/// 1-3 ARE ONE CAUSE, and it is the MAIN-WHEEL LEAD -- how far the mains sit ahead of the CG. Bisected to
/// 99d0a584 (2026-10-28, "The Pitts and the Extra are the size their drawings say"), which re-zeroed the
/// config's x datum onto the lower wing leading edge so A8SO's certificated CG arm could be used. The gear did
/// not move relative to the airframe (0.498 m ahead of the lower wing LE before, 0.463 m after); the CG moved
/// 0.22 m AFT, from 0.266 m ahead of that leading edge to the certificated 0.045 m. Lead 0.232 -> 0.418 m. At
/// b594ad89, the commit that added the tests, the 3-point rollout held 1.7 deg; it is now 179.7 deg, a complete
/// ground loop that quick feet and differential brake cannot stop.
///   <see cref="Ablate"/> sweeps the lead: the cliff is between 0.385 and 0.355 m, every landing holds below
///   0.355, and both swerve recoveries only come inside their 35 deg limit below about 0.215 m - which is where
///   the lead was before 99d0a584, and which is also the only place GroundLoopTests' owner-sourced yaw-growth
///   band (x4-100 at 8 m/s) and these landings agree. Nothing else does it: not the fin (which that commit also
///   halved, and which fixes the rollouts but NOT the swerve recovery), not the toe-out, not the fin offset, not
///   the tailwheel spring, not Izz, not the leg linkage, not the prop terms.
///   WHY THE LEAD IS THE WHOLE STORY: <see cref="SwerveMomentBudget"/>. At 0.418 m the mains' side force
///   saturates at 99 % of its friction circle, 5.2 kN, and makes +2.2 kN.m about the CG, where everything behind
///   can answer with at most 1.2 kN.m from the tailwheel (0.48 mu, 15 % of the weight, 3.64 m) and 0.6 kN.m of
///   aero. The tailwheel cannot even reach that: its steering spring is 15 N.m/rad against C.trail = 210, so the
///   rudder owns 7 % of where the wheel points (the Extra's 60 owns 20 %), the ground drags it the wrong way, and
///   past 45 deg it breaks out and the tail's moment collapses from 0.95 to 0.35 kN.m.
///   NOT A TUNING QUESTION. The gear is measured (GearMatchesTheModels holds it to the GLB within 2.5 cm) and
///   the CG is certificated (A8SO). Both cannot be right. That test cannot arbitrate: it derives the GLB's
///   alignment from the same config's outer-wing chord, so it is a self-consistency check, not a measurement -
///   when 99d0a584 re-measured the wing stations the alignment moved 0.24-0.33 m and the gear was re-authored to
///   follow it. In terms the owner can settle with a plumb line: the main tyre contact patch is 0.463 m (18.2 in)
///   ahead of the lower wing leading edge, on a 1.016 m (40 in) lower-wing chord, and the tests want 0.26 m
///   (10 in). The Pitts' tip-over angle is 30.4 deg where the whole rest of the fleet is 18-24.
///   The CG HEIGHT (cg.z +0.20, unsourced, one of the twelve flagged) is not it: <see cref="Ablate"/> sweeps it
///   from +0.20 to -0.24 and no value fixes a single one of the three.
///
/// 4 IS A SEPARATE CAUSE, in the PROPELLER, and it is not a Pitts problem. <see cref="TakeoffRollAblation"/>:
/// the roll was 120.5 m with the pre-99d0a584 gear and CG and is 133.8 m now, so it is SHORTER before the
/// regression and shares nothing with 1-3; the takeoff holds heading to 1.0 deg and 0.5 m off the centreline,
/// which is the directional problem not appearing at all. The 25 % is two things:
///   - WEIGHT, about 12 %: the model flies 737 kg = 1625 lb, A8SO's acrobatic gross, while the published figure
///     is at 1700 lb. At 771 kg the same model rolls 149.6 m.
///   - THRUST, about 17 %: <see cref="ThrustCurve"/>. PropModel's thrust is Math.Min(eta.P/V, momentum-theory
///     static thrust), and for this type the static cap BINDS ACROSS THE WHOLE ROLL - flat 3943 N from 0 to
///     lift-off at 34 m/s, with no advance-ratio lapse at all. Proof: setting propulsion.efficiency 0.8 -> 0.65
///     changes the roll by ZERO metres, because the eta.P/V branch never binds. A real 78 in two-blade at
///     J = 0.2-0.4 makes 15-20 % less than static over that range; -15 % of power gives 156.2 m, and -15 % at
///     1700 lb gives 176.0 m against the published 170. The Pitts shows it worst because its static T/W, 0.545,
///     is the second highest in the fleet behind the Gee Bee. The Pitts never solves its blade in flight
///     (PropModel.IntegratesShaft needs bladePitch75Deg, which this type has not got, or fitted governor stops),
///     so it is on the estimated law for every metre of its takeoff.
///   AND THE TARGET IS THE WRONG AEROPLANE'S. The 554/557 ft ground roll and 750 ft landing roll in
///   AerobatGroundHandlingTests' own doc comments are published for the Pitts S-2C, at 1700 lb; no source
///   found gives a takeoff ground roll for the S-2B at all, and the S-2C is the later, cleaner airframe.
/// </summary>
[Collection("WorldTerrainActive")]
public class PittsGroundAblationProbe : IDisposable
{
    public void Dispose() => WorldTerrain.Active = null;
    private readonly ITestOutputHelper _out;
    public PittsGroundAblationProbe(ITestOutputHelper o) { _out = o; }
    private static bool On => Environment.GetEnvironmentVariable("ABLATE") != null;

    private static AircraftConfig Load(string f) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", f));

    private static string Row(AircraftConfig c, string name)
    {
        var t = AerobatGroundHandlingTests.Types.First(x => x.file.Contains(c.Id.Split('-')[0]));
        var l3 = AerobatGroundHandlingTests.Land(c, false, t.v3pt, 0.5, 0.3, 0.0);
        var lw = AerobatGroundHandlingTests.Land(c, true, t.vWheel, 0.3, 0.5, 0.0);
        var rec12 = AerobatGroundHandlingTests.Recover(c, 12, 8, 0.4);
        var rec18 = AerobatGroundHandlingTests.Recover(c, 18, 8, 0.4);
        var g8 = GroundStabilityProbe.YawRateGrowth(c, 8, 5, 3);
        return $"{name,-30} 3pt hdg {l3.MaxHdgDeg,6:F1}deg | wheel hdg {lw.MaxHdgDeg,6:F1}deg | swerve12 max {rec12.maxDeg,6:F1}deg end {rec12.endDeg,6:F1}deg | swerve18 max {rec18.maxDeg,6:F1}deg | growth@8 x{g8.ratio:F2} hdg {g8.heading,6:F1}deg";
    }

    [Fact]
    public void Ablate()
    {
        if (!On) return;
        var rows = new List<string>();
        void Try(string name, Action<AircraftConfig> mut)
        {
            var c = Load("pitts-s2b-like.json");
            mut(c);
            try { rows.Add(Row(c, name)); } catch (Exception e) { rows.Add($"{name,-30} THREW {e.GetType().Name} {e.Message}"); }
        }
        foreach (double mx in new[] { 0.463, 0.430, 0.400, 0.370, 0.336, 0.300, 0.260, 0.202 })
        {
            double v = mx;
            Try($"mains x {v:F3} (lead {v - 0.045:F3} m)", c => { foreach (var g in c.Gear) if (!g.IsTailwheel) g.Pos[0] = v; });
        }
        // The named knobs: what the breaking commit changed, and the whole list of things that do NOT do it.
        Try("baseline", _ => { });
        Try("GEAR pre-99d0a584", c => { foreach (var g in c.Gear) { if (g.IsTailwheel) { g.Pos[0] = -3.864; g.Pos[2] = 0.470; } else { g.Pos[0] = 0.202; g.Pos[2] = 1.194; } } });
        Try("  tailwheel station only", c => { foreach (var g in c.Gear) if (g.IsTailwheel) g.Pos[0] = -3.864; });
        Try("CG pre-99d0a584 [-.03,0,0]", c => { c.Mass.Cg[0] = -0.03; c.Mass.Cg[2] = 0.0; });
        Try("FIN pre-99d0a584 (vStab x1.81)", c => { foreach (var sf in c.Surfaces) if (sf.Id == "vStab") foreach (var st in sf.Strips) st.Area *= 1.81; });
        Try("GEAR+CG+FIN all pre-99d0a584", c => {
            foreach (var g in c.Gear) { if (g.IsTailwheel) { g.Pos[0] = -3.864; g.Pos[2] = 0.470; } else { g.Pos[0] = 0.202; g.Pos[2] = 1.194; } }
            c.Mass.Cg[0] = -0.03; c.Mass.Cg[2] = 0.0;
            foreach (var sf in c.Surfaces) if (sf.Id == "vStab") foreach (var st in sf.Strips) st.Area *= 1.81;
        });
        Try("toe 0 (authored 2 out)", c => { foreach (var g in c.Gear) g.ToeOutDeg = 0; });
        Try("toe 1 out (Ty's own number)", c => { foreach (var g in c.Gear) if (!g.IsTailwheel) g.ToeOutDeg = 1.0; });
        Try("toe 2 IN", c => { foreach (var g in c.Gear) if (!g.IsTailwheel) g.ToeOutDeg = -2.0; });
        Try("finOffset 0", c => c.Rig.FinOffsetRad = 0);
        Try("finOffset 1.997 deg (last night's)", c => c.Rig.FinOffsetRad = 1.997 * Math.PI / 180);
        Try("steerSpring 15 -> 60 (Extra's)", c => { foreach (var g in c.Gear) if (g.IsTailwheel) g.SteerSpringNmPerRad = 60; });
        Try("steerSpring 15 -> 6 (pre-10-28)", c => { foreach (var g in c.Gear) if (g.IsTailwheel) g.SteerSpringNmPerRad = 6; });
        Try("tailwheel cornering 2200->6600", c => { foreach (var g in c.Gear) if (g.IsTailwheel) g.CorneringStiffnessN = 6600; });
        Try("tailwheel mu 0.6 -> 0.8", c => { foreach (var g in c.Gear) if (g.IsTailwheel) g.TireMu = 0.8; });
        Try("main mu 0.8 -> 0.5", c => { foreach (var g in c.Gear) if (!g.IsTailwheel) g.TireMu = 0.5; });
        Try("legPivot null (no leg linkage)", c => { foreach (var g in c.Gear) g.LegPivot = null; });
        Try("maxSteerRad 0.45 -> 0.30", c => { foreach (var g in c.Gear) if (g.IsTailwheel) g.MaxSteerRad = 0.30; });
        Try("Izz 1185 -> 2000", c => c.Mass.Inertia.Izz = 2000);
        Try("pFactorK 0", c => c.Propulsion.PFactorK = 0);
        Try("slipstreamK 0", c => c.Propulsion.SlipstreamK = 0);
        foreach (double cz in new[] { 0.2, 0.0, -0.12, -0.24 })
        {
            double v = cz;
            Try($"cg.z {v:+0.00;-0.00} (CG height)", c => c.Mass.Cg[2] = v);
        }
        foreach (var r in rows) _out.WriteLine(r);
    }

    [Fact]
    public void TakeoffRollAblation()
    {
        if (!On) return;
        void Try(string name, Action<AircraftConfig> mut)
        {
            var c = Load("pitts-s2b-like.json"); mut(c);
            var t = AerobatGroundHandlingTests.Types.First(x => x.file.Contains("pitts"));
            var r = AerobatGroundHandlingTests.Takeoff(c, t.tailUp, t.vr, 10.09 - 0.5);
            _out.WriteLine($"{name,-34} roll {r.RollM,6:F1} m  V_lo {r.LiftoffV,5:F1} m/s ({r.LiftoffV * 2.237:F0} mph)  {r.RollS,4:F1} s  swing {r.HandsOffSwingDeg,6:F0}deg  rudder {r.MeanRudderTailUp * 100,4:F0}%  offCtr {r.MaxOffCentreM,4:F1} m hdg {r.MaxHdgDeg,4:F1}deg");
        }
        Try("baseline (HEAD)", _ => { });
        Try("gear+cg+fin pre-99d0a584", c => {
            foreach (var g in c.Gear) { if (g.IsTailwheel) { g.Pos[0] = -3.864; g.Pos[2] = 0.470; } else { g.Pos[0] = 0.202; g.Pos[2] = 1.194; } }
            c.Mass.Cg[0] = -0.03; c.Mass.Cg[2] = 0.0;
            foreach (var sf in c.Surfaces) if (sf.Id == "vStab") foreach (var st in sf.Strips) st.Area *= 1.81;
        });
        Try("mains x 0.260 only", c => { foreach (var g in c.Gear) if (!g.IsTailwheel) g.Pos[0] = 0.260; });
        Try("mass 737 -> 771 kg (1700 lb)", c => c.Mass.MassKg = 771);
        Try("efficiency 0.8 -> 0.65", c => c.Propulsion.Efficiency = 0.65);
        Try("maxPower -15% (thrust lapse proxy)", c => c.Propulsion.MaxPowerW = (int)(194000 * 0.85));
        Try("1700 lb AND power -15%", c => { c.Mass.MassKg = 771; c.Propulsion.MaxPowerW = (int)(194000 * 0.85); });
    }

    /// <summary>The takeoff roll, broken into what pushes and what holds back, every 10 m of ground roll.</summary>
    [Fact]
    public void RollBudget()
    {
        if (!On) return;
        foreach (string f in new[] { "pitts-s2b-like.json", "extra-300-like.json" })
        {
            var c = Load(f);
            WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null;
            Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null; Atmosphere.Field = null;
            WorldDecks.All.Clear(); WorldSolids.Shapes.Clear(); WorldSolids.Boxes.Clear();
            var rest = LandingGear.RestingState(c, 0, 0, 0);
            var ac = new Aircraft(c, rest, ControlDeflections.Neutral) { CaptureForces = true };
            var sim = new SimLoop(ac); const double dt = 0.01;
            Vec3 cg = c.Mass.CgVec();
            _out.WriteLine($"=== {c.Id}: mass {c.Mass.MassKg} kg, maxPower {c.Propulsion.MaxPowerW / 1000.0:F0} kW, eta {c.Propulsion.Efficiency}, D {c.Propulsion.PropDiameterM} m");
            double next = 0;
            for (double t = 0; t < 20; t += dt)
            {
                double lever = Math.Max(-1.0, 1.0 - t);
                var v = ac.State.Velocity; double sp = Math.Sqrt(v.X * v.X + v.Y * v.Y);
                sim.RunFor(dt, new ControlInputs(0, 0, 0, lever));
                double x = ac.State.Position.X;
                if (x >= next)
                {
                    var byKind = new Dictionary<string, double>();
                    foreach (var fo in ac.LastForces)
                    {
                        if (fo.Kind == "tailflow" || fo.Kind == "gyro") continue;
                        Vec3 fb = fo.ForceBody;
                        byKind[fo.Kind] = byKind.GetValueOrDefault(fo.Kind) + fb.X;
                    }
                    string s = string.Join("  ", byKind.OrderByDescending(k => Math.Abs(k.Value)).Where(k => Math.Abs(k.Value) > 2).Select(k => $"{k.Key} {k.Value:F0}"));
                    _out.WriteLine($" x {x,5:F0} m  t {t,4:F1}  V {sp,5:F1} m/s  rpm {ac.EngineRpm,5:F0}  pitch {Math.Asin(Math.Clamp(2 * (ac.State.Attitude.W * ac.State.Attitude.Y - ac.State.Attitude.Z * ac.State.Attitude.X), -1, 1)) * 57.3,5:F1}deg  Fx: {s}");
                    next = x + 10;
                }
                if (!LandingGear.AnyMainWheelOnGround(c, ac.State)) { _out.WriteLine($"  airborne at x {ac.State.Position.X:F0} m, V {sp:F1} m/s, t {t:F1} s"); break; }
            }
        }
    }

    /// <summary>Where the yaw moment comes from while the Pitts (and the Extra, for contrast) diverges out of an 8 deg
    /// swerve at 12 m/s with the pilot on the rudder and the brakes -- the same scenario as Recover().</summary>
    [Fact]
    public void SwerveMomentBudget()
    {
        if (!On) return;
        foreach (string f in new[] { "pitts-s2b-like.json", "extra-300-like.json" })
        {
            var c = Load(f);
            WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null;
            Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null; Atmosphere.Field = null;
            WorldDecks.All.Clear(); WorldSolids.Shapes.Clear(); WorldSolids.Boxes.Clear();
            var rest = LandingGear.RestingState(c, 0, 0, 0);
            double psi = 8 * Math.PI / 180;
            Quat att = Quat.Multiply(new Quat(0, 0, Math.Sin(psi / 2), Math.Cos(psi / 2)), rest.Attitude);
            var ac = new Aircraft(c, new RigidBodyState(rest.Position, att, att.Conjugate().Rotate(new Vec3(12, 0, 0)), Vec3.Zero), ControlDeflections.Neutral) { CaptureForces = true };
            var sim = new SimLoop(ac);
            Vec3 cg = c.Mass.CgVec();
            _out.WriteLine($"=== {c.Id}: Izz {c.Mass.Inertia.Izz}, tail arm {c.Gear.First(g => g.IsTailwheel).Pos[0] - cg.X:F2} m, main lead {c.Gear.First(g => !g.IsTailwheel).Pos[0] - cg.X:F3} m, steerSpring {c.Gear.First(g => g.IsTailwheel).SteerSpringNmPerRad}");
            // the same cascaded pilot Recover() flies, re-implemented here so the forces can be read each step
            double integ = 0;
            for (int i = 0; i < 160; i++)
            {
                var q = ac.State.Attitude;
                double hdg = Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z));
                double y = ac.State.Position.Y, r = ac.State.Rates.Z;
                double hdgErr = hdg - Math.Clamp(-0.015 * y, -0.04, 0.04);
                while (hdgErr > Math.PI) hdgErr -= 2 * Math.PI; while (hdgErr < -Math.PI) hdgErr += 2 * Math.PI;
                double rCmd = Math.Clamp(-1.2 * hdgErr, -0.26, 0.26), rErr = r - rCmd;
                double t = i * 0.05;
                double rud = 0;
                if (t >= 0.4) { integ = Math.Clamp(integ - 0.8 * rErr * 0.05, -0.6, 0.6); rud = Math.Clamp(integ - 6.0 * rErr, -1, 1); double b = Math.Clamp(1.5 * Math.Abs(rErr) - 0.05, 0, 0.6); ac.BrakeInput = b; ac.BrakeBias = b > 0 ? -Math.Sign(rErr) : 0; }
                sim.RunFor(0.05, new ControlInputs(0, -1, rud, 1.0));
                double mzMain = 0, fyMain = 0, mzTail = 0, fyTail = 0, mzAero = 0, mzProp = 0;
                double mainLongMz = 0, mainLatMz = 0;
                foreach (var fo in ac.LastForces)
                {
                    Vec3 rr = fo.PosBody - cg;
                    double m = rr.X * fo.ForceBody.Y - rr.Y * fo.ForceBody.X + fo.MomentBody.Z;
                    if (fo.Kind == "tailflow" || fo.Kind == "gyro") continue;
                    if (fo.Kind == "gear")
                    {
                        if (fo.PosBody.X < -1.5) { mzTail += m; fyTail += fo.ForceBody.Y; }
                        else { mzMain += m; fyMain += fo.ForceBody.Y; mainLatMz += rr.X * fo.ForceBody.Y; mainLongMz += -rr.Y * fo.ForceBody.X; }
                    }
                    else if (fo.Kind == "thrust" || fo.Kind == "prop" || fo.Kind == "slipstream") mzProp += m;
                    else mzAero += m;
                }
                if (i % 4 == 0)
                    _out.WriteLine($" t {t,4:F2} hdg {hdg * 57.3,7:F1}deg r {r * 57.3,7:F2}deg/s rud {rud,5:F2} brk {ac.BrakeInput:F2}/{ac.BrakeBias:F0} | mains Fy {fyMain,6:F0} Mz {mzMain,7:F0} (lat {mainLatMz,6:F0} long {mainLongMz,6:F0}) | tail Fy {fyTail,6:F0} Mz {mzTail,7:F0} | aero {mzAero,6:F0} | prop {mzProp,5:F0} | twAngle {(ac.Tailwheel?.AngleRad ?? 0) * 57.3,5:F1}deg free {(ac.Tailwheel?.FreeSwivel == true ? 1 : 0)} load {ac.Tailwheel?.LastLoadN ?? 0,6:F0}");
                if (Math.Abs(hdg) > 2.9) { _out.WriteLine("  ...gone round"); break; }
            }
        }
    }

    /// <summary>Static thrust and the thrust-speed curve each type's propeller model actually makes.</summary>
    [Fact]
    public void ThrustCurve()
    {
        if (!On) return;
        foreach (string f in new[] { "pitts-s2b-like.json", "extra-300-like.json" })
        {
            var c = Load(f);
            _out.WriteLine($"=== {c.Id}");
            foreach (double v in new[] { 0.0, 5.0, 10.0, 15.0, 20.0, 25.0, 30.0, 34.0, 40.0 })
                _out.WriteLine($"  V {v,5:F1} m/s  T {PropModel.Compute(c.Propulsion, 1.0, new Vec3(v, 0, 0), Vec3.Zero, 1.225).Force.X:F0} N  (static cap {PropModel.StaticThrustN(c.Propulsion, 1.0):F0} N, ideal P/V {(v > 0.5 ? c.Propulsion.MaxPowerW * c.Propulsion.Efficiency / v : 0):F0} N)");
        }
    }
}
