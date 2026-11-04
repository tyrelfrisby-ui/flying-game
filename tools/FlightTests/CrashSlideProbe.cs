using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Wreck;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Measurement probe (not an assertion): how far a wreck slides, on flat test ground and on the island.</summary>
[Collection("WorldTerrainActive")]
public class CrashSlideProbe
{
    private readonly ITestOutputHelper _o;
    public CrashSlideProbe(ITestOutputHelper o) { _o = o; }

    private static AircraftConfig Load(string f) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", f));

    [Theory]
    [InlineData("flat-grass")]
    [InlineData("island-rough")]
    [InlineData("island-paved")]
    public void Slide(string where)
    {
        try
        {
            double x = 0, y = 0;
            if (where.StartsWith("island"))
            {
                WorldTerrain.Active = new WorldTerrain();
                WorldTerrain.Airport a = WorldTerrain.Airports.First(ap => IslandRunways.Ends(ap).Count > 0);
                WorldTerrain.RunwayEnd rw = IslandRunways.Ends(a)[0];
                x = rw.CentreX; y = rw.CentreY;
                if (where.EndsWith("rough")) { x += -rw.AlongY * 40.0; y += rw.AlongX * 40.0; }
            }
            else WorldTerrain.SurfaceOverride = WorldTerrain.Surface.Grass;
            AircraftConfig c = Load("pitts-s2b-like.json");
            var ac = new Aircraft(c, new RigidBodyState(new Vec3(x, y, -50), Quat.Identity, new Vec3(30.87, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
            foreach (var comp in new[] { AirframeComponent.WingLeft, AirframeComponent.WingRight, AirframeComponent.GearLeft, AirframeComponent.GearRight, AirframeComponent.GearTail, AirframeComponent.Propeller })
                ac.LoseComponent(comp);
            // Lowest hard point 2 cm above the ground, level, 60 kt along x.
            Vec3 cg = c.Mass.CgVec();
            double low = double.NegativeInfinity;
            foreach (ContactPoint p in ac.ContactPoints) if (!p.PropDisc) low = Math.Max(low, p.Body.Z - cg.Z);
            double g0 = WorldTerrain.HardPointGroundAt(x, y, WorldTerrain.GroundHeightAt(x, y));
            ac.State = new RigidBodyState(new Vec3(x, y, -(g0 + low + 0.02)), Quat.Identity, new Vec3(30.87, 0, 0), Vec3.Zero);
            var surf = WorldTerrain.SurfaceAt(x, y, g0);
            double mu = AirframeContact.SlidingMu(surf);
            Vec3 p0 = ac.State.Position; double t = 0;
            while (t < 30)
            {
                ac.Step(new ControlInputs(0, 0, 0, 0), 0.005); t += 0.005;
                Vec3 v = ac.State.Attitude.Rotate(ac.State.Velocity);
                if (Math.Sqrt(v.X * v.X + v.Y * v.Y) < 0.3) break;
            }
            double dist = Math.Sqrt(Math.Pow(ac.State.Position.X - p0.X, 2) + Math.Pow(ac.State.Position.Y - p0.Y, 2));
            _o.WriteLine($"{where}: surface {surf} mu {mu}; stopped after {t:F2} s, {dist:F1} m; v^2/(2 mu g) = {30.87 * 30.87 / (2 * mu * 9.81):F1} m; mass {ac.MassProperties.MassKg:F0} kg");
        }
        finally { WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cartwheel(bool grass)
    {
        try
        {
            WorldTerrain.Active = new WorldTerrain();
            AircraftConfig c = Load("pitts-s2b-like.json");
            RigidBodyState s0 = CrashScenario.PittsStart(c, grass);
            var ac = new Aircraft(c, s0, ControlDeflections.Neutral);
            var relay = new CrashRelay(ac);
            var start = new Dictionary<WreckPiece, (Vec3 p, double v, double t)>();
            double t = 0; const double dt = 0.005;
            Vec3 p0 = s0.Position;
            for (int i = 0; i < 2400; i++)
            {
                int born = relay.Wreck.Pieces.Count;
                ac.Step(new ControlInputs(0, 0, 0, 1), dt); relay.Step(dt); t += dt;
                for (int k = born; k < relay.Wreck.Pieces.Count; k++) { var wp = relay.Wreck.Pieces[k]; start[wp] = (wp.State.Position, wp.WorldVelocity.Length, t); _o.WriteLine($"t {t:F2} {wp.Name} off {wp.MassKg:F0} kg at {wp.WorldVelocity.Length:F1} m/s"); }
                if (i % 50 == 49)
                {
                    Vec3 v = ac.State.Attitude.Rotate(ac.State.Velocity);
                    _o.WriteLine($"t {t:F2} airframe {v.Length:F1} m/s  w {ac.State.Rates.Length:F2}  travelled {Math.Sqrt(Math.Pow(ac.State.Position.X - p0.X, 2) + Math.Pow(ac.State.Position.Y - p0.Y, 2)):F1} m  lost [{string.Join(",", ac.LostComponents)}]");
                }
            }
            foreach (var wp in relay.Wreck.Pieces)
            {
                var st = start[wp]; var q = wp.State.Position;
                _o.WriteLine($"  {wp.Name,-18} {wp.MassKg,6:F1} kg born {st.t:F2}s at {st.v:F1} m/s  slid {Math.Sqrt(Math.Pow(q.X - st.p.X, 2) + Math.Pow(q.Y - st.p.Y, 2)):F1} m  now {wp.WorldVelocity.Length:F2} m/s {wp.Phase} asleep {wp.Asleep}");
            }
        }
        finally { WorldTerrain.Active = null; CrashEvents.ClearForTests(); }
    }

    [Theory]
    [InlineData(60.0, 0.9)]
    [InlineData(60.0, 0.6)]
    [InlineData(60.0, 0.35)]
    [InlineData(60.0, 0.12)]
    [InlineData(70.0, 0.5)]
    [InlineData(20.0, 0.85, "j3-cub-like.json")]
    [InlineData(20.0, 0.5, "j3-cub-like.json")]
    [InlineData(45.0, 0.5, "j3-cub-like.json")]
    public void TreePass(double v, double rhoFrac, string file = "pitts-s2b-like.json")
    {
        WorldTerrain.Active = null;
        try
        {
            AircraftConfig c = Load(file);
            var tree = new Forest.Tree(40, 0, 0.0, 30, 0.22 + 30 / 24.0, 0, 0);
            double up = 15.0;
            double rc = TreeStructure.CrownRadiusAt(tree, up);
            // Fly north past the stem, which stands beyond the left wingtip: the tip passes at rhoFrac of the crown radius.
            double semi = WingPanels.Semispan(c) + 0.3;
            tree = new Forest.Tree(40, -(semi + rhoFrac * rc), 0.0, 30, 0.22 + 30 / 24.0, 0, 0);
            var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -up), Quat.Identity, new Vec3(v, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
            ac.Trees = new FixedTrees(tree);
            var relay = new CrashRelay(ac, new FlatWreckWorld(null, 0.0));
            var twin = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -up), Quat.Identity, new Vec3(v, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
            int dmgEvents = 0; ac.Contact += e => { if (e.Kind == ContactEventKind.BranchDamage) dmgEvents++; };
            var sim = new SimLoop(ac); var sim2 = new SimLoop(twin);
            sim.RunFor(Math.Min(2.0, 80.0 / v), new ControlInputs(0, 0, 0, -1)); sim2.RunFor(Math.Min(2.0, 80.0 / v), new ControlInputs(0, 0, 0, -1));
            double dv = twin.State.Velocity.Length - ac.State.Velocity.Length;
            _o.WriteLine($"{file} v {v} rho {rhoFrac:F2} rc {rc:F2}: dv {dv:F2} m/s, max strip damage {ac.MaxBranchDamage:F3}, events {dmgEvents}, prop R {ac.PropRadiusM():F3}, lost [{string.Join(",", ac.LostComponents)}]");
            foreach (var r in ac.TreeStrikeLog) _o.WriteLine("   " + r);
        }
        finally { WorldTerrain.Active = null; CrashEvents.ClearForTests(); }
    }
}
