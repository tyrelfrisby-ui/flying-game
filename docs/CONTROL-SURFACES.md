# Control surfaces: hinged sections, control power, stick-free float

Owner 2026-10-06: *"the previous modeling with each control surface acting as its own 'wing' has me wondering how
accurate our control power calculations were … research both control power and stick free control position now that
the hinged surface model has been adopted."*

## How a surface is built

Each lifting surface is a set of spanwise strips. A control surface is split into a FIXED row (wing / stabiliser / fin)
and a HINGED row (aileron / elevator / rudder) at the same stations. A deflection δ:

- turns the hinged strip (gain ±1): it sees its local α + δ;
- adds camber to the fixed strip ahead of it (gain g): it sees α + g·δ.

The section's lift change per δ, relative to its lift slope, is the flap effectiveness **τ = E + (1 − E)·g**, with
**E = c_f / c** the hinged chord over the whole section chord.

## What was wrong

| Issue | Effect | Fix |
|---|---|---|
| Induced drag per PIECE: the wing and its aileron row each had their own aspect ratio (aileron row ≈ 50+) | 172 induced drag ~20 % low, best glide 60 kt (POH 65) | `AeroModel.SystemAspectRatio`: one AR per lifting system |
| Stabiliser strips sampled `naca0012-like`, a **2-D** section (6.3 /rad), with no finite-span correction | Tail ~1.7× too strong: stick-fixed static margins 25–70 % MAC across the fleet | `AeroModel.SectionTableKey`: 2-D tail tables scaled to the DATCOM/Helmbold slope for the system AR (fins × 1.55 end-plate) |
| Elevator/rudder strips on a different table (`fin-lowAR`, 3.7 /rad, stall 30°) than the fixed surface | The hinged share of section lift and its stall were inconsistent with the section | Hinged strips fly the fixed surface's section table |
| Fixed-strip gain hand-set to 0.45–0.55 everywhere | τ (theory-level, inviscid) regardless of chord ratio | g from thin-airfoil τ(E) × 0.85 (viscous, DATCOM small-δ ratio for t/c ≈ 12 %) |

### Pitch stability: what else the research turned up

| Issue | Effect | Fix |
|---|---|---|
| Each piece of a hinged section loaded at its OWN quarter-chord | the section's α-lift acted at ~37 % chord; the 172's aileron row (0.9 m behind the CG) added ~0.3 of spurious Cmα stability; same at the tail | `AeroModel.LoadPoint`: a fixed/hinged pair loads at the SECTION quarter-chord, LE − ¼(c_f + c_h) |
| No fuselage pitch moment | every type with an honest CG and tail came out 25–45 % MAC stable | Gilruth & White (NACA TR 711): M = 57.3·K_f·q·w²·L·½sin 2α, K_f from the wing root's position along the body (`FuselageMunkM3`); `fuselage.munkScale` per type (Gee Bee 0.55 → +3 % MAC, owner: "just barely stable") |
| No tail dynamic-pressure ratio | tail ~10 % too strong | η_t = 0.9 (DATCOM 0.85–0.95; the slipstream raises it under power) |
| CG of the 172, Super Cub, bush Cub (commit c01221c, 2026-09-15), SR22, Archer and Glasair set from the strips as if `pos` were the LEADING EDGE (it is the quarter-chord, DATA-CONTRACTS) | CG near 50 % MAC instead of 27 %; oversized tails and the 2-D tail slope hid it | CG back to 27 % MAC (gear moved with it, so ground handling is unchanged) |
| 172 horizontal tail 3.71 m², elevator 32 % chord | — | 3.35 m², elevator 40 % (Wolfram System Modeler 172: stab 2.0 m² + elevator 1.35 m²) |

### Static margin, stick fixed (% MAC), before → after

| Type | Before | After |
|---|---|---|
| Skyhawk | 29 | 24 (UIUC 172 data: 17; Cmδe −1.52 vs −1.28) |
| Archer | 28 | 21 |
| SR22 | 26 | 14 |
| Glasair III | 21 | 15 |
| Super Cub | 15 | 18 |
| 2-33 | 18 | 6 |
| P-51 | 35 | 12 |
| Extra 300 | 42 | 9 → **17.1** (surfaces rebuilt 2026-10-28) |
| Pitts S-2B | 44 | **12.9** (surfaces rebuilt + CG from TCDS A8SO, 2026-10-28) |
| Pawnee | 32 | 4 |
| Gee Bee R-2 | 35 | 3 |
| 737 | 53 | 18 |

Real light aircraft fly at roughly 5–20 % MAC. Still high (CG or tail geometry to check against data): EB29R 42,
Stearman 26. `PitchStabilityTests` prints them all and fails any type that is unstable. The **Pitts** was the worst of
them at 44 %; it is now 12.9 % — see "Pitts and Extra: the surfaces rebuilt from the models" below.

### Full-aft authority (StallAuthorityTests, every type now)

