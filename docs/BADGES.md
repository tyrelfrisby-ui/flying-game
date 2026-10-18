# Badges, boards and the privacy line

2026-10-18. Four messages from the owner, in order, each one tightening the design:

> *"As a user completes lessons their badge (new feature) changes. This badge is visible to other users above
> their airplane with their handle or callsign. Badges are glider student pre solo glider student solo glider
> pilot Pvt and glider pilot comm power student pre solo power student solo power Pvt power comm change power
> to asel or ases as applicable if they are taking lessons in both etc so glider asel ases amel ames etc and
> finally ATP if they meet the minimums and pass the tests this badge system is modeled on the real pilots
> licenses but is more skill/test based than hour based and minimums are you must have appropriate rating
> prior to getting the next one ie comm before atp Pvt before comm"*

> *"There are also badges for non FAA licenses/ratings like aerobatics (based on IAC categories getting 5 or
> higher on the known sequence) soaring completing cross country races achieving altitude thresholds
> thermaling etc plus stol competition levels of achievement etc things that anchor more advancements due to
> peer recognition. Lesson records and scores are private nobody needs to know you took 100 hours to get a
> Pvt license in a glider or failed the checkride 20 times but challenge scores are public with a top 100
> scoreboard so every challenge … must have the exact same conditions locked … and every challenge has a
> running leaderboard of the top 100 scores for that aircraft type in that challenge … The score is the
> summation of the inverse of the number you hold on every leader board."*

> *"A user can choose to show/hide the badges/scores etc of both others and themselves but their handle is
> always displayed."*

> *"no show hide is both a declutter and a personal preference not to show your own badges and
> accomplishments. they would still be visible on the user's profile just not shown above their handle to
> others as they fly."*

## The five rules, and where each one lives

| Rule | Where | Test |
|---|---|---|
| You cannot skip a rung | `RatingResolver.Resolve` stops at the first missing one | `YouCannotSkipARung` |
| The class comes from the aeroplane | `Certificate.ClassOf` reads floats and engine count | `TheClassIsWorkedOutFromTheAeroplane…` |
| Lesson records private, results public | `PilotIdentity` is the only thing that travels | `NothingAboutHowHardItWasEverLeavesTheDevice` |
| A scored contest owns the air | `Contests` + `SessionSettings.LockContestConditions` | `EveryScoredContestPinsEveryThing…` |
| Score = Σ 100 / rank | `PilotScore` | `TheScoreIsTheSumOfOneHundredOverYourRank` |

## The ladder

`None → StudentPreSolo → StudentSolo → Private → Commercial → ATP`, held **per class** —
**GLIDER, ASEL, ASES, AMEL, AMES**.

**The same lessons to a tighter standard is what the ladder is.** A level does not ask for new manoeuvres so
much as the same ones flown better: 70 % to go solo, **78 % for a private, 88 % for a commercial, 93 % for an
ATP**. That is how real certification works — the commercial ACS halves the altitude and heading tolerances
the private one allows — and it means the whole thing is skill based without an hour counted anywhere, which
is what was asked for.

A **glider has its own shorter list**, because it has no powered climb and no go-around to fly.

Fly something in a class and you are a pre-solo student in it; that costs nothing but turning up. Everything
above that is the full list at the level's score. **A bad day never costs you a rating** — the log keeps your
best score per lesson, so a rating once earned stands.

**The class is read off the aeroplane**, not from a list that would go stale: a glider by its id, sea by
whether it has floats, multi by whether it has two or more engine mounts. So the AirCam is **AMEL** (it is a
twin), the amphibious AirCam is **AMES**, and the H-4 is eight engines on a hull, which is AMES too. Fly the
syllabus twice in two aeroplanes and you earn two badges, exactly as a real pilot does when they add a
seaplane rating — and a landing flown on floats does nothing at all for the land rating.

An ATP is written the way it is spoken: **`ATP ASEL AMEL`**. Below that the badge leads with the highest thing
you hold and fits in what it can — `ASEL COMM · ASES PVT +1` — because a tag nobody can read at two hundred
yards is not a badge.

## The awards that are not licences

A rating says you are allowed to do something. These say you are **good at it, in front of other people**.

* **Aerobatics** follows the IAC's own ladder — Primary, Sportsman, Intermediate, Advanced, Unlimited — and
  the bar is the owner's: fly that category's Known sequence averaging **5 or better per figure**.
* **Soaring** follows the FAI badges, the oldest award scheme in the sport: Bronze, Silver, Gold, Diamond,
  each needing **all three legs** — height gained in one flight, cross-country courses completed, and time
  aloft. Height alone does not buy you a Silver. **The distances are scaled and that is stated rather than
  hidden**: a Silver distance is 50 km and this world is 27 km across, so a Diamond here is not a Diamond at
  Minden.
* **STOL** is take-off plus landing added together, which is how a real STOL drag is scored: 900 / 600 / 420 /
  300 ft for Bronze, Silver, Gold and Master.

## The privacy line

**It runs through one class, and it is a type rather than a rule so that it can be checked.**

* **`PilotLog` — private, never leaves the device.** Every lesson, every attempt, every score, in
  PlayerPrefs. *"nobody needs to know you took 100 hours to get a Pvt license in a glider or failed the
  checkride 20 times."*
