# Extra 300 shared cockpit + aileron junctions (lane/claude-extra-cockpit-shared, 2026-11-04)

- `eye/` — Unity `CockpitEyeShots`, the game's own build of the aeroplane (rig posed, instrument lighting on, LCDs bound),
  from the config eye `seats[].eye`. `extra-quest-*`: one Quest 3S eye (vfov 90); `extra-ios-*`: the iPhone cockpit camera
  (vfov 72, 2160x1000). `sun` = high sun ahead-left; `night` = sky only, very dim. `-hand` shots carry a gloved-fist proxy
  (95 x 105 x 85 mm) on the stick grip. `extra-panel-*`: the rear panel close (vfov 46).
- `aileron/` — Blender, `tools/own_models/render_aileron_deflect.py`: 2 mm section at the left aileron's mid-span, aileron
  orange, wing dark, turned about its rig hinge (30 % of its chord). `-30` = trailing edge DOWN 30 deg (the nose stands above
  the upper skin), `+0` neutral, `+30` trailing edge up. The small orange piece below is the spade arm.
- `junction-before/`, `junction-after/` — Blender, `tools/own_models/render_aileron_junctions.py`, neutral: the aileron's
  inboard end from above / below, and a section at 30 % of its span. Numbers: `tools/own_models/check_aileron_junction.py`.