With real stability most types pulled far past the stall at full aft (737 45°, Beaver 36°, Extra 34°). The protocol's
plain-flap saturation is now calibrated for every type. Low values (< 8°: F-86 3.0, Gee Bee 4.9, Swift 5.2, 2-33 6.0,
Pawnee 7.5, Cassutt 7.8, SR22 7.9) mean an elevator large for the type's stability — candidates for real geometry.
Rejected and left PENDING: the Stearman (too stable to reach its target); the Cub on floats (×1.4 chord for 0.5°)
keeps its elevator. The **Pitts** was on that list — the calibrator wanted its elevator chord ×3.6, and it flew on a
non-physical 120° saturation — until its surfaces were rebuilt on 2026-10-28; it now calibrates to 22.9° like any other
type (20.3°) and balances full aft on its 18° target.

## Pitts and Extra: the surfaces rebuilt from the models (2026-10-28)

Owner: *"with the new extra and pitts models i want you to recheck all of the dimensions and sizes for lifting surfaces
wings, ailerons, stabilizers and elevator/rudder. adjust the formulas for the control powers as needed based on the
updated measurements and stability. check the fuselage aerodynamic contribution as well."*

Both configs predated the models they are now drawn as. Every lifting surface was re-measured off the GLB — the Pitts
from v12, whose stab, elevator, fin and rudder outlines are traced from the Aviat factory three-view
(`docs/models/pitts-s2b-own-PROVENANCE.md`), and the Extra from its own build. Method: each surface is sliced into
cosine-spaced spanwise bands (the spacing the configs already used — it reproduces their station list to 1 mm); a band's
area is a rasterised silhouette **clipped to the band**, so two bands cannot claim the same square metre, and its chord
is the mean of thin-slab chords sampled across it, which gets a planform break — an aileron ending mid-band — right
where a single centre slice would not.

### What was wrong

| | config before | model | |
|---|---|---|---|
| Pitts horizontal stab | 1.503 m² | 1.161 m² | +29 % |
| Pitts fin | 0.644 m² | 0.356 m² | +81 % |
| Pitts rudder | 0.347 m² | 0.514 m² | −32 % |
| Pitts rudder vertical centre | 0.81 m above the body axis | 0.26 m | 0.55 m high |
| Pitts tail arm | stab q/c at −3.14 m | −2.87 m | 0.27 m long |
| Extra wing span | 7.43 m | 7.99 m | −7 % (published 8.00) |
| Extra aileron | 1.498 m² | 1.872 m² | −20 % |
| Extra stab span | 2.80 m | 3.19 m | −12 % (published 3.20) |
| Extra elevator | 0.947 m² | 0.728 m² | +30 % |
| Extra fin | 0.887 m² | 0.445 m² | +99 % |
| Extra tail arm | stab q/c at −2.88 m | −3.98 m | **1.10 m short** |

Two of those are structural rather than cosmetic. The Extra's tail arm was a quarter short, so its pitch and yaw
stiffness and its elevator and rudder power were all being derived off the wrong lever — and its fin was twice the
drawn size, which is how it came out at a plausible 9 % MAC: two errors cancelling. And its config span being 7 %
under the published 8.00 m meant the engine was **shrinking the new model** to fit a flight model that was too small.

The fixed and hinged chords also used to overlap. The old Pitts aileron row sat 0.196 m forward of the wing's own
trailing edge, so `AeroModel.BuildLoadX` reconstructed a section chord of 1.313 m where the real section is 1.048 m —
19 % long, on the lever arm that sets Cmα. Rebuilt, the two rows meet at the hinge line to within 6 mm.

### Areas are scaled to the published total

A uniform scale cannot change a ratio, and these meshes were only ever checked on length/span and height/span, never on
area. So the measured spanwise **distribution** is kept and the areas are scaled to the published total: the Pitts from
11.33 to 11.6 m² (×1.023, Aviat brochure / Jane's) and the Extra from 10.02 to 10.84 m² (×1.082, the reference pack's
`extra300/SPEC.md`, from NZ CAA TAR 8/21B/31 Rev 1). Nothing else is scaled — arms, chords, heights and stations are
the measurements.

### Control power is derived, not hand-set

The fixed-strip gain now comes from the chord ratio the model actually has, through the τ(E) table below:
g = (0.85·τ(E) − E)/(1 − E). The hand-set 0.45 everywhere was never that.

| | E = c_hinged/c_section | gain before | gain now |
|---|---|---|---|
| Pitts aileron, upper / lower | 0.248 / 0.236 | 0.450 | 0.356 / 0.351 |
| Pitts elevator | 0.409 | 0.379 | 0.393 |
| Pitts rudder | 0.446 | 0.450 | 0.395 |
| Extra aileron | 0.271 | 0.450 | 0.365 |
| Extra elevator | 0.308 | 0.379 | 0.376 |
| Extra rudder | 0.473 | 0.450 | 0.394 |

