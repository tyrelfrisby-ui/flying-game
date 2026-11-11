using FlyingGame.Core;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// <see cref="PlaningWake"/> against Ty's three observations and the two papers they come out of.
///
/// Ty, 2026-11-11, from the cockpit of his own J-3 on floats: "There needs to be more white spray/foam behind the
/// floats at any speed above 10 mph ramping up with speed. The keel pushes down into the water and creates a
/// 'rooster tail' of spray that is not modeled it should be. At plow the water is significantly displaced downward
/// and a 'cavity' is created behind the airplane that is visible". Then: "Using physics".
///
/// Sources: Savitsky (1964) for the planing onset at Cv = 2, Savitsky and Morabito (2010) for the wake surface
/// behind the step (which is already <see cref="FloatHydro.WakeHeightBeams"/> and already pinned by HydroJ3Tests).
/// </summary>
public sealed class PlaningWakeTests
{
    private readonly ITestOutputHelper _out;
    public PlaningWakeTests(ITestOutputHelper o) { _out = o; }

    /// <summary>The J-3's own float, from configs/aircraft/j3-cub-floats-like.json.</summary>
    private const double BeamM = 0.5358, DeadriseDeg = 18.8;
    private const double Mph = 0.44704;

    /// <summary>The 15 mph PLOW state the render fixture measured and wrote to hydro-regime.txt on 2026-11-11:
    /// regime=PLOW, trim 8.233 deg, lambda 1.832, step draft 0.1706 m, stern draft 0.0265 m, ventilation 1.0.</summary>
    private const double PlowSpeed = 6.7, PlowTrim = 8.233, PlowLambda = 1.832, PlowStepDraft = 0.1706;

    // ---- 1. his 10 mph is a Froude number -----------------------------------------------------------------------

    /// <summary>
    /// THE CHECK THAT DECIDED THE WHOLE DESIGN. Ty gave a speed; Savitsky gives a speed coefficient. On this
    /// float's own 0.5358 m beam they are the same number: Cv = 2 falls at 10.25 mph, and he said "above 10 mph".
    /// So the onset is stored as a Cv and every other float gets it for free.
    /// </summary>
    [Fact]
    public void TenMilesPerHourIsSavitskySPlaningOnsetOnThisFloatSBeam()
    {
        double cv = PlaningWake.SpeedCoefficient(10 * Mph, BeamM);
        Assert.Equal(1.95, cv, 2);
        double onset = PlaningWake.SpeedForCv(PlaningWake.WhiteWaterOnsetCv, BeamM) / Mph;
        Assert.Equal(10.26, onset, 2);
        _out.WriteLine($"  10 mph on a {BeamM:F4} m beam is Cv {cv:F2}; Savitsky's Cv = 2 is {onset:F2} mph");
    }

    /// <summary>And the same onset on a wider float is a HIGHER speed, which is the whole reason for storing a
    /// Froude number: a Beaver's float is about twice the Cub's beam, so its white water starts about 40 % faster.</summary>
    [Fact]
    public void AWiderFloatStartsThrowingWhiteWaterAtAHigherSpeed()
    {
        double cub = PlaningWake.SpeedForCv(2.0, BeamM);
        double beaver = PlaningWake.SpeedForCv(2.0, 1.07);
        Assert.Equal(System.Math.Sqrt(1.07 / BeamM), beaver / cub, 6);
        _out.WriteLine($"  Cv = 2: Cub float {cub / Mph:F1} mph, a 1.07 m beam {beaver / Mph:F1} mph");
    }

    // ---- 2. it switches off where it should ---------------------------------------------------------------------

    /// <summary>A displacement taxi parts the water and leaves it clear. No white water, no rooster tail: a
    /// rooster that is still there at idle is wrong, and this is the assertion that says so.</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(1.5)]
    [InlineData(3.0)]
    public void ADisplacementTaxiThrowsNoWhiteWater(double mph)
    {
        double cv = PlaningWake.SpeedCoefficient(mph * Mph, BeamM);
        Assert.True(cv < PlaningWake.WhiteWaterOnsetCv);
        Assert.Equal(0.0, PlaningWake.WhiteWater01(cv), 9);
    }

