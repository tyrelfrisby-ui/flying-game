using System;
using System.Collections.Generic;
using System.IO;
using FlyingGame.Sim.Commerce;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlyingGame.Bridge.Commerce
{
    /// <summary>
    /// Holds the answer to "may this person fly the paid content, and may they go online" for the whole
    /// session, and is the only place the rest of the game should ask.
    ///
    /// There are two separate questions and two separate subscriptions, resolved independently:
    ///  - CONTENT (<see cref="Content"/>): the airplanes, the syllabus, the recorder. Granted by the App
    ///    Store subscription, an external membership if one is linked, or a comp. Forgives a dropped
    ///    connection for <see cref="EntitlementResolver.GraceDays"/> — a network error must never cost
    ///    somebody a flight they have paid for.
    ///  - ONLINE (<see cref="Pass"/>): the Multiplayer Pass, its own subscription, needed by everyone who
    ///    flies online including members entitled to the content through an external provider (owner
    ///    2026-10-15). No grace: going online needs a network, so if the relay is reachable the store is too.
    ///
    /// Each resolution is cached with its own high-water clock, so a flight with no signal behaves exactly as
    /// if online and winding the device clock about cannot buy time.
    ///
    /// Product identifiers come from StreamingAssets/commerce.json, never from source, and PRICES are never
    /// stored anywhere in this project — they are read from the store at runtime, localised, which is both
    /// the correct way and the only way that stays right when a price changes.
    /// </summary>
    public sealed class EntitlementRuntime : MonoBehaviour
    {
        public static EntitlementRuntime Instance { get; private set; }

        /// <summary>How often the StoreKit snapshot is read. There is nothing to gain from per-frame.</summary>
        public float PollSeconds = 1.0f;

        // One resolver per product: the same rules, asked twice, so neither subscription can mask the other.
        private readonly EntitlementResolver _contentResolver = new();
        private readonly EntitlementResolver _passResolver = new();
        private readonly AirtimeMeter _airtime = new();
        private readonly List<EntitlementGrant> _grants = new();
        private float _pollT;
        private bool _started;

        private string _subscriptionId = "";
        private string _multiplayerId = "";

        /// <summary>May they fly the paid content (grace included).</summary>
        public EntitlementResolution Content { get; private set; }

        /// <summary>May they go online right now (confirmed only).</summary>
        public EntitlementResolution Pass { get; private set; }

        /// <summary>Connected hours, for our own cost check. It gates nothing — see <see cref="AirtimeMeter"/>.</summary>
        public AirtimeMeter Airtime => _airtime;

        /// <summary>What the paywall should offer, with the store's own localised prices.</summary>
        public StoreOffer SubscriptionOffer => StoreKitBridge.Offer(_subscriptionId);
        public StoreOffer MultiplayerOffer => StoreKitBridge.Offer(_multiplayerId);

        private const string PrefAirtime = "commerce.airtime";
        private const string PrefContentHighWater = "commerce.highwater";
        private const string PrefPassHighWater = "commerce.highwater.pass";

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            LoadConfig();
            LoadPersisted();
            var ids = new List<string>();
            if (!string.IsNullOrEmpty(_subscriptionId)) ids.Add(_subscriptionId);
            if (!string.IsNullOrEmpty(_multiplayerId)) ids.Add(_multiplayerId);
            if (ids.Count > 0) { StoreKitBridge.Start(ids); _started = true; }
            Resolve();
        }

        private void Update()
        {
            _airtime.Tick(DateTime.UtcNow);
            if (!_started) return;
            _pollT += Time.unscaledDeltaTime;
            if (_pollT < PollSeconds) return;
            _pollT = 0f;
            StoreKitBridge.Poll();
            FinishStrayConsumables();
            Resolve();
        }

        private void LoadConfig()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "commerce.json");
            try
            {
                if (!File.Exists(path)) { Debug.LogWarning("commerce.json missing — nothing is for sale this run."); return; }
                var o = JObject.Parse(File.ReadAllText(path));
                _subscriptionId = o.Value<string>("subscriptionProductId") ?? "";
                _multiplayerId = o.Value<string>("multiplayerProductId") ?? "";
            }
            catch (Exception e) { Debug.LogWarning($"commerce.json unreadable: {e.Message}"); }
        }

        private void LoadPersisted()
        {
            try
            {
                string s = PlayerPrefs.GetString(PrefAirtime, "");
                if (!string.IsNullOrEmpty(s))
                {
                    var o = JObject.Parse(s);
                    _airtime.Load(new AirtimeMeter.Snapshot(
                        o.Value<double?>("per") ?? 0, o.Value<int?>("pern") ?? 0,
                        o.Value<double?>("life") ?? 0, o.Value<int?>("lifen") ?? 0,
                        Utc(o.Value<string>("end")), o.Value<int?>("days") ?? 0, Utc(o.Value<string>("hw"))));
                }
                // Seed each resolver's high-water mark, so a clock wound back cannot buy either subscription time.
                string hw = PlayerPrefs.GetString(PrefContentHighWater, "");
                if (!string.IsNullOrEmpty(hw)) _contentResolver.Resolve(Array.Empty<EntitlementGrant>(), Utc(hw));
                string phw = PlayerPrefs.GetString(PrefPassHighWater, "");
                if (!string.IsNullOrEmpty(phw)) _passResolver.Resolve(Array.Empty<EntitlementGrant>(), Utc(phw));
            }
            catch (Exception e) { Debug.LogWarning($"commerce state unreadable, starting fresh: {e.Message}"); }
        }

        private static DateTime Utc(string s)
        {
            if (string.IsNullOrEmpty(s)) return DateTime.MinValue;
            const System.Globalization.DateTimeStyles st =
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal;
            return DateTime.TryParse(s, null, st, out var t) ? t : DateTime.MinValue;
        }

        private void Persist()
        {
            var s = _airtime.Save();
            PlayerPrefs.SetString(PrefAirtime, new JObject
            {
                ["per"] = s.PeriodSeconds, ["pern"] = s.PeriodSessions,
                ["life"] = s.LifetimeSeconds, ["lifen"] = s.LifetimeSessions,
                ["end"] = s.PeriodEndUtc.ToString("o"), ["days"] = s.PeriodDays, ["hw"] = s.HighWaterUtc.ToString("o"),
            }.ToString(Newtonsoft.Json.Formatting.None));
            PlayerPrefs.SetString(PrefContentHighWater, _contentResolver.HighWaterUtc.ToString("o"));
            PlayerPrefs.SetString(PrefPassHighWater, _passResolver.HighWaterUtc.ToString("o"));
            PlayerPrefs.Save();
        }

        /// <summary>Nothing in the app is sold as a consumable — both products are subscriptions — so a
        /// pending consumable means something unexpected arrived (a leftover from an older build, a sandbox
        /// experiment). Finish it rather than leaving it unfinished for ever, and say so in the log.</summary>
        private void FinishStrayConsumables()
        {
            var pend = StoreKitBridge.PendingConsumables;
            if (pend.Count == 0) return;
            for (int i = 0; i < pend.Count; i++)
            {
                var (productId, txnId) = pend[i];
                Debug.LogWarning($"Unexpected consumable {productId} — nothing is sold as a consumable; finishing it.");
                StoreKitBridge.Finish(txnId);
            }
        }

        private void Resolve()
        {
            EntitlementResolution beforeContent = Content, beforePass = Pass;

            // ---- content: the subscription, an external membership, or a comp -------------------------
            _grants.Clear();
            foreach (var e in StoreKitBridge.Entitlements)
            {
                if (e.Id != _subscriptionId) continue;
                _grants.Add(new EntitlementGrant(EntitlementSource.AppStore, e.ExpiresUtc, DateTime.UtcNow, e.Revoked));
            }

            // An external membership, when one is linked. The provider handshake is specified in
            // docs/EXTERNAL-ENTITLEMENT.md and does not exist yet; the stub stands in for it so the
            // resolution rules and the offline behaviour can be exercised end to end. It is compiled out
            // of a release build. It grants the CONTENT only: the Multiplayer Pass is ours and is bought
            // from us by everyone (owner 2026-10-15).
#if AERO_EXTERNAL_STUB
            if (ExternalEntitlementStub.TryRead(out var ext)) _grants.Add(ext);
#endif

            // A comp grant (press, testers, the owner). Local, because its whole purpose is to work when
            // nothing else does. Forging one costs us a subscription and costs the forger nothing, which
            // is an acceptable trade for a handful of grants; if that ever stops being true it wants a
            // signature checked against a public key compiled into the build.
            DateTime comp = Utc(PlayerPrefs.GetString("commerce.comp", ""));
            if (comp > DateTime.MinValue) _grants.Add(new EntitlementGrant(EntitlementSource.Comp, comp, DateTime.UtcNow));

            Content = _contentResolver.Resolve(_grants, DateTime.UtcNow);

            // ---- the Multiplayer Pass: its own subscription, plus the same comp ------------------------
            _grants.Clear();
            foreach (var e in StoreKitBridge.Entitlements)
            {
                if (e.Id != _multiplayerId) continue;
                _grants.Add(new EntitlementGrant(EntitlementSource.AppStore, e.ExpiresUtc, DateTime.UtcNow, e.Revoked));
            }
            DateTime passComp = Utc(PlayerPrefs.GetString("commerce.comp.multiplayer", ""));
            if (passComp > DateTime.MinValue) _grants.Add(new EntitlementGrant(EntitlementSource.Comp, passComp, DateTime.UtcNow));

            Pass = _passResolver.Resolve(_grants, DateTime.UtcNow);

            if (beforeContent.Trust != Content.Trust || beforeContent.Source != Content.Source
                || beforePass.Trust != Pass.Trust || beforePass.Source != Pass.Source) Persist();
        }

        /// <summary>The whole question for a multiplayer join: does this pilot hold the Pass, confirmed now.
        /// <paramref name="reason"/> is shown to the pilot verbatim when the answer is no. Airtime is counted
        /// but never consulted — nobody is refused a join for having flown too much.</summary>
        public bool CanJoinMultiplayer(out string reason)
        {
            if (Pass.Confirmed) { reason = ""; return true; }
            reason = Pass.Trust switch
            {
                EntitlementTrust.Grace => "Connect to the internet once so the App Store can confirm your Multiplayer Pass.",
                EntitlementTrust.Expired => "Your Multiplayer Pass has lapsed. Single player and local flying still work.",
                _ => "Flying online needs the Multiplayer Pass. Single player and local flying do not.",
            };
            return false;
        }

        /// <summary>Apple's signed transaction for the Pass, to be shown to the relay at join so it can
        /// verify the subscription against Apple's keys and hand back a short-lived join token. Empty when
        /// there is nothing to show. A bearer token: TLS only, never logged, never written to disk.</summary>
        public string MultiplayerPassJws()
        {
            foreach (var e in StoreKitBridge.Entitlements)
                if (e.Id == _multiplayerId && !e.Revoked) return e.Jws;
            return "";
        }

        /// <summary>Joined a room. Starts counting this pilot's connected time.</summary>
        public void BeginMultiplayerSession() => _airtime.BeginSession(DateTime.UtcNow);

        /// <summary>Left the room, for any reason.</summary>
        public void EndMultiplayerSession() { _airtime.EndSession(); Persist(); }

        /// <summary>A step of connected time, reported by the transport. Returns nothing to check: airtime
        /// is accounting, not a limit.</summary>
        public void RecordMultiplayerTime(float dt) => _airtime.Record(dt);

        private void OnApplicationPause(bool paused) { if (paused) Persist(); }
        private void OnApplicationQuit() => Persist();
    }
}