Leading-edge sweep is measured too, from a least-squares fit of the LE against span, rather than copied: the Pitts'
upper wing 5.6° (its provenance says "about 6°"), its stab 21.6° and its fin 38.4° where the config said 30°.

### What it did

| | before | after |
|---|---|---|
| Pitts static margin | 44 % MAC | **12.9 %** |
| Pitts elevator saturation | 120° (non-physical) | **20.3°**, full aft balances on its 18° target |
| Pitts horizontal tail volume V_h | 0.60 | 0.47 (aerobatic band 0.35–0.45) |
| Extra static margin | 9 % MAC | **17.1 %** |
| Extra elevator saturation | 13.5°, full aft 4.8° short of target | **48.7°**, on target |

The Pitts' 12.9 % needed its CG as well as its tail: on the rebuilt surfaces alone, with `mass.cg` where it was, it
came out at 2.2 % MAC — stable, but on the edge.

### The Pitts' CG, against the type certificate

The config datum is the **lower wing leading edge**: the model's origin sits exactly there (`Wing_Lower` runs from
x 0.000 to 1.016, and 1.016 m is the published 40-inch constant chord), and `GearMatchesTheModels` puts the config's
wing within 12 mm of the model's, so the two frames coincide.

**FAA TCDS A8SO, Model S-2B** (Revision 21, 2005): datum **91.81" forward of the lower wing leading edge**; C.G. range
Acrobatic **(+89.58 to +90.50) at 1625 pounds**, Normal (+88.50 to +90.20) at 1700 pounds, (+86.35) at 1475 lb or less.
Our `massKg` is 737 = **1625 lb**, which is exactly the acrobatic category maximum, so the acrobatic range is the one
that applies. Subtracting the datum offset:

| | CG, relative to the lower wing LE |
|---|---|
| TCDS acrobatic @ 1625 lb (our weight) | **0.033 – 0.057 m FORWARD** |
| TCDS normal @ 1700 lb | 0.041 – 0.084 m forward |
| TCDS, 1475 lb or less | 0.139 m forward |
| config before 2026-10-28 | 0.030 m **AFT** — the wrong side of the leading edge, ~3 in outside the envelope |
| config now | **0.045 m forward** — the acrobatic mid-range |

So the certificate confirms what the aerodynamics were asking for, and the old CG was not merely mis-placed within the
envelope but on the wrong side of the datum feature entirely. At the certified mid-range the static margin is 12.9 %
MAC and the elevator calibrates to 20.3°.

The 0.20 m **down** is still derived, not certified: the datum sat 1.33 m above the wheels on an aeroplane 2.17 m tall,
and a taildragger with its CG that high ground-loops and noses over on the brakes. TCDSs do not give a vertical CG.

### Against the type certificates (Ty asked, 2026-10-28)

Both types were fact-checked against their certificates. Two things came out of it.

**The Pitts' CG was on the wrong side of its datum feature** — see the section above. The conversion is corroborated
by the seat arms: A8SO puts the two seats at +105.15" and +136.50", which is 13.34" and 44.69" aft of the lower wing
leading edge, and the model's provenance independently recorded "front ~13 in and rear ~45 in aft of the lower-wing
LE". So the datum conversion and the model's origin agree.

**The Extra's CG is already inside its envelope**, and needed no change. EASA.A.362 Issue 09 gives the EA 300's datum
as the **plane of the firewall** and the C.G. range at 820 kg or below — `massKg` is 820 — as 75.0 to 89.8 cm aft of
it. Taking the firewall at the aft face of the engine (`Engine` ends at config x +0.79; the model carries no firewall
node, so this is ±0.05 m), that is config x **+0.04 to −0.11**, against `mass.cg` of **−0.02**: inside, a little
forward of the mid-range. Its 17.1 % MAC static margin agrees.

**Control-surface travel was over-certified on both, and is now the certificate's.** Both configs carried
`maxDeflRad` 0.52 rad — 29.8° — on all three axes, which is a deflection limit, and therefore control power, that
nobody had sourced.

| | certified | was | now |
|---|---|---|---|
| Pitts aileron (symmetrical) | 25° ± 2° (A8SO) | 29.8° | **25°** |
| Pitts elevator | 27° ± 0.75° (A8SO) | 29.8° | **27°** |
| Pitts rudder | 30° ± 0.75° (A8SO) | 29.8° | 30° |
| Extra aileron | 30° ± 2° (EASA.A.362) | 29.8° | 30° |
| Extra elevator | 25° ± 2° (EASA.A.362) | 29.8° | **25°** |
| Extra rudder | 30° ± 2° (EASA.A.362) | 29.8° | 30° |

The Pitts loses 19 % of its aileron travel and 10 % of its elevator; the Extra loses 19 % of its elevator. Both
elevators were re-calibrated afterwards and both land exactly on their full-aft targets: Pitts saturation 20.3° for
an 18.0° balance, Extra 48.7° for 20.0°.

### The fuselage

