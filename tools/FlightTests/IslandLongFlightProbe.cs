using System.Diagnostics;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Ty: "the pitts flys for about 30 seconds then freezes." If that freeze is in the simulation it will
/// show up here — an exception, a NaN, or a second of sim that suddenly costs far more than the ones before it.
/// If sixty seconds run clean and flat, the freeze is above the sim, in the Unity layer.</summary>
public sealed class IslandLongFlightProbe
{
    private readonly ITestOutputHelper _o;
    public IslandLongFlightProbe(ITestOutputHelper o) { _o = o; }

    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("c172-like")]
    public void SixtySecondsOfIslandFlight(string id)
    {
        WorldTerrain.Active = new WorldTerrain();
        try
        {
            var config = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
            WorldTerrain.Airport ap = WorldTerrain.Airports[WorldTerrain.DefaultAirportIndex];
            IslandSites.Start? s = IslandRunways.Authored(ap.Name, "InTheAir");
            Assert.True(s.HasValue);
            double hdg = s.Value.HeadingTrueDeg * Math.PI / 180;
            var att = new Quat(0, 0, Math.Sin(hdg / 2), Math.Cos(hdg / 2));
            var state = new RigidBodyState(new Vec3(s.Value.X, s.Value.Y, -s.Value.AltitudeM), att,
                                           new Vec3(config.SpawnIasMs > 1 ? config.SpawnIasMs : 40, 0, 0), Vec3.Zero);
            var loop = new SimLoop(new Aircraft(config, state, ControlDeflections.Neutral));
            var cruise = new ControlInputs(0, -0.05, 0, -0.6);
            double dt = SimLoop.DefaultFixedDtSec;
            var sw = new Stopwatch();
            double worst = 0; int worstSec = -1;
            for (int sec = 0; sec < 60; sec++)
            {
                sw.Restart();
                for (int i = 0; i < (int)(1 / dt); i++) loop.RunFor(dt, cruise);
                sw.Stop();
                double ms = sw.Elapsed.TotalMilliseconds;
                if (ms > worst) { worst = ms; worstSec = sec; }
                RigidBodyState st = loop.Aircraft.State;
                Assert.False(double.IsNaN(st.Position.Z), $"{id}: NaN at t={sec}s");
                if (sec % 10 == 0 || ms > 400)
                    _o.WriteLine($"{id,-16} t={sec,2}s  {ms,7:F1} ms of CPU  alt {-st.Position.Z,8:F0} m  agl {-st.Position.Z - WorldTerrain.GroundHeightAt(st.Position.X, st.Position.Y),8:F0}  speed {st.Velocity.Length,6:F1} m/s");
            }
            _o.WriteLine($"{id,-16} worst second: {worst:F1} ms at t={worstSec}s");
        }
        finally { WorldTerrain.Active = null; }
    }
}