    /// <summary>And it ramps with speed from there, monotonically, reaching full by about 18 mph.</summary>
    [Fact]
    public void WhiteWaterRampsWithSpeedAboveTheOnset()
    {
        double last = -1;
        foreach (double mph in new[] { 8.0, 10.0, 13.0, 15.0, 20.0, 26.0, 40.0 })
        {
            double w = PlaningWake.WhiteWater01(PlaningWake.SpeedCoefficient(mph * Mph, BeamM));
            Assert.True(w >= last, $"white water must not fall with speed ({mph} mph)");
            last = w;
            _out.WriteLine($"  {mph,5:F0} mph  Cv {PlaningWake.SpeedCoefficient(mph * Mph, BeamM):F2}  white water {w:F2}");
        }
        Assert.Equal(0.0, PlaningWake.WhiteWater01(PlaningWake.SpeedCoefficient(8 * Mph, BeamM)), 9);
        Assert.Equal(1.0, PlaningWake.WhiteWater01(PlaningWake.SpeedCoefficient(18 * Mph, BeamM)), 3);
        // Two thirds of the way up at the 15 mph plow Ty judged it at.
        Assert.InRange(PlaningWake.WhiteWater01(PlaningWake.SpeedCoefficient(15 * Mph, BeamM)), 0.6, 0.72);
    }

    // ---- 3. the cavity and the rooster are the same curve --------------------------------------------------------

    /// <summary>
    /// THE CAVITY. At the step the free surface is as far below the undisturbed water as the keel is immersed —
    /// which is what Ty described: the keel pushed it down and it has not come back up. For the measured 15 mph
    /// plow that is 17 cm, and it closes about 1.1 m astern.
    /// </summary>
    [Fact]
    public void AtPlowTheWaterBehindTheStepIsPushedDownAndClosesAboutAMetreAstern()
    {
        double cv = PlaningWake.SpeedCoefficient(PlowSpeed, BeamM);
        Assert.Equal(PlowStepDraft, PlaningWake.CavityDepthM(PlowStepDraft), 9);
        Assert.Equal(-PlowStepDraft, PlaningWake.SurfaceM(0, PlowStepDraft, BeamM, PlowLambda, PlowTrim, cv, DeadriseDeg), 9);
        double close = PlaningWake.CavityLengthM(PlowStepDraft, BeamM, PlowLambda, PlowTrim, cv, DeadriseDeg);
        Assert.InRange(close, 1.0, 1.25);
        // And the surface really is back at the undisturbed level there, which is what "closes" means.
        Assert.Equal(0.0, PlaningWake.SurfaceM(close, PlowStepDraft, BeamM, PlowLambda, PlowTrim, cv, DeadriseDeg), 6);
        _out.WriteLine($"  15 mph plow (Cv {cv:F2}): cavity {PlowStepDraft * 100:F0} cm deep at the step, " +
                       $"closed {close:F2} m astern");
    }

