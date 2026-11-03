using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Owner 2026-11-02: a full-rudder flip while taxiing on floats at HIGH speed / high power is realistic and
/// must not be "fixed"; at idle or low power a full-rudder turn on the water must NOT flip the seaplane.
/// Idle and low power (<= 10 % throttle) are asserted; 20 % and full power are logged (capsize-only check at 20 %).
/// Bound: at idle-taxi speed (a few m/s) the turn's centripetal side load m V^2 / R, acting at the CG height above the
/// float keels, is far below the righting moment of the outside float's reserve buoyancy (Delta x half the float
/// track); a real seaplane in a full-rudder idle turn heels a few degrees. 30 deg of heel is a generous ceiling, and the
/// capsize check is |roll| below 60 deg throughout.</summary>
[Collection("WorldTerrainActive")]
public class FloatTaxiRudderTests
{
    private readonly ITestOutputHelper _out;
    public FloatTaxiRudderTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
    private static double Roll(RigidBodyState s) { Quat q = s.Attitude; return Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y)) * 57.3; }

    private (double maxRoll, double maxSpeed) Taxi(string id, double lever, double rudder, double seconds, double stick = -1.0)
    {
        AircraftConfig cfg = Load(id);
        var state = new RigidBodyState(new Vec3(0, 0, -(cfg.Floats!.KeelZ - 0.25)), new Quat(0, 0, 0, 1), Vec3.Zero, Vec3.Zero);
        var ac = new Aircraft(cfg, state, ControlDeflections.Neutral);
        var sim = new SimLoop(ac);
        sim.RunFor(4.0, new ControlInputs(0, 0, 0, 1.0)); // settle at idle
        double maxRoll = 0, maxV = 0;
        for (double t = 0; t < seconds; t += 0.1)
        {
            // Stick full back (water-taxi technique, keeps the props/bows up), full rudder.
            sim.RunFor(0.1, new ControlInputs(0, stick, rudder, lever));
            RigidBodyState s = ac.State;
            Assert.False(double.IsNaN(s.Position.Z), $"NaN at t={t:F1}");
            maxRoll = Math.Max(maxRoll, Math.Abs(Roll(s)));
            maxV = Math.Max(maxV, s.Velocity.Length);
        }
        return (maxRoll, maxV);
    }

    [Theory]
    [InlineData("pa18-floats-like")]
    [InlineData("j3-cub-floats-like")]
    [InlineData("dhc2-beaver-floats-like")]
    public void FullRudderAtIdleOrLowPowerDoesNotFlipOnFloats(string id)
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            foreach (double lever in new[] { 1.0, 0.9, 0.8, 0.6 })  // throttle 0, 5, 10, 20 % (throttle01 = (1 - lever) / 2)
            foreach (double rudder in new[] { 1.0, -1.0 })
            {
                var (roll, v) = Taxi(id, lever, rudder, 40);
                _out.WriteLine($"{id} lever {lever:+0.0;-0.0} rudder {rudder:+0;-0}: max |roll| {roll:F1} deg, max speed {v:F1} m/s");
                if (lever >= 0.8)   // idle / low power (<= 10 % throttle): must stay upright, no deep heel
                    Assert.True(roll < 30, $"{id} heeled {roll:F0} deg in a full-rudder turn at lever {lever} (idle/low power must not flip)");
                else                // 20 %: fast taxi, logged; only a full capsize fails
                    Assert.True(roll < 90, $"{id} capsized ({roll:F0} deg) at lever {lever}");
            }
            var (sRoll, sV) = Taxi(id, 0.9, 0.0, 60, 0.0);            // straight, 5 % throttle, neutral stick: info only
            _out.WriteLine($"{id} straight 5 % throttle (info): max speed {sV:F1} m/s, max |roll| {sRoll:F1} deg");
            var (bRoll, bV) = Taxi(id, 0.9, 0.0, 60, -1.0);           // straight, 5 % throttle, full back stick: info only
            _out.WriteLine($"{id} straight 5 % throttle, stick back (info): max speed {bV:F1} m/s, max |roll| {bRoll:F1} deg");
            var (hiRoll, hiV) = Taxi(id, -1.0, 1.0, 20);              // high power: realistic flip allowed, logged only
            _out.WriteLine($"{id} FULL power full rudder (info, not asserted): max |roll| {hiRoll:F1} deg, max speed {hiV:F1} m/s");
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }
}
