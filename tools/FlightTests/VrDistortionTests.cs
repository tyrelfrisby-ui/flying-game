using FlyingGame.Core.Vr;
using Xunit;

namespace FlyingGame.FlightTests;

public sealed class VrDistortionTests
{
    private static VrLensConfig Linear() => new VrLensConfig
    {
        K1 = 0, K2 = 0, InterLensDistanceM = .064, ScreenToLensDistanceM = .04,
        TrayToLensDistanceM = .035, FovLeftDeg = 85, FovRightDeg = 85,
        FovBottomDeg = 85, FovTopDeg = 85
    };

    [Theory]
    [InlineData(false, 0, 0)]
    [InlineData(false, 1, 1)]
    [InlineData(true, 0, 1)]
    [InlineData(true, .25, .75)]
    public void LinearLensPreservesEyeUv(bool right, double u, double v)
    {
        var uv = VrDistortion.Sample(Linear(), right, .14, .07, u, v);
        Assert.Equal(u, uv.U, 10); Assert.Equal(v, uv.V, 10);
    }

    [Fact]
    public void CardboardSamplesFurtherFromAxisThanLinearLens()
    {
        var lens = Linear();
        lens.TrayToLensDistanceM = .04;
        var linear = VrDistortion.Sample(lens, false, .128, .08, .8125, .5);
        lens.K1 = .33582564; lens.K2 = .55348791;
        var warped = VrDistortion.Sample(lens, false, .128, .08, .8125, .5);
        double edge = VrOptics.EdgeTangent(lens, VrOptics.Edge.Right, .128, .08);
        double eyeTangent = (2 * warped.U - 1) * edge;
        // Screen tangent = (.8125 * .064 - .032) / .04 = .5.
        // .5 + .33582564 * .125 + .55348791 * .03125
        // = .5 + .041978205 + .0172964971875 (hand literals).
        Assert.Equal(.5592747021875, eyeTangent, 12);
        Assert.Equal(.5, (2 * linear.U - 1) * .8, 12);
        Assert.True(eyeTangent > .5);
        // Out-of-frustum coordinates stay out of range for the shader's black mask.
        var outside = VrDistortion.Sample(lens, false, .128, .08, 1, .5);
        Assert.True(outside.U > 1);
    }

    [Fact]
    public void ColourScaleIsAboutLensAxisRatherThanTextureCentre()
    {
        var uv = VrDistortion.Sample(Linear(), false, .14, .07, 1, .5, .5);
        Assert.Equal(.7714285714285714, uv.U, 10);
        Assert.Equal(.5, uv.V, 10);
    }

    [Fact]
    public void CardboardAxesAndMirrorAgree()
    {
        var lens = new VrLensConfig { K1 = .33582564, K2 = .55348791 };
        var left = VrDistortion.Sample(lens, false, .14, .07, .3, .7);
        var right = VrDistortion.Sample(lens, true, .14, .07, .7, .7);
        Assert.Equal(1 - left.U, right.U, 10);
        Assert.Equal(left.V, right.V, 10);
        // At the bottom-aligned lens axis the symmetric vertical frustum is exactly centred.
        var axis = VrDistortion.Sample(lens, false, .14, .07, .5435714285714286, .5);
        Assert.Equal(.5, axis.V, 10);
    }

    [Theory]
    [InlineData("off", false)]
    [InlineData("minimal", true)]
    [InlineData("MINIMAL", true)]
    [InlineData("unknown", false)]
    [InlineData(null, false)]
    public void HudVocabulary(string? mode, bool expected) => Assert.Equal(expected, VrDistortion.MinimalHud(mode!));
}
