# Owning the airframes: AirframeVisual out of Unity and into Core

**Status: a map for approval. Nothing built.** Written 2026-10-21 after Ty: *"we need to work on upgrading our own
AirframeVisual so we can continuously improve our visuals … either internal to Aero Playground or a new
application … for use on all of my apps like Glass Overlay."*

## 1. Why, and it is three reasons rather than one

**Licensing.** 17 downloaded models, 15 CC BY 4.0 and 2 Sketchfab Free Standard. CC BY permits commercial use, so
the licence text is not the problem; the problems are chain of title (a fraudulent uploader writes the same
licence text as an honest one — the Drake Hill plan already caught one re-upload of the T-6), the attribution that
does not currently reach the end user, and the Jenny's second notice saying GPL. A generated airframe has none of
these. See the model-licence section of `docs/CREDITS.md` and each model's `LICENSE.txt`.

**The Unreal port already has this problem.** `ARCHITECTURE.md`: *"Dependency direction is strictly downward: UI →
Game → Bridge → Sim → Core. Core and Sim never reference anything Unity."* `AirframeVisual` is 2,588 lines of
`UnityEngine.Mesh`/`Material`/`GameObject` sitting in **Bridge**. The planned Mac desktop build on Unreal inherits
a fleet it cannot draw, independently of anything to do with licences.

**Glass Overlay is Remotion** (React/TypeScript). A Unity C# mesh builder does not port to it. Lifting
`AirframeVisual` wholesale into a shared app does not work — but see §3, because the valuable part does travel.

## 2. The thing that is actually portable

Not the mesh code. The **parametric description**: `AirframeVisual.Style` (body profile as `(x, radius)` pairs,
`TailSpec`, `BiplaneStruts`/`HighWingStruts`/`LowWingGear` flags, `PropRadius`, colours) plus the geometry already
in `AircraftConfig` (every strip's position, chord, area, dihedral, sweep; the gear positions; the prop diameter).

That is data. Data goes anywhere. Today it is trapped in a Unity file.

## 3. Shape

```
src/FlyingGame.Core/Airframe/        pure C#, no UnityEngine  (src/ is ALREADY a Unity local package:
  AirframeStyle.cs                    Packages/manifest.json has "com.flyinggame.simcore": "file:../../src",
  AirframeMesh.cs                     so Core reaches Unity, the headless tests and Unreal with no new plumbing)
  AirframeGeometry.cs                 fuselage lathe, wings, tail, struts, gear, floats, slats, spoilers, prop
  GltfExport.cs                       -> .glb

unity/.../Bridge/AirframeVisual.cs   thin adapter: Core arrays -> UnityEngine.Mesh, plus what genuinely needs a
                                      live engine (hinge animation, gear retraction, damage splitting, prop blur)
```

**glTF is the keystone.** One generator, one standard output, every consumer downstream: three.js (so
Glass/Remotion), Unreal, Unity, Blender. "Continuously improve the visuals" then means improving one Core
generator and every app inherits it.

## 4. Why this is tractable rather than a rewrite

**The visual regression harness already exists.** `AirframeRender` renders every type to `build/airframes/*.png`,
and `AIRFRAMES_PROCEDURAL=1` (added 2026-10-20) makes it draw the GENERATED shapes rather than the downloaded
models — without that flag every modelled type renders its model and its `Style` is never seen. So geometry can
move one function at a time and the rendered images diff to prove nothing changed.

Order, each step shippable and each verified by the render diff:

1. `AirframeStyle` + the mesh container to Core; Bridge converts. **No geometry moves.** Pixel-identical renders.
2. Fuselage lathe and canopy.
3. Wings, tail, control surfaces.
4. Struts, gear, floats, slats, spoilers, prop.
5. `GltfExport`, and a test that every type exports and re-imports with the right span.
6. Glass consumes the `.glb` set.

## 5. What this does NOT solve, said plainly

- **Faces.** Nothing here helps. Different geometry, different tooling, different licensing (MetaHuman and Ready
  Player Me each carry their own restrictions). If Glass needs faces that is a separate decision.
- **Generic props** — buildings, vehicles, scenery. A parametric *aircraft* generator does not touch them.
- **Trademark.** Generating the mesh yourself gives clean copyright and changes nothing about Textron's or
  Piper's marks. That is handled by naming (see the protocol's "no brand names in the display name") and by a
  lawyer, not by geometry.

## 6. Decisions — ANSWERED by Ty, 2026-10-21

1. **The generated fleet is the SHIPPING LOOK**, not a fallback. It becomes what players fly, and the downloaded
   models are retired as each type is replaced. This is the only answer that removes the licensing exposure rather
   than managing it, and it sets the bar: steps 2–4 are not a faithful port of today's shapes. They need real art
   — panel lines, rivets, glass, wear, something that holds up next to helijah's work. Plan for that, not for
   "geometrically correct and flat".
2. **Keep both paths alive, toggled.** `SessionSettings.UseAirframeModels` stays a live switch and each aeroplane
   moves over when its generated version is actually good enough, verified by the render diff. Nothing breaks
   mid-flight and the two can be compared side by side. The cost, accepted: the CC BY attribution duty and the
   chain-of-title risk persist until the last type switches, so the attribution must reach users in the meantime
   (the credits screen, §7).
3. **Glass gets exported `.glb` files.** Core emits one per aeroplane; Glass loads them in three.js. Unreal and
   Blender read the same files for free. Geometry fixed at export time is fine for an overlay. No TypeScript port
   of the generator — one generator, in C#, in Core.

### What answer 1 changes about the staging

Step 1 is unchanged and still has to be pixel-identical. Steps 2–4 each gain an art pass on top of the geometry
move, and the render diff stops being "prove nothing changed" and becomes "prove nothing REGRESSED" — the images
are expected to improve, so each step needs a look rather than a byte compare. Budget accordingly: the port is the
small half.

## 7. First, and independent of all of the above

The attribution gap is live NOW and stays live for as long as decision 2 keeps the downloaded models in the build.
`docs/CREDITS.md` lists all 17 models correctly, but it is a repo file — it does not reach a single user. CC BY
requires the credit to reach the person using the work. A credits screen in `StartMenu` reading the file from
StreamingAssets closes it, and it is a couple of hours rather than a project.