    /// <summary>THE ROOSTER TAIL: the crest of the same curve, 2.1 m astern at 15 mph and 12 cm above the
    /// undisturbed water. It is the crest of Savitsky and Morabito's wake, at the quarter wave where their sine
    /// reaches one — not a separate effect, and not a drawn plume.</summary>
    [Fact]
    public void TheRoosterTailIsTheCrestOfTheSameWake()
    {
        double cv = PlaningWake.SpeedCoefficient(PlowSpeed, BeamM);
        double x = PlaningWake.CrestDistanceM(BeamM, cv);
        double h = PlaningWake.CrestHeightM(PlowStepDraft, BeamM, PlowLambda, PlowTrim, DeadriseDeg);
        Assert.InRange(x, 2.0, 2.2);
        Assert.InRange(h, 0.10, 0.14);
        // The crest really is the maximum: half a metre either side of it the surface is lower.
        double here = PlaningWake.SurfaceM(x, PlowStepDraft, BeamM, PlowLambda, PlowTrim, cv, DeadriseDeg);
        Assert.True(here > PlaningWake.SurfaceM(x - 0.5, PlowStepDraft, BeamM, PlowLambda, PlowTrim, cv, DeadriseDeg));
        Assert.Equal(h, here, 6);
        _out.WriteLine($"  15 mph plow: rooster crest {x:F2} m astern, {h * 100:F0} cm above the undisturbed water, " +
                       $"rising at {PlaningWake.CrestRiseSpeedMs(PlowSpeed, PlowStepDraft, BeamM, PlowLambda, PlowTrim, cv, DeadriseDeg):F2} m/s");
    }

    /// <summary>The rooster moves AFT as the speed rises — x goes as Cv^(2/3) — which is what anyone who has
    /// watched a boat accelerate has seen. From 15 mph to 40 mph it roughly doubles its distance astern.</summary>
    [Fact]
    public void TheRoosterMovesAsternWithSpeed()
    {
        double plow = PlaningWake.CrestDistanceM(BeamM, PlaningWake.SpeedCoefficient(6.7, BeamM));
        double step = PlaningWake.CrestDistanceM(BeamM, PlaningWake.SpeedCoefficient(17.9, BeamM));
        Assert.True(step > 1.8 * plow);
        Assert.Equal(System.Math.Pow(17.9 / 6.7, 2.0 / 3.0), step / plow, 6);
        _out.WriteLine($"  rooster crest: {plow:F2} m astern at 15 mph, {step:F2} m at 40 mph");
    }

    /// <summary>On the step the aeroplane is riding high: the draft at the step is small, so the cavity is shallow
    /// and short while the rooster is further aft and still stands clear of the water. That is the regime change
    /// Ty will judge the 40 mph frame on, and it falls out of the draft rather than being switched.</summary>
    [Fact]
    public void OnStepTheCavityIsShallowAndTheRoosterIsFarAstern()
    {
        // A planing trim and a light step draft: 4 deg, lambda 1.2, 4 cm, which is what a float riding on its
        // step looks like against the 17 cm and 8.2 deg of the plow.
        PlaningWake.Shape onStep = PlaningWake.Of(17.9, BeamM, 0.04, 1.2, 4.0, DeadriseDeg);
        PlaningWake.Shape plow = PlaningWake.Of(6.7, BeamM, PlowStepDraft, PlowLambda, PlowTrim, DeadriseDeg);
        Assert.True(onStep.CavityDepthM < plow.CavityDepthM * 0.3);
        Assert.True(onStep.CavityLengthM < plow.CavityLengthM);
        Assert.True(onStep.CrestDistanceM > 1.9 * plow.CrestDistanceM);
        Assert.True(onStep.WhiteWater01 > 0.99, "on step the whole wake aft of the step is aerated");
        Assert.InRange(plow.WhiteWater01, 0.6, 0.72);   // plowing is two thirds of the way up the ramp
        _out.WriteLine($"  on step (Cv {onStep.Cv:F1}): cavity {onStep.CavityDepthM * 100:F0} cm / " +
                       $"{onStep.CavityLengthM:F2} m, rooster {onStep.CrestDistanceM:F2} m astern at " +
                       $"{onStep.CrestHeightM * 100:F0} cm, white water {onStep.WhiteWater01:F2}");
        _out.WriteLine($"  plow    (Cv {plow.Cv:F1}): cavity {plow.CavityDepthM * 100:F0} cm / " +
                       $"{plow.CavityLengthM:F2} m, rooster {plow.CrestDistanceM:F2} m astern at " +
                       $"{plow.CrestHeightM * 100:F0} cm, white water {plow.WhiteWater01:F2}");
    }

