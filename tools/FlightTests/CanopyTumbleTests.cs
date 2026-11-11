using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Vr;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// A JETTISONED CANOPY SKIDS AND TUMBLES ON ITS OWN FORCES (Ty 2026-11-10: "Make sure departing wings and canopies
/// use real aerodynamic[s] for movement … real forces leading to tumbles and flutter etc").
///
/// <see cref="CanopyFreeBody"/> held its rotation: the rate it left with plus a 3 rad/s pitch-over, for ever, so a
/// canopy that left at 40 kt and one that left at 150 kt tumbled identically and neither ever stopped. Its three
/// drag areas are unchanged — broadside Cd 1.2 on the plan, edge-on 0.4 on the arch, and now 0.4 on the side
/// projection too — but they go through <see cref="FlyingGame.Core.Wreck.DebrisAero"/>, so the force acts at a
/// centre of pressure that moves with the angle and the rotation falls out of the moment.
/// </summary>
public class CanopyTumbleTests
{
    private readonly ITestOutputHelper _o;
    public CanopyTumbleTests(ITestOutputHelper o) { _o = o; }

    private static readonly Vec3 Level1G = new(0, -9.80665, 0);   // Unity-ish: y up
    private static CanopyDynamics Extra() => new(CanopySpec.For("extra-300-like"));
    private static CanopyDynamics Pitts() => new(CanopySpec.For("pitts-s2b-like"));

    /// <summary>Crown up, flying forward along +z, at <paramref name="ms"/>.</summary>
    private static CanopyFreeBody Release(CanopyDynamics c, double ms, Vec3 spin) =>
        new(c, new Vec3(0, 1000, 0), new Vec3(0, 0, ms), new Vec3(0, 1, 0), new Vec3(0, 0, 1), spin);

    /// <summary>
    /// THE SHELL'S INERTIA IS INTEGRATED, NOT GUESSED. The surface integral in
    /// <see cref="CanopyFreeBody.ShellInertia"/> must reproduce the closed form for the one shape that has one: a
    /// thin hemispherical shell is ⅔mR² about any diameter through the sphere's centre, and ⅔mR² about its axis too.
    /// </summary>
    [Fact]
    public void TheShellInertiaIntegralMatchesAHemisphere()
    {
        const double r = 0.5, m = 10.0;
        // comHeight 0 keeps it about the sphere's centre, where the closed form is written.
        Vec3 i = CanopyFreeBody.ShellInertia(r, r, r, m, 0.0);
        double want = 2.0 / 3.0 * m * r * r;
        _o.WriteLine($"  hemisphere R {r} m, {m} kg: I = ({i.X:F5}, {i.Y:F5}, {i.Z:F5}) kg.m2, closed form {want:F5}");
        Assert.InRange(i.X, 0.99 * want, 1.01 * want);
        Assert.InRange(i.Y, 0.99 * want, 1.01 * want);
        Assert.InRange(i.Z, 0.99 * want, 1.01 * want);
    }

