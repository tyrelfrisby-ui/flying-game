using System;
using System.Collections.Generic;
using System.Globalization;
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
/// THE ELEVATOR-ACCELERATED SPIN (ELE_SPIN=1). Ty 2026-11-10, after a clip of his own Pitts flat spin:
/// "Pushing the stick forward during a spin accelerates the spin due to conservation of angular momentum and
/// I don't think our sim give the elevator enough authority to do that", and "the elevator literally does
/// nothing in the spin".
///
/// So: fly the developed power-on flat spin, then hold the stick FULL FORWARD and FULL AFT in turn, and report
/// what each does to the attitude and to the rate of rotation. The real aeroplane steepens and SPEEDS UP on the
/// push (mass drawn in toward the spin axis — the skater), and flattens and slows on the pull. Sweeps
/// SpinAeroConfig.StabSidewashK (ELE_STABK=...) because that is the knob that gives the tail its dynamic
/// pressure back.
/// </summary>
public class ElevatorSpinProbe
{
    private readonly ITestOutputHelper _o;
    public ElevatorSpinProbe(ITestOutputHelper o) { _o = o; }
    private const double D = 180.0 / Math.PI;

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("ELE_SPIN") == null) return;
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        string id = Environment.GetEnvironmentVariable("ELE_SPIN_ID") ?? "pitts-s2b-like";
        string path = Path.Combine(d.FullName, "configs", "aircraft", id + ".json");

        _o.WriteLine($"{id}  —  developed power-on flat spin, then the stick held at a stop for 4 s");
        _o.WriteLine(" highK stabK |  developed       |  STICK FULL FORWARD        |  STICK FULL AFT");
        _o.WriteLine("             | alpha   r   s/tn | alpha   r    s/tn   q̇ deg/s² | alpha   r    s/tn   q̇");
        foreach (double hk in Nums("ELE_HIGHK", new[] { -1.0 }))
        foreach (double sk in Nums("ELE_STABK", new[] { -1.0 }))
        {
            var cfg = AircraftConfigLoader.LoadFromFile(path);
            cfg.SpinAero ??= new SpinAeroConfig();
            if (sk >= 0) cfg.SpinAero.StabSidewashK = sk;
            if (hk >= 0) cfg.SpinAero.StabHighAlphaK = hk;
            if (Environment.GetEnvironmentVariable("ELE_SPOIL") is { } sp)
                cfg.SpinAero.ControlSpoilerK = double.Parse(sp, CultureInfo.InvariantCulture);

            (double a0, double r0, var snap) = Develop(cfg);
            (double aF, double rF, double qdF) = Hold(cfg, snap, +1.0);   // stick FORWARD
            (double aA, double rA, double qdA) = Hold(cfg, snap, -1.0);   // stick AFT
            _o.WriteLine($"  {cfg.SpinAero.StabHighAlphaK,4:F2} {cfg.SpinAero.StabSidewashK,4:F2} | {a0,5:F1} {r0,6:F2} {Turn(r0),5:F2} | {aF,5:F1} {rF,6:F2} {Turn(rF),5:F2} {qdF,8:F1} | {aA,5:F1} {rA,6:F2} {Turn(rA),5:F2} {qdA,8:F1}");
        }
    }

    private static double Turn(double r) => Math.Abs(r) > 1e-6 ? 2 * Math.PI / Math.Abs(r) : double.NaN;

    /// <summary>Ty's own entry: stall, two turns of normal spin left, then power and out-spin aileron.</summary>
    private static (double alpha, double r, RigidBodyState state) Develop(AircraftConfig cfg)
    {
        var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -3000), new Quat(0, 0, 0, 1), new Vec3(50, 0, 0), Vec3.Zero),
                              new ControlDeflections(0, 0, 0, 0, 0));
        var sim = new SimLoop(ac);
        const double IDLE = 1.0, FULL = -1.0;
        for (int i = 0; i < 300 && ac.State.Velocity.Length > 27; i++) sim.RunFor(0.1, new ControlInputs(0, -Math.Min(1, 0.15 + 0.01 * i), 0, IDLE));
        double turns = 0;
        for (int i = 0; i < 200 && turns < 2.0; i++) { sim.RunFor(0.1, new ControlInputs(0, -1, -1, IDLE)); turns += Math.Abs(ac.State.Rates.Z) * 0.1 / (2 * Math.PI); Wrap(ac); }
        for (int i = 0; i < 300; i++) { sim.RunFor(0.1, new ControlInputs(1.0, -1.0, -1, FULL)); Wrap(ac); }
        return (Alpha(ac), ac.State.Rates.Z, ac.State);
    }

    /// <summary>From the developed spin, hold the stick at one stop for 4 s. Everything else stays where it was:
    /// full power, out-spin aileron, in-spin rudder. q̇ is the pitch acceleration in the first step after the move.</summary>
    private static (double alpha, double r, double qdot) Hold(AircraftConfig cfg, RigidBodyState from, double stick)
    {
        var ac = new Aircraft(cfg, from, new ControlDeflections(0, 0, 0, 0, 0));
        var sim = new SimLoop(ac);
        var ci = new ControlInputs(1.0, stick, -1, -1.0);
        double q0 = ac.State.Rates.Y;
        sim.RunFor(0.05, ci);
        double qdot = (ac.State.Rates.Y - q0) / 0.05 * D;
        for (int i = 0; i < 40; i++) { sim.RunFor(0.1, ci); Wrap(ac); }
        return (Alpha(ac), ac.State.Rates.Z, qdot);
    }

    private static double Alpha(Aircraft ac) => Math.Atan2(ac.State.Velocity.Z, ac.State.Velocity.X) * D;

    private static double[] Nums(string env, double[] fallback)
    {
        string? s = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrWhiteSpace(s)) return fallback;
        var list = new List<double>();
        foreach (var p in s.Split(',', StringSplitOptions.RemoveEmptyEntries)) list.Add(double.Parse(p.Trim(), CultureInfo.InvariantCulture));
        return list.ToArray();
    }

    private static void Wrap(Aircraft ac)
    {
        if (-ac.State.Position.Z < 600) ac.State = new RigidBodyState(new Vec3(ac.State.Position.X, ac.State.Position.Y, ac.State.Position.Z - 1800), ac.State.Attitude, ac.State.Velocity, ac.State.Rates);
    }
}
