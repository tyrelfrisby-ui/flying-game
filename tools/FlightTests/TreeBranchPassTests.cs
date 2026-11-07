using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Going through branches without hitting a trunk, and what the gear and floats meet (Ty 2026-11-05 13:49–13:52: the wing
/// "has too little drag; should yaw the airplane more toward the struck wing"; keep the soft tips, "solid center only below
/// about 4 ft from top"; gear and float strikes "drag with moment (e.g. nose-down pitch) AND velocity penalty"; "can fly
/// through every treetop with little/no slowdown").
///
/// Every run is a trimmed J-3 (AirStart) beside an untouched twin flown with the same stick, on flat ground at 0 with a
/// <see cref="FixedTrees"/> list, so each difference is the trees'. Measured on the base e3e754bc with the same flights
/// (a throwaway probe): the right wing 0.3 m inside a 25 m fir's crown 4 m below its top, 0.00 m/s and 0.0°; a wheel or a
/// float through four treetops, nothing at all (the gear had no strike edges); eight treetops 3 m deep, −0.21 m/s.
/// </summary>
[Collection("WorldTerrainActive")]
public class TreeBranchPassTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public TreeBranchPassTests(ITestOutputHelper o) { _o = o; WorldTerrain.Active = null; CrashEvents.ClearForTests(); }
    public void Dispose() { WorldTerrain.Active = null; CrashEvents.ClearForTests(); }

    private static AircraftConfig Load(string n) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", n + ".json"));
    private static Vec3 WV(Aircraft a) => a.State.Attitude.Rotate(a.State.Velocity);
    private static double Psi(Aircraft a) { Quat q = a.State.Attitude; return Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z)) * 180 / Math.PI; }
    private static double Theta(Aircraft a) { Quat q = a.State.Attitude; return Math.Asin(Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1)) * 180 / Math.PI; }

    private sealed class Run
    {
        public Aircraft Ac = null!, Twin = null!;
        public double DV, DVE, DPsi, DTheta, PeakYawRateDeg;
        public readonly List<ContactEvent> Wood = new();
        public override string ToString() => $"dV {DV:+0.00;-0.00} m/s (energy {DVE:+0.00;-0.00}), dPsi {DPsi:+0.0;-0.0}°, dTheta {DTheta:+0.0;-0.0}°, peak r {PeakYawRateDeg:0.0}°/s; " +
            $"strikes [{string.Join("; ", Ac.TreeStrikeLog.Select(r => $"{r.Edge} {r.Outcome} {r.StemDiameterM * 100:0.0} cm at {r.HeightM:0.00} m"))}]; lost [{string.Join(",", Ac.LostComponents)}]; " +
            $"wood events {Wood.Count} ({Wood.Sum(w => w.Intensity):0.0} kg); branch damage {Ac.MaxBranchDamage:0.00}";
    }

    /// <summary>Trimmed at <paramref name="cgUp"/> m, heading north; a row of <paramref name="n"/> firs of height
    /// <paramref name="h"/>, the first 25 m ahead, every <paramref name="spacing"/> m, their stems <paramref name="right"/> m
    /// to the right of the track. Flown until 0.5 s past the last.</summary>
    private Run Fly(string cfg, double cgUp, double h, double right, int n = 1, double spacing = 15)
    {
        AircraftConfig c = Load(cfg);
        AirStart.Plan plan = AirStart.Compute(c, 0, 0, cgUp, 0.0, c.SpawnIasMs > 0 ? c.SpawnIasMs : 30.0);
        var run = new Run { Ac = AirStart.Build(c, plan), Twin = AirStart.Build(c, plan) };
        ControlInputs hold = AirStart.HeldInputs(plan);
        Vec3 wv = WV(run.Ac), fwd = new Vec3(wv.X, wv.Y, 0).Normalized(), side = new(-fwd.Y, fwd.X, 0), p0 = run.Ac.State.Position;
        var trees = new Forest.Tree[n];
        for (int i = 0; i < n; i++) { Vec3 q = p0 + fwd * (25 + i * spacing) + side * right; trees[i] = new Forest.Tree(q.X, q.Y, 0.0, h, 0.22 + h / 24.0, 0, 0); }
        run.Ac.Trees = new FixedTrees(trees);
        run.Ac.Contact += e => { if (e.Kind == ContactEventKind.WoodThrown) run.Wood.Add(e); };
        var a = new SimLoop(run.Ac); var b = new SimLoop(run.Twin);
        double end = (25 + (n - 1) * spacing) / wv.Length + 0.5;
        for (double t = 0; t < end; t += 0.01)
        {
            a.RunFor(0.01, hold); b.RunFor(0.01, hold);
            run.PeakYawRateDeg = Math.Max(run.PeakYawRateDeg, Math.Abs(run.Ac.State.Rates.Z - run.Twin.State.Rates.Z) * 180 / Math.PI);
        }
        run.DV = WV(run.Ac).Length - WV(run.Twin).Length;
        // The speed the energy lost would be at the twin's height: speed traded for height (a nose pitched down by the
        // gear) is not a penalty, and this does not count it.
        double va = WV(run.Ac).Length, vt = WV(run.Twin).Length;
        run.DVE = (0.5 * (va * va - vt * vt) + Atmosphere.GravityMs2 * (run.Twin.State.Position.Z - run.Ac.State.Position.Z)) / vt;
        run.DPsi = Psi(run.Ac) - Psi(run.Twin);
        run.DTheta = Theta(run.Ac) - Theta(run.Twin);
        return run;
    }

    private static double Semi => WingPanels.Semispan(Load("j3-cub-like"));

    // ---- 1. a wing through branches, no trunk: it yaws toward the struck wing ---------------------------------------

    [Fact]
    public void AWingThroughACrownYawsTowardItAndCostsSpeed()
    {
        // A 42 m fir 8 m below its top, its stem 0.5 m beyond the J-3's wingtip: the top skirt there is 1.6–1.8 m in radius,
        // so the outer metre or so of the wing goes through branches and nothing else. Body yaw + = nose right.
        Run right = Fly("j3-cub-like", 34, 42, Semi + 0.5), left = Fly("j3-cub-like", 34, 42, -(Semi + 0.5));
        _o.WriteLine("right: " + right); _o.WriteLine("left:  " + left);
        foreach (Run r in new[] { right, left })
        {
            Assert.Empty(r.Ac.TreeStrikeLog);         // no stem, no limb
            Assert.Empty(r.Ac.LostComponents);
            Assert.True(r.PeakYawRateDeg > 20.0, $"peak yaw rate {r.PeakYawRateDeg:0.0}°/s");
            Assert.True(r.DVE < -0.3, $"energy speed {r.DVE:0.00} m/s");
        }
        // Measured +7.0° / −6.6° at CrownGrip 3 (+0.8° at 1, the model as derived); peak yaw rate 28.5°/s, −0.5 m/s.
        Assert.True(right.DPsi > 5.0, $"right {right.DPsi:0.0}°");
        Assert.True(left.DPsi < -5.0, $"left {left.DPsi:0.0}°");
        Assert.True(Math.Abs(right.DPsi + left.DPsi) < 1.5, "a mirror pass turns the mirror way");

        // A 25 m fir 4 m below its top, the wing 0.3 m into it: on the base e3e754bc 0.00 m/s and 0.0°.
        Run small = Fly("j3-cub-like", 21, 25, Semi + 0.3);
        _o.WriteLine("small: " + small);
        Assert.Empty(small.Ac.TreeStrikeLog);
        Assert.True(small.DPsi > 1.0, $"{small.DPsi:0.0}°");
    }

    // ---- 2. the soft top and the stem below it -----------------------------------------------------------------------

    [Fact]
    public void TheTopFourFeetAreSoftAndTheStemBelowIsSolid()
    {
        // The right wing 1.5 m inboard of its tip into a 25 m fir's stem (0.417 m at the ground, H/D 60). A stem dents a
        // leading edge as a branch does (BranchStrike): F = MOR·π·d²/64 + ρ_w·d²·v² past 1,500 N, which at 32.3 m/s is
        // d = √(1500 / (2.602e6 + 600·32.3²)) = 2.15 cm — 1.29 m below the top (4.2 ft). Above it the top snaps and costs
        // nothing; below it the wing is dented and pulled round; far below, the panel goes.
        Run soft = Fly("j3-cub-like", 25 - 3 + 0.9, 25, Semi - 1.5);              // strikes 2.1 cm, 1.27 m down
        Run dented = Fly("j3-cub-like", 25 - 4 + 0.9, 25, Semi - 1.5);             // 3.8 cm, 2.27 m down
        Run solid = Fly("j3-cub-like", 25 - 10 + 0.9, 25, Semi - 1.5);             // 13.8 cm, 8.27 m down
        Run broke = Fly("j3-cub-like", 25 - 14 + 0.9, 25, Semi - 1.5);             // 20.4 cm, 12.27 m down
        foreach (Run r in new[] { soft, dented, solid, broke }) _o.WriteLine(r.ToString());
        TreeStrikeRecord s0 = Assert.Single(soft.Ac.TreeStrikeLog), s1 = Assert.Single(dented.Ac.TreeStrikeLog), s2 = Assert.Single(solid.Ac.TreeStrikeLog);
        Assert.InRange(25 - s0.HeightM, 1.0, 1.29);
        Assert.Equal(TreeStrikeOutcome.StemSnapped, s0.Outcome);
        Assert.Equal(0.0, soft.Ac.MaxBranchDamage);
        Assert.True(Math.Abs(soft.DPsi) < 1.0, $"{soft.DPsi:0.0}°");
        Assert.InRange(25 - s1.HeightM, 1.29, 3.0);
        Assert.Equal(TreeStrikeOutcome.StemSnapped, s1.Outcome);
        Assert.True(dented.Ac.MaxBranchDamage > 0.1, $"{dented.Ac.MaxBranchDamage:0.00}");
        Assert.True(dented.DPsi > 1.0, $"{dented.DPsi:0.0}°");
        Assert.Equal(TreeStrikeOutcome.StemSnapped, s2.Outcome);
        Assert.Equal(1.0, solid.Ac.MaxBranchDamage, 6);
        Assert.True(solid.DPsi > 15.0, $"{solid.DPsi:0.0}°");
        Assert.Contains(broke.Ac.TreeStrikeLog, r => r.Outcome == TreeStrikeOutcome.PartBroken && r.Component == AirframeComponent.WingTipRight);
        Assert.True(broke.Ac.IsLost(AirframeComponent.WingTipRight));
        Assert.False(broke.Ac.IsLost(AirframeComponent.WingRight));
    }

    // ---- 3. gear and floats ------------------------------------------------------------------------------------------

    [Fact]
    public void GearAndFloatsInTheTopsPitchTheNoseDownAndCostEnergy()
    {
        // Four treetops 15 m apart, the lowest point 2.5 m into them: the right wheel straight at the stems, the right float
        // 0.5 m beside them. Both are 1.2–1.4 m under the CG, so what they meet pitches the nose down; the energy speed
        // counts the speed lost after the dive it starts has paid some back. On the base neither had a strike edge and all
        // of this was zero.
        AircraftConfig c = Load("j3-cub-like"), f = Load("j3-cub-floats-like");
        Run wheels = Fly("j3-cub-like", 25 + c.Gear.Max(g => g.Pos[2]) - 2.5, 25, 0.92, 4);
        Run floats = Fly("j3-cub-floats-like", 25 + f.Floats!.KeelZ - 2.5, 25, 0.5 * f.Floats.SpreadM + 0.5, 4);
        _o.WriteLine("wheels: " + wheels); _o.WriteLine("floats: " + floats);
        Assert.Contains(wheels.Ac.TreeStrikeLog, r => r.Edge == "gear-right");
        Assert.Contains(floats.Ac.TreeStrikeLog, r => r.Edge == "float-R");
        Assert.Contains(floats.Wood, w => w.Part == "float-R");
        foreach (Run r in new[] { wheels, floats })
        {
            Assert.Empty(r.Ac.LostComponents);
            Assert.True(r.DTheta < -1.5, $"pitch {r.DTheta:0.0}°");
            Assert.True(r.DVE < -0.5, $"energy speed {r.DVE:0.00} m/s");
        }
    }

    // ---- 4. a run of treetops ----------------------------------------------------------------------------------------

    [Fact]
    public void EveryTreetopCostsSpeed()
    {
        // Eight 25 m firs in line, 15 m apart, the CG 2 m under their tops (the nose takes 3.3–6 cm of stem from each):
        // the base lost 0.18 m/s in this flight (0.21 at 3 m). Now it loses speed at each, and with it lift: it settles
        // into them. Keeps its parts at 2 m; at 1 m, in the soft tops (1.6–2.8 cm), it is slowed less and nothing is dented.
        Run deep = Fly("j3-cub-like", 23, 25, 0, 8), skim = Fly("j3-cub-like", 24, 25, 0, 8);
        _o.WriteLine("2 m: " + deep); _o.WriteLine("1 m: " + skim);
        Assert.Empty(deep.Ac.LostComponents);
        Assert.True(deep.DVE < -1.5, $"{deep.DVE:0.00} m/s");
        Assert.Empty(skim.Ac.LostComponents);
        Assert.True(skim.DVE < -0.5 && skim.DVE > deep.DVE + 0.8, $"{skim.DVE:0.00} m/s");
    }

    // ---- 5. bits of tree --------------------------------------------------------------------------------------------

    [Fact]
    public void TheStruckWingThrowsWood()
    {
        Run r = Fly("j3-cub-like", 34, 42, Semi + 0.5);
        Assert.NotEmpty(r.Wood);
        Assert.All(r.Wood, w => Assert.StartsWith("wing", w.Part));
        Assert.All(r.Wood, w => Assert.True(w.Intensity >= Aircraft.WoodEventKg));
        // Where the wood leaves: off the right wing, right of the aeroplane's track (world y; it flies north).
        Assert.All(r.Wood, w => Assert.True(w.PositionWorld.Y - r.Twin.State.Position.Y > 2.0, $"{w.PositionWorld}"));
    }
}
