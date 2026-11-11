using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Aero;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

public class SmokeGeometryTests
{
    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(
        Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    // pitts-s2b-like left this list on 2026-11-11: it now carries a measured PAIR of pipes (smoke.pipes) instead of one
    // outlet, and TheSmokePipesSitAtTheFirewallCentredAndAimedFortyFiveDown below makes every one of these same checks on
    // every mouth of it and of the Extra.
    [Theory]
    [InlineData("great-lakes-like")]
    [InlineData("c172-like")]
    [InlineData("pa18-cub-like")]
    [InlineData("pa18-bush-like")]
    [InlineData("pa18-floats-like")]
    [InlineData("j3-cub-like")]
    [InlineData("j3-cub-floats-like")]
    public void MeasuredFleetOutletsStartClearOfTheFlowBodyWithASensibleJet(string id)
    {
        var cfg = Load(id);
        Assert.NotNull(cfg.Smoke);
        Assert.True(cfg.Smoke.TryOutlet(out var outlet));
        var crossflow = cfg.Fuselage.Crossflow!;
        var flow = new AirframeFlow(cfg, .4 * crossflow.LengthM, crossflow.BodyRadiusM);
        Assert.False(flow.Inside(outlet, .005), $"{id}: outlet inside the smoke flow body's collision envelope");
        var sm = cfg.Smoke;
        var pr = cfg.Propulsion!;
        double exit = ExhaustJet.ExitVelocity(sm.DisplacementCuIn * 16.387e-6, pr.MaxRpm, 1, 1.225,
            sm.PipeDiameterIn * .0254, sm.ExhaustTempK);
        Assert.InRange(exit, 15, 100);
        double idle = ExhaustJet.ExitVelocity(sm.DisplacementCuIn * 16.387e-6, pr.IdleRpm, 0, 1.225,
            sm.PipeDiameterIn * .0254, sm.ExhaustTempK);
        Assert.InRange(idle, .01, exit / 5);
        Assert.InRange(sm.DownDeg, 0, 90);
    }

    // THE SMOKE PIPES (Ty 2026-11-11). "The extras smoke comes out too far forward it needs to come out about the
    // firewall and it comes out of two pipes 2 inch dia each separated by 1 ft on center pointed down at 45 degrees with
    // one puff per pipe every other combustion stroke" ... "The pitts has the same problem but the pipes are 1.5 dia each
    // and separated by 8 inches on center" ... "The pitts is two pipes centered at the firewall" ... "1.5 dia each 45
    // degrees Down".
    //
    // The firewall stations are inline here as well as in the configs ON PURPOSE: they are the bug Ty reported (the smoke
    // was born AHEAD of the firewall - the Extra's at body x ~ +0.95 off a prop-hub estimate, 0.35 m forward of its
    // firewall), they were measured off the GLBs, and the Pitts' needed a judgement against its own model (see the
    // config's stationSource). If either number moves, it moves here too, deliberately.
    public static TheoryData<string, double, double, double> PipedTypes() => new()
    {
        // id, firewall station (body x, m), pipe spacing on centre (in), pipe bore (in)
        { "extra-300-like", 0.605, 12.0, 2.0 },
        { "pitts-s2b-like", 0.574, 8.0, 1.5 },
    };

    [Theory]
    [MemberData(nameof(PipedTypes))]
    public void TheSmokePipesSitAtTheFirewallCentredAndAimedFortyFiveDown(string id, double firewallX, double spacingIn, double diameterIn)
    {
        AircraftConfig cfg = Load(id);
        Assert.NotNull(cfg.Smoke);
        SmokePipesConfig pipes = cfg.Smoke!.Pipes;
        Assert.NotNull(pipes);

        // Ty's four numbers, unaltered, and the sources that name him.
        Assert.Equal(2, pipes!.Count);
        Assert.Equal(spacingIn, pipes.SpacingIn, 6);
        Assert.Equal(diameterIn, pipes.DiameterIn, 6);
        Assert.Equal(45.0, pipes.DownDeg, 6);
        Assert.Equal(2, pipes.CombustionStrokesPerPuff);
        Assert.Contains("2026-11-11", pipes.Source);
        Assert.NotEqual("", pipes.StationSource);

        // The measured firewall station, and the inches in metres.
        Assert.Equal(firewallX, pipes.FirewallXM, 6);
        Assert.Equal(spacingIn * 0.0254, pipes.SpacingM, 9);
        Assert.Equal(diameterIn * 0.0254, pipes.DiameterM, 9);

        Assert.True(pipes.TryPipes(out Vec3[] mouths, out Vec3[] aim));
        Assert.Equal(2, mouths.Length);
        Assert.Equal(mouths.Length, aim.Length);

        // THE BUG: no mouth may be ahead of the firewall.
        foreach (Vec3 m in mouths)
            Assert.True(m.X <= pipes.FirewallXM + 1e-9,
                $"{id}: a smoke pipe is {1000 * (m.X - pipes.FirewallXM):F0} mm AHEAD of the firewall station");

        // Centred on the centreline, at the authored spacing, at one height.
        Assert.Equal(0.0, mouths[0].Y + mouths[1].Y, 9);
        Assert.Equal(pipes.SpacingM, Math.Abs(mouths[0].Y - mouths[1].Y), 9);
        foreach (Vec3 m in mouths) Assert.Equal(0.5 * pipes.SpacingM, Math.Abs(m.Y), 9);
        Assert.Equal(mouths[0].Z, mouths[1].Z, 9);
        Assert.Equal(pipes.MouthZM, mouths[0].Z, 9);

        // 45 degrees DOWN from the longitudinal axis, pointing aft, no sideways component.
        foreach (Vec3 d in aim)
        {
            Assert.Equal(1.0, d.Length, 9);
            Assert.Equal(0.0, d.Y, 12);
            Assert.True(d.X < 0, $"{id}: a pipe blows forward");
            Assert.True(d.Z > 0, $"{id}: a pipe blows up (body z is down)");
            Assert.Equal(45.0, Math.Atan2(d.Z, -d.X) * 180 / Math.PI, 6);
        }

        // Every mouth starts in clear air, and the jet that carries it is still the exhaust's (unchanged).
        CrossflowConfig crossflow = cfg.Fuselage.Crossflow!;
        var flow = new AirframeFlow(cfg, .4 * crossflow.LengthM, crossflow.BodyRadiusM);
        foreach (Vec3 m in mouths)
            Assert.False(flow.Inside(m, .005), $"{id}: a pipe mouth is inside the smoke flow body's collision envelope");
        SmokeConfig sm = cfg.Smoke;
        PropulsionConfig pr = cfg.Propulsion!;
        double exit = ExhaustJet.ExitVelocity(sm.DisplacementCuIn * 16.387e-6, pr.MaxRpm, 1, 1.225,
            sm.PipeDiameterIn * .0254, sm.ExhaustTempK);
        Assert.InRange(exit, 15, 100);
        double idle = ExhaustJet.ExitVelocity(sm.DisplacementCuIn * 16.387e-6, pr.IdleRpm, 0, 1.225,
            sm.PipeDiameterIn * .0254, sm.ExhaustTempK);
        Assert.InRange(idle, .01, exit / 5);

        // The pipes supersede the old single-outlet authoring for the SMOKE, and every one of them is aft of where that
        // authoring put it — which is the whole complaint. (The old fields stay authored because
        // Wreck.AirframeHydroModel locates the nose from outletBodyM + aftOfNoseM; see the config's stationSource.)
        if (sm.TryOutlet(out Vec3 was))
            foreach (Vec3 m in mouths)
                Assert.True(m.X < was.X, $"{id}: a pipe is no further aft than the outlet Ty called too far forward");
    }

    // ONE PUFF PER PIPE EVERY OTHER COMBUSTION STROKE, derived from the engine and not from a chosen rate: the cylinder
    // count gives the combustion strokes a crank revolution (cylinders / 2 on a four-stroke) and the rpm gives the rate.
    [Theory]
    [MemberData(nameof(PipedTypes))]
    public void ThePuffCadenceComesOutOfTheEnginesOwnFiringRate(string id, double firewallX, double spacingIn, double diameterIn)
    {
        _ = firewallX; _ = spacingIn; _ = diameterIn;
        AircraftConfig cfg = Load(id);
        SmokePipesConfig pipes = cfg.Smoke!.Pipes!;
        int cyl = cfg.Smoke.Cylinders;
        PropulsionConfig pr = cfg.Propulsion!;
        Assert.Equal(6, cyl);                      // both are AEIO-540s: three combustion strokes a revolution
        Assert.Equal(3.0, ExhaustPulse.EventsPerRev(cyl), 9);

        // 2,700 rpm: 135 strokes a second, 67.5 puffs a second from each of the two pipes.
        Assert.Equal(2700, pr.MaxRpm);
        Assert.Equal(135.0, ExhaustPulse.PulseHz(cyl, pr.MaxRpm), 9);
        Assert.Equal(67.5, ExhaustPulse.PuffHz(cyl, pr.MaxRpm, pipes.CombustionStrokesPerPuff), 9);

        // Nothing invented and nothing lost: the bank together is the engine's own stroke rate, because there are as many
        // pipes as there are strokes between one pipe's puffs - the two fire alternately.
        Assert.Equal(pipes.Count, pipes.CombustionStrokesPerPuff);
        Assert.Equal(ExhaustPulse.PulseHz(cyl, pr.MaxRpm),
                     pipes.Count * ExhaustPulse.PuffHz(cyl, pr.MaxRpm, pipes.CombustionStrokesPerPuff), 9);

        // It slows with the engine, which is what the cadence is FOR: 67 Hz is unresolvable, 17.5 Hz at idle is not.
        double idle = ExhaustPulse.PuffHz(cyl, pr.IdleRpm, pipes.CombustionStrokesPerPuff);
        Assert.Equal(17.5, idle, 9);
        Assert.True(idle < 20, $"{id}: the smoke would not visibly pulse at idle");
        // And it stops when the engine does.
        Assert.Equal(0.0, ExhaustPulse.PuffHz(cyl, 0, pipes.CombustionStrokesPerPuff), 12);
    }

    [Theory]
    [InlineData(6, 2700, 1, 135.0)]   // every stroke, which is what a single-stack type still does
    [InlineData(6, 2700, 2, 67.5)]
    [InlineData(6, 2700, 3, 45.0)]
    [InlineData(4, 2400, 2, 40.0)]    // a four: two strokes a revolution
    [InlineData(6, 2700, 0, 135.0)]   // a nonsense cadence cannot divide by zero
    [InlineData(6, -100, 2, 0.0)]
    public void PuffHzIsThePulseRateSharedOutOverTheStrokes(int cyl, double rpm, int strokes, double hz)
        => Assert.Equal(hz, ExhaustPulse.PuffHz(cyl, rpm, strokes), 9);

    [Fact]
    public void AnyPipeCountStaysCentredOnTheCentreline()
    {
        foreach (int n in new[] { 1, 2, 3, 4 })
        {
            var pipes = new SmokePipesConfig { Count = n, SpacingIn = 8, DiameterIn = 1.5, FirewallXM = 0.5, MouthZM = 0.6 };
            Assert.True(pipes.TryPipes(out Vec3[] m, out _));
            Assert.Equal(n, m.Length);
            double sum = 0; foreach (Vec3 p in m) sum += p.Y;
            Assert.Equal(0.0, sum, 9);
        }
    }

    [Fact]
    public void AHalfAuthoredPipeBlockFallsBackInsteadOfDrawingSmokeAtNaN()
    {
        SmokePipesConfig Good() => new() { Count = 2, SpacingIn = 8, DiameterIn = 1.5, FirewallXM = 0.574, MouthZM = 0.655 };
        Assert.True(Good().TryPipes(out _, out _));
        Assert.False(new SmokePipesConfig().TryPipes(out _, out _));                                 // nothing authored
        Assert.False(new SmokePipesConfig { Count = 0, DiameterIn = 1.5 }.TryPipes(out _, out _));
        var noSpacing = Good(); noSpacing.SpacingIn = 0; Assert.False(noSpacing.TryPipes(out _, out _));
        var noBore = Good(); noBore.DiameterIn = 0; Assert.False(noBore.TryPipes(out _, out _));
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var a = Good(); a.FirewallXM = bad; Assert.False(a.TryPipes(out _, out _));
            var b = Good(); b.MouthZM = bad; Assert.False(b.TryPipes(out _, out _));
            var c = Good(); c.SpacingIn = bad; Assert.False(c.TryPipes(out _, out _));
            var d = Good(); d.DownDeg = bad; Assert.False(d.TryPipes(out _, out _));
        }
        // One pipe needs no spacing.
        Assert.True(new SmokePipesConfig { Count = 1, DiameterIn = 1.5, SpacingIn = 0 }.TryPipes(out Vec3[] one, out _));
        Assert.Single(one);
        Assert.Equal(0.0, one[0].Y, 12);
    }

