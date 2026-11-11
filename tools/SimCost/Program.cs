using System.Diagnostics;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;

namespace FlyingGame.SimCost;

/// <summary>
/// WHERE THE SIM'S TIME GOES, PER AIRCRAFT TYPE, IN MICROSECONDS PER STEP. Ty, 2026-11-09: "the j3 is noticibly slower
/// to run/process so there might be a leak on it?" — and his instruction was to find out where the time goes BEFORE
/// changing anything.
///
/// <see cref="FlyingGame.Core.SimProfile"/> already attributes a step to subsystems for the Quest build, which cannot
/// be sampled; this drives it off-device, one type at a time, and adds the propeller scopes the Quest report never
/// needed because no type had a fitted blade when it was written.
///
/// It also answers LEAK OR COST, which is a different measurement from either: a cost is the same every step from the
/// first, a leak grows. So the per-step time is reported in windows over a two-minute flight, with the managed bytes
/// retained after a full collection at each window, and both are printed rather than averaged away.
///
/// Timing depends on the machine. The numbers in the committed guard (HotPathCostTests) are what it reported here;
/// this tool prints, it does not assert.
/// </summary>
public static class Program
{
    /// <summary>The fleet's profile, in the order the question needs them: the J-3 he flew, the two other fitted
    /// blades (one fixed-pitch floatplane, two constant-speed), an estimated-propeller type as the control, and a
    /// glider and a jet as the floor with no propeller at all.</summary>
    private static readonly (string File, string Why)[] Fleet =
    {
        ("j3-cub-like.json", "fixed pitch, FITTED BLADE (bladePitch75Deg 15.33) — the type Ty reported"),
        ("j3-cub-floats-like.json", "the same blade on floats (hull hydro on top)"),
        ("pitts-s2b-like.json", "constant speed, stops fitted — governed, so NOT solved in normal flight"),
        ("extra-300-like.json", "constant speed, 3 blades, stops fitted"),
        ("great-lakes-like.json", "constant speed, biplane cellule"),
        ("c172-like.json", "no fitted blade: the estimated propeller, unchanged since before 2026-11-09"),
        ("glider-2-33-like.json", "no propeller at all: the floor"),
        ("boeing-737-like.json", "a jet (propDiameterM 0): the floor with an engine"),
    };

    public static void Main(string[] args)
    {
        bool leak = args.Contains("--leak");
        bool parts = args.Contains("--parts") || args.Length == 0;
        bool blade = args.Contains("--blade") || args.Length == 0;
        if (args.Length == 0) leak = true;

        if (args.Contains("--azimuth")) { AzimuthError(); return; }
        if (parts) StepProfile();
        if (blade) BladeCost();
        if (leak) LeakOrCost();
    }

    private static AircraftConfig Load(string file)
        => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", file));

    /// <summary>In the air over the island's default airport, at the type's own spawn speed, trimmed enough to fly
    /// level for the length of the probe.</summary>
    private static SimLoop Airborne(AircraftConfig config, double aglM = 300.0)
    {
        WorldTerrain.Active ??= new WorldTerrain();
        WorldTerrain.Airport ap = WorldTerrain.Airports[WorldTerrain.DefaultAirportIndex];
        double hdg = (IslandRunways.Authored(ap.Name, "InTheAir")?.HeadingTrueDeg ?? 0.0) * Math.PI / 180.0;
        var att = new Quat(0, 0, Math.Sin(hdg / 2), Math.Cos(hdg / 2));
        double v = config.SpawnIasMs > 1 ? config.SpawnIasMs : 40;
        double ground = WorldTerrain.GroundHeightAt(ap.X, ap.Y);
        var state = new RigidBodyState(new Vec3(ap.X, ap.Y, -(ground + aglM)), att,
                                       att.Conjugate().Rotate(new Vec3(v, 0, 0)), Vec3.Zero);
        return new SimLoop(new Aircraft(config, state, ControlDeflections.Neutral));
    }

    private static ControlInputs Cruise(AircraftConfig c)
        => new(0, 0, 0, c.Propulsion is { PropDiameterM: > 0 } or { MaxPowerW: > 0 } ? 0.65 : 0.0);

