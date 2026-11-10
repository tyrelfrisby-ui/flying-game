using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE MECHANISM CHECK BEHIND THE MODEL-IDENTITY DIAGNOSTIC (Ty 2026-11-09).
///
/// He switched from the Cub to the Pitts and the Pitts flew with a Cub's dihedral effect for about a minute: "almost
/// like it was the extra flying but the pitts livery … maybe the flight model was stuck on cub and the pitts livery
/// was present". He asked for a diagnostic rather than a fix: "make the build log or display which config the flight
/// model is actually using, so the next time it happens we know for certain rather than inferring".
///
/// One way that could happen without any id ever being wrong is a REUSED CONFIG OBJECT. Six tables in AeroModel, plus
/// TailWake, PropJet, BiplaneCellule, WeightAndBalance, FloatHull and FloatHydro, cache per-config geometry in a
/// ConditionalWeakTable keyed on the AircraftConfig OBJECT — on the standing assumption that one object is one
/// aeroplane for its whole life. Nothing violates that today (UnityAircraftConfigLoader deserializes a fresh object
/// per load and caches nothing), but if anything ever did, those tables would serve the FIRST aeroplane's geometry
/// under the SECOND aeroplane's name, silently, which is exactly the report.
///
/// So the diagnostic reads the span twice — once through the cache and once from the strips as authored — and says so
/// when they differ. These tests are what make that comparison meaningful: they prove the two really are a cached and
/// an uncached path over one formula, so agreement is evidence and disagreement is not noise.
/// </summary>
public sealed class ModelIdentityGeometryTests
{
    [Fact]
    public void TheCachedAndUncachedSpansAgreeOnAHealthyAeroplane()
    {
        AircraftConfig config = TestAircraftConfig.Load();
        Assert.Equal(AeroModel.WingSpan(config), AeroModel.WingSpanUncached(config), 9);
        // And the 2-33's span really is the authored one, so neither path is quietly returning zero.
        Assert.True(AeroModel.WingSpanUncached(config) > 10.0, "the 2-33 spans about 15 m");
    }

    /// <summary>
    /// The detector detects. Measure once (which fills the per-config table), then change the wing underneath the same
    /// object: the CACHED span does not move, the UNCACHED one does, and that gap is precisely the signal the badge
    /// reports. This is the only place the stale reading can be produced on purpose — in the game the object is never
    /// reused, which is what the diagnostic is there to confirm rather than assume.
    /// </summary>
    [Fact]
    public void AChangedWingUnderACachedConfigShowsUpAsADisagreement()
    {
        AircraftConfig config = TestAircraftConfig.Load();
        double cachedBefore = AeroModel.WingSpan(config);          // fills the table for this object
        Assert.Equal(cachedBefore, AeroModel.WingSpanUncached(config), 9);

        SurfaceConfig wing = config.Surfaces.First(s => s.Id.Contains("wing", StringComparison.OrdinalIgnoreCase));
        StripConfig tip = wing.Strips.OrderByDescending(s => Math.Abs(s.Pos[1])).First();
        double wasY = tip.Pos[1];
        tip.Pos[1] = wasY + (wasY < 0 ? -3.0 : 3.0);               // move the outermost strip 3 m further out

        Assert.Equal(cachedBefore, AeroModel.WingSpan(config), 9);                      // the table is stale, by design
        Assert.True(AeroModel.WingSpanUncached(config) > cachedBefore + 2.5);           // the strips are not
        Assert.True(Math.Abs(AeroModel.WingSpan(config) - AeroModel.WingSpanUncached(config)) > 0.01,
                    "the badge's threshold is 0.01 m, so a real swap is far past it");

        // TestAircraftConfig.Load() deserializes the file afresh every call, so this object is this test's alone and
        // the bent wing cannot leak into another test. Put it back anyway, so a future caller that caches the load
        // does not inherit a 3 m longer glider from here.
        tip.Pos[1] = wasY;
    }
}
