using System;
using System.Collections.Generic;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE ASYMMETRIC BROADSIDE-CONTROL COEFFICIENT (`spinAero.controlSpoilerKNeg`), 2026-11-11. Ty, having flown the
/// published build: <i>"beggs mueller passes easily inverted and barely upright probably 10 turns or so but it did
/// pass we should try to get beggs mueller which I will call BM TO PASS WITHIN 4 turns so bump the spoiler up a bit
/// upright but leave the inverted curve the same so it will be an assymetric spoiler k for positive and negative
/// aoa or deflection."</i>
///
/// These tests hold the three properties the change has to have, and they are the reason it is safe to ship:
///   1. NOT AUTHORED IS BIT-IDENTICAL. Every config in the fleet that does not carry the field produces exactly the
///      same forces and moments as before, at positive AND negative alpha. The field's default is −1, which
///      AeroModel reads as "use the positive value", resolved once per call.
///   2. IT SPLITS ON THE SIGN OF THE FLOW, not on anything else, and the split is real: with two different values
///      the upright and inverted elevator moments differ in the way the two coefficients differ.
///   3. IT DOES NOT REACH THE FIN. A vertical surface's sin(alpha_chord) sign is which SIDE the air comes from, so
///      splitting there would make a left spin behave differently from a right one. The rudder keeps one value.
/// </summary>
public class SpoilerAsymmetryTests
{
    private readonly ITestOutputHelper _o;
    public SpoilerAsymmetryTests(ITestOutputHelper o) { _o = o; }

