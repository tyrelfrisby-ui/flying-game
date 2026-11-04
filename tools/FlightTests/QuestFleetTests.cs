using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.Vr;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>The aeroplanes a headset offers (Ty, Quest 3S 2026-11-02: the 2-33 and the J-3 opened with the eye inside
/// geometry). Expected lists are literals, copied by hand: moving a type in or out of the headset is a deliberate edit
/// here and in QuestFleet (and in tools/own_models/check_eye_interiors.py, which measures each listed eye against the
/// GLB the game draws).</summary>
public sealed class QuestFleetTests
{
    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    [Fact]
    public void TheHeadsetListsOurOwnCockpitsOnly()
    {
        Assert.Equal(new[] { "glider-2-33-like", "extra-300-like", "pitts-s2b-like", "great-lakes-like", "j3-cub-like", "j3-cub-floats-like" },
                     QuestFleet.Listed);
        Assert.False(QuestFleet.IsListed("c172-like"));
        Assert.False(QuestFleet.IsListed("pa18-cub-like"));
        Assert.False(QuestFleet.IsListed(null));
        Assert.Equal("glider-2-33-like", QuestFleet.Default);
    }

    [Fact]
    public void FilterKeepsTheFleetsOrderAndNames()
    {
        var fleet = new[] { ("paraglider-en-b-like", "Para"), ("glider-2-33-like", "2-33"), ("j3-cub-like", "J-3"), ("j3-cub-floats-like", "J-3 floats"),
                            ("c172-like", "172"), ("extra-300-like", "Extra"), ("pitts-s2b-like", "Pitts"), ("great-lakes-like", "Great Lakes"), ("p51d-like", "P-51") };
        Assert.Equal(new[] { ("glider-2-33-like", "2-33"), ("j3-cub-like", "J-3"), ("j3-cub-floats-like", "J-3 floats"), ("extra-300-like", "Extra"),
                             ("pitts-s2b-like", "Pitts"), ("great-lakes-like", "Great Lakes") },
                     QuestFleet.Filter(fleet));
    }

    [Fact]
    public void ASavedTypeTheHeadsetDoesNotOfferFallsBackToTheDefault()
    {
        Assert.Equal("pitts-s2b-like", QuestFleet.Resolve("pitts-s2b-like"));
        Assert.Equal("j3-cub-floats-like", QuestFleet.Resolve("j3-cub-floats-like"));
        Assert.Equal("glider-2-33-like", QuestFleet.Resolve("c172-like"));
        Assert.Equal("glider-2-33-like", QuestFleet.Resolve(""));
        Assert.Equal("glider-2-33-like", QuestFleet.Resolve(null));
    }

    /// <summary>Every listed type has an authored eye on every seat (the cockpit view needs no fallback guess), and
    /// every type hidden for a reason really has eye points (otherwise the reason is wrong).</summary>
    [Fact]
    public void ListedTypesHaveAnEyeOnEverySeat()
    {
        foreach (string id in QuestFleet.Listed)
        {
            var seats = Load(id).Loading!.Seats;
            Assert.NotEmpty(seats);
            foreach (var s in seats) Assert.True(s.HasEye, $"{id} {s.Id}");
        }
        foreach (string id in QuestFleet.HiddenBecause.Keys)
            Assert.Contains(Load(id).Loading!.Seats, s => s.HasEye);
    }

    /// <summary>The J-3, wheels and floats, is flown from the REAR seat only in a headset: its front eye has a cabin
    /// ceiling tube 9 cm away (check_eye_interiors.py). A saved "front" reads as the primary seat; every other listed
    /// type keeps all its seats.</summary>
    [Fact]
    public void TheJ3IsSeatedInTheRearOnly()
    {
        foreach (string id in new[] { "j3-cub-like", "j3-cub-floats-like" })
        {
            var seats = Load(id).Loading!.Seats;
            Assert.Equal(new[] { "rear" }, QuestFleet.Seats(id, seats, s => s.Id).Select(s => s.Id));
            Assert.False(QuestFleet.SeatCleared(id, "front"));
            Assert.True(QuestFleet.SeatCleared(id, "rear"));
            Assert.True(QuestFleet.SeatCleared(id, null));
            Assert.Equal("rear", seats[0].Id);   // the primary seat a refused choice falls back to
        }
        foreach (string id in new[] { "glider-2-33-like", "extra-300-like", "pitts-s2b-like", "great-lakes-like" })
        {
            var seats = Load(id).Loading!.Seats;
            Assert.Equal(seats.Count, QuestFleet.Seats(id, seats, s => s.Id).Count);
            foreach (var s in seats) Assert.True(QuestFleet.SeatCleared(id, s.Id), $"{id} {s.Id}");
        }
    }

    /// <summary>The 2-33 is listed because it always draws our own yellow model now; if it could still resolve to the
    /// procedural trainer ("type"), its eye would be back inside the closed tube.</summary>
    [Fact]
    public void TheListed233DrawsOurOwnModel()
    {
        foreach (string? saved in new[] { null, "", "type", "factory", "bogus" })
            Assert.Equal("-own-tf", LiveryChoice.OwnSuffix("glider-2-33-like", LiveryChoice.Resolve("glider-2-33-like", null, saved)));
    }
}
