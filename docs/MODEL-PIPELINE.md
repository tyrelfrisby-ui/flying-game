# Building our own airframes: references for measurement, geometry of our own

**Owner's decision, 2026-10-21:** *"look for 3 view drawings, rc plans and usdz files all as reference only for
measurements but you build your own blender model for each airplane we have."*

Every aeroplane in the fleet gets a model we built, from measurements taken off references we never ship. This
is the companion to `NEW-AIRPLANE-PROTOCOL.md`, which governs the *physics* config; this governs the *geometry*.

## 1. Why measurement and not adaptation

Copyright protects a modeller's **expression** — their topology, their UV layout, their texture art. It does not
protect **the shape of a real aeroplane**, which is a fact about a machine somebody built. Three modellers
measuring the same 172 converge on the same sections, and a 1972 RC plan agrees with them: on 2026-10-21 the
Marutaka 1:8 plan and the median of three meshes gave the length of a 172 as 8.20 m and 8.206 m — **six
millimetres apart**. That convergence is the evidence that what is being read out is the aeroplane, not anyone's
interpretation of it.

**Averaging the meshes does not work**, and was considered and rejected. A work derived from three copyrighted
meshes is a derivative of all three — infringing each instead of one — and technically there is no vertex
correspondence to average, so a voxel remesh mushes exactly the cowl lip, windscreen frame and fin leading edge
that make an aeroplane read as itself. Averaging the **measurements** converges on the real thing. Averaging the
meshes converges on porridge.

**A reference needs no licence to be measured and never shipped.** References live in `assets-incoming/`, which
is gitignored. Nothing of theirs enters the repo, so no attribution attaches to our mesh.

## 2. What each kind of reference is good for

| Reference | Gives | Does not give |
|---|---|---|
| **RC plan** (Outerzone, AeroFred — free) | **Cross-section OUTLINES** — every former drawn at a numbered station, rib profiles, a scaled three-view | Nothing, if it is semi-scale — check it first |
| **3-D model** (USDZ/GLB/OBJ) | Station **positions and widths**, overall proportions, median of several | An outline. A slice reports a width and a height, never a shape |
| **Published three-view / POH** | The **arbiter** when the other two disagree; span, length, height, areas | Sections |
| **Photographs** (Wikipedia / Wikimedia Commons — free) | What the aeroplane **actually looks like**: where the tailplane sits on the fin, how far forward the canopy starts, the gear rake, the stance. The check that catches a model that measures right and reads wrong | A number, unless the shot is orthogonal. A three-quarter photo cannot give you a ratio |

**Always check a plan before trusting it.** RC plans are frequently semi-scale — fuselages fattened to swallow
radio gear, wing sections thickened for docile flying, tails enlarged for stability. Scale the plan up by its
stated ratio and compare span and length against published figures. The Marutaka 172 passed (11.008 m span
against a real 11.0); one that does not is still useful for section *character* but not for proportions.

## 3. The steps, per aeroplane

