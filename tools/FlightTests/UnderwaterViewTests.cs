using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The pilot's view under water (Ty t922u): Core <see cref="UnderwaterView"/>, whose arithmetic the
/// Hidden/FlyingGame/PilotUnderwater shader repeats per pixel. Expected values are literals worked by hand.
/// </summary>
public class UnderwaterViewTests
{
    private readonly ITestOutputHelper _out;
    public UnderwaterViewTests(ITestOutputHelper o) { _out = o; }

    [Theory]
    // Eye 0.5 m under water: down, level, and up a 45° ray that leaves the water after 0.5/sin45 = 0.7071 m.
    [InlineData(-0.5, -1.0, 2.0, 2.0)]
    [InlineData(-0.5, 0.0, 3.0, 3.0)]
    [InlineData(-0.5, 0.70710678, 3.0, 0.70710678)]
    [InlineData(-0.5, 0.70710678, 0.4, 0.4)]          // the panel is reached before the surface: all of it is wet
    // Eye 0.3 m above water: up or level is dry; straight down 1.0 m wets the last 0.7 m; 30° down wets 2.0 - 0.6.
    [InlineData(0.3, 1.0, 5.0, 0.0)]
    [InlineData(0.3, 0.0, 5.0, 0.0)]
    [InlineData(0.3, -1.0, 1.0, 0.7)]
    [InlineData(0.3, -0.5, 2.0, 1.4)]
    [InlineData(0.3, -0.5, 0.5, 0.0)]                 // the ray ends before it reaches the surface
    public void SubmergedLengthIsThePartOfTheRayUnderThePlane(double eye, double up, double len, double expected) =>
        Assert.Equal(expected, UnderwaterView.SubmergedLength(eye, up, len), 6);

    [Fact]
    public void OnlyTheSeatViewOverWaterWithTheEyeInOrNearItIsTreated()
    {
        Assert.Equal(UnderwaterView.Mode.Off, UnderwaterView.ModeFor(false, 100.0, 99.0));     // chase/aerial: never
        Assert.Equal(UnderwaterView.Mode.Off, UnderwaterView.ModeFor(true, null, 99.0));       // no water under the eye
        Assert.Equal(UnderwaterView.Mode.Full, UnderwaterView.ModeFor(true, 100.0, 99.4));
        Assert.Equal(UnderwaterView.Mode.NearField, UnderwaterView.ModeFor(true, 100.0, 100.0));
        Assert.Equal(UnderwaterView.Mode.NearField, UnderwaterView.ModeFor(true, 100.0, 101.0));
        Assert.Equal(UnderwaterView.Mode.Off, UnderwaterView.ModeFor(true, 100.0, 101.01));
        Assert.Equal(UnderwaterView.Mode.Off, UnderwaterView.ModeFor(true, 100.0, 400.0));    // flying over the lake
    }

    [Fact]
    public void ThePanelUnderWaterIsVisiblyMurkyAndADryPartIsUntouched()
    {
        // Head 0.4 m in the water, panel 0.7 m ahead and level: 1 - exp(-0.7 * 0.55) = 0.3195.
        Assert.Equal(0.3195, UnderwaterView.Murk(UnderwaterView.Mode.Full, -0.4, 0.0, 0.7), 4);
        // The same head, a strut 1 m away up-and-out of the water at 60° (leaves after 0.4/sin60 = 0.4619 m): 0.2243.
        Assert.Equal(0.2243, UnderwaterView.Murk(UnderwaterView.Mode.Full, -0.4, 0.8660254, 1.0), 4);
        // Head 0.5 m above the water: anything above the surface stays exactly as it was.
        Assert.Equal(0.0, UnderwaterView.Murk(UnderwaterView.Mode.NearField, 0.5, 0.2, 1.0));
        Assert.Equal(0.0, UnderwaterView.Murk(UnderwaterView.Mode.Off, -0.4, 0.0, 0.7));
    }

    [Fact]
    public void WithTheEyeAboveTheWaterTheLakeBeyondTheAirframeKeepsItsOwnLook()
    {
        // Eye 0.5 m up, looking 30° down: at 3 m (the nose) 2 m are wet and get full extra murk, 1 - exp(-1.1) = 0.6671;
        // at 8 m and beyond (the lake bed) nothing is added — the water's own pass draws it as it always did.
        Assert.Equal(0.6671, UnderwaterView.Murk(UnderwaterView.Mode.NearField, 0.5, -0.5, 3.0), 4);
        Assert.Equal(0.0, UnderwaterView.Murk(UnderwaterView.Mode.NearField, 0.5, -0.5, 8.0));
        Assert.Equal(0.0, UnderwaterView.Murk(UnderwaterView.Mode.NearField, 0.5, -0.5, 40.0));
        // With the head in the water the far view does go to murk: 40 m of lake is opaque.
        Assert.True(UnderwaterView.Murk(UnderwaterView.Mode.Full, -0.5, -0.5, 40.0) > 0.999);
    }

    // The aeroplane from the report (the other float configs have no authored seat eye).
    [Theory]
    [InlineData("j3-cub-floats-like")]
    public void AFloatplaneSittingUprightOnTheWaterIsNeverTreatedFromTheSeat(string id)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json");
        AircraftConfig c = AircraftConfigLoader.LoadFromFile(path);
        Assert.NotNull(c.Floats);
        // Body z is DOWN from the CG; afloat level, the keel is a generous 0.5 m under the surface.
        double eyeZ = c.Loading.Seats.First().EyeVec().Z;
        double eyeAboveWater = (c.Floats!.KeelZ - 0.5) - eyeZ;
        _out.WriteLine($"{id}: eye {eyeAboveWater:F2} m above the water afloat (keel {c.Floats.KeelZ:F3}, eye z {eyeZ:F3})");
        Assert.True(eyeAboveWater > UnderwaterView.NearAboveM + 0.2, $"{id}: eye only {eyeAboveWater:F2} m up");
        Assert.Equal(UnderwaterView.Mode.Off, UnderwaterView.ModeFor(true, 10.0, 10.0 + eyeAboveWater));
    }
}
