# HUD declutter — Run A

Run A inventories the existing display and adds an opt-in Mac screenshot harness. It does **not** implement Ty's requested rearrangement. Run B should put Analog flight instruments in a neat upper row and engine instruments in a lower row above the trackpads, and address the small text. Glass Overlay is outside this work.

## Inventory (before)

Source audit of `lane/codex-hud-declutter` from island-nav `0cb298e`. All source paths below are relative to `unity/Assets/_Project/Scripts/Bridge/` unless prefixed `src/`. Line references describe the original drawing routines (TouchFlightControls gains two test lines). This is a code-derived inventory, **not measured renders**: the operator still needs to build, run, and inspect the PNGs.

### Reading the sizes and conditions

At 2622×1206 landscape, `S=1206`, margin=21.7, gap=14.5, toolbar height=74.8. Simulated safe insets are **left/right 186 px (62 pt ×3), bottom 63 px (21 pt ×3), top 0**; toolbar bottom=96.5. These are fixed test assumptions, not a claim that macOS reports an iPhone safe area. For portrait the test uses top=186, bottom=63, left/right=0.

`B` below means the shared centre-band width. With standard touch pads: half-size=361.8, pad size=723.6, band approximately x=790..1728, **B≈938**. The existing controls do not apply these safe insets to every pad/button: the harness deliberately preserves that before behavior. In no-pads shots B≈2579. Many text rows call `UiLayout.Label` → `Fit`: the nominal font can shrink to **45%** (minimum 6 px), then clip if it still cannot fit. A box's font size is therefore an upper bound, not the rendered glyph size. NavRadios and most on-dial/HUD labels use `GUI.Label` directly and do **not** Fit. Font metrics depend on the loaded font.

Fresh install: **Hud**, default aircraft **glider-2-33-like**, InTheAir, Solo, no challenge, nav radios shown; bubbles, lift markers, force vectors, flight path, flight-test data, atmosphere map all off. AERO master vectors off, insets empty, autopilot off (individual subordinate vector switches can be true without drawing). C172 is the requested powered comparison, not the shipped aircraft default. Saved settings can differ. Both modes retain status/nav/controls. “Conditional” includes things a normal free-flight player can encounter without selecting a lesson or scored event. Transient panels opened by toolbar buttons are distinguished from the closed flight screen.

**Position flags:** **DIRECT** = explicit location/map/terrain lookup; **FIX** = radio/range/cross-track/landmark information that can reveal or constrain position; **MOTION** = movement/heading/altitude without horizontal coordinates. Ty's rule is never show the player's position. Existing disclosures are recorded here, not silently removed in a before-render run. No new position readout is added by the harness.

### Closed free-flight screen

