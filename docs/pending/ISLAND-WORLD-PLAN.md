# Aero Playground island world — Phase 1A design

**G1a: PENDING Ty. Documentation and review generator only. This draft has measured fit failures; it is not a passing world or approval to implement Phase 1B.**

## Authority and numerical status

Ty’s 2026-10-30 locked v1 decisions, including revised 13 and 20, govern. Cursor Page could not be opened; the complete session restatement is the source. Menu lane is an ancestor of split-surfaces. No root AGENTS.md exists. Current lane rules supersede older ownership notes. Every authored coordinate, dimension, elevation or clearance is a **target** unless explicitly locked by Ty. Values in generated fit-check JSON are **computed design-screen measurements**, with sampling limits below; none is a TERPS result. No runtime, Tests/, menu, IFR or weather code changes are part of this phase.

## Frame, scale and regions

Origin is Frank Church summit (0,0). Design x east/y north in km; true north up. Engine is x north/y east in metres: `(simX,simY,simZ)=(1000*y,1000*x,-0.3048*altFt)`. Keep physics headings true; published courses and runway numbering use true minus 7° E. Preserve all supplied IFR values separately under Claude ownership. Main island target 18.3 × 17.5 km; authored coast spans 18.3 × 17.6 km (computed y extent includes southern canyon outlet), with Catalina separate inside the 20 km ocean ring. Coast polygon and Catalina ellipse are explicit in the spec.

North-east is Mokuleia/Dillingham, north-west Talkeetna/Denali/Ruth, west Cook Inlet/Lake Hood/Knik, central high country McCall/Bighorn/Frank Church, south-west Carson/Drake/Chugach, south-east Page/Powell, south OC and offshore Catalina. These are compressed teaching landscapes, not a geographic replica. Coast has a 100 m smooth fade into -30 m sea bed; open ocean continues through the ring. Ring fade is 19.5–20 km with no collision wall.

## Height field, landforms and water

The Python evaluator uses smooth compact envelopes, lake/glacier cuts, canyon cuts, lesson forks and runway pad blends in the order in the spec. One evaluator drives elevation render, binary bake and checks. It is a review prototype: ridge envelopes and cuts can compete, and failures are preserved rather than hidden. No established binary terrain loader exists in WorldTerrain (currently analytic), so `island-heightfield.f32` is a review interchange with JSON metadata, not a drop-in engine asset. 401 × 401 samples at 100 m; float32 metres MSL, little endian, south-to-north rows, west-to-east columns, x fastest. Continuous evaluator remains authoritative for features smaller than the grid.

The Invisible Walls rule applies to every operation: fade to zero relief before a footprint ends; continuous weighted blends rather than boolean precedence jumps. No cave or arch roof is represented as a second height field; those become WorldSolids shells and decks in Phase 1B. The review bake shows the upper rock envelope: Drake only applies its exterior 152 m ground pad, while the underground floor remains an independent height-aware deck at 4,700 ft. Contact queries below a roof require explicit shell routing in Phase 1B; they cannot use the upper terrain surface blindly. The 100 m image cannot validate sub-metre continuity or roofs.

| Peak | Centre km E,N | Top ft MSL (target) | Envelope radius km (target) |
|---|---|---:|---:|
| Frank Church | [0, 0] | 9020 | 2.0 |
| Sierra | [-1.54, -0.58] | 7550 | 1.3 |
| Chugach | [-5.4, -1.4] | 6890 | 1.6 |
| Denali-like | [-3.46, 5.86] | 8530 | 1.5 |
| Mokuleia | [5.6, 4] | 2950 | 1.5 |
| Catalina | [2.9, -13.7] | 2100 | 0.8 |
| Drake Hill | [-4.9, -4.5] | 5750 | 0.7 |

All four authored crest polylines are in the spec: Mokuleia, Sierra, Chugach–Denali and summit. Seven lift terrain sites S1–S7 are retained; ground only is Codex-owned, lift/weather is Claude-owned.

| Water | Centre km E,N | Surface ft MSL (target unless locked) | Character |
|---|---|---:|---|
| Payette Lake | [-0.48, 4.42] | 4990 | lake |
| Lake Hood | [-7.96, -1.8] | 79 | lake |
| Knik ice lake | [-6.95, -0.4] | 100 | lake |
| Lake Powell | [2.775, -4.525] | 3700 | locked full-pool elevation |
| Bighorn Outlet | [0.7, 0.9] | 6400 | landable float lake / open outlet |
| Bighorn Shelf | [1.8, 2.7] | 6660 | landable float lake / open outlet |
| Bighorn Larch | [3.3, 2] | 6600 | landable float lake / open outlet |
| Bighorn Hidden | [0.2, 1.3] | 7380 | committing bowl |
| Bighorn Cirque | [1.2, 0.8] | 7580 | committing bowl |
| Bighorn Crown | [2.7, 1.9] | 7840 | committing bowl |

Powell is locked at 3,700 ft; the prototype bed is 50 m below full pool, with dam, arches/spires and marina/ramp at F6. Knik/Ruth tongues carry 1,100/1,300 ft ski pads (locked range 1,000–1,300), glacial troughs, ice lake and bergs. Bighorn retains all six old alpine lake characters: three float-landable open outlets, three committing bowls, including Larch timber and Hidden outlet. Basin/outlet geometry in the review is a target; detailed alpine relief belongs to the later world build.

| Canyon | Rim width m | Floor width m | Depth m | Floor ft MSL |
|---|---|---|---|---|
| C1 | [1300, 900] | [250, 250] | [450, 450] | [4900, 4900] |
| C2 | [900, 600, 400] | [300, 225, 150] | [600, 600] | [4400, 3450] |
| C3 | [1400, 1400] | [350, 350] | [900, 500] | [3700, 0] |
| C4 | [800, 800] | [300, 300] | [1200, 1200] | [100, 100] |

C3 uses the complete five-point supplied path. C1 meets 3U2 and W1; C2 carries the nine retained non-Johnson strips and narrows 900→600→400 m; C4 carries the Knik route and W3. Design turn diameters at 45° bank: 60 kt 194/243 m, 80 kt 345/432 m, 100 kt 539/674 m at sea level/DA 7,500 (supplied targets). Floor widths differ from rim widths: the narrowing requires slow-flight judgement.

| Lesson | Fork | Safe route / saddle ft target | Box target |
|---|---|---|---|
| L1 | [0.6, 1.7] | [[-0.58, 2.78], [0.6, 1.7], [1.82, -0.86]]; 6400 | 600 m, rise 250 m to headwall 7500 ft |
| L2 | [-4.6, -0.9] | [[-3.17, -1.73], [-4.6, -0.9], [-5.8, -0.3], [-7.96, -1.8]]; 5740 | 500 m, rise 300 m to headwall 6890 ft |
| L3 | [1.92, -6.34] | [[2.88, -5.38], [1.92, -6.34], [0, -6.72], [-1.54, -9.6]]; 3200 | 300 m, rise 150 m to headwall 5000 ft |
| L4 | [-4.5, 4.7] | [[-6.34, 4.99], [-4.5, 4.7], [-3.9, 4.7]]; 1300 | 400 m, rise 350 m to headwall 8530 ft |

L1 right SE to Johnson under W1, left NE blocked; L2 right west to Knik/Lake Hood under W3, left SW blocked; L3 right west/SW to OC, left short dry side canyon; L4 right east to Ruth 1,300 ft amphitheatre, left north under Denali wall. Branch dimensions and climb rates are authored targets, not flight-model evidence.

## Airports, runway surfaces and pads

All supplied true field elevations and runway lengths are retained. PANC is 10,865 ft. KAVX is not a start. There are eight start airports; PALH is the immediately adjacent float variant of PANC. Every surface has 60 m side/end shoulders, then 200–300 m continuous blends. Pads are flat across; only explicitly sloped strips slope along. 3U2 target 0.5% requires survey confirmation; AP6 is 3%. Legacy sloped backcountry strips preserve their slopes and one-way character. Overlapping pads at different elevations cannot both be flat: conflicts below require redesign. Water lanes retain a flat water plane with submerged bed, rather than a land runway at water height.

