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
/// The graded glacier ski landing zones (owner 2026-10-29, from the visual owner's handoff "glacier ski
/// landing zones"). Three of them — the Knik, the Colony and the East Fork — each 800 m along the ice by
/// 80 m across, centred at S = 0.65 × length and aligned down-glacier, inside a 920 × 200 m crevasse-free
/// safety buffer.
///
/// The tolerances are the owner's: 3° or less along (5° the absolute ceiling), 2° or less across, and no
/// more than 0.5 m off a smooth plane over any 30 m span. They are measured here the way they are stated —
/// over samples INSIDE the rectangle. A span that reaches out into the apron is measuring the apron, which
/// is not the thing that was graded; measuring it that way made the Knik read 5.57° along when its graded
/// surface is 2.8° by construction.
/// </summary>
public class GlacierLandingZoneTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    private readonly WorldTerrain _t;

    public GlacierLandingZoneTests(ITestOutputHelper o)
    {
        _o = o;
        _t = new WorldTerrain();
        WorldTerrain.Active = _t;
    }

    public void Dispose()
    {
        WorldTerrain.Active = null;
        WorldTerrain.SurfaceOverride = null;
        Atmosphere.SteadyWind = Vec3.Zero;
    }

    // ---- the geometry the visual owner pinned ----------------------------------------------------

    [Fact]
    public void TheZonesAreWhereTheVisualOwnerPutThem()
    {
        // The handoff's table, to the metre and the hundredth of a degree. If these drift, the shader is
        // painting its ice and its LZ exclusion somewhere the height field has not graded.
        var want = new (string name, double cx, double cy, double headingDeg, double halfWidth)[]
        {
            ("Knik",      5195, -6155, 77.01, 420),
            ("Colony",    6530, -6055, 81.25, 340),
            ("East Fork", 7630, -6025, 82.41, 280),
        };
        Assert.Equal(want.Length, AlaskaRange.LandingZones.Length);
        for (int i = 0; i < want.Length; i++)
        {
            AlaskaRange.LandingZone z = AlaskaRange.LandingZones[i];
            var (cx, cy) = z.Centre;
            _o.WriteLine($"{z.Name,-10} centre ({cx:0},{cy:0})  heading {z.HeadingDeg:0.00}  " +
                         $"half-width {z.Ice.HalfWidthM:0} m  grade {z.AlongDeg:0.0}° ({z.Grade})");
            Assert.Equal(want[i].name, z.Name);
            Assert.Equal(want[i].cx, cx, 0);
            Assert.Equal(want[i].cy, cy, 0);
            Assert.Equal(want[i].headingDeg, z.HeadingDeg, 2);
            Assert.Equal(want[i].halfWidth, z.Ice.HalfWidthM, 3);
        }
        Assert.Equal(800, AlaskaRange.LzLengthM, 3);
        Assert.Equal(80, AlaskaRange.LzWidthM, 3);
        Assert.Equal(920, AlaskaRange.LzBufferLengthM, 3);
        Assert.Equal(200, AlaskaRange.LzBufferWidthM, 3);
        // And the apron that blends the earthwork out is the shader's own 60 m / 60 m feather.
        Assert.Equal(60, AlaskaRange.LzApronAlongM, 3);
        Assert.Equal(60, AlaskaRange.LzApronAcrossM, 3);
    }

    /// <summary>Height at (S, U) on a zone's corridor.</summary>
    private double H(AlaskaRange.LandingZone z, double ds, double du)
    {
        var (x, y) = z.Ice.At(z.CentreS + ds, du);
        return _t.HeightAt(x, y);
    }

    // ---- the tolerances -------------------------------------------------------------------------

    [Fact]
    public void EveryZoneIsGradedToTheOwnersTolerances()
    {
        const double ha = AlaskaRange.LzHalfLengthM, hw = AlaskaRange.LzHalfWidthM;
        foreach (AlaskaRange.LandingZone z in AlaskaRange.LandingZones)
        {
            double maxAlong = 0, maxAcross = 0, resid = 0, lo = double.MaxValue, hi = double.MinValue;
            double wa = 0, wu = 0;
            for (double ds = -ha; ds <= ha + 1e-6; ds += 10)
            for (double du = -hw; du <= hw + 1e-6; du += 10)
            {
                double h = H(z, ds, du);
                Assert.False(double.IsNaN(h), $"{z.Name} has no ground at (S{ds:0}, U{du:0})");
                lo = Math.Min(lo, h); hi = Math.Max(hi, h);
                if (ds + 10 <= ha + 1e-6)
                {
                    double a = Math.Abs(Math.Atan2(H(z, ds + 10, du) - h, 10)) * 180 / Math.PI;
                    if (a > maxAlong) { maxAlong = a; wa = ds; }
                }
                if (du + 10 <= hw + 1e-6)
                {
                    double c = Math.Abs(Math.Atan2(H(z, ds, du + 10) - h, 10)) * 180 / Math.PI;
                    if (c > maxAcross) { maxAcross = c; wu = du; }
                }
                // 0.5 m off a smooth plane over any 30 m: the sag of the middle of a 30 m span.
                if (ds - 15 >= -ha - 1e-6 && ds + 15 <= ha + 1e-6)
                    resid = Math.Max(resid, Math.Abs(h - 0.5 * (H(z, ds - 15, du) + H(z, ds + 15, du))));
                if (du - 15 >= -hw - 1e-6 && du + 15 <= hw + 1e-6)
                    resid = Math.Max(resid, Math.Abs(h - 0.5 * (H(z, ds, du - 15) + H(z, ds, du + 15))));
            }
            _o.WriteLine($"{z.Name,-10} along <= {maxAlong:0.00}° (at S{wa:+0;-0}), across <= {maxAcross:0.00}° " +
                         $"(at U{wu:+0;-0}), 30 m residual {resid:0.00} m, {hi - lo:0.0} m of fall over 800 m " +
                         $"({lo:0.0}–{hi:0.0} m)");
            Assert.True(maxAlong <= AlaskaRange.LzMaxAlongDeg,
                        $"{z.Name} falls at {maxAlong:0.00}° along, past the {AlaskaRange.LzMaxAlongDeg:0.0}° target");
            Assert.True(maxAlong <= AlaskaRange.LzMaxAlongCeilingDeg);
            Assert.True(maxAcross <= AlaskaRange.LzMaxAcrossDeg,
                        $"{z.Name} is {maxAcross:0.00}° across, past the {AlaskaRange.LzMaxAcrossDeg:0.0}° limit");
            Assert.True(resid <= AlaskaRange.LzMaxPlaneErrM,
                        $"{z.Name} is {resid:0.00} m off a smooth plane over 30 m");
        }
    }

    [Fact]
    public void TheGradedSurfaceIsTheOneTheHeightFieldReturns()
    {
        // The zone is not just "flat enough": the height field has to BE the declared plane inside the clear
        // rectangle, or the plane the tolerances were derived from is a different surface from the one the
        // wheels touch — and the lake's shore blend was quietly taking 3 m off the Knik's lower end.
        foreach (AlaskaRange.LandingZone z in AlaskaRange.LandingZones)
        {
            double worst = 0;
            for (double ds = -AlaskaRange.LzHalfLengthM; ds <= AlaskaRange.LzHalfLengthM + 1e-6; ds += 25)
            for (double du = -AlaskaRange.LzHalfWidthM; du <= AlaskaRange.LzHalfWidthM + 1e-6; du += 10)
                worst = Math.Max(worst, Math.Abs(H(z, ds, du) - z.SurfaceAt(z.CentreS + ds)));
            _o.WriteLine($"{z.Name,-10} height field is within {worst * 100:0.0} cm of its own graded plane");
            Assert.True(worst < 0.02, $"{z.Name} is {worst:0.00} m off the plane it declares");
        }
    }

    // ---- the buffer and the crevasse no-go areas -------------------------------------------------

    [Fact]
    public void TheSafetyBufferIsCrevasseFreeAndTheIcefallAndMarginsAreNot()
    {
        foreach (AlaskaRange.LandingZone z in AlaskaRange.LandingZones)
        {
            // Nothing inside the 920 × 200 m buffer is a no-go area. That is what the buffer is for.
            int inBuffer = 0;
            for (double ds = -AlaskaRange.LzBufferHalfLengthM; ds <= AlaskaRange.LzBufferHalfLengthM + 1e-6; ds += 20)
            for (double du = -AlaskaRange.LzBufferHalfWidthM; du <= AlaskaRange.LzBufferHalfWidthM + 1e-6; du += 10)
            {
                var (x, y) = z.Ice.At(z.CentreS + ds, du);
                Assert.False(AlaskaRange.Crevassed(x, y),
                             $"a crevasse no-go area inside {z.Name}'s safety buffer at (S{ds:0}, U{du:0})");
                // A millimetre of slack: a point BUILT at exactly S = centre ± 460 and read back through
                // the frame lands a few parts in 10^10 outside it, which is arithmetic, not geometry.
                Assert.True(AlaskaRange.InLandingBuffer(x, y, 0.001),
                            $"{z.Name}'s own buffer corner reads as outside it at (S{ds:0}, U{du:0})");
                inBuffer++;
            }

            // The icefall is: the upper 55% of the length, outside the buffer, down the middle of the ice.
            var (ix, iy) = z.Ice.At(0.25 * z.Ice.LengthM, 0);
            Assert.True(AlaskaRange.Crevassed(ix, iy), $"{z.Name}'s icefall is not marked");
            // And so are the margins, where the ice shears against the valley wall.
            var (mx, my) = z.Ice.At(0.80 * z.Ice.LengthM, 0.80 * z.Ice.HalfWidthM);
            Assert.True(AlaskaRange.Crevassed(mx, my), $"{z.Name}'s margin is not marked");
            // Off the ice entirely there are no crevasses, because there is no ice.
            var (ox, oy) = z.Ice.At(0.80 * z.Ice.LengthM, z.Ice.HalfWidthM + 200);
            Assert.False(AlaskaRange.Crevassed(ox, oy));
            _o.WriteLine($"{z.Name,-10} {inBuffer} points in the buffer, all clear; icefall and margins marked");
        }
    }

    [Fact]
    public void BrokenIceIsSomethingTheWheelsCanFeel()
    {
        // A no-go area a wheel cannot feel is a decoration. Outside the buffer the broken surface is half a
        // metre of blocks; the graded zone is smooth.
        AlaskaRange.LandingZone knik = AlaskaRange.LandingZones[0];
        var (gx, gy) = knik.Centre;
        Assert.Equal(0.0, WorldTerrain.MicroBumpAt(gx, gy), 6);

        var (cx, cy) = knik.Ice.At(0.25 * knik.Ice.LengthM, 0);
        Assert.True(AlaskaRange.Crevassed(cx, cy));
        double worst = 0;
        for (double d = 0; d < 60; d += 1.5)
            worst = Math.Max(worst, WorldTerrain.MicroBumpAt(cx + d, cy + d * 0.7));
        _o.WriteLine($"broken ice in the Knik's icefall: blocks up to {worst:0.00} m; the graded zone is smooth");
        Assert.True(worst > 0.3, "the crevasse field should be rough enough to wreck an arrival");
    }

    // ---- the surface ----------------------------------------------------------------------------

    [Fact]
    public void TheZonesArePackedGlacierSnowAndTheRestOfTheIceIsNot()
    {
        foreach (AlaskaRange.LandingZone z in AlaskaRange.LandingZones)
        {
            var (cx, cy) = z.Centre;
            Assert.Equal(WorldTerrain.Surface.PackedGlacierSnow, WorldTerrain.SurfaceAt(cx, cy));
            Assert.True(AlaskaRange.OnIce(cx, cy), $"{z.Name}'s landing zone is not on ice");
            // Up in the icefall it is soft snow, which is what the whole glacier was before the zones.
            var (ix, iy) = z.Ice.At(0.25 * z.Ice.LengthM, 0);
            Assert.Equal(WorldTerrain.Surface.Snow, WorldTerrain.SurfaceAt(ix, iy));
            // Off the side of the corridor it is mountain again.
            var (ox, oy) = z.Ice.At(0.65 * z.Ice.LengthM, z.Ice.HalfWidthM + 250);
            Assert.NotEqual(WorldTerrain.Surface.PackedGlacierSnow, WorldTerrain.SurfaceAt(ox, oy));
            Assert.NotEqual(WorldTerrain.Surface.Snow, WorldTerrain.SurfaceAt(ox, oy));
        }
        // The East Fork had no sim ice designation at all: the shader painted it, the sim called it gravel.
        var (ex, ey) = AlaskaRange.LandingZones[2].Centre;
        _o.WriteLine($"East Fork at ({ex:0},{ey:0}) is now {WorldTerrain.SurfaceAt(ex, ey)}");
    }

    [Fact]
    public void PackedGlacierSnowHasTheCoefficientsTheHandoffAskedFor()
    {
        const WorldTerrain.Surface packed = WorldTerrain.Surface.PackedGlacierSnow;
        _o.WriteLine($"ski rolling {LandingGear.SkiRolling(packed):0.000} (soft snow {LandingGear.SkiRolling(WorldTerrain.Surface.Snow):0.000})");
        _o.WriteLine($"ski lateral {LandingGear.SkiLateralCoefficient(packed):0.000} (soft snow {LandingGear.SkiLateralCoefficient(WorldTerrain.Surface.Snow):0.000})");
        _o.WriteLine($"keel/skid sliding {WorldTerrain.KeelSlidingCoefficient(packed):0.000}");
        _o.WriteLine($"wheel rolling floor {WorldTerrain.RollingCoefficient(packed):0.000}");
        Assert.Equal(0.02, LandingGear.SkiRolling(packed), 6);
        Assert.Equal(0.12, LandingGear.SkiLateralCoefficient(packed), 6);
        Assert.Equal(0.04, WorldTerrain.KeelSlidingCoefficient(packed), 6);
        Assert.Equal(0.18, WorldTerrain.RollingCoefficient(packed), 6);

        // Sinkage: the wheel's own bearing pressure takes it from the 0.18 floor toward 0.35. A bushwheel
        // floats on the crust; the same load on a hard tyre breaks through.
        AircraftConfig bush = Load(WheelSkiTests.Cub, skis: false); bush.Bushwheels = true;
        AircraftConfig hard = Load(WheelSkiTests.Cub, skis: false); hard.Bushwheels = false;
        double soft = LandingGear.SnowSinkageRolling(bush, packed, 2600);
        double cut = LandingGear.SnowSinkageRolling(hard, packed, 2600);
        double heavy = LandingGear.SnowSinkageRolling(hard, packed, 12000);
        _o.WriteLine($"sinkage at 2.6 kN: bushwheel {soft:0.000}, hard tyre {cut:0.000}; at 12 kN {heavy:0.000}");
        Assert.InRange(soft, 0.18, 0.28);
        Assert.True(cut > soft, "a hard tyre should cut in further than a bushwheel");
        Assert.Equal(0.35, heavy, 6);
        // And it does not touch any other surface.
        Assert.Equal(WorldTerrain.RollingCoefficient(WorldTerrain.Surface.Gravel),
                     LandingGear.SnowSinkageRolling(hard, WorldTerrain.Surface.Gravel, 12000), 6);

        // A wheel-ski is a ski on both snows.
        AircraftConfig onSkis = Load(WheelSkiTests.Cub, skis: true);
        Assert.True(LandingGear.OnSki(onSkis, packed));
        Assert.True(LandingGear.OnSki(onSkis, WorldTerrain.Surface.Snow));
        Assert.False(LandingGear.OnSki(onSkis, WorldTerrain.Surface.Gravel));
    }

    // ---- flown ------------------------------------------------------------------------------------

    private static readonly ControlInputs Idle = new(0, 0, 0, 1);

    private static AircraftConfig Load(string id, bool skis)
    {
        AircraftConfig c = AircraftConfigLoader.LoadFromFile(
            Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
        c.Skis = skis;
        return c;
    }

    [Theory]
    [InlineData("Knik")]
    [InlineData("Colony")]
    [InlineData("East Fork")]
    public void ACubOnSkisCanBeLandedOnEveryZoneAndStopInsideIt(string name)
    {
        // The whole feature, flown: touch down at the downhill end of the graded zone going UP the ice —
        // which is how a glacier landing is made — and come to a stop still inside the 800 m rectangle,
        // without wandering out of the 80 m width.
        AlaskaRange.LandingZone z = Array.Find(AlaskaRange.LandingZones, l => l.Name == name);
        Atmosphere.SteadyWind = Vec3.Zero;

        // 40 m inside the downhill (down-glacier) end, on the centreline, pointing up the ice.
        double touchS = z.CentreS + AlaskaRange.LzHalfLengthM - 40;
        var (px, py) = z.Ice.At(touchS, 0);
        double uphill = Math.Atan2(-(z.Ice.TermY - z.Ice.HeadY), -(z.Ice.TermX - z.Ice.HeadX));
        Assert.Equal(WorldTerrain.Surface.PackedGlacierSnow, WorldTerrain.SurfaceAt(px, py));

        AircraftConfig c = Load(WheelSkiTests.Cub, skis: true);
        RigidBodyState rest = LandingGear.RestingState(c, px, py, _t.HeightAt(px, py), uphill);
        var ac = new Aircraft(c, new RigidBodyState(rest.Position, rest.Attitude, new Vec3(16, 0, 0), Vec3.Zero),
                              new ControlDeflections(0, 0, 0, 0));
        var loop = new SimLoop(ac);
        Vec3 start = ac.State.Position;
        double slowest = 99, worstAcross = 0, prevErr = 0;
        int steps = 0;
        for (; steps < 12000; steps++)
        {
            // FEET ON. A ski has a fraction of a tyre's lateral hold and a Cub's tailwheel castors, so an
            // aeroplane left to itself on snow wanders — the first version of this test turned the rudder
            // loose and the Cub drifted 55 to 68 m off the centreline and out of the 80 m width on all
            // three zones, which is not a finding about the zones. So there is a pilot on the rudder,
            // holding the centreline the way one is held: heading error plus the drift off it.
            var (sNow, uNow) = z.Ice.Frame(ac.State.Position.X, ac.State.Position.Y);
            // The aircraft's heading in the WORLD, from its attitude. RigidBodyState.Velocity is in BODY
            // axes, so reading a track out of Velocity.Y/Velocity.X gives the sideslip direction and a
            // heading of about zero — which pinned the rudder at full deflection for the whole run and made
            // two different gain sets produce byte-identical results. That is how this was caught.
            Vec3 nose = ac.State.Attitude.Rotate(new Vec3(1, 0, 0));
            double hdg = Math.Atan2(nose.Y, nose.X);
            double hdgErr = Wrap(uphill - hdg);
            double rate = (hdgErr - prevErr) / SimLoop.DefaultFixedDtSec;
            prevErr = hdgErr;
            double rudder = Math.Clamp(RudderP * hdgErr + RudderD * rate - RudderTrack * uNow, -1, 1);
            loop.RunFor(SimLoop.DefaultFixedDtSec, new ControlInputs(0, 0, rudder, 1));
            if (double.IsNaN(ac.State.Position.X)) break;
            worstAcross = Math.Max(worstAcross, Math.Abs(uNow));
            slowest = Math.Min(slowest, ac.State.Velocity.Length);
            if (ac.State.Velocity.Length < 0.3) break;
        }
        Assert.False(double.IsNaN(ac.State.Position.X), $"the landing on the {name} went to pieces");
        double run = Math.Sqrt((ac.State.Position.X - start.X) * (ac.State.Position.X - start.X)
                             + (ac.State.Position.Y - start.Y) * (ac.State.Position.Y - start.Y));
        var (endS, endU) = z.Ice.Frame(ac.State.Position.X, ac.State.Position.Y);
        double climb = -(ac.State.Position.Z - start.Z);
        _o.WriteLine($"{name,-10} Cub on skis, touchdown 31 kt up the ice: ran {run:0} m in " +
                     $"{steps * SimLoop.DefaultFixedDtSec:0.0} s, climbed {climb:0.0} m, " +
                     $"stopped at S{endS - z.CentreS:+0;-0} U{endU:+0.0;-0.0}, " +
                     $"wandered {worstAcross:0.0} m off the centreline, slowest {slowest:0.00} m/s");

        Assert.True(slowest < 0.4, $"it never stopped on the {name} (slowest {slowest:0.0} m/s)");
        Assert.True(Math.Abs(endS - z.CentreS) <= AlaskaRange.LzHalfLengthM,
                    $"it ran off the end of the {name}'s zone, to S{endS - z.CentreS:+0;-0}");
        Assert.True(worstAcross <= AlaskaRange.LzHalfWidthM,
                    $"it wandered {worstAcross:0} m off the centreline, outside the 80 m width");
    }

    [Fact]
    public void ThePackedZoneRunsOnFurtherThanTheSoftSnowAroundIt()
    {
        // The surface is worth having: on the packed zone a ski runs out nearly three times as far as it does
        // in the settled snow of the rest of the glacier, which is the whole reason to find the zone.
        double packed = SlideOn(WorldTerrain.Surface.PackedGlacierSnow);
        double soft = SlideOn(WorldTerrain.Surface.Snow);
        _o.WriteLine($"a Cub on skis from 8 m/s: {packed:0} m on the packed zone, {soft:0} m in settled snow");
        Assert.True(packed > soft * 1.8, $"packed {packed:0} m against soft {soft:0} m");
    }

    /// <summary>The pilot on the rudder: heading error, its rate, and the drift off the centreline. Gentle —
    /// the first attempt used a proportional gain of 2.2 and over-controlled the Cub straight off the other
    /// side of the zone while scrubbing two thirds of its ground run away.</summary>
    private const double RudderP = 1.00, RudderD = 0.20, RudderTrack = 0.010;

    private static double Wrap(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }

    private static double SlideOn(WorldTerrain.Surface s)
    {
        WorldTerrain.Active = null;                    // flat ground: this is about the surface, not a place
        Atmosphere.SteadyWind = Vec3.Zero;
        WorldTerrain.SurfaceOverride = s;
        AircraftConfig c = Load(WheelSkiTests.Cub, skis: true);
        RigidBodyState rest = LandingGear.RestingState(c, 0, 0, 0, 0);
        var ac = new Aircraft(c, new RigidBodyState(rest.Position, rest.Attitude, new Vec3(8, 0, 0), Vec3.Zero),
                              new ControlDeflections(0, 0, 0, 0));
        var loop = new SimLoop(ac);
        double start = ac.State.Position.X;
        for (int i = 0; i < 20000 && ac.State.Velocity.Length > 0.1; i++)
            loop.RunFor(SimLoop.DefaultFixedDtSec, Idle);
        double run = ac.State.Position.X - start;
        WorldTerrain.SurfaceOverride = null;
        return run;
    }
}
