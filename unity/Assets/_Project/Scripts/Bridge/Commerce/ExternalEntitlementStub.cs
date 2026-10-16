#if AERO_EXTERNAL_STUB
using System;
using System.IO;
using FlyingGame.Sim.Commerce;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlyingGame.Bridge.Commerce
{
    /// <summary>
    /// Stands in for an external subscription provider until one exists (docs/EXTERNAL-ENTITLEMENT.md).
    /// Reads a local JSON file so every awkward case can be driven by hand — entitled, expired, revoked,
    /// unreachable, malformed, clock-skewed — which is the point: the offline rules and the resolution order
    /// are the parts most likely to be wrong, and both can be exercised without a line of partner code.
    ///
    /// Compiled out unless AERO_EXTERNAL_STUB is defined, and that define is deliberately absent from the
    /// shipping build configuration. It can never grant anything in a release build.
    ///
    /// File: {persistentDataPath}/external-entitlement.json
    ///   { "entitled": true, "expiresUtc": "2026-12-01T00:00:00Z", "revoked": false, "unreachable": false }
    /// </summary>
    public static class ExternalEntitlementStub
    {
        public static bool TryRead(out EntitlementGrant grant)
        {
            grant = default;
            try
            {
                string path = Path.Combine(Application.persistentDataPath, "external-entitlement.json");
                if (!File.Exists(path)) return false;
                var o = JObject.Parse(File.ReadAllText(path));
                // "unreachable" simulates the provider being down: no grant at all, which is what exercises
                // the grace window in EntitlementResolver.
                if (o.Value<bool?>("unreachable") == true) return false;
                if (o.Value<bool?>("entitled") != true) return false;
                string exp = o.Value<string>("expiresUtc");
                if (!DateTime.TryParse(exp, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var when)) return false;
                grant = new EntitlementGrant(EntitlementSource.External, when, DateTime.UtcNow, o.Value<bool?>("revoked") ?? false);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"external entitlement stub unreadable: {e.Message}");
                return false;
            }
        }
    }
}
#endif
