# Great Lakes P3d — operator render corrections

CPU geometry generated for all three liveries from `0a1a7b4` bases, using
`vr_cockpit.py`. P3c's photo references and supplied before/after views were
opened, including `gl_cabane_gear_side.png`, `gl_cockpits_above.png`, and
`gl_cabane_closeup.png`. Dimensions remain photo-informed estimates.
No Blender, Unity, xcodebuild, commits, or CONTRACT.md creation performed.
New visual review is pending the operator renders below.

## 1. Main gear and Claude handoff

Replaced all four horizontal P3c supports. The 35 mm OD V tubes now connect
the actual lower oleo/axle pin A to two inboard belly clevis pins. A comes
from the world origin of `Oleo_Cylinder_L/R`. **Do not use the origin of
`Gear_Axle_L/R` for A**: those mesh origins are the inboard H pivot.
Both new tubes meet the existing axle solid at A, with zero measured solid
gap. Fittings have conforming pads and cross bolts as in P3c.

Builder coordinates in metres: X forward, Y left, Z up. Mirror Y for R.
Rounded coordinates below; full precision per side is in
`vr-cockpit-phase3c-checks.json`, and in each GLB's `vrCockpitPhase3c.gear`.
The metadata retains historical `bottom` = belly fitting and `top` = axle
endpoint names; the axle endpoint is physically lower.

| Livery | Forward belly pin | Aft belly pin | Common lower axle/oleo pin A |
|---|---|---|---|
| own (v1, static pose) | (0.100000, 0.100000, -0.702448) | (-0.700000, 0.100000, -0.832096) | (-0.267774, 0.964667, -1.289256) |
| red-sunburst | (0.100000, 0.100000, -0.742380) | (-0.700000, 0.100000, -0.832096) | (-0.377000, 0.840000, -1.487000) |
| white-stripes | (0.100000, 0.100000, -0.742380) | (-0.700000, 0.100000, -0.832096) | (-0.377000, 0.840000, -1.487000) |

Tube elevations above horizontal: v1 forward **31.985°**, aft **25.310°**;
red/white forward **40.223°**, aft **39.046°**. Tubes rise and run inboard
from A, with separate fore/aft destinations, so both projections show diagonals.
The different rest endpoints follow the existing bases' different gear poses.

**Claude rig item: yes, the new V implies a different hinge line.** If treated
as a rigid swinging V, its axis is the line between the two new belly pins
(Y ±0.10, with the Z slope above). That differs from the existing H-to-C
axis through `Gear_Pivot_*` (-0.36, ±0.05, -0.83) and `Gear_HingeAxis_*`
(-0.85, ±0.40, -0.78). The existing oleo top is still (-0.20, ±0.62, -0.60).
No markers, moving meshes, hierarchy, transforms, rig extras, or rig JSON were
changed. Claude owns reconciling the hinge/kinematics and animation parenting.
The new tubes are authored as fixed airframe rest geometry; current gear
animation will not carry them with the axle. Do not infer animation approval
from this rest-pose geometry check. Retained P3c pivot/compression sleeves and
protected axle members remain intact.

No colliders changed. Claude should reconcile capsules with the selected
kinematics; new V radii are 17.5 mm along these endpoint pairs. Keep belly
fittings airframe-fixed. Screens remain visual acrylic.

## 2. Windshields and Unity import

Each screen is now a **closed 3.5 mm shell** with two face surfaces and 304
perimeter edge triangles. Edge faces are the second primitive on the same
windshield node, with a polished acrylic material, rather than a frame tube.
Normals face outward; the checker welds the complete mesh and requires exactly
two incident triangles at every edge, plus one connected component.

Face PBR: slight blue tint (0.94, 0.98, 1), alpha **0.28**, roughness **0.055**,
IOR **1.49**, nonmetallic dielectric specular. Polished edge PBR: tint
(0.84, 0.94, 1), alpha **0.68**, roughness **0.035**. Both use `BLEND` and
are double sided. There is **no transmission extension**: this is the requested
alpha PBR option, avoiding the old alpha 0.12 + transmission 0.96 combination.
IOR is carried by `KHR_materials_ior`; importers without support retain their
ordinary dielectric Fresnel/specular behavior. No opaque border added.

