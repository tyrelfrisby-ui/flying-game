using System.IO;
using FlyingGame.Core.DataContracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Unity-side AircraftConfig loader (Core's System.Text.Json loader doesn't compile under Unity —
    /// see the #if guard in AircraftConfigLoader.cs). Deserializes the SAME data classes from
    /// StreamingAssets, which the ConfigSync editor script mirrors from the repo's configs/ directory.
    /// </summary>
    public static class UnityAircraftConfigLoader
    {
        private static readonly JsonSerializerSettings Settings = new()
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
        };

        public static AircraftConfig LoadFromStreamingAssets(string aircraftId)
        {
            string path = Path.Combine(Application.streamingAssetsPath, "aircraft", aircraftId + ".json");
            string json = File.ReadAllText(path);
            AircraftConfig config = JsonConvert.DeserializeObject<AircraftConfig>(json, Settings)
                ?? throw new InvalidDataException($"AircraftConfig at {path} deserialized to null.");
            // The bushwheel option belongs to the SESSION, not to the type: any aeroplane can be put on big
            // soft tyres. Applied here so every path that loads an aircraft gets it — the physics reads it for
            // the tyre's size and pressure, and the visual draws the tyre the same size the physics uses.
            config.Bushwheels = SessionSettings.Bushwheels;
            return config;
        }
    }
}
