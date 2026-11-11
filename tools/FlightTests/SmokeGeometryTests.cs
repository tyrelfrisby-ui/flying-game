using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Aero;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

public class SmokeGeometryTests
{
    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(
        Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("great-lakes-like")]
    [InlineData("c172-like")]
    [InlineData("pa18-cub-like")]
    [InlineData("pa18-bush-like")]
    [InlineData("pa18-floats-like")]
    [InlineData("j3-cub-like")]
    [InlineData("j3-cub-floats-like")]
    public void MeasuredFleetOutletsStartClearOfTheFlowBodyWithASensibleJet(string id)
    {
        var cfg = Load(id);
        Assert.NotNull(cfg.Smoke);
        Assert.True(cfg.Smoke.TryOutlet(out var outlet));
        var crossflow = cfg.Fuselage.Crossflow!;
        var flow = new AirframeFlow(cfg, .4 * crossflow.LengthM, crossflow.BodyRadiusM);
        Assert.False(flow.Inside(outlet, .005), $"{id}: outlet inside the smoke flow body's collision envelope");
        var sm = cfg.Smoke;
        var pr = cfg.Propulsion!;
        double exit = ExhaustJet.ExitVelocity(sm.DisplacementCuIn * 16.387e-6, pr.MaxRpm, 1, 1.225,
            sm.PipeDiameterIn * .0254, sm.ExhaustTempK);
        Assert.InRange(exit, 15, 100);
        double idle = ExhaustJet.ExitVelocity(sm.DisplacementCuIn * 16.387e-6, pr.IdleRpm, 0, 1.225,
            sm.PipeDiameterIn * .0254, sm.ExhaustTempK);
        Assert.InRange(idle, .01, exit / 5);
        Assert.InRange(sm.DownDeg, 0, 90);
    }

    [Fact]
    public void SharedCubFuselagesKeepIdenticalSmokeAuthoring()
    {
        foreach (var family in new[] {
            new[] { "pa18-cub-like", "pa18-bush-like", "pa18-floats-like" },
            new[] { "j3-cub-like", "j3-cub-floats-like" } })
        {
            var first = Load(family[0]).Smoke!;
            foreach (string id in family.Skip(1))
            {
                var other = Load(id).Smoke!;
                // The float J-3 visual is placed 10 cm lower; its mouth is the same point on the fuselage.
                Assert.Equal(first.OutletBodyM![0], other.OutletBodyM![0]);
                Assert.Equal(first.OutletBodyM[1], other.OutletBodyM[1]);
                if (id != "j3-cub-floats-like") Assert.Equal(first.OutletBodyM[2], other.OutletBodyM[2]);
                Assert.Equal(first.AftOfNoseM, other.AftOfNoseM);
                Assert.Equal(first.DownDeg, other.DownDeg);
                Assert.Equal(first.DisplacementCuIn, other.DisplacementCuIn);
                Assert.Equal(first.PipeDiameterIn, other.PipeDiameterIn);
                Assert.Equal(first.ExhaustTempK, other.ExhaustTempK);
            }
        }
    }

    [Fact]
    public void OldSmokeBlocksKeepTheHubEstimate()
    {
        var old = new SmokeConfig { AftOfNoseM = 1.22, DownDeg = 30, DisplacementCuIn = 540 };
        Assert.False(old.TryOutlet(out _));
    }

    [Fact]
    public void AMeasuredOutletPreservesAllThreeSignedAxesAndCanBeAtTheOrigin()
    {
        foreach (var p in new[] { new[] { -1.2, 0.23, 0.64 }, new double[3] })
        {
            var smoke = new SmokeConfig { OutletBodyM = p };
            Assert.True(smoke.TryOutlet(out Vec3 v));
            Assert.Equal(p[0], v.X); Assert.Equal(p[1], v.Y); Assert.Equal(p[2], v.Z);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    public void MalformedOutletFallsBackToTheHub(int length)
        => Assert.False(new SmokeConfig { OutletBodyM = new double[length] }.TryOutlet(out _));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ANonFiniteOutletCannotPoisonAPuff(double bad)
    {
        for (int axis = 0; axis < 3; axis++)
        {
            double[] p = { 1, 2, 3 }; p[axis] = bad;
            Assert.False(new SmokeConfig { OutletBodyM = p }.TryOutlet(out _));
        }
    }

    // A GLIDER'S SMOKE COMES FROM THE ACTUAL WINGTIP (Ty 2026-11-10: "Make the 2-33 smoke come from the trailing
    // edge of the actual wingtip"). SmokeTrail.WingtipsFor lives in the Unity bridge and cannot be called from here,
    // so this pins the CONFIG DATA that its derivation stands on: a strip's span is its own Area/Chord, and the
    // outermost strip's OUTER EDGE -- centre plus half a strip -- is the real wingtip. If that stops landing on the
    // published semi-span, the canister starts hanging in clear air again and nothing else would catch it.
    [Theory]
    [InlineData("glider-2-33-like", 7.770)]          // Schweizer SGS 2-33, 51 ft span
    [InlineData("glider-grob103-acro-like", 9.000)]  // Grob 103, 18.0 m
    [InlineData("glider-swift-s1-like", 6.330)]      // Swift S-1, 12.66 m
    [InlineData("glider-eb29r-like", 14.650)]        // Schempp-Hirth EB29R, 29.3 m
    public void AGlidersOutermostStripEndsAtThePublishedWingtip(string id, double semiSpanM)
    {
        AircraftConfig cfg = Load(id);
        StripConfig tip = null;
        foreach (SurfaceConfig sf in cfg.Surfaces)
            foreach (StripConfig st in sf.Strips)
                if (Math.Abs(st.Pos[2]) < 1.0 && (tip == null || Math.Abs(st.Pos[1]) > Math.Abs(tip.Pos[1]))) tip = st;

        Assert.NotNull(tip);
        Assert.True(tip.Chord > 0.0, id + ": the outermost strip has no chord, so its span cannot be derived");
        double outerEdge = Math.Abs(tip.Pos[1]) + 0.5 * tip.Area / tip.Chord;
        Assert.True(Math.Abs(outerEdge - semiSpanM) < 0.01,
            $"{id}: outermost strip's outer edge {outerEdge:F3} m is not the published semi-span {semiSpanM:F3} m " +
            $"(strip centre {Math.Abs(tip.Pos[1]):F3}, chord {tip.Chord:F3}, area {tip.Area:F4})");
    }
}