    private static string ConfigDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent;
        return Path.Combine(d!.FullName, "configs", "aircraft");
    }

    /// <summary>A state with the tail well broadside, both senses, at a spin's rates — where the term is fully in.</summary>
    private static (Vec3 v, Vec3 w) Broadside(double alphaDeg) =>
        (new Vec3(26.0 * Math.Cos(alphaDeg * Math.PI / 180), 0, 26.0 * Math.Sin(alphaDeg * Math.PI / 180)),
         new Vec3(-1.0, 0.37, -3.7));

    /// <summary>
    /// EVERY CONFIG THAT DOES NOT CARRY THE FIELD IS UNCHANGED, and this walks all of them rather than arguing it.
    /// The comparison is against a copy whose ControlSpoilerKNeg is forced to the positive value, which is what the
    /// fallback is defined to mean — so if the fallback is ever wired differently this fails on the whole fleet at
    /// once instead of on whichever type someone happened to fly.
    /// </summary>
    [Fact]
    public void EveryConfigWithoutTheNewFieldIsBitIdenticalInBothSenses()
    {
        int n = 0, carried = 0;
        foreach (string path in Directory.GetFiles(ConfigDir(), "*.json"))
        {
            var cfg = AircraftConfigLoader.LoadFromFile(path);
            if (cfg.SpinAero is null || cfg.SpinAero.ControlSpoilerK <= 0.0) continue;
            if (cfg.SpinAero.ControlSpoilerKNeg >= 0.0) { carried++; continue; }   // authored on purpose; tested below
            var same = AircraftConfigLoader.LoadFromFile(path);
            same.SpinAero!.ControlSpoilerKNeg = same.SpinAero.ControlSpoilerK;
            var t1 = Aircraft.BuildAirfoilTables(cfg);
            var t2 = Aircraft.BuildAirfoilTables(same);
            double e = cfg.Controls.Elevator.MaxDeflRad, r = cfg.Controls.Rudder.MaxDeflRad;
            foreach (double aDeg in new[] { 74.0, -74.0, 40.0, -40.0, 5.0 })
            {
                var (v, w) = Broadside(aDeg);
                var d = new ControlDeflections(0, -e, r, 0);
                var a = AeroModel.Compute(cfg, t1, v, w, Vec3.Zero, 1.057, d);
                var b = AeroModel.Compute(same, t2, v, w, Vec3.Zero, 1.057, d);
                Assert.Equal(a.Force.X, b.Force.X, 9);
                Assert.Equal(a.Force.Y, b.Force.Y, 9);
                Assert.Equal(a.Force.Z, b.Force.Z, 9);
                Assert.Equal(a.Moment.X, b.Moment.X, 9);
                Assert.Equal(a.Moment.Y, b.Moment.Y, 9);
                Assert.Equal(a.Moment.Z, b.Moment.Z, 9);
            }
            n++;
        }
        _o.WriteLine($"{n} configs carry controlSpoilerK and not controlSpoilerKNeg, and all are unchanged;"
            + $" {carried} author the asymmetry deliberately");
        Assert.True(n >= 10, $"the fleet should still have the dozen-odd types on the symmetric default: found {n}");
    }

    /// <summary>
    /// THE SPLIT IS REAL AND IT IS ON THE SIGN OF THE FLOW. Same elevator deflection, same speed and rates, alpha
    /// +74 against −74: with two different coefficients the horizontal tail's pitching moment differs by the ratio
    /// the coefficients differ by, and with equal coefficients it is a mirror. The term is odd in nothing else here,
    /// so this isolates the gate.
    /// </summary>
    [Fact]
    public void TheCoefficientsSplitOnTheSignOfTheAeroplanesOwnAlpha()
    {
        var cfg = SpinRecoveryProceduresProbe.Load("pitts-s2b-like");
        cfg.SpinAero!.ControlSpoilerK = 1.2;
        cfg.SpinAero.ControlSpoilerKNeg = 0.6;
        var hi = SpinRecoveryProceduresProbe.Load("pitts-s2b-like"); hi.SpinAero!.ControlSpoilerK = 1.2; hi.SpinAero.ControlSpoilerKNeg = 1.2;
        var lo = SpinRecoveryProceduresProbe.Load("pitts-s2b-like"); lo.SpinAero!.ControlSpoilerK = 0.6; lo.SpinAero.ControlSpoilerKNeg = 0.6;
        var tS = Aircraft.BuildAirfoilTables(cfg); var tH = Aircraft.BuildAirfoilTables(hi); var tL = Aircraft.BuildAirfoilTables(lo);
        double e = cfg.Controls.Elevator.MaxDeflRad;
        double M(AircraftConfig c, IReadOnlyDictionary<string, AirfoilTable> t, double aDeg)
        {
            var (v, w) = Broadside(aDeg);
            return AeroModel.Compute(c, t, v, w, Vec3.Zero, 1.057, new ControlDeflections(0, -e, 0, 0)).Moment.Y;
        }
        double upSplit = M(cfg, tS, 74), upHi = M(hi, tH, 74), upLo = M(lo, tL, 74);
        double dnSplit = M(cfg, tS, -74), dnHi = M(hi, tH, -74), dnLo = M(lo, tL, -74);
        _o.WriteLine($"alpha +74: split {upSplit:F0}  k=1.2 both {upHi:F0}  k=0.6 both {upLo:F0}");
        _o.WriteLine($"alpha -74: split {dnSplit:F0}  k=1.2 both {dnHi:F0}  k=0.6 both {dnLo:F0}");
        Assert.Equal(upHi, upSplit, 6);     // positive alpha takes the positive coefficient
        Assert.Equal(dnLo, dnSplit, 6);     // negative alpha takes the negative one
        Assert.True(Math.Abs(upHi - upLo) > 1.0, "and the two coefficients must actually differ in effect at this attitude");
    }

    /// <summary>
    /// THE SPLIT ADDS NO LEFT/RIGHT ASYMMETRY TO THE RUDDER, which is the safety property. Had the gate been each
    /// strip's own alpha, a fin strip's alpha sign would be which SIDE the air comes from and a left spin and a
    /// right spin would land on different coefficients.
    ///
    /// NOTE WHAT THIS CANNOT ASSERT. The Pitts' rudder is NOT antisymmetric left to right in the first place and
    /// should not be: it carries `rig.finOffsetRad`, a real fin incidence, which is worth about 0.6 N.m of the
    /// 45 N.m here. So the test compares the asymmetry WITH two different coefficients against the asymmetry with
    /// them equal; the split must add nothing to it. (The first version of this test asserted antisymmetry outright
    /// and failed on the fin offset, which is authored geometry and not this change's doing.)
    /// </summary>
    [Fact]
    public void TheSplitAddsNoLeftRightAsymmetryToTheRudder()
    {
        var split = SpinRecoveryProceduresProbe.Load("pitts-s2b-like");
        split.SpinAero!.ControlSpoilerK = 1.6; split.SpinAero.ControlSpoilerKNeg = 0.2;
        var even = SpinRecoveryProceduresProbe.Load("pitts-s2b-like");
        even.SpinAero!.ControlSpoilerK = 1.6; even.SpinAero.ControlSpoilerKNeg = 1.6;
        var tS = Aircraft.BuildAirfoilTables(split); var tE = Aircraft.BuildAirfoilTables(even);
        double r = split.Controls.Rudder.MaxDeflRad;
        var (v, _) = Broadside(74);
        double N(AircraftConfig c, IReadOnlyDictionary<string, AirfoilTable> t, double rud)
            => AeroModel.Compute(c, t, v, Vec3.Zero, Vec3.Zero, 1.057, new ControlDeflections(0, 0, rud, 0)).Moment.Z;
        double asymSplit = N(split, tS, -r) + N(split, tS, r);
        double asymEven = N(even, tE, -r) + N(even, tE, r);
        _o.WriteLine($"left+right with the split {asymSplit:F4} N.m, with equal coefficients {asymEven:F4} N.m"
            + $" (the Pitts' authored fin offset is all of it)");
        Assert.Equal(asymEven, asymSplit, 9);
    }

    /// <summary>
    /// TY'S ACCEPTANCE TEST FOR TONIGHT, flown. 2026-11-11: <i>"we should try to get beggs mueller which I will
    /// call BM TO PASS WITHIN 4 turns"</i> and, from dinner, <i>"find a number that gets us bm recovery in under 4
    /// turns ideally 3 turns"</i>. BM is idle + genuinely FREE stick and ailerons + full opposite rudder, flown from
    /// the developed POWER-ON FLAT SPIN. At the authored controlSpoilerK 2.4 this measures <b>3.28 turns</b>, where
    /// the 0.6 it replaced measured 4.29.
    ///
    /// The bound is 4.0 because that is his number. The measured value has 0.7 turns of margin under it, which is
    /// the margin that matters: other lanes move this aeroplane and a pin that sits on the boundary fails for
    /// reasons that have nothing to do with the recovery.
    /// </summary>
    [Fact]
    public void BeggsMuellerRecoversThePittsUprightFlatSpinInsideFourTurns()
    {
        var cfg = SpinRecoveryProceduresProbe.Load("pitts-s2b-like");
        var (st, ac) = SpinBalanceProbe.Developed(cfg, -1.0, -1.0, aileron: 1.0);
        var o = SpinRecoveryProceduresProbe.Fly(cfg, ac,
            new SpinRecoveryProceduresProbe.Proc("", 1.0, 0, 0, 1.0, true, true), -1.0);
        _o.WriteLine($"upright flat spin alpha {st.Alpha:F1} (band {st.AlphaMin:F0}..{st.AlphaMax:F0}),"
            + $" Omega {st.OmegaVertDeg:F0} deg/s, {st.SecPerTurn:F2} s/turn"
            + $" -> BM {(double.IsNaN(o.StopSec) ? $"NO RECOVERY ({o.Turns20:F1} turns)" : $"{o.StopTurns:F2} turns, {o.StopSec:F1} s")}");
        Assert.False(double.IsNaN(o.StopSec), "BM must recover the upright flat spin at all");
        Assert.True(o.StopTurns < 4.0, $"Ty's target is under four turns: measured {o.StopTurns:F2}");
        // AND IT MUST STILL BE A FLAT SPIN. A fast recovery on an aeroplane that will not spin is a failure, not a
        // success: two values in this knob's history stop it spinning at all. His stated upright target is 78.
        Assert.InRange(st.Alpha, 74.0, 81.0);
        Assert.True(Math.Abs(st.OmegaVertDeg) > 180.0, $"and it must still be spinning: {st.OmegaVertDeg:F0} deg/s");
    }

    /// <summary>
    /// AND THE INVERTED SPIN DID NOT MOVE, which is the other half of what he asked for ("leave the inverted curve
    /// the same"). Flown from the developed INVERTED flat spin: 1.61 turns and -92.0 deg of alpha, measured
    /// identical at every positive-alpha coefficient from 0.6 to 2.0 — the negative-alpha half is still the 0.6 he
    /// accepted, so the only thing that reaches the inverted case at all is the +-3 deg alpha blend catching the
    /// ENTRY transient, which is worth half a degree of attitude.
    /// </summary>
    [Fact]
    public void TheInvertedFlatSpinAndItsRecoveryAreWhereTyLeftThem()
    {
        var cfg = SpinRecoveryProceduresProbe.Load("pitts-s2b-like");
        var (st, ac) = SpinBalanceProbe.Developed(cfg, 1.0, -1.0, inverted: true, aileron: 1.0);
        var o = SpinRecoveryProceduresProbe.Fly(cfg, ac,
            new SpinRecoveryProceduresProbe.Proc("", 1.0, 0, 0, 1.0, true, true), 1.0);
        _o.WriteLine($"inverted flat spin alpha {st.Alpha:F1}, Omega {st.OmegaVertDeg:F0} deg/s"
            + $" -> BM {(double.IsNaN(o.StopSec) ? $"NO RECOVERY ({o.Turns20:F1} turns)" : $"{o.StopTurns:F2} turns, {o.StopSec:F1} s")}");
        Assert.False(double.IsNaN(o.StopSec), "BM recovers the inverted spin and must keep doing so");
        Assert.InRange(o.StopTurns, 1.3, 2.0);        // 1.61 measured at every coefficient swept
        Assert.InRange(st.Alpha, -94.0, -89.0);       // -92.0 measured; his inverted target is 95
    }
}