The body terms were measured the same way, as a rasterised union silhouette of the fuselage, canopy, cowl and engine —
not a bounding box, and not a sum of per-part projections, which double-counts a cowl over a fuselage and counts the
cockpit interior twice. The gear is left out: its drag is `LandingGear`'s.

| | Pitts before → after | Extra before → after |
|---|---|---|
| `crossflow.lengthM` | 4.00 → 5.19 | 6.65 → 6.94 |
| `crossflow.bodyRadiusM` | 0.40 → 0.498 | 0.55 → 0.533 |
| `crossflow.planArea` | 2.80 → 3.35 | 2.20 → 4.66 |
| `crossflow.planCenterX` | −0.40 → −0.36 | −0.30 → −0.65 |
| `crossflow.sideArea`, `sideForceArea` | 3.20 → 4.44 | 2.50 → 6.06 |
| `crossflow.sideCenterX` | −0.30 → −0.83 | −0.30 → −1.18 |

The Extra's side area was 2.4× low and its centroid 0.88 m forward of where the body actually is, which is a large
correction to everything that depends on the body in sideslip — the Munk pitch moment, the yaw damping and the flat
spin. `cd0Area` and the crossflow `cd` are deliberately **not** touched: the first is calibrated against the book
cruise speed and best glide, the second is the standard bluff-body coefficient, and neither is geometry.

## Great Lakes 2T-1A-2: a new type measured off its model (2026-10-28)

Owner: *"import it into aero and work the aerodynamics and physics off of the model by measuring the model just
like you did with the pitts and extra. reference the tcds for the cg location."*

The first type whose flight model was built from its mesh and its type certificate from the start, rather than
corrected afterwards. Same method as the Pitts and the Extra above: cosine-spaced spanwise bands, area from a
rasterised silhouette clipped to the band, chord from thin-slab stations across it, `pos` the strip's own
quarter-chord, fixed-strip gain derived from the measured chord ratio.

### The model measures true

| | model | published | |
|---|---|---|---|
| wing area, both wings | 17.460 m² | 187.6 sq ft = 17.429 m² | +0.2 % |
| span | 8.126 m | 26 ft 8 in = 8.128 m | −0.02 % |
| length | 6.214 m | 20 ft 4 in = 6.20 m | +0.2 % |
| main tyre radius | 0.222 m | 600×6, 17.5 in | exact |

So the areas are scaled by ×0.9982 to land on the published total and nothing else is touched.

| surface | measured |
|---|---|
| upper wing (no ailerons — factory 2-aileron) | 9.109 m² |
| lower wing | 7.143 m² |
| ailerons (lower only) | 1.177 m² |
| stabiliser / elevator | 1.472 / 1.127 m² |
| fin / rudder | 0.518 / 0.580 m² |

Derived gains: aileron 0.348 (E 0.229), elevator 0.395 (E 0.448), rudder 0.395 (E 0.442). Rigging from the
maintenance manual via the provenance: 3° incidence on both wings, 2° dihedral on the lower wing only, upper
wing flat.

### Locating the datum, and the CG

FAA TCDS A18EA gives the datum as "centre of most forward lateral fuselage tube", which is not a feature that
can be pointed at in a mesh. But the same sheet gives **M.A.C. 46.0 in with its leading edge at Sta. 9.0**, so
the MAC quarter-chord is 20.5 in = 0.521 m aft of datum — and the cellule's area-weighted quarter-chord measures
at config x −0.720. That puts the datum at **−0.200**. The model's own firewall station puts it at **−0.175**.
Two independent routes, 25 mm apart, which is good enough to trust.

| | |
|---|---|
| TCDS C.G. at 1800 lb (`massKg` 816 = 1800 lb, the max weight) | +20.0 to +23.8 in aft of datum |
| in the config frame | x −0.708 to −0.804 |
| as % MAC | **23.9 % to 32.2 %** |
| `mass.cg` | −0.756, the mid-range |

Everything else sourced from A18EA rev 11: 1800 lb max weight, Vne 153 mph, manoeuvring load factors
**+5.40 / −4.00** (NOTE 6, specific to the 2T-1A-2), Lycoming IO-360 at 180 hp / 2700 rpm, the Hartzell
HC-C2YK-4F at 74.0 in, and control travel — aileron 24 up / 22 down, elevator 21 up / 29 down, rudder ±35°.
The elevator's asymmetry cannot be expressed: `ControlAxisConfig` carries one symmetric `MaxDeflRad` per axis,
so the pair becomes its mean (25°). The stabiliser is a jack screw, not a trim tab (`trimType` "stabilizer").

### What came out