    [Fact]
    public void SharedCubFuselagesKeepIdenticalSmokeAuthoring()
    {
        foreach (var family in new[] {
            new[] { "pa18-cub-like", "pa18-bush-like", "pa18-floats-like" },
            new[] { "j3-cub-like", "j3-cub-floats-like" } })
        {
            var first = Load(family[0]).Smoke!;
            foreach (string id in family.Skip(1))
            {
                var other = Load(id).Smoke!;
                // The float J-3 visual is placed 10 cm lower; its mouth is the same point on the fuselage.
                Assert.Equal(first.OutletBodyM![0], other.OutletBodyM![0]);
                Assert.Equal(first.OutletBodyM[1], other.OutletBodyM[1]);
                if (id != "j3-cub-floats-like") Assert.Equal(first.OutletBodyM[2], other.OutletBodyM[2]);
                Assert.Equal(first.AftOfNoseM, other.AftOfNoseM);
                Assert.Equal(first.DownDeg, other.DownDeg);
                Assert.Equal(first.DisplacementCuIn, other.DisplacementCuIn);
                Assert.Equal(first.PipeDiameterIn, other.PipeDiameterIn);
                Assert.Equal(first.ExhaustTempK, other.ExhaustTempK);
            }
        }
    }

    [Fact]
    public void OldSmokeBlocksKeepTheHubEstimate()
    {
        var old = new SmokeConfig { AftOfNoseM = 1.22, DownDeg = 30, DisplacementCuIn = 540 };
        Assert.False(old.TryOutlet(out _));
    }

