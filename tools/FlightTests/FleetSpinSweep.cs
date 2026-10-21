using System.Linq;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Diagnostic (FLEET_SPIN=1): every type's upright idle spin WITH and WITHOUT the TR-379 rolling-moment-due-to-
/// sideslip table (docs/SPIN-ROTARY-DATA.md), so the question "should this go on everything?" is answered by
/// measurement rather than by argument. The column is chosen by geometry: a biplane gets Fig. 27, a monoplane
/// Fig. 28. Flown on the widget's own recipe — wings level, power at idle, full aft stick to the stall, then full
/// rudder, held — and reported over the LAST 20 s of a 45 s hold so the incipient phase is excluded.
///
/// FLEET_SPIN_ONLY=&lt;id&gt; measures one type instead of all 31.
///
/// READ THE ENTRY, NOT JUST THE COLUMNS. The hold-then-rudder entry is gated on
/// <see cref="StallAuthorityTests.StallDeg"/>: full aft stick until alpha exceeds it by 2 deg, THEN pro-spin
/// rudder. That number therefore decides the experiment and not only its reporting, and on 2026-10-20 a wrong
/// stall reference for biplanes (a hard-coded 15 deg) was putting the Pitts' rudder in 3 deg of alpha late and
/// making it read as a spiral that needed the table. It does not. See the CORRECTION section of
/// docs/SPIN-ROTARY-DATA.md.
/// </summary>
public class FleetSpinSweep
{
    private readonly ITestOutputHelper _out;
    public FleetSpinSweep(ITestOutputHelper o) { _out = o; }

    private static string Repo { get { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent; return d!.FullName; } }

    private static double AlphaDeg(RigidBodyState s)
    {
        Vec3 air = s.Velocity - s.Attitude.Conjugate().Rotate(Atmosphere.WindAtPosition(s.Position));
        return Math.Atan2(air.Z, air.X) * 57.3;
    }

    private static double OmVert(RigidBodyState s)
    {
        Vec3 zB = s.Attitude.Conjugate().Rotate(new Vec3(0, 0, 1));
        return s.Rates.X * zB.X + s.Rates.Y * zB.Y + s.Rates.Z * zB.Z;
    }

    /// <summary>Wing aspect ratio, and whether the type carries more than one wing (TR-379 has a column for each).</summary>
    public static (double ar, bool biplane, double sweepDeg) WingShape(AircraftConfig c)
    {
        var wings = c.Surfaces.Where(s => s.Id != null && s.Id.StartsWith("wing")).ToList();
        double area = wings.Sum(w => w.Strips.Sum(st => st.Area));
        var ys = wings.SelectMany(w => w.Strips.Select(st => st.Pos[1])).ToList();
        double span = ys.Max() - ys.Min();
        double sweep = wings.SelectMany(w => w.Strips).Max(st => Math.Abs(st.SweepRad)) * 57.3;
        return (area > 0 ? span * span / area : 0, wings.Count > 2, sweep);
    }

    [Fact]
    public void EveryTypeWithAndWithoutTheTable()
    {
        if (System.Environment.GetEnvironmentVariable("FLEET_SPIN") == null) return;
        _out.WriteLine($"{"type",-24} {"AR",5} {"swp",4} {"col",-9}  {"",-3}  {"turns",6} {"s/trn",6} {"ft/trn",7} {"stall",6} {"alpha",6} {"pitch",6}");
        string? only = System.Environment.GetEnvironmentVariable("FLEET_SPIN_ONLY");
        foreach (string file in Directory.GetFiles(Path.Combine(Repo, "configs", "aircraft"), "*.json").Order())
        {
            AircraftConfig probe = AircraftConfigLoader.LoadFromFile(file);
            if (only != null && probe.Id != only) continue;
            var (ar, bip, sweep) = WingShape(probe);
            string col = bip ? "biplane" : "monoplane";
            string line0 = "", line1 = "";
            foreach (bool on in new[] { false, true })
            {
                AircraftConfig c = AircraftConfigLoader.LoadFromFile(file);
                c.SpinAero = on ? new SpinAeroConfig { RollDueToSideslip = col } : null;
                string r;
                try { r = Run(c); }
                catch (Exception e) { r = "   (" + e.GetType().Name + ")"; }
                if (on) line1 = r; else line0 = r;
            }
            _out.WriteLine($"{probe.Id,-24} {ar,5:F1} {sweep,4:F0} {col,-9}  off {line0}");
            _out.WriteLine($"{"",-24} {"",5} {"",4} {"",-9}  ON  {line1}");
        }
    }

    private static string Run(AircraftConfig cfg)
    {
        double stallDeg = StallAuthorityTests.StallDeg(cfg);
        double v = ThermalSpawn.MinSinkSpeed(cfg, 2500);
        TrimSolver.Result t0 = TrimSolver.SolveGliderTrim(cfg, v, 2500, idleProp: true);
        var att = new Quat(0, Math.Sin(t0.ThetaRad / 2), 0, Math.Cos(t0.ThetaRad / 2));
        var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -2500), att,
            new Vec3(v * Math.Cos(t0.AlphaRad), 0, v * Math.Sin(t0.AlphaRad)), Vec3.Zero),
            new FlyingGame.Core.Aero.ControlDeflections(0, t0.ElevatorRad, 0, 0));
        double h = SimLoop.DefaultFixedDtSec;
        double lever = cfg.Propulsion == null ? -1.0 : 1.0;   // glider: spoilers stowed. powered: lever full aft = idle.
        for (double t = 0; t < 25; t += h) { ac.Step(new ControlInputs(0, -1.0, 0, lever), h); if (AlphaDeg(ac.State) > stallDeg + 2) break; }
        var pro = new ControlInputs(0, -1.0, -1.0, lever);
        var a = new System.Collections.Generic.List<double>(); var pit = new System.Collections.Generic.List<double>();
        double net = 0, lost = 0;
        for (double t = 0; t < 45; t += h)
        {
            double z0 = ac.State.Position.Z;
            ac.Step(pro, h);
            RigidBodyState s = ac.State;
            if (double.IsNaN(s.Position.Z)) return "   (NaN)";
            if (t >= 25)
            {
                a.Add(AlphaDeg(s)); pit.Add(FormationPilot.Euler(s.Attitude).Item2 * 57.3);
                net += OmVert(s) * h; lost += s.Position.Z - z0;
            }
            if (-s.Position.Z < 600) ac.State = new RigidBodyState(new Vec3(s.Position.X, s.Position.Y, s.Position.Z - 2500), s.Attitude, s.Velocity, s.Rates);
        }
        double turns = Math.Abs(net) / (2 * Math.PI);
        double stalled = a.Count(x => x > StallAuthorityTests.StallDeg(cfg) + 1) / (double)a.Count;
        return $"{turns,6:F2} {20 / Math.Max(0.01, turns),6:F1} {lost * 3.2808 / Math.Max(0.01, turns),7:F0} {stalled,6:P0} {a.Average(),6:F0} {pit.Average(),6:F0}";
    }
}
