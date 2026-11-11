using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Owner 2026-11-02 (iPhone build 2026.1102.1803): "the Pitts launches into a right spiral dive immediately … on the very
/// next launch it went LEFT". Ten consecutive launches of every rigged aeroplane, hands off, for 10 s, stick FIXED and
/// stick FREE, through the game's own start (<see cref="AirStart"/>: the build, the seeded first-frame inputs
/// <see cref="AirStart.HeldInputs"/> on a long first frame of SimLoop.MaxCatchUpSec) and the game's own composition
/// (<see cref="PilotControls.Compose"/>). Stick FIXED is what a connected gamepad now gives by default (its option off);
/// stick FREE is a touch pad with no finger on it (or a pad with the option on). The process-wide sim clock is not reset.
///
/// Band, from the spiral mode. A hands-off aeroplane may still bank away by its natural spiral mode, which grows a bank
/// disturbance as 2^(t/T2). MIL-F-8785C §3.3.1.3 (Level 1, flight phases A and C) asks for a time to double of at least
/// T2 = 12 s, so after an initial bank error φ_ref the bank may be φ_ref·2^(t/12 s) at t. The start is the rig solution
/// (wings level, every moment balanced at the rig altitude and power), so its only bank error is the start-up of the
/// downwash and unsteady lags; taking φ_ref = 1° (a quarter of the smallest bank a pilot reads on the attitude
/// indicator's 10° marks… a generous allowance for that start-up), the band is |φ(t) − φ(0)| ≤ 1°·2^(t/12 s) at every
/// frame — 1.78° at 10 s. The phone's departure was a spiral dive within seconds: tens of degrees. All ten launches must
/// also be the SAME launch (1e-9 rad): a pure function of config and still air cannot pick a different side each time.
/// </summary>
[Collection("WorldTerrainActive")]
public sealed class HandsOffLaunchSpiralTests
{
    private const double D = 180.0 / Math.PI;
    private const int Launches = 10;
    private const double FrameDt = 1.0 / 60.0, Duration = 10.0;
    private const double SpiralT2 = 12.0, PhiRefDeg = 1.0;

    private readonly ITestOutputHelper _out;
    public HandsOffLaunchSpiralTests(ITestOutputHelper o) => _out = o;

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    /// <summary>Every config the build carries, from the DIRECTORY — not RigTable.Rows, which is a hand-written list and
    /// so could leave a type out of its own launch test by omission (the Grob 103 was, for two days).</summary>
    public static IEnumerable<object[]> Fleet() =>
        RigTests.ConfigIds().SelectMany(id => new[] { new object[] { id, true }, new object[] { id, false } });

    /// <summary>Bank, + = right wing down (body y axis below the horizon; world z is down).</summary>
    private static double Bank(Quat q) => Math.Asin(Math.Clamp(q.Rotate(new Vec3(0, 1, 0)).Z, -1, 1));

    private static (double WorstExcessDeg, double WorstBankDeg, double EndBankDeg, Quat End) Fly(AircraftConfig c, bool stickFree, ControlInputs? firstFrame = null)
    {
        AirStart.Plan plan = AirStart.Compute(c, 0, 0, RigSolver.RigAltitudeM, 0.0, c.SpawnIasMs > 0 ? c.SpawnIasMs : 22.0);
        Assert.Null(plan.Warning);
        var sim = new SimLoop(AirStart.Build(c, plan));
        double phi0 = Bank(sim.Aircraft.State.Attitude);
        double acc = 0;
        sim.Advance(SimLoop.MaxCatchUpSec, firstFrame ?? AirStart.HeldInputs(plan), ref acc);   // the long first frame on the spawn's seeded inputs
        double t = SimLoop.MaxCatchUpSec, worstExcess = double.NegativeInfinity, worstBank = 0;
        // Stick fixed: a gamepad connected at rest, its stick-free option at the default (off). Stick free: no hand on a touch pad.
        bool pad = !stickFree;
        while (t < Duration - 1e-9)
        {
            ControlInputs u = PilotControls.Compose(0, 0, 0, 0, plan.TrimStick, 0, plan.Lever, false, false,
                gamepadConnected: pad, gamepadStickFree: GamepadShaping.DefaultGamepadStickFree);
            Assert.Equal(stickFree, u.AileronFree);
            sim.Advance(FrameDt, u, ref acc);
            t += FrameDt;
            double dphi = Math.Abs(Bank(sim.Aircraft.State.Attitude) - phi0) * D;
            worstBank = Math.Max(worstBank, dphi);
            worstExcess = Math.Max(worstExcess, dphi - PhiRefDeg * Math.Pow(2, t / SpiralT2));
        }
        Quat e = sim.Aircraft.State.Attitude;
        return (worstExcess, worstBank, (Bank(e) - phi0) * D, e);
    }

