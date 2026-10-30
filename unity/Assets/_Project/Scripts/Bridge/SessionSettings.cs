using FlyingGame.Core;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>What the landing page chose: aircraft, where to start, which challenge, and the weather.
    /// Plain static state so every controller can read it on spawn.</summary>
    public static class SessionSettings
    {
        public enum Start { InTheAir, OnTheRunway, OnFinal, InAeroBox, InCombatZone, InThermal }

        public static bool MenuOpen = true;               // landing page showing: pads/HUD hidden, sim paused
        /// <summary>Flight replay is playing: the sim and world are frozen, the control pads are hidden, the replay bar owns the tray.</summary>
        public static bool ReplayActive;
        /// <summary>SLOW MOTION (owner 2026-10-11): the whole world — the simulation, the AI, the tug, the effects — runs at this
        /// fraction of real time while the pilot's controls stay real time, to practise slow and work up to full speed.
        /// Applied through Time.timeScale whenever the game isn't paused; off (1) in multiplayer.</summary>
        public static float SimSpeed = 1f;
        public static readonly float[] SimSpeeds = { 1f, 0.5f, 0.25f, 0.1f };
        public static string SimSpeedLabel => SimSpeed >= 0.999f ? "TIME 100%" : $"SLOW {SimSpeed * 100:0}%";
        /// <summary>Bubbles streak with airspeed (OPTIONS). Owner 2026-10-15: "increase the streakiness of the bubbles
        /// as they pass by the airplane to enhance the linear nature of the movement, so the user can more easily
        /// determine the alpha and beta angles of their airplane relative to the wind."
        ///
        /// Each bubble is stretched along the RELATIVE WIND (the aircraft's velocity through the air mass), so the angle
        /// the line makes with the airframe IS the angle of attack seen from the side and the sideslip seen from above
        /// or in front — the streak is not decoration, it is the instrument. NORMAL keeps the photographic 0.05 s
        /// shutter the bubbles have had since 2026-10-02. LONG, the default, stretches them about three times further,
        /// dims them much less (a long streak spreading its light over more pixels is physically right but washed the
        /// line out), and holds a minimum line length in bubble diameters — at 55 KIAS a shutter-length streak is barely
        /// longer than the bubble itself, and slow flight is exactly where α is worth reading.</summary>
        public enum Streaks { Off, Normal, Long }
        public static Streaks BubbleStreaks = Streaks.Long;
        public static bool StreaksOn => BubbleStreaks != Streaks.Off;
        /// <summary>Seconds of relative-wind travel a bubble is stretched over (0 = round).</summary>
        public static float StreakShutterS => BubbleStreaks == Streaks.Long ? 0.16f : BubbleStreaks == Streaks.Normal ? 0.05f : 0f;
        /// <summary>How much a long streak dims, its light spread along its length: 0 = none, 1 = full.</summary>
        public static float StreakDim => BubbleStreaks == Streaks.Long ? 0.2f : 0.6f;
        /// <summary>A streak never draws shorter than this many bubble diameters (0 = length purely from speed).</summary>
        public static float StreakMinAspect => BubbleStreaks == Streaks.Long ? 6f : 0f;
        public static string AircraftId = "glider-2-33-like";
        public static string TugId = "pa25-pawnee-like";
        public enum InstrumentMode { Analog, Hud, None }
        /// <summary>Lift/sink markers: Auto = on for gliders, off for powered types; or forced On / Off.</summary>
        public enum Tri { Auto, On, Off }
        // Owner 2026-10-28: "default everything off". Auto used to switch the markers on for every glider;
        // now nothing is drawn until the player asks for it, and LoadPrefs restores whatever they asked for last.
        public static Tri LiftMarkers = Tri.Off;
        public static bool BubblesOn = false;             // owner 2026-10-28: off until asked for
        public static bool ShowForceVectors = false;
        /// <summary>Owner 2026-10-07: an optional flight-test data block (α, β, q, TAS/EAS/Mach, pressure and density
        /// altitude, OAT, load factors, rates, path angle, control positions…).</summary>
        public static bool FlightTestData = false;   // draw every force/moment the physics applies, on the airframe
        public static bool LiftMarkersVisible(string aircraftId) => LiftMarkers == Tri.On || (LiftMarkers == Tri.Auto && aircraftId != null && aircraftId.StartsWith("glider"));
        public static InstrumentMode Instruments = InstrumentMode.Hud;      // round dials / green HUD / nothing; owner 2026-10-28: the HUD is the default display
        public static Start StartMode = Start.InTheAir;
        /// <summary>Dense bubble field drawn with GPU instancing (thousands of bubbles). Off = the old per-bubble draws
        /// with a sparser field — the escape hatch if instancing fails to place instances on a device.</summary>
        public static bool BubbleInstancing = true;
        /// <summary>Magenta 5-second flight path vector with a cone at its tip (owner 2026-09-10).</summary>
        public static bool ShowFlightPath = false;        // owner 2026-10-28: off until asked for
        /// <summary>Real 3-D airframe models where one exists (AirframeModels); off = the procedural airframes.</summary>
        public static bool UseAirframeModels = true;

        /// <summary>Prefer the model we drew ourselves over the downloaded one, for the types where we have both
        /// (AirframeModels.OwnSpecs). Owner 2026-10-25: the downloaded models stay so we can switch between them
        /// "until our models are up to my standards", so this is OFF by default — the downloaded model is still
        /// what a player sees, and this is how we look at ours beside it.</summary>
        public static bool UseOwnModels = false;

        /// <summary>WHICH SEAT the cockpit view sits in, for a model that was drawn with more than one eye point
        /// (the Extra 300's Eye_Front / Eye_Rear). Aerobatic two-seaters are flown SOLO FROM THE REAR, which is
        /// why the model marks Eye_Rear `default_view: true` — so Rear is the default here, and the front seat is
        /// the passenger's view. A model with one eye point ignores this, and a model with none keeps the old
        /// canopy-top eye (PilotEgress.CockpitLocal).</summary>
        public enum CockpitSeat { Rear, Front }
        public static CockpitSeat Seat = CockpitSeat.Rear;
        public static string SeatLabel => Seat == CockpitSeat.Rear ? "REAR (PIC)" : "FRONT";

        /// <summary>The paint scheme OVERRIDE (Livery.cs), for every type: AERO_LIVERY at launch, which is how a capture
        /// instance is told to wear Talk Flying for a recorded shot, and the editor renders' -livery. null = none, and
        /// each type wears its own choice (<see cref="LiveryFor"/>).</summary>
        public static string LiveryOverride =
            System.Environment.GetEnvironmentVariable("AERO_LIVERY") is string lv && lv.Length > 0 ? lv : null;

        /// <summary>The livery picker's choices (owner 2026-10-28), per type, as saved in PlayerPrefs: read once per type.</summary>
        private static readonly System.Collections.Generic.Dictionary<string, string> _liveryChoice = new();

        /// <summary>The scheme id `aircraftId` is painted and loaded in: the override, else the player's saved choice for
        /// that type (only types with a picker, FlyingGame.Core.LiveryChoice), else the type's default (the unmarked scheme
        /// for the Pitts and the Extra), and "type" for every other type.</summary>
        public static string LiveryFor(string aircraftId)
        {
            if (!string.IsNullOrEmpty(LiveryOverride) || !LiveryChoice.HasChoice(aircraftId))
                return LiveryChoice.Resolve(aircraftId, LiveryOverride, null);
            if (!_liveryChoice.TryGetValue(aircraftId, out string saved))
                _liveryChoice[aircraftId] = saved = PlayerPrefs.GetString(LiveryChoice.PrefKey(aircraftId), "");
            return LiveryChoice.Resolve(aircraftId, null, saved);
        }

        /// <summary>The picker: choose `liveryId` for `aircraftId` and keep it across launches. The airframe on screen
        /// follows (AirframeVisual rebuilds when its type's livery changes).</summary>
        public static void SetLivery(string aircraftId, string liveryId)
        {
            _liveryChoice[aircraftId] = liveryId;
            PlayerPrefs.SetString(LiveryChoice.PrefKey(aircraftId), liveryId);
            PlayerPrefs.Save();
        }
        /// <summary>Which paved runway the runway start / on-final start / tow uses (owner 2026-09-10): into the wind,
        /// or the crosswind one.</summary>
        public enum RunwayPick { Headwind, Crosswind }
        public static RunwayPick Runway = RunwayPick.Headwind;
        public static FlyingGame.Core.WorldTerrain.RunwayEnd ChosenRunway() =>
            FlyingGame.Core.WorldTerrain.ChooseRunway(Airport, WindFromDeg * Mathf.Deg2Rad, Runway == RunwayPick.Headwind, WindSpeedMs);
        public static int AirportIndex = FlyingGame.Core.WorldTerrain.DefaultAirportIndex;   // WorldTerrain.Airports
        public static string ChallengeId = null;          // null = free flight
        public static float WindFromDeg = 90f;            // compass: wind blows FROM this heading — from the east, onto the
        public static float WindSpeedMs = 5f;             // ridge to the west: always a crosswind on the (north-south) runways
        public static int TurbulenceLevel = 0;            // 0 calm .. 3 severe
        public static float IsaDeviationC = 0f;           // hot/cold day
        public static float ThermalScale = 1f;            // 0 off .. 2 strong

        // ---- badges and scores over other people's aeroplanes (owner 2026-10-18) ----
        /// <summary>Show badges and scores on OTHER pilots' tags. Their handle is shown either way.</summary>
        public static bool ShowOtherBadges = true;
        /// <summary>Show my own badge and score on my own tag and in the HUD. Likewise never the handle.</summary>
        public static bool ShowOwnBadge = true;
        /// <summary>
        /// PUBLISH my badge and score to other pilots at all (owner 2026-10-18). A different kind of switch
        /// from the two above: those are about my screen, this is about what leaves the device. Off means
        /// the badge is NOT SENT rather than not drawn at the far end, because "nobody can see it" has to
        /// mean nobody has it or it is a request rather than a setting. It still shows on your own profile.
        /// Persisted, because it is a preference about yourself rather than about this flight.
        /// </summary>
        public static bool PublishBadge = true;
        private const string PrefPublishBadge = "badge.publish";
        public static void LoadBadgePrefs() => PublishBadge = PlayerPrefs.GetInt(PrefPublishBadge, 1) == 1;
        public static void SaveBadgePrefs() { PlayerPrefs.SetInt(PrefPublishBadge, PublishBadge ? 1 : 0); PlayerPrefs.Save(); }

        /// <summary>
        /// A SCORED CONTEST OWNS THE AIR (owner 2026-10-18: *"every challenge ... must have the exact same
        /// conditions locked ie users cannot tweak the wind to get a better score or crank up the thermal
        /// intensity"*). Called whenever the selected challenge changes and again before the flight starts:
        /// it overwrites the weather and tyre settings with the contest's own and returns true if the pilot
        /// no longer has a choice about them, so the menu can grey them out and say why.
        /// </summary>
        public static bool LockContestConditions()
        {
            if (!FlyingGame.Sim.Ratings.Contests.TryFor(ChallengeId, out var c)) return false;
            WindFromDeg = (float)c.WindFromDeg;
            WindSpeedMs = (float)c.WindSpeedMs;
            TurbulenceLevel = c.TurbulenceLevel;
            IsaDeviationC = (float)c.IsaDeviationC;
            ThermalScale = (float)c.ThermalScale;
            Bushwheels = c.Bushwheels;
            Skis = c.Skis;
            return true;
        }

        /// <summary>Is the current selection one whose score goes on a public board?</summary>
        public static bool ContestLocked => FlyingGame.Sim.Ratings.Contests.IsScored(ChallengeId);

        // ---- multiplayer (free play only: challenges/events force Solo) ----
        /// <summary>Lan is appended, never inserted: the chosen mode is kept in PlayerPrefs as its number.</summary>
        public enum MultiplayerMode { Solo, FreeForAll, PrivateRoom, Lan }
        public static MultiplayerMode Multiplayer = MultiplayerMode.Solo;
        public static string RoomCode = "";                // 6 × [A-Z0-9] for PrivateRoom; "ffa" is the well-known free-for-all
        public static string PilotName = "";               // shown on the name tag over your aircraft

        /// <summary>BUSHWHEELS (owner 2026-10-16): any aeroplane can go on big soft tyres — twice the tyre,
        /// no wheel pants, half the pressure, and it will skim water a Cub on hard tyres could not.</summary>
        public static bool Bushwheels;

        /// <summary>WHEEL-SKIS (owner 2026-10-16: "a Super Cub, a Beaver and an Aircam that can land on the
        /// glacier"). On snow the ski carries the aeroplane — it runs on for ever and the brakes barely bite —
        /// and on anything hard the wheel protrudes through and nothing changes, which is the whole design.</summary>
        public static bool Skis;
#if UNITY_EDITOR
        public static string RelayUrl = "ws://localhost:8787";   // `cd server/relay && npm run dev`
#else
        public static string RelayUrl = "wss://mp.aeroplayground.app";   // the multiplayer platform (owner 2026-10-11; same relay as workers.dev)
#endif
        public const string FfaRoom = "ffa";

        /// <summary>
        /// FLYING ON ONE NETWORK WITH NO INTERNET (owner 2026-10-17: "i want to be sure the ios devices and
        /// mac app can all join in the same local network with no external internet"). A room name carrying
        /// this prefix is not a relay room at all — the aeroplanes talk straight to each other over UDP and
        /// nothing leaves the building.
        /// </summary>
        public const string LanPrefix = "lan:";
        /// <summary>The well-known name everybody on a network lands in unless they type a code.</summary>
        public const string LanRoom = "LAN";
        /// <summary>
        /// An address to try when the sweep cannot find anybody: "192.168.1.42", or just "42" for that host on
        /// our own subnet — four digits one pilot can read out to another across a room. Usually empty.
        /// </summary>
        public static string LanHint = "";

        /// <summary>
        /// NO WIFI ROUTER AT ALL (owner 2026-10-17: "then the two ipads in a field"): the devices make their
        /// own link with Apple's peer-to-peer wifi instead of using an access point. Tens of metres, nothing
        /// to join and nothing to type. Ignored where there is no such thing (the editor, Windows).
        /// </summary>
        public static bool LanNoRouter;

        public static bool IsLanRoom(string room) => room != null && room.StartsWith(LanPrefix);
        /// <summary>The room name without the prefix: what the pilots are actually sharing.</summary>
        public static string LanRoomOf(string room) => IsLanRoom(room) ? room.Substring(LanPrefix.Length) : room;

        /// <summary>
        /// This installation's name on the network, kept so that leaving and rejoining a room does not look
        /// like two different pilots. Not an account and not a device identifier — a random number made once.
        /// </summary>
        public static string DeviceId
        {
            get
            {
                if (!string.IsNullOrEmpty(_deviceId)) return _deviceId;
                _deviceId = PlayerPrefs.GetString("net.deviceId", "");
                if (_deviceId.Length != 8)
                {
                    _deviceId = System.Guid.NewGuid().ToString("N").Substring(0, 8);
                    PlayerPrefs.SetString("net.deviceId", _deviceId);
                }
                return _deviceId;
            }
        }
        private static string _deviceId;

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
                    // A code splits one network into separate rooms; without one everybody shares "LAN".
                    MultiplayerMode.Lan => LanPrefix + (IsValidRoomCode(RoomCode) ? RoomCode : LanRoom),
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
            ("glider-2-33-like", "2-33-like Glider"), ("glider-eb29r-like", "EB29R-like Open Class"), ("glider-swift-s1-like", "Swift S-1-like"), ("c172-like", "172-like"), ("pa28-archer-like", "Archer-like"), ("cirrus-sr22-like", "SR22-like"), ("pa18-cub-like", "Super Cub-like"),
            ("pa18-bush-like", "Super Cub-like, Bushwheels"), ("pa18-floats-like", "Super Cub-like on Floats"),
            ("j3-cub-like", "J-3 Cub-like, 85 hp"), ("j3-cub-floats-like", "J-3 Cub-like on Floats"), ("dhc2-beaver-like", "Beaver-like"), ("dhc2-beaver-floats-like", "Beaver-like on Floats"), ("pa25-pawnee-like", "Pawnee-like"),
            ("decathlon-8kcab-like", "Decathlon-like"), ("extra-300-like", "Extra 300-like"), ("pitts-s2b-like", "Pitts S-2-like"), ("great-lakes-like", "Great Lakes-like"),
            ("jn4-jenny-like", "JN-4D-like Jenny"), ("stearman-pt17-like", "Stearman-like"), ("p51d-like", "P-51D-like"), ("f86-sabre-like", "F-86-like"),
            ("seminole-like", "Seminole-like"), ("dc3-like", "DC-3-like"), ("boeing-737-like", "737-like"), ("hughes-h4-like", "H-4-like"),
            ("cassutt-f1-like", "Cassutt-like Racer"), ("geebee-r2-like", "Gee Bee R-2-like"), ("glasair3-like", "Glasair III-like"),
            ("aircam-like", "AirCam-like"), ("aircam-amphib-like", "AirCam-like Amphib"),
        };

        /// <summary>Dogfight 1 v 1 (owner 2026-10-01): the opponent's aircraft and skill (0 easy, 1 moderate, 2 difficult).</summary>
        public static string DogfightOpponentId = "p51d-like";
        public static int DogfightSkill = 1;
        public static readonly string[] SkillNames = { "Easy", "Moderate", "Difficult" };

        public static readonly (string id, string name)[] Challenges =
        {
            (null, "Free flight"), ("event:race", "Air Racing"), ("event:stol", "STOL contest"), ("event:dust", "Crop dusting"), ("event:aerobox", "Aerobatic box"), ("event:combat", "Combat zone"), ("event:dogfight", "Dogfight 1 v 1"),
            ("a1c1-wings-level", "Wings level"), ("a1c2-best-glide", "Best glide"),
            ("a1c3-cardinal-turn", "Cardinal turn"), ("a1c4-stall-recover", "Stall recovery"),
            ("a1c10-headwind-landing", "Headwind landing"), ("a1c11-crosswind-landing", "Crosswind landing"),
        };

        /// <summary>FLYING LESSONS (owner 2026-09-15): the game flies every axis but the one being learned.</summary>
        public static readonly (string id, string name)[] Lessons =
        {
            ("lesson:straight", "Straight: wings level"),
            ("lesson:climb-level-descend", "Climb, level, descend"),
            ("lesson:glide-rear", "Glide: speed to fly"), ("lesson:glide-side", "Glide: speed to fly · side view"),
            ("lesson:climb-vy", "Climb: best rate (Vy)"), ("lesson:climb-vx", "Climb: best angle (Vx) · side view"),
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
            "lesson:climb-level-descend" => FlyingGame.Sim.Practice.PracticeKind.ClimbLevelDescend,
            "lesson:glide-rear" => FlyingGame.Sim.Practice.PracticeKind.GlideRear,
            "lesson:glide-side" => FlyingGame.Sim.Practice.PracticeKind.GlideSide,
            "lesson:climb-vy" => FlyingGame.Sim.Practice.PracticeKind.ClimbVyRear,
            "lesson:climb-vx" => FlyingGame.Sim.Practice.PracticeKind.ClimbVxSide,
            _ => null,
        };
        /// <summary>Wind choice for the practice: crosswind exercises offer Steady/Gusty/Shifting, approach and flare exercises
        /// Calm/Headwind/Gusty headwind/Tailwind/Gusty tailwind.</summary>
        public static FlyingGame.Sim.Practice.PracticeWind PracticeWindChoice = FlyingGame.Sim.Practice.PracticeWind.Steady;
        /// <summary>Lessons where the user picks the axes (bit 0 aileron, 1 elevator, 2 rudder, 3 throttle); −1 = the lesson's default.</summary>
        public static bool LessonHasAxisChoice(string id) => id is "practice:s-turns" or "practice:s-turns-test" or "lesson:climb-level-descend" or "lesson:glide-rear" or "lesson:glide-side" or "lesson:climb-vy" or "lesson:climb-vx";
        public static int LessonUserAxes = -1;
        /// <summary>Flap setting for the landing lessons (owner 2026-10-03): 0 up, 0.5 half, 1 full.</summary>
        public static float LessonFlaps = 0f;
        public static bool LessonHasFlapChoice(string id) => id is "practice:land-rudder" or "practice:land-aileron" or "practice:flare" or "practice:flare-side" or "practice:approach-side";
        public static int LessonDefaultAxes(string id) => id switch { "practice:s-turns" => 0b0100, "practice:s-turns-test" => 0b0101, "lesson:climb-level-descend" => 0b1010, "lesson:glide-rear" or "lesson:glide-side" => 0b0010, "lesson:climb-vy" => 0b0110, "lesson:climb-vx" => 0b0010, _ => 0 };
        /// <summary>Glide lessons take the along-runway winds (head / tail); the climb lessons fly calm.</summary>
        public static bool PracticeHasWindChoice(string id) => !PracticeIsAirwork(id) || id is "lesson:glide-rear" or "lesson:glide-side";
        public static bool PracticeIsAirwork(string id) => id is "lesson:straight" or "lesson:climb-level-descend" or "lesson:glide-rear" or "lesson:glide-side" or "lesson:climb-vy" or "lesson:climb-vx" or "practice:s-turns" or "practice:s-turns-test" or "practice:stall-side" or "practice:stall-rudder" or "practice:stall-elevator";
        /// <summary>Lessons that need an engine (a glider can't climb or hold level) — hidden for the gliders (regimen 2026-10-06:
        /// the climb lessons fed NaN controls to a glider).</summary>
        public static bool LessonNeedsEngine(string id) => id is "lesson:climb-level-descend" or "lesson:climb-vy" or "lesson:climb-vx";
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

        // ---- what the player set last time (owner 2026-10-28) ----
        //
        // "it should also remember how you last set it up instead of going back to defaults every time."
        // Everything on this list is written the moment it changes and read once at startup, so the defaults
        // above are only ever seen on a fresh install. The settings that already keep themselves are NOT on
        // this list and must not be added to it: clip length and clip instruments (ClipRecorder), the intercom
        // and the radio frequency (VoiceComms), the master volume (FlightAudio), the livery per type
        // (LiveryChoice), the badge publishing choice (SaveBadgePrefs) and the control feel (SaveFeel).

        /// <summary>Freeze the world while a menu or settings panel is open (owner 2026-10-28: "i keep crashing
        /// because i'm turning off bubbles etc"). Never applied in multiplayer — the other aeroplanes are real
        /// people and the relay keeps running, so there the panel is just a panel.</summary>
        public static bool PauseWhenMenuOpen = true;

        /// <summary>A settings panel is open over a flight that is already running (OPTIONS, and the landing page's
        /// own sub-pages reached from it). Held separately from <see cref="MenuOpen"/>, which means the landing
        /// page owns the whole screen.</summary>
        public static bool PanelPause;

        /// <summary>Other real pilots are in the air with us, so time is not ours to stop.</summary>
        public static bool MultiplayerLive => Multiplayer != MultiplayerMode.Solo && EffectiveRoom != null;

        /// <summary>Should the world be standing still this frame?</summary>
        public static bool WorldFrozen => MenuOpen || (PanelPause && PauseWhenMenuOpen && !MultiplayerLive);

        /// <summary>Put Time.timeScale where <see cref="WorldFrozen"/> says it belongs. A replay drives the clock
        /// itself, so it is left alone.</summary>
        public static void ApplyTimeScale()
        {
            if (ReplayActive) return;
            Time.timeScale = WorldFrozen ? 0f : SimSpeed;
        }

        private const string PrefVer = "set.v";
        private static bool _prefsLoaded;

        /// <summary>Restore the remembered settings. Safe to call more than once; only the first call reads.</summary>
        public static void LoadPrefs()
        {
            if (_prefsLoaded) return;
            _prefsLoaded = true;
            if (PlayerPrefs.GetInt(PrefVer, 0) < 1) return;   // fresh install: keep the defaults above
            LiftMarkers        = (Tri)PlayerPrefs.GetInt("set.liftMarkers", (int)LiftMarkers);
            BubblesOn          = GetBool("set.bubbles", BubblesOn);
            BubbleInstancing   = GetBool("set.bubbleInstancing", BubbleInstancing);
            BubbleStreaks      = (Streaks)PlayerPrefs.GetInt("set.bubbleStreaks", (int)BubbleStreaks);
            ShowFlightPath     = GetBool("set.flightPath", ShowFlightPath);
            ShowForceVectors   = GetBool("set.forceVectors", ShowForceVectors);
            FlightTestData     = GetBool("set.flightTestData", FlightTestData);
            ShowAtmosphereMap  = GetBool("set.atmosphereMap", ShowAtmosphereMap);
            Instruments        = (InstrumentMode)PlayerPrefs.GetInt("set.instruments", (int)Instruments);
            UseAirframeModels  = GetBool("set.airframeModels", UseAirframeModels);
            UseOwnModels       = GetBool("set.ownModels", UseOwnModels);
            Seat               = (CockpitSeat)PlayerPrefs.GetInt("set.seat", (int)Seat);
            ShowOtherBadges    = GetBool("set.otherBadges", ShowOtherBadges);
            ShowOwnBadge       = GetBool("set.ownBadge", ShowOwnBadge);
            PauseWhenMenuOpen  = GetBool("set.pauseOnMenu", PauseWhenMenuOpen);
            NavRadios.Shown    = GetBool("set.navRadios", NavRadios.Shown);
            // The landing page's own choices, so FLY is one tap after a restart.
            AircraftId         = PlayerPrefs.GetString("set.aircraft", AircraftId);
            TugId              = PlayerPrefs.GetString("set.tug", TugId);
            StartMode          = (Start)PlayerPrefs.GetInt("set.startMode", (int)StartMode);
            AirportIndex       = PlayerPrefs.GetInt("set.airport", AirportIndex);
            Runway             = (RunwayPick)PlayerPrefs.GetInt("set.runway", (int)Runway);
            WindFromDeg        = PlayerPrefs.GetFloat("set.windFrom", WindFromDeg);
            WindSpeedMs        = PlayerPrefs.GetFloat("set.windSpeed", WindSpeedMs);
            TurbulenceLevel    = PlayerPrefs.GetInt("set.turbulence", TurbulenceLevel);
            IsaDeviationC      = PlayerPrefs.GetFloat("set.isaDev", IsaDeviationC);
            ThermalScale       = PlayerPrefs.GetFloat("set.thermalScale", ThermalScale);
            SimSpeed           = PlayerPrefs.GetFloat("set.simSpeed", SimSpeed);
            if (!System.Array.Exists(Fleet, f => f.id == AircraftId)) AircraftId = Fleet[0].id;
            AirportIndex = Mathf.Clamp(AirportIndex, 0, WorldTerrain.Airports.Length - 1);
            SimSpeed = Mathf.Clamp(SimSpeed, SimSpeeds[SimSpeeds.Length - 1], 1f);
        }

        /// <summary>Write the remembered settings. Called from the panels on every change.</summary>
        public static void SavePrefs()
        {
            _prefsLoaded = true;                              // a save before any load must not be undone by one
            PlayerPrefs.SetInt(PrefVer, 1);
            PlayerPrefs.SetInt("set.liftMarkers", (int)LiftMarkers);
            SetBool("set.bubbles", BubblesOn);
            SetBool("set.bubbleInstancing", BubbleInstancing);
            PlayerPrefs.SetInt("set.bubbleStreaks", (int)BubbleStreaks);
            SetBool("set.flightPath", ShowFlightPath);
            SetBool("set.forceVectors", ShowForceVectors);
            SetBool("set.flightTestData", FlightTestData);
            SetBool("set.atmosphereMap", ShowAtmosphereMap);
            PlayerPrefs.SetInt("set.instruments", (int)Instruments);
            SetBool("set.airframeModels", UseAirframeModels);
            SetBool("set.ownModels", UseOwnModels);
            PlayerPrefs.SetInt("set.seat", (int)Seat);
            SetBool("set.otherBadges", ShowOtherBadges);
            SetBool("set.ownBadge", ShowOwnBadge);
            SetBool("set.pauseOnMenu", PauseWhenMenuOpen);
            SetBool("set.navRadios", NavRadios.Shown);
            PlayerPrefs.SetString("set.aircraft", AircraftId ?? "");
            PlayerPrefs.SetString("set.tug", TugId ?? "");
            PlayerPrefs.SetInt("set.startMode", (int)StartMode);
            PlayerPrefs.SetInt("set.airport", AirportIndex);
            PlayerPrefs.SetInt("set.runway", (int)Runway);
            PlayerPrefs.SetFloat("set.windFrom", WindFromDeg);
            PlayerPrefs.SetFloat("set.windSpeed", WindSpeedMs);
            PlayerPrefs.SetInt("set.turbulence", TurbulenceLevel);
            PlayerPrefs.SetFloat("set.isaDev", IsaDeviationC);
            PlayerPrefs.SetFloat("set.thermalScale", ThermalScale);
            PlayerPrefs.SetFloat("set.simSpeed", SimSpeed);
            PlayerPrefs.Save();
        }

        /// <summary>Put every remembered setting back to the shipped default (OPTIONS &gt; Game &gt; Reset).</summary>
        public static void ResetPrefs()
        {
            LiftMarkers = Tri.Off; BubblesOn = false; BubbleInstancing = true; BubbleStreaks = Streaks.Long;
            ShowFlightPath = false; ShowForceVectors = false; FlightTestData = false; ShowAtmosphereMap = false;
            Instruments = InstrumentMode.Hud; UseAirframeModels = true; UseOwnModels = false; Seat = CockpitSeat.Rear;
            ShowOtherBadges = true; ShowOwnBadge = true; PauseWhenMenuOpen = true; NavRadios.Shown = true;
            SavePrefs();
        }

        private static bool GetBool(string k, bool dflt) => PlayerPrefs.GetInt(k, dflt ? 1 : 0) == 1;
        private static void SetBool(string k, bool v) => PlayerPrefs.SetInt(k, v ? 1 : 0);

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

        // ---- regional air (owner 2026-10-27: "atmospheric conditions should vary by region across the landscape") ----
        /// <summary>
        /// Free flight flies in the REGIONAL atmosphere (configs/atmosphere/regional-atmosphere.json): the air differs
        /// from place to place, and the home field (the default start airport) sits in a dead-calm, standard-day zone.
        /// The menu's wind, temperature, turbulence and thermals become the air MASS the regions sit in. Off = the one
        /// uniform atmosphere everywhere, as before. Anything with its own weather — a challenge, a scored contest, a
        /// practice or lesson, an event — always flies uniform air (Atmosphere.Field = null): that is the override.
        /// </summary>
        public static bool RegionalWeather = true;
        /// <summary>Debug overlay of the regional air: regions, the calm zone, wind arrows, the lift/sink map (J).</summary>
        public static bool ShowAtmosphereMap = false;
        /// <summary>The regional turbulence multiplier for each menu turbulence step (Calm … Severe): the first step is the
        /// regions' own air, not none — the regions, not the menu, decide where the air is smooth.</summary>
        public static readonly float[] RegionalTurbulenceSteps = { 1f, 1.5f, 2.2f, 3f };
        private static FlyingGame.Core.AtmosphereField _regionalField;
        private static bool _regionalLoadFailed;
        /// <summary>The loaded regional field (null if the config is missing or broken — then the air is uniform).</summary>
        public static FlyingGame.Core.AtmosphereField RegionalField
        {
            get
            {
                if (_regionalField == null && !_regionalLoadFailed)
                {
                    try { _regionalField = new FlyingGame.Core.AtmosphereField(UnityAtmosphereLoader.Load()); }
                    catch (System.Exception e) { _regionalLoadFailed = true; Debug.LogWarning($"[Atmosphere] regional config not loaded ({e.Message}) — uniform air"); }
                }
                return _regionalField;
            }
        }
        /// <summary>Does this flight use the regional air? Free flight only, and only if the config loaded.</summary>
        public static bool UsesRegionalAir => RegionalWeather && ChallengeId == null && RegionalField != null;

        /// <summary>Push the weather choices into the atmosphere the wings and bubbles read.</summary>
        public static void ApplyWeather()
        {
            Atmosphere.IsaDeviationK = IsaDeviationC;
            Atmosphere.ThermalStrengthScale = ThermalScale;
            if (UsesRegionalAir)
            {
                FlyingGame.Core.AtmosphereField f = RegionalField;
                f.SynopticFromRad = WindFromDeg * Mathf.Deg2Rad;
                f.SynopticSpeedMs = WindSpeedMs;
                f.SynopticIsaDeviationK = IsaDeviationC;
                f.SynopticQnhPa = f.Config.Synoptic.QnhHpa * 100.0;
                f.TurbulenceScale = f.Config.Turbulence.Scale * RegionalTurbulenceSteps[Mathf.Clamp(TurbulenceLevel, 0, RegionalTurbulenceSteps.Length - 1)];
                f.ConvectionScale = ThermalScale;
                Atmosphere.Field = f;
                Atmosphere.SteadyWind = FlyingGame.Core.MathTypes.Vec3.Zero;   // (the field owns the wind; these are not read)
                Atmosphere.ActiveTurbulence = null;
                return;
            }
            Atmosphere.Field = null;
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
