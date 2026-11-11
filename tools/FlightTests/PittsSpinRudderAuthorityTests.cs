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
/// THE RUDDER'S AUTHORITY IN THE DEVELOPED FLAT SPIN, pinned at a FIXED flight state so it costs no sim time.
///
/// The division of labour matters, so it is stated here rather than left to be rediscovered. The flat spin's own
/// attitude is pinned elsewhere (PittsFlatSpinAileronTests, PittsPowerSpinTargetTests): if the aeroplane stops
/// spinning flat, those fail, and they are the ones to look at. THIS file pins the CONTROL MODEL at the attitude
/// the aeroplane is known to reach — "given the aeroplane is in its flat spin, can the rudder get it out" — which
/// is a separate question and a separate failure.
///
/// The state below was measured by SpinRudderBudgetProbe (RUD_BUD=1) on lane/claude-spin-rudder-tdpf off tip
/// 89e74242, from SpinBalanceProbe.Developed(rudder -1, lever -1, aileron +1) — full power, pro-spin aileron,
/// 45 s, the last 20 averaged. That is the condition Ty's flat-spin reports are flown in, and it is NOT the
/// condition the older probes used: at idle with the ailerons neutral the same aeroplane sits at alpha 28 in a
/// steep spin, where the rudder has 139 deg/s^2 and every question here is uninteresting.
///
/// WHY THE OLD "0.2 deg/s^2" NUMBER IS NOT A BUG REPORT. It reproduces exactly, and this file reproduces it on
/// purpose: set ControlSpoilerK and FinSidewashK both to zero and the rudder reversal makes 4 N.m, which is
/// 0.2 deg/s^2 on Izz 1185. Those two terms landed on 2026-11-09 and 2026-11-10. The standing figure is the
/// model as it was BEFORE them, and the two tests below exist so that it cannot quietly become true again.
/// </summary>
public class PittsSpinRudderAuthorityTests
{
    private readonly ITestOutputHelper _o;
    public PittsSpinRudderAuthorityTests(ITestOutputHelper o) { _o = o; }

    private const double D = 180.0 / Math.PI;

    // The developed power-on flat spin, as measured (see the class comment): alpha 74.1 deg, beta -0.1, TAS 25.7 m/s,
    // body rates p/q/r -57/+21/-210 deg/s, q_inf 349 Pa (rho 1.057, about 1450 m — Developed() relifts between 600 and
    // 2500 m, so this is mid-band and not a sea-level figure).
    private const double Tas = 25.7, AlphaDeg = 74.1, Rho = 1.057;
    private static readonly Vec3 SpinRates = new(-57 / D, 21 / D, -210 / D);
    private static Vec3 SpinVelocity => new(Tas * Math.Cos(AlphaDeg / D), 0, Tas * Math.Sin(AlphaDeg / D));

