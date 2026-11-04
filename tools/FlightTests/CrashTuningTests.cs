using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Wreck;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty's flight test of 2026.1104.0427: "momentum is still too weak or the friction is too high. The fuselage is really
/// strong so rarer than having it break apart have it tend to bend and crush more. Engine, wings, tails and canopy can break
/// off but for all steel tube airplanes like Pitts, Extra, Cub, Great Lakes etc. the fuselage doesn't break apart other than
/// at the firewall. Tree hits were too forgiving: it made a sound with no damage until I got deep toward the trunks."
///
/// Flat ground at 0 (WorldTerrain.Active = null) with the surface forced where a test is about a surface; every expected
/// value is a literal worked by hand (μ from the table in WorldTerrain.KeelSlidingCoefficient, g 9.81, the crush law's
/// δ₀ 0.15 m and capacities, TreeStructure's constants).
/// </summary>
[Collection("WorldTerrainActive")]
public class CrashTuningTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public CrashTuningTests(ITestOutputHelper o) { _o = o; WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null; CrashEvents.ClearForTests(); }
    public void Dispose() { WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null; CrashEvents.ClearForTests(); }

    private static AircraftConfig Load(string n) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", n + ".json"));
    private static double Horizontal(Vec3 a, Vec3 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    // ---- 1. momentum and sliding ------------------------------------------------------------------------------------

    [Fact]
    public void SlidingCoefficientsAreMetalOnGround()
    {
        // Literals: metal on asphalt 0.55, gravel 0.50, dry grass 0.35, the island's green pasture (Rough) 0.45, snow 0.10.
        Assert.Equal(0.55, AirframeContact.SlidingMu(WorldTerrain.Surface.Paved));
        Assert.Equal(0.50, AirframeContact.SlidingMu(WorldTerrain.Surface.Gravel));
        Assert.Equal(0.35, AirframeContact.SlidingMu(WorldTerrain.Surface.Grass));
        Assert.Equal(0.45, AirframeContact.SlidingMu(WorldTerrain.Surface.Rough));
        Assert.Equal(0.10, AirframeContact.SlidingMu(WorldTerrain.Surface.Snow));
    }

    /// <summary>A Pitts fuselage with its wings, gear and propeller gone, put down level at 60 kt (30.87 m/s): Coulomb says
    /// it slides v²/(2μg) — 138.8 m on grass (μ 0.35), 88.3 m on asphalt (0.55). Its own air drag shortens that a little.</summary>
    [Theory]
    [InlineData(WorldTerrain.Surface.Grass, 138.8)]
    [InlineData(WorldTerrain.Surface.Paved, 88.3)]
    public void ASixtyKnotSlideGoesAsFarAsCoulombSays(WorldTerrain.Surface surface, double coulombM)
    {
        WorldTerrain.SurfaceOverride = surface;
        AircraftConfig c = Load("pitts-s2b-like");
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -50), Quat.Identity, new Vec3(30.87, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
        foreach (var comp in new[] { AirframeComponent.WingLeft, AirframeComponent.WingRight, AirframeComponent.GearLeft, AirframeComponent.GearRight, AirframeComponent.GearTail, AirframeComponent.Propeller })
            ac.LoseComponent(comp);
        double low = double.NegativeInfinity; Vec3 cg = c.Mass.CgVec();
        foreach (ContactPoint p in ac.ContactPoints) if (!p.PropDisc) low = Math.Max(low, p.Body.Z - cg.Z);
        ac.State = new RigidBodyState(new Vec3(0, 0, -(low + 0.02)), Quat.Identity, new Vec3(30.87, 0, 0), Vec3.Zero);
        Vec3 p0 = ac.State.Position; double t = 0;
        while (t < 30)
        {
            ac.Step(new ControlInputs(0, 0, 0, 0), 0.005); t += 0.005;
            Vec3 v = ac.State.Attitude.Rotate(ac.State.Velocity);
            if (Math.Sqrt(v.X * v.X + v.Y * v.Y) < 0.3) break;
        }
        double dist = Horizontal(ac.State.Position, p0);
        _o.WriteLine($"{surface}: {dist:F1} m in {t:F2} s (Coulomb {coulombM} m); lost [{string.Join(",", ac.LostComponents)}]");
        Assert.InRange(dist, 0.88 * coulombM, 1.02 * coulombM);
        Assert.DoesNotContain(AirframeComponent.Cabin, ac.LostComponents);
    }

    private static WreckPiece Block(WreckSim w, double massKg, Vec3 vel, Vec3 rates, double up)
    {
        var e = new HydroElement("block", WreckAtom.Cabin, Vec3.Zero, new Vec3(0.4, 0.3, 0.25), 0.24, massKg, 0.24);
        return w.Add("block", AirframeComponent.Nose, new List<HydroElement> { e }, Vec3.Zero, new RigidBodyState(new Vec3(0, 0, -up), Quat.Identity, vel, rates));
    }

    /// <summary>A wreck piece (a 500 kg engine-sized block) slides on the SURFACE it is on: on grass at 60 kt it goes
    /// roughly v²/(2·0.35·g) = 138.8 m, less its air drag. With the old μ 0.7 everywhere it stopped in under 69.4 m.</summary>
    [Fact]
    public void AWreckPieceSlidesOnItsOwnSurfacesFriction()
    {
        var w = new WreckSim(new FlatWreckWorld(null, 0.0) { Surface = WorldTerrain.Surface.Grass });
        WreckPiece p = Block(w, 500, new Vec3(30.87, 0, 0), Vec3.Zero, 0.25);
        Vec3 p0 = p.State.Position;
        for (int i = 0; i < 6000 && !(p.Phase == WreckPhase.OnLand && p.WorldVelocity.Length < 0.05); i++) w.StepOnce();
        double dist = Horizontal(p.State.Position, p0);
        _o.WriteLine($"block slid {dist:F1} m, phase {p.Phase}, asleep {p.Asleep}");
        Assert.InRange(dist, 115.0, 140.0);
    }

    /// <summary>Restitution 0.2 above 1 m/s: a block arriving flat at 5 m/s leaves at 1–1.3 m/s (each corner bounces at 0.2 of
    /// its own arrival, and four corners' impulses in sequence overshoot a little); at 0.5 m/s it stays.</summary>
    [Fact]
    public void AWreckPieceBouncesALittleAndRestsQuietly()
    {
        var w = new WreckSim(new FlatWreckWorld(null, 0.0));
        WreckPiece p = Block(w, 200, new Vec3(0, 0, 5.0), Vec3.Zero, 0.26);
        double up = 0;
        for (int i = 0; i < 40; i++) { w.StepOnce(); up = Math.Min(up, p.WorldVelocity.Z); }
        _o.WriteLine($"5 m/s arrival: leaves at {-up:F2} m/s");
        Assert.InRange(-up, 0.75, 1.5);   // e 0.15–0.3: the corners' sequential impulses give 0.25
        var w2 = new WreckSim(new FlatWreckWorld(null, 0.0));
        WreckPiece q = Block(w2, 200, new Vec3(0, 0, 0.5), Vec3.Zero, 0.252);
        double up2 = 0;
        for (int i = 0; i < 40; i++) { w2.StepOnce(); up2 = Math.Min(up2, q.WorldVelocity.Z); }
        Assert.True(-up2 < 0.05, $"a 0.5 m/s arrival left at {-up2:F3} m/s");
    }

    /// <summary>A tumbling piece in the air keeps its angular momentum: no sleep or clamp touches it. What it does lose is
    /// its own aerodynamic damping (HydroBody's box-face drag of the turning faces), which a 500 kg block barely feels: a
    /// 60 kg block of the same size loses 7 % a second to it, real air on a tumbling box.</summary>
    [Fact]
    public void ATumblingPieceKeepsItsAngularMomentum()
    {
        var w = new WreckSim(new FlatWreckWorld(null, -10000.0));
        WreckPiece p = Block(w, 500, new Vec3(30, 0, 0), new Vec3(4.0, 1.5, -2.0), 1000);
        Vec3 L(WreckPiece x) { MassProperties mp = x.MassProps; Vec3 wb = x.State.Rates; return x.State.Attitude.Rotate(new Vec3(mp.Ixx * wb.X, mp.Iyy * wb.Y, mp.Izz * wb.Z)); }
        Vec3 l0 = L(p);
        for (int i = 0; i < 200; i++) w.StepOnce();
        Vec3 l1 = L(p);
        _o.WriteLine($"|L| {l0.Length:F3} -> {l1.Length:F3}, direction change {(l1 - l0).Length / l0.Length:E2}");
        Assert.True((l1 - l0).Length / l0.Length < 0.02, "angular momentum not kept");
        Assert.False(p.Asleep);
    }

    /// <summary>A part that breaks off leaves with exactly the velocity its parts had, v + ω × r, and the rest keeps its
    /// momentum: nothing slows at a separation.</summary>
    [Theory]
    [InlineData("pitts-s2b-like", AirframeComponent.WingTipRight)]
    [InlineData("c172-like", AirframeComponent.TailBoom)]
    [InlineData("pitts-s2b-like", AirframeComponent.Nose)]
    public void NothingLosesSpeedAtASeparation(string cfg, AirframeComponent comp)
    {
        AircraftConfig c = Load(cfg);
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -200), Quat.Identity, new Vec3(45, 2, 3), new Vec3(1.2, -0.4, 0.7)), ControlDeflections.Neutral);
        var relay = new CrashRelay(ac, new FlatWreckWorld(null, 0.0));
        Vec3 pBefore = ac.State.Attitude.Rotate(ac.State.Velocity + Vec3.Cross(ac.State.Rates, ac.CgShiftBody)) * ac.MassProperties.MassKg;
        RigidBodyState s = ac.State;
        ac.LoseComponent(comp);
        WreckPiece piece = relay.Wreck.Pieces.Single();
        Vec3 r = piece.CgBody - c.Mass.CgVec();
        Vec3 expect = s.Attitude.Rotate(s.Velocity + Vec3.Cross(s.Rates, r));
        Assert.True((piece.WorldVelocity - expect).Length < 1e-9, $"piece left at {piece.WorldVelocity} not {expect}");
        Vec3 pAfter = ac.State.Attitude.Rotate(ac.State.Velocity + Vec3.Cross(ac.State.Rates, ac.CgShiftBody)) * ac.MassProperties.MassKg + piece.WorldVelocity * piece.MassKg;
        Assert.True((pAfter - pBefore).Length / pBefore.Length < 1e-9, "linear momentum changed at the separation");
    }

    // ---- 2. structure type -----------------------------------------------------------------------------------------

    /// <summary>Every aeroplane is tagged; the steel-tube list is the roster's tube-and-fabric types (literals).</summary>
    [Fact]
    public void EveryAeroplaneIsTaggedAndTheSteelTubeTypesAreTheTubeAndFabricOnes()
    {
        var steel = new SortedSet<string>
        {
            "cassutt-f1-like", "decathlon-8kcab-like", "extra-300-like", "geebee-r2-like", "glider-2-33-like", "great-lakes-like",
            "j3-cub-floats-like", "j3-cub-like", "pa18-bush-like", "pa18-cub-like", "pa18-floats-like", "pa25-pawnee-like",
            "pitts-s2b-like", "stearman-pt17-like",
        };
        var wood = new SortedSet<string> { "hughes-h4-like", "jn4-jenny-like" };
        var composite = new SortedSet<string> { "cirrus-sr22-like", "glasair3-like", "glider-eb29r-like", "glider-swift-s1-like", "paraglider-en-b-like", "target-drone-like" };
        string dir = Path.Combine(AppContext.BaseDirectory, "TestData");
        int n = 0;
        foreach (string f in Directory.GetFiles(Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "..", "..", "..", "configs", "aircraft")), "*.json"))
        {
            AircraftConfig c = AircraftConfigLoader.LoadFromFile(f);
            string id = Path.GetFileNameWithoutExtension(f);
            StructureType t = FuselageStructure.Of(c);
            Assert.NotEqual(StructureType.Untagged, t);
            Assert.False(string.IsNullOrWhiteSpace(c.StructureTypeSource), $"{id}: no structureTypeSource");
            StructureType want = steel.Contains(id) ? StructureType.SteelTube : wood.Contains(id) ? StructureType.Wood
                : composite.Contains(id) ? StructureType.Composite : StructureType.AluminumMonocoque;
            Assert.True(want == t, $"{id}: {t}, expected {want}");
            n++;
        }
        Assert.Equal(34, n);
    }

    /// <summary>The crush law per type: δ = 0.15·(v²/v_b² − 1); one arrival breaks a section at v_b·√(1 + capacity/0.15).</summary>
    [Fact]
    public void EachStructureTypeCrushesBeforeItBreaks()
    {
        Assert.Equal(0.15, FuselageStructure.CrushFrom(8.0 * Math.Sqrt(2.0), 8.0), 9);
        Assert.Equal(0.0, FuselageStructure.CrushFrom(7.9, 8.0));
        // Cabin, v_b 8 m/s: untagged breaks at 8 (the old rule), aluminium at 11.31, wood at 9.906, composite at 9.24, steel never.
        Assert.Equal(8.000, FuselageStructure.SingleBlowBreakMs(StructureType.Untagged, AirframeComponent.Cabin, 8.0), 3);
        Assert.Equal(11.314, FuselageStructure.SingleBlowBreakMs(StructureType.AluminumMonocoque, AirframeComponent.Cabin, 8.0), 3);
        Assert.Equal(9.906, FuselageStructure.SingleBlowBreakMs(StructureType.Wood, AirframeComponent.Cabin, 8.0), 3);
        Assert.Equal(9.238, FuselageStructure.SingleBlowBreakMs(StructureType.Composite, AirframeComponent.Cabin, 8.0), 3);
        Assert.True(double.IsPositiveInfinity(FuselageStructure.SingleBlowBreakMs(StructureType.SteelTube, AirframeComponent.Cabin, 8.0)));
        Assert.True(double.IsPositiveInfinity(FuselageStructure.SingleBlowBreakMs(StructureType.SteelTube, AirframeComponent.TailBoom, 5.5)));
        // Steel tube: the engine mount bends 0.20 m and then the engine leaves at the firewall: nose v_b 6 → 9.17 m/s.
        Assert.Equal(9.165, FuselageStructure.SingleBlowBreakMs(StructureType.SteelTube, AirframeComponent.Nose, 6.0), 3);
        // Water joints (no arrival speed): a steel-tube cabin and tail boom hold, its engine mount and every other type's sections do not.
        Assert.False(new FuselageCrush(StructureType.SteelTube).BreaksUnderLoad(AirframeComponent.TailBoom));
        Assert.True(new FuselageCrush(StructureType.SteelTube).BreaksUnderLoad(AirframeComponent.Nose));
        Assert.True(new FuselageCrush(StructureType.AluminumMonocoque).BreaksUnderLoad(AirframeComponent.TailBoom));
        // Crush accumulates: two 10 m/s arrivals on an aluminium cabin (0.084 m each) pass its 0.15 m on the second.
        var al = new FuselageCrush(StructureType.AluminumMonocoque);
        Assert.False(al.Absorb(AirframeComponent.Cabin, 10.0, 8.0, 300, out double d1));
        Assert.Equal(0.084375, d1, 6);
        Assert.True(al.Absorb(AirframeComponent.Cabin, 10.0, 8.0, 300, out _));
    }

    /// <summary>Drop an aeroplane with no gear flat onto its belly at <paramref name="sinkMs"/> (20 m/s forward, on grass).</summary>
    private static Aircraft BellySlam(string cfg, double sinkMs, double pitchDeg = 0, bool inverted = false)
    {
        WorldTerrain.SurfaceOverride = WorldTerrain.Surface.Grass;
        AircraftConfig c = Load(cfg);
        double th = pitchDeg * Math.PI / 180;
        Quat q = new(0, Math.Sin(th / 2), 0, Math.Cos(th / 2));
        if (inverted) q = Quat.Multiply(q, new Quat(1, 0, 0, 0));
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -50), q, Vec3.Zero, Vec3.Zero), ControlDeflections.Neutral);
        foreach (var g in new[] { AirframeComponent.GearLeft, AirframeComponent.GearRight, AirframeComponent.GearTail, AirframeComponent.GearNose, AirframeComponent.Propeller })
            ac.LoseComponent(g);
        Vec3 cg = c.Mass.CgVec();
        double low = double.NegativeInfinity;
        foreach (ContactPoint p in ac.ContactPoints) if (!p.PropDisc) low = Math.Max(low, q.Rotate(p.Body - cg).Z);
        Vec3 vw = new(20, 0, sinkMs);
        ac.State = new RigidBodyState(new Vec3(0, 0, -(low + 0.01)), q, q.Conjugate().Rotate(vw), Vec3.Zero);
        var sim = new SimLoop(ac);
        sim.RunFor(1.5, new ControlInputs(0, 0, 0, -1));
        return ac;
    }

    /// <summary>Steel tube: a 15 m/s slam onto the belly — or onto its back, for the biplanes, whose gear stubs and lower
    /// wing take a belly drop — crushes the cabin and keeps the aeroplane in one piece; the same slam on an aluminium C172
    /// parts its fuselage (15 &gt; 11.3), and at 10 m/s the C172's cabin crushes and holds (the old rule broke it at 8).</summary>
    [Theory]
    [InlineData("pitts-s2b-like", 15.0, true, false)]
    [InlineData("great-lakes-like", 15.0, true, false)]
    [InlineData("extra-300-like", 15.0, false, false)]
    [InlineData("j3-cub-like", 15.0, false, false)]
    [InlineData("decathlon-8kcab-like", 15.0, false, false)]
    [InlineData("pa18-cub-like", 15.0, true, false)]
    [InlineData("c172-like", 15.0, false, true)]
    [InlineData("c172-like", 10.0, false, false)]
    public void ABellySlamCrushesTheFuselageAndOnlyAluminiumPartsAtTheHardOne(string cfg, double sink, bool inverted, bool parts)
    {
        Aircraft ac = BellySlam(cfg, sink, 0, inverted);
        _o.WriteLine($"{cfg} at {sink} m/s{(inverted ? " inverted" : "")}: {ac.Crush.Type}, cabin crush {ac.Crush.CabinCrushM:F3} m, tail {ac.Crush.TailCrushM:F3}, nose {ac.Crush.NoseCrushM:F3}, " +
                     $"absorbed {ac.Crush.AbsorbedJ / 1000:F1} kJ; lost [{string.Join(",", ac.LostComponents)}]");
        bool fuselageParted = ac.LostComponents.Contains(AirframeComponent.Cabin) || ac.LostComponents.Contains(AirframeComponent.TailBoom);
        Assert.Equal(parts, fuselageParted);
        if (!parts) Assert.True(ac.Crush.CabinCrushM > 0, "the slam must crush the cabin");
    }

    /// <summary>Steel tube: a tail-first arrival at 12 m/s (tail cone break speed 5.5) bends the tail boom up and turns the
    /// tailplane with it — it does not come off. (Not the Pitts: with its tailwheel gone, the leg's stub stands 29 cm below
    /// its tail cone at this attitude and takes the arrival, as the real spring leg would.)</summary>
    [Theory]
    [InlineData("pa18-cub-like", 14.0)]
    [InlineData("decathlon-8kcab-like", 14.0)]
    [InlineData("j3-cub-like", 14.0)]
    public void ASteelTubeTailStrikeBendsTheTailInsteadOfBreakingIt(string cfg, double pitchDeg)
    {
        Aircraft ac = BellySlam(cfg, 12.0, pitchDeg: pitchDeg);
        _o.WriteLine($"{cfg}: tail crush {ac.Crush.TailCrushM:F3} m, bend pitch {ac.Crush.TailBendPitchRad * 180 / Math.PI:F1}° yaw {ac.Crush.TailBendYawRad * 180 / Math.PI:F1}°; lost [{string.Join(",", ac.LostComponents)}]");
        Assert.DoesNotContain(AirframeComponent.TailBoom, ac.LostComponents);
        Assert.DoesNotContain(AirframeComponent.Cabin, ac.LostComponents);
        Assert.True(ac.Crush.TailCrushM > 0, "the tail strike must crush the tail boom");
        Assert.True(ac.Crush.TailBendPitchRad > 0.01, "the tail must bend UP");
        Assert.True(ac.Crush.TailBendPitchRad <= FuselageStructure.MaxBendRad + 1e-9);
    }

    /// <summary>Steel tube: a nose-first arrival well past the engine mount's 9.2 m/s takes the engine off at the firewall —
    /// the one fuselage separation Ty allows — and nothing else of the fuselage.</summary>
    [Fact]
    public void ASteelTubeNoseInTakesTheEngineOffAtTheFirewallOnly()
    {
        Aircraft ac = BellySlam("pitts-s2b-like", 16.0, pitchDeg: -70);
        _o.WriteLine($"nose crush {ac.Crush.NoseCrushM:F3}; lost [{string.Join(",", ac.LostComponents)}]");
        Assert.Contains(AirframeComponent.Nose, ac.LostComponents);
        Assert.DoesNotContain(AirframeComponent.Cabin, ac.LostComponents);
        Assert.DoesNotContain(AirframeComponent.TailBoom, ac.LostComponents);
    }

    /// <summary>A steel-tube cabin into a trunk is stopped and crushed, never broken (an aluminium one at the same speed parts).</summary>
    [Theory]
    [InlineData("pitts-s2b-like", false)]
    [InlineData("c172-like", true)]
    public void AFuselageIntoATrunk(string cfg, bool parts)
    {
        AircraftConfig c = Load(cfg);
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -4), Quat.Identity, new Vec3(30, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
        ac.Trees = new FixedTrees(new Forest.Tree(12, 0, 0.0, 30, 0.22 + 30 / 24.0, 0, 0));
        var relay = new CrashRelay(ac, new FlatWreckWorld(null, 0.0));
        new SimLoop(ac).RunFor(0.6, new ControlInputs(0, 0, 0, -1));
        foreach (var r in ac.TreeStrikeLog) _o.WriteLine(r.ToString());
        _o.WriteLine($"cabin crush {ac.Crush.CabinCrushM:F3}, nose {ac.Crush.NoseCrushM:F3}; lost [{string.Join(",", ac.LostComponents)}]");
        Assert.Equal(parts, ac.LostComponents.Contains(AirframeComponent.Cabin) || ac.LostComponents.Contains(AirframeComponent.TailBoom));
    }

    // ---- 3. trees ---------------------------------------------------------------------------------------------------

    [Fact]
    public void BranchesThinFromTheStemToTheCrownEdge()
    {
        var t = new Forest.Tree(0, 0, 0.0, 30, 0.22 + 30 / 24.0, 0, 0);
        // At 15 m: stem 0.25 m, crown radius 3.528·15/24 = 2.205 m. Branch 0.005 + 0.2·0.25 = 0.055 m at the stem, the twig
        // 0.005 m at the edge, 0.030 half way.
        Assert.Equal(2.205, TreeStructure.CrownRadiusAt(t, 15.0), 3);
        Assert.Equal(0.055, TreeStructure.BranchDiameterAt(t, 15.0, 0.0), 6);
        Assert.Equal(0.030, TreeStructure.BranchDiameterAt(t, 15.0, 1.1025), 6);
        Assert.Equal(0.0, TreeStructure.BranchDiameterAt(t, 15.0, 2.3));
        // λ = 10/(π·2.205) = 1.4436 m of branch per m³; a 1 m edge at 60 m/s crosses (2/π)·1.4436·60 = 55.1 a second.
        Assert.Equal(1.4436, TreeStructure.BranchLengthDensity(t, 15.0), 4);
        Assert.Equal(55.14, TreeStructure.CrossingsPerS(1.4436, 1.0, 60.0), 2);
        // A 2 cm branch at 60 m/s: 53e6·π·0.0004/64 + 600·0.0004·3600 = 1040.65 + 864 = 1904.65 N (dents a leading edge);
        // a 1 cm twig at 20 m/s: 260.16 + 24 = 284.16 N (does not).
        Assert.Equal(1904.65, TreeStructure.BranchStrikeForceN(0.02, 60.0), 2);
        Assert.Equal(284.16, TreeStructure.BranchStrikeForceN(0.01, 20.0), 2);
        Assert.True(TreeStructure.BranchStrikeForceN(0.02, 60.0) > BranchStrike.LeadingEdgeDentN);
        Assert.True(TreeStructure.BranchStrikeForceN(0.01, 20.0) < BranchStrike.LeadingEdgeDentN);
    }

    /// <summary>Fly north past a 30 m fir standing beyond the left wingtip, the tip at <paramref name="rhoFrac"/> of the
    /// crown's radius from the stem at 15 m, for 40 m of travel; and a twin with no tree.</summary>
    private (Aircraft ac, Aircraft twin, int events) CrownPass(string cfg, double v, double rhoFrac, double up = 15.0)
    {
        AircraftConfig c = Load(cfg);
        double rc = 3.528 * (30 - up) / 24.0;
        double semi = WingPanels.Semispan(c) + 0.3;
        var tree = new Forest.Tree(20, -(semi + rhoFrac * rc), 0.0, 30, 0.22 + 30 / 24.0, 0, 0);
        RigidBodyState s0 = new(new Vec3(0, 0, -up), Quat.Identity, new Vec3(v, 0, 0), Vec3.Zero);
        var ac = new Aircraft(c, s0, ControlDeflections.Neutral) { Trees = new FixedTrees(tree) };
        var twin = new Aircraft(c, s0, ControlDeflections.Neutral);
        var relay = new CrashRelay(ac, new FlatWreckWorld(null, 0.0));
        int events = 0; ac.Contact += e => { if (e.Kind == ContactEventKind.BranchDamage) events++; };
        new SimLoop(ac).RunFor(40.0 / v, new ControlInputs(0, 0, 0, -1));
        new SimLoop(twin).RunFor(40.0 / v, new ControlInputs(0, 0, 0, -1));
        _o.WriteLine($"{cfg} {v} m/s tip at {rhoFrac:F2} rc: Δv {twin.State.Velocity.Length - ac.State.Velocity.Length:F2} m/s, strip damage {ac.MaxBranchDamage:F3}, " +
                     $"{events} damage events, prop R {ac.PropRadiusM():F3}/{twin.PropRadiusM():F3}; lost [{string.Join(",", ac.LostComponents)}]");
        foreach (var r in ac.TreeStrikeLog) _o.WriteLine("   " + r);
        return (ac, twin, events);
    }

    /// <summary>Through the canopy at cruise, half way in: dented leading edges and torn fabric you can measure (strip lift
    /// down, drag up), and the drag of the branches; nothing comes off.</summary>
    [Fact]
    public void ACanopyPassAtCruiseDamagesAndDrags()
    {
        (Aircraft ac, Aircraft twin, int events) = CrownPass("pitts-s2b-like", 60.0, 0.35);
        Assert.True(ac.MaxBranchDamage >= 0.1, $"strip damage {ac.MaxBranchDamage:F3}");
        Assert.True(events > 0, "no BranchDamage events");
        Assert.True(twin.State.Velocity.Length - ac.State.Velocity.Length > 0.2, "the canopy must drag");
        Assert.Empty(ac.LostComponents);
        double[] lf = ac.FlowStateForTests.LiftFactor!;
        Assert.Contains(lf, x => x < 0.97);
    }

    /// <summary>A slow brush through the outer twigs (a J-3 at 20 m/s, its tip in the outer 15 % of the crown): drag and
    /// nothing more than minor damage.</summary>
    [Fact]
    public void ASlowBrushThroughTwigsIsMinor()
    {
        (Aircraft ac, Aircraft twin, _) = CrownPass("j3-cub-like", 20.0, 0.85);
        Assert.True(ac.MaxBranchDamage < 0.05, $"strip damage {ac.MaxBranchDamage:F3}");
        Assert.Empty(ac.LostComponents);
        Assert.Equal(twin.PropRadiusM(), ac.PropRadiusM(), 6);
    }

    /// <summary>The same 30 m fir's outer twigs at cruise: still no dents (the twigs are under the dent force), a little drag.</summary>
    [Fact]
    public void OuterTwigsAtCruiseOnlyDrag()
    {
        (Aircraft ac, _, _) = CrownPass("pitts-s2b-like", 60.0, 0.9);
        Assert.True(ac.MaxBranchDamage < 0.05, $"strip damage {ac.MaxBranchDamage:F3}");
        Assert.Empty(ac.LostComponents);
    }

    /// <summary>Deep in a big tree's lower crown at speed: a limb thick enough breaks the wing panel that meets it.</summary>
    [Fact]
    public void AHighEnergyLimbHitCanBreakAWing()
    {
        AircraftConfig c = Load("pitts-s2b-like");
        // A 40 m fir's lower crown, 10 m up: stem 0.500 m, limbs at the stem 0.005 + 0.2·0.5 = 0.105 m, ~0.085 m where the outer
        // third of the panel meets them (F ≈ 19 kN to snap + 22 kN to throw at 70 m/s: more than the panel's ~30 kN in plane).
        // The left tips pass 0.35 m from the stem. (Higher in the same tree, 15 m up, the limbs there — 5–7 cm — snap instead.)
        double semi = WingPanels.Semispan(c);
        var tree = new Forest.Tree(20, -(semi + 0.35), 0.0, 40, 0.22 + 40 / 24.0, 0, 0);
        RigidBodyState s0 = new(new Vec3(0, 0, -10), Quat.Identity, new Vec3(70, 0, 0), Vec3.Zero);
        var ac = new Aircraft(c, s0, ControlDeflections.Neutral) { Trees = new FixedTrees(tree) };
        var relay = new CrashRelay(ac, new FlatWreckWorld(null, 0.0));
        new SimLoop(ac).RunFor(0.6, new ControlInputs(0, 0, 0, -1));
        foreach (var r in ac.TreeStrikeLog) _o.WriteLine("   " + r);
        _o.WriteLine($"lost [{string.Join(",", ac.LostComponents)}]");
        Assert.Contains(ac.TreeStrikeLog, r => r.Edge.StartsWith("limb:") && r.Outcome == TreeStrikeOutcome.PartBroken);
        Assert.Contains(ac.LostComponents, x => x is AirframeComponent.WingTipLeft or AirframeComponent.WingTipLowerLeft or AirframeComponent.WingLeft);
    }
}
