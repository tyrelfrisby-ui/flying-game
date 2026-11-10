# Shared instrument face paint

`tools/own_models/instrument_face_style.py` is the common palette, bold font and raster/vector stroke treatment.
It follows the Extra's white markings on black. J-3 and PA-18 variants use cream with black ink. The generated
paint constants in `InstrumentFaces.cs` feed `AnalogGauges` for every aircraft, including Grob, Swift,
Decathlon and Beaver configurations without their own detailed cockpit instrument meshes. This does not add
new cockpit meshes to those aircraft. Glass Overlay is unchanged.

Dedicated embedded dial/compass materials go through the shared GLB save path. Existing art generators use the
shared bold font. C172 white vector primitives receive a 0.12 mm stroke expansion in the shared GLB save pass. The C172 faces, markings and coloured arcs now use the existing instrument lighting shader as well.
The existing sun/sky floor and marking glow are unchanged on both platforms.

Scales, units, arcs and needle calibrations remain those of each existing caller. This is a paint change,
not a fresh POH audit. The offline checker compares coloured pixels and retained rig calibration data to the
branch baseline. It also checks transparent face alpha, graph validity and idempotence.

## Pitts layout

Front: airspeed, altimeter, g-meter and tachometer, left to right; no switches/radio knobs. Those four faces
use the corresponding existing rear live dial art and needle maps. The old anonymous dial triangles are
removed from the mixed Cockpit mesh. Each face sits ahead of the bezel cap. A front prop lever parallels the
existing front throttle, reading the existing prop input and using the throttle's proven visual travel.

Rear: MASTER, ALTERNATOR, FUEL PUMP, SMOKE, plus retained prop/mixture/fuel valve and existing throttle.
The alternator is visual only; this task adds no electrical physics. Other old controls and their mounts are
inert compatibility nodes, removed from the active controls/rig lists. Node indices are never renumbered.

`pitts_cockpit_v3.py` now runs `pitts_panel_v4.py` after its existing baseline pass, so regeneration retains this
layout. The v4 pass can also be applied to the current assets directly and is idempotent.

## Regeneration and handoff

```
nice /usr/bin/python3 tools/own_models/style_fleet_instruments.py
nice /usr/bin/python3 tools/own_models/check_instrument_style.py
nice /usr/bin/python3 tools/own_models/render_instrument_review.py
```

The manifest prevents repeated stroke growth in standalone runtime PNGs. Embedded materials carry a style
marker; replacing a material through the model generator clears it and reapplies the shared paint.

For the parallel Extra lane merge, retain its cockpit geometry and run the style pass on its final GLBs. The
only edits to the Extra-specific generator are shared font/finish calls. No shared layout is placed in that file.

Review images are CPU geometry/texture renders, not Unity screenshots or lighting validation. Rear view uses
Eye_Rear (glTF 1.325, .300, 0); front view is an inspection camera at (.53, .300, 0). Blender crashed at startup.
The Claude lane owns the pending build/device check. C# test expectations have been updated but not compiled
or run here, following Ty's no-build instruction. Nothing is approved by this document.

## Local commit blocked by sandbox

The final tracked-ignore check printed nothing. Git staging failed because this worktree's index is under
`/Users/tyfrisby/Developer/flying-game/.git/worktrees/codex-instrument-style/`, outside this session's writable
roots (`index.lock: Operation not permitted`). No local commit was created. Changes remain in this lane's
working tree; no push, main update or other worktree change was attempted.

## Pitts photo follow-up (2026-11-04)

Read PITTS-REF-NOTES.md and opened both reference photos. `pitts_reference_visuals.py`
runs after v4: staggered front four / rear seven instruments, rough black curved
panel shells, left blue prop blade in the down/locked pose, right upright fuel
valve, forward-pointing left trim lever and sidewall legends / position ticks.
Photo proportions are fitted to the existing cockpit; they are not factory measurements.
Existing instrument art, coloured arcs, rig calibration and simulation are preserved.
The new sidewall models are static visuals: prop unlocking/travel, fuel starvation,
trim interaction, mixture, cowl flaps and floor-window work from the wider reference
notes are not implemented by this visuals-only follow-up. Glass Overlay unchanged.

Offline style/graph checks passed; CPU panel renders reviewed. No build or push.
Tracked-ignore check printed nothing; staging again failed creating index.lock in
the external shared Git metadata directory. No commit could be created in this sandbox.
