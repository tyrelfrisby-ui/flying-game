using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.DataContracts;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE SWIRL'S SIZE is still a lump sum, and the fin now rests on it. Aircraft.SwirlFactor = 0.2 says the slipstream's
/// tangential speed is a fifth of its axial increase — one constant for every propeller on the fleet, chosen before any
/// of them was solved. With the blade element in hand the swirl is not a constant to pick: the torque the blades make
/// IS the angular momentum they put into the air, so the tube's rotation follows from it.
///
/// Angular momentum through a tube of radius Rs rotating as a solid body at Omega, carrying axial speed Vt:
///     Q = rho * Vt * 2*pi * Omega * integral(r^3 dr, 0..Rs) = rho * Vt * pi * Omega * Rs^4 / 2
/// so the tangential speed AT THE TUBE EDGE, which is what PropJet's swirl parameter means, is
///     Omega * Rs = 2*Q / (rho * pi * Rs^3 * Vt).
/// No tuning constant. This probe prints both, for every solved type, at its rig condition and standing still.
/// </summary>
public class SwirlSourceProbe
{
    private readonly ITestOutputHelper _o;
    public SwirlSourceProbe(ITestOutputHelper o) { _o = o; }
    private const double Rho0 = 1.225;

    [Fact]
    public void Probe()
    {
        if (System.Environment.GetEnvironmentVariable("SWIRL_PROBE") == null) return;
        var d = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (!System.IO.Directory.Exists(System.IO.Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        string dir = System.IO.Path.Combine(d.FullName, "configs", "aircraft");

        _o.WriteLine("                      |            |  axial du |  tube  | swirl edge | as frac of du");
        foreach (string id in new[] { "j3-cub-like", "j3-cub-floats-like", "great-lakes-like", "pitts-s2b-like", "extra-300-like", "c172-like" })
        {
            var c = AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(dir, id + ".json"));
            var p = c.Propulsion!;
            bool solved = PropModel.IsSolved(p, PropModel.SteadyDrive(p, 0.75, 30.0, Rho0));
            foreach ((string what, double tasMs, double thr) in new[] { ("rig 75%", RigTas(id), 0.75), ("static full", 0.0, 1.0) })
            {
                double rho = Rho0;
                var vel = new Vec3(tasMs, 0, 0);
                PropModel.PropDrive drive = PropModel.SteadyDrive(p, thr, tasMs, rho);
                (double du, double rr, double lump) = Aircraft.TailSlipstream(c, thr, vel, Vec3.Zero, rho, drive, false);
                PropModel.Compute(p, thr, vel, Vec3.Zero, rho, drive, out PropModel.Parts parts);
                double q = System.Math.Abs(parts.TorqueRoll);
                double vt = tasMs + du;
                double fromTorque = rr > 0 && vt > 0.1 ? 2.0 * q / (rho * System.Math.PI * rr * rr * rr * vt) : 0.0;
                _o.WriteLine($" {id,-19} {(solved ? "BEM" : "coef"),-5}| {what,-11}| {du,9:F2} | {rr / (0.5 * p.PropDiameterM),6:F3} R | {System.Math.Abs(lump),9:F2} m/s | {System.Math.Abs(lump) / System.Math.Max(0.01, du),7:F2}");
            }
        }
    }

    private static double RigTas(string id) => id switch
    {
        "j3-cub-like" => 60.0 * 0.514444,
        "j3-cub-floats-like" => 63.5 * 0.514444,
        "great-lakes-like" => 108.5 * 0.514444,
        "pitts-s2b-like" => 139.1 * 0.514444,
        "extra-300-like" => 159.5 * 0.514444,
        _ => 123.4 * 0.514444,
    };

}
