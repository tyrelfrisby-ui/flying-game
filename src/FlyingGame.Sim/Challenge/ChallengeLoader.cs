#if !UNITY_5_3_OR_NEWER
using System.Text.Json;

namespace FlyingGame.Sim.Challenge;

/// <summary>Loads ChallengeDefinition JSON (headless/dotnet). Unity uses a Newtonsoft loader in the
/// Bridge, deserializing these same classes.</summary>
public static class ChallengeLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static ChallengeDefinition LoadFromFile(string path) =>
        LoadFromJson(File.ReadAllText(path));

    public static ChallengeDefinition LoadFromJson(string json) =>
        JsonSerializer.Deserialize<ChallengeDefinition>(json, Options)
        ?? throw new InvalidDataException("ChallengeDefinition JSON deserialized to null.");
}
#endif