* **`PilotIdentity` — public, and the only thing that travels.** A handle, a badge, the awards and the
  combined score. All four are **results**: they say what you can do, not what it cost you.

`BadgeTests.NothingAboutHowHardItWasEverLeavesTheDevice` walks the public type by reflection and fails if a
field appears whose name or type could count attempts, hours or failures. If somebody one day wants to put
"47 attempts" on a name tag, that test is where they find out it was deliberate.

The reasoning is not only politeness. The thing that would most discourage somebody from practising is other
people watching them practise badly — and the people who most need the practice are exactly the ones it would
chase away.

### Three switches, and one of them is a different kind

I built two and the owner corrected me: there are three, and the third is not about a screen at all.

| | What it does | About |
|---|---|---|
| Show other pilots' badges and scores | Whether I draw theirs on **my** screen | Declutter |
| Show my own badge on screen | Whether I draw mine on **my** screen | Declutter |
| **Let other pilots see my badge and score** | Whether it **leaves the device at all** | Privacy |

The first two are decluttering and change nothing for anybody else. The third is a decision about yourself,
and **an unpublished badge is not SENT rather than not drawn at the far end.** That distinction is the whole
value of the switch: "nobody can see it" has to mean nobody *has* it, or the setting is a polite request to
other people's clients rather than a decision. The net layer drops nulls, so with it off there is no badge
field in the message at all — just the handle and the aeroplane.

It still shows on **your own profile**, in OPTIONS → BADGES, because that is your profile. The profile readout
deliberately reads `PilotRecord.OwnBadge()` and `OwnScore()`, which the publishing choice does not touch.

**The handle is on none of the three.** Whichever way all of them go, you always get to know who is in the
circuit with you — asserted over all four combinations.

## The boards, and the air they are flown in

**A board is per contest AND per aircraft type** — a Pitts list, an Extra list, a Super Cub list. A placing is
worth **100 / rank**: first is 100 points, hundredth is 1, off the board is nothing. Ten firsts make 1,000 and
ten hundredths make 10, which are the owner's own two numbers.

The shape of that decides how people will play, so it is worth naming: it is **very top-heavy**. Second is
worth half of first and tenth is worth a tenth, so **one board you own outright is worth exactly fifty you are
merely fiftieth on**. That rewards specialising in an aeroplane and a course, which is how competition flying
actually works — and it means being the best in the world in a Super Cub is worth the same 100 points as being
the best in an Extra.

### A scored contest owns the air

This is the load-bearing piece. A board of times flown in air each pilot chose for themselves is not a board,
it is a list of who picked the best tailwind. So the moment a scored contest is selected, `SessionSettings`
overwrites the wind, turbulence, temperature, thermals **and tyres** with the contest's own, the menu's
steppers go dead and say why, and `Fly()` locks them again on the way out — because that is the one line every
flight passes through.

The standard is dead calm, with three deliberate exceptions:

* **The STOL contest gets bushwheels, locked ON.** They are a legitimate STOL modification and every real
  contest aeroplane has them, so locking them off would be the odd choice. Everybody gets them, which is the
  only thing that matters.
* **A glider task gets thermals**, at standard strength, because a soaring task with the lift switched off is
  not a soaring task.
* **The crosswind and headwind challenges keep their wind.** Their whole point is the wind, so it is pinned
  rather than removed.

Every contest's conditions have a **fingerprint** — a short stable string — to be carried with a submitted
score so a board can refuse anything flown in different air. Worth being honest about what that buys:

> It makes an **accident** impossible — a score flown with the wind left on from the last flight will not
> match and will be rejected, which is most of the problem. It does **not** stop a determined cheat, because
> the client computes it and a client can be made to lie. The real answer is that a leaderboard entry should
> be a **replay the server re-flies**, and this project already records replays, so that door is open. Until
> then it is a seatbelt and not a lock, and the leaderboard should say so.

## Still open

1. **There is no server, so there are no ranks.** The score, the boards and the top 100 all need somewhere to
   live that pilots cannot write to directly. `PilotRecord.Placings` is the hook and it is empty: a rank
   nobody else can verify is not a rank, so nothing local contributes to the public score. This is the
   biggest missing piece and it is a backend job (and an owner decision about where it runs).
2. **Two contests are designed and not built**, and are listed in `Contests.Planned` rather than left
   implicit: **judging an aerobatic sequence figure by figure** (which is what the IAC badges need) and a
   **cross-country soaring task** (which is what the FAI badges need). Each is a feature in its own right.
   Until they exist, the aerobatic and soaring awards have nothing to award them from.
3. **The STOL and race contests do not yet report a score to the record.** `StolController` and
   `RaceController` know the numbers; nothing calls `PilotRecord.Award` or submits a board entry.
4. **The badge does not travel on the LAN**, only over the relay. The mesh's hello packet and
   MultipeerConnectivity's 63-byte display name both need a field adding, so on a shared-network room you see
   handles only.
5. **Nothing ever shows you the boards.** There is no leaderboard screen; the badge and score appear on the
   tags and in OPTIONS.
6. **No ATP written test, and no checkrides as distinct events.** The ladder is lesson scores alone, where
   the owner's brief says *"pass the tests"* — a checkride as its own scored event, flown to the level's
   tolerances with no second attempt in the same sitting, is the obvious next piece.
