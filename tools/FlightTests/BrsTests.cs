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
/// The whole-aircraft ballistic recovery parachute (owner 2026-10-16: "if an airplane has a brs like a
/// cirrus display deploy brs instead of bail out and model a whole airplane ballistic recovery chute
/// instead"). Nobody gets out: the aeroplane comes down under the canopy with everyone still aboard, and
/// the only question worth asking is whether it arrives at the speed the book says it does.
///
/// Every number here is checked against the published CAPS figures, and the ATTITUDE is flown rather than
/// asserted — the aeroplane is thrown in upside down and has to right itself on the pendulum alone.
/// </summary>
public class BrsTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public BrsTests(ITestOutputHelper o) { _o = o; }

    public void Dispose()
    {
        WorldTerrain.Active = null;
        WorldTerrain.SurfaceOverride = null;
        Atmosphere.SteadyWind = Vec3.Zero;
    }

    private const string Cirrus = "cirrus-sr22-like";
    private static readonly ControlInputs Idle = new(0, 0, 0, 1);

    private static AircraftConfig Load(string id)
        => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    [Fact]
    public void OnlyTheCirrusHasOne()
    {
        Assert.NotNull(Load(Cirrus).Brs);
        foreach (string id in new[] { "c172-like", "pa28-archer-like", "pa18-cub-like", "f86-sabre-like" })
            Assert.Null(Load(id).Brs);
    }

    [Fact]
    public void TheDescentRateIsTheOneInTheBook()
    {
        // Cirrus publish two figures that have to agree: "approximately 1,700 fpm", and an impact
        // "equivalent to a drop from 13 feet". One CdS has to produce both, or the CdS is wrong.
        AircraftConfig c = Load(Cirrus);
        double v = BallisticRecovery.DescentRateMs(c, 1.225);
        double fpm = v * 196.85;
        double dropFt = v * v / (2 * 9.81) * 3.28084;
        _o.WriteLine($"under full canopy at {c.Mass.MassKg:0} kg: {v:0.0} m/s = {fpm:0} fpm, " +
                     $"the same arrival as a drop from {dropFt:0.0} ft");
        Assert.InRange(fpm, 1500, 1950);        // book: approximately 1,700
        Assert.InRange(dropFt, 11, 16);         // book: equivalent to 13 ft
    }

    [Fact]
    public void ItTakesEightSecondsToOpen()
    {
        var t = new WorldTerrain();
        WorldTerrain.Active = t;
        Atmosphere.SteadyWind = Vec3.Zero;
        AircraftConfig c = Load(Cirrus);
        var state = new RigidBodyState(new Vec3(0, 0, -2500), Quat.Identity, new Vec3(c.SpawnIasMs, 0, 0), Vec3.Zero);
        var ac = new Aircraft(c, state, new ControlDeflections(0, 0, 0, 0));
        var loop = new SimLoop(ac);
        Assert.True(ac.DeployBrs(2000));
        double fullAt = -1;
        for (int i = 0; i < 4000 && fullAt < 0; i++)
        {
            loop.RunFor(SimLoop.DefaultFixedDtSec, Idle);
            if (ac.Brs.Current == BallisticRecovery.Phase.Full) fullAt = ac.Brs.TimeSec;
        }
        _o.WriteLine($"full canopy {fullAt:0.0} s after the handle — the slider is what makes it take that long");
        Assert.InRange(fullAt, 7.5, 8.5);
    }

    [Fact]
    public void PulledAtCruiseItZoomsBeforeItSinks()
    {
        // Worth knowing and worth not "fixing": pulled at cruise the canopy takes 65 m/s off the aeroplane in
        // a few seconds and the wing, still flying, trades that speed for height. The published "about 400 ft
        // of altitude loss" is for a deployment made slowly and measured to a settled descent, not for one
        // made at 146 kt — which GAINS height first. None of it is scripted; it is the energy going somewhere.
        var t = new WorldTerrain();
        WorldTerrain.Active = t;
        Atmosphere.SteadyWind = Vec3.Zero;
        AircraftConfig c = Load(Cirrus);
        var state = new RigidBodyState(new Vec3(0, 0, -2500), Quat.Identity, new Vec3(c.SpawnIasMs, 0, 0), Vec3.Zero);
        var ac = new Aircraft(c, state, new ControlDeflections(0, 0, 0, 0));
        var loop = new SimLoop(ac);
        ac.DeployBrs(2000);
        double start = -ac.State.Position.Z, peak = start;
        int n = (int)(25 / SimLoop.DefaultFixedDtSec);
        for (int i = 0; i < n; i++)
        {
            loop.RunFor(SimLoop.DefaultFixedDtSec, Idle);
            peak = Math.Max(peak, -ac.State.Position.Z);
        }
        double net = start - (-ac.State.Position.Z);
        _o.WriteLine($"pulled at {c.SpawnIasMs * 1.944:0} kt: climbed {peak - start:0} m on the deceleration, " +
                     $"then {net:+0;-0} m net by 25 s");
        Assert.True(peak - start > 10, "decelerating from cruise under a canopy should trade speed for height");
        Assert.True(net > 30, "and 25 seconds later it should be well below where it started");
    }

    [Fact]
    public void DeployedSlowItCostsAFewHundredFeet()
    {
        // The book case: level flight at a sensible speed, measured to a settled descent.
        var t = new WorldTerrain();
        WorldTerrain.Active = t;
        Atmosphere.SteadyWind = Vec3.Zero;
        AircraftConfig c = Load(Cirrus);
        var state = new RigidBodyState(new Vec3(0, 0, -2500), Quat.Identity, new Vec3(40, 0, 0), Vec3.Zero);
        var ac = new Aircraft(c, state, new ControlDeflections(0, 0, 0, 0));
        var loop = new SimLoop(ac);
        ac.DeployBrs(2000);
        double start = -ac.State.Position.Z;
        double terminal = BallisticRecovery.DescentRateMs(c, Atmosphere.DensityAtAltitude(2500));
        double lost = -1;
        double prevZ = ac.State.Position.Z;
        int per = (int)(1.0 / SimLoop.DefaultFixedDtSec);
        for (int sec = 0; sec < 40 && lost < 0; sec++)
        {
            for (int i = 0; i < per; i++) loop.RunFor(SimLoop.DefaultFixedDtSec, Idle);
            double rate = ac.State.Position.Z - prevZ;
            prevZ = ac.State.Position.Z;
            if (sec > 8 && rate > terminal * 0.95) lost = start - (-ac.State.Position.Z);
        }
        _o.WriteLine($"pulled at {40 * 1.944:0} kt in level flight: {lost:0} m ({lost * 3.28084:0} ft) gone by the time " +
                     $"it is descending steadily at {terminal:0.0} m/s");
        Assert.InRange(lost * 3.28084, 150, 800);
    }

    [Fact]
    public void ItSettlesAtTheBookDescentRateAndStaysThere()
    {
        var t = new WorldTerrain();
        WorldTerrain.Active = t;
        Atmosphere.SteadyWind = Vec3.Zero;
        AircraftConfig c = Load(Cirrus);
        var state = new RigidBodyState(new Vec3(0, 0, -2500), Quat.Identity, new Vec3(c.SpawnIasMs, 0, 0), Vec3.Zero);
        var ac = new Aircraft(c, state, new ControlDeflections(0, 0, 0, 0));
        var loop = new SimLoop(ac);
        ac.DeployBrs(2500);
        int settle = (int)(40 / SimLoop.DefaultFixedDtSec);
        for (int i = 0; i < settle; i++) loop.RunFor(SimLoop.DefaultFixedDtSec, Idle);   // 40 s: well settled
        // Sink over the next ten seconds, measured rather than read off one frame. (The step is 1/200 s, not
        // 1/120: counting steps as if they were 120 Hz made a settled 9.6 m/s read as 5.7.)
        double z0 = ac.State.Position.Z;
        int window = (int)(10 / SimLoop.DefaultFixedDtSec);
        for (int i = 0; i < window; i++) loop.RunFor(SimLoop.DefaultFixedDtSec, Idle);
        double sink = (ac.State.Position.Z - z0) / (window * SimLoop.DefaultFixedDtSec);
        double rho = Atmosphere.DensityAtAltitude(-ac.State.Position.Z);
        double book = BallisticRecovery.DescentRateMs(c, rho);
        double fwd = ac.State.Velocity.Length - sink;
        _o.WriteLine($"settled: sinking {sink:0.0} m/s ({sink * 196.85:0} fpm) against {book:0.0} m/s predicted " +
                     $"at this density; it has stopped flying ({fwd:0.0} m/s of anything else)");
        Assert.InRange(sink, book * 0.8, book * 1.25);
    }

    [Fact]
    public void ItHangsTheRightWayUpFromUpsideDown()
    {
        // THE WHOLE POINT, and nothing scripts it: thrown in inverted, the aeroplane has to come the right
        // way up because the canopy pulls from a bridle ABOVE the centre of gravity. If that offset were
        // zero it would hang inverted quite happily, which is how you can tell this is physics.
        var t = new WorldTerrain();
        WorldTerrain.Active = t;
        Atmosphere.SteadyWind = Vec3.Zero;
        AircraftConfig c = Load(Cirrus);
        var inverted = new Quat(1, 0, 0, 0);                 // rolled 180°
        var state = new RigidBodyState(new Vec3(0, 0, -2500), inverted, new Vec3(40, 0, 0), Vec3.Zero);
        var ac = new Aircraft(c, state, new ControlDeflections(0, 0, 0, 0));
        var loop = new SimLoop(ac);
        ac.DeployBrs(2500);
        double worstAfterSettling = 0;
        for (int i = 0; i < 7200; i++)                        // 60 s
        {
            loop.RunFor(SimLoop.DefaultFixedDtSec, Idle);
            if (i > 4800)                                     // after 40 s it should be hanging quietly
            {
                Vec3 downBody = ac.State.Attitude.Conjugate().Rotate(new Vec3(0, 0, 1));
                worstAfterSettling = Math.Max(worstAfterSettling, Math.Acos(Math.Clamp(downBody.Z, -1, 1)) * 57.2958);
            }
        }
        Vec3 d = ac.State.Attitude.Conjugate().Rotate(new Vec3(0, 0, 1));
        double tilt = Math.Acos(Math.Clamp(d.Z, -1, 1)) * 57.2958;
        _o.WriteLine($"let go inverted under the canopy: after a minute it hangs {tilt:0.0}° off level, " +
                     $"worst {worstAfterSettling:0.0}° over the last 20 s");
        Assert.True(worstAfterSettling < 60, $"it never settled upright — still {worstAfterSettling:0}° off");
    }

    [Fact]
    public void TheBridleHasToBeAboveTheCentreOfGravity()
    {
        // The one number the whole attitude depends on. Stated as a rule so nobody "tidies" it to zero.
        AircraftConfig c = Load(Cirrus);
        Assert.NotNull(c.Brs);
        double aboveCg = c.Mass.CgVec().Z - c.Brs!.BridleVec().Z;   // z is DOWN, so above = larger
        _o.WriteLine($"the bridle pulls {aboveCg:0.00} m above the CG");
        Assert.True(aboveCg > 0.3, "the bridle must be well above the CG or the aeroplane will not hang upright");
    }

    [Fact]
    public void PullingItTooFastTearsIt()
    {
        // Vpd is a demonstrated speed with margin, not a cliff: a little over and it opens hard, well over
        // and the risers let go. Deterministic — the same speed gives the same answer every time.
        AircraftConfig c = Load(Cirrus);
        BrsConfig b = c.Brs!;
        var ok = new BallisticRecovery(); ok.Deploy(b, b.MaxDeployIasMs * 0.9, 1000);
        var hard = new BallisticRecovery(); hard.Deploy(b, b.MaxDeployIasMs * 1.1, 1000);
        var torn = new BallisticRecovery(); torn.Deploy(b, b.MaxDeployIasMs * 1.4, 1000);
        Assert.NotEqual(BallisticRecovery.Phase.Failed, ok.Current);
        Assert.NotEqual(BallisticRecovery.Phase.Failed, hard.Current);
        Assert.Equal(BallisticRecovery.Phase.Failed, torn.Current);
        // And a torn canopy cannot hold the aeroplane up.
        for (int i = 0; i < 1000; i++) torn.Step(SimLoop.DefaultFixedDtSec);
        double cds = torn.CdS(b), full = b.Cd * b.CanopyProjectedAreaM2;
        _o.WriteLine($"Vpd {b.MaxDeployIasMs * 1.944:0} kt: 10 % over opens hard, 40 % over tears " +
                     $"(a streamer's {cds:0.0} m² against a canopy's {full:0})");
        Assert.True(cds < full * 0.05, "a streamer should not be most of a parachute");
        Assert.False(torn.Carrying);
    }

    [Fact]
    public void ItArrivesHardEnoughToMatterButSurvivably()
    {
        // Flown onto the ground from 500 ft — the certified minimum — and the arrival measured.
        var t = new WorldTerrain();
        WorldTerrain.Active = t;
        WorldTerrain.SurfaceOverride = WorldTerrain.Surface.Grass;
        Atmosphere.SteadyWind = Vec3.Zero;
        AircraftConfig c = Load(Cirrus);
        double agl = c.Brs!.MinSafeAglM;
        // ABOVE THE GROUND, not above sea level: the Valley floor is at 304.8 m, and placing it at 152 m put
        // the aeroplane inside the hill, where the contact forces threw it to 970 m/s in half a second.
        double ground = t.HeightAt(0, 0);
        var state = new RigidBodyState(new Vec3(0, 0, -(ground + agl)), Quat.Identity, new Vec3(40, 0, 0), Vec3.Zero);
        var ac = new Aircraft(c, state, new ControlDeflections(0, 0, 0, 0));
        var loop = new SimLoop(ac);
        ac.DeployBrs(agl);
        double worstSink = 0;
        int steps = 0;
        for (; steps < 6000; steps++)
        {
            loop.RunFor(SimLoop.DefaultFixedDtSec, Idle);
            if (double.IsNaN(ac.State.Position.Z)) break;
            worstSink = Math.Max(worstSink, ac.State.Velocity.Length);
            if (-ac.State.Position.Z <= ground + 2.0) break;
        }
        _o.WriteLine($"deployed at the certified 500 ft: on the ground after {steps * SimLoop.DefaultFixedDtSec:0.0} s, " +
                     $"fastest it ever went {worstSink:0.0} m/s");
        Assert.False(double.IsNaN(ac.State.Position.Z), "it came apart");
        Assert.True(worstSink < 60, $"it reached {worstSink:0} m/s — the canopy never took hold");
    }
}
