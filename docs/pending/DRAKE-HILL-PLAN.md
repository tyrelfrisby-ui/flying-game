# Drake hill and the hidden school — build plan for the owner to approve

The owner's brief, 2026-10-16: a large hill called **Drake** with a VOR on top; a cliff on one side; what looks
like the start of a grass runway, with only 500 ft of it showing outside — it carries on through the cliff,
which is a hologram — and behind it a giant cave holding a flying school. Inside, on the **left**, a hill with
Lilienthal- and Chanute-style gliders flown down it into a "swimming pool" of pillows. On the **right**, rows
of parked trainers — 2-33s, Jennys, Stearmans, AT-6s — with students, instructors and mechanics at work, and
**behind them, through glass**, classrooms and labs: engines torn down, wing structures, wind tunnels, every
tool for teaching how an aeroplane works. Blackboards with drawings and equations. Land, roll through the
hologram, and you are enrolled in a harder curriculum in harder aeroplanes to higher standards.

Nothing below is built yet. Numbers are the proposal; the geometry gets pinned against the height field before
anything is laid down. Answers from the owner on 2026-10-16 are folded in, and the hill is now the second
piece of this work — the tunnel runway behind the first waterfall goes in first.

## 1. The name: there isn't one

**Settled: the school has no name anywhere in the game.** No sign, no title on a blackboard, nothing in the
in-world text. Nor does anything of the Richard Bach story it is influenced by appear — no names, characters
or lines. What carries over is only the idea: a hidden place where flying is pursued for its own sake, to the
limit of how well it can be done. The five names that were offered are withdrawn.

## 2. Where the hill goes

The Valley plain sits at 1,000 ft (`WorldTerrain.DatumM` = 304.8 m). Proposed centre **x = −2,500, y = +700**:
2.5 km south of the Valley runway and 700 m east of its centreline, which keeps clear of the aerobatic box
(400, 650), the race course (x 0–1,110, y 1,755–2,095), the tower (−900, −500) and the escarpment wall at
y = −1,050. Close enough to be seen from the circuit, far enough that nobody stumbles into it on downwind.
Exact position is checked against the height field, the river and the crop field before it is built.

| Piece | Size |
|---|---|
| Hill | 1,400 m across, rising **320 m** above the plain (crown ≈ 2,050 ft MSL) |
| VOR on the crown | Standard cone on a counterpoise, ident **DRK**. Scenery; no radio receiver yet (see §7) |
| Cliff | The **north** face, vertical, 200 m tall — the face you see leaving Valley |
| Grass strip, the part you can see | **152 m (500 ft)**, level, running north→south at the cliff's foot, straight at the rock |
| Cave mouth | **60 m wide, 26 m tall** — wide and tall enough to roll a 172 or a Stearman through at speed |
| The runway inside | **760 m (2,493 ft)** more of it, same surface, same centreline |
| **Total usable length** | **912 m (2,992 ft)**, of which 83 % is inside the hill |

