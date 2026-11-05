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

## The ladder (restructured 2026-11-01)

> **Superseded.** Until 2026-11-01 the ladder was `None → StudentPreSolo → StudentSolo → Private → Commercial →
> ATP`, held per class. Owner, 2026-11-01: the levels are exactly **Student pre-solo, Student solo, Pilot,
> Instrument (shown to the player as "Blind Flying"), Commercial, Flight Instructor**, earned along one path
> through particular aeroplanes; and the player never reads "private", "certified" or "license"/"licence".

`CertLevel`: None 0, StudentPreSolo 1, StudentSolo 2, Pilot 3, Instrument 4, Commercial 5, FlightInstructor 6.
**The numbers moved** (Commercial was 4); nothing ever persisted a level, and anything old goes through
`LegacyLevels`, never a cast.

The path (`RatingBook.Default`, pinned by `ProgressionTests.TheGraphIsTheOwnersGraph`):

| Stage | Aircraft (any ONE; every lesson in it) | Lessons @ score | After | Gives |
|---|---|---|---|---|
| glider-solo | glider-2-33-like | glider pre-solo @ 70 | — | Student solo |
| cub-solo | j3-cub-like | pre-solo @ 70 | glider-solo | — |
| pilot | decathlon-8kcab-like **or** great-lakes-like (player's choice) | full @ 78 | cub-solo | Pilot |
| aerobatic-glider | Grob 103 Acro-like / DG-class — **both placeholders** | glider full @ 78 | pilot | — |
| instrument ("Blind Flying") | any powered, not retired | full @ 83 | glider-solo **and** aerobatic-glider | Instrument |
| commercial | any powered, not retired | full @ 88 | instrument | Commercial |
| flight-instructor | any powered, not retired | full @ 93 | commercial | Flight Instructor |

Flying anything at all makes you a pre-solo student. **The same lessons to a tighter standard** is still what
the ladder is. **On this build the ladder stops at Pilot**, because the aerobatic glider stage cannot be flown
until one of its aeroplanes exists (`OnThisBuildTheLadderStopsAtPilot…`).

**Classes** are still read off the aeroplane (`Certificate.ClassOf`). A class goes on the badge once its whole
list has been flown in it to the Pilot standard (78 %): `PILOT · ASEL ASES`, `BLIND FLYING · GLIDER ASEL`. The
level leads the tag and classes follow only if they fit whole in 26 characters.

**Save migration** (`SaveMigration`, `SaveMigrationTests`): an old log (no `#v` key) is judged under the OLD
rules, frozen in the migration, and the highest old level held in any class maps to: pre-solo → pre-solo;
solo → solo (glider-solo carried over, plus cub-solo if the solo was powered); Private → Pilot; Commercial →
Commercial; ATP → Commercial. The stages leading there are written as carried over (`!pilot=1`) and the badge
screen says so. Old badges arriving over the network ("ASEL PVT") are redrawn in current words
(`LegacyLevels.UpgradeBadge`).

**Retired**: the 737-800 (`boeing-737-like`) — config, model and livery kept; off every picker; a flight in it
counts for nothing on the ladder. **Placeholders** (`Hangar.Placeholders`): Grob 103 Acro-like, DG-class
aerobatic glider, Beech 18-like, 737-200-like — data only, no config, never in a picker.

**Jobs** (`FlyingGame.Sim.Jobs`) are stubs: bush, float and backcountry in the Cubs, the Super Cub and the
Beaver; Beech 18 night air mail; the airline track in the DC-3 then the 737-200. Navigation is
`Pilotage / PaperMap / CompassAndClock / FourCourseRange / Ndb / Vor / Dme` and nothing else, ever.

## The awards that are not levels

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
