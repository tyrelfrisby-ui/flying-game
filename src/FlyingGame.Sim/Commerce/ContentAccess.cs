namespace FlyingGame.Sim.Commerce;

/// <summary>How much of the airplane list and the syllabus is open.</summary>
public enum AccessLevel
{
    /// <summary>The trainer glider and the C-172, and the lessons and challenges that start you earning.
    /// Everything else is earned with points or bought with the one-time unlock.</summary>
    Free = 0,
    /// <summary>The fourteen-day trial: everything, single player. Multiplayer is NOT part of it.</summary>
    Trial = 1,
    /// <summary>The one-time unlock, an external provider's membership, or a comp.</summary>
    Unlocked = 2,
}

/// <summary>What this pilot may fly, and why.</summary>
public readonly struct ContentAccess
{
    public readonly AccessLevel Level;
    /// <summary>When the trial runs out. Only meaningful at <see cref="AccessLevel.Trial"/>.</summary>
    public readonly DateTime TrialEndsUtc;
    /// <summary>The trial has been started and is over. Distinguishes "never tried" from "tried and done",
    /// which are the same <see cref="AccessLevel.Free"/> but not the same thing to say to somebody.</summary>
    public readonly bool TrialSpent;
    public readonly EntitlementSource Source;
    /// <summary>Plain words for the store screen — never an error code.</summary>
    public readonly string Reason;

    public ContentAccess(AccessLevel level, DateTime trialEndsUtc, bool trialSpent, EntitlementSource source, string reason)
    {
        Level = level; TrialEndsUtc = trialEndsUtc; TrialSpent = trialSpent; Source = source; Reason = reason;
    }

    /// <summary>Every airplane and every lesson is open, however that came about.</summary>
    public bool Everything => Level != AccessLevel.Free;

    /// <summary>The trial has not been started yet, so it is still worth offering.</summary>
    public bool TrialAvailable => Level == AccessLevel.Free && !TrialSpent;
}

/// <summary>
/// Resolves the one-time unlock, the trial and an external membership into one answer.
///
/// The trial's start date comes from a $0 non-consumable purchase, which means Apple signs and stores it:
/// it survives a reinstall, a new device and a wound-back clock, none of which is true of a date we write to
/// disk ourselves. This resolver keeps its own high-water mark on top of that, so moving the device clock
/// BACK cannot extend a trial either (moving it forward only ends the trial sooner, which is nobody's idea
/// of an exploit).
///
/// Hierarchy, highest first: the unlock is permanent and beats everything; an external membership unlocks
/// the same content for as long as the membership lasts; a comp does the same; the trial is last because it
/// is the only one with a deadline. Nobody entitled twice is ever told they are entitled zero times.
/// </summary>
public sealed class ContentAccessResolver
{
    /// <summary>How long the trial runs. Config — the owner set 14 days.</summary>
    public double TrialDays { get; set; } = 14.0;

    private DateTime _highWaterUtc = DateTime.MinValue;
    public DateTime HighWaterUtc => _highWaterUtc;

    public ContentAccessResolver() { }
    public ContentAccessResolver(DateTime highWaterUtc) { _highWaterUtc = highWaterUtc; }

    /// <param name="unlockOwned">The one-time unlock is owned (a verified, non-revoked non-consumable).</param>
    /// <param name="trialStartedUtc">Apple's purchase date for the $0 trial product; <see cref="DateTime.MinValue"/>
    /// if it was never started.</param>
    /// <param name="external">An external provider's membership, already resolved. Its grace window applies:
    /// a member whose provider cannot be reached keeps flying.</param>
    /// <param name="compUntilUtc">A local comp grant's end date, or <see cref="DateTime.MinValue"/>.</param>
    public ContentAccess Resolve(bool unlockOwned, DateTime trialStartedUtc, EntitlementResolution external,
                                 DateTime compUntilUtc, DateTime nowUtc)
    {
        if (nowUtc > _highWaterUtc) _highWaterUtc = nowUtc;
        DateTime now = _highWaterUtc;

        bool trialStarted = trialStartedUtc > DateTime.MinValue;
        DateTime trialEnds = trialStarted ? trialStartedUtc.AddDays(TrialDays) : DateTime.MinValue;
        bool trialLive = trialStarted && now < trialEnds;
        bool trialSpent = trialStarted && !trialLive;

        if (unlockOwned)
            return new ContentAccess(AccessLevel.Unlocked, trialEnds, trialSpent, EntitlementSource.AppStore,
                "Unlocked. Thank you — this one is yours for good.");

        if (external.PaidContent)
            return new ContentAccess(AccessLevel.Unlocked, trialEnds, trialSpent, EntitlementSource.External,
                external.Trust == EntitlementTrust.Grace ? external.Reason : "Unlocked through your membership.");

        if (compUntilUtc > now)
            return new ContentAccess(AccessLevel.Unlocked, trialEnds, trialSpent, EntitlementSource.Comp, "Unlocked.");

        if (trialLive)
        {
            int daysLeft = (int)Math.Ceiling((trialEnds - now).TotalDays);
            return new ContentAccess(AccessLevel.Trial, trialEnds, false, EntitlementSource.AppStore,
                $"Trial — everything is open for {daysLeft} more day{(daysLeft == 1 ? "" : "s")}. Flying online is separate.");
        }

        return new ContentAccess(AccessLevel.Free, trialEnds, trialSpent, EntitlementSource.None,
            trialSpent
                ? "Your trial has ended. The glider and the 172 stay, and the rest can be earned or unlocked."
                : "The glider and the 172 are yours. Earn the rest, or unlock everything at once.");
    }
}

