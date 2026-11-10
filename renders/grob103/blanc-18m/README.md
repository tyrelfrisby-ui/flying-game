# Blanc Twin III Acro v2 — 18 m — NEEDS YOU

G 103 C Twin III Acro, OSB 315-66 target, fixed tandem gear and upper-surface
Type GROB airbrakes. Source model and licence: Florian Blanc, CC BY-SA 4.0;
see CREDITS.md and LICENSE.txt beside both GLBs. No gate is marked approved.
The Blanc conversion and eye points were approved by Ty on 2026-11-03;
this v2 geometry/render review is pending.

Both **input** GLBs measured **18.000044823 m** span, so blanc-v1's README
was correct and the Claude handoff's 17.5 m GLB claim was stale. Their area
measured 17.232331 m². V2 retains span and scales wing chord by 1.0155329673.
Both output GLBs measure **18.000045776 m**, **17.500000169 m²**, length
8.179797739 m. Planform area integrates section chord from .45 m to each tip;
the hidden centre wing uses the .45 m chord. This extrapolation and the source
taper are approximations, not a recovered manufacturer's loft.

- Ailerons: author |Y| 5.17–8.68 m; upper-only airbrakes 2.43–3.91 m.
- Tip skid centres: author Y ±8.9 m, seated under the lower wing skin.
- Level-frame tyre-bottom contacts (author X,Y,Z metres): nose
  (+1.4595,0,-.600), main (-.5397,0,-.600), tail (-4.8768,0,-.260).
  Nose station moves to match the handoff config. Rounded tyre sizes retained;
  skin fillets, exposed tyre bottoms, no pants. Nose/main share a ground
  tangent; tail is .340 m clear. Main/tail rigid tyre support gives 4.487°
  pitch and .1567 m nose clearance. See ground-support.json for tangent points.
- `Nose_Steering`, `rudder`, rotation ±25° **ESTIMATED**, vertical fork axis,
  one child carrying fork + tyre + hub, independent axis witness. Skin fillet
  stays fixed. All 29 previous names/maps/hierarchy/extra fields preserved.
- Approved eyes remain bitwise unchanged. Both filenames/finishes are retained
  (v1 already used the same white/red synthetic D-103C finish for both).

Images: side, three-quarter, front, top with an 18.0 m span dimension line,
gear-main, gear-nose (straight), gear-nose-steered (+25°), gear-tail,
side-poh-overlay, and labelled contact-sheet.png. Images were inspected after
rendering. **CPU fallback, not Blender renders:** the required Blender binary
was attempted with `nice -n 5`, `-b --factory-startup`; it crashes during Metal
capability detection before Python. Temporary launch workarounds did not run.
The sandbox also refuses the requested priority adjustment. Blender studio
rendering is outstanding. The project CPU rasterizer renders the actual
exported GLB and a separately posed exported +25° nose assembly.

The overlay is regenerated from the current GLB and Twin III POH page 14 /
printed 1.6, uniform 854 px per 18 m, approximate scan-nose registration,
level axes, wings omitted. Reference pixels are review-only, outside the mesh
licence. Overlay mismatch is visible evidence, not a type-conformity pass.

Checks: check_grob103_blanc.py and check_grob103.py pass both GLBs, including
30 origins/axis markers/maps/hierarchy, control motion, upper-only spoiler
geometry, wing stations/area/span, tyres/contacts, UVs/normals/graph, approved
eye clearance, all 2,016 cockpit pairs and 1,608 shell-envelope samples per
asset. A separate comparison against the pre-v2 committed rig verified all
29 old maps/names/parents/extra fields; eye world transforms were compared
bitwise against v1. No Grob exists in sights-spec.json, so sights checks do
not apply. No Unity, xcodebuild, install, deployment or merge was run.

Rebuild: first run build_grob103_blanc.py to regenerate v1, then
`/path/to/python-with-numpy-pillow tools/own_models/build_grob103_18m.py`.
Alternatively supply `--source-dir` containing a preserved v1 GLB pair and
v1 grob103-own-rig.json. The converter rejects an already-v2 rig. Run the
checks, render_grob103_18m.py, and overlay_grob103_blanc.py with the local POH
and this output directory; contact_sheet() combines the nine views afterward.

Remaining estimates: steering travel/fork detail, visual wing loft and centre
area extrapolation, airbrake construction, vertical gear setup and skid shape.
The handoff config still says nose isSteerable=false and main radius .200 m;
Ty's steering decision is reflected here, while the requested retained visual
main tyre radius is .220 m. Physics integration belongs to the Claude lane.