1. **Gather** into `assets-incoming/ref-<id>/`, one directory per reference. Downloaded files are untrusted: keep
   scripts elsewhere and pass paths as arguments.
   - **Photographs first, every time.** *Owner's decision, 2026-10-25: "For the modeling of our own aircraft go
     to the Wikipedia page for each type and find unlicensed photos you can use to judge your design against
     for accuracy."* `tools/ref_photos.py <config-id> --download 8` does it: it reads the type's Wikipedia
     article and its Commons category, **rejects every licence it does not recognise as free**, ranks what is
     left so a three-view drawing beats a pretty photo, downloads into `assets-incoming/ref-<slug>/photos/`
     and writes a `PHOTOS.md` recording licence and author per file. `--check` verifies all 25 mappings still
     resolve. Nothing here ships; `assets-incoming/` is gitignored.
   - **Commons holds three-views too**, and they are free. It has a colour three-view of the 2-33A and two
     line drawings of the 172 — the arbiter the table above says to go and find, already there.
   - **Check which variant a drawing is of before you trust its length.** Commons' `Cessna-172-3-view-line-
     drawing.png` is the factory "172 PRINCIPLE DIMENSIONS" sheet, dimensioned on the drawing itself: 36'
     span, **24' length**, 8'6" height, 7'2" track, 5° ground angle. Those are the 1956 straight-tail 172.
     Our config is a 172S — same 36' span, but 27'2" long and 8'11" high. The span agreeing is what makes the
     trap: it looks like the right drawing. Take the sections, the ground angle and the track from it; take
     the length from the config. The 2-33A colour three-view has no such problem — one variant, and the
     2-33A is the one we model.
   - **Look one level into subcategories.** Several type categories hold no files at all, only
     `... by registration` and `... by location` children. Two levels reaches airshows and airports, which
     are full of other people's aeroplanes.
2. **Measure each mesh** — `blender --background --python tools/measure_model.py -- <file> <N> <true-span>`.
   It reads OBJ, GLB, USD/USDZ and FBX, scales by the known span, and prints half-width and depth per station.
3. **Average them** — `python3 tools/average_sections.py <true-span> <file> <file> ...`. It reports the **median**
   per station with the **spread**.
   - **Median, never mean.** With three references one bad slice ruins a mean: at one 172 station two references
     said 0.404 and 0.335 and the third said 0.097 (its cut fell between spinner and cowl); the mean returned
     0.279, a number none of them measured.
   - **Read the spread column.** Small spread means the references agree and the number is trustworthy. Large
     spread means something else sits near the centreline at that station — forward it is the spinner and nose
     gear, aft it is the tailplane root.
4. **Measure the SIDE VIEW, not only the plan view.** This is the step the 172 skipped, and skipping it is
   why that model read as a teardrop for two days: the half-widths were measured from three references and
   the top and bottom lines were written out by hand. An aeroplane lives in its side view.
   - The **belly** comes from the slice: `edge()` in `measure_model.py` bins a section by height and takes
     the outermost bin still a real fraction of the body's width, which steps over the fin, the gear legs
     and the wheels because **those are thin in span and the skin is not**.
   - The **top line cannot come from a slice** wherever the wing crosses the body, because a wing is as wide
     as anything and a high wing sits on the roof. Cutting at `|y| < 0.75 m` does not help — that leaves the
     wing's *root* in the picture. **Isolate the body by connected component**: a wing is one piece spanning
     more than any fuselage does, so throw away every island wider than 1.4 m. Render what is left as a white
     emission silhouette on a black world through an **orthographic** camera and read the outline back column
     by column. Threshold a lit grey render and the glass and the wing's highlights cross it; emission on
     black makes 0.5 unambiguous.
   - On the 172 this was the difference between "+0.72 flat from 1.98 m to 2.73 m" — a flat roof and **no
     windscreen at all** — and a deck at +0.407 breaking to a roof at +0.670, a 52° rake.
5. **Read the plan's formers** for the section outline, and set the superellipse exponent from it. A 172's
   firewall former is a rounded square (e ≈ 0.55), not an ellipse (e = 1).
6. **Build** `tools/model_<id>.py`:
   - **Dimensions come from `configs/aircraft/<id>.json`.** The physics is the authority — if the config says the
     wing spans 10.92 m then so does the model, or the aeroplane you see is not the one you fly.
   - Config **z is DOWN-positive**; Blender's is UP. Convert through one named function, not scattered minus
     signs. Getting this wrong puts a high wing under the fuselage and the undercarriage on the roof.
   - The real airfoil, generated (NACA 4-digit is four lines of maths).
   - **Separate named parts, each hinged part's origin ON its hinge line.**
7. **Export** GLB beside the type's other assets, register in `AirframeModels.Specs`.
8. **Verify** — render and compare against the references. The images are the test.
   - **Measure the three-view, do not eyeball it.** Segment the drawing by connected component (a flood fill
     from the widest ink run on a view's centre row) and read each view's bounding box. Cropping a view's
     bounding *rectangle* is not enough: on the 2-33 drawing the front view overlaps the side view's rows, so
     a rectangle crop reported the side view's fin reaching the top of the frame.
   - Then compare **ratios**, not metres, because the drawing has no scale bar. Span against length against
     height, measured on the mesh with glTF accessor `min`/`max`, which is exact and needs no sampling.

### Reading the config without being lied to

Three ways the config will hand you a wrong number if you ask carelessly. All three were live in the 172 at
once, and together they moved the whole aeroplane about 0.9 m relative to its own wing.

| Trap | What happens | What to do |
|---|---|---|
| **Prefix matching across surfaces** | `id.startswith("wing")` catches `wing` **and** `wing-aileron`, a metre apart in x. Their mean is a point on neither, and everything positioned from it is wrong together — so nothing looks obviously broken. | Name the surface you mean. |
| **A strip's `pos` is its CENTRE** | Reading span as `max(abs(y))` stops half a strip short at each tip. The 172's tailplane read 3.02 m where the aeroplane has 3.45 m. | Add half the gap between the outer two strips. The wing hides this — its outer strips are bunched. |
| **A control surface is its own surface** | `hStab`'s chord is the *stabiliser*; the elevator is separate. The real chord is the sum. | Pair them by station and add. Never "× 2.0". |

**And the config's z is DOWN-positive while Blender's is UP.** One named conversion, `bz()`. Getting it wrong
puts the high wing under the fuselage and the undercarriage on the roof. The same sign trap bites twice: an
aerofoil's ordinates are UP-positive, so once `WING_Z` has been through `bz()` the section is `WING_Z + py`,
not `WING_Z - py`. The 172 flew for a day with its wing upside down, which from most angles just looks like
the wing is buried in the cabin.

## 4. The parts every model carries

Named so Unity can bind them without solving anything at runtime:

```
Wing.L / Wing.R            Flap.L / Flap.R         Aileron.L / Aileron.R
HStab / Elevator           Fin / Rudder
Gear.Nose.Steer            Gear.Main.L / .R        (origin = the steering axis)
Prop / Spinner             Strut.L / Strut.R
```

Unity already **carves** ailerons, elevator and rudder out of any model's mesh from the config's surface regions
(`AirframeVisual.CarveModel`), so those animate even on a downloaded model. **Flaps and nose-wheel steering are
not animated anywhere in the game today** — the flap geometry does not exist and the steer angle `LandingGear`
computes never reaches the visual. Shipping them as their own objects is what makes binding them possible.

## 5. Order of work

The method is proven on one aeroplane end to end before it is repeated: shape, then controls bound and moving,
then livery. Then the rest, easiest geometry first so the pipeline matures on simple shapes:

1. **C172** — in progress. Sections measured, side view measured, glazing, spring-steel gear legs, struts
   that land on the skin, rounded tips, tail built from the strip edges. Still wants: door and panel lines,
   a blunter cowl, a dorsal fin, wheel fairings, and the model registered in `AirframeModels.Specs` so the
   game actually flies it instead of the downloaded one.
2. **PA-28 Archer**, **J-3 Cub**, **PA-18 Super Cub** — same class, same tooling.
3. **Stearman**, **Jenny** — biplanes: two wings, struts and wires.
4. **Extra, Pitts, Decathlon** — aerobatic singles.
5. **2-33, Swift, EB29R** — gliders, long wings, simple bodies.
6. **Beaver, Pawnee, AirCam, Cassutt, Gee Bee, Glasair** — the rest of the singles.
7. **Seminole, DC-3, P-51, F-86, 737, H-4** — twins, warbirds and jets last: retractable gear, jet fans.

**Go back.** When something is learned late that would have helped earlier — the median, the superellipse, the
z convention — return and apply it to the models already built. The scripts are parametric and re-runnable
precisely so that costs a re-run rather than a rebuild.

### Two techniques worth reusing

**Glazing is a patch of the skin, lifted.** Build each pane from the *same* section function as the fuselage,
between two stations and two limits, then push it out 10 mm. Because it is the same surface it cannot float
off the body or sink into it however the sections change, it follows the windscreen's rake for free, and the
fuselage stays one watertight loft — which is what `CarveModel` and the UV generator expect. Specify a side
window by **height** and a windscreen by **fraction of the half-width**, not by section angle: an angle band
on a deep section puts a windscreen's lower edge somewhere down the side of the nose.

**Shade smooth BY ANGLE.** Marking every polygon smooth rounds off the windscreen corners the measurement
went to such trouble to find, and smears the wing's trailing edge. `bpy.ops.object.shade_auto_smooth(angle=…)`
at about 38°. (Blender 4.1 removed `Mesh.use_auto_smooth`; it is a modifier now.)

## 6. What is still missing in the engine

- **Flap animation** — no geometry, no `"flap"` case in `AirframeVisual.SetDeflections`. `Aircraft.FlapFraction`
  (0..1) already exists and drives the HUD, so the state is there; only the visual is missing.
- **Nose/tailwheel steering** — `LandingGear` computes the angle (`TailwheelState.AngleRad`, `MaxSteerRad`) and
  nothing reads it for drawing.
- **Retractable gear** — `cfg.RetractableGear` exists and the model path handles gear-up exports; the travel is
  not animated from a model's own parts.
- **Jet fans** — `PropHub` spins propellers and blurs the disc above ~400 rpm; a jet's fan face has no equivalent.

## 5. What the photographs caught

Run on 2026-10-25, the first time the fleet was checked against free photographs and the free three-views
that came with them.

**The 2-33 is 0.55 m short in height.** Span 15.564 m against a published 15.545, length 7.850 m against a
published 7.848 — right to twenty millimetres. Height 2.320 m against a published **2.83 m**, and against
**2.870 m** measured off the Commons three-view, which agrees with the published figure to 1.4% and so
corroborates it independently. The planform is right and the side view is not, which is exactly the failure
[step 4](#3-the-steps-per-aeroplane) was written to prevent. Roughly 0.2 m of it is the fin and roughly 0.34 m
is the body depth below the boom; the remodel will have to settle the split.

**The 172 passes.** Span 11.136 m (real 11.00), length 8.132 m (8.28), height 2.617 m (2.72) — within 1.2%,
1.8% and 3.8%, measured against the published 172S figures and the mesh's own glTF bounds. It is the one that
was built from a checked plan and a measured side view, and it shows. Note the Commons drawings could not have
been used for this: both are of earlier, shorter 172s (see the variant warning in step 1).

**The Swift S-1 has a cruciform tail and the config does not.** Three photographs of SP-P600 agree: the
tailplane sits roughly half way up the fin. `configs/aircraft/glider-swift-s1-like.json` puts `hStab` and
`elevator` at `z = 0.000`, on the boom centreline, below even the fin root at `z = -0.319`. A photograph was
the only reference that was ever going to say so — the EASA certificate gives span, length and height and
never mentions where the tailplane is. The model must be cruciform or it will not read as a Swift; whether
the *config* moves is a physics question, and a small one, since the height of a tailplane changes its
downwash exposure rather than its area.

## 6. The free three-views Commons already has

Indexed 2026-10-25 across all 25 airframes. Each is on Wikimedia Commons under a free licence; the full list
with licence and author per file is in each type's `assets-incoming/ref-<slug>/photos/PHOTOS.md`. **Read the
variant warning in step 1 before using any of them for a length** — several are military or earlier marks of
the aeroplane our config describes, and a shared span hides it.

| Airframe | Free three-views on Commons | Variant to watch |
|---|---|---|
| `ref-c172` | `Cessna 172 3-view line drawing`, `Cessna 172G Skyhawk 3-view` | Both earlier/shorter than our 172S |
| `ref-glider233` | `Schweizer SGS 2-33A … colour 3-view drawing` | None — the mark we model |
| `ref-f86` | F-86A, F-86D, F-86H, XP-86 | Four different marks; D is the all-weather interceptor |
| `ref-dhc2-beaver` | L-20A, U-6A line drawings, DHC-2 silhouette | Military designations of the same airframe |
| `ref-p51d` | `P-51D Mustang 3-view (no specs)` | Matches |
| `ref-pa28` | PA-28-161 Warrior II, PA-28R-200 Arrow | We model an Archer; the Arrow is retractable |
| `ref-pa44` | `PA-44-180 Seminole 3-view` | Matches |
| `ref-pa25` | `PA-25 Pawnee C 3-view` | Matches |
| `ref-j3-cub` | Piper L-4B Grasshopper, Piper TG-8 | L-4B is the militarised J-3; the TG-8 is the GLIDER conversion |
| `ref-stearman` | `Boeing N2S-3 3-view` | Navy designation of the Model 75 |
| `ref-geebee` | `Gee Bee Super Sportster 3-view, L'Aerophile 1932` | A contemporary magazine drawing, not a factory one |