| Field | Runway magnetic | Centre km E,N | True ° | Length × width ft | Field ft MSL | Surface |
|---|---|---|---:|---|---:|---|
| PHDH | 8/26 | [4.99, 5.9] | 091 | 9007 × 75 | 14 | asphalt |
| PANC | 16/34 | [-8.64, -3.07] | 165 | 10865 × 200 | 151 | asphalt |
| PALH | 8W/26W | [-7.96, -1.8] | 090 | 4541 × 188 | 79 | water |
| PALH | 15/33 | [-7.85, -1.25] | 153 | 2200 × 75 | 79 | gravel |
| PATK | 2/20 | [-6.34, 4.99] | 027 | 3500 × 75 | 365 | asphalt |
| KSNA | 2L/20R | [0.96, -8.06] | 028 | 5700 × 150 | 56 | asphalt |
| KSNA | 2R/20L | [1.2, -8.16] | 028 | 2886 × 75 | 56 | asphalt |
| KMYL | 17/35 | [-0.58, 2.78] | 179 | 6101 × 75 | 5024 | asphalt |
| 3U2 | 19/1 | [1.82, -0.86] | 192 | 3400 × 150 | 4960 | turf |
| KMEV | 17/35 | [-3.02, -1.73] | 180 | 7399 × 100 | 4724 | asphalt |
| KMEV | 13/31 | [-2.35, -2.02] | 135 | 5299 × 75 | 4724 | asphalt |
| KMEV | 13G/31G | [-2.05, -1.9] | 135 | 2000 × 75 | 4724 | grass |
| KPGA | 16/34 | [3.74, -3.26] | 170 | 5950 × 150 | 4317 | asphalt |
| KPGA | 7/25 | [4.18, -2.5] | 082 | 2201 × 75 | 4317 | asphalt |
| KAVX | 5/23 | [3.6, -14.4] | 055 | 3000 × 75 | 1602 | asphalt |
| DRAKE | 17 | [-4.9, -3.98] | 180 | 2992 × 100 | 4700 | grass |

PALH water-lane bed is 3 m below the locked 79 ft surface; the bed and water plane are distinct. PALH gravel is provisionally 79 ft because its separate true elevation was not supplied. KMEV 13G/31G centre/dimensions are explicit new targets. 3U2: land 19/depart 1; Drake: runway 17 one-way through hologram, 152 m visible plus ~760 m inside. 2,992 ft = 911.962 m; 152+760 m is rounded. KPGA 075 magnetic tie retains 7/25. AP8 now reads 36/18 for 007 true at 7E (change logged).

## Airparks and backcountry

AP1–AP14 each get a 60 ft wide strip, three hangars, five houses, windsock, three seeded parked aircraft; target buildings at least 100 m from centreline and outside approach surfaces. Surface, length and gear are explicit; fictional field elevations below are targets. Willow float pond and Bullfrog marina float access are retained.

| ID / airpark | Centre | True ° / magnetic | Surface / length ft | Gear | Elevation ft target |
|---|---|---|---|---|---:|
| AP1 Payette | [-1.6, 4.6] | 167 / 16/34 | grass / 2800 | wheels or tailwheel | 5024 |
| AP2 Talkeetna village | [-6.9, 6.8] | 017 / 1/19 | gravel / 1700 | tundra tires | 365 |
| AP3 Carson Valley | [-2, -3.9] | 180 / 17/35 | grass / 2400 | wheels | 4700 |
| AP4 Mokuleia ranch | [2.3, 6.7] | 067 / 6/24 | grass / 1800 | tailwheel | 1350 |
| AP5 Windward bluff | [7.2, 3] | 040 / 3/21 | grass / 1500 | STOL | 900 |
| AP6 Upcountry ranch | [4.2, 2.6] | 060 / 5/23 | grass / 2000 | tailwheel | 2600 |
| AP7 Point Mac | [-8.6, 1.2] | 097 / 9/27 | gravel / 2000 | tundra tires | 180 |
| AP8 Willow-style | [-8.2, 3.6] | 007 / 36/18 | grass / 2600 | wheels or floats | 250 |
| AP9 Turnagain bench | [-5.7, -6] | 047 / 4/22 | gravel / 1600 | tundra tires | 2900 |
| AP10 Yellow Pine | [2.7, 1.2] | 017 / 1/19 | gravel / 2800 | wheels | 4500 |
| AP11 Payette east shore | [0.8, 4.6] | 147 / 14/32 | gravel / 1900 | tundra tires | 5024 |
| AP12 Bullfrog | [1.8, -4.4] | 167 / 16/34 | gravel / 2500 | wheels; floats at marina | 3800 |
| AP13 Navajo mesa | [5.8, -2] | 017 / 1/19 | dirt / 2000 | tundra tires | 4500 |
| AP14 Trabuco ranch | [-2.4, -7.6] | 027 / 2/20 | grass / 2200 | wheels | 1600 |

Starting positions remain reviewable in the changes ledger. AP2 is proposed farther north and KMEV 150 m east to improve spacing/grade. Four fictional airpark elevation targets are revised; AP9 additionally moves southeast to gain run between low PANC and high Drake. No waiver beyond PANC–PALH is invented. Nine non-Johnson Frank Church strips move to C2. Their legacy positions, dimensions, surface, slope and one-way values are retained in the audit and changes list. This dense provisional allocation does not yet fit; see failures. Johnson Creek becomes 3U2 at real locked dimensions/elevation.

| Backcountry strip | New centre | Length × width m | Slope % | One-way true ° |
|---|---|---|---:|---|
| Chamberlain | [2.8, -1.5] | 1100 × 40 | 0.0 | None |
| Big Creek | [3, -0.8] | 1070 × 34 | 0.8 | None |
| Thomas Creek | [3.8, -0.7] | 1220 × 30 | 1.2 | None |
| Sulphur Creek | [4.6, -0.3] | 915 × 30 | 0.0 | None |
| Soldier Bar | [4.4, 0.6] | 400 × 20 | 1.5 | 250 |
| Cabin Creek | [3.2, 0.3] | 460 × 20 | 3.0 | 70 |
| Dewey Moore | [4.8, 1.1] | 370 × 18 | 4.0 | 290 |
| Indian Creek | [3.8, 1.6] | 1220 × 30 | 1.8 | None |
| Mackay Bar | [5, 1.8] | 460 × 24 | 1.8 | None |

## Hazards, features and retained gameplay

All six towers and all five wires are charted and deterministic WorldSolids (not height-field spikes). Tower bases read terrain; chart top elevations are recomputed after final terrain. Guy anchors, lights, wire sag and orange marker balls are geometry targets; W minimum AGL is the lowest conductor clearance above the full underlying terrain profile, not merely the midpoint. Legacy crop conductors consolidate into W4 so no sixth uncharted wire appears. Forest has fixed seeds, water/pad/approach exclusions and 42 m maximum trees. Road bridge and Gateway-style arch survive; only the named Golden Gate, Eiffel tower, DC Mall and F5 tunnel are dropped.

| Hazard | Geometry | Height |
|---|---|---|
| T1 | [4.9, 4.45]; guyed | 75 m AGL |
| T2 | [-1.7, -1]; unguyed | 35 m AGL |
| T3 | [2.9, -7.4]; guyed | 180 m AGL |
| T4 | [-6.9, -2.9]; guyed | 50 m AGL |
| T5 | [-6.9, 5.4]; guyed | 55 m AGL |
| T6 | [4.6, -3.95]; guyed | 45 m AGL |
| W1 | [[1.55, 0.25], [2.2, 0.3]] | 40 m minimum AGL |
| W2 | [[2.4, -5.55], [3.3, -5.25]] | 250 m minimum AGL |
| W3 | [[-6.55, -1.1], [-6.65, -0.05]] | 30 m minimum AGL |
| W4 | [[-3.9, -3.45], [-2.5, -3.45]] | 18 m minimum AGL |
| W5 | [[-2.2, -8.5], [-0.2, -8.9]] | 60 m minimum AGL |

