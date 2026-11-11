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

    /// <summary>The fleet's cellules as measured from their strips, with the FITTED <see cref="BiplaneCellule.ClMaxFactor"/>
    /// alongside the DERIVED per-wing factors that are the ones actually applied — the fitted one is a cross-check and goes
    /// nowhere near the flight model. Vso scales as 1/sqrt(k), so a factor's penalty is 1/sqrt(k).
    /// For each type's Vso against its own book, see <see cref="TheDerivedInterferenceMovesEveryBiplaneTowardsItsBook"/>.</summary>
    [Fact]
    public void TheFleetsCellulesAreMeasured()
    {
        var biplanes = new List<string>();
        _out.WriteLine($"{"type",-26} {"gap",-6} {"g/c",-6} {"stagger",-8} {"s/c",-6} {"Su/Sl",-6} {"decal",-7} {"sigma",-6} "
                     + $"{"k up",-6} {"k lo",-6} {"k cell",-7} {"(fitted)",-9} {"Vso +",-7}  <- k is DERIVED and APPLIED; fitted is the cross-check");
        foreach ((string id, AircraftConfig c) in All())
        {
            BiplaneCellule cell = BiplaneCellule.For(c);
            if (!cell.IsCellule) continue;
            biplanes.Add(id);
            double vsoFactor = 1.0 / Math.Sqrt(cell.LiftSlopeInduction);
            _out.WriteLine($"{id,-26} {cell.GapM,5:F2}m {cell.GapOverChord,5:F2} {cell.StaggerM,7:+0.00;-0.00}m "
                         + $"{cell.StaggerOverChord,5:+0.00;-0.00} {cell.AreaRatio,5:F2} {cell.DecalageRad * 57.2958,6:F1}d "
                         + $"{cell.SigmaPrandtl,5:F3} {cell.LiftSlopeInductionUpper,6:F3} {cell.LiftSlopeInductionLower,6:F3} "
                         + $"{cell.LiftSlopeInduction,7:F3} {cell.ClMaxFactor,9:F3} {(vsoFactor - 1) * 100,6:+0.0;-0.0}%");
        }
        _out.WriteLine($"\n{biplanes.Count} cellules: {string.Join(", ", biplanes)}");
        Assert.True(biplanes.Count >= 4, "the fleet's biplanes should all be detected: " + string.Join(", ", biplanes));
        foreach (string want in new[] { "pitts-s2b-like", "stearman-pt17-like", "jn4-jenny-like", "great-lakes-like" })
            Assert.Contains(want, biplanes);
    }

    /// <summary>
    /// THE DERIVED INTERFERENCE, AND WHAT IT IS WORTH ON EVERY BIPLANE IN THE FLEET, against each type's own primary
    /// stall figure. Prints; asserts only the two things that are sourced hard enough to assert.
    ///
    /// The applied factor is <see cref="BiplaneCellule.LiftInterferenceFor"/> — the lattice's lift-slope interference,
    /// derived per wing from each config's own gap, stagger, span and planform. Nothing in it is fitted: the column to
    /// read is whether switching a DERIVED term on moves each type TOWARDS its book, and on 2026-11-10 all three types
    /// whose section tables are true 2-D sections did. The Pitts is the control that must stay OFF — its tables peak at
    /// 1.22/1.05, already deflated cellule values, so the term would be counted twice there.
    /// </summary>
    [Fact]
    public void TheDerivedInterferenceMovesEveryBiplaneTowardsItsBook()
    {
        // Book Vso mph, PRIMARY where a primary figure exists.
        var book = new (string Id, double Mph, string Source)[]
        {
            ("stearman-pt17-like", 54.2, "Pilot's Handbook for Stearman Airplanes N2S-1/-2/-3 (A75NI-9001, 20 Jan 1941): 53 mph at 50 % fuel and oil = ~2529 lb, weight-corrected to this model's 2646 lb"),
            ("great-lakes-like",   57.0, "Great Lakes 2T-1A-2 spec sheet, 50 kt stall"),
            ("pitts-s2b-like",     56.0, "Pitts S-2B, 56 mph stall clean"),
            ("jn4-jenny-like",     45.0, "JN-4D 45 mph MINIMUM LEVEL speed, not a stall: the stall must come out BELOW it"),
        };
        _out.WriteLine($"{"type",-24} {"k up",-6} {"k lo",-6} {"k cell",-7} {"fitted",-7} "
                     + $"{"Vso off",-9} {"Vso on",-9} {"book",-8} {"off-book",-10} {"on-book",-10} applied");
        foreach ((string id, double mph, _) in book)
        {
            AircraftConfig c = AircraftConfigLoader.LoadFromFile(Path.Combine(Dir, id + ".json"));
            BiplaneCellule cell = BiplaneCellule.For(c);
            bool applied = c.Cellule?.LiftInterference == true;
            double VsoOf(bool on)
            {
                c.Cellule = new CelluleConfig { LiftInterference = on };
                return FlyingGame.Sim.Practice.PracticeScenario.EstimateVso(c, 0) / 0.44704;
            }
            double off = VsoOf(false), on = VsoOf(true);
            c.Cellule = applied ? new CelluleConfig { LiftInterference = true } : null;
            _out.WriteLine($"{id,-24} {cell.LiftSlopeInductionUpper,6:F3} {cell.LiftSlopeInductionLower,6:F3} "
                         + $"{cell.LiftSlopeInduction,7:F3} {cell.ClMaxFactor,7:F3} {off,6:F1}mph {on,6:F1}mph "
                         + $"{mph,5:F1}mph {(off / mph - 1) * 100,8:+0.0;-0.0} % {(on / mph - 1) * 100,8:+0.0;-0.0} % "
                         + (applied ? "ON" : "off"));
        }
        foreach ((string id, double mph, string src) in book) _out.WriteLine($"  {id}: book {mph:F1} mph -- {src}");

        // The hard assertions. (1) The derived factor is a LOSS, and a bounded one, on every cellule we own: a biplane
        // never makes what two isolated wings would, and never loses half its lift either.
        foreach ((string id, AircraftConfig c) in All())
        {
            BiplaneCellule cell = BiplaneCellule.For(c);
            if (!cell.IsCellule) continue;
            Assert.InRange(cell.LiftSlopeInduction, 0.60, 1.00);
            Assert.InRange(cell.LiftInterferenceFor(cell.WingIds[0]), 0.60, 1.00);
            Assert.InRange(cell.LiftInterferenceFor(cell.WingIds[1]), 0.50, 1.00);
            // (2) Munk's result (NACA Rep. 151): in a POSITIVELY staggered cellule the forward — upper — wing keeps the
            // more of its isolated lift. Every biplane in this fleet has positive stagger, so it must hold for all of them.
            if (cell.StaggerM > 0.02)
                Assert.True(cell.LiftInterferenceFor(cell.WingIds[0]) > cell.LiftInterferenceFor(cell.WingIds[1]),
                    $"{id}: positive stagger must favour the UPPER wing (Munk, NACA Rep. 151) -- {cell}");
            // (3) It applies to the cellule's wings and to nothing else.
            Assert.Equal(1.0, cell.LiftInterferenceFor("hStab"), 9);
        }
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