| Plain-English element | Exact format / graphic | When shown (both modes unless stated) | Nominal landscape font px; approximate box px | Source file:line; position audit |
|---|---|---|---|---|
| Aircraft/build/sideslip status | `{AircraftName}  ·  build {Application.version}  ·  β {BetaDeg:+0.0;-0.0}°` | Always outside lessons; hidden by modal/menu | 29, Fit down to 13; B×43.5 stack row | FlightHud.cs:28,47; no coordinates |
| Multiplayer status | e.g. `FFA · 12 pilots`, `Room K7Q2ZP · 3 pilots` | Net session returns status; absent default Solo | 29 Fit; B×43.5 | FlightHud.cs:53; no own position |
| Event/tow/combat/damage status | First non-null Race.Line → Stol.Line → Dust.Line → Tow.StatusLine → combat in zone → LostLine; damage `BROKE OFF: {part}, {part}` | Event active, towing, entering combat zone, or structural loss; absent ordinary initial free flight | 29 Fit; B×43.5 | FlightHud.cs:55; EventControllers.cs:60,404; TowController.cs:156; StructuralDamage.cs:164; **FIX** possible event progress/ranges |
| Spin data | `SPIN  {SecPerTurn:F1} s/turn   {FtPerTurn:F0} ft/turn   {DescentFtPerSec:F0} ft/s down` | α>16° and established rotation | 29 Fit; B×43.5 | FlightHud.cs:61; **MOTION** |
| NAV 1 receiver | `NAV 1   {Freq[0]:0.00}   {Id or —}`; ← / `CRS {Obs[0]:000}°` / →; CDI dots/bar; `R-{RadialFrom:000}  TO/FROM   {DmeNm:0.0} NM` (DME only when available); LOC substitutes `LOC — no OBS`, adds GS bar | NavRadios.Shown=true default; terrain/sim available; chart closed. Signal flags `NO STATION`, `OFF`, `OVER THE STATION` replace needle readout | strip min(B,1518)×128.8; each of six cells ≈156×129; primary 23, small 19; course row 30 high; no Fit | NavRadios.cs:153,172,214; **FIX** radial, distance and overhead fix |
| NAV 2 receiver | Same as NAV 1 with index 2 and Freq[1]/Obs[1] | Same; default frequencies 115.70 / 113.40 MHz, OBS 360 | Same | NavRadios.cs:204,214; **FIX** second radial enables triangulation |
| ADF receiver | `ADF   {AdfKhz:0} kHz`; two `{Id} {FreqKhz:0}` buttons; needle/card; `RB {rb:000}  MB {bearing:000}` or `RB {rb:000}  terrain error`; `NO BEACON` / `OUT OF RANGE` | Same strip; default 353 kHz | 19/23; ≈156×129; card radius29.9 | NavRadios.cs:294; **FIX** bearings and terrain error |
| Four-course range receiver | `RANGE  {FreqKhz:0} kHz  {Id}`, ON/OFF, `receiver off`; live big `——`, A, N, —, CONE; `crs {near:000}  on/course right/course left`, `out of range`, `CONE OF SILENCE — overhead` | Same strip; RangeOn=false default | 19/23, big41; ≈156×129 | NavRadios.cs:380; **FIX** course sector/overhead fix |
| Heading card | `HEADING`, `{heading:000}°`, sliding tick numbers `{mark/10:00}`, `mag = true here` | Same strip | 19; big39; ≈156×129 | NavRadios.cs:440; **MOTION** |
| Leg clock | `LEG TIME`, `{mm:00}:{ss:00}`, START/STOP, RESET | Same strip; initially stopped at00:00 | 19; big39; ≈156×129; buttons≈67×32 | NavRadios.cs:462; no position by itself |
| Airspeed analog dial | Scale integers, red stall arc, `KNOTS`, numeric `{kt:F0}` under dial | Analog, unless exterior AERO insets replace dials; modal hidden | base fs48; readout65 in240×86; face nominal241 diameter, shrinks to region; scale≤30 and caption≤24 scale with dial | AnalogGauges.cs:200,220,295; **MOTION** |
| Altimeter analog dial | 0..9 scale, two hands, `FEET`, `{ft:N0}` | Analog, same | readout65 in288×86; face as above | AnalogGauges.cs:307; geometric MSL altitude from sim, **MOTION**, not terrain AGL |
| G-meter analog dial | integer scale, live needle, max/min telltales, `{g:F1} g` | Analog, same | readout65 in240×86; face as above | AnalogGauges.cs:320; no position |
| G structural limits | `LIMIT +{LimitPosG:0.#}/{LimitNegG:0.#}  ULT +{UltimatePosG:0.#}/{UltimateNegG:0.#}` | Analog, beneath g readout | 24 in432×48, direct label (no Fit) | AnalogGauges.cs:328 |
| Vertical-speed analog dial | Powered `FPM ×1000`, face 0/1/2, `{fpm:+0;-0}`; glider `KT`, face −5/0/+5/10, `{vzKt:+0.0;-0.0}` | Analog, all aircraft; uses inertial vertical velocity, not total-energy vario | 65 in240×86; scale/caption scale with radius | AnalogGauges.cs:335 |
| HUD pitch/horizon | Conformal ladder −60..60° at5° intervals, labelled major10° bars, horizon | Hud only, camera projection (can leave screen with view direction) | line2.65 px; small25, labels288×57.6 | HudOverlay.cs:103,137,264; **MOTION** |
| HUD bank scale | −60/−45/−30/−20/−10/0/+10/+20/+30/+45/+60 ticks and triangle | Hud only | arc radius434; tick lengths14.5/26.5 | HudOverlay.cs:195 |
| HUD nose/waterline and flight-path marker | W symbol; circle with wings/fin at velocity direction | Hud only, if projected in front of camera | marker radius16.9, wings span≈88; W width94 | HudOverlay.cs:161,175; **MOTION** |
| HUD airspeed / altitude boxes | `{kt:F0}` + `KT`; `{ft:N0}` + `FT` | Hud only | boxes132.7×50.7, centres ±428 from centre; font36 / small25; label allocation288×57.6 | HudOverlay.cs:221; MSL altitude **MOTION** |
| HUD α / β / heading | `α {AlphaDeg:F1}°`, `β {BetaDeg:F1}°`, `{heading:000}°` | Hud only | 25/36; each288×57.6 | HudOverlay.cs:239 |
| Left touchpad | RUD / THR, crosshair, knob, idle/dead/brake bands; `THR {round(throttle*100)}%`, `BRAKES ON  {round(brake*100)}%`, glider `SPOILER {round(spoiler*100)}%`; optional `GAME  ` prefix | Touch, both modes; default visible; pilot input mapping can vary in lessons | pad724×724; labels34 (value27), rows724×51; Fit | TouchFlightControls.cs:808,829,1212,1360 |
| Right touchpad | `AIL / ELE`, knob/crosshair; pilot-out `CHUTE  L / R` | Same | pad724×724; label34,724×51 | TouchFlightControls.cs:830,1212 |
| Pitch trim | Vertical track/ticks/knob; `TRIM` | Touch, both modes | 80×724; label34 in239×51 | TouchFlightControls.cs:229,1309 |
| Rudder / aileron trim | Horizontal bar/ticks/knob; `RUD TRIM`, `AIL TRIM` | Touch, non-strip; both modes | each724×54; label34 in724×51, Fit | TouchFlightControls.cs:265,864,1278 |
| Reset / aircraft buttons | `Reset`, short AircraftName (cuts at ` (`) | Both modes | each≤326×94; height may shrink to58 for aircraft keepout; font34 Fit | TouchFlightControls.cs:236,858 |
| Flaps buttons | `FLAP 0`, `50`, `100`, current choice lit | HasFlaps, no lesson; C172 yes, 2-33 no | thirds of≈326×94,≈107×58..94;34 Fit | TouchFlightControls.cs:900 |
| Gear button | `GEAR UP`, `GEAR DOWN`, `GEAR ↓ …`, `GEAR ↑ …` | Retractable types only |≈326×58..94;34 Fit | TouchFlightControls.cs:870 |
| Smoke button | `SMOKE` / `SMOKE ON` | SmokeAvailable, powered exhaust or glider wingtip canisters | double-width≈675×58..94;34 Fit | TouchFlightControls.cs:878 |
| Tow / release | `TOW` / `RELEASE` | Glider on ground / towing |≈326×58..94;34 Fit | TouchFlightControls.cs:922 |
| Bail/BRS | `BAIL OUT` or `DEPLOY BRS` | BailAvailable (cockpit only); BRS-equipped changes label |≈326 or675×58..94;34 Fit | TouchFlightControls.cs:835 |
| Ejection | `HOLD: EJECT`, `EJECT`, hold progress fill | Ejection seat / EjectAvailable |≈326×58..94;34 Fit | TouchFlightControls.cs:845 |
| Fire control | `FIRE` / `FIRING` | Armed, available combat state, touch | 289×157 at x24, y109;34 Fit | TouchFlightControls.cs:276,836; CombatHud governs sight |
| Touch look buttons | `◀ LOOK`, `AHEAD`, `LOOK ▶` | Cockpit, LookAvailable | each224×87, gaps19;34 Fit | TouchFlightControls.cs:273,886 |
| Cockpit look pad | ▲/◄/●/►/▼, `RECENTRE`, `SEAT: {label}` | Cockpit; recentre only device-look; seat only multiple authored seats | cell78.5; cross235.5²; extra rows235.5×74.8;31 Fit | ViewPanel.cs:159; separate from three touch look buttons |
| Clip/record toast | `_toast` (save/record/error text) | Until toast expiry |31 Fit in min(B,844)×58.9 | ViewPanel.cs:89 |
| Radio talk control | `HOLD TO TALK\n{Frequency}` / `TRANSMITTING\n{Frequency}` | RadioAvailable (multiplayer), absent default Solo |31 in338×104.7 | ViewPanel.cs:133 |
| Radio status / mic problem | `RADIO: {Receiving}` or MicProblem | Receiving/error; normally absent Solo |31 Fit; B×43.4 | ViewPanel.cs:140 |
| Rooftop race progress | `ROOFTOP RACE   ring {Next+1}/5   {elapsed:F1} s   ·   {LastEvent}` / LastEvent | Can trigger by flying through rooftop course in free flight; running or recent event |31 Fit, B×46.5 | PlaygroundBuilder.cs:570; **FIX** specific ring progress |
| Combat target | `TARGET  {TargetName}   {off:F0}° off the nose   {clock} o'clock   {range:F0} m` (≥1000 uses `{range/1000:F1} km`), ring+dot | Dogfight or InZone; may enter combat zone in free flight |34 Fit B×51; ring724 diameter, dot54 | Combat/CombatHud.cs:43,81; **FIX** relative target distance/bearing, not own coordinates |
| Gunsight / ballistic range | Pipper + `{pr:F0} m`, flight-path circle/wings | In combat state, aircraft has guns | pipper121 diameter; range34 in241×47.6; FP radius21.7 | Combat/CombatHud.cs:93; **FIX** target range |
| Pilot egress status | `BAIL OUT  canopy in {F1} s`, `CANOPY GONE  jump in {F1} s`, `EJECT  {F0} m/s`, `FREE FALL  {height:F0} m AGL  {speed:F0} m/s  chute in {F1} s`, `LINE STRETCH  {F0} m/s`, `INFLATING {percent}%  {F0} m/s`, `CANOPY  {height:F0} m AGL  sink {F1} m/s  steer: right pad`, `TUMBLING  {hits} hits  {speed:F0} m/s`, landing outcome/phrase | After leaving cockpit, not replay |37 in2202×70; no Fit | Pilot/PilotEgress.cs:354,386; **DIRECT: free-fall/canopy terrain AGL**, though no coordinates |
| Cloud / tunnel obscuration | Fullscreen near-white haze, no text | Enter cloud / waterfall blindness; defaults do not disable environment |2622×1206; no font | Cumulus.cs:205; TunnelHazards.cs:234 |

