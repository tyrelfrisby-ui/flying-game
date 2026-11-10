using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Wreck;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// A BAILED-OUT PILOT TUMBLES ON THE AIR'S MOMENT AND STABILISES ON THE CANOPY'S PULL (Ty 2026-11-10: "same for the
/// pilot when bailing out real forces leading to tumbles and flutter etc").
///
/// He was a point mass with an angular velocity set by hand; he is now six boxes with their own CG, inertia and air
/// forces (<see cref="JumperTumble"/>). His FALL SPEED is unchanged — <see cref="SkydiverAero"/>'s published terminal
/// speeds are what the drag areas are scaled to — so everything measured here is about his attitude.
/// </summary>
public class JumperTumbleTests
{
    private readonly ITestOutputHelper _o;
    public JumperTumbleTests(ITestOutputHelper o) { _o = o; }

    public const double Rho = 1.225;
    /// <summary>Over the side of a Pitts at 100 kt.</summary>
    public const double ExitMs = 51.4;

    /// <summary>His own geometry, printed once: it is the thing everything below comes out of.</summary>
    [Fact]
    public void HisBodyIsSevenBoxesWithRealMassProperties()
    {
        var j = new JumperTumble(Quat.Identity, Vec3.Zero);
        double m = 0;
        foreach (DebrisAero.Segment g in j.Segments) m += g.MassKg;
        _o.WriteLine($"  {j.Segments.Length} segments, {m:F1} kg, CG {j.CgOffsetZ:F3} m below mid-trunk");
        _o.WriteLine($"  I = ({j.Inertia.X:F2}, {j.Inertia.Y:F2}, {j.Inertia.Z:F2}) kg.m2");
        _o.WriteLine($"  Cd.A scale to hit SkydiverAero's belly 120 mph: {j.CdBellyScale:F3}");
        Assert.Equal(JumperTumble.MassKg, m, 6);
        // A man is long and thin: hardest to turn about his own long axis, easiest end over end.
        Assert.True(j.Inertia.Z < j.Inertia.X && j.Inertia.Z < j.Inertia.Y, $"I = {j.Inertia}");
        // And he is NOT fore-and-aft symmetric: the rig is on his back, so his CG sits behind his spine.
        Assert.True(j.CgOffsetX < -0.01, $"CG is {j.CgOffsetX:F3} m fore-and-aft — the rig is supposed to pull it aft");
        // And the scale had better be of order one, or the boxes are the wrong size.
        Assert.InRange(j.CdBellyScale, 0.3, 3.0);
    }

    /// <summary>
    /// THE BELLY-ON DRAG AREA IS STILL THE PUBLISHED ONE. Whatever the boxes say on their own, the force on him
    /// belly to earth must be the one that falls at 120 mph, because that is the number Ty's model is built on.
    /// </summary>
    [Fact]
    public void HisDragAreaStillHitsThePublishedTerminalSpeeds()
    {
        var j = new JumperTumble(Quat.Identity, Vec3.Zero);
        foreach (double pos in new[] { 0.0, 0.5, 1.0 })
        {
            double v = SkydiverAero.TerminalSpeed(pos, JumperTumble.MassKg, Rho);
            // Belly to earth: the flow is along his body x. At terminal the drag must equal his weight.
            var st = new RigidBodyState(Vec3.Zero, Quat.Identity, new Vec3(v, 0, 0), Vec3.Zero);
            (Vec3 f, Vec3 _) = DebrisAero.Forces(j.Segments, st, Rho, Vec3.Zero, j.CdScale(pos));
            double w = JumperTumble.MassKg * WaterPhysics.G;
            _o.WriteLine($"  body pos {pos:F1}: terminal {v:F1} m/s ({v * 2.23694:F0} mph), drag {-f.X:F0} N, weight {w:F0} N");
            Assert.InRange(-f.X / w, 0.97, 1.03);
        }
    }

