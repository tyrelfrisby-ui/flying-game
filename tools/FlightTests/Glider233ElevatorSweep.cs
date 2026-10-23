using System.Linq;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Diagnostic (ELEV_SWEEP=1): HOW MUCH ELEVATOR DOES THE 2-33 NEED TO HOLD A SPIN?
///
/// Owner, 2026-10-22: "give the 2-33 an incrementally bigger elevator until it can stall spin. We know a real
/// 2-33 can stall spin so there is no harm in this." He is right about the aeroplane: the SGS 2-33A is placarded
/// for intentional spins and is flown for spin training. The model cannot hold one — full aft stick balances it
/// at 16.0 deg against a 16.5 deg wing stall, half a degree SHORT — so the modelled tail is too small and the
/// direction of the correction is known even though the number is not.
///
/// This sweep finds the SMALLEST elevator that does it, rather than a number that works. What it changes is the
/// elevator's CHORD (and its area with it, which is what a bigger chord on the same span means); the stabiliser
/// in front of it, the span, the hinge station and everything else are left alone. Each step reports the static
/// balance and then flies the widget's own spin recipe, so the acceptance test is the aeroplane spinning rather
/// than a number being reached.
///
/// WHAT THIS IS AND IS NOT. It is a measured substitute for geometry we do not have: the 2-33A's elevator chord,
/// area and up-travel. It is not a fudge factor in attached flow — the elevator is a control surface, the change
/// is a dimension, and the result is checked against the real aeroplane's known behaviour. When the real numbers
/// turn up, they replace this and the sweep says whether they land in the same place.
/// </summary>
[Collection("WorldTerrainActive")]
public class Glider233ElevatorSweep
{
    private readonly ITestOutputHelper _out;
    public Glider233ElevatorSweep(ITestOutputHelper o) { _out = o; }

    private static double AlphaDeg(RigidBodyState s)
    {
        Vec3 air = s.Velocity - s.Attitude.Conjugate().Rotate(Atmosphere.WindAtPosition(s.Position));
        return Math.Atan2(air.Z, air.X) * 57.3;
    }

    private static double Air(RigidBodyState s)
        => (s.Velocity - s.Attitude.Conjugate().Rotate(Atmosphere.WindAtPosition(s.Position))).Length;

    private static double OmVert(RigidBodyState s)
    {
        Vec3 zB = s.Attitude.Conjugate().Rotate(new Vec3(0, 0, 1));
        return s.Rates.X * zB.X + s.Rates.Y * zB.Y + s.Rates.Z * zB.Z;
    }

    /// <summary>Scale one surface's chord, and its area with it: a bigger chord on the same span.</summary>
    private static void GrowSurface(AircraftConfig c, string surfaceId, double k)
    {
        foreach (SurfaceConfig sf in c.Surfaces)
        {
            if (!string.Equals(sf.Id, surfaceId, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (StripConfig st in sf.Strips)
            {
                st.Chord *= k;
                st.Area *= k;
            }
        }
    }

    [Theory]
    [InlineData("glider-2-33-like")]
    public void HowMuchElevatorToHoldASpin(string id)
    {
        if (Environment.GetEnvironmentVariable("ELEV_SWEEP") == null) return;
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json");
        double[] steps = (Environment.GetEnvironmentVariable("ELEV_FINE") != null)
            ? new[] { 1.30, 1.32, 1.34, 1.36, 1.38, 1.40, 1.42, 1.45 }
            : new[] { 1.0, 1.1, 1.2, 1.3, 1.4, 1.5, 1.7, 1.9, 2.2, 2.5 };
        _out.WriteLine($"== {id}: elevator chord x k, with the TR-379 table on (the gate needs the stall to open)");
        _out.WriteLine("   k      elev chord   balance vs stall     turns  stalled   mean a   s/turn   ft/turn");

        foreach (double k in steps)
        {
            AircraftConfig cfg = AircraftConfigLoader.LoadFromFile(path);
            GrowSurface(cfg, "elevator", k);
            if (Environment.GetEnvironmentVariable("ELEV_HINGE") != null)
            {
                // THE SAME TAILPLANE, HINGE MOVED FORWARD -- which is what "a bigger elevator" means to a
                // mechanic. The total horizontal tail chord and area stay exactly as they are; only the
                // split between the fixed part and the moving part changes. A far smaller claim than
                // "this aeroplane's tail is 16 % bigger than we thought".
                double eC0 = 0.48, sC0 = 0.72, total = eC0 + sC0;      // the config's own chords
                double want = Math.Max(0.05, total - eC0 * k);          // what is left for the stabiliser
                GrowSurface(cfg, "hStab", want / sC0);
            }
            // The table is what makes a stalled wing autorotate; the stall gate keeps it out of attached flow.
            cfg.SpinAero = new SpinAeroConfig { RollDueToSideslip = "monoplane" };
            double stall = StallAuthorityTests.StallDeg(cfg);
            double balance = StallAuthorityTests.FullAftBalanceDeg(cfg);
            double chord = cfg.Surfaces.First(s => s.Id == "elevator").Strips[0].Chord;
            double sChord = cfg.Surfaces.First(s => s.Id == "hStab").Strips[0].Chord;

            double v = ThermalSpawn.MinSinkSpeed(cfg, 1500);
            TrimSolver.Result t0 = TrimSolver.SolveGliderTrim(cfg, v, 1500, idleProp: true);
            var att = new Quat(0, Math.Sin(t0.ThetaRad / 2), 0, Math.Cos(t0.ThetaRad / 2));
            var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -1500), att,
                new Vec3(v * Math.Cos(t0.AlphaRad), 0, v * Math.Sin(t0.AlphaRad)), Vec3.Zero),
                new FlyingGame.Core.Aero.ControlDeflections(0, t0.ElevatorRad, 0, 0));
            double h = SimLoop.DefaultFixedDtSec;

            for (double t = 0; t < 20 && AlphaDeg(ac.State) <= stall + 2; t += h)
                ac.Step(new ControlInputs(0, -1.0, 0, -1.0), h);

            double net = 0, stalled = 0, n = 0, aSum = 0, lost = 0;
            var pro = new ControlInputs(0, -1.0, -1.0, -1.0);
            for (double t = 0; t < 30; t += h)
            {
                double z0 = ac.State.Position.Z;
                ac.Step(pro, h);
                RigidBodyState s = ac.State;
                lost += s.Position.Z - z0;
                net += OmVert(s) * h;
                if (AlphaDeg(s) > 16) stalled++;
                aSum += AlphaDeg(s); n++;
                if (-s.Position.Z < 600)
                    ac.State = new RigidBodyState(new Vec3(s.Position.X, s.Position.Y, s.Position.Z - 1500), s.Attitude, s.Velocity, s.Rates);
            }
            double turns = Math.Abs(net) / (2 * Math.PI);
            _out.WriteLine($"  {k,4:F2}  {chord,6:F3}+{sChord,5:F3}m  {balance,5:F1} vs {stall,4:F1}   {turns,7:F2}  {stalled / n,6:P0}   {aSum / n,6:F0}   " +
                           $"{30 / Math.Max(0.01, turns),6:F1}   {lost * 3.2808 / Math.Max(0.01, turns),7:F0}");
        }
    }
}