    // ---------------------------------------------------------------------------------------------------------------
    // 1. THE STEP, BY SCOPE.
    // ---------------------------------------------------------------------------------------------------------------

    private const int Hz = 200;

    /// <summary>
    /// HOW THIS MACHINE IS MEASURED ON, which matters more than usual here: several agents build and fly on it at once
    /// and the load average reached 238 while this was being written, which made one run of the fleet four times
    /// slower than the run before it on identical code. So nothing here is a single timing. Every cost is the MINIMUM
    /// of repeated windows — under contention the minimum is the only estimator that converges on the work rather than
    /// on the queue — and every before/after pair is measured in the SAME process, interleaved, so the two numbers
    /// share whatever the machine was doing. The ratio column exists for the same reason: a type's cost as a multiple
    /// of a propeller-less glider's step in the same run says the same thing on a quiet machine and a loaded one.
    /// </summary>
    private const int Repeats = 3;

    private static void StepProfile()
    {
        Console.WriteLine("=== us per 200 Hz step: the 2026-11-09 model and this one, in the same process ===");
        Console.WriteLine("(two aircraft from the same initial state, flown in alternating ten-second windows, three each, minimum taken)");
        Console.WriteLine($"{"type",-22} {"before",8} {"after",8} {"x",5} | {"forces",8} {"aero",8} {"wakes",6} {"jet",6} {"strips",8} {"prop",8} {"blade",8} {"slip",7} {"shaft",6} {"contact",8} {"B/step",7} {"solves",6} {"meanAz",7} {"iter/step",10}");
        foreach ((string file, string why) in Fleet)
        {
            AircraftConfig config = Load(file);
            SimLoop legacy = Airborne(config), now = Airborne(config);
            ControlInputs cruise = Cruise(config);
            const int steps = Hz * 10;
            double before = double.MaxValue, after = double.MaxValue;
            long bytes = 0; double solves = 0, meanAz = 0, iterPerStep = 0;
            var ticks = new long[(int)SimProfile.Scope.Count];
            for (int rep = 0; rep < Repeats; rep++)
            {
                Legacy(true);
                if (rep == 0) legacy.RunFor(2.0, cruise);                  // JIT, lazy tables, the first atmosphere tiles
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < steps; i++) legacy.Aircraft.Step(cruise, 1.0 / Hz);
                sw.Stop();
                before = Math.Min(before, sw.Elapsed.TotalMilliseconds * 1000.0 / steps);

                Legacy(false);
                if (rep == 0) now.RunFor(2.0, cruise);
                SimProfile.Enabled = true; SimProfile.Reset();
                BladeElement.Iterations = 0; BladeElement.AzimuthsUsed = 0; BladeElement.Solves = 0;
                long bytes0 = GC.GetAllocatedBytesForCurrentThread();
                sw.Restart();
                for (int i = 0; i < steps; i++) now.Aircraft.Step(cruise, 1.0 / Hz);
                sw.Stop();
                SimProfile.Enabled = false;
                double us = sw.Elapsed.TotalMilliseconds * 1000.0 / steps;
                if (us < after)
                {
                    after = us;
                    bytes = (GC.GetAllocatedBytesForCurrentThread() - bytes0) / steps;
                    solves = SimProfile.CallsOf(SimProfile.Scope.Blade) / (double)steps;
                    meanAz = BladeElement.Solves > 0 ? BladeElement.AzimuthsUsed / (double)BladeElement.Solves : 0.0;
                    iterPerStep = BladeElement.Iterations / (double)steps;
                    for (int k = 0; k < ticks.Length; k++) ticks[k] = (long)(SimProfile.Ms((SimProfile.Scope)k) * 1e6 / steps);
                }
            }
            double Us(SimProfile.Scope sc) => ticks[(int)sc] / 1000.0;
            Console.WriteLine($"{file.Replace("-like.json", ""),-22} {before,8:F1} {after,8:F1} {before / after,5:F1} | {Us(SimProfile.Scope.Forces),8:F1} "
                            + $"{Us(SimProfile.Scope.Aero),8:F1} {Us(SimProfile.Scope.Wakes),6:F1} {Us(SimProfile.Scope.Jet),6:F1} "
                            + $"{Us(SimProfile.Scope.Aero) - Us(SimProfile.Scope.Wakes) - Us(SimProfile.Scope.Jet),8:F1} "
                            + $"{Us(SimProfile.Scope.Prop),8:F1} {Us(SimProfile.Scope.Blade),8:F1} {Us(SimProfile.Scope.Slipstream),7:F1} "
                            + $"{Us(SimProfile.Scope.Shaft),6:F1} {Us(SimProfile.Scope.Contact),8:F1} {bytes,7} {solves,6:F1} {meanAz,7:F1} {iterPerStep,10:F0}");
            Console.WriteLine($"{"",-22}   [{why}]");
        }
        Console.WriteLine();
    }

    /// <summary>Puts the three seams back to the model as it stood on 2026-11-09, or forward to this one.</summary>
    private static void Legacy(bool on)
    {
        BladeElement.AzimuthOverride = on ? 16 : 0;
        BladeElement.ExactPrandtlLoss = on;
        BladeElement.LegacyInnerLoop = on;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // 2. THE BLADE-ELEMENT SOLVE ITSELF, before and after, in one process.
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// BEFORE AND AFTER WITHOUT TRUSTING THE CLOCK TWICE. The two seams on <see cref="BladeElement"/> reproduce the
    /// model as it stood on 2026-11-09 — sixteen azimuths whatever the crossflow, and the exact Exp/Acos tip loss — so
    /// the old and the new solve can be timed alternately in the same process, on the same machine, at the same load.
    /// The hoisting (sin and cos of the inflow angle taken off the triangle that is already there, the azimuth's
    /// cosine taken once instead of three times, the tip-loss numerators out of the inner loop) is unconditional and
    /// is therefore in BOTH columns: it is the one part of the speed-up that is exact to the last bit, so it needs no
    /// before and after.
    /// </summary>
    private static void BladeCost()
    {
        Console.WriteLine("=== BladeElement.Solve, us per call: 2026-11-09 (nAz 16, exact loss) vs now (adaptive, tabulated) ===");
        Console.WriteLine($"{"condition",-28} {"skew",7} {"before",9} {"after",9} {"x",6} {"iter/solve",11} {"thrust N",10} {"d thrust",10}");
        var g = new BladeElement.Geometry(2, 0.915, 15.33);     // the J-3's own fitted blade
        foreach ((string what, double vAx, double vCr, double rpm) in new[]
                 {
                     ("static, axial", 0.0, 0.0, 2300.0),
                     ("cruise, axial", 33.0, 0.0, 2300.0),
                     ("cruise, 0.06 m/s crossflow", 33.0, 0.06, 2300.0),
                     ("cruise, 2.6 m/s crossflow", 33.0, 2.6, 2300.0),
                     ("climb, 5 m/s crossflow", 25.0, 5.0, 2300.0),
                     ("spin, 20 m/s crossflow", 25.0, 20.0, 1200.0),
                     ("idle, 10 m/s crossflow", 20.0, 10.0, 600.0),
                 })
        {
            double skew = vCr / (rpm * 2.0 * Math.PI / 60.0 * 0.915);
            double before = double.MaxValue, after = double.MaxValue;
            double tBefore = 0, tAfter = 0, iters = 0;
            const int n = 300;
            for (int rep = 0; rep < 5; rep++)
            {
                BladeElement.AzimuthOverride = 16; BladeElement.ExactPrandtlLoss = true; BladeElement.LegacyInnerLoop = true;
                BladeElement.Solve(g, vAx, vCr, rpm, 1.225);
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < n; i++) tBefore = BladeElement.Solve(g, vAx, vCr, rpm, 1.225).ThrustN;
                sw.Stop();
                before = Math.Min(before, sw.Elapsed.TotalMilliseconds * 1000.0 / n);

                BladeElement.AzimuthOverride = 0; BladeElement.ExactPrandtlLoss = false; BladeElement.LegacyInnerLoop = false;
                BladeElement.Solve(g, vAx, vCr, rpm, 1.225);
                long it0 = BladeElement.Iterations;
                sw.Restart();
                for (int i = 0; i < n; i++) tAfter = BladeElement.Solve(g, vAx, vCr, rpm, 1.225).ThrustN;
                sw.Stop();
                after = Math.Min(after, sw.Elapsed.TotalMilliseconds * 1000.0 / n);
                iters = (BladeElement.Iterations - it0) / (double)n;
            }
            Console.WriteLine($"  {what,-26} {skew,7:F3} {before,9:F1} {after,9:F1} {before / after,6:F1} {iters,11:F0} {tAfter,10:F1} {tAfter - tBefore,10:E2}");
        }
        Console.WriteLine();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // 3. LEAK OR COST.
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A LEAK AND A COST LOOK THE SAME IN AN AVERAGE AND NOTHING ALIKE IN A SEQUENCE. Two minutes of flight in
    /// ten-second windows: a cost is flat from the first window, a leak climbs. The retained managed bytes are taken
    /// after a blocking collection at each window, which is the other half of the same question — time that grows
    /// without memory growing is not a leak, and memory that grows without time growing is not what he felt.
    /// </summary>
    private static void LeakOrCost()
    {
        Console.WriteLine("=== leak or cost: us/step, retained MiB and blade solves/step, in 10 s windows over 120 s of flight ===");
        Console.WriteLine("(flown by DronePilot so the aeroplane is still flying at the end: an aeroplane that has crashed stops its");
        Console.WriteLine(" engine, and a stopped propeller is not solved, so a crash LOOKS like the leak healing itself)");
        foreach ((string file, string _) in Fleet)
        {
            AircraftConfig config = Load(file);
            SimLoop loop = Airborne(config, 900.0);
            var pilot = new DronePilot(7, false, config.SpawnIasMs > 1 ? config.SpawnIasMs : 40);
            const double dt = 1.0 / Hz;
            for (int i = 0; i < Hz * 2; i++) loop.Aircraft.Step(pilot.Update(loop.Aircraft, dt), dt);
            Console.Write($"{file.Replace("-like.json", ""),-22}");
            for (int w = 0; w < 12; w++)
            {
                const int steps = Hz * 10;
                SimProfile.Enabled = true; SimProfile.Reset();
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < steps; i++) loop.Aircraft.Step(pilot.Update(loop.Aircraft, dt), dt);
                sw.Stop();
                SimProfile.Enabled = false;
                Console.Write($" {sw.Elapsed.TotalMilliseconds * 1000.0 / steps,6:F0}/{GC.GetTotalMemory(true) / 1048576.0,4:F0}/{SimProfile.CallsOf(SimProfile.Scope.Blade) / (double)steps,3:F0}");
            }
            Console.WriteLine($"   | alt {-loop.Aircraft.State.Position.Z,6:F0} m v {loop.Aircraft.State.Velocity.Length,5:F1}");
        }
        Console.WriteLine();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // 4. WHAT THE AZIMUTH COUNT AND THE PRANDTL TABLE COST IN ACCURACY.
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// THE ERROR THE SPEED-UP BUYS, MEASURED. nAz = 16 with the exact Exp/Acos loss is the reference — it is what the
    /// model computed before 2026-11-10 — and every candidate is compared against it on the same grid: the fleet's
    /// four fitted blades, through the whole envelope each one flies (static to Vd, no crossflow to a flat spin's, idle
    /// rpm to the red line, sea level to 4,000 m).
    ///
    /// The quantities reported are the ones anything downstream actually reads: thrust and torque (which are the
    /// calibration), the in-plane force and the thrust centre's offset (P-factor, which is the whole reason the
    /// azimuthal integral exists), and the swirl coefficient the traced jet takes.
    /// </summary>
    private static void AzimuthError()
    {
        (string Name, int Blades, double Radius, double Pitch, double Af)[] blades =
        {
            ("J-3 (2 blade, 15.3 deg)", 2, 0.915, 15.33, 100.0),
            ("Pitts fine stop (11 deg)", 2, 0.99, 11.0, 100.0),
            ("Pitts coarse stop (33 deg)", 2, 0.99, 33.0, 100.0),
            ("Extra (3 blade, 12.7-31 deg)", 3, 1.0, 22.0, 100.0),
            ("Great Lakes (2 blade, 26.8)", 2, 0.9398, 26.8, 100.0),
        };
        double[] vAxials = { 0.0, 10.0, 25.0, 40.0, 60.0, 90.0 };
        double[] vCross = { 0.001, 0.05, 0.3, 1.0, 3.0, 6.0, 12.0, 25.0, 40.0 };
        double[] rpms = { 500.0, 900.0, 1500.0, 2100.0, 2700.0 };
        double[] rhos = { 1.225, 0.9093 };    // sea level and 4,000 m

        Console.WriteLine("=== what the azimuth count costs in accuracy: max |relative error| against nAz=16 + exact Prandtl ===");
        Console.WriteLine($"{"blade",-30} {"case",-26} {"thrust",10} {"torque",10} {"inPlane",10} {"moment/R",10} {"swirlC",10} {"skew",8}");
        foreach ((string name, int nb, double radius, double pitch, double af) in blades)
        {
            var g = new BladeElement.Geometry(nb, radius, pitch, af);
            foreach ((string what, int az, bool exactLoss) in new[]
                     {
                         ("nAz 16: loss table + inner loop", 16, false),
                         ("nAz 8, table loss", 8, false),
                         ("nAz 4, table loss", 4, false),
                         ("ADAPTIVE (shipped)", 0, false),
                     })
            {
                double eT = 0, eQ = 0, eN = 0, eOff = 0, eC = 0, atSkew = 0;
                int cases = 0;
                foreach (double va in vAxials)
                foreach (double vc in vCross)
                foreach (double rpm in rpms)
                foreach (double rho in rhos)
                {
                    BladeElement.AzimuthOverride = 16; BladeElement.ExactPrandtlLoss = true; BladeElement.LegacyInnerLoop = true;
                    BladeElement.Result refr = BladeElement.Solve(g, va, vc, rpm, rho);
                    BladeElement.AzimuthOverride = az; BladeElement.ExactPrandtlLoss = exactLoss; BladeElement.LegacyInnerLoop = false;
                    BladeElement.Result cand = BladeElement.Solve(g, va, vc, rpm, rho);
                    cases++;
                    double skew = vc / (rpm * 2.0 * Math.PI / 60.0 * radius);
                    double Rel(double a, double b, double scale) => Math.Abs(scale) < 1e-9 ? 0.0 : Math.Abs(a - b) / Math.Abs(scale);
                    // Thrust and torque against the disc's own magnitude at this condition; the in-plane force and the
                    // offset against the thrust and the radius, because both are small differences of large numbers
                    // and a ratio against themselves says nothing where they are near zero.
                    double dT = Rel(cand.ThrustN, refr.ThrustN, Math.Max(Math.Abs(refr.ThrustN), 1.0));
                    double dQ = Rel(cand.TorqueNm, refr.TorqueNm, Math.Max(Math.Abs(refr.TorqueNm), 1.0));
                    double dN = Rel(cand.NormalN, refr.NormalN, Math.Max(Math.Abs(refr.ThrustN), 1.0));
                    // THE FIRST MOMENT OF THRUST, not the offset it is usually quoted as. The offset is
                    // ThrustMoment/Thrust and the aeroplane never feels it alone: PropModel multiplies it back by the
                    // thrust, so what is applied IS ThrustMoment. Dividing by a thrust that passes through zero inside
                    // this grid (the Extra's 3-blade disc does) makes the quoted error a conditioning artefact and not
                    // a physical one, so the moment is compared directly, in fractions of a propeller radius.
                    double dOff = Math.Abs(cand.ThrustMomentNm - refr.ThrustMomentNm)
                                / (Math.Max(Math.Abs(refr.ThrustN), 1.0) * radius);
                    double dC = Rel(cand.SwirlC, refr.SwirlC, Math.Max(Math.Abs(refr.SwirlC), 1e-3));
                    if (dT > eT) { eT = dT; atSkew = skew; }
                    eQ = Math.Max(eQ, dQ); eN = Math.Max(eN, dN); eOff = Math.Max(eOff, dOff); eC = Math.Max(eC, dC);
                }
                BladeElement.AzimuthOverride = 0; BladeElement.ExactPrandtlLoss = false; BladeElement.LegacyInnerLoop = false;
                Console.WriteLine($"{name,-30} {what,-26} {eT,10:E2} {eQ,10:E2} {eN,10:E2} {eOff,10:E2} {eC,10:E2} {atSkew,8:F3}");
            }
        }
        Console.WriteLine();
        Console.WriteLine("thrust/torque/inPlane: max |delta| / max(|reference|, 1 N). moment/R: max |delta| in the FIRST MOMENT OF");
        Console.WriteLine("THRUST, over (reference thrust x propeller radius) - the thrust centre's displacement as a fraction of the");
        Console.WriteLine("radius. skew: mu = |vCross| / (omega R) at the worst thrust case.");
        Console.WriteLine();

        // WHERE THE THRESHOLDS COME FROM. The error of a fixed azimuth count is not one number: it grows with the
        // skew, because that is what puts energy into the harmonics a coarse count cannot see. So the same grid is cut
        // by skew, and the threshold is read off as the last band that holds the bound — which is how Skew4 and Skew8
        // were set, rather than by picking a round figure and calling it small.
        Console.WriteLine("=== max |error| over only the cases BELOW each skew, so the thresholds can be read off ===");
        double[] bands = { 0.005, 0.01, 0.02, 0.04, 0.06, 0.09, 0.12, 0.18, 0.25, 0.4, 10.0 };
        foreach (int az in new[] { 4, 8 })
        {
            Console.WriteLine($"  nAz {az}:");
            Console.WriteLine($"    {"skew <",8} {"thrust",10} {"torque",10} {"inPlane",10} {"moment/R",10} {"swirlC",10} {"cases",7}");
            foreach (double band in bands)
            {
                double eT = 0, eQ = 0, eN = 0, eOff = 0, eC = 0; int cases = 0;
                foreach ((string _, int nb, double radius, double pitch, double af) in blades)
                {
                    var g = new BladeElement.Geometry(nb, radius, pitch, af);
                    foreach (double va in vAxials)
                    foreach (double vc in vCross)
                    foreach (double rpm in rpms)
                    foreach (double rho in rhos)
                    {
                        double skew = vc / (rpm * 2.0 * Math.PI / 60.0 * radius);
                        if (skew >= band) continue;
                        cases++;
                        BladeElement.AzimuthOverride = 16; BladeElement.ExactPrandtlLoss = true; BladeElement.LegacyInnerLoop = true;
                        BladeElement.Result refr = BladeElement.Solve(g, va, vc, rpm, rho);
                        BladeElement.AzimuthOverride = az; BladeElement.ExactPrandtlLoss = false; BladeElement.LegacyInnerLoop = false;
                        BladeElement.Result cand = BladeElement.Solve(g, va, vc, rpm, rho);
                        double sT = Math.Max(Math.Abs(refr.ThrustN), 1.0), sQ = Math.Max(Math.Abs(refr.TorqueNm), 1.0);
                        eT = Math.Max(eT, Math.Abs(cand.ThrustN - refr.ThrustN) / sT);
                        eQ = Math.Max(eQ, Math.Abs(cand.TorqueNm - refr.TorqueNm) / sQ);
                        eN = Math.Max(eN, Math.Abs(cand.NormalN - refr.NormalN) / sT);
                        eOff = Math.Max(eOff, Math.Abs(cand.ThrustMomentNm - refr.ThrustMomentNm)
                                             / (Math.Max(Math.Abs(refr.ThrustN), 1.0) * radius));
                        eC = Math.Max(eC, Math.Abs(cand.SwirlC - refr.SwirlC) / Math.Max(Math.Abs(refr.SwirlC), 1e-3));
                    }
                }
                BladeElement.AzimuthOverride = 0; BladeElement.ExactPrandtlLoss = false; BladeElement.LegacyInnerLoop = false;
                Console.WriteLine($"    {band,8:F3} {eT,10:E2} {eQ,10:E2} {eN,10:E2} {eOff,10:E2} {eC,10:E2} {cases,7}");
            }
        }
    }
}
