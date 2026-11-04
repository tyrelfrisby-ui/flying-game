# P3a hand controls — estimated geometry, review pending

No gate approved. CPU generated only; Blender/Unity/device review not run. Claude owns interaction/config/logic. Operator copies the naming seam below into the lane CONTRACT.md; no repository CONTRACT.md was created.

Each of Pitts, Extra and Great Lakes has **22 controls per seat / 44 total**, duplicated as an estimated training arrangement, linked by shared parameters. These are not claims about equipment duplication in a real aircraft. Rear has no suffix; front appends `_Front`. Moving mesh `Ctl_<Kind>_<Name>`, hinge/shaft origin in `pivot_gltf`, child empties `Grip_<Name>` and `Poke_<Name>`. Grip +X points forward in the existing model convention. Units metres; axes/pivots in exported glTF frame. Fixed `Mount_<Name>` backing plates are separate. Positions are **(E)**, on left/right panel-side console groups. JSON `controls` is the interaction inventory; `rig` remains the animation inventory (including instrument needles). Metadata is also embedded in GLB extras.

| Names (both seats, all three aircraft) | Kind | Travel | Detents / sim endpoints |
|---|---|---|---|
| Magneto | Rotary | 130° | OFF/R/L/BOTH/START at 0/30/60/90/130°; sim 0/1/2/3/4; START only springs to BOTH |
| Master, FuelPump, NavLights, Strobe, LandingLight, Smoke | Toggle | 40° | 0 OFF, 1 ON; two stops |
| Mixture, Prop | PushPull | 50 mm | continuous 0–1 |
| AlternateAir (Pitts/Extra) | PushPull | 35 mm | 0 normal, 1 alternate; two stops |
| CarbHeat (Great Lakes only) | PushPull | 35 mm | 0 cold, 1 hot; two stops |
| Trim | Rotary | 300° | continuous −1 to +1 |
| FuelSelector (Pitts/Extra) | Rotary | 180° | sim 0/1/2; provisional tank positions, Ty to identify |
| FuelSelector (Great Lakes) | Rotary | 90° | `fuel_valve` 0 OFF / 1 ON |
| ComOuter | Rotary | 540° | 19 MHz positions, 118–136; 1 MHz increments |
| ComInner | Rotary | 360° | 40 fractional positions, .025 MHz clicks; alternate 1/120 MHz spacing requires Claude channel table + Ty choice |
| ComVolume | Rotary | 270° | continuous 0–1 |
| ComSwap | PushPull | 4 mm | momentary 0/1, spring return 0 |
| XpdrDigit1/2/3/4 | Rotary | 315° | 8 octal stops 0–7, respectively thousands/hundreds/tens/units of `xpdr_squawk` |
| XpdrMode | Rotary | 90° | 0/1/2/3; Claude confirm mode enum |
| XpdrIdent | PushPull | 4 mm | momentary 0/1, spring return 0 |

`com_standby_mhz` is intentionally tuned, then ComSwap exchanges active/standby. Radio increment semantics and xpdr digit places are explicit; do not bind four digit knobs as four independent absolute squawk values. Existing lowercase ModelRig params preserved. New bindings requiring Claude: fuel_pump, nav_lights, strobe, landing_light, smoke, alternate_air/carb_heat, com_swap, xpdr_ident. Rig maps supply visual travel; the `controls` semantics supply interaction behavior. For a spring control only `spring_return_from` returns to `spring_return_to`; magneto detents OFF/R/L remain latched.

Prior stick, throttle, pedals/brakes, canopy, latches, harness and jettison names/rigs remain their previous interaction seam, not new Ctl aliases. Obsolete mixture/prop/trim/radio/mag/fuel selector meshes are removed; old names remain inert compatibility nodes. Great Lakes' formerly constant prop/mixture maps now have real travel through the new controls.

## Engine decision and questions for Ty

Pitts and Extra use injected AEIO-540s per task: alternate air estimated, **no carb heat**. Great Lakes project config has 134226 W (~180 hp), constant-speed prop and smoke displacement 361 in³; it does **not** identify an AEIO engine or injection. Its builder explicitly says O-360. Therefore Great Lakes gets provisional **CarbHeat**, not an invented AEIO confirmation. Confirm whether this project should instead depict AEIO-360 + alternate air.

Smoke switch chosen in **each aircraft, both seats**: left panel-side group, top row, beside the electrical switches (E), latched toggle. Not on stick. Exact coordinates are in rig JSON.

Ty: confirm smoke location per type (stick grip, throttle, or panel); front-seat electrical/radio duplication; actual fuel selector tank labels; alternate-air equipment on Pitts/Extra; Great Lakes engine/induction; COM 25 kHz vs 8.33 kHz; transponder mode mapping; switch/knob placements and reach while harnessed. These are owner review questions, not approvals claimed.

## Smoke outlet handoff

SmokeController, SmokeTrail and SmokeConfig already exist. Great Lakes has measured config `outletBodyM [-0.151,-0.187,0.628]`; Extra has `aftOfNoseM=1.22`, 30° down, 540 in³ but **no outletBodyM**. No smoke visuals/config/logic changed.

Extra measured candidate: aft right engine exhaust stub, Engine mesh, 20 duplicated end-ring vertices averaging **glTF (-0.96999996,-0.26000001,-0.36000010) m**, radius 26 mm (min/max 25.99989/26.00012 mm). Builder source endpoint **(+0.970,-0.360,-0.260) m**, `build_ea300.py` exhaust stub (`k=2`, right bank), axis from source (+0.990,-0.360,-0.100) toward endpoint. This is a modeled engine stub, **not a confirmed external collector outlet**. Claude must apply actual AirframeModels placement/scale/CG before assigning outletBodyM; do not copy raw model coordinates into body config. Ty should confirm final collector routing; add collector geometry in a subsequent review if needed.

## Validation and operator commands

`check_vr_phase3.py`: 10 GLBs, 440 new controls. Graph validation, unique names, mesh/local radius, pivot/axis, child contacts, metadata, detents and map travel checked. Conservative 40 mm control sphere plus entire pull travel; estimated 65 mm knee spheres and stick/throttle sweep boxes. Minimum clearance **27.17 mm**. This does not prove hand approach, neighboring-control separation, skin/mount fit, anthropometric coverage or usability. Visual reach and collision review remains required.

Fresh CPU migration from **0a1a7b4** tested for all 10 GLBs (Pitts rig created by P1); checks pass and a repeat update is byte-identical. Render presets not run here. No commits, builds, deployment or approval.

```sh
cd /Users/tyfrisby/Developer/flying-game-worktrees/codex-vr-cockpit
export PYTHONDONTWRITEBYTECODE=1
/Applications/Blender.app/Contents/Resources/5.2/python/bin/python3.13 tools/own_models/vr_cockpit.py
/Applications/Blender.app/Contents/Resources/5.2/python/bin/python3.13 tools/own_models/check_vr_phase3.py
# Operator only, outside sandbox: six panel views (both seats), smoke off/on each aircraft,
# plus Pitts magneto OFF/START close-ups. All filenames begin after3a_.
/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python tools/own_models/render_vr_phase3.py -- renders/phase3a
```
