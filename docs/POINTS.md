# Points — a draft for the owner to approve

The table is in `configs/progression.json`. **Nothing reads it yet**: no code earns, stores or spends a point.
This document is here so the numbers can be argued with before any of it is built.

The owner's brief, 2026-10-15, is the source for all of it: points are **earned only, never sold**, they buy
**aircraft and challenges**, and a **lesson is never bought** — a lesson is passed by flying it to the standard
or it is not passed at all.

## The one number everything else hangs off

The owner set the scale directly: flying and nothing else should take **about 1,000 hours** to unlock
everything. So everything costs **1,000,000 points** and plain flying pays **1,000 an hour**. That makes the
anchor exact rather than approximate, and it gives every other rate an honest unit — "how many hours of just
flying is this worth?"

The aircraft and challenge costs in the table sum to exactly 1,000,000. All 27 aircraft are accounted for:
two are free (the 2-33 and the 172) and 25 are priced.

## Earning rates, in the owner's own order

He listed them lowest to highest. The per-hour figure in the right-hand column is what each is worth in
practice, which is how the ordering was checked — a per-event award can outrank a per-hour one.

| Earned by | Award | ≈ per hour of doing it | Why |
|---|---|---|---|
| Flying | 1,000 / h | 1,000 | The anchor. 1,000 h alone unlocks everything. |
| A successful landing | 200 each | ~2,400 (5-min pattern) | Kept under the tow rate, as ordered. |
| Glider altitude gained | 25 / 100 ft | ~1,250 (good 2-hour flight) | After release or airstart, on the net high-water mark, so porpoising is not paid twice. |
| On the rope | 3,000 / h | 3,000 | Towing a glider up is flying too, and someone has to do it. |
| A gate | 120 each | ~24,000 (10-gate lap) | Rooftop rings, race gates, race pylons. |
| Inverted | ×2 | — | On gates and the bridge/arch/hangar passes only. Not on flying time or landings. |
| Under a bridge, through an arch, through a hangar | 400 each | occasional | A real gap with something solid either side. |
| Finding a hidden place | 10,000, once each | — | The largest single award: the only thing here that cannot be ground out. |

Definitions worth settling now, because they decide whether this feels fair:

- **A successful landing** is on a runway (or floats on the water at a seaplane base), within the surface, no
  gear damage, no ground loop. A touch-and-go counts. A bounce that turns into a go-around does not.
- **Inverted** means the wings are past 120° of bank for the *whole* pass, not at the instant of the gate.
- **Each pass counts once**, with a cooldown, so orbiting the same arch is not a points mill.

## Places the brief names — what exists and what does not

Checked against `src/FlyingGame.Core/`:

| The owner said | State |
|---|---|
| "the rings on the buildings" | **Exists.** `Playground.RooftopRings()` — five towers 190–300 m in a ring round downtown, flown in order, each ring facing the next. Plus a giant spinning ring on the tallest. |
| "the pylons for the air racing" | **Exists.** `WorldFeatures.RaceCourse` — gates and left/right pylons, with `AirRace` already scoring laps and penalties. |
| "arches" | **Exists.** Twin natural arches sharing a pillar in the Playground, the big river arch in `Landmarks`, and an island arch in `Seaside` ("an arch big enough to fly through"). |
| "bridges" | **Exists.** In the Playground, the Seaside (a Golden Gate), and `Landmarks`. |
| "hangars" | **Exists.** At the airports in `WorldTerrain`, and on the Seaside. Whether any is open at both ends and big enough to fly *through* has not been checked and is the thing that matters here. |
| "the cove on the island" | **Settled 2026-10-16: it is the island CAVE** — the chamber inland of the west shore whose dome breaks the cliff (`Seaside.SeaCave`). That is the hidden-place find; Avalon, the crescent bay on the mainland side, is not one. |
| "a tunnel with a runway inside it behind the waterfall" | **To be built** (owner, 2026-10-16). It does not exist today: there are three plunge falls, one at each canyon wall, each already with a 60 m amphitheater you can fly along behind the curtain — but no tunnel anywhere and no runway inside anything. New scenery, big enough to land in and take off from. |

So of the named hidden places, the island cave is there and the waterfall tunnel has to be built. Everything
else in the earning table can be scored against geometry that already exists. Two more hidden places are now
coming: the tunnel runway behind a fall, and the school in the cave inside Drake hill
(`docs/pending/DRAKE-HILL-PLAN.md`) — both worth the same one-time find as the cave.

## What building this actually involves

The table is the easy half. What is not written:

1. **An earner** that watches the flight and awards points — it needs hooks in the landing detection, the
   glider release, the tow, and the gate/pylon triggers that `AirRace` already has.
2. **Pass detection** for bridges, arches and hangars. The solids are registered (`WorldSolids.Shapes`), so
   this is a volume test per landmark rather than new physics.
3. **Hidden-place triggers**, one per place, plus the waterfall tunnel itself if he wants it.
4. **Storage**: on the device, and in the player's iCloud key-value store so points follow them to a new
   phone. iCloud KVS is the right choice — it is tiny, free, needs no account of ours, and syncs without a
   server, which keeps the privacy answer as it is. It is also best-effort, so the device copy is the
   authority and the cloud copy is a merge-on-launch that only ever takes the **higher** total, since points
   are earn-only and a stale cloud copy must never take points away.
5. **Gating**, which is where this touches the rest of the game: the aircraft picker and the challenge list
   have to ask what is unlocked. That is the same question the entitlement layer answers for the trial and the
   unlock, so the two want to meet in one place rather than two.

None of it should start before the table is approved, because every rate above decides how long someone is
asked to fly for the next aeroplane, and that is the owner's call and not an implementation detail.
