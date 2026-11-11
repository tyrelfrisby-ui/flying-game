using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>The physics spray (Ty 2026-11-02: "make it purely physics based"): emitted on the hull's waterline, jets within
/// the spray-root geometry, droplets that fall like droplets.</summary>
[Collection("WorldTerrainActive")]
public class SprayFieldTests
{
    private readonly ITestOutputHelper _out;
    public SprayFieldTests(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void ATwoMillimetreDropFallsAtItsMeasuredTerminalSpeed()
    {
        // Gunn & Kinzer 1949: 2.0 mm drop, 6.49 m/s in still air at sea level.
        Vec3 v = Vec3.Zero;
        for (int i = 0; i < 4000; i++) v += SprayField.DropAcceleration(v, Vec3.Zero, 0.002) * 0.002;
        _out.WriteLine($"2 mm terminal speed {v.Z:F2} m/s");
        Assert.InRange(v.Z, 6.0, 7.0);
    }

    [Fact]
    public void SprayLeavesTheWaterlineAtTheSprayRootSpeed()
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "j3-cub-floats-like.json"));
            // Planing at 13 m/s (29 mph), 6° nose-up, the step 0.1 m in: the CG 1.33 m above the water.
            double th = 6 / 57.2958, v = 13.0;
            var s = new RigidBodyState(new Vec3(0, 0, -1.33), new Quat(0, Math.Sin(th / 2), 0, Math.Cos(th / 2)), new Vec3(v * Math.Cos(th), 0, v * Math.Sin(th)), Vec3.Zero);
            FloatHydro.Compute(cfg, s, 0);
            var roots = new FloatHydro.SprayRoot[256];
            int n = FloatHydro.SprayRoots(cfg, s, roots);
            Assert.True(n > 4, $"only {n} spray roots");
            double flux = 0;
            for (int i = 0; i < n; i++)
            {
                FloatHydro.SprayRoot r = roots[i];
                flux += r.MassFlux;
                // on the waterline (the pile-up lifts the root at most a few cm above the undisturbed surface)
                Assert.InRange(-r.Position.Z, -0.05, 0.12);   // (the fluted V: the facet line is a chord of the drawn flute)
                // earth-frame jet: 2 V sin α ≤ 2 V; and it leaves upward or along the surface, never down into the water
                Assert.True(r.JetVelocity.Length <= 2 * v + 0.5, $"jet {r.JetVelocity.Length:F1} m/s");
                Assert.True(r.JetVelocity.Z <= 0.5, $"jet heading down at {r.JetVelocity.Z:F2} m/s");
            }
            var selected = new int[FloatSprayVisual.MaxArms];
            FloatSprayVisual.SelectRoots(roots.AsSpan(0, n), s.Attitude, selected);
            for (int arm = 0; arm < 4; arm++)
            {
                Assert.True(selected[arm] >= 0, $"missing chine arm {arm} on actual J-3 roots");
                var root = roots[selected[arm]];
                Vec3 body = s.Attitude.Conjugate().Rotate(root.Position - s.Position) + cfg.Mass.CgVec();
                double centre = arm < 2 ? -.9435 : .9435;
                Assert.InRange(Math.Abs(body.Y - centre), 0, .26791);
                Assert.True(Math.Abs(body.Y) > .65, "no spray origin between the floats");
            }
            _out.WriteLine($"{n} roots, {flux:F1} kg/s of spray at 29 mph");
            Assert.InRange(flux, 0.1, 200);
            // flown: every parcel ends on the water, none goes under it or flies for ever
            var f = new SprayField();
            for (int k = 0; k < 60; k++) f.Step(cfg, s, 1 / 60.0);
            int alive = 0; foreach (SprayField.Parcel p in f.Parcels) if (p.Alive) { alive++; Assert.True(-p.Pos.Z > -0.1, "a parcel under the water"); }
            Assert.InRange(alive, 1, SprayField.MaxParcels);
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }
    [Fact]
    public void VisualVsKeepTwoChinesOnEachFloatWithoutACentreEmitter()
    {
        // Literal J-3 float spacing 1.887 m, beam .5358 m. Origins are not averaged.
        var roots = new[]
        {
            new FloatHydro.SprayRoot { Slot=0, Position=new Vec3(1, -1.2104, 0), JetVelocity=new Vec3(1,-3,-2), MassFlux=1 },
            new FloatHydro.SprayRoot { Slot=0, Position=new Vec3(1, -.6746, 0), JetVelocity=new Vec3(1,3,-2), MassFlux=1 },
            new FloatHydro.SprayRoot { Slot=1, Position=new Vec3(1, .6746, 0), JetVelocity=new Vec3(1,-3,-2), MassFlux=1 },
            new FloatHydro.SprayRoot { Slot=1, Position=new Vec3(1, 1.2104, 0), JetVelocity=new Vec3(1,3,-2), MassFlux=1 },
            new FloatHydro.SprayRoot { Slot=0, Position=new Vec3(-1, -1.2104, 0), JetVelocity=new Vec3(1,-3,-2), MassFlux=10 },
            new FloatHydro.SprayRoot { Slot=1, Position=new Vec3(2, 1.2104, 0), JetVelocity=new Vec3(1,3,2), MassFlux=10 },
        };
        var selected = new int[FloatSprayVisual.MaxArms];
        foreach (double heading in new[] { 0.0, 1.4, -2.3 })
        {
            var q = new Quat(0, 0, Math.Sin(heading/2), Math.Cos(heading/2));
            var rotated = roots.Select(r => new FloatHydro.SprayRoot { Slot=r.Slot, Position=q.Rotate(r.Position), JetVelocity=q.Rotate(r.JetVelocity), MassFlux=r.MassFlux }).ToArray();
            FloatSprayVisual.SelectRoots(rotated, q, selected);
            Assert.Equal(new[] { 0, 1, 2, 3, -1, -1, -1, -1 }, selected);
            for (int arm = 0; arm < 4; arm++)
            {
                var root = rotated[selected[arm]];
                var points = new Vec3[FloatSprayVisual.Samples];
                FloatSprayVisual.SampleSheet(root, q.Rotate(new Vec3(18,0,0)), Vec3.Zero, points);
                Assert.Equal(root.Position, points[0]);
                Vec3 delta = q.Conjugate().Rotate(points[^1] - root.Position);
                Assert.True(delta.X < -2, "each arm trails its own moving contact");
                Assert.True(arm % 2 == 0 ? delta.Y < -.2 : delta.Y > .2, "both sides of each float form a V");
                Assert.True(delta.Z < 0, "spray rises above the contact");
                FloatSprayVisual.SampleSheet(root, q.Rotate(new Vec3(18,0,0)), Vec3.Zero, points, 0);
                Assert.All(points, point => Assert.Equal(root.Position, point)); // No pre-existing sheet at first contact.
            }
        }
        FloatSprayVisual.SelectRoots(ReadOnlySpan<FloatHydro.SprayRoot>.Empty, new Quat(0,0,0,1), selected);
        Assert.All(selected, i => Assert.Equal(-1, i)); // Dry floats emit no visual sheet.
    }

    [Fact]
    public void FineSprayFootprintStaysWithinEachWakeAcrossHeadings()
    {
        // Literal existing wake slope is .354, so the .30 drawing envelope leaves a margin.
        Assert.InRange(FloatSprayVisual.MaxFootprintM, .001, .025);
        foreach (double yaw in new[] { 0.0, 1.4, -2.3 })
        {
            var q = new Quat(0, 0, Math.Sin(yaw/2), Math.Cos(yaw/2));
            Vec3 f = q.Rotate(new Vec3(1, 0, 0));
            foreach (double floatY in new[] { -.9435, .9435 })
            {
                Vec3 step = q.Rotate(new Vec3(0, floatY, 0));
                Assert.True(FloatSprayVisual.InsideWake(step + q.Rotate(new Vec3(-4,.8,-.2)), step, f, .5358));
                Assert.False(FloatSprayVisual.InsideWake(step + q.Rotate(new Vec3(-4,2,-.2)), step, f, .5358));
                Assert.False(FloatSprayVisual.InsideWake(step + f, step, f, .5358));
                Vec3 contact = step + q.Rotate(new Vec3(.3, .2679, 0));
                Vec3 bounded = FloatSprayVisual.BoundSheet(contact + q.Rotate(new Vec3(-4,3,-2)), contact, f);
                Vec3 d = q.Conjugate().Rotate(bounded-contact);
                Assert.InRange(d.Y, -.721, .721);
                Assert.InRange(d.Z, -.301, 0);
                Assert.Equal(contact, FloatSprayVisual.BoundSheet(contact, contact, f));
            }
        }
    }

    [Fact]
    public void VisualRegimeUsesSupportVentilationAndHeelReleaseNotSpeed()
    {
        var f = new FloatHydro.FloatState { Wet=true, PlaningLiftN=2000, BuoyancyN=500,
            StepDraftM=.1, SternDraftM=-.02, VentilatedFraction=1, SpeedMs=18 };
        Assert.True(FloatSprayVisual.IsOnStep(f));
        Assert.Equal(1, FloatSprayVisual.StepFraction(f));
        Vec3 outside = new Vec3(-2, 2, -.1), forward = new Vec3(1,0,0);
        Assert.Equal(0, FloatSprayVisual.CloudVisibility(outside, forward, .5358, new[] { f }));
        // Same speed and full ventilation, but a deeply dragging heel: retain broad plow spray.
        f.SternDraftM=.2;
        Assert.False(FloatSprayVisual.IsOnStep(f));
        Assert.Equal(0, FloatSprayVisual.StepFraction(f));
        Assert.Equal(1, FloatSprayVisual.CloudVisibility(outside, forward, .5358, new[] { f }));
        f.SpeedMs=30; Assert.Equal(0, FloatSprayVisual.StepFraction(f));
        f.SternDraftM=.0025; Assert.InRange(FloatSprayVisual.StepFraction(f), .499, .501);
        f.SternDraftM=.00125; Assert.InRange(FloatSprayVisual.StepFraction(f), .749, .751);
        f.SternDraftM=0; f.PlaningLiftN=250;
        Assert.Equal(.5, FloatSprayVisual.StepFraction(f));
        f.VentilatedFraction=.5; Assert.Equal(.25, FloatSprayVisual.StepFraction(f));
        f.Wet=false; Assert.False(FloatSprayVisual.IsOnStep(f)); Assert.Equal(0, FloatSprayVisual.StepFraction(f));
        Assert.Equal(0, FloatSprayVisual.CloudVisibility(outside, forward, .5358, new[] { f }));
    }

    /// <summary>Every field a pilot can actually choose that has water a floatplane would be spawned onto
    /// (FlightSimDriver.StartWater asks IslandRunways.Water for the same thing). PALH, the float variant of PANC, is in
    /// the island spec but its start_group is "PANC float variant", so WorldTerrain.Airports — coastal and inland only —
    /// does not list it and no session can start there; the seaplane lanes a pilot reaches are these.</summary>
    public static IEnumerable<object[]> SeaplaneFields()
    {
        // MemberData is evaluated at DISCOVERY, before any test in the assembly runs, and WorldTerrain.Active is a
        // global: a world left installed here puts the whole "WorldTerrainActive" collection on the island, and the
        // flat-water tests that spawn at (0,0) under FlatWaterOverride — J3FloatsWaterTests, FloatTests — are then
        // sitting inside island terrain and go to NaN on their first step. Eager list, not an iterator, so the restore
        // has run by the time this returns.
        WorldTerrain before = WorldTerrain.Active;
        var fields = new List<object[]>();
        try
        {
            WorldTerrain.Active ??= new WorldTerrain();
            foreach (WorldTerrain.Airport a in WorldTerrain.Airports)
                if (IslandRunways.Water(a, 600) != null) fields.Add(new object[] { a.Name });
        }
        finally { WorldTerrain.Active = before; }
        return fields;
    }

    /// <summary>
    /// DOES THE SPRAY ACTUALLY COME OUT ON THE ISLAND? (Ty has said twice that the wake and the spray are not there.)
    /// The wake's bug was that WakeWaves looked its water up in the legacy <c>WorldTerrain.Lakes</c> table, which on the
    /// island names nothing within five lake-radii of the seaplane lanes, so its height field was never allocated. Every
    /// other spray test in this file sets <see cref="FloatHydro.FlatWaterOverride"/> — an infinite flat sea — so not one
    /// of them would have caught the same mistake here. This one flies the J-3 off the ISLAND'S OWN water, spawned
    /// exactly as FlightSimDriver spawns it, with nothing overridden, and steps the same <see cref="SprayField"/> that
    /// FloatSplash steps, on the same (config, state, dt).
    ///
    /// It then asks the question the picture asks: not only "were parcels emitted" but "would any of them be DRAWN".
    /// FloatSplash draws a speck only where <see cref="FloatSprayVisual.CloudVisibility"/> is above zero, and that reads
    /// the static <c>FloatHydro.Floats</c> — the per-float wet/step state the last Compute left behind — so a field full
    /// of live parcels can still draw nothing. Both counts are asserted, in the displacement taxi AND on the step.
    /// </summary>
    [Theory]
    [MemberData(nameof(SeaplaneFields))]
    public void TheIslandsOwnSeaplaneLaneThrowsSprayAndItIsDrawn(string airport)
    {
        WorldTerrain.Active = new WorldTerrain();
        try
        {
            AircraftConfig cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "j3-cub-floats-like.json"));
            int index = Array.FindIndex(WorldTerrain.Airports, a => a.Name == airport);
            Assert.True(index >= 0, "no airport called " + airport);
            WorldTerrain.Airport ap = WorldTerrain.Airports[index];
            WorldTerrain.Lake lane = IslandRunways.Water(ap, 600) ?? throw new Exception("no water at " + ap.Name);
            _out.WriteLine($"{ap.Name} seaplane lane ({lane.Cx:F0},{lane.Cy:F0}) surface {lane.SurfaceM:F2} m, rx {lane.Rx:F0} m — no FlatWaterOverride, the island's own WaterSurfaceAt");

            var pos = new Vec3(lane.Cx - lane.Rx * 0.5, lane.Cy, -(lane.SurfaceM + cfg.Floats!.KeelZ - 0.25));
            var ac = new Aircraft(cfg, new RigidBodyState(pos, new Quat(0, 0, 0, 1), Vec3.Zero, Vec3.Zero), ControlDeflections.Neutral);
            var sim = new SimLoop(ac);
            var field = new SprayField();

            int taxiDrawn = 0, stepDrawn = 0, taxiParcels = 0, stepParcels = 0;
            double peakFlux = 0;
            // Full power, stick back until the nose stops rising then held — FloatsIslandTakeoffProbe's "ease".
            double peak = -99, integ = 0; bool eased = false;
            for (double t = 0; t < 30; t += 1 / 60.0)
            {
                RigidBodyState s = ac.State;
                double v = s.Velocity.Length;
                double pitch = -Math.Asin(Math.Clamp(s.Attitude.Rotate(new Vec3(1, 0, 0)).Z, -1, 1)) * 57.2958, stick;
                if (!eased) { stick = -1; if (pitch > peak) peak = pitch; else if (peak > 6 && pitch < peak - 1) eased = true; }
                else
                {
                    double target = v < 18 ? 6.0 : 10.0;
                    integ = Math.Clamp(integ + (target - pitch) * (1 / 60.0), -20, 20);
                    stick = Math.Clamp(-0.15 - 0.10 * (target - pitch) - 0.03 * integ, -1, 1);
                }
                sim.RunFor(1 / 60.0, new ControlInputs(0, stick, 0, -1.0));

                // Exactly FloatSplash.Step's own two lines: the field off the live config and state, then the
                // per-parcel visibility test against the float states the sim's Compute has just written.
                field.Step(ac.Config, ac.State, 1 / 60.0);
                peakFlux = Math.Max(peakFlux, field.LastFluxKgS);
                Vec3 forward = ac.State.Attitude.Rotate(new Vec3(1, 0, 0));
                var floats = new ReadOnlySpan<FloatHydro.FloatState>(FloatHydro.Floats, 0, 2);
                int alive = 0, drawn = 0;
                foreach (SprayField.Parcel p in field.Parcels)
                {
                    if (!p.Alive || p.Age > FloatSprayVisual.VisibleLife) continue;
                    alive++;
                    if (FloatSprayVisual.CloudVisibility(p.Pos, forward, ac.Config.Floats!.BeamM, floats) > 0) drawn++;
                }
                bool onStep = FloatSprayVisual.IsOnStep(FloatHydro.Floats[0]) || FloatSprayVisual.IsOnStep(FloatHydro.Floats[1]);
                if (onStep) { stepParcels = Math.Max(stepParcels, alive); stepDrawn = Math.Max(stepDrawn, drawn); }
                else if (v > 1.0) { taxiParcels = Math.Max(taxiParcels, alive); taxiDrawn = Math.Max(taxiDrawn, drawn); }

                if (Math.Abs(t * 60 % 120) < 0.5)
                    _out.WriteLine($"t={t,5:F1}s V={v * 2.237,5:F1}mph roots={field.LastRootCount,3} flux={field.LastFluxKgS,7:F2}kg/s visible={alive,4} drawn={drawn,4} onStep={onStep} wet={FloatHydro.Floats[0].Wet}");
                if (!FloatHydro.Floats[0].Wet && !FloatHydro.Floats[1].Wet && -ac.State.Position.Z - lane.SurfaceM > 1.6) { _out.WriteLine($"airborne at {t:F1}s"); break; }
            }
            _out.WriteLine($"peak flux {peakFlux:F1} kg/s; plow/taxi: {taxiParcels} visible, {taxiDrawn} drawn; on the step: {stepParcels} visible, {stepDrawn} drawn");
            Assert.True(peakFlux > 0.1, $"the island's lane threw only {peakFlux:F3} kg/s of spray");
            Assert.True(taxiParcels > 0, "no spray parcels at all while taxiing on the island's lane");
            Assert.True(taxiDrawn > 0, $"{taxiParcels} parcels in the air taxiing and NONE of them would be drawn");
            Assert.True(stepParcels > 0, "no spray parcels at all on the step");
            Assert.True(stepDrawn > 0, $"{stepParcels} parcels in the air on the step and NONE of them would be drawn");
        }
        finally { WorldTerrain.Active = null; }
    }

    [Theory]
    [InlineData(18, 3, .13, true, 1996, 1998, 444, 446)]
    [InlineData(6, 12, .20, false, 1306, 1308, 898, 900)]
    [InlineData(6, 6, .13, true, 508, 510, 330, 332)]
    [InlineData(8, 12, .20, false, 2246, 2248, 647, 649)]
    public void ActualJ3ContactsSelectStepAndPlowAtSharedSpeeds(double speed, double pitch,
        double immersion, bool step, double minLift, double maxLift, double minBuoy, double maxBuoy)
    {
        FloatHydro.FlatWaterOverride=0;
        try
        {
            var cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "j3-cub-floats-like.json"));
            var q = new Quat(0, Math.Sin(pitch*Math.PI/360), 0, Math.Cos(pitch*Math.PI/360));
            var state = new RigidBodyState(new Vec3(0,0,-cfg.Floats!.KeelZ+immersion),q,
                q.Conjugate().Rotate(new Vec3(speed,0,0)),Vec3.Zero);
            var forces = FloatHydro.Compute(cfg,state,0);
            for(int i=0;i<2;i++)
            {
                var f=FloatHydro.Floats[i];
                // Measured from the unchanged hydro probe, not re-read from the config under test.
                Assert.InRange(f.PlaningLiftN,minLift,maxLift); Assert.InRange(f.BuoyancyN,minBuoy,maxBuoy);
                Assert.Equal(step,FloatSprayVisual.IsOnStep(f));
                if(step) { Assert.Equal(1,FloatSprayVisual.StepFraction(f)); Assert.InRange(f.SternDraftM,-1,.005); }
                else { Assert.InRange(f.SternDraftM,.11,.18); Assert.Equal(0,FloatSprayVisual.StepFraction(f)); }
                _out.WriteLine($"{i}: prescribed={speed}m/s pitch={pitch}deg L={f.PlaningLiftN:F2}N B={f.BuoyancyN:F2}N vent={f.VentilatedFraction:F3} stern={f.SternDraftM:F4}m step={step} blend={FloatSprayVisual.StepFraction(f):F3}");
            }
            // Reading/blending the visual state has no feedback into force or moment.
            Assert.Equal(forces,FloatHydro.Compute(cfg,state,0));
        }
        finally {FloatHydro.FlatWaterOverride=null;}
    }

}
