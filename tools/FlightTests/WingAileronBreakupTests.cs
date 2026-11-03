using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Owner 2026-11-02: "when the left wing panels break off, the ailerons stay attached". The ailerons are separate surfaces
/// in every config ("wing-aileron", the Pitts' "wing-upper-aileron" / "wing-lower-aileron", the Great Lakes' and Stearman's
/// "wing-lower-aileron", the Jenny's "wing-upper-aileron"); whatever takes a wing panel must take the aileron strips on it,
/// on that side only, and leave the other side's flying. (The visual half — AirframeVisual.DetachWings keeping a mesh that
/// lay wholly on the left — is Unity-only and is not reachable from here.)
/// </summary>
public class WingAileronBreakupTests
{
    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
    private static Aircraft Level(AircraftConfig c) =>
        new(c, new RigidBodyState(new Vec3(0, 0, -800), new Quat(0, 0, 0, 1), new Vec3(50, 0, 0), Vec3.Zero), ControlDeflections.Neutral);

    private static (int left, int leftMasked, int right, int rightMasked) Ailerons(Aircraft ac, Func<double, bool>? onPanel = null)
    {
        int idx = 0, l = 0, lm = 0, r = 0, rm = 0;
        foreach (SurfaceConfig sf in ac.Config.Surfaces)
            foreach (StripConfig st in sf.Strips)
            {
                bool masked = ac.StripMask is not null && !ac.StripMask[idx];
                if (sf.Id.Contains("aileron") && (onPanel is null || onPanel(st.Pos[1])))
                {
                    if (st.Pos[1] < 0) { l++; if (masked) lm++; } else { r++; if (masked) rm++; }
                }
                idx++;
            }
        return (l, lm, r, rm);
    }

