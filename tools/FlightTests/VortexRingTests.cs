using System;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.Engine;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE PROPELLER FLOWN BACKWARDS. Ty 2026-11-11:
///
///   "Sometimes lower like a tail slide where the reversed direction works against the prop and can actually stop the
///    engine if rpm falls below around 300 rpm."
///
/// The model did the opposite: flown backwards at the idle stop the rpm ROSE. These are the assertions that hold the
/// fix; <see cref="VortexRingProbe"/> is the measurement it was built from.
/// </summary>
public class VortexRingTests
{
    private readonly ITestOutputHelper _o;
    public VortexRingTests(ITestOutputHelper o) { _o = o; }

    private static BladeElement.Geometry Blade(string id)
    {
        AircraftConfig c = VortexRingProbe.Load(id);
        PropulsionConfig p = c.Propulsion!;
        return new BladeElement.Geometry(p.PropBladeCount, 0.5 * p.PropDiameterM, PistonCompression.FinePitchDeg(p), p.ActivityFactor);
    }

    /// <summary>
    /// THE CURVE ITSELF: continuous at both seams, single-valued, and exactly momentum theory wherever momentum
    /// theory is valid. The seams are where a piecewise inflow model goes wrong, and Johnson's coefficients are
    /// chosen so that it does not — so this is a test of the arithmetic, not of a tolerance someone picked.
    /// </summary>
    [Fact]
    public void TheInducedVelocityCurveIsContinuousAndSingleValuedAcrossEveryWorkingState()
    {
        const double load = 400.0;                       // vh = 10 m/s, so vbar is vAxial/10
        double vh = 0.5 * Math.Sqrt(load);
        double Vi(double vbar) => BladeElement.InducedAxialMs(vbar * vh, load);

        // Hover is vh by definition.
        Assert.Equal(vh, Vi(0.0), 9);
        // The seam at vbar = -1: momentum theory's normal-working branch and Johnson's cubic meet, in value AND in
        // slope to the eye — the cubic is matched there, so stepping across it is unremarkable.
        Assert.Equal(Vi(-1.0 + 1e-9), Vi(-1.0 - 1e-9), 6);
        // The seam at vbar = -2: Johnson's cubic and momentum theory's windmill-brake branch meet at exactly vh.
        Assert.Equal(vh, Vi(-2.0), 9);

        // AND THE SLOPE THERE IS INFINITE, which is momentum theory's own and not this model's doing: the
        // windmill-brake branch carries sqrt(V^2/4 - vh^2), whose derivative is singular where its argument reaches
        // zero. So the curve is CONTINUOUS at vbar = -2 but not Lipschitz, and an infinitesimal step across the seam
        // moves vi like the square root of the step. Asserted as what it is, because a tolerance that pretended the
        // slope were finite would be a lie about the function the sim integrates.
        double prevGap = double.MaxValue, h = 1e-4;
        for (int i = 0; i < 11; i++, h *= 0.25)
        {
            double gap = Math.Abs(Vi(-2.0 + h) - Vi(-2.0 - h));
            // The law: quartering the step halves the gap, and the gap reaches no particular small number — it
            // reaches zero only in the limit, like a square root, so the law is the assertion and not a floor.
            // Expanded about the seam, the cubic gives 1 + 2.4814 h and the windmill branch 1 + 0.5 h - sqrt(h), so
            // the gap is vh (sqrt(h) + 1.9814 h) exactly — a square root with a linear correction, not a bare one.
            Assert.True(gap <= vh * (Math.Sqrt(h) + 2.0 * h), $"the gap {gap:E3} at h = {h:E1} is worse than the square root the branch predicts");
            if (i > 0) Assert.InRange(prevGap / gap, 1.9, 2.1);
            prevGap = gap;
        }
        _o.WriteLine($"the vbar = -2 seam: gap {prevGap:E3} m/s at a step of {h / 0.25:E1}, halving with each quartering of the step");

        // Single-valued and smooth enough to integrate: no step anywhere, and the total velocity through the disc
        // V + v rises monotonically with V, so no descent rate has two answers.
        double prevTotal = double.NegativeInfinity, prevVi = Vi(-8.0);
        for (double vbar = -8.0; vbar <= 8.0001; vbar += 0.002)
        {
            double v = Vi(vbar), total = vbar * vh + v;
            Assert.False(double.IsNaN(v) || double.IsInfinity(v), $"vbar {vbar:F3} gave {v}");
            Assert.True(total >= prevTotal - 1e-9, $"V+v fell from {prevTotal:F4} to {total:F4} at vbar {vbar:F3}: the curve is multi-valued there");
            Assert.True(Math.Abs(v - prevVi) < 0.5, $"a step of {Math.Abs(v - prevVi):F3} m/s in the induced velocity at vbar {vbar:F3}");
            prevTotal = total; prevVi = v;
        }

        // And it IS momentum theory where momentum theory is valid, to the digit.
        foreach (double vbar in new[] { 0.0, 0.5, 1.0, 3.0, 10.0 })
            Assert.Equal(vh * (-0.5 * vbar + Math.Sqrt(0.25 * vbar * vbar + 1.0)), Vi(vbar), 9);
        foreach (double vbar in new[] { -2.5, -4.0, -10.0 })
            Assert.Equal(vh * (-0.5 * vbar - Math.Sqrt(0.25 * vbar * vbar - 1.0)), Vi(vbar), 9);
    }

