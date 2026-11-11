using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using FlyingGame.Sim.Practice;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// SPOILER_FLEET=1: WHAT <see cref="SpinAeroConfig.ControlSpoilerK"/> IS WORTH ON THE TEN PLATE-TAILED TYPES THAT DID
/// NOT HAVE IT — measured both ways in ONE run, off the same loaded config, so the two columns cannot differ by
/// anything but the coefficient.
///
/// WHY THIS PROBE EXISTS. On 2026-11-10 twelve fabric types were given flat-plate horizontal tails (stall 9°,
/// Cl_max 0.90, blend 44°) and all twelve were given `controlSpoilerOnsetDeg` 10, because a plate stalls at 9-12°
/// and the 30° default was fitted to a NACA 0012 whose stall is at 14°. But the spoiler block in
/// <see cref="AeroModel"/> is gated on `controlSpoilerK &gt; 0.0`, and only the Pitts and the Extra carried a
/// non-zero k — so on the other ten the new onset was INERT: it moved the start of a term that was multiplied by
/// zero. The hole in their elevator authority was closed by the 44° blend alone.
///
/// Ty 2026-11-10, asked whether the ten should have the Pitts' 0.6 now that all twelve fly a plate tail — the
/// restriction to two types having been staging, not a finding: <b>"Do it"</b>.
///
/// THE DAMPING CHECK IS THE POINT OF THE SECOND HALF. The claim this change rests on is that it costs the pitch
/// damping nothing, and the claim is MECHANICAL rather than lucky: the spoiler term is multiplied by
/// sin(gain·deflEff) and gated on |deflEff| &gt; 0, so it exists only while the control is DEFLECTED, while dM/dq is
/// measured controls-neutral. If a type's dM/dq moves at all between the two columns then the gate is not what we
/// think it is, and nothing should be changed until that is understood. So this probe prints the worst (most
/// positive = most anti-damping) dM/dq over α 10-60 for both k, and the exact bit-for-bit verdict.
///
/// THE AUTHORITY FLOOR is the minimum of |M(+full elevator) − M(−full elevator)| over α 10-60 (0.5° steps), as a
/// fraction of the same quantity at α 0 — i.e. the WORST fraction of its level-flight pitch authority the pilot has
/// anywhere in the post-stall band, which is the number the onset change was judged on (`controlSpoilerOnsetDegSource`
/// quotes 0.165 at onset 30 against 0.249 at onset 10 for the Pitts).
///
/// The clamp is forced off for the same reason <see cref="FlatPlateTailProbe"/> forces it off: `PlateClampProbe`
/// leaves <see cref="AeroModel.ClampNormalForcePastStall"/> ON when it finishes and both live in this assembly, so a
/// run containing both would otherwise measure whichever ran last. Off is what ships.
/// </summary>
[Collection("WorldTerrainActive")]
public class SpoilerFleetProbe
{
    private readonly ITestOutputHelper _out;
    public SpoilerFleetProbe(ITestOutputHelper o) { _out = o; }

