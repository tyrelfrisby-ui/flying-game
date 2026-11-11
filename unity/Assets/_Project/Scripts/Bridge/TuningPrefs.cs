using FlyingGame.Core.DataContracts;
using FlyingGame.Core.Tuning;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// PLAYERPREFS BEHIND THE DEVELOPER PAGE'S TUNING, and the one call that puts a pilot's numbers back on a
    /// freshly loaded aeroplane.
    ///
    /// THE PART THAT IS EASY TO GET WRONG, written down because getting it wrong is silent: the aircraft config is
    /// re-read from StreamingAssets on EVERY spawn — FlightSimDriver.ResetFlight reloads the JSON rather than
    /// keeping the object — so a knob written onto the live config is thrown away by the next FLY unless it is
    /// applied again here. A page that saved a value but did not re-apply it would show the right number while the
    /// aeroplane flew the shipped one, which is the worst failure available to us: it would quietly poison every
    /// flight report the beta test pilots send back.
    ///
    /// So <see cref="ApplyTo"/> is called at the spawn point, beside SessionSettings.ApplyFeel, which is the
    /// existing precedent for exactly this shape. It is solved once, in the registry, for every knob rather than
    /// per knob.
    /// </summary>
    public static class TuningPrefs
    {
        private sealed class PrefsStore : ITuningStore
        {
            public static readonly PrefsStore Instance = new PrefsStore();
            public string Get(string key) => PlayerPrefs.GetString(key, "");
            public void Set(string key, string value) { PlayerPrefs.SetString(key, value); PlayerPrefs.Save(); }
        }

        private static bool _wired;

        /// <summary>Hand Core the store, once. Cheap and idempotent, so it can sit on the spawn path.</summary>
        public static void Ensure()
        {
            if (_wired) return;
            _wired = true;
            TuningSession.UseStore(PrefsStore.Instance);
        }

        /// <summary>
        /// Capture this aeroplane's shipped defaults and put the pilot's saved knobs back on it. Call on a freshly
        /// loaded config BEFORE the Aircraft is built from it: the g limits and the tyre are read into the airframe
        /// at construction, so applying afterwards would leave those a spawn behind.
        /// </summary>
        public static void ApplyTo(AircraftConfig config)
        {
            if (config == null) return;
            Ensure();
            TuningSession.Apply(config);
        }
    }
}
