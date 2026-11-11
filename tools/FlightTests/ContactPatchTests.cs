using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Contact patches, wing-panel damage, slave struts and prop strikes (Ty 2026-11-03: "when the pitts wing touches the
/// ground it goes through the surface … if it scrapes too hard it breaks that lower wing panel off outboard of the I
/// strut … the upper aileron just trails in the wind stick free"; a medium strike bends the panel 5°, a bent panel goes
/// 5° at 1 g to 15° at 2 g and snaps past that; the slave strut keeps working through a bend).
/// Expected values are literals copied by hand from the configs and the own-model builders.
/// </summary>
[Collection("WorldTerrainActive")]
public class ContactPatchTests : IDisposable
{
    public void Dispose() { WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null; }
    private readonly ITestOutputHelper _out;
    public ContactPatchTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string n) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", n + ".json"));
    private const double Deg = Math.PI / 180.0;

    private static Quat Roll(double rad) => new(Math.Sin(rad / 2), 0, 0, Math.Cos(rad / 2));

    /// <summary>The Pitts banked <paramref name="bankDeg"/> right, its lowest hard point 2 cm up, moving at
    /// <paramref name="fwdMs"/> and sinking at <paramref name="sinkMs"/> (world).</summary>
    private static Aircraft BankedOntoRightTip(AircraftConfig c, double bankDeg, double fwdMs, double sinkMs)
    {
        Quat att = Roll(bankDeg * Deg);
        double low = double.MaxValue;
        foreach (ContactPoint p in AirframeContact.BuildPoints(c)) if (!p.PropDisc) low = Math.Min(low, -att.Rotate(p.Body - c.Mass.CgVec()).Z);
        return new Aircraft(c, new RigidBodyState(new Vec3(0, 0, low - 0.02), att, att.Conjugate().Rotate(new Vec3(fwdMs, 0, sinkMs)), Vec3.Zero), ControlDeflections.Neutral);
    }

    private static ContactPoint Point(Aircraft ac, string name) => ac.ContactPoints.First(p => p.Name == name);
    private static WingPanel Panel(Aircraft ac, AirframeComponent c) => ac.Panels.First(p => p.Component == c);

    // ---- (1) the patches are on the wing, and the wing no longer goes through the ground ----------------------------

    [Fact]
    public void PittsLowerWingtipPatchSitsOnTheWingTipNotInsideTheWing()
    {
        var c = Load("pitts-s2b-like");
        var pts = AirframeContact.BuildPoints(c);
        // Outer lower-right strip: pos (-0.388, 2.846, 0.408), chord 0.482, area 0.041, dihedral 0.0524 → half width 0.0425.
        ContactPoint le = pts.First(p => p.Name == "tip:wing-lower-LE-R");
        Assert.Equal(-0.2675, le.Body.X, 2);
        Assert.Equal(2.8885, le.Body.Y, 2);
        Assert.Equal(0.4058, le.Body.Z, 2);   // before 2026-11-03: 0.256, 0.15 m inside the wing (dihedral counted twice)
        Assert.Equal(-0.7495, pts.First(p => p.Name == "tip:wing-lower-TE-R").Body.X, 2);
        Assert.InRange(pts.First(p => p.Name == "tip:wing-lower-TEcorner-R").Body.X, -1.0205, -1.0105);   // the aileron's outer trailing corner
        Assert.True(pts.First(p => p.Name == "tip:wing-lower-skinLower-R").Body.Z > le.Body.Z, "the lower skin is below the chord line");
        int patch = le.Patch;
        Assert.True(patch >= 0);
        Assert.Equal(5, pts.Count(p => p.Patch == patch));
        Assert.All(pts.Where(p => p.Patch == patch), p => Assert.Equal(AirframeComponent.WingTipLowerRight, p.Component));
    }

    [Fact]
    public void AFlatStripWingStillRisesAlongItsDihedral()
    {
        // The 2-33's strips all sit at z −0.30 with dihedral 0.05 rad in dihedralRad only: its tip (outer strip y 7.722,
        // chord 0.645, area 0.062 → edge 7.770) is 7.770·tan 0.05 = 0.389 m higher, at z −0.689 — above its tip skid
        // (gear z −0.44), so the wing rests on the skid, not on its own tip.
        var pts = AirframeContact.BuildPoints(Load("glider-2-33-like"));
        ContactPoint le = pts.First(p => p.Name == "tip:wing-LE-R");
        Assert.Equal(7.770, le.Body.Y, 2);
        Assert.Equal(-0.689, le.Body.Z, 2);
    }

    [Fact]
    public void PittsLowerWingtipNoLongerPenetratesTheGround()
    {
        var c = Load("pitts-s2b-like");
        WorldTerrain.Active = null;   // flat ground at 0
        // Settled gently onto the lower right tip, rolling slowly forward: the tip must stay on the surface.
        var ac = BankedOntoRightTip(c, 35, 2.0, 0.2);
        var tip = new Vec3(-0.2675, 2.8885, 0.4058);   // the lower right tip's leading edge (literal, above)
        var sim = new SimLoop(ac);
        double deepest = 9, held = 0;
        WingPanel p = Panel(ac, AirframeComponent.WingTipLowerRight);
        for (double t = 0; t < 3 && p.State != PanelState.Broken; t += 0.01)
        {
            sim.RunFor(0.01, new ControlInputs(0, 0, 0, 1.0));
            if (p.State == PanelState.Broken) break;
            // The tip as it is now: turned up by any set, and flexed up by the load (the series compliance).
            Vec3 now = p.Bend(tip, p.BendRad + p.ElasticRad);   // flexed up by the load it carries
            Vec3 w = ac.State.Position + ac.State.Attitude.Rotate(now - c.Mass.CgVec());
            deepest = Math.Min(deepest, -w.Z);
            held = t;
        }
        _out.WriteLine($"deepest lower-tip LE {deepest * 100:F1} cm over {held:F2} s on the tip; panel {p.State}, peak {p.PeakContactNm:F0} N·m (limit {p.LimitNm:F0})");
        Assert.True(held > 0.05, "the tip must have carried load");
        // Before 2026-11-03 the nearest hard point was 0.15 m inside the wing: the tip went 15 cm in before anything pushed.
        Assert.True(deepest > -0.04, $"lower wingtip {-deepest * 100:F1} cm into the ground");
    }

    [Fact]
    public void EveryAeroplaneHasTipPatchesOnItsWingsTailAndPropeller()
    {
        foreach (string f in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData"), "*-like.json"))
        {
            var c = AircraftConfigLoader.LoadFromFile(f);
            var pts = AirframeContact.BuildPoints(c);
            string n = Path.GetFileNameWithoutExtension(f);
            foreach (SurfaceConfig sf in c.Surfaces.Where(s => WingStructure.IsWingBase(s.Id) && s.Strips.Count > 0))
                foreach (string side in new[] { "L", "R" })
                {
                    Assert.True(pts.Any(p => p.Name == $"tip:{sf.Id}-LE-{side}"), $"{n}: no {sf.Id} {side} tip LE");
                    Assert.True(pts.Any(p => p.Name == $"tip:{sf.Id}-TE-{side}"), $"{n}: no {sf.Id} {side} tip TE");
                }
            if (c.Surfaces.Any(s => s.Id.ToLowerInvariant() == "hstab")) { Assert.Contains(pts, p => p.Name == "stab-L"); Assert.Contains(pts, p => p.Name == "stab-R"); }
            if (c.Surfaces.Any(s => s.Id.ToLowerInvariant() == "vstab")) { Assert.Contains(pts, p => p.Name == "fin-top"); Assert.Contains(pts, p => p.Name == "fin-top-TE"); }
            if (c.Propulsion is { PropDiameterM: > 0 })
                Assert.Equal(12 * Math.Max(1, c.Engines.Count), pts.Count(p => p.PropDisc));
            // Every patch's points are listed together (they share one ground sample).
            var seen = new HashSet<int>(); int last = int.MinValue;
            foreach (ContactPoint p in pts.Where(q => q.Patch >= 0))
            {
                if (p.Patch != last) { Assert.True(seen.Add(p.Patch), $"{n}: patch {p.Patch} split"); last = p.Patch; }
            }
        }
    }

    [Fact]
    public void BreakStationsComeFromEachTypesStructure()
    {
        var cases = new (string type, AirframeComponent comp, double y, string source)[]
        {
            ("pitts-s2b-like", AirframeComponent.WingTipLowerLeft, 2.18, "measured"),     // I-strut, lower wing (build_pitts_s2b.py YI_L)
            ("pitts-s2b-like", AirframeComponent.WingTipLeft, 2.28, "measured"),          // I-strut, upper wing (YI_U)
            ("great-lakes-like", AirframeComponent.WingTipLowerLeft, 2.875, "measured"),  // N-strut (build_great_lakes.py YI)
            ("j3-cub-like", AirframeComponent.WingTipLeft, 3.40, "measured"),             // lift-strut attach (build_j3_cub.py Y_ST)
            ("extra-300-like", AirframeComponent.WingTipLeft, 0.905, "aileron inboard end"),   // cantilever
        };
        foreach (var (type, comp, y, source) in cases)
        {
            var ac = new Aircraft(Load(type), new RigidBodyState(new Vec3(0, 0, -500), new Quat(0, 0, 0, 1), new Vec3(40, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
            WingPanel p = Panel(ac, comp);
            _out.WriteLine($"{type} {comp}: station {p.BreakY:F3} ({p.StationSource}), M1 {p.M1Nm:F0}, design {p.DesignNm1:F0}, limit {p.LimitNm:F0}, ultimate {p.UltimateNm:F0} N·m, " +
                           $"intact rotation {WingStructure.IntactRotationAt1g(p, ac.Config) / Deg:F3}°/g");
            Assert.Equal(y, p.BreakY, 3);
            Assert.StartsWith(source, p.StationSource);
        }
    }

    // ---- (1) scrape friction ------------------------------------------------------------------------------------------

    [Fact]
    public void ScrapingTipYawsTowardItAtTheSurfacesOwnFriction()
    {
        var c = Load("pitts-s2b-like");
        WorldTerrain.Active = null;
        ContactPoint tip = AirframeContact.BuildPoints(c).First(p => p.Name == "tip:wing-lower-LE-R");
        double Yaw(WorldTerrain.Surface s, out double friction, out double normal)
        {
            WorldTerrain.SurfaceOverride = s;
            var pt = new ContactPoint { Body = tip.Body, Name = tip.Name, Component = tip.Component };
            Vec3 cg = c.Mass.CgVec();
            // Wings level, the right lower tip 2 cm into the ground, sliding forward at 20 m/s.
            var st = new RigidBodyState(new Vec3(0, 0, -(tip.Body.Z - cg.Z) + 0.02), new Quat(0, 0, 0, 1), new Vec3(20, 0, 0), Vec3.Zero);
            (Vec3 f, Vec3 m) = AirframeContact.Compute(new List<ContactPoint> { pt }, cg, st, null);
            friction = -f.X; normal = -f.Z;
            return m.Z;
        }
        double yawPaved = Yaw(WorldTerrain.Surface.Paved, out double fP, out double nP);
        double yawGrass = Yaw(WorldTerrain.Surface.Grass, out double fG, out double nG);
        _out.WriteLine($"paved: N {nP:F0} N, friction {fP:F0} N, yaw {yawPaved:F0} N·m; grass: friction {fG:F0} N, yaw {yawGrass:F0} N·m");
        Assert.True(yawPaved > 0, "a right tip dragging must swing the nose RIGHT, toward it");
        Assert.Equal(0.60, fP / nP, 3);   // wrecked/hard structure on asphalt (AirframeContact.SlidingMu, Ty 2026-11-04 18:31)
        Assert.Equal(0.65, fG / nG, 3);   // on grass
        Assert.Equal(0.65 / 0.60, yawGrass / yawPaved, 3);
    }

    [Fact]
    public void ScrapeStartsAndStopsWithItsSurfaceAndIntensity()
    {
        var c = Load("pitts-s2b-like");
        WorldTerrain.Active = null;
        WorldTerrain.SurfaceOverride = WorldTerrain.Surface.Grass;
        var ac = BankedOntoRightTip(c, 35, 15.0, 0.1);
        var ev = new List<ContactEvent>(); ac.Contact += ev.Add;
        var sim = new SimLoop(ac);
        bool sawList = false;
        for (double t = 0; t < 0.3; t += 0.01) { sim.RunFor(0.01, new ControlInputs(0, 0, 0, 1.0)); sawList |= ac.Scrapes.Any(s => s.Part.StartsWith("tip:wing-lower")); }
        ContactEvent start = ev.First(e => e.Kind == ContactEventKind.ScrapeStart && e.Part.StartsWith("tip:wing-lower"));
        _out.WriteLine(start.ToString());
        Assert.True(sawList);
        Assert.Equal((int)WorldTerrain.Surface.Grass, start.SurfaceCode);
        Assert.True(start.Intensity > 100, "μ·N·v of a tip sliding at ~15 m/s");
        // Lift it clear: the scrape stops.
        RigidBodyState s = ac.State;
        ac.State = new RigidBodyState(s.Position + new Vec3(0, 0, -3), s.Attitude, s.Velocity, s.Rates);
        sim.RunFor(0.02, new ControlInputs(0, 0, 0, 1.0));
        Assert.Contains(ev, e => e.Kind == ContactEventKind.ScrapeStop && e.Part == start.Part);
        Assert.DoesNotContain(ac.Scrapes, x => x.Part == start.Part);
    }

    // ---- (2) bend and break thresholds --------------------------------------------------------------------------------

    [Fact]
    public void PittsLowerPanelStrengthIsTheSparsDesignMoments()
    {
        var c = Load("pitts-s2b-like");
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -500), new Quat(0, 0, 0, 1), new Vec3(40, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
        WingPanel p = Panel(ac, AirframeComponent.WingTipLowerRight);
        Assert.True(p.Braced);
        Assert.Equal(6.0 * p.DesignNm1, p.LimitNm, 6);      // limits.gMax 6
        Assert.Equal(2.0 * p.LimitNm, p.UltimateNm, 6);     // limits.ultimateFactor 2.0 (the game's)
        Assert.True(p.DesignNm1 > p.M1Nm, "a braced spar is sized by its bay, not by the overhang's own lift");
    }

    [Fact]
    public void MediumTipStrikeLeavesASmallSetWithACrunch()
    {
        // Past 1.5 × its limit moment (Ty 2026-11-05: permanent deformation begins after 1.5 × limit) and short of its
        // ultimate a spruce spar takes a set of a fraction of a degree (it is short of its peak, 0.007 rad,
        // MemberMaterials) and stays on: Ty 2026-11-05 replaced the 11-03 fixed 5° set.
        var c = Load("pitts-s2b-like");
        WorldTerrain.Active = null;
        var ac = BankedOntoRightTip(c, 35, 20.0, SetSinkMs);
        var ev = new List<ContactEvent>(); ac.Contact += ev.Add;
        var sim = new SimLoop(ac);
        for (double t = 0; t < 0.6; t += 0.005) sim.RunFor(0.005, new ControlInputs(0, 0, 0, 1.0));
        WingPanel p = Panel(ac, AirframeComponent.WingTipLowerRight);
        _out.WriteLine($"peak {p.PeakContactNm:F0} N·m against limit {p.LimitNm:F0} (yield {p.Bending!.YieldNm:F0}), ultimate {p.UltimateNm:F0}; bend {p.BendRad / Deg:F3}°, twist {p.TwistRad / Deg:F3}°; events {string.Join(" | ", ev)}");
        Assert.Contains(ev, e => e.Kind == ContactEventKind.PanelBent && e.Component == AirframeComponent.WingTipLowerRight);
        Assert.Equal(PanelState.Bent, p.State);
        Assert.InRange(p.PeakContactNm, 1.5 * p.LimitNm, p.UltimateNm);
        Assert.InRange(Math.Abs(p.BendRad) / Deg, 0.001, 0.4);
        Assert.False(p.Bending!.Damaged, "short of its ultimate the spar has not cracked");
        Assert.False(ac.IsLost(AirframeComponent.WingTipLowerRight));
        Assert.Equal(PanelState.Intact, Panel(ac, AirframeComponent.WingTipRight).State);   // the upper wing is untouched
    }

    private const double SetSinkMs = 1.0;

    [Fact]
    public void ATipStrikeBetweenTheLimitAndOnePointFiveTimesItIsElastic()
    {
        // Ty 2026-11-05: "After 1.5 limit load permanent deformation begins." The same touch at 0.8 m/s asks the Pitts'
        // lower station for 1.37 × its limit moment: it flexes and springs back.
        var c = Load("pitts-s2b-like");
        WorldTerrain.Active = null;
        var ac = BankedOntoRightTip(c, 35, 20.0, 0.8);
        var sim = new SimLoop(ac);
        for (double t = 0; t < 0.6; t += 0.005) sim.RunFor(0.005, new ControlInputs(0, 0, 0, 1.0));
        WingPanel p = Panel(ac, AirframeComponent.WingTipLowerRight);
        _out.WriteLine($"peak {p.PeakContactNm:F0} N·m against limit {p.LimitNm:F0}; bend {p.BendRad / Deg:F4}°");
        Assert.InRange(p.PeakContactNm, p.LimitNm, 1.5 * p.LimitNm);
        Assert.Equal(PanelState.Intact, p.State);
        Assert.Equal(0.0, p.BendRad, 12);
    }

    [Fact]
    public void HardTipStrikeFoldsTheLowerPanelUpAndItHangsOn()
    {
        // 3 m/s onto the tip asks the station for more than twice its ultimate. The spar cracks and the panel folds up
        // about the I-strut; what is left of it (35 %, the rear spar, the drag truss, the fabric) holds it on.
        var c = Load("pitts-s2b-like");
        WorldTerrain.Active = null;
        var ac = BankedOntoRightTip(c, 35, 20.0, 3.0);
        Vec3 tipBefore = Point(ac, "tip:wing-lower-LE-R").Body;
        var sim = new SimLoop(ac);
        for (double t = 0; t < 0.6; t += 0.005) sim.RunFor(0.005, new ControlInputs(0, 0, 0, 1.0));
        WingPanel p = Panel(ac, AirframeComponent.WingTipLowerRight);
        Vec3 tipAfter = Point(ac, "tip:wing-lower-LE-R").Body;
        _out.WriteLine($"peak {p.PeakContactNm:F0} N·m against ultimate {p.UltimateNm:F0}; bend {p.BendRad / Deg:F1}°, path {p.Bending!.PathRad:F3} rad; tip up {(tipBefore.Z - tipAfter.Z) * 100:F1} cm");
        Assert.True(p.PeakContactNm > 2 * p.UltimateNm);
        Assert.Equal(PanelState.Bent, p.State);
        Assert.True(p.Bending.Damaged);
        Assert.InRange(p.BendRad / Deg, 10.0, 22.9);   // short of the 0.40 rad the fabric tears at
        Assert.False(ac.IsLost(AirframeComponent.WingTipLowerRight));
        Assert.True(tipBefore.Z - tipAfter.Z > 0.10, "the tip has turned up about the I-strut");
        // Its rows are still flying, folded: their dihedral has grown by the bend.
        Assert.All(p.Strips, i => Assert.True(ac.StripMask is null || ac.StripMask[i]));
        Assert.All(p.Strips, i => Assert.Equal(p.BendRad, ac.FlowStateForTests.BendRad![i], 12));
    }

    [Fact]
    public void AHarderStrikeBreaksTheLowerPanelOffOutboardOfTheIStrut()
    {
        var c = Load("pitts-s2b-like");
        WorldTerrain.Active = null;
        var ac = BankedOntoRightTip(c, 35, 20.0, 6.0);
        var ev = new List<ContactEvent>(); ac.Contact += ev.Add;
        double mass0 = ac.MassProperties.MassKg;
        var sim = new SimLoop(ac);
        for (double t = 0; t < 0.6 && !ac.IsLost(AirframeComponent.WingTipLowerRight); t += 0.005) sim.RunFor(0.005, new ControlInputs(0, 0, 0, 1.0));
        WingPanel p = Panel(ac, AirframeComponent.WingTipLowerRight);
        _out.WriteLine($"peak {p.PeakContactNm:F0} N·m against ultimate {p.UltimateNm:F0}; work {p.Bending!.WorkJ:F0} J; lost {string.Join(",", ac.LostComponents)}; mass {mass0:F1} → {ac.MassProperties.MassKg:F1} (panel {p.MassKg:F1} kg); CG shift {ac.CgShiftBody}");
        Assert.Equal(PanelState.Broken, p.State);
        Assert.Contains(ev, e => e.Kind == ContactEventKind.PanelBroken && e.Component == AirframeComponent.WingTipLowerRight);
        Assert.True(p.Bending.Ruptured || p.Twisting!.Ruptured);
        Assert.False(ac.IsLost(AirframeComponent.WingTipRight));
        Assert.False(ac.IsLost(AirframeComponent.WingRightOuter));
        // Its rows are out of the aero, the rest of the lower wing and the whole upper wing are in.
        Assert.All(p.Strips, i => Assert.False(ac.StripMask![i]));
        Assert.All(Panel(ac, AirframeComponent.WingTipRight).Strips, i => Assert.True(ac.StripMask![i]));
        Assert.DoesNotContain(ac.ContactPoints, q => q.Component == AirframeComponent.WingTipLowerRight);
        // Mass: the panel's structure and the lower right aileron's leave; the CG moves left, away from the missing side.
        double shed = mass0 - ac.MassProperties.MassKg;
        Assert.InRange(shed, p.MassKg, p.MassKg + 5.0);
        Assert.True(ac.CgShiftBody.Y < 0, "mass gone from the right: the remainder's CG moves left");
    }

    // ---- (2c) the bent panel in flight --------------------------------------------------------------------------------

    [Fact]
    public void ABentPanelFliesOnWhatIsLeftOfItAndGoesPastIt()
    {
        // The J-3's right panel bent 5° (spruce: past its peak, down to the 35 % residual, MemberMaterials). In flight the
        // air bends it by n × M1: it holds while that is under 0.35 × 6190 N·m and goes beyond — 0.35·6190/613 = 3.53 g
        // (literals: ContactPatchTests.BreakStationsComeFromEachTypesStructure prints M1 613, ultimate 6190).
        var c = Load("j3-cub-like");
        WorldTerrain.Active = null;
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -1500), new Quat(0, 0, 0, 1), new Vec3(55, 0, 1), Vec3.Zero), ControlDeflections.Neutral);
        Assert.True(ac.BendPanel(AirframeComponent.WingTipRight));
        WingPanel p = Panel(ac, AirframeComponent.WingTipRight);
        Assert.InRange(p.Bending!.Capacity, 0.35 * 6190 - 5, 0.35 * 6190 + 5);
        var sim = new SimLoop(ac);
        for (double t = 0; t < 1.0; t += 0.01) sim.RunFor(0.01, new ControlInputs(0, -0.02, 0, 0.0));
        Assert.Equal(PanelState.Bent, p.State);
        Assert.Equal(5.0, p.BendRad / Deg, 1);
        double nAtBreak = double.NaN, nMaxBent = 0;
        for (double t = 0; t < 4.0 && p.State != PanelState.Broken; t += 0.005)
        {
            sim.RunFor(0.005, new ControlInputs(0, -Math.Min(1.0, 0.4 * t), 0, 0.0));   // aft stick is negative
            if (p.State == PanelState.Broken) nAtBreak = ac.AeroLoadFactorZ;
            else nMaxBent = Math.Max(nMaxBent, ac.AeroLoadFactorZ);
        }
        // nAtBreak is a SENTINEL, not a measurement: it stays NaN when the panel never broke, so printing it raw read
        // "went at NaN g" and sent an agent hunting a divide-by-zero in the load path that was never there (2026-11-10).
        // When it did not go, say so, and print the two numbers that decide it — the bending hinge's remaining backbone
        // and the most the air ever asked of it (AeroLoadFactorZ x M1) — because the margin between them is tenths of a
        // percent and a reader needs to see which side of it the pull landed on.
        _out.WriteLine(double.IsNaN(nAtBreak)
            ? $"bent panel held to {nMaxBent:F3} g and NEVER WENT: the air asked {nMaxBent * p.M1Nm:F1} N·m at the peak "
              + $"against a {p.Bending!.Capacity:F1} N·m backbone (it needs n > {p.Bending!.Capacity / p.M1Nm:F4})"
            : $"bent panel held to {nMaxBent:F3} g, went at {nAtBreak:F3} g");
        Assert.Equal(PanelState.Broken, p.State);
        Assert.InRange(nMaxBent, 3.2, 3.6);
        Assert.InRange(nAtBreak, 3.4, 3.8);
        Assert.False(ac.IsLost(AirframeComponent.WingTipLeft));
    }

    // ---- (2b) slave strut ---------------------------------------------------------------------------------------------

    [Fact]
    public void PittsSlaveStrutMovesTheUpperAileronsWithTheLowers()
    {
        var c = Load("pitts-s2b-like");
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -500), new Quat(0, 0, 0, 1), new Vec3(50, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
        Assert.Equal(2, ac.SlaveLinks.Count);
        foreach (SlaveLink l in ac.SlaveLinks)
            foreach (double d in new[] { -24.0, -12.0, 0.0, 6.0, 12.0, 24.0 })
                Assert.InRange(l.Solve(d * Deg, null, null, d * Deg, 0.7) / Deg, d - 0.3, d + 0.3);
        // The Pitts's struts pin on 0.254 m INBOARD of the I-struts: a bent lower panel leaves the link where it was.
        Assert.True(ac.BendPanel(AirframeComponent.WingTipLowerLeft));
        var sim = new SimLoop(ac);
        sim.RunFor(0.2, new ControlInputs(0.5, 0, 0, 0.0));
        SlaveLink left = ac.SlaveLinks.First(l => l.Side < 0);
        double lowerLocal = left.DrivenSign * (ac.CurrentDeflections.AileronRad + c.Rig.AileronRad);
        Assert.InRange(ac.SlavedAileronLocalRad(-1) / Deg, lowerLocal / Deg - 0.3, lowerLocal / Deg + 0.3);
    }

    [Fact]
    public void ABendOutboardOfTheStrutDrivesTheSlavedAileronThroughTheLink()
    {
        // The same Pitts with its left slave strut's lower pin moved 0.22 m outboard of the lower I-strut (driven y 2.40):
        // a 5° bend of the lower panel lifts that pin (2.40 − 2.18)·sin 5° = 19 mm. The strut does not stretch, so it
        // pushes the upper horn ≈ 19 mm·0.91 (the strut's vertical cosine) along itself: 17 mm on a 0.072 m arm ≈ 14°
        // trailing edge DOWN. The ailerons still work: the slaved one keeps following the stick about that offset.
        var c = Load("pitts-s2b-like");
        c.SlaveStrut!.DrivenY = 2.40;
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -500), new Quat(0, 0, 0, 1), new Vec3(50, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
        SlaveLink left = ac.SlaveLinks.First(l => l.Side < 0);
        WingPanel lower = Panel(ac, AirframeComponent.WingTipLowerLeft);
        Func<Vec3, Vec3> bend = v => lower.Bend(v, 5 * Deg);
        double at0 = left.Solve(0, bend, null, 0, 0.7), atPlus = left.Solve(5 * Deg, bend, null, at0, 0.7);
        _out.WriteLine($"bent 5°: slaved {at0 / Deg:F2}° at neutral, {atPlus / Deg:F2}° with the driven at +5°");
        Assert.InRange(at0 / Deg, 11.0, 17.0);
        Assert.InRange((atPlus - at0) / (5 * Deg), 0.8, 1.2);
        // And in the running aeroplane: bent, the slaved aileron sits at that offset and still moves with the stick.
        Assert.True(ac.BendPanel(AirframeComponent.WingTipLowerLeft));
        var sim = new SimLoop(ac);
        sim.RunFor(0.3, new ControlInputs(0, 0, 0, 0.0));
        double a0 = ac.SlavedAileronLocalRad(-1);
        sim.RunFor(0.3, new ControlInputs(0.6, 0, 0, 0.0));
        double a1 = ac.SlavedAileronLocalRad(-1);
        Assert.InRange(a0 / Deg, 10.0, 18.0);
        Assert.True(Math.Abs(a1 - a0) > 3 * Deg, "the link must not freeze the ailerons");
    }

    // ---- (2) aileron loss ---------------------------------------------------------------------------------------------

    [Fact]
    public void LosingThePittsLowerPanelLosesItsAileronAndTheUpperOneTrailsStickFree()
    {
        var c = Load("pitts-s2b-like");
        WorldTerrain.Active = null;
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -1500), new Quat(0, 0, 0, 1), new Vec3(45, 0, 3), Vec3.Zero), ControlDeflections.Neutral);
        var sim = new SimLoop(ac);
        sim.RunFor(0.2, new ControlInputs(0, 0, 0, 0.0));
        ac.LoseComponent(AirframeComponent.WingTipLowerLeft);
        WingPanel lower = Panel(ac, AirframeComponent.WingTipLowerLeft);
        // The whole lower left aileron is out of the aero (its hinge line crossed the station), the upper left is not.
        Assert.All(lower.AileronHinged, i => Assert.False(ac.StripMask![i]));
        SlaveLink left = ac.SlaveLinks.First(l => l.Side < 0), right = ac.SlaveLinks.First(l => l.Side > 0);
        Assert.All(left.SlavedRows, i => Assert.True(ac.StripMask![i]));
        Assert.False(left.Connected);
        Assert.True(right.Connected);
        Assert.False(ac.IsLost(AirframeComponent.AileronLeft), "only the lower aileron went: the four-aileron Pitts keeps its upper one");
        // Stick hard right, then hard left: the free upper left aileron does not care.
        sim.RunFor(0.6, new ControlInputs(1.0, 0, 0, 0.0));
        double freeA = ac.SlavedAileronLocalRad(-1), rightA = ac.SlavedAileronLocalRad(1);
        sim.RunFor(0.6, new ControlInputs(-1.0, 0, 0, 0.0));
        double freeB = ac.SlavedAileronLocalRad(-1), rightB = ac.SlavedAileronLocalRad(1);
        _out.WriteLine($"free upper-left {freeA / Deg:F2}° / {freeB / Deg:F2}°; linked upper-right {rightA / Deg:F2}° / {rightB / Deg:F2}°");
        Assert.True(Math.Abs(rightA - rightB) > 20 * Deg, "the right ailerons still follow the stick");
        Assert.True(Math.Abs(freeA - freeB) < 1.5 * Deg, "the disconnected upper aileron is not on the stick");
        // It trails: Chα −0.1, Chδ −0.3 → δ = −α/3 — trailing edge UP at the positive angle of attack of level flight.
        Assert.InRange(freeB / Deg, -5.0, -0.2);
    }

    [Fact]
    public void GreatLakesLowerPanelLossLosesThatSidesOnlyAileronTheOtherStillRolls()
    {
        var c = Load("great-lakes-like");
        var tables = Aircraft.BuildAirfoilTables(c);
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -1500), new Quat(0, 0, 0, 1), new Vec3(40, 0, 2), Vec3.Zero), ControlDeflections.Neutral);
        int strips = c.Surfaces.Sum(s => s.Strips.Count);
        double RollPower(bool[]? mask, int[] deadRows)
        {
            var v = new Vec3(40, 0, 2);
            double M(double d)
            {
                // The fixed rows whose aileron went carry no aileron camber any more (Aircraft parks them at 0).
                var fs = new StripFlowState(); fs.EnsureSize(strips);
                fs.DeflectionOverride = Enumerable.Repeat(double.NaN, strips).ToArray();
                foreach (int r in deadRows) fs.DeflectionOverride[r] = 0.0;
                return AeroModel.Compute(c, tables, v, Vec3.Zero, Vec3.Zero, 1.225, new ControlDeflections(d, 0, 0, 0), -1.0, fs, 0, 0, null, mask).Moment.X;
            }
            return M(0.2) - M(-0.2);
        }
        double intact = RollPower(null, Array.Empty<int>());
        ac.LoseComponent(AirframeComponent.WingTipLowerLeft);
        int[] dead = Panel(ac, AirframeComponent.WingTipLowerLeft).AileronFixed;
        Assert.True(ac.IsLost(AirframeComponent.AileronLeft));
        Assert.False(ac.IsLost(AirframeComponent.AileronRight));
        double after = RollPower(ac.StripMask, dead);
        _out.WriteLine($"Great Lakes roll power intact {intact:F0}, without the left lower panel {after:F0} N·m per 0.4 rad");
        Assert.InRange(after / intact, 0.35, 0.65);
    }

    [Fact]
    public void CubLosesOnlyTheAileronOnThePanelThatWent()
    {
        var c = Load("j3-cub-like");
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -1500), new Quat(0, 0, 0, 1), new Vec3(30, 0, 1), Vec3.Zero), ControlDeflections.Neutral);
        ac.LoseComponent(AirframeComponent.WingTipRight);
        Assert.True(ac.IsLost(AirframeComponent.AileronRight));
        Assert.False(ac.IsLost(AirframeComponent.AileronLeft));
        Assert.False(ac.IsLost(AirframeComponent.WingRightInner));
    }

    // ---- prop strike --------------------------------------------------------------------------------------------------

    /// <summary>The lowest tip 5 mm into the ground for one step (5 ms, a quarter turn at 2,700 rpm), full power, then
    /// clear, at a dozen instants. Each blade is struck only if it sweeps through the struck arc in that step
    /// (AircraftContactDamage.PropStrike, lane/claude-floats 2026-11-05), so a brush curls ONE blade by the 5 mm it
    /// went in, or none; never both. The engine runs on; the thrust is (mean R_k²/R₀²)^(1/3) (PropBladeDamage.Performance).</summary>
    [Fact]
    public void APropBrushCurlsTheTipsAndTheEngineRunsOn()
    {
        var c = Load("pitts-s2b-like");
        WorldTerrain.Active = null;
        var att = new Quat(0, 0, 0, 1);
        double low = AirframeContact.BuildPoints(c).Where(p => p.PropDisc).Max(p => p.Body.Z - c.Mass.CgVec().Z);
        int curled = 0;
        try
        {
            for (int i = 0; i < 12; i++)
            {
                // In the air, the lowest blade tip 5 mm into the ground for one step, full power, then clear. The rotor turns on
                // its own rpm, so i more 5 ms steps up high first put the blades somewhere else when the brush comes.
                var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -50), att, new Vec3(30, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
                var ev = new List<ContactEvent>(); ac.Contact += ev.Add;
                var sim = new SimLoop(ac);
                sim.RunFor(0.005 * i, new ControlInputs(0, 0, 0, -1.0));
                ac.State = new RigidBodyState(new Vec3(0, 0, -(low - 0.005)), att, new Vec3(30, 0, 0), Vec3.Zero);
                sim.RunFor(0.005, new ControlInputs(0, 0, 0, -1.0));
                RigidBodyState s = ac.State;
                ac.State = new RigidBodyState(s.Position + new Vec3(0, 0, -2), s.Attitude, s.Velocity, s.Rates);
                sim.RunFor(0.1, new ControlInputs(0, 0, 0, -1.0));
                double b0 = 0.99 - ac.PropBladeRadiusM(0, 0), b1 = 0.99 - ac.PropBladeRadiusM(0, 1);
                _out.WriteLine($"events {string.Join(" | ", ev)}; blades {ac.PropBladeRadiusM(0, 0):F4}/{ac.PropBladeRadiusM(0, 1):F4} m, thrust x{ac.PropThrustScale():F4}, rpm {ac.EngineRpm:F0}");
                Assert.Contains(ev, e => e.Kind == ContactEventKind.PropStrike);
                Assert.DoesNotContain(ev, e => e.Kind == ContactEventKind.PropStoppage);
                Assert.False(ac.EngineStopped);
                Assert.True(ac.EngineRpm > 1000);
                Assert.Equal(0.0, Math.Min(b0, b1), 9);
                if (Math.Max(b0, b1) <= 0) continue;
                curled++;
                Assert.InRange(Math.Max(b0, b1), 0.002, 0.012);   // curled by about the 5 mm it went in
                double x = (0.99 - Math.Max(b0, b1)) / 0.99;
                Assert.Equal(Math.Pow(0.5 * (1 + x * x), 1.0 / 3.0), ac.PropThrustScale(), 9);   // PropBladeDamage.Performance
                Assert.Equal(1, ac.PropBladeCurl(0, b0 > 0 ? 0 : 1));   // full power, a governing prop: it was pulling, so it curls forward
                Assert.True(ac.PropThrustScale() < 1.0);
            }
        }
        finally { Atmosphere.SimTimeSec = 0; }
        Assert.True(curled >= 2, $"only {curled} of 12 brushes met a blade");
    }

    [Fact]
    public void ANoseOverStopsTheEngine()
    {
        var c = Load("pitts-s2b-like");
        WorldTerrain.Active = null;
        double pitch = -25 * Deg;
        var att = new Quat(0, Math.Sin(pitch / 2), 0, Math.Cos(pitch / 2));
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -1.4), att, new Vec3(8, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
        var ev = new List<ContactEvent>(); ac.Contact += ev.Add;
        var sim = new SimLoop(ac);
        for (double t = 0; t < 2 && !ac.EngineStopped; t += 0.01) sim.RunFor(0.01, new ControlInputs(0, 0, 0, -1.0));
        _out.WriteLine(string.Join(" | ", ev.Where(e => e.Kind is ContactEventKind.PropStrike or ContactEventKind.PropStoppage)));
        Assert.True(ac.EngineStopped);
        Assert.Contains(ev, e => e.Kind == ContactEventKind.PropStoppage);
        Assert.Equal(0.0, ac.EngineRpm);
    }
}