    [Fact]
    public void AMeasuredOutletPreservesAllThreeSignedAxesAndCanBeAtTheOrigin()
    {
        foreach (var p in new[] { new[] { -1.2, 0.23, 0.64 }, new double[3] })
        {
            var smoke = new SmokeConfig { OutletBodyM = p };
            Assert.True(smoke.TryOutlet(out Vec3 v));
            Assert.Equal(p[0], v.X); Assert.Equal(p[1], v.Y); Assert.Equal(p[2], v.Z);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    public void MalformedOutletFallsBackToTheHub(int length)
        => Assert.False(new SmokeConfig { OutletBodyM = new double[length] }.TryOutlet(out _));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ANonFiniteOutletCannotPoisonAPuff(double bad)
    {
        for (int axis = 0; axis < 3; axis++)
        {
            double[] p = { 1, 2, 3 }; p[axis] = bad;
            Assert.False(new SmokeConfig { OutletBodyM = p }.TryOutlet(out _));
        }
    }

    // A GLIDER'S SMOKE COMES FROM THE ACTUAL WINGTIP (Ty 2026-11-10: "Make the 2-33 smoke come from the trailing
    // edge of the actual wingtip"). SmokeTrail.WingtipsFor lives in the Unity bridge and cannot be called from here,
    // so this pins the CONFIG DATA that its derivation stands on: a strip's span is its own Area/Chord, and the
    // outermost strip's OUTER EDGE -- centre plus half a strip -- is the real wingtip. If that stops landing on the
    // published semi-span, the canister starts hanging in clear air again and nothing else would catch it.
    [Theory]
    [InlineData("glider-2-33-like", 7.770)]          // Schweizer SGS 2-33, 51 ft span
    [InlineData("glider-grob103-acro-like", 9.000)]  // Grob 103, 18.0 m
    [InlineData("glider-swift-s1-like", 6.330)]      // Swift S-1, 12.66 m
    [InlineData("glider-eb29r-like", 14.650)]        // Schempp-Hirth EB29R, 29.3 m
    public void AGlidersOutermostStripEndsAtThePublishedWingtip(string id, double semiSpanM)
    {
        AircraftConfig cfg = Load(id);
        StripConfig tip = null;
        foreach (SurfaceConfig sf in cfg.Surfaces)
            foreach (StripConfig st in sf.Strips)
                if (Math.Abs(st.Pos[2]) < 1.0 && (tip == null || Math.Abs(st.Pos[1]) > Math.Abs(tip.Pos[1]))) tip = st;

        Assert.NotNull(tip);
        Assert.True(tip.Chord > 0.0, id + ": the outermost strip has no chord, so its span cannot be derived");
        double outerEdge = Math.Abs(tip.Pos[1]) + 0.5 * tip.Area / tip.Chord;
        Assert.True(Math.Abs(outerEdge - semiSpanM) < 0.01,
            $"{id}: outermost strip's outer edge {outerEdge:F3} m is not the published semi-span {semiSpanM:F3} m " +
            $"(strip centre {Math.Abs(tip.Pos[1]):F3}, chord {tip.Chord:F3}, area {tip.Area:F4})");
    }
}
