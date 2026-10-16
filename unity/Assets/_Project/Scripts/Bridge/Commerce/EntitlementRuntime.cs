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
    /// Providers are consulted in order and the longest-lived grant wins (<see cref="EntitlementResolver"/>):
    /// the App Store subscription, an external membership if one is linked, and a comp. The resolution is
    /// cached to disk with its own high-water clock, so a flight with no signal behaves exactly as if online
    /// and winding the device clock about cannot buy time.
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

        private readonly EntitlementResolver _resolver = new();
        private readonly DataMeter _meter = new();
        private readonly List<EntitlementGrant> _grants = new();
        private float _pollT;
        private bool _started;

        private string _subscriptionId = "";
        private readonly List<string> _dataProductIds = new();
        private readonly Dictionary<string, long> _dataBytesByProduct = new();

        public EntitlementResolution Current { get; private set; }
        public DataMeter Meter => _meter;

        /// <summary>What the paywall should offer for the subscription, with the store's own price.</summary>
        public StoreOffer SubscriptionOffer => StoreKitBridge.Offer(_subscriptionId);
        public IEnumerable<string> DataProductIds => _dataProductIds;

        private const string PrefMeter = "commerce.meter";
        private const string PrefHighWater = "commerce.highwater";

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            LoadConfig();
            LoadPersisted();
            var ids = new List<string>(_dataProductIds);
            if (!string.IsNullOrEmpty(_subscriptionId)) ids.Insert(0, _subscriptionId);
            if (ids.Count > 0) { StoreKitBridge.Start(ids); _started = true; }
            Resolve();
        }

        private void Update()
        {
            _meter.Tick(DateTime.UtcNow);
            if (!_started) return;
            _pollT += Time.unscaledDeltaTime;
            if (_pollT < PollSeconds) return;
            _pollT = 0f;
            StoreKitBridge.Poll();
            CreditPendingPurchases();
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
                if (o["dataProducts"] is JArray arr)
                    foreach (var x in arr)
                    {
                        string id = x.Value<string>("id");
                        long mb = x.Value<long?>("megabytes") ?? 0;
                        if (string.IsNullOrEmpty(id) || mb <= 0) continue;
                        _dataProductIds.Add(id);
                        _dataBytesByProduct[id] = mb * 1024L * 1024L;
                    }
            }
            catch (Exception e) { Debug.LogWarning($"commerce.json unreadable: {e.Message}"); }
        }

        private void LoadPersisted()
        {
            try
            {
                string s = PlayerPrefs.GetString(PrefMeter, "");
                if (!string.IsNullOrEmpty(s))
                {
                    var o = JObject.Parse(s);
                    _meter.Load(new DataMeter.Snapshot(
                        o.Value<long?>("bal") ?? 0, o.Value<long?>("alw") ?? 0, o.Value<long?>("rem") ?? 0,
                        o.Value<long?>("life") ?? 0,
                        DateTime.Parse(o.Value<string>("end") ?? DateTime.MinValue.ToString("o"), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal),
                        DateTime.Parse(o.Value<string>("hw") ?? DateTime.MinValue.ToString("o"), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal),
                        o.Value<int?>("days") ?? 0));
                }
                string hw = PlayerPrefs.GetString(PrefHighWater, "");
                if (!string.IsNullOrEmpty(hw) && DateTime.TryParse(hw, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var t))
                    _resolver.Resolve(Array.Empty<EntitlementGrant>(), t);   // seeds the high-water mark
            }
            catch (Exception e) { Debug.LogWarning($"commerce state unreadable, starting fresh: {e.Message}"); }
        }

        private void Persist()
        {
            var s = _meter.Save();
            PlayerPrefs.SetString(PrefMeter, new JObject
            {
                ["bal"] = s.Balance, ["alw"] = s.PlanAllowance, ["rem"] = s.PlanRemaining, ["life"] = s.Lifetime,
                ["end"] = s.PlanPeriodEndUtc.ToString("o"), ["hw"] = s.HighWaterUtc.ToString("o"), ["days"] = s.PlanPeriodDays,
            }.ToString(Newtonsoft.Json.Formatting.None));
            PlayerPrefs.SetString(PrefHighWater, _resolver.HighWaterUtc.ToString("o"));
            PlayerPrefs.Save();
        }

        /// <summary>A data purchase has arrived: credit it, then tell StoreKit it is done. In that order —
        /// if the app dies in between, the transaction is still unfinished and comes back next launch, so the
        /// pilot gets the bytes they bought. The reverse order would lose them.</summary>
        private void CreditPendingPurchases()
        {
            var pend = StoreKitBridge.PendingConsumables;
            if (pend.Count == 0) return;
            for (int i = 0; i < pend.Count; i++)
            {
                var (productId, txnId) = pend[i];
                if (_dataBytesByProduct.TryGetValue(productId, out long bytes))
                {
                    _meter.Credit(bytes);
                    Persist();
                    Debug.Log($"Credited {bytes / (1024 * 1024)} MB of multiplayer data ({productId}).");
                }
                StoreKitBridge.Finish(txnId);
            }
        }

        private void Resolve()
        {
            _grants.Clear();

            // 1. The App Store subscription.
            foreach (var e in StoreKitBridge.Entitlements)
            {
                if (e.Id != _subscriptionId) continue;
                _grants.Add(new EntitlementGrant(EntitlementSource.AppStore, e.ExpiresUtc, DateTime.UtcNow, e.Revoked));
            }

            // 2. An external membership, when one is linked. The provider handshake is specified in
            //    docs/EXTERNAL-ENTITLEMENT.md and does not exist yet; the stub stands in for it so the
            //    resolution rules and the offline behaviour can be exercised end to end. It is compiled out
            //    of a release build.
#if AERO_EXTERNAL_STUB
            if (ExternalEntitlementStub.TryRead(out var ext)) _grants.Add(ext);
#endif

            // 3. A comp grant (press, testers, the owner). Local, because its whole purpose is to work when
            //    nothing else does. Forging one costs us a subscription and costs the forger nothing, which
            //    is an acceptable trade for a handful of grants; if that ever stops being true it wants a
            //    signature checked against a public key compiled into the build.
            string comp = PlayerPrefs.GetString("commerce.comp", "");
            if (!string.IsNullOrEmpty(comp) && DateTime.TryParse(comp, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var compExp))
                _grants.Add(new EntitlementGrant(EntitlementSource.Comp, compExp, DateTime.UtcNow));

            EntitlementResolution before = Current;
            Current = _resolver.Resolve(_grants, DateTime.UtcNow);
            if (before.Trust != Current.Trust || before.Source != Current.Source) Persist();
        }

        /// <summary>The whole question for a multiplayer join: entitled, and enough data to be worth it.
        /// <paramref name="reason"/> is shown to the pilot verbatim when the answer is no.</summary>
        public bool CanJoinMultiplayer(out string reason)
        {
            if (!Current.MultiplayerEntitled)
            {
                reason = Current.Trust == EntitlementTrust.Grace
                    ? "Multiplayer needs a live subscription check — connect to the internet once to go online."
                    : Current.Reason;
                return false;
            }
            return _meter.CanJoin(out reason);
        }

        /// <summary>Called by the transport with what it actually moved. Returns false when the data has run
        /// out and the session should end.</summary>
        public bool RecordMultiplayerTraffic(long bytes, float dt)
        {
            bool ok = _meter.Record(bytes, dt);
            if (!ok || _meter.LowBalance) Persist();
            return ok;
        }

        private void OnApplicationPause(bool paused) { if (paused) Persist(); }
        private void OnApplicationQuit() => Persist();
    }
}