Drake F1: 1,400 m hill, 320 m relief above 4,700 ft valley, 200 m north cliff, chamber 820 × 420 × 140 m and ≥60 m rock cover. East/left glider slope 70 m high, 260 m run, 70 × 45 m pillow pool; west/right 240 m apron and 240 × 45 m two-level glass labs. No school name. Codex designs shell/interior geometry; Claude owns enrolment/hologram trigger, advanced curriculum and radio logic. The old tunnel-first order is superseded. Mouth/cliff and overhead chamber cover need 3D shell validation; a height map cannot certify them.

Pirate cave F2: Catalina SW/leeward shore, 60 × 26 m mouth pointing 240 true (target), 90 m radius chamber, 10 m skylight, wreck/beach/seeded driftwood. Catalina remains at its locked ellipse/position; Avalon, Casino, moorings/beach on NE shore and 150 m sea arch at isthmus. Carrier target (6.8,-10.5), operating radius 0.3 km; the 18 kt moving deck survives, but route clearance requires full IFR geometry.

OC Fly City retains rooftop race, city grid, through-building hole, sky bridges, fountain plaza, rooftop rings, spinning ring and landable raised disc. New consolidated target city extent 1.02 × 2.55 km; maximum buildings 300 m. RACE retains the exact 12 ordered gates/pylons with explicit per-element transforms in the spec, 1.11 × 0.35 km footprint and 75 m pylons near (7,-0.3); at least 1 km from every final is a target requiring the screen below. STOL/tow at AP3; crop dusting at F11 hay fields. Page retains landable mesa tops, ruts, rock-pile thresholds and playa. Rivers retain seeded gravel bars, clean landing lanes, chalk lines/cones and driftwood.

| Gameplay ID | Inventory item | Allocation |
|---|---|---|
| event:race | Air Racing | RACE |
| event:stol | STOL contest | AP3 Carson Valley; 150 m line setback, 600 ft marks, 3 rounds |
| event:dust | Crop dusting | F11 |
| event:aerobox | Aerobatic box | AB1-AB3 |
| event:combat | Combat zone | CZ1-CZ3 |
| event:dogfight | Dogfight 1 v 1 | CZ1-CZ3 |
| a1c1-wings-level | Wings level | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines |
| a1c2-best-glide | Best glide | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines |
| a1c3-cardinal-turn | Cardinal turn | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines |
| a1c4-stall-recover | Stall recovery | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines |
| a1c10-headwind-landing | Headwind landing | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines |
| a1c11-crosswind-landing | Crosswind landing | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines |
| lesson:straight | Straight: wings level | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines |
| lesson:climb-level-descend | Climb, level, descend | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines |
| lesson:glide-rear | Glide: speed to fly | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines |
| lesson:glide-side | Glide: speed to fly · side view | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines |
| lesson:climb-vy | Climb: best rate (Vy) | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines |
| lesson:climb-vx | Climb: best angle (Vx) · side view | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines |
| practice:xwind-rudder | Crosswind · rudder | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines |
| practice:xwind-aileron | Crosswind · aileron | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines |
| practice:land-rudder | Crosswind landing · rudder | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines |
| practice:land-aileron | Crosswind landing · aileron | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines |
| practice:flare | Round-out & flare | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines |
| practice:flare-side | Flare · side view | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines |
| practice:approach-side | Approach · side view | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines |
| practice:s-turns | S-turns 45° | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines |
| practice:s-turns-test | Test: S-turns, one speed cycle | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines |
| practice:stall-side | Stall · side view | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines |
| practice:stall-rudder | Stall, wing drop · rudder | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines |
| practice:stall-elevator | Stall, recover · elevator | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines |
| F1 | Drake Hill and hidden school shell | F1 |
| F2 | Pirate cave | F2 |
| F3 | Avalon Casino harbour beach | F3 |
| F4 | Catalina sea arch | F4 |
| F6 | Powell arches spires dam marina ramp | F6 |
| F7 | Sea stacks Dillingham beach | F7 |
| F11 | Carson hay fields crop dusting | F11 |
| F12 | Page landable mesas rock pile strips | F12 |
| F13 | Knik braided bars driftwood | F13 |
| F14 | Middle Fork bars and strips | F14 |
| F15 | Talkeetna River bars | F15 |
| F16 | OC beaches piers marina | F16 |
| F17 | Bighorn Crags lakes | F17 |
| F18 | Historic outdoor glider slope | F18 |
| CITY | OC Fly City and city grid | CITY |
| RACE | Air race pylon course | RACE |
| CARRIER | Carrier off OC | CARRIER |
| FOREST | Regional forest | FOREST |
| GATEWAY | Gateway style river arch | GATEWAY |
| PLAYA | Page dry playa | PLAYA |
| strip:Chamberlain | Chamberlain | C2 Middle Fork |
| strip:Big Creek | Big Creek | C2 Middle Fork |
| strip:Thomas Creek | Thomas Creek | C2 Middle Fork |
| strip:Sulphur Creek | Sulphur Creek | C2 Middle Fork |
| strip:Soldier Bar | Soldier Bar | C2 Middle Fork |
| strip:Cabin Creek | Cabin Creek | C2 Middle Fork |
| strip:Dewey Moore | Dewey Moore | C2 Middle Fork |
| strip:Indian Creek | Indian Creek | C2 Middle Fork |
| strip:Mackay Bar | Mackay Bar | C2 Middle Fork |
| extra:59 | aerotow and tow hookup | AP3 tow; PALH/Powell floats; Knik/Ruth ski; AP3 separate undulation lane; C3 falls/bridge; F1/F18 school |
| extra:60 | float bases ramps buoy lanes | AP3 tow; PALH/Powell floats; Knik/Ruth ski; AP3 separate undulation lane; C3 falls/bridge; F1/F18 school |
| extra:61 | glacier ski landings and icebergs | AP3 tow; PALH/Powell floats; Knik/Ruth ski; AP3 separate undulation lane; C3 falls/bridge; F1/F18 school |
| extra:62 | sloped grass Snoopy swoop training lane | AP3 tow; PALH/Powell floats; Knik/Ruth ski; AP3 separate undulation lane; C3 falls/bridge; F1/F18 school |
| extra:63 | waterfalls and plunge pools | AP3 tow; PALH/Powell floats; Knik/Ruth ski; AP3 separate undulation lane; C3 falls/bridge; F1/F18 school |
| extra:64 | river road bridge | AP3 tow; PALH/Powell floats; Knik/Ruth ski; AP3 separate undulation lane; C3 falls/bridge; F1/F18 school |
| extra:65 | hidden school interior shell and outdoor historic gliders | AP3 tow; PALH/Powell floats; Knik/Ruth ski; AP3 separate undulation lane; C3 falls/bridge; F1/F18 school |
| gunnery:targets | five 30 m bullseyes + one 14 m target; three target drones | six moored target rafts per CZ1-CZ3; three-drone orbit |
| combat:concealment | six legacy puffy combat clouds / concealment | CZ1-CZ3 weather volumes proposed; Claude-owned |

Inventory was extracted from current SessionSettings Challenges/Lessons, StartMenu dispatch, all six configs/challenges JSONs, WorldFeatures, Playground, Seaside and regional world sources. Extra retained items beyond the supplied list include six additional lesson variants (climb/level/descend, Vx/Vy and glide views), all practice variants, tow hookup, float ramp/buoys, rooftop disc/rings/sky bridges, playa, plunge pools, river bridge and Gateway arch. Source audit lists all public geometry entry points. No event/scoring code is authored here.

## Starts and lesson infrastructure