Analog exterior geometry detail: regT=max(view top, toolbarBottom+4.2×48)=298.1. Region bottom depends on centre button keepout; `maxR=min(B×.11, (colH−2×91.2−57.6)/4.2)`. At an unshrunk 94 px four-row button cluster, regB≈721, r≈43.5 (diameter≈87), scale≈11 px and caption≈9 px! If keepout reduces button heights to58, r≈78 (diameter≈156), scale≈19 and caption≈16. These are **ranges**, not a claimed captured measurement. Readouts remain65 px even when faces shrink. Fixed columns: ASI above VSI left; ALT above G right. Cockpit instead paints an opaque panel across the camera's bottom: panelH=min(.30×viewH,.36×s)=362; ASI/ALT/VSI/G at fifth-width intervals, r≤120.6, G/VSI radius102.5. Run A preserves all of this.

### Optional overlays and panels (off/closed for a new free-flight player)

| Element | What it shows / exact format | Condition | Size at landscape; source |
|---|---|---|---|
| Analog flight-test block | 7 rows ×4 cells, each `{key,-5}{value,9}`: KIAS/KTAS/KEAS `F1`, MACH `F3`; ALPHA `F1°`, BETA signed1dp°, q `F0 Pa` / `F1 psf`; Hp/Hd `F0 ft`, OAT `F1°C`, Ps `F1hPa`; SIGMA `F3`, **AGL `F0 ft`**, GS `F0 kt`, NZ `F2 g`; GAMMA/PITCH/ROLL signed1dp°, ROC signed integer fpm; HDG `000°`, P/Q/R signed integer°/s; ELEV/AIL/RUD signed1dp°, FLAP `F0%` | FlightTestData=true AND Analog; default false | font23; row27.6; box width measured from first row+28.8, height≈212; between columns / cockpit above panel. AnalogGauges.cs:379–426. **DIRECT: AGL uses WorldTerrain.GroundHeightAt(player X,Y)**; GS aids dead reckoning; no latitude/longitude printed |
| Regional atmosphere map + data | Region boundaries, calm zone, wind/lift/sink map; text `AIR [J]  {RegionId}  calm {P0}`; `wind {000}° {F0} kt  w {signed1dp} m/s  gust σ {F1}/{F1} m/s  L {F0} m`; OAT/ISA/QNH/DA, slope/lee/wave/conv/speed-up, terrain roughness/lake/rotor | ShowAtmosphereMap=true and regional field; default false |22 font; box1573×241 at(52,145); map world projection. AtmosphereOverlay.cs:38,190,200. **DIRECT/FIX: player-local region and terrain sampling, geographical overlay** |
| Force vectors | Lift/drag/thrust/weight etc drawn on airframe, no GUI text in this component | ShowForceVectors=true; default false | World-projected GL geometry; no fixed pixel box. ForceVectorOverlay.cs:35 |
| Flight path / lift markers / bubbles | Projected path cone, lift/sink symbols and air streaks | respective SessionSettings flags; defaults false | World geometry, no fixed font/box; SessionSettings.cs:42–63. **MOTION**, terrain/geographic lift patterns can constrain location |
| AERO vectors and insets | CL–α, lift/drag, power-required, slip ball, AoA, weight/CG, force-balance/trend and other selected teaching insets; labelled vectors/axes/moments | Host.Show master and Insets; empty/off default; remembered separately under aero.parity | Adaptive area between dials/text/pads; plate300×176×u×k (k=.85/.75/.65), u=clamp(.58×dpi/163,1.2,2.2), so ≈234..561×137..329; titles15×adjusted text scale; dropped insets explicitly counted. Widget/GameAeroHost.cs:38,104,129,185,190; Widget/WidgetInsets.cs:17,82,120; Widget/WidgetControlsDisplay.cs:491. **FIX risk** optional groundTrack/flightPath/aimPoint/earthGrid visualize movement/ground reference; inspect before enabling in Run B |
| AERO no-room line | `{n} inset(s) hidden: no room beside the airplane (fewer insets, a wider window or portrait)` | Insets requested and unable to fit |31 Fit; B×43.4; Widget/GameAeroPanel.cs:85 |
| Autopilot FMA | `AP · ALT {0} · HDG {000}`; pitch variants `PIT {signed1dp}°`, `VS {signed0}`, `FLC {0} → {0}`, `ALT* {0}`; roll ROL / `BANK {0}° L/R`; prefixes CWS, suffix YD/A/T; failure `CAN'T HOLD — FULL POWER` / ELEVATOR/TRIM LIMIT / STALL / OSCILLATING | AP, AT or YD selected; default null |31 Fit B×49.6; Widget/GameAeroPanel.cs:86; Widget/WidgetAutopilot.cs:249. **MOTION** |
| Altitude lock annunciation | `ALT LOCK {AltLockFt:0} AGL` | AltLock and applies; off normal free flight |31 Fit B×46.5; Widget/GameAeroPanel.cs:87; Widget/AeroHost.cs:192. **DIRECT terrain AGL reference** |
| Four-forces coaching | `LESSON · {Instruction}`, Live string, latest result | FourForcesLesson active via AERO; default off |head33 or31 Fit, B×46.5 /43.4; Widget/GameAeroPanel.cs:89 |
| Open AERO / OPTIONS / view / chart panels | Selection/configuration UI, view buttons, static charts | Explicit toolbar action; closed initial flight | Modal/adaptive panels; not permanent clutter. ViewPanel.cs:218; Widget/GameAeroPanel.cs:104; OptionsPanel.cs; ChartPanel.cs. **Chart is geographic but explicitly has NO own-ship marker**. AERO panel's `{AltLockFt:0} FT AGL` is terrain-lock configuration, not live coordinates (GameAeroPanel.cs:191). No Glass Overlay inspected or changed |
| Replay tray | Time, seek track, −10s/PLAY/PAUSE/+10s/SPEED/VIEW/CLIP/REC/EXIT REPLAY | Explicit replay, outside live free-flight scope | ViewPanel.cs:260; replaces controls/tray |