The installed glTFast 6.9.1 `BuiltInMaterialGenerator.cs` maps `BLEND` to Fade,
SrcAlpha/OneMinusSrcAlpha, Transparent queue 3000, ZWrite=0. It imports base
color/alpha and roughness into its metallic-roughness shader. Built-in's
transmission approximation is bypassed because that extension is absent.
It does not apply the IOR extension; its standard dielectric specular is the
fallback. Thus imported alpha stays explicitly between 0 and 1 on both
primitives; no runtime material override is needed. This is source inspection,
not a Unity render test. Blender imports the same reflective alpha PBR shells.
Environment/light reflections and the brighter physical edges provide visibility.

Both **front and rear bases are 25.000 mm forward of their respective opening's
forward edge at the centreline**, all three liveries. Bases retain P3c's forward
corner wrap and follow the actual deck with 4 mm seating clearance; neither
cockpit/control layout was moved. The full base is outside the aperture:
v1 follows a forward half ellipse; red/white follow their wider rectangular
openings with aft-swept outer corners. The 25 mm number describes the centreline
base foot, not every point of the curved side wrap. The closed outer surface is
3.5 mm away along the local sheet normal. Clearances test both surfaces/edges.

| Livery | Front opening forward X | Front base X | Rear opening forward X | Rear base X |
|---|---:|---:|---:|---:|
| own | -0.970 | -0.945 | -1.640 | -1.615 |
| red/white | -0.950 | -0.925 | -1.480 | -1.455 |

## 3. Cameras, checks, operator commands

`render_gl_p3c.py` now also emits `gl3c_windscreen_rear.png`,
`gl3c_windscreen_front.png`, and `gl3c_gear_front.png`. All eight cameras use
literal poses and accept any commit's GLB; no metadata dependency or cutaways.
The same lighting, import path, and cameras apply before and after.

Checker PASS all three liveries: axle and belly-fitting contact ≤2 mm,
upward/inboard endpoints, 20–65° tube elevation; shell thickness
3.49988–3.50012 mm; 304 edge faces, closed manifold, one component per screen;
25 mm base distances, deck seating and corner wrap; alpha/roughness/IOR;
cockpit-aperture and both eye-to-panel clearance checks. Existing P3 checker
PASS 10 assets / 440 controls, minimum clearance 163.75 mm.

`vr-cockpit-phase3d-preservation.json` records byte-identical canonical
regeneration and second-run idempotence. All 259/259/357 existing node records
match d936b10, including all protected markers and rig extras. All unchanged
mesh nodes were compared geometrically (148/148/218); shared rig JSON is byte
unchanged. Cabane, coaming, P3a controls and moving gear meshes remain intact.

```sh
export PYTHONDONTWRITEBYTECODE=1
/tmp/topo-venv/bin/python tools/own_models/vr_cockpit.py
/tmp/topo-venv/bin/python tools/own_models/check_gl_p3c.py
/tmp/topo-venv/bin/python tools/own_models/check_vr_phase3.py
```

For a clean rebuild, reset ONLY the three GLBs from `0a1a7b4` before the CPU
command; existing P3c metadata is an update guard. Do not reset current sources
or rig JSON. Operator-only Blender comparison against committed P3c:

```sh
mkdir -p /tmp/gl3d-before-assets
for name in great-lakes-like-own great-lakes-like-own-red-sunburst great-lakes-like-own-white-stripes; do
  git show "d936b10:unity/Assets/_Project/Resources/Models/great-lakes-like/$name.glb" > "/tmp/gl3d-before-assets/$name.glb"
  PYTHONDONTWRITEBYTECODE=1 blender -b --factory-startup --python tools/own_models/render_gl_p3c.py -- "/tmp/gl3d-before/$name" --model "/tmp/gl3d-before-assets/$name.glb"
  PYTHONDONTWRITEBYTECODE=1 blender -b --factory-startup --python tools/own_models/render_gl_p3c.py -- "/tmp/gl3d-after/$name" --model "unity/Assets/_Project/Resources/Models/great-lakes-like/$name.glb"
done
```
