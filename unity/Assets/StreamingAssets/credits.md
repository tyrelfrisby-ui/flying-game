
## Sound
- Rolls-Royce Merlin static run — Freesound #276597 by squashy555, CC0 / public domain (https://freesound.org/people/squashy555/sounds/276597/). Cut into rpm loops for the P-51 (unity/Assets/_Project/Resources/Audio/merlin_*.wav). No attribution required; credited anyway.

## 3-D models

Every model is used under its own licence; the full notice, the download date and any extra condition live in
`unity/Assets/_Project/Resources/Models/<id>/LICENSE.txt`. CC Attribution requires the credit to reach the person
using the work, which is what this list is for.

- **boeing-737-like** — "B737-800 Model" by hikami3150, https://sketchfab.com/3d-models/b737-800-model-6cbe380405794ea69d6b0a3d144dd1af, licensed under CC Attribution
- **c172-like** — "Cessna 172" by Viva_1, https://sketchfab.com/3d-models/cessna-172-c2a410d914f746c684a63674485c5cbf, licensed under CC Attribution
- **cirrus-sr22-like** — "Cirrus SR 22" by helijah (@helijah), https://sketchfab.com/3d-models/f0f1089f374f4d398f0483403fe6af7a, licensed under Free Standard
- **dc3-like** — "Douglas C-47 Skytrain" by Spark_Customs, https://sketchfab.com/3d-models/douglas-c-47-skytrain-bd01ee417416492f919a4b988f03bc8b, licensed under CC Attribution
- **extra-300-like** — what a player sees, in every livery (Talk Flying by default, plain N300UY as livery `n300uy`): our own EA 300, `extra-300-like-own.glb` / `extra-300-like-own-n300uy.glb`, built by `tools/own_models/build_ea300.py` (provenance: `docs/models/ea300-own-PROVENANCE.md`).
  - **Shape reference: "EXTRA-EA300" for FlightGear by Patrizio Melis** (© 2017–2019, https://github.com/cobe571/EXTRA-EA300), licensed under the **GNU GPL** (v2 per its `LICENSE`/`COPYNG`; its `License.txt` says v3 or later). By Ty's choice, the airframe shell from v2 on was measured from that model into numeric tables (`tools/own_models/ea300_ref_tables.json`) and redrawn to match it within a few centimetres. No FlightGear mesh, UVs or textures are copied, but the shape may count as a derivative of the GPL work. **A GPL derivative review is pending before App Store release** (AGENT-LOG.md, 2026-10-27): not a blocker for TestFlight/internal builds. The cockpit, engine, cowl inlets, 3-blade prop and all paint are our own work.
  - **Fonts** (pre-rendered into the baked paint; no font files ship): Liberation Sans Bold (Liberation Fonts, Red Hat) for the fuselage lettering and Archivo Black (Omnibus-Type) for the wing lettering, both under the **SIL Open Font License 1.1**.
  - The Talk Flying fin badge is cropped from the project's own Talk Flying logo.
  - _Not shown:_ "Aerobatic Airplane" by Kemal Çolak (@colakart), https://sketchfab.com/3d-models/6bdb6d7c221b463c8943a1d58c13f96f, licensed under CC Attribution, stays in the folder (`extra-300-like.glb`) but is no longer loaded. The FlightGear-derived EA300 GLB from codex/fg-models is retired and is not in the build.
- **f86-sabre-like** — "F86 Sabre" by Aidan6604 (@Aidan6604), https://sketchfab.com/3d-models/61110a84fd094893895be5c0cfc4e024, licensed under CC Attribution
- **geebee-r2-like** — "Gee Bee R-1" by helijah, https://sketchfab.com/3d-models/gee-bee-r-1-xgTLDRqv1tJyWDKl7ePtOwcWo0O, licensed under CC Attribution  _the model is the Gee Bee R-1 (used for the R-2 type)._
- **glider-2-33-like** — "Grob Astir" by helijah, https://sketchfab.com/3d-models/grob-astir-0b2bfc642c3d4368b1115003827f34fd, licensed under CC Attribution  _the model is a Grob Astir; the 2-33 is drawn procedurally (AirframeModels.cs), so this model is not shown._
- **glider-eb29r-like** — "UNDERPOLY: Free Sailplane Glider" by UNDERPOLY Project, https://sketchfab.com/3d-models/underpoly-free-sailplane-glider-45ffefc38fcf4e76a9d0c2a4e76262ef, licensed under CC Attribution  _a generic ~15 m sailplane standing in for a 29.3 m open-class aeroplane. Still what a player sees by default; `tools/model_eb29.py` draws ours, shown with Options → Airframe → Ours._
- **glider-swift-s1-like** — modelled for this project with `tools/model_swift_s1.py`; no downloaded model, nothing to attribute to anyone else  _drawn to EASA TCDS A.038, with the cruciform tail three freely-licensed photographs of SP-P600 show. Shown only with Options → Airframe → Ours._
- **hughes-h4-like** — "Hughes H4 Hercules" by helijah (@helijah), https://sketchfab.com/3d-models/x0y09hdXZmrWo1vLbT07toq0z1c, licensed under CC Attribution
- **jn4-jenny-like** — "Curtiss JN-4 "Jenny"" by helijah (BARANGER Emmanuel), https://sketchfab.com/3d-models/curtiss-jn-4-jenny-65df9818a34c44f3a77764d665408f8a, licensed under CC Attribution
- **p51d-like** — "P-51 (Low Ploy)" by Terminal-NJ (@Din-Gabisi), https://sketchfab.com/3d-models/fb4c5b00a7de40a2a8222ec3d049f0e8, licensed under CC Attribution
- **pa18-cub-like** — "Piper Pa 18" by helijah, https://sketchfab.com/3d-models/piper-pa-18-413e3f81b0db4e0ebba0f27490732770, licensed under CC Attribution
- **pa28-archer-like** — "Piper Pa 28 "Cadet"" by helijah (@helijah), https://sketchfab.com/3d-models/ba310f1e1ba349c7a54ab9db92e48970, licensed under CC Attribution
- **pitts-s2b-like** — what a player sees, in every livery (N31DG by default, the factory N1191 sunburst as livery `n1191`): our own clean-room Pitts S-2B, `pitts-s2b-like-own.glb` / `pitts-s2b-like-own-n1191.glb`, built by `tools/own_models/build_pitts_s2b.py` (provenance: `docs/models/pitts-s2b-own-PROVENANCE.md`). Modelled for this project; no third-party mesh was used, traced or measured (dimensions from the Aviat brochure, Jane's and FAA TCDS A8SO; paint from reference photos by eye only), so there is nothing to attribute to anyone else.
  - **Fonts** (pre-rendered into the baked paint; no font files ship): Liberation Sans Bold (Liberation Fonts, Red Hat) for the registrations, under the **SIL Open Font License 1.1**.
- **seminole-like** — "Piper Pa 23 Apache/Aztec" by helijah, https://sketchfab.com/3d-models/piper-pa-23-apacheaztec-0645db1d6e404cbca2800b5c82ad83e0, licensed under Free Standard  _the model is a Piper Apache/Aztec, a different aeroplane with a conventional tail. Still what a player sees by default; `tools/model_pa44.py` draws ours, shown with Options → Airframe → Ours._
- **stearman-pt17-like** — "Boeing Stearman PT.17" by helijah (@helijah), https://sketchfab.com/3d-models/3ccea297db744ba4aee10ef3f5289bf2, licensed under CC Attribution
- **target-drone-like** — "QinetiQ Banshee Flight Military Drone" by Muhamad Mirza Arrafi (@MirzaArrafiERV_45), https://sketchfab.com/3d-models/af7b7cdb9f524420a0e4f533388c8360, licensed under CC Attribution
