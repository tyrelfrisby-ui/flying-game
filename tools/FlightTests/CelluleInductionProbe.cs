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
            BiplaneCellule c = BiplaneCellule.For(AircraftConfigLoader.LoadFromFile(f));
            if (c.IsCellule) _out.WriteLine($"CELL {Path.GetFileNameWithoutExtension(f),-24} {c}");
        }
    }
}