Fourteen airframes have none, and four have almost no free photography either: `ref-eb29` returned **2**
candidates, `ref-geebee` 7, `ref-decathlon` 8, `ref-cassutt` 11. Those will need the RC-plan and measured-mesh
routes the rest of this document describes; a photograph check is not available to them.

## 7. The fleet, measured (2026-10-25)

`python3 tools/fleet_accuracy.py` measures the model `AirframeModels.Place()` actually loads against the
published dimensions of the real aeroplane, for all 25 airframes at once.

**It compares ratios, never metres.** `Place()` scales every model to the config's span at runtime, so a
model's raw size tells you nothing — a uniform scale cannot change a ratio. Length/span and height/span are
what survive the scaling, and they are what caught the Swift mesh being short-coupled by 26%.

Three things the table cannot do, stated so nobody reads more into it than it holds:

- **The published figures are the article's FIRST `{{Aircraft specs}}` block**, which is often a different
  mark from the one our config describes. A modest error may be variant, not model: the 737 row compares an
  early 737 against a -800 mesh, and the PA-28 row compares a Cherokee against a Cadet.
- **A model with a non-zero `PitchDownDeg` is drawn sitting on its tail** and `Place()` levels it, so its raw
  height is not the aeroplane's. Those rows show no height error rather than a wrong one.
