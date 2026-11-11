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
            string path = "aircraft/" + aircraftId + ".json";
            string json = StreamingFiles.ReadAllText(path);   // not File: on Android StreamingAssets is inside the APK
            AircraftConfig config = JsonConvert.DeserializeObject<AircraftConfig>(json, Settings)
                ?? throw new InvalidDataException($"AircraftConfig at {path} deserialized to null.");
            // The bushwheel option belongs to the SESSION, not to the type: any aeroplane can be put on big
            // soft tyres. Applied here so every path that loads an aircraft gets it — the physics reads it for
            // the tyre's size and pressure, and the visual draws the tyre the same size the physics uses.
            config.Bushwheels = SessionSettings.Bushwheels;
            config.Skis = SessionSettings.Skis;
            return config;
        }

        /// <summary>
        /// JUST the aeroplane's <c>displayName</c>, without building the whole config. The aircraft picker needs one
        /// string from each of up to 33 configs before the landing page can draw its first frame, and deserialising 33
        /// complete aeroplanes (every surface, every station, every curve) to read one property is a cost nobody asked
        /// for. This walks the JSON and stops at the first top-level "displayName".
        ///
        /// Null when the config is missing, unreadable or has no displayName — the caller then keeps its compiled
        /// fallback (<see cref="FlyingGame.Core.FleetNames"/>). Never throws: a type with a broken config must still
        /// appear in the picker, under whatever name the code had.
        /// </summary>
        public static string DisplayName(string aircraftId)
        {
            if (string.IsNullOrEmpty(aircraftId)) return null;
            if (!StreamingFiles.TryReadAllText("aircraft/" + aircraftId + ".json", out string json, out string error))
            {
                Debug.LogWarning($"[Fleet] no config for {aircraftId} ({error}): the menu keeps its built-in name");
                return null;
            }
            try
            {
                using var reader = new JsonTextReader(new StringReader(json));
                while (reader.Read())
                {
                    // Depth 1 is a property of the root object: a nested "displayName" (a station's, say) is not the type's.
                    if (reader.TokenType != JsonToken.PropertyName || reader.Depth != 1) continue;
                    if (!string.Equals((string)reader.Value, "displayName", System.StringComparison.OrdinalIgnoreCase)) continue;
                    return reader.Read() ? reader.Value as string : null;
                }
            }
            catch (System.Exception e) { Debug.LogWarning($"[Fleet] {aircraftId}.json: {e.Message}"); }
            return null;
        }
    }
}
