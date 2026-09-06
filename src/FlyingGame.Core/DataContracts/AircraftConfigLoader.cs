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
