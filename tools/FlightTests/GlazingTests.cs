using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Canopy, windshield and window glass in a crash (Ty 2026-11-05 ~13:32, Q028b: "canopy is very tough plastic; stays
/// attached if unlatched; breaks/shatters ONLY if it takes a hard enough DIRECT impact (not merely because of a crash)";
/// ~18:23: every aeroplane, not only the Pitts). Flat ground at 0 (WorldTerrain.Active = null). Expected values are
/// literals worked by hand: acrylic 10 ksi, 3/16 in, σ·t²/2.24 × 2; the tree's MOR 53 MPa, π·d²/64; the chosen closing
/// speeds 6 / 8 / 12 m/s.
/// </summary>
[Collection("WorldTerrainActive")]
public class GlazingTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public GlazingTests(ITestOutputHelper o) { _o = o; WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null; CrashEvents.ClearForTests(); }
    public void Dispose() { WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null; CrashEvents.ClearForTests(); }

    private static AircraftConfig Load(string n) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", n + ".json"));
    private static readonly Quat Inverted = new(1, 0, 0, 0);   // 180° about x

    /// <summary>The aeroplane at attitude <paramref name="att"/>, its lowest hard point 2 cm over the ground, moving down at
    /// <paramref name="sinkMs"/> and forward at <paramref name="fwdMs"/> (world).</summary>
    private static Aircraft Dropped(AircraftConfig c, Quat att, double sinkMs, double fwdMs = 0)
    {
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -50), att, Vec3.Zero, Vec3.Zero), ControlDeflections.Neutral);
        Vec3 cg = c.Mass.CgVec();
        double low = double.NegativeInfinity;
        foreach (ContactPoint p in ac.ContactPoints) if (!p.PropDisc) low = Math.Max(low, att.Rotate(p.Body - cg).Z);
        foreach (GearConfig g in c.Gear) low = Math.Max(low, att.Rotate(g.PosVec() - cg).Z);
        Vec3 vWorld = new(fwdMs, 0, sinkMs);
        ac.State = new RigidBodyState(new Vec3(0, 0, -(low + 0.02)), att, att.Conjugate().Rotate(vWorld), Vec3.Zero);
        return ac;
    }

    private static List<ContactEvent> Run(Aircraft ac, double seconds)
    {
        var ev = new List<ContactEvent>();
        ac.Contact += e => { if (e.Kind == ContactEventKind.GlazingShattered) ev.Add(e); };
        for (double t = 0; t < seconds; t += 0.005) ac.Step(new ControlInputs(0, 0, 0, -1), 0.005);
        return ev;
    }

    // ---- the material --------------------------------------------------------------------------------------------

    [Fact]
    public void AcrylicTakesAPointLoadOfAboutOnePointFourKilonewtons()
    {
        // 2 · 68.95 MPa · (4.7625 mm)² / 2.24 = 1,396 N (cast); 2 · 75.84 MPa · (6.35 mm)² / 2.24 = 2,730 N (stretched).
        Assert.Equal(1396, Glazing.FractureForceN(GlazingMaterial.CastAcrylic), 0);
        Assert.Equal(2730, Glazing.FractureForceN(GlazingMaterial.StretchedAcrylic), 0);
        Assert.Equal(6.0, Glazing.ShatterClosingMs(GlazingMaterial.CastAcrylic));
        Assert.Equal(8.0, Glazing.ShatterClosingMs(GlazingMaterial.StretchedAcrylic));
        Assert.Equal(12.0, Glazing.ShatterClosingMs(GlazingMaterial.LaminatedGlass));
        // A stem breaks cast acrylic above √(1396·64/(53e6·π)) = 2.32 cm: the soft top of a tree (2.15 cm 1.3 m down, Q141)
        // snaps on the glass and leaves it whole; a 2.5 cm stem does not.
        Assert.False(Glazing.ShattersOnStem(GlazingMaterial.CastAcrylic, 0.0215, 40));
        Assert.True(Glazing.ShattersOnStem(GlazingMaterial.CastAcrylic, 0.025, 40));
        Assert.False(Glazing.ShattersOnStem(GlazingMaterial.CastAcrylic, 0.10, 1.5));   // a walk-pace nudge
        Assert.False(Glazing.ShattersOnSurface(GlazingMaterial.CastAcrylic, 5.9));
        Assert.True(Glazing.ShattersOnSurface(GlazingMaterial.CastAcrylic, 6.1));
    }

    [Fact]
    public void EveryGlazedTypeHasPanesAndTheRestHaveNone()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "TestData");
        foreach (string f in Directory.GetFiles(dir, "*-like.json"))
        {
            AircraftConfig c = AircraftConfigLoader.LoadFromFile(f);
            var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -500), Quat.Identity, new Vec3(30, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
            _o.WriteLine($"{c.Id}: {ac.GlazingMaterial} {Glazing.EnclosureFor(c.Id)} [{string.Join(", ", ac.GlazingPanes.Select(p => p.Id))}]");
            if (c.Id is "hang-glider-like" or "paraglider-en-b-like" or "target-drone-like") Assert.Empty(ac.GlazingPanes);
            else Assert.NotEmpty(ac.GlazingPanes);
        }
        Assert.Equal(CockpitEnclosure.Open, Glazing.EnclosureFor("great-lakes-like"));
        Assert.Equal(CockpitEnclosure.Canopy, Glazing.EnclosureFor("pitts-s2b-like"));
        Assert.Equal(CockpitEnclosure.Cabin, Glazing.EnclosureFor("c172-like"));
        Assert.Equal(GlazingMaterial.StretchedAcrylic, Glazing.MaterialFor("p51d-like"));
        Assert.Equal(GlazingMaterial.LaminatedGlass, Glazing.MaterialFor("boeing-737-like"));
    }

    // ---- a crash is not a direct hit ------------------------------------------------------------------------------

    /// <summary>Upright onto the wheels hard enough to crush the fuselage and tear the gear off (12 m/s): the loads reach the
    /// glass only through its frame, and it stays whole.</summary>
    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("c172-like")]
    [InlineData("extra-300-like")]
    [InlineData("glider-2-33-like")]
    public void AHardUprightCrashLeavesTheGlassWhole(string id)
    {
        AircraftConfig c = Load(id);
        var ac = Dropped(c, Quat.Identity, 12.0, 25.0);
        var ev = Run(ac, 2.0);
        _o.WriteLine($"{id}: lost [{string.Join(",", ac.LostComponents)}], crush {ac.Crush.CabinCrushM:F3} m; glass {ev.Count}");
        Assert.NotEmpty(ac.LostComponents);   // it was a crash
        Assert.Empty(ev);
        Assert.All(ac.GlazingPanes, p => Assert.False(p.Shattered));
    }

    /// <summary>Over on its back slowly (a nose-over's 2.5 m/s): the canopy touches, and holds.</summary>
    [Fact]
    public void ASlowArrivalOnTheCanopyLeavesItWhole()
    {
        AircraftConfig c = Load("pitts-s2b-like");
        var ac = Dropped(c, Inverted, 2.5);
        var ev = Run(ac, 2.0);
        Assert.Empty(ev);
        Assert.All(ac.GlazingPanes, p => Assert.False(p.Shattered));
    }

    // ---- a direct hit -----------------------------------------------------------------------------------------------

    /// <summary>The Extra (a low wing: nothing above the canopy) on its back at 14 m/s: the canopy itself arrives on the ground: it shatters, stays on the aeroplane (the crash is
    /// exactly the crash without glass: nothing more is lost, no more mass shed; the frame and what is left of the glass ride on, latched or not) and opens the water's glazing
    /// hatch.</summary>
    [Fact]
    public void ADirectHitOnTheCanopyShattersItAndItStaysOn()
    {
        AircraftConfig c = Load("extra-300-like");
        var ac = Dropped(c, Inverted, 14.0);
        var bare = Dropped(c, Inverted, 14.0);
        bare.SetGlazing(Array.Empty<GlazingPane>());   // the same crash with no glass at all
        var ev = Run(ac, 1.0);
        Run(bare, 1.0);
        foreach (var e in ev) _o.WriteLine(e.ToString());
        Assert.Contains(ev, e => e.Part == "canopy");
        GlazingPane canopy = ac.GlazingPanes.First(p => p.Id == "canopy");
        Assert.True(canopy.Shattered);
        Assert.Equal("ground", canopy.ShatterBy);
        Assert.True(canopy.ShatterClosingMs > 6.0);
        // The glass is not a part that leaves: the crash is the glassless crash to the last bit.
        Assert.Equal(bare.ShedMassKg, ac.ShedMassKg);
        Assert.Equal(bare.LostComponents.OrderBy(x => x), ac.LostComponents.OrderBy(x => x));
        Assert.Equal(bare.State.Position.X, ac.State.Position.X);
        Assert.Equal(bare.State.Position.Z, ac.State.Position.Z);
        // The Extra's water model has no canopy opening of its own (config construction.cockpits empty): its glazing hatch is
        // the cabin's windshield, and that is what opens.
        Assert.Contains(ac.Hydro.Attached.SelectMany(h => h.Panels), p => (p.Why.StartsWith("canopy broken") || p.Why.StartsWith("windshield broken")) && p.Broken >= 1);
    }

    /// <summary>A pane measured on the drawn glass (Aircraft.SetGlazing) is what is checked: a pane whose point sits 1 cm
    /// outside the aeroplane's lowest hard point on its back meets the ground first, at the sink speed, and shatters above
    /// cast acrylic's 6 m/s; at 5 m/s it holds.</summary>
    [Theory]
    [InlineData(7.0, true)]
    [InlineData(5.0, false)]
    public void AMeasuredPaneShattersAtTheMaterialsSpeed(double sink, bool shatters)
    {
        AircraftConfig c = Load("pitts-s2b-like");
        var ac = Dropped(c, Inverted, sink);
        Vec3 cg = c.Mass.CgVec();
        ContactPoint low = ac.ContactPoints.Where(p => !p.PropDisc).OrderByDescending(p => Inverted.Rotate(p.Body - cg).Z).First();
        ac.SetGlazing(new[] { Glazing.Pane("Canopy_Glass", GlazingKind.Canopy, new[] { low.Body + new Vec3(0, 0, -0.01) }) });
        var ev = Run(ac, 1.0);
        _o.WriteLine($"lowest {low.Name}; {string.Join("; ", ev)}");
        Assert.Equal(shatters, ev.Any(e => e.Part == "Canopy_Glass"));
    }

    /// <summary>A pane point 1.5 m ahead of the nose (clear of the nose edge's own 0.5 m radius) and a metre above the CG (so the glass, not the fuselage, is what meets
    /// the stem first) flown at 40 m/s into a 30 m fir: where the stem there is 15 cm through (21 m up: 0.5·(1 − 21/30)) the
    /// glass shatters by the tree; where it is 1.5 cm (29.1 m up), the stem snaps on it and the glass holds.</summary>
    [Theory]
    [InlineData(21.0, true)]
    [InlineData(29.1, false)]
    public void AStemShattersTheGlassOnlyWhereItIsStrong(double paneUpM, bool shatters)
    {
        AircraftConfig c = Load("pitts-s2b-like");
        Vec3 cg = c.Mass.CgVec();
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -(paneUpM - 1.0)), Quat.Identity, new Vec3(40, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
        double noseX = ac.ContactPoints.First(p => p.Name == "nose").Body.X;
        var pt = new Vec3(noseX + 1.5, 0, cg.Z - 1.0);
        ac.SetGlazing(new[] { Glazing.Pane("canopy", GlazingKind.Canopy, new[] { pt }) });
        var tree = new Forest.Tree(6.0, 0.0, 0.0, 30.0, 0.22 + 30.0 / 24.0, 0, 0);
        ac.Trees = new FixedTrees(tree);
        _o.WriteLine($"stem at {paneUpM} m: {TreeStructure.DiameterAt(tree, paneUpM):F3} m");
        var ev = Run(ac, 0.4);
        foreach (var e in ev) _o.WriteLine(e.ToString());
        Assert.Equal(shatters, ev.Any(e => e.Part == "canopy"));
        if (shatters) Assert.Equal("tree", ac.GlazingPanes[0].ShatterBy);
    }

    /// <summary>The water's slam bursts the canopy hatch (WaterSlam): the drawn glass shatters with it.</summary>
    [Fact]
    public void TheWatersBurstCanopyHatchShattersTheGlass()
    {
        AircraftConfig c = Load("pitts-s2b-like");
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -100), Quat.Identity, new Vec3(30, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
        _ = ac.GlazingPanes;
        ac.Hydro.DamageLog.Add("canopy broken");
        var ev = Run(ac, 0.01);
        Assert.Contains(ev, e => e.Part == "canopy" && e.SurfaceCode == ContactPoint.SurfaceWater);
    }

    /// <summary>Across the fleet, on its back, with the headless estimated panes (the game measures the drawn glass; the
    /// Pitts' measured canopy is under its upper wing too, GlassShatterShots): at 14 m/s every type whose glass is the lowest
    /// thing on its back breaks some; a high wing or a biplane's upper wing holds the glass off the ground, and it stays whole
    /// (the Beavers, the Decathlon, the Super Cubs, the Aircams, the JN-4, the Great Lakes, the Stearman and the Pitts, from
    /// this test's run of 2026-11-06); and at
    /// 2 m/s no type breaks any.</summary>
    [Fact]
    public void OnItsBackGlassBreaksOnlyWhereItIsWhatHitsAndNeverOnATouch()
    {
        var shielded = new HashSet<string> { "dhc2-beaver-like", "dhc2-beaver-floats-like", "decathlon-8kcab-like", "pa18-cub-like", "pa18-bush-like", "pa18-floats-like",
            "aircam-like", "aircam-amphib-like", "jn4-jenny-like", "great-lakes-like", "stearman-pt17-like", "pitts-s2b-like" };
        string dir = Path.Combine(AppContext.BaseDirectory, "TestData");
        var wrong = new List<string>(); var soft = new List<string>();
        foreach (string f in Directory.GetFiles(dir, "*-like.json"))
        {
            AircraftConfig c = AircraftConfigLoader.LoadFromFile(f);
            if (Glazing.MaterialFor(c.Id) == GlazingMaterial.None) continue;
            CrashEvents.ClearForTests();
            var hard = Dropped(c, Inverted, 14.0);
            int n = Run(hard, 0.8).Count;
            var gentle = Dropped(c, Inverted, 2.0);
            int m = Run(gentle, 0.8).Count;
            _o.WriteLine($"{c.Id}: 14 m/s → {n} panes [{string.Join(",", hard.GlazingLog.Select(p => $"{p.Id} {p.ShatterClosingMs:F1}"))}]; 2 m/s → {m}");
            if ((n == 0) != shielded.Contains(c.Id)) wrong.Add(c.Id);
            if (m > 0) soft.Add(c.Id);
        }
        Assert.True(wrong.Count == 0, "glass broke where a wing shields it, or held where it is what hits: " + string.Join(", ", wrong));
        Assert.True(soft.Count == 0, "glass broke on a 2 m/s touch: " + string.Join(", ", soft));
    }
}
