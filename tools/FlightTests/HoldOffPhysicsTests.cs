using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>The hold-off with a PERFECT pilot (owner 2026-10-05: "they float too far before touchdown"): the aircraft is
/// pinned at a fixed height, its pitch servoed so lift = weight and its elevator so the pitching moment balances (trim drag
/// counted), engine idle. What is left is pure physics: how fast it bleeds and the slowest speed it can still be held.</summary>
[Collection("WorldTerrainActive")]
public class HoldOffPhysicsTests
{
    private readonly ITestOutputHelper _o;
    public HoldOffPhysicsTests(ITestOutputHelper o) { _o = o; }

    internal static (double vHoldMin, List<(double kt, double decelKtS, double aoaDeg)> curve, double floatM, double floatS) Hold(AircraftConfig c, double flaps, double wheelsM, double v0, ITestOutputHelper? o = null)
    {
        WorldTerrain.Active = null; Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null;
        var mains = c.Gear.FindAll(g => !g.IsTailwheel);
        double theta = 0.08, ele = 0, v = v0, x = 0, t = 0, vMin = v0;
        var curve = new List<(double, double, double)>();
        Aircraft? ac = null;
        double lowestGear(double th) { var q = new Quat(0, Math.Sin(th / 2), 0, Math.Cos(th / 2)); double m = -9; foreach (var g in c.Gear) m = Math.Max(m, q.Rotate(g.PosVec() - c.Mass.CgVec()).Z); return m; }
        int bad = 0; double nextLog = v0 * 1.943844 - 1.0;
        while (t < 120 && v > 5)
        {
            var att = new Quat(0, Math.Sin(theta / 2), 0, Math.Cos(theta / 2));
            double z = -(wheelsM + lowestGear(theta));
            var st = new RigidBodyState(new Vec3(x, 0, z), att, att.Conjugate().Rotate(new Vec3(v, 0, 0)), Vec3.Zero);
            if (ac == null) { ac = new Aircraft(c, st, ControlDeflections.Neutral); ac.FlapFraction = flaps; } else ac.State = st;
            ac.Step(new ControlInputs(0, ele, 0, 1.0), 0.01);
            Vec3 vw = ac.State.Attitude.Rotate(ac.State.Velocity);
            double az = vw.Z / 0.01, ax = (vw.X - v) / 0.01, q = ac.State.Rates.Y;
            double aoa = Math.Atan2(st.Velocity.Z, st.Velocity.X) * 57.2958;
            // Servo: sinking → more pitch; pitching → elevator against it.
            theta = Math.Clamp(theta + 0.0015 * az, -0.2, 0.6);
            ele = Math.Clamp(ele + 0.6 * q, -1, 1);
            if (Math.Abs(az) < 0.4 && Math.Abs(q) < 0.05) { vMin = Math.Min(vMin, v); bad = 0; } else if (t > 2 && az > 0.4) bad++;
            if (v * 1.943844 <= nextLog && t > 1.5) { curve.Add((v * 1.943844, -ax * 1.943844, aoa)); nextLog = v * 1.943844 - 2.0; }
            if (bad > 60 || aoa > 35) break;   // can no longer hold it: it settles
            v += ax * 0.01; x += v * 0.01; t += 0.01;
        }
        return (vMin * 1.943844, curve, x, t);
    }

    [Theory]
    [InlineData("c172-like.json", 0.0, 68, 48)]   // POH 172S: Vs1 48 KIAS
    [InlineData("c172-like.json", 1.0, 62, 40)]   // POH 172S: Vso 40 KIAS (flaps 30)
    [InlineData("pa28-archer-like.json", 1.0, 66, 45)]   // Archer III POH: Vso 45 KIAS, short-field approach 66 KIAS
    [InlineData("pa18-cub-like.json", 0.0, 57, 43)]
    [InlineData("pa18-cub-like.json", 1.0, 52, 38)]
    public void PerfectHoldOff(string file, double flaps, double fromKt, double bookStallKt)
    {
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", file));
        var ge = Hold(c, flaps, 0.45, fromKt / 1.943844);
        var free = Hold(c, flaps, 30.0, fromKt / 1.943844);
        _o.WriteLine($"{file} flaps {flaps}: book stall {bookStallKt} kt | at 1.5 ft: held down to {ge.vHoldMin:F0} kt, float {ge.floatM / 0.3048:F0} ft in {ge.floatS:F1} s | at 100 ft: held down to {free.vHoldMin:F0} kt");
        _o.WriteLine("  1.5 ft decel (kt → kt/s, AoA): " + string.Join("  ", ge.curve.Select(p => $"{p.kt:F0}:{p.decelKtS:F2}/{p.aoaDeg:F0}°")));
        _o.WriteLine("  100 ft decel (kt → kt/s, AoA): " + string.Join("  ", free.curve.Select(p => $"{p.kt:F0}:{p.decelKtS:F2}/{p.aoaDeg:F0}°")));
    }
}