    // ---- the aerated keel track: air entrainment, not spray -----------------------------------------------------

    /// <summary>
    /// THE TEST THAT WOULD HAVE CAUGHT THE FITTED WIDTH, and the one Ty's own description turns on: "The spray is
    /// directly behind and in line with the floats keels and WIDENS AT A NARROWER RATE THAN THE WAKE DOES".
    ///
    /// The two angles come from two different physics and must never be confused. The Kelvin wake's half-angle is
    /// asin(1/3) = 19.47 deg, set by the dispersion of deep-water gravity waves and so the same at every speed. The
    /// bubble track's is set by TURBULENT SPREADING of a plane mixing layer, 4.86 deg. A fitted band of "1.6 beams"
    /// had no angle at all and sat between the two; this says they are a factor of four apart and which way round.
    /// </summary>
    [Fact]
    public void TheAeratedTrackWidensFourTimesMoreSlowlyThanTheKelvinWake()
    {
        double track = PlaningWake.TrackHalfAngleRad, kelvin = PlaningWake.KelvinHalfAngleRad;
        Assert.Equal(19.47, kelvin * 57.29578, 2);
        Assert.Equal(4.86, track * 57.29578, 2);
        Assert.True(track < kelvin, "the aerated track must widen more slowly than the wake");
        Assert.InRange(kelvin / track, 3.5, 4.5);
        _out.WriteLine($"  keel track {track * 57.29578:F2} deg, Kelvin {kelvin * 57.29578:F2} deg, " +
                       $"ratio {kelvin / track:F2}");
    }

    /// <summary>And it holds at EVERY speed in the planing range, not just at the one that was shot: the track's
    /// angle does not depend on speed and neither does Kelvin's, so a test at one speed would prove nothing about
    /// another. Both widths are compared at a metre astern as well, where the track's own starting width could in
    /// principle still put it outside the wake.</summary>
    [Theory]
    [InlineData(10.0)]
    [InlineData(15.0)]
    [InlineData(25.0)]
    [InlineData(40.0)]
    [InlineData(60.0)]
    public void TheTrackStaysInsideTheWakeAtEverySpeed(double mph)
    {
        double v = mph * Mph;
        double draft = mph < 20 ? PlowStepDraft : 0.04;      // the hull rides higher as it planes
        for (double x = 0.0; x <= 6.0; x += 0.5)
        {
            double track = PlaningWake.TrackHalfWidthAtM(x, draft, BeamM, DeadriseDeg);
            double kelvin = 0.5 * BeamM + x * Math.Tan(PlaningWake.KelvinHalfAngleRad);
            Assert.True(track <= kelvin + 1e-9,
                $"at {mph} mph and {x} m astern the aerated track ({track:F3} m) is outside the wake ({kelvin:F3} m)");
        }
        _out.WriteLine($"  {mph,4:F0} mph: track half-width {PlaningWake.TrackHalfWidthAtM(0, draft, BeamM, DeadriseDeg):F3} m " +
                       $"at the keel, {PlaningWake.TrackHalfWidthAtM(6, draft, BeamM, DeadriseDeg):F3} m at 6 m astern");
    }

