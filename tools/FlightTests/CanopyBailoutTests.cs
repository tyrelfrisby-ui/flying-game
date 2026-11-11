using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Vr;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty 2026-11-05 06:38: the bail-out works the canopy system that already exists (CanopyDynamics: latch, hinge, jettison),
/// not a new canopy object. Numbers copied by hand.
///
/// AND SINCE 2026-11-11 IT ASKS FIRST WHETHER ANYTHING SHOULD LEAVE AT ALL. Ty, after a hard landing in the Extra:
/// "the canopy shattered even though it didn't hit anything … And on the ground it should not fall off it should stay
/// open. The extra has no jettison feature." So the jettison is pulled only by a type whose config authors one, and
/// only in the air; on the ground the canopy is shoved open and stays on the aeroplane, and the pilot climbs out past
/// it (CanopyBailout.ClearToLeave, not Departed).
/// </summary>
public class CanopyBailoutTests
{
    private static readonly Vec3 OneG = new(0, -9.80665, 0);
    private const double Q100Kt = 1620.8;   // ½ρ₀ (100 kt)²

    /// <summary>A type that HAS a jettison, bailing out in the air: the old behaviour, which is still most of them.</summary>
    private static (CanopyBailout b, double t) Run(CanopySpec spec, double q, Vec3 g, double maxSec = 3.0) =>
        Run(spec.WithJettison(true), q, g, onGround: false, maxSec);

    private static (CanopyBailout b, double t) Run(CanopySpec spec, double q, Vec3 g, bool onGround, double maxSec = 3.0)
    {
        var b = new CanopyBailout(new CanopyDynamics(spec), onGround);
        double t = 0;
        while (!b.Departed && t < maxSec) { b.Step(0.01, q, g); t += 0.01; }
        return (b, t);
    }