**The 500 ft is what shows, not what you get** (owner: "the runway continues inside the cave but only 500ft is
visible outside"). That changes who can find it, and for the better: a 172 needs about 1,335 ft from 50 ft to
a full stop, so it lands on the visible grass and rolls through the hologram to stop inside with room to
spare. Anything that can use 3,000 ft can get in and out again. What the 500 ft does instead is make the
approach a commitment — from outside there is no visible way to stop, so the pilot has to trust what the
hologram is hiding.

## 3. The chamber

Entering southbound through the mouth, with **left = east** and **right = west**, as the brief has it:

| Piece | Size |
|---|---|
| Chamber | **820 m** deep (into the hill), 420 m wide, **140 m** to the crown of the dome |
| The runway down the middle | 760 m of it, on the centreline of the mouth, with 60 m of floor past its end |
| Glider hill (left) | 70 m high, a 260 m slope at ~15°, launch shelf at the top |
| Pillow pool (left) | 70 × 45 m at the foot of the slope, the gliders landing into it |
| Apron (right) | 240 m of parked trainers — 2-33s, Jennys, Stearmans, AT-6s — with figures working on them |
| Classrooms and labs (right, behind the glass) | 240 × 45 m, two levels, glass to the apron |

Rock above the dome stays ≥ 60 m thick everywhere, so the hill holds the cave without a thin spot.

The labs hold what the brief asks for: engines torn down on stands, wing structures with the fabric off,
wind tunnels, props and tools. The blackboards carry **real** equations, correctly written — lift
L = ½ρV²S·C_L, C_L = C_Lα(α − α₀), the drag polar C_D = C_D0 + k·C_L², best glide at C_D0 = k·C_L²,
load factor n = 1/cos φ with V_s√n beside it, the pitching moment M = C_m·½ρV²S·c̄, and Bernoulli
p + ½ρV² = constant. A flight school's blackboard with a wrong equation on it would be the one thing in the
room that gave it away.

## 4. Finding it, and getting in

- **Finding** the school is a hidden-place find — 10,000 points in the draft table (`configs/progression.json`),
  the largest single award there, awarded once.
- **Enrolment** needs the landing, confirmed by the owner: touch down on the strip and roll through the
  hologram. Flying through the face at speed without landing finds the place and earns the find, but does not
  enrol you. Enrolment is the wheels on the ground and the roll through the wall.
- The hologram is a visual surface with no solid behind it, so the airframe passes through. The rest of the
  cliff is solid rock, which means a mis-aimed arrival hits the wall. That seems right: the strip is the door.

## 5. The VOR on the crown — a real one

**Settled: a working VOR**, on the real Drake VORTAC's frequency in Arizona (owner: "use the same frequency as
the drake VOR in Arizona") — **114.10 MHz, ident DRK**, which in Morse is `-.. .-. -.-`. The real station near
Prescott is a VORTAC on TACAN channel 88; the game builds the VOR half.

Nothing in the sim receives a navaid today, so this is a new system and not a piece of scenery. What it needs:

1. **The station**: position, frequency, ident, service volume, and a cone of confusion overhead.
2. **A receiver**: tune a frequency, decide whether the signal is usable, and derive the radial the aircraft is
   on from the station's bearing. The Morse ident keyed over the audio, since that is how a pilot confirms the
   station is the one they tuned.
3. **An instrument**: an OBS with a course card, a CDI needle, and the TO/FROM flag — the one instrument that
   teaches the thing pilots actually find hard, which is that the needle does not care which way the aeroplane
   is pointing.
4. **The lessons that go with it**, which is where the teaching is: track a radial inbound and outbound,
   identify which side of the course you are on, and intercept a radial at an angle.

Worth being plain about the scope: (1) and (2) are small, (3) is a new panel instrument, and (4) is syllabus
work. It wants its own piece of work after Drake rather than being folded into the hill, and the station on
the crown can go in with the hill so the hardware is there waiting.

## 6. The advanced curriculum

Enrolment opens a second course — harder aeroplanes, tighter standards. The slot is small to build because
`SylStd` already takes the ACS tolerance as a parameter: the advanced course is the same judging machinery at
a tighter band (on the order of 0.6× the ACS limit) with its own aircraft list, and its lessons live in
`configs/challenges/` beside the six that are there now. **It ships empty** — the owner is writing the
syllabus himself, so this delivers the course, the gate and the standards, with no lessons in it.

Points buy aircraft and challenges; a lesson is still passed only by flying it to the standard, enrolled or
not.

## 7. The two aeroplanes

Both go through `docs/NEW-AIRPLANE-PROTOCOL.md` in full — book data, a dimensioned three-view, strips, mass
and inertia from a component breakdown, then the flight checks. Ids `jn4-jenny-like` and `at6-texan-like`.

**The Stearman already exists** — `configs/aircraft/stearman-pt17-like.json`, with a model picked in
`docs/research/AIRCRAFT-3D-MODELS.md` §10. Nothing to build there.

Models found for the two new types, both CC BY, neither used yet (owner's call first):

| Type | Model | Author | Licence | Faces | Note |
|---|---|---|---|---|---|
| Curtiss JN-4 "Jenny" | [sketchfab.com/…/curtiss-jn-4-jenny-65df9818](https://sketchfab.com/3d-models/curtiss-jn-4-jenny-65df9818a34c44f3a77764d665408f8a) | helijah | CC BY 4.0 | 45k | Good game weight. helijah publishes his own work — the most trustworthy family in the earlier survey. |
| JN-4 (alternative) | [sketchfab.com/…/curtiss-jn-4-jenny-stormworks-93918718](https://sketchfab.com/3d-models/curtiss-jn-4-jenny-stormworks-939187187edc42edb98a5804dc8c7f9b) | ThalesMML | CC BY 4.0 | 67k | Built for another game; heavier. |
| AT-6 / T-6 Texan | [sketchfab.com/…/north-american-t-6-texan-b5298f03](https://sketchfab.com/3d-models/north-american-t-6-texan-b5298f03e97e4624a41df3482fcfe508) | helijah | CC BY 4.0 | **233k** | The right aeroplane, far too heavy for a phone as it stands — it needs decimating to ~40k. |
| AT-6 (avoid) | north-american-t-6-texan by Ara_my45yt | CC BY 4.0 | 233k | Identical face count to helijah's: almost certainly a re-upload. Use the original. |

Nothing NC or ND. Credit goes in `docs/CREDITS.md` with the licence and the link, as with the Merlin audio and
the other models. "Beechcraft T6 Texan II" turns up in the same search and is the modern turboprop — wrong
aeroplane.

## 8. The three historic gliders — flyable, not scenery

The owner's answer to that question is to build them: **"build Lilienthal chanute and wright gliders for the
glider hill with appropriate control (weight shift) for each"**. Each gets the control the real machine had,
which is the interesting part and a new control model for this sim:

| Glider | Span | Control, as it really was |
|---|---|---|
| **Lilienthal Normalsegelapparat** (1894–96) | ~6.7 m | Pure **weight shift**. The pilot hangs by the arms and swings hips and legs to move the CG fore-aft and sideways. Fixed tailplane and fin; not one moving surface. Launched running off the hill. |
| **Chanute–Herring two-surface** (1896) | ~4.9 m | **Weight shift** again, pilot hanging below — but Herring's spring-mounted cruciform tail does some of the pitching itself, so the pilot moves far less than on the Lilienthal. The contrast between these two is the lesson. |
| **Wright 1902** | ~9.8 m | **Not** weight shift. Prone pilot; a **hip cradle** warps the wings for roll and is rigged to the movable rear rudder; a hand lever works the forward elevator. |

What this needs that the sim does not have: **a control that moves the CG instead of a surface**. The aero
model takes moments about the CG, so weight shift is a real addition rather than a re-mapping — the pilot's
mass becomes a movable item and the stick commands where it hangs. The Wright needs **wing warping** (a
twist distributed along the strips, not a hinged aileron) and the rudder **coupled** to the same input, which
is why it flew the way it did.

These are 15–25 mph wings at a Reynolds number an order below anything else in the hangar, so the aero model
has to be checked down there before the numbers are trusted: attached-flow lift curves and the drag build-up
both want a look at that scale, and no fudge factors go in to make them fly. The cambered sections stay
cambered (the symmetric-airfoil rule is a Remotion animation convention, not an aero one).

Each goes through `docs/NEW-AIRPLANE-PROTOCOL.md` like any other type, with the launch (running off the hill)
and the landing (the pillow pool) as their own cases. Models: CC BY or CC0 with the credit recorded, or built
from the three-views.

## 9. Order of work, and what each piece touches

0. **The tunnel runway behind the first waterfall** — the owner's other hidden place, and deliberately first:
   it is the same machinery at a quarter of the size (a bore cut in the height field, a runway inside it, a
   surface you fly through), so Drake's much bigger cave is the second one of these built rather than the
   first.
1. **The hill, the cliff, the strip and the VOR station** — `WorldTerrain`/`Landmarks`: a height-field bump
   with a vertical face, the grass surface for the visible 500 ft, the solids for the cliff, and the VOR
   hardware on the crown. The pilot can fly to it and land on it at the end of this step, and the cliff is
   solid: no way in yet.
2. **The chamber** — the dome, the floor, the mouth and the roof solid. The sea cave south of Avalon
   (`Seaside.SeaCave`) is the precedent: a chamber cut into a height field with a non-box roof solid the
   airframe collides with.
3. **The hologram, the find and enrolment** — the surface with nothing behind it, the discovery trigger, and
   the landing-roll test that enrols.
4. **The interior** — glider hill, pillow pool, the apron with parked trainers, the glass wall, the classrooms
   and labs, the blackboards, and the figures at work. The largest piece, and the one that decides whether the
   room feels like a school or a warehouse.
5. **The advanced course slot** — the gate, the tighter standards, the aircraft list, no lessons.
6. **The Jenny**, then **the AT-6**, each through the protocol. Independent of everything above; they can be
   built first if the owner would rather fly them sooner.
7. **Weight shift**, then the **Lilienthal** and the **Chanute** — the control model before the aeroplanes
   that need it, because there is no point in the airframes until the pilot can move his own weight.
8. **Wing warping with a coupled rudder**, then the **Wright 1902**.
9. **The VOR as a system**: receiver, OBS/CDI, Morse ident, and the lessons that use it (§5).

## 10. Open questions

Answered on 2026-10-16: the school has no name (§1); the 500 ft is only what shows and the runway carries on
inside (§2); the VOR is a working one on 114.10 / DRK (§5); only a landing roll enrols (§4); and the three
historic gliders are flyable aircraft with their own controls (§8).

Still open:

1. **Total runway length** — **912 m (2,992 ft)** is the proposal, 152 m of it outside the hologram and 760 m
   inside. Enough for anything that needs 3,000 ft, which is everything in the hangar bar the 737 and the H-4.
   Say if it should be longer; the chamber grows with it.
2. **The models** for the Jenny and the AT-6 (§7), and the AT-6 decimation from 233k faces.
3. **Whether the glider hill inside the cave is flyable at all.** A 70 m hill in still air inside a chamber
   gives a Lilienthal about 25 seconds of flight, which is right for the machine — but it means the three
   gliders are flyable *there* and almost nowhere else, unless they also get a hillside outdoors with a
   breeze on it. The Seaside bluffs and the escarpment faces already have slope lift modelled, so putting
   them somewhere outdoors as well is nearly free and probably wanted.
