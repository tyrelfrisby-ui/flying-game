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
| the drawing's geometry | `src/FlyingGame.Core/GroundImprintMesh.cs` | the look of a mark, and the vertices that draw it — engine-free, so every platform gets it |
| the engine's share | `Bridge/GroundImprintVisual.cs` + `Shaders/GroundImprint.shader` | convert NED to Unity, upload, choose how often |

Engine-free, so it is on the Quest and the Mac for free, which is the parity requirement. **No particles** (a standing
prohibition of Ty's): a mark is geometry in the ground.

`GroundImprints.World` is process-wide, like `StrippedTrees` and `Forest`'s snapped tops, and `Clear()` is called with
them for a new flight from `StructuralDamage.Repair` (2026-11-10, lane/claude-imprint-render: until that one line
existed, a session's marks were still lying there in the next flight). Every aeroplane — player and AI — writes into
it, so you can see where the other one landed, and each mark carries its `Owner`.

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

### One depth still wrong at its source, and one that was

1. **The airframe penetration.** `AirframeContact`'s penalty spring is mass-independent (150 kN/m, no position
   correction), so depth = weight/k: a J-3 wingtip slapping down reads **1.04 m** in and leaves an 83 cm scar. Being
   fixed on `lane/claude-contact-depth` ("a few centimetres, mass-independent"; Ty called a Pitts on its belly at
   3.3 cm about right). When that lands, the scars become a few centimetres with **no change here**.
2. ~~**The tyre rut at an impulsive load.**~~ **FIXED 2026-11-10** (lane/claude-imprint-render) — and the diagnosis
   this paragraph used to carry was wrong. It is not an impulsive load: **it is a GRAZING TOUCH.**

   Expand `GroundSinkageM` and the footprint length *l* cancels exactly, leaving
   *z* = ground·(p_g/p_ref)^(1/n)·l_ref·(n+1): the rut is a function of the **ground pressure alone**. And p_g was
   taken as the leg's whole normal load over the *carcass's* footprint at its own deflection, which are not one
   tyre's two numbers — the footprint is the carcass's at δ, the load includes the leg spring and the leg damper.
   Measured on a J-3 put down on grass at 14 m/s: the worst step in 2,400 carried **281 N** — a sixth of a main's
   static load, a sixty-fifth of that landing's 18 kN peak — on a 10 × 13 mm footprint, so p_g read **2,806 kPa** and
   the rut came out **558 mm**. At the genuinely impulsive load the model was always fine: the same landing's
   11,973 N peak reads 111 kPa over a 211 × 366 mm patch and a 5.5 mm rut. z ∝ (load/δ)^(1/n), and 1/n is 1.43 on
   turf, so it is δ → 0 that diverges, not the load.

   `TireModel.GroundRutM` evaluates the **same** relation at the pressure the tyre's **carcass** is transmitting
   (`CarcassGroundPressurePa`: TR R-64 eq. 24 over eq. 12 — one tyre, one deflection, so it is self-consistent and
   therefore bounded between 0.42 and 1.04 of (p + 0.08·p_rated), which is the classical statement that a pneumatic
   tyre's mean ground pressure *is* its inflation pressure). An undeflected carcass cannot transmit 2.8 MPa through a
   square centimetre; if it were, it would be deflected.

   **Nothing the aeroplane feels changed.** The rolling resistance it is charged for still comes from the whole load;
   only the rut the surface tracks read is re-evaluated, on the first stage, per wheel. Measured after: grass
   558 → **10.1 mm**, dirt 489 → 8.8 mm, snow 300 → 52.6 mm, and the deepest rut of a landing is now at its heaviest
   load in every case — a J-3 dropped in from a metre with no sink rate (22 kN peak) presses 13.3 mm on grass and
   58.8 mm in snow, against 4.6 and 36.6 mm for a gentle arrival. Ty's "several cm on initial impact", out of the
   physics rather than out of a clamp.

   REJECTED, and still rejected: recomputing the sinkage from a filtered load. There is no filter and no state in
   this — the same relation, at the same deflection, with the tyre's own two numbers instead of two parts'.

   The snow **crust** zones keep the old path: there `RollingCoefficient` is not in play at all (`SnowSinkageRolling`
   is), *l* does not cancel, and a graze takes the rut to zero on its own. The geometric bound in `AircraftImprints` —
   a tyre's rut cannot exceed its own rolling radius — stays; it costs nothing and nothing reaches it any more.

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
- **Drawing** 18 triangles a mark → 18,432 for every mark in the world, in **one mesh and one draw call**, inside a
  16-bit index buffer (16,384 vertices). Still a fraction of one of ForestBuilder's detail trees. Not the one quad
  this note first reckoned on: *The drawing* below says why a quad is not enough.
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

## The drawing

`GroundImprintDrawing` and `GroundImprintMesh` (Core, engine-free) turn the ring into vertices; `GroundImprintVisual`
converts them to Unity's frame, uploads them and chooses how often. **No particles.** No second depth or width
calculation: everything the drawing needs that the record does not already carry lives in `GroundImprintDrawing`, with
the record, and not in Unity.

**A mark is not a hole in the geometry.** It is a flat sheet lying ON the ground, and its depth is in its ALPHA. The
record says 3.3 mm and the drawing makes that flattened, bruised turf — that is the other half of the bargain that let
`MinDepthM` stay at 1 mm instead of the rejected 5.

- **16 vertices, 18 triangles, a 4 × 4 grid.** Along: a cap, the start, the end, a cap. Across: shoulder, floor,
  floor, shoulder. One flat quad was not enough on either axis. A curving track is a CHAIN, and consecutive stretches
  are one physics step apart — 12 cm at a 14 m/s touchdown — so without a cap of half a width a landing roll is a
  dotted line. And the cap's alpha is **derived, not chosen**: alpha-blending compounds, 1 − (1 − c)², so full-alpha
  caps draw a dark bead at every joint (one every 2–4 m) and zero-alpha caps leave a light band. The c that makes the
  overlap read back *exactly* the mark's own alpha is **c = 1 − √(1 − a)** — 0.163 for a faint 0.3 mark (0.54 of
  it), 0.613 for a saturated 0.85 one (0.72 of it), exact at both, which no fixed fraction can do. An isolated end
  then fades to that and stops
  instead of being chopped square.
- **The shoulders** are the outer columns, drawn at **sin(repose)** of the floor's alpha: how steeply the slumped side
  turns away from the light. Nothing tuned — turf's root mat holds a 60° shoulder and reads 0.87, nearly as dark as
  the floor, which is why a rut in grass has a sharp edge; dry sand's 32° reads 0.53 and fades out at the sides.
- **Alpha saturates with depth**, 1 − exp(−d/8 mm), because contrast saturates: a 20 cm rut does not look five times
  more like a rut than a 4 cm one. It puts the marks the record actually holds across the visible range — a Cub's
  3.3 mm rut on dirt at 0.34 of full, 5.6 mm on grass at 0.50, a ski's 1.9 cm track at 0.91, a bare tyre's 11.5 cm rut
  saturated — with a floor of 0.12, because drawing what the record kept at an alpha nobody can see would put the
  rejected 5 mm floor back in wearing a different hat.
- **The colour is the surface's own**, from `CrashDust.For` — the per-surface palette a crash's dust already uses,
  keyed on the same `WorldTerrain.Surface` the physics decided — darkened. The darkening is read off the renderer's
  own light model rather than picked: the shader lights a mark exactly as `FlyingGame/IslandTerrain` lights the ground
  under it, sky + sun·d, and ground a hollow's own walls keep the sun off is lit by the sky alone, which is
  .50/.94, .57/.98, .64/.98 = **0.59** of it. A gouge or scar has also brought up the damper subsoil (0.75 of the
  albedo), so 0.44; a skid has smeared the surface without opening a hollow to shade itself with, so 0.66. **On soft
  ground a skid marks it less than a rolling tyre does**, which is right: the famous black streak is on PAVEMENT,
  where nothing marks at all and the streak is `GroundMaterialRow.SkidMark`, not geometry.
- **On the surface, on a slope too.** Every vertex is sampled against `WorldTerrain.GroundHeightAt` — the analytic
  ground the wheels run on — so a mark follows the ground across its width as well as along its length. The drawn
  island mesh is that same height function on a 25 m lattice, so the two differ by the lattice's own curvature:
  MEASURED over the island's land, on ground flat enough to roll on, the median gap is 0 mm and the 95th percentile
  38 mm — and where it is larger the AEROPLANE stands off the drawn mesh by the same amount, so a mark is always
  under the wheel that made it, which is the only consistent choice there is. Every ground that marks at all is open
  terrain (`Marks()` is false for asphalt, a deck, ice and water), so there is no runway, carrier deck or cave floor
  to special-case.
- **The lift is 5 cm, and the shader's depth bias is CONSTANT, and both of those are the first real render's doing.**
  At 2 cm with a purely slope-scaled `Offset`, the marks were plainly visible from half a metre up and **absent from
  every plan view** — the signature of losing the depth test face-on, where the slope term contributes nothing. Over
  the AP1 strip against its own 25 m lattice the drawn mesh stands up to **120 mm** above the analytic surface, the
  95th percentile is **47 mm**, and a 2 cm lift buried the mark over **14.8 %** of the strip. (On the 5 m sub-lattice
  a shaped field gets at LOD0 it is ±9 mm and nothing is buried — but which LOD is drawn is the renderer's business,
  so the lift has to survive the coarse case.) 5 cm clears the 95th percentile of both, and `Offset -2, -200` puts
  the bias in the constant term where a face-on view can use it.
- **Incremental, and not every frame.** `GroundImprintMesh.Refresh` rewrites only the marks that changed — at most a
  dozen a step, one per contact in contact — and `GroundImprintVisual` calls it at 20 Hz, which leaves the growing end
  of a rut up to 0.7 m behind a 14 m/s wheel (under the aeroplane, where it cannot be seen) and spends a twentieth of
  the vertex traffic. Without both, a landing roll re-uploads 16,384 vertices on every step of the step loop.

### The plates, and the four ways the harness refuses to lie

`unity/Assets/_Project/Scripts/Editor/GroundImprintShots.cs` shoots the review plates (plan from 30 m, eye height
beside the tracks, a close low look along one rut) on the real island, from marks an aeroplane lays by rolling.

It needs a GPU — **not** `-nographics`, which is what the build scripts use — and it takes its shader from the
**asset database**, because `Shader.Find` returns null for this project's own shaders in a bare `-executeMethod`
batchmode editor (measured: null for `FlyingGame/GroundImprint` and for its `FlyingGame/UnlitTransparent` fallback
alike, both present and compiling clean). It hands it over through `GroundImprintVisual.ShaderOverride`, set
**before** `WorldBuilder.BuildAll()` because `IslandWorldBuilder.Build` attaches the visual itself. The game keeps
`Shader.Find`, with `BuildScript.EnsureAlwaysIncludedShaders` keeping the shader in the player.

It refuses in four places rather than produce a picture that is not what it says it is. All four are things that actually happened on 2026-11-10:

1. **no graphics device.** With `-nographics` it wrote one blank grey PNG and then segfaulted, exit 139. The crash is
   a nuisance; the blank PNG was nearly relayed on as "the real island render".
2. **no material** — the shader is missing, the ground keeps its marks and nothing draws them, which on screen is
   indistinguishable from a flight that left none.
3. **stretches in the record, no triangles in the mesh.**
4. **ground that is not the surface the scene claims** — the one that got through. The first version set
   `WorldTerrain.SurfaceOverride`, which steers the physics and not the island's shader, and rolled every scene down
   the AP1 grass strip: three plates labelled "snow" were snow-coloured marks lying on green turf, and the
   ski-against-tyre comparison — the most persuasive thing in this lane — could not be seen in any of them, because
   both aeroplanes were on the same grass. Each scene now rolls on ground that really is its surface (a measured
   snowfield at sim −4600, −4600 on 225°: **295 m of snow at a worst slope of 0.3°**, the flattest long run on the
   island), nothing is overridden, and the surface is checked along the whole length of the intended roll.

### What a landing roll looks like, and what is still somebody else's

A J-3's touchdown roll comes out DASHED for its first second or two and then goes continuous. That is the physics
talking, and it is honest: over a 6 s roll at 14 m/s on grass a tyre is in contact on only 919 of 1,200 steps — the
Cub is chattering on its own gear — and the record never joins a stretch across a gap the wheel flew over.

The **airframe** scars are still enormous — a wingtip's gouge draws as a black blot — because `AirframeContact`'s
penalty spring is mass-independent and a J-3 wingtip reads 1.04 m into the ground. That is `lane/claude-contact-depth`,
and when it lands the scars become a few centimetres with **no change to any of this**.

### Open: the contrast, measured against the real frames and NOT yet changed

Measured off the first real renders (`low-cub-grass.png`, the band where the tracks are resolved): the marks darken
the ground by **7.7 % of mean luminance** where the terrain's own banding is **4.4 %** — a ratio of **1.75×**. The
model predicts about **16 %** at full width (alpha 0.40 at a 5.8 mm rut × the 0.41 the darkening takes out), and the
gap between 16 and 7.7 is pixel coverage: at that range the rut is a few pixels and is averaged down. So the drawing
is doing what it was built to do; the question is whether what it was built to do is enough.

**The flaw in the 0.59 is now visible, and it is a conflation.** 0.59 is the sky-only illumination of a hollow whose
own walls take the sun off it — the FULLY self-shading case. A 5.8 mm rut 216 mm across does not shade itself at all:
geometrically its walls subtend sin(atan(2d/w)) = **0.054**, five per cent, not forty-one. What actually makes a tyre
track on turf visible is not shadow but **disturbance** — flattened, bruised, compacted surface — which does not
depend on the trough's depth-to-width ratio at all. The honest model is those two terms separately:

> darkening = self-shadow, sin(atan(2d/w)), geometric and derivable · disturbance, which is a property of the
> surface being broken rather than of its shape

**Not done tonight, deliberately.** The disturbance term is a number nobody has measured, and the three frames it
would be judged against were broken in two other ways at the time (three of four scenarios never showed their real
surface, and no plan view showed a mark at all). Measuring a contrast on frames like that is premature; the decision
belongs to the next render, with the surface and the depth bias fixed.

### Still not done

- **No tread, no texture.** A rut's `Kind` picks its alpha and its darkening, not a pattern: the record distinguishes
  a rut's tread from a skid's smear and a gouge's torn edge, and the drawing so far distinguishes only how dark and
  how plain each is. A tread or a torn edge wants a texture or a noise term in the shader, and it is the obvious next
  thing to add.
- **No weathering.** `TimeSec` is recorded and nothing reads it, here as in the record.
