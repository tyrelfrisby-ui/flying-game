using System.IO;
using FlyingGame.Core.DataContracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>Unity-side loader for the regional atmosphere (configs/atmosphere → StreamingAssets/atmosphere, mirrored by
    /// ConfigSync). Core's System.Text.Json loader is #if-guarded out of Unity; this reads the same file into the same
    /// classes.</summary>
    public static class UnityAtmosphereLoader
    {
        public const string FileName = "regional-atmosphere.json";

        private static readonly JsonSerializerSettings Settings = new()
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
            ObjectCreationHandling = ObjectCreationHandling.Replace,
        };

        public static AtmosphereFieldConfig Load()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "atmosphere", FileName);
            return JsonConvert.DeserializeObject<AtmosphereFieldConfig>(File.ReadAllText(path), Settings)
                ?? throw new InvalidDataException("Regional atmosphere config deserialized to null.");
        }
    }
}
