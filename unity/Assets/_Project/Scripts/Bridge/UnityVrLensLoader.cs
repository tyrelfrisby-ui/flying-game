using System.IO;
using FlyingGame.Core.Vr;
using Newtonsoft.Json;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The headset profile, read with Newtonsoft because Unity has no System.Text.Json — the same split
    /// AtmosphereFieldConfigLoader / UnityAtmosphereLoader already uses. The data class is shared, so the
    /// headless tests and the game read the identical file into the identical fields.
    /// </summary>
    public static class UnityVrLensLoader
    {
        private static readonly JsonSerializerSettings Settings = new()
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore,
        };

        /// <summary>The profile from StreamingAssets, or the built-in generic Cardboard v2 values if it is
        /// missing or unreadable: VR must never fail to start over a config file.</summary>
        public static VrLensConfig Load()
        {
            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, "vr-cardboard.json");
                if (File.Exists(path))
                {
                    var c = JsonConvert.DeserializeObject<VrLensConfig>(File.ReadAllText(path), Settings);
                    if (c != null) return c.Sane();
                }
                else Debug.Log("[VR] no vr-cardboard.json in StreamingAssets; using the built-in Cardboard v2 profile.");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[VR] vr-cardboard.json could not be read, using the built-in profile: " + e.Message);
            }
            return new VrLensConfig().Sane();
        }
    }
}
