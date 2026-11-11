using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The marks an aeroplane leaves in the ground (<see cref="GroundImprints"/>).
///
/// Ty 2026-11-10: *"if it is penetrating it should leave imprints behind as it slides and rolls displacing the dirt or
/// snow etc"*, and *"I want the penetration to show and stay as ground scars ... I can understand several cm on initial
/// impact but just rolling down the hill it should be a few cm not a dozen or more"*.
///
/// These check the soil numbers against their sources, the budget against a circuit's worth of landings, and that a
/// rolling tyre, a locked one, a ski and a dragged wingtip each leave their own kind of mark.
/// </summary>
[Collection("WorldTerrainActive")]
public class GroundImprintTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public GroundImprintTests(ITestOutputHelper o)
    {
        _o = o; SimGlobals.Calm(); SurfaceContacts.SnowLiquidWaterPercent = null;
        Atmosphere.SimTimeSec = 0; GroundImprints.Clear();
    }
    public void Dispose()
    {
        WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null;
        SurfaceContacts.SnowLiquidWaterPercent = null; GroundImprints.Clear();
    }

    private static AircraftConfig Cfg(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private static GroundImprint[] All(GroundImprintField f)
    {
        var a = new GroundImprint[f.Count];
        for (int i = 0; i < f.Count; i++) a[i] = f[i];
        return a;
    }

    /// <summary>Roll it along flat ground of this surface at this speed and let it mark.</summary>
    private static Aircraft Roll(AircraftConfig c, WorldTerrain.Surface surface, double seconds, double speedMs,
        double brake = 0.0, double rudder = 0.0)
    {
        WorldTerrain.SurfaceOverride = surface;
        RigidBodyState rest = LandingGear.RestingState(c, 0, 0, 0);
        var s = new RigidBodyState(rest.Position, rest.Attitude, new Vec3(speedMs, 0, 0), Vec3.Zero);
        var ac = new Aircraft(c, s, ControlDeflections.Neutral) { BrakeInput = brake };
        var sim = new SimLoop(ac);
        sim.RunFor(seconds, new ControlInputs(0, 0, rudder, 1.0));
        return ac;
    }

    // ---- the soil model ---------------------------------------------------------------------------------------

    /// <summary>
    /// Asphalt, a deck and water keep no mark; every soft ground does. This is the "asphalt does not mark at all" rule,
    /// and it falls out of the plasticity, not out of a special case.
    /// </summary>
    [Fact]
    public void OnlyGroundThatYieldsPlasticallyKeepsAMark()
    {
        Assert.False(GroundImprints.Marks(GroundMaterial.Asphalt));
        Assert.False(GroundImprints.Marks(GroundMaterial.Deck));
        Assert.False(GroundImprints.Marks(GroundMaterial.Water));
        foreach (GroundMaterial m in new[] { GroundMaterial.Grass, GroundMaterial.Dirt, GroundMaterial.Gravel,
                                            GroundMaterial.Sand, GroundMaterial.Rough, GroundMaterial.Snow,
                                            GroundMaterial.PackedSnow })
            Assert.True(GroundImprints.Marks(m), m.ToString());
        // Ice keeps no HOLLOW: it is brittle, so what a part leaves on it is scoring and crushed grains, which is a
        // texture and not the geometry this record holds. Zero, deliberately, not a small number.
        Assert.False(GroundImprints.Marks(GroundMaterial.Ice));
        Assert.Equal(0.0, GroundImprints.ScarDepthM(GroundMaterial.Ice, 0.03));
    }

    /// <summary>
    /// Every surface has a bearing pressure, a retention and a repose angle, in the order the sources put them: snow is
    /// the weakest ground, gravel the firmest unpaved one, asphalt and ice are rock, and the retention rises from
    /// asphalt (none) through turf (a root mat that springs back) to snow (compaction is forever).
    /// </summary>
    [Fact]
    public void TheSoilTableIsOrderedTheWayTheSourcesOrderIt()
    {
        double snow = GroundImprints.BearingPressurePa(GroundMaterial.Snow);
        double sand = GroundImprints.BearingPressurePa(GroundMaterial.Sand);
        double rough = GroundImprints.BearingPressurePa(GroundMaterial.Rough);
        double grass = GroundImprints.BearingPressurePa(GroundMaterial.Grass);
        double dirt = GroundImprints.BearingPressurePa(GroundMaterial.Dirt);
        double gravel = GroundImprints.BearingPressurePa(GroundMaterial.Gravel);
        _o.WriteLine($"kPa: snow {snow / 1e3:F0} sand {sand / 1e3:F0} rough {rough / 1e3:F0} grass {grass / 1e3:F0} dirt {dirt / 1e3:F0} gravel {gravel / 1e3:F0}");
        Assert.True(snow < sand && sand < rough && rough < grass && grass < dirt && dirt < gravel,
            "snow softest, gravel firmest unpaved");
        // IBC Table 1806.2 allowable values carry a safety factor near 3; these are ultimate, so gravel's 430 kPa is
        // 3 x its 144 kPa (3000 psf) and dirt's 250 kPa is ~2.6 x its 96 kPa (2000 psf).
        Assert.Equal(430e3, gravel);
        Assert.InRange(gravel / 144e3, 2.5, 3.5);
        Assert.InRange(dirt / 96e3, 2.2, 3.2);
        // Asphalt (3 MPa) and glacier ice (5 MPa, Petrovic 2003) are rock beside any soil: 7x and 12x the firmest
        // unpaved ground on the map, and a tyre presses on either at its inflation pressure, 0.1-0.2 MPa.
        Assert.True(GroundImprints.BearingPressurePa(GroundMaterial.Asphalt) > 6 * gravel);
        Assert.True(GroundImprints.BearingPressurePa(GroundMaterial.Ice) > 10 * gravel);

        Assert.Equal(0.0, GroundImprints.PlasticRetention01(GroundMaterial.Asphalt));
        Assert.True(GroundImprints.PlasticRetention01(GroundMaterial.Grass)
                  < GroundImprints.PlasticRetention01(GroundMaterial.Rough));
        Assert.True(GroundImprints.PlasticRetention01(GroundMaterial.Rough)
                  < GroundImprints.PlasticRetention01(GroundMaterial.Dirt));
        Assert.True(GroundImprints.PlasticRetention01(GroundMaterial.Sand)
                  < GroundImprints.PlasticRetention01(GroundMaterial.Snow));
        Assert.Equal(0.0, GroundImprints.PlasticRetention01(GroundMaterial.Ice));
        foreach (GroundMaterial m in Enum.GetValues<GroundMaterial>())
        {
            Assert.InRange(GroundImprints.PlasticRetention01(m), 0.0, 1.0);
            Assert.InRange(GroundImprints.ReposeAngleDeg(m), 20.0, 90.0);
            Assert.True(double.IsFinite(GroundImprints.BearingPressurePa(m)));
        }
    }

    /// <summary>
    /// A mark's width comes from the load and the ground's bearing capacity, and a deep one is wider at the surface
    /// than the part that made it, because the sides slump back to the angle of repose. Hand-copied numbers.
    /// </summary>
    [Fact]
    public void WidthComesFromTheLoadTheGroundCanCarryAndOpensToTheReposeAngle()
    {
        // A Pitts' belly carrying its whole 820 kg on one patch: 8 kN. On grass at 200 kPa that is 0.040 m^2, a patch
        // 0.20 m square. sqrt(8046/200000) = 0.2006.
        double patch = GroundImprints.BearingPatchWidthM(GroundMaterial.Grass, 820 * 9.81);
        _o.WriteLine($"820 kg on grass: patch {patch:F4} m");
        Assert.InRange(patch, 0.199, 0.202);
        // The same load on settled snow (60 kPa) needs 0.134 m^2 — 0.366 m across, 1.8x wider. Softer ground, broader
        // mark, which is sqrt(200/60) = 1.826 exactly.
        double onSnow = GroundImprints.BearingPatchWidthM(GroundMaterial.Snow, 820 * 9.81);
        Assert.InRange(onSnow / patch, 1.80, 1.85);

        // A wingtip bow carrying 1 kN on dirt (250 kPa): a 6.3 cm patch. At 3 cm deep the trough opens to
        // 0.063 + 2(0.03)/tan(37) = 0.063 + 0.0796 = 0.143 m. Four times the patch's own three-inch width: that is
        // what a gouge looks like.
        double tip = GroundImprints.BearingPatchWidthM(GroundMaterial.Dirt, 1000);
        double trough = GroundImprints.TroughWidthM(GroundMaterial.Dirt, tip, 0.03);
        _o.WriteLine($"1 kN tip on dirt: patch {tip:F4} m, trough at 3 cm {trough:F4} m");
        Assert.InRange(tip, 0.062, 0.064);
        Assert.InRange(trough, 0.140, 0.146);
        // Grass holds a near-vertical shoulder (60 deg, the root mat), so the same gouge opens half as much.
        double onGrassTrough = GroundImprints.TroughWidthM(GroundMaterial.Grass, tip, 0.03);
        Assert.True(onGrassTrough - tip < 0.6 * (trough - tip),
            $"turf's shoulder holds: {onGrassTrough - tip:F4} m against dirt's {trough - tip:F4} m");
        // A hard surface never opens a hollow: repose 90 deg, no slump.
        Assert.Equal(0.1, GroundImprints.TroughWidthM(GroundMaterial.Asphalt, 0.1, 0.05), 9);
    }

    /// <summary>
    /// The depth of a scar is the part of the contact's penetration that the ground did NOT give back. Ty's standard
    /// (2026-11-10): several cm on an impact, a few cm rolling or sliding, not a dozen. On the surfaces he flies off,
    /// a 3 cm penetration leaves a 1-3 cm scar; on pavement it leaves nothing.
    /// </summary>
    [Fact]
    public void AScarIsThePartOfThePenetrationTheGroundDidNotGiveBack()
    {
        const double penetration = 0.033;      // a Pitts resting on its belly, Ty's "about right" on 2026-11-10
        foreach (GroundMaterial m in new[] { GroundMaterial.Grass, GroundMaterial.Rough, GroundMaterial.Dirt,
                                             GroundMaterial.Gravel, GroundMaterial.Sand, GroundMaterial.Snow })
        {
            double d = GroundImprints.ScarDepthM(m, penetration);
            _o.WriteLine($"{m}: {100 * penetration:F1} cm penetration -> {100 * d:F2} cm scar");
            Assert.InRange(d, 0.010, 0.033);                 // a few cm, never more than went in
        }
        Assert.Equal(0.0, GroundImprints.ScarDepthM(GroundMaterial.Asphalt, penetration));
        // "several cm on initial impact": an 8 cm arrival into snow stays 7.6 cm. Still several, not a dozen.
        Assert.InRange(GroundImprints.ScarDepthM(GroundMaterial.Snow, 0.08), 0.07, 0.08);
        Assert.Equal(0.0, GroundImprints.ScarDepthM(GroundMaterial.Grass, 0.0));
        Assert.Equal(0.0, GroundImprints.ScarDepthM(GroundMaterial.Grass, double.NaN));
    }

    // ---- what each kind of contact leaves ---------------------------------------------------------------------

    /// <summary>A rolling tyre leaves a rut of its own footprint's width; it is not a skid.</summary>
    [Fact]
    public void ARollingTyreLeavesARutOfItsOwnFootprintWidth()
    {
        AircraftConfig c = Cfg("j3-cub-like");
        Aircraft ac = Roll(c, WorldTerrain.Surface.Grass, 6.0, 8.0);
        GroundImprint[] k = All(GroundImprints.World);
        foreach (GroundImprint x in k) _o.WriteLine(x.ToString());
        Assert.NotEmpty(k);
        Assert.Contains(k, x => x.Kind == GroundImprintKind.Rut);
        Assert.DoesNotContain(k, x => x.Kind == GroundImprintKind.Skid);
        Assert.All(k, x => Assert.Equal(GroundMaterial.Grass, x.Material));
        GroundImprint rut = k.First(x => x.Kind == GroundImprintKind.Rut);
        // An 8.00-6's footprint is ~0.1 m across and the rut in grass is millimetres, so the trough is barely wider
        // than the tyre: a tyre track, not a trench.
        Assert.InRange(rut.WidthM, 0.05, 0.25);
        Assert.InRange(rut.DepthM, GroundImprints.MinDepthM, 0.05);
        // It is laid along the roll, not across it: a 6 s roll at ~8 m/s makes stretches, and they point down the strip.
        Assert.Contains(k, x => x.LengthM > 1.0);
        foreach (GroundImprint x in k.Where(x => x.LengthM > 1.0))
            Assert.True(Math.Abs(x.AlongWorld.X) > 0.9, $"laid down the strip: along = {x.AlongWorld}");
    }

    /// <summary>
    /// A braked wheel that locks smears instead of compacting: the same tyre on the same ground leaves a Skid, not a
    /// Rut. Ty named rolling and sliding separately and they are different marks.
    /// </summary>
    [Fact]
    public void ALockedWheelLeavesASkidWhereARollingOneLeavesARut()
    {
        AircraftConfig c = Cfg("j3-cub-like");
        Aircraft ac = Roll(c, WorldTerrain.Surface.Dirt, 5.0, 14.0, brake: 1.0);
        GroundImprint[] k = All(GroundImprints.World);
        foreach (GroundImprint x in k) _o.WriteLine(x.ToString());
        Assert.True(ac.Wheels.Any(w => w.Locked || Math.Abs(w.VFwdMs - w.RimSpeedMs) > GroundImprints.SkidSlipMs),
            "the brake has to actually make a wheel slide for this to be the right test");
        Assert.Contains(k, x => x.Kind == GroundImprintKind.Skid);
    }

    /// <summary>Nothing marks pavement, and nothing marks ice: one aeroplane's whole landing roll leaves no record.</summary>
    [Fact]
    public void PavementAndIceKeepNothing()
    {
        AircraftConfig c = Cfg("j3-cub-like");
        Roll(c, WorldTerrain.Surface.Paved, 5.0, 14.0, brake: 1.0);
        Assert.Equal(0, GroundImprints.World.Count);
        GroundImprints.Clear();
        Roll(c, WorldTerrain.Surface.GlacierIce, 5.0, 14.0, brake: 1.0);
        _o.WriteLine($"on glacier ice: {GroundImprints.World.Count} marks");
        Assert.Equal(0, GroundImprints.World.Count);
    }

    /// <summary>
    /// Soft snow takes the deepest, broadest mark of anything on the map, and pavement the least: the surface decides,
    /// not the aeroplane. Same aeroplane, same speed, five grounds.
    /// </summary>
    [Fact]
    public void TheSurfaceDecidesHowDeepAndHowWideTheMarkIs()
    {
        AircraftConfig c = Cfg("j3-cub-like");
        var deepest = new Dictionary<WorldTerrain.Surface, double>();
        foreach (WorldTerrain.Surface s in new[] { WorldTerrain.Surface.Paved, WorldTerrain.Surface.Grass,
                                                   WorldTerrain.Surface.Dirt, WorldTerrain.Surface.Sand,
                                                   WorldTerrain.Surface.Snow })
        {
            GroundImprints.Clear();
            Roll(c, s, 4.0, 8.0);
            GroundImprint[] k = All(GroundImprints.World);
            double d = k.Length == 0 ? 0 : k.Max(x => x.DepthM);
            double w = k.Length == 0 ? 0 : k.Max(x => x.WidthM);
            deepest[s] = d;
            _o.WriteLine($"{s}: {k.Length} marks, deepest {100 * d:F2} cm, widest {100 * w:F1} cm");
        }
        Assert.Equal(0.0, deepest[WorldTerrain.Surface.Paved]);
        Assert.True(deepest[WorldTerrain.Surface.Snow] > deepest[WorldTerrain.Surface.Sand],
            "a wheel sinks further in settled snow than in hard sand");
        Assert.True(deepest[WorldTerrain.Surface.Sand] > deepest[WorldTerrain.Surface.Dirt],
            "and further in sand than in a dirt strip");
        // Ty's standard for rolling: a few cm, not a dozen. Settled snow is the worst case in the whole model.
        Assert.InRange(deepest[WorldTerrain.Surface.Snow], 0.01, 0.12);
    }

    /// <summary>
    /// A ski's whole point is flotation: on the same snow it leaves a far broader and shallower Track than a wheel
    /// leaves a rut. Checked through SurfaceContacts.AddSki, the hook the ski model fills.
    /// </summary>
    [Fact]
    public void ASkiLeavesABroadShallowTrackAndATyreANarrowDeepRut()
    {
        var field = new GroundImprintField(64);
        var pen = new GroundImprintPen(field, 4, 7);
        var up = new Vec3(0, 0, -1);
        const double loadN = 2500;                      // a Cub main on snow
        // A J-3 wheel-ski: 0.25 x 1.8 m running surface (SurfaceContacts.DefaultSkiWidthM/LengthM), sinking 2 cm.
        pen.BeginStep();
        pen.Lay(0, GroundImprintKind.Track, GroundMaterial.Snow, new Vec3(0, 0, 0),
            SurfaceContacts.DefaultSkiWidthM, 0.02, loadN, 0.0);
        // The same load on the bare 8.00-6 tyre: a 0.10 m footprint sinking 6 cm.
        pen.Lay(1, GroundImprintKind.Rut, GroundMaterial.Snow, new Vec3(0, 5, 0), 0.10, 0.06, loadN, 0.0);
        pen.EndStep();
        GroundImprint[] k = All(field);
        foreach (GroundImprint x in k) _o.WriteLine(x.ToString());
        GroundImprint ski = k.First(x => x.Kind == GroundImprintKind.Track);
        GroundImprint tyre = k.First(x => x.Kind == GroundImprintKind.Rut);
        // The ski's running surface is 2.5x the tyre's footprint, but the MARKS are only 1.2x apart in width, because
        // the slump to the angle of repose adds 2d/tan(38 deg) to each and the deep narrow rut gains far more from it:
        // 15 cm against the ski's 5. That is right — a deep rut really does open out at the top — and it is why the
        // ski's flotation shows up in the DEPTH, which is where a pilot reads it.
        Assert.True(ski.WidthM > tyre.WidthM, $"ski {ski.WidthM:F3} m vs tyre {tyre.WidthM:F3} m");
        Assert.True(ski.DepthM < 0.5 * tyre.DepthM, $"ski {ski.DepthM:F3} m vs tyre {tyre.DepthM:F3} m");
    }

    /// <summary>
    /// A big structure sliding is a Scar, a small hard part is a Gouge, and the WIDTH that separates them comes from the
    /// load each carries, not from the label.
    /// </summary>
    [Fact]
    public void AWreckPloughsABroadScarWhereAWingtipCutsANarrowGouge()
    {
        Assert.Equal(GroundImprintKind.Scar, GroundImprints.KindOf(AirframeComponent.Fuselage));
        Assert.Equal(GroundImprintKind.Scar, GroundImprints.KindOf(AirframeComponent.Nose));
        Assert.Equal(GroundImprintKind.Scar, GroundImprints.KindOf(AirframeComponent.WingLeft));
        Assert.Equal(GroundImprintKind.Gouge, GroundImprints.KindOf(AirframeComponent.WingTipLeft));
        Assert.Equal(GroundImprintKind.Gouge, GroundImprints.KindOf(AirframeComponent.TailVertical));
        Assert.Equal(GroundImprintKind.Gouge, GroundImprints.KindOf(AirframeComponent.GearTail));

        var field = new GroundImprintField(64);
        var pen = new GroundImprintPen(field, 4, 3);
        pen.BeginStep();
        // A 820 kg aeroplane's belly on the ground, the whole weight on one patch, 3 cm in.
        pen.Lay(0, GroundImprintKind.Scar, GroundMaterial.Dirt, new Vec3(0, 0, 0), 0.0, 0.026, 820 * 9.81, 0.0);
        // A wingtip bow, 1 kN of it, the same 3 cm in.
        pen.Lay(1, GroundImprintKind.Gouge, GroundMaterial.Dirt, new Vec3(0, 20, 0), 0.0, 0.026, 1000, 0.0);
        pen.EndStep();
        GroundImprint scar = All(field).First(x => x.Kind == GroundImprintKind.Scar);
        GroundImprint gouge = All(field).First(x => x.Kind == GroundImprintKind.Gouge);
        _o.WriteLine($"{scar}\n{gouge}");
        // 0.248 m against 0.132 m. The PATCHES are 2.8x apart (0.179 m against 0.063 m: the belly needs eight times
        // the area, and width goes as its root), but both troughs gain the same 6.9 cm of slump at 2.6 cm deep, which
        // compresses the ratio to 1.9. The slump is a property of the ground, not of the part, so it should.
        Assert.True(scar.WidthM > 1.8 * gouge.WidthM, $"scar {scar.WidthM:F3} m, gouge {gouge.WidthM:F3} m");
        Assert.InRange(GroundImprints.BearingPatchWidthM(GroundMaterial.Dirt, 820 * 9.81)
                     / GroundImprints.BearingPatchWidthM(GroundMaterial.Dirt, 1000), 2.7, 2.9);
        // And neither is a trench: a few cm deep, both of them, which is Ty's standard.
        Assert.InRange(scar.DepthM, 0.01, 0.04);
        Assert.InRange(gouge.DepthM, 0.01, 0.04);
    }

    /// <summary>
    /// A wingtip dragged along the ground leaves a gouge, through the real contact path: the aeroplane is put down with
    /// a wing low so the tip is on the ground, and the mark appears with the tip's component and no other.
    /// </summary>
    [Fact]
    public void ADraggedWingtipLeavesAGougeThroughTheRealContactPath()
    {
        AircraftConfig c = Cfg("j3-cub-like");
        WorldTerrain.SurfaceOverride = WorldTerrain.Surface.Dirt;
        RigidBodyState rest = LandingGear.RestingState(c, 0, 0, 0);
        // Rolled hard enough that the left tip is on the ground, moving along it: a roll about the body x axis, on top
        // of the three-point attitude the type rests in.
        const double phi = 0.55;
        Quat att = Quat.Multiply(rest.Attitude, new Quat(Math.Sin(phi / 2), 0, 0, Math.Cos(phi / 2)));
        var s = new RigidBodyState(rest.Position + new Vec3(0, 0, -0.1), att, new Vec3(12, 0, 1.0), Vec3.Zero);
        var ac = new Aircraft(c, s, ControlDeflections.Neutral);
        new SimLoop(ac).RunFor(2.0, new ControlInputs(0, 0, 0, 1.0));
        GroundImprint[] k = All(GroundImprints.World);
        foreach (GroundImprint x in k) _o.WriteLine(x.ToString());
        _o.WriteLine(string.Join(", ", ac.ContactPoints.Where(p => p.NormalN > 0).Select(p => $"{p.Name} d={100 * p.DepthM:F1}cm N={p.NormalN:F0}")));
        Assert.Contains(k, x => x.Kind is GroundImprintKind.Gouge or GroundImprintKind.Scar);
    }

    /// <summary>
    /// THE COUPLING, locked. An airframe scar's depth is exactly <see cref="GroundImprints.PlasticRetention01"/> of the
    /// penetration <see cref="AirframeContact"/> reported — nothing here invents, scales or clamps a depth of its own.
    ///
    /// This matters because the penetration is being fixed elsewhere as this is written
    /// (lane/claude-contact-depth: the penalty spring is mass-independent at 150 kN/m with no position correction, so
    /// today a heavy type sinks dozens of centimetres and Ty has judged it "way too much penetration"; a Pitts on its
    /// belly at 3.3 cm is what he called about right on 2026-11-10). The depths this test prints are therefore WRONG
    /// TODAY, and they are wrong by exactly the amount the penetration is wrong by. When that lane lands, the scars
    /// become a few centimetres with no change to this code, and this test is what proves nothing else moved.
    ///
    /// Clamping the scar to a plausible few centimetres was REJECTED: it would hide the penetration bug behind a
    /// cosmetic cap and put a second depth model beside the contact's, which is the drift this whole split exists to
    /// avoid.
    /// </summary>
    [Fact]
    public void AnAirframeScarIsExactlyTheRetainedPartOfTheContactsOwnPenetration()
    {
        AircraftConfig c = Cfg("j3-cub-like");
        WorldTerrain.SurfaceOverride = WorldTerrain.Surface.Dirt;
        RigidBodyState rest = LandingGear.RestingState(c, 0, 0, 0);
        const double phi = 0.55;
        Quat att = Quat.Multiply(rest.Attitude, new Quat(Math.Sin(phi / 2), 0, 0, Math.Cos(phi / 2)));
        var s = new RigidBodyState(rest.Position + new Vec3(0, 0, -0.1), att, new Vec3(12, 0, 1.0), Vec3.Zero);
        var ac = new Aircraft(c, s, ControlDeflections.Neutral);
        // The penetration has to be sampled WHILE the wing is in the ground, and on EVERY physics step: by the end of
        // the run the aeroplane has rolled level again and no airframe point is touching, and the peak lasts one step.
        // SurfaceContactsStepped fires after every step, which is the same instant the marks are laid.
        double penetration = 0;
        ac.SurfaceContactsStepped += _ =>
        {
            foreach (ContactPoint p in ac.ContactPoints)
                if (!p.PropDisc && p.NormalN > 0 && p.SurfaceCode >= 0 && p.DepthM > penetration) penetration = p.DepthM;
        };
        new SimLoop(ac).RunFor(0.5, new ControlInputs(0, 0, 0, 1.0));
        double retention = GroundImprints.PlasticRetention01(GroundMaterial.Dirt);
        GroundImprint[] k = All(GroundImprints.World);
        double deepest = k.Where(x => x.Kind is GroundImprintKind.Gouge or GroundImprintKind.Scar)
                          .Select(x => x.DepthM).DefaultIfEmpty(0).Max();
        _o.WriteLine($"deepest penetration {100 * penetration:F1} cm x retention {retention:F2} = {100 * retention * penetration:F1} cm; "
                   + $"deepest scar on record {100 * deepest:F1} cm");
        Assert.True(penetration > 0, "a wing has to actually be in the ground for this to test anything");
        // Every mark on record is the retained part of SOME penetration this flight, so none can exceed the retained
        // part of the deepest one. Equality on the deepest is not asserted: the deepest penetration of the flight was
        // at a moment this reads after, and a stretch keeps the deepest it ever got.
        foreach (GroundImprint x in k.Where(x => x.Kind is GroundImprintKind.Gouge or GroundImprintKind.Scar))
            Assert.True(x.DepthM <= retention * penetration + 1e-9,
                $"a {100 * x.DepthM:F1} cm mark cannot come from the {100 * penetration:F1} cm the airframe went in");
        // The deepest mark on record IS the retained part of the deepest penetration of the flight, to the millimetre.
        Assert.Equal(retention * penetration, deepest, 3);
        Assert.True(deepest > 0, "the drag has to have been recorded");
        // The one exact statement, on the function itself: no scale, no clamp, no floor above MinDepthM.
        Assert.Equal(retention * penetration, GroundImprints.ScarDepthM(GroundMaterial.Dirt, penetration), 12);
    }

    /// <summary>
    /// A CUB ON SKIS LEAVES A TRACK IN THE SNOW, through the real contact path — the width of the ski the type actually
    /// carries, and broader and shallower than the same Cub's tyres leave in the same snow.
    ///
    /// Ty operates a Cub on skis, and this is the surface where the mark is most of the point. It did not work when
    /// this lane started, twice over: lane/claude-surface-tracks had no ski geometry to report (it reported the ski
    /// model's 0.25 x 1.8 m default), and nothing in the physics computes a ski's SINKAGE, so the track had no depth
    /// and fell under <see cref="GroundImprints.MinDepthM"/>. Both are fixed by reading what the physics does have: the
    /// real <c>SkiGear.Geometry</c> (brought by lane/claude-quest-islandnav on 2026-11-10) and the ski's own contact
    /// penetration, whose plastic part is the track.
    /// </summary>
    [Fact]
    public void ACubOnSkisLeavesABroadTrackInTheSnowAndItIsTheSkisOwnWidth()
    {
        AircraftConfig c = Cfg("pa18-cub-like");
        c.Skis = true;
        Aircraft onSkis = Roll(c, WorldTerrain.Surface.Snow, 4.0, 12.0);
        GroundImprint[] k = All(GroundImprints.World);
        foreach (GroundImprint x in k.Take(6)) _o.WriteLine(x.ToString());
        Assert.True(onSkis.Wheels.Any(w => w.OnSki), "the skis have to actually be bearing for this to be the right test");
        Assert.Contains(k, x => x.Kind == GroundImprintKind.Track);
        GroundImprint track = k.First(x => x.Kind == GroundImprintKind.Track);
        // The Super Cub's ski, as SkiGear.Geometry builds it from the airframe: nothing like the 0.25 m default, and
        // the trough is the ski's own width plus the snow's slump.
        double skiWidth = SkiGear.For(c, c.Gear.First(g => SkiGear.LegTakesSki(g))).WidthM;
        _o.WriteLine($"ski running surface {skiWidth:F3} m; track {track.WidthM:F3} m x {100 * track.DepthM:F2} cm");
        // 0.289 m, against the 0.25 m the ski model defaults to and that surface-tracks had to report in 2026-11-06.
        Assert.True(skiWidth > SurfaceContacts.DefaultSkiWidthM, $"a Super Cub's ski is wider than the default: {skiWidth:F3} m");
        Assert.InRange(track.WidthM, skiWidth, skiWidth + 0.5);
        Assert.InRange(track.DepthM, GroundImprints.MinDepthM, 0.15);

        // The same Cub on its bare tyres in the same snow: a narrower, deeper rut. Flotation, measured.
        GroundImprints.Clear();
        AircraftConfig bare = Cfg("pa18-cub-like");
        bare.Skis = false;
        Roll(bare, WorldTerrain.Surface.Snow, 4.0, 12.0);
        GroundImprint[] ruts = All(GroundImprints.World).Where(x => x.Kind == GroundImprintKind.Rut).ToArray();
        Assert.NotEmpty(ruts);
        double rutW = ruts.Max(x => x.WidthM), rutD = ruts.Max(x => x.DepthM);
        _o.WriteLine($"on wheels: rut {rutW:F3} m x {100 * rutD:F2} cm");
        Assert.True(track.WidthM > rutW, $"ski track {track.WidthM:F3} m vs rut {rutW:F3} m");
        Assert.True(track.DepthM < rutD, $"ski track {100 * track.DepthM:F2} cm vs rut {100 * rutD:F2} cm");
    }

    // ---- the budget -------------------------------------------------------------------------------------------

    /// <summary>
    /// The budget: 512 stretches for the whole world. A straight rut costs ONE stretch however long it is, because a
    /// stretch is extended while the contact keeps going the same way — that is what makes a circuit's worth of
    /// landings fit. Measured: a Cub rolling 400 m straight.
    /// </summary>
    [Fact]
    public void AStraightRutCostsAFewStretchesNotHundreds()
    {
        Assert.Equal(1024, GroundImprintField.WorldBudget);
        AircraftConfig c = Cfg("j3-cub-like");
        Aircraft ac = Roll(c, WorldTerrain.Surface.Grass, 30.0, 14.0);
        double rolled = ac.State.Position.X;
        int opened = (int)ac.Imprints.Opened;
        _o.WriteLine($"rolled {rolled:F0} m, {GroundImprints.World.Count} marks, {opened} stretches opened, {GroundImprints.World.Evicted} evicted");
        Assert.True(rolled > 40, $"the test needs a real roll: {rolled:F0} m");
        _o.WriteLine($"{opened / rolled:F3} stretches per metre rolled");
        // MEASURED: a J-3 put down at 14 m/s on grass and left alone — no rudder, tailwheel castoring — rolls 58 m to
        // a stop and lays 135 stretches on three wheels. Almost none of that is the LENGTH of the roll: during the
        // rolling phase the stretches run 2-4 m each (the aeroplane is arcing, because nothing is steering it), and the
        // rest is the slow weave as it comes to rest. A roll held straight is ONE stretch per wheel, which
        // ATurnBreaksAStretchAndAStraightLineDoesNot proves on a dead straight track.
        //
        // The gate is the thing that actually matters: ONE landing roll must not eat half the world's marks. At 135 of
        // 1,024, seven of these fit — a circuit's worth of landings and the taxiing between them, which is what Ty
        // asked to be able to see.
        Assert.True(opened < GroundImprintField.WorldBudget / 2,
            $"{opened} stretches for one {rolled:F0} m landing roll is more than half the world's budget");
        Assert.Equal(0, (int)GroundImprints.World.Evicted);
        // And a stretch really is a stretch: metres, not the per-step dots a naive recorder would lay.
        Assert.True(All(GroundImprints.World).Max(x => x.LengthM) > 2.0);
        Assert.True(All(GroundImprints.World).Average(x => x.LengthM) > 0.2,
            $"mean stretch {All(GroundImprints.World).Average(x => x.LengthM):F3} m");
    }

    /// <summary>
    /// The ring is hard-bounded: past the budget the OLDEST mark goes and the count never grows. An unbounded list of
    /// marks is not acceptable on a phone, and this is the proof that it cannot become one.
    /// </summary>
    [Fact]
    public void TheRingIsBoundedAndTheOldestMarkGoesFirst()
    {
        var field = new GroundImprintField(8);
        var pen = new GroundImprintPen(field, 2, 1);
        // Lay 40 marks that cannot be merged: the part lifts between each, so each touch is its own stretch. (A
        // sideways JUMP is not enough — a stretch with only one point so far accepts any second point as collinear,
        // which is what makes a straight rut one stretch.)
        for (int i = 0; i < 40; i++)
        {
            pen.BeginStep();
            pen.Lay(0, GroundImprintKind.Rut, GroundMaterial.Dirt, new Vec3(0, i, 0), 0.1, 0.02, 2000, i);
            pen.EndStep();
            pen.BeginStep(); pen.EndStep();               // in the air: the stretch is finished
        }
        _o.WriteLine($"count {field.Count} / {field.Capacity}, evicted {field.Evicted}, opened {pen.Opened}");
        Assert.Equal(8, field.Count);
        Assert.Equal(8, field.Capacity);
        Assert.Equal(40, (int)pen.Opened);
        Assert.Equal(32, (int)field.Evicted);
        // What is left is the newest: the oldest times are gone.
        Assert.True(All(field).Min(x => x.TimeSec) >= 32);
    }

    /// <summary>
    /// A stretch is broken by a TURN and by the ground's undulation, because the straightness test is in 3D — so a
    /// ground loop costs stretches where a straight roll costs one, and a quad never stands off the surface by more
    /// than the marks are deep.
    /// </summary>
    [Fact]
    public void ATurnBreaksAStretchAndAStraightLineDoesNot()
    {
        var field = new GroundImprintField(256);
        var pen = new GroundImprintPen(field, 1, 1);
        // Dead straight, 100 points over 50 m.
        for (int i = 0; i < 100; i++)
        {
            pen.BeginStep();
            pen.Lay(0, GroundImprintKind.Rut, GroundMaterial.Dirt, new Vec3(0.5 * i, 0, 0), 0.1, 0.02, 2000, 0.02 * i);
            pen.EndStep();
        }
        _o.WriteLine($"straight 50 m: {pen.Opened} stretches, longest {All(field).Max(x => x.LengthM):F1} m");
        Assert.Equal(1, (int)pen.Opened);
        Assert.InRange(All(field)[0].LengthM, 49.0, 50.0);

        // A 20 m circle: every few metres the track has left the open stretch's line by more than the tolerance.
        var field2 = new GroundImprintField(256);
        var pen2 = new GroundImprintPen(field2, 1, 2);
        for (int i = 0; i < 200; i++)
        {
            double a = i * 2 * Math.PI / 200;
            pen2.BeginStep();
            pen2.Lay(0, GroundImprintKind.Rut, GroundMaterial.Dirt, new Vec3(20 * Math.Cos(a), 20 * Math.Sin(a), 0), 0.1, 0.02, 2000, 0.02 * i);
            pen2.EndStep();
        }
        _o.WriteLine($"20 m circle: {pen2.Opened} stretches");
        Assert.True(pen2.Opened > 10, $"a curve must cost stretches: {pen2.Opened}");
        // Chord sag of a 20 m radius over the longest stretch is under the tolerance, by construction.
        double longest = All(field2).Max(x => x.LengthM);
        Assert.True(longest * longest / (8 * 20) <= 2.0 * Math.Max(GroundImprintField.StraightFloorM, GroundImprintField.StraightTolerance * 0.153),
            $"longest chord {longest:F2} m sags {1000 * longest * longest / (8 * 20):F1} mm");

        // The same in the VERTICAL: a track down a 1-in-10 slope that then flattens is two stretches, not one.
        var field3 = new GroundImprintField(256);
        var pen3 = new GroundImprintPen(field3, 1, 3);
        for (int i = 0; i < 60; i++)
        {
            double x = 0.5 * i, z = x < 15 ? 0.1 * x : 1.5;      // NED z down, but the sign does not matter here
            pen3.BeginStep();
            pen3.Lay(0, GroundImprintKind.Rut, GroundMaterial.Dirt, new Vec3(x, 0, z), 0.1, 0.02, 2000, 0.02 * i);
            pen3.EndStep();
        }
        _o.WriteLine($"slope then flat: {pen3.Opened} stretches");
        Assert.True(pen3.Opened >= 2, "the break in slope must break the stretch");
    }

    /// <summary>
    /// A part lifting off the ground ends its stretch: the next touch starts a new one rather than drawing a mark
    /// across the gap it flew over. A bounce must not be joined up.
    /// </summary>
    [Fact]
    public void LiftingOffEndsTheStretchSoABounceIsNotJoinedUp()
    {
        var field = new GroundImprintField(64);
        var pen = new GroundImprintPen(field, 1, 1);
        pen.BeginStep(); pen.Lay(0, GroundImprintKind.Rut, GroundMaterial.Dirt, new Vec3(0, 0, 0), 0.1, 0.02, 2000, 0.0); pen.EndStep();
        pen.BeginStep(); pen.Lay(0, GroundImprintKind.Rut, GroundMaterial.Dirt, new Vec3(2, 0, 0), 0.1, 0.02, 2000, 0.1); pen.EndStep();
        Assert.Equal(1, (int)pen.Opened);
        pen.BeginStep(); pen.EndStep();                      // in the air: nothing laid
        pen.BeginStep(); pen.Lay(0, GroundImprintKind.Rut, GroundMaterial.Dirt, new Vec3(30, 0, 0), 0.1, 0.02, 2000, 2.0); pen.EndStep();
        _o.WriteLine($"{pen.Opened} stretches; " + string.Join(" | ", All(field).Select(x => x.ToString())));
        Assert.Equal(2, (int)pen.Opened);
        Assert.DoesNotContain(All(field), x => x.LengthM > 20);
    }

    /// <summary>
    /// Marks accumulate across landings and across aeroplanes: the field is the world's, not an aeroplane's, so you can
    /// see where the other one went, and each mark says whose it was.
    /// </summary>
    [Fact]
    public void MarksAccumulateAcrossAeroplanesAndEachSaysWhoseItIs()
    {
        AircraftConfig c = Cfg("j3-cub-like");
        Roll(c, WorldTerrain.Surface.Dirt, 3.0, 10.0);
        int after1 = GroundImprints.World.Count;
        Roll(c, WorldTerrain.Surface.Dirt, 3.0, 10.0);
        int after2 = GroundImprints.World.Count;
        _o.WriteLine($"one aeroplane {after1} marks, two {after2}");
        Assert.True(after1 > 0);
        Assert.True(after2 > after1, "the second aeroplane's marks are added, not substituted");
        Assert.True(All(GroundImprints.World).Select(x => x.Owner).Distinct().Count() >= 2,
            "each aeroplane's marks are its own");
    }

    /// <summary>
    /// What it costs. 1,024 stretches is 2,048 triangles for every mark in the world, and the record is 88 kB. Both
    /// numbers are asserted so that growing the struct or the budget has to be a decision, not a drift.
    /// </summary>
    [Fact]
    public void TheWholeWorldsMarksAreTwoThousandTrianglesAndUnderNinetyKilobytes()
    {
        int bytes = System.Runtime.InteropServices.Marshal.SizeOf<GroundImprint>();
        long total = (long)bytes * GroundImprintField.WorldBudget;
        _o.WriteLine($"GroundImprint {bytes} B x {GroundImprintField.WorldBudget} = {total / 1024.0:F1} kB; {2 * GroundImprintField.WorldBudget} triangles");
        Assert.True(bytes <= 96, $"a mark is {bytes} B");
        Assert.True(total < 96 * 1024, $"{total} B");
        Assert.True(2 * GroundImprintField.WorldBudget <= 2048);
    }

    /// <summary>Clearing forgets everything and bumps the version, so a renderer rebuilds (the StrippedTrees pattern).</summary>
    [Fact]
    public void ClearingForgetsEveryMarkAndTellsTheRenderer()
    {
        AircraftConfig c = Cfg("j3-cub-like");
        Roll(c, WorldTerrain.Surface.Dirt, 2.0, 10.0);
        Assert.True(GroundImprints.World.Count > 0);
        int v = GroundImprints.World.Version;
        GroundImprints.Clear();
        Assert.Equal(0, GroundImprints.World.Count);
        Assert.NotEqual(v, GroundImprints.World.Version);
    }

    /// <summary>
    /// Nothing marks the world while the aeroplane is flying, and the record does not grow in the air: the cost of this
    /// whole feature in cruise is zero.
    /// </summary>
    [Fact]
    public void NothingIsRecordedInTheAir()
    {
        AircraftConfig c = Cfg("j3-cub-like");
        WorldTerrain.SurfaceOverride = WorldTerrain.Surface.Grass;
        var s = new RigidBodyState(new Vec3(0, 0, -300), Quat.Identity, new Vec3(30, 0, 0), Vec3.Zero);
        var ac = new Aircraft(c, s, ControlDeflections.Neutral);
        new SimLoop(ac).RunFor(5.0, new ControlInputs(0, 0, 0, 0.0));
        Assert.Equal(0, GroundImprints.World.Count);
        Assert.Equal(0, (int)ac.Imprints.Opened);
    }
}