### Verified exclusions: selected challenges / events / lessons

These are not present for `ChallengeId=null` at a normal start. They explain additional conditional rows in the common text stack and must not be mistaken for always-on clutter.

| Element | Format / condition | Size / source / position concern |
|---|---|---|
| Graded challenge callout/timer/tolerance/results | callout; `phase {index+1}/{count}   {time:F0}s   score {score:F0}%`; `●/○ {signal}`; `PASS`/`TRY AGAIN   {score:F0}%`, id/retry line |29/41 Fit, B×43.5 or60.9. ChallengeHud.cs:35–56. Desktop-only `Press C to start a graded challenge · N for next` is absent on iPhone; harness disables this otherwise empty component because Input.touchSupported stays false on Mac |
| Timed race/STOL/dust/tow status | `AIR RACE  next {Next+1}/{count}   {TotalSec:F1} s   {LastEvent}` / `AIR RACE  {LastEvent}   (penalties {PenaltySec:F0} s)`; `STOL  {Message}   ·  L {landing distances:F0 / …}   T {takeoff distances:F0 / …}` (feet); dust `CROP DUST  {passes} passes left   {elapsed:F0} s + {penalty:F0} s   {event}`, finish/wire strike; tow StatusLine | FlightHud common29 Fit/B×43.5, EventControllers.cs:60,228,404 / TowController.cs:156: `TOW  {Status}   rope {Tension:F0} N   RELEASE when ready` / `TOW  released ({SeverReason})  tug {Status}`. **FIX** progress and known-course distance may reveal location |
| Dust steering LEDs, XTK, radio height |21 LEDs; `SWATH {n}` / `FIELD DONE`; `XTK {abs:F1} m R/L`; `RA {aglFt:F0} ft` / `RA ----` | B×90.5, LEDs≤36.2, GUI.skin-derived fonts (not explicitly sized). EventControllers.cs:370–400. **DIRECT/FIX: terrain AGL and known-swath cross-track** |
| Practice briefing / lesson pages / countdown / score orb / instruction / debrief | page title/index, text, illustrations; countdown; `{points:F0} pts`, `{grade}   {rate:+0;-0;0} per second`; user-axis/task instructions, STALLED/FLYING; result graphs and captured shots | Practice/PracticeHud.cs:24,42,90,111,145,260,350; adaptive cards/viewport, fonts based on S and scaling. **DIRECT/FIX:** debrief track over runway, side path and height history (392,532); does not draw during free flight |
| Lesson force/airflow/wheel overlays and 2D practice view | world-projected explanatory vectors, wheel forces, side view/aim references | Practice/StallVectorOverlay.cs, WheelForceOverlay.cs, Side2DView.cs; active lesson only, not free flight; projected sizes vary; **FIX** course/ground references |
| Lesson control strips | `ELEVATOR`, `push = nose down`, THROTTLE/BRAKES/SPOILERS and percentages, `your thumb here` while briefing | TouchFlightControls.cs:801,821; only StripMode lesson mapping; not default touchpad layout |

