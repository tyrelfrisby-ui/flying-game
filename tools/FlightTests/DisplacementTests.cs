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
// Mutates the global FloatHydro.FlatWaterOverride, so it must not run in parallel with another class that
// does: one class's finally clears the water out from under another class's seaplane mid-flight.
[Collection("WorldTerrainActive")]
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
    // A J-3 on Edo 60-1320s (FAA Aircraft Specification A-691 Rev 34, Item 206: "Edo 60-1320 floats with water
    // rudders (171 lb.)", maximum weight 1,300 lb). Edo names a float for the pounds it displaces, so the pair is
    // 2,640 lb on a 1,300 lb aeroplane. This row exists because its absence is how the J-3 came to be flying on the
    // SUPER CUB's EDO 2000s -- the float block was a byte-for-byte copy, 1,972 lb a side and 2.92 W of reserve.
    [InlineData("j3-cub-floats-like", 1.8, 2.3)]
    [InlineData("pa18-floats-like", 1.8, 2.6)]   // EDO 2000 pair ≈ 4,000 lb on a ~1,800-2,000 lb Cub ≈ 2.0-2.2 W
    [InlineData("dhc2-beaver-floats-like", 1.8, 2.3)]   // EDO 4930 pair ≈ 9,860 lb on a 4,850-5,090 lb Beaver ≈ 1.9-2.0 W
    [InlineData("hughes-h4-like", 1.8, 25.0)]    // hull: 25 x 30 x 218 ft — enormous reserve; its rest DRAFT is the real check (FlyingBoatTests)
    public void ReserveBuoyancyMeetsTheRule(string id, double minRatio, double maxRatio)
    {
        AircraftConfig c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
        // 23.751 SAYS MAXIMUM WEIGHT, and this test used to read today's loading. The rule is "80 percent in excess
        // of the buoyancy required by that float to support its MAXIMUM WEIGHT in fresh water" — a float is approved
        // once, against the placard, exactly as 23.527's water load is (Aircraft.InitFloatWaterLoads). Harmless while
        // every config authored mass.massKg AS its gross; on 2026-11-11 the J-3 on floats went 589.67 → 451.142 kg,
        // the aeroplane as flown, and its reserve read 2.67 W against a window of 1.8-2.3 that the row's own comment
        // derives from the certificate: "Edo 60-1320 floats ... maximum weight 1,300 lb ... 2,640 lb on a 1,300 lb
        // aeroplane". At the 1,300 lb the comment names it is 2.04. The float did not grow; the divisor shrank.
        // Every other row here is authored at its own gross, so its placard is its mass and nothing moves.
        double placardKg = new AircraftLoad(c).PlacardGrossKg;
        double grossKg = placardKg > 0 ? placardKg : c.Mass.MassKg;
        double w = grossKg * Atmosphere.GravityMs2;
        (double main, double tip) = FullyImmersed(c);
        _out.WriteLine($"{id}: maximum weight {grossKg:F0} kg{(placardKg > 0 ? " (placard)" : " (authored, no placard)")}, as flown {c.Mass.MassKg:F0} kg; "
                       + $"main floats/hull fully immersed displace {main / 9.81:F0} kg = {main / w:F2} x weight ({main / 9.81 * 2.2046 / (c.Floats!.Count == 1 ? 1 : 2):F0} lb per float){(tip > 0 ? $"; one tip float {tip / 9.81:F0} kg = {tip / w * 100:F1} % of weight" : "")}");
        Assert.InRange(main / w, minRatio, maxRatio);
    }
}