    public static IEnumerable<object[]> AileronTypes() =>
        new[] { "pitts-s2b-like", "great-lakes-like", "stearman-pt17-like", "jn4-jenny-like", "extra-300-like", "c172-like", "pa18-cub-like" }
            .Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(AileronTypes))]
    public void LosingTheLeftWingTakesEveryLeftAileronStripAndNoRightOne(string id)
    {
        foreach (AirframeComponent comp in new[] { AirframeComponent.WingLeft, AirframeComponent.WingLeftInner })
        {
            var ac = Level(Load(id));
            ac.LoseComponent(comp);
            var (l, lm, r, rm) = Ailerons(ac);
            Assert.True(l > 0, $"{id}: has left aileron strips");
            Assert.True(lm == l, $"{id} {comp}: {l - lm} of {l} left aileron strips still fly");
            Assert.True(rm == 0, $"{id} {comp}: {rm} right aileron strips went with the left wing");
            Assert.True(ac.IsLost(AirframeComponent.AileronLeft), $"{id} {comp}: AileronLeft not recorded lost");
        }
        // The mirror image.
        var acR = Level(Load(id));
        acR.LoseComponent(AirframeComponent.WingRight);
        var (l2, lm2, r2, rm2) = Ailerons(acR);
        Assert.True(rm2 == r2 && lm2 == 0, $"{id} WingRight: right {rm2}/{r2} masked, left {lm2} masked");
    }

    [Theory]
    [MemberData(nameof(AileronTypes))]
    public void LosingTheLeftOuterPanelTakesTheAileronStripsOnIt(string id)
    {
        AircraftConfig cfg = Load(id);
        var ac = Level(cfg);
        ac.LoseComponent(AirframeComponent.WingLeftOuter);
        double semi = WingPanels.Semispan(cfg);
        var (l, lm, _, rm) = Ailerons(ac, y => WingPanels.PanelOf(y, semi) == AirframeComponent.WingLeftOuter);
        Assert.True(lm == l, $"{id}: {l - lm} of {l} aileron strips on the lost outer panel still fly");
        Assert.Equal(0, rm);
    }

    /// <summary>
    /// And in the aero: with the left wing gone, the left ailerons' deflection must not move the aircraft. Rolling the stick
    /// changes the aero moment only through the right aileron strips, exactly as if the left ones had never existed.
    /// </summary>
    [Theory]
    [MemberData(nameof(AileronTypes))]
    public void AFreedAileronMakesNoForce(string id)
    {
        AircraftConfig cfg = Load(id);
        var ac = Level(cfg);
        ac.LoseComponent(AirframeComponent.WingLeft);
        var tables = Aircraft.BuildAirfoilTables(cfg);
        bool[] mask = ac.StripMask!;
        // The same mask with every right-side aileron strip removed too: then the stick must do NOTHING at all.
        var noAilerons = (bool[])mask.Clone();
        int idx = 0;
        foreach (SurfaceConfig sf in cfg.Surfaces) foreach (StripConfig st in sf.Strips) { if (sf.Id.Contains("aileron") || (st.Control?.Surface == "aileron")) noAilerons[idx] = false; idx++; }
        Vec3 v = new Vec3(50, 0, 3);
        double d = cfg.Controls.Aileron.MaxDeflRad;
        Vec3 m0 = AeroModel.Compute(cfg, tables, v, Vec3.Zero, Vec3.Zero, 1.1, new ControlDeflections(0, 0, 0, 0), stripMask: noAilerons).Moment;
        Vec3 m1 = AeroModel.Compute(cfg, tables, v, Vec3.Zero, Vec3.Zero, 1.1, new ControlDeflections(d, 0, 0, 0), stripMask: noAilerons).Moment;
        Assert.True((m1 - m0).Length < 1e-9, $"{id}: aileron deflection still moves the aircraft with every aileron strip gone ({(m1 - m0).Length:E2} N·m)");
        // With the left wing lost the stick still rolls the aircraft (the right aileron flies) …
        Vec3 r0 = AeroModel.Compute(cfg, tables, v, Vec3.Zero, Vec3.Zero, 1.1, new ControlDeflections(0, 0, 0, 0), stripMask: mask).Moment;
        Vec3 r1 = AeroModel.Compute(cfg, tables, v, Vec3.Zero, Vec3.Zero, 1.1, new ControlDeflections(d, 0, 0, 0), stripMask: mask).Moment;
        Assert.True(Math.Abs(r1.X - r0.X) > 1.0, $"{id}: the right aileron should still roll the aircraft");
        // … and every left-side strip with an aileron on it is out of the aero.
        idx = 0;
        foreach (SurfaceConfig sf in cfg.Surfaces)
            foreach (StripConfig st in sf.Strips)
            {
                if (st.Pos[1] < -0.3 && (sf.Id.Contains("aileron") || st.Control?.Surface == "aileron"))
                    Assert.False(mask[idx], $"{id}: {sf.Id} strip at y {st.Pos[1]:F2} still flies with the left wing gone");
                idx++;
            }
    }

    [Theory]
    [MemberData(nameof(AileronTypes))]
    public void GLimitWingFailureTakesEveryAileronSurface(string id)
    {
        var ac = Level(Load(id));
        ac.FailWings();
        var tables = Aircraft.BuildAirfoilTables(ac.Config);
        // Every aileron row is a "wing" surface: with the wings failed, an aileron input must change nothing.
        double d = ac.Config.Controls.Aileron.MaxDeflRad;
        Vec3 v = new Vec3(50, 0, 3);
        bool[] sm = ac.SurfaceMask!;
        for (int i = 0; i < ac.Config.Surfaces.Count; i++)
            if (ac.Config.Surfaces[i].Id.Contains("aileron")) Assert.False(sm[i], $"{id}: {ac.Config.Surfaces[i].Id} still flies after the wings failed");
        Vec3 m0 = AeroModel.Compute(ac.Config, tables, v, Vec3.Zero, Vec3.Zero, 1.1, new ControlDeflections(0, 0, 0, 0), surfaceMask: sm).Moment;
        Vec3 m1 = AeroModel.Compute(ac.Config, tables, v, Vec3.Zero, Vec3.Zero, 1.1, new ControlDeflections(d, 0, 0, 0), surfaceMask: sm).Moment;
        Assert.True((m1 - m0).Length < 1e-9, $"{id}: aileron input still makes a moment after the wings failed");
    }
}
