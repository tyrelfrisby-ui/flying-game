using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using FlyingGame.Sim.Practice;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// FLAT_PLATE_TAIL=1: the WHOLE FABRIC FLEET's horizontal tail, measured the way `PlateClampProbe` measured the
/// Pitts' — because the trade Ty accepted on 2026-11-09 was measured on ONE aeroplane and he is about to fly
/// several. One line per type: the section the stab flies, the derived finite-span table's normal force, the
/// elevator's authority as a fraction of its α 0 value, the tail's pitch damping dM/dq, and the stall speed and
/// trim, which a TAIL SECTION HAS NO BUSINESS MOVING.
///
/// Run it before and after a section change and diff the two: it reads `configs/aircraft/` directly (not the
/// TestData copy), so no rebuild is needed between the two runs.
///
/// Ty 2026-11-09: "the pitts stabilizer is flat plate all the time not 0012", of types "cub great lakes decathlon
/// super cub etc", then "the 2.33 is also flat plate", then "fit the flat plate table against those three tests".
/// Today, shown the measured table, he chose plate stall 12°/Cl_max 1.05 over 9°/0.80: 9°/0.80 has more elevator
/// everywhere but dM/dq +448 N·m per rad/s at α 20 — anti-damping right where he flies aerobatics — and 12°/1.05
/// has none anywhere at the price of a HOLE in elevator authority at α 30, where an ordinary spin sits. He was
/// shown that hole in those words and took it. This probe is what says whether the hole and the damping are the
/// same on the other eleven types as they were on his Pitts.
///
/// WHY `ClampNormalForcePastStall` IS FORCED OFF HERE. It is off in the game (`CN_CLAMP` unset), but
/// `PlateClampProbe` leaves it ON when it finishes, and both probes live in this assembly, so a run that
/// includes both would otherwise measure whichever ran last. Off is what ships: the clamp pins the derived
/// table's Cn to its running maximum, which on the Pitts was CN 1.03 dead flat from 35° to 60°, and a flat Cn is
/// zero control effectiveness — it cost α 45 0.227 → 0.157 and α 60 0.095 → 0.068.
/// </summary>
[Collection("WorldTerrainActive")]
public class FlatPlateTailProbe
{
    private readonly ITestOutputHelper _out;
    public FlatPlateTailProbe(ITestOutputHelper o) { _out = o; }

    /// <summary>The fabric-tailed fleet, plus three controls that must NOT move: a metal-skinned trainer, a
    /// composite-tailed aerobat and a composite glider.</summary>
    private static readonly string[] Types =
    {
        "pitts-s2b-like", "great-lakes-like", "decathlon-8kcab-like", "j3-cub-like", "j3-cub-floats-like",
        "pa18-cub-like", "pa18-bush-like", "pa18-floats-like", "stearman-pt17-like", "jn4-jenny-like",
        "pa25-pawnee-like", "glider-2-33-like",
        // controls
        "c172-like", "extra-300-like", "glider-swift-s1-like",
    };

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("FLAT_PLATE_TAIL") == null) return;
        bool clampWas = AeroModel.ClampNormalForcePastStall;
        AeroModel.ClampNormalForcePastStall = false;        // see WHY ... IS FORCED OFF HERE
        try
        {
            DirectoryInfo? d = new(AppContext.BaseDirectory);
            while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
            string root = d.FullName;
            foreach (string id in Types)
            {
                var cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(root, "configs", "aircraft", id + ".json"));
                int hi = cfg.Surfaces.FindIndex(s => s.Id == "hStab");
                if (hi < 0) { _out.WriteLine($"{id}: no hStab"); continue; }
                var tables = Aircraft.BuildAirfoilTables(cfg);
                string baseAf = cfg.Surfaces[hi].Strips[0].Airfoil;
                string key = AeroModel.SectionTableKey(cfg, hi) ?? baseAf;
                AirfoilTable t = tables[key];

                double Cn(double deg)
                {
                    double a = deg * Math.PI / 180;
                    AeroCoefficients c = t.Sample(a);
                    return c.Cl * Math.Cos(a) + c.Cd * Math.Sin(a);
                }
                // Elevator authority: the pitching moment the stick moves between its stops, at 30 m/s, with the
                // aeroplane's velocity rotated to the body alpha asked for. As a FRACTION of the same thing at
                // α 0, so it is the authority the pilot has lost, not a number in N·m that differs per type.
                double e = cfg.Controls.Elevator.MaxDeflRad;
                double DCm(double deg)
                {
                    double a = deg * Math.PI / 180; Vec3 v = new(30 * Math.Cos(a), 0, 30 * Math.Sin(a));
                    double m(double de) => AeroModel.Compute(cfg, tables, v, Vec3.Zero, Vec3.Zero, 1.0,
                                                             new ControlDeflections(0, de, 0, 0)).Moment.Y;
                    return Math.Abs(m(-e) - m(e));
                }
                // Pitch damping about the trim, controls neutral. POSITIVE is anti-damping: a tail that drives the
                // motion instead of resisting it. This is the whole reason Ty chose this fit over the other one.
                double dMdq(double deg)
                {
                    double a = deg * Math.PI / 180; Vec3 v = new(30 * Math.Cos(a), 0, 30 * Math.Sin(a));
                    double m(double q) => AeroModel.Compute(cfg, tables, v, new Vec3(0, q, 0), Vec3.Zero, 1.0,
                                                            new ControlDeflections(0, 0, 0, 0)).Moment.Y;
                    return (m(0.1) - m(-0.1)) / 0.2;
                }

                double a0 = DCm(0);
                string au = string.Join("  ", new[] { 20, 30, 45, 60 }.Select(x => $"{x}:{(a0 > 0 ? DCm(x) / a0 : 0):F3}"));
                string cn = string.Join("  ", new[] { 20, 30, 45, 60, 90 }.Select(x => $"{x}:{Cn(x):F3}"));
                string dq = string.Join("  ", new[] { 10, 20, 30, 45, 60 }.Select(x => $"{x}:{dMdq(x):+0;-0}"));

                // A horizontal tail's SECTION should not move the aeroplane's stall speed: Vso is the wing's
                // CLmax against the weight. It is measured here because the trim solver balances the tail, so a
                // tail that trims differently CAN shift it, and if it does by more than the scan's own 0.5 m/s
                // step something other than the section has moved.
                double vso = PracticeScenario.EstimateVso(cfg, 0);
                double vref = cfg.SpawnIasMs > 1 ? cfg.SpawnIasMs : 25.0;
                TrimSolver.Result tr = TrimSolver.SolveGliderTrim(cfg, vref, 0);

                _out.WriteLine(
                    $"{id,-24} stab section {baseAf} -> {key}  2-D slope {AeroModel.SectionSlope(cfg.AirfoilTables[baseAf]):F3}/rad\n" +
                    $"{"",26}CN                                {cn}\n" +
                    $"{"",26}elevator authority / at a0        {au}   (a0 = {a0:F1} N.m)\n" +
                    $"{"",26}dM/dq N.m per rad/s (+ = ANTI)    {dq}\n" +
                    $"{"",26}Vso {vso / 0.44704:F1} mph ({vso:F2} m/s)   trim at {vref / 0.44704:F0} mph: " +
                    $"alpha {tr.AlphaRad * 180 / Math.PI:F2} deg, elevator {tr.ElevatorRad * 180 / Math.PI:+0.00;-0.00} deg, " +
                    $"L/D {tr.GlideRatio:F2}{(tr.Converged ? "" : "  NOT CONVERGED")}");
            }
        }
        finally { AeroModel.ClampNormalForcePastStall = clampWas; }
    }
}