Eight starts: coastal PHDH/PANC/PATK/KSNA; inland KMYL/3U2/KMEV/KPGA. PALH is a PANC float variant, not a ninth airport start. Every start has OnTheRunway, OnFinal (300 ft above field, 0.8 km before threshold) and InTheAir (1,500 ft above field) candidate positions in the spec. Initial runway points are 80 m in from the threshold. These are Codex geometry proposals; Claude owns runway selection, ApproachSpawn, aircraft-specific trim and terrain-safe spawn validation. Every AB and active CZ has a start; S1–S4 have 1,500 ft AGL thermal start positions. Each field has an explicit straight pad-shoulder section line for S-turns, 30 m outside the runway edge, up to 1.2 km long (shorter where the runway requires); PALH uses a buoy reference. Terrain range is measured below; obstacle and flight checks remain pending.

## Aerobatic boxes

Three standard IAC squares, 1,000 × 1,000 m, sides true N/S/E/W; floor 100 m and ceiling 1,100 m above centre ground/water. Judges line 150 m outside airport-facing side. White centre cross arms 40 × 4 m; corner L legs 30 × 4 m; side T markers use 30 × 4 m target legs. Water versions are moored white raft shapes with orange corner buoys and shore judges marker. Exact 100/1,100 m governs; rounded 330/3,610 ft labels in the brief are approximate. Generated values use conversion from metres.

| Box | Centre | Density altitude | Centre / floor / ceiling ft MSL targets | Judges side |
|---|---|---|---|---|
| AB1 Dillingham | [6.9, 7.6] | low | 0 / 328 / 3609 | south |
| AB2 Cook Inlet | [-10.1, -3.6] | low | 0 / 328 / 3609 | east |
| AB3 McCall | [-2.5, 4] | high | 5000 / 5328 / 8609 | east |

AB1 supplied proximity targets: ILS 26 final 1.2 km, AP4 final 0.5 km; T201 KAALA–TRADE ≥5,000 ft. AB2 targets: PANC runway 0.9 km, ILS 34 final 1.5 km, PATK NDB final 1.0 km; G1/V1/V5 ≥9,000, adjacent T201 ≥5,000. AB3 targets: KMYL RNAV/LDA final 1.4 km, AP1 0.4 km; A1 11,000, V7 11,500; warm-day DA >8,000 ft. All require final recomputation from complete IFR geometry, especially box-edge versus centre clearance. No additional box at KSNA/KMEV/KPGA.

## Combat zones

Charted magenta hatched annular sectors, true bearings from summit, 500–12,500 ft MSL target, HOT by default and switchable COLD. Status is shown on chart/ATIS; Claude owns event:combat/dogfight hook: HOT can engage, COLD never engages. No zone lies in the south Catalina/BC hold/carrier corridor. Required distance ≥1.85 km from every 0–4 nm final, missed approach, hold and airway. Full procedure geometry is incomplete, so preliminary distances cannot certify this requirement. Each active CZ retains six gunnery bullseyes on moored target rafts (five 30 m radii, one 14 m) and a three-drone orbit target. This moves existing targets off the land without adding islands. Claude adapts hit height, drone logic and the six legacy concealment cloud volumes. CZ4 is optional/off, never silently enabled.

| Zone | True bearings | Radial band km | Status |
|---|---|---|---|
| CZ1 KAIWI MOA | [15, 50] | [14, 19.5] | HOT target |
| CZ2 PAINTED MOA | [95, 135] | [13.5, 19.5] | HOT target |
| CZ3 SUSITNA MOA | [315, 350] | [14.5, 19.5] | HOT target |
| CZ4 KACHEMAK MOA | [228, 262] | [14, 19.5] | OPTIONAL / OFF pending Ty |

