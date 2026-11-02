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
/// MEASUREMENT, not a gate. Prints the Pitts' steady full-aileron roll rate and how long it takes to get
/// there, so a tune can be made against a number rather than a feeling. Run it with
/// <c>--filter FullyQualifiedName~PittsRollProbe</c> and read the output.
/// </summary>
public sealed class PittsRollProbe
{
    private readonly ITestOutputHelper _out;
    public PittsRollProbe(ITestOutputHelper o) { _out = o; }

    // The three aerobatic types at the same 160 mph IAS so they are directly comparable, and the slow types at
    // a speed they actually fly: roll rate scales roughly with airspeed, and 160 mph is past Vne for a 2-33.
    [Theory]
    [InlineData("pitts-s2b-like", 160.0)]
    [InlineData("extra-300-like", 160.0)]
    [InlineData("great-lakes-like", 160.0)]
    [InlineData("c172-like", 110.0)]
    [InlineData("j3-cub-like", 75.0)]
    [InlineData("glider-2-33-like", 60.0)]
    public void SteadyFullAileronRollRate(string id, double iasMph)
    {
        (double steadyDps, double toNinety, double peak) = PittsRoll.Measure(id, iasMph);
        _out.WriteLine($"{id} at {iasMph:F0} mph IAS: steady {steadyDps:F1} deg/s, " +
                       $"peak {peak:F1} deg/s, {toNinety:F2} s to reach 90% of steady");
    }
}

/// <summary>Shared steady-roll measurement: full aileron from level flight, integrate until the roll rate
/// stops changing, and report it. Used by the probe above and by the gate in <see cref="PittsRollRateTests"/>
/// so both measure the same way.</summary>
internal static class PittsRoll
{
    private const double MphToMs = 0.44704;

    /// <summary>Steady roll rate (deg/s, magnitude), seconds to reach 90% of it, and the peak seen.</summary>
    internal static (double SteadyDps, double SecondsTo90, double PeakDps) Measure(string id, double iasMph)
    {
        AircraftConfig cfg = AircraftConfigLoader.LoadFromFile(
            Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
        double v = iasMph * MphToMs;
        // Low enough that indicated is close to true, high enough to be out of ground effect: an aerobatic box
        // entry. Wings level, a shade of alpha to carry the weight, full left-to-right aileron, power on.
        double alpha = 2.0 * Math.PI / 180.0;
        var state = new RigidBodyState(new Vec3(0, 0, -600),
                                       new Quat(0, 0, 0, 1),
                                       new Vec3(v * Math.Cos(alpha), 0, v * Math.Sin(alpha)),
                                       Vec3.Zero);
        var ac = new Aircraft(cfg, state, ControlDeflections.Neutral);
        var loop = new SimLoop(ac, 1.0 / 400.0);

        double dt = 1.0 / 400.0, peak = 0.0, steady = 0.0, t = 0.0, to90 = double.NaN;
        // Three seconds is several full rolls for an aerobatic biplane; the rate plateaus well inside it.
        for (int i = 0; i < (int)(3.0 / dt); i++)
        {
            loop.RunFor(dt, new ControlInputs(1.0, 0, 0, -1.0));
            t += dt;
            double dps = Math.Abs(ac.State.Rates.X) * 180.0 / Math.PI;
            if (dps > peak) peak = dps;
            // The last half second is the plateau; average it so a little oscillation does not pick a spike.
            if (t > 2.5) steady += dps * dt / 0.5;
        }
        // Second pass for the time constant, now that the steady value is known.
        var ac2 = new Aircraft(cfg, state, ControlDeflections.Neutral);
        var loop2 = new SimLoop(ac2, dt);
        for (int i = 0; i < (int)(3.0 / dt) && double.IsNaN(to90); i++)
        {
            loop2.RunFor(dt, new ControlInputs(1.0, 0, 0, -1.0));
            if (Math.Abs(ac2.State.Rates.X) * 180.0 / Math.PI >= 0.9 * steady) to90 = (i + 1) * dt;
        }
        return (steady, to90, peak);
    }
}