Portrait differences: `ScreenLayout.cs:21–67` reserves a bottom tray ~1032 px at1206×2622; the camera renders the upper ~1590 px. Pads shrink to482×482 in tray corners, font still34, controls arranged in rows above them; trim labels move above bars. The band spans almost the whole width (~1163), not between pads. Analog camera short edge remains1206; available column height is greater, and dials can remain near their241 px nominal diameter. HUD geometry uses camera viewport dimensions and short edge; conformal labels convert back to screen coordinates. Toolbar moves below the simulated186 px top inset. Safe-area overrides alone do not rewrite the old tray/pad geometry. No-pads portrait intentionally retains the existing camera viewport/tray split (ScreenLayout is unmodified); it hides the controls and removes UiLayout's band/bottom reservation, not the viewport split.

## Engine values available for Run B

| Value | Actual producer / computation | Qualification |
|---|---|---|
| Throttle | Aircraft.Throttle01, private clamped control fraction `_throttle01`; TouchFlightControls maps left pad to this control | Real sim input0..1, percent=100×fraction; Aircraft.cs:310 |
| RPM | Aircraft.EngineRpm, Aircraft.cs:356: no propulsion or lost nose→0. Constant-speed: below .3 throttle blends idle to min(governed,1800); above holds governor, .9..1 smoothsteps to max. Fixed pitch: idle+(max−idle)×thr^.8×(.82+.18×clamp(body speed/spawn IAS,0,1)) | Sim's existing speed law, not a separately integrated crankshaft. FlightSimDriver.cs:86 forwards it. CockpitRigState.cs:123 forces0 when EngineStopped |
| Fuel flow | Aircraft.FuelFlowKgPerSec, Aircraft.cs:332: MaxPowerW×clamp(throttle,0,1)×BsfcKgPerWs; zero without propulsion/tank capacity/positive BSFC | kg/s; jet MaxPowerW represents thrust, coefficient acts as TSFC. Not an independently measured fuel sensor |
| Fuel quantity | Aircraft.Load.FuelKg / FuelCapacityKg and per-slot Kg; AircraftLoad.cs:139,144; Aircraft.cs:674 burns flow×dt in authored order | Available actual quantity! CockpitRigState.cs:233 still sets FuelCentreFrac=1 and has a stale “no fuel quantity” comment; do not copy that constant for a new instrument |
| Model rig RPM | `RigInputs.Rpm` in Core/ModelRig.cs:177; CockpitRigState.Fill:123 copies engine RPM | ModelRig is the channel contract, not a separate engine simulation |
| Manifold pressure | CockpitRigState.FillEngineGauges:212: ambient=Atmosphere.PressureAtAltitude(altM)/3386.389 inHg; running→ambient×(.33+.62×throttle), stopped→ambient | **Provided, but explicitly a synthetic cockpit proxy**. No standalone thermodynamic manifold sim. Uses altitude-only ambient, not regional PressureAt(position) |
| Oil temperature | CockpitRigState.cs:215–222: running target150+60×thr°F, stopped100°F; exponential lag τ150s | Presentation estimate, state held in CockpitRigState; not authoritative thermal physics |
| CHT | Same: target260+160×thr°F, stopped200°F; exponential lag τ45s | Presentation estimate |
| Oil pressure | CockpitRigState.cs:226–230: running clamp(20+65×min(1,rpm/(.85×maxRpm))−.08×(oilTemp−180),0,115)psi; stopped0; fallback maxRPM2700 | Presentation estimate |
| Prop/mixture/fuel/magneto/master channels | CockpitRigState.cs:134: prop1, mixture1 if powered/not stopped, fuel selector1, magneto3 when running else0, master1 | Fixed cockpit poses, not independently simulated control states. Do not label as measured engine values |

Glider has no engine: avoid fabricating RPM/MP/oil/CHT. A future row should distinguish the physical sim RPM/fuel/throttle from the cockpit's explicitly estimated oil/CHT/MP. No new terrain AGL, GPS, coordinates, distance-from-origin, or own-ship marker is authorized.

## Render harness usage

Operator runs the build; Run A did not run Unity, xcodebuild, deployment or the full tests. This is the build portion of `tools/deploy/mac.sh`, with output only under `/private/tmp`. Do not run that script itself (it installs to Applications). From this worktree, one Unity build at a time:

```zsh
REPO="$PWD"
UNITY=/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity
mkdir -p /private/tmp/aero-hudshots-build
FLYINGGAME_MAC_OUT=/private/tmp/aero-hudshots-build "$UNITY" \
  -batchmode -quit -projectPath "$REPO/unity" -buildTarget OSXUniversal \
  -executeMethod FlyingGame.EditorTools.BuildScript.BuildMac \
  -logFile /private/tmp/aero-hudshots-build.log
rg 'macOS build SUCCEEDED|error CS' /private/tmp/aero-hudshots-build.log
```

Launch the binary directly (GUI session required; do not use `-nographics` or `-batchmode` for screenshots):

```zsh
mkdir -p /private/tmp/aero-hudshots-before
AERO_SELFTEST=hudshots AERO_SELFTEST_QUIT=1 AERO_HUDSHOTS_PORTRAIT=1 \
  AERO_SHOT_DIR=/private/tmp/aero-hudshots-before \
  '/private/tmp/aero-hudshots-build/Aero Playground.app/Contents/MacOS/Aero Playground' \
  -logFile /private/tmp/aero-hudshots-before/player.log
```

Omit `AERO_HUDSHOTS_PORTRAIT=1` for landscape only. `AERO_SHOT_DIR` must be absolute; omitted/empty uses Application.persistentDataPath. Do not combine with AERO_PARITY or another self-test. Use a clean player profile for strict new-player livery/loading comparison (the harness preserves saved aircraft loads/liveries and external input/device settings).

Expected files per orientation (`landscape`, optional `portrait`), eight total:

- `hud-analog-touch-<orient>.png` / `hud-analog-gamepad-<orient>.png`
- `hud-hud-touch-<orient>.png` / `hud-hud-gamepad-<orient>.png`
- `hud-analog-touch-<orient>-glider.png` / `hud-analog-gamepad-<orient>-glider.png`
- `hud-analog-touch-<orient>-cockpit.png` / `hud-analog-gamepad-<orient>-cockpit.png`

