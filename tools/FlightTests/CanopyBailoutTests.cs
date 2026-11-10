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

    [Fact]
    public void InTheAirTheExtraGoesOnTheFirstStep()
    {
        var (b, t) = Run(CanopySpec.For("extra-300-like"), Q100Kt, OneG);
        Assert.True(b.Departed);
        Assert.Equal(CanopyDeparture.Jettisoned, b.Canopy.DepartedBy);
        Assert.True(t <= 0.011, $"took {t:F3} s");
        Assert.True(b.Canopy.HingePinsOut);
    }

    [Fact]
    public void InTheAirThePittsRunsAftPastItsTabAndItsHooksLetGo()
    {
        // v2 stop tab: the jettison frees the track; it runs aft (4 in + 2 in) past the tab and leaves past 150°.
        var (b, t) = Run(CanopySpec.For("pitts-s2b-like"), Q100Kt, OneG);
        Assert.True(b.Departed, $"never left ({b.Canopy.AngleDeg:F0}°, slid {b.Canopy.SlideM * 1000:F0} mm)");
        Assert.True(t < 2.0, $"took {t:F2} s");
    }

    /// <summary>
    /// A FLAT SPIN IS NEITHER STILL AIR NOR CRUISE (Ty 2026-11-08: "Bailing out while in an upright flat spin in the pitts
    /// doesn't work. I press the button nothing happens"). Between the dynamic pressure of 20 kt — where the pilot's hand
    /// used to be switched off, on the assumption that anything faster has air enough to do the job — and about 45 kt,
    /// where the air really can, the Pitts' slide-then-hinge canopy slid its 152 mm aft and then STAYED ON: measured still
    /// on after 30 s at 21, 30 and 40 kt, while every plain side-hinged canopy in the fleet left on the first step. On the
    /// device it finally went 12.63 s later at 7 KIAS, i.e. once the aeroplane had hit the ground, and the pilot jumped at
    /// 6 m AGL. A flat spin sits squarely in that band, and so does the back of a botched loop. Every speed he can bail out
    /// at has to get him out, and in about the time the sequence promises.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(19.0)]
    [InlineData(21.0)]
    [InlineData(30.0)]
    [InlineData(40.0)]
    [InlineData(51.0)]
    [InlineData(80.0)]
    [InlineData(100.0)]
    public void ThePittsCanopyLeavesAtEverySpeedHeCanBailOutAt(double kt)
    {
        double ms = kt * 0.514444, q = 0.5 * 1.225 * ms * ms;
        var (b, t) = Run(CanopySpec.For("pitts-s2b-like"), q, OneG, maxSec: 10.0);
        Assert.True(b.Departed, $"{kt:F0} kt (q {q:F0} Pa): still on after 10 s at {b.Canopy.AngleDeg:F0}°, slid {b.Canopy.SlideM * 1000:F0} mm");
        Assert.True(t < 2.0, $"{kt:F0} kt: took {t:F2} s");
    }

    /// <summary>And the plain side-hinged canopies keep what they had: in moving air the airflow takes them at once, so
    /// giving the pilot a hand at every speed has not slowed anyone down.</summary>
    [Theory]
    [InlineData("extra-300-like")]
    [InlineData("great-lakes-like")]
    [InlineData("j3-cub-like")]
    public void ASideHingedCanopyStillGoesOnTheFirstStepInMovingAir(string id)
    {
        var (b, t) = Run(CanopySpec.For(id), 0.5 * 1.225 * 15.4 * 15.4, OneG);   // 30 kt
        Assert.True(b.Departed && t <= 0.011, $"{id}: took {t:F3} s");
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
    public void OnTheGroundThePittsSlidesPastItsTabBeforeItGoesOver()
    {
        var (b, t) = Run(CanopySpec.For("pitts-s2b-like"), 0.0, OneG);
        Assert.True(b.Departed, "it never left");
        Assert.Equal(CanopyDeparture.Jettisoned, b.Canopy.DepartedBy);
        Assert.Equal(0.1524, b.Canopy.SlideM, 4);                 // 4 in + the 2 in the jettison frees
        Assert.True(b.Canopy.AngleDeg >= 150.0, $"left at {b.Canopy.AngleDeg:F1}°");
        Assert.InRange(t, 0.38, 2.5);   // 6 in at 0.4 m/s is 0.38 s before it can swing
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
