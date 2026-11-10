using System;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// TY'S OWN ACCEPTANCE TEST, flown hands off (FLOAT_ELE=1). 2026-11-09, his Pitts:
///   "in a real pitts if i let go of the stick in an upright spin the stick will stay fully aft and slightly inspin
///    aileron. if i then go to idle and full opposite rudder within a turn or two the turn rate slows and the nose
///    drops correspondingly and then the leading edge flow reattaches on the stab and the stick lurches forward
///    briskly due to the new attached flow over the top breaking the stall with no pilot input completely hands off"
/// The stick is FREE the whole way: the elevator is where the air puts it, never where a pilot put it.
/// </summary>
public class FloatingElevatorProbe
{
    private readonly ITestOutputHelper _o;
    public FloatingElevatorProbe(ITestOutputHelper o) { _o = o; }
    private const double D = 180.0 / Math.PI;

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("FLOAT_ELE") == null) return;
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        var cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(d.FullName, "configs", "aircraft", "pitts-s2b-like.json"));
        double eMax = cfg.Controls.Elevator.MaxDeflRad;

        var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -2500), new Quat(0, 0, 0, 1), new Vec3(45, 0, 0), Vec3.Zero),
                              new ControlDeflections(0, 0, 0, 0, 0));
        var sim = new SimLoop(ac);
        // ENTRY, hands ON: decelerate, then full aft and full left rudder until it is spinning.
        for (int i = 0; i < 100 && ac.State.Velocity.Length > 33; i++) sim.RunFor(0.1, new ControlInputs(0, -Math.Min(1, 0.2 + 0.02 * i), 0, 1.0));
        for (int i = 0; i < 60; i++) sim.RunFor(0.1, new ControlInputs(0, -1, -1, 0.6));

        _o.WriteLine("  t   | phase                  |  alpha | yaw rate | separated  | ELEVATOR (free)");
        void Row(double t, string phase)
        {
            var s = ac.State;
            double alpha = Math.Atan2(s.Velocity.Z, s.Velocity.X) * D;
            // The LE-stall latch is gone (2026-11-10); count the strips the separation blend calls separated instead.
            int st = 0; foreach (double sepv in ac.FlowStateForTests.Separation) if (sepv > 0.5) st++;
            _o.WriteLine($" {t,5:F1} | {phase,-22} | {alpha,6:F1} | {s.Rates.Z,8:F2} | {st,3}        | {ac.CurrentDeflections.ElevatorRad * D,7:F2} deg  ({ac.CurrentDeflections.ElevatorRad / eMax * 100,5:F0}% of aft stop)");
        }

        // HANDS OFF, still spinning: where does the stick sit?
        double tNow = 0.0;
        for (int i = 0; i < 40; i++)
        {
            sim.RunFor(0.1, new ControlInputs(0, 0, -1, 0.6, false, true, false));
            tNow += 0.1;
            if (i % 10 == 9) Row(tNow, "hands off, spinning");
        }
        // RECOVERY, still hands off the stick: idle and full OPPOSITE rudder.
        for (int i = 0; i < 100; i++)
        {
            sim.RunFor(0.1, new ControlInputs(0, 0, 1, 0.0, false, true, false));
            tNow += 0.1;
            if (i % 5 == 4) Row(tNow, "idle + full OPP rudder");
        }
    }
}
