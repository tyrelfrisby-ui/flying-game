# Ty's decisions

Ty Frisby's answers to design questions, recorded as he gave them. Newest first. When code, a model or a
checker disagrees with an entry here, this file wins: change the code, not the decision. Link each entry to
the commits and checks that enforce it.

## 2026-11-02 — Great Lakes switches: one labelled row along the bottom of the panel

- **Decision:** "arrange the Great Lakes switches in a SINGLE ROW along the BOTTOM of the instrument panel, each
  with a legible placard label (MASTER, MAGS, FUEL PUMP, etc. — whatever each switch is), keeping them grabbable
  and inside the cockpit (the interior test must pass)."
- **Context:** Ty, 2026-11-02 (build after 2026.1102): "the switches were outside of the great lakes on the sides."
- **Where:** `tools/own_models/gl_quadrant_v3.py` (strip `GL_SwitchStrip_R/F` with placards), checked by
  `tools/own_models/check_gl_quadrant.py` and `CockpitControlInteriorTests`.

## 2026-11-02 — Great Lakes throttle is a quadrant lever

- **Decision:** "the Great Lakes throttle is a QUADRANT LEVER (not push-pull)." Model it on the left sidewall of
  each cockpit per the 2T-1A reference photos, with prop and mixture beside it, grabbable by the VR hands.
- **Context:** Ty, 2026-11-02: "hand control did not work on the great lakes stick or throttle."
- **Standing order (Ty, 2026-11-01):** from the seat, left to right: THROTTLE (black), PROP (blue), MIXTURE (red).
  All three forward = full throttle / fine pitch / rich.
- **Where:** `tools/own_models/gl_quadrant_v3.py` (levers `Throttle_R/F`, `Ctl_PushPull_Prop/Mixture(_Front)`,
  grip empties `Grip_Throttle_Rear/Front`), checked by `tools/own_models/check_great_lakes_rig.py`,
  `tools/own_models/check_gl_quadrant.py` and
  `CockpitControlInteriorTests.Great_Lakes_throttle_is_a_grabbable_quadrant_lever_on_the_left_sidewall`.