    /// <summary>Run it to a stop whether or not it departs, and say where it ended up.</summary>
    private static (CanopyBailout b, double t) RunFor(CanopySpec spec, double q, Vec3 g, bool onGround, double sec)
    {
        var b = new CanopyBailout(new CanopyDynamics(spec), onGround);
        double t = 0;
        while (t < sec) { b.Step(0.01, q, g); t += 0.01; }
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

    /// <summary>The jettison pulled and no airflow to help (a stalled or tumbling aeroplane, not the ground — on the
    /// ground nothing is pulled at all): it goes only once his hand has lifted it past 10°.</summary>
    [Fact]
    public void WithThePinsOutAndNoAirflowItGoesOnlyOnceHisHandHasLiftedIt()
    {
        var (b, t) = Run(CanopySpec.For("extra-300-like"), 0.0, OneG);
        Assert.True(b.Departed, "it never left");
        Assert.Equal(CanopyDeparture.Jettisoned, b.Canopy.DepartedBy);
        Assert.True(b.Canopy.AngleDeg >= 10.0, $"left at {b.Canopy.AngleDeg:F1}°");
        Assert.InRange(t, 0.03, 1.0);
    }

    /// <summary>Same for the Pitts: pins out, no airflow — his hand runs it the whole freed track aft before the
    /// swing can clear the tab.</summary>
    [Fact]
    public void WithThePinsOutAndNoAirflowThePittsSlidesPastItsTabBeforeItGoesOver()
    {
        var (b, t) = Run(CanopySpec.For("pitts-s2b-like"), 0.0, OneG);
        Assert.True(b.Departed, "it never left");
        Assert.Equal(CanopyDeparture.Jettisoned, b.Canopy.DepartedBy);
        Assert.Equal(0.1524, b.Canopy.SlideM, 4);                 // 4 in + the 2 in the jettison frees
        Assert.True(b.Canopy.AngleDeg >= 150.0, $"left at {b.Canopy.AngleDeg:F1}°");
        Assert.InRange(t, 0.38, 2.5);   // 6 in at 0.4 m/s is 0.38 s before it can swing
    }

    /// <summary>
    /// TY'S BUG, 2026-11-11, as nearly as a unit test can hold it: an Extra sitting intact and upright after a hard
    /// landing. Nothing may leave the aeroplane. Before this the canopy was jettisoned on the spot, fell a metre onto
    /// the turtledeck, counted as a strike and shattered.
    /// </summary>
    [Fact]
    public void OnTheGroundTheExtrasCanopyOpensAndStaysOn()
    {
        var (b, _) = RunFor(Extra(), 0.0, OneG, onGround: true, sec: 5.0);
        Assert.False(b.Departed, $"it left the aeroplane on the ground ({b.Canopy.DepartedBy})");
        Assert.False(b.JettisonPulled);
        Assert.False(b.Canopy.HingePinsOut);
        Assert.True(b.ClearToLeave, $"he cannot get out past it ({b.Canopy.AngleDeg:F0}°)");
        Assert.InRange(b.Canopy.AngleDeg, CanopyDynamics.ClearToLeaveDeg, 101.0);
    }

    /// <summary>And the Pitts, which HAS a jettison: on the ground nobody pulls it. It opens onto its stop tab and
    /// stays there, and he climbs out past it.</summary>
    [Fact]
    public void OnTheGroundThePittsIsNotJettisonedEitherAlthoughItCouldBe()
    {
        CanopySpec pitts = CanopySpec.For("pitts-s2b-like").WithJettison(true);
        var (b, _) = RunFor(pitts, 0.0, OneG, onGround: true, sec: 5.0);
        Assert.True(b.Canopy.Spec.HasJettison);
        Assert.False(b.JettisonPulled);
        Assert.False(b.Departed, "the Pitts threw its canopy away on the ground");
        Assert.Equal(pitts.SlideM, b.Canopy.SlideM, 4);   // the normal 4 in: the jettison never freed the rest of the track
        Assert.True(b.ClearToLeave, $"he cannot get out past it ({b.Canopy.AngleDeg:F0}°)");
    }

    /// <summary>A type with NO jettison, in the air. The canopy is not thrown off: it is flung open and the AIR
    /// decides. Below the fitting-failure speed it flails at its stop, attached, and he goes over the side past it;
    /// above it the hinge fittings fail and it is torn off — a tear, not a jettison.</summary>
    [Fact]
    public void WithNoJettisonTheAirDecidesAndItIsATearNotAJettison()
    {
        var (fast, _) = Run(Extra(), Q100Kt, OneG, onGround: false, maxSec: 3.0);
        Assert.True(fast.Departed, "at 100 kt with the canopy open the hinge fittings should let go");
        Assert.Equal(CanopyDeparture.HingesFailed, fast.Canopy.DepartedBy);
        Assert.False(fast.JettisonPulled);
        Assert.False(fast.Canopy.HingePinsOut);

        // 25 kt: there is not the air to break anything, so it stays on and he still gets out past it.
        double ms = 25 * 0.514444;
        var (slow, _) = RunFor(Extra(), 0.5 * 1.225 * ms * ms, OneG, onGround: false, sec: 5.0);
        Assert.False(slow.Departed, "25 kt tore the canopy off a no-jettison type");
        Assert.True(slow.ClearToLeave, $"he cannot get out past it ({slow.Canopy.AngleDeg:F0}°)");
    }

    /// <summary>He has gone over the side: nothing is pushing the canopy any more.</summary>
    [Fact]
    public void OnceHeIsOutNoHandIsOnIt()
    {
        var b = new CanopyBailout(new CanopyDynamics(Extra()), onGround: true);
        for (int i = 0; i < 200; i++) b.Step(0.01, 0.0, OneG);
        Assert.True(b.Canopy.Held);
        b.HandsOff();
        Assert.True(b.Unhanded);
        Assert.False(b.Canopy.Held);
        for (int i = 0; i < 200; i++) b.Step(0.01, 0.0, OneG);
        Assert.False(b.Canopy.Held, "his hand came back after he had left the aeroplane");
        // It settles ON its own stop, which is a spring: a fraction of a degree into it and no further,
        // because nothing is pushing it.
        Assert.False(b.Departed);
        Assert.True(b.Canopy.AngleDeg <= b.Canopy.Spec.StopDeg + 1.0,
                    $"{b.Canopy.AngleDeg:F1}\u00b0 past a {b.Canopy.Spec.StopDeg:F0}\u00b0 stop");
    }

    /// <summary>The Extra's canopy as the config has it: no jettison.</summary>
    private static CanopySpec Extra() => CanopySpec.For("extra-300-like");

    [Fact]
    public void AWreckOnItsBackLetsItFallOpenAndStillKeepsIt()
    {
        var (b, _) = RunFor(Extra(), 0.0, new Vec3(0, 9.80665, 0), onGround: true, sec: 5.0);
        Assert.False(b.Departed, "an upside-down wreck dropped its canopy");
        Assert.True(b.ClearToLeave);
    }

    [Fact]
    public void WithoutTheBailoutALatchedCanopyStaysOn()
    {
        var c = new CanopyDynamics(CanopySpec.For("extra-300-like").WithJettison(true));
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
    public void APartSizedCanopyWithAJettisonGoesInTheAirAndInStillAir(double l, double w, double h)
    {
        CanopySpec s = CanopySpec.ForPart(l, w, h);
        Assert.InRange(s.MassKg, 2.0, 40.0);
        if (w < 0.5 * h) Assert.Equal(1.05, s.SpanM, 6);   // a door: hinge to free edge is its chord
        Assert.True(Run(s, Q100Kt, OneG).b.Departed);
        var (b, t) = Run(s, 0.0, OneG);
        Assert.True(b.Departed, $"in still air it never left ({b.Canopy.AngleDeg:F1}°)");
        Assert.True(t < 1.0, $"took {t:F2} s");
        // ... and ForPart authors no jettison of its own: the config has to say so.
        Assert.False(CanopySpec.ForPart(l, w, h).HasJettison);
        var (stays, _) = RunFor(CanopySpec.ForPart(l, w, h), 0.0, OneG, onGround: true, sec: 5.0);
        Assert.False(stays.Departed);
        Assert.True(stays.ClearToLeave);
    }
}