    /// <summary>
    /// FORWARD FLIGHT DOES NOT MOVE, and not by a tolerance: the new inflow returns the old expression term for term
    /// whenever the free stream goes through the disc the way momentum theory assumes. Asserted against the legacy
    /// seam so that it stays true if either side is ever edited.
    /// </summary>
    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("j3-cub-like")]
    [InlineData("great-lakes-like")]
    [InlineData("c172-like")]
    public void TheNewInflowIsTheOldOneExactlyWhereverTheAirGoesThroughTheDiscForwards(string id)
    {
        BladeElement.Geometry g = Blade(id);
        try
        {
            foreach (double rpm in new[] { 500.0, 700.0, 1500.0, 2300.0 })
                foreach (double v in new[] { 0.0, 2.0, 10.0, 25.0, 40.0, 60.0 })
                {
                    BladeElement.LegacyAxialInflow = false;
                    BladeElement.Result now = BladeElement.Solve(g, v, 0.0, rpm, 1.225);
                    BladeElement.LegacyAxialInflow = true;
                    BladeElement.Result was = BladeElement.Solve(g, v, 0.0, rpm, 1.225);
                    Assert.Equal(was.ThrustN, now.ThrustN, 9);
                    Assert.Equal(was.TorqueNm, now.TorqueNm, 9);
                }
        }
        finally { BladeElement.LegacyAxialInflow = false; }
    }

    /// <summary>
    /// THE DEFECT, IN ITS OWN TERMS. Flown backwards the propeller must ABSORB more torque, not less: the reversed
    /// stream works against it. Before the fix the Pitts' blade made 16.3 N.m at -20 m/s against 22.6 standing
    /// still — less torque the harder it was driven backwards — and its thrust went UP.
    /// </summary>
    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("j3-cub-like")]
    public void FlownBackwardsTheBladeAbsorbsMoreTorqueTheFasterItGoes(string id)
    {
        BladeElement.Geometry g = Blade(id);
        double still = BladeElement.Solve(g, 0.0, 0.0, 700.0, 1.225).TorqueNm;
        double last = double.NegativeInfinity;
        foreach (double v in new[] { -20.0, -25.0, -30.0, -40.0 })
        {
            double q = BladeElement.Solve(g, v, 0.0, 700.0, 1.225).TorqueNm;
            _o.WriteLine($"{id} at {v:F0} m/s: {q:F2} N.m (standing still {still:F2})");
            Assert.True(q > last, $"at {v:F0} m/s the blade absorbs {q:F2} N.m, no more than the {last:F2} it absorbed at a lower reversed speed");
            last = q;
        }
        Assert.True(last > still, $"{id} flown backwards at 40 m/s absorbs {last:F2} N.m, which is no more than the {still:F2} it absorbs standing still");
    }

    /// <summary>
    /// THE ACCEPTANCE CONDITION, FIRST HALF, in Ty's own words: "the reversed direction works against the prop".
    /// Flown backwards the rpm must FALL. Before the fix it rose — the Pitts from 700 to 805 rpm at -20 m/s and the
    /// J-3 from 700 to 942 — because the disc held a forward through-flow however hard the air was driven through it
    /// the other way. The run enters from a glide at 25 m/s with the throttle closed, so it starts from the
    /// windmilling rpm the aeroplane actually carries into the manoeuvre (1,528 on the Pitts, 1,272 on the J-3)
    /// rather than from a number chosen here.
    /// </summary>
    [Theory]
    [InlineData("pitts-s2b-like", -20.0)]
    [InlineData("pitts-s2b-like", -30.0)]
    [InlineData("j3-cub-like", -20.0)]
    [InlineData("j3-cub-like", -30.0)]
    public void FlownBackwardsTheRpmFalls(string id, double vAxialMs)
    {
        VortexRingProbe.Run r = VortexRingProbe.Tailslide(id, vAxialMs, 10.0);
        _o.WriteLine($"{id} at {vAxialMs:F0} m/s: {r.StartRpm:F0} rpm -> {r.EndRpm:F0} after 10 s");
        Assert.True(r.EndRpm < r.StartRpm - 50.0,
            $"{id} flown backwards at {-vAxialMs:F0} m/s went from {r.StartRpm:F0} rpm to {r.EndRpm:F0}: the reversed "
            + "air is not working against the propeller, which is the defect this lane exists for");
    }

    /// <summary>
    /// THE ACCEPTANCE CONDITION, SECOND HALF: "...and can actually stop the engine if rpm falls below around 300
    /// rpm." It must be able to reach the stop, not merely sag. The 300 rpm static-friction latch is NOT
    /// re-implemented here — it is <see cref="ImpulseCoupling.StaticFrictionStopRpm"/>, already built and already
    /// working — this asserts only that the propeller now drags the shaft down far enough to reach it.
    ///
    /// MEASURED: the J-3 gets there at 40 m/s backwards in 1.7 s. It is a severe tailslide and it should be: Ty says
    /// a tailslide "can ACTUALLY stop the engine", not that it always does, and at 20 and 30 m/s backwards both
    /// types sag and recover. The Pitts does not reach the stop at all at these speeds (see the probe's table); its
    /// governor drives the blade to the fine stop as the rpm falls, and a fine blade in reversed flow absorbs less.
    /// </summary>
    [Fact]
    public void ATailslideCanStopTheEngineOutright()
    {
        VortexRingProbe.Run r = VortexRingProbe.Tailslide("j3-cub-like", -40.0, 15.0);
        _o.WriteLine($"j3-cub-like at -40 m/s: {r.StartRpm:F0} rpm -> {r.EndRpm:F0}, " +
                     $"{(double.IsNaN(r.StopSeconds) ? "never stopped" : $"stopped at {r.StopSeconds:F2} s")}");
        Assert.False(double.IsNaN(r.StopSeconds),
            $"a 40 m/s tailslide took the J-3 from {r.StartRpm:F0} rpm to {r.EndRpm:F0} but never reached the "
            + $"{ImpulseCoupling.StaticFrictionStopRpm:F0} rpm stop");
        Assert.Equal(0.0, r.EndRpm, 6);
    }

    /// <summary>
    /// THE SECTION AT HIGH ANGLES, which is what decides the sign of the torque in reversed flow. An element is
    /// DRIVEN rather than dragged when its cl/cd beats cot|phi|, so a stalled section that still reports an L/D of
    /// ten turns the outer blade into a turbine. Broadside it must be a plate: no lift, and all the drag.
    /// </summary>
    [Fact]
    public void AStalledSectionBehavesLikeAPlateAndNotLikeAnAerofoil()
    {
        // THE BOUND IS DERIVED, NOT CHOSEN. An element is driven rather than dragged when cl/cd beats cot|phi|, and
        // the Pitts' blade at 0.77 R in a 20 m/s tailslide sits at phi = -14.6 deg — so an L/D above cot(14.6) = 3.83
        // at the angle of attack that condition puts on it, 25 deg, turns the outer blade into a turbine. That is
        // the number the old curve failed: it returned 10.
        double drivingLD = 1.0 / Math.Tan(14.6 * Math.PI / 180.0);
        double prev = double.MaxValue;
        foreach (double deg in new[] { 15.0, 20.0, 25.0, 35.0, 45.0, 60.0, 80.0 })
        {
            (double cl, double cd) = BladeElement.Section(deg * Math.PI / 180.0);
            double ld = cl / cd;
            _o.WriteLine($"alpha {deg,5:F1} deg: cl {cl:F3}, cd {cd:F3}, L/D {ld:F2}");
            Assert.True(ld < prev, $"the stalled section's L/D rose from {prev:F2} to {ld:F2} at {deg:F0} degrees");
            prev = ld;
            if (deg >= 25.0)
                Assert.True(ld < drivingLD,
                    $"at {deg:F0} degrees the section still has an L/D of {ld:F2}, above the {drivingLD:F2} at which a "
                    + "tailslide's outer blade stops being dragged and starts being driven");
        }
        (double cl90, double cd90) = BladeElement.Section(Math.PI / 2.0);
        Assert.True(Math.Abs(cl90) < 0.02, $"broadside the section still makes cl {cl90:F3}");
        Assert.True(cd90 > 1.1, $"broadside the section makes only cd {cd90:F3}");
        // And it is continuous at the stall: the linear branch is untouched and the plate branch meets it there.
        (double clA, double cdA) = BladeElement.Section(10.0 * Math.PI / 180.0);
        (double clB, double cdB) = BladeElement.Section(10.3 * Math.PI / 180.0);
        Assert.True(Math.Abs(clA - clB) < 0.02 && Math.Abs(cdA - cdB) < 0.01,
            $"the section steps at the stall: ({clA:F3},{cdA:F4}) to ({clB:F3},{cdB:F4})");
    }

}
