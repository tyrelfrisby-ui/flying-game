# J-3 cockpit v16: before / after from the rear seat (2026-11-02)

These are Ty's seven cockpit changes, rendered on the **wheels** variant. **Before** is the GLB at ce62f612. **After** is
the GLB on `lane/claude-j3-cockpit`, built on Codex's new panel atlas. Each pair uses the same camera.

**Camera.** Every shot except 04b sits at the exact approved **rear eye** that the game uses:
- glTF (0.7774, 0.5067, 0.0), from `configs/aircraft/j3-cub-like.json` seat `rear`, placed as `AirframeModels.Place`
  places the model (see `tools/own_models/check_eye_interiors.py`).
- Only the view direction turns, as if you turned your head.
- The wide shots use a 96° horizontal field of view, close to a Quest 3S.
- The close-ups are a narrow field of view from the **same eye**: a zoom, not a move.

**How they were made.** `blender -b --python tools/own_models/j3_cockpit_shots.py -- <glb> <dir> <before|after>`.
- Renderer: EEVEE, sky light plus one sun, 1200×900.
- Materials are as exported. Every J-3 material is double-sided, as it is in the game.

Nothing here is approved. The headset is the judge.

| file | what it shows |
|---|---|
| `01-top-tubes-{before,after}.png` | Looking up and forward into the wing well. **Before:** the ceiling tubes hang at the wing's lower skin and meet the windshield V at the front spar. **After:** the tubes are 0.14–0.17 m higher. They run straight from the top of the front-spar face to the rear spar, 1.6–4.5 cm under the clear skylight and visibly under the glass, and they stay inside the root ribs. The windshield V still meets at the bottom of the front spar. |
| `02-mag-switch-{before,after}.png` | Looking forward-left and up. **Before:** the mag switch is on the ceiling ahead of the front spar (small, centre of the frame). **After:** it is on the LEFT root rib, the vertical yellow wall above the left window, 9.5 cm aft of the front spar, low on the rib, facing inboard. |
| `03-carb-heat-{before,after}.png` | Looking down the right wall. **Before:** a black rod runs aft from the carb heat plate to a red rear-seat knob. **After:** the rod and the knob are gone. The only carb heat control is the lever on the plate low on the right wall, just aft of the panel, so from the rear seat you lean forward. |
| `04-under-tank-tubes-{before,after}.png` | Straight ahead and down, under the panel. **Before:** a diagonal tube on each wall and a horizontal cross bar low in the footwell. **After:** one tube per side from the panel's bottom corners down to a single fitting on the firewall centreline (the V behind the front stick). The low cross bar is gone. |
| `04b-under-tank-tubes-cutaway-{before,after}.png` | **Not the rear eye.** The camera is low in the front footwell, where the front pilot's shins are, looking forward and slightly up, because the rear eye cannot see all the tube work. **After:** a V from the clusters at the panel's bottom corners to the firewall fitting between the front pedals. The **only** cross bar runs just ahead of the panel's bottom edge; from the seats the panel hides it, and only this low camera sees its underside. |
| `05-lettering-{before,after}.png` | The whole front office as the rear seat sees it, to judge the gauge faces and the wall plates together at headset scale. |
| `06-trim-crank-{before,after}.png` | Looking down the left wall. **Before:** the trim plate and crank sit right under the fuel plate. **After:** they are 0.30 m aft, beside the front seat. The plate grew from 88 to 104 mm and has larger lettering. |
| `07-fuel-valve-{before,after}.png` | Looking down-left at the fuel shut-off. **Before:** a recessed D plate with a lever and a small red ball. **After:** the same place, now a flat D plate with a red push-pull knob on a chrome shaft and sleeve, labelled FUEL / PUSH ON / PULL OFF. It is drawn ON (pushed in); OFF pulls it 30 mm straight out of the wall. |
| `closeup-panel-lettering-{before,after}.png` | The panel from the rear eye, 24° field of view. This shows Codex's atlas: larger, bolder, darker lettering on a lighter face. Compare the RPM, AIR SPEED, ALTITUDE and oil labels. |
| `closeup-fuel-knob-{before,after}.png` | The fuel shut-off from the rear eye, 13° field of view. **After:** the push-pull knob, with all three labels readable past it. |
| `closeup-mag-switch-{before,after}.png` | The new mag switch spot from the rear eye, 14° field of view. **Before:** the bare rib, with the old switch on the ceiling at the bottom right. **After:** the switch on the rib. |
| `closeup-trim-crank-{before,after}.png` | The new trim spot, 16° field of view. **Before:** the empty wall beside the front seat. **After:** the plate (UP · NOSE · DOWN with a tick scale) and the crank. |
| `closeup-carb-heat-{before,after}.png` | The carb heat plate, 14° field of view. **Before:** the rod leaving it for the rear knob, plus the old under-tank tubes. **After:** the plate alone. |
| `closeup-throttle-front-{before,after}.png` | The front throttle plate, 14° field of view. CLOSED → OPEN / THROTTLE is slightly larger and white. |

## Things the renders show that this lane did not change

- **Mag switch close-up:** a crinkled sliver where the skylight's rubber trim turns round the left root nose, in front
  of the rib. It is in the before render too (`closeup-mag-switch-before.png`, bottom centre).
- **Carb heat plate:** from the rear seat it is almost edge-on, and its 10 mm bezel hides the top of CARB HEAT. Its
  recessed plate is from Ty's v14 photo, so it was left alone.
- **Cutaway 04b:** above the firewall you can see through to the cowling (the red fuel cap). The nose tank is not
  modelled. You cannot see this from either seat.
