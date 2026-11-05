using System.Linq;
using FlyingGame.Core.Landing;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The landing flow's one instance and its wiring to the game (docs/LANDING-V2.md). Builds the catalog from the
    /// game's own lists (SessionSettings.Fleet / Challenges / Lessons, WorldTerrain.Airports, the challenge configs),
    /// keeps the flow's memory in PlayerPrefs, and turns a selection into SessionSettings — for the live preview on
    /// every change (<see cref="ApplyPreview"/>) and for real on LAUNCH (<see cref="ApplyLaunch"/>).
    ///
    /// The store shares the keys the game already had, so nothing a pilot set before v2 is lost: a station's kg is
    /// SessionSettings' own per-type load (what the sim spawns with), "my seat" is SessionSettings' seat choice by id,
    /// and the first-run hero falls back to the aircraft the settings last saved.
    /// </summary>
    public static class LandingSession
    {
        private static LandingFlow _flow;
        public static LandingFlow Flow => _flow ??= new LandingFlow(BuildCatalog(), new PrefsStore(), LoadConfig);

        /// <summary>Drop the instance (tests, or the settings were reset): the next <see cref="Flow"/> reads the store again.</summary>
        public static void Reset() => _flow = null;

        /// <summary>The pilot's units (Standard | Metric) for anything outside the landing page — a HUD or an instrument
        /// label that grows a units choice reads THIS, the same PlayerPrefs key the page writes
        /// (<see cref="DisplayUnits.PrefKey"/>), and never a setting of its own. Display only.</summary>
        public static UnitSystem SavedUnits => _flow?.Units ?? DisplayUnits.Parse(PlayerPrefs.GetString(DisplayUnits.PrefKey, null));

        private static FlyingGame.Core.DataContracts.AircraftConfig LoadConfig(string id)
        {
            try { return UnityAircraftConfigLoader.LoadFromStreamingAssets(id); }
            catch (System.Exception e) { Debug.LogWarning($"[Landing] no config for {id}: {e.Message}"); return null; }
        }

        public static LandingCatalog BuildCatalog()
        {
            var airports = FlyingGame.Core.WorldTerrain.Airports.Select(a => (a.Name, a.Name, a.ElevationM * 3.28084));
            return new LandingCatalog(SessionSettings.Fleet, airports, SessionSettings.Challenges.Select(c => ((string)c.id, c.name)), SessionSettings.Lessons,
                challengeAircraft: ChallengeAircraft, isScored: FlyingGame.Sim.Ratings.Contests.IsScored, lessonNeedsEngine: SessionSettings.LessonNeedsEngine);
        }

        /// <summary>A challenge config's own aeroplane (events have none).</summary>
        private static string ChallengeAircraft(string id)
        {
            if (SessionSettings.IsEvent(id)) return null;
            try { var def = UnityChallengeLoader.Load(id); return string.IsNullOrEmpty(def?.AircraftId) ? null : def.AircraftId; }
            catch (System.Exception) { return null; }
        }

        // ---------------------------------------------------------------- selection → game

        /// <summary>What the 3-D preview needs to redraw the aeroplane where it will start: the aircraft and its gear,
        /// the airport and the viewpoint, the activity (events start in their own places), the load and my seat.</summary>
        public static void ApplyPreview(LandingFlow f)
        {
            SessionSettings.AircraftId = f.AircraftId;
            SessionSettings.Bushwheels = f.Gear.Bushwheels;
            SessionSettings.Skis = f.Gear.Skis;
            SessionSettings.AirportIndex = f.LocationIndex;
            SessionSettings.StartMode = (SessionSettings.Start)(int)f.Viewpoint;
            SessionSettings.ChallengeId = f.ActivityId;
            foreach (var s in f.Seats) if (f.Load?.Find(s.SeatId) != null) SessionSettings.SetLoadKg(f.AircraftId, s.SeatId, s.Kg);
            foreach (var st in f.Stations) SessionSettings.SetLoadKg(f.AircraftId, st.StationId, st.Kg);
            var me = f.Seats.FirstOrDefault(s => s.Occupant == Occupant.Me);
            if (me != null && f.Load?.Find(me.SeatId) != null && SessionSettings.SeatIdFor(f.AircraftId) != me.SeatId) SessionSettings.ChooseSeat(f.AircraftId, me.SeatId);
        }

        /// <summary>LAUNCH: the preview's settings plus the conditions (weather preset, time of day, reliability).</summary>
        public static void ApplyLaunch(LandingFlow f)
        {
            ApplyPreview(f);
            if (!f.ConditionsLocked && f.WeatherId != "custom")
            {
                var w = LandingCatalog.WeatherPresets.FirstOrDefault(p => p.id == f.WeatherId);
                if (w.id != null)
                {
                    SessionSettings.WindSpeedMs = (float)(w.windKt / 1.94384);
                    SessionSettings.TurbulenceLevel = w.turbulence;
                    SessionSettings.ThermalScale = (float)w.thermals;
                }
            }
            SessionSettings.TimeOfDayHour = (float)f.SunHour;
            SessionSettings.EngineUnreliability = f.Unreliability;
            TimeOfDayLighting.Apply(f.TimeOfDay);
            if (f.Seats.Any(s => s.Occupant == Occupant.InvitedPlayer))
                Debug.Log("[Landing] invited player requested (stub: the invite transport is not built yet): "
                          + string.Join(", ", f.Seats.Where(s => s.Occupant == Occupant.InvitedPlayer).Select(s => $"{s.SeatId}:{s.Role}")));
        }

        // ---------------------------------------------------------------- memory

        /// <summary>PlayerPrefs, under "landing.", except the keys the game already kept (see the class notes).</summary>
        private sealed class PrefsStore : ILandingStore
        {
            public string Get(string key)
            {
                if (TrySplit(key, "landing.kg.", out string ac, out string st))
                {
                    double? kg = SessionSettings.SavedLoadKg(ac, st);
                    return kg?.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                if (key.StartsWith("landing.me."))
                {
                    string id = SessionSettings.SeatIdFor(key.Substring("landing.me.".Length));
                    return string.IsNullOrEmpty(id) ? null : id;
                }
                if (PlayerPrefs.HasKey(key)) return PlayerPrefs.GetString(key);
                // First v2 run on a device that has flown before: the hero is the aeroplane the settings last saved.
                if (key == "landing.lastFlown" && PlayerPrefs.HasKey("set.aircraft")) return PlayerPrefs.GetString("set.aircraft");
                return null;
            }

            public void Set(string key, string value)
            {
                if (TrySplit(key, "landing.kg.", out string ac, out string st))
                {
                    if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double kg))
                        SessionSettings.SetLoadKg(ac, st, kg);
                    return;
                }
                if (key.StartsWith("landing.me.")) { SessionSettings.ChooseSeat(key.Substring("landing.me.".Length), value); return; }
                PlayerPrefs.SetString(key, value);
                PlayerPrefs.Save();
            }

            /// <summary>"landing.kg.&lt;aircraft&gt;.&lt;station&gt;": aircraft ids have no dots, station ids neither.</summary>
            private static bool TrySplit(string key, string prefix, out string a, out string b)
            {
                a = b = null;
                if (!key.StartsWith(prefix)) return false;
                string rest = key.Substring(prefix.Length);
                int dot = rest.IndexOf('.');
                if (dot <= 0) return false;
                a = rest.Substring(0, dot); b = rest.Substring(dot + 1);
                return true;
            }
        }
    }
}
