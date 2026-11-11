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