- **`?` means span and length are within 10% of each other**, so sorting the extents cannot tell them apart.

| type | model file | ΔL/S | ΔH/S | reading |
|---|---|---|---|---|
| `glider-eb29r-like` | `.obj` | **+57%** | **+127%** | Not an EB29. The file is UNDERPOLY's *Free Sailplane Glider* — a generic ~15 m ship standing in for a 29.3 m open-class aeroplane. The worst in the fleet by a wide margin |
| `pa18-cub-like` | `.obj` | +0.8% | +32% | Planform exact. The height is a taildragger drawn three-point with no `PitchDownDeg` set — suspected, not proven |
| `seminole-like` | `.obj` | +18% | +28% | A **Piper PA-23 Apache/Aztec**, not a PA-44 |
| `boeing-737-like` | `.obj` | +18% | −4% | A -800 against an early-737 published row. Variant, already handled by `scaleTrim: 1.158` |
| `pa28-archer-like` | `.glb` | −13% | +6% | A PA-28 **Cadet** against a Cherokee published row. Variant |
| `dc3-like` | `.obj` | +0.7% | +9% | Good |
| `p51d-like` | `.glb` | −5% | −9% | Good |
| `extra-300-like` | `.glb` ? | +6% | — | Three-point; no height comparison |
| `f86-sabre-like` | `.glb` ? | +5% | +3% | Good |
| `stearman-pt17-like` | `.glb` | −4% | −5% | Good |
| `geebee-r2-like` | `.obj` | −1.0% | +5% | Good |
| `hughes-h4-like` | `.glb` | +0.1% | +1.2% | Good |
| `jn4-jenny-like` | `.obj` | −1.0% | −1.0% | Good |
| `c172-like` | `.obj` | −0.9% | +0.1% | Good |
| `cirrus-sr22-like` | `.glb` | +0.7% | +0.7% | Good |