    /// <summary>The real canopies' inertia, printed: it is what the tumble rates below come out of.</summary>
    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("extra-300-like")]
    public void TheRealCanopiesHaveRealMassProperties(string id)
    {
        var b = Release(new CanopyDynamics(CanopySpec.For(id)), 0, Vec3.Zero);
        _o.WriteLine($"  {id,-16} {b.MassKg:F1} kg, plan {b.PlanAreaM2:F3} m2, frontal {b.FrontalAreaM2:F3}, side {b.SideAreaM2:F3}");
        _o.WriteLine($"  {id,-16} I = ({b.Inertia.X:F4}, {b.Inertia.Y:F4}, {b.Inertia.Z:F4}) kg.m2; "
                     + $"Cd.A per axis ({b.AxisDragAreaM2.X:F3}, {b.AxisDragAreaM2.Y:F3}, {b.AxisDragAreaM2.Z:F3}) m2");
        // A canopy is a flattish shell: hardest to turn about its vertical, easiest about its long axis.
        Assert.True(b.Inertia.Z > b.Inertia.X, $"I = {b.Inertia}");
        Assert.True(b.Inertia.X > 0 && b.Inertia.Y > 0);
    }

    /// <summary>
    /// THE DRAG AREAS ARE UNTOUCHED. This is what keeps the canopy's path and the tail check the shell's own: the
    /// broadside and edge-on figures are exactly what they were (the numbers VrBailoutTests pins), and the per-axis
    /// set DebrisAero is given agrees with them.
    /// </summary>
    [Fact]
    public void TheMeasuredDragAreasAreUnchanged()
    {
        var flat = new CanopyFreeBody(Extra(), Vec3.Zero, Vec3.Zero, new Vec3(0, 1, 0), new Vec3(0, 0, 1), Vec3.Zero);
        Assert.Equal(0.11762, flat.DragAreaM2(new Vec3(0, 0, 1)), 4);   // edge-on, as before
        Assert.Equal(1.55848, flat.DragAreaM2(new Vec3(0, 1, 0)), 4);   // broadside, as before
        _o.WriteLine($"  edge-on {flat.DragAreaM2(new Vec3(0, 0, 1)):F5} m2, broadside {flat.DragAreaM2(new Vec3(0, 1, 0)):F5} m2");
        Assert.Equal(flat.DragAreaM2(new Vec3(0, 0, 1)), flat.AxisDragAreaM2.X, 6);
        Assert.Equal(flat.DragAreaM2(new Vec3(0, 1, 0)), flat.AxisDragAreaM2.Z, 6);
    }

    /// <summary>
    /// IT TUMBLES FROM NOTHING. Released crown-up and flying forward with its rate EXACTLY ZERO — no pitch-over
    /// handed to it — the air turns it: the shell's normal force acts a quarter of its length ahead of its centroid,
    /// and its centroid is not its centre of mass. Under the old model this was a dead straight glide for ever.
    /// </summary>
    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("extra-300-like")]
    public void ACanopyReleasedWithNoSpinIsTurnedByTheAir(string id)
    {
        var b = Release(new CanopyDynamics(CanopySpec.For(id)), 41.16, Vec3.Zero);   // 80 KIAS
        Assert.Equal(0.0, b.Spin.Length, 12);
        double turned = 0, peak = 0;
        for (int i = 0; i < 600; i++)
        {
            b.Step(0.005, 1.225, Vec3.Zero, Level1G);
            turned += b.Spin.Length * 0.005;
            peak = Math.Max(peak, b.Spin.Length);
            if (i % 100 == 0)
                _o.WriteLine($"  {id,-16} t {i * 0.005:F2}s  |w| {b.Spin.Length,6:F2} rad/s  moment {b.LastMomentNm.Length,6:F1} N.m  "
                             + $"|v| {b.Velocity.Length,5:F1} m/s");
        }
        _o.WriteLine($"  {id,-16} => turned {turned / (2 * Math.PI):F2} rev in 3 s, peak |w| {peak:F2} rad/s");
        Assert.True(peak > 1.0, $"{id}: the air only got it to {peak:F2} rad/s");
        Assert.True(turned > 1.0, $"{id}: it turned {turned:F2} rad in 3 s");
    }

    /// <summary>
    /// AND HARDER THE FASTER IT LEFT, which is the thing the held spin could not do. The moment is the air's, so it
    /// goes as the square of the speed: off a Cub at 40 kt it rolls over lazily, off a Pitts at 150 kt it is thrown.
    /// </summary>
    [Fact]
    public void TheTumbleGrowsWithTheSpeedItLeftAt()
    {
        double Peak(double ms)
        {
            var b = Release(Pitts(), ms, Vec3.Zero);
            double p = 0;
            for (int i = 0; i < 200; i++) { b.Step(0.005, 1.225, Vec3.Zero, Level1G); p = Math.Max(p, b.Spin.Length); }
            return p;
        }
        double slow = Peak(20.6), fast = Peak(77.2);
        _o.WriteLine($"  40 kt: peak {slow:F2} rad/s in 1 s;  150 kt: peak {fast:F2} rad/s");
        Assert.True(fast > 3.0 * slow, $"150 kt gave {fast:F2}, 40 kt {slow:F2} — the air is supposed to decide this");
    }

    /// <summary>
    /// THE DEPARTURE RATE IS NO LONGER HELD. Given the <see cref="CanopyFreeBody.PitchOverRadS"/> the hinge swing
    /// hands it, the shell's own air changes that rate instead of carrying it unchanged to the ground — which is what
    /// "held" meant and what made the old canopy a spinning prop rather than a tumbling shell.
    /// </summary>
    [Fact]
    public void TheDepartureRateIsWorkedOnByTheAirInsteadOfHeld()
    {
        var b = Release(Extra(), 41.16, new Vec3(CanopyFreeBody.PitchOverRadS, 0, 0));
        double start = b.Spin.Length;
        var rates = new List<double>();
        for (int i = 0; i < 600; i++) { b.Step(0.005, 1.225, Vec3.Zero, Level1G); if (i % 100 == 0) rates.Add(b.Spin.Length); }
        _o.WriteLine($"  left at {start:F2} rad/s, then " + string.Join(", ", rates.ConvertAll(r => r.ToString("F2"))));
        Assert.Equal(CanopyFreeBody.PitchOverRadS, start, 9);
        double most = 0;
        foreach (double r in rates) most = Math.Max(most, Math.Abs(r - start));
        Assert.True(most > 0.3 * start, $"the air moved its rate by only {most:F2} rad/s off {start:F2} — that is 'held'");
    }

    /// <summary>Still air turns it and pushes it nowhere, whatever attitude it is in.</summary>
    [Fact]
    public void StillAirDoesNothingToIt()
    {
        var b = new CanopyFreeBody(Extra(), Vec3.Zero, Vec3.Zero, new Vec3(0, 1, 0), new Vec3(0, 0, 1), Vec3.Zero);
        b.Step(0.005, 1.225, Vec3.Zero, Vec3.Zero);
        Assert.Equal(0.0, b.LastMomentNm.Length, 12);
        Assert.Equal(0.0, b.Spin.Length, 12);
        Assert.Equal(0.0, b.Velocity.Length, 12);
    }

    /// <summary>
    /// IT STILL FALLS AND STILL DRAGS, which is what VrBailoutTests has always asked of it: let go at 80 KIAS level
    /// it loses its speed fast and goes down. Kept here as well because the force model moved underneath it.
    /// </summary>
    [Fact]
    public void ItStillLosesItsSpeedAndFalls()
    {
        var b = Release(Extra(), 41.16, new Vec3(3, 0, 0));
        b.Step(1.0, 1.225, Vec3.Zero, Level1G);
        _o.WriteLine($"  after 1 s: forward {b.Velocity.Z:F1} m/s, at {b.Position.Y:F1} m, sink {-b.Velocity.Y:F1} m/s");
        Assert.True(b.Velocity.Z < 30.0, $"still {b.Velocity.Z:F1} m/s forward");
        Assert.True(b.Velocity.Z > 0.0);
        b.Step(2.0, 1.225, Vec3.Zero, Level1G);
        _o.WriteLine($"  after 3 s: at {b.Position.Y:F1} m, sink {-b.Velocity.Y:F1} m/s, |w| {b.Spin.Length:F2} rad/s");
        Assert.True(b.Position.Y < 995.0, $"at {b.Position.Y:F1} m");
        Assert.True(b.Velocity.Y < -5.0);
    }

    // ---- torn off by the air, and hitting the aeroplane on the way past ------------------------------------------

    /// <summary>
    /// THE AIR TEARS IT OFF ABOVE A SPEED, AND THAT WAS ALREADY MODELLED — I did not add a threshold, and there was
    /// no honest one to add. Ty 2026-11-10: "it gets 'torn off' by the air if the speed is high enough". The existing
    /// <see cref="CanopyDynamics"/> already does it, from Ty's OWN earlier rule: unlatched in flight the suction
    /// flings the canopy open, it arrives at its stop with ∫(M_a + M_g) dβ of kinetic energy, and the peak stop load
    /// M_s + √(M_s² + 2KE) over the stop lever is what the fittings have to carry. His rule "the canopy departs above
    /// 80 KIAS (Extra) and 80 mph (Pitts) when flung open, and stays on below" is what SET
    /// <see cref="CanopySpec.FittingStrengthN"/> (Extra 15,100 N, Pitts 11,500 N, from
    /// <see cref="CanopyDynamics.PeakStopLoadN"/> running that same dynamics). So the threshold is derived from the
    /// loads and the geometry exactly as asked, and it is measured here rather than written down again.
    /// </summary>
    [Theory]
    [InlineData("extra-300-like", 41.2)]      // 80 KIAS
    [InlineData("pitts-s2b-like", 35.8)]      // 80 mph
    public void TheAirTearsItOffAboveItsOwnSpeedAndNotBelow(string id, double thresholdMs)
    {
        bool Departs(double ms)
        {
            var c = new CanopyDynamics(CanopySpec.For(id));
            double q = 0.5 * 1.225 * ms * ms;
            var gApp = new Vec3(0, -9.80665, 0);
            // UNLATCHED IN FLIGHT, pins IN: the suction flings it open and the stop has to catch it. That is the
            // tear-off, and it is the condition Ty's rule was written for. Pulling the pins instead is the JETTISON
            // path, which goes at any flying speed above DepartMinQ (the q of 20 kt) and is a different thing.
            for (int i = 0; i < 2000 && !c.Departed; i++) c.Step(0.002, q, gApp, false, Vec3.Zero, Vec3.Zero);
            return c.Departed;
        }
        bool below = Departs(0.80 * thresholdMs), above = Departs(1.25 * thresholdMs);
        _o.WriteLine($"  {id,-16} threshold {thresholdMs:F1} m/s ({thresholdMs * 1.9438445:F0} kt): "
                     + $"at {0.80 * thresholdMs:F1} m/s departs {below}, at {1.25 * thresholdMs:F1} m/s departs {above}");
        _o.WriteLine($"  {id,-16} fittings {CanopySpec.For(id).FittingStrengthN:F0} N, peak stop load at the threshold "
                     + $"{CanopyDynamics.PeakStopLoadN(CanopySpec.For(id), 0.5 * 1.225 * thresholdMs * thresholdMs):F0} N");
        Assert.True(above, $"{id} did not depart well above its threshold");
        Assert.False(below, $"{id} departed well below its threshold");
    }

    /// <summary>A sphere of boxes the canopy can be made to fly into, for the strike test.</summary>
    private sealed class Wall : ICanopyObstacle
    {
        public readonly double AtZ;
        public Wall(double atZ) { AtZ = atZ; }
        public string? Hit(Vec3 at, double radiusM) => at.Z + radiusM >= AtZ ? "HStab" : null;
        /// <summary>A wall bolted to the frame the shell is flown in: it does not move, so the shell's own velocity
        /// IS the closing speed.</summary>
        public Vec3 VelocityAt(Vec3 at) => Vec3.Zero;
    }

    /// <summary>
    /// IT HITS THE TAIL AND THE GLASS BREAKS — once, with the speed and the surface recorded, which is what the
    /// mechanical impact sound and the visual shatter need. Flown straight into a surface 6 m ahead.
    /// </summary>
    [Fact]
    public void HittingTheAeroplaneShattersItOnceAndRecordsTheImpact()
    {
        var b = Release(Extra(), 41.16, new Vec3(3, 0, 0));
        var wall = new Wall(6.0);
        Assert.False(b.Shattered);
        for (int i = 0; i < 400; i++) b.Step(0.005, 1.225, Vec3.Zero, Level1G, wall, null);
        _o.WriteLine($"  struck {b.StruckWhat} at {b.StrikeAtSec:F3} s, {b.StrikeSpeedMs:F1} m/s, spinning {b.StrikeSpinRadS:F2} rad/s, "
                     + $"at z {b.StrikeAt.Z:F2} m");
        Assert.True(b.Shattered);
        Assert.Equal("HStab", b.StruckWhat);
        Assert.True(b.StrikeSpeedMs > 5.0, $"it crept into the tail at {b.StrikeSpeedMs:F1} m/s");
        Assert.True(double.IsFinite(b.StrikeAtSec));
        // Once only: the glass does not break twice.
        double at = b.StrikeAtSec, v = b.StrikeSpeedMs;
        for (int i = 0; i < 200; i++) b.Step(0.005, 1.225, Vec3.Zero, Level1G, wall, null);
        Assert.Equal(at, b.StrikeAtSec, 9);
        Assert.Equal(v, b.StrikeSpeedMs, 9);
    }

    /// <summary>A surface it is already beside when it leaves is not a strike: that is the Pitts' top wing over the
    /// front cockpit, and the tail check has always ignored it.</summary>
    [Fact]
    public void TheSurfaceItLeavesBesideIsNotAStrike()
    {
        var b = Release(Extra(), 0.0, Vec3.Zero);
        var wall = new Wall(b.Position.Z);                   // already inside it at release
        string? started = wall.Hit(b.Position, b.RadiusM);
        Assert.Equal("HStab", started);
        for (int i = 0; i < 50; i++) b.Step(0.005, 1.225, Vec3.Zero, Level1G, wall, started);
        _o.WriteLine($"  released already touching {started}: shattered {b.Shattered}");
        Assert.False(b.Shattered);
    }

    /// <summary>Nothing to hit, nothing happens — and a canopy that never meets the aeroplane never shatters.</summary>
    [Fact]
    public void WithNothingToHitItNeverShatters()
    {
        var b = Release(Extra(), 41.16, new Vec3(3, 0, 0));
        for (int i = 0; i < 600; i++) b.Step(0.005, 1.225, Vec3.Zero, Level1G, null, null);
        Assert.False(b.Shattered);
        Assert.Null(b.StruckWhat);
        Assert.True(double.IsNaN(b.StrikeAtSec));
    }
}