C172 shots use InTheAir, free flight, Solo, RelativeWind; glider uses2-33; cockpit comparison uses C172. Each starts fresh through Menu.Fly(), waits4 realtime seconds, then captures the complete end-of-frame including IMGUI, encodes/writes synchronously. Verify `[SelfTest] hudshots wrote <absolute path>` per PNG, then `[SelfTest] DONE hudshots`; quit only for AERO_SELFTEST_QUIT=1. Requested and actual Screen dimensions are logged for each pass; a macOS window/display may cap the requested native pixel size. Inspect PNG dimensions and actual-screen log before accepting a “2622×1206” comparison.

Simulation: both layouts use UiLayout's test-only iPhone safe area (186/63 px insets) and ForceTouchLayoutForTest (DeskMode false). Disconnect physical gamepads for capture: `touch` uses normal disconnected input; `gamepad` sets GamepadInput.SimulateConnectedForTest, making Now return Connected=true with every axis/button neutral, before Menu.Fly(). This bypasses physical pad input and works with or without ENABLE_INPUT_SYSTEM. The real TouchSticksHidden path handles visibility and touch exclusion; GamepadShaping and hide logic are unchanged. Run A's HideTouchControlsForTest and its artificial band/bottom overrides are removed. Only the touch-platform and safe-area overrides remain necessary on the Mac. The default ChallengeHud is disabled only during capture to suppress its Mac-only keyboard hint. Optional AERO visibility is cleared temporarily so saved insets do not replace the analog dials. No chart is opened and all direct position/debug overlays are off.

Gamepad inventory (current branch, before the separate full-screen/centred-airplane change): touch pads or lesson strips, pitch-trim slider and trim bars disappear and do not accept touches. Buttons remain. Landscape reveals the existing camera scene behind the removed controls; portrait retains the opaque tray, leaving empty tray space. Nothing automatically expands into those spaces: PublishBand does not branch on TouchSticksHidden. In normal landscape BandMin remains leftCenter.x + half + gap, BandMax remains min(trimRect.x, rightCenter.x − half) − gap, BottomLimit remains h − max(bailRect.yMax, acftRect.yMax, towRect.yMax) − gap; gap = min(w,h) × .02. Lesson strips retain their own band: BandMin = (throttle/brake strip present ? thrStrip.xMax : 0) + gap, BandMax = trimRect.x − gap, with the same button-based BottomLimit. Portrait keeps BandMin=0, BandMax=w and BottomLimit=h−TrayHeightPx. LeftBottomLimit resets to99999 on mobile. Thus hidden controls still reserve the existing dial/text bounds; no full-width override is applied for screenshots.

Restoration uses try/finally: all mutable public SessionSettings fields, nav visibility, AERO visibility/insets, test flags, challenge component state, window resolution/mode, run-in-background and every PlayerPrefs key written by Fly/SavePrefs (including missing keys). No settings reset or migration. Abrupt process termination during capture cannot execute finally; let DONE/QUIT complete. Normal launches never enter this coroutine and all override flags default false.

## Run A validation and changed files

Only ClipSelfTest.cs, UiLayout.cs, TouchFlightControls.cs and this document are owned by this run. No assets added under Assets, so no new meta GUID is required. AGENT-LOG.md had a pre-existing modification and was left untouched. No commit made. Unity runtime screenshots and actual phone appearance remain operator verification; no visual gate is approved.

Headless validation: temporary netstandard2.1/C#9 Bridge compile using Unity 6000.3 managed assemblies and this worktree’s Core/Sim sources passed with 0 errors. Two existing WidgetVectors unused-local warnings; NuGet vulnerability audit could not reach nuget.org (NU1900). Compile harness lives only under `/private/tmp/hudshots-check`; this is not a Unity player build. `git diff --check` passed.

## Run B1 harness update

Only ClipSelfTest.cs, GamepadInput.cs (inert test flag), UiLayout.cs / TouchFlightControls.cs (Run A hook removal), and this document changed. Normal-player layout behavior is unchanged. The simulation flag is restored alongside the other test flags in finally. No Glass Overlay or position display changes. Changes remain uncommitted; Unity, xcodebuild and deploy scripts were not run. Screenshots remain operator verification.

B1 validation: reused `/private/tmp/hudshots-check` for the headless netstandard2.1/C#9 Bridge compile: 0 errors, two existing WidgetVectors unused-local warnings and two NU1900 audit-network warnings. This compile does not define ENABLE_INPUT_SYSTEM; the test override is outside that conditional. `git diff --check` passed. No runtime renders were captured.

## Proposal (run B2)

The proposal removes the permanent aircraft/build/β row and collapses six navigation cards into one RADIOS button. Analog has four equal flight columns directly below the toolbar and a second, compact engine readout row. A second row of round engine dials would need another ~300 px and would cover the aircraft or touch controls; rectangular readouts preserve the useful data in 92 px. No attitude dial is added: the airplane and conformal HUD already provide attitude. The order is airspeed, altitude, g, vertical speed, preserving the relative classic-pack order of the available instruments.

### Reading contract and geometry

At the reference 3× phone resolution, primary flight text has a floor of **11 pt / 33 px**, dial scales and captions **10 pt / 30 px**. Exterior dial diameter is **70 pt / 210 px** (209.844 before rounding); readouts 21.7 pt / 65 px. Shared `UiLayout.Fit` now floors at 33 px rather than 45% of the requested font; `Label` abbreviates excess trailing text with an ellipsis. Native pixel sizes scale with the screen short edge, with absolute fallback floors of 22/20 px for low-resolution desktop windows. These are font sizes, not a promise of measured glyph height. Toolbar geometry is unchanged. Primary HUD small labels rise 25→33 px; conformal lines, ladder, bank and flight-path symbols remain.

