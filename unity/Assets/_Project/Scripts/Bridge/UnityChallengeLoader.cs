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
            string json = StreamingFiles.ReadAllText("challenges/" + challengeId + ".json");
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
                var list = JsonConvert.DeserializeObject<System.Collections.Generic.List<FlyingGame.Sim.Practice.GlideEntry>>(StreamingFiles.ReadAllText("glide-table.json"));
                if (list != null) FlyingGame.Sim.Practice.GlideTable.Load(list);
                Debug.Log($"[GlideTable] {FlyingGame.Sim.Practice.GlideTable.Count} rows");
            }
            catch (System.Exception e) { Debug.LogWarning($"[GlideTable] not loaded ({e.Message}) — lessons compute their glide live"); }
        }
    }
}
