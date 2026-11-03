using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Owner 2026-11-02: "on the very next launch the Pitts went LEFT instead of right" — the direction varied between
/// launches. These fly several CONSECUTIVE in-air launches through the code the game uses for one:
///
///  • <see cref="AirStart"/> builds the aeroplane (lifted out of FlightSimDriver.Spawn, so it IS the game's start);
///  • the first rendered frame steps the sim on the inputs the driver seeds at spawn (<see cref="AirStart.HeldInputs"/>),
///    for a long first frame (SimLoop.MaxCatchUpSec = 0.05 s, ten steps) — the frame before the controls publish;
///  • then every frame the pilot's hands go through <see cref="PilotControls.Compose"/> (TouchFlightControls'
///    composition) and <see cref="SimLoop.Advance"/> at 60 fps, as FlightSimDriver.Update does;
///  • the process-wide sim clock (Atmosphere.SimTimeSec) is NOT reset between launches, as it is not in the game;
///  • launches 2–5 have a gamepad connected, its sticks and triggers at rest with a different seeded noise every
///    launch, inside the pad's dead zones (|noise| ≤ 0.03 per stick axis, radius ≤ 0.042 against the 0.07 radial zone;
///    ≤ 0.02 on a trigger against 0.05) — a pad at rest typically reads 1–3 % off centre;
///  • stick-free ON (hands-off lets the reversible surfaces float) and OFF (surfaces held) — for the pad launches
///    through the "Gamepad: stick-free" option itself (OFF is its default), so the five launches also show that a pad
///    with the option off flies exactly as held surfaces do, and with it on exactly as a lifted finger does.
///
/// What stays Unity-only and untested here: the order Unity runs the scripts in, the touch pointer handling
/// (ReadPointers), GamepadInput's device read, and TouchFlightControls.PresetTrim (mirrored below: power at 75 %,
/// trims centred). The gamepad glue in <see cref="PadFrame"/> mirrors TouchFlightControls.GamepadFlight line for line.
///
/// Bands. LAUNCH SPREAD: every launch must be the same launch to the bit — the start is a pure function of config and
/// still air, and a pad at rest inside its dead zone hands the sim exactly zero — so the band is 1e-6 m / 1e-9 rad,
/// which no physical difference fits inside. HANDS OFF, at the rig altitude, measured from 20 s to 60 s (the first
/// 20 s carry the start-up of the downwash and unsteady lags): the same bands as RigTests.HandsOffLevelAtRigPower —
/// rates below 0.25 °/s (a twelfth of standard rate), sideslip below 0.2° (a quarter of a ball width), altitude within
/// 15 m (50 ft) and vertical speed below 1.5 m/s for the powered types, and a heading change below 2° over the 40 s
/// (a fifth of the PTS ±10° for straight and level).
/// </summary>
[Collection("WorldTerrainActive")]
public sealed class LaunchReplicationTests
{
    private const double D = 180.0 / Math.PI;
    private const int Launches = 5;
    private const double FrameDt = 1.0 / 60.0, FirstFrameDt = SimLoop.MaxCatchUpSec;
    private const double Settle = 20.0, Window = 40.0;

    private readonly ITestOutputHelper _out;
    public LaunchReplicationTests(ITestOutputHelper o) => _out = o;

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    public static IEnumerable<object[]> Fleet() =>
        RigTable.Rows.SelectMany(r => new[] { new object[] { r.Id, true }, new object[] { r.Id, false } });

    /// <summary>One launch's record: the end state and the hands-off figures over the window.</summary>
    private sealed record Launch(RigidBodyState End, double MaxP, double MaxQ, double MaxR, double MaxBeta, double AltChange, double MaxVs, double HeadingChange);

    /// <summary>TouchFlightControls.GamepadFlight, the sticks: the pad takes the stick only while it is deflected (and
    /// the one frame after), and its output is lifted past the aeroplane's own dead zone.</summary>
    private sealed class PadFrame
    {
        public double Aileron, Elevator, Rudder;
        private bool _stickWas, _rudderWas;
        public void Apply(double rx, double ry, double lx, double lt, double rt, AircraftConfig c)
        {
            var (roll, pitch) = GamepadShaping.Stick(rx, ry);
            double rud = GamepadShaping.Rudder(lx, lt, rt);
            bool stick = roll != 0.0 || pitch != 0.0;
            if (stick || _stickWas)
            {
                Aileron = GamepadShaping.PreCompensateDeadZone(roll, c.Controls.Aileron.DeadZone);
                Elevator = GamepadShaping.PreCompensateDeadZone(pitch, c.Controls.Elevator.DeadZone);   // pad y + = pushed away = nose down = sim +; InvertElevator off
            }
            _stickWas = stick;
            bool r = rud != 0.0;
            if (r || _rudderWas) Rudder = GamepadShaping.PreCompensateDeadZone(rud, c.Controls.Rudder.DeadZone);
            _rudderWas = r;
        }
    }

