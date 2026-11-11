using System;
using System.IO;
using System.Linq;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>EMPTY_WEIGHTS=1. Ty 2026-11-11: "Show me all of the airplanes empty weights for review."
/// Read through <see cref="AircraftLoad"/> itself, not out of the JSON, because the empty mass is DERIVED
/// (gross minus every station's reference load) unless loading.emptyMassKg is authored.</summary>
public class EmptyWeightProbe
{
    private readonly ITestOutputHelper _out;
    public EmptyWeightProbe(ITestOutputHelper o) { _out = o; }
    private const double Lb = 2.20462262;

    [Fact]
    public void EveryTypesEmptyWeight()
    {
        if (Environment.GetEnvironmentVariable("EMPTY_WEIGHTS") == null) return;
        string dir = Path.Combine(AppContext.BaseDirectory, "TestData");
        if (!Directory.Exists(dir))
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent;
            dir = Path.Combine(d!.FullName, "configs", "aircraft");
        }
        _out.WriteLine($"{"aircraft",-34} {"EMPTY lb",9} {"GROSS lb",9} {"USEFUL lb",10} {"seats",6} {"fuel lb",8}  empty from");
        _out.WriteLine(new string('-', 118));
        foreach (string path in Directory.GetFiles(dir, "*.json").OrderBy(p => p))
        {
            AircraftConfig c = AircraftConfigLoader.LoadFromFile(path);
            var load = new AircraftLoad(c);
            LoadingConfig l = c.Loading ?? new LoadingConfig();
            double gross = l.MaxTakeoffMassKg > 0 ? l.MaxTakeoffMassKg : c.Mass.MassKg;
            double fuel = l.Tanks?.Sum(t => t.MaxKg) ?? 0.0;
            string from = l.EmptyMassKg > 0 ? "authored"
                        : (l.Seats?.Count ?? 0) + (l.Tanks?.Count ?? 0) + (l.Baggage?.Count ?? 0) == 0
                          ? "NO STATIONS — empty = gross, no payload model"
                          : "derived: gross - reference load";
            _out.WriteLine($"{c.DisplayName ?? c.Id,-34} {load.EmptyMassKg * Lb,9:F0} {gross * Lb,9:F0} " +
                           $"{(gross - load.EmptyMassKg) * Lb,10:F0} {l.Seats?.Count ?? 0,6} {fuel * Lb,8:F0}  {from}");
        }
    }

    /// <summary>What the SIM actually flies at (Aircraft.MassProperties = AircraftLoad.Properties) against what the
    /// RIG was solved at (RigSolver reads config.Mass.MassKg). They have to be the same aeroplane.</summary>
    [Fact]
    public void FlownMassAgainstRiggedMass()
    {
        if (Environment.GetEnvironmentVariable("EMPTY_WEIGHTS") == null) return;
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent;
        string dir = Path.Combine(d!.FullName, "configs", "aircraft");
        _out.WriteLine($"{"aircraft",-34} {"RIGGED AT lb",12} {"SIM FLIES lb",12} {"gap lb",8} {"gap %",7}");
        _out.WriteLine(new string('-', 80));
        foreach (string path in Directory.GetFiles(dir, "*.json").OrderBy(p => p))
        {
            AircraftConfig c = AircraftConfigLoader.LoadFromFile(path);
            var load = new AircraftLoad(c);
            double rigged = c.Mass.MassKg, flown = load.Properties.MassKg;
            if (rigged <= 0) continue;
            double gap = flown - rigged;
            if (Math.Abs(gap) < 0.5 && !(c.Id ?? "").StartsWith("j3")) continue;
            _out.WriteLine($"{c.DisplayName ?? c.Id,-34} {rigged * Lb,12:F0} {flown * Lb,12:F0} {gap * Lb,8:F0} {100 * gap / rigged,7:F1}" +
                           $"   empty {load.EmptyMassKg * Lb:F0} + loaded {(load.Properties.MassKg - load.EmptyMassKg) * Lb:F0}");
        }
    }
}
