// Unity has no System.Text.Json; there the Bridge assembly reads the same file with Newtonsoft
// (UnityAtmosphereLoader) into these same data classes. This file only compiles in the headless build.
#if !UNITY_5_3_OR_NEWER
using System.Text.Json;

namespace FlyingGame.Core.DataContracts;

/// <summary>Loads the regional atmosphere config. Unknown fields are ignored; missing ones take the class defaults.</summary>
public static class AtmosphereFieldConfigLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static AtmosphereFieldConfig LoadFromFile(string path) => LoadFromJson(File.ReadAllText(path));

    public static AtmosphereFieldConfig LoadFromJson(string json) =>
        JsonSerializer.Deserialize<AtmosphereFieldConfig>(json, Options)
        ?? throw new InvalidDataException("AtmosphereFieldConfig JSON deserialized to null.");
}
#endif
