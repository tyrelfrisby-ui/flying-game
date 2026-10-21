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

**Always check a plan before trusting it.** RC plans are frequently semi-scale — fuselages fattened to swallow
radio gear, wing sections thickened for docile flying, tails enlarged for stability. Scale the plan up by its
stated ratio and compare span and length against published figures. The Marutaka 172 passed (11.008 m span
against a real 11.0); one that does not is still useful for section *character* but not for proportions.

## 3. The steps, per aeroplane

1. **Gather** into `assets-incoming/ref-<id>/`, one directory per reference. Downloaded files are untrusted: keep
   scripts elsewhere and pass paths as arguments.
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
4. **Read the plan's formers** for the section outline, and set the superellipse exponent from it. A 172's
   firewall former is a rounded square (e ≈ 0.55), not an ellipse (e = 1).
5. **Build** `tools/model_<id>.py`:
   - **Dimensions come from `configs/aircraft/<id>.json`.** The physics is the authority — if the config says the
     wing spans 10.92 m then so does the model, or the aeroplane you see is not the one you fly.
   - Config **z is DOWN-positive**; Blender's is UP. Convert through one named function, not scattered minus
     signs. Getting this wrong puts a high wing under the fuselage and the undercarriage on the roof.
   - The real airfoil, generated (NACA 4-digit is four lines of maths).
   - **Separate named parts, each hinged part's origin ON its hinge line.**
6. **Export** GLB beside the type's other assets, register in `AirframeModels.Specs`.
7. **Verify** — render and compare against the references. The images are the test.

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

1. **C172** — in progress. Sections measured and plan-corrected; needs windscreen, shaped tail, real gear.
2. **PA-28 Archer**, **J-3 Cub**, **PA-18 Super Cub** — same class, same tooling.
3. **Stearman**, **Jenny** — biplanes: two wings, struts and wires.
4. **Extra, Pitts, Decathlon** — aerobatic singles.
5. **2-33, Swift, EB29R** — gliders, long wings, simple bodies.
6. **Beaver, Pawnee, AirCam, Cassutt, Gee Bee, Glasair** — the rest of the singles.
7. **Seminole, DC-3, P-51, F-86, 737, H-4** — twins, warbirds and jets last: retractable gear, jet fans.

**Go back.** When something is learned late that would have helped earlier — the median, the superellipse, the
z convention — return and apply it to the models already built. The scripts are parametric and re-runnable
precisely so that costs a re-run rather than a rebuild.

## 6. What is still missing in the engine

- **Flap animation** — no geometry, no `"flap"` case in `AirframeVisual.SetDeflections`. `Aircraft.FlapFraction`
  (0..1) already exists and drives the HUD, so the state is there; only the visual is missing.
- **Nose/tailwheel steering** — `LandingGear` computes the angle (`TailwheelState.AngleRad`, `MaxSteerRad`) and
  nothing reads it for drawing.
- **Retractable gear** — `cfg.RetractableGear` exists and the model path handles gear-up exports; the travel is
  not animated from a model's own parts.
- **Jet fans** — `PropHub` spins propellers and blurs the disc above ~400 rpm; a jet's fan face has no equivalent.