MOA naming check: FAA-domain web searches for KAIWI, PAINTED, SUSITNA and KACHEMAK did not yield an authoritative full name-list match. Absence from search results is not uniqueness verification; all names remain explicit fictional placeholders for design review only. Check the [FAA SUA portal](https://sua.faa.gov/) before runtime chart publication.

## IFR preservation and ownership

The `ifr` section preserves all supplied navaids/frequencies, routes/courses/MEA targets, twelve approach records, BC fixes/feeders/hold/missed/notes, one range approach, VFR-only fields and weather defaults. It is Claude-owned and does not alter runtime nav. KSNA has only LOC BC RWY 2L on I-SNA 111.75, no glideslope or ILS. Plain CDI reverse sensing; HSI front course 201. Final 021 magnetic/028 true, BALBO/LIDOS/WEDGE and supplied offshore missed/hold constraints are unchanged. Minima are targets; Terps computed results win. Drawn IFR finals are a thin geometric overlay reconstructed from supplied courses/thresholds, not procedure plates. T201 fix coordinates and most missed/hold geometry are not specified, so no fabricated coordinates are assigned. Existing Terps lateral compression and sampling are Claude concerns, not changes in this design.

## Fit check — computed design screen

Race course sampled element-to-known-final minimum: **3.198 km**, nearest KPGA RNAV 16. Includes 24 m lateral gate allowance; full pylon/marker and box/zone hull validation remains to be done.

Run `python3 docs/pending/tools/generate_island.py --png`. Checks use the continuous prototype height function, not runtime terrain. Final screen: 75 longitudinal × 3 lateral probes over 4 nm, fan half-width 150 m + 5% distance, plus tower and 21-point wire probes. Airway screen: 21 × 11 probes within 2 nm. Pad samples: nine per runway; field distances: padded rectangle separation and centre distance. These screens can find failures; passing samples do not prove full-area clearance. All runtime, solids, forest/buildings, actual climb performance and full IFR validation remain future work.

| Design rule | Result | Evidence |
|---|---|---|
| 1 | PASS sampled | 345 continuous samples; maximum runway surface error 0.00 m at KAVX 5/23; 0 incompatible overlapping pad pairs. 60 m shoulders are authored; Drake cave interior excluded (3D floor/roof unverified); PALH gravel datum and 3U2 slope remain targets. |
| 2 | FAIL | 19 flat-field pairs exceed 35% mean edge-to-edge grade; worst finite 102.63%. PALH–KMEV pairs: PALH 8W/26W/KMEV 17/35 34.42%, PALH 8W/26W/KMEV 13/31 33.70%, PALH 8W/26W/KMEV 13G/31G 29.19%, PALH 15/33/KMEV 17/35 31.38%, PALH 15/33/KMEV 13/31 30.82%, PALH 15/33/KMEV 13G/31G 26.97%. This is an endpoint feasibility test, not a slope-profile certificate. |
| 3 | FAIL | 7/12 supplied finals penetrate sampled terrain fan; 0 sampled tower/wire penetrations. All-runway-end surfaces, buildings, trees, non-KSNA misses/holds remain unverified. KSNA data retained unchanged. |
| 4 | FAIL | 4 sampled airway segments have less than 2000 ft target clearance within 2 nm; T201 coordinates absent. Full-width grid/obstacle sweep and final computed MEAs belong to Claude. |
| 5 | PASS footprint / UNVERIFIED flight volumes | Coast/Catalina vertices maximum radius 16.446 km <20; active CZ outer radius 19.5 km; ocean bed is continuous and has no wall. BALBO hold outer radius 19.2 km is supplied target, not computed; T201/misses need geometry. |
| 6 | FAIL | 7 centre-spacing violations (already fails before pad-edge interpretation); 0 runway-end shore setback violations. PANC/PALH waiver alone applied. Backcountry and airpark relocation needs approval/revision. |
| 7 | PASS target / UNVERIFIED flight | L1: 3376 ft/min required at 80 kt versus 300 ft/min reference, L2: 4861 ft/min required at 80 kt versus 300 ft/min reference, L3: 4051 ft/min required at 80 kt versus 300 ft/min reference, L4: 7089 ft/min required at 80 kt versus 300 ft/min reference. Need actual loaded trainer/DA trajectory and headwall mesh; no sim claim. |
| 8 | UNVERIFIED | 0 box/final proximity warnings (<0.5 km screen); known CZ/final/airway boundary distances: CZ1 3.199 km, CZ2 2.880 km, CZ3 3.380 km. Missing T201, missed/hold polygons prevent 1 nm certification; supplied box/MEA targets retained. |
| 9 | PASS generated | No random calls; authored integer seeds; continuous evaluator and binary/raster generated in fixed iteration order. Re-run SHA-256 comparison required before commit. |
| 10 | PASS design / UNVERIFIED integration | One continuous evaluator drives bake/render/checks. Source engine remains unchanged analytic WorldTerrain; Phase 1B must bind physics and renderer to same evaluator. Towers/wires/cave shells remain separate WorldSolids. |

### Peak centre elevations after all cuts

```json
[
  {
    "id": "Frank Church",
    "target_ft": 9020,
    "computed_ft": 9020.0
  },
  {
    "id": "Sierra",
    "target_ft": 7550,
    "computed_ft": 7550.0
  },
  {
    "id": "Chugach",
    "target_ft": 6890,
    "computed_ft": 6890.0
  },
  {
    "id": "Denali-like",
    "target_ft": 8530,
    "computed_ft": 8530.0
  },
  {
    "id": "Mokuleia",
    "target_ft": 2950,
    "computed_ft": 2950.0
  },
  {
    "id": "Catalina",
    "target_ft": 2100,
    "computed_ft": 2100.0
  },
  {
    "id": "Drake Hill",
    "target_ft": 5750,
    "computed_ft": 5750.0
  }
]
```

### Practice line terrain range (not a flatness guarantee)

```json
[
  {
    "id": "ST-PHDH",
    "range_m": 0.0
  },
  {
    "id": "ST-PANC",
    "range_m": 0.0
  },
  {
    "id": "ST-PALH",
    "range_m": 0.0
  },
  {
    "id": "ST-PATK",
    "range_m": 0.0
  },
  {
    "id": "ST-KSNA",
    "range_m": 0.0
  },
  {
    "id": "ST-KMYL",
    "range_m": 0.0
  },
  {
    "id": "ST-3U2",
    "range_m": 4.98
  },
  {
    "id": "ST-KMEV",
    "range_m": 0.0
  },
  {
    "id": "ST-KPGA",
    "range_m": 0.0
  }
]
```

### Centre-spacing failures

```json
[
  {
    "fields": [
      "AP6",
      "Indian Creek"
    ],
    "centre_distance_km": 1.077,
    "pad_gap_km": 0.236,
    "required_km": 1.5
  },
  {
    "fields": [
      "AP6",
      "Mackay Bar"
    ],
    "centre_distance_km": 1.131,
    "pad_gap_km": 0.752,
    "required_km": 1.5
  },
  {
    "fields": [
      "AP10",
      "Cabin Creek"
    ],
    "centre_distance_km": 1.03,
    "pad_gap_km": 0.528,
    "required_km": 1.5
  },
  {
    "fields": [
      "AP10",
      "Indian Creek"
    ],
    "centre_distance_km": 1.17,
    "pad_gap_km": 0.598,
    "required_km": 1.5
  },
  {
    "fields": [
      "Chamberlain",
      "Big Creek"
    ],
    "centre_distance_km": 0.728,
    "pad_gap_km": 0.339,
    "required_km": 0.8
  },
  {
    "fields": [
      "Soldier Bar",
      "Dewey Moore"
    ],
    "centre_distance_km": 0.64,
    "pad_gap_km": 0.317,
    "required_km": 0.8
  },
  {
    "fields": [
      "Dewey Moore",
      "Mackay Bar"
    ],
    "centre_distance_km": 0.728,
    "pad_gap_km": 0.359,
    "required_km": 0.8
  }
]
```

### Runway-end shore failures

```json
[]
```

### Grade feasibility failures

```json
[
  {
    "grade_percent": 102.63,
    "fields": [
      "KPGA 16/34",
      "Chamberlain"
    ],
    "edge_gap_km": 0.7969
  },
  {
    "grade_percent": 79.82,
    "fields": [
      "KPGA 7/25",
      "Chamberlain"
    ],
    "edge_gap_km": 1.0245
  },
  {
    "grade_percent": 54.14,
    "fields": [
      "AP5",
      "Sulphur Creek"
    ],
    "edge_gap_km": 3.4343
  },
  {
    "grade_percent": 53.96,
    "fields": [
      "AP4",
      "AP11"
    ],
    "edge_gap_km": 2.0754
  },
  {
    "grade_percent": 52.17,
    "fields": [
      "PHDH 8/26",
      "AP11"
    ],
    "edge_gap_km": 2.927
  },
  {
    "grade_percent": 51.54,
    "fields": [
      "PALH 8W/26W",
      "DRAKE 17"
    ],
    "edge_gap_km": 2.7325
  },
  {
    "grade_percent": 47.35,
    "fields": [
      "KSNA 2L/20R",
      "AP12"
    ],
    "edge_gap_km": 2.4102
  },
  {
    "grade_percent": 47.28,
    "fields": [
      "AP13",
      "Sulphur Creek"
    ],
    "edge_gap_km": 1.6115
  },
  {
    "grade_percent": 45.12,
    "fields": [
      "KPGA 16/34",
      "Sulphur Creek"
    ],
    "edge_gap_km": 1.8123
  },
  {
    "grade_percent": 44.15,
    "fields": [
      "PANC 16/34",
      "DRAKE 17"
    ],
    "edge_gap_km": 3.1402
  },
  {
    "grade_percent": 43.78,
    "fields": [
      "KPGA 7/25",
      "Sulphur Creek"
    ],
    "edge_gap_km": 1.8678
  },
  {
    "grade_percent": 43.44,
    "fields": [
      "PALH 15/33",
      "DRAKE 17"
    ],
    "edge_gap_km": 3.2425
  },
  {
    "grade_percent": 41.32,
    "fields": [
      "AP10",
      "Sulphur Creek"
    ],
    "edge_gap_km": 1.844
  },
  {
    "grade_percent": 41.3,
    "fields": [
      "AP12",
      "Chamberlain"
    ],
    "edge_gap_km": 2.3616
  },
  {
    "grade_percent": 40.28,
    "fields": [
      "DRAKE 17",
      "AP9"
    ],
    "edge_gap_km": 1.3621
  },
  {
    "grade_percent": 39.67,
    "fields": [
      "KSNA 2R/20L",
      "AP12"
    ],
    "edge_gap_km": 2.8769
  },
  {
    "grade_percent": 37.55,
    "fields": [
      "KSNA 2L/20R",
      "Chamberlain"
    ],
    "edge_gap_km": 5.6368
  },
  {
    "grade_percent": 36.22,
    "fields": [
      "PHDH 8/26",
      "Sulphur Creek"
    ],
    "edge_gap_km": 5.879
  },
  {
    "grade_percent": 35.59,
    "fields": [
      "AP10",
      "Chamberlain"
    ],
    "edge_gap_km": 2.1413
  }
]
```

### Incompatible pad claims

```json
[]
```

### Airway clearance failures

```json
[
  {
    "route": "V2",
    "segment": "SLI-PGA",
    "terrain_ft": 4600,
    "mea_target_ft": 6500,
    "clearance_ft": 1900
  },
  {
    "route": "V3",
    "segment": "CKH-PGA",
    "terrain_ft": 7000,
    "mea_target_ft": 7500,
    "clearance_ft": 500
  },
  {
    "route": "A2",
    "segment": "CMQ-PEE",
    "terrain_ft": 7000,
    "mea_target_ft": 5000,
    "clearance_ft": -2000
  },
  {
    "route": "G1",
    "segment": "PGA-MEV",
    "terrain_ft": 7008,
    "mea_target_ft": 8000,
    "clearance_ft": 992
  }
]
```

### Final terrain screen

```json
[
  {
    "airport": "PANC",
    "title": "ILS 16",
    "max_terrain_penetration_m": 172.39,
    "location": [
      -8.986895534018865,
      -1.137794542123886
    ],
    "sampled_half_width": "150 m + 0.05 * distance; design screen only"
  },
  {
    "airport": "PANC",
    "title": "ILS 34",
    "max_terrain_penetration_m": 1019.87,
    "location": [
      -7.943572880681959,
      -5.012188291656271
    ],
    "sampled_half_width": "150 m + 0.05 * distance; design screen only"
  },
  {
    "airport": "KSNA",
    "title": "LOC BC RWY 2L",
    "max_terrain_penetration_m": 417.42,
    "location": [
      0.26330559922604646,
      -9.017700218327105
    ],
    "sampled_half_width": "150 m + 0.05 * distance; design screen only"
  },
  {
    "airport": "PHDH",
    "title": "ILS 26",
    "max_terrain_penetration_m": 143.62,
    "location": [
      6.659856389598627,
      5.705811195485112
    ],
    "sampled_half_width": "150 m + 0.05 * distance; design screen only"
  },
  {
    "airport": "KMYL",
    "title": "RNAV 17",
    "max_terrain_penetration_m": 0.0,
    "location": [
      -0.5962271148670972,
      3.7096507881139296
    ],
    "sampled_half_width": "150 m + 0.05 * distance; design screen only"
  },
  {
    "airport": "KMYL",
    "title": "LDA/DME 17",
    "max_terrain_penetration_m": 0.0,
    "location": [
      -0.5962271148670972,
      3.7096507881139296
    ],
    "sampled_half_width": "150 m + 0.05 * distance; design screen only"
  },
  {
    "airport": "KMEV",
    "title": "Range 35",
    "max_terrain_penetration_m": 0.0,
    "location": [
      -3.02,
      -2.8576076
    ],
    "sampled_half_width": "150 m + 0.05 * distance; design screen only"
  },
  {
    "airport": "KMEV",
    "title": "RNAV 35",
    "max_terrain_penetration_m": 0.0,
    "location": [
      -3.02,
      -2.8576076
    ],
    "sampled_half_width": "150 m + 0.05 * distance; design screen only"
  },
  {
    "airport": "KPGA",
    "title": "VOR/DME 34",
    "max_terrain_penetration_m": 66.24,
    "location": [
      3.799556445562766,
      -4.576876883051978
    ],
    "sampled_half_width": "150 m + 0.05 * distance; design screen only"
  },
  {
    "airport": "KPGA",
    "title": "RNAV 16",
    "max_terrain_penetration_m": 772.73,
    "location": [
      3.234001569167525,
      -1.5135806990795826
    ],
    "sampled_half_width": "150 m + 0.05 * distance; design screen only"
  },
  {
    "airport": "KAVX",
    "title": "VOR-A",
    "max_terrain_penetration_m": 0.0,
    "location": [
      3.9745163146489273,
      -14.137760853300302
    ],
    "sampled_half_width": "150 m + 0.05 * distance; design screen only"
  },
  {
    "airport": "PATK",
    "title": "NDB 2",
    "max_terrain_penetration_m": 1270.81,
    "location": [
      -6.763951052672996,
      4.157949210204009
    ],
    "sampled_half_width": "150 m + 0.05 * distance; design screen only"
  }
]
```

### Final obstacle screen

```json
[]
```

### Box proximity warnings

```json
[]
```

### Zone known-segment screen

```json
[
  {
    "zone": "CZ1",
    "minimum_sampled_distance_km": 3.199,
    "nearest": "PHDH ILS 26",
    "passes_known_segments": true
  },
  {
    "zone": "CZ2",
    "minimum_sampled_distance_km": 2.88,
    "nearest": "KAVX VOR-A",
    "passes_known_segments": true
  },
  {
    "zone": "CZ3",
    "minimum_sampled_distance_km": 3.38,
    "nearest": "KMYL LDA/DME 17",
    "passes_known_segments": true
  }
]
```

## Changes ledger

Every authored relocation/correction is listed below and in spec.changes. All locked elevations, runway lengths and supplied IFR data remain unchanged. Proposed centre/elevation revisions are explicitly listed; KMEV shifts 150 m east, AP2 moves north and AP9 moves southeast. The previously unspecified glider runway has explicit target dimensions. Changes from legacy code are grouped only where the exact per-object transforms are still a later implementation task.

| Item | Old | New target | Reason |
|---|---|---|---|
| AP8 runway numbers | 18/36 at 007 true | 36/18 at unchanged 007 true | 007 true - 7E = 000 magnetic; order follows the authored heading; length and position unchanged |
| KMEV glider strip | 13G/31G; no dimensions/centre | centre (-2.20,-1.90), 135 true, 2000 x 75 ft grass, 4724 ft | explicit review geometry; target pending Ty approval |
| F1 Drake Hill and hidden school shell | legacy duplicated plateau/world position or unspecified starting position | [-4.9, -4.3] | consolidated island allocation; supplied F positions retained where given; new placements are targets |
| F2 Pirate cave | legacy duplicated plateau/world position or unspecified starting position | [2.7, -14.6] | consolidated island allocation; supplied F positions retained where given; new placements are targets |
| F3 Avalon Casino harbour beach | legacy duplicated plateau/world position or unspecified starting position | [4.5, -13.7] | consolidated island allocation; supplied F positions retained where given; new placements are targets |
| F4 Catalina sea arch | legacy duplicated plateau/world position or unspecified starting position | [3.5, -14] | consolidated island allocation; supplied F positions retained where given; new placements are targets |
| F6 Powell arches spires dam marina ramp | legacy duplicated plateau/world position or unspecified starting position | [2.88, -5.15] | consolidated island allocation; supplied F positions retained where given; new placements are targets |
| F7 Sea stacks Dillingham beach | legacy duplicated plateau/world position or unspecified starting position | [6, 6.2] | consolidated island allocation; supplied F positions retained where given; new placements are targets |
| F11 Carson hay fields crop dusting | legacy duplicated plateau/world position or unspecified starting position | [-2.9, -4.4] | consolidated island allocation; supplied F positions retained where given; new placements are targets |
| F12 Page landable mesas rock pile strips | legacy duplicated plateau/world position or unspecified starting position | [5, -3.7] | consolidated island allocation; supplied F positions retained where given; new placements are targets |
| F13 Knik braided bars driftwood | legacy duplicated plateau/world position or unspecified starting position | [-6.7, 0.3] | consolidated island allocation; supplied F positions retained where given; new placements are targets |
| F14 Middle Fork bars and strips | legacy duplicated plateau/world position or unspecified starting position | [3.5, 0.5] | consolidated island allocation; supplied F positions retained where given; new placements are targets |
| F15 Talkeetna River bars | legacy duplicated plateau/world position or unspecified starting position | [-7.2, 5.8] | consolidated island allocation; supplied F positions retained where given; new placements are targets |
| F16 OC beaches piers marina | legacy duplicated plateau/world position or unspecified starting position | [2.8, -9] | consolidated island allocation; supplied F positions retained where given; new placements are targets |
| F17 Bighorn Crags lakes | legacy duplicated plateau/world position or unspecified starting position | [1.2, 1.8] | consolidated island allocation; supplied F positions retained where given; new placements are targets |
| F18 Historic outdoor glider slope | legacy duplicated plateau/world position or unspecified starting position | [4.3, 4] | consolidated island allocation; supplied F positions retained where given; new placements are targets |
| CITY OC Fly City and city grid | legacy duplicated plateau/world position or unspecified starting position | [4.8, -6.6] | consolidated island allocation; supplied F positions retained where given; new placements are targets |
| RACE Air race pylon course | legacy duplicated plateau/world position or unspecified starting position | [7, -0.3] | consolidated island allocation; supplied F positions retained where given; new placements are targets |
| CARRIER Carrier off OC | legacy duplicated plateau/world position or unspecified starting position | [6.8, -10.5] | consolidated island allocation; supplied F positions retained where given; new placements are targets |
| FOREST Regional forest | legacy duplicated plateau/world position or unspecified starting position | [0, 2] | consolidated island allocation; supplied F positions retained where given; new placements are targets |
| GATEWAY Gateway style river arch | legacy duplicated plateau/world position or unspecified starting position | [0.5, -6.7] | consolidated island allocation; supplied F positions retained where given; new placements are targets |
| PLAYA Page dry playa | legacy duplicated plateau/world position or unspecified starting position | [5.8, -4.9] | consolidated island allocation; supplied F positions retained where given; new placements are targets |
| Chamberlain | {'sim_xy_m': [-3900, -11000], 'elevation': 'legacy relative to 7000 ft plateau'} | {'centre': [2.8, -1.5], 'elevation_ft': 7000} | move into Middle Fork; retain dimensions, surface, slope and one-way character; candidate fit unresolved |
| Big Creek | {'sim_xy_m': [-2600, -11400], 'elevation': 'legacy relative to 7000 ft plateau'} | {'centre': [3, -0.8], 'elevation_ft': 7000} | move into Middle Fork; retain dimensions, surface, slope and one-way character; candidate fit unresolved |
| Thomas Creek | {'sim_xy_m': [-4700, -14100], 'elevation': 'legacy relative to 7000 ft plateau'} | {'centre': [3.8, -0.7], 'elevation_ft': 7000} | move into Middle Fork; retain dimensions, surface, slope and one-way character; candidate fit unresolved |
| Sulphur Creek | {'sim_xy_m': [-3200, -14300], 'elevation': 'legacy relative to 7000 ft plateau'} | {'centre': [4.6, -0.3], 'elevation_ft': 7000} | move into Middle Fork; retain dimensions, surface, slope and one-way character; candidate fit unresolved |
| Soldier Bar | {'sim_xy_m': [-1900, -13900], 'elevation': 'legacy relative to 7000 ft plateau'} | {'centre': [4.4, 0.6], 'elevation_ft': 5865} | move into Middle Fork; retain dimensions, surface, slope and one-way character; candidate fit unresolved |
| Cabin Creek | {'sim_xy_m': [-3500, -12126], 'elevation': 'legacy relative to 7000 ft plateau'} | {'centre': [3.2, 0.3], 'elevation_ft': 6259} | move into Middle Fork; retain dimensions, surface, slope and one-way character; candidate fit unresolved |
| Dewey Moore | {'sim_xy_m': [-2900, -13528], 'elevation': 'legacy relative to 7000 ft plateau'} | {'centre': [4.8, 1.1], 'elevation_ft': 6094} | move into Middle Fork; retain dimensions, surface, slope and one-way character; candidate fit unresolved |
| Indian Creek | {'sim_xy_m': [-300, -12830], 'elevation': 'legacy relative to 7000 ft plateau'} | {'centre': [3.8, 1.6], 'elevation_ft': 4841} | move into Middle Fork; retain dimensions, surface, slope and one-way character; candidate fit unresolved |
| Mackay Bar | {'sim_xy_m': [1000, -12486], 'elevation': 'legacy relative to 7000 ft plateau'} | {'centre': [5, 1.8], 'elevation_ft': 4917} | move into Middle Fork; retain dimensions, surface, slope and one-way character; candidate fit unresolved |
| legacy airports/features | Valley/Bench/Mesa/Summit and repeated boxes/races/cities | eight real-elevation starts; AB1-AB3; one RACE; OC city | locked island plan |
| legacy glacial/alpine terrain | Alaska/Enchantments blocks and old high ski elevations | Knik/Ruth 1100/1300 ft ski tongues; Bighorn six-lake stair | locked renaming and low glacier landing zones |
| 3U2 slope | unspecified slope | 0.5% longitudinal target | make explicit pending surveyed value; locked centre elevation and dimensions retained |
| airpark elevations/widths | unspecified | per-airpark target elevations, width 60 ft, 60 m shoulders | required for height-field review; no true elevation was supplied for fictional strips |
| legacy waterfall tunnel | existing tunnel and older tunnel-first Drake plan | excluded from island design; code unchanged in Phase 1A | locked deletion decision 18 |
| legacy crop-field conductors | separate 30.48 m AGL wire set | use charted W4 at 18 m minimum AGL | keep exactly five authored wire spans; no hidden duplicate hazard |
| legacy Gateway-style river arch | Landmarks.Arch duplicated across plateaus | one target arch at (0.5,-6.7) | not on the dropped list; preserve audited fly-through feature |
| Coast transition band | 0.35 | 0.1 | 100 m shore fade keeps locked Catalina runway and its 60 m shoulders at true elevation; compact smooth transition still reaches sea bed continuously |
| AP9 fictional elevation target | 600 | 3200 | reduce mean field-to-field grades while keeping supplied position, dimensions and surface |
| AP14 fictional elevation target | 250 | 1600 | reduce mean field-to-field grades while keeping supplied position, dimensions and surface |
| AP4 fictional elevation target | 180 | 1600 | reduce mean field-to-field grades while keeping supplied position, dimensions and surface |
| AP10 fictional elevation target | 5200 | 4500 | reduce mean field-to-field grades while keeping supplied position, dimensions and surface |
| AP2 Talkeetna village centre | [-7, 4.7] | [-6.9, 6.8] | separate village strip from PATK while retaining Talkeetna regional identity; new shore/approach clearance remains to be checked |
| KMEV 17/35 centre | [-3.17, -1.73] | [-3.02, -1.73] | 150 m east shift increases Lake Hood edge-to-edge run, bringing critical flat-field grade below 35%; IFR records unchanged |
| KMEV 13/31 centre | [-2.5, -2.02] | [-2.35, -2.02] | 150 m east shift increases Lake Hood edge-to-edge run, bringing critical flat-field grade below 35%; IFR records unchanged |
| KMEV 13G/31G centre | [-2.2, -1.9] | [-2.05, -1.9] | 150 m east shift increases Lake Hood edge-to-edge run, bringing critical flat-field grade below 35%; IFR records unchanged |
| PHDH east beach coast vertices | [[6.4, 6.2], [7.7, 4.5]] | [[6.6, 6.4], [7.9, 4.5]] | add 0.2 km of coastal apron near east runway end for 300 m shore setback |
| Legacy CombatZone targets | {'five_open_ground_bullseyes_radius_m': 30, 'one_gorge_floor_target_radius_m': 14, 'three_orbiting_drones': True} | six target rafts and three-drone orbit per active CZ; exact positions in each zone | preserve gunnery over the new over-water sectors without creating new land; Claude adapts target water/altitude handling |
| Box labels | {'water_floor_ceiling_ft': [330, 3610], 'AB3_floor_ceiling_ft': [5330, 8610]} | {'water_floor_ceiling_ft': [328, 3609], 'AB3_floor_ceiling_ft': [5328, 8609]} | label conversions rounded to nearest foot from locked 100 m / 1100 m AGL dimensions; no change to box dimensions |
| RACE element geometry | [{'sequence': 1, 'sim_xy_m': [45, 1800], 'heading_true_deg': 0}, {'sequence': 2, 'sim_xy_m': [345, 1800], 'heading_true_deg': 0}, {'sequence': 3, 'sim_xy_m': [540, 1755], 'heading_true_deg': 0}, {'sequence': 4, 'sim_xy_m': [720, 1845], 'heading_true_deg': 0}, {'sequence': 5, 'sim_xy_m': [900, 1755], 'heading_true_deg': 0}, {'sequence': 6, 'sim_xy_m': [1050, 1800], 'heading_true_deg': 0}, {'sequence': 7, 'sim_xy_m': [1110, 1920], 'heading_true_deg': 90}, {'sequence': 8, 'sim_xy_m': [1020, 2050], 'heading_true_deg': 180}, {'sequence': 9, 'sim_xy_m': [720, 2050], 'heading_true_deg': 180}, {'sequence': 10, 'sim_xy_m': [540, 2095], 'heading_true_deg': 180}, {'sequence': 11, 'sim_xy_m': [360, 2005], 'heading_true_deg': 180}, {'sequence': 12, 'sim_xy_m': [120, 2050], 'heading_true_deg': 180}] | [{'sequence': 1, 'centre': [6.4675, -0.175], 'heading_true_deg': 90}, {'sequence': 2, 'centre': [6.7675, -0.175], 'heading_true_deg': 90}, {'sequence': 3, 'centre': [6.9625, -0.13], 'heading_true_deg': 90}, {'sequence': 4, 'centre': [7.1425, -0.22], 'heading_true_deg': 90}, {'sequence': 5, 'centre': [7.3225, -0.13], 'heading_true_deg': 90}, {'sequence': 6, 'centre': [7.4725, -0.175], 'heading_true_deg': 90}, {'sequence': 7, 'centre': [7.5325, -0.295], 'heading_true_deg': 180}, {'sequence': 8, 'centre': [7.4425, -0.425], 'heading_true_deg': 270}, {'sequence': 9, 'centre': [7.1425, -0.425], 'heading_true_deg': 270}, {'sequence': 10, 'centre': [6.9625, -0.47], 'heading_true_deg': 270}, {'sequence': 11, 'centre': [6.7825, -0.38], 'heading_true_deg': 270}, {'sequence': 12, 'centre': [6.5425, -0.425], 'heading_true_deg': 270}] | preserve all 12 gates/chicane/turn sequence; rotate course 90 true and move to eastern island; dimensions retained |
| AP9 Turnagain bench refinement | {'centre': [-6.6, -5], 'elevation_ft': 3200} | {'centre': [-5.7, -6], 'elevation_ft': 2900} | move bench southeast to obtain enough horizontal run from both PANC and Drake; keep gravel surface,1600 ft length and heading |
| AP4 fictional elevation refinement | 1600 | 1350 | keep PHDH-to-ranch padded-edge mean grade within35%; AP11 relationship remains an explicit fit conflict |
| Height-field composition refinement | coast-derived4600ft interior base; glacier cuts can replace heads; full Drake pad flattens hill | Mokuleia1800ft regional base; fade glacier head claim; reassert authored peaks; exterior-only Drake terrain pad | honour all seven authored summit targets and preserve the hidden chamber rock envelope; interior floor belongs to3D shell, not an open trench |
| ST-PHDH practice reference line | [[4.39, 6.5], [5.59, 6.5]] | [[4.389368, 5.869048], [5.589186, 5.848105]] | align reference line with flat runway shoulder30m outside surface edge, avoiding steep arbitrary cross-country road; use buoy reference at PALH |
| ST-PANC practice reference line | [[-9.24, -2.47], [-8.04, -2.47]] | [[-8.853711, -2.506098], [-8.543128, -3.665209]] | align reference line with flat runway shoulder30m outside surface edge, avoiding steep arbitrary cross-country road; use buoy reference at PALH |
| ST-PALH practice reference line | [[-8.56, -1.2], [-7.36, -1.2]] | [[-8.56, -1.858651], [-7.36, -1.858651]] | align reference line with flat runway shoulder30m outside surface edge, avoiding steep arbitrary cross-country road; use buoy reference at PALH |
| ST-PATK practice reference line | [[-6.94, 5.59], [-5.74, 5.59]] | [[-6.536164, 4.513748], [-6.070007, 5.428634]] | align reference line with flat runway shoulder30m outside surface edge, avoiding steep arbitrary cross-country road; use buoy reference at PALH |
| ST-KSNA practice reference line | [[0.36, -7.46], [1.56, -7.46]] | [[0.72499, -8.614585], [1.288356, -7.555048]] | align reference line with flat runway shoulder30m outside surface edge, avoiding steep arbitrary cross-country road; use buoy reference at PALH |
| ST-KMYL practice reference line | [[-1.18, 3.38], [0.02, 3.38]] | [[-0.631895, 3.379186], [-0.610952, 2.179368]] | align reference line with flat runway shoulder30m outside surface edge, avoiding steep arbitrary cross-country road; use buoy reference at PALH |
| ST-3U2 practice reference line | [[1.22, -0.26], [2.42, -0.26]] | [[1.871868, -0.361736], [1.664722, -1.336284]] | align reference line with flat runway shoulder30m outside surface edge, avoiding steep arbitrary cross-country road; use buoy reference at PALH |
| ST-KMEV practice reference line | [[-3.62, -1.13], [-2.42, -1.13]] | [[-3.06524, -1.13], [-3.06524, -2.33]] | align reference line with flat runway shoulder30m outside surface edge, avoiding steep arbitrary cross-country road; use buoy reference at PALH |
| ST-KPGA practice reference line | [[3.14, -2.66], [4.34, -2.66]] | [[3.583754, -2.678294], [3.792132, -3.860064]] | align reference line with flat runway shoulder30m outside surface edge, avoiding steep arbitrary cross-country road; use buoy reference at PALH |

## Open for Ty

1. CZ4 remains optional and disabled until Ty decides; placeholder MOA names require official name check before chart publication.
2. AP8 magnetic order corrected to 36/18; approve correction or explain intended runway convention.
3. Spacing/grade conflicts remain in supplied airpark and legacy backcountry allocation. AP2 moved to (-6.9,6.8), four fictional airpark elevations adjusted and KMEV group shifted 150 m east; approve those proposals. IFR remains Claude-owned and unchanged.
4. Strict 20:1 from every runway end conflicts with one-way terrain and Drake cliff/hologram. Clarify exceptions; no locked geometry silently changed.
5. Drake 5750 ft peak and 4700 ft valley differ by 1050 ft / 320.04 m; 320 m relief is rounded. Resolve crown centre (-4.9,-4.5) versus DRK (-4.9,-4.3) and 60 m roof cover near dome edges in detailed shell design.
6. PALH gravel elevation unspecified: provisionally 79 ft same as water lane; needs true field datum confirmation.
7. 3U2 slope 0.5% is a target pending real longitudinal survey; KMEV glider strip dimensions are targets.
8. T201 fix coordinates and non-KSNA missed/hold geometries are not supplied: full zone/airway clearance cannot be established; Claude must supply final geometry.
9. 100 m design bake is not flight-certified or an engine import; source analytic API has no baked format. Fine terrain/solid continuity, cave roofs, forest/buildings and loaded-trainer climb validation await Phase 1B and Claude.
10. Initial field centres and true headings do not always reproduce stated IFR lines exactly (e.g. KSNA supplied threshold); IFR data remains unchanged; Claude reconciles geometry after approval.
11. Review fit-check failures and decide whether to authorize a revised layout at G1a; this draft is not a passing world or G1 approval.
12. Gunnery targets are now over water: six moored bullseye rafts per active zone. Claude must adapt ground-hit height logic and drone/cloud placement; no land added in MOAs.
13. Drake interior cannot be validated by a single upper-surface bake. The height-aware cave floor/deck and collision routing must preserve the roof while allowing underground flight; Phase1B shell validation required.
14. AB1 judges line south of the offshore square is still over water; a literal shore marker on that line may not intersect the authored beach. Confirm a shore-aligned extension/reference marker rather than moving the locked box dimensions.

## Artefacts and gate

`island-spec.json` is the single authoring source. `tools/generate_island.py` generates SVG/PNG map, elevation PNG, float32 grid + metadata, fit-check JSON and this plan. `island-source-audit.json` records repo inventory/provenance. Rebuilds must be byte-identical for deterministic artefacts. PNG rendering uses macOS AppKit via the included Swift helper; generation of grid/elevation/SVG/report is Python standard library. No engine/deploy test applies because src/ is untouched.

Stop at G1a after local commit and the exact AGENT-LOG pending line. Only Ty changing that line to APPROVED by Ty permits the next phase; this draft explicitly asks review of known failures, not an exemption from them.
