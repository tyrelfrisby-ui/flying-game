using FlyingGame.Core;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>What the landing page chose: aircraft, where to start, which challenge, and the weather.
    /// Plain static state so every controller can read it on spawn.</summary>
    public static class SessionSettings
    {
        public enum Start { InTheAir, OnTheRunway }

        public static bool MenuOpen = true;               // landing page showing: pads/HUD hidden, sim paused
        public static string AircraftId = "glider-2-33-like";
        public static string TugId = "pa25-pawnee-like";      // aerotow tug for the glider: Pawnee or Super Cub
        public static Start StartMode = Start.InTheAir;
        public static int AirportIndex = 0;               // WorldTerrain.Airports
        public static string ChallengeId = null;          // null = free flight
        public static float WindFromDeg = 0f;             // compass: wind blows FROM this heading
        public static float WindSpeedMs = 0f;
        public static int TurbulenceLevel = 0;            // 0 calm .. 3 severe
        public static float IsaDeviationC = 0f;           // hot/cold day
        public static float ThermalScale = 1f;            // 0 off .. 2 strong

        public static bool IsEvent(string id) => id != null && id.StartsWith("event:");

        public static WorldTerrain.Airport Airport => WorldTerrain.Airports[Mathf.Clamp(AirportIndex, 0, WorldTerrain.Airports.Length - 1)];

        public static readonly (string id, string name)[] Fleet =
        {
            ("glider-2-33-like", "Trainer Glider"), ("c172-like", "Skyhawk"), ("pa18-cub-like", "Super Cub"),
            ("pa18-bush-like", "Cub Bushwheels"), ("pa18-floats-like", "Cub on Floats"), ("pa25-pawnee-like", "Pawnee"),
            ("decathlon-8kcab-like", "Decathlon"), ("extra-300-like", "Extra 300"), ("pitts-s2b-like", "Pitts S-2"),
            ("stearman-pt17-like", "Stearman"), ("p51d-like", "P-51 Mustang"), ("f86-sabre-like", "F-86 Sabre"),
            ("seminole-like", "Seminole"), ("dc3-like", "DC-3"), ("boeing-737-like", "737"),
        };

        public static readonly (string id, string name)[] Challenges =
        {
            (null, "Free flight"), ("event:race", "Air Racing"), ("event:stol", "STOL contest"), ("event:aerobox", "Aerobatic box"),
            ("a1c1-wings-level", "Wings level"), ("a1c2-best-glide", "Best glide"),
            ("a1c3-cardinal-turn", "Cardinal turn"), ("a1c4-stall-recover", "Stall recovery"),
            ("a1c10-headwind-landing", "Headwind landing"), ("a1c11-crosswind-landing", "Crosswind landing"),
        };

        /// <summary>Push the weather choices into the atmosphere the wings and bubbles read.</summary>
        public static void ApplyWeather()
        {
            Atmosphere.IsaDeviationK = IsaDeviationC;
            Atmosphere.ThermalStrengthScale = ThermalScale;
            float rad = WindFromDeg * Mathf.Deg2Rad;
            // Wind FROM heading h blows toward h+180: vector = -speed·(cos h, sin h) in sim (x north, y east).
            Atmosphere.SteadyWind = WindSpeedMs > 0.01f
                ? new FlyingGame.Core.MathTypes.Vec3(-WindSpeedMs * Mathf.Cos(rad), -WindSpeedMs * Mathf.Sin(rad), 0)
                : FlyingGame.Core.MathTypes.Vec3.Zero;
            Atmosphere.ActiveTurbulence = TurbulenceLevel switch
            {
                1 => Turbulence.Light(), 2 => Turbulence.Moderate(), 3 => Turbulence.Severe(), _ => null,
            };
        }
    }
}
