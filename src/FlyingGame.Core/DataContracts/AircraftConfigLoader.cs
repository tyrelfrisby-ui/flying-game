// Unity has no System.Text.Json; there the Bridge assembly supplies a Newtonsoft-based loader
// (UnityAircraftConfigLoader) deserializing into these same data classes. This file only compiles
// in the headless dotnet build (FlightTests).
#if !UNITY_5_3_OR_NEWER
using System.Text.Json;

namespace FlyingGame.Core.DataContracts;

/// <summary>Loads AircraftConfig JSON. Unknown fields are ignored (forward compat); missing fields default per-type.</summary>
public static class AircraftConfigLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static AircraftConfig LoadFromFile(string path)
    {
        string json = File.ReadAllText(path);
        return LoadFromJson(json);
    }

    public static AircraftConfig LoadFromJson(string json)
    {
        AircraftConfig? config = JsonSerializer.Deserialize<AircraftConfig>(json, Options);
        if (config is null)
        {
            throw new InvalidDataException("AircraftConfig JSON deserialized to null.");
        }

        return config;
    }
}
#endif
