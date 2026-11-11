using System;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// A STRUCTURAL LIMIT IS CERTIFICATED AT THE DESIGN WEIGHT, so it must not move when the aeroplane is loaded light.
///
/// This was harmless while every config authored `mass.massKg` AS its max gross — the two were the same number. They
/// stopped being the same on 2026-11-11, when the fleet's empty weights were corrected and `massKg` became the
/// aeroplane AS FLOWN (the J-3 on floats, 589.7 kg to 451.1). Anything structural still reading it shrank with it:
/// 14 CFR 23.527's ultimate water load goes as W^(2/3), so that one type's float lost 16.4 % of its modelled
/// strength without anybody touching the float.
///
/// The placard is SET IN THE TEST rather than read from a shipped config, so this proves the mechanism and does not
/// depend on which lane has authored which certificate yet.
/// </summary>
public class StructuralGrossTests
{
    private readonly ITestOutputHelper _o;
    public StructuralGrossTests(ITestOutputHelper o) { _o = o; }

    private static AircraftConfig Floats() =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "j3-cub-floats-like.json"));

    private static double UltimateN(AircraftConfig c) =>
        new Aircraft(c, LandingGear.RestingState(c, 0, 0, 0), ControlDeflections.Neutral).FloatUltimateWaterLoadN;

    [Fact]
    public void AFloatsUltimateWaterLoadDoesNotFallWhenTheAeroplaneIsLoadedLight()
    {
        const double placard = 589.67;             // the J-3's float placard, 1300 lb (TCDS A-691, Edo 60-1320)

        var heavy = Floats();
        heavy.Loading ??= new LoadingConfig();
        heavy.Loading.MaxTakeoffMassKg = placard;
        heavy.Mass.MassKg = placard;               // authored AT the placard, the old convention

        var light = Floats();
        light.Loading ??= new LoadingConfig();
        light.Loading.MaxTakeoffMassKg = placard;
        light.Mass.MassKg = 451.142;               // the same aeroplane, authored as flown

        double a = UltimateN(heavy), b = UltimateN(light);
        _o.WriteLine($"placard {placard:F1} kg. ultimate water load: authored at the placard {a / 1000:F2} kN, authored as flown {b / 1000:F2} kN");
        Assert.True(a > 0, "the type should have a float ultimate load at all");
        Assert.Equal(a, b, 3);
    }

    /// <summary>And the defect it replaces: with NO placard the design weight falls back to the authored mass, so the
    /// float really does weaken — which is what every float type did before a certificate was authored for it.</summary>
    [Fact]
    public void WithoutAPlacardItStillFallsBackToTheAuthoredMass()
    {
        var heavy = Floats(); heavy.Loading!.MaxTakeoffMassKg = 0; heavy.Loading.MaxGrossMassKg = 0; heavy.Mass.MassKg = 589.67;
        var light = Floats(); light.Loading!.MaxTakeoffMassKg = 0; light.Loading.MaxGrossMassKg = 0; light.Mass.MassKg = 451.142;
        double a = UltimateN(heavy), b = UltimateN(light);
        double drop = 1 - b / a;
        _o.WriteLine($"no placard: {a / 1000:F2} kN -> {b / 1000:F2} kN, a {100 * drop:F1} % loss of modelled float strength");
        Assert.True(drop > 0.10, "this is the defect the placard fixes; it should still be visible without one");
        // W^(2/3): (451.142/589.67)^(2/3) = 0.836
        Assert.Equal(Math.Pow(451.142 / 589.67, 2.0 / 3.0), b / a, 2);
    }
}
