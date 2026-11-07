# Water visuals overhaul — review evidence

Lane: `lane/codex-water-visuals`; baseline runtime sources: `e3e754bc`.
Full gallery: `~/Developer/agent-lanes/renders/water-visuals-overhaul/index.html`. This folder contains ten selected unaltered Unity PNGs. Nothing is visually approved by this lane.

- `before-taxi` / `after-taxi`: 2.5 m/s calm tow, Tail + Chase + close bow. The extra dark surface silhouette is present before and absent after; one aircraft mirror remains.
- `before-spray` / `after-spray`: 18 m/s tow with real `SprayField` parcels and foam. Look at `bow.png`; sheets/beads are finer and less opaque, foam has broken cell edges. This is not a free-running ski takeoff.
- `before-sunset` / `after-sunset`: identical controlled shore stage at the current island water lane. `bow.png` shows the removed 91% mirror floor best; `sunset-hero.png` is a low grazing view where a strong reflection is still correct.
- `after-submerged/bow.png`: zero speed, aircraft 35 cm lower; submerged float sides remain visible through the volume.
- Reflection-source PNGs and text diagnostics accompany each capture. Main captures are 1600 × 1000; portrait sunset 1000 × 1600. Reflection tier is High (1536 max dimension), matching before and after.

Validation: seven successful Unity capture invocations; every fixture logged wet=True. WaterMagentaCheck PASS: six main views and six reflection targets, zero magenta pixels, water layer correct. iOS ** BUILD SUCCEEDED **, stamp **2026.1105.2035** / build **202611052035**, zero C# and shader errors. Explicit NOINSTALL exit; no install, merge or push. Platform-lane strict parity check: OK; this review lane has not been integrated into either platform lane.

Limits: transmission uses alpha blending with depth absorption; no screen-space refraction displacement, caustics or submerged-bed shadow. Extinction .28/m is a visual clarity choice. One mirror camera and existing RT tiers/particle caps remain; five reflection taps add four texture reads and spray adds one depth read. Phone/iPad/Quest frame time and fly appearance still require review. Foam remains a budgeted parcel representation, not a fluid simulation. Glass Overlay untouched.

Initial dry legacy-harbor renders are preserved in `invalid-dry-fixture/`, excluded from the gallery and acceptance evidence. The fixture now rejects dry captures and follows the real island water lane/heading.

Full logs are in the external gallery’s `evidence/` folder; magenta-report.txt is included here. A selected 10-PNG set is committed in the lane's `renders/water-visuals-overhaul/`.
