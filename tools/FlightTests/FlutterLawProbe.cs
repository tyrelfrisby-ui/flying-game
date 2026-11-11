using System;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE TWO SHAPES TY HAS NOT SPECIFIED, printed so he can move them (FLUTTER=1).
///
/// His flutter spec fixes points, not curves. The damage rate is fixed at ONE speed — "the equivalent exposure at
/// 1.35 vd at 1 minute equals failure on the weaker of the two" — and the strength is fixed at BOTH ENDS — the
/// certificated ultimate at no damage, "an ultimate strength of 1g" at a full fatigue life. The curve through the
/// first and the line between the second two are mine, and this is the deliverable that makes them visible rather
/// than buried in a constant.
/// </summary>
public class FlutterLawProbe
{
    private readonly ITestOutputHelper _o;
    public FlutterLawProbe(ITestOutputHelper o) { _o = o; }

    private static AircraftConfig Load(string n) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", n + ".json"));

    private static string Time(double s) =>
        double.IsPositiveInfinity(s) ? "never"
        : s >= 3600.0 ? $"{s / 3600.0:F1} h"
        : s >= 60.0 ? $"{s / 60.0:F1} min"
        : $"{s:F0} s";

    /// <summary>TABLE ONE: how long you have, at a steady speed, before a full fatigue life is used up.</summary>
    [Fact]
    public void TimeToFailureAcrossTheSpeedRange()
    {
        if (Environment.GetEnvironmentVariable("FLUTTER") != "1") return;
        _o.WriteLine("TIME TO A FULL FATIGUE LIFE at a steady speed. The rate is m^k / 60 s with");
        _o.WriteLine("m = (v/Vd - 1.20) / (1.35 - 1.20), so m = 1 at Ty's calibration point by construction.");
        _o.WriteLine("k is TY'S CHOICE from this table: shipping k = 1; k = 2 shown for comparison.");
        _o.WriteLine("The band that OPERATES is 1.20 to 1.30 Vd - he moved the ceiling down to 1.30 and the");
        _o.WriteLine("self-initiation to 1.275, so 1.35 is now a DEFINING point for the slope, not a flyable one.\n");
        _o.WriteLine("  v/Vd    margin m      k = 1 (shipping)        k = 2");
        foreach (double v in new[] { 1.19, 1.20, 1.21, 1.25, 1.275, 1.29, 1.30, 1.35 })
        {
            double m = Flutter.Margin(v);
            double t2 = m > 0 ? Flutter.CalibrationSeconds / Math.Pow(m, 1.0) : double.PositiveInfinity;
            double t3 = m > 0 ? Flutter.CalibrationSeconds / Math.Pow(m, 2.0) : double.PositiveInfinity;
            string note = v < Flutter.ArmVd ? "  (below onset: nothing arms)"
                        : v >= Flutter.CeilingVd ? "  (at/above the ceiling: immediate, regardless)"
                        : v >= Flutter.FallbackInitiateVd ? "  (self-initiates here)" : "  (armed; waits for a control)";
            _o.WriteLine($"  {v,5:F2}  {m,9:F3}  {Time(t2),20}  {Time(t3),12}{note}");
        }
        _o.WriteLine($"\nShipping exponent is {Flutter.RateExponent:F0}. At 1.25 Vd that is {Time(Flutter.SecondsToFailure(1.25))}, " +
                     $"which is Ty's \"a long exposure at 1.25 vd stil produces a failure\".");
        // The calibration has to come out exactly, or the shape has been fitted to the wrong thing.
        Assert.Equal(Flutter.CalibrationSeconds, Flutter.SecondsToFailure(Flutter.CalibrationVd), 6);
    }

