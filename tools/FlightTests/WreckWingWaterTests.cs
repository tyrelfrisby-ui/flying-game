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
/// Ty 2026-11-04 (Quest, Great Lakes into water): "detached wings hover above the water." A broken-off left wing, dropped
/// still 1 m above flat water (FlatWreckWorld, 20 m deep), must float IN the water — some of it wet at a waterline, never
/// riding wholly clear of it — and, for the fabric-covered biplanes, go on sinking. Measured 2026-11-04 (WreckSim, the
/// same code on iOS and Quest): a rigid two-wing cell capsizes onto its edge and floats like a two-hulled raft with about a
/// third of its chord wet, then floods and sinks (Great Lakes on the bottom by 900 s); the 172's sealed metal wing floats
/// flat. Ranges are literals around those measurements.
/// </summary>
[Collection("WorldTerrainActive")]
public class WreckWingWaterTests
{
    private readonly ITestOutputHelper _o;
    public WreckWingWaterTests(ITestOutputHelper o) { _o = o; WorldTerrain.Active = null; }

    [Theory]
    [InlineData("great-lakes-like", true)]
    [InlineData("pitts-s2b-like", true)]
    [InlineData("c172-like", false)]
    public void ADetachedWingFloatsInTheWaterAndAFabricOneSinks(string id, bool fabricSinks)
    {
        AircraftConfig c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -3), Quat.Identity, Vec3.Zero, Vec3.Zero), ControlDeflections.Neutral);
        List<HydroElement> els = ac.Hydro.Detach(AirframeComponent.WingLeft);
        Assert.NotEmpty(els);
        var w = new WreckSim(new FlatWreckWorld(0.0, -20.0));
        WreckPiece p = w.Add("wing", AirframeComponent.WingLeft, els, c.Mass.CgVec(), new RigidBodyState(new Vec3(0, 0, -1.0), Quat.Identity, Vec3.Zero, Vec3.Zero));
        double t = 0, depth60 = 0;
        foreach (double until in new[] { 10.0, 30.0, 60.0, 900.0 })
        {
            while (t < until) { w.StepOnce(); t += w.Dt; }
            double wettest = 0;
            foreach (HydroElement e in els)
            {
                Vec3 r = p.State.Attitude.Rotate(e.Centre - p.CgBody);
                double d = p.State.Position.Z + r.Z;   // + water up 0
                wettest = Math.Max(wettest, HydroElement.SubmergedFraction(d, e.VerticalHalf(p.State.Attitude)));
            }
            _o.WriteLine($"{id} t {t:F0} s: {p.Phase}, CG depth {p.DepthM:F2} m, wettest element {wettest:F2} submerged");
            if (until <= 60.0)
            {
                Assert.True(wettest >= 0.10, $"{id} at {t:F0} s: no element is in the water (hovering)");
                Assert.InRange(p.DepthM, -0.6, 1.6);   // at the waterline, not riding above it nor gone
            }
            if (until == 60.0) depth60 = p.DepthM;
        }
        if (fabricSinks) Assert.True(p.DepthM > depth60 + 0.5, $"{id}: the fabric wing did not go on sinking ({depth60:F2} -> {p.DepthM:F2} m)");
    }
}