    private static Launch Fly(AircraftConfig c, bool stickFree, int? padSeed)
    {
        AirStart.Plan plan = AirStart.Compute(c, 0, 0, RigSolver.RigAltitudeM, 0.0, c.SpawnIasMs > 0 ? c.SpawnIasMs : 22.0);
        Assert.Null(plan.Warning);
        var sim = new SimLoop(AirStart.Build(c, plan));
        // TouchFlightControls.PresetTrim for an air start: the pitch trim on the spawn trim, aileron and rudder trim centred,
        // the lever at the rig power (a glider's spoilers stowed).
        double pitchTrim = plan.TrimStick, aileronTrim = 0.0, rudderTrim = 0.0, lever = plan.Lever;
        var pad = new PadFrame();
        var rng = padSeed.HasValue ? new Random(padSeed.Value) : null;
        double Noise(double a) => (rng!.NextDouble() * 2 - 1) * a;

        double acc = 0;
        ControlInputs inputs = AirStart.HeldInputs(plan);   // what the driver seeded at spawn
        sim.Advance(FirstFrameDt, inputs, ref acc);          // the long first frame, before the controls publish
        double t = FirstFrameDt;
        double alt0 = 0, psi0 = 0, maxP = 0, maxQ = 0, maxR = 0, maxB = 0, maxVs = 0;
        bool started = false;
        while (t < Settle + Window - 1e-9)
        {
            // A pad at rest: noise INSIDE its dead zones. Sticks ±0.014 per axis (the right stick's radius then stays under the
            // 0.02 radial zone, Ty 2026-11-03; was ±0.03 inside 0.07), triggers 0…0.02 under their 0.05.
            if (rng != null) pad.Apply(Noise(0.014), Noise(0.014), Noise(0.014), Math.Abs(Noise(0.02)), Math.Abs(Noise(0.02)), c);
            // With the pad: the game's own path, the player's "Gamepad: stick-free" option going straight into the composition
            // (GamepadShaping.ControlsFree, lane/claude-hydro). Touch always lets go when released, so the touch launch flies
            // stick-free OFF as the surfaces HELD — the same physics the pad's default gives.
            ControlInputs u = PilotControls.Compose(pad.Aileron, pad.Elevator, pad.Rudder, aileronTrim, pitchTrim, rudderTrim, lever, false, false,
                gamepadConnected: rng != null, gamepadStickFree: stickFree);
            if (rng == null && !stickFree) u = new ControlInputs(u.Aileron, u.Elevator, u.Rudder, u.ThrottleLever);   // surfaces held
            sim.Advance(FrameDt, u, ref acc);
            t += FrameDt;
            RigidBodyState s = sim.Aircraft.State;
            if (!started && t >= Settle) { started = true; alt0 = -s.Position.Z; psi0 = Heading(s.Attitude); }
            if (started)
            {
                maxP = Math.Max(maxP, Math.Abs(s.Rates.X)); maxQ = Math.Max(maxQ, Math.Abs(s.Rates.Y)); maxR = Math.Max(maxR, Math.Abs(s.Rates.Z));
                maxB = Math.Max(maxB, Math.Abs(Math.Asin(Math.Clamp(s.Velocity.Y / s.Velocity.Length, -1, 1))));
                maxVs = Math.Max(maxVs, Math.Abs(s.Attitude.Rotate(s.Velocity).Z));
            }
        }
        RigidBodyState e = sim.Aircraft.State;
        double dpsi = Heading(e.Attitude) - psi0; dpsi = Math.Atan2(Math.Sin(dpsi), Math.Cos(dpsi));
        return new Launch(e, maxP * D, maxQ * D, maxR * D, maxB * D, -e.Position.Z - alt0, maxVs, dpsi * D);
    }

    private static double Heading(Quat q) { Vec3 x = q.Rotate(new Vec3(1, 0, 0)); return Math.Atan2(x.Y, x.X); }