    /// <summary>The eleven configs that got the plate tail and the onset but no k (ten airframes — the PA-18 cub and
    /// bush share a tail and differ only in gear and spawn), with the Pitts last as the reference that already had
    /// 0.6, and the Extra as the other type that is unaffected because it already had k.</summary>
    private static readonly string[] Types =
    {
        "great-lakes-like", "j3-cub-like", "pa18-cub-like", "pa18-bush-like", "glider-2-33-like",
        "jn4-jenny-like", "stearman-pt17-like", "decathlon-8kcab-like", "pa25-pawnee-like",
        "j3-cub-floats-like", "pa18-floats-like",
        // reference: these two already carried k 0.6, so their two columns must come out identical
        "pitts-s2b-like", "extra-300-like",
    };

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("SPOILER_FLEET") == null) return;
        bool clampWas = AeroModel.ClampNormalForcePastStall;
        AeroModel.ClampNormalForcePastStall = false;
        try
        {
            DirectoryInfo? d = new(AppContext.BaseDirectory);
            while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
            string root = d.FullName;
            double kNew = double.TryParse(Environment.GetEnvironmentVariable("SPOILER_K"), out double kv) ? kv : 0.6;
            int moved = 0;
            foreach (string id in Types)
            {
                var cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(root, "configs", "aircraft", id + ".json"));
                var tables = Aircraft.BuildAirfoilTables(cfg);
                cfg.SpinAero ??= new SpinAeroConfig();
                double kWas = cfg.SpinAero.ControlSpoilerK;
                double e = cfg.Controls.Elevator.MaxDeflRad;

                // The stick's whole travel, as a pitching moment, at a fixed 30 m/s with the velocity rotated to the
                // body alpha asked for. Absolute, because past the stall the sign of dM/dδ can invert.
                double DCm(double deg)
                {
                    double a = deg * Math.PI / 180; Vec3 v = new(30 * Math.Cos(a), 0, 30 * Math.Sin(a));
                    double m(double de) => AeroModel.Compute(cfg, tables, v, Vec3.Zero, Vec3.Zero, 1.0,
                                                             new ControlDeflections(0, de, 0, 0)).Moment.Y;
                    return Math.Abs(m(-e) - m(e));
                }
                // CONTROLS NEUTRAL, which is the whole argument: the spoiler cannot appear in this number.
                double dMdq(double deg)
                {
                    double a = deg * Math.PI / 180; Vec3 v = new(30 * Math.Cos(a), 0, 30 * Math.Sin(a));
                    double m(double q) => AeroModel.Compute(cfg, tables, v, new Vec3(0, q, 0), Vec3.Zero, 1.0,
                                                            new ControlDeflections(0, 0, 0, 0)).Moment.Y;
                    return (m(0.1) - m(-0.1)) / 0.2;
                }

                (double floor, double at, double a20, double a30, double a45, double a60, double worstQ, double atQ,
                 double vso, double trimElev, double trimAlpha) Measure(double k)
                {
                    cfg.SpinAero!.ControlSpoilerK = k;
                    double a0 = DCm(0);
                    double floor = double.MaxValue, at = 0;
                    for (double a = 10; a <= 60.0001; a += 0.5)
                    {
                        double f = a0 > 0 ? DCm(a) / a0 : 0;
                        if (f < floor) { floor = f; at = a; }
                    }
                    double worstQ = double.MinValue, atQ = 0;
                    for (double a = 10; a <= 60.0001; a += 1.0)
                    {
                        double q = dMdq(a);
                        if (q > worstQ) { worstQ = q; atQ = a; }
                    }
                    // Vso and the trim are here because the spoiler fires on LOCAL flow angle past 10°, and a tail
                    // strip at the stall with the stick aft can be past 10° — so unlike the α 60-90 band of
                    // StabHighAlphaK this term is NOT confined to a spin, and whether it moves the stall speed is a
                    // question to measure rather than to assert.
                    double vso = PracticeScenario.EstimateVso(cfg, 0);
                    double vref = cfg.SpawnIasMs > 1 ? cfg.SpawnIasMs : 25.0;
                    TrimSolver.Result tr = TrimSolver.SolveGliderTrim(cfg, vref, 0);
                    return (floor, at, DCm(20) / a0, DCm(30) / a0, DCm(45) / a0, DCm(60) / a0, worstQ, atQ,
                            vso, tr.ElevatorRad * 180 / Math.PI, tr.AlphaRad * 180 / Math.PI);
                }

                var off = Measure(0.0);
                var on = Measure(kNew);
                cfg.SpinAero.ControlSpoilerK = kWas;

                bool dampingMoved = off.worstQ != on.worstQ;
                if (dampingMoved) moved++;
                _out.WriteLine(
                    $"{id,-22} k {kWas:F1} as shipped\n" +
                    $"{"",24}authority floor a10-60   {off.floor:F3} @a{off.at:F0}  ->  {on.floor:F3} @a{on.at:F0}" +
                    $"   ({(off.floor > 0 ? on.floor / off.floor : 0):F2}x)\n" +
                    $"{"",24}authority 20/30/45/60    {off.a20:F3} {off.a30:F3} {off.a45:F3} {off.a60:F3}" +
                    $"  ->  {on.a20:F3} {on.a30:F3} {on.a45:F3} {on.a60:F3}\n" +
                    $"{"",24}worst dM/dq a10-60       {off.worstQ:+0.000000;-0.000000} @a{off.atQ:F0}  ->  " +
                    $"{on.worstQ:+0.000000;-0.000000} @a{on.atQ:F0}   {(dampingMoved ? "*** MOVED ***" : "identical")}\n" +
                    $"{"",24}Vso                      {off.vso:F2} m/s ({off.vso / 0.44704:F1} mph)  ->  " +
                    $"{on.vso:F2} m/s ({on.vso / 0.44704:F1} mph)\n" +
                    $"{"",24}trim at spawn IAS        alpha {off.trimAlpha:F2} elev {off.trimElev:+0.00;-0.00}  ->  " +
                    $"alpha {on.trimAlpha:F2} elev {on.trimElev:+0.00;-0.00}");
            }
            _out.WriteLine($"\ndamping moved on {moved} of {Types.Length} types (0 is the expected answer: the spoiler " +
                           "term is gated on a deflected control and dM/dq is measured controls-neutral)");
        }
        finally { AeroModel.ClampNormalForcePastStall = clampWas; }
    }
}
