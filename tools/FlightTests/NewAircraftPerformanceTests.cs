using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Sim;
using FlyingGame.Sim.Practice;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>PA-28-181 Archer and Cirrus SR22 (owner 2026-10-02) against their POH numbers, next to the C172 the sim is
/// calibrated on (its 75 % cruise reads ~0.9 of book; ROC reads high — see PerformanceSpeedsAreOrderedSensibly).</summary>
public class NewAircraftPerformanceTests
{
    private readonly ITestOutputHelper _out;
    public NewAircraftPerformanceTests(ITestOutputHelper o) { _out = o; }

    // id, book Vs0 kt, book Vy kt, book cruise75 KTAS
    [Theory]
    [InlineData("c172-like", 48, 74, 122)]
    [InlineData("pa28-archer-like", 45, 76, 125)]
    [InlineData("cirrus-sr22-like", 61, 104, 175)]
    public void PerformanceNearTheBook(string id, double vs0, double vy, double cruise)
    {
        AircraftConfig c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
        var sc = new PracticeScenario(PracticeKind.ClimbLevelDescend, PracticeWind.Calm, c, PracticeScenarioTests.TestRunway(), 0);
        sc.Spawn();
        _out.WriteLine($"{id}: Vso {sc.VsoMs * 1.944:F0} kt (book {vs0})  Vx {sc.VxMs * 1.944:F0}  Vy {sc.VyMs * 1.944:F0} (book {vy}) ROC {sc.RocAtVyMs * 196.85:F0} fpm  best glide {sc.BestLdMs * 1.944:F0} kt {sc.BestGlideRatio:F1}:1  cruise75 {sc.CruiseMs * 1.944:F0} kt (book {cruise})");
        Assert.InRange(sc.VsoMs * 1.944, vs0 * 0.85, vs0 * 1.2);
        Assert.InRange(sc.VyMs * 1.944, vy * 0.8, vy * 1.25);
        Assert.InRange(sc.CruiseMs * 1.944, cruise * 0.8, cruise * 1.08);
    }
}
