using FlyingGame.Core.DataContracts;

namespace FlyingGame.FlightTests;

/// <summary>Loads the shared glider-2-33-like AircraftConfig JSON once for all flight tests.</summary>
internal static class TestAircraftConfig
{
    public static AircraftConfig Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", "glider-2-33-like.json");
        return AircraftConfigLoader.LoadFromFile(path);
    }
}
