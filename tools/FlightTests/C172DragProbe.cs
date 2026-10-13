using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Diagnostic (DRAG_PROBE=1, DRAG_ID=type): the drag polar from the glide trim at sea level — L/D and drag (lb) at each
/// speed, with the idling prop and with the airframe alone (no prop) — and the power level flight needs (thrust = drag).
/// 2026-10-13, Ty: the C-172's four-forces drag at 120 kt looked low.</summary>
public class C172DragProbe
{
    private readonly ITestOutputHelper _out;
    public C172DragProbe(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void Probe()
    {
        if (System.Environment.GetEnvironmentVariable("DRAG_PROBE") == null) return;
        string repo = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, "../../../../.."));
        string id = System.Environment.GetEnvironmentVariable("DRAG_ID") ?? "c172-like";
        var cfg = AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(repo, "configs", "aircraft", id + ".json"));
        var bare = AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(repo, "configs", "aircraft", id + ".json")); bare.Propulsion = null;
        double W = cfg.Mass.MassKg * 9.81;
        _out.WriteLine($"{id}: W {W * 0.2248:0} lb");
        for (double kt = 55; kt <= 125; kt += 5)
        {
            double v = kt / 1.944;
            var a = TrimSolver.SolveGliderTrim(cfg, v, 0, idleProp: true); var b = TrimSolver.SolveGliderTrim(bare, v, 0);
            double dB = b.Converged ? W / b.GlideRatio : double.NaN;
            double powHp = dB * v / 745.7;
            _out.WriteLine($"  {kt,3} kt: L/D idle-prop {(a.Converged ? a.GlideRatio : double.NaN),5:0.0}  airframe {(b.Converged ? b.GlideRatio : double.NaN),5:0.0}  drag {dB * 0.2248,5:0} lb  α {b.AlphaRad * 57.3,5:0.0}°  thrust power {powHp,5:0} hp");
        }
    }
}
