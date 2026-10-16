# The maze behind the waterfall

**Built, 2026-10-16** — the geometry, the strips and the hazard sites are in and flyable
(`src/FlyingGame.Core/WaterfallTunnel.cs`). What is NOT built yet is listed below: the rockfall and the water
sheet have no behaviour, and the tow has no AI. Ty's three answers are folded in — the slit stays 40 ft, every
strip is 2,500 ft, and under tow the tow plane refuses the slit.

Ty, 2026-10-16: *"The waterfall tunnel airport should be a loop the can fly into and out of hidden behind the
waterfall. Give it a bit of a maze with forks etc and put landing strips here and there… a bad fork could
require an immediate landing (dead end) or passage through a tight spot or challenge like falling rocks a
narrow slit in the rock requiring knife edge 40 ft wide for example or a waterfall sheet of water… every "bad
fork" should be passable for any airplane up to the t6 in size and speed but no need to support larger or
faster aircraft."*

The layout is written as a PATH — a pen that goes straight, turns and drops — not as a list of coordinates, so
the numbers below are what the pen was told and a test measures what came out: every leg has at least 200 m of
mountain over its crown, the floor has exactly one answer everywhere two legs overlap, every leg starts inside
another one, and the last leg lands on the end of the bore to the metre.

## The design aeroplane sets every number

The T-6 is the largest and fastest thing that has to fit, and everything smaller has to fit too — which is the
harder half, because a 172 cannot hold a knife edge and a glider cannot climb out of a mistake.

| Question | Figure | Where it comes from |
|---|---|---|
| Span to clear | **12.8 m** (42 ft) | T-6 |
| Speed in the maze | 120–180 KIAS | T-6 cruise to a fast run |
| Tightest main-loop bend | **250 m radius** | 150 kt at 3 g is 215 m; 180 kt at 4 g is 226 m. 250 m leaves something in hand for a pilot who arrives fast. |
| Every strip, usable | **762 m** (2,500 ft) | T-6 lands in ~1,500 ft and clears 50 ft in ~1,800 ft. Owner's rule: every dead end has to work up to the T-6, so they are all this length. |
| Bore cross-section | 70 m wide, 32 m tall | A 172 can be sloppy in it and a T-6 can roll in it. |

## The loop, and the glider's way out

Both mouths are in the same back wall, and the loop **descends the whole way**: in at **430 m**, out at
**305 m** — 125 m of drop over about 5 km of tunnel.

The way **out** is on the river's own centreline, so you leave through the falling water. The way **in** is
**110 m to the side of it**, which changed from the first draft for two reasons. The honest one: at 430 m the
curtain hangs only about 19 m clear of the rock, so a pocket entered through the water up there would be
unflyable. The structural one: the entry leg would otherwise sit directly on top of the exit bore, and the
ground under a wheel has to have one answer.

