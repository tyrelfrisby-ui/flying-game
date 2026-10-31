using System.Threading;
using FlyingGame.Core;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The flat-water test hook must not leak between threads, because that is exactly how it broke: seven test classes
/// shared one static, xUnit ran them in parallel, and one class's cleanup took the water away from another class's
/// seaplane in mid-flight. Deliberately NOT in the WorldTerrainActive collection — this test is about the hook being
/// thread-local, so serialising it would hide the very thing it checks.
/// </summary>
public sealed class FlatWaterHookTests
{
    [Fact]
    public void OneThreadsFlatWaterIsInvisibleToAnother()
    {
        FloatHydro.FlatWaterOverride = 12.5;
        try
        {
            double? seenByOther = 999.0;
            bool otherSawWater = true;
            var t = new Thread(() =>
            {
                seenByOther = FloatHydro.FlatWaterOverride;
                otherSawWater = FloatHydro.WaterSurfaceAt(0, 0).HasValue && WorldTerrain.Active == null;
            });
            t.Start();
            Assert.True(t.Join(5000), "the other thread never finished");

            Assert.Null(seenByOther);                                  // it cannot see this thread's override
            Assert.False(otherSawWater);                               // so it gets the world's answer, not ours
            Assert.Equal(12.5, FloatHydro.FlatWaterOverride);          // and ours is untouched by it
            Assert.Equal(12.5, FloatHydro.WaterSurfaceAt(0, 0));
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    /// <summary>And the other direction: a thread that sets it cannot leave it set for anyone else — which is the
    /// case that was corrupting the seaplanes.</summary>
    [Fact]
    public void AThreadThatSetsTheHookCannotLeaveItSetForAnyoneElse()
    {
        Assert.Null(FloatHydro.FlatWaterOverride);
        var t = new Thread(() => FloatHydro.FlatWaterOverride = 3.0);   // set, and deliberately never cleared
        t.Start();
        Assert.True(t.Join(5000), "the other thread never finished");
        Assert.Null(FloatHydro.FlatWaterOverride);
    }
}
