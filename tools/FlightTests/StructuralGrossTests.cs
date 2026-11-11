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
        double spread = Math.Abs(b - a) / a;
        _o.WriteLine($"placard {placard:F1} kg. ultimate water load: authored at the placard {a / 1000:F3} kN, authored as flown {b / 1000:F3} kN ({spread:P2} apart)");
        Assert.True(a > 0, "the type should have a float ultimate load at all");
        // NOT BIT-EQUAL, AND IT USED TO BE FOR THE WRONG REASON. This asserted Equal(a, b, 3) — the same number to a
        // thousandth of a NEWTON — and got it, because Aircraft's V_S0 cache was keyed by type id alone and these two
        // copies share an id: whichever reached the dictionary first handed the other its stall speed, so of course
        // they agreed. The key now carries the weights, each copy solves its own trim, and what is left is
        // PracticeScenario.EstimateVso's 0.5 m/s scan grid: the two land on neighbouring rungs (19.55 m/s against
        // 17.10 scaled up by √(589.67/451.142)) and n_w squares that, which is the 0.5 % below. Without the scaling
        // at all the gap would be the whole 23.5 % of the mass, so the window is well inside what it is proving.
        Assert.True(spread < 0.02, $"the float's ultimate moved {spread:P2} ({a / 1000:F3} -> {b / 1000:F3} kN) when only today's loading changed");
    }

    /// <summary>
    /// And the defect it replaces: with NO placard the design weight falls back to the authored mass, so the float
    /// really does weaken — which is what every float type did before a certificate was authored for it.
    ///
    /// IT WEAKENS AS W^(5/3), NOT W^(2/3), and this test used to say W^(2/3) because of an accident. The ultimate is
    /// 1.5·n_w·W with n_w = C1·V_S0²/(tan^(2/3)β·W^(1/3)), so it goes as V_S0²·W^(2/3) — and when the whole aeroplane
    /// is lighter, V_S0 falls too. V_S ∝ √W, so V_S0² carries another full factor of W and the product is W^(5/3):
    /// (451.142/589.67)^(5/3) = 0.640, measured 0.637. The W^(2/3) this test asserted, and the "16.4 %" in
    /// AircraftOverstress.StructuralGrossKg's own summary, both came from holding V_S0 still while the weight moved.
    /// It LOOKED right because Aircraft's V_S0 cache was keyed by type id alone, so the two copies below — same id,
    /// different authored mass — were handed one another's stall speed and the V_S0² factor cancelled itself out.
    /// The key now carries the weights (Aircraft.InitFloatWaterLoads) and the two copies get their own answers.
    ///
    /// So the defect the placard fixes is 36 % of the float's modelled strength on this type, not 16 %.
    /// </summary>
    [Fact]
    public void WithoutAPlacardItStillFallsBackToTheAuthoredMass()
    {
        var heavy = Floats(); heavy.Loading!.MaxTakeoffMassKg = 0; heavy.Loading.MaxGrossMassKg = 0; heavy.Mass.MassKg = 589.67;
        var light = Floats(); light.Loading!.MaxTakeoffMassKg = 0; light.Loading.MaxGrossMassKg = 0; light.Mass.MassKg = 451.142;
        double a = UltimateN(heavy), b = UltimateN(light);
        double drop = 1 - b / a;
        _o.WriteLine($"no placard: {a / 1000:F2} kN -> {b / 1000:F2} kN, a {100 * drop:F1} % loss of modelled float strength "
                     + $"(W^(5/3) = {Math.Pow(451.142 / 589.67, 5.0 / 3.0):F3}, measured {b / a:F3})");
        Assert.True(drop > 0.10, "this is the defect the placard fixes; it should still be visible without one");
        // V_S0²·W^(2/3) with V_S0 ∝ √W: (451.142/589.67)^(5/3) = 0.640
        Assert.Equal(Math.Pow(451.142 / 589.67, 5.0 / 3.0), b / a, 2);
    }
}
