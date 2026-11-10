using System;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.Aero;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// WHAT A DROOPED ENGINE ACTUALLY DOES (DROOP=1). Ty 2026-11-10 asked for the bent engine's handling penalty and
/// then, asked which way it should come out, said: "I have never felt one just use the physics and call it good".
/// So this measures the physics rather than asking a pilot — the propulsion force and moment at a fixed state,
/// with the mount straight and drooped, split so the two competing terms can be told apart:
///   * the THRUST TILT, which puts T·sin(droop) downward at the hub ahead of the CG: nose DOWN.
///   * the PROP DISC's normal force in the now-angled inflow, which acts up at the nose: nose UP.
/// A hand balance says the first should win by about four to one at 10 degrees; the suite says the aeroplane
/// pitches down LESS when drooped, which is the other way round. One of the two is wrong and this says which.
/// </summary>
public class DroopedEngineProbe
{
    private readonly ITestOutputHelper _o;
    public DroopedEngineProbe(ITestOutputHelper o) { _o = o; }

    // The sim's own two rotations (AircraftOverstress.ToMount/FromMount are internal); kept identical on purpose —
    // if these ever disagree with the sim's, this probe is measuring something the aeroplane does not do.
    private static Vec3 ToMount(Vec3 v, double sag)
    {
        double c = Math.Cos(sag), s = Math.Sin(sag);
        return new Vec3(v.X * c + v.Z * s, v.Y, -v.X * s + v.Z * c);
    }

    private static Vec3 FromMount(Vec3 v, double sag)
    {
        double c = Math.Cos(sag), s = Math.Sin(sag);
        return new Vec3(v.X * c - v.Z * s, v.Y, v.X * s + v.Z * c);
    }

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("DROOP") == null) return;
        AircraftConfig cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "j3-cub-like.json"));

        var vel = new Vec3(30, 0, 1.5);
        Vec3 rates = Vec3.Zero;
        const double rho = 1.0;
        Vec3 cg = cfg.Mass.CgVec();
        (double noseCut, _) = AirframeContact.FuselageStations(cfg);
        _o.WriteLine($"J-3 at {vel.Length:F1} m/s, full power. CG x {cg.X:F3}, nose cut x {noseCut:F3}, "
                   + $"thrust line z {cfg.Propulsion!.ThrustLineZ:F3} (+ is below the CG)");
        _o.WriteLine("  droop |     Fx       Fz   |     My    | what the tilt alone would give");

        foreach (double deg in new[] { 0.0, 2.0, 5.0, 10.0, 20.0 })
        {
            double sag = deg * Math.PI / 180.0;
            // The same two calls MountedPropulsion makes: the prop run in its own tilted frame, turned back.
            (Vec3 f0, Vec3 m0) = Aircraft.PropulsionForces(cfg, 1.0, ToMount(vel, sag),
                                                           ToMount(rates, sag), rho, false, default, null);
            Vec3 f = FromMount(f0, sag);
            Vec3 m = FromMount(m0, sag);
            // …and the hub's real pivot about the firewall, which MountedPropulsion adds for a nose engine.
            Vec3 pivot = new(noseCut, 0.0, cg.Z + cfg.Propulsion.ThrustLineZ);
            Vec3 arm = cg - pivot;
            m += Vec3.Cross(FromMount(arm, sag) - arm, f);

            // What the tilt ALONE would do: the straight thrust turned down by the droop, at the same arm.
            (Vec3 fs, _) = Aircraft.PropulsionForces(cfg, 1.0, vel, rates, rho, false, default, null);
            double tiltOnlyMy = -(-arm.X) * (fs.X * Math.Sin(sag));   // −x_arm · T·sin(droop), the nose-down term
            _o.WriteLine($"   {deg,4:F0}° | {f.X,8:F1} {f.Z,8:F1} | {m.Y,9:F1} | {tiltOnlyMy,9:F1}");
        }
        _o.WriteLine("  (+My is NOSE UP: PropModel puts a thrust line BELOW the CG at +ThrustLineZ and calls it nose-up)");
    }
}
