# What you can buy, and what you earn

**No prices in this file.** They are read from the App Store at run time and localised, so the only true
answer to "how much is it?" is "what the store shows you when you open the Store screen". Source of truth for
the identifiers and the rules: `unity/Assets/StreamingAssets/commerce.json`.

## The four things

**Free, forever, no purchase.** The 2-33 trainer glider and the Cessna 172, plus the lessons and challenges
that start you earning points. Somebody who never pays anything still has two aeroplanes and the whole
syllabus they fly in.

**The 14-day trial.** Everything open, single player only, for fourteen days. It is started by "buying" a
zero-cost item in the App Store — that is deliberate, and worth explaining if a customer is startled by a
purchase sheet appearing for something free: it is how **Apple** holds the start date, which means the trial
survives deleting the app, getting a new iPad, and setting the clock back. We never see it and cannot extend
it. It does **not** include flying online.

**Full Unlock.** One payment, not a subscription. Opens every aeroplane and every lesson permanently, and
includes the first 30 days of online flying. Family Sharing is off for it.

**The Multiplayer Pass.** A subscription, monthly or yearly — one subscription group, so switching between
the two is something the App Store handles and we are not involved in. Needed by anyone flying online through
our relay. It is **unlimited**: there is no hour allowance, nothing to top up, and the app will never refuse a
flight over hours. (The app does count a pilot's connected hours on their own device, because relay time is
what the pass has to cover. That number never leaves the device.)

Flying together over the same Wi-Fi needs **no pass at all** — see MULTIPLAYER.md. Nothing of ours is in the
middle, so there is nothing to charge for.

## Points

Points are **earned only and never sold.** There is no way to buy one, and there never will be — the owner's
rule. They buy aeroplanes and challenges; they can never buy a lesson, because a lesson is passed by flying
it to the standard or it is not passed.

The scale: everything costs 1,000,000 points, and plain flying pays about 1,000 an hour, so flying and
nothing else unlocks the whole fleet in roughly 1,000 hours. Landings, towing a glider, gaining height in a
glider, race gates, flying under a bridge or through an arch, and finding a hidden place all pay more per hour
than plain flying. Full detail: `docs/POINTS.md`.

> **State of it today:** the table exists and the earning is **not built yet** — no code earns, stores or
> spends a point. If a customer asks why their points are not going up, that is the honest answer, and it is
> not a bug in their copy. Check KNOWN-ISSUES.md before answering, in case this has changed.

## Restoring a purchase

New device, deleted and reinstalled, or "I paid and it's gone":

1. Make sure they are signed in to the **same Apple Account** that bought it. This is the answer about nine
   times in ten — a family member's device, or a second Apple Account.
2. In the game: **Store** (on the landing page) → **Restore purchases** at the bottom. That asks the App Store
   for everything that Apple Account has bought.
3. It needs a working connection the first time on a new device, because the receipt has to come from Apple.
   After that it is held on the device and works offline.
4. The trial restores the same way, with its original start date — restoring does not restart it.

If the right Apple Account restores and the content still does not appear: hand it to a person. Do not guess.

## Refunds

**We cannot issue one, and we do not hold the money.** Every purchase is Apple's transaction. A refund is
requested at **<https://reportaproblem.apple.com>** — sign in with the Apple Account that paid, find Aero
Playground, choose the purchase and say what went wrong. Apple decides.

Say that plainly and without hedging. Do not say "I'll look into a refund", do not say it is likely, and do
not say it is unlikely. If somebody is asking for a refund because something is broken, point them at
reportaproblem.apple.com **and** take the bug report properly — those are two separate jobs and they both get
done.

A cancelled subscription is also Apple's: Settings → their name → Subscriptions, on the device. Cancelling
stops the next renewal and the pass runs to the end of the period already paid for.
