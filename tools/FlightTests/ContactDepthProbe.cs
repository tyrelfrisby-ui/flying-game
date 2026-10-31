using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Not an assertion. The one number that decides how the cliff bug can be fixed: how deep INTO THE TERRAIN
/// an aeroplane's contact points get in each situation. If a tunnel tow and a cliff strike are separable by depth,
/// the contact model can treat them differently; if they are not, they cannot be told apart that way.</summary>
[Collection("WorldTerrainActive")]
public sealed class ContactDepthProbe : IDisposable
{
    /// <summary>xUnit calls this after EVERY test, pass or FAIL. A bare reset at the end of a test method
    /// does not run when the test throws, so a failing test used to leave its world installed in the global
    /// WorldTerrain.Active for the whole rest of the run — which silently changed the ground under every
    /// class that came after it, whether or not it was serialised.</summary>
    public void Dispose() => WorldTerrain.Active = null;

    private readonly ITestOutputHelper _out;
    public ContactDepthProbe(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    /// <summary>Deepest terrain penetration of any contact point, sampled the way AirframeContact samples it.</summary>
    private static double DeepestPenetration(Aircraft ac)
    {
        RigidBodyState s = ac.State;
        double worst = double.MinValue;
        foreach (ContactPoint p in ac.ContactPoints)
        {
            Vec3 rBody = p.Body - ac.Config.Mass.CgVec();
            Vec3 w = s.Position + s.Attitude.Rotate(rBody);
            double groundH = WorldTerrain.WheelGroundHeightAt(w.X, w.Y, -w.Z);
            worst = System.Math.Max(worst, w.Z - (-groundH));
        }
        return worst;
    }

    [Fact]
    public void HowDeepDoesEachSituationGo()
    {
        WorldTerrain.Active = new WorldTerrain();
        try
        {
            AircraftConfig dc3 = Load("dc3-like");
            double x = 3245, y = 877, top = WorldTerrain.GroundHeightAt(x, y);
            var st = new RigidBodyState(new Vec3(x, y, -(top - 20)), new Quat(0, 0, 0, 1), new Vec3(74, 0, 0), Vec3.Zero);
            var ac = new Aircraft(dc3, st, ControlDeflections.Neutral);
            double worst = double.MinValue;
            for (int i = 0; i < 100; i++)
            {
                new SimLoop(ac, 1.0 / 50.0).RunFor(1.0 / 50.0, new ControlInputs(0, 0, 0, 1.0));
                if (!double.IsFinite(ac.State.Position.Z)) { _out.WriteLine($"  cliff: non-finite at step {i}"); break; }
                worst = System.Math.Max(worst, DeepestPenetration(ac));
            }
            _out.WriteLine($"  CLIFF strike, deepest terrain penetration: {worst,10:F2} m");

            // A normal aeroplane at rest on a runway, for the other end of the scale.
            foreach (IslandField f in IslandSites.Fields)
            {
                if (f.Surface == "water" || f.Length < 500) continue;
                var rest = new Aircraft(dc3, LandingGear.RestingState(dc3, f.Centre.X, f.Centre.Y, f.Elevation, 0), ControlDeflections.Neutral);
                for (int i = 0; i < 100; i++) new SimLoop(rest, 1.0 / 50.0).RunFor(1.0 / 50.0, new ControlInputs(0, 0, 0, 1.0));
                _out.WriteLine($"  at rest on {f.Id,-14} deepest penetration: {DeepestPenetration(rest),10:F3} m");
                break;
            }
        }
        finally { WorldTerrain.Active = null; }
    }
}
