# The marks the ground keeps

Ty 2026-11-10: *"if it is penetrating it should leave imprints behind as it slides and rolls displacing the dirt or
snow etc"*, and on how deep they go: *"I want the penetration to show and stay as ground scars if it is going to be
there and I can understand several cm on initial impact but just rolling down the hill it should be a few cm not a
dozen or more"*.

An aeroplane presses into soft ground, the ground yields, and when the load moves on the hollow is still there. The
mark is **the ground that did not come back**.

## The split

| | where | what |
|---|---|---|
| the record | `src/FlyingGame.Core/GroundImprints.cs` | the soil mechanics, the ring of marks, the pen that lays them |
| the feed | `src/FlyingGame.Sim/AircraftImprints.cs` | one pass per physics step over this aeroplane's contacts |
| the contacts it reads | `SurfaceContacts` (gear, skis, floats) and `Aircraft.ContactPoints` (airframe) | unchanged physics; it reads, it never writes |
| the drawing | Unity — **NOT WRITTEN YET** | reads `GroundImprints.World`, draws a quad per mark |

Engine-free, so it is on the Quest and the Mac for free, which is the parity requirement. **No particles** (a standing
prohibition of Ty's): a mark is geometry in the ground.

`GroundImprints.World` is process-wide, like `StrippedTrees` and `Forest`'s snapped tops, and cleared with them for a
new flight. Every aeroplane — player and AI — writes into it, so you can see where the other one landed, and each mark
carries its `Owner`.

## Depth: read from the contact, never recomputed

Two paths, because the two contacts measure different things. Getting this wrong is how two models end up disagreeing
about one rut.

- **Where the physics has a SINKAGE** (`SurfaceContact.DepthM` non-zero): a tyre's `TireModel.GroundSinkageM`, which
  is Bekker's pressure-sinkage compaction inverted out of the rolling resistance the aeroplane is already being charged
  for; a float's draft; a ski model's own sinkage when one exists. A plate-bearing sinkage is by definition the
  *permanent* set of the soil, so it **is** the mark: taken as it stands, with no retention applied.
- **Where it has none**, the mark is the plastic part of the whole **contact compression** — an *elastic* spring
  deflection by construction, which returns when the load leaves, so only `PlasticRetention01` of it stays:
  - an airframe hard point: `ContactPoint.DepthM`, recorded on each step's first stage;
  - a bearing **ski**, a **skid shoe**, a **float keel on land**, a pilot's **feet**: `SurfaceContact.PenetrationM`
    (from `WheelState.PenetrationM`). Nothing in the physics computes how far any of these compact the ground. Without
    this a Cub on SKIS left no track at all — the one surface Ty operates on where the mark is most of the point.

The two are never mixed for one part, and `WheelState.PenetrationM`'s own doc comment says which it is and why.

Nothing here invents, scales or clamps a depth. `GroundImprintTests.AnAirframeScarIsExactlyTheRetainedPartOfTheContactsOwnPenetration`
pins the equality. **Clamping the scar to a plausible few centimetres was REJECTED**: it would hide a penetration bug
behind a cosmetic cap and put a second depth model beside the contact's.

### Two depths that are wrong at their source today

1. **The airframe penetration.** `AirframeContact`'s penalty spring is mass-independent (150 kN/m, no position
   correction), so depth = weight/k: a J-3 wingtip slapping down reads **1.03 m** in and leaves an 82 cm scar. Being
   fixed on `lane/claude-contact-depth` ("a few centimetres, mass-independent"; Ty called a Pitts on its belly at
   3.3 cm about right). When that lands, the scars become a few centimetres with **no change here**.
2. **The tyre rut at an impulsive load.** `TireModel.GroundSinkageM` inverts a *steady-state* relation
   (R/W = z/((n+1)·l)), and a steady-state relation handed an impulsive load gives an impulsive answer: a J-3 put down
   on grass at 14 m/s reported a **306 mm** rut for the one step of the touchdown spike, against the 3.8 mm it settles
   to standing on the same grass. One geometric bound is applied here — a tyre's rut cannot exceed the tyre's own
   rolling radius, because past that the axle is at the surface and the gear leg is making the mark, not the tyre —
   which brings that spike to 229 mm. **The number is still wrong where it is MADE** and wants fixing in `TireModel`
   or in how `LandingGear` loads it. **NEEDS YOU.**

## Width: from the ground's bearing capacity, and the slump

A `ContactPoint` has no breadth — a hard point is a point — so the width comes from the one ground property that says
how much surface a load needs. The ground yields at `BearingPressurePa`, so a load N needs a patch of area N/p, taken
**square** (a square is the one assumption that adds no invented geometry). A tyre, ski or float measures its own
footprint and uses that instead.

