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
/// BEGGS-MUELLER, flown (BEGGS=1). Ty 2026-11-09, flying the build: "pitts upright flat spin beggs mueller recovery
/// results power off full opposite rudder let go of stick... stick stayed full aft ailerons full right and no
/// recovery was possible". The emergency procedure is exactly that: power to idle, let go of the stick, full
/// opposite rudder, and the aeroplane is supposed to come out. If ours will not, either the spin is wrong or the
/// free controls are.
/// </summary>
public class BeggsMuellerProbe
{
    private readonly ITestOutputHelper _o;
    public BeggsMuellerProbe(ITestOutputHelper o) { _o = o; }
    private const double D = 180.0 / Math.PI;

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("BEGGS") == null) return;
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        var cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(d.FullName, "configs", "aircraft", "pitts-s2b-like.json"));
        double eMax = cfg.Controls.Elevator.MaxDeflRad, aMax = cfg.Controls.Aileron.MaxDeflRad;

        var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -3000), new Quat(0, 0, 0, 1), new Vec3(50, 0, 0), Vec3.Zero),
                              new ControlDeflections(0, 0, 0, 0, 0));
        var sim = new SimLoop(ac);
        // FLAT SPIN ENTRY, hands on: decelerate, full aft + full left rudder, then power and pro-spin aileron.
        void Row(double t, string phase)
        {
            var s = ac.State;
            double alpha = Math.Atan2(s.Velocity.Z, s.Velocity.X) * D;
            var cd = ac.CurrentDeflections;
            var h = ac.HingeDebug("aileron", cfg.Controls.Aileron);
            _o.WriteLine($" {t,5:F1} | {phase,-20} | a {alpha,6:F1} | r {s.Rates.Z,6:F2} | ele {cd.ElevatorRad / eMax * 100,5:F0}% | ail {cd.AileronRad / aMax * 100,5:F0}% | TAS {s.Velocity.Length,5:F1}"
                + $"  [a {h.A,8:F0} b {h.B,8:F0} k {h.K,6:F0} den {h.Den,8:F0} | L {h.Left,8:F0} R {h.Right,8:F0}]");
        }
                // NOTE THE THROTTLE SIGN: ControlInputs.ThrottleLever is INVERTED, -1 = full power, +1 = idle
        // (Aircraft: _throttle01 = (1 - lever) * 0.5).
        const double IDLE = 1.0, FULL = -1.0;
        // Ty's own entry, 2026-11-09: "I get it into a normal spin with left rudder upright ... then add power and
        // aileron."  Decelerate, full aft + full LEFT rudder at idle to get the normal spin, then power and aileron
        // to drive it flat.
        // Slow it to the STALL before the rudder goes in: stopping at 33 m/s (64 kt) left it flying, and full aft
        // plus full rudder from there rolled it into a SPIRAL (alpha 12, speed RISING to 65 m/s), not a spin.
        for (int i = 0; i < 300 && ac.State.Velocity.Length > 27; i++) sim.RunFor(0.1, new ControlInputs(0, -Math.Min(1, 0.15 + 0.01 * i), 0, IDLE));
        Row(-20, "at the stall (idle)");
        // Ty 2026-11-09: "I spin two turns left then add full power and right aileron for an upright flat spin."
        double turns = 0;
        for (int i = 0; i < 200 && turns < 2.0; i++) { sim.RunFor(0.1, new ControlInputs(0, -1, -1, IDLE)); turns += Math.Abs(ac.State.Rates.Z) * 0.1 / (2 * Math.PI); Wrap(ac); }
        Row(-10, $"after {turns:F1} turns left");
        for (int i = 0; i < 300; i++) { sim.RunFor(0.1, new ControlInputs(1.0, -1.0, -1, FULL)); Wrap(ac); }   // power + RIGHT aileron

_o.WriteLine("    t  | phase                |  alpha | yaw r  | ELEVATOR | AILERON | TAS");
        Row(0, "developed (hands on)");

        // BEGGS-MUELLER: power OFF, hands OFF stick and ailerons, full OPPOSITE rudder (held by the feet).
        double t = 0;
        for (int i = 0; i < 150; i++)
        {
            sim.RunFor(0.1, new ControlInputs(0, 0, 1, IDLE, true, true, false));   // power OFF, stick+ailerons free
            t += 0.1;
            Wrap(ac);
            if (i % 10 == 9) Row(t, "BEGGS: idle+opp rud");
        }
    }

    private static void Wrap(Aircraft ac)
    {
        if (-ac.State.Position.Z < 600) ac.State = new RigidBodyState(new Vec3(ac.State.Position.X, ac.State.Position.Y, ac.State.Position.Z - 1800), ac.State.Attitude, ac.State.Velocity, ac.State.Rates);
    }
}