Ten of fifteen are within 5% on both. Two are the wrong aeroplane (`glider-eb29r-like`, `seminole-like`), two
are a different mark of the right one, and one needs its attitude checked.

### Switching between a downloaded model and ours

*Owner, 2026-10-25: "Keep the usdz files for the ones that have them so we can switch between them as
needed/desired until our models are up to my standards."*

Nothing downloaded is removed. `SessionSettings.UseOwnModels` (Options: **Our own models where we have one**)
chooses, and it is **off** by default — the downloaded model is still what a player sees. Our model lives
beside the downloaded one as `Models/<id>/<id>-own`, and `AirframeModels.OwnSpecs` carries its orientation,
which is *not* the same as the downloaded model's: our Blender exports come out in the config's own
convention (nose +x, up +y, span z) while the downloaded files land however their author left them.

A caller asking a question **about the model on screen** — does it need the procedural gear under it? — must
go through `AirframeModels.TryPlacedSpec`, not `Specs`, or it answers for a model that is not being drawn.

`c172-like-own.glb` had been sitting in `Resources/Models/c172-like/` since it was built and was **never
loaded by anything**: `Place()` asks for `Models/<id>/<id>`, which is the downloaded OBJ. That is now a
switch rather than a dead file.

On the USDZ files themselves: only the 172 still has its originals, `ref-c172/truescale/` and `ref-c172/wip/`.
The rest were converted to OBJ in April 2026 (`fdbbee3`/`c0f2ad5`/`95d21e8`) and the USDZ originals are not in
the repo — each model's `LICENSE.txt` records where it came from, so any of them can be fetched again.
