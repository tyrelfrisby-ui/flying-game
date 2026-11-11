using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty 2026-11-03: the iOS build offers only the proven aircraft — "SGS 2-33 glider, J-3 Cub, Great Lakes, Pitts S-2B,
/// Extra 300. Hide all others (172, P-51, etc.) … Default aircraft must be one of the five." The five ids are written
/// here by hand from that message, not read from FleetAllowList, so the list can only change by editing both.
/// </summary>
public sealed class FleetAllowListTests
{
    // SIX since 2026-11-10. Ty asked for five on 11-03 and confirmed "keep the five - it was about quality" earlier
    // the same evening, then flew the slim build and said "The j3 on floats is not in this build it should be". The
    // floats were the one type TestFlightFleet PACKAGED and FleetAllowList did not OFFER, so the build carried the
    // model folder and the selector hid it. The name below stays deliberately un-numbered so the next addition does
    // not have to rename it again.
    private static readonly string[] TheProven = { "glider-2-33-like", "j3-cub-like", "j3-cub-floats-like", "great-lakes-like", "pitts-s2b-like", "extra-300-like" };

    /// <summary>A slice of the master fleet (SessionSettings.Fleet's order and names), hidden types included.</summary>
    private static readonly (string id, string name)[] Master =
    {
        ("paraglider-en-b-like", "Paraglider-like (EN-B)"), ("glider-2-33-like", "2-33-like Glider"), ("c172-like", "172-like"),
        ("pa18-cub-like", "Super Cub-like"), ("j3-cub-like", "J-3 Cub-like, 85 hp"), ("j3-cub-floats-like", "J-3 Cub-like on Floats"),
        ("extra-300-like", "Extra 300-like"), ("pitts-s2b-like", "Pitts S-2-like"), ("great-lakes-like", "Great Lakes-like"),
        ("p51d-like", "P-51D-like"), ("boeing-737-like", "737-like"),
    };

    [Fact]
    public void IosOffersExactlyTheProvenAircraft()
    {
        Assert.Equal(TheProven.OrderBy(x => x), FleetAllowList.Ios.OrderBy(x => x));
        var offered = FleetAllowList.Offered(Master, FleetAllowList.Ios);
        Assert.Equal(new[] { "glider-2-33-like", "j3-cub-like", "j3-cub-floats-like", "extra-300-like", "pitts-s2b-like", "great-lakes-like" }, offered.Select(f => f.id));   // the master's order
        foreach (string hidden in new[] { "c172-like", "p51d-like", "boeing-737-like", "pa18-cub-like", "paraglider-en-b-like" })
        {
            Assert.DoesNotContain(offered, f => f.id == hidden);
            Assert.False(FleetAllowList.Allows(FleetAllowList.Ios, hidden));
        }
    }

    [Fact]
    public void EveryProvenAircraftHasItsConfigAndNothingWasDeleted()
    {
        foreach (string id in TheProven.Concat(new[] { "c172-like", "p51d-like" }))
        {
            AircraftConfig c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
            Assert.Equal(id, c.Id);
        }
    }

    /// <summary>The five are real entries of the game's master fleet (SessionSettings.Fleet), spelled the same: a typo here
    /// would silently hide an aircraft from the phone. (Read as text: the master list lives in the Unity assembly.)</summary>
    [Fact]
    public void TheProvenAircraftAreInTheGamesMasterFleet()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "unity", "Assets", "_Project", "Scripts", "Bridge", "SessionSettings.cs"))) dir = Path.GetDirectoryName(dir)!;
        Assert.NotNull(dir);
        string src = File.ReadAllText(Path.Combine(dir!, "unity", "Assets", "_Project", "Scripts", "Bridge", "SessionSettings.cs"));
        foreach (string id in TheProven) Assert.Contains($"(\"{id}\", ", src);
        Assert.Contains("public static string AircraftId = \"glider-2-33-like\";", src);   // the default is one of the five
    }

    /// <summary>Ty 2026-11-03: "the red and white 2-33 must be removed everywhere … only the yellow 2-33 remains". The game
    /// can only draw a 2-33 from what is in its Resources folder (or the procedural fallback, now painted yellow): the folder
    /// must hold our two yellow GLBs and nothing else — the Grob Astir download (white with red tips) is gone.</summary>
    [Fact]
    public void TheOnly233ModelsShippedAreTheYellowOnes()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "unity", "Assets", "_Project", "Resources", "Models"))) dir = Path.GetDirectoryName(dir)!;
        Assert.NotNull(dir);
        string folder = Path.Combine(dir!, "unity", "Assets", "_Project", "Resources", "Models", "glider-2-33-like");
        // MODELS, not every file in the folder. The folder also holds the licence notice this project writes
        // beside each of its own models (2026-11-10), and a LICENSE.txt is not a 2-33 anybody can fly — listing it
        // here made a licence notice look like a stray airframe. The point of the test is that no OTHER 2-33 mesh
        // creeps in beside the two yellow ones, so it asks about meshes.
        var files = Directory.GetFiles(folder).Select(Path.GetFileName)
                             .Where(f => f!.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
                             .OrderBy(f => f).ToArray();
        Assert.Equal(new[] { "glider-2-33-like-own-tf.glb", "glider-2-33-like-own.glb" }, files);
    }

    [Theory]
    [InlineData("glider-2-33-like", "glider-2-33-like")]   // the game's default stays
    [InlineData("pitts-s2b-like", "pitts-s2b-like")]       // a saved proven choice stays
    [InlineData("c172-like", "glider-2-33-like")]          // a saved hidden type falls back to the default
    [InlineData("p51d-like", "glider-2-33-like")]
    [InlineData(null, "glider-2-33-like")]
    [InlineData("no-such-aircraft", "glider-2-33-like")]
    public void ASavedOrDefaultChoiceAlwaysLandsOnAProvenAircraft(string? saved, string expected)
    {
        var offered = FleetAllowList.Offered(Master, FleetAllowList.Ios);
        string id = FleetAllowList.Resolve(saved, offered, "glider-2-33-like");
        Assert.Equal(expected, id);
        Assert.Contains(id, TheProven);
        // Even a preferred default that is itself hidden lands on one of the five.
        Assert.Contains(FleetAllowList.Resolve(saved == "glider-2-33-like" ? "c172-like" : saved, offered, "c172-like"), TheProven);
    }

    [Fact]
    public void PlatformsWithoutAListOfferEverything()
    {
        Assert.Equal(Master, FleetAllowList.Offered(Master, null));
        Assert.True(FleetAllowList.Allows(null, "p51d-like"));
        Assert.Equal("c172-like", FleetAllowList.Resolve("c172-like", Master, "glider-2-33-like"));
    }
}
