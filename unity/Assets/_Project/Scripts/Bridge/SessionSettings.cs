using FlyingGame.Core;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>What the landing page chose: aircraft, where to start, which challenge, and the weather.
    /// Plain static state so every controller can read it on spawn.</summary>
    public static class SessionSettings
    {
        public enum Start { InTheAir, OnTheRunway, OnFinal }

        public static bool MenuOpen = true;               // landing page showing: pads/HUD hidden, sim paused
        public static string AircraftId = "glider-2-33-like";
        public static string TugId = "pa25-pawnee-like";
        public enum InstrumentMode { Analog, Hud, None }
        /// <summary>Lift/sink markers: Auto = on for gliders, off for powered types; or forced On / Off.</summary>
        public enum Tri { Auto, On, Off }
        public static Tri LiftMarkers = Tri.Auto;
        public static bool BubblesOn = true;
        public static bool ShowForceVectors = false;   // draw every force/moment the physics applies, on the airframe
        public static bool LiftMarkersVisible(string aircraftId) => LiftMarkers == Tri.On || (LiftMarkers == Tri.Auto && aircraftId != null && aircraftId.StartsWith("glider"));
        public static InstrumentMode Instruments = InstrumentMode.Analog;   // round dials / green HUD / nothing      // aerotow tug for the glider: Pawnee or Super Cub
        public static Start StartMode = Start.InTheAir;
        /// <summary>Dense bubble field drawn with GPU instancing (thousands of bubbles). Off = the old per-bubble draws
        /// with a sparser field — the escape hatch if instancing fails to place instances on a device.</summary>
        public static bool BubbleInstancing = true;
        /// <summary>Magenta 5-second flight path vector with a cone at its tip (owner 2026-09-10).</summary>
        public static bool ShowFlightPath = true;
        /// <summary>Real 3-D airframe models where one exists (AirframeModels); off = the procedural airframes.</summary>
        public static bool UseAirframeModels = true;
        /// <summary>Which paved runway the runway start / on-final start / tow uses (owner 2026-09-10): into the wind,
        /// or the crosswind one.</summary>
        public enum RunwayPick { Headwind, Crosswind }
        public static RunwayPick Runway = RunwayPick.Headwind;
        public static FlyingGame.Core.WorldTerrain.RunwayEnd ChosenRunway() =>
            FlyingGame.Core.WorldTerrain.ChooseRunway(Airport, WindFromDeg * Mathf.Deg2Rad, Runway == RunwayPick.Headwind, WindSpeedMs);
        public static int AirportIndex = 0;               // WorldTerrain.Airports
        public static string ChallengeId = null;          // null = free flight
        public static float WindFromDeg = 90f;            // compass: wind blows FROM this heading — from the east, onto the
        public static float WindSpeedMs = 5f;             // ridge to the west: always a crosswind on the (north-south) runways
        public static int TurbulenceLevel = 0;            // 0 calm .. 3 severe
        public static float IsaDeviationC = 0f;           // hot/cold day
        public static float ThermalScale = 1f;            // 0 off .. 2 strong

        // ---- multiplayer (free play only: challenges/events force Solo) ----
        public enum MultiplayerMode { Solo, FreeForAll, PrivateRoom }
        public static MultiplayerMode Multiplayer = MultiplayerMode.Solo;
        public static string RoomCode = "";                // 6 × [A-Z0-9] for PrivateRoom; "ffa" is the well-known free-for-all
        public static string PilotName = "";               // shown on the name tag over your aircraft
#if UNITY_EDITOR
        public static string RelayUrl = "ws://localhost:8787";   // `cd server/relay && npm run dev`
#else
        public static string RelayUrl = "wss://flyinggame-relay.tyrel-frisby.workers.dev";
#endif
        public const string FfaRoom = "ffa";

        /// <summary>Room the session actually joins (null = solo). Challenges/events are always solo.</summary>
        public static string EffectiveRoom
        {
            get
            {
                if (ChallengeId != null) return null;
                return Multiplayer switch
                {
                    MultiplayerMode.FreeForAll => FfaRoom,
                    MultiplayerMode.PrivateRoom => IsValidRoomCode(RoomCode) ? RoomCode : null,
                    _ => null,
                };
            }
        }

        public static bool IsValidRoomCode(string code)
        {
            if (code == null || code.Length != 6) return false;
            foreach (char c in code) if (!(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9')) return false;
            return true;
        }

        public static string NewRoomCode()
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";   // no 0/O/1/I ambiguity
            var sb = new System.Text.StringBuilder(6);
            for (int i = 0; i < 6; i++) sb.Append(alphabet[Random.Range(0, alphabet.Length)]);
            return sb.ToString();
        }

        public static bool IsEvent(string id) => id != null && id.StartsWith("event:");

        public static WorldTerrain.Airport Airport => WorldTerrain.Airports[Mathf.Clamp(AirportIndex, 0, WorldTerrain.Airports.Length - 1)];

        public static readonly (string id, string name)[] Fleet =
        {
            ("glider-2-33-like", "Trainer Glider"), ("glider-eb29r-like", "EB29R Open Class"), ("glider-swift-s1-like", "Swift Aerobatic"), ("c172-like", "Skyhawk"), ("pa18-cub-like", "Super Cub"),
            ("pa18-bush-like", "Cub Bushwheels"), ("pa18-floats-like", "Cub on Floats"), ("pa25-pawnee-like", "Pawnee"),
            ("decathlon-8kcab-like", "Decathlon"), ("extra-300-like", "Extra 300"), ("pitts-s2b-like", "Pitts S-2"),
            ("stearman-pt17-like", "Stearman"), ("p51d-like", "P-51 Mustang"), ("f86-sabre-like", "F-86 Sabre"),
            ("seminole-like", "Seminole"), ("dc3-like", "DC-3"), ("boeing-737-like", "737"),
            ("cassutt-f1-like", "Cassutt Formula One"), ("geebee-r2-like", "Gee Bee R-2"), ("glasair3-like", "Glasair III 400"),
        };

        public static readonly (string id, string name)[] Challenges =
        {
            (null, "Free flight"), ("event:race", "Air Racing"), ("event:stol", "STOL contest"), ("event:dust", "Crop dusting"), ("event:aerobox", "Aerobatic box"), ("event:combat", "Combat zone"),
            ("a1c1-wings-level", "Wings level"), ("a1c2-best-glide", "Best glide"),
            ("a1c3-cardinal-turn", "Cardinal turn"), ("a1c4-stall-recover", "Stall recovery"),
            ("a1c10-headwind-landing", "Headwind landing"), ("a1c11-crosswind-landing", "Crosswind landing"),
        };

        /// <summary>FLYING LESSONS (owner 2026-09-15): the game flies every axis but the one being learned.</summary>
        public static readonly (string id, string name)[] Lessons =
        {
            ("lesson:straight", "Straight: wings level"),
            ("practice:xwind-rudder", "Crosswind · rudder"), ("practice:xwind-aileron", "Crosswind · aileron"),
            ("practice:land-rudder", "Crosswind landing · rudder"), ("practice:land-aileron", "Crosswind landing · aileron"),
            ("practice:flare", "Round-out & flare"), ("practice:flare-side", "Flare · side view"),
            ("practice:approach-side", "Approach · side view"),
            ("practice:s-turns", "S-turns 45°"), ("practice:s-turns-test", "Test: S-turns, one speed cycle"),
            ("practice:stall-side", "Stall · side view"), ("practice:stall-rudder", "Stall, wing drop · rudder"),
            ("practice:stall-elevator", "Stall, recover · elevator"),
        };
        // ---- practice exercises (owner 2026-09-15): the game flies every axis but the one being practised ----
        public static bool IsPractice(string id) => id != null && (id.StartsWith("practice:") || id.StartsWith("lesson:"));
        public static FlyingGame.Sim.Practice.PracticeKind? PracticeKindFor(string id) => id switch
        {
            "practice:xwind-rudder" => FlyingGame.Sim.Practice.PracticeKind.CrosswindRudder,
            "practice:xwind-aileron" => FlyingGame.Sim.Practice.PracticeKind.CrosswindAileron,
            "practice:land-rudder" => FlyingGame.Sim.Practice.PracticeKind.LandingRudder,
            "practice:land-aileron" => FlyingGame.Sim.Practice.PracticeKind.LandingAileron,
            "practice:flare" => FlyingGame.Sim.Practice.PracticeKind.Flare,
            "practice:flare-side" => FlyingGame.Sim.Practice.PracticeKind.FlareSideView,
            "practice:approach-side" => FlyingGame.Sim.Practice.PracticeKind.ApproachSideView,
            "practice:s-turns" => FlyingGame.Sim.Practice.PracticeKind.STurns,
            "practice:s-turns-test" => FlyingGame.Sim.Practice.PracticeKind.STurnsTest,
            "practice:stall-side" => FlyingGame.Sim.Practice.PracticeKind.StallSideView,
            "practice:stall-rudder" => FlyingGame.Sim.Practice.PracticeKind.StallRudder,
            "practice:stall-elevator" => FlyingGame.Sim.Practice.PracticeKind.StallElevator,
            "lesson:straight" => FlyingGame.Sim.Practice.PracticeKind.Straight,
            _ => null,
        };
        /// <summary>Wind choice for the practice: crosswind exercises offer Steady/Gusty/Shifting, approach and flare exercises
        /// Calm/Headwind/Gusty headwind/Tailwind/Gusty tailwind.</summary>
        public static FlyingGame.Sim.Practice.PracticeWind PracticeWindChoice = FlyingGame.Sim.Practice.PracticeWind.Steady;
        public static bool PracticeIsAirwork(string id) => id is "lesson:straight" or "practice:s-turns" or "practice:s-turns-test" or "practice:stall-side" or "practice:stall-rudder" or "practice:stall-elevator";
        public static bool PracticeIsCrosswind(string id) => id is "practice:xwind-rudder" or "practice:xwind-aileron" or "practice:land-rudder" or "practice:land-aileron";
        public static readonly (FlyingGame.Sim.Practice.PracticeWind w, string name)[] CrosswindChoices =
        {
            (FlyingGame.Sim.Practice.PracticeWind.Steady, "Steady"), (FlyingGame.Sim.Practice.PracticeWind.Gusty, "Gusty"), (FlyingGame.Sim.Practice.PracticeWind.Shifting, "Shifting"),
        };
        public static readonly (FlyingGame.Sim.Practice.PracticeWind w, string name)[] AlongWindChoices =
        {
            (FlyingGame.Sim.Practice.PracticeWind.Calm, "Calm"), (FlyingGame.Sim.Practice.PracticeWind.Headwind, "Headwind"), (FlyingGame.Sim.Practice.PracticeWind.HeadwindGusty, "Gusty headwind"),
            (FlyingGame.Sim.Practice.PracticeWind.Tailwind, "Tailwind"), (FlyingGame.Sim.Practice.PracticeWind.TailwindGusty, "Gusty tailwind"),
        };

        // ---- control feel (owner 2026-09-15): per-axis expo and dead zone the user can tune; < 0 = the aircraft's own ----
        public static readonly float[] FeelExpo = { -1f, -1f, -1f };       // aileron, elevator, rudder
        public static readonly float[] FeelDeadZone = { -1f, -1f, -1f };
        public static readonly string[] FeelAxisNames = { "Aileron", "Elevator", "Rudder" };
        private static bool _feelLoaded;
        public static void LoadFeel()
        {
            if (_feelLoaded) return; _feelLoaded = true;
            for (int i = 0; i < 3; i++) { FeelExpo[i] = PlayerPrefs.GetFloat("feel.expo." + i, -1f); FeelDeadZone[i] = PlayerPrefs.GetFloat("feel.dz." + i, -1f); }
        }
        public static void SaveFeel()
        {
            for (int i = 0; i < 3; i++) { PlayerPrefs.SetFloat("feel.expo." + i, FeelExpo[i]); PlayerPrefs.SetFloat("feel.dz." + i, FeelDeadZone[i]); }
            PlayerPrefs.Save();
        }
        /// <summary>Push the user's feel settings into a loaded config (the aircraft's own values stay where a setting is off).</summary>
        public static void ApplyFeel(FlyingGame.Core.DataContracts.AircraftConfig cfg)
        {
            LoadFeel();
            if (cfg?.Controls == null) return;
            var axes = new[] { cfg.Controls.Aileron, cfg.Controls.Elevator, cfg.Controls.Rudder };
            for (int i = 0; i < 3; i++)
            {
                if (axes[i] == null) continue;
                if (FeelExpo[i] >= 0f) axes[i].Expo = FeelExpo[i];
                if (FeelDeadZone[i] >= 0f) axes[i].DeadZone = FeelDeadZone[i];
            }
        }

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
