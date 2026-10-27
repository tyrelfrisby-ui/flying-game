# Agent log — codex/own-ea300

## NEEDS YOU
- **GPL question on the EA 300 v2 (Ty's decision, for Claude to assess).** Ty asked for the EA 300 to be redrawn to match the approved FlightGear-based Extra (codex/fg-models v9, 5fdacad, wing raised about 10 in) to within about 1 in. That model comes from the FlightGear EA300 by Patrizio Melis, which is GPL. Ty was told about the GPL-derivative risk before he decided. v2 copies no vertices, faces, UVs or textures: the FG model was measured into numeric tables (tools/own_models/ea300_ref_tables.json), and all geometry was drawn fresh from those tables. However, the shape was fitted on purpose to match the GPL model to within a few centimetres. **Claude: please assess whether this counts as a GPL derivative for our licensing.** One option is to ask Patrizio Melis for written permission or a relicence. Until that's decided, treat this asset as possibly GPL-encumbered. Do not ship or merge it.

## HANDOFF
- [codex → Claude] codex/own-ea300 · decide the GPL status of extra-300-like-own.glb v2 (see NEEDS YOU and docs/models/ea300-own-PROVENANCE.md). Possible outcomes:
  - Accept it.
  - Ask Patrizio Melis for permission.
  - Revert to the v1 clean-room commit 638c05a.

## Log
### 2026-10-26 — Codex · codex/own-ea300 · EA 300 v2 redraw to the FG v9 shape
- Added tools/own_models/ea300_measure.py, which slices the v9 FG GLB into tables.
- Rewrote build_ea300.py to loft all-new geometry from those tables, with the v9 N300UY paint rules unchanged.
- Added ea300_compare.py, which measures the 3-D and silhouette deviation.
- Visible-surface p99 is ≤ 3.7 cm in every part. Silhouette outlines are within 25 mm on 99.3–99.9% of the outline. Details are in PROVENANCE.
- Unity rig binds 4/4 surfaces and 1/1 prop.
- Local commit only: no push and no merge.

## 2026-10-26 EA 300 own model v3 (codex/own-ea300)
Did the v3 shape pass and visual QA (see docs/models/ea300-own-PROVENANCE.md v3). Outputs are in the box at /workspace/ea300-own-v3/. Not pushed. The GPL NEEDS YOU note from v2 still stands.
- 2026-10-27 Chief/executor: EA300 own model v4 — added clean-room cockpit interior (seats, harness, sticks, front + rear panels, tub, tube frame); exterior unchanged; renders in /workspace/ea300-own-v4 (box).
- 2026-10-27 Chief/executor: EA300 own v5 — smaller/lower seats+sticks, front seat fwd 0.16 m, angular rear dash top 0.20 m deep with panel under; renders /workspace/ea300-own-v5 (box).

- 2026-10-27 Grok Bot executor: EA300 own v6 cockpit layout (seats/panel/sticks moved, black dash top, collision fixes). Local commit only.
- 2026-10-27 Grok Bot executor: EA300 own v7 cockpit layout (pans -6in, panel/front seat/stick fwd 6in, sticks +4in). Local commit only.
- 2026-10-27 Grok Bot executor: EA300 own v8 (baggage area, TALKFLYING.COM, TALK FLYING PODCAST). Local commit only.

- 2026-10-27 EA300 own v9: two liveries off one build (EA_LIVERY=tf -> extra-300-like-own.glb with fin TF badge; n300uy -> extra-300-like-own-n300uy.glb, N300UY sides, red tapered underwing bar, no TF branding). Side font now Liberation Sans Bold (OFL 1.1). Awaiting Ty sign-off; not handed to Aero Playground.
- 2026-10-27 EA300 own v10: parallel stab paint, side lettering centred wing TE/stab LE, 2nd thin red stripe, 22% underwing bar (plain), 3-blade prop (1/1 bound), TF fin badge centred. Awaiting Ty sign-off.
- 2026-10-27 EA300 own v11: straight spanwise stab paint lines; open cowl inlets; simplified AEIO-540 flat six (staggered banks, left 0.13 m aft), baffles, left governor. Awaiting Ty sign-off; not handed to Aero Playground.