| Orientation / controls | Strip width and columns | Flight row bounds (px) | Engine row (px) | Airplane keepout |
|---|---|---|---|---|
| Landscape touch, 2622×1206 | Band ~938 wide, 234.5 pitch | y≈111..321; four 210 px dials; readouts end≈410 | y≈412..504, four 234.5×92 cells | Between pads, above their centre and bottom button cluster |
| Landscape gamepad | Centred in safe width; capped1013, pitch253 | Same y and diameter | Same y, four253×92 cells | All ordinary instruments above y504; screen centre y603 clear |
| Portrait touch, 1206×2622 | Centred1013 wide, pitch253 | y≈297..507; same diameter, readouts end≈596 | y≈598..690 | Above tray/pads; upper camera centre≈795 clear |
| Portrait gamepad | Same1013 width and dimensions | Same as portrait touch | Same as portrait touch | No dependency on forthcoming camera/full-screen lane |

Numbers are code-derived nominal geometry, not measured after renders. Safe-area override and pixel dimensions must match before/after. Engine captions: RPM (jet RPM %), THROTTLE %, FUEL KG/H, FUEL KG. Throttle is labelled as the control percentage, not measured shaft power. RPM follows the sim's existing speed law; jets normalise by MaxRpm. Fuel comes from actual burn rate and `Load.FuelKg`. No oil/CHT/manifold proxies or fixed mixture/prop poses are promoted to measured instruments. Gliders have only the flight row. Cockpit retains its bottom panel and gets the same larger scale text, without an added floating engine strip.

The shared text stack starts below the exterior instrument strip, so event lines and the radio button cannot allocate space on the dials. Explicitly opened radios temporarily take a two-column panel; closing restores the normal display. Existing nav visibility preferences are retained; the expanded state is transient and defaults closed even when the saved visibility preference is true. No new saved keys, reset or migration. Aircraft identification remains on the existing aircraft selection button. Structural thresholds remain encoded in the g dial's coloured marks; no flight behaviour changes.

### Inventory disposition (every Run A item)

Before sizes below are the original inventory, not measurements. KEEP retains conditional visibility and existing specialised geometry; shared fitted labels inherit the new floor.

| Inventory item | Decision / destination | Before → after |
|---|---|---|
| Aircraft/build/sideslip status | MOVE / MERGE | 29, Fit down to 13; B×43.5 stack row → 0 px / no permanent row. Aircraft remains on aircraft button; build in OPTIONS › Display; β in HUD / AERO. |
| Multiplayer status | KEEP | 29 Fit; B×43.5 → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| Event/tow/combat/damage status | KEEP | 29 Fit; B×43.5 → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| Spin data | KEEP | 29 Fit; B×43.5 → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| NAV 1 receiver | MOVE | strip min(B,1518)×128.8; each of six cells ≈156×129; primary 23, small 19; course row 30 high; no Fit → RADIOS · OPEN, collapsed each launch; panel 2 columns ×3 rows, 33 px minimum. Heading no longer duplicates HUD during ordinary flight. |
| NAV 2 receiver | MOVE | Same → RADIOS · OPEN, collapsed each launch; panel 2 columns ×3 rows, 33 px minimum. Heading no longer duplicates HUD during ordinary flight. |
| ADF receiver | MOVE | 19/23; ≈156×129; card radius29.9 → RADIOS · OPEN, collapsed each launch; panel 2 columns ×3 rows, 33 px minimum. Heading no longer duplicates HUD during ordinary flight. |
| Four-course range receiver | MOVE | 19/23, big41; ≈156×129 → RADIOS · OPEN, collapsed each launch; panel 2 columns ×3 rows, 33 px minimum. Heading no longer duplicates HUD during ordinary flight. |
| Heading card | MOVE | 19; big39; ≈156×129 → RADIOS · OPEN, collapsed each launch; panel 2 columns ×3 rows, 33 px minimum. Heading no longer duplicates HUD during ordinary flight. |
| Leg clock | MOVE | 19; big39; ≈156×129; buttons≈67×32 → RADIOS · OPEN, collapsed each launch; panel 2 columns ×3 rows, 33 px minimum. Heading no longer duplicates HUD during ordinary flight. |
| Airspeed analog dial | ENLARGE | base fs48; readout65 in240×86; face nominal241 diameter, shrinks to region; scale≤30 and caption≤24 scale with dial → Exterior diameter 210 px; scales/captions 30 px, readout 65 px. Flight row: airspeed, altitude, g, vertical speed. |
| Altimeter analog dial | ENLARGE | readout65 in288×86; face as above → Exterior diameter 210 px; scales/captions 30 px, readout 65 px. Flight row: airspeed, altitude, g, vertical speed. |
| G-meter analog dial | ENLARGE | readout65 in240×86; face as above → Exterior diameter 210 px; scales/captions 30 px, readout 65 px. Flight row: airspeed, altitude, g, vertical speed. |
| G structural limits | MERGE | 24 in432×48, direct label (no Fit) → 0 px text; existing yellow limit / red ultimate dial marks retained (same numeric thresholds). |
| Vertical-speed analog dial | ENLARGE | 65 in240×86; scale/caption scale with radius → Exterior diameter 210 px; scales/captions 30 px, readout 65 px. Flight row: airspeed, altitude, g, vertical speed. |
| HUD pitch/horizon | ENLARGE | line2.65 px; small25, labels288×57.6 → Small text 25→33 px; main readouts 36 px; conformal geometry retained. |
| HUD bank scale | KEEP | arc radius434; tick lengths14.5/26.5 → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| HUD nose/waterline and flight-path marker | KEEP | marker radius16.9, wings span≈88; W width94 → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| HUD airspeed / altitude boxes | ENLARGE | boxes132.7×50.7, centres ±428 from centre; font36 / small25; label allocation288×57.6 → Small text 25→33 px; main readouts 36 px; conformal geometry retained. |
| HUD α / β / heading | ENLARGE | 25/36; each288×57.6 → Small text 25→33 px; main readouts 36 px; conformal geometry retained. |
| Left touchpad | ENLARGE | pad724×724; labels34 (value27), rows724×51; Fit → Value text 27→33 px; pad/axis labels 34 px. No input or visibility changes. |
| Right touchpad | KEEP | pad724×724; label34,724×51 → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| Pitch trim | KEEP | 80×724; label34 in239×51 → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| Rudder / aileron trim | KEEP | each724×54; label34 in724×51, Fit → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| Reset / aircraft buttons | KEEP | each≤326×94; height may shrink to58 for aircraft keepout; font34 Fit → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| Flaps buttons | KEEP | thirds of≈326×94,≈107×58..94;34 Fit → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| Gear button | KEEP | ≈326×58..94;34 Fit → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| Smoke button | KEEP | double-width≈675×58..94;34 Fit → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| Tow / release | KEEP | ≈326×58..94;34 Fit → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| Bail/BRS | KEEP | ≈326 or675×58..94;34 Fit → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| Ejection | KEEP | ≈326×58..94;34 Fit → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| Fire control | KEEP | 289×157 at x24, y109;34 Fit → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| Touch look buttons | KEEP | each224×87, gaps19;34 Fit → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| Cockpit look pad | KEEP | cell78.5; cross235.5²; extra rows235.5×74.8;31 Fit → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| Clip/record toast | KEEP | 31 Fit in min(B,844)×58.9 → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| Radio talk control | KEEP | 31 in338×104.7 → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| Radio status / mic problem | KEEP | 31 Fit; B×43.4 → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| Rooftop race progress | KEEP | 31 Fit, B×46.5 → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| Combat target | KEEP | 34 Fit B×51; ring724 diameter, dot54 → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| Gunsight / ballistic range | KEEP | pipper121 diameter; range34 in241×47.6; FP radius21.7 → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| Pilot egress status | KEEP | 37 in2202×70; no Fit → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| Cloud / tunnel obscuration | KEEP | 2622×1206; no font → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| Analog flight-test block | ENLARGE / REMOVE | See original inventory → 23→33 px; 14 rows ×2 cells, replaces exterior dials while selected. AGL removed, RPM in its cell; OPTIONS selects flight-test data. |
| Regional atmosphere map + data | KEEP | See original inventory → Existing optional specialised renderer, same sizes as inventory; outside ordinary flight proposal (see limits below). |
| Force vectors | KEEP | See original inventory → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| Flight path / lift markers / bubbles | KEEP | See original inventory → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| AERO vectors and insets | KEEP | See original inventory → Existing optional specialised renderer, same sizes as inventory; outside ordinary flight proposal (see limits below). |
| AERO no-room line | KEEP | See original inventory → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| Autopilot FMA | KEEP | See original inventory → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| Altitude lock annunciation | KEEP | See original inventory → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| Four-forces coaching | KEEP | See original inventory → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| Open AERO / OPTIONS / view / chart panels | KEEP | See original inventory → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| Replay tray | KEEP | See original inventory → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| Graded challenge callout/timer/tolerance/results | KEEP | See original inventory → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| Timed race/STOL/dust/tow status | KEEP | See original inventory → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| Dust steering LEDs, XTK, radio height | KEEP | See original inventory → Existing optional specialised renderer, same sizes as inventory; outside ordinary flight proposal (see limits below). |
| Practice briefing / lesson pages / countdown / score orb / instruction / debrief | KEEP | See original inventory → Existing optional specialised renderer, same sizes as inventory; outside ordinary flight proposal (see limits below). |
| Lesson force/airflow/wheel overlays and 2D practice view | KEEP | See original inventory → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |
| Lesson control strips | KEEP | See original inventory → Existing geometry; shared fitted text now ≥33 px, trailing detail abbreviated. |

