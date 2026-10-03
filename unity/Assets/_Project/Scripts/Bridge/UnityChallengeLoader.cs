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

    /// <summary>The idle-glide table (configs/glide-table.json → StreamingAssets): every landing lesson starts on its row.</summary>
    public static class UnityGlideTableLoader
    {
        public static void Load()
        {
            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, "glide-table.json");
                var list = JsonConvert.DeserializeObject<System.Collections.Generic.List<FlyingGame.Sim.Practice.GlideEntry>>(File.ReadAllText(path));
                if (list != null) FlyingGame.Sim.Practice.GlideTable.Load(list);
                Debug.Log($"[GlideTable] {FlyingGame.Sim.Practice.GlideTable.Count} rows");
            }
            catch (System.Exception e) { Debug.LogWarning($"[GlideTable] not loaded ({e.Message}) — lessons compute their glide live"); }
        }
    }
}