    /// <summary>The track starts at the FLOAT'S OWN wetted width — a V bottom of deadrise β immersed to d is wetted
    /// to d/tan β, capped at the chine — and not at a multiple of the beam. On this float both the plow and the
    /// planing run are chine-limited, so the track leaves the keel exactly one beam wide; a shallower draft on a
    /// deeper V would make it narrower, and that is the branch the second case checks.</summary>
    [Fact]
    public void TheTrackStartsAtTheKeelSOwnWettedWidth()
    {
        // The two drafts the fixture actually measured: 0.171 m plowing and 0.149 m on the step. Both wet the V
        // past its chine (0.171/tan 18.8 = 0.50 m, 0.149/tan 18.8 = 0.44 m, against a 0.268 m half-beam), so both
        // leave the keel exactly one beam wide.
        Assert.Equal(0.5 * BeamM, PlaningWake.TrackHalfWidthM(PlowStepDraft, BeamM, DeadriseDeg), 9);
        Assert.Equal(0.5 * BeamM, PlaningWake.TrackHalfWidthM(0.1486, BeamM, DeadriseDeg), 9);
        // The other branch, and it has to work or the chine cap is doing all the arithmetic: a float barely wet
        // is NOT chine-limited and its track is narrower than its beam. 4 cm on this V is 0.117 m, not 0.268 m.
        Assert.Equal(0.04 / Math.Tan(DeadriseDeg * Math.PI / 180), PlaningWake.TrackHalfWidthM(0.04, BeamM, DeadriseDeg), 9);
        Assert.True(PlaningWake.TrackHalfWidthM(0.04, BeamM, DeadriseDeg) < 0.5 * BeamM);
        Assert.Equal(0.0, PlaningWake.TrackHalfWidthM(0.0, BeamM, DeadriseDeg), 9);
    }

    /// <summary>
    /// How long the white water lasts is how long the bubbles take to rise out: V·d/v_rise with v_rise = 0.25 m/s.
    /// 4.6 m at the 15 mph plow and 10.6 m at 40 mph on the step — longer with speed AND with how deep the keel
    /// is, both of which fall out of the mechanism rather than being chosen. The old exponential had neither.
    /// </summary>
    [Fact]
    public void TheWhiteWaterLastsAsLongAsTheBubblesTakeToRise()
    {
        double plow = PlaningWake.AeratedLengthM(PlowSpeed, PlowStepDraft);
        double step = PlaningWake.AeratedLengthM(17.9, 0.1486);
        Assert.InRange(plow, 4.5, 4.7);
        Assert.InRange(step, 10.4, 10.8);
        // It thins LINEARLY as the column empties, and it ends.
        Assert.Equal(1.0, PlaningWake.AeratedFraction01(0, PlowSpeed, PlowStepDraft), 9);
        Assert.Equal(0.5, PlaningWake.AeratedFraction01(plow / 2, PlowSpeed, PlowStepDraft), 6);
        Assert.Equal(0.0, PlaningWake.AeratedFraction01(plow, PlowSpeed, PlowStepDraft), 9);
        Assert.Equal(0.0, PlaningWake.AeratedFraction01(plow + 5, PlowSpeed, PlowStepDraft), 9);
        _out.WriteLine($"  white water lasts {plow:F1} m astern at 15 mph, {step:F1} m at 40 mph " +
                       $"(bubbles rising at {PlaningWake.BubbleRiseMs} m/s)");
    }

    /// <summary>A float out of the water entrains nothing, so there is no track to draw — the same gate the rest
    /// of this class uses, applied to the thing that would otherwise trail behind an aeroplane in the air.</summary>
    [Fact]
    public void ADryFloatLeavesNoTrack()
    {
        Assert.Equal(0.0, PlaningWake.AeratedLengthM(17.9, 0.0), 9);
        Assert.Equal(0.0, PlaningWake.TrackHalfWidthM(0.0, BeamM, DeadriseDeg), 9);
        Assert.Equal(0.0, PlaningWake.AeratedFraction01(1.0, 17.9, 0.0), 9);
    }

    /// <summary>Nothing happens at all when the float is not in the water: every length is zero, so a float a
    /// foot in the air cannot leave a cavity behind it.</summary>
    [Fact]
    public void ADryFloatLeavesNothing()
    {
        PlaningWake.Shape dry = PlaningWake.Of(17.9, BeamM, 0.0, 0.0, 0.0, DeadriseDeg);
        Assert.Equal(0.0, dry.CavityDepthM, 9);
        Assert.Equal(0.0, dry.CrestHeightM, 9);
    }
}
