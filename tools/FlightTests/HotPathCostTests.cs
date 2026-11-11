using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// WHAT THE PROPELLER COSTS A STEP, GUARDED. Ty, 2026-11-09, after flying: "the j3 is noticibly slower to run/process
/// so there might be a leak on it?" It was not a leak — the retained bytes and the solve count per step were flat over
/// two minutes of flight (tools/SimCost --leak) — it was cost, and the whole of it was in one place:
///
///   - Blade-element momentum theory is 24 annuli, and 16 azimuths whenever there is ANY crossflow, with an inner
///     momentum balance at each. One solve of the J-3's blade took 1,220 us.
///   - The sim reaches it SIX times a step on a type with a fitted blade: four RK4 stages through
///     Aircraft.PropulsionForces, once more for Aircraft.TailSlipstream, and once for the shaft integration
///     (AircraftEngine.StepShaft).
///   - So the J-3 cost 8,372 us a step at 200 Hz — 1.67 SECONDS of CPU per second of flight — against 120 to 205 us
///     for every other type in the fleet. It is the only type with a fitted FIXED-PITCH blade, which is why his
///     report singled it out: the three constant-speed types have blades fitted too, but while their governor has oil
///     they fly the throttle law and never reach the solve (PropModel.IsSolved, AircraftEngine.SolvesShaft).
///
/// THIS GUARDS THE WORK, NOT THE CLOCK. The machine these numbers were taken on runs several agents at once; its load
/// average reached 238 and one run of the fleet came out four times slower than the run before it on identical code.
/// A microsecond bound would fail on a busy afternoon and pass on a regression. The inner-iteration count is
/// deterministic, it is exactly what the azimuth count multiplies, and it is what moved: so it is what is asserted,
/// three ways — the cost of one solve at fixed conditions, the number of solves a step, and the whole second of
/// flight measured against the 2026-11-09 model in the same process. The microseconds are printed beside them.
/// </summary>
[Collection("WorldTerrainActive")]
public sealed class HotPathCostTests : IDisposable
{
    public void Dispose()
    {
        WorldTerrain.Active = null;
        BladeElement.AzimuthOverride = 0; BladeElement.ExactPrandtlLoss = false; BladeElement.LegacyInnerLoop = false;
    }

    private readonly ITestOutputHelper _o;
    public HotPathCostTests(ITestOutputHelper o) { _o = o; }

    /// <summary>
    /// ONE SOLVE, AT CONDITIONS THAT DO NOT DEPEND ON A TRAJECTORY. The bounds are the measured counts plus about a
    /// fifth (2026-11-10, this worktree): they pin BOTH halves of the speed-up, because the azimuth count multiplies
    /// the iterations and the warm start divides them. The 2026-11-09 model's figures are in the last column of the
    /// output for scale — it ran 2,522 iterations in the cruise case where this runs 1,358, and 9,724 where the skew
    /// earns sixteen azimuths.
    /// </summary>
    [Theory]
    // vAxial, vCross, rpm, skew = vCross/(omega R), bound
    [InlineData(33.0, 0.0, 2300.0, 500L)]        // axial: the shaft integration's own solve, 1 azimuth
    [InlineData(33.0, 0.06, 2300.0, 1700L)]      // a tenth of a degree of alpha: skew 0.0003, four azimuths
    [InlineData(33.0, 2.6, 2300.0, 1700L)]       // the J-3's own cruise, 4.5 deg of alpha: skew 0.012, four azimuths
    [InlineData(25.0, 5.0, 2300.0, 3400L)]       // climbing: skew 0.023, eight azimuths
    [InlineData(25.0, 20.0, 1200.0, 5600L)]      // a flat spin: skew 0.174, sixteen azimuths — unchanged in count
    public void OneBladeSolveStaysInsideItsIterationBudget(double vAxial, double vCross, double rpm, long bound)
    {
        var g = new BladeElement.Geometry(2, 0.915, 15.33);          // the J-3's own fitted blade
        BladeElement.Solve(g, vAxial, vCross, rpm, 1.225);           // warm the table and the JIT
        BladeElement.Iterations = 0;
        BladeElement.Result now = BladeElement.Solve(g, vAxial, vCross, rpm, 1.225);
        long iterations = BladeElement.Iterations;

        BladeElement.AzimuthOverride = 16; BladeElement.ExactPrandtlLoss = true; BladeElement.LegacyInnerLoop = true;
        BladeElement.Solve(g, vAxial, vCross, rpm, 1.225);
        BladeElement.Iterations = 0;
        BladeElement.Result was = BladeElement.Solve(g, vAxial, vCross, rpm, 1.225);
        long legacy = BladeElement.Iterations;
        BladeElement.AzimuthOverride = 0; BladeElement.ExactPrandtlLoss = false; BladeElement.LegacyInnerLoop = false;

        double skew = vCross / (rpm * 2.0 * Math.PI / 60.0 * 0.915);
        _o.WriteLine($"{vAxial:F0} m/s axial, {vCross:F2} crossflow, {rpm:F0} rpm (skew {skew:F4}): {iterations} iterations "
                   + $"(bound {bound}), was {legacy} on 2026-11-09; thrust {now.ThrustN:F2} N against {was.ThrustN:F2} N");
        Assert.True(iterations <= bound,
            $"one blade-element solve at skew {skew:F4} now runs {iterations} inner iterations against a bound of {bound}. "
            + "That count is what Ty felt as the J-3 being slower to process (2026-11-09); see BladeElement's azimuth "
            + "count and warm start, and tools/SimCost, before raising the bound.");
    }

