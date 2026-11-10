using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The seat is chosen by ID, and an ID means the same seat in every aeroplane (owner Ty, 2026-11-02, on the Quest:
/// "i chose the back seat and it put me in the front seat"). Expected eyes are literals copied from
/// docs/models/eyepoints.json (v5/v6), not read from it.
/// </summary>
public sealed class SeatChoiceTests
{
    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private static Vec3 EyeFor(string aircraftId, string? seatId)
    {
        var seats = Load(aircraftId).Loading.Seats;
        return seats[SeatChoice.IndexOf(seats, seatId)].EyeVec();
    }

    private static void AssertEye(Vec3 eye, double x, double y, double z, string what)
    {
        Assert.True(System.Math.Abs(eye.X - x) <= 1e-3 && System.Math.Abs(eye.Y - y) <= 1e-3 && System.Math.Abs(eye.Z - z) <= 1e-3,
            $"{what}: eye ({eye.X}, {eye.Y}, {eye.Z}) wanted ({x}, {y}, {z})");
    }

    /// <summary>The bug itself. The landing page has the 2-33 loaded; the pilot picks its REAR seat; FLY spawns
    /// the Pitts. Carried as an index (1) that was the Pitts' front seat. Carried as an ID it is the Pitts' rear.</summary>
    [Fact]
    public void RearChosenWhileTheGliderWasLoadedIsThePittsRear()
    {
        var glider = Load("glider-2-33-like").Loading.Seats;
        string? chosen = SeatChoice.Cycle(glider, null, +1);   // the menu's > from the glider's default (front)
        Assert.Equal("rear", chosen);
        AssertEye(EyeFor("pitts-s2b-like", chosen), -1.325, 0.0, -0.300, "Pitts rear");
    }

    /// <summary>The PRIMARY (solo) seat is the default, and every config lists it first. eyepoints.json: "the rear
    /// on a tandem, the front on the 2-33, the left on the 172".</summary>
    [Theory]
    [InlineData("pitts-s2b-like", "rear")]
    [InlineData("great-lakes-like", "rear")]
    [InlineData("extra-300-like", "rear")]
    [InlineData("j3-cub-like", "rear")]
    [InlineData("j3-cub-floats-like", "rear")]
    [InlineData("glider-2-33-like", "front")]
    [InlineData("c172-like", "pilot")]
    public void WithNoChoiceThePilotIsInThePrimarySeat(string aircraftId, string primary)
    {
        var seats = Load(aircraftId).Loading.Seats;
        Assert.Equal(primary, SeatChoice.IdAt(seats, SeatChoice.IndexOf(seats, null)));
    }

    [Fact]
    public void ThePittsDefaultIsTheRearSeatEye() => AssertEye(EyeFor("pitts-s2b-like", null), -1.325, 0.0, -0.300, "Pitts default");

    [Fact]
    public void ThePittsFrontIsTheFrontEye() => AssertEye(EyeFor("pitts-s2b-like", "front"), -0.564, 0.0, -0.308, "Pitts front");

    /// <summary>Each tandem two-seater: front and rear are their own eyes whichever order they are listed in.</summary>
    [Theory]
    [InlineData("great-lakes-like", "rear", -1.960, 0.0, -0.305)]
    [InlineData("great-lakes-like", "front", -1.193, 0.0, -0.293)]
    [InlineData("j3-cub-like", "rear", -0.840, 0.0, -0.489)]
    [InlineData("j3-cub-like", "front", -0.157, 0.0, -0.599)]
    [InlineData("extra-300-like", "rear", -1.597, 0.0, -0.518)]
    [InlineData("extra-300-like", "front", -0.687, 0.0, -0.529)]
    [InlineData("glider-2-33-like", "front", 0.971, 0.0, -0.2122)]
    [InlineData("glider-2-33-like", "rear", 0.164, 0.0, -0.060)]
    [InlineData("c172-like", "copilot", -0.346, 0.332, -0.548)]
    public void ASeatIdIsThatSeatInEveryAeroplane(string aircraftId, string seatId, double x, double y, double z) =>
        AssertEye(EyeFor(aircraftId, seatId), x, y, z, $"{aircraftId}/{seatId}");

    /// <summary>An aeroplane without the asked-for seat (the 172 has no "rear") puts the pilot in its primary
    /// seat, never in whatever happens to sit at the same index.</summary>
    [Fact]
    public void AnUnknownSeatIsThePrimarySeat()
    {
        var c172 = Load("c172-like").Loading.Seats;
        Assert.Equal("pilot", SeatChoice.IdAt(c172, SeatChoice.IndexOf(c172, "rear")));
        Assert.Equal("pilot", SeatChoice.IdAt(c172, SeatChoice.IndexOf(c172, "")));
    }

    [Fact]
    public void CyclingWrapsBothWays()
    {
        var pitts = Load("pitts-s2b-like").Loading.Seats;
        Assert.Equal("front", SeatChoice.Cycle(pitts, "rear", +1));
        Assert.Equal("rear", SeatChoice.Cycle(pitts, "front", +1));
        Assert.Equal("front", SeatChoice.Cycle(pitts, "rear", -1));
        Assert.Equal("Rear seat (solo)", SeatChoice.Label(pitts, null));
        Assert.Equal("Front seat", SeatChoice.Label(pitts, "front"));
    }

    [Fact]
    public void NoSeatsAtAllIsHarmless()
    {
        Assert.Equal(0, SeatChoice.IndexOf(null, "rear"));
        Assert.Null(SeatChoice.IdAt(new System.Collections.Generic.List<StationConfig>(), 0));
        Assert.Null(SeatChoice.Cycle(null, "rear", 1));
        Assert.Equal("Cockpit", SeatChoice.Label(null, "rear"));
    }
}