Either way the trough **at the surface** is wider than the patch at the bottom, because the sides of an open hollow
slump back to the ground's angle of repose: `w = patch + 2d/tan(repose)`.

Measured consequences (hand-checked in `GroundImprintTests`):

- 820 kg on one patch on grass (200 kPa) → a 0.20 m patch. The same load on settled snow (60 kPa) → 0.37 m, √(200/60)
  wider. Softer ground, broader mark.
- A 1 kN wingtip bow on dirt → a 6.3 cm patch; at 3 cm deep the trough opens to 14.3 cm. Four times the bow's own
  width: that is what a gouge looks like.
- A Super Cub on skis in settled snow: a **0.338 m** track **1.9 cm** deep (the ski's own 0.289 m running surface plus
  the snow's slump). The same Cub on its bare tyres in the same snow: a **0.320 m** rut **11.5 cm** deep. Narrower and
  six times deeper — flotation, measured, through the real contact path.
- The slump adds the **same** amount to a belly scar and to a wingtip gouge, so it compresses their ratio — patches
  2.8× apart become troughs 1.9× apart. That is right: the slump is a property of the ground, not of the part.
- A ski's running surface is 2.5× a tyre's footprint, but on snow their *marks* are only 1.2× apart in width, because
  the deep narrow rut gains 15 cm of slump against the ski's 5. A ski's flotation therefore shows up in the **depth**,
  which is where a pilot reads it.

## Whether a ground marks at all

It falls out of the same numbers rather than being a special case. Asphalt's retention is 0 and its bearing pressure is
3 MPa, so a Pitts sliding down a runway leaves nothing (the black streak is `GroundMaterialRow.SkidMark`, not geometry).
Glacier ice is brittle: it scores and crushes, which is a *texture* and not the geometry this record holds, so its
retention is 0 too.

## The soil table

Three numbers per surface. Every one of them is sourced in the code.

| | bearing (kPa) | plastic retention | repose (°) |
|---|---|---|---|
| Asphalt | 3 000 | 0 | — |
| Deck | 200 000 | 0 | — |
| Ice | 5 000 | 0 | — |
| Gravel | 430 | 0.85 | 38 |
| Dirt | 250 | 0.80 | 37 |
| Grass | 200 | 0.35 | 60 |
| PackedSnow | 200 | 0.90 | 38 |
| Rough (pasture) | 180 | 0.50 | 45 |
| Sand (hard, wet-packed) | 150 | 0.90 | 32 |
| Snow (settled) | 60 | 0.95 | 38 |
| Water | 0 | 0 | — |

**Bearing pressure** is ultimate, from presumptive allowable values × their safety factor of ~3 (IBC Table 1806.2:
sandy gravel 3000 psf = 144 kPa; silty/clayey sand 2000 psf = 96 kPa), plus asphalt's 3-5 MPa unconfined compressive
strength and ice's 5-25 MPa crushing stress (Petrovic 2003, J. Mater. Sci. 38 — the source `WorldTerrain` already cites
for ice being rigid under a tyre). The order agrees with the model already in the repo: `WorldTerrain` gives sand a
worse rolling coefficient than dirt, and the Bekker sinkage that comes out of that puts a Cub at 10 mm in sand against
3.3 mm in dirt.

**Plastic retention** is a different property from strength: asphalt is strong *and* springy, snow is weak *and* keeps
every millimetre. Turf's root mat springs back and the soil under it does not (0.35); pasture has less mat (0.50);
cohesive earth yields with little rebound (0.80); gravel's stones rearrange and stay rearranged (0.85); damp sand holds
a footprint almost exactly (0.90); snow compaction is irreversible on the timescale of a flight (0.95).

**Angle of repose**: dry sand 30-35, gravel and crushed stone 35-40, firm earth 35-40, settled snow 35-40. Turf is the
odd one at 60: the root mat holds a near-vertical shoulder, which is why a tyre rut in grass has a sharp edge.

## What each contact leaves

| kind | from | what it looks like |
|---|---|---|
| `Rut` | a tyre rolling | its own footprint's width, the ground compacted straight down |
| `Skid` | a tyre locked, or slipping past its friction peak | a smear along the slip, not a compaction |
| `Track` | a ski or a float bottom | one broad, flat-bottomed track the width of the running surface |
| `Gouge` | a small hard part dragging: a wingtip bow, a tailskid, a fin tip, a gear stub | narrow, deep for its width, the surface torn open |
| `Scar` | a large structure sliding: a belly, a nose, a wing panel — a wreck down a hillside | the same mechanism, broad because a structure's breadth carries the load |

