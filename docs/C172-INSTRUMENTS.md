# 1970s Cessna 172 instruments — what is modelled, and the rig params

Code: `src/FlyingGame.Core/Instruments/` (engine-free). Tests: `tools/FlightTests/C172InstrumentTests.cs`.
Unity: `CockpitRigState.FillC172` steps the panel for config `c172-like` and writes the params below into
`RigInputs`; the model's rig (Codex, `docs/models/c172-panel-rig.md`, lane/codex-172-cockpit) binds nodes to them.

Every instrument is built from its mechanism and reads the sim's own state (attitude, body rates, specific
force, position, engine rpm, ambient pressure, bus volts). No error is added on top: northerly turning error,
the DG's apparent drift, a tumbled horizon, a slant-range DME and an ADF that leans in a bank all come out of
the mechanism. Numbers marked (E) are engineering estimates and say why in the code comments.

## The units

| Unit | Mechanism | What comes out of it |
|---|---|---|
| Vacuum system | Dry vane pump at 1.3 × crank (E) against the AI and DG jets (compressible orifice flow, rated 2.3 / 1.3 SCFM at 5 inHg, E), relief valve at 5.0 inHg. Pump displacement derived from one checkable fact: regulator takes over at 1,200 rpm at sea level (E) → 78.5 cc/rev. | 1.9 inHg at 700 rpm idle, 3.6 at 1,000, 5.0 from 1,200 up. Less suction at altitude for the same rpm (1.4 at idle at 10,000 ft). 0 with a failed pump. |
| Gyro rotors (AI, DG) | Brass wheel (E 0.43 kg / 27 mm at 21,000 rpm; 0.31 kg / 24 mm at 18,000), impulse-turbine drive ṁ·r·(v_jet − ωr), windage ∝ ω² (derived from rated speed), Coulomb bearings. | From rest at 1,700 rpm: 46 % at 30 s, 74 % at 1 min, 95 % at 2 min, 100 % by 5 min. Pump failure: 58 % after 1 min, 41 % at 2, 21 % at 5, 9 % at 10. |
| Attitude indicator | Vertical gyro, outer gimbal fore-and-aft, inner athwartships. Pendulous-vane erection toward the APPARENT vertical (6 °/min at rated, E), gimbal-bearing friction (3 °/min at rated, E), Earth rate, stops at ±70° pitch / ±110° bank (PHAK) with the stop torque precessing the rotor = tumble. | Accurate in a turn; after a 180° standard-rate turn shows ~2.9° climb and ~0.6° bank the other way (PHAK's "slight climb and a turn in the opposite direction"); nose-up creep under acceleration; tumbles past the stops; with vacuum failed, wanders by more than 10° within 15 minutes of S-turns. |
| Directional gyro | NOT slaved; no latitude nut. Horizontal gyro, outer gimbal on the case vertical (card), inner across it. Earth rate, residual unbalance (4 °/h at rated, E), gimbal friction, leveling jet, stops at ±55° (PHAK). Push-and-turn knob cages and turns the gimbal. | At 45° N reading falls 10.64 °/h from the Earth alone; as built −3.66° per 15 min (FAA acceptance is 3° in 15 min all-in). Friction drift of ~1.5° in a 90° standard-rate turn. Gimbal error from the geometry. Dead after a vacuum failure: the card turns with the aeroplane. |
| Magnetic compass | Pendulous float in fluid (0.3 s lag, E; 18° tilt limit), magnets turn only in the card's plane, damped oscillator (2.0 s period, ζ 0.35 at 45° N, E) with the fluid turning with the bowl; dipole Earth field (dip 63.4° at 45° N); airframe hard-iron field (3 % / 2 %, E) for deviation. | Deviation ≤ 3°. Turning right off north it first swings the other way; through south it leads; accelerating east or west it shows a turn to the north, decelerating to the south; nothing on north/south headings. |
| Turn coordinator (electric) | Rate gyro, gimbal canted 30°, spring + dashpot (0.6 s, ζ 0.7, E), calibrated for a standard-rate coordinated turn at 90 KTAS. DC motor rotor: speed ∝ bus volts, 30 s up / 180 s coast (E). Power flag below 9 V. Ball = apparent gravity in the case's y–z plane, 0.25 s lag, ±12° tube (E). | Needle on the mark at standard rate; moves on the roll-in (cant); ignores the vacuum; on a dead bus the flag drops and the needle decays with the rotor (e⁻¹ at 180 s). |
| NAV1 / NAV2 | Line of sight with Fresnel fade (`NavReception`). VOR ±10° full scale, TO/FROM, OFF within ±3° of abeam and over the station. Localizer course tailored to ±350 ft at the threshold (max ±3°), coverage ±10° to 18 NM / ±35° to 10 NM, back course senses the same side. NAV1 glideslope from an antenna 305 m in and 120 m to the side, ±0.7°, coverage ±8° / 10 NM / 0.45–1.75 θ. Needle lag 0.5 s (E). Off below 10 V. | Threshold crossing height ~17 m (56 ft) from the geometry. NAV2 never shows a glideslope. |
| ADF | Loop bearing measured in the AIRFRAME'S plane (so it leans in a bank), terrain error from `NavReception.NdbBearingErrorDeg`, servo 0.3 s / 90 °/s (E); card rotated by hand. No signal: the needle stays put. | Relative bearing level; 40.9° instead of 45° banked 30°. |
| DME | Own unit, source NAV1 / NAV2 / HOLD. Slant range; ground speed = closure rate (4 s smoothing, E); time to station while closing; 2 s search, 10 s memory (E); UHF line of sight. | 0.99 NM overhead at 6,000 ft; reads ground speed inbound and ~0 abeam. |

The panel's latitude is 45° N (`C172Panel.LatitudeDeg`): this world has none of its own, and 45° N gives the
dip and Earth-rate drift a pilot in the northern US actually sees.

## Failure hooks

`C172Panel.Failures`: `VacuumPump` (partial panel), `Electrical` (whole bus), `TurnCoordinator`, `Nav1`, `Nav2`,
`Adf`, `Dme`. In game: OPTIONS › cockpit rows (vacuum pump, electrical bus, turn coordinator, DME source) when
the 172 is being flown. Each is the mechanical fault; the instruments then do whatever their physics does.

## Knobs (in game)

Tuning and the OBS: the existing RADIOS panel (`NavRadios.Freq/Obs/AdfKhz`). For the 172 the RADIOS heading
cell no longer shows true heading; it shows the DG and the compass, DG knob −5/−1/+1/+5 and "DG = COMPASS",
the suction, and ADF card ±10.

## Rig params (the contract for `c172-panel-rig`)

| param | units / range | drives |
|---|---|---|
| `suction_inhg` | inHg, 0–≈5.4 | suction gauge needle |
| `ai_pitch_deg` | deg, + nose up, ±70 | AI horizon card translation/rotation about the pitch axis |
| `ai_bank_deg` | deg, + right wing down, ±110 | AI horizon card / bank pointer roll (card rolls by −bank relative to the case) |
| `dg_card_deg` | deg 0–360 under the lubber line | DG card (wrap 360) |
| `dg_knob_deg` | deg, cumulative | DG knob (wrap 360) |
| `compass_card_deg` | deg 0–360 under the lubber line | whisky compass card about its vertical (wrap 360) |
| `compass_card_pitch_deg`, `compass_card_roll_deg` | deg, ±18 | float tilt in the bowl (case relative to float) |
| `tc_needle` | "rates", ±2, +1 = right wing on R | TC miniature aeroplane (map 1 → the mark's angle) |
| `tc_flag` | 0/1 | TC power flag |
| `slip_ball` | −1…+1 of the tube's ±12° | inclinometer ball |
| `nav1_cdi`, `nav2_cdi` | −1…+1, + = course right | CDI needles |
| `nav1_gs` | −1…+1, + = fly up | glideslope needle |
| `nav1_tofrom`, `nav2_tofrom` | +1 TO, −1 FROM, 0 OFF | TO/FROM flag (three positions) |
| `nav1_flag`, `nav2_flag`, `nav1_gs_flag` | 0/1 | NAV and GS warning flags |
| `nav1_obs_deg`, `nav2_obs_deg` | deg 0–360 | OBS card and knob |
| `nav1_freq_mhz`, `nav2_freq_mhz`, `adf_freq_khz` | MHz / kHz | radio displays |
| `adf_needle_deg` | deg clockwise from the nose | ADF needle (wrap 360) |
| `adf_card_deg` | deg | ADF card (wrap 360) |
| `dme_nm`, `dme_kt`, `dme_min`, `dme_valid` | NM, kt, min, 0/1 | DME display (dashes when `dme_valid` is 0) |
| `ai_rotor`, `dg_rotor`, `tc_rotor` | 0–1 of rated speed | audio, checks |

Existing params the 172 also uses: `airspeed_kias`/`airspeed_mph`, `altitude_ft`, `vsi_fpm`, `rpm`,
`throttle`, `mixture`, `master`, `magneto`, `oil_*`, `fuel_*`, `com_*`, `xpdr_*`.

## Not modelled (and why)

- Transport rate (the DG and AI drift from moving over a curved Earth, ~2 °/h at 120 kt): this world is flat.
- AI rotor pendulosity (the small turn indication on acceleration) and the AI's miniature-aeroplane knob.
- ADF quadrantal error and night effect; the VOR's scalloping; false glideslope lobes above 1.75 θ.
- Soft-iron (quadrantal) compass deviation; deviation from radios/master switch.
- Sound: the gyro whine has `ai_rotor`/`dg_rotor`/`tc_rotor` to hang on, but no audio here.