    /// <summary>TABLE TWO: what the fatigue does to the structure it is in, as the g it will break at.</summary>
    [Fact]
    public void UltimateStrengthAcrossTheFatigueRange()
    {
        if (Environment.GetEnvironmentVariable("FLUTTER") != "1") return;
        _o.WriteLine("ULTIMATE STRENGTH AGAINST FATIGUE INDEX, as the load factor the structure breaks at.");
        _o.WriteLine("Index 0 and index 1 are TY's (certificated ultimate, and 1 g). The shape between is MINE:");
        _o.WriteLine("linear in the breaking load, which is also linear in OverstressMember.Strength.");
        _o.WriteLine("Geometric shown for comparison — it would keep a lightly fluttered wing nearly as good as new.\n");
        foreach (string id in new[] { "pitts-s2b-like", "extra-300-like", "great-lakes-like", "j3-cub-like", "c172-like" })
        {
            AircraftConfig c = Load(id);
            double limitG = c.Limits?.GMax ?? 0.0;
            double breakRatio = OverstressMember.BreakRatio;
            if (limitG <= 0.0) continue;
            _o.WriteLine($"  {id}  limit {limitG:F1} g, ultimate {breakRatio * limitG:F1} g");
            var lin = new System.Text.StringBuilder("     linear     ");
            var geo = new System.Text.StringBuilder("     geometric  ");
            foreach (double i in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
            {
                double atOne = 1.0 / (breakRatio * limitG);
                lin.Append($"  i={i:F2}: {Flutter.BreakingGForIndex(i, limitG):F2} g");
                geo.Append($"  i={i:F2}: {breakRatio * limitG * Math.Pow(atOne, i):F2} g");
            }
            _o.WriteLine(lin.ToString());
            _o.WriteLine(geo.ToString());
            // Both ends are Ty's and must be exact whichever shape is chosen.
            Assert.Equal(breakRatio * limitG, Flutter.BreakingGForIndex(0.0, limitG), 6);
            Assert.Equal(1.0, Flutter.BreakingGForIndex(1.0, limitG), 6);
        }
        _o.WriteLine("\nWHAT THE SHAPE BUYS: a partly fluttered wing is a flyable aeroplane that must be flown gently.");
        _o.WriteLine("Pull more than the row says and it comes off NOW; fly level and it holds. The failure speed is");
        _o.WriteLine("no longer a property of the flutter alone - it depends on how hard the pilot is flying.");
    }

    /// <summary>And the signature: what the pilot hears and feels as it runs away.</summary>
    [Fact]
    public void TheFlutterSignature()
    {
        if (Environment.GetEnvironmentVariable("FLUTTER") != "1") return;
        _o.WriteLine("FREQUENCY AND AMPLITUDE against fatigue index. Ty: \"increases in amplitude and decreases in");
        _o.WriteLine("frequency\". The two endpoint frequencies are AUTHORED, not derived - there is no structural");
        _o.WriteLine("dynamics model here to derive them from, and anything built from area or chord alone would be a");
        _o.WriteLine("derivation in appearance only.\n");
        foreach (double i in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
            _o.WriteLine($"  index {i:F2}:  {Flutter.FrequencyHz(i),5:F1} Hz   {Flutter.AmplitudeG(i),5:F2} g at the pilot");
        Assert.True(Flutter.FrequencyHz(1.0) < Flutter.FrequencyHz(0.0), "frequency must FALL");
        Assert.True(Flutter.AmplitudeG(1.0) > Flutter.AmplitudeG(0.0), "amplitude must RISE");
    }

    /// <summary>Diagnostic: what the aeroplane actually reports, so a failing bound is a measurement not a guess.</summary>
    [Fact]
    public void WhatTheAeroplaneReports()
    {
        if (Environment.GetEnvironmentVariable("FLUTTER") != "1") return;
        AircraftConfig c = Load("pitts-s2b-like");
        foreach (double over in new[] { 1.19, 1.25, 1.35, 1.52 })
        {
            double v = over * Flutter.VdOverVne * (c.Limits?.VneMs ?? 100.0);
            var ac = new FlyingGame.Sim.Aircraft(c,
                new FlyingGame.Core.RigidBodyState(new FlyingGame.Core.MathTypes.Vec3(0, 0, -5.0),
                    FlyingGame.Core.MathTypes.Quat.Identity, new FlyingGame.Core.MathTypes.Vec3(v, 0, 0),
                    FlyingGame.Core.MathTypes.Vec3.Zero), FlyingGame.Core.Aero.ControlDeflections.Neutral);
            _o.WriteLine($"  asked {over:F2} Vd ({v:F1} m/s TAS)  ->  SpeedOverVd {ac.SpeedOverVd:F4}");
        }
        var a2 = new FlyingGame.Sim.Aircraft(c,
            new FlyingGame.Core.RigidBodyState(new FlyingGame.Core.MathTypes.Vec3(0, 0, -5.0),
                FlyingGame.Core.MathTypes.Quat.Identity, new FlyingGame.Core.MathTypes.Vec3(60, 0, 0),
                FlyingGame.Core.MathTypes.Vec3.Zero), FlyingGame.Core.Aero.ControlDeflections.Neutral);
        double ratio1g = a2.Structure.WingRatioAt(1.0, a2.WingWeightRatio);
        _o.WriteLine($"\n  limit {a2.Structure.LimitPosG:F2} g, wingWeightRatio {a2.WingWeightRatio:F4}, " +
                     $"wing ratio at 1 g {ratio1g:F4}, strength at 1 g {Flutter.StrengthAtOneG(ratio1g):F5}");
        a2.SetFlutterForTesting(FlyingGame.Core.AirframeComponent.AileronLeft, 5.0);
        double s = a2.Structure.LeftWing.Member.Strength;
        _o.WriteLine($"  after a full life: Strength {s:F5}; ratio at 1 g / Strength = {ratio1g / s:F3} " +
                     $"(breaks past {FlyingGame.Core.OverstressMember.BreakRatio:F1})");
        for (int i = 0; i < 400 && !a2.Structure.WingsFailed; i++) a2.HoldLoadFactor(1.05, 1.05, 1.0 / 200.0);
        _o.WriteLine($"  held at 1.05 g: wingsFailed {a2.Structure.WingsFailed}, " +
                     $"effective {a2.Structure.LeftWing.Member.EffectiveRatio:F3}, broken {a2.Structure.LeftWing.Member.Broken}");
    }

    /// <summary>What the speeds actually are in knots, per type, since every one of them hangs off Vd = 1.1 x Vne.</summary>
    [Fact]
    public void TheFlutterSpeedsInKnots()
    {
        if (Environment.GetEnvironmentVariable("FLUTTER") != "1") return;
        _o.WriteLine("THE SPEEDS, CAS. Vd is NOT authored anywhere in this project: it is 1.1 x Vne by Ty's own");
        _o.WriteLine("2026-11-09 rule, the same convention the constant-speed coarse stops were derived at. A 10 %");
        _o.WriteLine("error in that convention moves every number below.\n");
        _o.WriteLine("  type                   Vne     Vd   arm 1.20  init 1.35  ceiling 1.50");
        foreach (string id in new[] { "pitts-s2b-like", "extra-300-like", "great-lakes-like", "j3-cub-like", "c172-like" })
        {
            AircraftConfig c = Load(id);
            double vne = (c.Limits?.VneMs ?? 0.0) * 1.94384;
            if (vne <= 0.0) continue;
            double vd = Flutter.VdOverVne * vne;
            _o.WriteLine($"  {id,-22}{vne,4:F0}   {vd,4:F0}     {Flutter.ArmVd * vd,4:F0} kt    {Flutter.FallbackInitiateVd * vd,4:F0} kt     {Flutter.CeilingVd * vd,4:F0} kt");
        }
    }
}
