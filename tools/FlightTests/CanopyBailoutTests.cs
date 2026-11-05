using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Vr;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty 2026-11-05 06:38: the bail-out works the canopy system that already exists (CanopyDynamics: latch, hinge, jettison),
/// not a new canopy object. Pins out and latch open: it goes at once in the air, and on the ground (a wreck at rest) only
/// once the pilot's hand has shoved it up past 10° (and, the Pitts, slid it its 4 in aft first). Numbers copied by hand.
/// </summary>
public class CanopyBailoutTests
{
    private static readonly Vec3 OneG = new(0, -9.80665, 0);
    private const double Q100Kt = 1620.8;   // ½ρ₀ (100 kt)²

    private static (CanopyBailout b, double t) Run(CanopySpec spec, double q, Vec3 g, double maxSec = 3.0)
    {
        var b = new CanopyBailout(new CanopyDynamics(spec));
        double t = 0;
        while (!b.Departed && t < maxSec) { b.Step(0.01, q, g); t += 0.01; }
        return (b, t);
    }

    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("extra-300-like")]
    public void InTheAirItGoesOnTheFirstStep(string id)
    {
        var (b, t) = Run(CanopySpec.For(id), Q100Kt, OneG);
        Assert.True(b.Departed);
        Assert.Equal(CanopyDeparture.Jettisoned, b.Canopy.DepartedBy);
        Assert.True(t <= 0.011, $"took {t:F3} s");
        Assert.True(b.Canopy.HingePinsOut);
    }

    [Fact]
    public void OnTheGroundTheExtraGoesOnlyOnceHisHandHasLiftedIt()
    {
        var (b, t) = Run(CanopySpec.For("extra-300-like"), 0.0, OneG);
        Assert.True(b.Departed, "it never left");
        Assert.Equal(CanopyDeparture.Jettisoned, b.Canopy.DepartedBy);
        Assert.True(b.Canopy.AngleDeg >= 10.0, $"left at {b.Canopy.AngleDeg:F1}°");
        Assert.InRange(t, 0.03, 1.0);
    }

    [Fact]
    public void OnTheGroundThePittsSlidesItsFourInchesBeforeItLifts()
    {
        var (b, t) = Run(CanopySpec.For("pitts-s2b-like"), 0.0, OneG);
        Assert.True(b.Departed, "it never left");
        Assert.Equal(0.1016, b.Canopy.SlideM, 4);
        Assert.True(b.Canopy.AngleDeg >= 10.0, $"left at {b.Canopy.AngleDeg:F1}°");
        Assert.InRange(t, 0.25, 1.2);   // 4 in at 0.4 m/s is 0.25 s before it can swing
    }

    [Fact]
    public void AWreckOnItsBackLetsItFallOpen()
    {
        var (b, _) = Run(CanopySpec.For("extra-300-like"), 0.0, new Vec3(0, 9.80665, 0));
        Assert.True(b.Departed);
    }

    [Fact]
    public void WithoutTheBailoutALatchedCanopyStaysOn()
    {
        var c = new CanopyDynamics(CanopySpec.For("extra-300-like"));
        for (int i = 0; i < 300; i++) c.Step(0.01, Q100Kt, OneG, true, Vec3.Zero, Vec3.Zero);
        Assert.False(c.Departed);
        Assert.True(c.Shut);
    }

    [Fact]
    public void APartSizedCanopyHasThePittsAndExtrasOwnMass()
    {
        // ForPart at the two written-down bubbles gives their written-down masses (10.8 kg/m² of plan area).
        Assert.Equal(9.0, CanopySpec.ForPart(1.48, 0.716, 0.406).MassKg, 1);
        Assert.Equal(14.0, CanopySpec.ForPart(2.12, 0.78, 0.48).MassKg, 1);
        Assert.True(CanopySpec.Known("pitts-s2b-like") && CanopySpec.Known("extra-300-like"));
        Assert.False(CanopySpec.Known("sgs-2-33-like") || CanopySpec.Known("c172-like") || CanopySpec.Known(null!));
    }

    [Theory]
    [InlineData(1.6, 0.7, 0.45)]    // a glider's bubble (2-33 / Grob class)
    [InlineData(1.05, 0.06, 0.95)]  // a cabin door on its edge (the 172's)
    public void APartSizedCanopyGoesInTheAirAndOnTheGround(double l, double w, double h)
    {
        CanopySpec s = CanopySpec.ForPart(l, w, h);
        Assert.InRange(s.MassKg, 2.0, 40.0);
        if (w < 0.5 * h) Assert.Equal(1.05, s.SpanM, 6);   // a door: hinge to free edge is its chord
        Assert.True(Run(s, Q100Kt, OneG).b.Departed);
        var (b, t) = Run(s, 0.0, OneG);
        Assert.True(b.Departed, $"on the ground it never left ({b.Canopy.AngleDeg:F1}°)");
        Assert.True(t < 1.0, $"took {t:F2} s");
    }
}