**Rolling against skidding is a slip RATIO, not an absolute slip**, and that was measured rather than argued: a J-3's
mains are splayed, so rolling straight down a grass strip at 14 m/s each main scrubs sideways at 0.65 m/s — a 2.7°
slip angle, an ordinary rolling tyre. Against a flat 0.3 m/s threshold every Cub main read as a skid for the whole
landing roll, which is both wrong and expensive. The line is `WheelSpin.FallStart` (0.15), the tyre model's own number
for where the coefficient starts falling from its peak toward the sliding value, with 0.3 m/s as a floor for the
standstill case where a ratio means nothing.

## The budget

**1,024 stretches for the whole world**, every aeroplane together (the precedent is `SprayField.MaxParcels` at 600, set
as an "iPhone / Quest budget"). The ring is hard-bounded; when it is full the **oldest** mark goes.

- **Memory** 88 B a mark → 88 kB, allocated once.
- **Drawing** one quad a mark → 2,048 triangles for every mark in the world. A single detail tree costs more.
- **Per step** one append-or-extend per contact in contact, at most a dozen per aeroplane. A handful of dot products
  each: no allocation, no dictionary, no terrain query — deliberately, after the J-3 was found 8.8× too expensive on
  2026-11-09 with a `ConditionalWeakTable` in the hot path. Nothing at all runs while the aeroplane is flying.

A stretch is **extended** while the contact keeps going the same way at much the same width and depth, so a straight
rut costs ONE stretch however long it is. What breaks a stretch:

- straying more than 0.3 of the mark's own width (floor 4 cm) off its line, **in 3D**, so the ground's undulation
  breaks one as surely as a turn does and a flat quad never stands off the surface by more than that;
- the width or the depth changing by more than 25 %, with absolute floors of 1 cm and 1 mm — a fraction *alone* was
  measured to be wrong and expensively so: a braked Cub laid dozens of zero-length stretches because its 3 mm rut
  deepened to 4 mm between two steps, which is 33 %. A quarter of nothing is nothing;
- the part lifting off, so a bounce is never joined up across the gap it flew over.

**Measured**: a J-3 put down at 14 m/s on grass and left entirely alone — no rudder, tailwheel castoring — rolls 58 m to
a stop and lays 135 stretches on three wheels. Almost none of that is the *length* of the roll: while it is rolling the
stretches run 2-4 m each, set by the arc the aeroplane is describing because nothing is steering it, and the rest is the
slow weave as it comes to rest. A roll held straight is one stretch per wheel. So 1,024 holds about **seven** landing
rolls of that kind and effectively unlimited straight rolling — a circuit's worth of landings and the taxiing between
them. A ground loop is the expensive case, because it is all curve; that is the right place for the budget to be spent.

## Not here

- **Fading.** A rut in soft ground does not fade on the timescale of a flight, and Ty asked for marks that "stay".
  Marks go only when the ring runs out. `TimeSec` is recorded so a renderer *can* weather them.
- **Mud** — surface-tracks **Q145**, open since 2026-11-06. `GroundMaterial` has no mud and no world surface produces
  one. When one exists it is three numbers in the table above: bearing near 50 kPa, retention ~1.0, repose near 25°,
  which would give the deepest, widest, longest-lasting mark on the map. Until then wet grass and dirt are what the
  island has.
- **A ski's SINKAGE.** Nothing computes how far a ski compacts snow, so a ski track's depth is the plastic part of the
  ski's contact penetration, which is the airframe's treatment and not a flotation model. A real pressure-sinkage model
  for a ski — the thing that would say a 0.29 x 1.98 m ski floats where a 8.00-6 tyre ploughs, by more than their
  penetrations happen to differ — is still wanted, and `lane/claude-ski-physics` is where it would come from.
- **Ice scoring**, which would be a mark with no depth. Nothing draws one.

## What the renderer still has to do (none of it is written)

1. **Draw the marks.** `GroundImprints.World` — rebuild when `Version` changes, one quad per `GroundImprint` lying on
   the terrain: `StartWorld`→`EndWorld` along, `WidthM` across, sunk `DepthM`. World NED (z down), converted as every
   other sim position is. `Kind` picks the look (a rut has tread; a skid is a smear; a track is smooth and flat-bottomed;
   a gouge and a scar are torn), `Material` picks the colour — `CrashDust.For` already holds a per-surface palette that
   agrees with the physics, and the darker/wetter version of it is what a fresh mark should be.
2. **Clear it for a new flight.** `unity/Assets/_Project/Scripts/Bridge/StructuralDamage.cs` already calls
   `Forest.ClearBroken()` and `StrippedTrees.Clear()` where a flight is reset; `GroundImprints.Clear()` belongs on the
   same line. **Until that line exists, marks carry over between flights in one session.** It is one line in a shared
   path, and this lane had already spent its one leading shared commit under the parity lock, so it is left to whoever
   lands the renderer.
3. **Nothing else.** No particles, no emitters, no second depth or width calculation. If the drawing wants a number
   this record does not carry, that number belongs here and not there.
