using System.IO;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>MEASUREMENT (prints always; cheap): every config's cellule and its lattice induction factors.</summary>
public sealed class CelluleInductionProbe
{
    private readonly ITestOutputHelper _out;
    public CelluleInductionProbe(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void Print()
    {
        foreach (string f in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData"), "*-like.json"))
        {
            AircraftConfig cfg = AircraftConfigLoader.LoadFromFile(f);
            BiplaneCellule c = BiplaneCellule.For(cfg);
            if (c.IsCellule) _out.WriteLine($"CELL {Path.GetFileNameWithoutExtension(f),-24} {c}");
            var k = BiplaneCellule.LiftingSurfaceOverStrip(cfg);
            _out.WriteLine($"LL/STRIP {Path.GetFileNameWithoutExtension(f),-24} aileron x{k.Aileron:F3}  damping x{k.Damping:F3}  steady pb/2V x{k.SteadyRate:F3}");
        }
    }
}