    /// <summary>
    /// WHICH WAY HE MEETS THE AIR DECIDES HOW HARD HE TUMBLES, and that falls out of his shape with nothing told to
    /// it. Released with his rates EXACTLY ZERO at 100 kt, three ways:
    ///   BELLY ON   the flow on his chest, the attitude a jumper holds. The gentlest of the three by a clear margin.
    ///   SIDE ON    the flow on his flank, which is how he actually leaves — over the right side of the aeroplane
    ///              while it flies on. His narrow side is to the wind and his long axis across it, and he goes over
    ///              in all three axes at once (the gyroscopic coupling of a body with Izz about a quarter of Ixx).
    ///   FEET FIRST the other one a man cannot hold, and the one an ejection seat hands him.
    /// Nothing in the old model could tell these apart: it spun him at whatever rate he was constructed with.
    ///
    /// HE IS NOT TRIMMED BELLY-ON, and that is stated rather than fitted away. His frontal-area centroid sits about
    /// 48 mm from his CG — his legs are dense for their area, his rig and arms are area-rich for their mass — so
    /// belly to earth carries a real 46 N·m at 100 kt and he pitches off it at 4 rad/s². A real skydiver trims
    /// exactly that out with his arms and legs, which is what "arch" and "de-arch" MEAN and the one control a
    /// rigid seven-box body does not have. So what is asserted here is the ordering, which is the physical claim:
    /// belly on is the least unstable attitude he has. Giving him a limb-trim would be the next real step, not a fix.
    /// </summary>
    [Theory]
    [InlineData("belly on", 0, 0, 0)]
    [InlineData("side on", 0, 1, 0)]
    [InlineData("feet first", 0, 0, 1)]
    public void WhichWayHeMeetsTheAirDecidesHowHardHeTumbles(string what, double ax, double ay, double az)
    {
        // Rotate him so the wind (world +x) lands on the body axis named: the attitude is his, the wind is fixed.
        Quat q = ay > 0 ? new Quat(0, 0, System.Math.Sin(System.Math.PI / 4), System.Math.Cos(System.Math.PI / 4))
               : az > 0 ? new Quat(0, System.Math.Sin(-System.Math.PI / 4), 0, System.Math.Cos(System.Math.PI / 4))
               : Quat.Identity;
        var j = new JumperTumble(q, Vec3.Zero);
        var vel = new Vec3(ExitMs, 0, 0);
        double dt = 0.005, turned = 0, peak = 0;
        Assert.Equal(0.0, j.RatesBody.Length, 12);
        for (int i = 0; i < 600; i++)
        {
            j.Step(dt, vel, Vec3.Zero, Rho, 0.0, Vec3.Zero, 0.0);
            turned += j.RatesBody.Length * dt;
            peak = System.Math.Max(peak, j.RatesBody.Length);
            if (i % 150 == 0)
                _o.WriteLine($"  {what,-11} t {i * dt:F2}s  |w| {j.RatesBody.Length,5:F2} rad/s  "
                             + $"(p {j.RatesBody.X,6:F2} q {j.RatesBody.Y,6:F2} r {j.RatesBody.Z,6:F2})  air moment {j.LastAirMomentNm.Length,7:F1} N.m");
        }
        _o.WriteLine($"  {what,-11} => turned {turned / (2 * System.Math.PI):F2} rev in 3 s, peak |w| {peak:F2} rad/s");
        // Every attitude must be driven by the air and by nothing else: he started at rest.
        Assert.True(peak > 1.0, $"{what}: the air moved him only to {peak:F2} rad/s");
        Assert.True(turned > 2.0, $"{what}: he turned {turned:F2} rad in 3 s");
        // Belly on is the gentlest: that ordering is the claim.
        Store[what] = peak;
        if (Store.Count == 3)
            Assert.True(Store["belly on"] < Store["side on"] && Store["belly on"] < Store["feet first"],
                $"belly on {Store["belly on"]:F2}, side on {Store["side on"]:F2}, feet first {Store["feet first"]:F2} rad/s");
    }

    /// <summary>The three cases of the theory above are separate runs; this collects their peaks so the ordering can
    /// be asserted once all three are in. xUnit runs a theory's cases on one instance of the class.</summary>
    private static readonly Dictionary<string, double> Store = new();

    /// <summary>
    /// AND HE TUMBLES HARDER THE FASTER HE LEFT. The moment is the air's, so it goes as the square of his exit
    /// speed — over the side of a Cub at 40 kt he rolls over slowly; out of a Pitts at 150 kt it takes him. Nothing
    /// in the old model could tell the two apart.
    /// </summary>
    [Fact]
    public void TheTumbleGrowsWithTheExitSpeed()
    {
        double Peak(double ms)
        {
            var j = new JumperTumble(Quat.Identity, Vec3.Zero);
            double peak = 0;
            for (int i = 0; i < 200; i++) { j.Step(0.005, new Vec3(ms, 0, 0), Vec3.Zero, Rho, 0.0, Vec3.Zero, 0.0); peak = System.Math.Max(peak, j.RatesBody.Length); }
            return peak;
        }
        double slow = Peak(20.6), fast = Peak(77.2);          // 40 kt and 150 kt
        _o.WriteLine($"  40 kt: peak {slow:F2} rad/s in 1 s;  150 kt: peak {fast:F2} rad/s");
        Assert.True(fast > 3.0 * slow, $"150 kt gave {fast:F2} and 40 kt {slow:F2} — the air is supposed to decide this");
    }

