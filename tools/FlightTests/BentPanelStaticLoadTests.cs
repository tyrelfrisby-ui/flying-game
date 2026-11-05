using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty's answer to contact-patch decision 1 (2026-11-03): STATIC load counts. The aeroplane's own weight resting on a wing
/// panel is load like any other, and goes into the panel's plastic hinges (Ty 2026-11-05, <see cref="PlasticHinge"/>):
/// a panel weakened by an earlier strike bends further under the aeroplane lying on it, and one already bent nearly to
/// tearing goes. (Until 11-05 a bent panel snapped past 2 × its 1 g moment, a fifth of its ultimate; under that rule the
/// PA-18 lying on two tips bent 5° lost both.)
/// </summary>
[Collection("WorldTerrainActive")]
public class BentPanelStaticLoadTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public BentPanelStaticLoadTests(ITestOutputHelper o) { _o = o; WorldTerrain.Active = null; }
    public void Dispose() { WorldTerrain.Active = null; }

    private static AircraftConfig Load(string n) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", n + ".json"));

    /// <summary>The PA-18 upside down, pitched so its two wingtips and its fin top are level — lying on all three at once,
    /// not tipping from one onto the other — every hard point clear by <paramref name="gapM"/>: set down, not dropped.</summary>
    private Aircraft InvertedOnItsTips(AircraftConfig c, double gapM, double bendRad = 0)
    {
        Quat roll = new(1, 0, 0, 0);   // 180° about x
        // The hard points as they are: a bent panel's are where the bend put them.
        var probe = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -100), roll, Vec3.Zero, Vec3.Zero), ControlDeflections.Neutral);
        if (bendRad != 0) { probe.BendPanel(AirframeComponent.WingTipLeft, bendRad); probe.BendPanel(AirframeComponent.WingTipRight, bendRad); }
        var pts = probe.ContactPoints.Where(p => !p.PropDisc).ToList();
        Vec3 cg = c.Mass.CgVec();
        Quat best = roll; double bestErr = double.MaxValue;
        for (double th = -0.4; th <= 0.4; th += 0.0005)
        {
            Quat q = Quat.Multiply(roll, new Quat(0, Math.Sin(th / 2), 0, Math.Cos(th / 2)));
            double Up(Func<ContactPoint, bool> f) => pts.Where(f).Min(p => -q.Rotate(p.Body - cg).Z);
            double tip = Up(p => p.Component is AirframeComponent.WingTipLeft or AirframeComponent.WingTipRight);
            double fin = Up(p => p.Component == AirframeComponent.TailVertical);
            double rest = Up(p => p.Component is not (AirframeComponent.WingTipLeft or AirframeComponent.WingTipRight or AirframeComponent.TailVertical));
            if (rest < Math.Min(tip, fin)) continue;   // something else would be lowest
            double err = Math.Abs(tip - fin);
            if (err < bestErr) { bestErr = err; best = q; }
        }
        double lowest = pts.Min(p => -best.Rotate(p.Body - cg).Z);
        _o.WriteLine($"rest attitude found to {bestErr * 1000:0.0} mm between tips and fin");
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, lowest - gapM), best, Vec3.Zero, Vec3.Zero), ControlDeflections.Neutral);
        if (bendRad != 0)
        {
            Assert.True(ac.BendPanel(AirframeComponent.WingTipLeft, bendRad));
            Assert.True(ac.BendPanel(AirframeComponent.WingTipRight, bendRad));
        }
        return ac;
    }

    private void Settle(Aircraft ac, double s)
    {
        var sim = new SimLoop(ac);
        for (double t = 0; t < s; t += 0.05) sim.RunFor(0.05, new ControlInputs(0, 0, 0, 1.0));
        foreach (WingPanel p in ac.Panels)
            _o.WriteLine($"{p.Component}: {p.State}, bend {p.BendRad * 180 / Math.PI:0.0}°, peak contact {p.PeakContactNm:0} N·m = {p.PeakContactNm / p.M1Nm:0.00} × M1 {p.M1Nm:0} (limit {p.LimitNm:0}, ultimate {p.UltimateNm:0})");
        _o.WriteLine("lost: " + string.Join(", ", ac.LostComponents));
    }

    [Fact]
    public void ThePa18LyingOnTwoBentTipsBendsThemFurtherAndTheyHold()
    {
        var c = Load("pa18-cub-like");
        // Both tips bent 5° UP by upright wingtip strikes (a ground loop, a dropped wing), then over onto its back: inverted,
        // body "up" is toward the ground, so it lies on the two bent tips and its fin, set down level on all three.
        var ac = InvertedOnItsTips(c, 0.005, WingStructure.BentSetRad);
        Settle(ac, 3.0);
        // Its weight asks each station for more than its bent spars have left (aluminium past its 0.02 rad peak: 58 % of
        // the ultimate at 0.087 rad), so they fold further toward the ground — and the folding takes the weight off them
        // onto what is left (the buckled spar's 50 % residual), and they stay on.
        foreach (WingPanel p in ac.Panels.Where(p => p.Component is AirframeComponent.WingTipLeft or AirframeComponent.WingTipRight))
        {
            Assert.False(ac.IsLost(p.Component), $"{p.Component} held the weight on what was left of it");
            Assert.True(p.PeakContactNm > 2.0 * p.M1Nm);
            Assert.True(p.BendRad < WingStructure.BentSetRad - 3.0 * Math.PI / 180, $"{p.Component} was pushed back by more than 3°");
        }
    }

    [Fact]
    public void ThePa18LyingOnTipsBentNearlyToTearingLosesThem()
    {
        var c = Load("pa18-cub-like");
        // Bent 19° (0.33 rad: the tension flange tears at 0.36, MemberMaterials), it lies on them: they go.
        var ac = InvertedOnItsTips(c, 0.005, 19.0 * Math.PI / 180);
        Settle(ac, 3.0);
        Assert.True(ac.IsLost(AirframeComponent.WingTipLeft));
        Assert.True(ac.IsLost(AirframeComponent.WingTipRight));
    }

    [Fact]
    public void ThePa18SetDownGentlyOnItsIntactTipsKeepsThem()
    {
        var c = Load("pa18-cub-like");
        var ac = InvertedOnItsTips(c, 0.005);
        Settle(ac, 3.0);
        // The same static load on INTACT panels is below their ultimate: neither comes off (an intact panel is not held
        // to the 2 g rule; that is the damaged spar's).
        Assert.False(ac.IsLost(AirframeComponent.WingTipLeft));
        Assert.False(ac.IsLost(AirframeComponent.WingTipRight));
        Assert.All(ac.Panels.Where(p => p.Component is AirframeComponent.WingTipLeft or AirframeComponent.WingTipRight),
            p => Assert.True(p.PeakContactNm < p.UltimateNm));
        // The weight is past their yield, short of their ultimate: a set of a fraction of a degree, no crack.
        Assert.All(ac.Panels.Where(p => p.Component is AirframeComponent.WingTipLeft or AirframeComponent.WingTipRight),
            p => { Assert.True(Math.Abs(p.BendRad) < 0.5 * Math.PI / 180); Assert.False(p.Bending!.Damaged); });
    }
}