    [Theory]
    [MemberData(nameof(Fleet))]
    public void ConsecutiveAirStartsAreTheSameLaunchAndFlyHandsOff(string id, bool stickFree)
    {
        using var still = StillAir();
        AircraftConfig c = Load(id);
        RigTable.Row row = RigTable.Get(id);
        var launches = new List<Launch>();
        for (int k = 0; k < Launches; k++)
            launches.Add(Fly(c, stickFree, k == 0 ? null : 1000 * k + id.Length));   // launch 1 touch, 2–5 a gamepad at rest

        Launch a = launches[0];
        double dPos = 0, dAtt = 0;
        foreach (Launch b in launches.Skip(1))
        {
            dPos = Math.Max(dPos, (b.End.Position - a.End.Position).Length);
            Quat qa = a.End.Attitude, qb = b.End.Attitude;
            // Component by component (q and −q are one attitude): for a unit quaternion a component difference ε is an angle
            // of order 2ε. (Not 2·acos(q_a·q_b): two IDENTICAL quaternions whose norm is 1 − 1e-16 read 3e-8 rad that way.)
            double sgn = qa.W * qb.W + qa.X * qb.X + qa.Y * qb.Y + qa.Z * qb.Z < 0 ? -1 : 1;
            double dq = Math.Max(Math.Max(Math.Abs(qa.W - sgn * qb.W), Math.Abs(qa.X - sgn * qb.X)), Math.Max(Math.Abs(qa.Y - sgn * qb.Y), Math.Abs(qa.Z - sgn * qb.Z)));
            dAtt = Math.Max(dAtt, 2 * dq);
        }
        _out.WriteLine($"{id} stick-free {(stickFree ? "ON " : "OFF")}: {Launches} launches, spread {dPos:E1} m / {dAtt:E1} rad; "
            + $"p {a.MaxP:F3} q {a.MaxQ:F3} r {a.MaxR:F3} °/s, β {a.MaxBeta:F3}°, Δh {a.AltChange:F1} m, |vs| {a.MaxVs:F2} m/s, Δψ {a.HeadingChange:F2}° (sim clock {Atmosphere.SimTimeSec:F0} s)");
        Assert.True(dPos < 1e-6 && dAtt < 1e-9, $"{id}: launches differ by {dPos:E2} m / {dAtt:E2} rad");

        Assert.True(a.MaxP < 0.25 && a.MaxQ < 0.25 && a.MaxR < 0.25, $"{id}: rates p {a.MaxP:F3} q {a.MaxQ:F3} r {a.MaxR:F3} °/s");
        Assert.True(a.MaxBeta < 0.2, $"{id}: sideslip {a.MaxBeta:F3}°");
        Assert.True(Math.Abs(a.HeadingChange) < 2.0, $"{id}: heading changed {a.HeadingChange:F2}° in {Window} s");
        if (row.Powered) Assert.True(Math.Abs(a.AltChange) < 15 && a.MaxVs < 1.5, $"{id}: altitude change {a.AltChange:F1} m, vertical speed {a.MaxVs:F2} m/s");
    }

    /// <summary>The composition the game publishes: a pad (or a finger) at rest lets go; any deflection holds; a finger on
    /// the pad holds even centred; the trims ride under the stick and clamp at the stops.</summary>
    [Fact]
    public void PilotControlsLetGoOnlyWithNoHandOnThem()
    {
        ControlInputs rest = PilotControls.Compose(0, 0, 0, 0.1, -0.2, 0.05, -0.5, false, false);
        Assert.True(rest.AileronFree && rest.ElevatorFree && rest.RudderFree);
        Assert.Equal(0.1, rest.Aileron, 12); Assert.Equal(-0.2, rest.Elevator, 12); Assert.Equal(0.05, rest.Rudder, 12); Assert.Equal(-0.5, rest.ThrottleLever, 12);
        ControlInputs held = PilotControls.Compose(0, 0, 0, 0, 0, 0, 0, true, true);
        Assert.False(held.AileronFree || held.ElevatorFree || held.RudderFree);
        // A pad connected: released controls go free only with the stick-free option on (default off).
        ControlInputs padDefault = PilotControls.Compose(0, 0, 0, 0, 0, 0, 0, false, false, gamepadConnected: true);
        Assert.False(padDefault.AileronFree || padDefault.ElevatorFree || padDefault.RudderFree);
        ControlInputs padFree = PilotControls.Compose(0, 0, 0, 0, 0, 0, 0, false, false, gamepadConnected: true, gamepadStickFree: true);
        Assert.True(padFree.AileronFree && padFree.ElevatorFree && padFree.RudderFree);
        ControlInputs moved = PilotControls.Compose(0.01, 0, 0, 0, 0, 0, 0, false, false);
        Assert.False(moved.AileronFree || moved.ElevatorFree);
        Assert.True(moved.RudderFree);
        Assert.Equal(1.0, PilotControls.Compose(0.9, 0, 0, 0.3, 0, 0, 0, false, false).Aileron, 12);
        // A pad at rest inside its dead zone hands over exactly zero, so it lets go like a lifted finger.
        var (x, y) = GamepadShaping.Stick(0.012, -0.012);   // |(0.012, −0.012)| = 0.017, inside the 0.02 zone
        Assert.Equal(0.0, x); Assert.Equal(0.0, y);
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