    [Theory]
    [MemberData(nameof(Fleet))]
    public void TenHandsOffLaunchesStayInsideTheSpiralMode(string id, bool stickFree)
    {
        using var still = StillAir();
        AircraftConfig c = Load(id);
        var runs = Enumerable.Range(0, Launches).Select(_ => Fly(c, stickFree)).ToList();
        var a = runs[0];
        double spread = runs.Skip(1).Max(b =>
        {
            Quat qa = a.End, qb = b.End;
            double sgn = qa.W * qb.W + qa.X * qb.X + qa.Y * qb.Y + qa.Z * qb.Z < 0 ? -1 : 1;
            return 2 * Math.Max(Math.Max(Math.Abs(qa.W - sgn * qb.W), Math.Abs(qa.X - sgn * qb.X)), Math.Max(Math.Abs(qa.Y - sgn * qb.Y), Math.Abs(qa.Z - sgn * qb.Z)));
        });
        _out.WriteLine($"{id} stick {(stickFree ? "FREE " : "FIXED")}: {Launches} launches, spread {spread:E1} rad; worst |Δφ| {a.WorstBankDeg:F3}°, "
            + $"Δφ at {Duration:F0} s {a.EndBankDeg:+0.000;-0.000;0.000}° (band {PhiRefDeg * Math.Pow(2, Duration / SpiralT2):F2}°)");
        Assert.True(spread < 1e-9, $"{id}: the {Launches} launches differ by {spread:E2} rad");
        Assert.True(a.WorstExcessDeg <= 0, $"{id} stick {(stickFree ? "free" : "fixed")}: bank left the spiral-mode band by {a.WorstExcessDeg:F3}° (worst |Δφ| {a.WorstBankDeg:F3}°)");
    }

    /// <summary>The gamepad default, through the game's composition: a connected pad at rest holds its surfaces unless the
    /// player turned stick-free on; a touch pad with no finger lets go as before.</summary>
    [Fact]
    public void AConnectedPadAtRestHoldsTheControlsByDefault()
    {
        ControlInputs padDefault = PilotControls.Compose(0, 0, 0, 0, 0.1, 0, 0, false, false, gamepadConnected: true, gamepadStickFree: GamepadShaping.DefaultGamepadStickFree);
        Assert.False(padDefault.AileronFree || padDefault.ElevatorFree || padDefault.RudderFree);
        ControlInputs padOption = PilotControls.Compose(0, 0, 0, 0, 0.1, 0, 0, false, false, gamepadConnected: true, gamepadStickFree: true);
        Assert.True(padOption.AileronFree && padOption.ElevatorFree && padOption.RudderFree);
        ControlInputs touch = PilotControls.Compose(0, 0, 0, 0, 0.1, 0, 0, false, false);
        Assert.True(touch.AileronFree && touch.ElevatorFree && touch.RudderFree);
    }

    /// <summary>The band does see the phone's departure. Build 2026.1102.1803 predates the spawn seeding its inputs
    /// (FlightSimDriver.Spawn, b62b67d0): the long first frame flew whatever the PREVIOUS flight left in
    /// FlightSimDriver.Inputs — a stick and pedals held over, so a side that changed from launch to launch — before the
    /// pad published "hands off". Replayed here with the last flight's stick held full right (then full left), held
    /// through that one frame: stick free it leaves the spiral-mode band, to the side the stale stick was on.</summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(-1.0)]
    public void AStaleFirstFrameIsTheDepartureTheBandCatches(double side)
    {
        using var still = StillAir();
        AircraftConfig c = Load("pitts-s2b-like");
        AirStart.Plan plan = AirStart.Compute(c, 0, 0, RigSolver.RigAltitudeM, 0.0, c.SpawnIasMs > 0 ? c.SpawnIasMs : 22.0);
        var stale = new ControlInputs(side, plan.TrimStick, side, plan.Lever);   // the previous flight's hands, held
        var r = Fly(c, stickFree: true, firstFrame: stale);
        _out.WriteLine($"stale first frame {side:+0;-0}: worst |Δφ| {r.WorstBankDeg:F2}°, Δφ at {Duration:F0} s {r.EndBankDeg:+0.00;-0.00}°");
        Assert.True(r.WorstExcessDeg > 0, $"a stale full-{(side > 0 ? "right" : "left")} first frame stayed inside the band ({r.WorstBankDeg:F2}°)");
        Assert.Equal(Math.Sign(side), Math.Sign(r.EndBankDeg));
    }

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
}
