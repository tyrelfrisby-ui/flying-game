using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Sim;
using FlyingGame.Sim.Practice;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Diagnostic (CRUISE_PROBE=1): the 75 % power cruise speed at sea level (the widget's cruise estimate).</summary>
public class C172CruiseProbe
{
    private readonly ITestOutputHelper _out;
    public C172CruiseProbe(ITestOutputHelper o) { _out = o; }
    [Fact]
    public void Probe()
    {
        if (System.Environment.GetEnvironmentVariable("CRUISE_PROBE") == null) return;
        string repo = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, "../../../../.."));
        var cfg = AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(repo, "configs", "aircraft", "c172-like.json"));
        double vso = PracticeScenario.EstimateVso(cfg, 0, 0.0);
        _out.WriteLine($"c172 75% cruise at sea level: {PracticeScenario.EstimateCruise75(cfg, 0, vso) * 1.944:0.0} kt; at 2,500 ft {PracticeScenario.EstimateCruise75(cfg, 762, vso) * 1.944:0.0} kt");
    }
}
