# Drake hill and the hidden school — build plan for the owner to approve

The owner's brief, 2026-10-16: a large hill called **Drake** with a VOR on top; a cliff on one side; what looks
like the start of a grass runway but is only 500 ft long, running right up to the cliff; the cliff face is a
hologram, and behind it a giant cave holding a flying school. Inside, on the **left**, a hill with
Lilienthal- and Chanute-style gliders flown down it into a "swimming pool" of pillows. On the **right**, rows
of parked trainers — 2-33s, Jennys, Stearmans, AT-6s — with students, instructors and mechanics at work, and
**behind them, through glass**, classrooms and labs: engines torn down, wing structures, wind tunnels, every
tool for teaching how an aeroplane works. Blackboards with drawings and equations. Land, roll through the
hologram, and you are enrolled in a harder curriculum in harder aeroplanes to higher standards.

Nothing below is built yet. Numbers are the proposal; the geometry gets pinned against the height field before
anything is laid down.

## 1. The name

The brief says the school is influenced by a Richard Bach story and asks that the story's own name not be
used. Agreed, and it goes further than the sign: no names, characters or lines from it anywhere in the
in-world text. What carries over is only the idea — a hidden place where flying is pursued for its own sake,
to the limit of how well it can be done. Five original names, owner's pick:

| Name | Why |
|---|---|
| **The Hollow** | What the students would actually call it. Plain, and it belongs to the geography. |
| **Lilienthal Hall** | Otto Lilienthal is history, not fiction; it salutes the gliders on the hill. |
| **The Inner Field** | An airfield inside a hill, and the discipline is inward. |
| **Still Air Academy** | Inside a cave the air never moves — the one place a wing can be judged honestly. |
| **Drake Deep School of Flight** | Ties the school to the hill the pilot found it in. |

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
| Grass strip | **152 m (500 ft)**, level, running north→south at the cliff's foot, ending at the rock |
| Overrun | 30 m of grass between the strip's end and the cliff, so a well-flown landing rolls in |
| Cave mouth | 50 m wide, 24 m tall — a Stearman or a Jenny fits with room to spare |

**500 ft is very short, and that looks deliberate.** A Cub, a Jenny or the 2-33 can make it; a 172 cannot land
in 500 ft, so of the two free aeroplanes only the glider gets in. Worth confirming that is the intent — it
makes the school something you earn your way into rather than something you find.

## 3. The chamber

Entering southbound through the mouth, with **left = east** and **right = west**, as the brief has it:

| Piece | Size |
|---|---|
| Chamber | 700 m deep (into the hill), 420 m wide, **140 m** to the crown of the dome |
| Roll-out | 300 m of floor straight ahead of the mouth: enough to stop, and to take off again |
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
- **Enrolment** needs the landing: touch down on the strip and roll through the hologram. Flying through the
  face at speed without landing finds the place but does not enrol you. **Owner to confirm** — the brief says
  "lands and rolls through", so that is how it is written unless he says a fly-through counts too.
- The hologram is a visual surface with no solid behind it, so the airframe passes through. The rest of the
  cliff is solid rock, which means a mis-aimed arrival hits the wall. That seems right: the strip is the door.

## 5. The advanced curriculum

Enrolment opens a second course — harder aeroplanes, tighter standards. The slot is small to build because
`SylStd` already takes the ACS tolerance as a parameter: the advanced course is the same judging machinery at
a tighter band (on the order of 0.6× the ACS limit) with its own aircraft list, and its lessons live in
`configs/challenges/` beside the six that are there now. **It ships empty** — the owner is writing the
syllabus himself, so this delivers the course, the gate and the standards, with no lessons in it.

Points buy aircraft and challenges; a lesson is still passed only by flying it to the standard, enrolled or
not.

## 6. The two aeroplanes

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

## 7. Order of work, and what each piece touches

1. **The hill, the cliff, the strip and the VOR** — `WorldTerrain`/`Landmarks`: a height-field bump with a
   vertical face, the grass surface for the strip, the solids for the cliff, the VOR as scenery. The pilot can
   fly to it and land on it at the end of this step, and the cliff is solid: no way in yet.
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

The hang gliders on the hill are scenery flown by the scene, not simulated airframes — a Lilienthal glider
is not in the fleet and does not need to be. If the owner wants to *fly* one down that hill, that is a
separate aeroplane and a separate decision.

## 8. Open questions

1. Which name (§1).
2. Is the 500 ft strip meant to keep most of the fleet out (§2)?
3. Does a fly-through without landing enrol, or only a landing roll (§4)?
4. The VOR: scenery only, or a working navaid? Nothing in the sim receives a VOR today, so a working one is a
   new instrument and a new system — worth doing, but it is its own piece of work and not part of this.
5. Which Jenny and which AT-6 model, and the AT-6 decimation (§6).