/// <summary>Whether this pilot may fly online at all, before the hours question.</summary>
public readonly struct MultiplayerAccess
{
    public readonly bool Permitted;
    public readonly EntitlementSource Source;
    /// <summary>When the unlock's included month runs out, if that is what is permitting this.</summary>
    public readonly DateTime IncludedUntilUtc;
    public readonly string Reason;

    public MultiplayerAccess(bool permitted, EntitlementSource source, DateTime includedUntilUtc, string reason)
    {
        Permitted = permitted; Source = source; IncludedUntilUtc = includedUntilUtc; Reason = reason;
    }
}

/// <summary>
/// The online half, which is deliberately separate from the content half: the trial does not include it, an
/// external membership does not include it, and the one-time unlock includes it only for its first
/// <see cref="IncludedDaysWithUnlock"/> days. After that it is the pass.
///
/// No grace, ever. Going online needs a network by definition, so if the relay is reachable the App Store is
/// too, and there is no case where believing an unverified claim is the kind thing to do.
/// </summary>
public sealed class MultiplayerAccessResolver
{
    /// <summary>Days of online flying included with the one-time unlock. Config — the owner set 30.</summary>
    public double IncludedDaysWithUnlock { get; set; } = 30.0;

    private DateTime _highWaterUtc = DateTime.MinValue;
    public DateTime HighWaterUtc => _highWaterUtc;

    public MultiplayerAccessResolver() { }
    public MultiplayerAccessResolver(DateTime highWaterUtc) { _highWaterUtc = highWaterUtc; }

    /// <param name="pass">The multiplayer subscription, already resolved. Only <see cref="EntitlementResolution.Confirmed"/> counts.</param>
    /// <param name="unlockPurchasedUtc">Apple's purchase date for the one-time unlock, or
    /// <see cref="DateTime.MinValue"/> if it is not owned.</param>
    public MultiplayerAccess Resolve(EntitlementResolution pass, DateTime unlockPurchasedUtc, DateTime nowUtc)
    {
        if (nowUtc > _highWaterUtc) _highWaterUtc = nowUtc;
        DateTime now = _highWaterUtc;

        DateTime includedUntil = unlockPurchasedUtc > DateTime.MinValue
            ? unlockPurchasedUtc.AddDays(IncludedDaysWithUnlock) : DateTime.MinValue;

        // The subscription first: somebody who holds both should be told about the one that keeps going.
        if (pass.Confirmed)
            return new MultiplayerAccess(true, pass.Source, includedUntil, "Online flying is active.");

        if (includedUntil > now)
        {
            int daysLeft = (int)Math.Ceiling((includedUntil - now).TotalDays);
            return new MultiplayerAccess(true, EntitlementSource.AppStore, includedUntil,
                $"Online flying is included with your unlock for {daysLeft} more day{(daysLeft == 1 ? "" : "s")}.");
        }

        return new MultiplayerAccess(false, EntitlementSource.None, includedUntil, pass.Trust switch
        {
            EntitlementTrust.Grace => "Connect to the internet once so the App Store can confirm your online pass.",
            EntitlementTrust.Expired => "Your online pass has lapsed. Single player and flying together on the same Wi-Fi still work.",
            _ => includedUntil > DateTime.MinValue
                ? "The month of online flying included with your unlock has ended."
                : "Flying online needs a pass. Single player and flying together on the same Wi-Fi do not.",
        });
    }
}
