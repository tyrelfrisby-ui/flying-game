using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The J-3 floatplane's water rudder (Ty 2026-11-02, t278u): spring-loaded DOWN, raised by hanging its cockpit handle on
/// the hook, and a hydrodynamic surface from first principles: ½·ρ_water·V²·S·a·α on the blade's immersed area, with
/// the lift-curve slope from its aspect ratio (Helmbold). Effective only when down and in the water; up = no force.
///
/// The literals are the drawn blade (build_j3_cub.py, Edo drawing 60-S-908A at 16.53 px/in): chord 150 px = 0.2305 m,
/// span 160 px = 0.2459 m (v18 t286u: v17's 212 x 112 px stood 8 cm over the post top retracted), an ellipse of
/// S = π/4·c·b = 0.04452 m², AR = b²/S = 1.3583, a = 2π·AR/(2+√(AR²+4)) = 1.9318 /rad. Max rudder 25° (the rig's WaterRudder_L ±25).
/// </summary>
[Collection("WorldTerrainActive")]
public class J3WaterRudderTests
{
    private const string Id = "j3-cub-floats-like";
    private const double S = 0.04452, A = 1.9318, Rho = 1000.0, MaxDelta = 25.0 * System.Math.PI / 180.0;
    private readonly ITestOutputHelper _out;
    public J3WaterRudderTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load() =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", Id + ".json"));
    private static double Lever(double throttle01) => 1.0 - 2.0 * throttle01;
    private static double Yaw(RigidBodyState s) { Quat q = s.Attitude; return Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z)) * 57.3; }

    private static (Aircraft ac, SimLoop sim) Afloat(AircraftConfig cfg)
    {
        var state = new RigidBodyState(new Vec3(0, 0, -(cfg.Floats!.KeelZ - 0.25)), new Quat(0, 0, 0, 1), Vec3.Zero, Vec3.Zero);
        var ac = new Aircraft(cfg, state, ControlDeflections.Neutral);
        var sim = new SimLoop(ac);
        sim.RunFor(15.0, new ControlInputs(0, 0, 0, Lever(0)));
        return (ac, sim);
    }

    /// <summary>The settled floating pose with a forward speed and no rotation: what the blade feels on a straight taxi.</summary>
    private static RigidBodyState Moving(RigidBodyState rest, double v) => new(rest.Position, rest.Attitude, new Vec3(v, 0, 0), Vec3.Zero);

    [Fact]
    public void TheBladeIsTheDrawnOneAndItsSlopeIsHelmbolds()
    {
        FloatsConfig f = Load().Floats!;
        double s = System.Math.PI / 4 * f.WaterRudderChordM * f.WaterRudderSpanM, ar = f.WaterRudderSpanM * f.WaterRudderSpanM / s;
        double a = 2 * System.Math.PI * ar / (2 + System.Math.Sqrt(ar * ar + 4));
        _out.WriteLine($"blade: c {f.WaterRudderChordM} m, b {f.WaterRudderSpanM} m, S {s:F5} m², AR {ar:F4}, a {a:F4} /rad; max {f.WaterRudderMaxRad * 57.29578:F1} deg; left float only {f.WaterRudderLeftOnly}");
        Assert.Equal(S, s, 4);
        Assert.Equal(1.3583, ar, 3);
        Assert.Equal(A, a, 3);
        Assert.Equal(25.0, f.WaterRudderMaxRad * 180 / System.Math.PI, 1);
        Assert.True(f.WaterRudderLeftOnly);
        Assert.True(f.WaterRudderRetractable);
    }

    [Fact]
    public void ItStartsDownAndTheHookRaisesIt()
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig cfg = Load();
            (Aircraft ac, SimLoop sim) = Afloat(cfg);
            Assert.True(ac.HasWaterRudderRetract);
            Assert.False(ac.WaterRudderHooked);
            Assert.Equal(0.0, ac.WaterRudderUp01);          // a water start: the spring has it down
            ac.ToggleWaterRudder();                          // hang the handle on the hook
            sim.RunFor(0.4, new ControlInputs(0, 0, 0, Lever(0)));
            Assert.InRange(ac.WaterRudderUp01, 0.3, 0.7);    // on its way: the hand takes 0.8 s
            sim.RunFor(0.6, new ControlInputs(0, 0, 0, Lever(0)));
            Assert.Equal(1.0, ac.WaterRudderUp01);
            ac.ToggleWaterRudder();                          // off the hook: the spring drops it
            sim.RunFor(1.0, new ControlInputs(0, 0, 0, Lever(0)));
            Assert.Equal(0.0, ac.WaterRudderUp01);
            ac.WaterRudderHeldUp01 = 0.5;                    // a VR hand holding it half way
            sim.RunFor(0.1, new ControlInputs(0, 0, 0, Lever(0)));
            Assert.Equal(0.5, ac.WaterRudderUp01);
            // A landplane has no retract.
            var cub = new Aircraft(AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "j3-cub-like.json")),
                new RigidBodyState(new Vec3(0, 0, -100), new Quat(0, 0, 0, 1), new Vec3(30, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
            Assert.False(cub.HasWaterRudderRetract);
            cub.ToggleWaterRudder();
            Assert.False(cub.WaterRudderHooked);
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    /// <summary>Down and wholly under water (the hull pushed 0.3 m deeper than it floats, so every cell is wet), straight
    /// ahead with full right pedal: no sideslip and no yaw rate, so α = δ = 25° and the lift is q̄·S·a·δ with the drawn
    /// blade's S and Helmbold a. In open water q̄ would be ½ρV², 3 m/s: ½·1000·9·0.04452·1.9318·0.4363 = 168.9 N; the
    /// blade's top hangs 1.7 cm under the keel, inside the hull's 6 cm boundary layer, so it is a little less (2026-11-05:
    /// the float's wake, J3WaterRudderWakeTests).</summary>
    [Fact]
    public void WhollyUnderItsLiftIsHalfRhoVSquaredSAAlpha()
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig cfg = Load();
            (Aircraft ac, _) = Afloat(cfg);
            RigidBodyState m = Moving(ac.State, 3.0);
            m = new RigidBodyState(m.Position + new Vec3(0, 0, 0.3), m.Attitude, m.Velocity, m.Rates);
            FloatHydro.Compute(cfg, m, 1.0, 0.0);
            FloatHydro.WaterRudderReport r = FloatHydro.LastWaterRudder;
            _out.WriteLine($"wholly under, 3 m/s, 25 deg: S_imm {r.ImmersedAreaM2:F5} m², span {r.ImmersedSpanM:F4} m, AR {r.AspectRatio:F4}, a {r.LiftSlopePerRad:F4}/rad, lift {r.LiftN:F1} N, drag {r.DragN:F2} N, yaw {r.YawMomentNm:F0} N·m");
            Assert.Equal(S, r.ImmersedAreaM2, 4);
            Assert.Equal(0.2459, r.ImmersedSpanM, 3);
            Assert.Equal(A, r.LiftSlopePerRad, 2);
            Assert.Equal(-25.0, r.AlphaRad * 180 / System.Math.PI, 1);    // flow onto the blade's left face
            Assert.Equal(0.5 * Rho * 9.0, r.FreeStreamPressurePa, 6);
            Assert.Equal(r.DynamicPressurePa * r.ImmersedAreaM2 * r.LiftSlopePerRad * r.AlphaRad, r.LiftN, 6);
            Assert.InRange(-r.LiftN, 0.97 * 168.9, 168.9);                // the wake only shaves its top
            Assert.True(r.SideForceBodyN < 0, "TE right must push the stern left");
            Assert.True(r.YawMomentNm > 0, "and yaw the nose right");
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    /// <summary>Down, afloat as it really floats, straight ahead at taxi speeds with full right pedal. The hinge rides
    /// above the water and only the blade's lower part is wet, so S_imm and a are the wetted part's, and the lift is
    /// q̄·S_imm·a(AR_imm)·δ, q̄ the wetted cells' mean ½ρw² (a little under ½ρV²: the hull's wake). The numbers the report quotes.</summary>
    [Fact]
    public void AfloatItsForceIsHalfRhoVSquaredOnTheWettedPart()
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig cfg = Load();
            (Aircraft ac, _) = Afloat(cfg);
            foreach (double v in new[] { 2.0, 4.0, 6.0 })
            {
                FloatHydro.Compute(cfg, Moving(ac.State, v), 1.0, 0.0);
                FloatHydro.WaterRudderReport r = FloatHydro.LastWaterRudder;
                _out.WriteLine($"afloat, V {v} m/s, full right pedal, rudder DOWN: S_imm {r.ImmersedAreaM2:F5} m² ({r.ImmersedAreaM2 / S * 100:F0} %), span {r.ImmersedSpanM:F3} m, AR {r.AspectRatio:F3}, a {r.LiftSlopePerRad:F3}/rad, "
                               + $"alpha {r.AlphaRad * 57.29578:F1} deg, Re {r.ReynoldsNumber:E2}, lift {-r.LiftN:F1} N, drag {r.DragN:F2} N, side force {r.SideForceBodyN:F1} N, yaw moment {r.YawMomentNm:F0} N·m");
                Assert.InRange(r.ImmersedAreaM2, 0.2 * S, S);
                double ar = r.ImmersedSpanM * r.ImmersedSpanM / r.ImmersedAreaM2;
                Assert.Equal(2 * System.Math.PI * ar / (2 + System.Math.Sqrt(ar * ar + 4)), r.LiftSlopePerRad, 9);
                Assert.Equal(-r.DynamicPressurePa * r.ImmersedAreaM2 * r.LiftSlopePerRad * System.Math.Abs(r.AlphaRad), r.LiftN, 6);
                Assert.InRange(r.DynamicPressurePa, 0.9 * 0.5 * Rho * v * v, 0.5 * Rho * v * v);   // the hull's wake: a little under ½ρV²
                Assert.Equal(MaxDelta, System.Math.Abs(r.AlphaRad), 1);   // straight ahead: α is the pedal's δ (±the hull's slight trim)
                Assert.True(r.SideForceBodyN < 0 && r.YawMomentNm > 0);
            }
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    /// <summary>Hung on the hook. Ty 2026-11-04 (Q024): the retracted blade's tip still in the water at rest is real — the
    /// drawn hinge sits at the keel line at the heel, and the drawn Edo float rests bow-up with the heel ~8 cm deep. So up
    /// is not "no force": it is the same ½ρV²·S·a·α on whatever stays wet, and that is a small fraction of the down
    /// blade's — under a quarter of the pedal authority at 3 m/s, and nothing wet at all by a 5.5 m/s taxi.</summary>
    [Fact]
    public void UpOnlyItsTipStaysWetAndThePedalsDoLittle()
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig cfg = Load();
            (Aircraft ac, _) = Afloat(cfg);
            double Swing(double v, double retract01)
            {
                RigidBodyState m = Moving(ac.State, v);
                (_, Vec3 mL) = FloatHydro.Compute(cfg, m, -1.0, retract01);
                (_, Vec3 mR) = FloatHydro.Compute(cfg, m, 1.0, retract01);
                return ac.State.Attitude.Conjugate().Rotate(mR - mL).Z;
            }
            double down3 = Swing(3.0, 0.0), up3 = Swing(3.0, 1.0);
            FloatHydro.Compute(cfg, Moving(ac.State, 3.0), 1.0, 1.0); double upWet = FloatHydro.LastWaterRudder.ImmersedAreaM2;
            FloatHydro.Compute(cfg, Moving(ac.State, 3.0), 1.0, 0.0); double downWet = FloatHydro.LastWaterRudder.ImmersedAreaM2;
            FloatHydro.Compute(cfg, Moving(ac.State, 3.0), 1.0, 0.5); double halfWet = FloatHydro.LastWaterRudder.ImmersedAreaM2;
            _out.WriteLine($"3 m/s, full left to full right pedal: yaw moment swing {down3:F0} N·m down, {up3:F0} up; wet {downWet:F5} / {halfWet:F5} (half up) / {upWet:F5} m² up");
            Assert.True(down3 > 150, $"down, the pedals moved the yaw moment by only {down3:F0} N·m");
            Assert.True(upWet > 0, "at rest attitude the hooked blade's tip is in the water (Ty: real)");
            Assert.True(upWet < 0.5 * downWet, $"hooked, {upWet:F5} m² wet against {downWet:F5} down");
            Assert.InRange(halfWet, upWet, downWet);
            Assert.True(System.Math.Abs(up3) < 0.25 * down3, $"hooked, the pedals still swing {up3:F0} N·m against {down3:F0} down");
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    /// <summary>Only the left float has one (Kenmore 60-1320: RH float 'less water rudder').</summary>
    [Fact]
    public void OnlyTheLeftFloatCarriesOne()
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig cfg = Load();
            (Aircraft ac, _) = Afloat(cfg);
            FloatHydro.Compute(cfg, Moving(ac.State, 3.0), 1.0, 0.0);
            FloatHydro.WaterRudderReport r = FloatHydro.LastWaterRudder;
            Assert.True(r.ImmersedAreaM2 > 0);
            // The right float carries none: a left-pedal / right-pedal swing changes the force only by one blade's
            // worth (the left one's side force, twice).
            RigidBodyState m = Moving(ac.State, 3.0);
            (Vec3 fR, _) = FloatHydro.Compute(cfg, m, 1.0, 0.0); double sideR = FloatHydro.LastWaterRudder.SideForceBodyN;
            (Vec3 fL, _) = FloatHydro.Compute(cfg, m, -1.0, 0.0); double sideL = FloatHydro.LastWaterRudder.SideForceBodyN;
            Vec3 dB = ac.State.Attitude.Conjugate().Rotate(fR - fL);
            Assert.Equal(sideR - sideL, dB.Y, 6);
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    /// <summary>An idle taxi turn on full right pedal, water rudder down and up. At ~1.5 m/s the blade's ½ρV² is small,
    /// so the turn is mostly the air rudder in the slipstream and the floats' own yaw: down turns a little tighter, never
    /// less. Ty 2026-11-04 20:27 (Q024): 254° in 20 s with the rudder UP is acceptable; he will fly it. The 254 is that
    /// figure, copied here by hand.</summary>
    [Fact]
    public void AnIdleTaxiTurnIsAirRudderLedAndTheBladeOnlyAdds()
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            double Turn(bool up)
            {
                AircraftConfig cfg = Load();
                (Aircraft ac, SimLoop sim) = Afloat(cfg);
                ac.SetWaterRudderHooked(up, immediate: true);
                for (double t = 0; t < 30; t += 0.1) sim.RunFor(0.1, new ControlInputs(0, -0.6, 0, Lever(0.04)));
                double turned = 0, last = Yaw(ac.State);
                for (double t = 0; t < 20; t += 0.1)
                {
                    sim.RunFor(0.1, new ControlInputs(0, -0.6, 1.0, Lever(0.04)));
                    double now = Yaw(ac.State); turned += (now - last + 540) % 360 - 180; last = now;
                }
                _out.WriteLine($"idle, full right pedal 20 s, water rudder {(up ? "UP" : "DOWN")}: turned {turned:F0} deg, {ac.State.Velocity.Length:F2} m/s");
                return turned;
            }
            double down = Turn(false), up = Turn(true);
            Assert.True(down > up, $"down turned {down:F0} deg, up {up:F0} deg");
            Assert.InRange(up, 254.0 - 15.0, 254.0 + 15.0);
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    /// <summary>Hooked, the blade has swung 90° up and aft about its hinge and stands behind the post (v18, t286u). The
    /// hinge is drawn at the keel line at the heel, so at rest — float bow-up, heel ~8 cm deep — the hooked blade's lowest
    /// few centimetres stay wet (Ty 2026-11-04, Q024: real). Taxiing at ~5.5 m/s the float rides up and it is dry for any
    /// pedal; down, a good part of the blade is wet in both. Plowing through the hump the stern squats the hinge itself
    /// under, and that is reported, not hidden.</summary>
    [Fact]
    public void HookedOnlyItsTipIsWetAtRestAndItIsDryTaxiing()
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig cfg = Load();
            Assert.Equal(90.0, cfg.Floats!.WaterRudderRetractRad * 180 / System.Math.PI, 1);
            (Aircraft ac, SimLoop sim) = Afloat(cfg);
            ac.SetWaterRudderHooked(true, immediate: true);
            RigidBodyState rest = ac.State;
            for (double t = 0; t < 30; t += 0.1) sim.RunFor(0.1, new ControlInputs(0, -0.6, 0, Lever(0.10)));
            RigidBodyState taxi = ac.State;
            foreach ((string what, RigidBodyState st) in new[] { ("rest", rest), ("taxi", taxi) })
                foreach (double cmd in new[] { -1.0, 0.0, 1.0 })
                {
                    FloatHydro.Compute(cfg, st, cmd, 0.0);
                    double down = FloatHydro.LastWaterRudder.ImmersedAreaM2;
                    FloatHydro.Compute(cfg, st, cmd, 1.0);
                    double up = FloatHydro.LastWaterRudder.ImmersedAreaM2;
                    _out.WriteLine($"{what}, pedal {cmd:+0;-0;0}: wet {down:F5} m² down, {up:F5} hooked");
                    Assert.True(down > 0.3 * S, $"{what}: down, only {down:F4} m² wet");
                    Assert.InRange(up, 1e-6, 0.5 * down);
                }
            for (double t = 0; t < 30; t += 0.1) sim.RunFor(0.1, new ControlInputs(0, -0.6, 0, Lever(0.30)));
            FloatHydro.Compute(cfg, ac.State, 0.0, 1.0);
            _out.WriteLine($"hooked: rest pitch {Pitch(rest):F1} deg, taxi ({taxi.Velocity.X:F1} m/s, pitch {Pitch(taxi):F1}); plowing ({ac.State.Velocity.X:F1} m/s, pitch {Pitch(ac.State):F1}) {FloatHydro.LastWaterRudder.ImmersedAreaM2:F4} m² wet");
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    /// <summary>Ty, t286u: "the retracted water-rudder blade must sit COMPLETELY BEHIND the vertical round tube (the rudder
    /// post at the float stern), aligned with it in side and top views." The blade is in the post's own plane (the sim's
    /// plate turns about the post, so there is no sideways offset to have), and every point of its edge is aft of the
    /// post's 11 mm radius both down and retracted; retracted, all of it is above the hinge, standing up behind the post.
    /// The model is held to the same by build_j3_cub.py's assert on the GLB's own vertices.</summary>
    [Fact]
    public void RetractedItStandsWhollyBehindThePostInLineWithIt()
    {
        FloatsConfig f = Load().Floats!;
        const double PostR = 0.011;
        double ct = System.Math.Cos(f.WaterRudderRetractRad), st = System.Math.Sin(f.WaterRudderRetractRad);
        double minDownAft = double.MaxValue, minUpAft = double.MaxValue, maxUpBelow = double.MinValue;
        for (int i = 0; i < 360; i++)
        {
            double t = i * System.Math.PI / 180;
            double a0 = f.WaterRudderBladeAftM + 0.5 * f.WaterRudderChordM * System.Math.Cos(t);
            double d0 = f.WaterRudderBladeBelowM + 0.5 * f.WaterRudderSpanM * System.Math.Sin(t);
            double a1 = a0 * ct + d0 * st, d1 = d0 * ct - a0 * st;     // FloatHydro.DrawnWaterRudder's swing
            minDownAft = System.Math.Min(minDownAft, a0); minUpAft = System.Math.Min(minUpAft, a1); maxUpBelow = System.Math.Max(maxUpBelow, d1);
        }
        _out.WriteLine($"blade edge aft of the post axis: down {minDownAft:F3} m, up {minUpAft:F3} m; up, its lowest point {-maxUpBelow:F3} m above the hinge");
        Assert.True(minDownAft > PostR, $"down, the blade reaches {minDownAft:F3} m from the post axis");
        Assert.True(minUpAft > PostR, $"up, the blade reaches {minUpAft:F3} m from the post axis");
        Assert.True(maxUpBelow < 0, "up, the whole blade is above the hinge");
        Assert.InRange(f.WaterRudderRetractRad * 180 / System.Math.PI, 89.9, 90.1);   // standing up behind the post, not leaning
    }

    static double Pitch(RigidBodyState s) { Vec3 f = s.Attitude.Rotate(new Vec3(1, 0, 0)); return -System.Math.Asin(f.Z) * 180 / System.Math.PI; }
}