**Static margin 14.6 % MAC on the first run**, with no tuning — straight from the measured geometry and the
certificated CG. `cd0Area` is the one number that is neither geometry nor certificate: it is the parasite area
of fuselage, gear, struts and wires, and it is calibrated against book speeds with `BOOK_SPEEDS=1`. At **0.59**
the aeroplane cruises at **120 mph on 75 % power**, which is exactly the Waco/Great Lakes spec sheet's max
cruise, and runs to 133 mph at full power (the sheet's "153 mph maximum speed" is Vne, not a level speed).

### A new own-model type still needs a `StyleFor` entry — for its propeller

Worth knowing before the next aeroplane goes in. This type is always drawn from its GLB
(`AirframeModels.PickOwn`), so the procedural airframe in `AirframeVisual.StyleFor` is never rendered for it —
which makes it easy to assume no entry is needed. But that style is also **where the propeller's hub comes
from**: `Prop()` is driven by `Body[0].x + 0.12` and `PropRadius`, and the model's blades only bind and spin
when they sit within half a radius of that hub. With no `case`, the type falls through to the default arm — a
2-33 trainer, whose `PropRadius` is 0 — so no hub is created at all.

The airframe build logs it. Rendering the type with `AircraftRender` before and after the entry:

```
before   [Airframe] model rig: 4/4 control surfaces, 0/0 propellers bound
after    [Airframe] model rig: 4/4 control surfaces, 1/1 propellers bound
```

The control surfaces bind either way — those come from the config's own control strips — so a silent, static
propeller is the whole symptom, and no test in the suite sees it. The entry puts the hub at config x 0.85,
which is exactly where the model's blades are (tolerance 0.47 m), with `PropRadius` the TCDS 74 in.

### Open

- **Stall speed is 10 % low**: Vso 51 mph against the sheet's 57. It is a lift limit, so no amount of drag moves
  it; closing it means a lower cellule CLmax (1.43 now, 1.14 needed), which is what a biplane's mutual
  interference does at this gap/chord. The wing section is not known for this type — the provenance says only
  "~12 % with slight camber" — so nothing was changed. For scale, the fleet's Vso against book runs from the
  Jenny at −14 % to the Pitts at +20 %; this is not a biplane bias and not peculiar to this aeroplane.
- **The inertias are estimates**, from radii of gyration scaled off the Pitts and the Extra. There is no mass
  breakdown for the type (new-airplane protocol step 1c). This is the only part of the config with no source.
- **`mass.cg` z (0.25 m below the thrust line) is derived, not certified** — a TCDS gives no vertical CG.
- **The gear is exported extended.** The physics contacts are the *static* ones, so the aeroplane sits at the
  published 7 ft 4 in and its three-point angle comes out 7.2° against the provenance's solved 6.91°. Until the
  oleo rig is animated the drawn wheels hang about 0.23 m below where the aeroplane is standing.

## Biplane cellules, and what a stall speed is actually measuring (2026-10-28)

Owner: *"account for cellule clmax of a biplane configuration and don't just do it as a one off for this airplane
build it as a tool/table that applies to all biplanes current and future."*

Two wings one above the other never make what two isolated wings of the same total area would: each flies in the
other's field. `FlyingGame.Core.Aero.BiplaneCellule` measures that from the strips alone — nothing is hand-entered
per type, so a biplane added tomorrow is covered the moment its config exists.

### The fleet's cellules

| type | gap | gap/chord | stagger | Su/Sl | decalage | σ (Prandtl) | CLmax × | Vso would |
|---|---|---|---|---|---|---|---|---|
| great-lakes-like | 1.36 m | 1.24 | +0.40 c | 1.09 | 0.0° | 0.528 | 0.915 | +4.5 % |
| jn4-jenny-like | 1.52 m | 1.10 | +0.29 c | 1.36 | 0.0° | 0.623 | 0.893 | +5.8 % |
| pitts-s2b-like | 1.10 m | 1.23 | +0.61 c | 1.10 | 0.0° | 0.507 | 0.922 | +4.1 % |
| stearman-pt17-like | 1.08 m | 0.75 | +0.31 c | 1.02 | −1.0° | 0.630 | 0.833 | +9.6 % |

`BiplaneCelluleTests` prints this and guards it: all four are detected, five monoplanes are confirmed NOT cellules
and return exactly 1.0, the table stays monotonic and bounded, and turning the switch on demonstrably raises the
stall speed (which keeps the AeroModel wiring from going unreachable).

**The interference curve is a TABLE, `ClMaxByGapOverChord`, deliberately in the open.** It carries the shape the
classical data has — about three quarters of a monoplane at half a chord of gap, nine tenths at a chord and a
quarter, asymptotic to one — but it is a standard-form fit, **not a transcribed source**. Anyone with Diehl's
*Engineering Aerodynamics* or NACA TR 256 / TR 269 should overwrite those nine pairs; nothing else has to change.

### It is wired, and it is OFF

`cellule.liftInterference`, per type, absent by default. The evidence did not support switching it on fleet-wide:

- The **Stearman** has the tightest cellule we own (gap/chord 0.75, against the Great Lakes' 1.24 and the Pitts'
  1.23), so it should suffer the most interference of any of them. It already matches its book stall to **+1.3 %**
  with no interference term at all. Switching it on takes it to about **+11 %**.
- The **Pitts** needs 44 % MORE lift than it makes, not less: it realises CLmax 1.13 where its book stall wants
  1.63, because its section tables are 1.22 (upper) and 1.05 (lower, symmetric). Switching it on takes it from
  +20 % to about +25 %.

The reading is that the fleet's section tables are already doing this work implicitly — an *effective cellule*
value rather than a true 2-D section — so a type may only switch this on once its tables are the real section, or
the effect is counted twice. On the Great Lakes it would move Vso 51.2 → 53.5 mph, i.e. −10.2 % → −6.1 % against
book.

### What a stall speed was actually measuring

Worth knowing before anyone reads a Vso number as aerodynamics. `PracticeScenario.EstimateVso` scans down and stops
where the trim solver can no longer hold the aeroplane below a ceiling on angle of attack. That ceiling was a flat
**14°**, which truncated any type whose wing reaches CLmax above it: the scan stopped before the aeroplane had
stalled and reported the last speed it could still trim, which is faster than the stall.

It is now the type's own wing stall in body axes — its section's α at CLmax less the rigging incidence — plus 3°,
clamped to 12–28°. The 172, whose wing reaches CLmax at 16.9° body α and so was being cut off 2.9° early, went from
**+13.4 % to +9.3 %** against its book stall.

That did not explain the whole fleet, and the claim should not be stretched: the Extra and the Decathlon sit exactly
*at* 14°, so there was nothing to recover and neither moved. Their remaining error, and the Pitts' +20 %, have
causes not yet isolated — but they are no longer confounded by a measurement artefact.

### Travel, and the stops that are not symmetric

A type certificate lists each surface's movement, and for most aeroplanes the elevator's two stops are not the same
distance from neutral: more up than down, because the flare needs it and a pushover does not. `ControlAxisConfig` now
carries an optional **`maxDeflNegRad`** beside `maxDeflRad` — the limit in the NEGATIVE-deflection direction, 0 meaning
symmetric. In the sim's sign convention positive elevator is trailing-edge DOWN, so `maxDeflNegRad` is the UP travel.
Everything that clamps a deflection (stick shaping, its inverse, the free-float balance, the tab calibration, the trim
solver, the practice scenarios' pitch search) goes through `axis.ClampDefl`/`axis.LimitFor(sign)` rather than
±`MaxDeflRad`, and the full-aft authority measurement uses `LimitFor(-1)`.

The J-3 was the first type measured against its certificate (FAA Aircraft Specification **A-691 Rev 34**, Item 4
"Control Surface Movements"), 2026-10-30:

| Surface | A-691 | config before | config now |
|---|---|---|---|
| Elevator | 34° up / 29° down | ±29.8° | 34° up / 29° down |
| Aileron | 18° up / 18° down | ±20.1° | ±18° |
| Rudder | 30° left / 30° right | ±25.2° | ±30° |
| Stabiliser (trim) | 2.5° up / 4° down | not modelled | not modelled |

The same reading of A-691 settled the engine: Item 311C certificates the C-85 as an optional engine for the J-3, which
is the 85 hp the config claims, but the config turned it at **2,700 rpm** — above the C-85's 2,575 and well above the
A-65's 2,350. The redline is now 2,575 (owner, 2026-10-30), so the 85 hp is made where the engine makes it. Power and
thrust do not depend on `maxRpm`; torque reaction and the gyroscopic couple do, in opposite directions — the same power
at lower rpm is **+4.9 % torque** (a little more left swing) and **−4.6 % prop angular momentum**.

It matters twice over. The 12 % of extra aileron was part of why the Cub rolled at 78°/s where a real one rolls at
45–55 (70°/s now — the rest is effectiveness, not travel), and the own-model GLB is rigged to the certificate's
numbers, so a config that disagrees draws a surface in one place and flies it in another. Full-aft authority moves with
the up travel, so both J-3 configs were re-calibrated after the change: saturation 71.9 → 42.7° (landplane),
120 → 59.8° (floats).

## Control effectiveness τ (thin-airfoil theory)

Flat plate with a plain flap, Glauert: cos θ_h = 2E − 1, A₀ = α + δ(π − θ_h)/π, A_n = 2δ sin(nθ_h)/(nπ).

| E | τ theory | τ × 0.85 | fixed-strip g |
|---|---|---|---|
| 0.22 | 0.57 | 0.48 | 0.34 |
| 0.25 | 0.61 | 0.52 | 0.36 |
| 0.32 | 0.68 | 0.58 | 0.38 |
| 0.35 | 0.71 | 0.60 | 0.39 |
| 0.40 | 0.75 | 0.64 | 0.39 |
| 0.45 | 0.79 | 0.67 | 0.40 |

Large deflections: plain-flap effectiveness falls past ~15–20° (separation off the deflected surface). The elevator's
`SaturationDeg` (δs·tanh(δ/δs)) models it; it is set per type by the stall calibration (`StallAuthorityTests`).

## Hinge moments and stick-free float

A released surface floats where its hinge moment balances: C_h = C_hα·α + C_hδ·δ (+ tab, + centring spring), so
δ_free = −(C_hα/C_hδ)·α. Reference: hinged-strip area × chord, local q; α is the local flow at the hinged strip
(downwash, slipstream and rates included).

Thin-airfoil, 2-D, per rad, from the same Glauert loading:

| E | c_hα | c_hδ | float ratio c_hα/c_hδ |
|---|---|---|---|
| 0.25 | −0.57 | −0.95 | 0.60 |
| 0.30 | −0.63 | −0.97 | 0.65 |
| 0.35 | −0.69 | −0.99 | 0.69 |
| 0.40 | −0.75 | −1.01 | 0.74 |

Real sections carry about 0.55 × c_hα and 0.72 × c_hδ (boundary-layer thickening at the trailing edge, t/c ≈ 12 %).
On a finite surface: C_hα = c_hα·(a/a₀); C_hδ = c_hδ − c_hα·τ·(1 − a/a₀).

For an unbalanced 172-size elevator (E 0.32, tail AR 3.2) that gives **C_hα ≈ −0.19, C_hδ ≈ −0.61** (float ratio 0.31).
Aerodynamic balance cuts both: a horn balance roughly ×0.6 on C_hα and ×0.75 on C_hδ; Frise ailerons roughly halve C_hδ.
The configs carry C_hα −0.12…−0.20, C_hδ −0.30…−0.45: the values of balanced surfaces, plausible for the 172
(horn-balanced elevator and rudder, Frise ailerons) but copied across types without per-type sources.

## Open per-type work

- EB29R, 737, Seminole, Stearman, floatplanes, DC-3 and others still sit at 25–60 % MAC static margin: CG and
  tail sizes to check against each type's data. The Pitts and the Extra came off this list on 2026-10-28.
- **The Pitts' CG height is derived; its station is now sourced.** `mass.cg` is [0.045, 0, 0.20]. The 0.045 is TCDS
  A8SO (below). The 0.20 **down** is not: a TCDS gives no vertical CG, and that number is what the ground handling
  needs. A weight-and-balance breakdown per the protocol's step 1c would settle it.
- Seminole: the real aircraft has an all-moving stabilator with an anti-servo tab; it is modelled as stab + elevator.
- Hinge coefficients per type from each surface's balance (none / horn / overhang / Frise / anti-servo tab).


## Roll authority: the cambered span, and two fudges that were rejected (2026-11-01)

Owner: *"NO fudge factors tuned to published numbers; published roll rates are VALIDATION ONLY."*

The Pitts rolled **391.6 deg/s** at 160 mph IAS with full aileron, against the owner's (ex-Unlimited, S-1S)
**260–300**. Two attempts to close that were both the same mistake and both were thrown away:

1. **Shrinking the travel**, 25° → 16.5°. That is less aileron travel than this model gives a J-3 Cub, and
   `maxDeflRad` is not a free parameter: it is FAA TCDS A8SO Rev. 21 p. 9, *ailerons symmetrical 25° ± 2°*.
2. **`controls.aileron.effectiveness = 0.66`, called τ** (commit 4e5e1a7). τ is ALREADY in the model, as the
   fixed row's camber gain `g = 0.85·τ(E)` — the table at the top of this document. A second chord-ratio factor
   counts it twice. The "corroboration" offered at the time (thin-aerofoil τ ≈ 0.60 against 0.66) was comparing
   the number to a quantity the model had already applied. Removed; the field survives as a documented hook at
   its default of 1.0, used by nothing, and explicitly **not** τ.

### What was actually wrong: camber applied past the end of the aileron

A deflection cambers the FIXED strip ahead of the control. That camber was applied across the strip's whole
span — including the part with no aileron behind it. It adds roll POWER without adding roll DAMPING, so it goes
straight into the steady rate.

`AeroModel.ControlSpanCoverage` now scales the fixed row's gain by the fraction of its span the control
actually occupies, measured as `area/chord` on both rows from the same rasterised silhouette the configs were
built from. **Scaling the gain rather than splitting the strip** keeps the station list, strip count, load
points, aspect ratios and `WeightAndBalance.MacGeom`'s `>= 0.9` hinged-row test all untouched, and in the
linear range the two are equivalent.

Coverage within **1 %** of full is taken as full: these spans come from a rasterised silhouette and are not
accurate to better than that, and without the deadband the exactly-tiled surfaces measure 0.999 and shift
every type's trim in the fourth decimal for no physics. Coverage is then **1.000 for 28 of the fleet's 32
aileron and elevator systems**, which are bit-for-bit unchanged.
It is not 1 for exactly the surfaces rebuilt from their GLB models on 2026-10-28:

| surface | aileron span (m) | cambered span (m) | coverage |
|---|---|---|---|
| Pitts wing-upper | 2.875 | 4.225 | 0.681 |
| Pitts wing-lower | 2.943 | 4.090 | 0.719 |
| Extra 300 wing | 5.569 | 6.740 | 0.826 |
| Great Lakes wing-lower | 4.289 | 4.890 | 0.877 |
| Pitts / Extra / Great Lakes / Jenny elevators | — | — | 0.90–0.96 |

### Wings on 2-D tables: BUILT, MEASURED, and NOT shipped

The 2026-10-06 work rescaled 2-D TAIL tables to the DATCOM/Helmbold finite-span slope and left wings alone
because every wing polar was finite-span then (clarkY-like 4.47/rad). That is no longer true. Nine wing
surfaces authored or rebuilt since carry 2-D section data:

| surface | 2-D a₀ | Helmbold a (if applied) |
|---|---|---|
| Pitts wing-upper / lower | 6.30 | 4.56 / 4.55 |
| Great Lakes wing-upper / lower | 6.17 | 4.72 |
| Extra 300 wing | 5.82 | 4.27 |
| Swift S-1 wing | 5.82 | 5.08 |
| Cassutt / target drone wing | 5.82 | — |
| 737 wing | 5.53 | 4.60 |

The rescale was implemented, run against the fleet, and then **taken back out**. Helmbold stretches the alpha
axis, so CLmax survives but more incidence is needed to reach any given lift — and a taildragger on its wheels
is pinned at its ground attitude, so that goes straight into the lift-off speed:

| | lift-off, as shipped | lift-off, with the rescale | real |
|---|---|---|---|
| Pitts S-2B | 34.0 m/s (76 mph) | 40.2 m/s (90 mph) | ~60–70 mph |
| Extra 300 | 38.7 m/s (87 mph) | 44.7 m/s (100 mph) | ~60–70 mph |

An 18 % move the wrong way on a number checked against the books, in exchange for 7 % on the roll rate that
was the point — and in the wrong direction there too (the Pitts goes 291 → 311 deg/s, out of the owner's
260–300). `AerobatGroundHandlingTests.TakeoffRollIsTheBooks` fails for both types with it in and passes with it
out.

The slope is still wrong. What the measurement says is that these configs' wing area, incidence, CLmax and
ground attitude were authored against a 2-D slope and silently absorb it, so the table cannot be corrected on
its own — the wing has to be re-derived as a whole. That is its own job.
`PittsRollRateTests.TheseWingsStillCarryTwoDimensionalSectionTables` keeps the list so it cannot be lost, and
asserts the rescale stays off until then. **Biplanes** would take each wing's own geometric AR (upper and lower
are separate systems here); Munk interference between them is not modelled and would lower both further.

### Large-deflection saturation: already present, and already stronger than DATCOM

An added `tanh` for ailerons (the elevator has one) would have been the third version of the same error. The
hinged strip already samples its section table at α + δ, and at 25° that is well past the table's stall — the
Pitts' own wing table gives Cl 1.05 at 12° and 0.514 at 25°. Measured as the ratio of the section's actual ΔCl
to its small-deflection linear value:

| aileron deflection | Pitts, modelled | DATCOM plain flap, c_f/c 0.25 |
|---|---|---|
| 5–10° | 1.00 | ~1.00 |
| 15° | 0.94 | ~0.90 |
| 20° | 0.67 | ~0.82 |
| 25° | **0.63** | **~0.75** |

So the model saturates **18 % harder** than DATCOM, not less. Adding a second factor would have taken it to
~0.47. Left alone, and recorded as an open question: forcing it to the DATCOM curve would put the Pitts at
roughly 369 deg/s, above the owner's band, which says the remaining error is somewhere else (biplane
interference, the Pitts' own section table) rather than in the saturation.

### Hinge-gap / flex loss: deliberately NOT added

A ~0.9 for an unsealed hinge gap is real physics, but this fleet has no sealed-vs-unsealed datum anywhere in
its configs, so the factor would be chosen rather than derived — the same move as the 0.66 with a different
name. Not added. It needs a per-aircraft physical input first.

### Extra 300 spades: a stick-force device, already modelled as one

The Extra's aileron spades are aerodynamic balances on arms ahead of and below the hinge line. With the
ailerons neutral they lie along the airstream and do nothing; deflected, the spade's load acts about the HINGE
and reduces the stick force. They are not a lift-producing surface of any consequence — the roll moment is the
aileron's. No public source gives spade chord, span or area for the Extra 300 (searched: manufacturer manual,
the Roskam-method hinge-moment study of the Extra-300, owner forums), so no geometry could be authored without
inventing it. The effect is already in the config where it belongs: the Extra's aileron carries the BALANCED
hinge coefficients `hingeChAlpha -0.10 / hingeChDelta -0.30` against a plain unbalanced surface's `-0.20 /
-0.45` (the 172). Nothing was changed.

Sources: Extra 300/SC Information Manual (flight control system); *Hinge Moment Coefficient Prediction Tool and
Control Force Analysis of Extra-300 Aerobatic Aircraft* (Roskam method; max lateral wheel force ~341 N in
aileron roll); Wikipedia *Aileron* (spades vs. mass balances).
