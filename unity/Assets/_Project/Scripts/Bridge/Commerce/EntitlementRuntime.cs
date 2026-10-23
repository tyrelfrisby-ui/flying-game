using System;
using System.Collections.Generic;
using System.IO;
using FlyingGame.Sim.Commerce;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlyingGame.Bridge.Commerce
{
    /// <summary>
    /// Holds the answer to "what may this person fly, and may they go online" for the whole session, and is
    /// the only place the rest of the game should ask.
    ///
    /// The owner's model (2026-10-15), and where each part lives:
    ///  - FREE: the trainer glider and the C-172, plus the lessons and challenges that start you earning.
    ///    Everything else is earned with points or bought outright. <see cref="Content"/> reports
    ///    <see cref="AccessLevel.Free"/>; the points economy is progression, not commerce, and is not here.
    ///  - TRIAL: fourteen days of everything, single player only. Started by buying a free non-consumable, so
    ///    Apple holds the start date and it survives a reinstall, a new device and a wound-back clock.
    ///  - UNLOCK: a one-time purchase that opens everything for good and includes the first thirty days of
    ///    online flying.
    ///  - ONLINE: the pass, monthly or yearly, one subscription group. Needed by everyone who flies online,
    ///    including members entitled to the content through an external provider. UNLIMITED — the owner
    ///    dropped the hour cap and the top-up (2026-10-16), so <see cref="AirtimeMeter"/> counts hours for
    ///    the cost check and nothing here ever refuses a pilot over them.
    ///
    /// Each resolver keeps its own high-water clock, so a flight with no signal behaves exactly as if online
    /// and winding the device clock about cannot buy anybody anything.
    ///
    /// Product identifiers and the rules come from StreamingAssets/commerce.json, never from source, and
    /// PRICES are never stored anywhere in this project — they are read from the store at runtime, localised,
    /// which is both the correct way and the only way that stays right when a price changes.
    /// </summary>
    public sealed class EntitlementRuntime : MonoBehaviour
    {
        public static EntitlementRuntime Instance { get; private set; }

        /// <summary>How often the StoreKit snapshot is read. There is nothing to gain from per-frame.</summary>
        public float PollSeconds = 1.0f;

        private readonly EntitlementResolver _passResolver = new();
        private readonly EntitlementResolver _externalResolver = new();
        private readonly ContentAccessResolver _contentResolver = new();
        private readonly MultiplayerAccessResolver _onlineResolver = new();
        private readonly AirtimeMeter _airtime = new();
        private readonly List<EntitlementGrant> _grants = new();
        private float _pollT;
        private bool _started;

        private string _trialId = "", _unlockId = "";
        private readonly List<string> _passIds = new();

        /// <summary>What they may fly.</summary>
        public ContentAccess Content { get; private set; }

        /// <summary>Whether they may go online. The whole question: there is no hours question any more.</summary>
        public MultiplayerAccess Online { get; private set; }

        /// <summary>Connected online hours, counted on this device for the cost check only.</summary>
        public AirtimeMeter Airtime => _airtime;

        /// <summary>What the store screen should offer, with the store's own localised prices.</summary>
        public StoreOffer TrialOffer => StoreKitBridge.Offer(_trialId);
        public StoreOffer UnlockOffer => StoreKitBridge.Offer(_unlockId);
        public IEnumerable<string> PassProductIds => _passIds;

        /// <summary>Whether <see cref="CanJoinMultiplayer"/>'s answer is acted on. Config, and FALSE until the
        /// products exist in App Store Connect and the relay asks for a join token: switching it on before
        /// then would refuse everybody, because there would be no pass in the world to hold. The hours are
        /// counted and the answer is computed either way — only the refusal waits.</summary>
        public bool GateMultiplayer { get; private set; }

        /// <summary>Links the paywall must show. Empty until the pages are published.</summary>
        public string TermsUrl { get; private set; } = "";
        public string PrivacyUrl { get; private set; } = "";

        private const string PrefAirtime = "commerce.airtime";
        private const string PrefContentHighWater = "commerce.highwater.content";
        private const string PrefPassHighWater = "commerce.highwater.pass";

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            LoadConfig();
            LoadPersisted();
            var ids = new List<string>();
            if (!string.IsNullOrEmpty(_trialId)) ids.Add(_trialId);
            if (!string.IsNullOrEmpty(_unlockId)) ids.Add(_unlockId);
            ids.AddRange(_passIds);
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
                _trialId = o.Value<string>("trialProductId") ?? "";
                _unlockId = o.Value<string>("unlockProductId") ?? "";
                if (o["multiplayerProductIds"] is JArray pass)
                    foreach (var x in pass) { string id = (string)x; if (!string.IsNullOrEmpty(id)) _passIds.Add(id); }
                // The rules travel with the identifiers, so changing one is a config edit and not a build.
                double trialDays = o.Value<double?>("trialDays") ?? 0;
                if (trialDays > 0) _contentResolver.TrialDays = trialDays;
                double includedDays = o.Value<double?>("multiplayerDaysWithUnlock") ?? 0;
                if (includedDays > 0) _onlineResolver.IncludedDaysWithUnlock = includedDays;
                GateMultiplayer = o.Value<bool?>("gateMultiplayer") ?? false;
                TermsUrl = o.Value<string>("termsUrl") ?? "";
                PrivacyUrl = o.Value<string>("privacyUrl") ?? "";
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
                // Seed each resolver's high-water mark, so a clock wound back cannot extend a trial or a pass.
                DateTime chw = Utc(PlayerPrefs.GetString(PrefContentHighWater, ""));
                if (chw > DateTime.MinValue) _contentResolver.Resolve(false, DateTime.MinValue, default, DateTime.MinValue, chw);
                DateTime phw = Utc(PlayerPrefs.GetString(PrefPassHighWater, ""));
                if (phw > DateTime.MinValue) _onlineResolver.Resolve(default, DateTime.MinValue, phw);
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
            PlayerPrefs.SetString(PrefPassHighWater, _onlineResolver.HighWaterUtc.ToString("o"));
            PlayerPrefs.Save();
        }

        /// <summary>There are no consumable products any more — the hours top-up went with the cap. If one
        /// ever arrives anyway, from a product created in App Store Connect ahead of the code that honours it,
        /// finish it and say so in the log: an unfinished transaction is re-delivered forever and App Review
        /// treats a queue that never drains as a bug, which it is.</summary>
        private void FinishStrayConsumables()
        {
            var pend = StoreKitBridge.PendingConsumables;
            for (int i = 0; i < pend.Count; i++)
            {
                var (productId, txnId) = pend[i];
                Debug.LogWarning($"Consumable {productId} arrived and nothing here grants anything for it — finishing it.");
                StoreKitBridge.Finish(txnId);
            }
        }

        /// <summary>A verified, non-revoked, not-family-shared entitlement for this product, if there is one.
        /// Family Sharing is off for every product here, so a shared copy is a thing that should not exist;
        /// ignoring it costs nothing and "cannot happen" is not a check.</summary>
        private bool TryGetOwned(string productId, out StoreEntitlement found)
        {
            foreach (var e in StoreKitBridge.Entitlements)
            {
                if (e.Id != productId || e.Revoked) continue;
                if (e.FamilyShared) { Debug.LogWarning($"Ignoring a family-shared {productId}: Family Sharing is off for it."); continue; }
                found = e;
                return true;
            }
            found = default;
            return false;
        }

        private void Resolve()
        {
            ContentAccess beforeContent = Content;
            MultiplayerAccess beforeOnline = Online;
            DateTime now = DateTime.UtcNow;

            // ---- the one-time unlock and the trial, both non-consumables -------------------------------
            bool unlockOwned = TryGetOwned(_unlockId, out var unlock);
            DateTime unlockBought = unlockOwned ? unlock.PurchasedUtc : DateTime.MinValue;
            DateTime trialStarted = TryGetOwned(_trialId, out var trial) ? trial.PurchasedUtc : DateTime.MinValue;

            // ---- an external membership, when one is linked --------------------------------------------
            // The provider handshake is specified in docs/EXTERNAL-ENTITLEMENT.md and does not exist yet; the
            // stub stands in for it so the resolution rules and the offline behaviour can be exercised end to
            // end. It is compiled out of a release build. It grants the UNLOCK's content for as long as the
            // membership lasts and nothing more: the online pass is ours and everyone buys it (owner
            // 2026-10-15).
            _grants.Clear();
#if AERO_EXTERNAL_STUB
            if (ExternalEntitlementStub.TryRead(out var ext)) _grants.Add(ext);
#endif
            EntitlementResolution external = _externalResolver.Resolve(_grants, now);

            // A comp grant (press, testers, the owner). Local, because its whole purpose is to work when
            // nothing else does. Forging one costs us one unlock and costs the forger nothing, which is an
            // acceptable trade for a handful of grants; if that ever stops being true it wants a signature
            // checked against a public key compiled into the build.
            DateTime comp = Utc(PlayerPrefs.GetString("commerce.comp", ""));

            Content = _contentResolver.Resolve(unlockOwned, trialStarted, external, comp, now);

            // ---- the online pass: monthly or yearly, one group ----------------------------------------
            _grants.Clear();
            foreach (string id in _passIds)
                if (TryGetOwned(id, out var p))
                    _grants.Add(new EntitlementGrant(EntitlementSource.AppStore, p.ExpiresUtc, now));
            DateTime passComp = Utc(PlayerPrefs.GetString("commerce.comp.multiplayer", ""));
            if (passComp > DateTime.MinValue) _grants.Add(new EntitlementGrant(EntitlementSource.Comp, passComp, now));
            EntitlementResolution pass = _passResolver.Resolve(_grants, now);

            Online = _onlineResolver.Resolve(pass, unlockBought, now);

            if (beforeContent.Level != Content.Level || beforeContent.Source != Content.Source
                || beforeOnline.Permitted != Online.Permitted || beforeOnline.Source != Online.Source) Persist();
        }

        /// <summary>The whole question for a multiplayer join: may they go online. The pass is unlimited, so
        /// a pilot who holds one is never turned away for any reason this layer knows about.
        /// <paramref name="reason"/> is shown to the pilot verbatim when the answer is no.</summary>
        public bool CanJoinMultiplayer(out string reason)
        {
            reason = Online.Permitted ? "" : Online.Reason;
            return Online.Permitted;
        }

        /// <summary>Apple's signed transaction for whatever permits this pilot to fly online — the pass, or
        /// the unlock during its included month — to be shown to the relay at join so it can verify against
        /// Apple's keys and hand back a short-lived join token. Empty when there is nothing to show. A bearer
        /// token: TLS only, never logged, never written to disk.</summary>
        public string MultiplayerJws()
        {
            foreach (string id in _passIds)
                if (TryGetOwned(id, out var p) && !string.IsNullOrEmpty(p.Jws)) return p.Jws;
            if (Online.Permitted && TryGetOwned(_unlockId, out var u)) return u.Jws;
            return "";
        }

        /// <summary>Joined a room. Starts counting this pilot's online time.</summary>
        public void BeginMultiplayerSession() => _airtime.BeginSession(DateTime.UtcNow);

        /// <summary>Left the room, for any reason.</summary>
        public void EndMultiplayerSession() { _airtime.EndSession(); Persist(); }

        /// <summary>A step of online time, reported by the transport. Counted, never judged: there is no
        /// return value because there is no answer that would end a flight.</summary>
        public void RecordMultiplayerTime(float dt) => _airtime.Record(dt);

        private void OnApplicationPause(bool paused) { if (paused) Persist(); }
        private void OnApplicationQuit() => Persist();
    }
}
