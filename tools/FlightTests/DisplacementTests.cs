using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Owner 2026-10-02: floatplane / flying-boat DISPLACEMENT must be right. FAR 23.751: each main float carries 80 %
/// more buoyancy than its share of the max weight (pair >= 1.8 W); EDO floats are named for their displacement per
/// float (EDO 2000 = 2,000 lb each on a Super Cub). Measured from the hydrostatics the sim actually uses: the floats
/// pushed fully under, at rest.</summary>
public class DisplacementTests
{
    private readonly ITestOutputHelper _out;
    public DisplacementTests(ITestOutputHelper o) { _out = o; }

    /// <summary>Buoyancy (N) of the main floats/hull fully submerged (and of the tip floats separately).</summary>
    public static (double main, double tips) FullyImmersed(AircraftConfig c)
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            FloatsConfig f = c.Floats!;
            // CG pushed down so the keel is DepthM + 2 m under (decks awash), level, at rest.
            double z = f.KeelZ - (f.DepthM + 2.0);
            FloatHydro.Compute(c, new RigidBodyState(new Vec3(0, 0, -z), new Quat(0, 0, 0, 1), Vec3.Zero, Vec3.Zero), 0);
            double main = FloatHydro.Floats[0].BuoyancyN + (f.Count == 1 ? 0 : FloatHydro.Floats[1].BuoyancyN);
            double tips = 0;
            if (f.TipFloats is { } t)
            {
                double zt = t.KeelZ - (t.DepthM + 0.5);
                FloatHydro.Compute(c, new RigidBodyState(new Vec3(0, 0, -zt), new Quat(0, 0, 0, 1), Vec3.Zero, Vec3.Zero), 0);
                tips = FloatHydro.Floats[2].BuoyancyN;   // one tip float
            }
            return (main, tips);
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    [Theory]
    [InlineData("pa18-floats-like", 1.8, 2.6)]   // EDO 2000 pair ≈ 4,000 lb on a ~1,800-2,000 lb Cub ≈ 2.0-2.2 W
    [InlineData("dhc2-beaver-floats-like", 1.8, 2.3)]   // EDO 4930 pair ≈ 9,860 lb on a 4,850-5,090 lb Beaver ≈ 1.9-2.0 W
    [InlineData("hughes-h4-like", 1.8, 25.0)]    // hull: 25 x 30 x 218 ft — enormous reserve; its rest DRAFT is the real check (FlyingBoatTests)
    public void ReserveBuoyancyMeetsTheRule(string id, double minRatio, double maxRatio)
    {
        AircraftConfig c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
        double w = c.Mass.MassKg * Atmosphere.GravityMs2;
        (double main, double tip) = FullyImmersed(c);
        _out.WriteLine($"{id}: weight {c.Mass.MassKg:F0} kg; main floats/hull fully immersed displace {main / 9.81:F0} kg = {main / w:F2} x weight ({main / 9.81 * 2.2046 / (c.Floats!.Count == 1 ? 1 : 2):F0} lb per float){(tip > 0 ? $"; one tip float {tip / 9.81:F0} kg = {tip / w * 100:F1} % of weight" : "")}");
        Assert.InRange(main / w, minRatio, maxRatio);
    }
}