    private static AircraftConfig Load()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        return AircraftConfigLoader.LoadFromFile(Path.Combine(d.FullName, "configs", "aircraft", "pitts-s2b-like.json"));
    }

    /// <summary>Yawing moment from reversing the rudder from full pro-spin to full anti-spin, at the fixed state.</summary>
    private static double Reversal(AircraftConfig cfg)
    {
        var tables = Aircraft.BuildAirfoilTables(cfg);
        double e = cfg.Controls.Elevator.MaxDeflRad, r = cfg.Controls.Rudder.MaxDeflRad;
        double a = cfg.Controls.Aileron.MaxDeflRad;
        double N(double rud) => AeroModel.Compute(cfg, tables, SpinVelocity, SpinRates, Vec3.Zero, Rho,
                                                  new ControlDeflections(a, -e, rud, 0)).Moment.Z;
        return Math.Abs(N(r) - N(-r));
    }

    /// <summary>
    /// The rudder has real authority in the flat spin: reversing it is worth at least 100 N.m, about 5 deg/s^2 on
    /// Izz 1185. Measured 154 N.m / 7.5 deg/s^2; the floor is set well below that because the point is to catch a
    /// COLLAPSE (the pre-fix model made 4 N.m), not to freeze the third digit while the aerodynamics are still moving.
    /// </summary>
    [Fact]
    public void TheRudderIsNotDeadInTheDevelopedFlatSpin()
    {
        var cfg = Load();
        double m = Reversal(cfg);
        _o.WriteLine($"rudder reversal at the developed flat spin: {m:F0} N.m -> {m / cfg.Mass.Inertia.Izz * D:F1} deg/s^2");
        Assert.True(m > 100.0, $"rudder reversal collapsed to {m:F0} N.m ({m / cfg.Mass.Inertia.Izz * D:F2} deg/s^2) — "
            + "the pre-2026-11-09 model made 4 N.m / 0.2 deg/s^2 here, which is what Ty reported as 'no recovery'");
    }

    /// <summary>
    /// And it has that authority BECAUSE of the two terms the owner flew and approved, not by accident. Switching
    /// off the base-pressure spoiler term (ControlSpoilerK, "That is it! Now it behaves like a Pitts really does")
    /// and the fuselage crossflow sidewash at the fin (FinSidewashK) together returns the model to 4 N.m. Each is
    /// load-bearing on its own: without the spoiler term 25 N.m, without sidewash 70 N.m.
    ///
    /// This is the test that would have made the standing "0.2 deg/s^2" figure impossible to quote as current.
    /// </summary>
    [Fact]
    public void BothHighAlphaControlTermsAreLoadBearing()
    {
        double full = Reversal(Load());
        var noSpoil = Load(); noSpoil.SpinAero!.ControlSpoilerK = 0;
        var noSide = Load(); noSide.SpinAero!.FinSidewashK = 0;
        var neither = Load(); neither.SpinAero!.ControlSpoilerK = 0; neither.SpinAero.FinSidewashK = 0;
        double s = Reversal(noSpoil), w = Reversal(noSide), n = Reversal(neither);
        _o.WriteLine($"full {full:F0} N.m | no spoiler {s:F0} | no sidewash {w:F0} | neither {n:F0} N.m"
            + $" ({n / 1185.0 * D:F2} deg/s^2 — the figure that was reported as the bug)");
        Assert.True(s < 0.5 * full, $"the spoiler term should carry most of the reversal; without it {s:F0} of {full:F0} N.m");
        Assert.True(w < 0.8 * full, $"sidewash should matter; without it {w:F0} of {full:F0} N.m");
        Assert.True(n < 0.1 * full, $"with both off the model should collapse to the pre-fix figure; got {n:F0} of {full:F0} N.m");
    }

    /// <summary>
    /// THE RECOVERY IS AILERON-LIMITED, NOT RUDDER-LIMITED — which is the whole finding, and it is why Ty's report
    /// and the measured rudder authority can both be true at once.
    ///
    /// Flown from the developed flat spin (SpinRudderBudgetProbe, RUD_BUD=1), full opposite rudder:
    ///   stick aft, ailerons neutral   recovered 1.97 turns
    ///   stick neutral (Beggs)         recovered 2.13 turns
    ///   stick aft, AILERON HELD       DID NOT RECOVER in 20 s (10.3 turns, still 225 deg/s)
    ///   Beggs, AILERON HELD           4.50 turns  (recovers, but fails the 2-turn standard)
    ///   2/3 rudder, 1/3 pro aileron   DID NOT RECOVER in 20 s (12.6 turns) — NASA TP 1009's "criterion spin"
    ///
    /// So the rudder is not dead: take the aileron out and the aeroplane meets NACA TN 1329's 2-turn standard.
    /// Hold the aileron in — which is how the spin was entered, and is what a pilot who has not consciously
    /// centred the stick is doing — and full opposite rudder does not get it out. This is the measurable form of
    /// Beggs-Mueller's insistence on letting go of the stick, and it is consistent with the flat-spin aileron
    /// behaviour already pinned in PittsFlatSpinAileronTests (out-spin aileron flattens the spin and speeds it up,
    /// per NACA TR-379 / TN 1329 for fuselage-loaded aeroplanes — and the Pitts is extremely fuselage-loaded).
    ///
    /// This test holds the static form of it: at the developed flat spin the aileron's own yawing moment is a
    /// first-order term against everything the rudder can do. If that ever stops being true, the flown table above
    /// has moved and this comment is stale — re-fly the probe rather than trusting it.
    /// </summary>
    [Fact]
    public void TheAileronsProSpinYawRivalsTheWholeRudder()
    {
        var cfg = Load();
        var tables = Aircraft.BuildAirfoilTables(cfg);
        double e = cfg.Controls.Elevator.MaxDeflRad, r = cfg.Controls.Rudder.MaxDeflRad, a = cfg.Controls.Aileron.MaxDeflRad;
        // Anti-spin rudder throughout; only the aileron moves, from held to neutral.
        double N(double ail) => AeroModel.Compute(cfg, tables, SpinVelocity, SpinRates, Vec3.Zero, Rho,
                                                  new ControlDeflections(ail, -e, r, 0)).Moment.Z;
        double aileronWorth = Math.Abs(N(a) - N(0)), rudderWorth = Reversal(cfg);
        _o.WriteLine($"at the developed flat spin: taking the aileron out is worth {aileronWorth:F0} N.m of yaw;"
            + $" reversing the whole rudder is worth {rudderWorth:F0} N.m  (ratio {aileronWorth / rudderWorth:F2})");
        Assert.True(aileronWorth > 0.4 * rudderWorth,
            $"the aileron should be a first-order term in the spin's yaw balance: {aileronWorth:F0} N.m against the "
            + $"rudder's {rudderWorth:F0} N.m — if this has collapsed, the recovery table in the comment above is "
            + "describing a model that no longer exists");
    }

    /// <summary>
    /// WHICH TAIL IS BLANKETED, and it is not the one the standing note says. The figure quoted against the rudder
    /// — "eta_t at the tail is 1.3-1.5, MORE dynamic pressure than free stream" — is the HORIZONTAL tail's, and it
    /// is true of the horizontal tail: it is swung broadside on the rotation and sees more than free stream. The FIN
    /// is in the body's wake and sees about a quarter of it. Those are different surfaces and the rudder's budget
    /// was being argued from the stabiliser's number.
    ///
    /// Diagnostic, not a pin on the third digit: it asserts only the ORDERING, that the fin is substantially worse
    /// off than the stabiliser, which is the structural fact that was misread.
    /// </summary>
    [Fact]
    public void TheFinIsBlanketedInTheFlatSpinEvenThoughTheStabiliserIsNot()
    {
        var cfg = Load();
        var tables = Aircraft.BuildAirfoilTables(cfg);
        double e = cfg.Controls.Elevator.MaxDeflRad, a = cfg.Controls.Aileron.MaxDeflRad;
        var cap = new List<ForceSample>(512);
        ForceDebug.FlowDiagnostics = true; ForceDebug.Samples = cap;
        try
        {
            AeroModel.Compute(cfg, tables, SpinVelocity, SpinRates, Vec3.Zero, Rho,
                              new ControlDeflections(a, -e, -cfg.Controls.Rudder.MaxDeflRad, 0));
        }
        finally { ForceDebug.Samples = null; ForceDebug.FlowDiagnostics = false; }

        double finSum = 0, stabSum = 0; int nf = 0, ns = 0;
        var seen = new HashSet<string>();
        foreach (var f in cap)
        {
            if (f.Kind != "tailq") continue;
            if (!seen.Add($"{f.MomentBody.X:0}/{f.PosBody.X:0.00}/{f.PosBody.Y:0.00}/{f.PosBody.Z:0.00}")) continue;
            if (f.MomentBody.X > 0.5) { finSum += f.ForceBody.Z; nf++; } else { stabSum += f.ForceBody.Z; ns++; }
        }
        double fin = nf > 0 ? finSum / nf : 0, stab = ns > 0 ? stabSum / ns : 0;
        _o.WriteLine($"mean eta_t: fin {fin:F2} ({nf} strips), horizontal tail {stab:F2} ({ns} strips)");
        Assert.True(nf > 0 && ns > 0, "no tail-q samples captured");
        Assert.True(fin < 0.7 * stab, $"the fin should be the blanketed one: fin {fin:F2} against stabiliser {stab:F2}");
    }
}
