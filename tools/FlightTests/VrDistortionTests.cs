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
        // The screen's own edges land on the texture's own edges: no black margin where the lens could see more.
        // Both reaches are .032 / .04 = .8 tangent, and Distort(.8) = 1.1533096460288 (hand literal) is far
        // inside the 85° lens, so the frustum edge IS the distorted screen reach.
        Assert.Equal(1.1533096460288, edge, 12);
        Assert.Equal(0.0, VrDistortion.Sample(lens, false, .128, .08, 0, .5).U, 10);
        Assert.Equal(1.0, VrDistortion.Sample(lens, false, .128, .08, 1, .5).U, 10);
        // Where the LENS is the limit, what the screen shows beyond it stays out of range for the black mask.
        lens.FovRightDeg = 45;
        var outside = VrDistortion.Sample(lens, false, .128, .08, 1, .5);
        Assert.True(outside.U > 1);
    }

    /// <summary>The bug this pins: the frustum edge was Undistort(reach / d), narrower than the eye tangent the
    /// compositor computes for the screen's own edge, so the edge sampled outside the eye texture and showed
    /// black. On an iPhone 17 Pro (0.1406 x 0.0650 m) the inner, bottom and top edges are screen-limited and
    /// must map to exactly 1, 0 and 1; the outer edge is lens-limited (60°), so it may fall outside.</summary>
    [Fact]
    public void ScreenLimitedEdgesLeaveNoBlackMargin()
    {
        var lens = new VrLensConfig().Sane();
        const double wM = 0.1406, hM = 0.0650;
        // Sample along the lens axes so each edge is a pure horizontal or vertical tangent.
        const double axisRow = .035 / .0650;                  // v of the lens axis: 35 mm above the bottom
        const double axisColumn = .03835 / .0703;             // u of the left lens axis: (0.0703 - 0.03195) / 0.0703
        Assert.Equal(1.0, VrDistortion.Sample(lens, false, wM, hM, 1, axisRow).U, 9);   // inner edge, left eye
        Assert.Equal(0.0, VrDistortion.Sample(lens, true, wM, hM, 0, axisRow).U, 9);    // inner edge, right eye
        Assert.Equal(0.0, VrDistortion.Sample(lens, false, wM, hM, axisColumn, 0).V, 9);  // bottom edge
        Assert.Equal(1.0, VrDistortion.Sample(lens, false, wM, hM, axisColumn, 1).V, 9);  // top edge
        // Outer edge: screen tangent .9758 is SEEN at 1.7776 > tan 60°, so it lies just beyond the lens:
        // (1.7320508075688767 - 1.777630305736899) / (1.7320508075688767 + 1.1899857961857687) (hand literal).
        Assert.Equal(-0.015598537714912682, VrDistortion.Sample(lens, false, wM, hM, 0, axisRow).U, 9);
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
