using FlyingGame.Core.Engine;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE TWO-BLADE PROP'S PULSATING GYROSCOPIC LOAD, AND THE CRANK IT BREAKS (Ty 2026-11-09: "The mt on the pitts
/// prevents crank shaft failures from the pulsating gyroscopic load of the Hartzell two blade metal prop").
/// </summary>
public class CrankshaftFatigueTests
{
    private readonly ITestOutputHelper _out;
    public CrankshaftFatigueTests(ITestOutputHelper o) { _out = o; }

    private const double PittsPropInertia = 3.5;   // kg.m^2, prop + crank (PropulsionConfig.PropInertia)

    /// <summary>
    /// THE WHOLE POINT: three blades cannot do this. A rotor with three or more blades has the same inertia about
    /// every axis across its shaft, so its gyroscopic reaction is steady and there is no alternating load to fatigue
    /// anything. Two blades swing between end-on and across twice a revolution. This is the difference between the
    /// Hartzell and the MT, and it is geometry, not a material property.
    /// </summary>
    [Fact]
    public void ThreeBladesPulsateNotAtAllAndTwoBladesDo()
    {
        const double rpm = 2700, rate = 2.0;
        double two = CrankshaftFatigue.PulseAmplitudeNm(2, PittsPropInertia, rpm, rate);
        _out.WriteLine($"2-blade {two:F0} N.m at {rate} rad/s, 2700 rpm");
        Assert.True(two > 500, $"a two-blade must pulsate: {two:F0} N.m");
        foreach (int b in new[] { 3, 4, 5 })
            Assert.Equal(0.0, CrankshaftFatigue.PulseAmplitudeNm(b, PittsPropInertia, rpm, rate), 9);
        // And so the MT accumulates no damage at all, however it is flown.
        foreach (double r in new[] { 1.0, 3.0, 6.0 })
            Assert.Equal(0.0, CrankshaftFatigue.DamagePerSecond(CrankshaftFatigue.PulseAmplitudeNm(3, PittsPropInertia, rpm, r), rpm), 12);
    }

    /// <summary>Gentle flying is free: below the endurance limit steel does not accumulate fatigue, which is why
    /// cranks survive thousands of cross-country hours and fail in aerobatic service.</summary>
    [Fact]
    public void BelowTheEnduranceLimitNothingIsSpent()
    {
        // 20 deg/s at cruise rpm — a normal turn.
        double gentle = CrankshaftFatigue.PulseAmplitudeNm(2, PittsPropInertia, 2300, 20.0 * System.Math.PI / 180.0);
        _out.WriteLine($"a normal turn: {gentle:F0} N.m, endurance limit {CrankshaftFatigue.EnduranceNm:F0}");
        Assert.True(gentle < CrankshaftFatigue.EnduranceNm);
        Assert.Equal(0.0, CrankshaftFatigue.DamagePerSecond(gentle, 2300), 12);
    }

    /// <summary>
    /// And hard aerobatics is not. The damage law is a fifth power, so the rate climbs very fast with the rate the
    /// pilot pulls: this is what makes the failure a consequence of how the aeroplane is flown rather than a dice
    /// roll against the clock.
    /// </summary>
    [Fact]
    public void DamageClimbsSteeplyWithHowHardItIsFlown()
    {
        const double rpm = 2700;
        double prev = 0; double lastRatio = 0;
        foreach (double degS in new[] { 60.0, 90.0, 120.0, 180.0, 300.0 })
        {
            double amp = CrankshaftFatigue.PulseAmplitudeNm(2, PittsPropInertia, rpm, degS * System.Math.PI / 180.0);
            double d = CrankshaftFatigue.DamagePerSecond(amp, rpm);
            double hoursToSpend = d > 0 ? 1.0 / (d * 3600.0) : double.PositiveInfinity;
            _out.WriteLine($"{degS,5:F0} deg/s: {amp,7:F0} N.m -> {hoursToSpend,12:F0} hours of THIS to spend the crank");
            if (prev > 0) lastRatio = d / prev;
            Assert.True(d >= prev, "damage must not fall as the loading rises");
            prev = d;
        }
        Assert.True(lastRatio > 2.0, "a fifth-power law must climb steeply");
    }

    /// <summary>The hazard is zero on a fresh crank and serious on a spent one, and it rises as a cube so a part-worn
    /// crank is not treated as about to break.</summary>
    [Fact]
    public void TheHazardRisesWithWhatHasBeenSpent()
    {
        Assert.Equal(0.0, CrankshaftFatigue.RatePerHour(0.0), 12);
        double half = CrankshaftFatigue.RatePerHour(0.5), full = CrankshaftFatigue.RatePerHour(1.0);
        _out.WriteLine($"half-spent {half:F4}/h, spent {full:F4}/h ({1 / full:F0} hours between failures at that point)");
        Assert.True(full > 7 * half, "a cube: the last of the life is where the risk is");
        Assert.InRange(1.0 / full, 5.0, 20.0);
        Assert.True(CrankshaftFatigue.StepProbability(1.0, 0.02) > 0);
        Assert.Equal(0.0, CrankshaftFatigue.StepProbability(0.0, 0.02), 12);
    }
}