    /// <summary>
    /// THE CANOPY STOPS THE TUMBLE AS IT TAKES THE LOAD, which is the whole of the stabilising: the risers meet him
    /// 0.37 m up his own long axis, so their tension is a moment on a lever, and it grows with the inflation. Set
    /// tumbling at 6 rad/s side-on and then given a tension, the swing he is left with — measured as how far off the
    /// lines he averages over the last two seconds — collapses as the tension rises. With no canopy he goes on
    /// tumbling. There is no flag and no slerp anywhere in it: one force, one lever, read out at four tensions.
    ///
    /// WHY "full" IS HIS WEIGHT: at a steady descent the risers carry exactly mg, because nothing is accelerating.
    /// PilotBody computes the tension the honest way round — the canopy's own drag, ½ρ·CdS·v² on the air it is
    /// actually meeting — and at terminal that IS mg, so the number used here is the one the bridge arrives at.
    ///
    /// He is NOT left perfectly still under load, and should not be: a restoring moment with nothing to damp it is a
    /// pendulum, ω = √(T·r/I) = 6.1 rad/s at full load, and a parachutist under a fresh canopy does swing. The swing
    /// of the HARNESS — the pilot under the canopy on 7 m of line — is PilotBody's own damped pendulum and a
    /// different thing from this, which is his body about its own CG.
    /// </summary>
    [Fact]
    public void TheCanopyStopsTheTumbleAsItTakesTheLoad()
    {
        var hang = new Vec3(0, 0, -1);                        // the canopy is straight up (world z is down)
        double full = JumperTumble.MassKg * WaterPhysics.G;
        var sideOn = new Quat(0, 0, System.Math.Sin(System.Math.PI / 4), System.Math.Cos(System.Math.PI / 4));
        (double worstOff, double meanRate) Fall(double tensionN)
        {
            var j = new JumperTumble(sideOn, new Vec3(0, 6.0, 0));
            double worst = 0, rate = 0; int n = 0;
            for (int i = 0; i < 1200; i++)
            {
                j.Step(0.005, new Vec3(0, 0, SkydiverAero.BellyTerminalMs), Vec3.Zero, Rho, tensionN, hang, 0.0);
                if (i >= 800) { worst += j.HangAlignment(hang); rate += j.RatesBody.Length; n++; }
            }
            return (worst / n, rate / n);
        }
        var rows = new List<(string, double, double)>();
        foreach ((string name, double t) in new[] { ("no canopy", 0.0), ("bag off", 0.05 * full), ("half full", 0.5 * full), ("full", full) })
        {
            (double off, double rate) = Fall(t);
            double deg = System.Math.Acos(System.Math.Clamp(off, -1, 1)) * 57.2957795;
            _o.WriteLine($"  {name,-10} tension {t,6:F0} N: mean {deg,5:F0} deg off the lines over the last 2 s, mean |w| {rate:F2} rad/s");
            rows.Add((name, deg, rate));
        }
        double OffOf(string n) { foreach (var r in rows) if (r.Item1 == n) return r.Item2; return double.NaN; }
        double RateOf(string n) { foreach (var r in rows) if (r.Item1 == n) return r.Item3; return double.NaN; }
        // More tension, less wander: monotone, and the full canopy holds him near the lines.
        Assert.True(OffOf("full") < OffOf("half full"), $"full {OffOf("full"):F0} deg vs half {OffOf("half full"):F0}");
        Assert.True(OffOf("half full") < OffOf("bag off"), $"half {OffOf("half full"):F0} deg vs bag off {OffOf("bag off"):F0}");
        Assert.True(OffOf("bag off") < OffOf("no canopy"), $"bag off {OffOf("bag off"):F0} deg vs none {OffOf("no canopy"):F0}");
        Assert.True(OffOf("full") < 35.0, $"under a full canopy he averages {OffOf("full"):F0} deg off the lines");
        // And with no canopy at all he is still going round.
        Assert.True(RateOf("no canopy") > 1.0, $"with no canopy he settled to {RateOf("no canopy"):F2} rad/s");
    }

    /// <summary>The harness moment IS a force on a lever, and nothing else: zero tension, zero moment.</summary>
    [Fact]
    public void TheHarnessMomentIsTensionTimesTheLever()
    {
        var j = new JumperTumble(new Quat(0, System.Math.Sin(0.25), 0, System.Math.Cos(0.25)).Normalized(), Vec3.Zero);
        var hang = new Vec3(0, 0, -1);
        j.Step(1e-6, Vec3.Zero, Vec3.Zero, Rho, 1000.0, hang, 0.0);
        double got = j.LastHarnessMomentNm.Length;
        double want = 1000.0 * JumperTumble.HarnessAboveCgM * System.Math.Sin(0.5);   // T · r · sin(angle to the lines)
        _o.WriteLine($"  tilted {0.5 * 57.2957795:F0} deg to the lines at 1000 N: harness moment {got:F2} N.m, T.r.sin {want:F2}");
        Assert.InRange(got, 0.99 * want, 1.01 * want);
        j.Step(1e-6, Vec3.Zero, Vec3.Zero, Rho, 0.0, hang, 0.0);
        Assert.Equal(0.0, j.LastHarnessMomentNm.Length, 12);
    }

    /// <summary>Still air does nothing to him, whatever attitude he is in: no moment appears out of the geometry.</summary>
    [Fact]
    public void StillAirDoesNotTurnHim()
    {
        var j = new JumperTumble(new Quat(0.2, 0.3, 0.4, 0.84).Normalized(), Vec3.Zero);
        j.Step(0.005, Vec3.Zero, Vec3.Zero, Rho, 0.0, Vec3.Zero, 0.0);
        Assert.Equal(0.0, j.LastAirMomentNm.Length, 12);
        Assert.Equal(0.0, j.RatesBody.Length, 12);
    }
}
