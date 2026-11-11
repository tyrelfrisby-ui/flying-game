# The shared cockpit instrument library

Ty, 2026-11-05 06:53: *"we don't need 6 different coordinators that is part of the problem we just need one and it gets
used 6 times. we need to start creating a list of things that get used over and over again so that they aren't built
from scratch each time like airspeed altitude turn coordinator attitude indicator etc... some will need to be "period
correct" like a p-51 or j3 cub but the majority will use the generic one"*. 07:26: *"G meter should be standardized too"*.

## What there is

One generator, one GLB per instrument, one runtime, used by every aircraft:

| Instrument | GLB (`Resources/Models/instruments/`) | Moving parts → param | Per aircraft |
|---|---|---|---|
| Turn coordinator | `turn-coordinator` | `TC_Airplane` ← `turn_rate_dps` (3 °/s = the 20° index), `TC_Ball` ← `slip_ball` | nothing |
| Airspeed | `airspeed` | `ASI_Needle` ← `airspeed_mph` / `airspeed_kias` (floor at 30°, top at 330°) | units, floor, top, arcs, red line: `configs/aircraft/<id>.json` `instruments.airspeed` (the UNIT is fleet-wide — see below) |
| Altimeter (3-pointer) | `altimeter` | `ALT_Hand100/1000/10000` ← `altitude_ft` | nothing (Kollsman window: the panel's, or CockpitTaps') |
| G-meter | `g-meter` | `GM_Needle` ← `g_load`, `GM_Max`/`GM_Min` ← the followers | red lines = `limits.gMax/gMin`, when `instruments.gMeter` is present |
| Attitude | `attitude` | `AI_Roll` ← `bank_deg`, `AI_Pitch` (slide) ← `pitch_deg` | nothing |
| Vertical speed | `vertical-speed` | `VSI_Needle` ← `vsi_fpm` (0 at 9 o'clock, ±2,000) | nothing |
| Heading (DG) | `heading` | `DG_Card` ← `heading_deg` | nothing |

An aeroplane whose gyro instruments have a model of their own reads that instead (the 172: `tc_needle` in rates,
`ai_bank_deg`, `ai_pitch_deg`, `dg_card_deg`) through a per-mount override; the instrument is the same.

- **Generator**: `tools/own_models/instruments/` (`library.py` the instruments, `faces.py` the ONE set of dial-paint
  primitives, `geometry.py` meshes + GLB writer, `build.py` writes everything, `measure_mounts.py` measures mounts).
  `/usr/bin/python3 tools/own_models/instruments/build.py --review` rebuilds every GLB and face and renders CPU close-ups
  into `renders/shared-instruments/`. Pure NumPy + Pillow, no Blender needed (and importable from a Blender builder).
- **Frame**: face radius 1, +z toward the pilot, +y up, +x the pilot's right; every moving part's origin is the dial
  centre. The runtime scales by the mount's face radius.
- **Table**: `src/FlyingGame.Core/Instruments/InstrumentLibrary.cs` — designs (maps) and mounts (where each aeroplane has
  which instrument, in its GLB's own glTF frame, measured off the GLB).
- **Runtime**: `Bridge/SharedInstruments.cs`, bound and driven by `CockpitLiveParts` (so the game on iOS/Mac and the
  Quest, and the editor shot tools, all get it). It places the library GLB at the mount, and switches off the old
  model's nodes of that instrument (renderers only), so GLBs built before the library show the library instrument too.
- **Tests**: `InstrumentLibraryTests` — map literals, the GLB contract (parts on the dial centre), mounts against every
  livery's GLB (centre within 1.2 mm of the old pivot in the face plane), the ASI scales against the configs, and a scan
  that FAILS when any aircraft builder under `tools/own_models` draws a library instrument of its own.

## Where it is used (2026-11-05)

| Aircraft | Library instruments |
|---|---|
| C172 | ASI, AI, altimeter, TC, DG, VSI (the whole six-pack; its cases and knobs stay the panel's) |
| Extra 300 (3 liveries) | rear ASI, altimeter, TC, g-meter; front ASI, altimeter, g-meter |
| Great Lakes | front ASI, altimeter, TC; rear ASI, altimeter, TC, g-meter (the panel's bezels stay) |
| Pitts S-2B (4 liveries) | rear and front ASI, altimeter, g-meter |
| J-3 Cub | none: PERIOD instruments (mph ASI, 2-pointer altimeter, no gyros) — `InstrumentLibrary.PeriodVariants` |
| SGS 2-33, Grob 103 | none yet (glider ASI/altimeter/variometer): next; listed in `PeriodVariants` until then |

## Which unit the airspeed indicator reads (fleet-wide, Ty 2026-11-10)

Ty had settled five types by hand — *"mph on the J-3, the 2-33, the Great Lakes and the Pitts; knots on the Extra"* —
and, asked what rule the other 27 follow, chose **"by era and category"**:

> *"Vintage, fabric and experimental types read mph; anything certified modern or turbine reads knots."*

So **every** config in `configs/aircraft/` now declares `instruments.airspeed.units` (`"mph"` or `"kt"`) with a
`_unitsSource` saying why, and `FlyingGame.Core.Instruments.AsiUnits` / `AsiDial` is the one place that reads it.
`AsiUnitsTests` fails if a type leaves the key out, if one of the five he settled drifts, or if a declared red line is
not Vne in the declared unit.

| | types |
|---|---|
| **mph** (17) | J-3 (+floats), PA-18 (cub/bush/floats), Stearman, Jenny, Gee Bee, Pawnee, Great Lakes, 2-33, Pitts S-2B, Cassutt, Decathlon, AirCam (+amphib), hang glider, paraglider, **P-51D, DC-3, H-4** |
| **knots** (18) | 172, Archer, SR22, Extra 300, Glasair III, Beaver (+floats), Seminole, 737, F-86, EB29R, Swift S-1, Grob 103, target drone |

Two of those are **the owner's word against the rule, and his word wins — do not "correct" either**: the **Great
Lakes** is mph (TCDS A18EA is in mph CAS anyway) and the **Extra 300** is knots (its POH section 2 is in KIAS, so here
the rule is what was wrong). Three **depart from the shape he was shown** by applying the rule he chose — the
**P-51D, DC-3 and H-4** are vintage pistons, and their real indicators are marked in mph; one line flips any of them.

Everything the pilot reads an airspeed off comes from `AsiUnits`: the flat dials and their readout
(`Bridge/AnalogGauges`), the HUD box (`Bridge/HudOverlay`), the headset's world HUD (`Bridge/VrWorldHud`), the
split-screen HUD (`Bridge/LocalPlayerHud`), and `docs/GLIDE-TABLE.md`'s speeds to fly. What stays in **knots** on
purpose: the flight-test data block (KIAS/KTAS/KEAS/MACH), the companion widget's NTSB panel and its wire protocol
(`kias`), and the ACS tolerances in `Syllabus/Standards` — those are instrumentation, a contract and a published
standard, not the dial in front of him.

A **full face** (`scaleMin`/`scaleMax`/`arcs`/`redline`) is authored only for the types whose cockpit is modelled, off
a TCDS or a POH; `build.py` draws a PNG for exactly those. Everything else gets a scale derived from its spawn speed
and `limits.vneMs` by `AsiDial`, with no invented arcs — a derived scale is still forced to reach Vne, because the
red line has to go somewhere.

## Adding an aircraft or an instrument

- **Every new type declares `instruments.airspeed.units` in its config, with a `_unitsSource`** — `AsiUnitsTests`
  will not let it ship silent. The rest of the `instruments` block (the face) is only for a modelled cockpit.
- A new cockpit gets NO instrument builder: leave a hole and a bezel on the panel, add mounts to `InstrumentLibrary`
  (numbers from `measure_mounts.py`), and, for an ASI or g-meter, the rest of the `instruments` block in its config.
- A period-correct face (J-3, P-51) is a VARIANT in the library, not a private copy in an aircraft script; until it
  exists, the aircraft script is named in `PeriodVariants` with the reason.
- Builders that draw the library's instruments were cut back to the panel's part (bezel / case / knobs): c172_cockpit,
  gl_cockpit, ea_cockpit, vr_phase3b (blank faces kept for the Extra's later passes), rear_panel (blank faces), the
  dial scripts (make_ea_dials, make_gl_dials, make_live_dials). Nobody re-built a GLB for this: each GLB has one owning
  lane, and the runtime covers the old ones.
