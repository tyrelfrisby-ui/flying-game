namespace FlyingGame.Sim.Commerce;

/// <summary>Where a grant came from. Ordered by how much we trust it to survive a dead network: a StoreKit
/// receipt and a comp grant are on the device, an external provider's is not.</summary>
public enum EntitlementSource
{
    None = 0,
    AppStore = 1,
    External = 2,
    Comp = 3,
}

/// <summary>How solid the current answer is.</summary>
public enum EntitlementTrust
{
    /// <summary>Nobody has granted anything.</summary>
    None = 0,
    /// <summary>Verified, and the grant has not expired.</summary>
    Fresh = 1,
    /// <summary>Expired, but inside the offline grace window — we believe it and keep flying.</summary>
    Grace = 2,
    /// <summary>Expired past grace, or revoked.</summary>
    Expired = 3,
}

/// <summary>
/// One provider's statement that this person holds one subscription — the Pilot subscription or the
/// Multiplayer Pass — already verified by whoever owns that provider's keys. The core never verifies
/// signatures; it resolves between grants, which is the part worth testing headless. One
/// <see cref="EntitlementResolver"/> answers for one product, so the two subscriptions are resolved
/// independently and neither can mask the other.
/// </summary>
public readonly struct EntitlementGrant
{
    public readonly EntitlementSource Source;
    /// <summary>When the grant stops being good. A subscription's renewal date, a comp's end date.</summary>
    public readonly DateTime ExpiresUtc;
    /// <summary>When we last actually confirmed it against its provider.</summary>
    public readonly DateTime VerifiedUtc;
    /// <summary>The provider told us it is cancelled/refunded. Beats any expiry date.</summary>
    public readonly bool Revoked;

    public EntitlementGrant(EntitlementSource source, DateTime expiresUtc, DateTime verifiedUtc, bool revoked = false)
    {
        Source = source;
        ExpiresUtc = expiresUtc;
        VerifiedUtc = verifiedUtc;
        Revoked = revoked;
    }
}

/// <summary>What the game should do, right now.</summary>
public readonly struct EntitlementResolution
{
    public readonly EntitlementTrust Trust;
    public readonly EntitlementSource Source;
    public readonly DateTime ExpiresUtc;
    /// <summary>Plain words for the paywall/settings screen — never an error code.</summary>
    public readonly string Reason;

    public EntitlementResolution(EntitlementTrust trust, EntitlementSource source, DateTime expiresUtc, string reason)
    {
        Trust = trust;
        Source = source;
        ExpiresUtc = expiresUtc;
        Reason = reason;
    }

    /// <summary>The paid single-player content. Grace counts: a network error must never cost somebody a
    /// flight they have paid for.</summary>
    public bool PaidContent => Trust == EntitlementTrust.Fresh || Trust == EntitlementTrust.Grace;

    /// <summary>Verified against its provider, not taken on trust while offline. What the Multiplayer Pass
    /// is checked with: going online needs a network by definition, so if the relay is reachable the store is
    /// too, and there is no case where grace is the kind thing to do. Single-player content uses
    /// <see cref="PaidContent"/> instead, which does forgive a dropped connection.</summary>
    public bool Confirmed => Trust == EntitlementTrust.Fresh;
}

/// <summary>
/// Resolves the grants on hand into one answer, and is the only place that decides what happens when a
/// provider cannot be reached.
///
/// The rules, in one place so they can be argued with:
///  1. The longest-lived non-revoked grant wins. Somebody entitled twice is never told they are entitled
///     zero times.
///  2. A revoked grant is dead immediately, whatever its expiry says.
///  3. An expired grant still flies for <see cref="GraceDays"/> — this is a flight trainer, and taking the
///     airplane away over a dropped connection would be the wrong call every time.
///  4. The clock cannot be used to extend anything. We keep a high-water mark of the latest time we have
///     ever seen; winding the device clock BACK resolves against the high-water mark instead, and winding it
///     FORWARD only expires things sooner, which is nobody's idea of an exploit.
/// </summary>
public sealed class EntitlementResolver
{
    /// <summary>How long an expired grant keeps flying when its provider cannot be reached.</summary>
    public double GraceDays { get; set; } = 14.0;

    private DateTime _highWaterUtc = DateTime.MinValue;

    /// <summary>The latest time this resolver has ever been shown. Persist it alongside the cached grants.</summary>
    public DateTime HighWaterUtc => _highWaterUtc;

    public EntitlementResolver() { }

    public EntitlementResolver(DateTime highWaterUtc) { _highWaterUtc = highWaterUtc; }

    public EntitlementResolution Resolve(IReadOnlyList<EntitlementGrant> grants, DateTime nowUtc)
    {
        // Rule 4: never let a backward clock buy time.
        if (nowUtc > _highWaterUtc) _highWaterUtc = nowUtc;
        DateTime now = _highWaterUtc;

        bool sawRevoked = false;
        bool haveBest = false;
        EntitlementGrant best = default;
        for (int i = 0; i < grants.Count; i++)
        {
            EntitlementGrant g = grants[i];
            if (g.Source == EntitlementSource.None) continue;
            if (g.Revoked) { sawRevoked = true; continue; }          // rule 2
            if (!haveBest || g.ExpiresUtc > best.ExpiresUtc) { best = g; haveBest = true; }   // rule 1
        }

        if (!haveBest)
        {
            return sawRevoked
                ? new EntitlementResolution(EntitlementTrust.Expired, EntitlementSource.None, DateTime.MinValue,
                    "Your subscription was cancelled or refunded.")
                : new EntitlementResolution(EntitlementTrust.None, EntitlementSource.None, DateTime.MinValue,
                    "No subscription found on this device.");
        }

        if (best.ExpiresUtc > now)
        {
            return new EntitlementResolution(EntitlementTrust.Fresh, best.Source, best.ExpiresUtc, "Subscription active.");
        }

        DateTime graceEnd = best.ExpiresUtc.AddDays(GraceDays);      // rule 3
        if (now <= graceEnd)
        {
            int daysLeft = (int)Math.Ceiling((graceEnd - now).TotalDays);
            return new EntitlementResolution(EntitlementTrust.Grace, best.Source, best.ExpiresUtc,
                $"We could not reach the store to renew. You can keep flying for {daysLeft} more day{(daysLeft == 1 ? "" : "s")}.");
        }

        return new EntitlementResolution(EntitlementTrust.Expired, best.Source, best.ExpiresUtc,
            "Your subscription has expired.");
    }
}
