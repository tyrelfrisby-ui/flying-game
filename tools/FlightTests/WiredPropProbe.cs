using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>The blade-element propeller, in the aeroplane: does the tachometer behave?</summary>
public class WiredPropProbe
{
    private readonly ITestOutputHelper _o;
    public WiredPropProbe(ITestOutputHelper o) { _o = o; }
    private const double MphPerMs = 2.23694;

    private static AircraftConfig Load(string id)
    {
        var d = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (!System.IO.Directory.Exists(System.IO.Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        return AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(d.FullName, "configs", "aircraft", id + ".json"));
    }

    [Fact]
    public void Probe()
    {
        foreach (string id in new[] { "j3-cub-like", "j3-cub-floats-like", "pitts-s2b-like", "extra-300-like", "great-lakes-like" })
        {
            var c = Load(id);
            var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -500), Quat.Identity, new Vec3(0.5, 0, 0), Vec3.Zero));
            var full = new ControlInputs(0, 0, 0, -1.0);   // lever -1 = full power
            _o.WriteLine($"=== {id}: {(c.Propulsion!.ConstantSpeed ? $"governed, stops {c.Propulsion.FineStopDeg:F1}-{c.Propulsion.CoarseStopDeg:F1} deg" : c.Propulsion.BladePitch75Deg > 0 ? $"fixed {c.Propulsion.BladePitch75Deg:F2} deg" : "NO fitted blade")}, redline {c.Propulsion.MaxRpm:F0}");
            // Hold it still and let the shaft spin up: a run-up.
            for (int i = 0; i < 360; i++) ac.Step(full, 1.0 / 120.0);   // 2 s: the shaft spins up long before the aeroplane moves
            _o.WriteLine($"    full throttle from rest, after 2 s: {ac.EngineRpm:F0} rpm at {ac.State.Velocity.Length * MphPerMs:F0} mph");
            // Now fly it at a few speeds and let it settle each time.
            foreach (double mph in new[] { 40.0, 60.0, 80.0, 100.0 })
            {
                var a2 = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -500), Quat.Identity, new Vec3(mph / MphPerMs, 0, 0), Vec3.Zero));
                for (int i = 0; i < 360; i++) a2.Step(full, 1.0 / 120.0);
                _o.WriteLine($"    launched at {mph,5:F0} mph -> {a2.State.Velocity.Length * MphPerMs,5:F0} mph, {a2.EngineRpm,6:F0} rpm");
            }
        }
    }
}
