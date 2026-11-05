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
/// HINGED WHEEL-SKIS (Ty 2026-11-05 07:58–07:59): "wheel skis as an option on every airplane … a selector for wheels/skis …
/// they rotate forward and up when raised and rotate down and aft when lowered and the tire sits on top of the ski when
/// lowered instead of poking down below". The geometry is checked on every aeroplane, the contact is flown, and the
/// selector is the thing that moves it.
/// </summary>
[Collection("WorldTerrainActive")]
public class WheelSkiHingeTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public WheelSkiHingeTests(ITestOutputHelper o) { _o = o; }

    public void Dispose()
    {
        WorldTerrain.Active = null;
        WorldTerrain.SurfaceOverride = null;
        Atmosphere.SteadyWind = Vec3.Zero;
    }

    private static readonly ControlInputs Idle = new(0, 0, 0, 1);
    private const string Cub = WheelSkiTests.Cub;

    private static AircraftConfig Load(string id, bool skis)
    {
        AircraftConfig c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
        c.Skis = skis;
        return c;
    }

    /// <summary>Every aeroplane config shipped (configs/aircraft), by id.</summary>
    public static IEnumerable<object[]> AllAircraft()
    {
        string dir = Path.Combine(RepoRoot(), "configs", "aircraft");
        foreach (string f in Directory.GetFiles(dir, "*.json").OrderBy(x => x))
            yield return new object[] { Path.GetFileNameWithoutExtension(f) };
    }

    private static string RepoRoot()
    {
        string d = AppContext.BaseDirectory;
        while (d != null && !Directory.Exists(Path.Combine(d, "configs", "aircraft"))) d = Path.GetDirectoryName(d)!;
        return d!;
    }

    private static AircraftConfig LoadAny(string id, bool skis)
    {
        AircraftConfig c = AircraftConfigLoader.LoadFromFile(Path.Combine(RepoRoot(), "configs", "aircraft", id + ".json"));
        c.Skis = skis;
        return c;
    }

    // ---- the geometry, on every aeroplane --------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllAircraft))]
    public void EveryWheelTakesTheOptionAndItsHingeWorks(string id)
    {
        AircraftConfig c = LoadAny(id, skis: true);
        bool anyWheel = c.Gear.Any(SkiGear.LegTakesSki);
        Assert.Equal(anyWheel, SkiGear.AnyFitted(c));
        foreach (GearConfig g in c.Gear)
        {
            if (!SkiGear.LegTakesSki(g)) continue;
            SkiGear.Geometry geo = SkiGear.For(c, g);
            double tyre = geo.TyreBottom.Z;                                        // body z, down
            // LOWERED: the ski's top is at the tyre's bottom (the tyre SITS ON the ski, within the 2 mm seat gap) and
            // its bottom is the lowest thing on the leg by its thickness.
            Vec3 pinDown = geo.PinAt(SkiGear.LoweredLinkRad);
            Assert.InRange(pinDown.Z - tyre, 0.0, 0.0025);
            Assert.True(geo.BottomUnderPinAt(SkiGear.LoweredLinkRad).Z > tyre + 0.03, $"{id} {g.GearType}@{g.Pos[0]:F2}: lowered ski bottom not below the tyre");
            // and with solid ski under the tyre: the slot ends at least 1 cm aft of where the tyre rests.
            double rest = geo.Axle.X - pinDown.X;
            Assert.True(geo.SlotFwdM < rest - 0.01, $"{id} {g.GearType}@{g.Pos[0]:F2}: the slot ({geo.SlotFwdM:F3} m) reaches the tyre's resting point ({rest:F3} m)");
            // RAISED: the ski's bottom is clear above the tyre's bottom, so the tyre carries the leg.
            double clear = tyre - geo.BottomUnderPinAt(SkiGear.RaisedLinkRad).Z;
            Assert.True(clear > 0.02, $"{id} {g.GearType}@{g.Pos[0]:F2}: raised ski only {clear * 100:F1} cm above the tyre's bottom");
            // and the slot fits inside the ski (it may open at the heel on a short tail ski, never past the front).
            Assert.True(geo.SlotFwdM < geo.FlatFwdM);
        }
    }

    [Fact]
    public void RaisingSwingsTheSkiForwardAndUpLoweringDownAndAft()
    {
        AircraftConfig c = Load(Cub, true);
        GearConfig main = c.Gear.First(g => !g.IsTailwheel);
        SkiGear.Geometry geo = SkiGear.For(c, main);
        Vec3 prev = geo.PinAt(SkiGear.LinkRad(1.0));
        for (int i = 9; i >= 0; i--)
        {
            Vec3 p = geo.PinAt(SkiGear.LinkRad(i / 10.0));
            Assert.True(p.X > prev.X, "raising: forward at every step");
            // Up (body z falling) once the over-centre links have passed straight down (the first 24° of travel).
            if (SkiGear.LinkRad(i / 10.0) > -SkiGear.LoweredLinkRad) Assert.True(p.Z < prev.Z, "raising: up");
            prev = p;
        }
        Vec3 up = geo.PinAt(SkiGear.RaisedLinkRad), down = geo.PinAt(SkiGear.LoweredLinkRad);
        _o.WriteLine($"Cub main ski: lowered→raised moves {100 * (up.X - down.X):F1} cm forward, {100 * (down.Z - up.Z):F1} cm up; " +
                     $"L {geo.LengthM:F2} W {geo.WidthM:F2} T {geo.ThicknessM:F3} link {geo.LinkM:F3}; slot {geo.SlotAftM:F3}..{geo.SlotFwdM:F3}");
        // On the Cub's 8.00-6 the links pivot on the axle itself, and every pin is the link's length from it.
        Assert.Equal(0.0, (geo.Pivot - geo.Axle).Length, 9);
        for (int i = 0; i <= 10; i++)
            Assert.Equal(geo.LinkM, (geo.PinAt(SkiGear.LinkRad(i / 10.0)) - geo.Axle).Length, 9);
    }

    [Theory]
    [InlineData(WheelSkiTests.Cub)]
    [InlineData("c172-like")]
    [InlineData("dhc2-beaver-like")]
    [InlineData("pitts-s2b-like")]
    public void TheTyreNeverPassesThroughSolidSki(string id)
    {
        // At every link angle, every point of the tyre's circle that is inside the ski's slab must be over the slot.
        AircraftConfig c = Load(id, true);
        foreach (GearConfig g in c.Gear.Where(SkiGear.LegTakesSki))
        {
            SkiGear.Geometry geo = SkiGear.For(c, g);
            for (int i = 0; i <= 200; i++)
            {
                double th = SkiGear.LinkRad(i / 200.0);
                Vec3 pin = geo.PinAt(th);
                for (int k = 0; k < 360; k++)
                {
                    double a = k * Math.PI / 180;
                    double x = geo.Axle.X + geo.TyreRadiusM * Math.Sin(a), z = geo.Axle.Z + geo.TyreRadiusM * Math.Cos(a);
                    bool inSlab = z > pin.Z + 1e-6 && z < pin.Z + geo.ThicknessM;
                    double along = x - pin.X;
                    bool onSki = along > geo.FlatAftM && along < geo.FlatFwdM;
                    if (inSlab && onSki)
                        Assert.True(along >= geo.SlotAftM && along <= geo.SlotFwdM,
                                    $"{id} {g.GearType}@{g.Pos[0]:F2}: tyre through solid ski at link {th * 180 / Math.PI:F0}°, {along:F3} m along");
                }
            }
        }
    }

    // ---- the contact ----------------------------------------------------------------------------------------------

    private static (Aircraft ac, SimLoop loop) OnTheGround(string id, bool skis, WorldTerrain.Surface surface, double v, SkiSelector.Position? lever = null)
    {
        WorldTerrain.Active = null;
        Atmosphere.SteadyWind = Vec3.Zero;
        WorldTerrain.SurfaceOverride = surface;
        AircraftConfig c = Load(id, skis);
        double lowered = lever == null ? double.NaN : lever == SkiSelector.Position.Skis ? 1.0 : 0.0;
        RigidBodyState rest = LandingGear.RestingState(c, 0, 0, 0, 0, lowered);
        var ac = new Aircraft(c, new RigidBodyState(rest.Position, rest.Attitude, new Vec3(v, 0, 0), Vec3.Zero), new ControlDeflections(0, 0, 0, 0));
        if (lever != null && ac.Skis != null)
        {
            // Put the skis where the lever is before the roll starts (the aeroplane is resting for that position already).
            ac.Skis.Selector.Select(lever.Value);
            ac.Skis.Selector.Step(2 * SkiGear.TransitSec);
        }
        return (ac, new SimLoop(ac));
    }

    private static double RollOut(Aircraft ac, SimLoop loop)
    {
        double start = ac.State.Position.X;
        for (int i = 0; i < 12000 && ac.State.Velocity.Length > 0.1; i++) loop.RunFor(SimLoop.DefaultFixedDtSec, Idle);
        return ac.State.Position.X - start;
    }

    [Fact]
    public void LoweredTheSkiBearsAndTheTyreIsAboveItsBottom()
    {
        var (ac, loop) = OnTheGround(Cub, true, WorldTerrain.Surface.Snow, 0, SkiSelector.Position.Skis);
        for (int i = 0; i < 400; i++) loop.RunFor(SimLoop.DefaultFixedDtSec, Idle);
        SkiState st = ac.Skis!;
        for (int gi = 0; gi < ac.Config.Gear.Count; gi++)
        {
            if (st.Legs[gi] == null) continue;
            Assert.True(st.Bearing[gi], $"lowered: the leg {gi} ski should be on the snow");
            // The tyre's bottom is above the ski's: it sits on the ski. (Both points are rigid on the airframe in this
            // model — the leg's give is the contact sinking in — so this is measured between them, not against the snow.)
            Vec3 tyre = ac.State.Position + ac.State.Attitude.Rotate(ac.Config.Gear[gi].PosVec() - ac.Config.Mass.CgVec());
            SkiGear.Contact sc = SkiGear.Probe(st.Legs[gi]!, ac.Config.Mass.CgVec(), ac.State, st.Lowered01, 0);
            Assert.True(sc.PointWorld.Z - tyre.Z > st.Legs[gi]!.ThicknessM * 0.9, $"leg {gi}: tyre only {(sc.PointWorld.Z - tyre.Z) * 100:F1} cm above the ski's bottom");
        }
    }

    [Fact]
    public void RaisedTheTyresBearAndTheSkisAreClear()
    {
        var (ac, loop) = OnTheGround(Cub, true, WorldTerrain.Surface.Paved, 0, SkiSelector.Position.Wheels);
        for (int i = 0; i < 400; i++) loop.RunFor(SimLoop.DefaultFixedDtSec, Idle);
        Assert.All(ac.Skis!.Bearing, b => Assert.False(b));
        // and the aeroplane stands where a plain Cub stands, within the tyres' extra squash under 26 kg of ski.
        var (plain, plainLoop) = OnTheGround(Cub, false, WorldTerrain.Surface.Paved, 0);
        for (int i = 0; i < 400; i++) plainLoop.RunFor(SimLoop.DefaultFixedDtSec, Idle);
        Assert.InRange(ac.State.Position.Z - plain.State.Position.Z, -0.002, 0.01);
    }

    [Fact]
    public void LoweredOnPavementTheSkiScrapesOnSnowItRuns()
    {
        // Skis down, same speed, throttle shut: the snow lets the ski run on, the runway grabs it.
        var (sa, sl) = OnTheGround(Cub, true, WorldTerrain.Surface.Snow, 8, SkiSelector.Position.Skis);
        double snow = RollOut(sa, sl);
        var (pa, pl) = OnTheGround(Cub, true, WorldTerrain.Surface.Paved, 8, SkiSelector.Position.Skis);
        double paved = RollOut(pa, pl);
        var (wa, wl) = OnTheGround(Cub, true, WorldTerrain.Surface.Paved, 8, SkiSelector.Position.Wheels);
        double wheels = RollOut(wa, wl);
        _o.WriteLine($"Cub from 8 m/s, idle: skis down on snow {snow:0.0} m, skis down on pavement {paved:0.0} m, skis up on pavement {wheels:0.0} m");
        // v²/(2μg) with μ 0.35 on pavement is 9.3 m; the idle propeller's thrust pushes it a little further (it is the
        // same thrust that carries the Cub 93 m on its wheels here).
        Assert.True(paved < 15, $"a ski on pavement slid {paved:0.0} m");
        Assert.True(snow > 4 * paved, "the ski should run much further on snow than on pavement");
        Assert.True(wheels > 3 * paved, "on its wheels the same aeroplane rolls far further than on scraping skis");
    }

    [Fact]
    public void RaisedOnSnowItIsAWheelPlane()
    {
        // Skis up on snow: the tyres are in the snow, and it stops like the plain Cub does (within the skis' weight).
        var (ua, ul) = OnTheGround(Cub, true, WorldTerrain.Surface.Snow, 8, SkiSelector.Position.Wheels);
        double up = RollOut(ua, ul);
        var (pa, pl) = OnTheGround(Cub, false, WorldTerrain.Surface.Snow, 8);
        double plain = RollOut(pa, pl);
        _o.WriteLine($"on snow: skis up {up:0.0} m, plain wheels {plain:0.0} m");
        Assert.InRange(up / plain, 0.95, 1.05);
    }

    // ---- weight and drag ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(WheelSkiTests.Cub, 15.0, 40.0)]            // published kit increase 50–80 lb on a Cub incl. hydraulics; ours is the skis' share
    [InlineData("dhc2-beaver-like", 35.0, 100.0)]
    public void TheSkisWeighWhatAKitWeighs(string id, double lo, double hi)
    {
        AircraftConfig c = Load(id, true);
        double kg = SkiGear.TotalMassKg(c);
        var ac = new Aircraft(c, LandingGear.RestingState(c, 0, 0, 0, 0));
        var plain = new Aircraft(Load(id, false), LandingGear.RestingState(Load(id, false), 0, 0, 0, 0));
        _o.WriteLine($"{id}: skis {kg:F1} kg, drag area {SkiGear.TotalDragAreaM2(c):F3} m²; mass {plain.MassProperties.MassKg:F0} → {ac.MassProperties.MassKg:F0} kg");
        Assert.InRange(kg, lo, hi);
        Assert.Equal(plain.MassProperties.MassKg + kg, ac.MassProperties.MassKg, 6);
        Assert.True(ac.MassProperties.Ixx > plain.MassProperties.Ixx && ac.MassProperties.Iyy > plain.MassProperties.Iyy);
        // Hung under the airframe: the CG moves down (body +z).
        Assert.True(ac.CgShiftBody.Z > 0);
    }

    [Fact]
    public void TheSkisCostAirspeed()
    {
        // Same Cub, same state at 80 mph in still air: the one on skis decelerates harder by the skis' own drag.
        static double DecelAt(bool skis)
        {
            WorldTerrain.Active = null;
            AircraftConfig c = Load(Cub, skis);
            var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -500), Quat.Identity, new Vec3(36, 0, 0), Vec3.Zero));
            var loop = new SimLoop(ac);
            loop.RunFor(0.05, Idle);
            return 36 - ac.State.Velocity.Length;
        }
        double with = DecelAt(true), without = DecelAt(false);
        AircraftConfig sc = Load(Cub, true);
        _o.WriteLine($"lost in 0.05 s at 36 m/s: skis {with:F4} m/s, wheels {without:F4} m/s; ski drag area {SkiGear.TotalDragAreaM2(sc):F3} m²");
        Assert.True(with > without, "the skis add drag");
        // D = ½ρV²·f = ½ · 1.225 · 36² · 0.167 = 132 N on 820 kg: 0.16 m/s², 0.008 m/s in 0.05 s; less the 3 % the
        // skis' own mass takes off the rest of the deceleration (1.37 m/s² × 0.033 × 0.05 s = 0.002 m/s): about 0.006.
        Assert.InRange(with - without, 0.004, 0.008);
    }

    // ---- the selector drives it ----------------------------------------------------------------------------------

    [Fact]
    public void TheSelectorRunsTheSkisAndTheAnnunciatorFollows()
    {
        var sel = new SkiSelector(SkiSelector.Position.Wheels);
        Assert.Equal(SkiSelector.Light.Wheels, sel.Annunciation);
        sel.Select(SkiSelector.Position.Skis);
        sel.Step(1.0);
        Assert.Equal(SkiSelector.Light.Transit, sel.Annunciation);
        Assert.Equal(1.0 / SkiGear.TransitSec, sel.Lowered01, 9);
        sel.Step(SkiGear.TransitSec);
        Assert.Equal(SkiSelector.Light.Skis, sel.Annunciation);
        sel.Toggle();
        Assert.Equal(SkiSelector.Position.Wheels, sel.Commanded);
        sel.Step(0.5 * SkiGear.TransitSec);
        Assert.Equal(0.5, sel.Lowered01, 9);
        // The lever shows the command, the lamps the skis.
        Assert.Equal(SkiSelectorPart.WheelsDeg, SkiSelectorPart.LeverDeg(sel.Commanded));
        Assert.Equal(1, SkiSelectorPart.LitLamp(sel.Annunciation));
    }

    [Fact]
    public void SelectingSkisOnTheRunwayLowersThemUnderTheTyresAndJacksItUp()
    {
        // Standing on its wheels on pavement, the pilot selects SKIS: the skis swing down and aft under the tyres and
        // lift the aeroplane onto themselves — the geometry's doing, nothing scripted.
        var (ac, loop) = OnTheGround(Cub, true, WorldTerrain.Surface.Paved, 0, SkiSelector.Position.Wheels);
        for (int i = 0; i < 400; i++) loop.RunFor(SimLoop.DefaultFixedDtSec, Idle);
        double z0 = ac.State.Position.Z;
        Assert.All(ac.Skis!.Bearing, b => Assert.False(b));
        ac.Skis.Selector.Select(SkiSelector.Position.Skis);
        bool sawTransit = false;
        double t = 0;
        while (t < SkiGear.TransitSec + 3)
        {
            loop.RunFor(SimLoop.DefaultFixedDtSec, Idle);
            t += SimLoop.DefaultFixedDtSec;
            sawTransit |= ac.Skis.Selector.Annunciation == SkiSelector.Light.Transit;
        }
        Assert.True(sawTransit);
        Assert.Equal(SkiSelector.Light.Skis, ac.Skis.Selector.Annunciation);
        for (int gi = 0; gi < ac.Config.Gear.Count; gi++) if (ac.Skis.Legs[gi] != null) Assert.True(ac.Skis.Bearing[gi]);
        double lift = z0 - ac.State.Position.Z;
        _o.WriteLine($"selecting SKIS on the runway lifted the Cub {lift * 100:F1} cm");
        // By about the ski's thickness (5 cm on the Cub) plus the tyre's static squash coming out.
        Assert.InRange(lift, 0.03, 0.12);
    }

    [Fact]
    public void ASkiDownOnTheTailwheelSteersOnItsEdgeNotItsTyre()
    {
        // On packed snow the tail ski's lateral hold is the ski's edge (0.12), not the tyre's grip.
        var (ac, loop) = OnTheGround(Cub, true, WorldTerrain.Surface.PackedGlacierSnow, 5, SkiSelector.Position.Skis);
        for (int i = 0; i < 200; i++) loop.RunFor(SimLoop.DefaultFixedDtSec, new ControlInputs(0, 0, 0.5, 1));
        Assert.NotNull(ac.Tailwheel);
        Assert.True(ac.Tailwheel!.LastLoadN > 0);
        Assert.True(Math.Abs(ac.Tailwheel.LastLateralN) <= (0.12 + 1e-6) * ac.Tailwheel.LastLoadN + 1e-6);
    }

    // ---- one shared selector ---------------------------------------------------------------------------------------

    [Fact]
    public void TheSelectorIsOneSharedPart()
    {
        string root = RepoRoot();
        // The part is drawn by exactly one runtime file, and nobody else builds one.
        string scripts = Path.Combine(root, "unity", "Assets", "_Project", "Scripts");
        var builders = Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("\"SkiSelector_")).Select(Path.GetFileName).ToList();
        Assert.Equal(new[] { "SkiSelectorLever.cs" }, builders);
        // No model or model builder carries a selector of its own.
        foreach (string f in Directory.GetFiles(Path.Combine(root, "tools", "own_models"), "*.py", SearchOption.AllDirectories))
            Assert.DoesNotContain("SkiSelector", File.ReadAllText(f));
        string models = Path.Combine(root, "unity", "Assets", "_Project", "Resources", "Models");
        foreach (string f in Directory.GetFiles(models, "*.glb", SearchOption.AllDirectories))
        {
            byte[] bytes = File.ReadAllBytes(f);
            int n = Math.Min(bytes.Length, 1 << 22);
            string head = System.Text.Encoding.ASCII.GetString(bytes, 0, n);
            Assert.DoesNotContain("SkiSelector", head);
        }
        // Configs place it by anchor only: a 3-vector on a seat, and nothing else.
        foreach (object[] row in AllAircraft())
        {
            AircraftConfig c = LoadAny((string)row[0], true);
            foreach (StationConfig s in c.Loading?.Seats ?? new List<StationConfig>())
                if (s.SkiSelector != null) Assert.Equal(3, s.SkiSelector.Length);
            List<SkiSelectorPart.Mount> mounts = SkiSelectorPart.MountsFor(c, new Vec3(0, 0, -0.5));
            Assert.Equal(SkiGear.AnyFitted(c), mounts.Count > 0);
        }
    }

    [Fact]
    public void TheDefaultMountIsUnderThePilotsLeftHand()
    {
        // The Super Cub has no seats authored, so the cockpit view's eye estimate stands in; the J-3 has its seats.
        AircraftConfig c = Load(Cub, true);
        Assert.Empty(SkiSelectorPart.MountsFor(c));
        SkiSelectorPart.Mount m = SkiSelectorPart.MountsFor(c, new Vec3(-0.5, 0, -0.6)).Single();
        Vec3 d = m.Pos - m.Eye;
        SkiSelectorPart.Mount j3 = SkiSelectorPart.MountsFor(Load("j3-cub-like", true)).Single();
        Assert.Equal(-0.84 + 0.50, j3.Pos.X, 9);   // the J-3's front seat eye (configs: x −0.84) plus the offset
        Assert.Equal(0.50, d.X, 9);     // ahead
        Assert.Equal(-0.22, d.Y, 9);    // left
        Assert.Equal(0.32, d.Z, 9);     // below
        Assert.Empty(SkiSelectorPart.MountsFor(Load(Cub, false)));
    }
}
