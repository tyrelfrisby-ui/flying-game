using System;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Engine;
using FlyingGame.Core.DataContracts;
using FlyingGame.Sim.Practice;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// WHAT THE OIL-LOSS PITCH RULE IS WORTH (OILPITCH=1). Ty 2026-11-10: "aerobatic aircraft with a constant speed
/// propeller default to high pitch with loss of oil pressure like twins do (Seminole beech 18 etc) and standard
/// single engines default to low pitch with no oil pressure".
///
/// The rule only means something if the two stops cost different amounts of drag, so this measures it rather than
/// asserting it: for every constant-speed type with fitted stops, the windmilling rpm and the drag the blade element
/// gives at the FINE stop and at the COARSE one, against the authored coefficient the sim used before.
/// </summary>
public class OilLossPitchProbe
{
    private readonly ITestOutputHelper _o;
    public OilLossPitchProbe(ITestOutputHelper o) { _o = o; }

    [Fact]
    public void MeasureTheStops()
    {
        if (Environment.GetEnvironmentVariable("OILPITCH") != "1") return;
        double rho = Atmosphere.SeaLevelDensityKgM3;
        foreach (string name in new[] { "pitts-s2b-like", "extra-300-like", "great-lakes-like", "j3-cub-like" })
        {
            AircraftConfig c = AircraftConfigLoader.LoadFromFile(Path.Combine(
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..")), "configs", "aircraft", name + ".json"));
            PropulsionConfig? p = c.Propulsion;
            if (p is null) continue;
            _o.WriteLine($"--- {name}  cs={p.ConstantSpeed} fine={p.FineStopDeg:0.##} coarse={p.CoarseStopDeg:0.##} " +
                         $"coarseOnOilLoss={p.CoarseOnOilLoss} oilLossPitch={PropModel.OilLossPitchDeg(p):0.##}  " +
                         $"dryBoundaryExcess={PistonEngine.BoundaryExcessFor(p):0.00}  " +
                         $"D={p.PropDiameterM:0.##} blades={p.PropBladeCount} AF={p.ActivityFactor:0.#} idleCd={p.IdleDragCd:0.###}");
            double disc = Math.PI * 0.25 * p.PropDiameterM * p.PropDiameterM;
            // 1.3 Vso IS IN THIS LIST, and it is the one to read: the authored coefficient's band describes a
            // fine-pitch propeller driven by the air at APPROACH speed, and since the dry boundary friction was solved
            // to put the fine blade down at the stall speed (2026-11-10, second pass) a fixed 30 m/s is below the stall
            // on some of these types and well above it on others.
            double vso = PracticeScenario.EstimateVso(c, 0.0, 0.0);
            foreach (double v in new[] { 30.0, 1.3 * vso, 45.0, 60.0, 80.0 })
            {
                double authored = p.IdleDragCd * PropModel.StoppedDragFraction * 0.5 * rho * v * v * disc;
                string line = $"   V={v,5:0.0} m/s   authored(stopped) {authored,7:0.0} N";
                foreach ((string tag, double pitch) in new[] { ("fine", p.FineStopDeg), ("coarse", p.CoarseStopDeg) })
                {
                    if (pitch <= 0.0) { line += $"   {tag}: n/a"; continue; }
                    var g = new BladeElement.Geometry(p.PropBladeCount, 0.5 * p.PropDiameterM, pitch, p.ActivityFactor);
                    double EngineQ(double r) => PistonEngine.MotoringTorqueNm(p, r, 0.0, 1.0, 1.0);
                    var free = BladeElement.Windmill(g, v, rho, p.MaxRpm);
                    var cranked = BladeElement.Windmill(g, v, rho, p.MaxRpm, EngineQ);
                    line += $"   {tag} {pitch,5:0.0}: free {(free.Turning ? $"{free.Rpm,6:0} rpm {free.DragN,7:0.0} N" : "   STOPPED      ")}" +
                            $" | cranked {(cranked.Turning ? $"{cranked.Rpm,6:0} rpm {cranked.DragN,7:0.0} N  Cd {cranked.DragN / (0.5 * rho * v * v * disc),5:0.000}" : "STOPPED")}";
                }
                _o.WriteLine(line);
            }
        }
    }
}