    /// <summary>
    /// HOW MANY TIMES A STEP THE SIM SOLVES A DISC. Six, on a type with a fitted blade, and not one more: the four RK4
    /// stages, the tail slipstream and the shaft. This is the half of the 2026-11-09 cost that was not the azimuth
    /// count, and it is the half that can come back silently — a new caller in the force path costs a sixth of the
    /// propeller again and nothing else would notice. Zero on every type whose propeller is estimated: a type with no
    /// fitted blade has no business reaching the solve in level flight.
    /// </summary>
    [Theory]
    [InlineData("j3-cub-like.json", 6)]
    [InlineData("j3-cub-floats-like.json", 6)]
    [InlineData("pitts-s2b-like.json", 0)]
    [InlineData("extra-300-like.json", 0)]
    [InlineData("great-lakes-like.json", 0)]
    [InlineData("c172-like.json", 0)]
    [InlineData("glider-2-33-like.json", 0)]
    public void TheStepSolvesTheDiscSixTimesAndNoMore(string file, int solvesPerStep)
    {
        WorldTerrain.Active = new WorldTerrain();
        try
        {
            SimLoop loop = Airborne(file, out AircraftConfig config);
            ControlInputs cruise = Cruise(config);
            // FIVE SECONDS OF SETTLE, NOT ONE, AND THAT IS NOT PADDING. Hands off the stick the aeroplane spends the
            // first second or two after a spawn in a pitch transient, where the crossflow is large and the skew earns
            // eight or sixteen azimuths; measured at one second the J-3 ran 18,553 inner iterations a step and at five
            // it runs 5,068, both correctly. What is guarded here is steady flight, which is what Ty was flying.
            loop.RunFor(5.0, cruise);                                // settle, and warm every lazy table

            const int steps = 200;                                   // one second at the default 200 Hz
            SimProfile.Enabled = true;
            SimProfile.Reset();
            BladeElement.Iterations = 0; BladeElement.AzimuthsUsed = 0; BladeElement.Solves = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < steps; i++) loop.Aircraft.Step(cruise, SimLoop.DefaultFixedDtSec);
            sw.Stop();
            SimProfile.Enabled = false;
            double solves = SimProfile.CallsOf(SimProfile.Scope.Blade) / (double)steps;

            double meanAz = BladeElement.Solves > 0 ? BladeElement.AzimuthsUsed / (double)BladeElement.Solves : 0.0;
            _o.WriteLine($"{file}: {solves:F2} blade solves per step, {meanAz:F1} azimuths each, {BladeElement.Iterations:N0} "
                       + $"inner iterations per sim-second, {sw.Elapsed.TotalMilliseconds * 1000.0 / steps:F0} us per step on "
                       + $"this machine ({sw.Elapsed.TotalMilliseconds:F0} ms of CPU per sim-second)");
            Assert.False(double.IsNaN(loop.Aircraft.State.Position.Z));
            Assert.Equal(solvesPerStep, (int)Math.Round(solves));
        }
        finally { WorldTerrain.Active = null; }
    }

    /// <summary>
    /// AND THE WHOLE SECOND OF FLIGHT, AGAINST THE MODEL IT REPLACED, IN ONE PROCESS. Two J-3s from the same initial
    /// state, one flown with the seams set back to 2026-11-09 and one as it ships, so neither the machine's load nor
    /// the trajectory can flatter either. Measured 2026-11-10 in steady flight: 9.71 million inner iterations a
    /// sim-second then against 0.89 million now, 10.9x. The bound is 2.5x, which leaves room for a flight phase whose
    /// skew earns eight or sixteen azimuths — the spawn transient is one, and measured there it still came out at 2.6x
    /// — while reverting the azimuth count would put it near 2 and reverting the warm start near 1.
    /// </summary>
    [Fact]
    public void TheJ3IsSeveralTimesCheaperThanTheModelItReplaced()
    {
        WorldTerrain.Active = new WorldTerrain();
        try
        {
            long Fly(bool legacy)
            {
                BladeElement.AzimuthOverride = legacy ? 16 : 0;
                BladeElement.ExactPrandtlLoss = legacy;
                BladeElement.LegacyInnerLoop = legacy;
                SimLoop loop = Airborne("j3-cub-like.json", out AircraftConfig config);
                ControlInputs cruise = Cruise(config);
                loop.RunFor(5.0, cruise);                            // out of the spawn transient (see above)
                BladeElement.Iterations = 0;
                for (int i = 0; i < 200; i++) loop.Aircraft.Step(cruise, SimLoop.DefaultFixedDtSec);
                Assert.False(double.IsNaN(loop.Aircraft.State.Position.Z));
                return BladeElement.Iterations;
            }

            long was = Fly(true), now = Fly(false);
            BladeElement.AzimuthOverride = 0; BladeElement.ExactPrandtlLoss = false; BladeElement.LegacyInnerLoop = false;
            _o.WriteLine($"J-3, one sim-second of the same flight: {was:N0} inner iterations on the 2026-11-09 model, "
                       + $"{now:N0} now — {was / (double)now:F1}x");
            Assert.True(was >= 2.5 * now, $"the propeller now runs {now:N0} iterations a sim-second against {was:N0} "
                                        + "then, which is less than the 2.5x this is meant to hold.");
        }
        finally { WorldTerrain.Active = null; }
    }

    /// <summary>
    /// AND THE AZIMUTH COUNT IS NOT FREE OF THE PHYSICS. The adaptive count is only allowed because four azimuths
    /// integrate a first harmonic exactly, which is what P-factor and the in-plane force ARE; above the measured skew
    /// thresholds it goes back to sixteen. So this pins the error against the model as it stood on 2026-11-09 — the
    /// seams restore it — over the fitted blades and the whole envelope they fly. The bound on all five is a tenth of
    /// a per cent, which is where the measured maxima sit; tools/SimCost --azimuth prints the whole sweep, banded by
    /// skew, which is where the two thresholds were read off.
    /// </summary>
    [Fact]
    public void TheAdaptiveAzimuthCountKeepsTheDiscsNumbers()
    {
        (string Name, int Blades, double Radius, double Pitch)[] blades =
        {
            ("J-3", 2, 0.915, 15.33), ("Pitts fine", 2, 0.99, 11.0), ("Pitts coarse", 2, 0.99, 33.0),
            ("Extra", 3, 1.0, 22.0), ("Great Lakes", 2, 0.9398, 26.8),
        };
        double worstT = 0, worstQ = 0, worstN = 0, worstOffset = 0, worstC = 0;
        string worstWhere = "";
        try
        {
            foreach ((string name, int nb, double radius, double pitch) in blades)
            {
                var g = new BladeElement.Geometry(nb, radius, pitch);
                foreach (double va in new[] { 0.0, 10.0, 25.0, 40.0, 60.0, 90.0 })
                foreach (double vc in new[] { 0.001, 0.05, 0.3, 1.0, 3.0, 6.0, 12.0, 25.0, 40.0 })
                foreach (double rpm in new[] { 500.0, 900.0, 1500.0, 2100.0, 2700.0 })
                foreach (double rho in new[] { 1.225, 0.9093 })        // sea level and 4,000 m
                {
                    BladeElement.AzimuthOverride = 16; BladeElement.ExactPrandtlLoss = true; BladeElement.LegacyInnerLoop = true;
                    BladeElement.Result was = BladeElement.Solve(g, va, vc, rpm, rho);
                    BladeElement.AzimuthOverride = 0; BladeElement.ExactPrandtlLoss = false; BladeElement.LegacyInnerLoop = false;
                    BladeElement.Result now = BladeElement.Solve(g, va, vc, rpm, rho);

                    double sT = Math.Max(Math.Abs(was.ThrustN), 1.0), sQ = Math.Max(Math.Abs(was.TorqueNm), 1.0);
                    double dT = Math.Abs(now.ThrustN - was.ThrustN) / sT;
                    double dQ = Math.Abs(now.TorqueNm - was.TorqueNm) / sQ;
                    double dN = Math.Abs(now.NormalN - was.NormalN) / sT;
                    // THE FIRST MOMENT OF THRUST, in fractions of a propeller radius — not the thrust-centre offset
                    // it is usually quoted as. The offset is ThrustMoment/Thrust and nothing feels it alone: PropModel
                    // multiplies it straight back by the thrust, so the moment the aeroplane gets IS ThrustMoment.
                    // Quoted as an offset, a disc passing through zero thrust inside this grid (the Extra's does)
                    // reports a conditioning artefact instead of an error.
                    double dOff = Math.Abs(now.ThrustMomentNm - was.ThrustMomentNm) / (sT * radius);
                    double dC = Math.Abs(now.SwirlC - was.SwirlC) / Math.Max(Math.Abs(was.SwirlC), 1e-3);
                    if (dT > worstT) { worstT = dT; worstWhere = $"{name} at {va:F0} m/s axial, {vc:F2} crossflow, {rpm:F0} rpm, rho {rho:F3}"; }
                    worstQ = Math.Max(worstQ, dQ); worstN = Math.Max(worstN, dN);
                    worstOffset = Math.Max(worstOffset, dOff); worstC = Math.Max(worstC, dC);
                }
            }
        }
        finally
        {
            BladeElement.AzimuthOverride = 0; BladeElement.ExactPrandtlLoss = false; BladeElement.LegacyInnerLoop = false;
        }

        _o.WriteLine($"against the 2026-11-09 model, over 5 blades x 540 conditions: thrust {worstT:E2}, torque {worstQ:E2}, "
                   + $"in-plane {worstN:E2}, thrust moment {worstOffset:E2} of (thrust x radius), swirl C {worstC:E2}");
        _o.WriteLine($"worst thrust case: {worstWhere}");
        Assert.True(worstT < 1e-3, $"thrust moved by {worstT:E2} of itself");
        Assert.True(worstQ < 1e-3, $"torque moved by {worstQ:E2} of itself");
        Assert.True(worstN < 1e-3, $"the in-plane force moved by {worstN:E2} of the thrust");
        Assert.True(worstOffset < 1e-3, $"the first moment of thrust moved by {worstOffset:E2} of (thrust x radius)");
        Assert.True(worstC < 1e-3, $"the swirl coefficient moved by {worstC:E2} of itself");
    }

    /// <summary>In the air over the island's default airport, at the type's own spawn speed — the same setup
    /// tools/SimCost profiles, so the two sets of numbers are of the same flight.</summary>
    private static SimLoop Airborne(string file, out AircraftConfig config)
    {
        config = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", file));
        WorldTerrain.Airport ap = WorldTerrain.Airports[WorldTerrain.DefaultAirportIndex];
        double hdg = (IslandRunways.Authored(ap.Name, "InTheAir")?.HeadingTrueDeg ?? 0.0) * Math.PI / 180.0;
        var att = new Quat(0, 0, Math.Sin(hdg / 2), Math.Cos(hdg / 2));
        double v = config.SpawnIasMs > 1 ? config.SpawnIasMs : 40;
        double ground = WorldTerrain.GroundHeightAt(ap.X, ap.Y);
        var state = new RigidBodyState(new Vec3(ap.X, ap.Y, -(ground + 300.0)), att,
                                       att.Conjugate().Rotate(new Vec3(v, 0, 0)), Vec3.Zero);
        return new SimLoop(new Aircraft(config, state, ControlDeflections.Neutral));
    }

    private static ControlInputs Cruise(AircraftConfig c) => new(0, 0, 0, c.Propulsion is not null ? 0.65 : 0.0);
}
