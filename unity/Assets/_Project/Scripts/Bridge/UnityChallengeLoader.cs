using System.IO;
using FlyingGame.Sim.Challenge;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>Unity-side ChallengeDefinition loader (Core's System.Text.Json loader is #if-guarded
    /// out of Unity). Reads from StreamingAssets/challenges, mirrored from the repo by ConfigSync.</summary>
    public static class UnityChallengeLoader
    {
        private static readonly JsonSerializerSettings Settings = new()
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
        };

        public static ChallengeDefinition Load(string challengeId)
        {
            string path = Path.Combine(Application.streamingAssetsPath, "challenges", challengeId + ".json");
            string json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<ChallengeDefinition>(json, Settings)
                ?? throw new InvalidDataException($"Challenge {challengeId} deserialized to null.");
        }
    }
}
