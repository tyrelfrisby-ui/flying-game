using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The wing cellule of every multi-wing type in the fleet, measured from its own strips (owner 2026-10-28:
/// "build it as a tool/table that applies to all biplanes current and future"). Prints the table — run it with
/// <c>-l "console;verbosity=detailed"</c> to read it — and guards the two things that would quietly break it:
/// that monoplanes are never mistaken for cellules, and that the interference table keeps its shape.
/// </summary>
public class BiplaneCelluleTests
{
    private readonly ITestOutputHelper _out;
    public BiplaneCelluleTests(ITestOutputHelper o) { _out = o; }

    private static string Dir => Path.Combine(AppContext.BaseDirectory, "TestData");
    private static IEnumerable<(string Id, AircraftConfig C)> All()
    {
        foreach (string f in Directory.GetFiles(Dir, "*.json").OrderBy(f => f))
        {
            AircraftConfig c;
            try { c = AircraftConfigLoader.LoadFromFile(f); } catch (Exception) { continue; }
            if (c?.Surfaces is null || c.Surfaces.Count == 0) continue;
            yield return (Path.GetFileNameWithoutExtension(f), c);
        }
    }

    /// <summary>The fleet's cellules, and what the measured factor would do to each one's stall speed if its type
    /// switched <c>cellule.liftInterference</c> on. Vso scales as 1/sqrt(CLmax), so the penalty is 1/sqrt(k).</summary>
    [Fact]
    public void TheFleetsCellulesAreMeasured()
    {
        var biplanes = new List<string>();
        _out.WriteLine($"{"type",-26} {"gap",-6} {"g/c",-6} {"stagger",-8} {"s/c",-6} {"Su/Sl",-6} {"decal",-7} {"sigma",-6} {"CLmax x",-8} {"Vso would",-9}");
        foreach ((string id, AircraftConfig c) in All())
        {
            BiplaneCellule cell = BiplaneCellule.For(c);
            if (!cell.IsCellule) continue;
            biplanes.Add(id);
            double vsoFactor = 1.0 / Math.Sqrt(cell.ClMaxFactor);
            _out.WriteLine($"{id,-26} {cell.GapM,5:F2}m {cell.GapOverChord,5:F2} {cell.StaggerM,7:+0.00;-0.00}m "
                         + $"{cell.StaggerOverChord,5:+0.00;-0.00} {cell.AreaRatio,5:F2} {cell.DecalageRad * 57.2958,6:F1}d "
                         + $"{cell.SigmaPrandtl,5:F3} {cell.ClMaxFactor,7:F3} {(vsoFactor - 1) * 100,8:+0.0;-0.0}%");
        }
        _out.WriteLine($"\n{biplanes.Count} cellules: {string.Join(", ", biplanes)}");
        Assert.True(biplanes.Count >= 4, "the fleet's biplanes should all be detected: " + string.Join(", ", biplanes));
        foreach (string want in new[] { "pitts-s2b-like", "stearman-pt17-like", "jn4-jenny-like", "great-lakes-like" })
            Assert.Contains(want, biplanes);
    }

    /// <summary>A monoplane has no cellule, whatever its surfaces are called, and reports a factor of exactly 1 so
    /// callers need no special case.</summary>
    [Theory]
    [InlineData("c172-like")]
    [InlineData("extra-300-like")]
    [InlineData("pa28-archer-like")]
    [InlineData("boeing-737-like")]
    [InlineData("glider-2-33-like")]
    public void AMonoplaneHasNoCellule(string id)
    {
        BiplaneCellule cell = BiplaneCellule.For(AircraftConfigLoader.LoadFromFile(Path.Combine(Dir, id + ".json")));
        Assert.False(cell.IsCellule, $"{id} was read as a cellule: {cell}");
        Assert.Equal(1.0, cell.ClMaxFactor, 6);
    }

    /// <summary>The interference table keeps its shape: it rises with the gap, never exceeds a monoplane, and is
    /// worth something real at the gaps our aeroplanes actually have.</summary>
    [Fact]
    public void TheInterferenceTableKeepsItsShape()
    {
        var t = BiplaneCellule.ClMaxByGapOverChord;
        for (int i = 1; i < t.Length; i++)
        {
            Assert.True(t[i].GapOverChord > t[i - 1].GapOverChord, "gap/chord must increase down the table");
            Assert.True(t[i].Factor >= t[i - 1].Factor, "the factor must not fall as the wings move apart");
            Assert.InRange(t[i].Factor, 0.5, 1.0);
        }
        Assert.Equal(1.0, t[^1].Factor, 6);   // an infinite gap is two monoplanes
    }

    /// <summary>Switching it on must actually change the lift, and in the right direction. Guards the AeroModel
    /// wiring, which is easy to leave unreachable.</summary>
    [Fact]
    public void TurningItOnReducesTheLift()
    {
        AircraftConfig c = AircraftConfigLoader.LoadFromFile(Path.Combine(Dir, "great-lakes-like.json"));
        BiplaneCellule cell = BiplaneCellule.For(c);
        Assert.True(cell.IsCellule);
        Assert.InRange(cell.ClMaxFactor, 0.80, 0.95);

        double VsoOf(bool on)
        {
            c.Cellule = new CelluleConfig { LiftInterference = on };
            return FlyingGame.Sim.Practice.PracticeScenario.EstimateVso(c, 0);
        }
        double off = VsoOf(false), onV = VsoOf(true);
        _out.WriteLine($"great-lakes-like Vso: interference off {off / 0.44704:F1} mph, on {onV / 0.44704:F1} mph "
                     + $"({(onV / off - 1) * 100:+0.0;-0.0} %), CLmax x{cell.ClMaxFactor:F3}");
        Assert.True(onV > off, "a cellule penalty must RAISE the stall speed");
        c.Cellule = null;
    }
}