### Position audit

No coordinates, GPS, own-ship map symbol, terrain-database AGL or position-derived instrument is added. The optional flight-test block loses its terrain query and AGL readout altogether; RPM replaces that cell. Geometric MSL altitude and inertial vertical speed remain. Existing explicit navigation aids retain their original station bearings/radials/DME, available only after opening radios; no new fix source is introduced. Existing ALT LOCK AGL configuration/annunciation, crop-dust RA/XTK, pilot-egress AGL, atmosphere regional map and practice debrief track remain flagged from Run A. They are existing specialised modes, not new normal-flight position displays. Chart still has no own-ship. Glass Overlay is untouched.

### Validation and limits

`AERO_SELFTEST=hudshots` and all filenames/aircraft/layout/orientation cases are unchanged. Operator capture uses the identical command with `AERO_SHOT_DIR=/private/tmp/aero-hudshots-after`. Neither Unity nor xcodebuild nor deployment was run here; there are no after images from this run and no visual approval is claimed.

This proposal's minimum applies to ordinary flight instruments, shared fitted labels, touch values and the radios. Specialised direct-drawing AERO insets, atmosphere diagnostics, practice pages and crop-dust LEDs retain their audited fonts; they do **not** yet satisfy a global 33 px minimum. Multi-event stack overflow and long-text ellipsis still require visual review; ellipsis can discard secondary status detail. The fixed dial strip targets the requested phone dimensions; narrower devices need further omission/paging rather than scale-down. These limits must be judged alongside the after renders, not assumed green from compilation.

Pure strip dimensions live in Core with hand-written phone expectations in `HudInstrumentLayoutTests`; only that test filter was requested. Headless Bridge compile uses `/private/tmp/hudshots-check`, not a player build. No new Unity Assets files, so no new .meta GUIDs. Changes remain uncommitted; AGENT-LOG.md untouched.

B2 checks: final headless Bridge compile passed (0 errors; two existing unused WidgetVectors locals, two NU1900 network-audit warnings). Filtered FlightTests built, but VSTest aborted because the sandbox rejects its local socket; retry with single-node MSBuild reached the same runner restriction. Both new test methods then passed through a temporary direct .NET runner invoking the identical methods with equivalent assertions, without VSTest sockets (`/private/tmp/hud-layout-direct`). No full suite run. `git diff --check` passed. Runtime phone/render verification remains outstanding.