**The maze is for powered aeroplanes** (owner, 2026-10-16: *"There should be a landing strip early on for
gliders but no this is meant for powered airplanes"*). So a glider that follows somebody in gets **STRIP 0**,
300 m past the entrance and before the first fork: 500 m of pavement, glider markings, impossible to mistake
for the way on. Everything past STRIP 0 only has to work for powered aeroplanes up to the T-6 — and a glider
that stops on STRIP 0 is not finished, because of what happens next.

The 4.8 % descent is now a choice rather than a requirement, and it stays: it gives the loop a one-way sense,
which is what makes a fork a decision instead of a corridor you can back out of.

Both pockets are measured by a test rather than assumed: more than 30 m of air between the water and the rock
at each mouth's height, and less than 200 m, which is the line between "behind the waterfall" and "a hole in a
cliff".

## The map, as built

```
   BEHIND THE CURTAIN                        C: dead end, STRIP 1
   IN 430 m ─┐  (110 m to the side)          ┌──────────────┐
             │                               │  762 m + pad │
        A.entry 120 m                        └──────▲───────┘
             │                                      │ left at FORK 1
        bend 250 m ──► north                        │
             │                                ┌─────┴──────┐
        S0 GLIDER STRIP 500 m ─────────────────►  FORK 1    │
             │                                └─────┬──────┘
        B1 north 700 m ──────────────────────────────┘
             │
        bend ──► west,  B2 west 500 m
             │
        ┌────┴──── FORK 2 ───────────┐
   D: 20° out, 120, SLIT 40,        E: 20° out, 120, ROCKFALL 40,
      120, 40° back, 280, 20° in       120, 40° back, 280, 20° in
        └────┬───────────────────────┘   (symmetric doglegs, so both
             │                            end at the same point)
        bend ──► south
             │
        F HALL: STRIP 2, 762 m ─────────── the big chamber
             │
        ┌────┴──── FORK 3 ───────────┐
   G: 70, WATER SHEET 40, 70        H: 45° away, 160 m,
        │                               STRIP 3 762 m + pad (dead end)
   K south 365 m
        │
   bend ──► east,  K join 1,143 m
        │
   OUT 305 m ─► through the curtain, over the bore's own 2,500 ft
```

Four strips, three forks, three hazard sites, 92 segments, a footprint of about 2.1 × 3.1 km under the Bench
plateau, and **204 m** of rock over the thinnest crown.

| Leg | What it is |
|---|---|
| **A.entry** → bend | In from the high mouth and straight into a 250 m bend, which also takes the bore clear of the upper gorge. |
| **S0.glider** | The glider strip, 500 m, green markings and green edge lights so it cannot be mistaken for the way on. Before any fork, per the owner. |
| **FORK 1** | Straight on, or left into **C**: 762 m of strip and a pad. Land, turn, take off, and you are back at the fork with the other way to go. |
| **B1 / B2** | The long legs — 700 m north, a left bend, 500 m west. |
| **FORK 2** | **D** the slit, or **E** the falling rock. |
| **D.slit** | **12.2 m wide, 20 m tall, 40 m long.** A T-6 goes through on a wing. A 172 spans 11 m, so it fits with 60 cm a side and has to bank to be comfortable — the challenge the owner asked for, kept at 40 ft on his word. |
| **E.rockfall** | The same 40 m at full width: the hazard is the rock, not the gap. |
| **F.hall** | The big chamber, **STRIP 2 at 762 m**, lengthened on the owner's rule. |
| **FORK 3** | **G** the sheet of water on the way home, or **H**, 45° away from the exit into the last dead end and **STRIP 3**. It turns away deliberately — a blind leg crossing the exit would put two floors in the same place. |
| **K** | South, a left bend onto the bore's own line, and 1,143 m east to the end of the bore. |

## What is not built yet

The geometry can be flown today. These have no behaviour:

1. **The rockfall** — the shaft, the 9 s cycle, the dust and the sound, and rocks that can hit you.
2. **The water sheet** — a curtain across the bore that blinds for a second and does no damage.
3. **The tow** (below) — the reset, the hook-up, the rope-steered forks, and the tow plane refusing the slit.
4. **The find and the gates** — the maze is listed in `configs/progression.json` under `hiddenPlaces`, but no
   code earns a point anywhere yet.
5. **How any of it looks.** The arches read as dim rock on the vertex shader's ambient floor, which is about
   right for a bore with a 70 m mouth, but nobody has seen it on a device — `AERO_SELFTEST=tunnel` takes
   fourteen screenshots for exactly that.

## The tow through the maze — the glider's way round after all

Owner, 2026-10-16: *"If a glider lands on the strip inside the tunnel it gets a tow plane to launch and pull
it through the course under tow. The tow plane makes left right choices based on the direction the rope is
pulling on the tail. If the rope is pulling tail left on the tow plane the tow plane takes the right option
for example"*.

This is the best idea in the whole feature, because it turns the glider's dead end into the hardest way to fly
the maze — and the physics of it is already right. A rope pulling the tow plane's tail to the left yaws its
nose to the **right**, so a tow pilot being dragged that way would go right. The glider pilot therefore steers
the pair by **sliding to the side opposite the branch they want**: slide left, and the tow goes right.

How it is built:

| Piece | How |
|---|---|
| The slit | **The tow plane refuses that branch**, whatever the rope says (owner 2026-10-16). No tow pair can fly a knife edge, and a tow pilot who was asked to would say no. |
| The reset | A full stop on STRIP 0, **on the runway surface and undamaged**, and the glider takes itself back to the threshold: it rolls backwards to the start, lined up, with the tow plane ahead and the rope on. Rolled off the surface, or bent, and none of that happens — that is the ordinary crash-and-reset, and a pilot who lands in the rocks should not be rescued by the scenery. |
| The hook-up | The existing aerotow does the rope — the same slack and the same break load that already work outdoors. |
| The start | The glider has no engine, so its **throttle becomes the go signal**: full throttle and the tow plane starts its roll, anything less and nothing happens. The hint on screen says so — "Throttle full to start the tow" — because nobody would guess it. |
| The decision | At each fork the tow plane averages the rope's **lateral pull at its tail** over a window (about 1.5 s) ending at the decision point, with a dead band so small wobbles do not count. Tail pulled left → the RIGHT branch. Tail pulled right → the LEFT branch. |
| Centred | Inside the dead band the tow plane takes the **safe** branch — the one without the hazard. A glider pilot who does nothing gets round; a glider pilot who wants the slit has to ask for it. |
| The cue | A light pair on the roof about 400 m before each fork, and the tow plane rocks its wings once. The pilot has to know a decision is coming or it is not a decision. |
| Survivability | Every branch has to pass the **pair**, not just one aeroplane: the slit at knife edge is impossible under tow, so the tow route through FORK 2 takes the rockfall side, and a dead end means the pair lands — STRIP 1 and STRIP 3 at 760 m hold a tow plane and a glider with room. |
| Points | Tow time already earns at the highest per-hour rate in the draft table. Each fork taken under tow is a gate (120), and completing the loop under tow should pay a one-off on its own — it is harder than flying it. |

One thing to decide: **the slit cannot be flown under tow.** Either the tow declines that branch whatever the
rope says (my proposal — it reads as the tow pilot refusing, which is what a tow pilot would do), or the slit
gets a second, wider passage alongside it that only opens for a tow.

## The hazards, and the rule they all follow

Every one is **deterministic or telegraphed** — a hazard that kills at random is not a challenge, it is a dice
roll, and this game is about flying:

- **The slit** never changes. It is geometry, and it either fits or it does not.
- **The rockfall** is a fixed 9 s cycle with a one-second warning. Nothing about it is hidden after the first
  look.
- **The water sheet** does no damage at all. It takes your sight for a second at a place where sight matters.

## Points

Per the draft table in `configs/progression.json`: the maze itself is one **hidden-place find** (10,000, once)
when a pilot first gets in. Beyond that it is **gates** — each fork and each strip's threshold is a gate at
120, doubled inverted — so flying the loop fast and clean pays about what a race lap pays, which is right: it
is a race course that happens to be inside a mountain.

## Settled with the owner

- The maze is for powered aeroplanes, with the glider strip before the first fork.
- **The slit stays 40 ft** and you must bank to get through it.
- **Every strip is 2,500 ft** — no strip is a trap for a T-6.
- **Under tow the tow plane refuses the slit branch**, whatever the rope says. It reads as the tow pilot
  refusing, which is what a tow pilot would do.

Nothing is built past the straight bore. Exact routing gets pinned against the height field the same way the
bore was — the rock above every crown, the clearance from the upper gorge, and the bends all measured before
a metre of it is laid down.
