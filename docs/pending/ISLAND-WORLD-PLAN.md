# Aero Playground island world — Phase 1B

**G1 — PENDING Ty. Phase 1A.2 approved by Ty 2026-10-30 20:25 PDT at e23bb64, defaults TY1–TY8; Drake single-portal ruling supersedes TY2. Phase 1B implementation accompanies this specification.**

The 19 original grade failures and seven spacing conflicts have been addressed. Full water surfaces are included, so a cut lake bed cannot hide an obstruction above it. Every supplied final is checked across its whole protection fan; every known airway and proposed perimeter segment is checked across its full 2 nm terrain swath. Remaining failures and missing procedure geometry are explicit below. No failed check is converted into an approved waiver.

## Authority, frame and numerical status

Ty’s original Phase 1A brief and nine decisions dated 2026-10-30 govern. The Cursor source Page remains inaccessible; the complete brief restatement is the source of truth. The baseline is local commit `048d9cd`. No new branch, push, merge, rebase, deployment, other-worktree edit belongs to this package. Runtime world code is included for Phase 1B.

Coordinates are km east/north from Frank Church summit (0,0); true north is up. Engine mapping remains `(simX,simY,simZ)=(1000*north,1000*east,-0.3048*MSL_ft)`. Physics headings are true; published magnetic courses are true minus the locked 7° E. Supplied ICAO field elevations, runway lengths, Lake Powell 3,700 ft, glacier landing zones 1,000–1,300 ft, Drake’s 4,700 ft valley and 2,992 ft strip, pirate cave dimensions, Catalina position and the 20 km ring are retained.

Main coast extent is **[20.2, 17.6] km**, versus approximately 18.3 × 17.5 km in the locked context. This western expansion is explicitly **TY1, approved by Ty**, not silently approved. Five outer runway islands and the combat islets remain inside the same 20 km ring.

Every authored number is a **design target** unless Ty explicitly locked it. Fit-report numbers are **computed measurements/proofs**, with their stated representation limits. A 100 m review bake is not an engine-ready terrain import or flight certification.

## Fit check — baseline to revision

| Rule | 1A result | Current 1B evidence | Computed evidence |
| --- | --- | --- | --- |
| 1 | PASS sampled | PASS design geometry | 51 complete pad rectangles; 0 incompatible intersections; 0 evaluator pad errors. Full planes/60m shoulders are analytic authoritative detail; inverse-distance exterior pad blend is continuous and tends to own plane at edge. Pirate cave full pad/flight-volume shell proof: PASS analytic shell. Drake full chamber/roof/pad analytic proof: PASS analytic design. Runtime cavity/solid checks are recorded separately in island-runtime-solids.json and island-runtime-checks.json. |
| 2 | FAIL | PASS | Exact padded-rectangle edge distances for every flat-field pair: 0 above locked 35%; beaches and all outer runways included. Mean endpoint feasibility, not global peak/canyon slope cap. |
| 3 | FAIL | UNVERIFIED | Full clipped-triangle sweeps of 113 fans, including all 12 supplied finals: 0/12 supplied and 0 total terrain/pad failures; 0 tower/wire failures or conservative bounds. Missing non-KSNA miss/hold geometry and future building/tree solids prevent full rule certification. |
| 4 | FAIL | PASS known geometry / UNVERIFIED T201 | 0 of 23 known segments below 2000 ft; full waterplanes + 2 nm swath + rounded circumscribed32-gon endcaps, all grid triangles and pads/towers. Nine proposed perimeter segments included with2nm terrain swaths. T201 positions/MEAs absent; built-solid screening is recorded separately in island-runtime-solids.json. |
| 5 | PASS footprint / UNVERIFIED flight volumes | UNVERIFIED | Full analytic radius bounds for 378 authored footprints/flight volumes: 0 outside 20 km. Missing IFR hold/miss/T201 volumes prevent whole-rule verification; targets are not accepted as proofs. |
| 6 | FAIL | PASS | All 51 runways/strips including beaches/outer islands: 0 spacing conflicts, 0 shore-setback violations; only PANC/PALH spacing waiver used. |
| 7 | PASS target / UNVERIFIED flight | PASS profile / UNVERIFIED flight | Entire post-cut piecewise-linear profiles checked againstC172-like1043kg/134000W loaded sustainedROC bound 2579fpm (100%efficiency/no drag/no DA loss),50kt conservative ground speed and30m entry clearance (80kt profile rates also shown). L1 5722fpm, L2 8862fpm, L3 6865fpm, L4 8323fpm. Physical flight/zoom/turnback certification requires later build. |
| 8 | UNVERIFIED | PASS known geometry / UNVERIFIED missing IFR | Full polygon/segment boundary distances, complete clipped-grid areas and rounded2nm airway swaths; 150m box/final guard. 0 box, 0 zone and 0 combat-feature failures. Sector exclusions preserve2.05km; own airport corridors have5000ft combat floor above3000ft traffic. Missing IFR misses/holds/T201 remain UNVERIFIED. |
| 9 | PASS generated | PASS deterministic algorithm | Fixed authored seeds, no random calls. External two-generation byte comparison recorded in island-determinism.json and checked by verify_island.py. |
| 10 | PASS design / UNVERIFIED integration | PASS shared runtime evaluator / UNVERIFIED player | One deterministic height authoring evaluator/bake drives grid/elevation/full-area tests; explicit analytic pad/cave detail. WorldTerrain and IslandWorldBuilder use the same C# evaluator; numerical parity and contact probes recorded separately. Unity player/render validation remains pending sandbox build. |

Full machine-readable evidence, active MOA polygon unions, exclusion masks, raised-floor masks, cave proofs and individual field results: [island-fit-check.json](island-fit-check.json).

Flat-field pairs checked: **854**. Maximum edge-to-edge mean grade: **34.954359%**, PHDH 8/26, AP11. All padded rectangles, including outer runways, six beaches and AP8’s float lane, are included. The 35% cap applies between flat fields, not peaks/canyon walls or explicitly sloped strips.

## Remaining approach failures and unresolved geometry

| Final/end | Terrain penetration m | Pad / water penetration m | Limiting pad |
| --- | --- | --- | --- |

No known terrain/pad penetration remains.

| Final | Obstacle | Penetration/bound m |
| --- | --- | --- |

Drake is **ONE WAY IN / ONE WAY OUT** through its single north portal. Only operational end A is screened; there is no reverse final, exemption or second portal. KSNA conflicts are resolved by the approved Page/Powell/backcountry layout.

Missing: BALBO hold exact speed/turn geometry (19.2 km supplied target only); KSNA missed exact curved boundary; T201 complete route protection; other missed approaches/holds; KAVX circling. No full clearance/ring pass is claimed for geometry that was never supplied. All of the supplied `ifr` data remains semantically identical to the baseline; Claude owns the final procedure geometry and computed minima.

## Height field, landforms, water and continuity

One standard-library Python authoring evaluator supplies the review grid and both chart/elevation annotations. The grid is 401 × 401, 100 m spacing, little-endian float32 metres MSL, rows south to north, columns west to east. Interpolation uses fixed SW–NE triangle diagonals. Analytic pad planes and cavity decks are detail from the same authoring definition; roofs/arches/bridges become height-aware WorldSolids, not a second ground field. Runtime integration uses the compiled same-source analytic evaluator, with shared sim/render height queries.

Outer-island contributions fade independently into the ocean and combine by continuous maxima. Pad exterior blends use inverse-distance weights and tend exactly to each plane at its boundary. Finite influence weights fade to zero; only Drake’s named north cliff is intentionally vertical. The ocean continues through the ring with a 19.5–20 km visual fade and no collision wall. Broad arrival/airway cuts are a design prototype for valleys and terraces; the built renderer adds regional materials and procedural geometry.

| Peak | Centre E,N km | Top ft MSL | Envelope radius km |
| --- | --- | --- | --- |
| Frank Church | [0, 0] | 8072.2 | 2.0 |
| Sierra | [-1.6, -0.3] | 7328.7 | 1.3 |
| Chugach | [-3.8, -1.8] | 5914.9 | 1.6 |
| Fairweather | [-0.5, 6.3] | 5187.0 | 1.5 |
| Mokuleia | [5.8, 3.3] | 2950 | 1.5 |
| Catalina | [2.9, -13.7] | 2100 | 0.8 |
| Drake Hill | [-4.9, -4.5] | 5750 | 0.7 |

| Crest | Path E,N km | Top ft | Width km |
| --- | --- | --- | --- |
| Mokuleia | [[3.17, 4.99], [4.6, 4.4], [6.05, 3.74]] | 2950 | 0.5 |
| Sierra | [[-1.1, 0.3], [-1.54, -0.58], [-2.2, -2]] | 7550 | 0.5 |
| Chugach-Denali | [[-5.6, -2.8], [-5.4, -1.4], [-5.3, -0.6], [-5, 1.4], [-4.2, 3.4], [-3.46, 5.86]] | 7000 | 0.5 |
| Summit | [[-0.9, 1], [0, 0], [1, 0.6], [0.4, -1.2]] | 8000 | 0.5 |

| Water | Centre E,N km | Surface ft MSL | Radii km |
| --- | --- | --- | --- |
| Payette Lake | [-0.6, 3.9] | 4900 | [0.5, 0.6] |
| Lake Hood | [-10.18, -1.8] | 79 | [0.78, 0.16] |
| Knik ice lake | [-6.95, -0.4] | 100 | [0.45, 0.25] |
| Lake Powell | [4.475, -4.525] | 3700 | [0.575, 0.625] |
| Bighorn Outlet | [0, 2.4] | 6400 | [0.4575, 0.12] |
| Bighorn Shelf | [0.3, 3.5] | 6660 | [0.4, 0.12] |
| Bighorn Larch | [0.4, 2.05] | 6600 | [0.38, 0.12] |
| Bighorn Hidden | [0.2, 2.95] | 7380 | [0.275, 0.12] |
| Bighorn Cirque | [0.3, 0.1] | 7580 | [0.23, 0.12] |
| Bighorn Crown | [0.7, 3.2] | 7840 | [0.1525, 0.12] |

| Glacier | Path | Ski-zone centre | Ski-zone ft MSL |
| --- | --- | --- | --- |
| Knik | [[-5.4, -1.4], [-6, 0.1], [-6.5, 0.1]] | — (removed 2026-11-04: Knik River bar strip, bush_strips) | — |
| Fairweather | [[-1.2, 5.6], [-3.0, 4.15], [-3.9, 4.7]] | [-3.9, 4.7] | 1300 |

| Canyon | Path | Rim widths m | Floor widths m | Floor ft MSL |
| --- | --- | --- | --- | --- |
| C1 | [[1.05, 2.1], [1.85, 0.25], [1.82, -0.76], [0.7, -1.8]] | [1300, 900] | [250, 250] | [4900, 4900] |
| C3 | [[4.58, -5.38], [2.9, -6.3], [0, -6.72], [-1.15, -7.87], [-1.54, -9.6]] | [1400, 1400] | [350, 350] | [3700, 0] |
| C4 | [[-4.6, -0.9], [-5.7, -0.4], [-6.95, -0.4]] | [800, 800] | [300, 300] | [100, 100] |

## Airports, runway planes and shore classes

Each listed runway has 60 m shoulders; exact planes/slopes override the coarse review grid. Coastal ends require ≥0.3 km from the shore; inland fields/airparks/backcountry ends require ≥2 km. The PANC–PALH 0.8 km centre-spacing waiver is the only spacing exception. Eight original airport starts remain; PALH is a PANC float variant and every outer field is a non-start.

| Field | Magnetic numbers | Centre E,N km | True heading ° | Length × width m | ft MSL | Surface | Slope % | Shore class |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| PHDH | 8/26 | [4.99, 5.9] | 91 | 2745.334 × 22.860 | 14 | asphalt | 0 | coastal |
| PANC | 16/34 | [-10.24, -3.07] | 165 | 3311.652 × 60.960 | 151 | asphalt | 0 | coastal |
| PALH | 16W/34W | [-10.18, -1.8] | 165 | 1384.097 × 57.302 | 79 | water | 0 | coastal |
| PALH | 16/34 | [-9.5, -1.3] | 165 | 670.560 × 22.860 | 79 | gravel | 0 | coastal |
| PATK | 2/20 | [-6.34, 4.99] | 27 | 1066.800 × 22.860 | 365 | asphalt | 0 | coastal |
| KSNA | 2L/20R | [0.96, -8.06] | 28 | 1737.360 × 45.720 | 56 | asphalt | 0 | coastal |
| KSNA | 2R/20L | [1.2, -8.16] | 28 | 879.653 × 22.860 | 56 | asphalt | 0 | coastal |
| KMYL | 17/35 | [-0.88, 2.78] | 179 | 1859.585 × 22.860 | 5024 | asphalt | 0 | inland |
| 3U2 | 19/1 | [1.82, -0.76] | 192 | 1036.320 × 45.720 | 4960 | turf | 0.5 | inland |
| KMEV | 17/35 | [-3.02, -1.73] | 180 | 2255.215 × 30.480 | 4724 | asphalt | 0 | inland |
| KMEV | 13/31 | [-2.35, -2.02] | 135 | 1615.135 × 22.860 | 4724 | asphalt | 0 | inland |
| KMEV | 13G/31G | [-2.05, -1.9] | 135 | 609.600 × 22.860 | 4724 | grass | 0 | inland |
| KPGA | 16/34 | [5.79, -1.06] | 170 | 1813.560 × 45.720 | 4317 | asphalt | 0 | inland |
| KPGA | 10/28 | [5.95, -0.3] | 102 | 670.865 × 22.860 | 4317 | asphalt | 0 | inland |
| KAVX | 5/23 | [3.57, -14.36] | 55 | 914.400 × 22.860 | 1602 | asphalt | 0 | coastal |
| DRAKE | 17 | [-4.9, -4.18] | 180 | 911.962 × 30.480 | 4700 | grass | 0 | inland |
| SITX | 35/17 | [-15, 1.1] | 0 | 1100.000 × 30.000 | 30 | asphalt | 0 | coastal |
| TERN | 35/17 | [11.3, 2] | 0 | 900.000 × 30.000 | 45 | gravel | 0 | coastal |
| NHVN | 8/26 | [0, 10.3] | 90 | 900.000 × 30.000 | 55 | gravel | 0 | coastal |
| KITE | 11/29 | [8, 15.3] | 118 | 750.000 × 24.000 | 50 | gravel | 0 | coastal |
| RIME | 5/23 | [-9.5, 14.8] | 56 | 750.000 × 24.000 | 70 | gravel | 0 | coastal |

## Airparks and retained backcountry strips

| ID/name | Centre E,N km | True ° / magnetic numbers | Length × width ft | ft MSL | Surface / gear | Slope % | Shore class |
| --- | --- | --- | --- | --- | --- | --- | --- |
| AP1 Payette | [-1.6, 4.6] | 90 / 8/26 | 2800 × 60 | 4925 | grass; wheels or tailwheel | 0 | inland |
| AP2 Talkeetna village | [-6.9, 6.6] | 17 / 1/19 | 1700 × 60 | 365 | gravel; tundra tires | 0 | coastal |
| AP3 Carson Valley | [-2, -3.9] | 164 / 16/34 | 2400 × 60 | 4700 | grass; wheels | 0 | inland |
| AP4 Mokuleia ranch | [2.3, 6.7] | 144 / 14/32 | 1800 × 60 | 900 | grass; tailwheel | 0 | coastal |
| AP5 Windward bluff | [7.2, 3] | 335 / 33/15 | 1500 × 60 | 1200 | grass; STOL | 0 | coastal |
| AP6 Upcountry ranch | [5.1, 4.3] | 110 / 10/28 | 2000 × 60 | 2600 | grass; tailwheel | 3 | inland |
| AP7 Point Mac | [-8.6, 1.2] | 94 / 9/27 | 2000 × 60 | 180 | gravel; tundra tires | 0 | coastal |
| AP8 Willow-style | [-8.2, 3.6] | 7 / 36/18 | 2600 × 60 | 250 | grass; wheels or floats | 0 | coastal |
| AP9 Turnagain bench | [-5.7, -6] | 103 / 10/28 | 1600 × 60 | 3200 | gravel; tundra tires | 0 | coastal |
| AP10 Yellow Pine | [1.8, 1.5] | 25 / 2/20 | 2800 × 60 | 4500 | gravel; wheels | 0 | inland |
| AP11 Payette east shore | [0, 4.6] | 122 / 12/30 | 1900 × 60 | 4035 | gravel; tundra tires | 0 | inland |
| AP12 Bullfrog | [1.4, -3.2] | 65 / 06/24 | 2500 × 60 | 4300 | gravel; wheels; floats at marina | 0 | inland |
| AP13 Navajo mesa | [5.5, -5] | 17 / 1/19 | 2000 × 60 | 4000 | dirt; tundra tires | 0 | coastal |
| AP14 Trabuco ranch | [-2.4, -7.6] | 75 / 7/25 | 2200 × 60 | 1600 | grass; wheels | 0 | coastal |

All 14 retain hangars, houses, windsocks and seeded parked aircraft with the explicit counts/setbacks in the spec. AP8 also has a separate **600 × 120 m water lane at 250 ft**, true 000°, with 60 m shoulders and a 3 m bed offset; it receives the same fit rules as every other strip.

| Strip | Centre E,N km | True heading ° | Length × width m | ft MSL | Slope % | Surface | One-way true ° |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Chamberlain | [0.7, -1.8] | 31 | 1100 × 40 | 4800 | 0.0 | gravel | None |
| Big Creek | [2.5, -0.3] | 0 | 1070 × 34 | 4800 | 0.8 | gravel | None |
| Thomas Creek | [4.4, 0.65] | 98 | 1220 × 30 | 5000 | 1.2 | gravel | None |
| Sulphur Creek | [3.4, -0.4] | 70 | 915 × 30 | 4830 | 0.0 | turf | None |
| Soldier Bar | [4, 1.35] | 86 | 400 × 20 | 4400 | 1.5 | dirt | 86 |
| Cabin Creek | [3.1, 0.55] | 179 | 460 × 20 | 4700 | 3.0 | dirt | 179 |
| Dewey Moore | [-0.8, -2.8] | 215 | 370 × 18 | 4600 | 4.0 | dirt | 215 |
| Indian Creek | [3.5, 2] | 47 | 1220 × 30 | 4500 | 1.8 | gravel | None |
| Mackay Bar | [5.1, 1.3] | 330 | 460 × 24 | 4500 | 1.8 | gravel | None |

## Five outer runway islands, Sitka and navaid sites

| Island | Airport | Base ft MSL | Coast polygon E,N km |
| --- | --- | --- | --- |
| OI1 Sitka Sound | SITX | 60 | [[-15.7,-0.8],[-15.7,0.55],[-15.2,0.9],[-15.2,1.3],[-15.7,1.65],[-15.7,3.9],[-14.7,4.7],[-12.4,4.5],[-12.1,2.5],[-13.1,2.1],[-13.7,1.6],[-14.8,1.25],[-14.8,0.95],[-13.3,0.9],[-12.5,1],[-12.7,-0.6],[-14,-1.2]] |
| OI2 Tern Island | TERN | 180 | [[10.6,0.3],[10.6,4],[11.5,4.6],[17.4,4.3],[18.2,3.5],[18.2,0.3],[13,-0.3],[11.4,-0.2]] |
| OI3 North Haven | NHVN | 120 | [[-1.3,8.8],[-1.3,12.1],[-0.4,12.7],[1.4,12.2],[1.4,9],[0.5,8.6]] |
| OI4 Kite Anchorage | KITE | 50 | [[9.059537111430712,14.736634124656932],[9.007577789750602,14.660780346759742],[8.938378532734522,14.595863794354662],[8.853123358855669,14.542995207651611],[8.753271007776318,14.503079182827022],[8.640529980907736,14.476798694132455],[8.516829308477357,14.464603408015801],[8.38428554328678,14.46670198920369],[8.245166545906484,14.48305853039016],[8.101852680952717,14.513393166620622],[7.956796088388952,14.557186863858819],[7.812478726731241,14.613690299803103],[7.671369906049876,14.681936684998751],[7.535884037389474,14.760758304873598],[7.408339321526593,14.848806499658098],[7.2909180839117225,14.944574740327921],[7.18562943447577,15.04642440573323],[7.094274891202115,15.152612819860883],[7.018417555654432,15.26132306950416],[6.95935536787557,15.370695092151493],[6.918098898273558,15.478857502171731],[6.8953540564817395,15.583959610741143],[6.891510013049234,15.684203091642297],[6.906632540624942,15.77787275112423],[6.940462888569288,15.863365875343069],[6.992422210249398,15.93921965324026],[7.061621467265479,16.00413620564534],[7.146876641144329,16.05700479234839],[7.246728992223681,16.09692081717298],[7.3594700190922655,16.123201305867546],[7.483170691522641,16.1353965919842],[7.61571445671322,16.133298010796313],[7.754833454093518,16.11694146960984],[7.8981473190472835,16.08660683337938],[8.043203911611048,16.04281313614118],[8.187521273268759,15.986309700196898],[8.328630093950123,15.91806331500125],[8.464115962610526,15.839241695126404],[8.591660678473406,15.751193500341904],[8.709081916088277,15.655425259672082],[8.81437056552423,15.553575594266771],[8.905725108797885,15.44738718013912],[8.981582444345568,15.338676930495843],[9.040644632124428,15.229304907848508],[9.081901101726443,15.12114249782827],[9.104645943518262,15.016040389258858],[9.108489986950767,14.915796908357706],[9.093367459375056,14.822127248875772]] |
| OI5 Rime Harbour | RIME | 70 | [[-8.50515491293395,15.471031484164897],[-8.455274494240175,15.378721822752956],[-8.423269618602582,15.276510070501713],[-8.409687898316758,15.166145098830631],[-8.41476172038236,15.04951528300396],[-8.438404270297115,14.928616191506384],[-8.480211017477027,14.805516441312989],[-8.539466636886829,14.682322303279282],[-8.615157248449657,14.561141663262887],[-8.705987764816271,14.444047955612108],[-8.810404050668659,14.333044686129357],[-8.926619514406225,14.230031151531405],[-9.052645677223403,14.136769941955967],[-9.186326196533338,14.054856782555776],[-9.325373761587397,13.985693230198786],[-9.467409229996235,13.930462692441582],[-9.610002335515707,13.890110179097992],[-9.750713270576096,13.865326132859153],[-9.887134432066079,13.856534615627645],[-10.016931616089465,13.863886052700773],[-10.137883956841042,13.88725465895201],[-10.247921926236224,13.926240591049394],[-10.345162744110372,13.980176788885796],[-10.427942593109407,14.0481403891623],[-10.49484508706605,14.128968515835105],[-10.544725505759825,14.221278177247045],[-10.576730381397418,14.323489929498288],[-10.590312101683242,14.433854901169369],[-10.58523827961764,14.550484716996042],[-10.561595729702885,14.671383808493617],[-10.519788982522973,14.794483558687013],[-10.460533363113171,14.91767769672072],[-10.384842751550341,15.038858336737114],[-10.294012235183729,15.155952044387893],[-10.189595949331341,15.266955313870644],[-10.073380485593775,15.369968848468597],[-9.947354322776597,15.463230058044035],[-9.813673803466662,15.545143217444224],[-9.674626238412603,15.614306769801216],[-9.532590770003765,15.66953730755842],[-9.389997664484293,15.70988982090201],[-9.249286729423904,15.734673867140849],[-9.112865567933921,15.743465384372357],[-8.983068383910535,15.736113947299229],[-8.862116043158958,15.712745341047992],[-8.752078073763776,15.673759408950609],[-8.65483725588963,15.619823211114205],[-8.572057406890593,15.5518596108377]] |

**Sitka Sound** is near Lake Hood on the west perimeter, with twelve tiny surrounding islands and a sheltered east-facing cove whose head reaches the airport fill. SITX is an actual waterfront causeway: water lies 200 m either side of its centreline at the midsection, and broad end lobes preserve the 300 m end setback. The causeway is visible in the chart inset.

The symmetric stratovolcano is centred at [-13.35, 3.2], radius 0.85 km, with a 2600 ft summit rim, 2250 ft crater floor and 280 m crater diameter. PASS analytic radial model: whole cone disk coast/fade margin 0.164172 km; final-cut margin 0.01975 km. Whole-footprint exclusion, rather than radial samples, proves that final/airway cuts do not deform it.

| Site | Position E,N km | ft MSL | Computed ft MSL |
| --- | --- | --- | --- |
| SITE-SITX | [-15.05, 1.1] | 30 | 30.0 |
| SITE-TERN | [12.1, 2] | 180 | 180.0 |
| SITE-NHVN | [0, 10.65] | 120 | 120.0 |
| SITE-KITE | [8.05, 15.3] | 50 | 50.0 |
| SITE-RIME | [-9.45, 14.8] | 70 | 70.0 |

These are **sites only**. Claude assigns equipment, idents, frequencies and procedures. Proposed perimeter traffic uses the path [[-15,1.1],[-13,-5.5],[-8,-10],[0.5,-11],[8.8,-9],[11.3,2],[8,8],[0,10.3],[-7,9],[-15,1.1]], 350 m flight half-width and full 2 nm terrain screening. Segment MEA targets are [6000, 6000, 6000, 6000, 6000, 6000, 11000, 11000, 6000] ft; northern segments rise to 11,000 ft to clear Denali. This proposal does not alter or publish T201.

## Landable hard-sand beaches

| ID/island | Position E,N km | True heading ° | Usable length × width m | ft MSL | Slope % | Surface |
| --- | --- | --- | --- | --- | --- | --- |
| B1 / main | [2.8, -8.5] | 91 | 350 × 35 | 12 | 0 | hard sand |
| B2 / main | [-8.6, 5.9] | 35 | 300 × 35 | 12 | 0 | hard sand |
| B3 / main | [-10.9, -5.4] | 0 | 300 × 35 | 12 | 0 | hard sand |
| B4 / OI1 | [-13.6, -0.3] | 40 | 300 × 35 | 12 | 0 | hard sand |
| B5 / OI2 | [11.3, 3.7] | 0 | 350 × 35 | 12 | 0 | hard sand |
| B6 / OI3 | [0.6, 11.8] | 90 | 300 × 35 | 12 | 0 | hard sand |

These are dry upper beach berms with flat cross-sections and explicit usable dimensions, not wet foreshore. They retain 60 m shoulders and ≥300 m shore-end setbacks and receive all field/grade/approach/spacing/ring checks.

## Aerobatic boxes and moored markers

Exactly three IAC boxes remain: two water boxes and only one high-DA box, at McCall. Each is 1,000 m square, true N–S/E–W, with floor 100 m and ceiling 1,100 m above centre ground/water. The judges line is 150 m outside the airport-facing side.

| Box | Centre | DA | Floor–ceiling ft MSL | Nearest full final km | Overhead minimum gap ft |
| --- | --- | --- | --- | --- | --- |
| AB1 Dillingham | [6.9, 7.6] | low | 328.084–3608.924 | 1.0398 | 2391.076 |
| AB2 Cook Inlet | [-12.2, -6.3] | low | 328.084–3608.924 | 0.5868 | 2391.076 |
| AB3 McCall | [-1.8, 2.8] | high | 5328.084–8608.924 | 0.4444 | 2391.076 |

**AB1**: nine moored marker rafts (one centre cross, four corner Ls, four side Ts), **one additional 12 × 8 m judge raft**, four orange corner buoys; ten rafts total. Cross arms are 40 × 4 m; L/T legs are 30 × 4 m. Every raft is at 0.5 m MSL.

| Raft / shape | Position E,N km |
| --- | --- |
| AB1-R1 / cross | [6.9, 7.6] |
| AB1-R2 / L | [6.4, 7.1] |
| AB1-R3 / L | [6.4, 8.1] |
| AB1-R4 / L | [7.4, 7.1] |
| AB1-R5 / L | [7.4, 8.1] |
| AB1-R6 / T | [6.9, 8.1] |
| AB1-R7 / T | [6.9, 7.1] |
| AB1-R8 / T | [7.4, 7.6] |
| AB1-R9 / T | [6.4, 7.6] |
| AB1-JR / judge | [6.9, 6.95] |

Corner buoys: [[6.4,7.1],[6.4,8.1],[7.4,7.1],[7.4,8.1]]. Dry shore marker: [3.1,6.95], on the actual extended judges-line axis. Whole raft and marker bounds are verified in the fit report.

**AB2**: nine moored marker rafts (one centre cross, four corner Ls, four side Ts), **one additional 12 × 8 m judge raft**, four orange corner buoys; ten rafts total. Cross arms are 40 × 4 m; L/T legs are 30 × 4 m. Every raft is at 0.5 m MSL.

| Raft / shape | Position E,N km |
| --- | --- |
| AB2-R1 / cross | [-12.2, -6.3] |
| AB2-R2 / L | [-12.7, -6.8] |
| AB2-R3 / L | [-12.7, -5.8] |
| AB2-R4 / L | [-11.7, -6.8] |
| AB2-R5 / L | [-11.7, -5.8] |
| AB2-R6 / T | [-12.2, -5.8] |
| AB2-R7 / T | [-12.2, -6.8] |
| AB2-R8 / T | [-11.7, -6.3] |
| AB2-R9 / T | [-12.7, -6.3] |
| AB2-JR / judge | [-11.55, -6.3] |

Corner buoys: [[-12.7,-6.8],[-12.7,-5.8],[-11.7,-6.8],[-11.7,-5.8]]. Dry shore marker: [-11.55,-5.65], on the actual extended judges-line axis. Whole raft and marker bounds are verified in the fit report.

## Combat zones, land MOA and airport access

All zones are **HOT by default**, selectable COLD, with no automatic timed schedule. On entry into the active **3D** HOT volume, that zone’s targets and drones appear; COLD entry produces neither. The active footprint is the polygon union after charted exclusions. Traffic beneath a raised MOA floor does not enter combat airspace. Claude implements those hooks later. Ground targets are fixtures below the aircraft floor; flight/drone volumes remain within the stated floors/ceilings.

| Zone | Reference boundary E,N or sector | Base floor–ceiling ft MSL | Computed floor AGL ft | Computed ceiling AGL ft | Full final / airway minimum km |
| --- | --- | --- | --- | --- | --- |
| CZ1 AP KAIWI MOA | {"bearings_true_deg":[15,50],"radii_km":[14,19.5]} | 150–12500 | [40, 150] | [12390, 12500] | 3.5775 / 2.05 |
| CZ2 AP PAINTED MOA | {"bearings_true_deg":[95,135],"radii_km":[13.5,19.5]} | 150–12500 | [40, 150] | [12390, 12500] | 5.1328 / 2.05 |
| CZ3 AP SUSITNA MOA | {"bearings_true_deg":[315,350],"radii_km":[14.5,19.5]} | 150–12500 | [40, 150] | [12390, 12500] | 4.0135 / 2.05 |
| CZ5 AP TERN MESA MOA | [[17.1,2.2],[17.5,2.2],[17.5,2.6],[17.1,2.6]] | 200–2500 | [12, 140.0] | [2312, 2440.0] | 4.2488 / 2.0877 |

**CZ5 AP TERN MESA MOA** is a 0.16 km² polygon entirely over Tern Island. Its four vertices, in order, are [[17.1,2.2],[17.5,2.2],[17.5,2.6],[17.1,2.6]]. It has **no airport/approach cut-out** and stays at least **2.05 km** from the full known final and airway boundaries, including the proposed perimeter’s full 2 nm terrain swath. It is separate from the airport-over-MOA arrangements.

| MOA / airport | Local MOA floor–ceiling ft MSL | Airport ft / pattern ft | Traffic ceiling ft | Vertical buffer ft | Entire island under active zone |
| --- | --- | --- | --- | --- | --- |
| CZ1 / KITE | 5000–12500 | 50 / 1050 | 3000 | 2000 | True |
| CZ3 / RIME | 5000–12500 | 70 / 1070 | 3000 | 2000 | False |

Kite’s whole island and both airport centres lie beneath active combat airspace. Local protection covers complete two-end 4 nm final fans plus 300 m lateral guard and a 1.2 km pattern disk. Arrivals/departures stay at or below 3,000 ft until outside these reservations; combat starts at 5,000 ft, leaving **2,000 ft vertical separation**. No low-floor target, drone orbit or feature flight tube intersects those masks. Outside own-airport vertical reservations, zones have 2.05 km horizontal exclusions around every known final and full airway swath.

The magenta hatch is active airspace; gaps are exclusions, and pale violet shows raised-floor traffic reservations. The thin original sector outline is a chart reference, not permission to engage within a cut-out. Exact active polygon unions are in the fit report. The 100-cell polygonal sector definition is authoritative for this review; radial arc difference is under 0.12 m.

The names are fictional simulator names. Susitna’s real-world name collision was confirmed in the [FAA Alaska chart supplement](https://aeronav.faa.gov/afd/02OCT2025/AK_rear_02OCT2025.pdf); the AP prefix distinguishes this world from real SUA.

## Combat terrain, structures, targets and drones

**CZ1 AP KAIWI MOA**

| Feature | Type / position | Dimensions and clearances |
| --- | --- | --- |
| CZ1-CHANNEL | island cluster and channel / [9.3,16.0] | {"channel_width_m":300,"channel_length_m":1100,"water_msl_ft":0,"flight_half_width_m":40,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"terrain":"five islands with smooth ridges/sea stacks, two 300m channels; hide-and-seek and strafe route"} |
| CZ1-BRIDGE | bridge / [9.3,16.0] | {"heading_true_deg":90,"span_m":300,"deck_width_m":14,"under_clearance_m":120,"under_base_msl_ft":0,"deck_thickness_m":4,"flight_opening_width_m":240,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids bridge deck/piers; no heightfield bridge deck"} |
| CZ1-ARCH | rock arch / [9.8,16.4] | {"axis_true_deg":0,"opening_width_m":100,"opening_height_m":110,"base_msl_ft":60,"rock_top_msl_ft":460,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids overhang/opening; footprint pedestals only in shared heightfield"} |

Ground targets: [{"id":"CZ1-GT1","centre":[8.8,16.4],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170},{"id":"CZ1-GT2","centre":[9.8,16.0],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170},{"id":"CZ1-GT3","centre":[9.3,16.8],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170}]. Drone reservation: {"centre_start":"CZ1","count":3,"radius_m_target":150,"altitude_ft_target":900,"owner":"Claude opponent/AI logic","centre":[9.3,16.0]}.

**CZ2 AP PAINTED MOA**

| Feature | Type / position | Dimensions and clearances |
| --- | --- | --- |
| CZ2-CHANNEL | island cluster and channel / [16.5,-7.5] | {"channel_width_m":300,"channel_length_m":1100,"water_msl_ft":0,"flight_half_width_m":40,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"terrain":"five islands with smooth ridges/sea stacks, two 300m channels; hide-and-seek and strafe route"} |
| CZ2-BRIDGE | bridge / [16.5,-7.5] | {"heading_true_deg":90,"span_m":300,"deck_width_m":14,"under_clearance_m":120,"under_base_msl_ft":0,"deck_thickness_m":4,"flight_opening_width_m":240,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids bridge deck/piers; no heightfield bridge deck"} |
| CZ2-ARCH | rock arch / [17.0,-7.1] | {"axis_true_deg":0,"opening_width_m":100,"opening_height_m":110,"base_msl_ft":60,"rock_top_msl_ft":460,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids overhang/opening; footprint pedestals only in shared heightfield"} |

Ground targets: [{"id":"CZ2-GT1","centre":[16.0,-7.5],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170},{"id":"CZ2-GT2","centre":[17.0,-7.5],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170},{"id":"CZ2-GT3","centre":[16.5,-6.7],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170}]. Drone reservation: {"centre_start":"CZ2","count":3,"radius_m_target":150,"altitude_ft_target":900,"owner":"Claude opponent/AI logic","centre":[16.5,-7.5]}.

**CZ3 AP SUSITNA MOA**

| Feature | Type / position | Dimensions and clearances |
| --- | --- | --- |
| CZ3-CHANNEL | island cluster and channel / [-7.9,17.05] | {"channel_width_m":300,"channel_length_m":600,"water_msl_ft":0,"flight_half_width_m":40,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"terrain":"five islands with smooth ridges/sea stacks, two 300m channels; hide-and-seek and strafe route"} |
| CZ3-BRIDGE | bridge / [-7.9,17.0] | {"heading_true_deg":90,"span_m":300,"deck_width_m":14,"under_clearance_m":120,"under_base_msl_ft":0,"deck_thickness_m":4,"flight_opening_width_m":240,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids bridge deck/piers; no heightfield bridge deck"} |
| CZ3-ARCH | rock arch / [-7.4,17.4] | {"axis_true_deg":0,"opening_width_m":100,"opening_height_m":110,"base_msl_ft":60,"rock_top_msl_ft":460,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids overhang/opening; footprint pedestals only in shared heightfield"} |

Ground targets: [{"id":"CZ3-GT1","centre":[-8.4,17.0],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170},{"id":"CZ3-GT2","centre":[-7.4,17.15],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170},{"id":"CZ3-GT3","centre":[-7.9,17.4],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170}]. Drone reservation: {"centre_start":"CZ3","count":3,"radius_m_target":150,"altitude_ft_target":900,"owner":"Claude opponent/AI logic","centre":[-7.9,17.0]}.

**CZ5 AP TERN MESA MOA**

| Feature | Type / position | Dimensions and clearances |
| --- | --- | --- |
| CZ5-RIDGE | paired ridges and dry canyon / [17.3,2.4] | {"axis_true_deg":0,"length_m":300,"canyon_width_m":100,"canyon_depth_m":39.0144,"terrace_floor_ft":180,"rim_ft":188,"ridge_half_width_m":35,"flight_half_width_m":20,"flight_floor_msl_ft":220,"flight_ceiling_msl_ft":300,"canyon_floor_ft":60,"canyon_floor_width_m":60} |
| CZ5-BRIDGE | bridge / [17.3,2.4] | {"heading_true_deg":90,"span_m":120,"deck_width_m":14,"under_clearance_m":100,"under_base_msl_ft":60,"deck_thickness_m":4,"flight_opening_width_m":100,"flight_floor_msl_ft":220,"flight_ceiling_msl_ft":300,"collision":"WorldSolids bridge deck/piers; no heightfield bridge deck"} |
| CZ5-ARCH | rock arch / [17.41,2.488] | {"axis_true_deg":0,"opening_width_m":60,"opening_height_m":55,"base_msl_ft":188,"rock_top_msl_ft":400,"flight_floor_msl_ft":220,"flight_ceiling_msl_ft":300,"collision":"WorldSolids overhang/opening; footprint pedestals only in shared heightfield"} |
| CZ5-STACKS | rock spire pair / [17.3,2.4] | {"stack_centres":[[17.24,2.3],[17.36,2.5]],"radius_m":10,"base_msl_ft":60,"top_msl_ft":188,"height_m":39.0144,"flight_floor_msl_ft":220,"flight_ceiling_msl_ft":300,"flight_half_width_m":20,"length_m":280,"terrain":"Two isolatederoded columns beside drycanyon; smooth radial cones insharedheightfield, no overhang."} |

Ground targets: [{"id":"CZ5-GT1","centre":[17.16,2.28],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":220},{"id":"CZ5-GT2","centre":[17.44,2.52],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":220}]. Drone reservation: {"centre_start":"CZ5","count":3,"radius_m_target":70,"altitude_ft_target":600,"owner":"Claude opponent/AI logic","centre":[17.3,2.4]}.

Each outer zone has five island/sea-stack landforms with ridges/spires, channels, a 300 m bridge span with 120 m under-clearance and a 100 × 110 m arch opening. CZ5 has a 60 ft dry-canyon floor, 188 ft rims/stacks, 39 m depth, a 120 m bridge span with 100 m under-clearance and a 60 × 55 m arch opening. Island relief, the land canyon and spires are in the common height field. Bridge decks and arch overhangs are analytic WorldSolids design bounds; their openings are not filled by a false terrain roof.

Every complete feature flight tube, target envelope and drone orbit (+20 m aircraft guard) is checked against active polygons, raised floors, analytic terrain maxima and roof clearances. Exact results: `combat_feature_checks` in the fit report.

## Caves, Drake and lesson validation

**Pirate cave** keeps its locked 60 × 26 m leeward mouth and 90 m chamber radius on Catalina. All pad corners lie inside the chamber disk. The 20 m wide entry/chamber chord is 5–23 m MSL above a 3 m flat floor, with ≥6 m throat roof clearance; wreck/driftwood bounds lie outside that strip. The chamber roof is 40 m, authored upper rock at 60 m (full coast-faded chamber bound still gives ≥15 m cover except the intentional skylight). This is a full analytic clearance proof for the stated entry volume and pads, not a claim that a trainer can make an unrestricted airborne U-turn inside the cave.

Pirate proof: `{"status":"PASS analytic shell","minimum_chamber_upper_terrain_m":56.7853,"minimum_chamber_rock_cover_m":16.7853,"throat_shell_cover_m":6,"pads":[{"pad":"landing","max_corner_radius_m":52.7376,"wall_margin_m":37.2624,"flat_floor_msl_m":3,"vertical_clearance_m":37},{"pad":"parking","max_corner_radius_m":49.2443,"wall_margin_m":40.7557,"flat_floor_msl_m":3,"vertical_clearance_m":37}],"mouth_m":[60,26],"chamber_radius_m":90,"throat_side_clearance_m":20.0,"lane_floor_margin_m":2,"lane_roof_margin_m":6,"contents":[{"id":"wreck","lateral_lane_margin_m":14.5},{"id":"beach-driftwood","lateral_lane_margin_m":12.5}],"proof":"Planar pads entirely inside circular chamber by corner radial maxima. Flight volume: 20 m wide throat tube connected to 20 m wide chamber chord inset within radius80m; no whole empty chamber disk claimed; min 2 m above floor, 6 m below throat roof. Contents lie outside full flight strip. Analytic design shell only; collider/runtime build deferred."}`

**Drake** keeps a 1,400 m hill diameter, 320 m rounded relief, 200 m north cliff, 4,700 ft floor and 2,992 ft strip. The strip moved 200 m south so the whole 820 × 420 × 140 m chamber, roof blend and 60 m cover fit inside the hill. DRK remains at (-4.9,-4.3) on the 5,750 ft crown. Exterior length is exactly 500 ft (152.4 m); interior is 759.5616 m, leaving the 60 m end shoulder inside the chamber. Interior deck/shoulders never flatten the upper rock roof.

Drake proof: `{"status":"PASS analytic design","maximum_chamber_corner_radius_m":657.9917,"hill_radius_m":700,"roof_blend_m":20,"minimum_rock_cover_m":60.0,"floor_msl_m":1432.5600000000002,"ceiling_msl_m":1572.5600000000002,"minimum_roof_msl_m":1632.5600000000002,"exterior_strip_m":152.4,"interior_strip_m":759.5616000000001,"total_strip_m":911.9616000000001,"runtime":"UNVERIFIED closed3D collision/deck routing; northcliff is explicitlyvertical and opening not expressible in uppergrid"}`

| Lesson | Fork / box path | Actual rise m | Required fpm at 80 kt | Required fpm at 50 kt | Loaded ROC upper bound fpm | Result |
| --- | --- | --- | --- | --- | --- | --- |
| L1 | [[0.6,1.7],[1.05,2.1]] | 425.239 | 5721.946 | 3576.216 | 2578.91 | PASS conservative sustained-climb profile |
| L2 | [[-4.2,-0.9],[-4.25,-1.25]] | 386.749 | 8862.158 | 5538.848 | 2578.91 | PASS conservative sustained-climb profile |
| L3 | [[2.9,-6.3],[3.14,-6.48]] | 254.216 | 6865.103 | 4290.69 | 2578.91 | PASS conservative sustained-climb profile |
| L4 | [[-4.5,4.7],[-4.5,5.1]] | 410.913 | 8322.508 | 5201.568 | 2578.91 | PASS conservative sustained-climb profile |

Each full piecewise-linear path is tested against the C172-like configuration’s loaded 1,043 kg / 134 kW engine-power ceiling, assuming 100% propeller efficiency, zero drag and no density-altitude power loss. Those deliberately generous assumptions bound sustained climb at every DA. At conservative 50 kt ground speed and 30 m entry clearance, the rising terrain overtakes that path. Runtime trajectories, zoom, turnback, atmospheric lift and collision behavior remain unverified until Ty’s flight review.

## Hazards, race, legacy features and inventory

| Tower | Position | Height AGL m | Guy footprint radius m |
| --- | --- | --- | --- |
| T1 | [4.9, 4.45] | 75 | 65.0 |
| T2 | [-1.7, -0.65] | 35 | 5 |
| T3 | [5.2, -6.3] | 180 | 149.0 |
| T4 | [-6.9, -2.9] | 50 | 45.0 |
| T5 | [-6.9, 5.4] | 55 | 49.0 |
| T6 | [6.5, -4.8] | 45 | 41.0 |

| Wire | Endpoints | Lowest AGL m | Conservative sag fraction |
| --- | --- | --- | --- |
| W1 | [[6.8, -3.8], [7.2, -3.75]] | 40 | 0.08 |
| W2 | [[5.8, -5.9], [6.1, -5.6]] | 250 | 0.08 |
| W3 | [[-6.55, -0.65], [-6.65, -0.15]] | 30 | 0.08 |
| W4 | [[5.7, -5.5], [6.1, -5.5]] | 18 | 0.08 |
| W5 | [[1.3, -9.4], [2, -9.3]] | 60 | 0.08 |

All six towers and five wires remain deterministic and charted. Complete guy footprints and conservative full-span wire upper bounds enter clearance checks. T4’s 2,700 ft knoll keeps its 50 m tower top below A2’s 3,000 ft obstruction budget.

| Feature | Position | Design details |
| --- | --- | --- |
| F1 Drake Hill and hidden school shell | [-4.9, -4.3] | {"status":"target","hill_diameter_m":1400,"relief_m":320,"north_cliff_m":200,"valley_ft":4700,"chamber_m":[820,420,140],"minimum_rock_cover_m":60,"exterior_strip_m":152.4,"interior_strip_m":759.5616000000001,"hologram":"Claude trigger; Codex visual shell"} |
| F2 Pirate cave | [2.633604444, -14.638333492] | {"status":"target","mouth_m":[60,26],"chamber_radius_m":90,"skylight_diameter_m":10,"mouth_true_deg":240,"contents":["wreck","beach","seeded driftwood"]} |
| F3 Avalon Casino harbour beach | [4.5, -13.7] | {"status":"target"} |
| F4 Catalina sea arch | [3.5, -14] | {"status":"target","opening_m":150} |
| F6 Powell arches spires dam marina ramp | [4.58, -5.15] | {"status":"target"} |
| F7 Sea stacks Dillingham beach | [6, 6.2] | {"status":"target"} |
| F11 Carson hay fields crop dusting | [-2.9, -4.4] | {"status":"target","crop_field_m":[600,300],"wire":"W4 replaces legacy field wire; no extra uncharted wire"} |
| F12 Page landable mesas rock pile strips | [5, -3.7] | {"status":"target","top_max_slope_deg":4,"clean_lane_width_m":46,"threshold_pile_spacing_m":30.48} |
| F13 Knik braided bars driftwood | [-6.7, 0.3] | {"status":"target","braids":8,"bar_raise_m":1.6,"clean_lane_m":[520,52],"chalk_line_and_cones":true} |
| F14 Middle Fork bars and strips | [3.5, 0.5] | {"status":"target"} |
| F15 Talkeetna River bars | [-7.2, 5.8] | {"status":"target"} |
| F16 OC beaches piers marina | [2.8, -9] | {"status":"target"} |
| F17 Bighorn Crags lakes | [1.2, 1.8] | {"status":"target"} |
| F18 Historic outdoor glider slope | [4.3, 4] | {"status":"target"} |
| CITY OC Fly City and city grid | [4.8, -6.6] | {"status":"target","extent_km":[1.02,2.55],"max_building_m":300,"keep":["rooftop rings","spinning ring","sky bridges","hole tower","fountain plaza","raised landing disc","street grid"],"ring_radius_m":22,"spinning_ring_radius_m":45} |
| RACE Air race pylon course | [10, -13] | {"status":"target","extent_km":[1.11,0.35],"gate_half_width_m":22.86,"pylon_height_m":75,"element_count":12,"chart":true,"platform":"moored offshore racepylons; collisiongeometryanchored atwater0MSL, no land made; allseeds retained"} |
| CARRIER Carrier off OC | [6.8, -10.5] | {"status":"target","moving_deck":true,"operating_radius_km":0.3} |
| FOREST Regional forest | [0, 2] | {"status":"target","cell_m":18,"treeline_m":2350,"max_height_m":42,"exclude":["pads","approach corridors","water","cities","canyon safe lines"]} |
| GATEWAY Gateway style river arch | [0.3, -6.5] | {"status":"target"} |
| PLAYA Page dry playa | [5.8, -4.9] | {"status":"target"} |

The race course keeps all 12 original gates/pylons and moves offshore to (10,-13), on moored bases. Whole gate/pylon enclosure final clearance: 3.963749 km; it is clear of every box and active combat footprint. Carrier stays off OC, clear of the southern LOC BC/hold reservation.

| Inventory ID | Feature/task | Destination | Owner/source |
| --- | --- | --- | --- |
| event:race | Air Racing | RACE | Codex geometry/placement; Claude event/scoring/spawn logic; SessionSettings.cs Challenges/Lessons; StartMenu dispatch |
| event:stol | STOL contest | AP3 Carson Valley; 150 m line setback, 600 ft marks, 3 rounds | Codex geometry/placement; Claude event/scoring/spawn logic; SessionSettings.cs Challenges/Lessons; StartMenu dispatch |
| event:dust | Crop dusting | F11 | Codex geometry/placement; Claude event/scoring/spawn logic; SessionSettings.cs Challenges/Lessons; StartMenu dispatch |
| event:aerobox | Aerobatic box | AB1-AB3 | Codex geometry/placement; Claude event/scoring/spawn logic; SessionSettings.cs Challenges/Lessons; StartMenu dispatch |
| event:combat | Combat zone | CZ1/CZ2/CZ3/CZ5; active3D HOT entry spawnsgroundtargets and3drones; COLD none | Codex geometry/placement; Claude event/scoring/spawn logic; SessionSettings.cs Challenges/Lessons; StartMenu dispatch |
| event:dogfight | Dogfight 1 v 1 | CZ1/CZ2/CZ3/CZ5; active3D HOT entry spawnsgroundtargets and3drones; COLD none | Codex geometry/placement; Claude event/scoring/spawn logic; SessionSettings.cs Challenges/Lessons; StartMenu dispatch |
| a1c1-wings-level | Wings level | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines | Codex geometry/placement; Claude event/scoring/spawn logic; SessionSettings.cs Challenges/Lessons; StartMenu dispatch |
| a1c2-best-glide | Best glide | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines | Codex geometry/placement; Claude event/scoring/spawn logic; SessionSettings.cs Challenges/Lessons; StartMenu dispatch |
| a1c3-cardinal-turn | Cardinal turn | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines | Codex geometry/placement; Claude event/scoring/spawn logic; SessionSettings.cs Challenges/Lessons; StartMenu dispatch |
| a1c4-stall-recover | Stall recovery | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines | Codex geometry/placement; Claude event/scoring/spawn logic; SessionSettings.cs Challenges/Lessons; StartMenu dispatch |
| a1c10-headwind-landing | Headwind landing | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines | Codex geometry/placement; Claude event/scoring/spawn logic; SessionSettings.cs Challenges/Lessons; StartMenu dispatch |
| a1c11-crosswind-landing | Crosswind landing | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines | Codex geometry/placement; Claude event/scoring/spawn logic; SessionSettings.cs Challenges/Lessons; StartMenu dispatch |
| lesson:straight | Straight: wings level | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines | Codex geometry/placement; Claude event/scoring/spawn logic; SessionSettings.cs Challenges/Lessons; StartMenu dispatch |
| lesson:climb-level-descend | Climb, level, descend | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines | Codex geometry/placement; Claude event/scoring/spawn logic; SessionSettings.cs Challenges/Lessons; StartMenu dispatch |
| lesson:glide-rear | Glide: speed to fly | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines | Codex geometry/placement; Claude event/scoring/spawn logic; SessionSettings.cs Challenges/Lessons; StartMenu dispatch |
| lesson:glide-side | Glide: speed to fly · side view | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines | Codex geometry/placement; Claude event/scoring/spawn logic; SessionSettings.cs Challenges/Lessons; StartMenu dispatch |
| lesson:climb-vy | Climb: best rate (Vy) | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines | Codex geometry/placement; Claude event/scoring/spawn logic; SessionSettings.cs Challenges/Lessons; StartMenu dispatch |
| lesson:climb-vx | Climb: best angle (Vx) · side view | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines | Codex geometry/placement; Claude event/scoring/spawn logic; SessionSettings.cs Challenges/Lessons; StartMenu dispatch |
| practice:xwind-rudder | Crosswind · rudder | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines | Codex geometry/placement; Claude event/scoring/spawn logic; SessionSettings.cs Challenges/Lessons; StartMenu dispatch |
| practice:xwind-aileron | Crosswind · aileron | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines | Codex geometry/placement; Claude event/scoring/spawn logic; SessionSettings.cs Challenges/Lessons; StartMenu dispatch |
| practice:land-rudder | Crosswind landing · rudder | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines | Codex geometry/placement; Claude event/scoring/spawn logic; SessionSettings.cs Challenges/Lessons; StartMenu dispatch |
| practice:land-aileron | Crosswind landing · aileron | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines | Codex geometry/placement; Claude event/scoring/spawn logic; SessionSettings.cs Challenges/Lessons; StartMenu dispatch |
| practice:flare | Round-out & flare | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines | Codex geometry/placement; Claude event/scoring/spawn logic; SessionSettings.cs Challenges/Lessons; StartMenu dispatch |
| practice:flare-side | Flare · side view | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines | Codex geometry/placement; Claude event/scoring/spawn logic; SessionSettings.cs Challenges/Lessons; StartMenu dispatch |
| practice:approach-side | Approach · side view | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines | Codex geometry/placement; Claude event/scoring/spawn logic; SessionSettings.cs Challenges/Lessons; StartMenu dispatch |
| practice:s-turns | S-turns 45° | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines | Codex geometry/placement; Claude event/scoring/spawn logic; SessionSettings.cs Challenges/Lessons; StartMenu dispatch |
| practice:s-turns-test | Test: S-turns, one speed cycle | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines | Codex geometry/placement; Claude event/scoring/spawn logic; SessionSettings.cs Challenges/Lessons; StartMenu dispatch |
| practice:stall-side | Stall · side view | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines | Codex geometry/placement; Claude event/scoring/spawn logic; SessionSettings.cs Challenges/Lessons; StartMenu dispatch |
| practice:stall-rudder | Stall, wing drop · rudder | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines | Codex geometry/placement; Claude event/scoring/spawn logic; SessionSettings.cs Challenges/Lessons; StartMenu dispatch |
| practice:stall-elevator | Stall, recover · elevator | all eight starts; airwork area 1500 ft AGL; landing tasks use chosen runway; S-turns use ST field lines | Codex geometry/placement; Claude event/scoring/spawn logic; SessionSettings.cs Challenges/Lessons; StartMenu dispatch |
| F1 | Drake Hill and hidden school shell | F1 | Codex; world code / Ty inventory |
| F2 | Pirate cave | F2 | Codex; world code / Ty inventory |
| F3 | Avalon Casino harbour beach | F3 | Codex; world code / Ty inventory |
| F4 | Catalina sea arch | F4 | Codex; world code / Ty inventory |
| F6 | Powell arches spires dam marina ramp | F6 | Codex; world code / Ty inventory |
| F7 | Sea stacks Dillingham beach | F7 | Codex; world code / Ty inventory |
| F11 | Carson hay fields crop dusting | F11 | Codex; world code / Ty inventory |
| F12 | Page landable mesas rock pile strips | F12 | Codex; world code / Ty inventory |
| F13 | Knik braided bars driftwood | F13 | Codex; world code / Ty inventory |
| F14 | Middle Fork bars and strips | F14 | Codex; world code / Ty inventory |
| F15 | Talkeetna River bars | F15 | Codex; world code / Ty inventory |
| F16 | OC beaches piers marina | F16 | Codex; world code / Ty inventory |
| F17 | Bighorn Crags lakes | F17 | Codex; world code / Ty inventory |
| F18 | Historic outdoor glider slope | F18 | Codex; world code / Ty inventory |
| CITY | OC Fly City and city grid | CITY | Codex; world code / Ty inventory |
| RACE | Air race pylon course | RACE | Codex; world code / Ty inventory |
| CARRIER | Carrier off OC | CARRIER | Codex; world code / Ty inventory |
| FOREST | Regional forest | FOREST | Codex; world code / Ty inventory |
| GATEWAY | Gateway style river arch | GATEWAY | Codex; world code / Ty inventory |
| PLAYA | Page dry playa | PLAYA | Codex; world code / Ty inventory |
| strip:Chamberlain | Chamberlain | C2 Middle Fork | Codex; FrankChurch.Strips |
| strip:Big Creek | Big Creek | C2 Middle Fork | Codex; FrankChurch.Strips |
| strip:Thomas Creek | Thomas Creek | C2 Middle Fork | Codex; FrankChurch.Strips |
| strip:Sulphur Creek | Sulphur Creek | C2 Middle Fork | Codex; FrankChurch.Strips |
| strip:Soldier Bar | Soldier Bar | C2 Middle Fork | Codex; FrankChurch.Strips |
| strip:Cabin Creek | Cabin Creek | C2 Middle Fork | Codex; FrankChurch.Strips |
| strip:Dewey Moore | Dewey Moore | C2 Middle Fork | Codex; FrankChurch.Strips |
| strip:Indian Creek | Indian Creek | C2 Middle Fork | Codex; FrankChurch.Strips |
| strip:Mackay Bar | Mackay Bar | C2 Middle Fork | Codex; FrankChurch.Strips |
| extra:59 | aerotow and tow hookup | AP3 tow; PALH/Powell floats; Fairweather ski; Knik River bar strip; AP3 separate undulation lane; C3 falls/bridge; F1/F18 school | Codex geometry; Claude logic; world source audit beyond supplied list |
| extra:60 | float bases ramps buoy lanes | AP3 tow; PALH/Powell floats; Fairweather ski; Knik River bar strip; AP3 separate undulation lane; C3 falls/bridge; F1/F18 school | Codex geometry; Claude logic; world source audit beyond supplied list |
| extra:61 | glacier ski landings and icebergs | AP3 tow; PALH/Powell floats; Fairweather ski; Knik River bar strip; AP3 separate undulation lane; C3 falls/bridge; F1/F18 school | Codex geometry; Claude logic; world source audit beyond supplied list |
| extra:62 | sloped grass Snoopy swoop training lane | AP3 tow; PALH/Powell floats; Fairweather ski; Knik River bar strip; AP3 separate undulation lane; C3 falls/bridge; F1/F18 school | Codex geometry; Claude logic; world source audit beyond supplied list |
| extra:63 | waterfalls and plunge pools | AP3 tow; PALH/Powell floats; Fairweather ski; Knik River bar strip; AP3 separate undulation lane; C3 falls/bridge; F1/F18 school | Codex geometry; Claude logic; world source audit beyond supplied list |
| extra:64 | river road bridge | AP3 tow; PALH/Powell floats; Fairweather ski; Knik River bar strip; AP3 separate undulation lane; C3 falls/bridge; F1/F18 school | Codex geometry; Claude logic; world source audit beyond supplied list |
| extra:65 | hidden school interior shell and outdoor historic gliders | AP3 tow; PALH/Powell floats; Fairweather ski; Knik River bar strip; AP3 separate undulation lane; C3 falls/bridge; F1/F18 school | Codex geometry; Claude logic; world source audit beyond supplied list |
| gunnery:targets | Island ground bullseyes and target drones | 3 groundtargets ineachCZ1-CZ3,2 inCZ5;3 dronesperzone; HOT3Dentry only | Codex target geometry; Claude AI/hit/event logic; Combat/Gunnery.cs CombatZone.BuildGroundTargets; CombatController |
| combat:concealment | six legacy puffy combat clouds / concealment | CZ1-CZ3 weather volumes proposed; Claude-owned | Claude weather; no world cloud implementation; Combat/Gunnery.cs CombatZone.Clouds |

The inventory retains all 68 original entries: six events, six challenges, lesson/practice variants, rooftop/city, STOL/dusting, tow/float/ski infrastructure, backcountry bars, historic glider slope, carrier, Catalina/Avalon/cave/arch, Powell, glaciers, forest, landable mesas and Drake school geometry. Golden Gate, Eiffel tower, DC Mall and waterfall tunnel remain deleted. No lesson/event/scoring/weather runtime is changed.

## G1a approved defaults — G1 flight review pending

Ty approved TY1–TY8 at G1a on 2026-10-30 20:25 PDT. TY2 is superseded by his single-portal one-way ruling. The following records the accepted design assumptions; G1 remains pending Ty’s VFR review.

**TY1 — Main-island footprint versus the locked approximate size**

The west coast needed for the PANC/Lake Hood fit makes the main polygon 20.2 × 17.6 km, versus the brief’s approximately 18.3 × 17.5 km. The 20 km world ring remains unchanged. This size difference is a proposal requiring Ty, not an approved change to the locked context.

**APPROVED DEFAULT:** Approve the 20.2 × 17.6 km main polygon and the five outer runway islands inside the unchanged ring.

Status: APPROVED by Ty 2026-10-30 20:25 PDT.

**TY2 — Drake one-way operation**

ONE WAY IN / ONE WAY OUT through the single north portal. Land runway17; depart back through the same north portal. No reverse final, no second portal.

**APPROVED DEFAULT:** ONE WAY IN / ONE WAY OUT through the single north portal. Land runway17; depart back through the same north portal. No reverse final, no second portal.

Status: APPROVED by Ty 2026-10-30 20:25 PDT.

**TY3 — Missing Claude-owned procedure protection**

T201 fix coordinates/segment MEAs, complete missed approaches/holds including BALBO’s exact turns, and KAVX circling geometry were not supplied. Whole-rule certification for rules 3–5 and 8 remains UNVERIFIED for these volumes.

**APPROVED DEFAULT:** Approve the design reservations conditionally; Claude supplies the missing polygons and recomputes TERPS/MEAs, and Codex repairs any terrain conflicts before G1. Preserve the known 2.05 km MOA guards and 20 km ring.

Status: APPROVED by Ty 2026-10-30 20:25 PDT.

**TY4 — Unspecified real-field survey details**

PALH gravel elevation 79 ft, 3U2 longitudinal slope 0.5%, and KMEV glider-strip dimensions are provisional targets because the brief supplied no definitive values. Outer airfields, airparks and beach datums are fictional design values. Payette Lake’s 4,900 ft level is a fictional design target; only Powell at 3,700 ft is locked among these lake levels.

**APPROVED DEFAULT:** Retain those provisional values for design review, visibly labelled as targets; replace them with confirmed survey values before G1 if Ty provides them, then rerun all fits.

Status: APPROVED by Ty 2026-10-30 20:25 PDT.

**TY5 — IFR alignment after world-coordinate revisions**

The supplied IFR section is unchanged. PANC/Lake Hood, KMYL, KMEV, KAVX and KPGA world geometry moved; station/fix geometry and the supplied KSNA threshold remain for Claude to reconcile. KPGA crossrunway is now true102° (magnetic095°,10/28). New outer navaids are sites only.

**APPROVED DEFAULT:** Approve the revised world positions and have Claude fit the IFR layer to them after the design gate; no world-agent edit to the supplied IFR transcription.

Status: APPROVED by Ty 2026-10-30 20:25 PDT.

**TY6 — Terrain, fictional airpark datums and combat operating defaults**

The complete changes ledger records all moved peaks, reheaded strips, fictional elevations, outer islands, beaches, combat islands/structures and the offshore race course. Combat base floors are 150 ft MSL, or 200 ft at the land MOA; airport traffic overlays raise the floor to 5,000 ft above traffic capped at 3,000 ft.

**APPROVED DEFAULT:** Approve the final coordinates/dimensions/datums and default-HOT zones, with targets and drones appearing only upon entry into the active 3D volume; COLD zones do not spawn them.

Status: APPROVED by Ty 2026-10-30 20:25 PDT.

**TY7 — Combat-zone names**

Susitna is an actual MOA name in an FAA chart supplement. All four design names now carry an AP prefix to identify fictional simulator airspace; no claim of authoritative worldwide name uniqueness is made.

**APPROVED DEFAULT:** Approve AP KAIWI, AP PAINTED, AP SUSITNA and AP TERN MESA as fictional chart names.

Status: APPROVED by Ty 2026-10-30 20:25 PDT.

**TY8 — Design proofs versus runtime validation**

The full clipped-triangle checks are exact on the 100 m review grid; analytic pad/cavity/marker/structure bounds cover their detail. Fine terrain continuity, water containment, cave contact routing, loaded-trainer zoom/turnback and collider/airframe behavior cannot be flight-certified without the Phase 1B build.

**APPROVED DEFAULT:** Approve the design evidence and keep runtime/flight certification pending for G1. This session stops at G1a; it does not authorize or start implementation.

Status: APPROVED by Ty 2026-10-30 20:25 PDT.

## Phase 1B runtime and G1 verification

Implementation, local compile evidence, runtime checks, broken tests, concept gallery and remaining G1 review items are recorded in [ISLAND-WORLD-G1.md](ISLAND-WORLD-G1.md). Fixed WorldRender cameras are authored; no sandbox screenshot is claimed. New runtime asset kinds read from StreamingAssets: **none**.

## Closed review items

| Item | Resolution |
| --- | --- |
| CZ4 KACHEMAK | Removed activezone and start by Tydecision2; historical removal remains inchangeledger only. |
| Extra high-DA boxes | Exactlyone high-DA box, AB3 atMcCall; AB1/AB2 remainwater. |
| Water box markers/judges | Each waterbox has9 moored cross/L/Trafts plus1 judge raft,4 orangebuoys, and dryshore marker on extendedjudges-line axis; fullboundsverified. |
| 19 grade pairs /7 spacing conflicts | Coordinates/headings/fictional datums revised; all-pair full-pad grade and centre-spacing checks rerun, including newouterairports/beaches/floatpond. |
| Drake crown/roof geometry | Strip200m south; 820x420x140m shell fits700m radius hill; fullroof>=60m cover. DRK remains at(-4.9,-4.3) on5750ft crown; exact2992ft total and4700ft floor unchanged. Runtime collision stilldeferred. |
| Pirate cave interior | Entire planar pads and20m-wide throat/chamber chord checked analytically against locked60x26m mouth/90m-radius chamber and contentbounds. |
| Floating gunnery targets | Replaced by groundtargets oncombat islands/landterrace. Everyzone now has explicitHOT-entry target/drone spawning and COLD behavior. |
| AP8 numbering | 36/18 follows true007 minus7E; opposite18. Added explicitAP8 floatlane and allfit checks. |
| KSNA north-side / water-surface conflicts | RelocatedPage/Powell/backcountryallocation andsixCrags lakes; fullwaterplanes nowincluded in allapproach/airwaychecks. NoKSNA directional waiver. |
| Drake operation (TY2 superseded) | ONE WAY IN / ONE WAY OUT through the single north portal. Land runway17; depart back through the same north portal. No reverse final, no second portal. |

## Reproduction and verification

Run from this worktree, using only Python’s standard library and macOS AppKit:

```sh
python3 docs/pending/tools/generate_island.py --png
python3 docs/pending/tools/verify_island.py --rebuild
```

Swift/Clang module caches stay in a temporary directory. `island-determinism.json` records byte comparisons and output hashes. The validator checks full-fit recomputation, locked values, untouched IFR, all added zone/field/island/site/beach/raft/cave evidence, XML/PNG dimensions and grid/spec hashes. A successful package check means the reported evidence is consistent; it does not turn FAIL or UNVERIFIED rules into approval.

Review sheets: [map PNG](island-map.png), [map SVG](island-map.svg), [elevation PNG](island-elevation.png), [elevation SVG](island-elevation.svg). The elevation image uses the very same baked grid with the chart annotations; the raw raster is [island-elevation-base.png](island-elevation-base.png). Machine spec: [island-spec.json](island-spec.json).

## Complete changes ledger

Every entry carries item, old value, new value and reason. Baseline 1A entries and chronological partial 1A.2 iterations remain visible; later entries supersede earlier attempts. The final tables above and current `world` object give the final design. This preserves the stopped-session work history rather than pretending earlier candidates were approved.

<details>
<summary>1. AP8 runway numbers</summary>

```json
{"item":"AP8 runway numbers","old":"18/36 at 007 true","new":"36/18 at unchanged 007 true","reason":"007 true - 7E = 000 magnetic; order follows the authored heading; length and position unchanged"}
```

</details>

<details>
<summary>2. KMEV glider strip</summary>

```json
{"item":"KMEV glider strip","old":"13G/31G; no dimensions/centre","new":"centre (-2.20,-1.90), 135 true, 2000 x 75 ft grass, 4724 ft","reason":"explicit review geometry; target pending Ty approval"}
```

</details>

<details>
<summary>3. F1 Drake Hill and hidden school shell</summary>

```json
{"item":"F1 Drake Hill and hidden school shell","old":"legacy duplicated plateau/world position or unspecified starting position","new":[-4.9,-4.3],"reason":"consolidated island allocation; supplied F positions retained where given; new placements are targets"}
```

</details>

<details>
<summary>4. F2 Pirate cave</summary>

```json
{"item":"F2 Pirate cave","old":"legacy duplicated plateau/world position or unspecified starting position","new":[2.7,-14.6],"reason":"consolidated island allocation; supplied F positions retained where given; new placements are targets"}
```

</details>

<details>
<summary>5. F3 Avalon Casino harbour beach</summary>

```json
{"item":"F3 Avalon Casino harbour beach","old":"legacy duplicated plateau/world position or unspecified starting position","new":[4.5,-13.7],"reason":"consolidated island allocation; supplied F positions retained where given; new placements are targets"}
```

</details>

<details>
<summary>6. F4 Catalina sea arch</summary>

```json
{"item":"F4 Catalina sea arch","old":"legacy duplicated plateau/world position or unspecified starting position","new":[3.5,-14],"reason":"consolidated island allocation; supplied F positions retained where given; new placements are targets"}
```

</details>

<details>
<summary>7. F6 Powell arches spires dam marina ramp</summary>

```json
{"item":"F6 Powell arches spires dam marina ramp","old":"legacy duplicated plateau/world position or unspecified starting position","new":[2.88,-5.15],"reason":"consolidated island allocation; supplied F positions retained where given; new placements are targets"}
```

</details>

<details>
<summary>8. F7 Sea stacks Dillingham beach</summary>

```json
{"item":"F7 Sea stacks Dillingham beach","old":"legacy duplicated plateau/world position or unspecified starting position","new":[6,6.2],"reason":"consolidated island allocation; supplied F positions retained where given; new placements are targets"}
```

</details>

<details>
<summary>9. F11 Carson hay fields crop dusting</summary>

```json
{"item":"F11 Carson hay fields crop dusting","old":"legacy duplicated plateau/world position or unspecified starting position","new":[-2.9,-4.4],"reason":"consolidated island allocation; supplied F positions retained where given; new placements are targets"}
```

</details>

<details>
<summary>10. F12 Page landable mesas rock pile strips</summary>

```json
{"item":"F12 Page landable mesas rock pile strips","old":"legacy duplicated plateau/world position or unspecified starting position","new":[5,-3.7],"reason":"consolidated island allocation; supplied F positions retained where given; new placements are targets"}
```

</details>

<details>
<summary>11. F13 Knik braided bars driftwood</summary>

```json
{"item":"F13 Knik braided bars driftwood","old":"legacy duplicated plateau/world position or unspecified starting position","new":[-6.7,0.3],"reason":"consolidated island allocation; supplied F positions retained where given; new placements are targets"}
```

</details>

<details>
<summary>12. F14 Middle Fork bars and strips</summary>

```json
{"item":"F14 Middle Fork bars and strips","old":"legacy duplicated plateau/world position or unspecified starting position","new":[3.5,0.5],"reason":"consolidated island allocation; supplied F positions retained where given; new placements are targets"}
```

</details>

<details>
<summary>13. F15 Talkeetna River bars</summary>

```json
{"item":"F15 Talkeetna River bars","old":"legacy duplicated plateau/world position or unspecified starting position","new":[-7.2,5.8],"reason":"consolidated island allocation; supplied F positions retained where given; new placements are targets"}
```

</details>

<details>
<summary>14. F16 OC beaches piers marina</summary>

```json
{"item":"F16 OC beaches piers marina","old":"legacy duplicated plateau/world position or unspecified starting position","new":[2.8,-9],"reason":"consolidated island allocation; supplied F positions retained where given; new placements are targets"}
```

</details>

<details>
<summary>15. F17 Bighorn Crags lakes</summary>

```json
{"item":"F17 Bighorn Crags lakes","old":"legacy duplicated plateau/world position or unspecified starting position","new":[1.2,1.8],"reason":"consolidated island allocation; supplied F positions retained where given; new placements are targets"}
```

</details>

<details>
<summary>16. F18 Historic outdoor glider slope</summary>

```json
{"item":"F18 Historic outdoor glider slope","old":"legacy duplicated plateau/world position or unspecified starting position","new":[4.3,4],"reason":"consolidated island allocation; supplied F positions retained where given; new placements are targets"}
```

</details>

<details>
<summary>17. CITY OC Fly City and city grid</summary>

```json
{"item":"CITY OC Fly City and city grid","old":"legacy duplicated plateau/world position or unspecified starting position","new":[4.8,-6.6],"reason":"consolidated island allocation; supplied F positions retained where given; new placements are targets"}
```

</details>

<details>
<summary>18. RACE Air race pylon course</summary>

```json
{"item":"RACE Air race pylon course","old":"legacy duplicated plateau/world position or unspecified starting position","new":[7,-0.3],"reason":"consolidated island allocation; supplied F positions retained where given; new placements are targets"}
```

</details>

<details>
<summary>19. CARRIER Carrier off OC</summary>

```json
{"item":"CARRIER Carrier off OC","old":"legacy duplicated plateau/world position or unspecified starting position","new":[6.8,-10.5],"reason":"consolidated island allocation; supplied F positions retained where given; new placements are targets"}
```

</details>

<details>
<summary>20. FOREST Regional forest</summary>

```json
{"item":"FOREST Regional forest","old":"legacy duplicated plateau/world position or unspecified starting position","new":[0,2],"reason":"consolidated island allocation; supplied F positions retained where given; new placements are targets"}
```

</details>

<details>
<summary>21. GATEWAY Gateway style river arch</summary>

```json
{"item":"GATEWAY Gateway style river arch","old":"legacy duplicated plateau/world position or unspecified starting position","new":[0.5,-6.7],"reason":"consolidated island allocation; supplied F positions retained where given; new placements are targets"}
```

</details>

<details>
<summary>22. PLAYA Page dry playa</summary>

```json
{"item":"PLAYA Page dry playa","old":"legacy duplicated plateau/world position or unspecified starting position","new":[5.8,-4.9],"reason":"consolidated island allocation; supplied F positions retained where given; new placements are targets"}
```

</details>

<details>
<summary>23. Chamberlain</summary>

```json
{"item":"Chamberlain","old":{"sim_xy_m":[-3900,-11000],"elevation":"legacy relative to 7000 ft plateau"},"new":{"centre":[2.8,-1.5],"elevation_ft":7000},"reason":"move into Middle Fork; retain dimensions, surface, slope and one-way character; candidate fit unresolved"}
```

</details>

<details>
<summary>24. Big Creek</summary>

```json
{"item":"Big Creek","old":{"sim_xy_m":[-2600,-11400],"elevation":"legacy relative to 7000 ft plateau"},"new":{"centre":[3,-0.8],"elevation_ft":7000},"reason":"move into Middle Fork; retain dimensions, surface, slope and one-way character; candidate fit unresolved"}
```

</details>

<details>
<summary>25. Thomas Creek</summary>

```json
{"item":"Thomas Creek","old":{"sim_xy_m":[-4700,-14100],"elevation":"legacy relative to 7000 ft plateau"},"new":{"centre":[3.8,-0.7],"elevation_ft":7000},"reason":"move into Middle Fork; retain dimensions, surface, slope and one-way character; candidate fit unresolved"}
```

</details>

<details>
<summary>26. Sulphur Creek</summary>

```json
{"item":"Sulphur Creek","old":{"sim_xy_m":[-3200,-14300],"elevation":"legacy relative to 7000 ft plateau"},"new":{"centre":[4.6,-0.3],"elevation_ft":7000},"reason":"move into Middle Fork; retain dimensions, surface, slope and one-way character; candidate fit unresolved"}
```

</details>

<details>
<summary>27. Soldier Bar</summary>

```json
{"item":"Soldier Bar","old":{"sim_xy_m":[-1900,-13900],"elevation":"legacy relative to 7000 ft plateau"},"new":{"centre":[4.4,0.6],"elevation_ft":5865},"reason":"move into Middle Fork; retain dimensions, surface, slope and one-way character; candidate fit unresolved"}
```

</details>

<details>
<summary>28. Cabin Creek</summary>

```json
{"item":"Cabin Creek","old":{"sim_xy_m":[-3500,-12126],"elevation":"legacy relative to 7000 ft plateau"},"new":{"centre":[3.2,0.3],"elevation_ft":6259},"reason":"move into Middle Fork; retain dimensions, surface, slope and one-way character; candidate fit unresolved"}
```

</details>

<details>
<summary>29. Dewey Moore</summary>

```json
{"item":"Dewey Moore","old":{"sim_xy_m":[-2900,-13528],"elevation":"legacy relative to 7000 ft plateau"},"new":{"centre":[4.8,1.1],"elevation_ft":6094},"reason":"move into Middle Fork; retain dimensions, surface, slope and one-way character; candidate fit unresolved"}
```

</details>

<details>
<summary>30. Indian Creek</summary>

```json
{"item":"Indian Creek","old":{"sim_xy_m":[-300,-12830],"elevation":"legacy relative to 7000 ft plateau"},"new":{"centre":[3.8,1.6],"elevation_ft":4841},"reason":"move into Middle Fork; retain dimensions, surface, slope and one-way character; candidate fit unresolved"}
```

</details>

<details>
<summary>31. Mackay Bar</summary>

```json
{"item":"Mackay Bar","old":{"sim_xy_m":[1000,-12486],"elevation":"legacy relative to 7000 ft plateau"},"new":{"centre":[5,1.8],"elevation_ft":4917},"reason":"move into Middle Fork; retain dimensions, surface, slope and one-way character; candidate fit unresolved"}
```

</details>

<details>
<summary>32. legacy airports/features</summary>

```json
{"item":"legacy airports/features","old":"Valley/Bench/Mesa/Summit and repeated boxes/races/cities","new":"eight real-elevation starts; AB1-AB3; one RACE; OC city","reason":"locked island plan"}
```

</details>

<details>
<summary>33. legacy glacial/alpine terrain</summary>

```json
{"item":"legacy glacial/alpine terrain","old":"Alaska/Enchantments blocks and old high ski elevations","new":"Knik/Ruth 1100/1300 ft ski tongues; Bighorn six-lake stair","reason":"locked renaming and low glacier landing zones"}
```

</details>

<details>
<summary>34. 3U2 slope</summary>

```json
{"item":"3U2 slope","old":"unspecified slope","new":"0.5% longitudinal target","reason":"make explicit pending surveyed value; locked centre elevation and dimensions retained"}
```

</details>

<details>
<summary>35. airpark elevations/widths</summary>

```json
{"item":"airpark elevations/widths","old":"unspecified","new":"per-airpark target elevations, width 60 ft, 60 m shoulders","reason":"required for height-field review; no true elevation was supplied for fictional strips"}
```

</details>

<details>
<summary>36. legacy waterfall tunnel</summary>

```json
{"item":"legacy waterfall tunnel","old":"existing tunnel and older tunnel-first Drake plan","new":"excluded from island design; code unchanged in Phase 1A","reason":"locked deletion decision 18"}
```

</details>

<details>
<summary>37. legacy crop-field conductors</summary>

```json
{"item":"legacy crop-field conductors","old":"separate 30.48 m AGL wire set","new":"use charted W4 at 18 m minimum AGL","reason":"keep exactly five authored wire spans; no hidden duplicate hazard"}
```

</details>

<details>
<summary>38. legacy Gateway-style river arch</summary>

```json
{"item":"legacy Gateway-style river arch","old":"Landmarks.Arch duplicated across plateaus","new":"one target arch at (0.5,-6.7)","reason":"not on the dropped list; preserve audited fly-through feature"}
```

</details>

<details>
<summary>39. Coast transition band</summary>

```json
{"item":"Coast transition band","old":0.35,"new":0.1,"reason":"100 m shore fade keeps locked Catalina runway and its 60 m shoulders at true elevation; compact smooth transition still reaches sea bed continuously"}
```

</details>

<details>
<summary>40. AP9 fictional elevation target</summary>

```json
{"item":"AP9 fictional elevation target","old":600,"new":3200,"reason":"reduce mean field-to-field grades while keeping supplied position, dimensions and surface"}
```

</details>

<details>
<summary>41. AP14 fictional elevation target</summary>

```json
{"item":"AP14 fictional elevation target","old":250,"new":1600,"reason":"reduce mean field-to-field grades while keeping supplied position, dimensions and surface"}
```

</details>

<details>
<summary>42. AP4 fictional elevation target</summary>

```json
{"item":"AP4 fictional elevation target","old":180,"new":1600,"reason":"reduce mean field-to-field grades while keeping supplied position, dimensions and surface"}
```

</details>

<details>
<summary>43. AP10 fictional elevation target</summary>

```json
{"item":"AP10 fictional elevation target","old":5200,"new":4500,"reason":"reduce mean field-to-field grades while keeping supplied position, dimensions and surface"}
```

</details>

<details>
<summary>44. AP2 Talkeetna village centre</summary>

```json
{"item":"AP2 Talkeetna village centre","old":[-7,4.7],"new":[-6.9,6.8],"reason":"separate village strip from PATK while retaining Talkeetna regional identity; new shore/approach clearance remains to be checked"}
```

</details>

<details>
<summary>45. KMEV 17/35 centre</summary>

```json
{"item":"KMEV 17/35 centre","old":[-3.17,-1.73],"new":[-3.02,-1.73],"reason":"150 m east shift increases Lake Hood edge-to-edge run, bringing critical flat-field grade below 35%; IFR records unchanged"}
```

</details>

<details>
<summary>46. KMEV 13/31 centre</summary>

```json
{"item":"KMEV 13/31 centre","old":[-2.5,-2.02],"new":[-2.35,-2.02],"reason":"150 m east shift increases Lake Hood edge-to-edge run, bringing critical flat-field grade below 35%; IFR records unchanged"}
```

</details>

<details>
<summary>47. KMEV 13G/31G centre</summary>

```json
{"item":"KMEV 13G/31G centre","old":[-2.2,-1.9],"new":[-2.05,-1.9],"reason":"150 m east shift increases Lake Hood edge-to-edge run, bringing critical flat-field grade below 35%; IFR records unchanged"}
```

</details>

<details>
<summary>48. PHDH east beach coast vertices</summary>

```json
{"item":"PHDH east beach coast vertices","old":[[6.4,6.2],[7.7,4.5]],"new":[[6.6,6.4],[7.9,4.5]],"reason":"add 0.2 km of coastal apron near east runway end for 300 m shore setback"}
```

</details>

<details>
<summary>49. Legacy CombatZone targets</summary>

```json
{"item":"Legacy CombatZone targets","old":{"five_open_ground_bullseyes_radius_m":30,"one_gorge_floor_target_radius_m":14,"three_orbiting_drones":true},"new":"six target rafts and three-drone orbit per active CZ; exact positions in each zone","reason":"preserve gunnery over the new over-water sectors without creating new land; Claude adapts target water/altitude handling"}
```

</details>

<details>
<summary>50. Box labels</summary>

```json
{"item":"Box labels","old":{"water_floor_ceiling_ft":[330,3610],"AB3_floor_ceiling_ft":[5330,8610]},"new":{"water_floor_ceiling_ft":[328,3609],"AB3_floor_ceiling_ft":[5328,8609]},"reason":"label conversions rounded to nearest foot from locked 100 m / 1100 m AGL dimensions; no change to box dimensions"}
```

</details>

<details>
<summary>51. RACE element geometry</summary>

```json
{"item":"RACE element geometry","old":[{"sequence":1,"sim_xy_m":[45,1800],"heading_true_deg":0},{"sequence":2,"sim_xy_m":[345,1800],"heading_true_deg":0},{"sequence":3,"sim_xy_m":[540,1755],"heading_true_deg":0},{"sequence":4,"sim_xy_m":[720,1845],"heading_true_deg":0},{"sequence":5,"sim_xy_m":[900,1755],"heading_true_deg":0},{"sequence":6,"sim_xy_m":[1050,1800],"heading_true_deg":0},{"sequence":7,"sim_xy_m":[1110,1920],"heading_true_deg":90},{"sequence":8,"sim_xy_m":[1020,2050],"heading_true_deg":180},{"sequence":9,"sim_xy_m":[720,2050],"heading_true_deg":180},{"sequence":10,"sim_xy_m":[540,2095],"heading_true_deg":180},{"sequence":11,"sim_xy_m":[360,2005],"heading_true_deg":180},{"sequence":12,"sim_xy_m":[120,2050],"heading_true_deg":180}],"new":[{"sequence":1,"centre":[6.4675,-0.175],"heading_true_deg":90},{"sequence":2,"centre":[6.7675,-0.175],"heading_true_deg":90},{"sequence":3,"centre":[6.9625,-0.13],"heading_true_deg":90},{"sequence":4,"centre":[7.1425,-0.22],"heading_true_deg":90},{"sequence":5,"centre":[7.3225,-0.13],"heading_true_deg":90},{"sequence":6,"centre":[7.4725,-0.175],"heading_true_deg":90},{"sequence":7,"centre":[7.5325,-0.295],"heading_true_deg":180},{"sequence":8,"centre":[7.4425,-0.425],"heading_true_deg":270},{"sequence":9,"centre":[7.1425,-0.425],"heading_true_deg":270},{"sequence":10,"centre":[6.9625,-0.47],"heading_true_deg":270},{"sequence":11,"centre":[6.7825,-0.38],"heading_true_deg":270},{"sequence":12,"centre":[6.5425,-0.425],"heading_true_deg":270}],"reason":"preserve all 12 gates/chicane/turn sequence; rotate course 90 true and move to eastern island; dimensions retained"}
```

</details>

<details>
<summary>52. AP9 Turnagain bench refinement</summary>

```json
{"item":"AP9 Turnagain bench refinement","old":{"centre":[-6.6,-5],"elevation_ft":3200},"new":{"centre":[-5.7,-6],"elevation_ft":2900},"reason":"move bench southeast to obtain enough horizontal run from both PANC and Drake; keep gravel surface,1600 ft length and heading"}
```

</details>

<details>
<summary>53. AP4 fictional elevation refinement</summary>

```json
{"item":"AP4 fictional elevation refinement","old":1600,"new":1350,"reason":"keep PHDH-to-ranch padded-edge mean grade within35%; AP11 relationship remains an explicit fit conflict"}
```

</details>

<details>
<summary>54. Height-field composition refinement</summary>

```json
{"item":"Height-field composition refinement","old":"coast-derived4600ft interior base; glacier cuts can replace heads; full Drake pad flattens hill","new":"Mokuleia1800ft regional base; fade glacier head claim; reassert authored peaks; exterior-only Drake terrain pad","reason":"honour all seven authored summit targets and preserve the hidden chamber rock envelope; interior floor belongs to3D shell, not an open trench"}
```

</details>

<details>
<summary>55. ST-PHDH practice reference line</summary>

```json
{"item":"ST-PHDH practice reference line","old":[[4.39,6.5],[5.59,6.5]],"new":[[4.389368,5.869048],[5.589186,5.848105]],"reason":"align reference line with flat runway shoulder30m outside surface edge, avoiding steep arbitrary cross-country road; use buoy reference at PALH"}
```

</details>

<details>
<summary>56. ST-PANC practice reference line</summary>

```json
{"item":"ST-PANC practice reference line","old":[[-9.24,-2.47],[-8.04,-2.47]],"new":[[-8.853711,-2.506098],[-8.543128,-3.665209]],"reason":"align reference line with flat runway shoulder30m outside surface edge, avoiding steep arbitrary cross-country road; use buoy reference at PALH"}
```

</details>

<details>
<summary>57. ST-PALH practice reference line</summary>

```json
{"item":"ST-PALH practice reference line","old":[[-8.56,-1.2],[-7.36,-1.2]],"new":[[-8.56,-1.858651],[-7.36,-1.858651]],"reason":"align reference line with flat runway shoulder30m outside surface edge, avoiding steep arbitrary cross-country road; use buoy reference at PALH"}
```

</details>

<details>
<summary>58. ST-PATK practice reference line</summary>

```json
{"item":"ST-PATK practice reference line","old":[[-6.94,5.59],[-5.74,5.59]],"new":[[-6.536164,4.513748],[-6.070007,5.428634]],"reason":"align reference line with flat runway shoulder30m outside surface edge, avoiding steep arbitrary cross-country road; use buoy reference at PALH"}
```

</details>

<details>
<summary>59. ST-KSNA practice reference line</summary>

```json
{"item":"ST-KSNA practice reference line","old":[[0.36,-7.46],[1.56,-7.46]],"new":[[0.72499,-8.614585],[1.288356,-7.555048]],"reason":"align reference line with flat runway shoulder30m outside surface edge, avoiding steep arbitrary cross-country road; use buoy reference at PALH"}
```

</details>

<details>
<summary>60. ST-KMYL practice reference line</summary>

```json
{"item":"ST-KMYL practice reference line","old":[[-1.18,3.38],[0.02,3.38]],"new":[[-0.631895,3.379186],[-0.610952,2.179368]],"reason":"align reference line with flat runway shoulder30m outside surface edge, avoiding steep arbitrary cross-country road; use buoy reference at PALH"}
```

</details>

<details>
<summary>61. ST-3U2 practice reference line</summary>

```json
{"item":"ST-3U2 practice reference line","old":[[1.22,-0.26],[2.42,-0.26]],"new":[[1.871868,-0.361736],[1.664722,-1.336284]],"reason":"align reference line with flat runway shoulder30m outside surface edge, avoiding steep arbitrary cross-country road; use buoy reference at PALH"}
```

</details>

<details>
<summary>62. ST-KMEV practice reference line</summary>

```json
{"item":"ST-KMEV practice reference line","old":[[-3.62,-1.13],[-2.42,-1.13]],"new":[[-3.06524,-1.13],[-3.06524,-2.33]],"reason":"align reference line with flat runway shoulder30m outside surface edge, avoiding steep arbitrary cross-country road; use buoy reference at PALH"}
```

</details>

<details>
<summary>63. ST-KPGA practice reference line</summary>

```json
{"item":"ST-KPGA practice reference line","old":[[3.14,-2.66],[4.34,-2.66]],"new":[[3.583754,-2.678294],[3.792132,-3.860064]],"reason":"align reference line with flat runway shoulder30m outside surface edge, avoiding steep arbitrary cross-country road; use buoy reference at PALH"}
```

</details>

<details>
<summary>64. PANC 16/34 centre</summary>

```json
{"phase":"1A.2","item":"PANC 16/34 centre","old":[-8.64,-3.07],"new":[-10.24,-3.07],"reason":"Move adjacent Cook Inlet group west together; preserve true elevations, runway dimensions and relative Lake Hood adjacency; clear locked 35% grades to Drake without moving DRK or the hill."}
```

</details>

<details>
<summary>65. PALH 8W/26W centre</summary>

```json
{"phase":"1A.2","item":"PALH 8W/26W centre","old":[-7.96,-1.8],"new":[-9.56,-1.8],"reason":"Move adjacent Cook Inlet group west together; preserve true elevations, runway dimensions and relative Lake Hood adjacency; clear locked 35% grades to Drake without moving DRK or the hill."}
```

</details>

<details>
<summary>66. PALH 15/33 centre</summary>

```json
{"phase":"1A.2","item":"PALH 15/33 centre","old":[-7.85,-1.25],"new":[-9.45,-1.25],"reason":"Move adjacent Cook Inlet group west together; preserve true elevations, runway dimensions and relative Lake Hood adjacency; clear locked 35% grades to Drake without moving DRK or the hill."}
```

</details>

<details>
<summary>67. Cook Inlet coast apron</summary>

```json
{"phase":"1A.2","item":"Cook Inlet coast apron","old":[[-9.7,-5.7],[-9.6,-3.8],[-9.5,-1],[-9.1,2],[-8.8,5.7],[-7,7.3],[-4.5,7.8],[-1,7.7],[2,7.4],[4.5,6.5],[6.6,6.4],[7.9,4.5],[8.6,2],[8.4,-1],[7.5,-3.8],[6.2,-6],[4,-8.4],[2,-9.7],[0,-9.8],[-1.54,-9.6],[-3.3,-8.7],[-5.3,-7.4],[-7.8,-6.6]],"new":[[-11.6,-5.7],[-11.5,-3.8],[-11.4,-1],[-11.0,2],[-9.700000000000001,5.7],[-7,7.3],[-4.5,7.8],[-1,7.7],[2,7.4],[4.5,6.5],[6.6,6.4],[7.9,4.5],[8.6,2],[8.4,-1],[7.5,-3.8],[6.2,-6],[4,-8.4],[2,-9.7],[0,-9.8],[-1.54,-9.6],[-3.3,-8.7],[-5.3,-7.4],[-7.8,-6.6]],"reason":"Keep moved coastal field ends at least 300 m inland; preserve all other shoreline vertices. Approximate island width grows; explicitly submitted to Ty."}
```

</details>

<details>
<summary>68. AB2 centre</summary>

```json
{"phase":"1A.2","item":"AB2 centre","old":[-10.1,-3.6],"new":[-12.0,-3.6],"reason":"Keep 1 km square fully over water west of the revised Cook Inlet shoreline and away from PANC finals."}
```

</details>

<details>
<summary>69. AP6 centre</summary>

```json
{"phase":"1A.2","item":"AP6 centre","old":[4.2,2.6],"new":[5.1,3.6],"reason":"Separate pads/fields or lengthen grade run while retaining regional identity, length, surface and gear."}
```

</details>

<details>
<summary>70. AP10 centre</summary>

```json
{"phase":"1A.2","item":"AP10 centre","old":[2.7,1.2],"new":[1.8,1.5],"reason":"Separate pads/fields or lengthen grade run while retaining regional identity, length, surface and gear."}
```

</details>

<details>
<summary>71. AP11 centre</summary>

```json
{"phase":"1A.2","item":"AP11 centre","old":[0.8,4.6],"new":[0.1,4.6],"reason":"Separate pads/fields or lengthen grade run while retaining regional identity, length, surface and gear."}
```

</details>

<details>
<summary>72. AP12 centre</summary>

```json
{"phase":"1A.2","item":"AP12 centre","old":[1.8,-4.4],"new":[1.4,-3.5],"reason":"Separate pads/fields or lengthen grade run while retaining regional identity, length, surface and gear."}
```

</details>

<details>
<summary>73. AP5 fictional elevation</summary>

```json
{"phase":"1A.2","item":"AP5 fictional elevation","old":900,"new":1200,"reason":"Balance padded-edge grades at <=35%; no real field elevation supplied for this fictional airpark."}
```

</details>

<details>
<summary>74. AP9 fictional elevation</summary>

```json
{"phase":"1A.2","item":"AP9 fictional elevation","old":2900,"new":3200,"reason":"Balance padded-edge grades at <=35%; no real field elevation supplied for this fictional airpark."}
```

</details>

<details>
<summary>75. AP11 fictional elevation</summary>

```json
{"phase":"1A.2","item":"AP11 fictional elevation","old":5024,"new":4000,"reason":"Balance padded-edge grades at <=35%; no real field elevation supplied for this fictional airpark."}
```

</details>

<details>
<summary>76. Chamberlain centre</summary>

```json
{"phase":"1A.2","item":"Chamberlain centre","old":[2.8,-1.5],"new":[2.4,-2.1],"reason":"Remove supplied allocation spacing conflicts while retaining the Middle Fork group and strip geometry."}
```

</details>

<details>
<summary>77. Soldier Bar centre</summary>

```json
{"phase":"1A.2","item":"Soldier Bar centre","old":[4.4,0.6],"new":[4.1,0.5],"reason":"Remove supplied allocation spacing conflicts while retaining the Middle Fork group and strip geometry."}
```

</details>

<details>
<summary>78. Dewey Moore centre</summary>

```json
{"phase":"1A.2","item":"Dewey Moore centre","old":[4.8,1.1],"new":[4.9,0.9],"reason":"Remove supplied allocation spacing conflicts while retaining the Middle Fork group and strip geometry."}
```

</details>

<details>
<summary>79. Mackay Bar centre</summary>

```json
{"phase":"1A.2","item":"Mackay Bar centre","old":[5,1.8],"new":[5.3,1.8],"reason":"Remove supplied allocation spacing conflicts while retaining the Middle Fork group and strip geometry."}
```

</details>

<details>
<summary>80. Chamberlain island elevation target</summary>

```json
{"phase":"1A.2","item":"Chamberlain island elevation target","old":7000,"new":4800,"reason":"Replace unsurveyed legacy 7000-ft-plateau-derived datum with compressed Middle Fork bench target; retain lengths, widths, slopes and one-way rules. Real datum confirmation remains Open for Ty."}
```

</details>

<details>
<summary>81. Big Creek island elevation target</summary>

```json
{"phase":"1A.2","item":"Big Creek island elevation target","old":7000,"new":4800,"reason":"Replace unsurveyed legacy 7000-ft-plateau-derived datum with compressed Middle Fork bench target; retain lengths, widths, slopes and one-way rules. Real datum confirmation remains Open for Ty."}
```

</details>

<details>
<summary>82. Thomas Creek island elevation target</summary>

```json
{"phase":"1A.2","item":"Thomas Creek island elevation target","old":7000,"new":5000,"reason":"Replace unsurveyed legacy 7000-ft-plateau-derived datum with compressed Middle Fork bench target; retain lengths, widths, slopes and one-way rules. Real datum confirmation remains Open for Ty."}
```

</details>

<details>
<summary>83. Sulphur Creek island elevation target</summary>

```json
{"phase":"1A.2","item":"Sulphur Creek island elevation target","old":7000,"new":4800,"reason":"Replace unsurveyed legacy 7000-ft-plateau-derived datum with compressed Middle Fork bench target; retain lengths, widths, slopes and one-way rules. Real datum confirmation remains Open for Ty."}
```

</details>

<details>
<summary>84. Soldier Bar island elevation target</summary>

```json
{"phase":"1A.2","item":"Soldier Bar island elevation target","old":5865,"new":4400,"reason":"Replace unsurveyed legacy 7000-ft-plateau-derived datum with compressed Middle Fork bench target; retain lengths, widths, slopes and one-way rules. Real datum confirmation remains Open for Ty."}
```

</details>

<details>
<summary>85. Cabin Creek island elevation target</summary>

```json
{"phase":"1A.2","item":"Cabin Creek island elevation target","old":6259,"new":4700,"reason":"Replace unsurveyed legacy 7000-ft-plateau-derived datum with compressed Middle Fork bench target; retain lengths, widths, slopes and one-way rules. Real datum confirmation remains Open for Ty."}
```

</details>

<details>
<summary>86. Dewey Moore island elevation target</summary>

```json
{"phase":"1A.2","item":"Dewey Moore island elevation target","old":6094,"new":4600,"reason":"Replace unsurveyed legacy 7000-ft-plateau-derived datum with compressed Middle Fork bench target; retain lengths, widths, slopes and one-way rules. Real datum confirmation remains Open for Ty."}
```

</details>

<details>
<summary>87. Indian Creek island elevation target</summary>

```json
{"phase":"1A.2","item":"Indian Creek island elevation target","old":4841,"new":4500,"reason":"Replace unsurveyed legacy 7000-ft-plateau-derived datum with compressed Middle Fork bench target; retain lengths, widths, slopes and one-way rules. Real datum confirmation remains Open for Ty."}
```

</details>

<details>
<summary>88. Mackay Bar island elevation target</summary>

```json
{"phase":"1A.2","item":"Mackay Bar island elevation target","old":4917,"new":4500,"reason":"Replace unsurveyed legacy 7000-ft-plateau-derived datum with compressed Middle Fork bench target; retain lengths, widths, slopes and one-way rules. Real datum confirmation remains Open for Ty."}
```

</details>

<details>
<summary>89. Terrain clearance envelopes</summary>

```json
{"phase":"1A.2","item":"Terrain clearance envelopes","old":null,"new":{"finals":{"length_km":7.408,"half_width_at_threshold_km":0.15,"widen_per_km":0.05,"outer_blend_km":0.2,"ils_loc_lda_ratio":34,"other_ratio":20,"vertical_margin_m":5},"airways":{"half_width_km":3.704,"outer_blend_km":0.3,"clearance_ft":2025},"order":"cut terrain after landforms; reassert locked field pads and authored peak cores; conflicts remain failures, never lowered by stealth"},"reason":"Continuously lower broad ridge/lowland envelopes to reserve supplied final fans and known airway capsules; keep IFR data unchanged and report protected geometry conflicts."}
```

</details>

<details>
<summary>90. Lake Hood water centre</summary>

```json
{"phase":"1A.2","item":"Lake Hood water centre","old":[-7.96,-1.8],"new":[-9.56,-1.8],"reason":"Translate water with PALH lane, preserving locked 79 ft water plane."}
```

</details>

<details>
<summary>91. PANC:OnTheRunway</summary>

```json
{"phase":"1A.2","item":"PANC:OnTheRunway","old":[-9.047854,-1.547869],"new":[-10.647854,-1.547869],"reason":"Translate dependent start with field."}
```

</details>

<details>
<summary>92. PANC:OnFinal</summary>

```json
{"phase":"1A.2","item":"PANC:OnFinal","old":[-9.275615,-0.697854],"new":[-10.875615,-0.697854],"reason":"Translate dependent start with field."}
```

</details>

<details>
<summary>93. PANC:InTheAir</summary>

```json
{"phase":"1A.2","item":"PANC:InTheAir","old":[-9.327378,-0.504669],"new":[-10.927378,-0.504669],"reason":"Translate dependent start with field."}
```

</details>

<details>
<summary>94. PALH:OnTheRunway</summary>

```json
{"phase":"1A.2","item":"PALH:OnTheRunway","old":[-8.572048,-1.8],"new":[-10.172048,-1.8],"reason":"Translate dependent start with field."}
```

</details>

<details>
<summary>95. PALH:OnFinal</summary>

```json
{"phase":"1A.2","item":"PALH:OnFinal","old":[-9.452048,-1.8],"new":[-11.052048,-1.8],"reason":"Translate dependent start with field."}
```

</details>

<details>
<summary>96. PALH:InTheAir</summary>

```json
{"phase":"1A.2","item":"PALH:InTheAir","old":[-9.652048,-1.8],"new":[-11.252048,-1.8],"reason":"Translate dependent start with field."}
```

</details>

<details>
<summary>97. AB2 start</summary>

```json
{"phase":"1A.2","item":"AB2 start","old":[-10.1,-3.6],"new":[-12,-3.6],"reason":"Follow box relocation."}
```

</details>

<details>
<summary>98. ST-PANC</summary>

```json
{"phase":"1A.2","item":"ST-PANC","old":[[-8.853711,-2.506098],[-8.543128,-3.665209]],"new":[[-10.453711,-2.506098],[-10.143128,-3.665209]],"reason":"Translate shoulder reference with field."}
```

</details>

<details>
<summary>99. ST-PALH</summary>

```json
{"phase":"1A.2","item":"ST-PALH","old":[[-8.56,-1.858651],[-7.36,-1.858651]],"new":[[-10.16,-1.858651],[-8.96,-1.858651]],"reason":"Translate shoulder reference with field."}
```

</details>

<details>
<summary>100. Missing airspace contract</summary>

```json
{"phase":"1A.2","item":"Missing airspace contract","old":null,"new":{"owner":"Codex world design proposals for Claude acceptance; not published IFR","unknown_geometry":["T201 fix positions/segment MEAs","11 non-KSNA missed approaches and holds","BALBO hold speed/turn radius","KAVX circling area"],"rule":"No full-airspace PASS until Claude supplies geometry. New land zone has 1.85 km + 0.20 km guard from known final fan boundaries and airway centrelines; reserve that guard against future routes."},"reason":"Explicitly preserve uncertainty rather than invent Claude-owned coordinates."}
```

</details>

<details>
<summary>101. AP12 north refinement</summary>

```json
{"phase":"1A.2","item":"AP12 north refinement","old":[1.4,-3.5],"new":[1.4,-3.4],"reason":"Additional 100 m north gives grade margin below locked 35% from KSNA."}
```

</details>

<details>
<summary>102. F2 cave mouth position</summary>

```json
{"phase":"1A.2","item":"F2 cave mouth position","old":[2.7,-14.6],"new":[2.633604444,-14.638333492],"reason":"Move 77 m along leeward bearing to the exact Catalina SW shoreline; keep locked island placement and mouth/chamber sizes."}
```

</details>

<details>
<summary>103. F2 explicit cave interior</summary>

```json
{"phase":"1A.2","item":"F2 explicit cave interior","old":null,"new":{"chamber_centre":[2.806809525,-14.538333492],"floor_msl_m":3,"ceiling_msl_m":40,"roof_top_msl_m":55,"roof_min_cover_m":15,"mouth_floor_msl_m":3,"mouth_ceiling_msl_m":29,"throat_length_m":200,"throat_width_m":60,"throat_ceiling_msl_m":29,"skylight_centre":[2.806809525,-14.538333492],"skylight_diameter_m":10,"pads":[{"id":"landing","offset_m":[0,-35],"size_m":[55,20]},{"id":"parking","offset_m":[0,35],"size_m":[40,20]}],"flight_lane":{"axis":"local inward distance from mouth; chamber pads local east/north","half_width_m":10,"floor_msl_m":5,"ceiling_msl_m":23},"contents_bounds":[{"id":"wreck","offset_m":[-65,0],"radius_m":8,"top_msl_m":11},{"id":"beach-driftwood","offset_m":[65,0],"radius_m":10,"top_msl_m":5}],"representation":"closed 3D shell; planar deck, rectangular throat, horizontal chamber roof except open skylight; phase1A.2 analytically verified design solids; no runtime implementation"},"reason":"Make full-volume mathematical clearance possible without sampling the upper-surface bake: flat pads, protected flight lane, bounded content solids, roof and connected throat."}
```

</details>

<details>
<summary>104. CZ5 TERN MESA land MOA</summary>

```json
{"phase":"1A.2","item":"CZ5 TERN MESA land MOA","old":[{"id":"CZ1","name":"KAIWI MOA","bearings_true_deg":[15,50],"radii_km":[14,19.5],"floor_msl_ft_target":500,"ceiling_msl_ft_target":12500,"default_status":"HOT","available_statuses":["HOT","COLD"],"optional":false,"enabled":true,"minimum_clearance_km":1.85,"chart":"magenta hatched boundary; floor-ceiling-status","name_status":"placeholder; unverified against official MOA list","hook_owner":"Claude: HOT may trigger event:combat and dogfight; COLD never triggers","gunnery_targets":[{"id":"CZ1-GT1","centre":[8.056562,14.685173],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ1-GT2","centre":[8.589346,14.728395],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ1-GT3","centre":[8.814083,14.2434],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ1-GT4","centre":[9.3484,14.258679],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ1-GT5","centre":[9.547446,13.762586],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ1-GT6","centre":[10.08183,13.749881],"radius_m":14,"platform":"moored floating bullseye raft","target_top_msl_m":1}],"drone_orbit":{"centre_start":"CZ1","count":3,"radius_m_target":500,"altitude_ft_target":3000,"owner":"Claude opponent/AI logic"}},{"id":"CZ2","name":"PAINTED MOA","bearings_true_deg":[95,135],"radii_km":[13.5,19.5],"floor_msl_ft_target":500,"ceiling_msl_ft_target":12500,"default_status":"HOT","available_statuses":["HOT","COLD"],"optional":false,"enabled":true,"minimum_clearance_km":1.85,"chart":"magenta hatched boundary; floor-ceiling-status","name_status":"placeholder; unverified against official MOA list","hook_owner":"Claude: HOT may trigger event:combat and dogfight; COLD never triggers","gunnery_targets":[{"id":"CZ2-GT1","centre":[15.37813,-5.980228],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ2-GT2","centre":[15.492976,-6.496744],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ2-GT3","centre":[15.044074,-6.776861],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ2-GT4","centre":[15.13173,-7.29868],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ2-GT5","centre":[14.668783,-7.55492],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ2-GT6","centre":[14.729009,-8.080611],"radius_m":14,"platform":"moored floating bullseye raft","target_top_msl_m":1}],"drone_orbit":{"centre_start":"CZ2","count":3,"radius_m_target":500,"altitude_ft_target":3000,"owner":"Claude opponent/AI logic"}},{"id":"CZ3","name":"SUSITNA MOA","bearings_true_deg":[315,350],"radii_km":[14.5,19.5],"floor_msl_ft_target":500,"ceiling_msl_ft_target":12500,"default_status":"HOT","available_statuses":["HOT","COLD"],"optional":false,"enabled":true,"minimum_clearance_km":1.85,"chart":"magenta hatched boundary; floor-ceiling-status","name_status":"placeholder; unverified against official MOA list","hook_owner":"Claude: HOT may trigger event:combat and dogfight; COLD never triggers","gunnery_targets":[{"id":"CZ3-GT1","centre":[-8.819145,14.533502],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ3-GT2","centre":[-8.584546,15.019839],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ3-GT3","centre":[-8.046434,14.975143],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ3-GT4","centre":[-7.786703,15.448536],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ3-GT5","centre":[-7.251669,15.375737],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ3-GT6","centre":[-6.967518,15.834889],"radius_m":14,"platform":"moored floating bullseye raft","target_top_msl_m":1}],"drone_orbit":{"centre_start":"CZ3","count":3,"radius_m_target":500,"altitude_ft_target":3000,"owner":"Claude opponent/AI logic"}},{"id":"CZ4","name":"KACHEMAK MOA","bearings_true_deg":[228,262],"radii_km":[14,19.5],"floor_msl_ft_target":500,"ceiling_msl_ft_target":12500,"default_status":"HOT","available_statuses":["HOT","COLD"],"optional":true,"enabled":false,"minimum_clearance_km":1.85,"chart":"magenta hatched boundary; floor-ceiling-status","name_status":"placeholder; unverified against official MOA list","hook_owner":"Claude: HOT may trigger event:combat and dogfight; COLD never triggers"}],"new":[{"id":"CZ1","name":"KAIWI MOA","bearings_true_deg":[15,50],"radii_km":[14,19.5],"floor_msl_ft_target":500,"ceiling_msl_ft_target":12500,"default_status":"HOT","available_statuses":["HOT","COLD"],"optional":false,"enabled":true,"minimum_clearance_km":1.85,"chart":"magenta hatched boundary; floor-ceiling-status","name_status":"placeholder; unverified against official MOA list","hook_owner":"Claude: HOT may trigger event:combat and dogfight; COLD never triggers","gunnery_targets":[{"id":"CZ1-GT1","centre":[8.056562,14.685173],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ1-GT2","centre":[8.589346,14.728395],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ1-GT3","centre":[8.814083,14.2434],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ1-GT4","centre":[9.3484,14.258679],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ1-GT5","centre":[9.547446,13.762586],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ1-GT6","centre":[10.08183,13.749881],"radius_m":14,"platform":"moored floating bullseye raft","target_top_msl_m":1}],"drone_orbit":{"centre_start":"CZ1","count":3,"radius_m_target":500,"altitude_ft_target":3000,"owner":"Claude opponent/AI logic"}},{"id":"CZ2","name":"PAINTED MOA","bearings_true_deg":[95,135],"radii_km":[13.5,19.5],"floor_msl_ft_target":500,"ceiling_msl_ft_target":12500,"default_status":"HOT","available_statuses":["HOT","COLD"],"optional":false,"enabled":true,"minimum_clearance_km":1.85,"chart":"magenta hatched boundary; floor-ceiling-status","name_status":"placeholder; unverified against official MOA list","hook_owner":"Claude: HOT may trigger event:combat and dogfight; COLD never triggers","gunnery_targets":[{"id":"CZ2-GT1","centre":[15.37813,-5.980228],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ2-GT2","centre":[15.492976,-6.496744],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ2-GT3","centre":[15.044074,-6.776861],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ2-GT4","centre":[15.13173,-7.29868],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ2-GT5","centre":[14.668783,-7.55492],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ2-GT6","centre":[14.729009,-8.080611],"radius_m":14,"platform":"moored floating bullseye raft","target_top_msl_m":1}],"drone_orbit":{"centre_start":"CZ2","count":3,"radius_m_target":500,"altitude_ft_target":3000,"owner":"Claude opponent/AI logic"}},{"id":"CZ3","name":"SUSITNA MOA","bearings_true_deg":[315,350],"radii_km":[14.5,19.5],"floor_msl_ft_target":500,"ceiling_msl_ft_target":12500,"default_status":"HOT","available_statuses":["HOT","COLD"],"optional":false,"enabled":true,"minimum_clearance_km":1.85,"chart":"magenta hatched boundary; floor-ceiling-status","name_status":"placeholder; unverified against official MOA list","hook_owner":"Claude: HOT may trigger event:combat and dogfight; COLD never triggers","gunnery_targets":[{"id":"CZ3-GT1","centre":[-8.819145,14.533502],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ3-GT2","centre":[-8.584546,15.019839],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ3-GT3","centre":[-8.046434,14.975143],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ3-GT4","centre":[-7.786703,15.448536],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ3-GT5","centre":[-7.251669,15.375737],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ3-GT6","centre":[-6.967518,15.834889],"radius_m":14,"platform":"moored floating bullseye raft","target_top_msl_m":1}],"drone_orbit":{"centre_start":"CZ3","count":3,"radius_m_target":500,"altitude_ft_target":3000,"owner":"Claude opponent/AI logic"}},{"id":"CZ4","name":"KACHEMAK MOA","bearings_true_deg":[228,262],"radii_km":[14,19.5],"floor_msl_ft_target":500,"ceiling_msl_ft_target":12500,"default_status":"HOT","available_statuses":["HOT","COLD"],"optional":true,"enabled":false,"minimum_clearance_km":1.85,"chart":"magenta hatched boundary; floor-ceiling-status","name_status":"placeholder; unverified against official MOA list","hook_owner":"Claude: HOT may trigger event:combat and dogfight; COLD never triggers"},{"id":"CZ5","name":"TERN MESA MOA","boundary":[[7.75,-1.0],[8.05,-1.0],[8.05,-0.7],[7.75,-0.7]],"over_land":true,"floor_msl_ft_target":1800,"ceiling_msl_ft_target":3500,"agl_range_ft_bound":[500,3200],"default_status":"COLD","schedule":"No automatic HOT schedule. Manual HOT only after complete Claude airspace clearance; COLD never triggers combat.","available_statuses":["HOT","COLD"],"optional":false,"enabled":true,"minimum_clearance_km":1.85,"additional_margin_km":0.2,"clearance_reference":"airway centreline; final fan boundary. 2 nm airway terrain-check swath is not a route boundary supplied by Claude.","chart":"magenta hatched boundary; name, floor/ceiling, status; full airspace acceptance pending","name_status":"fictional design name; FAA exact-name search 2026-10-30 found no match, not authoritative uniqueness","hook_owner":"Claude: manual HOT may trigger combat; COLD never triggers","gunnery_targets":[{"id":"CZ5-GT1","centre":[7.9,-0.85],"radius_m":14,"platform":"ground bullseye","top_agl_m":0.2}],"drone_orbit":{"centre_start":"CZ5","count":3,"radius_m_target":70,"altitude_ft_target":2600,"owner":"Claude opponent/AI logic"}}],"reason":"Add 300 x 300 m overland combat/gunnery zone with explicit polygon, vertical limits and conditional manual HOT scheduling."}
```

</details>

<details>
<summary>105. CZ5 combat start</summary>

```json
{"phase":"1A.2","item":"CZ5 combat start","old":[{"id":"PHDH:OnTheRunway","airport":"PHDH","group":"coastal","mode":"OnTheRunway","centre":[3.69753,5.92256],"altitude_ft_target":14,"heading_true_deg":91,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"PHDH:OnFinal","airport":"PHDH","group":"coastal","mode":"OnFinal","centre":[2.817664,5.937918],"altitude_ft_target":314,"heading_true_deg":91,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"PHDH:InTheAir","airport":"PHDH","group":"coastal","mode":"InTheAir","centre":[2.617695,5.941409],"altitude_ft_target":1514,"heading_true_deg":91,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"PANC:OnTheRunway","airport":"PANC","group":"coastal","mode":"OnTheRunway","centre":[-10.647854,-1.547869],"altitude_ft_target":151,"heading_true_deg":165,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"PANC:OnFinal","airport":"PANC","group":"coastal","mode":"OnFinal","centre":[-10.875615,-0.697854],"altitude_ft_target":451,"heading_true_deg":165,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"PANC:InTheAir","airport":"PANC","group":"coastal","mode":"InTheAir","centre":[-10.927378,-0.504669],"altitude_ft_target":1651,"heading_true_deg":165,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"PALH:OnTheRunway","airport":"PALH","group":"PANC float variant","mode":"OnTheRunway","centre":[-10.172048,-1.8],"altitude_ft_target":79,"heading_true_deg":90,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"PALH:OnFinal","airport":"PALH","group":"PANC float variant","mode":"OnFinal","centre":[-11.052048,-1.8],"altitude_ft_target":379,"heading_true_deg":90,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"PALH:InTheAir","airport":"PALH","group":"PANC float variant","mode":"InTheAir","centre":[-11.252048,-1.8],"altitude_ft_target":1579,"heading_true_deg":90,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"PATK:OnTheRunway","airport":"PATK","group":"coastal","mode":"OnTheRunway","centre":[-6.545839,4.586018],"altitude_ft_target":365,"heading_true_deg":27,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"PATK:OnFinal","airport":"PATK","group":"coastal","mode":"OnFinal","centre":[-6.945351,3.801932],"altitude_ft_target":665,"heading_true_deg":27,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"PATK:InTheAir","airport":"PATK","group":"coastal","mode":"InTheAir","centre":[-7.036149,3.623731],"altitude_ft_target":1865,"heading_true_deg":27,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"KSNA:OnTheRunway","airport":"KSNA","group":"coastal","mode":"OnTheRunway","centre":[0.589737,-8.756363],"altitude_ft_target":56,"heading_true_deg":28,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"KSNA:OnFinal","airport":"KSNA","group":"coastal","mode":"OnFinal","centre":[0.176602,-9.533357],"altitude_ft_target":356,"heading_true_deg":28,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"KSNA:InTheAir","airport":"KSNA","group":"coastal","mode":"InTheAir","centre":[0.082708,-9.709947],"altitude_ft_target":1556,"heading_true_deg":28,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"KMYL:OnTheRunway","airport":"KMYL","group":"inland","mode":"OnTheRunway","centre":[-0.594831,3.629663],"altitude_ft_target":5024,"heading_true_deg":179,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"KMYL:OnFinal","airport":"KMYL","group":"inland","mode":"OnFinal","centre":[-0.610189,4.509529],"altitude_ft_target":5324,"heading_true_deg":179,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"KMYL:InTheAir","airport":"KMYL","group":"inland","mode":"InTheAir","centre":[-0.61368,4.709498],"altitude_ft_target":6524,"heading_true_deg":179,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"3U2:OnTheRunway","airport":"3U2","group":"inland","mode":"OnTheRunway","centre":[1.911099,-0.431415],"altitude_ft_target":4960,"heading_true_deg":192,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"3U2:OnFinal","airport":"3U2","group":"inland","mode":"OnFinal","centre":[2.094061,0.429355],"altitude_ft_target":5260,"heading_true_deg":192,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"3U2:InTheAir","airport":"3U2","group":"inland","mode":"InTheAir","centre":[2.135643,0.624985],"altitude_ft_target":6460,"heading_true_deg":192,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"KMEV:OnTheRunway","airport":"KMEV","group":"inland","mode":"OnTheRunway","centre":[-3.02,-0.682392],"altitude_ft_target":4724,"heading_true_deg":180,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"KMEV:OnFinal","airport":"KMEV","group":"inland","mode":"OnFinal","centre":[-3.02,0.197608],"altitude_ft_target":5024,"heading_true_deg":180,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"KMEV:InTheAir","airport":"KMEV","group":"inland","mode":"InTheAir","centre":[-3.02,0.397608],"altitude_ft_target":6224,"heading_true_deg":180,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"KPGA:OnTheRunway","airport":"KPGA","group":"inland","mode":"OnTheRunway","centre":[3.596431,-2.445781],"altitude_ft_target":4317,"heading_true_deg":170,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"KPGA:OnFinal","airport":"KPGA","group":"inland","mode":"OnFinal","centre":[3.443621,-1.57915],"altitude_ft_target":4617,"heading_true_deg":170,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"KPGA:InTheAir","airport":"KPGA","group":"inland","mode":"InTheAir","centre":[3.408891,-1.382188],"altitude_ft_target":5817,"heading_true_deg":170,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"AB1","mode":"InAeroBox","centre":[6.9,7.6],"altitude_ft_target":2300,"heading_true_deg":0},{"id":"AB2","mode":"InAeroBox","centre":[-12,-3.6],"altitude_ft_target":2300,"heading_true_deg":0},{"id":"AB3","mode":"InAeroBox","centre":[-2.5,4],"altitude_ft_target":7300,"heading_true_deg":0},{"id":"CZ1","mode":"InCombatZone","enabled":true,"centre":[8.999768,14.126807],"altitude_ft_target":5000,"heading_true_deg":122.5},{"id":"CZ2","mode":"InCombatZone","enabled":true,"centre":[14.954078,-6.973201],"altitude_ft_target":5000,"heading_true_deg":205.0},{"id":"CZ3","mode":"InCombatZone","enabled":true,"centre":[-7.849726,15.079184],"altitude_ft_target":5000,"heading_true_deg":62.5},{"id":"CZ4","mode":"InCombatZone","enabled":false,"centre":[-15.180655,-7.078856],"altitude_ft_target":5000,"heading_true_deg":335.0},{"id":"S1","mode":"InThermal","centre":[4.3,4],"altitude_agl_ft_target":1500,"owner":"Codex position; Claude lift and spawn trim"},{"id":"S2","mode":"InThermal","centre":[-3.71,-1],"altitude_agl_ft_target":1500,"owner":"Codex position; Claude lift and spawn trim"},{"id":"S3","mode":"InThermal","centre":[4.5,-4.6],"altitude_agl_ft_target":1500,"owner":"Codex position; Claude lift and spawn trim"},{"id":"S4","mode":"InThermal","centre":[0.2,2.4],"altitude_agl_ft_target":1500,"owner":"Codex position; Claude lift and spawn trim"}],"new":[{"id":"PHDH:OnTheRunway","airport":"PHDH","group":"coastal","mode":"OnTheRunway","centre":[3.69753,5.92256],"altitude_ft_target":14,"heading_true_deg":91,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"PHDH:OnFinal","airport":"PHDH","group":"coastal","mode":"OnFinal","centre":[2.817664,5.937918],"altitude_ft_target":314,"heading_true_deg":91,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"PHDH:InTheAir","airport":"PHDH","group":"coastal","mode":"InTheAir","centre":[2.617695,5.941409],"altitude_ft_target":1514,"heading_true_deg":91,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"PANC:OnTheRunway","airport":"PANC","group":"coastal","mode":"OnTheRunway","centre":[-10.647854,-1.547869],"altitude_ft_target":151,"heading_true_deg":165,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"PANC:OnFinal","airport":"PANC","group":"coastal","mode":"OnFinal","centre":[-10.875615,-0.697854],"altitude_ft_target":451,"heading_true_deg":165,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"PANC:InTheAir","airport":"PANC","group":"coastal","mode":"InTheAir","centre":[-10.927378,-0.504669],"altitude_ft_target":1651,"heading_true_deg":165,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"PALH:OnTheRunway","airport":"PALH","group":"PANC float variant","mode":"OnTheRunway","centre":[-10.172048,-1.8],"altitude_ft_target":79,"heading_true_deg":90,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"PALH:OnFinal","airport":"PALH","group":"PANC float variant","mode":"OnFinal","centre":[-11.052048,-1.8],"altitude_ft_target":379,"heading_true_deg":90,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"PALH:InTheAir","airport":"PALH","group":"PANC float variant","mode":"InTheAir","centre":[-11.252048,-1.8],"altitude_ft_target":1579,"heading_true_deg":90,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"PATK:OnTheRunway","airport":"PATK","group":"coastal","mode":"OnTheRunway","centre":[-6.545839,4.586018],"altitude_ft_target":365,"heading_true_deg":27,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"PATK:OnFinal","airport":"PATK","group":"coastal","mode":"OnFinal","centre":[-6.945351,3.801932],"altitude_ft_target":665,"heading_true_deg":27,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"PATK:InTheAir","airport":"PATK","group":"coastal","mode":"InTheAir","centre":[-7.036149,3.623731],"altitude_ft_target":1865,"heading_true_deg":27,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"KSNA:OnTheRunway","airport":"KSNA","group":"coastal","mode":"OnTheRunway","centre":[0.589737,-8.756363],"altitude_ft_target":56,"heading_true_deg":28,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"KSNA:OnFinal","airport":"KSNA","group":"coastal","mode":"OnFinal","centre":[0.176602,-9.533357],"altitude_ft_target":356,"heading_true_deg":28,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"KSNA:InTheAir","airport":"KSNA","group":"coastal","mode":"InTheAir","centre":[0.082708,-9.709947],"altitude_ft_target":1556,"heading_true_deg":28,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"KMYL:OnTheRunway","airport":"KMYL","group":"inland","mode":"OnTheRunway","centre":[-0.594831,3.629663],"altitude_ft_target":5024,"heading_true_deg":179,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"KMYL:OnFinal","airport":"KMYL","group":"inland","mode":"OnFinal","centre":[-0.610189,4.509529],"altitude_ft_target":5324,"heading_true_deg":179,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"KMYL:InTheAir","airport":"KMYL","group":"inland","mode":"InTheAir","centre":[-0.61368,4.709498],"altitude_ft_target":6524,"heading_true_deg":179,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"3U2:OnTheRunway","airport":"3U2","group":"inland","mode":"OnTheRunway","centre":[1.911099,-0.431415],"altitude_ft_target":4960,"heading_true_deg":192,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"3U2:OnFinal","airport":"3U2","group":"inland","mode":"OnFinal","centre":[2.094061,0.429355],"altitude_ft_target":5260,"heading_true_deg":192,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"3U2:InTheAir","airport":"3U2","group":"inland","mode":"InTheAir","centre":[2.135643,0.624985],"altitude_ft_target":6460,"heading_true_deg":192,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"KMEV:OnTheRunway","airport":"KMEV","group":"inland","mode":"OnTheRunway","centre":[-3.02,-0.682392],"altitude_ft_target":4724,"heading_true_deg":180,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"KMEV:OnFinal","airport":"KMEV","group":"inland","mode":"OnFinal","centre":[-3.02,0.197608],"altitude_ft_target":5024,"heading_true_deg":180,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"KMEV:InTheAir","airport":"KMEV","group":"inland","mode":"InTheAir","centre":[-3.02,0.397608],"altitude_ft_target":6224,"heading_true_deg":180,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"KPGA:OnTheRunway","airport":"KPGA","group":"inland","mode":"OnTheRunway","centre":[3.596431,-2.445781],"altitude_ft_target":4317,"heading_true_deg":170,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"KPGA:OnFinal","airport":"KPGA","group":"inland","mode":"OnFinal","centre":[3.443621,-1.57915],"altitude_ft_target":4617,"heading_true_deg":170,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"KPGA:InTheAir","airport":"KPGA","group":"inland","mode":"InTheAir","centre":[3.408891,-1.382188],"altitude_ft_target":5817,"heading_true_deg":170,"note":"geometry candidate; Claude chooses runway, aircraft trim and clearance"},{"id":"AB1","mode":"InAeroBox","centre":[6.9,7.6],"altitude_ft_target":2300,"heading_true_deg":0},{"id":"AB2","mode":"InAeroBox","centre":[-12,-3.6],"altitude_ft_target":2300,"heading_true_deg":0},{"id":"AB3","mode":"InAeroBox","centre":[-2.5,4],"altitude_ft_target":7300,"heading_true_deg":0},{"id":"CZ1","mode":"InCombatZone","enabled":true,"centre":[8.999768,14.126807],"altitude_ft_target":5000,"heading_true_deg":122.5},{"id":"CZ2","mode":"InCombatZone","enabled":true,"centre":[14.954078,-6.973201],"altitude_ft_target":5000,"heading_true_deg":205.0},{"id":"CZ3","mode":"InCombatZone","enabled":true,"centre":[-7.849726,15.079184],"altitude_ft_target":5000,"heading_true_deg":62.5},{"id":"CZ4","mode":"InCombatZone","enabled":false,"centre":[-15.180655,-7.078856],"altitude_ft_target":5000,"heading_true_deg":335.0},{"id":"S1","mode":"InThermal","centre":[4.3,4],"altitude_agl_ft_target":1500,"owner":"Codex position; Claude lift and spawn trim"},{"id":"S2","mode":"InThermal","centre":[-3.71,-1],"altitude_agl_ft_target":1500,"owner":"Codex position; Claude lift and spawn trim"},{"id":"S3","mode":"InThermal","centre":[4.5,-4.6],"altitude_agl_ft_target":1500,"owner":"Codex position; Claude lift and spawn trim"},{"id":"S4","mode":"InThermal","centre":[0.2,2.4],"altitude_agl_ft_target":1500,"owner":"Codex position; Claude lift and spawn trim"},{"id":"CZ5","mode":"InCombatZone","enabled":true,"centre":[7.9,-0.85],"altitude_ft_target":2600,"heading_true_deg":0}],"reason":"Provide a start inside the new land volume, clear of terrain and bounded drone orbit."}
```

</details>

<details>
<summary>106. Final envelope vertical margin</summary>

```json
{"phase":"1A.2","item":"Final envelope vertical margin","old":5,"new":15,"reason":"15 m below the protected surface accommodates coarse bilinear interpolation; 150 m lateral/end guard then 200 m outer blend."}
```

</details>

<details>
<summary>107. Final envelope lateral/end guard</summary>

```json
{"phase":"1A.2","item":"Final envelope lateral/end guard","old":null,"new":0.15,"reason":"Extend the full cut around the protected fan so intersecting grid-cell vertices are constrained too."}
```

</details>

<details>
<summary>108. Airway terrain envelope guard</summary>

```json
{"phase":"1A.2","item":"Airway terrain envelope guard","old":null,"new":0.15,"reason":"2 nm terrain protection plus 150 m full cut guard and 300 m smooth outer fade."}
```

</details>

<details>
<summary>109. aerobatic_boxes/AB1</summary>

```json
{"phase":"1A.2","item":"aerobatic_boxes/AB1","old":{"id":"AB1","name":"Dillingham","centre":[6.9,7.6],"airport":"PHDH","density_altitude":"low","side_m":1000,"floor_agl_m":100,"ceiling_agl_m":1100,"centre_ground_ft":0,"floor_msl_ft_target":328,"ceiling_msl_ft_target":3609,"judges_offset_m":150,"judges_side":"south","markers":{"colour":"white","cross_arm_m":40,"cross_width_m":4,"corner_L_leg_m":30,"corner_width_m":4,"side_T_leg_m":30,"side_T_width_m":4,"rafts":true,"orange_corner_buoys":true,"shore_judges_marker":true},"judges_shore_marker_status":"target: align shore reference with judges line; exact intersection pending shore geometry review"},"new":{"id":"AB1","name":"Dillingham","centre":[6.9,7.6],"airport":"PHDH","density_altitude":"low","side_m":1000,"floor_agl_m":100,"ceiling_agl_m":1100,"centre_ground_ft":0,"floor_msl_ft_target":328,"ceiling_msl_ft_target":3609,"judges_offset_m":150,"judges_side":"south","markers":{"colour":"white","cross_arm_m":40,"cross_width_m":4,"corner_L_leg_m":30,"corner_width_m":4,"side_T_leg_m":30,"side_T_width_m":4,"rafts":true,"orange_corner_buoys":true,"shore_judges_marker":true},"judges_shore_marker_status":"resolved: line raft plus aligned shore reference; no false claim that offshore judges line intersects land","rafts":[{"id":"AB1-R1","shape":"cross","centre":[6.9,7.6],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB1-R2","shape":"L","centre":[6.4,7.1],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB1-R3","shape":"L","centre":[6.4,8.1],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB1-R4","shape":"L","centre":[7.4,7.1],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB1-R5","shape":"L","centre":[7.4,8.1],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB1-R6","shape":"T","centre":[6.9,8.1],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB1-R7","shape":"T","centre":[6.9,7.1],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB1-R8","shape":"T","centre":[7.4,7.6],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB1-R9","shape":"T","centre":[6.4,7.6],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"}],"corner_buoys":[{"centre":[6.4,7.1],"colour":"orange","moored":true},{"centre":[6.4,8.1],"colour":"orange","moored":true},{"centre":[7.4,7.1],"colour":"orange","moored":true},{"centre":[7.4,8.1],"colour":"orange","moored":true}],"judges_line":{"centre":[6.9,6.949999999999999],"length_m":1000,"orientation_true_deg":90,"marker":"moored judge-reference raft on line; matching shore reference aligned along airport-facing axis"},"shore_reference_marker":{"centre":[6.9,6.0],"meaning":"shore sighting reference aligned to offshore judges line; actual line remains 150 m beyond square"}},"reason":"Ty decisions 2026-10-30 and fit revision: add perimeter islands/beaches, remove CZ4, explicit hot-entry behavior/rafts, or fix spacing/grade and protect land MOA."}
```

</details>

<details>
<summary>110. aerobatic_boxes/AB2</summary>

```json
{"phase":"1A.2","item":"aerobatic_boxes/AB2","old":{"id":"AB2","name":"Cook Inlet","centre":[-12.0,-3.6],"airport":"PANC","density_altitude":"low","side_m":1000,"floor_agl_m":100,"ceiling_agl_m":1100,"centre_ground_ft":0,"floor_msl_ft_target":328,"ceiling_msl_ft_target":3609,"judges_offset_m":150,"judges_side":"east","markers":{"colour":"white","cross_arm_m":40,"cross_width_m":4,"corner_L_leg_m":30,"corner_width_m":4,"side_T_leg_m":30,"side_T_width_m":4,"rafts":true,"orange_corner_buoys":true,"shore_judges_marker":true}},"new":{"id":"AB2","name":"Cook Inlet","centre":[-12.3,-3.6],"airport":"PANC","density_altitude":"low","side_m":1000,"floor_agl_m":100,"ceiling_agl_m":1100,"centre_ground_ft":0,"floor_msl_ft_target":328,"ceiling_msl_ft_target":3609,"judges_offset_m":150,"judges_side":"east","markers":{"colour":"white","cross_arm_m":40,"cross_width_m":4,"corner_L_leg_m":30,"corner_width_m":4,"side_T_leg_m":30,"side_T_width_m":4,"rafts":true,"orange_corner_buoys":true,"shore_judges_marker":true},"rafts":[{"id":"AB2-R1","shape":"cross","centre":[-12.3,-3.6],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R2","shape":"L","centre":[-12.8,-4.1],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R3","shape":"L","centre":[-12.8,-3.1],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R4","shape":"L","centre":[-11.8,-4.1],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R5","shape":"L","centre":[-11.8,-3.1],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R6","shape":"T","centre":[-12.3,-3.1],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R7","shape":"T","centre":[-12.3,-4.1],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R8","shape":"T","centre":[-11.8,-3.6],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R9","shape":"T","centre":[-12.8,-3.6],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"}],"corner_buoys":[{"centre":[-12.8,-4.1],"colour":"orange","moored":true},{"centre":[-12.8,-3.1],"colour":"orange","moored":true},{"centre":[-11.8,-4.1],"colour":"orange","moored":true},{"centre":[-11.8,-3.1],"colour":"orange","moored":true}],"judges_line":{"centre":[-11.65,-3.6],"length_m":1000,"orientation_true_deg":0,"marker":"moored judge-reference raft on line; matching shore reference aligned along airport-facing axis"},"shore_reference_marker":{"centre":[-11.4,-3.6],"meaning":"shore sighting reference aligned to offshore judges line; actual line remains 150 m beyond square"},"judges_shore_marker_status":"resolved: line raft plus aligned shore reference; no false claim that offshore judges line intersects land"},"reason":"Ty decisions 2026-10-30 and fit revision: add perimeter islands/beaches, remove CZ4, explicit hot-entry behavior/rafts, or fix spacing/grade and protect land MOA."}
```

</details>

<details>
<summary>111. airparks/AP12</summary>

```json
{"phase":"1A.2","item":"airparks/AP12","old":{"id":"AP12","name":"Bullfrog","centre":[1.4,-3.4],"heading_true_deg":167,"numbers_magnetic":"16/34","surface":"gravel","length_ft":2500,"width_ft":60,"gear":"wheels; floats at marina","elevation_ft":3800,"elevation_status":"target; fictional field","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":250},"buildings":{"hangars":3,"houses":5,"windsocks":1,"parked_aircraft":3,"setback_m":100}},"new":{"id":"AP12","name":"Bullfrog","centre":[1.4,-3.4],"heading_true_deg":167,"numbers_magnetic":"16/34","surface":"gravel","length_ft":2500,"width_ft":60,"gear":"wheels; floats at marina","elevation_ft":4100,"elevation_status":"target; fictional field","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":250},"buildings":{"hangars":3,"houses":5,"windsocks":1,"parked_aircraft":3,"setback_m":100}},"reason":"Ty decisions 2026-10-30 and fit revision: add perimeter islands/beaches, remove CZ4, explicit hot-entry behavior/rafts, or fix spacing/grade and protect land MOA."}
```

</details>

<details>
<summary>112. airports/NHVN</summary>

```json
{"phase":"1A.2","item":"airports/NHVN","old":null,"new":{"id":"NHVN","name":"North Haven","elevation_ft":55,"elevation_status":"fictional design target","start_group":"outer island non-start","runways":[{"id":"35/17","centre":[0,10.3],"heading_true_deg":0,"length_ft":2952.755906,"width_ft":98.425197,"surface":"gravel","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"shore_class":"coastal","island":"OI3"}]},"reason":"Ty decisions 2026-10-30 and fit revision: add perimeter islands/beaches, remove CZ4, explicit hot-entry behavior/rafts, or fix spacing/grade and protect land MOA."}
```

</details>

<details>
<summary>113. airports/SITX</summary>

```json
{"phase":"1A.2","item":"airports/SITX","old":null,"new":{"id":"SITX","name":"Sitka Sound waterfront","elevation_ft":30,"elevation_status":"fictional design target","start_group":"outer island non-start","runways":[{"id":"35/17","centre":[-15,1.1],"heading_true_deg":0,"length_ft":3608.923885,"width_ft":98.425197,"surface":"asphalt","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"shore_class":"coastal","island":"OI1"}]},"reason":"Ty decisions 2026-10-30 and fit revision: add perimeter islands/beaches, remove CZ4, explicit hot-entry behavior/rafts, or fix spacing/grade and protect land MOA."}
```

</details>

<details>
<summary>114. airports/TERN</summary>

```json
{"phase":"1A.2","item":"airports/TERN","old":null,"new":{"id":"TERN","name":"Tern perimeter","elevation_ft":45,"elevation_status":"fictional design target","start_group":"outer island non-start","runways":[{"id":"35/17","centre":[11.3,2],"heading_true_deg":0,"length_ft":2952.755906,"width_ft":98.425197,"surface":"gravel","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"shore_class":"coastal","island":"OI2"}]},"reason":"Ty decisions 2026-10-30 and fit revision: add perimeter islands/beaches, remove CZ4, explicit hot-entry behavior/rafts, or fix spacing/grade and protect land MOA."}
```

</details>

<details>
<summary>115. airspace_reservations</summary>

```json
{"phase":"1A.2","item":"airspace_reservations","old":{"owner":"Codex world design proposals for Claude acceptance; not published IFR","unknown_geometry":["T201 fix positions/segment MEAs","11 non-KSNA missed approaches and holds","BALBO hold speed/turn radius","KAVX circling area"],"rule":"No full-airspace PASS until Claude supplies geometry. New land zone has 1.85 km + 0.20 km guard from known final fan boundaries and airway centrelines; reserve that guard against future routes."},"new":{"owner":"Codex world design proposals for Claude acceptance; not published IFR","unknown_geometry":["T201 fix positions/segment MEAs","11 non-KSNA missed approaches and holds","BALBO hold speed/turn radius","KAVX circling area"],"rule":"No full-airspace PASS until Claude supplies geometry. New land zone has 1.85 km + 0.20 km guard from known final fan boundaries and airway centrelines; reserve that guard against future routes.","new_outer_procedures":"Claude to fit perimeter instrument routes in protected reservations; no additions to ifr section"},"reason":"Ty decisions and explicit deterministic design geometry for full fit verification."}
```

</details>

<details>
<summary>116. combat_zones/CZ1</summary>

```json
{"phase":"1A.2","item":"combat_zones/CZ1","old":{"id":"CZ1","name":"KAIWI MOA","bearings_true_deg":[15,50],"radii_km":[14,19.5],"floor_msl_ft_target":500,"ceiling_msl_ft_target":12500,"default_status":"HOT","available_statuses":["HOT","COLD"],"optional":false,"enabled":true,"minimum_clearance_km":1.85,"chart":"magenta hatched boundary; floor-ceiling-status","name_status":"placeholder; unverified against official MOA list","hook_owner":"Claude: HOT may trigger event:combat and dogfight; COLD never triggers","gunnery_targets":[{"id":"CZ1-GT1","centre":[8.056562,14.685173],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ1-GT2","centre":[8.589346,14.728395],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ1-GT3","centre":[8.814083,14.2434],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ1-GT4","centre":[9.3484,14.258679],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ1-GT5","centre":[9.547446,13.762586],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ1-GT6","centre":[10.08183,13.749881],"radius_m":14,"platform":"moored floating bullseye raft","target_top_msl_m":1}],"drone_orbit":{"centre_start":"CZ1","count":3,"radius_m_target":500,"altitude_ft_target":3000,"owner":"Claude opponent/AI logic"}},"new":{"id":"CZ1","name":"KAIWI MOA","bearings_true_deg":[15,50],"radii_km":[14,19.5],"floor_msl_ft_target":500,"ceiling_msl_ft_target":12500,"default_status":"HOT","available_statuses":["HOT","COLD"],"optional":false,"enabled":true,"minimum_clearance_km":1.85,"chart":"magenta hatched boundary; floor-ceiling-status","name_status":"placeholder; unverified against official MOA list","hook_owner":"Claude: HOT may trigger event:combat and dogfight; COLD never triggers","gunnery_targets":[{"id":"CZ1-GT1","centre":[8.056562,14.685173],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ1-GT2","centre":[8.589346,14.728395],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ1-GT3","centre":[8.814083,14.2434],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ1-GT4","centre":[9.3484,14.258679],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ1-GT5","centre":[9.547446,13.762586],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ1-GT6","centre":[10.08183,13.749881],"radius_m":14,"platform":"moored floating bullseye raft","target_top_msl_m":1}],"drone_orbit":{"centre_start":"CZ1","count":3,"radius_m_target":500,"altitude_ft_target":3000,"owner":"Claude opponent/AI logic"},"hot_entry_behavior":{"HOT":"On user entry, spawn this zone’s targets and drones; engagement hook belongs to Claude.","COLD":"No targets/drones appear on entry and no combat triggers."}},"reason":"Ty decisions 2026-10-30 and fit revision: add perimeter islands/beaches, remove CZ4, explicit hot-entry behavior/rafts, or fix spacing/grade and protect land MOA."}
```

</details>

<details>
<summary>117. combat_zones/CZ2</summary>

```json
{"phase":"1A.2","item":"combat_zones/CZ2","old":{"id":"CZ2","name":"PAINTED MOA","bearings_true_deg":[95,135],"radii_km":[13.5,19.5],"floor_msl_ft_target":500,"ceiling_msl_ft_target":12500,"default_status":"HOT","available_statuses":["HOT","COLD"],"optional":false,"enabled":true,"minimum_clearance_km":1.85,"chart":"magenta hatched boundary; floor-ceiling-status","name_status":"placeholder; unverified against official MOA list","hook_owner":"Claude: HOT may trigger event:combat and dogfight; COLD never triggers","gunnery_targets":[{"id":"CZ2-GT1","centre":[15.37813,-5.980228],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ2-GT2","centre":[15.492976,-6.496744],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ2-GT3","centre":[15.044074,-6.776861],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ2-GT4","centre":[15.13173,-7.29868],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ2-GT5","centre":[14.668783,-7.55492],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ2-GT6","centre":[14.729009,-8.080611],"radius_m":14,"platform":"moored floating bullseye raft","target_top_msl_m":1}],"drone_orbit":{"centre_start":"CZ2","count":3,"radius_m_target":500,"altitude_ft_target":3000,"owner":"Claude opponent/AI logic"}},"new":{"id":"CZ2","name":"PAINTED MOA","bearings_true_deg":[95,135],"radii_km":[13.5,19.5],"floor_msl_ft_target":500,"ceiling_msl_ft_target":12500,"default_status":"HOT","available_statuses":["HOT","COLD"],"optional":false,"enabled":true,"minimum_clearance_km":1.85,"chart":"magenta hatched boundary; floor-ceiling-status","name_status":"placeholder; unverified against official MOA list","hook_owner":"Claude: HOT may trigger event:combat and dogfight; COLD never triggers","gunnery_targets":[{"id":"CZ2-GT1","centre":[15.37813,-5.980228],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ2-GT2","centre":[15.492976,-6.496744],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ2-GT3","centre":[15.044074,-6.776861],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ2-GT4","centre":[15.13173,-7.29868],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ2-GT5","centre":[14.668783,-7.55492],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ2-GT6","centre":[14.729009,-8.080611],"radius_m":14,"platform":"moored floating bullseye raft","target_top_msl_m":1}],"drone_orbit":{"centre_start":"CZ2","count":3,"radius_m_target":500,"altitude_ft_target":3000,"owner":"Claude opponent/AI logic"},"hot_entry_behavior":{"HOT":"On user entry, spawn this zone’s targets and drones; engagement hook belongs to Claude.","COLD":"No targets/drones appear on entry and no combat triggers."}},"reason":"Ty decisions 2026-10-30 and fit revision: add perimeter islands/beaches, remove CZ4, explicit hot-entry behavior/rafts, or fix spacing/grade and protect land MOA."}
```

</details>

<details>
<summary>118. combat_zones/CZ3</summary>

```json
{"phase":"1A.2","item":"combat_zones/CZ3","old":{"id":"CZ3","name":"SUSITNA MOA","bearings_true_deg":[315,350],"radii_km":[14.5,19.5],"floor_msl_ft_target":500,"ceiling_msl_ft_target":12500,"default_status":"HOT","available_statuses":["HOT","COLD"],"optional":false,"enabled":true,"minimum_clearance_km":1.85,"chart":"magenta hatched boundary; floor-ceiling-status","name_status":"placeholder; unverified against official MOA list","hook_owner":"Claude: HOT may trigger event:combat and dogfight; COLD never triggers","gunnery_targets":[{"id":"CZ3-GT1","centre":[-8.819145,14.533502],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ3-GT2","centre":[-8.584546,15.019839],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ3-GT3","centre":[-8.046434,14.975143],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ3-GT4","centre":[-7.786703,15.448536],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ3-GT5","centre":[-7.251669,15.375737],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ3-GT6","centre":[-6.967518,15.834889],"radius_m":14,"platform":"moored floating bullseye raft","target_top_msl_m":1}],"drone_orbit":{"centre_start":"CZ3","count":3,"radius_m_target":500,"altitude_ft_target":3000,"owner":"Claude opponent/AI logic"}},"new":{"id":"CZ3","name":"SUSITNA MOA","bearings_true_deg":[315,350],"radii_km":[14.5,19.5],"floor_msl_ft_target":500,"ceiling_msl_ft_target":12500,"default_status":"HOT","available_statuses":["HOT","COLD"],"optional":false,"enabled":true,"minimum_clearance_km":1.85,"chart":"magenta hatched boundary; floor-ceiling-status","name_status":"placeholder; unverified against official MOA list","hook_owner":"Claude: HOT may trigger event:combat and dogfight; COLD never triggers","gunnery_targets":[{"id":"CZ3-GT1","centre":[-8.819145,14.533502],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ3-GT2","centre":[-8.584546,15.019839],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ3-GT3","centre":[-8.046434,14.975143],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ3-GT4","centre":[-7.786703,15.448536],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ3-GT5","centre":[-7.251669,15.375737],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ3-GT6","centre":[-6.967518,15.834889],"radius_m":14,"platform":"moored floating bullseye raft","target_top_msl_m":1}],"drone_orbit":{"centre_start":"CZ3","count":3,"radius_m_target":500,"altitude_ft_target":3000,"owner":"Claude opponent/AI logic"},"hot_entry_behavior":{"HOT":"On user entry, spawn this zone’s targets and drones; engagement hook belongs to Claude.","COLD":"No targets/drones appear on entry and no combat triggers."}},"reason":"Ty decisions 2026-10-30 and fit revision: add perimeter islands/beaches, remove CZ4, explicit hot-entry behavior/rafts, or fix spacing/grade and protect land MOA."}
```

</details>

<details>
<summary>119. combat_zones/CZ4</summary>

```json
{"phase":"1A.2","item":"combat_zones/CZ4","old":{"id":"CZ4","name":"KACHEMAK MOA","bearings_true_deg":[228,262],"radii_km":[14,19.5],"floor_msl_ft_target":500,"ceiling_msl_ft_target":12500,"default_status":"HOT","available_statuses":["HOT","COLD"],"optional":true,"enabled":false,"minimum_clearance_km":1.85,"chart":"magenta hatched boundary; floor-ceiling-status","name_status":"placeholder; unverified against official MOA list","hook_owner":"Claude: HOT may trigger event:combat and dogfight; COLD never triggers"},"new":null,"reason":"Ty decisions 2026-10-30 and fit revision: add perimeter islands/beaches, remove CZ4, explicit hot-entry behavior/rafts, or fix spacing/grade and protect land MOA."}
```

</details>

<details>
<summary>120. combat_zones/CZ5</summary>

```json
{"phase":"1A.2","item":"combat_zones/CZ5","old":{"id":"CZ5","name":"TERN MESA MOA","boundary":[[7.75,-1.0],[8.05,-1.0],[8.05,-0.7],[7.75,-0.7]],"over_land":true,"floor_msl_ft_target":1800,"ceiling_msl_ft_target":3500,"agl_range_ft_bound":[500,3200],"default_status":"COLD","schedule":"No automatic HOT schedule. Manual HOT only after complete Claude airspace clearance; COLD never triggers combat.","available_statuses":["HOT","COLD"],"optional":false,"enabled":true,"minimum_clearance_km":1.85,"additional_margin_km":0.2,"clearance_reference":"airway centreline; final fan boundary. 2 nm airway terrain-check swath is not a route boundary supplied by Claude.","chart":"magenta hatched boundary; name, floor/ceiling, status; full airspace acceptance pending","name_status":"fictional design name; FAA exact-name search 2026-10-30 found no match, not authoritative uniqueness","hook_owner":"Claude: manual HOT may trigger combat; COLD never triggers","gunnery_targets":[{"id":"CZ5-GT1","centre":[7.9,-0.85],"radius_m":14,"platform":"ground bullseye","top_agl_m":0.2}],"drone_orbit":{"centre_start":"CZ5","count":3,"radius_m_target":70,"altitude_ft_target":2600,"owner":"Claude opponent/AI logic"}},"new":{"id":"CZ5","name":"TERN MESA MOA","boundary":[[13.8,1.7],[14.2,1.7],[14.2,2.1],[13.8,2.1]],"over_land":true,"floor_msl_ft_target":800,"ceiling_msl_ft_target":2500,"agl_range_ft_bound":[620,2320],"default_status":"HOT","schedule":"Default HOT; pilot may select COLD at any time; no timed automatic schedule.","available_statuses":["HOT","COLD"],"optional":false,"enabled":true,"minimum_clearance_km":1.85,"additional_margin_km":0.2,"clearance_reference":">=1.85 km plus 0.20 km margin from final fan boundaries and FULL 2 nm airway terrain-protection swaths, including proposed perimeter reservation.","chart":"magenta hatched boundary; name, floor/ceiling, status; full airspace acceptance pending","name_status":"fictional design name; FAA exact-name search 2026-10-30 found no match, not authoritative uniqueness","hook_owner":"Claude: manual HOT may trigger combat; COLD never triggers","gunnery_targets":[{"id":"CZ5-GT1","centre":[14,1.9],"radius_m":14,"platform":"ground bullseye","top_agl_m":0.2}],"drone_orbit":{"centre_start":"CZ5","count":3,"radius_m_target":70,"altitude_ft_target":1500,"owner":"Claude opponent/AI logic"},"hot_entry_behavior":{"HOT":"On user entry, spawn this zone’s targets and drones; engagement hook belongs to Claude.","COLD":"No targets/drones appear on entry and no combat triggers."}},"reason":"Ty decisions 2026-10-30 and fit revision: add perimeter islands/beaches, remove CZ4, explicit hot-entry behavior/rafts, or fix spacing/grade and protect land MOA."}
```

</details>

<details>
<summary>121. height_field</summary>

```json
{"phase":"1A.2","item":"height_field","old":{"algorithm":"island-review-v1","grid":{"min_km":-20,"max_km":20,"spacing_m":100,"rows":401,"columns":401,"format":"little-endian float32 metres MSL; row 0 south; column 0 west; x fastest; bilinear interpolation; JSON sidecar"},"base":{"interior_ft":4600,"coastal_ft":30,"interior_radius_km":6,"shore_transition_km":2},"peaks":[{"id":"Frank Church","centre":[0,0],"top_ft":9020,"radius_km":2.0},{"id":"Sierra","centre":[-1.54,-0.58],"top_ft":7550,"radius_km":1.3},{"id":"Chugach","centre":[-5.4,-1.4],"top_ft":6890,"radius_km":1.6},{"id":"Denali-like","centre":[-3.46,5.86],"top_ft":8530,"radius_km":1.5},{"id":"Mokuleia","centre":[5.6,4],"top_ft":2950,"radius_km":1.5},{"id":"Catalina","centre":[2.9,-13.7],"top_ft":2100,"radius_km":0.8},{"id":"Drake Hill","centre":[-4.9,-4.5],"top_ft":5750,"radius_km":0.7}],"crests":[{"id":"Mokuleia","path":[[3.17,4.99],[4.6,4.4],[6.05,3.74]],"width_km":0.5,"top_ft":2950},{"id":"Sierra","path":[[-1.1,0.3],[-1.54,-0.58],[-2.2,-2]],"width_km":0.5,"top_ft":7550},{"id":"Chugach-Denali","path":[[-5.6,-2.8],[-5.4,-1.4],[-5.3,-0.6],[-5,1.4],[-4.2,3.4],[-3.46,5.86]],"width_km":0.5,"top_ft":7000},{"id":"Summit","path":[[-0.9,1],[0,0],[1,0.6],[0.4,-1.2]],"width_km":0.5,"top_ft":8000}],"order":["coast and bed","regional base","Mokuleia regional lowland override","smooth crest and summit envelopes","lakes and glacier tongues","canyon cuts","pass and box-fork cuts","reassert peak centre elevations with compact smooth envelopes","flat/sloped pads with continuous blend","coast fade to ocean bed"],"continuity":"Every local influence has zero weight at its footprint boundary using cubic smoothstep; no boolean relief cutoff. Cavities require WorldSolids shell, not a second ground field.","sampling_limit":"100 m bake is a review artefact; continuous evaluator authoritative; 60 m shoulders and small solids need analytic evaluation in Phase 1B.","water_lane_bed_offset_m":3,"summit_reassertion":{"core_radius_km":0.12,"blend_end_radius_km":0.4,"order":"after cuts, before surface pads; set exact authored summit elevations"},"regional_overrides":[{"id":"Mokuleia lowland","centre":[5,4],"radii_km":[3,3],"elevation_ft":1800}],"cavities":{"DRAKE":"height field is upper rock envelope; only exterior152m uses ground pad; interior floor is separate height-aware deck/shell at4700ft; never flatten full hill to cave floor","collision_status":"Phase1B requires below-roof ground/contact routing; current review bake cannot certify cave interiors"},"clearance_envelopes":{"finals":{"length_km":7.408,"half_width_at_threshold_km":0.15,"widen_per_km":0.05,"outer_blend_km":0.2,"ils_loc_lda_ratio":34,"other_ratio":20,"vertical_margin_m":15,"constant_guard_km":0.15},"airways":{"half_width_km":3.704,"outer_blend_km":0.3,"clearance_ft":2025,"constant_guard_km":0.15},"order":"cut terrain after landforms; reassert locked field pads and authored peak cores; conflicts remain failures, never lowered by stealth"}},"new":{"algorithm":"island-review-v1","grid":{"min_km":-20,"max_km":20,"spacing_m":100,"rows":401,"columns":401,"format":"little-endian float32 metres MSL; row 0 south; column 0 west; x fastest; bilinear interpolation; JSON sidecar","interpolation":"fixed SW-NE diagonal, piecewise-linear triangles; explicit analytic flat/sloped pad detail overlays are part of same authoring evaluator"},"base":{"interior_ft":4600,"coastal_ft":30,"interior_radius_km":6,"shore_transition_km":2},"peaks":[{"id":"Frank Church","centre":[0,0],"top_ft":9020,"radius_km":2.0},{"id":"Sierra","centre":[-1.54,-0.58],"top_ft":7550,"radius_km":1.3},{"id":"Chugach","centre":[-5.4,-1.4],"top_ft":6890,"radius_km":1.6},{"id":"Denali-like","centre":[-3.46,5.86],"top_ft":8530,"radius_km":1.5},{"id":"Mokuleia","centre":[5.6,4],"top_ft":2950,"radius_km":1.5},{"id":"Catalina","centre":[2.9,-13.7],"top_ft":2100,"radius_km":0.8},{"id":"Drake Hill","centre":[-4.9,-4.5],"top_ft":5750,"radius_km":0.7}],"crests":[{"id":"Mokuleia","path":[[3.17,4.99],[4.6,4.4],[6.05,3.74]],"width_km":0.5,"top_ft":2950},{"id":"Sierra","path":[[-1.1,0.3],[-1.54,-0.58],[-2.2,-2]],"width_km":0.5,"top_ft":7550},{"id":"Chugach-Denali","path":[[-5.6,-2.8],[-5.4,-1.4],[-5.3,-0.6],[-5,1.4],[-4.2,3.4],[-3.46,5.86]],"width_km":0.5,"top_ft":7000},{"id":"Summit","path":[[-0.9,1],[0,0],[1,0.6],[0.4,-1.2]],"width_km":0.5,"top_ft":8000}],"order":["coast and bed","regional base","Mokuleia regional lowland override","smooth crest and summit envelopes","lakes and glacier tongues","canyon cuts","pass and box-fork cuts","reassert peak centre elevations with compact smooth envelopes","flat/sloped pads with continuous blend","coast fade to ocean bed"],"continuity":"Every local influence has zero weight at its footprint boundary using cubic smoothstep; no boolean relief cutoff. Cavities require WorldSolids shell, not a second ground field.","sampling_limit":"100 m bake is a review artefact; continuous evaluator authoritative; 60 m shoulders and small solids need analytic evaluation in Phase 1B.","water_lane_bed_offset_m":3,"summit_reassertion":{"core_radius_km":0.12,"blend_end_radius_km":0.4,"order":"after cuts, before surface pads; set exact authored summit elevations"},"regional_overrides":[{"id":"Mokuleia lowland","centre":[5,4],"radii_km":[3,3],"elevation_ft":1800}],"cavities":{"DRAKE":"height field is upper rock envelope; only exterior152m uses ground pad; interior floor is separate height-aware deck/shell at4700ft; never flatten full hill to cave floor","collision_status":"Phase1B requires below-roof ground/contact routing; current review bake cannot certify cave interiors"},"clearance_envelopes":{"finals":{"length_km":7.408,"half_width_at_threshold_km":0.15,"widen_per_km":0.05,"outer_blend_km":0.2,"ils_loc_lda_ratio":34,"other_ratio":20,"vertical_margin_m":15,"constant_guard_km":0.15},"airways":{"half_width_km":3.704,"outer_blend_km":0.3,"clearance_ft":2025,"constant_guard_km":0.15},"order":"cut terrain after landforms; reassert locked field pads and authored peak cores; conflicts remain failures, never lowered by stealth"},"land_moa_terrace":{"boundary":[[13.8,1.7],[14.2,1.7],[14.2,2.1],[13.8,2.1]],"elevation_ft":180,"blend_m":150}},"reason":"Ty decisions and explicit deterministic design geometry for full fit verification."}
```

</details>

<details>
<summary>122. islets</summary>

```json
{"phase":"1A.2","item":"islets","old":null,"new":[{"id":"SS01","centre":[-12.6,1.2],"radii_km":[0.13,0.091],"elevation_ft":35},{"id":"SS02","centre":[-12.4,1.7],"radii_km":[0.1,0.06999999999999999],"elevation_ft":45},{"id":"SS03","centre":[-12.8,2],"radii_km":[0.09,0.063],"elevation_ft":55},{"id":"SS04","centre":[-12.2,0.8],"radii_km":[0.13,0.091],"elevation_ft":35},{"id":"SS05","centre":[-12.9,0.3],"radii_km":[0.12,0.08399999999999999],"elevation_ft":45},{"id":"SS06","centre":[-13.2,-1.3],"radii_km":[0.15,0.105],"elevation_ft":55},{"id":"SS07","centre":[-14,-1.6],"radii_km":[0.1,0.06999999999999999],"elevation_ft":35},{"id":"SS08","centre":[-15.8,0.2],"radii_km":[0.09,0.063],"elevation_ft":45},{"id":"SS09","centre":[-12,2.3],"radii_km":[0.09,0.063],"elevation_ft":55},{"id":"SS10","centre":[-12.3,3.3],"radii_km":[0.12,0.08399999999999999],"elevation_ft":35},{"id":"SS11","centre":[-11.9,1.3],"radii_km":[0.08,0.055999999999999994],"elevation_ft":45},{"id":"SS12","centre":[-13.2,4],"radii_km":[0.11,0.077],"elevation_ft":55}],"reason":"Ty decisions and explicit deterministic design geometry for full fit verification."}
```

</details>

<details>
<summary>123. landable_beaches</summary>

```json
{"phase":"1A.2","item":"landable_beaches","old":null,"new":[{"id":"B1","name":"B1 hard-sand berm","centre":[2.8,-8.8],"heading_true_deg":100,"length_m":350,"width_m":35,"elevation_ft":12,"slope_percent":0,"surface":"hard sand","pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"island":"main","shore_class":"coastal","note":"Dry upper beach on broad berm; usable strip set back >=300 m from shoreline, 60 m shoulders; wet foreshore not usable runway."},{"id":"B2","name":"B2 hard-sand berm","centre":[-8.6,5.9],"heading_true_deg":35,"length_m":300,"width_m":35,"elevation_ft":12,"slope_percent":0,"surface":"hard sand","pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"island":"main","shore_class":"coastal","note":"Dry upper beach on broad berm; usable strip set back >=300 m from shoreline, 60 m shoulders; wet foreshore not usable runway."},{"id":"B3","name":"B3 hard-sand berm","centre":[-10.9,-5.4],"heading_true_deg":80,"length_m":300,"width_m":35,"elevation_ft":12,"slope_percent":0,"surface":"hard sand","pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"island":"main","shore_class":"coastal","note":"Dry upper beach on broad berm; usable strip set back >=300 m from shoreline, 60 m shoulders; wet foreshore not usable runway."},{"id":"B4","name":"B4 hard-sand berm","centre":[-13.4,-0.5],"heading_true_deg":90,"length_m":300,"width_m":35,"elevation_ft":12,"slope_percent":0,"surface":"hard sand","pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"island":"OI1","shore_class":"coastal","note":"Dry upper beach on broad berm; usable strip set back >=300 m from shoreline, 60 m shoulders; wet foreshore not usable runway."},{"id":"B5","name":"B5 hard-sand berm","centre":[13.6,0.65],"heading_true_deg":90,"length_m":350,"width_m":35,"elevation_ft":12,"slope_percent":0,"surface":"hard sand","pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"island":"OI2","shore_class":"coastal","note":"Dry upper beach on broad berm; usable strip set back >=300 m from shoreline, 60 m shoulders; wet foreshore not usable runway."},{"id":"B6","name":"B6 hard-sand berm","centre":[0.6,11.8],"heading_true_deg":90,"length_m":300,"width_m":35,"elevation_ft":12,"slope_percent":0,"surface":"hard sand","pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"island":"OI3","shore_class":"coastal","note":"Dry upper beach on broad berm; usable strip set back >=300 m from shoreline, 60 m shoulders; wet foreshore not usable runway."},{"id":"B7","name":"B7 hard-sand berm","centre":[4.4,-14.1],"heading_true_deg":300,"length_m":220,"width_m":35,"elevation_ft":12,"slope_percent":0,"surface":"hard sand","pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"island":"Catalina","shore_class":"coastal","note":"Dry upper beach on broad berm; usable strip set back >=300 m from shoreline, 60 m shoulders; wet foreshore not usable runway."}],"reason":"Ty decisions and explicit deterministic design geometry for full fit verification."}
```

</details>

<details>
<summary>124. outer_islands</summary>

```json
{"phase":"1A.2","item":"outer_islands","old":null,"new":[{"id":"OI1","name":"Sitka Sound","coast_polygon":[[-15.7,-0.8],[-15.7,3.5],[-14.7,4.1],[-12.8,3.7],[-12.5,2.5],[-13.3,2.3],[-13.7,1.6],[-13.3,0.9],[-12.5,1],[-12.7,-0.6],[-14,-1.2]],"base_ft":110,"airport":"SITX","cove":{"mouth":[-12.7,1.6],"head":[-13.6,1.6],"shelter":"east-facing cove behind southern arm and islets"},"volcano":{"centre":[-14.1,2.5],"radius_km":0.85,"rim_radius_km":0.14,"rim_ft":2600,"crater_floor_ft":2250,"shape":"symmetric stratovolcano: linear radial cone, 280 m summit crater; continuous radial crater bowl"},"runway_note":"Waterfront causeway/fill on west coast; >300 m outer coastal apron; fictional compact Sitka-inspired airport, not PASI data"},{"id":"OI2","name":"Tern Island","coast_polygon":[[10.6,0.3],[10.6,4],[11.5,4.6],[13.8,4.3],[15,3.5],[15,0.3],[13,-0.3],[11.4,-0.2]],"base_ft":180,"airport":"TERN"},{"id":"OI3","name":"North Haven","coast_polygon":[[-1.3,8.8],[-1.3,12.1],[-0.4,12.7],[1.4,12.2],[1.4,9],[0.5,8.6]],"base_ft":120,"airport":"NHVN"}],"reason":"Ty decisions and explicit deterministic design geometry for full fit verification."}
```

</details>

<details>
<summary>125. outer_navaid_sites</summary>

```json
{"phase":"1A.2","item":"outer_navaid_sites","old":null,"new":[{"id":"SITE-SITX","centre":[-14.75,1.1],"elevation_ft":110,"antenna_height_m":0,"owner":"Claude assigns equipment, ident, frequency and procedure; position/elevation only"},{"id":"SITE-TERN","centre":[11.55,2],"elevation_ft":180,"antenna_height_m":0,"owner":"Claude assigns equipment, ident, frequency and procedure; position/elevation only"},{"id":"SITE-NHVN","centre":[0.25,10.3],"elevation_ft":120,"antenna_height_m":0,"owner":"Claude assigns equipment, ident, frequency and procedure; position/elevation only"}],"reason":"Ty decisions and explicit deterministic design geometry for full fit verification."}
```

</details>

<details>
<summary>126. perimeter_traffic_reservation</summary>

```json
{"phase":"1A.2","item":"perimeter_traffic_reservation","old":null,"new":{"owner":"Codex proposed terrain/land-use reservation for Claude; not T201 geometry","purpose":"Outer-airport traffic runs around main island via water perimeter; avoid interior crossings, combat and box reservations","path":[[-15,1.1],[-13,-5.5],[-8,-10],[0.5,-11],[8.8,-9],[11.3,2],[8,8],[0,10.3],[-7,9],[-15,1.1]],"half_width_km":0.35,"floor_ft":3000,"ceiling_ft":6000,"note":"T201 and instrument procedures remain unchanged/unknown. Proposed route is a reservation only; gate approval does not publish IFR."},"reason":"Ty decisions and explicit deterministic design geometry for full fit verification."}
```

</details>

<details>
<summary>127. starts/AB2</summary>

```json
{"phase":"1A.2","item":"starts/AB2","old":{"id":"AB2","mode":"InAeroBox","centre":[-12,-3.6],"altitude_ft_target":2300,"heading_true_deg":0},"new":{"id":"AB2","mode":"InAeroBox","centre":[-12.3,-3.6],"altitude_ft_target":2300,"heading_true_deg":0},"reason":"Ty decisions 2026-10-30 and fit revision: add perimeter islands/beaches, remove CZ4, explicit hot-entry behavior/rafts, or fix spacing/grade and protect land MOA."}
```

</details>

<details>
<summary>128. starts/CZ4</summary>

```json
{"phase":"1A.2","item":"starts/CZ4","old":{"id":"CZ4","mode":"InCombatZone","enabled":false,"centre":[-15.180655,-7.078856],"altitude_ft_target":5000,"heading_true_deg":335.0},"new":null,"reason":"Ty decisions 2026-10-30 and fit revision: add perimeter islands/beaches, remove CZ4, explicit hot-entry behavior/rafts, or fix spacing/grade and protect land MOA."}
```

</details>

<details>
<summary>129. starts/CZ5</summary>

```json
{"phase":"1A.2","item":"starts/CZ5","old":{"id":"CZ5","mode":"InCombatZone","enabled":true,"centre":[7.9,-0.85],"altitude_ft_target":2600,"heading_true_deg":0},"new":{"id":"CZ5","mode":"InCombatZone","enabled":true,"centre":[14,1.9],"altitude_ft_target":1500,"heading_true_deg":0},"reason":"Ty decisions 2026-10-30 and fit revision: add perimeter islands/beaches, remove CZ4, explicit hot-entry behavior/rafts, or fix spacing/grade and protect land MOA."}
```

</details>

<details>
<summary>130. AP12 final fictional datum</summary>

```json
{"phase":"1A.2","item":"AP12 final fictional datum","old":4100,"new":3875,"reason":"Computed feasible interval 3834.48–3911.56 ft between Chamberlain and KSNA; 3875 preserves locked 35% cap on both sides."}
```

</details>

<details>
<summary>131. B1 coastal berm</summary>

```json
{"phase":"1A.2","item":"B1 coastal berm","old":[2.8,-8.8],"new":[2.8,-8.5],"reason":"Move 300 m inland to obtain >=300 m setback at both usable ends."}
```

</details>

<details>
<summary>132. Beach inventory six strips</summary>

```json
{"phase":"1A.2","item":"Beach inventory six strips","old":[{"id":"B1","name":"B1 hard-sand berm","centre":[2.8,-8.5],"heading_true_deg":100,"length_m":350,"width_m":35,"elevation_ft":12,"slope_percent":0,"surface":"hard sand","pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"island":"main","shore_class":"coastal","note":"Dry upper beach on broad berm; usable strip set back >=300 m from shoreline, 60 m shoulders; wet foreshore not usable runway."},{"id":"B2","name":"B2 hard-sand berm","centre":[-8.6,5.9],"heading_true_deg":35,"length_m":300,"width_m":35,"elevation_ft":12,"slope_percent":0,"surface":"hard sand","pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"island":"main","shore_class":"coastal","note":"Dry upper beach on broad berm; usable strip set back >=300 m from shoreline, 60 m shoulders; wet foreshore not usable runway."},{"id":"B3","name":"B3 hard-sand berm","centre":[-10.9,-5.4],"heading_true_deg":80,"length_m":300,"width_m":35,"elevation_ft":12,"slope_percent":0,"surface":"hard sand","pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"island":"main","shore_class":"coastal","note":"Dry upper beach on broad berm; usable strip set back >=300 m from shoreline, 60 m shoulders; wet foreshore not usable runway."},{"id":"B4","name":"B4 hard-sand berm","centre":[-13.4,-0.5],"heading_true_deg":90,"length_m":300,"width_m":35,"elevation_ft":12,"slope_percent":0,"surface":"hard sand","pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"island":"OI1","shore_class":"coastal","note":"Dry upper beach on broad berm; usable strip set back >=300 m from shoreline, 60 m shoulders; wet foreshore not usable runway."},{"id":"B5","name":"B5 hard-sand berm","centre":[13.6,0.65],"heading_true_deg":90,"length_m":350,"width_m":35,"elevation_ft":12,"slope_percent":0,"surface":"hard sand","pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"island":"OI2","shore_class":"coastal","note":"Dry upper beach on broad berm; usable strip set back >=300 m from shoreline, 60 m shoulders; wet foreshore not usable runway."},{"id":"B6","name":"B6 hard-sand berm","centre":[0.6,11.8],"heading_true_deg":90,"length_m":300,"width_m":35,"elevation_ft":12,"slope_percent":0,"surface":"hard sand","pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"island":"OI3","shore_class":"coastal","note":"Dry upper beach on broad berm; usable strip set back >=300 m from shoreline, 60 m shoulders; wet foreshore not usable runway."},{"id":"B7","name":"B7 hard-sand berm","centre":[4.4,-14.1],"heading_true_deg":300,"length_m":220,"width_m":35,"elevation_ft":12,"slope_percent":0,"surface":"hard sand","pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"island":"Catalina","shore_class":"coastal","note":"Dry upper beach on broad berm; usable strip set back >=300 m from shoreline, 60 m shoulders; wet foreshore not usable runway."}],"new":[{"id":"B1","name":"B1 hard-sand berm","centre":[2.8,-8.5],"heading_true_deg":100,"length_m":350,"width_m":35,"elevation_ft":12,"slope_percent":0,"surface":"hard sand","pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"island":"main","shore_class":"coastal","note":"Dry upper beach on broad berm; usable strip set back >=300 m from shoreline, 60 m shoulders; wet foreshore not usable runway."},{"id":"B2","name":"B2 hard-sand berm","centre":[-8.6,5.9],"heading_true_deg":35,"length_m":300,"width_m":35,"elevation_ft":12,"slope_percent":0,"surface":"hard sand","pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"island":"main","shore_class":"coastal","note":"Dry upper beach on broad berm; usable strip set back >=300 m from shoreline, 60 m shoulders; wet foreshore not usable runway."},{"id":"B3","name":"B3 hard-sand berm","centre":[-10.9,-5.4],"heading_true_deg":80,"length_m":300,"width_m":35,"elevation_ft":12,"slope_percent":0,"surface":"hard sand","pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"island":"main","shore_class":"coastal","note":"Dry upper beach on broad berm; usable strip set back >=300 m from shoreline, 60 m shoulders; wet foreshore not usable runway."},{"id":"B4","name":"B4 hard-sand berm","centre":[-13.4,-0.5],"heading_true_deg":90,"length_m":300,"width_m":35,"elevation_ft":12,"slope_percent":0,"surface":"hard sand","pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"island":"OI1","shore_class":"coastal","note":"Dry upper beach on broad berm; usable strip set back >=300 m from shoreline, 60 m shoulders; wet foreshore not usable runway."},{"id":"B5","name":"B5 hard-sand berm","centre":[13.6,0.65],"heading_true_deg":90,"length_m":350,"width_m":35,"elevation_ft":12,"slope_percent":0,"surface":"hard sand","pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"island":"OI2","shore_class":"coastal","note":"Dry upper beach on broad berm; usable strip set back >=300 m from shoreline, 60 m shoulders; wet foreshore not usable runway."},{"id":"B6","name":"B6 hard-sand berm","centre":[0.6,11.8],"heading_true_deg":90,"length_m":300,"width_m":35,"elevation_ft":12,"slope_percent":0,"surface":"hard sand","pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"island":"OI3","shore_class":"coastal","note":"Dry upper beach on broad berm; usable strip set back >=300 m from shoreline, 60 m shoulders; wet foreshore not usable runway."}],"reason":"Remove optional Catalina beach: locked KAVX elevation makes nearby 12 ft flat beach incompatible with 35% field-to-field cap; six main/outer beaches remain."}
```

</details>

<details>
<summary>133. B4 heading_true_deg</summary>

```json
{"phase":"1A.2","item":"B4 heading_true_deg","old":90,"new":0,"reason":"Keep both 4 nm beach-strip finals inside ring."}
```

</details>

<details>
<summary>134. B5 centre</summary>

```json
{"phase":"1A.2","item":"B5 centre","old":[13.6,0.65],"new":[11.3,3.7],"reason":"Separate beach approach fans from Tern land MOA with margin."}
```

</details>

<details>
<summary>135. B5 heading_true_deg</summary>

```json
{"phase":"1A.2","item":"B5 heading_true_deg","old":90,"new":0,"reason":"North/south perimeter finals stay inside ring and west of land MOA."}
```

</details>

<details>
<summary>136. AP2 south setback</summary>

```json
{"phase":"1A.2","item":"AP2 south setback","old":[-6.9,6.8],"new":[-6.9,6.6],"reason":"Move 200 m south to exceed coastal 300 m end setback; retains village north of PATK."}
```

</details>

<details>
<summary>137. B4 inland berm</summary>

```json
{"phase":"1A.2","item":"B4 inland berm","old":[-13.4,-0.5],"new":[-13.6,-0.4],"reason":"Move beach berm northwest for 300 m shore-end setback."}
```

</details>

<details>
<summary>138. KAVX inward centre</summary>

```json
{"phase":"1A.2","item":"KAVX inward centre","old":[3.6,-14.4],"new":[3.57,-14.36],"reason":"Move 50 m northwest within locked Catalina ellipse for >=300 m runway-end shore setback; preserve heading, length and true elevation."}
```

</details>

<details>
<summary>139. Denali-like peak</summary>

```json
{"phase":"1A.2","item":"Denali-like peak","old":[-3.46,5.86],"new":[-4.2,6.0],"reason":"Move peak west of PHDH runway-end 20:1 fan and away from KMEV north fan; preserve 8530 ft top. Airway A2 conflict remains subject to Claude MEA target."}
```

</details>

<details>
<summary>140. NHVN perimeter heading</summary>

```json
{"phase":"1A.2","item":"NHVN perimeter heading","old":0,"new":90,"reason":"East-west tangential runway finals avoid McCall and remain within ring."}
```

</details>

<details>
<summary>141. NHVN magnetic numbers</summary>

```json
{"phase":"1A.2","item":"NHVN magnetic numbers","old":"35/17","new":"8/26","reason":"True090 minus7E gives083 magnetic."}
```

</details>

<details>
<summary>142. Thomas Creek relief of KPGA RNAV16</summary>

```json
{"phase":"1A.2","item":"Thomas Creek relief of KPGA RNAV16","old":[3.8,-0.7],"new":[4.4,-0.9],"reason":"Move compact backcountry runway east of entire KPGA RNAV16 final fan, retain elevation/length/slope."}
```

</details>

<details>
<summary>143. Sulphur Creek pad separation</summary>

```json
{"phase":"1A.2","item":"Sulphur Creek pad separation","old":[4.6,-0.3],"new":[4.7,0.0],"reason":"Separate Thomas and Sulphur flat-pad claims."}
```

</details>

<details>
<summary>144. revision decisions</summary>

```json
{"phase":"1A.2","item":"revision decisions","old":["One high-DA box at McCall only","CZ4 dropped","HOT entry spawns targets and drones in every combat zone","AB1/AB2 remain water boxes with 9 moored marker rafts and 4 orange buoys each","Three outer runway islands and 12 Sitka islets; sites only for Claude navaids","Seven hard-sand landable beaches; checked as strips","Sitka cove, cratered symmetric volcano and waterfront causeway runway"],"new":["One high-DA McCall box only; AB1 and AB2 remain over water with moored marker rafts","CZ4 KACHEMAK removed by Ty; HOT entry spawns targets and drones for every remaining combat zone","Outer islands with runways, flat pads and position/elevation-only navaid sites; proposed perimeter traffic reservation for Claude","Six hard-sand main/outer beach strips, all checked as fields","Sitka Sound cratered symmetric volcano, sheltered cove, waterfront causeway/fill strip and twelve tiny islets","At least one outer island and two airports under combat sectors, with charted approach/airport cutouts","Every combat zone has authored island terrain, fly-under structures, low-level routes and ground targets"],"reason":"Record all nine Ty decisions for this revision; six beach strips remain after removal of incompatible optional Catalina beach."}
```

</details>

<details>
<summary>145. AP3 runway rotation</summary>

```json
{"phase":"1A.2","item":"AP3 runway rotation","old":180,"new":164,"reason":"Smallest integer-degree reheading avoiding full other-pad/peak-core penetrations and 500 m box separation; retain length/elevation/surface and check full terrain after bake."}
```

</details>

<details>
<summary>146. AP3 magnetic numbers</summary>

```json
{"phase":"1A.2","item":"AP3 magnetic numbers","old":"17/35","new":"16/34","reason":"Recompute from new true heading minus locked7E."}
```

</details>

<details>
<summary>147. AP4 runway rotation</summary>

```json
{"phase":"1A.2","item":"AP4 runway rotation","old":67,"new":97,"reason":"Smallest integer-degree reheading avoiding full other-pad/peak-core penetrations and 500 m box separation; retain length/elevation/surface and check full terrain after bake."}
```

</details>

<details>
<summary>148. AP4 magnetic numbers</summary>

```json
{"phase":"1A.2","item":"AP4 magnetic numbers","old":"6/24","new":"9/27","reason":"Recompute from new true heading minus locked7E."}
```

</details>

<details>
<summary>149. AP5 runway rotation</summary>

```json
{"phase":"1A.2","item":"AP5 runway rotation","old":40,"new":21,"reason":"Smallest integer-degree reheading avoiding full other-pad/peak-core penetrations and 500 m box separation; retain length/elevation/surface and check full terrain after bake."}
```

</details>

<details>
<summary>150. AP5 magnetic numbers</summary>

```json
{"phase":"1A.2","item":"AP5 magnetic numbers","old":"3/21","new":"1/19","reason":"Recompute from new true heading minus locked7E."}
```

</details>

<details>
<summary>151. AP6 runway rotation</summary>

```json
{"phase":"1A.2","item":"AP6 runway rotation","old":60,"new":109,"reason":"Smallest integer-degree reheading avoiding full other-pad/peak-core penetrations and 500 m box separation; retain length/elevation/surface and check full terrain after bake."}
```

</details>

<details>
<summary>152. AP6 magnetic numbers</summary>

```json
{"phase":"1A.2","item":"AP6 magnetic numbers","old":"5/23","new":"10/28","reason":"Recompute from new true heading minus locked7E."}
```

</details>

<details>
<summary>153. AP9 runway rotation</summary>

```json
{"phase":"1A.2","item":"AP9 runway rotation","old":47,"new":71,"reason":"Smallest integer-degree reheading avoiding full other-pad/peak-core penetrations and 500 m box separation; retain length/elevation/surface and check full terrain after bake."}
```

</details>

<details>
<summary>154. AP9 magnetic numbers</summary>

```json
{"phase":"1A.2","item":"AP9 magnetic numbers","old":"4/22","new":"6/24","reason":"Recompute from new true heading minus locked7E."}
```

</details>

<details>
<summary>155. AP11 runway rotation</summary>

```json
{"phase":"1A.2","item":"AP11 runway rotation","old":147,"new":139,"reason":"Smallest integer-degree reheading avoiding full other-pad/peak-core penetrations and 500 m box separation; retain length/elevation/surface and check full terrain after bake."}
```

</details>

<details>
<summary>156. AP11 magnetic numbers</summary>

```json
{"phase":"1A.2","item":"AP11 magnetic numbers","old":"14/32","new":"13/31","reason":"Recompute from new true heading minus locked7E."}
```

</details>

<details>
<summary>157. AP12 runway rotation</summary>

```json
{"phase":"1A.2","item":"AP12 runway rotation","old":167,"new":169,"reason":"Smallest integer-degree reheading avoiding full other-pad/peak-core penetrations and 500 m box separation; retain length/elevation/surface and check full terrain after bake."}
```

</details>

<details>
<summary>158. Big Creek runway rotation</summary>

```json
{"phase":"1A.2","item":"Big Creek runway rotation","old":108,"new":145,"reason":"Smallest integer-degree reheading avoiding full other-pad/peak-core penetrations and 500 m box separation; retain length/elevation/surface and check full terrain after bake."}
```

</details>

<details>
<summary>159. Soldier Bar runway rotation</summary>

```json
{"phase":"1A.2","item":"Soldier Bar runway rotation","old":250,"new":189,"reason":"Smallest integer-degree reheading avoiding full other-pad/peak-core penetrations and 500 m box separation; retain length/elevation/surface and check full terrain after bake."}
```

</details>

<details>
<summary>160. Soldier Bar one-way direction</summary>

```json
{"phase":"1A.2","item":"Soldier Bar one-way direction","old":250,"new":189,"reason":"Preserve one-way operational sense after strip rotation."}
```

</details>

<details>
<summary>161. Cabin Creek runway rotation</summary>

```json
{"phase":"1A.2","item":"Cabin Creek runway rotation","old":70,"new":98,"reason":"Smallest integer-degree reheading avoiding full other-pad/peak-core penetrations and 500 m box separation; retain length/elevation/surface and check full terrain after bake."}
```

</details>

<details>
<summary>162. Cabin Creek one-way direction</summary>

```json
{"phase":"1A.2","item":"Cabin Creek one-way direction","old":70,"new":98,"reason":"Preserve one-way operational sense after strip rotation."}
```

</details>

<details>
<summary>163. Dewey Moore runway rotation</summary>

```json
{"phase":"1A.2","item":"Dewey Moore runway rotation","old":290,"new":282,"reason":"Smallest integer-degree reheading avoiding full other-pad/peak-core penetrations and 500 m box separation; retain length/elevation/surface and check full terrain after bake."}
```

</details>

<details>
<summary>164. Dewey Moore one-way direction</summary>

```json
{"phase":"1A.2","item":"Dewey Moore one-way direction","old":290,"new":282,"reason":"Preserve one-way operational sense after strip rotation."}
```

</details>

<details>
<summary>165. Indian Creek runway rotation</summary>

```json
{"phase":"1A.2","item":"Indian Creek runway rotation","old":34,"new":52,"reason":"Smallest integer-degree reheading avoiding full other-pad/peak-core penetrations and 500 m box separation; retain length/elevation/surface and check full terrain after bake."}
```

</details>

<details>
<summary>166. Mackay Bar runway rotation</summary>

```json
{"phase":"1A.2","item":"Mackay Bar runway rotation","old":8,"new":355,"reason":"Smallest integer-degree reheading avoiding full other-pad/peak-core penetrations and 500 m box separation; retain length/elevation/surface and check full terrain after bake."}
```

</details>

<details>
<summary>167. B1 runway rotation</summary>

```json
{"phase":"1A.2","item":"B1 runway rotation","old":100,"new":91,"reason":"Smallest integer-degree reheading avoiding full other-pad/peak-core penetrations and 500 m box separation; retain length/elevation/surface and check full terrain after bake."}
```

</details>

<details>
<summary>168. B3 runway rotation</summary>

```json
{"phase":"1A.2","item":"B3 runway rotation","old":80,"new":90,"reason":"Smallest integer-degree reheading avoiding full other-pad/peak-core penetrations and 500 m box separation; retain length/elevation/surface and check full terrain after bake."}
```

</details>

<details>
<summary>169. B4 runway rotation</summary>

```json
{"phase":"1A.2","item":"B4 runway rotation","old":0,"new":1,"reason":"Smallest integer-degree reheading avoiding full other-pad/peak-core penetrations and 500 m box separation; retain length/elevation/surface and check full terrain after bake."}
```

</details>

<details>
<summary>170. AB2 box with markers</summary>

```json
{"phase":"1A.2","item":"AB2 box with markers","old":{"id":"AB2","name":"Cook Inlet","centre":[-12.3,-3.6],"airport":"PANC","density_altitude":"low","side_m":1000,"floor_agl_m":100,"ceiling_agl_m":1100,"centre_ground_ft":0,"floor_msl_ft_target":328,"ceiling_msl_ft_target":3609,"judges_offset_m":150,"judges_side":"east","markers":{"colour":"white","cross_arm_m":40,"cross_width_m":4,"corner_L_leg_m":30,"corner_width_m":4,"side_T_leg_m":30,"side_T_width_m":4,"rafts":true,"orange_corner_buoys":true,"shore_judges_marker":true},"rafts":[{"id":"AB2-R1","shape":"cross","centre":[-12.3,-3.6],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R2","shape":"L","centre":[-12.8,-4.1],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R3","shape":"L","centre":[-12.8,-3.1],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R4","shape":"L","centre":[-11.8,-4.1],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R5","shape":"L","centre":[-11.8,-3.1],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R6","shape":"T","centre":[-12.3,-3.1],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R7","shape":"T","centre":[-12.3,-4.1],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R8","shape":"T","centre":[-11.8,-3.6],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R9","shape":"T","centre":[-12.8,-3.6],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"}],"corner_buoys":[{"centre":[-12.8,-4.1],"colour":"orange","moored":true},{"centre":[-12.8,-3.1],"colour":"orange","moored":true},{"centre":[-11.8,-4.1],"colour":"orange","moored":true},{"centre":[-11.8,-3.1],"colour":"orange","moored":true}],"judges_line":{"centre":[-11.65,-3.6],"length_m":1000,"orientation_true_deg":0,"marker":"moored judge-reference raft on line; matching shore reference aligned along airport-facing axis"},"shore_reference_marker":{"centre":[-11.4,-3.6],"meaning":"shore sighting reference aligned to offshore judges line; actual line remains 150 m beyond square"},"judges_shore_marker_status":"resolved: line raft plus aligned shore reference; no false claim that offshore judges line intersects land"},"new":{"id":"AB2","name":"Cook Inlet","centre":[-13.0,-5.4],"airport":"PANC","density_altitude":"low","side_m":1000,"floor_agl_m":100,"ceiling_agl_m":1100,"centre_ground_ft":0,"floor_msl_ft_target":328,"ceiling_msl_ft_target":3609,"judges_offset_m":150,"judges_side":"east","markers":{"colour":"white","cross_arm_m":40,"cross_width_m":4,"corner_L_leg_m":30,"corner_width_m":4,"side_T_leg_m":30,"side_T_width_m":4,"rafts":true,"orange_corner_buoys":true,"shore_judges_marker":true},"rafts":[{"id":"AB2-R1","shape":"cross","centre":[-13.0,-5.4],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R2","shape":"L","centre":[-13.5,-5.9],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R3","shape":"L","centre":[-13.5,-4.9],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R4","shape":"L","centre":[-12.5,-5.9],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R5","shape":"L","centre":[-12.5,-4.9],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R6","shape":"T","centre":[-13.0,-4.9],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R7","shape":"T","centre":[-13.0,-5.9],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R8","shape":"T","centre":[-12.5,-5.4],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R9","shape":"T","centre":[-13.5,-5.4],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"}],"corner_buoys":[{"centre":[-13.5,-5.9],"colour":"orange","moored":true},{"centre":[-13.5,-4.9],"colour":"orange","moored":true},{"centre":[-12.5,-5.9],"colour":"orange","moored":true},{"centre":[-12.5,-4.9],"colour":"orange","moored":true}],"judges_line":{"centre":[-12.35,-5.4],"length_m":1000,"orientation_true_deg":0,"marker":"moored judge-reference raft on line; matching shore reference aligned along airport-facing axis"},"shore_reference_marker":{"centre":[-11.4,-5.4],"meaning":"shore sighting reference aligned to offshore judges line; actual line remains 150 m beyond square"},"judges_shore_marker_status":"resolved: line raft plus aligned shore reference; no false claim that offshore judges line intersects land"},"reason":"Move AB2 southwest of A2 low-MEA swath; move single McCall high-DA bench east of A2 and clear other approach fans. Locked box dimensions retained."}
```

</details>

<details>
<summary>171. AB2 start</summary>

```json
{"phase":"1A.2","item":"AB2 start","old":[-12.3,-3.6],"new":[-13.0,-5.4],"reason":"Follow revised aerobatic box."}
```

</details>

<details>
<summary>172. AB3 box with markers</summary>

```json
{"phase":"1A.2","item":"AB3 box with markers","old":{"id":"AB3","name":"McCall","centre":[-2.5,4],"airport":"KMYL","density_altitude":"high","side_m":1000,"floor_agl_m":100,"ceiling_agl_m":1100,"centre_ground_ft":5000,"floor_msl_ft_target":5328,"ceiling_msl_ft_target":8609,"judges_offset_m":150,"judges_side":"east","markers":{"colour":"white","cross_arm_m":40,"cross_width_m":4,"corner_L_leg_m":30,"corner_width_m":4,"side_T_leg_m":30,"side_T_width_m":4,"rafts":false,"orange_corner_buoys":false,"shore_judges_marker":false}},"new":{"id":"AB3","name":"McCall","centre":[1.2,5.7],"airport":"KMYL","density_altitude":"high","side_m":1000,"floor_agl_m":100,"ceiling_agl_m":1100,"centre_ground_ft":5000,"floor_msl_ft_target":5328,"ceiling_msl_ft_target":8609,"judges_offset_m":150,"judges_side":"east","markers":{"colour":"white","cross_arm_m":40,"cross_width_m":4,"corner_L_leg_m":30,"corner_width_m":4,"side_T_leg_m":30,"side_T_width_m":4,"rafts":false,"orange_corner_buoys":false,"shore_judges_marker":false}},"reason":"Move AB2 southwest of A2 low-MEA swath; move single McCall high-DA bench east of A2 and clear other approach fans. Locked box dimensions retained."}
```

</details>

<details>
<summary>173. AB3 start</summary>

```json
{"phase":"1A.2","item":"AB3 start","old":[-2.5,4],"new":[1.2,5.7],"reason":"Follow revised aerobatic box."}
```

</details>

<details>
<summary>174. Perimeter reservation altitude</summary>

```json
{"phase":"1A.2","item":"Perimeter reservation altitude","old":3000,"new":6000,"reason":"Provide >=2000ft above low-DA boxes; proposed world reservation only, Claude owns IFR."}
```

</details>

<details>
<summary>175. Perimeter reservation ceiling</summary>

```json
{"phase":"1A.2","item":"Perimeter reservation ceiling","old":6000,"new":7000,"reason":"1000ft reserved en-route altitude band, separate low-level airport arrivals/departures."}
```

</details>

<details>
<summary>176. Tern Mesa boundary</summary>

```json
{"phase":"1A.2","item":"Tern Mesa boundary","old":[[13.8,1.7],[14.2,1.7],[14.2,2.1],[13.8,2.1]],"new":[[14.6,2.2],[15,2.2],[15,2.6],[14.6,2.6]],"reason":"Move small land MOA east/north beyond every known final and full airway swath with >2.05km guard."}
```

</details>

<details>
<summary>177. Tern coast extension</summary>

```json
{"phase":"1A.2","item":"Tern coast extension","old":[[10.6,0.3],[10.6,4],[11.5,4.6],[13.8,4.3],[15,3.5],[15,0.3],[13,-0.3],[11.4,-0.2]],"new":[[10.6,0.3],[10.6,4],[11.5,4.6],[14.6,4.3],[15.7,3.5],[15.7,0.3],[13,-0.3],[11.4,-0.2]],"reason":"Extend unoccupied eastern terrace to contain entire relocated400m land MOA and surrounding combat relief."}
```

</details>

<details>
<summary>178. Tern terrace</summary>

```json
{"phase":"1A.2","item":"Tern terrace","old":[[13.8,1.7],[14.2,1.7],[14.2,2.1],[13.8,2.1]],"new":[[14.6,2.2],[15,2.2],[15,2.6],[14.6,2.6]],"reason":"Follow land MOA terrace."}
```

</details>

<details>
<summary>179. Tern Mesa floor</summary>

```json
{"phase":"1A.2","item":"Tern Mesa floor","old":800,"new":200,"reason":"Reserve low-level20ftAGL floor over180ft terrace, making fly-under features usable within zone."}
```

</details>

<details>
<summary>180. Tern Mesa gunnery ground position</summary>

```json
{"phase":"1A.2","item":"Tern Mesa gunnery ground position","old":[14,1.9],"new":[14.8,2.4],"reason":"Follow land MOA terrace."}
```

</details>

<details>
<summary>181. CZ5 start</summary>

```json
{"phase":"1A.2","item":"CZ5 start","old":[14,1.9],"new":[14.8,2.4],"reason":"Follow land MOA."}
```

</details>

<details>
<summary>182. outer_islands/OI4</summary>

```json
{"phase":"1A.2","item":"outer_islands/OI4","old":null,"new":{"id":"OI4","name":"Kite Anchorage","coast_polygon":[[9.017657715387712,12.364096882920155],[8.953616837563027,12.28098313119099],[8.873259311255106,12.210171957436282],[8.777960076018386,12.152874960471545],[8.669349728189712,12.110072507800572],[8.549286620932904,12.082496961255647],[8.419825067300913,12.070620146083538],[8.283180190369904,12.074645277884715],[8.141690021869286,12.094503485538082],[7.997775497810586,12.129854989604956],[7.853899035601097,12.180094916049464],[7.712522401399313,12.244363645801],[7.576064588613436,12.321561523074859],[7.446860428253483,12.410367670787588],[7.327120639325377,12.509262591130108],[7.218894002815817,12.616554164596506],[7.124032306481573,12.730406602617927],[7.044158660247557,12.848871858413967],[6.980639724345992,12.969922958613182],[6.934562325380927,13.091488685329455],[6.906714860423726,13.211489015274287],[6.897573807320581,13.327870709531942],[6.907295572024465,13.43864244504657],[6.935713812445913,13.541908886711791],[6.9823422846122885,13.635903117079845],[7.046383162436971,13.71901686880901],[7.126740688744894,13.789828042563718],[7.222039923981614,13.847125039528455],[7.33065027181029,13.889927492199428],[7.450713379067096,13.917503038744353],[7.580174932699087,13.929379853916462],[7.7168198096300955,13.925354722115284],[7.858309978130715,13.905496514461918],[8.002224502189414,13.870145010395044],[8.146100964398903,13.819905083950536],[8.287477598600686,13.755636354199],[8.423935411386564,13.678438476925141],[8.553139571746515,13.589632329212412],[8.672879360674623,13.490737408869892],[8.781105997184182,13.383445835403494],[8.875967693518428,13.269593397382073],[8.95584133975244,13.151128141586033],[9.019360275654009,13.030077041386818],[9.065437674619073,12.908511314670545],[9.093285139576274,12.788510984725715],[9.102426192679419,12.672129290468058],[9.092704427975535,12.56135755495343],[9.064286187554087,12.458091113288209]],"base_ft":50,"airport":"KITE","under_zone":"CZ1"},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>183. outer_islands/OI5</summary>

```json
{"phase":"1A.2","item":"outer_islands/OI5","old":null,"new":{"id":"OI5","name":"Rime Harbour","coast_polygon":[[-6.971399239157465,14.118045689892066],[-6.926418294244393,14.02325199625277],[-6.8998066299698815,13.91950531588473],[-6.8920195792878145,13.808580783253053],[-6.903190380794341,13.692376347865164],[-6.933127898978553,13.572880299828498],[-6.981319894604071,13.45213724965553],[-7.046941789264395,13.332213144411021],[-7.128870774148061,13.215159918784941],[-7.225705021608705,13.102980385921041],[-7.335787670824749,12.997593968728667],[-7.45723517714743,12.900803858024611],[-7.587969540071956,12.81426615943831],[-7.725753858402277,12.73946155698536],[-7.868230604250732,12.67766997815348],[-8.012961960993833,12.629948693988352],[-8.157471534990595,12.597114228892877],[-8.299286727364311,12.579728389668272],[-8.435981040854866,12.578088652843936],[-8.565215597859615,12.592223074771557],[-8.684779159277616,12.621889811573148],[-8.792625959423724,12.66658125715686],[-8.886910709646662,12.725532728497948],[-8.966020171729438,12.79773554957733],[-9.028600760842535,12.881954310107934],[-9.073581705755606,12.97674800374723],[-9.100193370030118,13.080494684115271],[-9.107980420712185,13.191419216746946],[-9.09680961920566,13.307623652134836],[-9.066872101021447,13.427119700171502],[-9.01868010539593,13.547862750344468],[-8.953058210735605,13.667786855588979],[-8.87112922585194,13.784840081215059],[-8.774294978391294,13.897019614078959],[-8.664212329175252,14.002406031271333],[-8.54276482285257,14.099196141975389],[-8.412030459928044,14.18573384056169],[-8.274246141597724,14.26053844301464],[-8.13176939574927,14.322330021846518],[-7.987038039006167,14.370051306011648],[-7.842528465009404,14.402885771107123],[-7.700713272635689,14.420271610331728],[-7.564018959145134,14.421911347156064],[-7.4347844021403855,14.407776925228443],[-7.315220840722385,14.378110188426854],[-7.207374040576275,14.333418742843138],[-7.113089290353339,14.274467271502052],[-7.033979828270561,14.20226445042267]],"base_ft":70,"airport":"RIME","under_zone":"CZ3"},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>184. airports/KITE</summary>

```json
{"phase":"1A.2","item":"airports/KITE","old":null,"new":{"id":"KITE","name":"Kite Anchorage","elevation_ft":50,"start_group":"non-start","shore_class":"coastal","island":"OI4","runways":[{"id":"12/30","centre":[8,13],"heading_true_deg":122,"length_m":750,"width_m":24,"surface":"gravel","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":180}}],"note":"Fictional outer-island field; full two-end protection retained. Under MOA raised-floor traffic reservation, no new start."},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>185. airports/RIME</summary>

```json
{"phase":"1A.2","item":"airports/RIME","old":null,"new":{"id":"RIME","name":"Rime Harbour","elevation_ft":70,"start_group":"non-start","shore_class":"coastal","island":"OI5","runways":[{"id":"5/23","centre":[-8,13.5],"heading_true_deg":59,"length_m":750,"width_m":24,"surface":"gravel","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":180}}],"note":"Fictional outer-island field; full two-end protection retained. Under MOA raised-floor traffic reservation, no new start."},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>186. outer_navaid_sites/SITE-KITE</summary>

```json
{"phase":"1A.2","item":"outer_navaid_sites/SITE-KITE","old":null,"new":{"id":"SITE-KITE","centre":[8.05,13],"elevation_ft":50,"owner":"Claude equipment/ident/frequency; position/elevation only"},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>187. outer_navaid_sites/SITE-RIME</summary>

```json
{"phase":"1A.2","item":"outer_navaid_sites/SITE-RIME","old":null,"new":{"id":"SITE-RIME","centre":[-7.95,13.5],"elevation_ft":70,"owner":"Claude equipment/ident/frequency; position/elevation only"},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>188. islets/CZ1-ARCH-E</summary>

```json
{"phase":"1A.2","item":"islets/CZ1-ARCH-E","old":null,"new":{"id":"CZ1-ARCH-E","centre":[9.875,16.3],"radii_km":[0.025,0.04],"elevation_ft":110,"combat_zone":"CZ1","pedestal_only":true},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>189. islets/CZ1-ARCH-W</summary>

```json
{"phase":"1A.2","item":"islets/CZ1-ARCH-W","old":null,"new":{"id":"CZ1-ARCH-W","centre":[9.725000000000001,16.3],"radii_km":[0.025,0.04],"elevation_ft":110,"combat_zone":"CZ1","pedestal_only":true},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>190. islets/CZ1-IS1</summary>

```json
{"phase":"1A.2","item":"islets/CZ1-IS1","old":null,"new":{"id":"CZ1-IS1","centre":[8.8,15.9],"radii_km":[0.35,0.55],"elevation_ft":60,"combat_zone":"CZ1","relief":{"type":"ridge","top_ft":110,"radius_km":0.24499999999999997}},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>191. islets/CZ1-IS2</summary>

```json
{"phase":"1A.2","item":"islets/CZ1-IS2","old":null,"new":{"id":"CZ1-IS2","centre":[9.8,15.9],"radii_km":[0.35,0.55],"elevation_ft":60,"combat_zone":"CZ1","relief":{"type":"rolling island","top_ft":110,"radius_km":0.24499999999999997}},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>192. islets/CZ1-IS3</summary>

```json
{"phase":"1A.2","item":"islets/CZ1-IS3","old":null,"new":{"id":"CZ1-IS3","centre":[9.3,16.7],"radii_km":[0.34,0.25],"elevation_ft":60,"combat_zone":"CZ1","relief":{"type":"rolling island","top_ft":110,"radius_km":0.175}},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>193. islets/CZ1-IS4</summary>

```json
{"phase":"1A.2","item":"islets/CZ1-IS4","old":null,"new":{"id":"CZ1-IS4","centre":[8.5,15.3],"radii_km":[0.2,0.24],"elevation_ft":60,"combat_zone":"CZ1","relief":{"type":"rolling island","top_ft":110,"radius_km":0.13999999999999999}},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>194. islets/CZ1-IS5</summary>

```json
{"phase":"1A.2","item":"islets/CZ1-IS5","old":null,"new":{"id":"CZ1-IS5","centre":[10.15,16.55],"radii_km":[0.17,0.2],"elevation_ft":60,"combat_zone":"CZ1","relief":{"type":"spire","top_ft":110,"radius_km":0.119}},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>195. islets/CZ2-ARCH-E</summary>

```json
{"phase":"1A.2","item":"islets/CZ2-ARCH-E","old":null,"new":{"id":"CZ2-ARCH-E","centre":[17.075,-7.1],"radii_km":[0.025,0.04],"elevation_ft":110,"combat_zone":"CZ2","pedestal_only":true},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>196. islets/CZ2-ARCH-W</summary>

```json
{"phase":"1A.2","item":"islets/CZ2-ARCH-W","old":null,"new":{"id":"CZ2-ARCH-W","centre":[16.925,-7.1],"radii_km":[0.025,0.04],"elevation_ft":110,"combat_zone":"CZ2","pedestal_only":true},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>197. islets/CZ2-IS1</summary>

```json
{"phase":"1A.2","item":"islets/CZ2-IS1","old":null,"new":{"id":"CZ2-IS1","centre":[16.0,-7.5],"radii_km":[0.35,0.55],"elevation_ft":60,"combat_zone":"CZ2","relief":{"type":"ridge","top_ft":110,"radius_km":0.24499999999999997}},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>198. islets/CZ2-IS2</summary>

```json
{"phase":"1A.2","item":"islets/CZ2-IS2","old":null,"new":{"id":"CZ2-IS2","centre":[17.0,-7.5],"radii_km":[0.35,0.55],"elevation_ft":60,"combat_zone":"CZ2","relief":{"type":"rolling island","top_ft":110,"radius_km":0.24499999999999997}},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>199. islets/CZ2-IS3</summary>

```json
{"phase":"1A.2","item":"islets/CZ2-IS3","old":null,"new":{"id":"CZ2-IS3","centre":[16.5,-6.7],"radii_km":[0.34,0.25],"elevation_ft":60,"combat_zone":"CZ2","relief":{"type":"rolling island","top_ft":110,"radius_km":0.175}},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>200. islets/CZ2-IS4</summary>

```json
{"phase":"1A.2","item":"islets/CZ2-IS4","old":null,"new":{"id":"CZ2-IS4","centre":[15.7,-8.1],"radii_km":[0.2,0.24],"elevation_ft":60,"combat_zone":"CZ2","relief":{"type":"rolling island","top_ft":110,"radius_km":0.13999999999999999}},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>201. islets/CZ2-IS5</summary>

```json
{"phase":"1A.2","item":"islets/CZ2-IS5","old":null,"new":{"id":"CZ2-IS5","centre":[17.35,-6.85],"radii_km":[0.17,0.2],"elevation_ft":60,"combat_zone":"CZ2","relief":{"type":"spire","top_ft":110,"radius_km":0.119}},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>202. islets/CZ3-ARCH-E</summary>

```json
{"phase":"1A.2","item":"islets/CZ3-ARCH-E","old":null,"new":{"id":"CZ3-ARCH-E","centre":[-6.425,16.7],"radii_km":[0.025,0.04],"elevation_ft":110,"combat_zone":"CZ3","pedestal_only":true},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>203. islets/CZ3-ARCH-W</summary>

```json
{"phase":"1A.2","item":"islets/CZ3-ARCH-W","old":null,"new":{"id":"CZ3-ARCH-W","centre":[-6.575,16.7],"radii_km":[0.025,0.04],"elevation_ft":110,"combat_zone":"CZ3","pedestal_only":true},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>204. islets/CZ3-IS1</summary>

```json
{"phase":"1A.2","item":"islets/CZ3-IS1","old":null,"new":{"id":"CZ3-IS1","centre":[-7.5,16.3],"radii_km":[0.35,0.55],"elevation_ft":60,"combat_zone":"CZ3","relief":{"type":"ridge","top_ft":110,"radius_km":0.24499999999999997}},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>205. islets/CZ3-IS2</summary>

```json
{"phase":"1A.2","item":"islets/CZ3-IS2","old":null,"new":{"id":"CZ3-IS2","centre":[-6.5,16.3],"radii_km":[0.35,0.55],"elevation_ft":60,"combat_zone":"CZ3","relief":{"type":"rolling island","top_ft":110,"radius_km":0.24499999999999997}},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>206. islets/CZ3-IS3</summary>

```json
{"phase":"1A.2","item":"islets/CZ3-IS3","old":null,"new":{"id":"CZ3-IS3","centre":[-7.0,17.1],"radii_km":[0.34,0.25],"elevation_ft":60,"combat_zone":"CZ3","relief":{"type":"rolling island","top_ft":110,"radius_km":0.175}},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>207. islets/CZ3-IS4</summary>

```json
{"phase":"1A.2","item":"islets/CZ3-IS4","old":null,"new":{"id":"CZ3-IS4","centre":[-7.8,15.7],"radii_km":[0.2,0.24],"elevation_ft":60,"combat_zone":"CZ3","relief":{"type":"rolling island","top_ft":110,"radius_km":0.13999999999999999}},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>208. islets/CZ3-IS5</summary>

```json
{"phase":"1A.2","item":"islets/CZ3-IS5","old":null,"new":{"id":"CZ3-IS5","centre":[-6.15,16.95],"radii_km":[0.17,0.2],"elevation_ft":60,"combat_zone":"CZ3","relief":{"type":"spire","top_ft":110,"radius_km":0.119}},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>209. islets/CZ5-ARCH-E</summary>

```json
{"phase":"1A.2","item":"islets/CZ5-ARCH-E","old":null,"new":{"id":"CZ5-ARCH-E","centre":[14.965,2.488],"radii_km":[0.025,0.04],"elevation_ft":188,"combat_zone":"CZ5","pedestal_only":true},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>210. islets/CZ5-ARCH-W</summary>

```json
{"phase":"1A.2","item":"islets/CZ5-ARCH-W","old":null,"new":{"id":"CZ5-ARCH-W","centre":[14.855,2.488],"radii_km":[0.025,0.04],"elevation_ft":188,"combat_zone":"CZ5","pedestal_only":true},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>211. combat_zones/CZ1</summary>

```json
{"phase":"1A.2","item":"combat_zones/CZ1","old":{"id":"CZ1","name":"KAIWI MOA","bearings_true_deg":[15,50],"radii_km":[14,19.5],"floor_msl_ft_target":500,"ceiling_msl_ft_target":12500,"default_status":"HOT","available_statuses":["HOT","COLD"],"optional":false,"enabled":true,"minimum_clearance_km":1.85,"chart":"magenta hatched boundary; floor-ceiling-status","name_status":"placeholder; unverified against official MOA list","hook_owner":"Claude: HOT may trigger event:combat and dogfight; COLD never triggers","gunnery_targets":[{"id":"CZ1-GT1","centre":[8.056562,14.685173],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ1-GT2","centre":[8.589346,14.728395],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ1-GT3","centre":[8.814083,14.2434],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ1-GT4","centre":[9.3484,14.258679],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ1-GT5","centre":[9.547446,13.762586],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ1-GT6","centre":[10.08183,13.749881],"radius_m":14,"platform":"moored floating bullseye raft","target_top_msl_m":1}],"drone_orbit":{"centre_start":"CZ1","count":3,"radius_m_target":500,"altitude_ft_target":3000,"owner":"Claude opponent/AI logic"},"hot_entry_behavior":{"HOT":"On user entry, spawn this zone’s targets and drones; engagement hook belongs to Claude.","COLD":"No targets/drones appear on entry and no combat triggers."}},"new":{"id":"CZ1","name":"AP KAIWI MOA","bearings_true_deg":[15,50],"radii_km":[14,19.5],"floor_msl_ft_target":150,"ceiling_msl_ft_target":12500,"default_status":"HOT","available_statuses":["HOT","COLD"],"optional":false,"enabled":true,"minimum_clearance_km":1.85,"chart":"magenta hatched boundary; floor-ceiling-status","name_status":"Fictional simulator-prefixed name; not a depiction of real SUA. Susitna is a real MOA name (FAA chart supplement), hence AP prefix on all zones.","hook_owner":"Claude implements explicit HOT-entry targets/drones spawn; COLD never spawns.","gunnery_targets":[{"id":"CZ1-GT1","centre":[8.8,15.9],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170},{"id":"CZ1-GT2","centre":[9.8,15.9],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170},{"id":"CZ1-GT3","centre":[9.3,16.7],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170}],"drone_orbit":{"centre_start":"CZ1","count":3,"radius_m_target":150,"altitude_ft_target":900,"owner":"Claude opponent/AI logic","centre":[9.3,15.9]},"hot_entry_behavior":{"HOT":"On user entry, spawn this zone’s targets and drones; engagement hook belongs to Claude.","COLD":"No targets/drones appear on entry and no combat triggers."},"airport_overlays":[{"airport":"KITE","island":"OI4","floor_msl_ft":5000,"ceiling_msl_ft":12500,"traffic_floor_msl_ft":50,"traffic_ceiling_msl_ft":3000,"pattern_altitude_msl_ft":1050,"vertical_buffer_ft":2000,"pattern_radius_km":1.5,"approach_guard_km":1.85,"scope":"entire both-end 4nm final fans plus1.85km horizontal guard and 1.5km pattern disk; arrivals/departures stay at/below3000ft until beyond guard; combat floor5000ft over reservation; no attacks/targets/drones below raised floor","floor_priority":"raised floor overrides base floor in union of protection polygons"}],"name_audit_source":"https://aeronav.faa.gov/afd/02OCT2025/AK_rear_02OCT2025.pdf","schedule":"Default HOT; pilot may select COLD at any time; no timed automatic schedule.","boundary_mode":"annular sector minus charted horizontal exclusion polygons; raised floor on airport traffic overlays","exclusion_policy":{"guard_km":2.05,"protected_sources":"All known IFR finals AND every runway/beach end fan except own under-zone airport; full2nm airway swaths and proposed perimeter reservation; subtract Minkowski-expanded convex polygons, no combat in excluded volume at any altitude","unknown_geometry":"Reserve against future T201/misses/holds; pending Claude geometry remains unverified"},"combat_features":[{"id":"CZ1-CHANNEL","type":"island cluster and channel","centre":[9.3,15.9],"channel_width_m":300,"channel_length_m":1100,"water_msl_ft":0,"flight_half_width_m":40,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"terrain":"five islands with smooth ridges/sea stacks, two 300m channels; hide-and-seek and strafe route"},{"id":"CZ1-BRIDGE","type":"bridge","centre":[9.3,15.9],"heading_true_deg":90,"span_m":300,"deck_width_m":14,"under_clearance_m":120,"under_base_msl_ft":0,"deck_thickness_m":4,"flight_opening_width_m":240,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids bridge deck/piers; no heightfield bridge deck"},{"id":"CZ1-ARCH","type":"rock arch","centre":[9.8,16.3],"axis_true_deg":0,"opening_width_m":100,"opening_height_m":110,"base_msl_ft":60,"rock_top_msl_ft":460,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids overhang/opening; footprint pedestals only in shared heightfield"}],"low_level_training":{"floor_msl_ft":150,"flight_tube_ceiling_ft":300,"clear_of_traffic_overlays":true,"feature_solids":"All arch/bridge solids charted; no invisible heightfield ceilings; verified as analytic design envelopes"}},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>212. combat_zones/CZ2</summary>

```json
{"phase":"1A.2","item":"combat_zones/CZ2","old":{"id":"CZ2","name":"PAINTED MOA","bearings_true_deg":[95,135],"radii_km":[13.5,19.5],"floor_msl_ft_target":500,"ceiling_msl_ft_target":12500,"default_status":"HOT","available_statuses":["HOT","COLD"],"optional":false,"enabled":true,"minimum_clearance_km":1.85,"chart":"magenta hatched boundary; floor-ceiling-status","name_status":"placeholder; unverified against official MOA list","hook_owner":"Claude: HOT may trigger event:combat and dogfight; COLD never triggers","gunnery_targets":[{"id":"CZ2-GT1","centre":[15.37813,-5.980228],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ2-GT2","centre":[15.492976,-6.496744],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ2-GT3","centre":[15.044074,-6.776861],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ2-GT4","centre":[15.13173,-7.29868],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ2-GT5","centre":[14.668783,-7.55492],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ2-GT6","centre":[14.729009,-8.080611],"radius_m":14,"platform":"moored floating bullseye raft","target_top_msl_m":1}],"drone_orbit":{"centre_start":"CZ2","count":3,"radius_m_target":500,"altitude_ft_target":3000,"owner":"Claude opponent/AI logic"},"hot_entry_behavior":{"HOT":"On user entry, spawn this zone’s targets and drones; engagement hook belongs to Claude.","COLD":"No targets/drones appear on entry and no combat triggers."}},"new":{"id":"CZ2","name":"AP PAINTED MOA","bearings_true_deg":[95,135],"radii_km":[13.5,19.5],"floor_msl_ft_target":150,"ceiling_msl_ft_target":12500,"default_status":"HOT","available_statuses":["HOT","COLD"],"optional":false,"enabled":true,"minimum_clearance_km":1.85,"chart":"magenta hatched boundary; floor-ceiling-status","name_status":"Fictional simulator-prefixed name; not a depiction of real SUA. Susitna is a real MOA name (FAA chart supplement), hence AP prefix on all zones.","hook_owner":"Claude implements explicit HOT-entry targets/drones spawn; COLD never spawns.","gunnery_targets":[{"id":"CZ2-GT1","centre":[16.0,-7.5],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170},{"id":"CZ2-GT2","centre":[17.0,-7.5],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170},{"id":"CZ2-GT3","centre":[16.5,-6.7],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170}],"drone_orbit":{"centre_start":"CZ2","count":3,"radius_m_target":150,"altitude_ft_target":900,"owner":"Claude opponent/AI logic","centre":[16.5,-7.5]},"hot_entry_behavior":{"HOT":"On user entry, spawn this zone’s targets and drones; engagement hook belongs to Claude.","COLD":"No targets/drones appear on entry and no combat triggers."},"name_audit_source":"https://aeronav.faa.gov/afd/02OCT2025/AK_rear_02OCT2025.pdf","schedule":"Default HOT; pilot may select COLD at any time; no timed automatic schedule.","boundary_mode":"annular sector minus charted horizontal exclusion polygons; raised floor on airport traffic overlays","exclusion_policy":{"guard_km":2.05,"protected_sources":"All known IFR finals AND every runway/beach end fan except own under-zone airport; full2nm airway swaths and proposed perimeter reservation; subtract Minkowski-expanded convex polygons, no combat in excluded volume at any altitude","unknown_geometry":"Reserve against future T201/misses/holds; pending Claude geometry remains unverified"},"combat_features":[{"id":"CZ2-CHANNEL","type":"island cluster and channel","centre":[16.5,-7.5],"channel_width_m":300,"channel_length_m":1100,"water_msl_ft":0,"flight_half_width_m":40,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"terrain":"five islands with smooth ridges/sea stacks, two 300m channels; hide-and-seek and strafe route"},{"id":"CZ2-BRIDGE","type":"bridge","centre":[16.5,-7.5],"heading_true_deg":90,"span_m":300,"deck_width_m":14,"under_clearance_m":120,"under_base_msl_ft":0,"deck_thickness_m":4,"flight_opening_width_m":240,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids bridge deck/piers; no heightfield bridge deck"},{"id":"CZ2-ARCH","type":"rock arch","centre":[17.0,-7.1],"axis_true_deg":0,"opening_width_m":100,"opening_height_m":110,"base_msl_ft":60,"rock_top_msl_ft":460,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids overhang/opening; footprint pedestals only in shared heightfield"}],"low_level_training":{"floor_msl_ft":150,"flight_tube_ceiling_ft":300,"clear_of_traffic_overlays":true,"feature_solids":"All arch/bridge solids charted; no invisible heightfield ceilings; verified as analytic design envelopes"}},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>213. combat_zones/CZ3</summary>

```json
{"phase":"1A.2","item":"combat_zones/CZ3","old":{"id":"CZ3","name":"SUSITNA MOA","bearings_true_deg":[315,350],"radii_km":[14.5,19.5],"floor_msl_ft_target":500,"ceiling_msl_ft_target":12500,"default_status":"HOT","available_statuses":["HOT","COLD"],"optional":false,"enabled":true,"minimum_clearance_km":1.85,"chart":"magenta hatched boundary; floor-ceiling-status","name_status":"placeholder; unverified against official MOA list","hook_owner":"Claude: HOT may trigger event:combat and dogfight; COLD never triggers","gunnery_targets":[{"id":"CZ3-GT1","centre":[-8.819145,14.533502],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ3-GT2","centre":[-8.584546,15.019839],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ3-GT3","centre":[-8.046434,14.975143],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ3-GT4","centre":[-7.786703,15.448536],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ3-GT5","centre":[-7.251669,15.375737],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},{"id":"CZ3-GT6","centre":[-6.967518,15.834889],"radius_m":14,"platform":"moored floating bullseye raft","target_top_msl_m":1}],"drone_orbit":{"centre_start":"CZ3","count":3,"radius_m_target":500,"altitude_ft_target":3000,"owner":"Claude opponent/AI logic"},"hot_entry_behavior":{"HOT":"On user entry, spawn this zone’s targets and drones; engagement hook belongs to Claude.","COLD":"No targets/drones appear on entry and no combat triggers."}},"new":{"id":"CZ3","name":"AP SUSITNA MOA","bearings_true_deg":[315,350],"radii_km":[14.5,19.5],"floor_msl_ft_target":150,"ceiling_msl_ft_target":12500,"default_status":"HOT","available_statuses":["HOT","COLD"],"optional":false,"enabled":true,"minimum_clearance_km":1.85,"chart":"magenta hatched boundary; floor-ceiling-status","name_status":"Fictional simulator-prefixed name; not a depiction of real SUA. Susitna is a real MOA name (FAA chart supplement), hence AP prefix on all zones.","hook_owner":"Claude implements explicit HOT-entry targets/drones spawn; COLD never spawns.","gunnery_targets":[{"id":"CZ3-GT1","centre":[-7.5,16.3],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170},{"id":"CZ3-GT2","centre":[-6.5,16.3],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170},{"id":"CZ3-GT3","centre":[-7.0,17.1],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170}],"drone_orbit":{"centre_start":"CZ3","count":3,"radius_m_target":150,"altitude_ft_target":900,"owner":"Claude opponent/AI logic","centre":[-7.0,16.3]},"hot_entry_behavior":{"HOT":"On user entry, spawn this zone’s targets and drones; engagement hook belongs to Claude.","COLD":"No targets/drones appear on entry and no combat triggers."},"airport_overlays":[{"airport":"RIME","island":"OI5","floor_msl_ft":5000,"ceiling_msl_ft":12500,"traffic_floor_msl_ft":70,"traffic_ceiling_msl_ft":3000,"pattern_altitude_msl_ft":1070,"vertical_buffer_ft":2000,"pattern_radius_km":1.5,"approach_guard_km":1.85,"scope":"entire both-end 4nm final fans plus1.85km horizontal guard and 1.5km pattern disk; arrivals/departures stay at/below3000ft until beyond guard; combat floor5000ft over reservation; no attacks/targets/drones below raised floor","floor_priority":"raised floor overrides base floor in union of protection polygons"}],"name_audit_source":"https://aeronav.faa.gov/afd/02OCT2025/AK_rear_02OCT2025.pdf","schedule":"Default HOT; pilot may select COLD at any time; no timed automatic schedule.","boundary_mode":"annular sector minus charted horizontal exclusion polygons; raised floor on airport traffic overlays","exclusion_policy":{"guard_km":2.05,"protected_sources":"All known IFR finals AND every runway/beach end fan except own under-zone airport; full2nm airway swaths and proposed perimeter reservation; subtract Minkowski-expanded convex polygons, no combat in excluded volume at any altitude","unknown_geometry":"Reserve against future T201/misses/holds; pending Claude geometry remains unverified"},"combat_features":[{"id":"CZ3-CHANNEL","type":"island cluster and channel","centre":[-7.0,16.3],"channel_width_m":300,"channel_length_m":1100,"water_msl_ft":0,"flight_half_width_m":40,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"terrain":"five islands with smooth ridges/sea stacks, two 300m channels; hide-and-seek and strafe route"},{"id":"CZ3-BRIDGE","type":"bridge","centre":[-7.0,16.3],"heading_true_deg":90,"span_m":300,"deck_width_m":14,"under_clearance_m":120,"under_base_msl_ft":0,"deck_thickness_m":4,"flight_opening_width_m":240,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids bridge deck/piers; no heightfield bridge deck"},{"id":"CZ3-ARCH","type":"rock arch","centre":[-6.5,16.7],"axis_true_deg":0,"opening_width_m":100,"opening_height_m":110,"base_msl_ft":60,"rock_top_msl_ft":460,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids overhang/opening; footprint pedestals only in shared heightfield"}],"low_level_training":{"floor_msl_ft":150,"flight_tube_ceiling_ft":300,"clear_of_traffic_overlays":true,"feature_solids":"All arch/bridge solids charted; no invisible heightfield ceilings; verified as analytic design envelopes"}},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>214. combat_zones/CZ5</summary>

```json
{"phase":"1A.2","item":"combat_zones/CZ5","old":{"id":"CZ5","name":"TERN MESA MOA","boundary":[[14.6,2.2],[15,2.2],[15,2.6],[14.6,2.6]],"over_land":true,"floor_msl_ft_target":200,"ceiling_msl_ft_target":2500,"agl_range_ft_bound":[620,2320],"default_status":"HOT","schedule":"Default HOT; pilot may select COLD at any time; no timed automatic schedule.","available_statuses":["HOT","COLD"],"optional":false,"enabled":true,"minimum_clearance_km":1.85,"additional_margin_km":0.2,"clearance_reference":">=1.85 km plus 0.20 km margin from final fan boundaries and FULL 2 nm airway terrain-protection swaths, including proposed perimeter reservation.","chart":"magenta hatched boundary; name, floor/ceiling, status; full airspace acceptance pending","name_status":"fictional design name; FAA exact-name search 2026-10-30 found no match, not authoritative uniqueness","hook_owner":"Claude: manual HOT may trigger combat; COLD never triggers","gunnery_targets":[{"id":"CZ5-GT1","centre":[14.8,2.4],"radius_m":14,"platform":"ground bullseye","top_agl_m":0.2}],"drone_orbit":{"centre_start":"CZ5","count":3,"radius_m_target":70,"altitude_ft_target":1500,"owner":"Claude opponent/AI logic"},"hot_entry_behavior":{"HOT":"On user entry, spawn this zone’s targets and drones; engagement hook belongs to Claude.","COLD":"No targets/drones appear on entry and no combat triggers."}},"new":{"id":"CZ5","name":"AP TERN MESA MOA","boundary":[[14.6,2.2],[15,2.2],[15,2.6],[14.6,2.6]],"over_land":true,"floor_msl_ft_target":200,"ceiling_msl_ft_target":2500,"agl_range_ft_bound":[620,2320],"default_status":"HOT","schedule":"Default HOT; pilot may select COLD at any time; no timed automatic schedule.","available_statuses":["HOT","COLD"],"optional":false,"enabled":true,"minimum_clearance_km":1.85,"additional_margin_km":0.2,"clearance_reference":">=1.85 km plus 0.20 km margin from final fan boundaries and FULL 2 nm airway terrain-protection swaths, including proposed perimeter reservation.","chart":"magenta hatched boundary; name, floor/ceiling, status; full airspace acceptance pending","name_status":"Fictional simulator-prefixed name; not a depiction of real SUA. Susitna is a real MOA name (FAA chart supplement), hence AP prefix on all zones.","hook_owner":"Claude implements explicit HOT-entry targets/drones spawn; COLD never spawns.","gunnery_targets":[{"id":"CZ5-GT1","centre":[14.66,2.28],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":220},{"id":"CZ5-GT2","centre":[14.94,2.52],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":220}],"drone_orbit":{"centre_start":"CZ5","count":3,"radius_m_target":70,"altitude_ft_target":600,"owner":"Claude opponent/AI logic","centre":[14.8,2.4]},"hot_entry_behavior":{"HOT":"On user entry, spawn this zone’s targets and drones; engagement hook belongs to Claude.","COLD":"No targets/drones appear on entry and no combat triggers."},"name_audit_source":"https://aeronav.faa.gov/afd/02OCT2025/AK_rear_02OCT2025.pdf","combat_features":[{"id":"CZ5-RIDGE","type":"paired ridges and dry canyon","centre":[14.8,2.4],"axis_true_deg":0,"length_m":300,"canyon_width_m":100,"canyon_depth_m":2.4384,"terrace_floor_ft":180,"rim_ft":188,"ridge_half_width_m":35,"flight_half_width_m":20,"flight_floor_msl_ft":220,"flight_ceiling_msl_ft":300},{"id":"CZ5-BRIDGE","type":"bridge","centre":[14.8,2.4],"heading_true_deg":90,"span_m":120,"deck_width_m":14,"under_clearance_m":60,"under_base_msl_ft":180,"deck_thickness_m":4,"flight_opening_width_m":100,"flight_floor_msl_ft":220,"flight_ceiling_msl_ft":300,"collision":"WorldSolids bridge deck/piers; no heightfield bridge deck"},{"id":"CZ5-ARCH","type":"rock arch","centre":[14.91,2.488],"axis_true_deg":0,"opening_width_m":60,"opening_height_m":55,"base_msl_ft":180,"rock_top_msl_ft":400,"flight_floor_msl_ft":220,"flight_ceiling_msl_ft":300,"collision":"WorldSolids overhang/opening; footprint pedestals only in shared heightfield"}],"low_level_training":{"floor_msl_ft":200,"flight_tube_ceiling_ft":300,"clear_of_traffic_overlays":true,"feature_solids":"All arch/bridge solids charted; no invisible heightfield ceilings; verified as analytic design envelopes"}},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>215. starts/CZ1</summary>

```json
{"phase":"1A.2","item":"starts/CZ1","old":{"id":"CZ1","mode":"InCombatZone","enabled":true,"centre":[8.999768,14.126807],"altitude_ft_target":5000,"heading_true_deg":122.5},"new":{"id":"CZ1","mode":"InCombatZone","enabled":true,"centre":[9.3,15.9],"altitude_ft_target":900,"heading_true_deg":122.5},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>216. starts/CZ2</summary>

```json
{"phase":"1A.2","item":"starts/CZ2","old":{"id":"CZ2","mode":"InCombatZone","enabled":true,"centre":[14.954078,-6.973201],"altitude_ft_target":5000,"heading_true_deg":205.0},"new":{"id":"CZ2","mode":"InCombatZone","enabled":true,"centre":[16.5,-7.5],"altitude_ft_target":900,"heading_true_deg":205.0},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>217. starts/CZ3</summary>

```json
{"phase":"1A.2","item":"starts/CZ3","old":{"id":"CZ3","mode":"InCombatZone","enabled":true,"centre":[-7.849726,15.079184],"altitude_ft_target":5000,"heading_true_deg":62.5},"new":{"id":"CZ3","mode":"InCombatZone","enabled":true,"centre":[-7.0,16.3],"altitude_ft_target":900,"heading_true_deg":62.5},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>218. starts/CZ5</summary>

```json
{"phase":"1A.2","item":"starts/CZ5","old":{"id":"CZ5","mode":"InCombatZone","enabled":true,"centre":[14.8,2.4],"altitude_ft_target":1500,"heading_true_deg":0},"new":{"id":"CZ5","mode":"InCombatZone","enabled":true,"centre":[14.8,2.4],"altitude_ft_target":600,"heading_true_deg":0},"reason":"Ty decisions8/9: two outer airport islands beneath raised-floor MOA traffic reservations; every zone gets island terrain, bridges/arches, low-level channels and ground targets, with explicit HOT-only spawns. AP-prefixed fictional names avoid real-SUA identity."}
```

</details>

<details>
<summary>219. AP4 fictional datum</summary>

```json
{"phase":"1A.2","item":"AP4 fictional datum","old":1350,"new":1250,"reason":"Lower100ft to restore locked35% pad-edge grade after rotation; fictional field elevation is target, PHDH14ft remains locked."}
```

</details>

<details>
<summary>220. Sulphur spacing</summary>

```json
{"phase":"1A.2","item":"Sulphur spacing","old":[4.7,0.0],"new":[4.7,-0.1],"reason":"100m south separates SoldierBar>=.8km and restores grade margin fromAP5."}
```

</details>

<details>
<summary>221. Thomas Creek KPGA fan clearance</summary>

```json
{"phase":"1A.2","item":"Thomas Creek KPGA fan clearance","old":[4.4,-0.9],"new":[4.6,-0.65],"reason":"Move full runway and shoulders beyond KPGA north-final fan and V2 endcap; preserve true reference elevation/length."}
```

</details>

<details>
<summary>222. Sierra peak core</summary>

```json
{"phase":"1A.2","item":"Sierra peak core","old":[-1.54,-0.58],"new":[-1.6,-0.3],"reason":"Move compact peak core off AP3/AP9 northfinal fans while preserving crest character/top."}
```

</details>

<details>
<summary>223. Height algorithm</summary>

```json
{"phase":"1A.2","item":"Height algorithm","old":"island-review-v1","new":"island-review-v2-1a2","reason":"Clipped-triangle validation; radial outer-island/crater relief, combat ridge detail, clearance cuts and explicit pad/cavity overlays."}
```

</details>

<details>
<summary>224. Heightfield format</summary>

```json
{"phase":"1A.2","item":"Heightfield format","old":"little-endian float32 metres MSL; row 0 south; column 0 west; x fastest; bilinear interpolation; JSON sidecar","new":"little-endian float32 metres MSL; row0south; column0west; xfastest; fixed SW-NE diagonal linear interpolation; analytic same-source pads/cavities/solids detail","reason":"Remove stale bilinear-interpolation statement; fixed triangle diagonal is used by full-area validator."}
```

</details>

<details>
<summary>225. Height clearance envelope finals</summary>

```json
{"phase":"1A.2","item":"Height clearance envelope finals","old":15,"new":30,"reason":"Increase erosion margin15m to30m and horizontal guard150m to250m to absorb grid interpolation at narrow pad-edge blends."}
```

</details>

<details>
<summary>226. Height clearance envelope finals guard</summary>

```json
{"phase":"1A.2","item":"Height clearance envelope finals guard","old":0.15,"new":0.25,"reason":"Increase guard to250m to prevent grid triangle crossing uncut terrain beside protection."}
```

</details>

<details>
<summary>227. KMYL centre</summary>

```json
{"phase":"1A.2","item":"KMYL centre","old":[-0.58,2.78],"new":[-0.88,2.78],"reason":"300m west shift keeps south20:1 final clear of fixed FrankChurch summit; dimensions/elevation/heading unchanged; Claude must reconcile LDA after gate."}
```

</details>

<details>
<summary>228. Thomas Creek centre</summary>

```json
{"phase":"1A.2","item":"Thomas Creek centre","old":[4.6,-0.65],"new":[4.6,-0.8],"reason":"Separate all>=.8km backcountry field centres and full pad rectangles; Thomas clears KPGA final and V2 rounded endcap."}
```

</details>

<details>
<summary>229. Sulphur Creek centre</summary>

```json
{"phase":"1A.2","item":"Sulphur Creek centre","old":[4.7,-0.1],"new":[4.7,0.1],"reason":"Separate all>=.8km backcountry field centres and full pad rectangles; Thomas clears KPGA final and V2 rounded endcap."}
```

</details>

<details>
<summary>230. Soldier Bar centre</summary>

```json
{"phase":"1A.2","item":"Soldier Bar centre","old":[4.1,0.5],"new":[4.0,0.8],"reason":"Separate all>=.8km backcountry field centres and full pad rectangles; Thomas clears KPGA final and V2 rounded endcap."}
```

</details>

<details>
<summary>231. Cabin Creek centre</summary>

```json
{"phase":"1A.2","item":"Cabin Creek centre","old":[3.2,0.3],"new":[3.1,0.75],"reason":"Separate all>=.8km backcountry field centres and full pad rectangles; Thomas clears KPGA final and V2 rounded endcap."}
```

</details>

<details>
<summary>232. Thomas Creek heading</summary>

```json
{"phase":"1A.2","item":"Thomas Creek heading","old":25,"new":90,"reason":"Orient complete20:1 final fans away from other fields, mountain cores, box volumes and charted wire crossings; preserve runway dimensions."}
```

</details>

<details>
<summary>233. Big Creek heading</summary>

```json
{"phase":"1A.2","item":"Big Creek heading","old":145,"new":0,"reason":"Orient complete20:1 final fans away from other fields, mountain cores, box volumes and charted wire crossings; preserve runway dimensions."}
```

</details>

<details>
<summary>234. Cabin Creek heading</summary>

```json
{"phase":"1A.2","item":"Cabin Creek heading","old":98,"new":0,"reason":"Orient complete20:1 final fans away from other fields, mountain cores, box volumes and charted wire crossings; preserve runway dimensions."}
```

</details>

<details>
<summary>235. Cabin Creek one-way heading</summary>

```json
{"phase":"1A.2","item":"Cabin Creek one-way heading","old":98,"new":0,"reason":"One-way direction follows relocated strip."}
```

</details>

<details>
<summary>236. AP1 heading</summary>

```json
{"phase":"1A.2","item":"AP1 heading","old":167,"new":90,"reason":"Orient complete20:1 final fans away from other fields, mountain cores, box volumes and charted wire crossings; preserve runway dimensions."}
```

</details>

<details>
<summary>237. AP1 magnetic numbers</summary>

```json
{"phase":"1A.2","item":"AP1 magnetic numbers","old":"16/34","new":"8/26","reason":"Heading minus locked7E."}
```

</details>

<details>
<summary>238. AP10 heading</summary>

```json
{"phase":"1A.2","item":"AP10 heading","old":17,"new":90,"reason":"Orient complete20:1 final fans away from other fields, mountain cores, box volumes and charted wire crossings; preserve runway dimensions."}
```

</details>

<details>
<summary>239. AP10 magnetic numbers</summary>

```json
{"phase":"1A.2","item":"AP10 magnetic numbers","old":"1/19","new":"8/26","reason":"Heading minus locked7E."}
```

</details>

<details>
<summary>240. B3 heading</summary>

```json
{"phase":"1A.2","item":"B3 heading","old":90,"new":0,"reason":"Orient complete20:1 final fans away from other fields, mountain cores, box volumes and charted wire crossings; preserve runway dimensions."}
```

</details>

<details>
<summary>241. AP11 elevation</summary>

```json
{"phase":"1A.2","item":"AP11 elevation","old":4000,"new":5024,"reason":"Fictional east-shore strip shares McCall/Payette plateau and prevents mutual approach penetration from adjacent high fields; true ICAO elevations retained."}
```

</details>

<details>
<summary>242. AB2 box geometry</summary>

```json
{"phase":"1A.2","item":"AB2 box geometry","old":{"id":"AB2","name":"Cook Inlet","centre":[-13.0,-5.4],"airport":"PANC","density_altitude":"low","side_m":1000,"floor_agl_m":100,"ceiling_agl_m":1100,"centre_ground_ft":0,"floor_msl_ft_target":328,"ceiling_msl_ft_target":3609,"judges_offset_m":150,"judges_side":"east","markers":{"colour":"white","cross_arm_m":40,"cross_width_m":4,"corner_L_leg_m":30,"corner_width_m":4,"side_T_leg_m":30,"side_T_width_m":4,"rafts":true,"orange_corner_buoys":true,"shore_judges_marker":true},"rafts":[{"id":"AB2-R1","shape":"cross","centre":[-13.0,-5.4],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R2","shape":"L","centre":[-13.5,-5.9],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R3","shape":"L","centre":[-13.5,-4.9],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R4","shape":"L","centre":[-12.5,-5.9],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R5","shape":"L","centre":[-12.5,-4.9],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R6","shape":"T","centre":[-13.0,-4.9],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R7","shape":"T","centre":[-13.0,-5.9],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R8","shape":"T","centre":[-12.5,-5.4],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R9","shape":"T","centre":[-13.5,-5.4],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"}],"corner_buoys":[{"centre":[-13.5,-5.9],"colour":"orange","moored":true},{"centre":[-13.5,-4.9],"colour":"orange","moored":true},{"centre":[-12.5,-5.9],"colour":"orange","moored":true},{"centre":[-12.5,-4.9],"colour":"orange","moored":true}],"judges_line":{"centre":[-12.35,-5.4],"length_m":1000,"orientation_true_deg":0,"marker":"moored judge-reference raft on line; matching shore reference aligned along airport-facing axis"},"shore_reference_marker":{"centre":[-11.4,-5.4],"meaning":"shore sighting reference aligned to offshore judges line; actual line remains 150 m beyond square"},"judges_shore_marker_status":"resolved: line raft plus aligned shore reference; no false claim that offshore judges line intersects land"},"new":{"id":"AB2","name":"Cook Inlet","centre":[-12.3,-6.3],"airport":"PANC","density_altitude":"low","side_m":1000,"floor_agl_m":100,"ceiling_agl_m":1100,"centre_ground_ft":0,"floor_msl_ft_target":328,"ceiling_msl_ft_target":3609,"judges_offset_m":150,"judges_side":"east","markers":{"colour":"white","cross_arm_m":40,"cross_width_m":4,"corner_L_leg_m":30,"corner_width_m":4,"side_T_leg_m":30,"side_T_width_m":4,"rafts":true,"orange_corner_buoys":true,"shore_judges_marker":true},"rafts":[{"id":"AB2-R1","shape":"cross","centre":[-12.3,-6.3],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R2","shape":"L","centre":[-12.8,-6.8],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R3","shape":"L","centre":[-12.8,-5.8],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R4","shape":"L","centre":[-11.8,-6.8],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R5","shape":"L","centre":[-11.8,-5.8],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R6","shape":"T","centre":[-12.3,-5.8],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R7","shape":"T","centre":[-12.3,-6.8],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R8","shape":"T","centre":[-11.8,-6.3],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R9","shape":"T","centre":[-12.8,-6.3],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"}],"corner_buoys":[{"centre":[-12.8,-6.8],"colour":"orange","moored":true},{"centre":[-12.8,-5.8],"colour":"orange","moored":true},{"centre":[-11.8,-6.8],"colour":"orange","moored":true},{"centre":[-11.8,-5.8],"colour":"orange","moored":true}],"judges_line":{"centre":[-11.65,-6.3],"length_m":1000,"orientation_true_deg":0,"marker":"moored judge-reference raft on line; matching shore reference aligned along airport-facing axis"},"shore_reference_marker":{"centre":[-9,-6.3],"meaning":"shore sighting reference aligned to offshore judges line; actual line remains 150 m beyond square"},"judges_shore_marker_status":"resolved: line raft plus aligned shore reference; no false claim that offshore judges line intersects land"},"reason":"Keep AB2 fully water and outside low-MEA A2 swath, with B3/B4 final clearance. Place single high-DA AB3 beside McCall parallel runway midsection, east of A2. Retain locked standard square/vertical extent."}
```

</details>

<details>
<summary>243. AB2 start</summary>

```json
{"phase":"1A.2","item":"AB2 start","old":[-13.0,-5.4],"new":[-12.3,-6.3],"reason":"Start follows revised box."}
```

</details>

<details>
<summary>244. AB3 box geometry</summary>

```json
{"phase":"1A.2","item":"AB3 box geometry","old":{"id":"AB3","name":"McCall","centre":[1.2,5.7],"airport":"KMYL","density_altitude":"high","side_m":1000,"floor_agl_m":100,"ceiling_agl_m":1100,"centre_ground_ft":5000,"floor_msl_ft_target":5328,"ceiling_msl_ft_target":8609,"judges_offset_m":150,"judges_side":"east","markers":{"colour":"white","cross_arm_m":40,"cross_width_m":4,"corner_L_leg_m":30,"corner_width_m":4,"side_T_leg_m":30,"side_T_width_m":4,"rafts":false,"orange_corner_buoys":false,"shore_judges_marker":false}},"new":{"id":"AB3","name":"McCall","centre":[-1.8,2.8],"airport":"KMYL","density_altitude":"high","side_m":1000,"floor_agl_m":100,"ceiling_agl_m":1100,"centre_ground_ft":5000,"floor_msl_ft_target":5328,"ceiling_msl_ft_target":8609,"judges_offset_m":150,"judges_side":"east","markers":{"colour":"white","cross_arm_m":40,"cross_width_m":4,"corner_L_leg_m":30,"corner_width_m":4,"side_T_leg_m":30,"side_T_width_m":4,"rafts":false,"orange_corner_buoys":false,"shore_judges_marker":false}},"reason":"Keep AB2 fully water and outside low-MEA A2 swath, with B3/B4 final clearance. Place single high-DA AB3 beside McCall parallel runway midsection, east of A2. Retain locked standard square/vertical extent."}
```

</details>

<details>
<summary>245. AB3 start</summary>

```json
{"phase":"1A.2","item":"AB3 start","old":[1.2,5.7],"new":[-1.8,2.8],"reason":"Start follows revised box."}
```

</details>

<details>
<summary>246. box-final guard</summary>

```json
{"phase":"1A.2","item":"box-final guard","old":null,"new":0.15,"reason":"Require positive150m full-boundary separation; starting distances are targets, no locked numerical box/final buffer was specified. Report every computed nearest gap."}
```

</details>

<details>
<summary>247. PALH water runway heading</summary>

```json
{"phase":"1A.2","item":"PALH water runway heading","old":90,"new":165,"reason":"Rotate non-IFR water lane parallel toPANC, avoiding protected mountain/high-field final crossings; preserve locked length/elevation and immediate proximity."}
```

</details>

<details>
<summary>248. PALH water numbers</summary>

```json
{"phase":"1A.2","item":"PALH water numbers","old":"8W/26W","new":"16W/34W","reason":"158magnetic after rotation."}
```

</details>

<details>
<summary>249. PALH water centre</summary>

```json
{"phase":"1A.2","item":"PALH water centre","old":[-9.56,-1.8],"new":[-10.18,-1.8],"reason":"Set lake centre approximately400m east-normal ofPANC;160m lake half-width leaves about240m closest west shore/centreline gap, retaining deliberate~250m waiver."}
```

</details>

<details>
<summary>250. LakeHood centre</summary>

```json
{"phase":"1A.2","item":"LakeHood centre","old":[-9.56,-1.8],"new":[-10.18,-1.8],"reason":"Follow rotated water-lane pad."}
```

</details>

<details>
<summary>251. LakeHood axes</summary>

```json
{"phase":"1A.2","item":"LakeHood axes","old":[0.85,0.18],"new":[0.78,0.16],"reason":"780m along-lane/160m cross-lane ellipse, same dimensions after rotation."}
```

</details>

<details>
<summary>252. LakeHood oriented ellipse</summary>

```json
{"phase":"1A.2","item":"LakeHood oriented ellipse","old":null,"new":165,"reason":"Orient long lake axis with waterlane; no IFR atPALH."}
```

</details>

<details>
<summary>253. PALH gravel centre</summary>

```json
{"phase":"1A.2","item":"PALH gravel centre","old":[-9.45,-1.25],"new":[-9.5,-1.3],"reason":"Keep gravel strip adjacent east ofLakeHood and clear ofwaterpad, preserve datum/heading/length."}
```

</details>

<details>
<summary>254. T3 coastal tower</summary>

```json
{"phase":"1A.2","item":"T3 coastal tower","old":[2.9,-7.4],"new":[5.7,-7.4],"reason":"Move180m guyed tower offV6 swath and away fromLOCBC final; still charts coastal OC obstruction. Preserve tower height and count."}
```

</details>

<details>
<summary>255. Denali peak eastern headwall</summary>

```json
{"phase":"1A.2","item":"Denali peak eastern headwall","old":[-4.2,6.0],"new":[-2.1,6.9],"reason":"Place8530ft peak outsideA2 low-MEA capsule and PHDH westfinal; highA1/V7 allow required2000ft; retains northern alpine wall."}
```

</details>

<details>
<summary>256. outer_islands/OI4</summary>

```json
{"phase":"1A.2","item":"outer_islands/OI4","old":{"id":"OI4","name":"Kite Anchorage","coast_polygon":[[9.017657715387712,12.364096882920155],[8.953616837563027,12.28098313119099],[8.873259311255106,12.210171957436282],[8.777960076018386,12.152874960471545],[8.669349728189712,12.110072507800572],[8.549286620932904,12.082496961255647],[8.419825067300913,12.070620146083538],[8.283180190369904,12.074645277884715],[8.141690021869286,12.094503485538082],[7.997775497810586,12.129854989604956],[7.853899035601097,12.180094916049464],[7.712522401399313,12.244363645801],[7.576064588613436,12.321561523074859],[7.446860428253483,12.410367670787588],[7.327120639325377,12.509262591130108],[7.218894002815817,12.616554164596506],[7.124032306481573,12.730406602617927],[7.044158660247557,12.848871858413967],[6.980639724345992,12.969922958613182],[6.934562325380927,13.091488685329455],[6.906714860423726,13.211489015274287],[6.897573807320581,13.327870709531942],[6.907295572024465,13.43864244504657],[6.935713812445913,13.541908886711791],[6.9823422846122885,13.635903117079845],[7.046383162436971,13.71901686880901],[7.126740688744894,13.789828042563718],[7.222039923981614,13.847125039528455],[7.33065027181029,13.889927492199428],[7.450713379067096,13.917503038744353],[7.580174932699087,13.929379853916462],[7.7168198096300955,13.925354722115284],[7.858309978130715,13.905496514461918],[8.002224502189414,13.870145010395044],[8.146100964398903,13.819905083950536],[8.287477598600686,13.755636354199],[8.423935411386564,13.678438476925141],[8.553139571746515,13.589632329212412],[8.672879360674623,13.490737408869892],[8.781105997184182,13.383445835403494],[8.875967693518428,13.269593397382073],[8.95584133975244,13.151128141586033],[9.019360275654009,13.030077041386818],[9.065437674619073,12.908511314670545],[9.093285139576274,12.788510984725715],[9.102426192679419,12.672129290468058],[9.092704427975535,12.56135755495343],[9.064286187554087,12.458091113288209]],"base_ft":50,"airport":"KITE","under_zone":"CZ1"},"new":{"id":"OI4","name":"Kite Anchorage","coast_polygon":[[9.059537111430712,14.236634124656932],[9.001449956205997,14.149255568037168],[8.926227714576218,14.07301142906873],[8.8351574599512,14.009206266108237],[8.729797429637024,13.958931803184075],[8.611950362963686,13.923048250295594],[8.483632655915343,13.902169584981511],[8.347039860035986,13.896653046994016],[8.204509115933787,13.906593025827545],[8.058479164157319,13.931819445688374],[7.911448617668634,13.971900675538599],[7.7659332098827205,14.026150914422868],[7.624422749771287,14.09364192571286],[7.4893385205409535,14.173218919493362],[7.362991850806274,14.263520311337878],[7.247544567116325,14.363001019395673],[7.144972004503074,14.469958901170616],[7.057029207951322,14.582563877651209],[6.985220903092417,14.69888924646987],[6.930775749931521,14.816944648314632],[6.8946253201342635,14.934710122528784],[6.877388157577268,15.050170669197769],[6.879359194890931,15.161350726356366],[6.900504707080339,15.266347972401656],[6.940462888569288,15.363365875343069],[6.998550043794001,15.450744431962834],[7.073772285423782,15.526988570931271],[7.1648425400488005,15.590793733891765],[7.270202570362976,15.641068196815926],[7.388049637036315,15.676951749704408],[7.516367344084657,15.69783041501849],[7.652960139964013,15.703346953005985],[7.795490884066214,15.693406974172456],[7.941520835842681,15.668180554311627],[8.088551382331366,15.628099324461402],[8.234066790117279,15.573849085577134],[8.375577250228712,15.506358074287142],[8.510661479459047,15.42678108050664],[8.637008149193726,15.336479688662124],[8.752455432883675,15.23699898060433],[8.855027995496927,15.130041098829386],[8.942970792048678,15.017436122348794],[9.014779096907581,14.901110753530132],[9.069224250068478,14.78305535168537],[9.105374679865736,14.665289877471217],[9.122611842422732,14.549829330802233],[9.120640805109069,14.438649273643637],[9.099495292919661,14.333652027598346]],"base_ft":50,"airport":"KITE","under_zone":"CZ1"},"reason":"Move airport islands clear of external beach/airpark protection exclusions; retain above-pattern5000ft MOA floor over3000ft traffic with300m own-final guard. Move low-level island arenas and target bounds outside all raised-floor traffic masks and within ring."}
```

</details>

<details>
<summary>257. outer_islands/OI5</summary>

```json
{"phase":"1A.2","item":"outer_islands/OI5","old":{"id":"OI5","name":"Rime Harbour","coast_polygon":[[-6.971399239157465,14.118045689892066],[-6.926418294244393,14.02325199625277],[-6.8998066299698815,13.91950531588473],[-6.8920195792878145,13.808580783253053],[-6.903190380794341,13.692376347865164],[-6.933127898978553,13.572880299828498],[-6.981319894604071,13.45213724965553],[-7.046941789264395,13.332213144411021],[-7.128870774148061,13.215159918784941],[-7.225705021608705,13.102980385921041],[-7.335787670824749,12.997593968728667],[-7.45723517714743,12.900803858024611],[-7.587969540071956,12.81426615943831],[-7.725753858402277,12.73946155698536],[-7.868230604250732,12.67766997815348],[-8.012961960993833,12.629948693988352],[-8.157471534990595,12.597114228892877],[-8.299286727364311,12.579728389668272],[-8.435981040854866,12.578088652843936],[-8.565215597859615,12.592223074771557],[-8.684779159277616,12.621889811573148],[-8.792625959423724,12.66658125715686],[-8.886910709646662,12.725532728497948],[-8.966020171729438,12.79773554957733],[-9.028600760842535,12.881954310107934],[-9.073581705755606,12.97674800374723],[-9.100193370030118,13.080494684115271],[-9.107980420712185,13.191419216746946],[-9.09680961920566,13.307623652134836],[-9.066872101021447,13.427119700171502],[-9.01868010539593,13.547862750344468],[-8.953058210735605,13.667786855588979],[-8.87112922585194,13.784840081215059],[-8.774294978391294,13.897019614078959],[-8.664212329175252,14.002406031271333],[-8.54276482285257,14.099196141975389],[-8.412030459928044,14.18573384056169],[-8.274246141597724,14.26053844301464],[-8.13176939574927,14.322330021846518],[-7.987038039006167,14.370051306011648],[-7.842528465009404,14.402885771107123],[-7.700713272635689,14.420271610331728],[-7.564018959145134,14.421911347156064],[-7.4347844021403855,14.407776925228443],[-7.315220840722385,14.378110188426854],[-7.207374040576275,14.333418742843138],[-7.113089290353339,14.274467271502052],[-7.033979828270561,14.20226445042267]],"base_ft":70,"airport":"RIME","under_zone":"CZ3"},"new":{"id":"OI5","name":"Rime Harbour","coast_polygon":[[-8.50515491293395,14.671031484164896],[-8.455274494240175,14.578721822752955],[-8.423269618602582,14.476510070501712],[-8.409687898316758,14.36614509883063],[-8.41476172038236,14.249515283003959],[-8.438404270297115,14.128616191506383],[-8.480211017477027,14.005516441312988],[-8.539466636886829,13.882322303279281],[-8.615157248449657,13.761141663262887],[-8.705987764816271,13.644047955612107],[-8.810404050668659,13.533044686129356],[-8.926619514406225,13.430031151531404],[-9.052645677223403,13.336769941955966],[-9.186326196533338,13.254856782555775],[-9.325373761587397,13.185693230198785],[-9.467409229996235,13.130462692441581],[-9.610002335515707,13.09011017909799],[-9.750713270576096,13.065326132859152],[-9.887134432066079,13.056534615627644],[-10.016931616089465,13.063886052700772],[-10.137883956841042,13.08725465895201],[-10.247921926236224,13.126240591049394],[-10.345162744110372,13.180176788885795],[-10.427942593109407,13.2481403891623],[-10.49484508706605,13.328968515835104],[-10.544725505759825,13.421278177247045],[-10.576730381397418,13.523489929498288],[-10.590312101683242,13.633854901169368],[-10.58523827961764,13.750484716996041],[-10.561595729702885,13.871383808493617],[-10.519788982522973,13.994483558687012],[-10.460533363113171,14.117677696720719],[-10.384842751550341,14.238858336737113],[-10.294012235183729,14.355952044387893],[-10.189595949331341,14.466955313870644],[-10.073380485593775,14.569968848468596],[-9.947354322776597,14.663230058044034],[-9.813673803466662,14.745143217444223],[-9.674626238412603,14.814306769801215],[-9.532590770003765,14.869537307558419],[-9.389997664484293,14.90988982090201],[-9.249286729423904,14.934673867140848],[-9.112865567933921,14.943465384372356],[-8.983068383910535,14.936113947299228],[-8.862116043158958,14.91274534104799],[-8.752078073763776,14.873759408950608],[-8.65483725588963,14.819823211114205],[-8.572057406890593,14.7518596108377]],"base_ft":70,"airport":"RIME","under_zone":"CZ3"},"reason":"Move airport islands clear of external beach/airpark protection exclusions; retain above-pattern5000ft MOA floor over3000ft traffic with300m own-final guard. Move low-level island arenas and target bounds outside all raised-floor traffic masks and within ring."}
```

</details>

<details>
<summary>258. airports/KITE</summary>

```json
{"phase":"1A.2","item":"airports/KITE","old":{"id":"KITE","name":"Kite Anchorage","elevation_ft":50,"start_group":"non-start","shore_class":"coastal","island":"OI4","runways":[{"id":"12/30","centre":[8,13],"heading_true_deg":122,"length_m":750,"width_m":24,"surface":"gravel","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":180}}],"note":"Fictional outer-island field; full two-end protection retained. Under MOA raised-floor traffic reservation, no new start."},"new":{"id":"KITE","name":"Kite Anchorage","elevation_ft":50,"start_group":"non-start","shore_class":"coastal","island":"OI4","runways":[{"id":"11/29","centre":[8,14.8],"heading_true_deg":118,"length_m":750,"width_m":24,"surface":"gravel","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":180}}],"note":"Fictional outer-island field; full two-end protection retained. Under MOA raised-floor traffic reservation, no new start."},"reason":"Move airport islands clear of external beach/airpark protection exclusions; retain above-pattern5000ft MOA floor over3000ft traffic with300m own-final guard. Move low-level island arenas and target bounds outside all raised-floor traffic masks and within ring."}
```

</details>

<details>
<summary>259. airports/RIME</summary>

```json
{"phase":"1A.2","item":"airports/RIME","old":{"id":"RIME","name":"Rime Harbour","elevation_ft":70,"start_group":"non-start","shore_class":"coastal","island":"OI5","runways":[{"id":"5/23","centre":[-8,13.5],"heading_true_deg":59,"length_m":750,"width_m":24,"surface":"gravel","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":180}}],"note":"Fictional outer-island field; full two-end protection retained. Under MOA raised-floor traffic reservation, no new start."},"new":{"id":"RIME","name":"Rime Harbour","elevation_ft":70,"start_group":"non-start","shore_class":"coastal","island":"OI5","runways":[{"id":"5/23","centre":[-9.5,14],"heading_true_deg":56,"length_m":750,"width_m":24,"surface":"gravel","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":180}}],"note":"Fictional outer-island field; full two-end protection retained. Under MOA raised-floor traffic reservation, no new start."},"reason":"Move airport islands clear of external beach/airpark protection exclusions; retain above-pattern5000ft MOA floor over3000ft traffic with300m own-final guard. Move low-level island arenas and target bounds outside all raised-floor traffic masks and within ring."}
```

</details>

<details>
<summary>260. outer_navaid_sites/SITE-KITE</summary>

```json
{"phase":"1A.2","item":"outer_navaid_sites/SITE-KITE","old":{"id":"SITE-KITE","centre":[8.05,13],"elevation_ft":50,"owner":"Claude equipment/ident/frequency; position/elevation only"},"new":{"id":"SITE-KITE","centre":[8.05,14.8],"elevation_ft":50,"owner":"Claude equipment/ident/frequency; position/elevation only"},"reason":"Move airport islands clear of external beach/airpark protection exclusions; retain above-pattern5000ft MOA floor over3000ft traffic with300m own-final guard. Move low-level island arenas and target bounds outside all raised-floor traffic masks and within ring."}
```

</details>

<details>
<summary>261. outer_navaid_sites/SITE-RIME</summary>

```json
{"phase":"1A.2","item":"outer_navaid_sites/SITE-RIME","old":{"id":"SITE-RIME","centre":[-7.95,13.5],"elevation_ft":70,"owner":"Claude equipment/ident/frequency; position/elevation only"},"new":{"id":"SITE-RIME","centre":[-9.45,14],"elevation_ft":70,"owner":"Claude equipment/ident/frequency; position/elevation only"},"reason":"Move airport islands clear of external beach/airpark protection exclusions; retain above-pattern5000ft MOA floor over3000ft traffic with300m own-final guard. Move low-level island arenas and target bounds outside all raised-floor traffic masks and within ring."}
```

</details>

<details>
<summary>262. islets/CZ1-IS1</summary>

```json
{"phase":"1A.2","item":"islets/CZ1-IS1","old":{"id":"CZ1-IS1","centre":[8.8,15.9],"radii_km":[0.35,0.55],"elevation_ft":60,"combat_zone":"CZ1","relief":{"type":"ridge","top_ft":110,"radius_km":0.24499999999999997}},"new":{"id":"CZ1-IS1","centre":[8.8,15.6],"radii_km":[0.35,0.55],"elevation_ft":60,"combat_zone":"CZ1","relief":{"type":"ridge","top_ft":110,"radius_km":0.24499999999999997}},"reason":"Move airport islands clear of external beach/airpark protection exclusions; retain above-pattern5000ft MOA floor over3000ft traffic with300m own-final guard. Move low-level island arenas and target bounds outside all raised-floor traffic masks and within ring."}
```

</details>

<details>
<summary>263. islets/CZ1-IS2</summary>

```json
{"phase":"1A.2","item":"islets/CZ1-IS2","old":{"id":"CZ1-IS2","centre":[9.8,15.9],"radii_km":[0.35,0.55],"elevation_ft":60,"combat_zone":"CZ1","relief":{"type":"rolling island","top_ft":110,"radius_km":0.24499999999999997}},"new":{"id":"CZ1-IS2","centre":[9.8,15.6],"radii_km":[0.35,0.55],"elevation_ft":60,"combat_zone":"CZ1","relief":{"type":"rolling island","top_ft":110,"radius_km":0.24499999999999997}},"reason":"Move airport islands clear of external beach/airpark protection exclusions; retain above-pattern5000ft MOA floor over3000ft traffic with300m own-final guard. Move low-level island arenas and target bounds outside all raised-floor traffic masks and within ring."}
```

</details>

<details>
<summary>264. islets/CZ1-IS3</summary>

```json
{"phase":"1A.2","item":"islets/CZ1-IS3","old":{"id":"CZ1-IS3","centre":[9.3,16.7],"radii_km":[0.34,0.25],"elevation_ft":60,"combat_zone":"CZ1","relief":{"type":"rolling island","top_ft":110,"radius_km":0.175}},"new":{"id":"CZ1-IS3","centre":[9.3,16.4],"radii_km":[0.34,0.25],"elevation_ft":60,"combat_zone":"CZ1","relief":{"type":"rolling island","top_ft":110,"radius_km":0.175}},"reason":"Move airport islands clear of external beach/airpark protection exclusions; retain above-pattern5000ft MOA floor over3000ft traffic with300m own-final guard. Move low-level island arenas and target bounds outside all raised-floor traffic masks and within ring."}
```

</details>

<details>
<summary>265. islets/CZ1-IS4</summary>

```json
{"phase":"1A.2","item":"islets/CZ1-IS4","old":{"id":"CZ1-IS4","centre":[8.5,15.3],"radii_km":[0.2,0.24],"elevation_ft":60,"combat_zone":"CZ1","relief":{"type":"rolling island","top_ft":110,"radius_km":0.13999999999999999}},"new":{"id":"CZ1-IS4","centre":[8.5,15.0],"radii_km":[0.2,0.24],"elevation_ft":60,"combat_zone":"CZ1","relief":{"type":"rolling island","top_ft":110,"radius_km":0.13999999999999999}},"reason":"Move airport islands clear of external beach/airpark protection exclusions; retain above-pattern5000ft MOA floor over3000ft traffic with300m own-final guard. Move low-level island arenas and target bounds outside all raised-floor traffic masks and within ring."}
```

</details>

<details>
<summary>266. islets/CZ1-IS5</summary>

```json
{"phase":"1A.2","item":"islets/CZ1-IS5","old":{"id":"CZ1-IS5","centre":[10.15,16.55],"radii_km":[0.17,0.2],"elevation_ft":60,"combat_zone":"CZ1","relief":{"type":"spire","top_ft":110,"radius_km":0.119}},"new":{"id":"CZ1-IS5","centre":[10.15,16.25],"radii_km":[0.17,0.2],"elevation_ft":60,"combat_zone":"CZ1","relief":{"type":"spire","top_ft":110,"radius_km":0.119}},"reason":"Move airport islands clear of external beach/airpark protection exclusions; retain above-pattern5000ft MOA floor over3000ft traffic with300m own-final guard. Move low-level island arenas and target bounds outside all raised-floor traffic masks and within ring."}
```

</details>

<details>
<summary>267. islets/CZ3-IS1</summary>

```json
{"phase":"1A.2","item":"islets/CZ3-IS1","old":{"id":"CZ3-IS1","centre":[-7.5,16.3],"radii_km":[0.35,0.55],"elevation_ft":60,"combat_zone":"CZ3","relief":{"type":"ridge","top_ft":110,"radius_km":0.24499999999999997}},"new":{"id":"CZ3-IS1","centre":[-8.0,16.9],"radii_km":[0.35,0.55],"elevation_ft":60,"combat_zone":"CZ3","relief":{"type":"ridge","top_ft":110,"radius_km":0.24499999999999997}},"reason":"Move airport islands clear of external beach/airpark protection exclusions; retain above-pattern5000ft MOA floor over3000ft traffic with300m own-final guard. Move low-level island arenas and target bounds outside all raised-floor traffic masks and within ring."}
```

</details>

<details>
<summary>268. islets/CZ3-IS2</summary>

```json
{"phase":"1A.2","item":"islets/CZ3-IS2","old":{"id":"CZ3-IS2","centre":[-6.5,16.3],"radii_km":[0.35,0.55],"elevation_ft":60,"combat_zone":"CZ3","relief":{"type":"rolling island","top_ft":110,"radius_km":0.24499999999999997}},"new":{"id":"CZ3-IS2","centre":[-7.0,16.9],"radii_km":[0.35,0.55],"elevation_ft":60,"combat_zone":"CZ3","relief":{"type":"rolling island","top_ft":110,"radius_km":0.24499999999999997}},"reason":"Move airport islands clear of external beach/airpark protection exclusions; retain above-pattern5000ft MOA floor over3000ft traffic with300m own-final guard. Move low-level island arenas and target bounds outside all raised-floor traffic masks and within ring."}
```

</details>

<details>
<summary>269. islets/CZ3-IS3</summary>

```json
{"phase":"1A.2","item":"islets/CZ3-IS3","old":{"id":"CZ3-IS3","centre":[-7.0,17.1],"radii_km":[0.34,0.25],"elevation_ft":60,"combat_zone":"CZ3","relief":{"type":"rolling island","top_ft":110,"radius_km":0.175}},"new":{"id":"CZ3-IS3","centre":[-7.5,17.7],"radii_km":[0.34,0.25],"elevation_ft":60,"combat_zone":"CZ3","relief":{"type":"rolling island","top_ft":110,"radius_km":0.175}},"reason":"Move airport islands clear of external beach/airpark protection exclusions; retain above-pattern5000ft MOA floor over3000ft traffic with300m own-final guard. Move low-level island arenas and target bounds outside all raised-floor traffic masks and within ring."}
```

</details>

<details>
<summary>270. islets/CZ3-IS4</summary>

```json
{"phase":"1A.2","item":"islets/CZ3-IS4","old":{"id":"CZ3-IS4","centre":[-7.8,15.7],"radii_km":[0.2,0.24],"elevation_ft":60,"combat_zone":"CZ3","relief":{"type":"rolling island","top_ft":110,"radius_km":0.13999999999999999}},"new":{"id":"CZ3-IS4","centre":[-8.3,16.3],"radii_km":[0.2,0.24],"elevation_ft":60,"combat_zone":"CZ3","relief":{"type":"rolling island","top_ft":110,"radius_km":0.13999999999999999}},"reason":"Move airport islands clear of external beach/airpark protection exclusions; retain above-pattern5000ft MOA floor over3000ft traffic with300m own-final guard. Move low-level island arenas and target bounds outside all raised-floor traffic masks and within ring."}
```

</details>

<details>
<summary>271. islets/CZ3-IS5</summary>

```json
{"phase":"1A.2","item":"islets/CZ3-IS5","old":{"id":"CZ3-IS5","centre":[-6.15,16.95],"radii_km":[0.17,0.2],"elevation_ft":60,"combat_zone":"CZ3","relief":{"type":"spire","top_ft":110,"radius_km":0.119}},"new":{"id":"CZ3-IS5","centre":[-6.65,17.55],"radii_km":[0.17,0.2],"elevation_ft":60,"combat_zone":"CZ3","relief":{"type":"spire","top_ft":110,"radius_km":0.119}},"reason":"Move airport islands clear of external beach/airpark protection exclusions; retain above-pattern5000ft MOA floor over3000ft traffic with300m own-final guard. Move low-level island arenas and target bounds outside all raised-floor traffic masks and within ring."}
```

</details>

<details>
<summary>272. islets/CZ1-ARCH-W</summary>

```json
{"phase":"1A.2","item":"islets/CZ1-ARCH-W","old":{"id":"CZ1-ARCH-W","centre":[9.725000000000001,16.3],"radii_km":[0.025,0.04],"elevation_ft":110,"combat_zone":"CZ1","pedestal_only":true},"new":{"id":"CZ1-ARCH-W","centre":[9.725,16.0],"radii_km":[0.025,0.04],"elevation_ft":110,"combat_zone":"CZ1","pedestal_only":true},"reason":"Move airport islands clear of external beach/airpark protection exclusions; retain above-pattern5000ft MOA floor over3000ft traffic with300m own-final guard. Move low-level island arenas and target bounds outside all raised-floor traffic masks and within ring."}
```

</details>

<details>
<summary>273. islets/CZ1-ARCH-E</summary>

```json
{"phase":"1A.2","item":"islets/CZ1-ARCH-E","old":{"id":"CZ1-ARCH-E","centre":[9.875,16.3],"radii_km":[0.025,0.04],"elevation_ft":110,"combat_zone":"CZ1","pedestal_only":true},"new":{"id":"CZ1-ARCH-E","centre":[9.875,16.0],"radii_km":[0.025,0.04],"elevation_ft":110,"combat_zone":"CZ1","pedestal_only":true},"reason":"Move airport islands clear of external beach/airpark protection exclusions; retain above-pattern5000ft MOA floor over3000ft traffic with300m own-final guard. Move low-level island arenas and target bounds outside all raised-floor traffic masks and within ring."}
```

</details>

<details>
<summary>274. islets/CZ3-ARCH-W</summary>

```json
{"phase":"1A.2","item":"islets/CZ3-ARCH-W","old":{"id":"CZ3-ARCH-W","centre":[-6.575,16.7],"radii_km":[0.025,0.04],"elevation_ft":110,"combat_zone":"CZ3","pedestal_only":true},"new":{"id":"CZ3-ARCH-W","centre":[-7.075,17.3],"radii_km":[0.025,0.04],"elevation_ft":110,"combat_zone":"CZ3","pedestal_only":true},"reason":"Move airport islands clear of external beach/airpark protection exclusions; retain above-pattern5000ft MOA floor over3000ft traffic with300m own-final guard. Move low-level island arenas and target bounds outside all raised-floor traffic masks and within ring."}
```

</details>

<details>
<summary>275. islets/CZ3-ARCH-E</summary>

```json
{"phase":"1A.2","item":"islets/CZ3-ARCH-E","old":{"id":"CZ3-ARCH-E","centre":[-6.425,16.7],"radii_km":[0.025,0.04],"elevation_ft":110,"combat_zone":"CZ3","pedestal_only":true},"new":{"id":"CZ3-ARCH-E","centre":[-6.925,17.3],"radii_km":[0.025,0.04],"elevation_ft":110,"combat_zone":"CZ3","pedestal_only":true},"reason":"Move airport islands clear of external beach/airpark protection exclusions; retain above-pattern5000ft MOA floor over3000ft traffic with300m own-final guard. Move low-level island arenas and target bounds outside all raised-floor traffic masks and within ring."}
```

</details>

<details>
<summary>276. combat_zones/CZ1</summary>

```json
{"phase":"1A.2","item":"combat_zones/CZ1","old":{"id":"CZ1","name":"AP KAIWI MOA","bearings_true_deg":[15,50],"radii_km":[14,19.5],"floor_msl_ft_target":150,"ceiling_msl_ft_target":12500,"default_status":"HOT","available_statuses":["HOT","COLD"],"optional":false,"enabled":true,"minimum_clearance_km":1.85,"chart":"magenta hatched boundary; floor-ceiling-status","name_status":"Fictional simulator-prefixed name; not a depiction of real SUA. Susitna is a real MOA name (FAA chart supplement), hence AP prefix on all zones.","hook_owner":"Claude implements explicit HOT-entry targets/drones spawn; COLD never spawns.","gunnery_targets":[{"id":"CZ1-GT1","centre":[8.8,15.9],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170},{"id":"CZ1-GT2","centre":[9.8,15.9],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170},{"id":"CZ1-GT3","centre":[9.3,16.7],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170}],"drone_orbit":{"centre_start":"CZ1","count":3,"radius_m_target":150,"altitude_ft_target":900,"owner":"Claude opponent/AI logic","centre":[9.3,15.9]},"hot_entry_behavior":{"HOT":"On user entry, spawn this zone’s targets and drones; engagement hook belongs to Claude.","COLD":"No targets/drones appear on entry and no combat triggers."},"airport_overlays":[{"airport":"KITE","island":"OI4","floor_msl_ft":5000,"ceiling_msl_ft":12500,"traffic_floor_msl_ft":50,"traffic_ceiling_msl_ft":3000,"pattern_altitude_msl_ft":1050,"vertical_buffer_ft":2000,"pattern_radius_km":1.5,"approach_guard_km":1.85,"scope":"entire both-end 4nm final fans plus1.85km horizontal guard and 1.5km pattern disk; arrivals/departures stay at/below3000ft until beyond guard; combat floor5000ft over reservation; no attacks/targets/drones below raised floor","floor_priority":"raised floor overrides base floor in union of protection polygons"}],"name_audit_source":"https://aeronav.faa.gov/afd/02OCT2025/AK_rear_02OCT2025.pdf","schedule":"Default HOT; pilot may select COLD at any time; no timed automatic schedule.","boundary_mode":"annular sector minus charted horizontal exclusion polygons; raised floor on airport traffic overlays","exclusion_policy":{"guard_km":2.05,"protected_sources":"All known IFR finals AND every runway/beach end fan except own under-zone airport; full2nm airway swaths and proposed perimeter reservation; subtract Minkowski-expanded convex polygons, no combat in excluded volume at any altitude","unknown_geometry":"Reserve against future T201/misses/holds; pending Claude geometry remains unverified"},"combat_features":[{"id":"CZ1-CHANNEL","type":"island cluster and channel","centre":[9.3,15.9],"channel_width_m":300,"channel_length_m":1100,"water_msl_ft":0,"flight_half_width_m":40,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"terrain":"five islands with smooth ridges/sea stacks, two 300m channels; hide-and-seek and strafe route"},{"id":"CZ1-BRIDGE","type":"bridge","centre":[9.3,15.9],"heading_true_deg":90,"span_m":300,"deck_width_m":14,"under_clearance_m":120,"under_base_msl_ft":0,"deck_thickness_m":4,"flight_opening_width_m":240,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids bridge deck/piers; no heightfield bridge deck"},{"id":"CZ1-ARCH","type":"rock arch","centre":[9.8,16.3],"axis_true_deg":0,"opening_width_m":100,"opening_height_m":110,"base_msl_ft":60,"rock_top_msl_ft":460,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids overhang/opening; footprint pedestals only in shared heightfield"}],"low_level_training":{"floor_msl_ft":150,"flight_tube_ceiling_ft":300,"clear_of_traffic_overlays":true,"feature_solids":"All arch/bridge solids charted; no invisible heightfield ceilings; verified as analytic design envelopes"}},"new":{"id":"CZ1","name":"AP KAIWI MOA","bearings_true_deg":[15,50],"radii_km":[14,19.5],"floor_msl_ft_target":150,"ceiling_msl_ft_target":12500,"default_status":"HOT","available_statuses":["HOT","COLD"],"optional":false,"enabled":true,"minimum_clearance_km":1.85,"chart":"magenta hatched boundary; floor-ceiling-status","name_status":"Fictional simulator-prefixed name; not a depiction of real SUA. Susitna is a real MOA name (FAA chart supplement), hence AP prefix on all zones.","hook_owner":"Claude implements explicit HOT-entry targets/drones spawn; COLD never spawns.","gunnery_targets":[{"id":"CZ1-GT1","centre":[8.8,16.0],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170},{"id":"CZ1-GT2","centre":[9.8,15.6],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170},{"id":"CZ1-GT3","centre":[9.3,16.4],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170}],"drone_orbit":{"centre_start":"CZ1","count":3,"radius_m_target":150,"altitude_ft_target":900,"owner":"Claude opponent/AI logic","centre":[9.3,15.6]},"hot_entry_behavior":{"HOT":"On user entry, spawn this zone’s targets and drones; engagement hook belongs to Claude.","COLD":"No targets/drones appear on entry and no combat triggers."},"airport_overlays":[{"airport":"KITE","island":"OI4","floor_msl_ft":5000,"ceiling_msl_ft":12500,"traffic_floor_msl_ft":50,"traffic_ceiling_msl_ft":3000,"pattern_altitude_msl_ft":1050,"vertical_buffer_ft":2000,"pattern_radius_km":1.2,"approach_guard_km":0.3,"scope":"Full both-end4nm final fans +300m lateral guard and1.2km pattern disk; traffic at/below3000ft, combat at/above5000ft. No low-floor targets/engagement in reservation.2000ft vertical margin.","floor_priority":"raised floor overrides base floor in union of protection polygons"}],"name_audit_source":"https://aeronav.faa.gov/afd/02OCT2025/AK_rear_02OCT2025.pdf","schedule":"Default HOT; pilot may select COLD at any time; no timed automatic schedule.","boundary_mode":"annular sector minus charted horizontal exclusion polygons; raised floor on airport traffic overlays","exclusion_policy":{"guard_km":2.05,"protected_sources":"All known IFR finals AND every runway/beach end fan except own under-zone airport; full2nm airway swaths and proposed perimeter reservation; subtract Minkowski-expanded convex polygons, no combat in excluded volume at any altitude","unknown_geometry":"Reserve against future T201/misses/holds; pending Claude geometry remains unverified"},"combat_features":[{"id":"CZ1-CHANNEL","type":"island cluster and channel","centre":[9.3,15.6],"channel_width_m":300,"channel_length_m":1100,"water_msl_ft":0,"flight_half_width_m":40,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"terrain":"five islands with smooth ridges/sea stacks, two 300m channels; hide-and-seek and strafe route"},{"id":"CZ1-BRIDGE","type":"bridge","centre":[9.3,15.6],"heading_true_deg":90,"span_m":300,"deck_width_m":14,"under_clearance_m":120,"under_base_msl_ft":0,"deck_thickness_m":4,"flight_opening_width_m":240,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids bridge deck/piers; no heightfield bridge deck"},{"id":"CZ1-ARCH","type":"rock arch","centre":[9.8,16.0],"axis_true_deg":0,"opening_width_m":100,"opening_height_m":110,"base_msl_ft":60,"rock_top_msl_ft":460,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids overhang/opening; footprint pedestals only in shared heightfield"}],"low_level_training":{"floor_msl_ft":150,"flight_tube_ceiling_ft":300,"clear_of_traffic_overlays":true,"feature_solids":"All arch/bridge solids charted; no invisible heightfield ceilings; verified as analytic design envelopes"}},"reason":"Move airport islands clear of external beach/airpark protection exclusions; retain above-pattern5000ft MOA floor over3000ft traffic with300m own-final guard. Move low-level island arenas and target bounds outside all raised-floor traffic masks and within ring."}
```

</details>

<details>
<summary>277. combat_zones/CZ3</summary>

```json
{"phase":"1A.2","item":"combat_zones/CZ3","old":{"id":"CZ3","name":"AP SUSITNA MOA","bearings_true_deg":[315,350],"radii_km":[14.5,19.5],"floor_msl_ft_target":150,"ceiling_msl_ft_target":12500,"default_status":"HOT","available_statuses":["HOT","COLD"],"optional":false,"enabled":true,"minimum_clearance_km":1.85,"chart":"magenta hatched boundary; floor-ceiling-status","name_status":"Fictional simulator-prefixed name; not a depiction of real SUA. Susitna is a real MOA name (FAA chart supplement), hence AP prefix on all zones.","hook_owner":"Claude implements explicit HOT-entry targets/drones spawn; COLD never spawns.","gunnery_targets":[{"id":"CZ3-GT1","centre":[-7.5,16.3],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170},{"id":"CZ3-GT2","centre":[-6.5,16.3],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170},{"id":"CZ3-GT3","centre":[-7.0,17.1],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170}],"drone_orbit":{"centre_start":"CZ3","count":3,"radius_m_target":150,"altitude_ft_target":900,"owner":"Claude opponent/AI logic","centre":[-7.0,16.3]},"hot_entry_behavior":{"HOT":"On user entry, spawn this zone’s targets and drones; engagement hook belongs to Claude.","COLD":"No targets/drones appear on entry and no combat triggers."},"airport_overlays":[{"airport":"RIME","island":"OI5","floor_msl_ft":5000,"ceiling_msl_ft":12500,"traffic_floor_msl_ft":70,"traffic_ceiling_msl_ft":3000,"pattern_altitude_msl_ft":1070,"vertical_buffer_ft":2000,"pattern_radius_km":1.5,"approach_guard_km":1.85,"scope":"entire both-end 4nm final fans plus1.85km horizontal guard and 1.5km pattern disk; arrivals/departures stay at/below3000ft until beyond guard; combat floor5000ft over reservation; no attacks/targets/drones below raised floor","floor_priority":"raised floor overrides base floor in union of protection polygons"}],"name_audit_source":"https://aeronav.faa.gov/afd/02OCT2025/AK_rear_02OCT2025.pdf","schedule":"Default HOT; pilot may select COLD at any time; no timed automatic schedule.","boundary_mode":"annular sector minus charted horizontal exclusion polygons; raised floor on airport traffic overlays","exclusion_policy":{"guard_km":2.05,"protected_sources":"All known IFR finals AND every runway/beach end fan except own under-zone airport; full2nm airway swaths and proposed perimeter reservation; subtract Minkowski-expanded convex polygons, no combat in excluded volume at any altitude","unknown_geometry":"Reserve against future T201/misses/holds; pending Claude geometry remains unverified"},"combat_features":[{"id":"CZ3-CHANNEL","type":"island cluster and channel","centre":[-7.0,16.3],"channel_width_m":300,"channel_length_m":1100,"water_msl_ft":0,"flight_half_width_m":40,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"terrain":"five islands with smooth ridges/sea stacks, two 300m channels; hide-and-seek and strafe route"},{"id":"CZ3-BRIDGE","type":"bridge","centre":[-7.0,16.3],"heading_true_deg":90,"span_m":300,"deck_width_m":14,"under_clearance_m":120,"under_base_msl_ft":0,"deck_thickness_m":4,"flight_opening_width_m":240,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids bridge deck/piers; no heightfield bridge deck"},{"id":"CZ3-ARCH","type":"rock arch","centre":[-6.5,16.7],"axis_true_deg":0,"opening_width_m":100,"opening_height_m":110,"base_msl_ft":60,"rock_top_msl_ft":460,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids overhang/opening; footprint pedestals only in shared heightfield"}],"low_level_training":{"floor_msl_ft":150,"flight_tube_ceiling_ft":300,"clear_of_traffic_overlays":true,"feature_solids":"All arch/bridge solids charted; no invisible heightfield ceilings; verified as analytic design envelopes"}},"new":{"id":"CZ3","name":"AP SUSITNA MOA","bearings_true_deg":[315,350],"radii_km":[14.5,19.5],"floor_msl_ft_target":150,"ceiling_msl_ft_target":12500,"default_status":"HOT","available_statuses":["HOT","COLD"],"optional":false,"enabled":true,"minimum_clearance_km":1.85,"chart":"magenta hatched boundary; floor-ceiling-status","name_status":"Fictional simulator-prefixed name; not a depiction of real SUA. Susitna is a real MOA name (FAA chart supplement), hence AP prefix on all zones.","hook_owner":"Claude implements explicit HOT-entry targets/drones spawn; COLD never spawns.","gunnery_targets":[{"id":"CZ3-GT1","centre":[-8.0,16.9],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170},{"id":"CZ3-GT2","centre":[-7.0,16.9],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170},{"id":"CZ3-GT3","centre":[-7.5,17.7],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170}],"drone_orbit":{"centre_start":"CZ3","count":3,"radius_m_target":150,"altitude_ft_target":900,"owner":"Claude opponent/AI logic","centre":[-7.5,16.9]},"hot_entry_behavior":{"HOT":"On user entry, spawn this zone’s targets and drones; engagement hook belongs to Claude.","COLD":"No targets/drones appear on entry and no combat triggers."},"airport_overlays":[{"airport":"RIME","island":"OI5","floor_msl_ft":5000,"ceiling_msl_ft":12500,"traffic_floor_msl_ft":70,"traffic_ceiling_msl_ft":3000,"pattern_altitude_msl_ft":1070,"vertical_buffer_ft":2000,"pattern_radius_km":1.2,"approach_guard_km":0.3,"scope":"Full both-end4nm final fans +300m lateral guard and1.2km pattern disk; traffic at/below3000ft, combat at/above5000ft. No low-floor targets/engagement in reservation.2000ft vertical margin.","floor_priority":"raised floor overrides base floor in union of protection polygons"}],"name_audit_source":"https://aeronav.faa.gov/afd/02OCT2025/AK_rear_02OCT2025.pdf","schedule":"Default HOT; pilot may select COLD at any time; no timed automatic schedule.","boundary_mode":"annular sector minus charted horizontal exclusion polygons; raised floor on airport traffic overlays","exclusion_policy":{"guard_km":2.05,"protected_sources":"All known IFR finals AND every runway/beach end fan except own under-zone airport; full2nm airway swaths and proposed perimeter reservation; subtract Minkowski-expanded convex polygons, no combat in excluded volume at any altitude","unknown_geometry":"Reserve against future T201/misses/holds; pending Claude geometry remains unverified"},"combat_features":[{"id":"CZ3-CHANNEL","type":"island cluster and channel","centre":[-7.5,16.9],"channel_width_m":300,"channel_length_m":1100,"water_msl_ft":0,"flight_half_width_m":40,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"terrain":"five islands with smooth ridges/sea stacks, two 300m channels; hide-and-seek and strafe route"},{"id":"CZ3-BRIDGE","type":"bridge","centre":[-7.5,16.9],"heading_true_deg":90,"span_m":300,"deck_width_m":14,"under_clearance_m":120,"under_base_msl_ft":0,"deck_thickness_m":4,"flight_opening_width_m":240,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids bridge deck/piers; no heightfield bridge deck"},{"id":"CZ3-ARCH","type":"rock arch","centre":[-7.0,17.3],"axis_true_deg":0,"opening_width_m":100,"opening_height_m":110,"base_msl_ft":60,"rock_top_msl_ft":460,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids overhang/opening; footprint pedestals only in shared heightfield"}],"low_level_training":{"floor_msl_ft":150,"flight_tube_ceiling_ft":300,"clear_of_traffic_overlays":true,"feature_solids":"All arch/bridge solids charted; no invisible heightfield ceilings; verified as analytic design envelopes"}},"reason":"Move airport islands clear of external beach/airpark protection exclusions; retain above-pattern5000ft MOA floor over3000ft traffic with300m own-final guard. Move low-level island arenas and target bounds outside all raised-floor traffic masks and within ring."}
```

</details>

<details>
<summary>278. starts/CZ1</summary>

```json
{"phase":"1A.2","item":"starts/CZ1","old":{"id":"CZ1","mode":"InCombatZone","enabled":true,"centre":[9.3,15.9],"altitude_ft_target":900,"heading_true_deg":122.5},"new":{"id":"CZ1","mode":"InCombatZone","enabled":true,"centre":[9.3,15.6],"altitude_ft_target":900,"heading_true_deg":122.5},"reason":"Move airport islands clear of external beach/airpark protection exclusions; retain above-pattern5000ft MOA floor over3000ft traffic with300m own-final guard. Move low-level island arenas and target bounds outside all raised-floor traffic masks and within ring."}
```

</details>

<details>
<summary>279. starts/CZ3</summary>

```json
{"phase":"1A.2","item":"starts/CZ3","old":{"id":"CZ3","mode":"InCombatZone","enabled":true,"centre":[-7.0,16.3],"altitude_ft_target":900,"heading_true_deg":62.5},"new":{"id":"CZ3","mode":"InCombatZone","enabled":true,"centre":[-7.5,16.9],"altitude_ft_target":900,"heading_true_deg":62.5},"reason":"Move airport islands clear of external beach/airpark protection exclusions; retain above-pattern5000ft MOA floor over3000ft traffic with300m own-final guard. Move low-level island arenas and target bounds outside all raised-floor traffic masks and within ring."}
```

</details>

<details>
<summary>280. AP1 datum</summary>

```json
{"phase":"1A.2","item":"AP1 datum","old":5024,"new":4925,"reason":"Fictional plateau datum99ft lower thanMcCall restores35% mean grade toPATK afterEW strip rotation."}
```

</details>

<details>
<summary>281. AP11 datum</summary>

```json
{"phase":"1A.2","item":"AP11 datum","old":5024,"new":4000,"reason":"Retain lower east-shore terrace; proposed5024ft created35% conflicts withPHDH/AP4. Resolve approach by heading instead."}
```

</details>

<details>
<summary>282. AP11 heading</summary>

```json
{"phase":"1A.2","item":"AP11 heading","old":139,"new":70,"reason":"Southwest/northeast axis avoids higherPayette runway and McCall box; preserve lower terrace datum."}
```

</details>

<details>
<summary>283. AP11 magnetic</summary>

```json
{"phase":"1A.2","item":"AP11 magnetic","old":"13/31","new":"6/24","reason":"063magnetic."}
```

</details>

<details>
<summary>284. Sulphur centre</summary>

```json
{"phase":"1A.2","item":"Sulphur centre","old":[4.7,0.1],"new":[4.3,0],"reason":"Restore35% grade separation fromlowAP5, keep>=.8km fromSoldierBar/Thomas and distinct pad rectangles."}
```

</details>

<details>
<summary>285. BigCreek centre</summary>

```json
{"phase":"1A.2","item":"BigCreek centre","old":[3,-0.8],"new":[2.7,-0.8],"reason":"300m west keeps full pad out ofKPGA RNAV16 20:1 fan."}
```

</details>

<details>
<summary>286. DeweyMoore heading</summary>

```json
{"phase":"1A.2","item":"DeweyMoore heading","old":282,"new":270,"reason":"East-west final no longer crosses McCall box, preserve one-waywest operation."}
```

</details>

<details>
<summary>287. DeweyMoore one-way</summary>

```json
{"phase":"1A.2","item":"DeweyMoore one-way","old":282,"new":270,"reason":"Follow rotated westboundstrip."}
```

</details>

<details>
<summary>288. AP11 centre</summary>

```json
{"phase":"1A.2","item":"AP11 centre","old":[0.1,4.6],"new":[0.2,5.2],"reason":"Northern east-shore terrace gives full final clearance around AB1/AB3 and Payette, with enough field-to-field distance for35% cap toPHDH/AP4/KMYL. Fictional datum target, not locked realfield."}
```

</details>

<details>
<summary>289. AP11 heading_true_deg</summary>

```json
{"phase":"1A.2","item":"AP11 heading_true_deg","old":70,"new":85,"reason":"Northern east-shore terrace gives full final clearance around AB1/AB3 and Payette, with enough field-to-field distance for35% cap toPHDH/AP4/KMYL. Fictional datum target, not locked realfield."}
```

</details>

<details>
<summary>290. AP11 numbers_magnetic</summary>

```json
{"phase":"1A.2","item":"AP11 numbers_magnetic","old":"6/24","new":"8/26","reason":"Northern east-shore terrace gives full final clearance around AB1/AB3 and Payette, with enough field-to-field distance for35% cap toPHDH/AP4/KMYL. Fictional datum target, not locked realfield."}
```

</details>

<details>
<summary>291. AP11 elevation_ft</summary>

```json
{"phase":"1A.2","item":"AP11 elevation_ft","old":4000,"new":3500,"reason":"Northern east-shore terrace gives full final clearance around AB1/AB3 and Payette, with enough field-to-field distance for35% cap toPHDH/AP4/KMYL. Fictional datum target, not locked realfield."}
```

</details>

<details>
<summary>292. AP11 final terrace datum</summary>

```json
{"phase":"1A.2","item":"AP11 final terrace datum","old":3500,"new":3750,"reason":"3750ft lies between computed flat-field grade limits;3500ft fell too far below adjacentPayette. All true field datums remain unchanged."}
```

</details>

<details>
<summary>293. AP4/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"AP4/heading_true_deg","old":97,"new":103,"reason":"Final full-footprint search preserves35% field grade, clears other full pad planes and mountain cores, and avoids box flight volumes. AP11 returns to4000ft terrace with170true runway,0.1km west ofprior centre."}
```

</details>

<details>
<summary>294. AP4/numbers_magnetic</summary>

```json
{"phase":"1A.2","item":"AP4/numbers_magnetic","old":"9/27","new":"10/28","reason":"Final full-footprint search preserves35% field grade, clears other full pad planes and mountain cores, and avoids box flight volumes. AP11 returns to4000ft terrace with170true runway,0.1km west ofprior centre."}
```

</details>

<details>
<summary>295. AP6/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"AP6/heading_true_deg","old":109,"new":121,"reason":"Final full-footprint search preserves35% field grade, clears other full pad planes and mountain cores, and avoids box flight volumes. AP11 returns to4000ft terrace with170true runway,0.1km west ofprior centre."}
```

</details>

<details>
<summary>296. AP6/numbers_magnetic</summary>

```json
{"phase":"1A.2","item":"AP6/numbers_magnetic","old":"10/28","new":"11/29","reason":"Final full-footprint search preserves35% field grade, clears other full pad planes and mountain cores, and avoids box flight volumes. AP11 returns to4000ft terrace with170true runway,0.1km west ofprior centre."}
```

</details>

<details>
<summary>297. AP7/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"AP7/heading_true_deg","old":97,"new":94,"reason":"Final full-footprint search preserves35% field grade, clears other full pad planes and mountain cores, and avoids box flight volumes. AP11 returns to4000ft terrace with170true runway,0.1km west ofprior centre."}
```

</details>

<details>
<summary>298. AP11/centre</summary>

```json
{"phase":"1A.2","item":"AP11/centre","old":[0.2,5.2],"new":[0,4.6],"reason":"Final full-footprint search preserves35% field grade, clears other full pad planes and mountain cores, and avoids box flight volumes. AP11 returns to4000ft terrace with170true runway,0.1km west ofprior centre."}
```

</details>

<details>
<summary>299. AP11/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"AP11/heading_true_deg","old":85,"new":170,"reason":"Final full-footprint search preserves35% field grade, clears other full pad planes and mountain cores, and avoids box flight volumes. AP11 returns to4000ft terrace with170true runway,0.1km west ofprior centre."}
```

</details>

<details>
<summary>300. AP11/numbers_magnetic</summary>

```json
{"phase":"1A.2","item":"AP11/numbers_magnetic","old":"8/26","new":"16/34","reason":"Final full-footprint search preserves35% field grade, clears other full pad planes and mountain cores, and avoids box flight volumes. AP11 returns to4000ft terrace with170true runway,0.1km west ofprior centre."}
```

</details>

<details>
<summary>301. AP11/elevation_ft</summary>

```json
{"phase":"1A.2","item":"AP11/elevation_ft","old":3750,"new":4000,"reason":"Final full-footprint search preserves35% field grade, clears other full pad planes and mountain cores, and avoids box flight volumes. AP11 returns to4000ft terrace with170true runway,0.1km west ofprior centre."}
```

</details>

<details>
<summary>302. T3 coastal site</summary>

```json
{"phase":"1A.2","item":"T3 coastal site","old":[5.7,-7.4],"new":[5.2,-6.3],"reason":"Keep original tower height/count on land; low coastal terrain clears airway MEA margins, full runway-end fans and guy footprint."}
```

</details>

<details>
<summary>303. T6 coastal site</summary>

```json
{"phase":"1A.2","item":"T6 coastal site","old":[4.6,-3.95],"new":[6.5,-4.8],"reason":"Keep original tower height/count on land; low coastal terrain clears airway MEA margins, full runway-end fans and guy footprint."}
```

</details>

<details>
<summary>304. W2 fullspan relocation</summary>

```json
{"phase":"1A.2","item":"W2 fullspan relocation","old":[[2.4,-5.55],[3.3,-5.25]],"new":[[5.8,-5.9],[6.1,-5.6]],"reason":"Move full catenary conservative envelope clear of approach fans and lower coastal/crop terrain within MEA budget; preserve locked five-wire inventory and stated lowestAGL height."}
```

</details>

<details>
<summary>305. W4 fullspan relocation</summary>

```json
{"phase":"1A.2","item":"W4 fullspan relocation","old":[[-3.9,-3.45],[-2.5,-3.45]],"new":[[-1.5,-4.5],[-0.3,-4.5]],"reason":"Move full catenary conservative envelope clear of approach fans and lower coastal/crop terrain within MEA budget; preserve locked five-wire inventory and stated lowestAGL height."}
```

</details>

<details>
<summary>306. W5 fullspan relocation</summary>

```json
{"phase":"1A.2","item":"W5 fullspan relocation","old":[[-2.2,-8.5],[-0.2,-8.9]],"new":[[1.3,-9.4],[2,-9.3]],"reason":"Move full catenary conservative envelope clear of approach fans and lower coastal/crop terrain within MEA budget; preserve locked five-wire inventory and stated lowestAGL height."}
```

</details>

<details>
<summary>307. L1/safe_path</summary>

```json
{"phase":"1A.2","item":"L1/safe_path","old":[[-0.58,2.78],[0.6,1.7],[1.82,-0.86]],"new":[[-0.88,2.78],[0.6,1.7],[1.82,-0.86]],"reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>308. L1/box_length_m</summary>

```json
{"phase":"1A.2","item":"L1/box_length_m","old":600,"new":602.08,"reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>309. L1/headwall_ft</summary>

```json
{"phase":"1A.2","item":"L1/headwall_ft","old":7500,"new":8000,"reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>310. L1/box_floor_rise_m</summary>

```json
{"phase":"1A.2","item":"L1/box_floor_rise_m","old":250,"new":400,"reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>311. L1/climb_reference</summary>

```json
{"phase":"1A.2","item":"L1/climb_reference","old":"loaded trainer 300 ft/min at 80 kt; target only; Claude verifies aircraft/DA","new":"C172-like1043kg/134000W loaded reference; absolute sustained engine-power ROC upper boundP/(mg) at100%efficiency, zero drag, no DA power loss. Full actual-grid path checked at80kt ground speed; flight/turnback/zoom behavior remains Phase1B.","reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>312. L1/floor_half_width_m</summary>

```json
{"phase":"1A.2","item":"L1/floor_half_width_m","old":null,"new":100,"reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>313. L1/outer_blend_m</summary>

```json
{"phase":"1A.2","item":"L1/outer_blend_m","old":null,"new":100,"reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>314. L1/profile_protection</summary>

```json
{"phase":"1A.2","item":"L1/profile_protection","old":null,"new":"Reassert lesson box floor ramp after broad terrain cuts; approach/airway conflicts remain checked, not silently cut flat.","reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>315. L1/headwall_geometry</summary>

```json
{"phase":"1A.2","item":"L1/headwall_geometry","old":null,"new":{"centre":[1.1396891182420392,2.1797236606595907],"top_ft":8000,"radius_m":180,"core_radius_m":50,"blend_m":130},"reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>316. L2/safe_path</summary>

```json
{"phase":"1A.2","item":"L2/safe_path","old":[[-3.17,-1.73],[-4.6,-0.9],[-5.8,-0.3],[-7.96,-1.8]],"new":[[-3.02,-1.73],[-4.6,-0.9],[-5.8,-0.3],[-10.18,-1.8]],"reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>317. L2/box_length_m</summary>

```json
{"phase":"1A.2","item":"L2/box_length_m","old":500,"new":494.975,"reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>318. L2/headwall_ft</summary>

```json
{"phase":"1A.2","item":"L2/headwall_ft","old":6890,"new":7300,"reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>319. L2/box_floor_rise_m</summary>

```json
{"phase":"1A.2","item":"L2/box_floor_rise_m","old":300,"new":400,"reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>320. L2/climb_reference</summary>

```json
{"phase":"1A.2","item":"L2/climb_reference","old":"loaded trainer 300 ft/min at 80 kt; target only; Claude verifies aircraft/DA","new":"C172-like1043kg/134000W loaded reference; absolute sustained engine-power ROC upper boundP/(mg) at100%efficiency, zero drag, no DA power loss. Full actual-grid path checked at80kt ground speed; flight/turnback/zoom behavior remains Phase1B.","reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>321. L2/floor_half_width_m</summary>

```json
{"phase":"1A.2","item":"L2/floor_half_width_m","old":null,"new":100,"reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>322. L2/outer_blend_m</summary>

```json
{"phase":"1A.2","item":"L2/outer_blend_m","old":null,"new":100,"reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>323. L2/profile_protection</summary>

```json
{"phase":"1A.2","item":"L2/profile_protection","old":null,"new":"Reassert lesson box floor ramp after broad terrain cuts; approach/airway conflicts remain checked, not silently cut flat.","reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>324. L2/headwall_geometry</summary>

```json
{"phase":"1A.2","item":"L2/headwall_geometry","old":null,"new":{"centre":[-5.034852813742386,-1.3348528137423856],"top_ft":7300,"radius_m":180,"core_radius_m":50,"blend_m":130},"reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>325. L3/fork</summary>

```json
{"phase":"1A.2","item":"L3/fork","old":[1.92,-6.34],"new":[2.9,-6.3],"reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>326. L3/safe_path</summary>

```json
{"phase":"1A.2","item":"L3/safe_path","old":[[2.88,-5.38],[1.92,-6.34],[0,-6.72],[-1.54,-9.6]],"new":[[2.88,-5.38],[2.9,-6.3],[0,-6.72],[-1.54,-9.6]],"reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>327. L3/box_path</summary>

```json
{"phase":"1A.2","item":"L3/box_path","old":[[1.92,-6.34],[2.16,-6.52]],"new":[[2.9,-6.3],[3.14,-6.48]],"reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>328. L3/saddle_ft</summary>

```json
{"phase":"1A.2","item":"L3/saddle_ft","old":3200,"new":1500,"reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>329. L3/headwall_ft</summary>

```json
{"phase":"1A.2","item":"L3/headwall_ft","old":5000,"new":2450,"reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>330. L3/box_floor_rise_m</summary>

```json
{"phase":"1A.2","item":"L3/box_floor_rise_m","old":150,"new":250,"reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>331. L3/climb_reference</summary>

```json
{"phase":"1A.2","item":"L3/climb_reference","old":"loaded trainer 300 ft/min at 80 kt; target only; Claude verifies aircraft/DA","new":"C172-like1043kg/134000W loaded reference; absolute sustained engine-power ROC upper boundP/(mg) at100%efficiency, zero drag, no DA power loss. Full actual-grid path checked at80kt ground speed; flight/turnback/zoom behavior remains Phase1B.","reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>332. L3/floor_half_width_m</summary>

```json
{"phase":"1A.2","item":"L3/floor_half_width_m","old":null,"new":100,"reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>333. L3/outer_blend_m</summary>

```json
{"phase":"1A.2","item":"L3/outer_blend_m","old":null,"new":100,"reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>334. L3/profile_protection</summary>

```json
{"phase":"1A.2","item":"L3/profile_protection","old":null,"new":"Reassert lesson box floor ramp after broad terrain cuts; approach/airway conflicts remain checked, not silently cut flat.","reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>335. L3/headwall_geometry</summary>

```json
{"phase":"1A.2","item":"L3/headwall_geometry","old":null,"new":{"centre":[3.236,-6.5520000000000005],"top_ft":2450,"radius_m":180,"core_radius_m":50,"blend_m":130},"reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>336. L4/headwall_ft</summary>

```json
{"phase":"1A.2","item":"L4/headwall_ft","old":8530,"new":2900,"reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>337. L4/climb_reference</summary>

```json
{"phase":"1A.2","item":"L4/climb_reference","old":"loaded trainer 300 ft/min at 80 kt; target only; Claude verifies aircraft/DA","new":"C172-like1043kg/134000W loaded reference; absolute sustained engine-power ROC upper boundP/(mg) at100%efficiency, zero drag, no DA power loss. Full actual-grid path checked at80kt ground speed; flight/turnback/zoom behavior remains Phase1B.","reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>338. L4/floor_half_width_m</summary>

```json
{"phase":"1A.2","item":"L4/floor_half_width_m","old":null,"new":100,"reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>339. L4/outer_blend_m</summary>

```json
{"phase":"1A.2","item":"L4/outer_blend_m","old":null,"new":100,"reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>340. L4/profile_protection</summary>

```json
{"phase":"1A.2","item":"L4/profile_protection","old":null,"new":"Reassert lesson box floor ramp after broad terrain cuts; approach/airway conflicts remain checked, not silently cut flat.","reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>341. L4/headwall_geometry</summary>

```json
{"phase":"1A.2","item":"L4/headwall_geometry","old":null,"new":{"centre":[-4.5,5.22],"top_ft":2900,"radius_m":180,"core_radius_m":50,"blend_m":130},"reason":"Restore actual rising box floors after clearance cuts; moveGlen box east ofKSNA northfan and lower its datum/headwall to fitV6 budget. ReheadlowRuth terminalwall belowA2 while distantDenali stays8530ft. Locked glacier elevations unchanged. Conservative loaded-trainer climb bound replaces assumed300fpm."}
```

</details>

<details>
<summary>342. C3 fork</summary>

```json
{"phase":"1A.2","item":"C3 fork","old":[[2.88,-5.38],[1.92,-6.34],[0,-6.72],[-1.15,-7.87],[-1.54,-9.6]],"new":[[2.88,-5.38],[2.9,-6.3],[0,-6.72],[-1.15,-7.87],[-1.54,-9.6]],"reason":"Move lesson fork and main canyon together east ofKSNA northern runway fan, retain lockedPowell3700ft and coast outlet."}
```

</details>

<details>
<summary>343. AP5/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"AP5/heading_true_deg","old":21,"new":335,"reason":"120m extra fan fringe in heading search prevents100m baked triangles/pad blends protruding beyond exact analytic rectangles; retain35%grade/field dimensions and avoid full box/peak footprints."}
```

</details>

<details>
<summary>344. AP5/numbers_magnetic</summary>

```json
{"phase":"1A.2","item":"AP5/numbers_magnetic","old":"1/19","new":"33/15","reason":"120m extra fan fringe in heading search prevents100m baked triangles/pad blends protruding beyond exact analytic rectangles; retain35%grade/field dimensions and avoid full box/peak footprints."}
```

</details>

<details>
<summary>345. AP9/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"AP9/heading_true_deg","old":71,"new":103,"reason":"120m extra fan fringe in heading search prevents100m baked triangles/pad blends protruding beyond exact analytic rectangles; retain35%grade/field dimensions and avoid full box/peak footprints."}
```

</details>

<details>
<summary>346. AP9/numbers_magnetic</summary>

```json
{"phase":"1A.2","item":"AP9/numbers_magnetic","old":"6/24","new":"10/28","reason":"120m extra fan fringe in heading search prevents100m baked triangles/pad blends protruding beyond exact analytic rectangles; retain35%grade/field dimensions and avoid full box/peak footprints."}
```

</details>

<details>
<summary>347. AP10/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"AP10/heading_true_deg","old":90,"new":87,"reason":"120m extra fan fringe in heading search prevents100m baked triangles/pad blends protruding beyond exact analytic rectangles; retain35%grade/field dimensions and avoid full box/peak footprints."}
```

</details>

<details>
<summary>348. AP12/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"AP12/heading_true_deg","old":169,"new":171,"reason":"120m extra fan fringe in heading search prevents100m baked triangles/pad blends protruding beyond exact analytic rectangles; retain35%grade/field dimensions and avoid full box/peak footprints."}
```

</details>

<details>
<summary>349. AP14/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"AP14/heading_true_deg","old":27,"new":26,"reason":"120m extra fan fringe in heading search prevents100m baked triangles/pad blends protruding beyond exact analytic rectangles; retain35%grade/field dimensions and avoid full box/peak footprints."}
```

</details>

<details>
<summary>350. Thomas Creek/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"Thomas Creek/heading_true_deg","old":90,"new":85,"reason":"120m extra fan fringe in heading search prevents100m baked triangles/pad blends protruding beyond exact analytic rectangles; retain35%grade/field dimensions and avoid full box/peak footprints."}
```

</details>

<details>
<summary>351. Indian Creek/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"Indian Creek/heading_true_deg","old":52,"new":81,"reason":"120m extra fan fringe in heading search prevents100m baked triangles/pad blends protruding beyond exact analytic rectangles; retain35%grade/field dimensions and avoid full box/peak footprints."}
```

</details>

<details>
<summary>352. Mackay Bar/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"Mackay Bar/heading_true_deg","old":355,"new":351,"reason":"120m extra fan fringe in heading search prevents100m baked triangles/pad blends protruding beyond exact analytic rectangles; retain35%grade/field dimensions and avoid full box/peak footprints."}
```

</details>

<details>
<summary>353. Denali-like final core</summary>

```json
{"phase":"1A.2","item":"Denali-like final core","old":{"id":"Denali-like","centre":[-2.1,6.9],"top_ft":8530,"radius_km":1.5},"new":{"id":"Denali-like","centre":[0.5,7.1],"top_ft":8530,"radius_km":1.5},"reason":"Northern highpeak outside lowA2 capsule and both unchangedMcCall RNAV/LDA finals. Preserve8530ft elevation; nearby airpark headings rotated around it."}
```

</details>

<details>
<summary>354. AP4 elevation_ft</summary>

```json
{"phase":"1A.2","item":"AP4 elevation_ft","old":1250,"new":900,"reason":"Lower fictional ranch datum to900ft; southeast/northwest125true axis fits between Denali core and waterAB1, while retaining35% grade tolowPHDH and all other flat fields."}
```

</details>

<details>
<summary>355. AP4 heading_true_deg</summary>

```json
{"phase":"1A.2","item":"AP4 heading_true_deg","old":103,"new":125,"reason":"Lower fictional ranch datum to900ft; southeast/northwest125true axis fits between Denali core and waterAB1, while retaining35% grade tolowPHDH and all other flat fields."}
```

</details>

<details>
<summary>356. AP4 numbers_magnetic</summary>

```json
{"phase":"1A.2","item":"AP4 numbers_magnetic","old":"10/28","new":"12/30","reason":"Lower fictional ranch datum to900ft; southeast/northwest125true axis fits between Denali core and waterAB1, while retaining35% grade tolowPHDH and all other flat fields."}
```

</details>

<details>
<summary>357. Chamberlain/centre</summary>

```json
{"phase":"1A.2","item":"Chamberlain/centre","old":[2.4,-2.1],"new":[0.7,-1.8],"reason":"Move4800ft backcountry pads outside V2 low-MEA rounded capsule; separate sloped strips and rehead around lesson ramp volumes. L2 moves east ofA2 and lowers saddle/headwall to retain400m rising floor belowV5 7000ft terrain budget. No locked runway length/real datum changes."}
```

</details>

<details>
<summary>358. Big Creek/centre</summary>

```json
{"phase":"1A.2","item":"Big Creek/centre","old":[2.7,-0.8],"new":[2.7,-0.4],"reason":"Move4800ft backcountry pads outside V2 low-MEA rounded capsule; separate sloped strips and rehead around lesson ramp volumes. L2 moves east ofA2 and lowers saddle/headwall to retain400m rising floor belowV5 7000ft terrain budget. No locked runway length/real datum changes."}
```

</details>

<details>
<summary>359. Soldier Bar/centre</summary>

```json
{"phase":"1A.2","item":"Soldier Bar/centre","old":[4.0,0.8],"new":[4.4,0.8],"reason":"Move4800ft backcountry pads outside V2 low-MEA rounded capsule; separate sloped strips and rehead around lesson ramp volumes. L2 moves east ofA2 and lowers saddle/headwall to retain400m rising floor belowV5 7000ft terrain budget. No locked runway length/real datum changes."}
```

</details>

<details>
<summary>360. Dewey Moore/centre</summary>

```json
{"phase":"1A.2","item":"Dewey Moore/centre","old":[4.9,0.9],"new":[5.3,0.8],"reason":"Move4800ft backcountry pads outside V2 low-MEA rounded capsule; separate sloped strips and rehead around lesson ramp volumes. L2 moves east ofA2 and lowers saddle/headwall to retain400m rising floor belowV5 7000ft terrain budget. No locked runway length/real datum changes."}
```

</details>

<details>
<summary>361. AP10/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"AP10/heading_true_deg","old":87,"new":25,"reason":"Move4800ft backcountry pads outside V2 low-MEA rounded capsule; separate sloped strips and rehead around lesson ramp volumes. L2 moves east ofA2 and lowers saddle/headwall to retain400m rising floor belowV5 7000ft terrain budget. No locked runway length/real datum changes."}
```

</details>

<details>
<summary>362. AP10/numbers_magnetic</summary>

```json
{"phase":"1A.2","item":"AP10/numbers_magnetic","old":"8/26","new":"2/20","reason":"Move4800ft backcountry pads outside V2 low-MEA rounded capsule; separate sloped strips and rehead around lesson ramp volumes. L2 moves east ofA2 and lowers saddle/headwall to retain400m rising floor belowV5 7000ft terrain budget. No locked runway length/real datum changes."}
```

</details>

<details>
<summary>363. AP14/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"AP14/heading_true_deg","old":26,"new":75,"reason":"Move4800ft backcountry pads outside V2 low-MEA rounded capsule; separate sloped strips and rehead around lesson ramp volumes. L2 moves east ofA2 and lowers saddle/headwall to retain400m rising floor belowV5 7000ft terrain budget. No locked runway length/real datum changes."}
```

</details>

<details>
<summary>364. AP14/numbers_magnetic</summary>

```json
{"phase":"1A.2","item":"AP14/numbers_magnetic","old":"2/20","new":"7/25","reason":"Move4800ft backcountry pads outside V2 low-MEA rounded capsule; separate sloped strips and rehead around lesson ramp volumes. L2 moves east ofA2 and lowers saddle/headwall to retain400m rising floor belowV5 7000ft terrain budget. No locked runway length/real datum changes."}
```

</details>

<details>
<summary>365. L2/fork</summary>

```json
{"phase":"1A.2","item":"L2/fork","old":[-4.6,-0.9],"new":[-4.3,-0.9],"reason":"Move4800ft backcountry pads outside V2 low-MEA rounded capsule; separate sloped strips and rehead around lesson ramp volumes. L2 moves east ofA2 and lowers saddle/headwall to retain400m rising floor belowV5 7000ft terrain budget. No locked runway length/real datum changes."}
```

</details>

<details>
<summary>366. L2/safe_path</summary>

```json
{"phase":"1A.2","item":"L2/safe_path","old":[[-3.02,-1.73],[-4.6,-0.9],[-5.8,-0.3],[-10.18,-1.8]],"new":[[-3.02,-1.73],[-4.3,-0.9],[-5.8,-0.3],[-10.18,-1.8]],"reason":"Move4800ft backcountry pads outside V2 low-MEA rounded capsule; separate sloped strips and rehead around lesson ramp volumes. L2 moves east ofA2 and lowers saddle/headwall to retain400m rising floor belowV5 7000ft terrain budget. No locked runway length/real datum changes."}
```

</details>

<details>
<summary>367. L2/box_path</summary>

```json
{"phase":"1A.2","item":"L2/box_path","old":[[-4.6,-0.9],[-4.95,-1.25]],"new":[[-4.3,-0.9],[-4.65,-1.25]],"reason":"Move4800ft backcountry pads outside V2 low-MEA rounded capsule; separate sloped strips and rehead around lesson ramp volumes. L2 moves east ofA2 and lowers saddle/headwall to retain400m rising floor belowV5 7000ft terrain budget. No locked runway length/real datum changes."}
```

</details>

<details>
<summary>368. L2/saddle_ft</summary>

```json
{"phase":"1A.2","item":"L2/saddle_ft","old":5740,"new":5500,"reason":"Move4800ft backcountry pads outside V2 low-MEA rounded capsule; separate sloped strips and rehead around lesson ramp volumes. L2 moves east ofA2 and lowers saddle/headwall to retain400m rising floor belowV5 7000ft terrain budget. No locked runway length/real datum changes."}
```

</details>

<details>
<summary>369. L2/headwall_ft</summary>

```json
{"phase":"1A.2","item":"L2/headwall_ft","old":7300,"new":6950,"reason":"Move4800ft backcountry pads outside V2 low-MEA rounded capsule; separate sloped strips and rehead around lesson ramp volumes. L2 moves east ofA2 and lowers saddle/headwall to retain400m rising floor belowV5 7000ft terrain budget. No locked runway length/real datum changes."}
```

</details>

<details>
<summary>370. L2/headwall_geometry</summary>

```json
{"phase":"1A.2","item":"L2/headwall_geometry","old":{"centre":[-5.034852813742386,-1.3348528137423856],"top_ft":7300,"radius_m":180,"core_radius_m":50,"blend_m":130},"new":{"centre":[-4.734852813742386,-1.3348528137423856],"top_ft":6950,"radius_m":180,"core_radius_m":50,"blend_m":130},"reason":"Move4800ft backcountry pads outside V2 low-MEA rounded capsule; separate sloped strips and rehead around lesson ramp volumes. L2 moves east ofA2 and lowers saddle/headwall to retain400m rising floor belowV5 7000ft terrain budget. No locked runway length/real datum changes."}
```

</details>

<details>
<summary>371. Chugach core</summary>

```json
{"phase":"1A.2","item":"Chugach core","old":{"id":"Chugach","centre":[-5.4,-1.4],"top_ft":6890,"radius_km":1.6},"new":{"id":"Chugach","centre":[-4,-2.4],"top_ft":6890,"radius_km":1.6},"reason":"6890ft peak relocated east/south out ofA2 3000ft terrain budget, clear ofDrake/KMEV final fans; V5 retains2110ft clearance. Peak true target height preserved."}
```

</details>

<details>
<summary>372. T2 final clearance</summary>

```json
{"phase":"1A.2","item":"T2 final clearance","old":{"id":"T2","centre":[-1.7,-1],"height_agl_m":35,"guyed":false,"charted":true,"lights":true,"horizontal_envelope_radius_m":5},"new":{"id":"T2","centre":[-1.7,-0.65],"height_agl_m":35,"guyed":false,"charted":true,"lights":true,"horizontal_envelope_radius_m":5},"reason":"Relocate charted35m obstacle outside full Thomas final fan and guy footprint; keepsix towers."}
```

</details>

<details>
<summary>373. AP11/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"AP11/heading_true_deg","old":170,"new":132,"reason":"Full rising lesson ramp/headwall envelopes included in strip reheading, plus100m grid fringe, allother fieldpads, peaks andbox clearances. Locked aircraft-field datums/dimensions retained."}
```

</details>

<details>
<summary>374. AP11/numbers_magnetic</summary>

```json
{"phase":"1A.2","item":"AP11/numbers_magnetic","old":"16/34","new":"13/31","reason":"Full rising lesson ramp/headwall envelopes included in strip reheading, plus100m grid fringe, allother fieldpads, peaks andbox clearances. Locked aircraft-field datums/dimensions retained."}
```

</details>

<details>
<summary>375. Chamberlain/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"Chamberlain/heading_true_deg","old":85,"new":31,"reason":"Full rising lesson ramp/headwall envelopes included in strip reheading, plus100m grid fringe, allother fieldpads, peaks andbox clearances. Locked aircraft-field datums/dimensions retained."}
```

</details>

<details>
<summary>376. airparks/AP6/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"airparks/AP6/heading_true_deg","old":121,"new":110,"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>377. airparks/AP6/numbers_magnetic</summary>

```json
{"phase":"1A.2","item":"airparks/AP6/numbers_magnetic","old":"11/29","new":"10/28","reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>378. airparks/AP12/centre</summary>

```json
{"phase":"1A.2","item":"airparks/AP12/centre","old":[1.4,-3.4],"new":[1.4,-3.2],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>379. airparks/AP12/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"airparks/AP12/heading_true_deg","old":171,"new":93,"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>380. airparks/AP12/numbers_magnetic</summary>

```json
{"phase":"1A.2","item":"airparks/AP12/numbers_magnetic","old":"16/34","new":"9/27","reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>381. airparks/AP12/elevation_ft</summary>

```json
{"phase":"1A.2","item":"airparks/AP12/elevation_ft","old":3875,"new":4050,"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>382. airports/RIME/runways</summary>

```json
{"phase":"1A.2","item":"airports/RIME/runways","old":[{"id":"5/23","centre":[-9.5,14],"heading_true_deg":56,"length_m":750,"width_m":24,"surface":"gravel","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":180}}],"new":[{"id":"5/23","centre":[-9.5,14.8],"heading_true_deg":56,"length_m":750,"width_m":24,"surface":"gravel","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":180}}],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>383. backcountry_strips/Thomas Creek/centre</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Thomas Creek/centre","old":[4.6,-0.8],"new":[4.6,-0.65],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>384. backcountry_strips/Sulphur Creek/centre</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Sulphur Creek/centre","old":[4.3,0],"new":[4,0],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>385. backcountry_strips/Soldier Bar/centre</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Soldier Bar/centre","old":[4.4,0.8],"new":[4.5,1.2],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>386. backcountry_strips/Soldier Bar/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Soldier Bar/heading_true_deg","old":189,"new":95,"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>387. backcountry_strips/Soldier Bar/one_way_true_deg</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Soldier Bar/one_way_true_deg","old":189,"new":95,"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>388. lesson_terrain/L2/fork</summary>

```json
{"phase":"1A.2","item":"lesson_terrain/L2/fork","old":[-4.3,-0.9],"new":[-4,-0.9],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>389. lesson_terrain/L2/safe_path</summary>

```json
{"phase":"1A.2","item":"lesson_terrain/L2/safe_path","old":[[-3.02,-1.73],[-4.3,-0.9],[-5.8,-0.3],[-10.18,-1.8]],"new":[[-3.02,-1.73],[-4,-0.9],[-5.8,-0.3],[-10.18,-1.8]],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>390. lesson_terrain/L2/box_path</summary>

```json
{"phase":"1A.2","item":"lesson_terrain/L2/box_path","old":[[-4.3,-0.9],[-4.65,-1.25]],"new":[[-4,-0.9],[-4.15,-1.25]],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>391. lesson_terrain/L2/box_length_m</summary>

```json
{"phase":"1A.2","item":"lesson_terrain/L2/box_length_m","old":494.975,"new":380.789,"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>392. lesson_terrain/L2/headwall_geometry</summary>

```json
{"phase":"1A.2","item":"lesson_terrain/L2/headwall_geometry","old":{"centre":[-4.734852813742386,-1.3348528137423856],"top_ft":6950,"radius_m":180,"core_radius_m":50,"blend_m":130},"new":{"centre":[-4.197270315829501,-1.3602974036021669],"top_ft":6950,"radius_m":180,"core_radius_m":50,"blend_m":130},"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>393. outer_islands/OI1/coast_polygon</summary>

```json
{"phase":"1A.2","item":"outer_islands/OI1/coast_polygon","old":[[-15.7,-0.8],[-15.7,3.5],[-14.7,4.1],[-12.8,3.7],[-12.5,2.5],[-13.3,2.3],[-13.7,1.6],[-13.3,0.9],[-12.5,1],[-12.7,-0.6],[-14,-1.2]],"new":[[-15.7,-0.8],[-15.7,3.9],[-14.7,4.7],[-12.4,4.5],[-12.1,2.5],[-13.1,2.1],[-13.7,1.6],[-13.3,0.9],[-12.5,1],[-12.7,-0.6],[-14,-1.2]],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>394. outer_islands/OI1/base_ft</summary>

```json
{"phase":"1A.2","item":"outer_islands/OI1/base_ft","old":110,"new":35,"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>395. outer_islands/OI1/volcano</summary>

```json
{"phase":"1A.2","item":"outer_islands/OI1/volcano","old":{"centre":[-14.1,2.5],"radius_km":0.85,"rim_radius_km":0.14,"rim_ft":2600,"crater_floor_ft":2250,"shape":"symmetric stratovolcano: linear radial cone, 280 m summit crater; continuous radial crater bowl"},"new":{"centre":[-13.35,3.2],"radius_km":0.85,"rim_radius_km":0.14,"rim_ft":2600,"crater_floor_ft":2250,"shape":"symmetric stratovolcano: linear radial cone, 280 m summit crater; continuous radial crater bowl"},"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>396. outer_islands/OI2/coast_polygon</summary>

```json
{"phase":"1A.2","item":"outer_islands/OI2/coast_polygon","old":[[10.6,0.3],[10.6,4],[11.5,4.6],[14.6,4.3],[15.7,3.5],[15.7,0.3],[13,-0.3],[11.4,-0.2]],"new":[[10.6,0.3],[10.6,4],[11.5,4.6],[17.4,4.3],[18.2,3.5],[18.2,0.3],[13,-0.3],[11.4,-0.2]],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>397. outer_islands/OI5/coast_polygon</summary>

```json
{"phase":"1A.2","item":"outer_islands/OI5/coast_polygon","old":[[-8.50515491293395,14.671031484164896],[-8.455274494240175,14.578721822752955],[-8.423269618602582,14.476510070501712],[-8.409687898316758,14.36614509883063],[-8.41476172038236,14.249515283003959],[-8.438404270297115,14.128616191506383],[-8.480211017477027,14.005516441312988],[-8.539466636886829,13.882322303279281],[-8.615157248449657,13.761141663262887],[-8.705987764816271,13.644047955612107],[-8.810404050668659,13.533044686129356],[-8.926619514406225,13.430031151531404],[-9.052645677223403,13.336769941955966],[-9.186326196533338,13.254856782555775],[-9.325373761587397,13.185693230198785],[-9.467409229996235,13.130462692441581],[-9.610002335515707,13.09011017909799],[-9.750713270576096,13.065326132859152],[-9.887134432066079,13.056534615627644],[-10.016931616089465,13.063886052700772],[-10.137883956841042,13.08725465895201],[-10.247921926236224,13.126240591049394],[-10.345162744110372,13.180176788885795],[-10.427942593109407,13.2481403891623],[-10.49484508706605,13.328968515835104],[-10.544725505759825,13.421278177247045],[-10.576730381397418,13.523489929498288],[-10.590312101683242,13.633854901169368],[-10.58523827961764,13.750484716996041],[-10.561595729702885,13.871383808493617],[-10.519788982522973,13.994483558687012],[-10.460533363113171,14.117677696720719],[-10.384842751550341,14.238858336737113],[-10.294012235183729,14.355952044387893],[-10.189595949331341,14.466955313870644],[-10.073380485593775,14.569968848468596],[-9.947354322776597,14.663230058044034],[-9.813673803466662,14.745143217444223],[-9.674626238412603,14.814306769801215],[-9.532590770003765,14.869537307558419],[-9.389997664484293,14.90988982090201],[-9.249286729423904,14.934673867140848],[-9.112865567933921,14.943465384372356],[-8.983068383910535,14.936113947299228],[-8.862116043158958,14.91274534104799],[-8.752078073763776,14.873759408950608],[-8.65483725588963,14.819823211114205],[-8.572057406890593,14.7518596108377]],"new":[[-8.50515491293395,15.471031484164897],[-8.455274494240175,15.378721822752956],[-8.423269618602582,15.276510070501713],[-8.409687898316758,15.166145098830631],[-8.41476172038236,15.04951528300396],[-8.438404270297115,14.928616191506384],[-8.480211017477027,14.805516441312989],[-8.539466636886829,14.682322303279282],[-8.615157248449657,14.561141663262887],[-8.705987764816271,14.444047955612108],[-8.810404050668659,14.333044686129357],[-8.926619514406225,14.230031151531405],[-9.052645677223403,14.136769941955967],[-9.186326196533338,14.054856782555776],[-9.325373761587397,13.985693230198786],[-9.467409229996235,13.930462692441582],[-9.610002335515707,13.890110179097992],[-9.750713270576096,13.865326132859153],[-9.887134432066079,13.856534615627645],[-10.016931616089465,13.863886052700773],[-10.137883956841042,13.88725465895201],[-10.247921926236224,13.926240591049394],[-10.345162744110372,13.980176788885796],[-10.427942593109407,14.0481403891623],[-10.49484508706605,14.128968515835105],[-10.544725505759825,14.221278177247045],[-10.576730381397418,14.323489929498288],[-10.590312101683242,14.433854901169369],[-10.58523827961764,14.550484716996042],[-10.561595729702885,14.671383808493617],[-10.519788982522973,14.794483558687013],[-10.460533363113171,14.91767769672072],[-10.384842751550341,15.038858336737114],[-10.294012235183729,15.155952044387893],[-10.189595949331341,15.266955313870644],[-10.073380485593775,15.369968848468597],[-9.947354322776597,15.463230058044035],[-9.813673803466662,15.545143217444224],[-9.674626238412603,15.614306769801216],[-9.532590770003765,15.66953730755842],[-9.389997664484293,15.70988982090201],[-9.249286729423904,15.734673867140849],[-9.112865567933921,15.743465384372357],[-8.983068383910535,15.736113947299229],[-8.862116043158958,15.712745341047992],[-8.752078073763776,15.673759408950609],[-8.65483725588963,15.619823211114205],[-8.572057406890593,15.5518596108377]],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>398. outer_navaid_sites/SITE-SITX/elevation_ft</summary>

```json
{"phase":"1A.2","item":"outer_navaid_sites/SITE-SITX/elevation_ft","old":110,"new":35,"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>399. outer_navaid_sites/SITE-RIME/centre</summary>

```json
{"phase":"1A.2","item":"outer_navaid_sites/SITE-RIME/centre","old":[-9.45,14],"new":[-9.45,14.8],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>400. landable_beaches/B4/centre</summary>

```json
{"phase":"1A.2","item":"landable_beaches/B4/centre","old":[-13.6,-0.4],"new":[-13.6,-0.3],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>401. landable_beaches/B4/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"landable_beaches/B4/heading_true_deg","old":1,"new":40,"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>402. combat_zones/CZ3/gunnery_targets</summary>

```json
{"phase":"1A.2","item":"combat_zones/CZ3/gunnery_targets","old":[{"id":"CZ3-GT1","centre":[-8.0,16.9],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170},{"id":"CZ3-GT2","centre":[-7.0,16.9],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170},{"id":"CZ3-GT3","centre":[-7.5,17.7],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170}],"new":[{"id":"CZ3-GT1","centre":[-8.4,17.0],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170},{"id":"CZ3-GT2","centre":[-7.4,17.0],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170},{"id":"CZ3-GT3","centre":[-7.9,17.4],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170}],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>403. combat_zones/CZ3/drone_orbit</summary>

```json
{"phase":"1A.2","item":"combat_zones/CZ3/drone_orbit","old":{"centre_start":"CZ3","count":3,"radius_m_target":150,"altitude_ft_target":900,"owner":"Claude opponent/AI logic","centre":[-7.5,16.9]},"new":{"centre_start":"CZ3","count":3,"radius_m_target":150,"altitude_ft_target":900,"owner":"Claude opponent/AI logic","centre":[-7.9,17.0]},"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>404. combat_zones/CZ3/combat_features</summary>

```json
{"phase":"1A.2","item":"combat_zones/CZ3/combat_features","old":[{"id":"CZ3-CHANNEL","type":"island cluster and channel","centre":[-7.5,16.9],"channel_width_m":300,"channel_length_m":1100,"water_msl_ft":0,"flight_half_width_m":40,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"terrain":"five islands with smooth ridges/sea stacks, two 300m channels; hide-and-seek and strafe route"},{"id":"CZ3-BRIDGE","type":"bridge","centre":[-7.5,16.9],"heading_true_deg":90,"span_m":300,"deck_width_m":14,"under_clearance_m":120,"under_base_msl_ft":0,"deck_thickness_m":4,"flight_opening_width_m":240,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids bridge deck/piers; no heightfield bridge deck"},{"id":"CZ3-ARCH","type":"rock arch","centre":[-7.0,17.3],"axis_true_deg":0,"opening_width_m":100,"opening_height_m":110,"base_msl_ft":60,"rock_top_msl_ft":460,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids overhang/opening; footprint pedestals only in shared heightfield"}],"new":[{"id":"CZ3-CHANNEL","type":"island cluster and channel","centre":[-7.9,17.0],"channel_width_m":300,"channel_length_m":600,"water_msl_ft":0,"flight_half_width_m":40,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"terrain":"five islands with smooth ridges/sea stacks, two 300m channels; hide-and-seek and strafe route"},{"id":"CZ3-BRIDGE","type":"bridge","centre":[-7.9,17.0],"heading_true_deg":90,"span_m":300,"deck_width_m":14,"under_clearance_m":120,"under_base_msl_ft":0,"deck_thickness_m":4,"flight_opening_width_m":240,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids bridge deck/piers; no heightfield bridge deck"},{"id":"CZ3-ARCH","type":"rock arch","centre":[-7.4,17.4],"axis_true_deg":0,"opening_width_m":100,"opening_height_m":110,"base_msl_ft":60,"rock_top_msl_ft":460,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids overhang/opening; footprint pedestals only in shared heightfield"}],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>405. combat_zones/CZ5/boundary</summary>

```json
{"phase":"1A.2","item":"combat_zones/CZ5/boundary","old":[[14.6,2.2],[15,2.2],[15,2.6],[14.6,2.6]],"new":[[17.1,2.2],[17.5,2.2],[17.5,2.6],[17.1,2.6]],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>406. combat_zones/CZ5/gunnery_targets</summary>

```json
{"phase":"1A.2","item":"combat_zones/CZ5/gunnery_targets","old":[{"id":"CZ5-GT1","centre":[14.66,2.28],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":220},{"id":"CZ5-GT2","centre":[14.94,2.52],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":220}],"new":[{"id":"CZ5-GT1","centre":[17.16,2.28],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":220},{"id":"CZ5-GT2","centre":[17.44,2.52],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":220}],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>407. combat_zones/CZ5/drone_orbit</summary>

```json
{"phase":"1A.2","item":"combat_zones/CZ5/drone_orbit","old":{"centre_start":"CZ5","count":3,"radius_m_target":70,"altitude_ft_target":600,"owner":"Claude opponent/AI logic","centre":[14.8,2.4]},"new":{"centre_start":"CZ5","count":3,"radius_m_target":70,"altitude_ft_target":600,"owner":"Claude opponent/AI logic","centre":[17.3,2.4]},"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>408. combat_zones/CZ5/combat_features</summary>

```json
{"phase":"1A.2","item":"combat_zones/CZ5/combat_features","old":[{"id":"CZ5-RIDGE","type":"paired ridges and dry canyon","centre":[14.8,2.4],"axis_true_deg":0,"length_m":300,"canyon_width_m":100,"canyon_depth_m":2.4384,"terrace_floor_ft":180,"rim_ft":188,"ridge_half_width_m":35,"flight_half_width_m":20,"flight_floor_msl_ft":220,"flight_ceiling_msl_ft":300},{"id":"CZ5-BRIDGE","type":"bridge","centre":[14.8,2.4],"heading_true_deg":90,"span_m":120,"deck_width_m":14,"under_clearance_m":60,"under_base_msl_ft":180,"deck_thickness_m":4,"flight_opening_width_m":100,"flight_floor_msl_ft":220,"flight_ceiling_msl_ft":300,"collision":"WorldSolids bridge deck/piers; no heightfield bridge deck"},{"id":"CZ5-ARCH","type":"rock arch","centre":[14.91,2.488],"axis_true_deg":0,"opening_width_m":60,"opening_height_m":55,"base_msl_ft":180,"rock_top_msl_ft":400,"flight_floor_msl_ft":220,"flight_ceiling_msl_ft":300,"collision":"WorldSolids overhang/opening; footprint pedestals only in shared heightfield"}],"new":[{"id":"CZ5-RIDGE","type":"paired ridges and dry canyon","centre":[17.3,2.4],"axis_true_deg":0,"length_m":300,"canyon_width_m":100,"canyon_depth_m":2.4384,"terrace_floor_ft":180,"rim_ft":188,"ridge_half_width_m":35,"flight_half_width_m":20,"flight_floor_msl_ft":220,"flight_ceiling_msl_ft":300},{"id":"CZ5-BRIDGE","type":"bridge","centre":[17.3,2.4],"heading_true_deg":90,"span_m":120,"deck_width_m":14,"under_clearance_m":60,"under_base_msl_ft":180,"deck_thickness_m":4,"flight_opening_width_m":100,"flight_floor_msl_ft":220,"flight_ceiling_msl_ft":300,"collision":"WorldSolids bridge deck/piers; no heightfield bridge deck"},{"id":"CZ5-ARCH","type":"rock arch","centre":[17.41,2.488],"axis_true_deg":0,"opening_width_m":60,"opening_height_m":55,"base_msl_ft":180,"rock_top_msl_ft":400,"flight_floor_msl_ft":220,"flight_ceiling_msl_ft":300,"collision":"WorldSolids overhang/opening; footprint pedestals only in shared heightfield"}],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>409. islets/CZ3-IS1/centre</summary>

```json
{"phase":"1A.2","item":"islets/CZ3-IS1/centre","old":[-8.0,16.9],"new":[-8.4,17.0],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>410. islets/CZ3-IS2/centre</summary>

```json
{"phase":"1A.2","item":"islets/CZ3-IS2/centre","old":[-7.0,16.9],"new":[-7.4,17.0],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>411. islets/CZ3-IS3/centre</summary>

```json
{"phase":"1A.2","item":"islets/CZ3-IS3/centre","old":[-7.5,17.7],"new":[-7.9,17.4],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>412. islets/CZ3-IS3/radii_km</summary>

```json
{"phase":"1A.2","item":"islets/CZ3-IS3/radii_km","old":[0.34,0.25],"new":[0.34,0.18],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>413. islets/CZ3-IS4/centre</summary>

```json
{"phase":"1A.2","item":"islets/CZ3-IS4/centre","old":[-8.3,16.3],"new":[-8.7,16.4],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>414. islets/CZ3-IS5/centre</summary>

```json
{"phase":"1A.2","item":"islets/CZ3-IS5/centre","old":[-6.65,17.55],"new":[-7.05,17.65],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>415. islets/CZ3-ARCH-W/centre</summary>

```json
{"phase":"1A.2","item":"islets/CZ3-ARCH-W/centre","old":[-7.075,17.3],"new":[-7.475,17.4],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>416. islets/CZ3-ARCH-E/centre</summary>

```json
{"phase":"1A.2","item":"islets/CZ3-ARCH-E/centre","old":[-6.925,17.3],"new":[-7.325,17.4],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>417. islets/CZ5-ARCH-W/centre</summary>

```json
{"phase":"1A.2","item":"islets/CZ5-ARCH-W/centre","old":[14.855,2.488],"new":[17.355,2.488],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>418. islets/CZ5-ARCH-E/centre</summary>

```json
{"phase":"1A.2","item":"islets/CZ5-ARCH-E/centre","old":[14.965,2.488],"new":[17.465,2.488],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>419. starts/PALH:OnTheRunway/centre</summary>

```json
{"phase":"1A.2","item":"starts/PALH:OnTheRunway/centre","old":[-10.172048,-1.8],"new":[-10.33841,-1.208807],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>420. starts/PALH:OnTheRunway/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"starts/PALH:OnTheRunway/heading_true_deg","old":90,"new":165,"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>421. starts/PALH:OnFinal/centre</summary>

```json
{"phase":"1A.2","item":"starts/PALH:OnFinal/centre","old":[-11.052048,-1.8],"new":[-10.566171,-0.358792],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>422. starts/PALH:OnFinal/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"starts/PALH:OnFinal/heading_true_deg","old":90,"new":165,"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>423. starts/PALH:InTheAir/centre</summary>

```json
{"phase":"1A.2","item":"starts/PALH:InTheAir/centre","old":[-11.252048,-1.8],"new":[-10.617934,-0.165607],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>424. starts/PALH:InTheAir/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"starts/PALH:InTheAir/heading_true_deg","old":90,"new":165,"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>425. starts/KMYL:OnTheRunway/centre</summary>

```json
{"phase":"1A.2","item":"starts/KMYL:OnTheRunway/centre","old":[-0.594831,3.629663],"new":[-0.894831,3.629663],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>426. starts/KMYL:OnFinal/centre</summary>

```json
{"phase":"1A.2","item":"starts/KMYL:OnFinal/centre","old":[-0.610189,4.509529],"new":[-0.910189,4.509529],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>427. starts/KMYL:InTheAir/centre</summary>

```json
{"phase":"1A.2","item":"starts/KMYL:InTheAir/centre","old":[-0.61368,4.709498],"new":[-0.91368,4.709498],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>428. starts/CZ3/centre</summary>

```json
{"phase":"1A.2","item":"starts/CZ3/centre","old":[-7.5,16.9],"new":[-7.9,17],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>429. starts/CZ5/centre</summary>

```json
{"phase":"1A.2","item":"starts/CZ5/centre","old":[14.8,2.4],"new":[17.3,2.4],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>430. practice_lines/ST-PALH/path</summary>

```json
{"phase":"1A.2","item":"practice_lines/ST-PALH/path","old":[[-10.16,-1.858651],[-8.96,-1.858651]],"new":[[-10.391944,-1.235625],[-10.081361,-2.394736]],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>431. practice_lines/ST-PATK/path</summary>

```json
{"phase":"1A.2","item":"practice_lines/ST-PATK/path","old":[[-6.536164,4.513748],[-6.070007,5.428634]],"new":[[-6.57548,4.436587],[-6.030691,5.505795]],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>432. practice_lines/ST-KMYL/path</summary>

```json
{"phase":"1A.2","item":"practice_lines/ST-KMYL/path","old":[[-0.631895,3.379186],[-0.610952,2.179368]],"new":[[-0.931895,3.379186],[-0.910952,2.179368]],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>433. practice_lines/ST-3U2/path</summary>

```json
{"phase":"1A.2","item":"practice_lines/ST-3U2/path","old":[[1.871868,-0.361736],[1.664722,-1.336284]],"new":[[1.893042,-0.262121],[1.643548,-1.435898]],"reason":"Final all-rule layout: resolve low-MEA pad/volcano/wire conflicts, protect full2nm perimeter swaths, keep small land MOA beyond that swath, and synchronize starts/lesson/marker geometry. All changes are review targets under G1a."}
```

</details>

<details>
<summary>434. perimeter_traffic_reservation final fit coordination</summary>

```json
{"phase":"1A.2","item":"perimeter_traffic_reservation final fit coordination","old":{"owner":"Codex proposed terrain/land-use reservation for Claude; not T201 geometry","purpose":"Outer-airport traffic runs around main island via water perimeter; avoid interior crossings, combat and box reservations","path":[[-15,1.1],[-13,-5.5],[-8,-10],[0.5,-11],[8.8,-9],[11.3,2],[8,8],[0,10.3],[-7,9],[-15,1.1]],"half_width_km":0.35,"floor_ft":6000,"ceiling_ft":7000,"note":"T201 and instrument procedures remain unchanged/unknown. Proposed route is a reservation only; gate approval does not publish IFR."},"new":{"owner":"Codex proposed terrain/land-use reservation for Claude; not T201 geometry","purpose":"Outer-airport traffic runs around main island via water perimeter; avoid interior crossings, combat and box reservations","path":[[-15,1.1],[-13,-5.5],[-8,-10],[0.5,-11],[8.8,-9],[11.3,2],[8,8],[0,10.3],[-7,9],[-15,1.1]],"half_width_km":0.35,"floor_ft":6000,"ceiling_ft":7000,"note":"Claude-owned IFR not built: these are proposed terrain/traffic reservations.2nm terrain swath for clearance;350m flight half-width. Northern segments target11000ft to clear8530ft Denali; other segments6000ft. Descend below3000ft outside combat before entering protected KITE/RIME arrival corridors.","terrain_half_width_km":3.704,"mea_ft_targets":[6000,6000,6000,6000,6000,6000,11000,11000,6000],"ceiling_above_mea_ft":1000},"reason":"Same coordinated terrain/airspace changes; Chugach6890ft moved outsideG1 6000ft terrain cap, shortenedW3 overKnik outwash, movedL2 away fromDrake northfinal. Exact dimensions and values retained here."}
```

</details>

<details>
<summary>435. height_field final fit coordination</summary>

```json
{"phase":"1A.2","item":"height_field final fit coordination","old":{"algorithm":"island-review-v2-1a2","grid":{"min_km":-20,"max_km":20,"spacing_m":100,"rows":401,"columns":401,"format":"little-endian float32 metres MSL; row0south; column0west; xfastest; fixed SW-NE diagonal linear interpolation; analytic same-source pads/cavities/solids detail","interpolation":"fixed SW-NE diagonal, piecewise-linear triangles; explicit analytic flat/sloped pad detail overlays are part of same authoring evaluator"},"base":{"interior_ft":4600,"coastal_ft":30,"interior_radius_km":6,"shore_transition_km":2},"peaks":[{"id":"Frank Church","centre":[0,0],"top_ft":9020,"radius_km":2.0},{"id":"Sierra","centre":[-1.6,-0.3],"top_ft":7550,"radius_km":1.3},{"id":"Chugach","centre":[-4,-2.4],"top_ft":6890,"radius_km":1.6},{"id":"Denali-like","centre":[0.5,7.1],"top_ft":8530,"radius_km":1.5},{"id":"Mokuleia","centre":[5.6,4],"top_ft":2950,"radius_km":1.5},{"id":"Catalina","centre":[2.9,-13.7],"top_ft":2100,"radius_km":0.8},{"id":"Drake Hill","centre":[-4.9,-4.5],"top_ft":5750,"radius_km":0.7}],"crests":[{"id":"Mokuleia","path":[[3.17,4.99],[4.6,4.4],[6.05,3.74]],"width_km":0.5,"top_ft":2950},{"id":"Sierra","path":[[-1.1,0.3],[-1.54,-0.58],[-2.2,-2]],"width_km":0.5,"top_ft":7550},{"id":"Chugach-Denali","path":[[-5.6,-2.8],[-5.4,-1.4],[-5.3,-0.6],[-5,1.4],[-4.2,3.4],[-3.46,5.86]],"width_km":0.5,"top_ft":7000},{"id":"Summit","path":[[-0.9,1],[0,0],[1,0.6],[0.4,-1.2]],"width_km":0.5,"top_ft":8000}],"order":["coast and bed","regional base","Mokuleia regional lowland override","smooth crest and summit envelopes","lakes and glacier tongues","canyon cuts","pass and box-fork cuts","reassert peak centre elevations with compact smooth envelopes","flat/sloped pads with continuous blend","coast fade to ocean bed"],"continuity":"Every local influence has zero weight at its footprint boundary using cubic smoothstep; no boolean relief cutoff. Cavities require WorldSolids shell, not a second ground field.","sampling_limit":"100 m bake is a review artefact; continuous evaluator authoritative; 60 m shoulders and small solids need analytic evaluation in Phase 1B.","water_lane_bed_offset_m":3,"summit_reassertion":{"core_radius_km":0.12,"blend_end_radius_km":0.4,"order":"after cuts, before surface pads; set exact authored summit elevations"},"regional_overrides":[{"id":"Mokuleia lowland","centre":[5,4],"radii_km":[3,3],"elevation_ft":1800}],"cavities":{"DRAKE":"height field is upper rock envelope; only exterior152m uses ground pad; interior floor is separate height-aware deck/shell at4700ft; never flatten full hill to cave floor","collision_status":"Phase1B requires below-roof ground/contact routing; current review bake cannot certify cave interiors"},"clearance_envelopes":{"finals":{"length_km":7.408,"half_width_at_threshold_km":0.15,"widen_per_km":0.05,"outer_blend_km":0.2,"ils_loc_lda_ratio":34,"other_ratio":20,"vertical_margin_m":30,"constant_guard_km":0.25},"airways":{"half_width_km":3.704,"outer_blend_km":0.3,"clearance_ft":2025,"constant_guard_km":0.15},"order":"cut terrain after landforms; reassert locked field pads and authored peak cores; conflicts remain failures, never lowered by stealth"},"land_moa_terrace":{"boundary":[[14.6,2.2],[15,2.2],[15,2.6],[14.6,2.6]],"elevation_ft":180,"blend_m":150},"lesson_ramp_order":"after full terrain clearance cuts, before peak cores and pads; finite ramps/headwalls fade continuously; full conflict validator includes them"},"new":{"algorithm":"island-review-v2-1a2","grid":{"min_km":-20,"max_km":20,"spacing_m":100,"rows":401,"columns":401,"format":"little-endian float32 metres MSL; row0south; column0west; xfastest; fixed SW-NE diagonal linear interpolation; analytic same-source pads/cavities/solids detail","interpolation":"fixed SW-NE diagonal, piecewise-linear triangles; explicit analytic flat/sloped pad detail overlays are part of same authoring evaluator"},"base":{"interior_ft":4600,"coastal_ft":30,"interior_radius_km":6,"shore_transition_km":2},"peaks":[{"id":"Frank Church","centre":[0,0],"top_ft":9020,"radius_km":2.0},{"id":"Sierra","centre":[-1.6,-0.3],"top_ft":7550,"radius_km":1.3},{"id":"Chugach","centre":[-3.8,-1.8],"top_ft":6890,"radius_km":1.6},{"id":"Denali-like","centre":[0.5,7.1],"top_ft":8530,"radius_km":1.5},{"id":"Mokuleia","centre":[5.6,4],"top_ft":2950,"radius_km":1.5},{"id":"Catalina","centre":[2.9,-13.7],"top_ft":2100,"radius_km":0.8},{"id":"Drake Hill","centre":[-4.9,-4.5],"top_ft":5750,"radius_km":0.7}],"crests":[{"id":"Mokuleia","path":[[3.17,4.99],[4.6,4.4],[6.05,3.74]],"width_km":0.5,"top_ft":2950},{"id":"Sierra","path":[[-1.1,0.3],[-1.54,-0.58],[-2.2,-2]],"width_km":0.5,"top_ft":7550},{"id":"Chugach-Denali","path":[[-5.6,-2.8],[-5.4,-1.4],[-5.3,-0.6],[-5,1.4],[-4.2,3.4],[-3.46,5.86]],"width_km":0.5,"top_ft":7000},{"id":"Summit","path":[[-0.9,1],[0,0],[1,0.6],[0.4,-1.2]],"width_km":0.5,"top_ft":8000}],"order":["coast and bed","regional base","Mokuleia regional lowland override","smooth crest and summit envelopes","lakes and glacier tongues","canyon cuts","pass and box-fork cuts","reassert peak centre elevations with compact smooth envelopes","flat/sloped pads with continuous blend","coast fade to ocean bed"],"continuity":"Every local influence has zero weight at its footprint boundary using cubic smoothstep; no boolean relief cutoff. Cavities require WorldSolids shell, not a second ground field.","sampling_limit":"100 m bake is a review artefact; continuous evaluator authoritative; 60 m shoulders and small solids need analytic evaluation in Phase 1B.","water_lane_bed_offset_m":3,"summit_reassertion":{"core_radius_km":0.12,"blend_end_radius_km":0.4,"order":"after cuts, before surface pads; set exact authored summit elevations"},"regional_overrides":[{"id":"Mokuleia lowland","centre":[5,4],"radii_km":[3,3],"elevation_ft":1800}],"cavities":{"DRAKE":"height field is upper rock envelope; only exterior152m uses ground pad; interior floor is separate height-aware deck/shell at4700ft; never flatten full hill to cave floor","collision_status":"Phase1B requires below-roof ground/contact routing; current review bake cannot certify cave interiors"},"clearance_envelopes":{"finals":{"length_km":7.408,"half_width_at_threshold_km":0.15,"widen_per_km":0.05,"outer_blend_km":0.2,"ils_loc_lda_ratio":34,"other_ratio":20,"vertical_margin_m":30,"constant_guard_km":0.25},"airways":{"half_width_km":3.704,"outer_blend_km":0.3,"clearance_ft":2025,"constant_guard_km":0.15},"order":"cut terrain after landforms; reassert locked field pads and authored peak cores; conflicts remain failures, never lowered by stealth"},"land_moa_terrace":{"boundary":[[17.1,2.2],[17.5,2.2],[17.5,2.6],[17.1,2.6]],"elevation_ft":180,"blend_m":150},"lesson_ramp_order":"after full terrain clearance cuts, before peak cores and pads; finite ramps/headwalls fade continuously; full conflict validator includes them"},"reason":"Same coordinated terrain/airspace changes; Chugach6890ft moved outsideG1 6000ft terrain cap, shortenedW3 overKnik outwash, movedL2 away fromDrake northfinal. Exact dimensions and values retained here."}
```

</details>

<details>
<summary>436. hazards final fit coordination</summary>

```json
{"phase":"1A.2","item":"hazards final fit coordination","old":{"owner":"Codex; deterministic WorldSolids, excluded from height field; all charted","towers":[{"id":"T1","centre":[4.9,4.45],"height_agl_m":75,"guyed":true,"charted":true,"lights":true,"horizontal_envelope_radius_m":65.0},{"id":"T2","centre":[-1.7,-0.65],"height_agl_m":35,"guyed":false,"charted":true,"lights":true,"horizontal_envelope_radius_m":5},{"id":"T3","centre":[5.2,-6.3],"height_agl_m":180,"guyed":true,"charted":true,"lights":true,"horizontal_envelope_radius_m":149.0},{"id":"T4","centre":[-6.9,-2.9],"height_agl_m":50,"guyed":true,"charted":true,"lights":true,"horizontal_envelope_radius_m":45.0},{"id":"T5","centre":[-6.9,5.4],"height_agl_m":55,"guyed":true,"charted":true,"lights":true,"horizontal_envelope_radius_m":49.0},{"id":"T6","centre":[6.5,-4.8],"height_agl_m":45,"guyed":true,"charted":true,"lights":true,"horizontal_envelope_radius_m":41.0}],"wires":[{"id":"W1","endpoints":[[1.55,0.25],[2.2,0.3]],"lowest_agl_m":40,"charted":true,"marker_balls":true,"lights":true,"sag_fraction":0.08},{"id":"W2","endpoints":[[5.8,-5.9],[6.1,-5.6]],"lowest_agl_m":250,"charted":true,"marker_balls":true,"lights":true,"sag_fraction":0.08},{"id":"W3","endpoints":[[-6.55,-1.1],[-6.65,-0.05]],"lowest_agl_m":30,"charted":true,"marker_balls":true,"lights":true,"sag_fraction":0.08},{"id":"W4","endpoints":[[-1.5,-4.5],[-0.3,-4.5]],"lowest_agl_m":18,"charted":true,"marker_balls":true,"lights":true,"sag_fraction":0.08},{"id":"W5","endpoints":[[1.3,-9.4],[2,-9.3]],"lowest_agl_m":60,"charted":true,"marker_balls":true,"lights":true,"sag_fraction":0.08}]},"new":{"owner":"Codex; deterministic WorldSolids, excluded from height field; all charted","towers":[{"id":"T1","centre":[4.9,4.45],"height_agl_m":75,"guyed":true,"charted":true,"lights":true,"horizontal_envelope_radius_m":65.0},{"id":"T2","centre":[-1.7,-0.65],"height_agl_m":35,"guyed":false,"charted":true,"lights":true,"horizontal_envelope_radius_m":5},{"id":"T3","centre":[5.2,-6.3],"height_agl_m":180,"guyed":true,"charted":true,"lights":true,"horizontal_envelope_radius_m":149.0},{"id":"T4","centre":[-6.9,-2.9],"height_agl_m":50,"guyed":true,"charted":true,"lights":true,"horizontal_envelope_radius_m":45.0},{"id":"T5","centre":[-6.9,5.4],"height_agl_m":55,"guyed":true,"charted":true,"lights":true,"horizontal_envelope_radius_m":49.0},{"id":"T6","centre":[6.5,-4.8],"height_agl_m":45,"guyed":true,"charted":true,"lights":true,"horizontal_envelope_radius_m":41.0}],"wires":[{"id":"W1","endpoints":[[1.55,0.25],[2.2,0.3]],"lowest_agl_m":40,"charted":true,"marker_balls":true,"lights":true,"sag_fraction":0.08},{"id":"W2","endpoints":[[5.8,-5.9],[6.1,-5.6]],"lowest_agl_m":250,"charted":true,"marker_balls":true,"lights":true,"sag_fraction":0.08},{"id":"W3","endpoints":[[-6.55,-0.65],[-6.65,-0.15]],"lowest_agl_m":30,"charted":true,"marker_balls":true,"lights":true,"sag_fraction":0.08},{"id":"W4","endpoints":[[-1.5,-4.5],[-0.3,-4.5]],"lowest_agl_m":18,"charted":true,"marker_balls":true,"lights":true,"sag_fraction":0.08},{"id":"W5","endpoints":[[1.3,-9.4],[2,-9.3]],"lowest_agl_m":60,"charted":true,"marker_balls":true,"lights":true,"sag_fraction":0.08}]},"reason":"Same coordinated terrain/airspace changes; Chugach6890ft moved outsideG1 6000ft terrain cap, shortenedW3 overKnik outwash, movedL2 away fromDrake northfinal. Exact dimensions and values retained here."}
```

</details>

<details>
<summary>437. airparks/AP4/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"airparks/AP4/heading_true_deg","old":125,"new":143,"reason":"Thread short-strip final fans between neighboring pads and lesson ramp; rotate lowAP4 through gap between Mokuleia ridge, highAP6 and Mackay. Keep all locked dimensions/datums and>=.8/1.5km spacing."}
```

</details>

<details>
<summary>438. airparks/AP4/numbers_magnetic</summary>

```json
{"phase":"1A.2","item":"airparks/AP4/numbers_magnetic","old":"12/30","new":"14/32","reason":"Thread short-strip final fans between neighboring pads and lesson ramp; rotate lowAP4 through gap between Mokuleia ridge, highAP6 and Mackay. Keep all locked dimensions/datums and>=.8/1.5km spacing."}
```

</details>

<details>
<summary>439. backcountry_strips/Soldier Bar/centre</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Soldier Bar/centre","old":[4.5,1.2],"new":[4.5,1.25],"reason":"Thread short-strip final fans between neighboring pads and lesson ramp; rotate lowAP4 through gap between Mokuleia ridge, highAP6 and Mackay. Keep all locked dimensions/datums and>=.8/1.5km spacing."}
```

</details>

<details>
<summary>440. backcountry_strips/Soldier Bar/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Soldier Bar/heading_true_deg","old":95,"new":88,"reason":"Thread short-strip final fans between neighboring pads and lesson ramp; rotate lowAP4 through gap between Mokuleia ridge, highAP6 and Mackay. Keep all locked dimensions/datums and>=.8/1.5km spacing."}
```

</details>

<details>
<summary>441. backcountry_strips/Soldier Bar/one_way_true_deg</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Soldier Bar/one_way_true_deg","old":95,"new":88,"reason":"Thread short-strip final fans between neighboring pads and lesson ramp; rotate lowAP4 through gap between Mokuleia ridge, highAP6 and Mackay. Keep all locked dimensions/datums and>=.8/1.5km spacing."}
```

</details>

<details>
<summary>442. backcountry_strips/Cabin Creek/centre</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Cabin Creek/centre","old":[3.1,0.75],"new":[3.1,0.55],"reason":"Thread short-strip final fans between neighboring pads and lesson ramp; rotate lowAP4 through gap between Mokuleia ridge, highAP6 and Mackay. Keep all locked dimensions/datums and>=.8/1.5km spacing."}
```

</details>

<details>
<summary>443. backcountry_strips/Indian Creek/centre</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Indian Creek/centre","old":[3.8,1.6],"new":[3.8,1.7],"reason":"Thread short-strip final fans between neighboring pads and lesson ramp; rotate lowAP4 through gap between Mokuleia ridge, highAP6 and Mackay. Keep all locked dimensions/datums and>=.8/1.5km spacing."}
```

</details>

<details>
<summary>444. outer_islands/OI1/coast_polygon</summary>

```json
{"phase":"1A.2","item":"outer_islands/OI1/coast_polygon","old":[[-15.7,-0.8],[-15.7,3.9],[-14.7,4.7],[-12.4,4.5],[-12.1,2.5],[-13.1,2.1],[-13.7,1.6],[-13.3,0.9],[-12.5,1],[-12.7,-0.6],[-14,-1.2]],"new":[[-15.7,-0.8],[-15.7,0.55],[-15.2,0.9],[-15.2,1.3],[-15.7,1.65],[-15.7,3.9],[-14.7,4.7],[-12.4,4.5],[-12.1,2.5],[-13.1,2.1],[-13.7,1.6],[-14.8,1.25],[-14.8,0.95],[-13.3,0.9],[-12.5,1],[-12.7,-0.6],[-14,-1.2]],"reason":"Make Sitka runway an actual waterfront causeway with protected end lobes and correct site datums. Keep entireKite island underCZ1 and clear full2nm perimeter/mid-beach exclusion; move low-level arena beyond own raised-floor traffic masks. Explicit polygonal sector definition for exact verification."}
```

</details>

<details>
<summary>445. outer_islands/OI1/cove</summary>

```json
{"phase":"1A.2","item":"outer_islands/OI1/cove","old":{"mouth":[-12.7,1.6],"head":[-13.6,1.6],"shelter":"east-facing cove behind southern arm and islets"},"new":{"mouth":[-12.7,1.6],"head":[-14.8,1.1],"shelter":"east-facing cove behind southern arm and islets"},"reason":"Make Sitka runway an actual waterfront causeway with protected end lobes and correct site datums. Keep entireKite island underCZ1 and clear full2nm perimeter/mid-beach exclusion; move low-level arena beyond own raised-floor traffic masks. Explicit polygonal sector definition for exact verification."}
```

</details>

<details>
<summary>446. outer_islands/OI1/runway_note</summary>

```json
{"phase":"1A.2","item":"outer_islands/OI1/runway_note","old":"Waterfront causeway/fill on west coast; >300 m outer coastal apron; fictional compact Sitka-inspired airport, not PASI data","new":"1100x30m asphalt waterfront fill at30ft; causeway midsection has water200m from each side ofcentreline, with150m total runway+shoulders and125m land margin beyondpad. Ends open into widefill lobes to preserve>=300m end/shore setback. Volcano full footprint kept clear of all final cuts.","reason":"Make Sitka runway an actual waterfront causeway with protected end lobes and correct site datums. Keep entireKite island underCZ1 and clear full2nm perimeter/mid-beach exclusion; move low-level arena beyond own raised-floor traffic masks. Explicit polygonal sector definition for exact verification."}
```

</details>

<details>
<summary>447. outer_islands/OI4/coast_polygon</summary>

```json
{"phase":"1A.2","item":"outer_islands/OI4/coast_polygon","old":[[9.059537111430712,14.236634124656932],[9.001449956205997,14.149255568037168],[8.926227714576218,14.07301142906873],[8.8351574599512,14.009206266108237],[8.729797429637024,13.958931803184075],[8.611950362963686,13.923048250295594],[8.483632655915343,13.902169584981511],[8.347039860035986,13.896653046994016],[8.204509115933787,13.906593025827545],[8.058479164157319,13.931819445688374],[7.911448617668634,13.971900675538599],[7.7659332098827205,14.026150914422868],[7.624422749771287,14.09364192571286],[7.4893385205409535,14.173218919493362],[7.362991850806274,14.263520311337878],[7.247544567116325,14.363001019395673],[7.144972004503074,14.469958901170616],[7.057029207951322,14.582563877651209],[6.985220903092417,14.69888924646987],[6.930775749931521,14.816944648314632],[6.8946253201342635,14.934710122528784],[6.877388157577268,15.050170669197769],[6.879359194890931,15.161350726356366],[6.900504707080339,15.266347972401656],[6.940462888569288,15.363365875343069],[6.998550043794001,15.450744431962834],[7.073772285423782,15.526988570931271],[7.1648425400488005,15.590793733891765],[7.270202570362976,15.641068196815926],[7.388049637036315,15.676951749704408],[7.516367344084657,15.69783041501849],[7.652960139964013,15.703346953005985],[7.795490884066214,15.693406974172456],[7.941520835842681,15.668180554311627],[8.088551382331366,15.628099324461402],[8.234066790117279,15.573849085577134],[8.375577250228712,15.506358074287142],[8.510661479459047,15.42678108050664],[8.637008149193726,15.336479688662124],[8.752455432883675,15.23699898060433],[8.855027995496927,15.130041098829386],[8.942970792048678,15.017436122348794],[9.014779096907581,14.901110753530132],[9.069224250068478,14.78305535168537],[9.105374679865736,14.665289877471217],[9.122611842422732,14.549829330802233],[9.120640805109069,14.438649273643637],[9.099495292919661,14.333652027598346]],"new":[[9.059537111430712,14.736634124656932],[9.007577789750602,14.660780346759742],[8.938378532734522,14.595863794354662],[8.853123358855669,14.542995207651611],[8.753271007776318,14.503079182827022],[8.640529980907736,14.476798694132455],[8.516829308477357,14.464603408015801],[8.38428554328678,14.46670198920369],[8.245166545906484,14.48305853039016],[8.101852680952717,14.513393166620622],[7.956796088388952,14.557186863858819],[7.812478726731241,14.613690299803103],[7.671369906049876,14.681936684998751],[7.535884037389474,14.760758304873598],[7.408339321526593,14.848806499658098],[7.2909180839117225,14.944574740327921],[7.18562943447577,15.04642440573323],[7.094274891202115,15.152612819860883],[7.018417555654432,15.26132306950416],[6.95935536787557,15.370695092151493],[6.918098898273558,15.478857502171731],[6.8953540564817395,15.583959610741143],[6.891510013049234,15.684203091642297],[6.906632540624942,15.77787275112423],[6.940462888569288,15.863365875343069],[6.992422210249398,15.93921965324026],[7.061621467265479,16.00413620564534],[7.146876641144329,16.05700479234839],[7.246728992223681,16.09692081717298],[7.3594700190922655,16.123201305867546],[7.483170691522641,16.1353965919842],[7.61571445671322,16.133298010796313],[7.754833454093518,16.11694146960984],[7.8981473190472835,16.08660683337938],[8.043203911611048,16.04281313614118],[8.187521273268759,15.986309700196898],[8.328630093950123,15.91806331500125],[8.464115962610526,15.839241695126404],[8.591660678473406,15.751193500341904],[8.709081916088277,15.655425259672082],[8.81437056552423,15.553575594266771],[8.905725108797885,15.44738718013912],[8.981582444345568,15.338676930495843],[9.040644632124428,15.229304907848508],[9.081901101726443,15.12114249782827],[9.104645943518262,15.016040389258858],[9.108489986950767,14.915796908357706],[9.093367459375056,14.822127248875772]],"reason":"Make Sitka runway an actual waterfront causeway with protected end lobes and correct site datums. Keep entireKite island underCZ1 and clear full2nm perimeter/mid-beach exclusion; move low-level arena beyond own raised-floor traffic masks. Explicit polygonal sector definition for exact verification."}
```

</details>

<details>
<summary>448. outer_navaid_sites/SITE-SITX/centre</summary>

```json
{"phase":"1A.2","item":"outer_navaid_sites/SITE-SITX/centre","old":[-14.75,1.1],"new":[-15.05,1.1],"reason":"Make Sitka runway an actual waterfront causeway with protected end lobes and correct site datums. Keep entireKite island underCZ1 and clear full2nm perimeter/mid-beach exclusion; move low-level arena beyond own raised-floor traffic masks. Explicit polygonal sector definition for exact verification."}
```

</details>

<details>
<summary>449. outer_navaid_sites/SITE-SITX/elevation_ft</summary>

```json
{"phase":"1A.2","item":"outer_navaid_sites/SITE-SITX/elevation_ft","old":35,"new":30,"reason":"Make Sitka runway an actual waterfront causeway with protected end lobes and correct site datums. Keep entireKite island underCZ1 and clear full2nm perimeter/mid-beach exclusion; move low-level arena beyond own raised-floor traffic masks. Explicit polygonal sector definition for exact verification."}
```

</details>

<details>
<summary>450. outer_navaid_sites/SITE-TERN/centre</summary>

```json
{"phase":"1A.2","item":"outer_navaid_sites/SITE-TERN/centre","old":[11.55,2],"new":[11.7,2],"reason":"Make Sitka runway an actual waterfront causeway with protected end lobes and correct site datums. Keep entireKite island underCZ1 and clear full2nm perimeter/mid-beach exclusion; move low-level arena beyond own raised-floor traffic masks. Explicit polygonal sector definition for exact verification."}
```

</details>

<details>
<summary>451. outer_navaid_sites/SITE-NHVN/centre</summary>

```json
{"phase":"1A.2","item":"outer_navaid_sites/SITE-NHVN/centre","old":[0.25,10.3],"new":[0,10.65],"reason":"Make Sitka runway an actual waterfront causeway with protected end lobes and correct site datums. Keep entireKite island underCZ1 and clear full2nm perimeter/mid-beach exclusion; move low-level arena beyond own raised-floor traffic masks. Explicit polygonal sector definition for exact verification."}
```

</details>

<details>
<summary>452. outer_navaid_sites/SITE-KITE/centre</summary>

```json
{"phase":"1A.2","item":"outer_navaid_sites/SITE-KITE/centre","old":[8.05,14.8],"new":[8.05,15.3],"reason":"Make Sitka runway an actual waterfront causeway with protected end lobes and correct site datums. Keep entireKite island underCZ1 and clear full2nm perimeter/mid-beach exclusion; move low-level arena beyond own raised-floor traffic masks. Explicit polygonal sector definition for exact verification."}
```

</details>

<details>
<summary>453. airports/KITE/runways</summary>

```json
{"phase":"1A.2","item":"airports/KITE/runways","old":[{"id":"11/29","centre":[8,14.8],"heading_true_deg":118,"length_m":750,"width_m":24,"surface":"gravel","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":180}}],"new":[{"id":"11/29","centre":[8,15.3],"heading_true_deg":118,"length_m":750,"width_m":24,"surface":"gravel","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":180}}],"reason":"Make Sitka runway an actual waterfront causeway with protected end lobes and correct site datums. Keep entireKite island underCZ1 and clear full2nm perimeter/mid-beach exclusion; move low-level arena beyond own raised-floor traffic masks. Explicit polygonal sector definition for exact verification."}
```

</details>

<details>
<summary>454. islets/CZ1-IS1/centre</summary>

```json
{"phase":"1A.2","item":"islets/CZ1-IS1/centre","old":[8.8,15.6],"new":[8.8,16.0],"reason":"Make Sitka runway an actual waterfront causeway with protected end lobes and correct site datums. Keep entireKite island underCZ1 and clear full2nm perimeter/mid-beach exclusion; move low-level arena beyond own raised-floor traffic masks. Explicit polygonal sector definition for exact verification."}
```

</details>

<details>
<summary>455. islets/CZ1-IS2/centre</summary>

```json
{"phase":"1A.2","item":"islets/CZ1-IS2/centre","old":[9.8,15.6],"new":[9.8,16.0],"reason":"Make Sitka runway an actual waterfront causeway with protected end lobes and correct site datums. Keep entireKite island underCZ1 and clear full2nm perimeter/mid-beach exclusion; move low-level arena beyond own raised-floor traffic masks. Explicit polygonal sector definition for exact verification."}
```

</details>

<details>
<summary>456. islets/CZ1-IS3/centre</summary>

```json
{"phase":"1A.2","item":"islets/CZ1-IS3/centre","old":[9.3,16.4],"new":[9.3,16.8],"reason":"Make Sitka runway an actual waterfront causeway with protected end lobes and correct site datums. Keep entireKite island underCZ1 and clear full2nm perimeter/mid-beach exclusion; move low-level arena beyond own raised-floor traffic masks. Explicit polygonal sector definition for exact verification."}
```

</details>

<details>
<summary>457. islets/CZ1-IS4/centre</summary>

```json
{"phase":"1A.2","item":"islets/CZ1-IS4/centre","old":[8.5,15.0],"new":[8.5,15.4],"reason":"Make Sitka runway an actual waterfront causeway with protected end lobes and correct site datums. Keep entireKite island underCZ1 and clear full2nm perimeter/mid-beach exclusion; move low-level arena beyond own raised-floor traffic masks. Explicit polygonal sector definition for exact verification."}
```

</details>

<details>
<summary>458. islets/CZ1-IS5/centre</summary>

```json
{"phase":"1A.2","item":"islets/CZ1-IS5/centre","old":[10.15,16.25],"new":[10.05,16.35],"reason":"Make Sitka runway an actual waterfront causeway with protected end lobes and correct site datums. Keep entireKite island underCZ1 and clear full2nm perimeter/mid-beach exclusion; move low-level arena beyond own raised-floor traffic masks. Explicit polygonal sector definition for exact verification."}
```

</details>

<details>
<summary>459. islets/CZ1-ARCH-W/centre</summary>

```json
{"phase":"1A.2","item":"islets/CZ1-ARCH-W/centre","old":[9.725,16.0],"new":[9.725,16.4],"reason":"Make Sitka runway an actual waterfront causeway with protected end lobes and correct site datums. Keep entireKite island underCZ1 and clear full2nm perimeter/mid-beach exclusion; move low-level arena beyond own raised-floor traffic masks. Explicit polygonal sector definition for exact verification."}
```

</details>

<details>
<summary>460. islets/CZ1-ARCH-E/centre</summary>

```json
{"phase":"1A.2","item":"islets/CZ1-ARCH-E/centre","old":[9.875,16.0],"new":[9.875,16.4],"reason":"Make Sitka runway an actual waterfront causeway with protected end lobes and correct site datums. Keep entireKite island underCZ1 and clear full2nm perimeter/mid-beach exclusion; move low-level arena beyond own raised-floor traffic masks. Explicit polygonal sector definition for exact verification."}
```

</details>

<details>
<summary>461. combat_zones/CZ1/gunnery_targets</summary>

```json
{"phase":"1A.2","item":"combat_zones/CZ1/gunnery_targets","old":[{"id":"CZ1-GT1","centre":[8.8,16.0],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170},{"id":"CZ1-GT2","centre":[9.8,15.6],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170},{"id":"CZ1-GT3","centre":[9.3,16.4],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170}],"new":[{"id":"CZ1-GT1","centre":[8.8,16.4],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170},{"id":"CZ1-GT2","centre":[9.8,16.0],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170},{"id":"CZ1-GT3","centre":[9.3,16.8],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170}],"reason":"Make Sitka runway an actual waterfront causeway with protected end lobes and correct site datums. Keep entireKite island underCZ1 and clear full2nm perimeter/mid-beach exclusion; move low-level arena beyond own raised-floor traffic masks. Explicit polygonal sector definition for exact verification."}
```

</details>

<details>
<summary>462. combat_zones/CZ1/drone_orbit</summary>

```json
{"phase":"1A.2","item":"combat_zones/CZ1/drone_orbit","old":{"centre_start":"CZ1","count":3,"radius_m_target":150,"altitude_ft_target":900,"owner":"Claude opponent/AI logic","centre":[9.3,15.6]},"new":{"centre_start":"CZ1","count":3,"radius_m_target":150,"altitude_ft_target":900,"owner":"Claude opponent/AI logic","centre":[9.3,16.0]},"reason":"Make Sitka runway an actual waterfront causeway with protected end lobes and correct site datums. Keep entireKite island underCZ1 and clear full2nm perimeter/mid-beach exclusion; move low-level arena beyond own raised-floor traffic masks. Explicit polygonal sector definition for exact verification."}
```

</details>

<details>
<summary>463. combat_zones/CZ1/combat_features</summary>

```json
{"phase":"1A.2","item":"combat_zones/CZ1/combat_features","old":[{"id":"CZ1-CHANNEL","type":"island cluster and channel","centre":[9.3,15.6],"channel_width_m":300,"channel_length_m":1100,"water_msl_ft":0,"flight_half_width_m":40,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"terrain":"five islands with smooth ridges/sea stacks, two 300m channels; hide-and-seek and strafe route"},{"id":"CZ1-BRIDGE","type":"bridge","centre":[9.3,15.6],"heading_true_deg":90,"span_m":300,"deck_width_m":14,"under_clearance_m":120,"under_base_msl_ft":0,"deck_thickness_m":4,"flight_opening_width_m":240,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids bridge deck/piers; no heightfield bridge deck"},{"id":"CZ1-ARCH","type":"rock arch","centre":[9.8,16.0],"axis_true_deg":0,"opening_width_m":100,"opening_height_m":110,"base_msl_ft":60,"rock_top_msl_ft":460,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids overhang/opening; footprint pedestals only in shared heightfield"}],"new":[{"id":"CZ1-CHANNEL","type":"island cluster and channel","centre":[9.3,16.0],"channel_width_m":300,"channel_length_m":1100,"water_msl_ft":0,"flight_half_width_m":40,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"terrain":"five islands with smooth ridges/sea stacks, two 300m channels; hide-and-seek and strafe route"},{"id":"CZ1-BRIDGE","type":"bridge","centre":[9.3,16.0],"heading_true_deg":90,"span_m":300,"deck_width_m":14,"under_clearance_m":120,"under_base_msl_ft":0,"deck_thickness_m":4,"flight_opening_width_m":240,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids bridge deck/piers; no heightfield bridge deck"},{"id":"CZ1-ARCH","type":"rock arch","centre":[9.8,16.4],"axis_true_deg":0,"opening_width_m":100,"opening_height_m":110,"base_msl_ft":60,"rock_top_msl_ft":460,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids overhang/opening; footprint pedestals only in shared heightfield"}],"reason":"Make Sitka runway an actual waterfront causeway with protected end lobes and correct site datums. Keep entireKite island underCZ1 and clear full2nm perimeter/mid-beach exclusion; move low-level arena beyond own raised-floor traffic masks. Explicit polygonal sector definition for exact verification."}
```

</details>

<details>
<summary>464. combat_zones/CZ1/boundary_discretization</summary>

```json
{"phase":"1A.2","item":"combat_zones/CZ1/boundary_discretization","old":null,"new":"100 convex angular quadrilaterals; outerarc inscribed, inner radius inflated by1/cos(half angularstep). Polygonal review domain is authoritative, sector radii are chart targets. Maximum radial arc difference<0.12m. Exclusion policy constructs exact active polygon union.","reason":"Make Sitka runway an actual waterfront causeway with protected end lobes and correct site datums. Keep entireKite island underCZ1 and clear full2nm perimeter/mid-beach exclusion; move low-level arena beyond own raised-floor traffic masks. Explicit polygonal sector definition for exact verification."}
```

</details>

<details>
<summary>465. combat_zones/CZ2/boundary_discretization</summary>

```json
{"phase":"1A.2","item":"combat_zones/CZ2/boundary_discretization","old":null,"new":"100 convex angular quadrilaterals; outerarc inscribed, inner radius inflated by1/cos(half angularstep). Polygonal review domain is authoritative, sector radii are chart targets. Maximum radial arc difference<0.12m. Exclusion policy constructs exact active polygon union.","reason":"Make Sitka runway an actual waterfront causeway with protected end lobes and correct site datums. Keep entireKite island underCZ1 and clear full2nm perimeter/mid-beach exclusion; move low-level arena beyond own raised-floor traffic masks. Explicit polygonal sector definition for exact verification."}
```

</details>

<details>
<summary>466. combat_zones/CZ3/boundary_discretization</summary>

```json
{"phase":"1A.2","item":"combat_zones/CZ3/boundary_discretization","old":null,"new":"100 convex angular quadrilaterals; outerarc inscribed, inner radius inflated by1/cos(half angularstep). Polygonal review domain is authoritative, sector radii are chart targets. Maximum radial arc difference<0.12m. Exclusion policy constructs exact active polygon union.","reason":"Make Sitka runway an actual waterfront causeway with protected end lobes and correct site datums. Keep entireKite island underCZ1 and clear full2nm perimeter/mid-beach exclusion; move low-level arena beyond own raised-floor traffic masks. Explicit polygonal sector definition for exact verification."}
```

</details>

<details>
<summary>467. starts/CZ1/centre</summary>

```json
{"phase":"1A.2","item":"starts/CZ1/centre","old":[9.3,15.6],"new":[9.3,16],"reason":"Make Sitka runway an actual waterfront causeway with protected end lobes and correct site datums. Keep entireKite island underCZ1 and clear full2nm perimeter/mid-beach exclusion; move low-level arena beyond own raised-floor traffic masks. Explicit polygonal sector definition for exact verification."}
```

</details>

<details>
<summary>468. airparks/AP4/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"airparks/AP4/heading_true_deg","old":143,"new":142,"reason":"Final full-area fit repair: remove high pad blends fromV2/RNAV16, thread AP4 betweenhighfields, re-elevate fictionalBullfrog within computed3765–4569ft grade interval, synchronize MiddleFork/W1 route, explicitly checkAP8 floatlane, and fit Drake roof/chamber inside lockedhill with200m south stripshift. Locked lengths/true ICAOdatums/cave dimensions retained."}
```

</details>

<details>
<summary>469. airparks/AP6/centre</summary>

```json
{"phase":"1A.2","item":"airparks/AP6/centre","old":[5.1,3.6],"new":[4.8,4.3],"reason":"Final full-area fit repair: remove high pad blends fromV2/RNAV16, thread AP4 betweenhighfields, re-elevate fictionalBullfrog within computed3765–4569ft grade interval, synchronize MiddleFork/W1 route, explicitly checkAP8 floatlane, and fit Drake roof/chamber inside lockedhill with200m south stripshift. Locked lengths/true ICAOdatums/cave dimensions retained."}
```

</details>

<details>
<summary>470. airparks/AP8/float_pond</summary>

```json
{"phase":"1A.2","item":"airparks/AP8/float_pond","old":{"centre":[-8,3.7],"length_m":600,"width_m":120,"elevation_ft":250},"new":{"centre":[-7.8,3.7],"length_m":600,"width_m":120,"elevation_ft":250,"heading_true_deg":0,"surface":"water","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"water_surface_ft":250,"water_bed_offset_m":3,"note":"600x120m rectangular floatlane plus60m shoulders, grass runway remainsseparate; sameAP8 site, no newstart."},"reason":"Final full-area fit repair: remove high pad blends fromV2/RNAV16, thread AP4 betweenhighfields, re-elevate fictionalBullfrog within computed3765–4569ft grade interval, synchronize MiddleFork/W1 route, explicitly checkAP8 floatlane, and fit Drake roof/chamber inside lockedhill with200m south stripshift. Locked lengths/true ICAOdatums/cave dimensions retained."}
```

</details>

<details>
<summary>471. airparks/AP12/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"airparks/AP12/heading_true_deg","old":93,"new":90,"reason":"Final full-area fit repair: remove high pad blends fromV2/RNAV16, thread AP4 betweenhighfields, re-elevate fictionalBullfrog within computed3765–4569ft grade interval, synchronize MiddleFork/W1 route, explicitly checkAP8 floatlane, and fit Drake roof/chamber inside lockedhill with200m south stripshift. Locked lengths/true ICAOdatums/cave dimensions retained."}
```

</details>

<details>
<summary>472. airparks/AP12/numbers_magnetic</summary>

```json
{"phase":"1A.2","item":"airparks/AP12/numbers_magnetic","old":"9/27","new":"8/26","reason":"Final full-area fit repair: remove high pad blends fromV2/RNAV16, thread AP4 betweenhighfields, re-elevate fictionalBullfrog within computed3765–4569ft grade interval, synchronize MiddleFork/W1 route, explicitly checkAP8 floatlane, and fit Drake roof/chamber inside lockedhill with200m south stripshift. Locked lengths/true ICAOdatums/cave dimensions retained."}
```

</details>

<details>
<summary>473. airparks/AP12/elevation_ft</summary>

```json
{"phase":"1A.2","item":"airparks/AP12/elevation_ft","old":4050,"new":4300,"reason":"Final full-area fit repair: remove high pad blends fromV2/RNAV16, thread AP4 betweenhighfields, re-elevate fictionalBullfrog within computed3765–4569ft grade interval, synchronize MiddleFork/W1 route, explicitly checkAP8 floatlane, and fit Drake roof/chamber inside lockedhill with200m south stripshift. Locked lengths/true ICAOdatums/cave dimensions retained."}
```

</details>

<details>
<summary>474. airports/3U2/runways</summary>

```json
{"phase":"1A.2","item":"airports/3U2/runways","old":[{"id":"19/1","centre":[1.82,-0.86],"heading_true_deg":192,"numbers_magnetic":"19/1","length_ft":3400,"width_ft":150,"surface":"turf","slope_percent":0.5,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":300},"operation":"land 19; depart 1; slope target pending real survey"}],"new":[{"id":"19/1","centre":[1.82,-0.76],"heading_true_deg":192,"numbers_magnetic":"19/1","length_ft":3400,"width_ft":150,"surface":"turf","slope_percent":0.5,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":300},"operation":"land 19; depart 1; slope target pending real survey"}],"reason":"Final full-area fit repair: remove high pad blends fromV2/RNAV16, thread AP4 betweenhighfields, re-elevate fictionalBullfrog within computed3765–4569ft grade interval, synchronize MiddleFork/W1 route, explicitly checkAP8 floatlane, and fit Drake roof/chamber inside lockedhill with200m south stripshift. Locked lengths/true ICAOdatums/cave dimensions retained."}
```

</details>

<details>
<summary>475. airports/DRAKE/runways</summary>

```json
{"phase":"1A.2","item":"airports/DRAKE/runways","old":[{"id":"17","centre":[-4.9,-3.98],"heading_true_deg":180,"numbers_magnetic":"17","length_ft":2992,"width_ft":100,"surface":"grass","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":300},"operation":"one-way land 17; hidden chamber; approach-surface exception unresolved"}],"new":[{"id":"17","centre":[-4.9,-4.18],"heading_true_deg":180,"numbers_magnetic":"17","length_ft":2992,"width_ft":100,"surface":"grass","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":300},"operation":"one-way land 17; hidden chamber; approach-surface exception unresolved"}],"reason":"Final full-area fit repair: remove high pad blends fromV2/RNAV16, thread AP4 betweenhighfields, re-elevate fictionalBullfrog within computed3765–4569ft grade interval, synchronize MiddleFork/W1 route, explicitly checkAP8 floatlane, and fit Drake roof/chamber inside lockedhill with200m south stripshift. Locked lengths/true ICAOdatums/cave dimensions retained."}
```

</details>

<details>
<summary>476. backcountry_strips/Sulphur Creek/centre</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Sulphur Creek/centre","old":[4,0],"new":[4.2,0.1],"reason":"Final full-area fit repair: remove high pad blends fromV2/RNAV16, thread AP4 betweenhighfields, re-elevate fictionalBullfrog within computed3765–4569ft grade interval, synchronize MiddleFork/W1 route, explicitly checkAP8 floatlane, and fit Drake roof/chamber inside lockedhill with200m south stripshift. Locked lengths/true ICAOdatums/cave dimensions retained."}
```

</details>

<details>
<summary>477. backcountry_strips/Dewey Moore/centre</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Dewey Moore/centre","old":[5.3,0.8],"new":[5.3,0.65],"reason":"Final full-area fit repair: remove high pad blends fromV2/RNAV16, thread AP4 betweenhighfields, re-elevate fictionalBullfrog within computed3765–4569ft grade interval, synchronize MiddleFork/W1 route, explicitly checkAP8 floatlane, and fit Drake roof/chamber inside lockedhill with200m south stripshift. Locked lengths/true ICAOdatums/cave dimensions retained."}
```

</details>

<details>
<summary>478. backcountry_strips/Mackay Bar/centre</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Mackay Bar/centre","old":[5.3,1.8],"new":[5.3,1.5],"reason":"Final full-area fit repair: remove high pad blends fromV2/RNAV16, thread AP4 betweenhighfields, re-elevate fictionalBullfrog within computed3765–4569ft grade interval, synchronize MiddleFork/W1 route, explicitly checkAP8 floatlane, and fit Drake roof/chamber inside lockedhill with200m south stripshift. Locked lengths/true ICAOdatums/cave dimensions retained."}
```

</details>

<details>
<summary>479. backcountry_strips/Mackay Bar/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Mackay Bar/heading_true_deg","old":351,"new":330,"reason":"Final full-area fit repair: remove high pad blends fromV2/RNAV16, thread AP4 betweenhighfields, re-elevate fictionalBullfrog within computed3765–4569ft grade interval, synchronize MiddleFork/W1 route, explicitly checkAP8 floatlane, and fit Drake roof/chamber inside lockedhill with200m south stripshift. Locked lengths/true ICAOdatums/cave dimensions retained."}
```

</details>

<details>
<summary>480. canyons/C1/path</summary>

```json
{"phase":"1A.2","item":"canyons/C1/path","old":[[0.6,1.7],[1.82,-0.86],[2.5,-1.8]],"new":[[1.05,2.1],[1.85,0.25],[1.82,-0.76],[0.7,-1.8]],"reason":"Final full-area fit repair: remove high pad blends fromV2/RNAV16, thread AP4 betweenhighfields, re-elevate fictionalBullfrog within computed3765–4569ft grade interval, synchronize MiddleFork/W1 route, explicitly checkAP8 floatlane, and fit Drake roof/chamber inside lockedhill with200m south stripshift. Locked lengths/true ICAOdatums/cave dimensions retained."}
```

</details>

<details>
<summary>481. canyons/C2/path</summary>

```json
{"phase":"1A.2","item":"canyons/C2/path","old":[[2.5,-1.8],[3,-0.2],[4,1.2],[5,1.8]],"new":[[0.7,-1.8],[2.7,-0.4],[3.1,0.55],[3.8,1.7],[4.5,1.25],[5.3,1.5],[5.3,0.65],[4.2,0.1],[4.6,-0.65]],"reason":"Final full-area fit repair: remove high pad blends fromV2/RNAV16, thread AP4 betweenhighfields, re-elevate fictionalBullfrog within computed3765–4569ft grade interval, synchronize MiddleFork/W1 route, explicitly checkAP8 floatlane, and fit Drake roof/chamber inside lockedhill with200m south stripshift. Locked lengths/true ICAOdatums/cave dimensions retained."}
```

</details>

<details>
<summary>482. lesson_terrain/L1/safe_path</summary>

```json
{"phase":"1A.2","item":"lesson_terrain/L1/safe_path","old":[[-0.88,2.78],[0.6,1.7],[1.82,-0.86]],"new":[[-0.88,2.78],[0.6,1.7],[1.85,0.25],[1.82,-0.76]],"reason":"Final full-area fit repair: remove high pad blends fromV2/RNAV16, thread AP4 betweenhighfields, re-elevate fictionalBullfrog within computed3765–4569ft grade interval, synchronize MiddleFork/W1 route, explicitly checkAP8 floatlane, and fit Drake roof/chamber inside lockedhill with200m south stripshift. Locked lengths/true ICAOdatums/cave dimensions retained."}
```

</details>

<details>
<summary>483. features/F1/exterior_strip_m</summary>

```json
{"phase":"1A.2","item":"features/F1/exterior_strip_m","old":152,"new":152.4,"reason":"Final full-area fit repair: remove high pad blends fromV2/RNAV16, thread AP4 betweenhighfields, re-elevate fictionalBullfrog within computed3765–4569ft grade interval, synchronize MiddleFork/W1 route, explicitly checkAP8 floatlane, and fit Drake roof/chamber inside lockedhill with200m south stripshift. Locked lengths/true ICAOdatums/cave dimensions retained."}
```

</details>

<details>
<summary>484. features/F1/interior_strip_m</summary>

```json
{"phase":"1A.2","item":"features/F1/interior_strip_m","old":760,"new":759.5616000000001,"reason":"Final full-area fit repair: remove high pad blends fromV2/RNAV16, thread AP4 betweenhighfields, re-elevate fictionalBullfrog within computed3765–4569ft grade interval, synchronize MiddleFork/W1 route, explicitly checkAP8 floatlane, and fit Drake roof/chamber inside lockedhill with200m south stripshift. Locked lengths/true ICAOdatums/cave dimensions retained."}
```

</details>

<details>
<summary>485. features/F1/shell_geometry</summary>

```json
{"phase":"1A.2","item":"features/F1/shell_geometry","old":null,"new":{"mouth":[-4.9,-3.8764192],"chamber_centre":[-4.9,-4.2864192],"chamber_size_m":[820,420,140],"floor_msl_m":1432.5600000000002,"ceiling_msl_m":1572.5600000000002,"minimum_roof_msl_m":1632.5600000000002,"minimum_rock_cover_m":60,"hill_centre":[-4.9,-4.5],"hill_radius_m":700,"crown_centre":[-4.9,-4.3],"crown_msl_ft":5750,"crown_radius_m":250,"roof_blend_m":20,"opening_m":[60,26],"operational_rule":"Runway17 only; reversefinal meets closedfarwall/roof and remainsFAIL until Ty approves explicitone-way exemption. No secondentrance/tunnel added.","representation":"Analytic closed3D shell + floor deck beneath shared upperterrain. Full footprint roof-cover proof; runtime collider/height-aware contact routing deferredPhase1B."},"reason":"Final full-area fit repair: remove high pad blends fromV2/RNAV16, thread AP4 betweenhighfields, re-elevate fictionalBullfrog within computed3765–4569ft grade interval, synchronize MiddleFork/W1 route, explicitly checkAP8 floatlane, and fit Drake roof/chamber inside lockedhill with200m south stripshift. Locked lengths/true ICAOdatums/cave dimensions retained."}
```

</details>

<details>
<summary>486. starts/3U2:OnTheRunway/centre</summary>

```json
{"phase":"1A.2","item":"starts/3U2:OnTheRunway/centre","old":[1.911099,-0.431415],"new":[1.911099,-0.331415],"reason":"Final full-area fit repair: remove high pad blends fromV2/RNAV16, thread AP4 betweenhighfields, re-elevate fictionalBullfrog within computed3765–4569ft grade interval, synchronize MiddleFork/W1 route, explicitly checkAP8 floatlane, and fit Drake roof/chamber inside lockedhill with200m south stripshift. Locked lengths/true ICAOdatums/cave dimensions retained."}
```

</details>

<details>
<summary>487. starts/3U2:OnFinal/centre</summary>

```json
{"phase":"1A.2","item":"starts/3U2:OnFinal/centre","old":[2.094061,0.429355],"new":[2.094061,0.529355],"reason":"Final full-area fit repair: remove high pad blends fromV2/RNAV16, thread AP4 betweenhighfields, re-elevate fictionalBullfrog within computed3765–4569ft grade interval, synchronize MiddleFork/W1 route, explicitly checkAP8 floatlane, and fit Drake roof/chamber inside lockedhill with200m south stripshift. Locked lengths/true ICAOdatums/cave dimensions retained."}
```

</details>

<details>
<summary>488. starts/3U2:InTheAir/centre</summary>

```json
{"phase":"1A.2","item":"starts/3U2:InTheAir/centre","old":[2.135643,0.624985],"new":[2.135643,0.724985],"reason":"Final full-area fit repair: remove high pad blends fromV2/RNAV16, thread AP4 betweenhighfields, re-elevate fictionalBullfrog within computed3765–4569ft grade interval, synchronize MiddleFork/W1 route, explicitly checkAP8 floatlane, and fit Drake roof/chamber inside lockedhill with200m south stripshift. Locked lengths/true ICAOdatums/cave dimensions retained."}
```

</details>

<details>
<summary>489. height_field/cavities</summary>

```json
{"phase":"1A.2","item":"height_field/cavities","old":{"DRAKE":"height field is upper rock envelope; only exterior152m uses ground pad; interior floor is separate height-aware deck/shell at4700ft; never flatten full hill to cave floor","collision_status":"Phase1B requires below-roof ground/contact routing; current review bake cannot certify cave interiors"},"new":{"DRAKE":"Full820x420x140m analytic chamber under1400m hill at4700ft floor; minimum60m cover. Only exterior500ft runway is upperterrain;2992ft exacttotal preserved, interior759.5616m separate deck. CrownDRK stays(-4.9,-4.3),5750ft.","collision_status":"Phase1B requires below-roof ground/contact routing; current review bake cannot certify cave interiors"},"reason":"Continuous hazardknoll, explicitfloatlane/Drake shell; minimum60m roofcover and A2 towerobstruction budget proved numerically."}
```

</details>

<details>
<summary>490. height_field/hazard_benches</summary>

```json
{"phase":"1A.2","item":"height_field/hazard_benches","old":null,"new":[{"id":"T4 foundation knoll","centre":[-6.9,-2.9],"elevation_ft":2700,"core_radius_m":200,"blend_m":150,"reason":"50m tower top2864ft MSL + fullguy footprint belowA2 budget3000ft; no field datum changes"}],"reason":"Continuous hazardknoll, explicitfloatlane/Drake shell; minimum60m roofcover and A2 towerobstruction budget proved numerically."}
```

</details>

<details>
<summary>491. aerobatic_boxes/AB1/floor_msl_ft_target</summary>

```json
{"phase":"1A.2","item":"aerobatic_boxes/AB1/floor_msl_ft_target","old":328,"new":328.084,"reason":"Complete raft count/positions and literal extended shorejudges geometry; exactIAC100–1100m conversion; keepCZ3 fullflighttube andgroundtarget outside raisedfloor; relocate racefromcongestedmainisland tooffshore1km-clear course."}
```

</details>

<details>
<summary>492. aerobatic_boxes/AB1/ceiling_msl_ft_target</summary>

```json
{"phase":"1A.2","item":"aerobatic_boxes/AB1/ceiling_msl_ft_target","old":3609,"new":3608.924,"reason":"Complete raft count/positions and literal extended shorejudges geometry; exactIAC100–1100m conversion; keepCZ3 fullflighttube andgroundtarget outside raisedfloor; relocate racefromcongestedmainisland tooffshore1km-clear course."}
```

</details>

<details>
<summary>493. aerobatic_boxes/AB1/judges_line</summary>

```json
{"phase":"1A.2","item":"aerobatic_boxes/AB1/judges_line","old":{"centre":[6.9,6.949999999999999],"length_m":1000,"orientation_true_deg":90,"marker":"moored judge-reference raft on line; matching shore reference aligned along airport-facing axis"},"new":{"centre":[6.9,6.95],"length_m":1000,"orientation_true_deg":90,"marker":"moored judge-reference raft on line; matching shore reference aligned along airport-facing axis","shore_extension_path":[[6.9,6.95],[3.1,6.95]]},"reason":"Complete raft count/positions and literal extended shorejudges geometry; exactIAC100–1100m conversion; keepCZ3 fullflighttube andgroundtarget outside raisedfloor; relocate racefromcongestedmainisland tooffshore1km-clear course."}
```

</details>

<details>
<summary>494. aerobatic_boxes/AB1/shore_reference_marker</summary>

```json
{"phase":"1A.2","item":"aerobatic_boxes/AB1/shore_reference_marker","old":{"centre":[6.9,6.0],"meaning":"shore sighting reference aligned to offshore judges line; actual line remains 150 m beyond square"},"new":{"centre":[3.1,6.95],"meaning":"Dryshore marker on actual extended judge-line axis; judging line remains150m outside airport-facing side."},"reason":"Complete raft count/positions and literal extended shorejudges geometry; exactIAC100–1100m conversion; keepCZ3 fullflighttube andgroundtarget outside raisedfloor; relocate racefromcongestedmainisland tooffshore1km-clear course."}
```

</details>

<details>
<summary>495. aerobatic_boxes/AB1/judge_raft</summary>

```json
{"phase":"1A.2","item":"aerobatic_boxes/AB1/judge_raft","old":null,"new":{"id":"AB1-JR","centre":[6.9,6.95],"size_m":[12,8],"deck_msl_m":0.5,"moored":true,"colour":"white","role":"judges reference raft, separatefrom9 standardcross/L/T markerrafts"},"reason":"Complete raft count/positions and literal extended shorejudges geometry; exactIAC100–1100m conversion; keepCZ3 fullflighttube andgroundtarget outside raisedfloor; relocate racefromcongestedmainisland tooffshore1km-clear course."}
```

</details>

<details>
<summary>496. aerobatic_boxes/AB1/marker_raft_count</summary>

```json
{"phase":"1A.2","item":"aerobatic_boxes/AB1/marker_raft_count","old":null,"new":9,"reason":"Complete raft count/positions and literal extended shorejudges geometry; exactIAC100–1100m conversion; keepCZ3 fullflighttube andgroundtarget outside raisedfloor; relocate racefromcongestedmainisland tooffshore1km-clear course."}
```

</details>

<details>
<summary>497. aerobatic_boxes/AB1/judge_raft_count</summary>

```json
{"phase":"1A.2","item":"aerobatic_boxes/AB1/judge_raft_count","old":null,"new":1,"reason":"Complete raft count/positions and literal extended shorejudges geometry; exactIAC100–1100m conversion; keepCZ3 fullflighttube andgroundtarget outside raisedfloor; relocate racefromcongestedmainisland tooffshore1km-clear course."}
```

</details>

<details>
<summary>498. aerobatic_boxes/AB1/total_raft_count</summary>

```json
{"phase":"1A.2","item":"aerobatic_boxes/AB1/total_raft_count","old":null,"new":10,"reason":"Complete raft count/positions and literal extended shorejudges geometry; exactIAC100–1100m conversion; keepCZ3 fullflighttube andgroundtarget outside raisedfloor; relocate racefromcongestedmainisland tooffshore1km-clear course."}
```

</details>

<details>
<summary>499. aerobatic_boxes/AB2/centre</summary>

```json
{"phase":"1A.2","item":"aerobatic_boxes/AB2/centre","old":[-12.3,-6.3],"new":[-12.2,-6.3],"reason":"Complete raft count/positions and literal extended shorejudges geometry; exactIAC100–1100m conversion; keepCZ3 fullflighttube andgroundtarget outside raisedfloor; relocate racefromcongestedmainisland tooffshore1km-clear course."}
```

</details>

<details>
<summary>500. aerobatic_boxes/AB2/floor_msl_ft_target</summary>

```json
{"phase":"1A.2","item":"aerobatic_boxes/AB2/floor_msl_ft_target","old":328,"new":328.084,"reason":"Complete raft count/positions and literal extended shorejudges geometry; exactIAC100–1100m conversion; keepCZ3 fullflighttube andgroundtarget outside raisedfloor; relocate racefromcongestedmainisland tooffshore1km-clear course."}
```

</details>

<details>
<summary>501. aerobatic_boxes/AB2/ceiling_msl_ft_target</summary>

```json
{"phase":"1A.2","item":"aerobatic_boxes/AB2/ceiling_msl_ft_target","old":3609,"new":3608.924,"reason":"Complete raft count/positions and literal extended shorejudges geometry; exactIAC100–1100m conversion; keepCZ3 fullflighttube andgroundtarget outside raisedfloor; relocate racefromcongestedmainisland tooffshore1km-clear course."}
```

</details>

<details>
<summary>502. aerobatic_boxes/AB2/rafts</summary>

```json
{"phase":"1A.2","item":"aerobatic_boxes/AB2/rafts","old":[{"id":"AB2-R1","shape":"cross","centre":[-12.3,-6.3],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R2","shape":"L","centre":[-12.8,-6.8],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R3","shape":"L","centre":[-12.8,-5.8],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R4","shape":"L","centre":[-11.8,-6.8],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R5","shape":"L","centre":[-11.8,-5.8],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R6","shape":"T","centre":[-12.3,-5.8],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R7","shape":"T","centre":[-12.3,-6.8],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R8","shape":"T","centre":[-11.8,-6.3],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R9","shape":"T","centre":[-12.8,-6.3],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"}],"new":[{"id":"AB2-R1","shape":"cross","centre":[-12.2,-6.3],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R2","shape":"L","centre":[-12.7,-6.8],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R3","shape":"L","centre":[-12.7,-5.8],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R4","shape":"L","centre":[-11.7,-6.8],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R5","shape":"L","centre":[-11.7,-5.8],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R6","shape":"T","centre":[-12.2,-5.8],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R7","shape":"T","centre":[-12.2,-6.8],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R8","shape":"T","centre":[-11.7,-6.3],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R9","shape":"T","centre":[-12.7,-6.3],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"}],"reason":"Complete raft count/positions and literal extended shorejudges geometry; exactIAC100–1100m conversion; keepCZ3 fullflighttube andgroundtarget outside raisedfloor; relocate racefromcongestedmainisland tooffshore1km-clear course."}
```

</details>

<details>
<summary>503. aerobatic_boxes/AB2/corner_buoys</summary>

```json
{"phase":"1A.2","item":"aerobatic_boxes/AB2/corner_buoys","old":[{"centre":[-12.8,-6.8],"colour":"orange","moored":true},{"centre":[-12.8,-5.8],"colour":"orange","moored":true},{"centre":[-11.8,-6.8],"colour":"orange","moored":true},{"centre":[-11.8,-5.8],"colour":"orange","moored":true}],"new":[{"centre":[-12.7,-6.8],"colour":"orange","moored":true},{"centre":[-12.7,-5.8],"colour":"orange","moored":true},{"centre":[-11.7,-6.8],"colour":"orange","moored":true},{"centre":[-11.7,-5.8],"colour":"orange","moored":true}],"reason":"Complete raft count/positions and literal extended shorejudges geometry; exactIAC100–1100m conversion; keepCZ3 fullflighttube andgroundtarget outside raisedfloor; relocate racefromcongestedmainisland tooffshore1km-clear course."}
```

</details>

<details>
<summary>504. aerobatic_boxes/AB2/judges_line</summary>

```json
{"phase":"1A.2","item":"aerobatic_boxes/AB2/judges_line","old":{"centre":[-11.65,-6.3],"length_m":1000,"orientation_true_deg":0,"marker":"moored judge-reference raft on line; matching shore reference aligned along airport-facing axis"},"new":{"centre":[-11.55,-6.3],"length_m":1000,"orientation_true_deg":0,"marker":"moored judge-reference raft on line; matching shore reference aligned along airport-facing axis","shore_extension_path":[[-11.55,-6.3],[-11.55,-5.65]]},"reason":"Complete raft count/positions and literal extended shorejudges geometry; exactIAC100–1100m conversion; keepCZ3 fullflighttube andgroundtarget outside raisedfloor; relocate racefromcongestedmainisland tooffshore1km-clear course."}
```

</details>

<details>
<summary>505. aerobatic_boxes/AB2/shore_reference_marker</summary>

```json
{"phase":"1A.2","item":"aerobatic_boxes/AB2/shore_reference_marker","old":{"centre":[-9,-6.3],"meaning":"shore sighting reference aligned to offshore judges line; actual line remains 150 m beyond square"},"new":{"centre":[-11.55,-5.65],"meaning":"Dryshore marker on actual extended judge-line axis; judging line remains150m outside airport-facing side."},"reason":"Complete raft count/positions and literal extended shorejudges geometry; exactIAC100–1100m conversion; keepCZ3 fullflighttube andgroundtarget outside raisedfloor; relocate racefromcongestedmainisland tooffshore1km-clear course."}
```

</details>

<details>
<summary>506. aerobatic_boxes/AB2/judge_raft</summary>

```json
{"phase":"1A.2","item":"aerobatic_boxes/AB2/judge_raft","old":null,"new":{"id":"AB2-JR","centre":[-11.55,-6.3],"size_m":[12,8],"deck_msl_m":0.5,"moored":true,"colour":"white","role":"judges reference raft, separatefrom9 standardcross/L/T markerrafts"},"reason":"Complete raft count/positions and literal extended shorejudges geometry; exactIAC100–1100m conversion; keepCZ3 fullflighttube andgroundtarget outside raisedfloor; relocate racefromcongestedmainisland tooffshore1km-clear course."}
```

</details>

<details>
<summary>507. aerobatic_boxes/AB2/marker_raft_count</summary>

```json
{"phase":"1A.2","item":"aerobatic_boxes/AB2/marker_raft_count","old":null,"new":9,"reason":"Complete raft count/positions and literal extended shorejudges geometry; exactIAC100–1100m conversion; keepCZ3 fullflighttube andgroundtarget outside raisedfloor; relocate racefromcongestedmainisland tooffshore1km-clear course."}
```

</details>

<details>
<summary>508. aerobatic_boxes/AB2/judge_raft_count</summary>

```json
{"phase":"1A.2","item":"aerobatic_boxes/AB2/judge_raft_count","old":null,"new":1,"reason":"Complete raft count/positions and literal extended shorejudges geometry; exactIAC100–1100m conversion; keepCZ3 fullflighttube andgroundtarget outside raisedfloor; relocate racefromcongestedmainisland tooffshore1km-clear course."}
```

</details>

<details>
<summary>509. aerobatic_boxes/AB2/total_raft_count</summary>

```json
{"phase":"1A.2","item":"aerobatic_boxes/AB2/total_raft_count","old":null,"new":10,"reason":"Complete raft count/positions and literal extended shorejudges geometry; exactIAC100–1100m conversion; keepCZ3 fullflighttube andgroundtarget outside raisedfloor; relocate racefromcongestedmainisland tooffshore1km-clear course."}
```

</details>

<details>
<summary>510. aerobatic_boxes/AB3/floor_msl_ft_target</summary>

```json
{"phase":"1A.2","item":"aerobatic_boxes/AB3/floor_msl_ft_target","old":5328,"new":5328.084,"reason":"Complete raft count/positions and literal extended shorejudges geometry; exactIAC100–1100m conversion; keepCZ3 fullflighttube andgroundtarget outside raisedfloor; relocate racefromcongestedmainisland tooffshore1km-clear course."}
```

</details>

<details>
<summary>511. aerobatic_boxes/AB3/ceiling_msl_ft_target</summary>

```json
{"phase":"1A.2","item":"aerobatic_boxes/AB3/ceiling_msl_ft_target","old":8609,"new":8608.924,"reason":"Complete raft count/positions and literal extended shorejudges geometry; exactIAC100–1100m conversion; keepCZ3 fullflighttube andgroundtarget outside raisedfloor; relocate racefromcongestedmainisland tooffshore1km-clear course."}
```

</details>

<details>
<summary>512. combat_zones/CZ3/gunnery_targets</summary>

```json
{"phase":"1A.2","item":"combat_zones/CZ3/gunnery_targets","old":[{"id":"CZ3-GT1","centre":[-8.4,17.0],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170},{"id":"CZ3-GT2","centre":[-7.4,17.0],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170},{"id":"CZ3-GT3","centre":[-7.9,17.4],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170}],"new":[{"id":"CZ3-GT1","centre":[-8.4,17.0],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170},{"id":"CZ3-GT2","centre":[-7.4,17.15],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170},{"id":"CZ3-GT3","centre":[-7.9,17.4],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":170}],"reason":"Complete raft count/positions and literal extended shorejudges geometry; exactIAC100–1100m conversion; keepCZ3 fullflighttube andgroundtarget outside raisedfloor; relocate racefromcongestedmainisland tooffshore1km-clear course."}
```

</details>

<details>
<summary>513. combat_zones/CZ3/combat_features</summary>

```json
{"phase":"1A.2","item":"combat_zones/CZ3/combat_features","old":[{"id":"CZ3-CHANNEL","type":"island cluster and channel","centre":[-7.9,17.0],"channel_width_m":300,"channel_length_m":600,"water_msl_ft":0,"flight_half_width_m":40,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"terrain":"five islands with smooth ridges/sea stacks, two 300m channels; hide-and-seek and strafe route"},{"id":"CZ3-BRIDGE","type":"bridge","centre":[-7.9,17.0],"heading_true_deg":90,"span_m":300,"deck_width_m":14,"under_clearance_m":120,"under_base_msl_ft":0,"deck_thickness_m":4,"flight_opening_width_m":240,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids bridge deck/piers; no heightfield bridge deck"},{"id":"CZ3-ARCH","type":"rock arch","centre":[-7.4,17.4],"axis_true_deg":0,"opening_width_m":100,"opening_height_m":110,"base_msl_ft":60,"rock_top_msl_ft":460,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids overhang/opening; footprint pedestals only in shared heightfield"}],"new":[{"id":"CZ3-CHANNEL","type":"island cluster and channel","centre":[-7.9,17.05],"channel_width_m":300,"channel_length_m":600,"water_msl_ft":0,"flight_half_width_m":40,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"terrain":"five islands with smooth ridges/sea stacks, two 300m channels; hide-and-seek and strafe route"},{"id":"CZ3-BRIDGE","type":"bridge","centre":[-7.9,17.0],"heading_true_deg":90,"span_m":300,"deck_width_m":14,"under_clearance_m":120,"under_base_msl_ft":0,"deck_thickness_m":4,"flight_opening_width_m":240,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids bridge deck/piers; no heightfield bridge deck"},{"id":"CZ3-ARCH","type":"rock arch","centre":[-7.4,17.4],"axis_true_deg":0,"opening_width_m":100,"opening_height_m":110,"base_msl_ft":60,"rock_top_msl_ft":460,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids overhang/opening; footprint pedestals only in shared heightfield"}],"reason":"Complete raft count/positions and literal extended shorejudges geometry; exactIAC100–1100m conversion; keepCZ3 fullflighttube andgroundtarget outside raisedfloor; relocate racefromcongestedmainisland tooffshore1km-clear course."}
```

</details>

<details>
<summary>514. features/F1/shell_geometry</summary>

```json
{"phase":"1A.2","item":"features/F1/shell_geometry","old":{"mouth":[-4.9,-3.8764192],"chamber_centre":[-4.9,-4.2864192],"chamber_size_m":[820,420,140],"floor_msl_m":1432.5600000000002,"ceiling_msl_m":1572.5600000000002,"minimum_roof_msl_m":1632.5600000000002,"minimum_rock_cover_m":60,"hill_centre":[-4.9,-4.5],"hill_radius_m":700,"crown_centre":[-4.9,-4.3],"crown_msl_ft":5750,"crown_radius_m":250,"roof_blend_m":20,"opening_m":[60,26],"operational_rule":"Runway17 only; reversefinal meets closedfarwall/roof and remainsFAIL until Ty approves explicitone-way exemption. No secondentrance/tunnel added.","representation":"Analytic closed3D shell + floor deck beneath shared upperterrain. Full footprint roof-cover proof; runtime collider/height-aware contact routing deferredPhase1B."},"new":{"mouth":[-4.9,-3.8764192],"chamber_centre":[-4.9,-4.2864192],"chamber_size_m":[820,420,140],"floor_msl_m":1432.5600000000002,"ceiling_msl_m":1572.5600000000002,"minimum_roof_msl_m":1632.5600000000002,"minimum_rock_cover_m":60,"hill_centre":[-4.9,-4.5],"hill_radius_m":700,"crown_centre":[-4.9,-4.5],"crown_msl_ft":5750,"crown_radius_m":250,"roof_blend_m":20,"opening_m":[60,26],"operational_rule":"Runway17 only; reversefinal meets closedfarwall/roof and remainsFAIL until Ty approves explicitone-way exemption. No secondentrance/tunnel added.","representation":"Analytic closed3D shell + floor deck beneath shared upperterrain. Full footprint roof-cover proof; runtime collider/height-aware contact routing deferredPhase1B.","nav_site":[-4.9,-4.3]},"reason":"Complete raft count/positions and literal extended shorejudges geometry; exactIAC100–1100m conversion; keepCZ3 fullflighttube andgroundtarget outside raisedfloor; relocate racefromcongestedmainisland tooffshore1km-clear course."}
```

</details>

<details>
<summary>515. features/RACE/centre</summary>

```json
{"phase":"1A.2","item":"features/RACE/centre","old":[7,-0.3],"new":[10,-13],"reason":"Complete raft count/positions and literal extended shorejudges geometry; exactIAC100–1100m conversion; keepCZ3 fullflighttube andgroundtarget outside raisedfloor; relocate racefromcongestedmainisland tooffshore1km-clear course."}
```

</details>

<details>
<summary>516. features/RACE/elements</summary>

```json
{"phase":"1A.2","item":"features/RACE/elements","old":[{"sequence":1,"kind":"Gate","old_sim_xy_m":[45,1800],"centre":[6.4675,-0.175],"heading_true_deg":90},{"sequence":2,"kind":"Gate","old_sim_xy_m":[345,1800],"centre":[6.7675,-0.175],"heading_true_deg":90},{"sequence":3,"kind":"PylonOnRight","old_sim_xy_m":[540,1755],"centre":[6.9625,-0.13],"heading_true_deg":90},{"sequence":4,"kind":"PylonOnLeft","old_sim_xy_m":[720,1845],"centre":[7.1425,-0.22],"heading_true_deg":90},{"sequence":5,"kind":"PylonOnRight","old_sim_xy_m":[900,1755],"centre":[7.3225,-0.13],"heading_true_deg":90},{"sequence":6,"kind":"Gate","old_sim_xy_m":[1050,1800],"centre":[7.4725,-0.175],"heading_true_deg":90},{"sequence":7,"kind":"PylonOnRight","old_sim_xy_m":[1110,1920],"centre":[7.5325,-0.295],"heading_true_deg":180},{"sequence":8,"kind":"Gate","old_sim_xy_m":[1020,2050],"centre":[7.4425,-0.425],"heading_true_deg":270},{"sequence":9,"kind":"Gate","old_sim_xy_m":[720,2050],"centre":[7.1425,-0.425],"heading_true_deg":270},{"sequence":10,"kind":"PylonOnLeft","old_sim_xy_m":[540,2095],"centre":[6.9625,-0.47],"heading_true_deg":270},{"sequence":11,"kind":"PylonOnRight","old_sim_xy_m":[360,2005],"centre":[6.7825,-0.38],"heading_true_deg":270},{"sequence":12,"kind":"Gate","old_sim_xy_m":[120,2050],"centre":[6.5425,-0.425],"heading_true_deg":270}],"new":[{"sequence":1,"kind":"Gate","old_sim_xy_m":[45,1800],"centre":[9.4675,-12.875],"heading_true_deg":90},{"sequence":2,"kind":"Gate","old_sim_xy_m":[345,1800],"centre":[9.7675,-12.875],"heading_true_deg":90},{"sequence":3,"kind":"PylonOnRight","old_sim_xy_m":[540,1755],"centre":[9.9625,-12.83],"heading_true_deg":90},{"sequence":4,"kind":"PylonOnLeft","old_sim_xy_m":[720,1845],"centre":[10.1425,-12.92],"heading_true_deg":90},{"sequence":5,"kind":"PylonOnRight","old_sim_xy_m":[900,1755],"centre":[10.3225,-12.83],"heading_true_deg":90},{"sequence":6,"kind":"Gate","old_sim_xy_m":[1050,1800],"centre":[10.4725,-12.875],"heading_true_deg":90},{"sequence":7,"kind":"PylonOnRight","old_sim_xy_m":[1110,1920],"centre":[10.5325,-12.995],"heading_true_deg":180},{"sequence":8,"kind":"Gate","old_sim_xy_m":[1020,2050],"centre":[10.4425,-13.125],"heading_true_deg":270},{"sequence":9,"kind":"Gate","old_sim_xy_m":[720,2050],"centre":[10.1425,-13.125],"heading_true_deg":270},{"sequence":10,"kind":"PylonOnLeft","old_sim_xy_m":[540,2095],"centre":[9.9625,-13.17],"heading_true_deg":270},{"sequence":11,"kind":"PylonOnRight","old_sim_xy_m":[360,2005],"centre":[9.7825,-13.08],"heading_true_deg":270},{"sequence":12,"kind":"Gate","old_sim_xy_m":[120,2050],"centre":[9.5425,-13.125],"heading_true_deg":270}],"reason":"Complete raft count/positions and literal extended shorejudges geometry; exactIAC100–1100m conversion; keepCZ3 fullflighttube andgroundtarget outside raisedfloor; relocate racefromcongestedmainisland tooffshore1km-clear course."}
```

</details>

<details>
<summary>517. features/RACE/platform</summary>

```json
{"phase":"1A.2","item":"features/RACE/platform","old":null,"new":"moored offshore racepylons; collisiongeometryanchored atwater0MSL, no land made; allseeds retained","reason":"Complete raft count/positions and literal extended shorejudges geometry; exactIAC100–1100m conversion; keepCZ3 fullflighttube andgroundtarget outside raisedfloor; relocate racefromcongestedmainisland tooffshore1km-clear course."}
```

</details>

<details>
<summary>518. starts/AB2/centre</summary>

```json
{"phase":"1A.2","item":"starts/AB2/centre","old":[-12.3,-6.3],"new":[-12.2,-6.3],"reason":"Complete raft count/positions and literal extended shorejudges geometry; exactIAC100–1100m conversion; keepCZ3 fullflighttube andgroundtarget outside raisedfloor; relocate racefromcongestedmainisland tooffshore1km-clear course."}
```

</details>

<details>
<summary>519. main_island_size_km_computed</summary>

```json
{"phase":"1A.2","item":"main_island_size_km_computed","old":null,"new":[20.2,17.6],"reason":"Expose expandedwesterncoast supporting PANC/LakeHood grade fit; approximate18.3x17.5km lockedcontext now20.2x17.6km, explicitOpenforTy ratherthan silentrelaxation."}
```

</details>

<details>
<summary>520. Sitka foundation datum</summary>

```json
{"phase":"1A.2","item":"Sitka foundation datum","old":35,"new":60,"reason":"Foundation above all35–55ft tiny-islet surfaces ensures overlapping max-composition cannot deform exact symmetricvolcano footprint. Waterfront fill remains30ft and independent true runway pad."}
```

</details>

<details>
<summary>521. airports/PALH/runways</summary>

```json
{"phase":"1A.2","item":"airports/PALH/runways","old":[{"id":"16W/34W","centre":[-10.18,-1.8],"heading_true_deg":165,"numbers_magnetic":"8W/26W","length_ft":4541,"width_ft":188,"surface":"water","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":300}},{"id":"15/33","centre":[-9.5,-1.3],"heading_true_deg":153,"numbers_magnetic":"15/33","length_ft":2200,"width_ft":75,"surface":"gravel","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":300}}],"new":[{"id":"16W/34W","centre":[-10.18,-1.8],"heading_true_deg":165,"numbers_magnetic":"8W/26W","length_ft":4541,"width_ft":188,"surface":"water","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":300}},{"id":"16/34","centre":[-9.5,-1.3],"heading_true_deg":165,"numbers_magnetic":"15/33","length_ft":2200,"width_ft":75,"surface":"gravel","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":300}}],"reason":"Remove final movable-field conflicts: PALH gravel parallel165true keeps fullvolcano symmetric; L2 westfringe clears KMEV final, AP4/6 andDewey axes avoid high terrain. Tern navsite outside all erosionblends. Add39m-deep drycanyon/rockstacks and100m-underdeck bridge tosmalllandarena without changing its clearboundary/floor/ceiling."}
```

</details>

<details>
<summary>522. airparks/AP4/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"airparks/AP4/heading_true_deg","old":142,"new":144,"reason":"Remove final movable-field conflicts: PALH gravel parallel165true keeps fullvolcano symmetric; L2 westfringe clears KMEV final, AP4/6 andDewey axes avoid high terrain. Tern navsite outside all erosionblends. Add39m-deep drycanyon/rockstacks and100m-underdeck bridge tosmalllandarena without changing its clearboundary/floor/ceiling."}
```

</details>

<details>
<summary>523. airparks/AP6/centre</summary>

```json
{"phase":"1A.2","item":"airparks/AP6/centre","old":[4.8,4.3],"new":[5.1,4.3],"reason":"Remove final movable-field conflicts: PALH gravel parallel165true keeps fullvolcano symmetric; L2 westfringe clears KMEV final, AP4/6 andDewey axes avoid high terrain. Tern navsite outside all erosionblends. Add39m-deep drycanyon/rockstacks and100m-underdeck bridge tosmalllandarena without changing its clearboundary/floor/ceiling."}
```

</details>

<details>
<summary>524. backcountry_strips/Dewey Moore/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Dewey Moore/heading_true_deg","old":270,"new":160,"reason":"Remove final movable-field conflicts: PALH gravel parallel165true keeps fullvolcano symmetric; L2 westfringe clears KMEV final, AP4/6 andDewey axes avoid high terrain. Tern navsite outside all erosionblends. Add39m-deep drycanyon/rockstacks and100m-underdeck bridge tosmalllandarena without changing its clearboundary/floor/ceiling."}
```

</details>

<details>
<summary>525. backcountry_strips/Dewey Moore/one_way_true_deg</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Dewey Moore/one_way_true_deg","old":270,"new":160,"reason":"Remove final movable-field conflicts: PALH gravel parallel165true keeps fullvolcano symmetric; L2 westfringe clears KMEV final, AP4/6 andDewey axes avoid high terrain. Tern navsite outside all erosionblends. Add39m-deep drycanyon/rockstacks and100m-underdeck bridge tosmalllandarena without changing its clearboundary/floor/ceiling."}
```

</details>

<details>
<summary>526. lesson_terrain/L2/fork</summary>

```json
{"phase":"1A.2","item":"lesson_terrain/L2/fork","old":[-4,-0.9],"new":[-4.2,-0.9],"reason":"Remove final movable-field conflicts: PALH gravel parallel165true keeps fullvolcano symmetric; L2 westfringe clears KMEV final, AP4/6 andDewey axes avoid high terrain. Tern navsite outside all erosionblends. Add39m-deep drycanyon/rockstacks and100m-underdeck bridge tosmalllandarena without changing its clearboundary/floor/ceiling."}
```

</details>

<details>
<summary>527. lesson_terrain/L2/safe_path</summary>

```json
{"phase":"1A.2","item":"lesson_terrain/L2/safe_path","old":[[-3.02,-1.73],[-4,-0.9],[-5.8,-0.3],[-10.18,-1.8]],"new":[[-3.02,-1.73],[-4.2,-0.9],[-5.8,-0.3],[-10.18,-1.8]],"reason":"Remove final movable-field conflicts: PALH gravel parallel165true keeps fullvolcano symmetric; L2 westfringe clears KMEV final, AP4/6 andDewey axes avoid high terrain. Tern navsite outside all erosionblends. Add39m-deep drycanyon/rockstacks and100m-underdeck bridge tosmalllandarena without changing its clearboundary/floor/ceiling."}
```

</details>

<details>
<summary>528. lesson_terrain/L2/box_path</summary>

```json
{"phase":"1A.2","item":"lesson_terrain/L2/box_path","old":[[-4,-0.9],[-4.15,-1.25]],"new":[[-4.2,-0.9],[-4.25,-1.25]],"reason":"Remove final movable-field conflicts: PALH gravel parallel165true keeps fullvolcano symmetric; L2 westfringe clears KMEV final, AP4/6 andDewey axes avoid high terrain. Tern navsite outside all erosionblends. Add39m-deep drycanyon/rockstacks and100m-underdeck bridge tosmalllandarena without changing its clearboundary/floor/ceiling."}
```

</details>

<details>
<summary>529. lesson_terrain/L2/box_length_m</summary>

```json
{"phase":"1A.2","item":"lesson_terrain/L2/box_length_m","old":380.789,"new":353.553,"reason":"Remove final movable-field conflicts: PALH gravel parallel165true keeps fullvolcano symmetric; L2 westfringe clears KMEV final, AP4/6 andDewey axes avoid high terrain. Tern navsite outside all erosionblends. Add39m-deep drycanyon/rockstacks and100m-underdeck bridge tosmalllandarena without changing its clearboundary/floor/ceiling."}
```

</details>

<details>
<summary>530. lesson_terrain/L2/headwall_geometry</summary>

```json
{"phase":"1A.2","item":"lesson_terrain/L2/headwall_geometry","old":{"centre":[-4.197270315829501,-1.3602974036021669],"top_ft":6950,"radius_m":180,"core_radius_m":50,"blend_m":130},"new":{"centre":[-4.266970562748477,-1.36879393923934],"top_ft":6950,"radius_m":180,"core_radius_m":50,"blend_m":130},"reason":"Remove final movable-field conflicts: PALH gravel parallel165true keeps fullvolcano symmetric; L2 westfringe clears KMEV final, AP4/6 andDewey axes avoid high terrain. Tern navsite outside all erosionblends. Add39m-deep drycanyon/rockstacks and100m-underdeck bridge tosmalllandarena without changing its clearboundary/floor/ceiling."}
```

</details>

<details>
<summary>531. outer_navaid_sites/SITE-TERN/centre</summary>

```json
{"phase":"1A.2","item":"outer_navaid_sites/SITE-TERN/centre","old":[11.7,2],"new":[12.1,2],"reason":"Remove final movable-field conflicts: PALH gravel parallel165true keeps fullvolcano symmetric; L2 westfringe clears KMEV final, AP4/6 andDewey axes avoid high terrain. Tern navsite outside all erosionblends. Add39m-deep drycanyon/rockstacks and100m-underdeck bridge tosmalllandarena without changing its clearboundary/floor/ceiling."}
```

</details>

<details>
<summary>532. combat_zones/CZ5/combat_features</summary>

```json
{"phase":"1A.2","item":"combat_zones/CZ5/combat_features","old":[{"id":"CZ5-RIDGE","type":"paired ridges and dry canyon","centre":[17.3,2.4],"axis_true_deg":0,"length_m":300,"canyon_width_m":100,"canyon_depth_m":2.4384,"terrace_floor_ft":180,"rim_ft":188,"ridge_half_width_m":35,"flight_half_width_m":20,"flight_floor_msl_ft":220,"flight_ceiling_msl_ft":300},{"id":"CZ5-BRIDGE","type":"bridge","centre":[17.3,2.4],"heading_true_deg":90,"span_m":120,"deck_width_m":14,"under_clearance_m":60,"under_base_msl_ft":180,"deck_thickness_m":4,"flight_opening_width_m":100,"flight_floor_msl_ft":220,"flight_ceiling_msl_ft":300,"collision":"WorldSolids bridge deck/piers; no heightfield bridge deck"},{"id":"CZ5-ARCH","type":"rock arch","centre":[17.41,2.488],"axis_true_deg":0,"opening_width_m":60,"opening_height_m":55,"base_msl_ft":180,"rock_top_msl_ft":400,"flight_floor_msl_ft":220,"flight_ceiling_msl_ft":300,"collision":"WorldSolids overhang/opening; footprint pedestals only in shared heightfield"}],"new":[{"id":"CZ5-RIDGE","type":"paired ridges and dry canyon","centre":[17.3,2.4],"axis_true_deg":0,"length_m":300,"canyon_width_m":100,"canyon_depth_m":39.0144,"terrace_floor_ft":180,"rim_ft":188,"ridge_half_width_m":35,"flight_half_width_m":20,"flight_floor_msl_ft":220,"flight_ceiling_msl_ft":300,"canyon_floor_ft":60,"canyon_floor_width_m":60},{"id":"CZ5-BRIDGE","type":"bridge","centre":[17.3,2.4],"heading_true_deg":90,"span_m":120,"deck_width_m":14,"under_clearance_m":100,"under_base_msl_ft":60,"deck_thickness_m":4,"flight_opening_width_m":100,"flight_floor_msl_ft":220,"flight_ceiling_msl_ft":300,"collision":"WorldSolids bridge deck/piers; no heightfield bridge deck"},{"id":"CZ5-ARCH","type":"rock arch","centre":[17.41,2.488],"axis_true_deg":0,"opening_width_m":60,"opening_height_m":55,"base_msl_ft":188,"rock_top_msl_ft":400,"flight_floor_msl_ft":220,"flight_ceiling_msl_ft":300,"collision":"WorldSolids overhang/opening; footprint pedestals only in shared heightfield"},{"id":"CZ5-STACKS","type":"rock spire pair","centre":[17.3,2.4],"stack_centres":[[17.24,2.3],[17.36,2.5]],"radius_m":10,"base_msl_ft":60,"top_msl_ft":188,"height_m":39.0144,"flight_floor_msl_ft":220,"flight_ceiling_msl_ft":300,"flight_half_width_m":20,"length_m":280,"terrain":"Two isolatederoded columns beside drycanyon; smooth radial cones insharedheightfield, no overhang."}],"reason":"Remove final movable-field conflicts: PALH gravel parallel165true keeps fullvolcano symmetric; L2 westfringe clears KMEV final, AP4/6 andDewey axes avoid high terrain. Tern navsite outside all erosionblends. Add39m-deep drycanyon/rockstacks and100m-underdeck bridge tosmalllandarena without changing its clearboundary/floor/ceiling."}
```

</details>

<details>
<summary>533. combat_zones/CZ5/agl_range_ft_bound</summary>

```json
{"phase":"1A.2","item":"combat_zones/CZ5/agl_range_ft_bound","old":{"floor":[12,20],"ceiling":[2312,2320],"terrain":[180,188],"meaning":"Analytic continuous terrain extrema, including subgridridge. Groundtargets and structures are fixtures; aircraft/drone volumes stayabovefloor."},"new":{"floor":[12,140],"ceiling":[2312,2440],"terrain":[60,188],"meaning":"Analytic continuous extrema include60ft drycanyon floor and188ft rims/spires. Aircraft/drone volumes remainabovefloor; targets aregroundfixtures."},"reason":"Remove final movable-field conflicts: PALH gravel parallel165true keeps fullvolcano symmetric; L2 westfringe clears KMEV final, AP4/6 andDewey axes avoid high terrain. Tern navsite outside all erosionblends. Add39m-deep drycanyon/rockstacks and100m-underdeck bridge tosmalllandarena without changing its clearboundary/floor/ceiling."}
```

</details>

<details>
<summary>534. Mokuleia core position</summary>

```json
{"phase":"1A.2","item":"Mokuleia core position","old":{"id":"Mokuleia","centre":[5.6,4],"top_ft":2950,"radius_km":1.5},"new":{"id":"Mokuleia","centre":[5.8,3.3],"top_ft":2950,"radius_km":1.5},"reason":"Preserve2950ft targetpeak onridge southof shiftedAP6; whole peakcore clear of both airparkfinalfans."}
```

</details>

<details>
<summary>535. airports/PHDH/shore_class</summary>

```json
{"phase":"1A.2","item":"airports/PHDH/shore_class","old":null,"new":"coastal","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>536. airports/PANC/shore_class</summary>

```json
{"phase":"1A.2","item":"airports/PANC/shore_class","old":null,"new":"coastal","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>537. airports/PALH/shore_class</summary>

```json
{"phase":"1A.2","item":"airports/PALH/shore_class","old":null,"new":"coastal","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>538. airports/PATK/shore_class</summary>

```json
{"phase":"1A.2","item":"airports/PATK/shore_class","old":null,"new":"coastal","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>539. airports/KSNA/shore_class</summary>

```json
{"phase":"1A.2","item":"airports/KSNA/shore_class","old":null,"new":"coastal","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>540. airports/KMYL/shore_class</summary>

```json
{"phase":"1A.2","item":"airports/KMYL/shore_class","old":null,"new":"inland","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>541. airports/3U2/shore_class</summary>

```json
{"phase":"1A.2","item":"airports/3U2/shore_class","old":null,"new":"inland","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>542. airports/KMEV/shore_class</summary>

```json
{"phase":"1A.2","item":"airports/KMEV/shore_class","old":null,"new":"inland","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>543. airports/KPGA/shore_class</summary>

```json
{"phase":"1A.2","item":"airports/KPGA/shore_class","old":null,"new":"inland","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>544. airports/KAVX/shore_class</summary>

```json
{"phase":"1A.2","item":"airports/KAVX/shore_class","old":null,"new":"coastal","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>545. airports/DRAKE/shore_class</summary>

```json
{"phase":"1A.2","item":"airports/DRAKE/shore_class","old":null,"new":"inland","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>546. airports/SITX/shore_class</summary>

```json
{"phase":"1A.2","item":"airports/SITX/shore_class","old":null,"new":"coastal","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>547. airports/TERN/shore_class</summary>

```json
{"phase":"1A.2","item":"airports/TERN/shore_class","old":null,"new":"coastal","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>548. airports/NHVN/shore_class</summary>

```json
{"phase":"1A.2","item":"airports/NHVN/shore_class","old":null,"new":"coastal","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>549. airparks/AP1/shore_class</summary>

```json
{"phase":"1A.2","item":"airparks/AP1/shore_class","old":null,"new":"inland","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>550. airparks/AP2/shore_class</summary>

```json
{"phase":"1A.2","item":"airparks/AP2/shore_class","old":null,"new":"coastal","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>551. airparks/AP3/shore_class</summary>

```json
{"phase":"1A.2","item":"airparks/AP3/shore_class","old":null,"new":"inland","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>552. airparks/AP4/shore_class</summary>

```json
{"phase":"1A.2","item":"airparks/AP4/shore_class","old":null,"new":"coastal","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>553. airparks/AP5/shore_class</summary>

```json
{"phase":"1A.2","item":"airparks/AP5/shore_class","old":null,"new":"coastal","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>554. airparks/AP6/shore_class</summary>

```json
{"phase":"1A.2","item":"airparks/AP6/shore_class","old":null,"new":"inland","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>555. airparks/AP7/shore_class</summary>

```json
{"phase":"1A.2","item":"airparks/AP7/shore_class","old":null,"new":"coastal","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>556. airparks/AP8/float_pond</summary>

```json
{"phase":"1A.2","item":"airparks/AP8/float_pond","old":{"centre":[-7.8,3.7],"length_m":600,"width_m":120,"elevation_ft":250,"heading_true_deg":0,"surface":"water","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"water_surface_ft":250,"water_bed_offset_m":3,"note":"600x120m rectangular floatlane plus60m shoulders, grass runway remainsseparate; sameAP8 site, no newstart."},"new":{"centre":[-7.8,3.7],"length_m":600,"width_m":120,"elevation_ft":250,"heading_true_deg":0,"surface":"water","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"water_surface_ft":250,"water_bed_offset_m":3,"note":"600x120m rectangular floatlane plus60m shoulders, grass runway remainsseparate; sameAP8 site, no newstart.","shore_class":"coastal"},"reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>557. airparks/AP8/shore_class</summary>

```json
{"phase":"1A.2","item":"airparks/AP8/shore_class","old":null,"new":"coastal","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>558. airparks/AP9/shore_class</summary>

```json
{"phase":"1A.2","item":"airparks/AP9/shore_class","old":null,"new":"coastal","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>559. airparks/AP10/shore_class</summary>

```json
{"phase":"1A.2","item":"airparks/AP10/shore_class","old":null,"new":"inland","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>560. airparks/AP11/shore_class</summary>

```json
{"phase":"1A.2","item":"airparks/AP11/shore_class","old":null,"new":"inland","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>561. airparks/AP12/shore_class</summary>

```json
{"phase":"1A.2","item":"airparks/AP12/shore_class","old":null,"new":"inland","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>562. airparks/AP13/shore_class</summary>

```json
{"phase":"1A.2","item":"airparks/AP13/shore_class","old":null,"new":"inland","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>563. airparks/AP14/shore_class</summary>

```json
{"phase":"1A.2","item":"airparks/AP14/shore_class","old":null,"new":"coastal","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>564. backcountry_strips/Chamberlain/shore_class</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Chamberlain/shore_class","old":null,"new":"inland","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>565. backcountry_strips/Big Creek/shore_class</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Big Creek/shore_class","old":null,"new":"inland","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>566. backcountry_strips/Thomas Creek/shore_class</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Thomas Creek/shore_class","old":null,"new":"inland","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>567. backcountry_strips/Sulphur Creek/shore_class</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Sulphur Creek/shore_class","old":null,"new":"inland","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>568. backcountry_strips/Soldier Bar/shore_class</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Soldier Bar/shore_class","old":null,"new":"inland","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>569. backcountry_strips/Cabin Creek/shore_class</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Cabin Creek/shore_class","old":null,"new":"inland","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>570. backcountry_strips/Dewey Moore/shore_class</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Dewey Moore/shore_class","old":null,"new":"inland","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>571. backcountry_strips/Indian Creek/shore_class</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Indian Creek/shore_class","old":null,"new":"inland","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>572. backcountry_strips/Mackay Bar/shore_class</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Mackay Bar/shore_class","old":null,"new":"inland","reason":"Finalfull-fan separation and explicitcoastal/inland classifications for shore rule; inlandairparks/backcountry/Drake also require2km ends, not merelythestart airports."}
```

</details>

<details>
<summary>573. backcountry_strips/Soldier Bar/centre</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Soldier Bar/centre","old":[4.5,1.25],"new":[4.2,1.25],"reason":"Move Mackay southwest normal toAP4 fan to remove coarse-grid pad-fringe penetration; maintain0.8km spacing bymoving Soldier/Indian/Dewey, and preserve symmetricvolcano outsideactualfinalfans instead of erodingit with discretionaryouterguard. Fullfan/surfacechecks unchanged."}
```

</details>

<details>
<summary>574. backcountry_strips/Dewey Moore/centre</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Dewey Moore/centre","old":[5.3,0.65],"new":[5.3,0.25],"reason":"Move Mackay southwest normal toAP4 fan to remove coarse-grid pad-fringe penetration; maintain0.8km spacing bymoving Soldier/Indian/Dewey, and preserve symmetricvolcano outsideactualfinalfans instead of erodingit with discretionaryouterguard. Fullfan/surfacechecks unchanged."}
```

</details>

<details>
<summary>575. backcountry_strips/Dewey Moore/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Dewey Moore/heading_true_deg","old":160,"new":155,"reason":"Move Mackay southwest normal toAP4 fan to remove coarse-grid pad-fringe penetration; maintain0.8km spacing bymoving Soldier/Indian/Dewey, and preserve symmetricvolcano outsideactualfinalfans instead of erodingit with discretionaryouterguard. Fullfan/surfacechecks unchanged."}
```

</details>

<details>
<summary>576. backcountry_strips/Dewey Moore/one_way_true_deg</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Dewey Moore/one_way_true_deg","old":160,"new":155,"reason":"Move Mackay southwest normal toAP4 fan to remove coarse-grid pad-fringe penetration; maintain0.8km spacing bymoving Soldier/Indian/Dewey, and preserve symmetricvolcano outsideactualfinalfans instead of erodingit with discretionaryouterguard. Fullfan/surfacechecks unchanged."}
```

</details>

<details>
<summary>577. backcountry_strips/Indian Creek/centre</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Indian Creek/centre","old":[3.8,1.7],"new":[3.6,1.9],"reason":"Move Mackay southwest normal toAP4 fan to remove coarse-grid pad-fringe penetration; maintain0.8km spacing bymoving Soldier/Indian/Dewey, and preserve symmetricvolcano outsideactualfinalfans instead of erodingit with discretionaryouterguard. Fullfan/surfacechecks unchanged."}
```

</details>

<details>
<summary>578. backcountry_strips/Mackay Bar/centre</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Mackay Bar/centre","old":[5.3,1.5],"new":[5.1,1.3],"reason":"Move Mackay southwest normal toAP4 fan to remove coarse-grid pad-fringe penetration; maintain0.8km spacing bymoving Soldier/Indian/Dewey, and preserve symmetricvolcano outsideactualfinalfans instead of erodingit with discretionaryouterguard. Fullfan/surfacechecks unchanged."}
```

</details>

<details>
<summary>579. canyons/C2/path</summary>

```json
{"phase":"1A.2","item":"canyons/C2/path","old":[[0.7,-1.8],[2.7,-0.4],[3.1,0.55],[3.8,1.7],[4.5,1.25],[5.3,1.5],[5.3,0.65],[4.2,0.1],[4.6,-0.65]],"new":[[0.7,-1.8],[2.7,-0.4],[3.1,0.55],[3.6,1.9],[4.2,1.25],[5.1,1.3],[5.3,0.25],[4.2,0.1],[4.6,-0.65]],"reason":"Move Mackay southwest normal toAP4 fan to remove coarse-grid pad-fringe penetration; maintain0.8km spacing bymoving Soldier/Indian/Dewey, and preserve symmetricvolcano outsideactualfinalfans instead of erodingit with discretionaryouterguard. Fullfan/surfacechecks unchanged."}
```

</details>

<details>
<summary>580. outer_islands/OI1/volcano</summary>

```json
{"phase":"1A.2","item":"outer_islands/OI1/volcano","old":{"centre":[-13.35,3.2],"radius_km":0.85,"rim_radius_km":0.14,"rim_ft":2600,"crater_floor_ft":2250,"shape":"symmetric stratovolcano: linear radial cone, 280 m summit crater; continuous radial crater bowl"},"new":{"centre":[-13.35,3.2],"radius_km":0.85,"rim_radius_km":0.14,"rim_ft":2600,"crater_floor_ft":2250,"shape":"symmetric stratovolcano: linear radial cone, 280 m summit crater; continuous radial crater bowl","clearance_reservation":{"minimum_full_final_boundary_km":0.1,"preserve_radial_formula_from_outer_erosion_guards":true,"rule":"No terraincut inside cone disk; entire cone is at least100m outsideactual protected finalfans. Nonessential outer guard doesnot deform it. Fullarea terrain and obstacle checks remain mandatory."}},"reason":"Move Mackay southwest normal toAP4 fan to remove coarse-grid pad-fringe penetration; maintain0.8km spacing bymoving Soldier/Indian/Dewey, and preserve symmetricvolcano outsideactualfinalfans instead of erodingit with discretionaryouterguard. Fullfan/surfacechecks unchanged."}
```

</details>

<details>
<summary>581. backcountry_strips/Soldier Bar/centre</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Soldier Bar/centre","old":[4.2,1.25],"new":[4,1.25],"reason":"Move Soldierwest to gain20:1 climb distance toMackay; Indianwestfinal turns south ofL1 risingfloor while keeping allspacing. Increase pirateupperrock5m to guarantee>=15m fullchamber cover despite coastfade; explicitportal/throat rock shell retains lockedmouth/chamberdimensions."}
```

</details>

<details>
<summary>582. backcountry_strips/Indian Creek/centre</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Indian Creek/centre","old":[3.6,1.9],"new":[3.5,2],"reason":"Move Soldierwest to gain20:1 climb distance toMackay; Indianwestfinal turns south ofL1 risingfloor while keeping allspacing. Increase pirateupperrock5m to guarantee>=15m fullchamber cover despite coastfade; explicitportal/throat rock shell retains lockedmouth/chamberdimensions."}
```

</details>

<details>
<summary>583. backcountry_strips/Indian Creek/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Indian Creek/heading_true_deg","old":81,"new":50,"reason":"Move Soldierwest to gain20:1 climb distance toMackay; Indianwestfinal turns south ofL1 risingfloor while keeping allspacing. Increase pirateupperrock5m to guarantee>=15m fullchamber cover despite coastfade; explicitportal/throat rock shell retains lockedmouth/chamberdimensions."}
```

</details>

<details>
<summary>584. canyons/C2/path</summary>

```json
{"phase":"1A.2","item":"canyons/C2/path","old":[[0.7,-1.8],[2.7,-0.4],[3.1,0.55],[3.6,1.9],[4.2,1.25],[5.1,1.3],[5.3,0.25],[4.2,0.1],[4.6,-0.65]],"new":[[0.7,-1.8],[2.7,-0.4],[3.1,0.55],[3.5,2],[4,1.25],[5.1,1.3],[5.3,0.25],[4.2,0.1],[4.6,-0.65]],"reason":"Move Soldierwest to gain20:1 climb distance toMackay; Indianwestfinal turns south ofL1 risingfloor while keeping allspacing. Increase pirateupperrock5m to guarantee>=15m fullchamber cover despite coastfade; explicitportal/throat rock shell retains lockedmouth/chamberdimensions."}
```

</details>

<details>
<summary>585. features/F2/interior</summary>

```json
{"phase":"1A.2","item":"features/F2/interior","old":{"chamber_centre":[2.806809525,-14.538333492],"floor_msl_m":3,"ceiling_msl_m":40,"roof_top_msl_m":55,"roof_min_cover_m":15,"mouth_floor_msl_m":3,"mouth_ceiling_msl_m":29,"throat_length_m":200,"throat_width_m":60,"throat_ceiling_msl_m":29,"skylight_centre":[2.806809525,-14.538333492],"skylight_diameter_m":10,"pads":[{"id":"landing","offset_m":[0,-35],"size_m":[55,20]},{"id":"parking","offset_m":[0,35],"size_m":[40,20]}],"flight_lane":{"axis":"local inward distance from mouth; chamber pads local east/north","half_width_m":10,"floor_msl_m":5,"ceiling_msl_m":23},"contents_bounds":[{"id":"wreck","offset_m":[-65,0],"radius_m":8,"top_msl_m":11},{"id":"beach-driftwood","offset_m":[65,0],"radius_m":10,"top_msl_m":5}],"representation":"closed 3D shell; planar deck, rectangular throat, horizontal chamber roof except open skylight; phase1A.2 analytically verified design solids; no runtime implementation"},"new":{"chamber_centre":[2.806809525,-14.538333492],"floor_msl_m":3,"ceiling_msl_m":40,"roof_top_msl_m":60,"roof_min_cover_m":15,"mouth_floor_msl_m":3,"mouth_ceiling_msl_m":29,"throat_length_m":200,"throat_width_m":60,"throat_ceiling_msl_m":29,"skylight_centre":[2.806809525,-14.538333492],"skylight_diameter_m":10,"pads":[{"id":"landing","offset_m":[0,-35],"size_m":[55,20]},{"id":"parking","offset_m":[0,35],"size_m":[40,20]}],"flight_lane":{"axis":"local inward distance from mouth; chamber pads local east/north","half_width_m":10,"floor_msl_m":5,"ceiling_msl_m":23},"contents_bounds":[{"id":"wreck","offset_m":[-65,0],"radius_m":8,"top_msl_m":11},{"id":"beach-driftwood","offset_m":[65,0],"radius_m":10,"top_msl_m":5}],"representation":"closed 3D shell; planar deck, rectangular throat, horizontal chamber roof except open skylight; phase1A.2 analytically verified design solids; no runtime implementation","throat_roof_top_msl_m_min":35,"throat_min_rock_cover_m":6,"portal_shell":"Physicalportalcliff/overhang inWorldSolids, with flat3mdeck and29m throatceiling; inlandchamber upperrock envelope insharedheightfield. No secondgroundsurface. Runtime contact routing remainsPhase1B."},"reason":"Move Soldierwest to gain20:1 climb distance toMackay; Indianwestfinal turns south ofL1 risingfloor while keeping allspacing. Increase pirateupperrock5m to guarantee>=15m fullchamber cover despite coastfade; explicitportal/throat rock shell retains lockedmouth/chamberdimensions."}
```

</details>

<details>
<summary>586. Indian Creek heading fine fit</summary>

```json
{"phase":"1A.2","item":"Indian Creek heading fine fit","old":50,"new":47,"reason":"Three-degree rotation adds coarse-grid fringe clearance southof fixedFrankChurch summit; fullprotectedfans and padseparation unchanged."}
```

</details>

<details>
<summary>587. water/Payette Lake/centre</summary>

```json
{"phase":"1A.2 water-surface correction","item":"water/Payette Lake/centre","old":[-0.48,4.42],"new":[-0.6,4.1],"reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>588. water/Payette Lake/elevation_ft</summary>

```json
{"phase":"1A.2 water-surface correction","item":"water/Payette Lake/elevation_ft","old":4990,"new":4900,"reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>589. water/Payette Lake/radii_km</summary>

```json
{"phase":"1A.2 water-surface correction","item":"water/Payette Lake/radii_km","old":[0.55,0.8],"new":[0.5,0.6],"reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>590. water/Payette Lake/basin_detail</summary>

```json
{"phase":"1A.2 water-surface correction","item":"water/Payette Lake/basin_detail","old":null,"new":{"bank_width_m":40,"ridge_offset_m":10,"ridge_height_m":5,"bed_rule":"Waterminus depth atcentre, smoothly towater atshore; narrow5m closedrockrim, thenfade to surroundingterrain. Opening/outletsolidshape awaitsPhase1B."},"reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>591. water/Lake Powell/centre</summary>

```json
{"phase":"1A.2 water-surface correction","item":"water/Lake Powell/centre","old":[2.775,-4.525],"new":[4.275,-4.525],"reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>592. water/Lake Powell/basin_detail</summary>

```json
{"phase":"1A.2 water-surface correction","item":"water/Lake Powell/basin_detail","old":null,"new":{"bank_width_m":40,"ridge_offset_m":10,"ridge_height_m":5,"bed_rule":"Waterminus depth atcentre, smoothly towater atshore; narrow5m closedrockrim, thenfade to surroundingterrain. Opening/outletsolidshape awaitsPhase1B."},"reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>593. water/Bighorn Outlet/centre</summary>

```json
{"phase":"1A.2 water-surface correction","item":"water/Bighorn Outlet/centre","old":[0.7,0.9],"new":[0.1,2],"reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>594. water/Bighorn Outlet/basin_detail</summary>

```json
{"phase":"1A.2 water-surface correction","item":"water/Bighorn Outlet/basin_detail","old":null,"new":{"bank_width_m":40,"ridge_offset_m":10,"ridge_height_m":5,"bed_rule":"Waterminus depth atcentre, smoothly towater atshore; narrow5m closedrockrim, thenfade to surroundingterrain. Opening/outletsolidshape awaitsPhase1B."},"reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>595. water/Bighorn Shelf/centre</summary>

```json
{"phase":"1A.2 water-surface correction","item":"water/Bighorn Shelf/centre","old":[1.8,2.7],"new":[0.15,3.5],"reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>596. water/Bighorn Shelf/basin_detail</summary>

```json
{"phase":"1A.2 water-surface correction","item":"water/Bighorn Shelf/basin_detail","old":null,"new":{"bank_width_m":40,"ridge_offset_m":10,"ridge_height_m":5,"bed_rule":"Waterminus depth atcentre, smoothly towater atshore; narrow5m closedrockrim, thenfade to surroundingterrain. Opening/outletsolidshape awaitsPhase1B."},"reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>597. water/Bighorn Larch/centre</summary>

```json
{"phase":"1A.2 water-surface correction","item":"water/Bighorn Larch/centre","old":[3.3,2],"new":[0.8,2],"reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>598. water/Bighorn Larch/long_axis_true_deg</summary>

```json
{"phase":"1A.2 water-surface correction","item":"water/Bighorn Larch/long_axis_true_deg","old":null,"new":0,"reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>599. water/Bighorn Larch/basin_detail</summary>

```json
{"phase":"1A.2 water-surface correction","item":"water/Bighorn Larch/basin_detail","old":null,"new":{"bank_width_m":40,"ridge_offset_m":10,"ridge_height_m":5,"bed_rule":"Waterminus depth atcentre, smoothly towater atshore; narrow5m closedrockrim, thenfade to surroundingterrain. Opening/outletsolidshape awaitsPhase1B."},"reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>600. water/Bighorn Hidden/centre</summary>

```json
{"phase":"1A.2 water-surface correction","item":"water/Bighorn Hidden/centre","old":[0.2,1.3],"new":[0.3,2.5],"reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>601. water/Bighorn Hidden/basin_detail</summary>

```json
{"phase":"1A.2 water-surface correction","item":"water/Bighorn Hidden/basin_detail","old":null,"new":{"bank_width_m":40,"ridge_offset_m":10,"ridge_height_m":5,"bed_rule":"Waterminus depth atcentre, smoothly towater atshore; narrow5m closedrockrim, thenfade to surroundingterrain. Opening/outletsolidshape awaitsPhase1B."},"reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>602. water/Bighorn Cirque/centre</summary>

```json
{"phase":"1A.2 water-surface correction","item":"water/Bighorn Cirque/centre","old":[1.2,0.8],"new":[0.65,0.55],"reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>603. water/Bighorn Cirque/basin_detail</summary>

```json
{"phase":"1A.2 water-surface correction","item":"water/Bighorn Cirque/basin_detail","old":null,"new":{"bank_width_m":40,"ridge_offset_m":10,"ridge_height_m":5,"bed_rule":"Waterminus depth atcentre, smoothly towater atshore; narrow5m closedrockrim, thenfade to surroundingterrain. Opening/outletsolidshape awaitsPhase1B."},"reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>604. water/Bighorn Crown/centre</summary>

```json
{"phase":"1A.2 water-surface correction","item":"water/Bighorn Crown/centre","old":[2.7,1.9],"new":[0.7,3.2],"reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>605. water/Bighorn Crown/basin_detail</summary>

```json
{"phase":"1A.2 water-surface correction","item":"water/Bighorn Crown/basin_detail","old":null,"new":{"bank_width_m":40,"ridge_offset_m":10,"ridge_height_m":5,"bed_rule":"Waterminus depth atcentre, smoothly towater atshore; narrow5m closedrockrim, thenfade to surroundingterrain. Opening/outletsolidshape awaitsPhase1B."},"reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>606. airports/KPGA/runways</summary>

```json
{"phase":"1A.2 water-surface correction","item":"airports/KPGA/runways","old":[{"id":"16/34","centre":[3.74,-3.26],"heading_true_deg":170,"numbers_magnetic":"16/34","length_ft":5950,"width_ft":150,"surface":"asphalt","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":300}},{"id":"7/25","centre":[4.18,-2.5],"heading_true_deg":82,"numbers_magnetic":"7/25","length_ft":2201,"width_ft":75,"surface":"asphalt","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":300}}],"new":[{"id":"16/34","centre":[5.24,-2.76],"heading_true_deg":170,"numbers_magnetic":"16/34","length_ft":5950,"width_ft":150,"surface":"asphalt","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":300}},{"id":"7/25","centre":[5.68,-2.0],"heading_true_deg":82,"numbers_magnetic":"7/25","length_ft":2201,"width_ft":75,"surface":"asphalt","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":300}}],"reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>607. airparks/AP13/centre</summary>

```json
{"phase":"1A.2 water-surface correction","item":"airparks/AP13/centre","old":[5.8,-2],"new":[5.5,-5],"reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>608. airparks/AP13/elevation_ft</summary>

```json
{"phase":"1A.2 water-surface correction","item":"airparks/AP13/elevation_ft","old":4500,"new":4000,"reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>609. airparks/AP13/shore_class</summary>

```json
{"phase":"1A.2 water-surface correction","item":"airparks/AP13/shore_class","old":"inland","new":"coastal","reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>610. backcountry_strips/Thomas Creek/centre</summary>

```json
{"phase":"1A.2 water-surface correction","item":"backcountry_strips/Thomas Creek/centre","old":[4.6,-0.65],"new":[4.45,-0.1],"reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>611. backcountry_strips/Sulphur Creek/centre</summary>

```json
{"phase":"1A.2 water-surface correction","item":"backcountry_strips/Sulphur Creek/centre","old":[4.2,0.1],"new":[3.6,-0.5],"reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>612. features/F6/centre</summary>

```json
{"phase":"1A.2 water-surface correction","item":"features/F6/centre","old":[2.88,-5.15],"new":[4.38,-5.15],"reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>613. canyons/C2/path</summary>

```json
{"phase":"1A.2 water-surface correction","item":"canyons/C2/path","old":[[0.7,-1.8],[2.7,-0.4],[3.1,0.55],[3.5,2],[4,1.25],[5.1,1.3],[5.3,0.25],[4.2,0.1],[4.6,-0.65]],"new":[[0.7,-1.8],[2.7,-0.4],[3.1,0.55],[3.5,2],[4,1.25],[5.1,1.3],[5.3,0.25],[3.6,-0.5],[4.45,-0.1]],"reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>614. canyons/C3/path</summary>

```json
{"phase":"1A.2 water-surface correction","item":"canyons/C3/path","old":[[2.88,-5.38],[2.9,-6.3],[0,-6.72],[-1.15,-7.87],[-1.54,-9.6]],"new":[[4.38,-5.38],[2.9,-6.3],[0,-6.72],[-1.15,-7.87],[-1.54,-9.6]],"reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>615. lesson_terrain/L3/safe_path</summary>

```json
{"phase":"1A.2 water-surface correction","item":"lesson_terrain/L3/safe_path","old":[[2.88,-5.38],[2.9,-6.3],[0,-6.72],[-1.54,-9.6]],"new":[[4.38,-5.38],[2.9,-6.3],[0,-6.72],[-1.54,-9.6]],"reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>616. lift_terrain/S3/centre</summary>

```json
{"phase":"1A.2 water-surface correction","item":"lift_terrain/S3/centre","old":[4.5,-4.6],"new":[5.7,-4.6],"reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>617. starts/KPGA:OnTheRunway/centre</summary>

```json
{"phase":"1A.2 water-surface correction","item":"starts/KPGA:OnTheRunway/centre","old":[3.596431,-2.445781],"new":[5.096431,-1.945781],"reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>618. starts/KPGA:OnFinal/centre</summary>

```json
{"phase":"1A.2 water-surface correction","item":"starts/KPGA:OnFinal/centre","old":[3.443621,-1.57915],"new":[4.943621,-1.07915],"reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>619. starts/KPGA:InTheAir/centre</summary>

```json
{"phase":"1A.2 water-surface correction","item":"starts/KPGA:InTheAir/centre","old":[3.408891,-1.382188],"new":[4.908891,-0.882188],"reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>620. practice_lines/ST-KPGA/path</summary>

```json
{"phase":"1A.2 water-surface correction","item":"practice_lines/ST-KPGA/path","old":[[3.583754,-2.678294],[3.792132,-3.860064]],"new":[[5.083754,-2.178294],[5.292132,-3.360064]],"reason":"Checker now includes complete waterplanes, not only cutlakebeds. Move sixCrags lakes/Payette toclear protectedfans, retaininglakeidentity andhighCrags elevations; keep lockedPowell3700ft whilemoving iteast. Translate Page1.5kmE/0.5kmN and moveThomas/Sulphur/AP13 outofKSNAnorthfan, preservingtruefielddatums andlockedrunwaylengths. No operational-direction exceptionneededforKSNA."}
```

</details>

<details>
<summary>621. Thomas Creek/centre</summary>

```json
{"phase":"1A.2","item":"Thomas Creek/centre","old":[4.45,-0.1],"new":[6.1,-0.1],"reason":"Keep relocatedPageRNAV16 fullfan clear; Thomasgoes toan easternNS terrace, with350true heading avoidingwaterAB1 and2kmshoreendsetback. Deweyseparatesitsapproach fromThomas highpad."}
```

</details>

<details>
<summary>622. Thomas Creek/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"Thomas Creek/heading_true_deg","old":85,"new":350,"reason":"Keep relocatedPageRNAV16 fullfan clear; Thomasgoes toan easternNS terrace, with350true heading avoidingwaterAB1 and2kmshoreendsetback. Deweyseparatesitsapproach fromThomas highpad."}
```

</details>

<details>
<summary>623. Dewey Moore/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"Dewey Moore/heading_true_deg","old":155,"new":160,"reason":"Keep relocatedPageRNAV16 fullfan clear; Thomasgoes toan easternNS terrace, with350true heading avoidingwaterAB1 and2kmshoreendsetback. Deweyseparatesitsapproach fromThomas highpad."}
```

</details>

<details>
<summary>624. Dewey Moore/one_way_true_deg</summary>

```json
{"phase":"1A.2","item":"Dewey Moore/one_way_true_deg","old":155,"new":160,"reason":"Keep relocatedPageRNAV16 fullfan clear; Thomasgoes toan easternNS terrace, with350true heading avoidingwaterAB1 and2kmshoreendsetback. Deweyseparatesitsapproach fromThomas highpad."}
```

</details>

<details>
<summary>625. C2 finalconfluence</summary>

```json
{"phase":"1A.2","item":"C2 finalconfluence","old":[[0.7,-1.8],[2.7,-0.4],[3.1,0.55],[3.5,2],[4,1.25],[5.1,1.3],[5.3,0.25],[3.6,-0.5],[6.1,-0.1]],"new":[[0.7,-1.8],[2.7,-0.4],[3.1,0.55],[3.5,2],[4,1.25],[5.1,1.3],[5.3,0.25],[3.6,-0.5],[6.1,-0.1]],"reason":"Follow finalThomas terrace."}
```

</details>

<details>
<summary>626. water/Bighorn Outlet/centre</summary>

```json
{"phase":"1A.2","item":"water/Bighorn Outlet/centre","old":[0.1,2],"new":[0,2.2],"reason":"PackCrags basins between protectedfinalfans, clear ofrisingL1 floor, neighboringlake footprints andMcCall field. Floatlakes retainoriginaldimensions/elevations butlongaxes canrotate."}
```

</details>

<details>
<summary>627. water/Bighorn Outlet/long_axis_true_deg</summary>

```json
{"phase":"1A.2","item":"water/Bighorn Outlet/long_axis_true_deg","old":null,"new":0,"reason":"PackCrags basins between protectedfinalfans, clear ofrisingL1 floor, neighboringlake footprints andMcCall field. Floatlakes retainoriginaldimensions/elevations butlongaxes canrotate."}
```

</details>

<details>
<summary>628. water/Bighorn Larch/centre</summary>

```json
{"phase":"1A.2","item":"water/Bighorn Larch/centre","old":[0.8,2],"new":[0.4,2.05],"reason":"PackCrags basins between protectedfinalfans, clear ofrisingL1 floor, neighboringlake footprints andMcCall field. Floatlakes retainoriginaldimensions/elevations butlongaxes canrotate."}
```

</details>

<details>
<summary>629. water/Bighorn Hidden/centre</summary>

```json
{"phase":"1A.2","item":"water/Bighorn Hidden/centre","old":[0.3,2.5],"new":[0.2,2.95],"reason":"PackCrags basins between protectedfinalfans, clear ofrisingL1 floor, neighboringlake footprints andMcCall field. Floatlakes retainoriginaldimensions/elevations butlongaxes canrotate."}
```

</details>

<details>
<summary>630. KPGA 16/34 finalcentre</summary>

```json
{"phase":"1A.2","item":"KPGA 16/34 finalcentre","old":[5.24,-2.76],"new":[5.44,-1.96],"reason":"Move northeastern runways beyondbothKSNA full4nm northfans and recoverlocked2km inlandshore-endsetbacks; true4317ft andlockedlengths retained."}
```

</details>

<details>
<summary>631. KPGA 7/25 finalcentre</summary>

```json
{"phase":"1A.2","item":"KPGA 7/25 finalcentre","old":[5.68,-2.0],"new":[5.88,-1.2],"reason":"Move northeastern runways beyondbothKSNA full4nm northfans and recoverlocked2km inlandshore-endsetbacks; true4317ft andlockedlengths retained."}
```

</details>

<details>
<summary>632. Thomas Creek/centre</summary>

```json
{"phase":"1A.2","item":"Thomas Creek/centre","old":[6.1,-0.1],"new":[5.9,-0.4],"reason":"SeparatefullAP4/Mackay approaches from highThomas pad; restore >100m coarse-grid fringe without shrinkingprotectedfans."}
```

</details>

<details>
<summary>633. Mackay Bar/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"Mackay Bar/heading_true_deg","old":330,"new":350,"reason":"SeparatefullAP4/Mackay approaches from highThomas pad; restore >100m coarse-grid fringe without shrinkingprotectedfans."}
```

</details>

<details>
<summary>634. airports/KPGA/runways</summary>

```json
{"phase":"1A.2 finalwater/fieldallocation","item":"airports/KPGA/runways","old":[{"id":"16/34","centre":[5.44,-1.96],"heading_true_deg":170,"numbers_magnetic":"16/34","length_ft":5950,"width_ft":150,"surface":"asphalt","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":300}},{"id":"7/25","centre":[5.88,-1.2],"heading_true_deg":82,"numbers_magnetic":"7/25","length_ft":2201,"width_ft":75,"surface":"asphalt","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":300}}],"new":[{"id":"16/34","centre":[5.79,-1.06],"heading_true_deg":170,"numbers_magnetic":"16/34","length_ft":5950,"width_ft":150,"surface":"asphalt","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":300}},{"id":"7/25","centre":[5.95,-0.3],"heading_true_deg":82,"numbers_magnetic":"7/25","length_ft":2201,"width_ft":75,"surface":"asphalt","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":300}}],"reason":"Move finalPage runways northeast ofKSNA four-mile fans while preserving2km shore setbacks. Thomas/Dewey use separateapproach corridors;Cirque becomesa near-summit bowl clear ofThomas/AP10. TrueICAOdatums andlockedlengths unchanged."}
```

</details>

<details>
<summary>635. backcountry_strips/Thomas Creek/centre</summary>

```json
{"phase":"1A.2 finalwater/fieldallocation","item":"backcountry_strips/Thomas Creek/centre","old":[5.9,-0.4],"new":[4.15,0.35],"reason":"Move finalPage runways northeast ofKSNA four-mile fans while preserving2km shore setbacks. Thomas/Dewey use separateapproach corridors;Cirque becomesa near-summit bowl clear ofThomas/AP10. TrueICAOdatums andlockedlengths unchanged."}
```

</details>

<details>
<summary>636. backcountry_strips/Thomas Creek/heading_true_deg</summary>

```json
{"phase":"1A.2 finalwater/fieldallocation","item":"backcountry_strips/Thomas Creek/heading_true_deg","old":350,"new":100,"reason":"Move finalPage runways northeast ofKSNA four-mile fans while preserving2km shore setbacks. Thomas/Dewey use separateapproach corridors;Cirque becomesa near-summit bowl clear ofThomas/AP10. TrueICAOdatums andlockedlengths unchanged."}
```

</details>

<details>
<summary>637. backcountry_strips/Dewey Moore/centre</summary>

```json
{"phase":"1A.2 finalwater/fieldallocation","item":"backcountry_strips/Dewey Moore/centre","old":[5.3,0.25],"new":[5.75,0.45],"reason":"Move finalPage runways northeast ofKSNA four-mile fans while preserving2km shore setbacks. Thomas/Dewey use separateapproach corridors;Cirque becomesa near-summit bowl clear ofThomas/AP10. TrueICAOdatums andlockedlengths unchanged."}
```

</details>

<details>
<summary>638. backcountry_strips/Dewey Moore/heading_true_deg</summary>

```json
{"phase":"1A.2 finalwater/fieldallocation","item":"backcountry_strips/Dewey Moore/heading_true_deg","old":160,"new":210,"reason":"Move finalPage runways northeast ofKSNA four-mile fans while preserving2km shore setbacks. Thomas/Dewey use separateapproach corridors;Cirque becomesa near-summit bowl clear ofThomas/AP10. TrueICAOdatums andlockedlengths unchanged."}
```

</details>

<details>
<summary>639. backcountry_strips/Dewey Moore/one_way_true_deg</summary>

```json
{"phase":"1A.2 finalwater/fieldallocation","item":"backcountry_strips/Dewey Moore/one_way_true_deg","old":160,"new":210,"reason":"Move finalPage runways northeast ofKSNA four-mile fans while preserving2km shore setbacks. Thomas/Dewey use separateapproach corridors;Cirque becomesa near-summit bowl clear ofThomas/AP10. TrueICAOdatums andlockedlengths unchanged."}
```

</details>

<details>
<summary>640. water/Bighorn Cirque/centre</summary>

```json
{"phase":"1A.2 finalwater/fieldallocation","item":"water/Bighorn Cirque/centre","old":[0.65,0.55],"new":[0.3,0.1],"reason":"Move finalPage runways northeast ofKSNA four-mile fans while preserving2km shore setbacks. Thomas/Dewey use separateapproach corridors;Cirque becomesa near-summit bowl clear ofThomas/AP10. TrueICAOdatums andlockedlengths unchanged."}
```

</details>

<details>
<summary>641. canyons/C2/path</summary>

```json
{"phase":"1A.2 finalwater/fieldallocation","item":"canyons/C2/path","old":[[0.7,-1.8],[2.7,-0.4],[3.1,0.55],[3.5,2],[4,1.25],[5.1,1.3],[5.3,0.25],[3.6,-0.5],[5.9,-0.4]],"new":[[0.7,-1.8],[2.7,-0.4],[3.1,0.55],[3.5,2],[4,1.25],[5.1,1.3],[5.3,0.25],[3.6,-0.5],[4.15,0.35]],"reason":"Move finalPage runways northeast ofKSNA four-mile fans while preserving2km shore setbacks. Thomas/Dewey use separateapproach corridors;Cirque becomesa near-summit bowl clear ofThomas/AP10. TrueICAOdatums andlockedlengths unchanged."}
```

</details>

<details>
<summary>642. starts/KPGA:OnTheRunway/centre</summary>

```json
{"phase":"1A.2 finalwater/fieldallocation","item":"starts/KPGA:OnTheRunway/centre","old":[5.296431,-1.145781],"new":[5.646431,-0.245781],"reason":"Move finalPage runways northeast ofKSNA four-mile fans while preserving2km shore setbacks. Thomas/Dewey use separateapproach corridors;Cirque becomesa near-summit bowl clear ofThomas/AP10. TrueICAOdatums andlockedlengths unchanged."}
```

</details>

<details>
<summary>643. starts/KPGA:OnFinal/centre</summary>

```json
{"phase":"1A.2 finalwater/fieldallocation","item":"starts/KPGA:OnFinal/centre","old":[5.143621,-0.27915],"new":[5.493621,0.62085],"reason":"Move finalPage runways northeast ofKSNA four-mile fans while preserving2km shore setbacks. Thomas/Dewey use separateapproach corridors;Cirque becomesa near-summit bowl clear ofThomas/AP10. TrueICAOdatums andlockedlengths unchanged."}
```

</details>

<details>
<summary>644. starts/KPGA:InTheAir/centre</summary>

```json
{"phase":"1A.2 finalwater/fieldallocation","item":"starts/KPGA:InTheAir/centre","old":[5.108891,-0.082188],"new":[5.458891,0.817812],"reason":"Move finalPage runways northeast ofKSNA four-mile fans while preserving2km shore setbacks. Thomas/Dewey use separateapproach corridors;Cirque becomesa near-summit bowl clear ofThomas/AP10. TrueICAOdatums andlockedlengths unchanged."}
```

</details>

<details>
<summary>645. practice_lines/ST-KPGA/path</summary>

```json
{"phase":"1A.2 finalwater/fieldallocation","item":"practice_lines/ST-KPGA/path","old":[[5.283754,-1.378294],[5.492132,-2.560064]],"new":[[5.633754,-0.478294],[5.842132,-1.660064]],"reason":"Move finalPage runways northeast ofKSNA four-mile fans while preserving2km shore setbacks. Thomas/Dewey use separateapproach corridors;Cirque becomesa near-summit bowl clear ofThomas/AP10. TrueICAOdatums andlockedlengths unchanged."}
```

</details>

<details>
<summary>646. Dewey Moore easternterrace</summary>

```json
{"phase":"1A.2","item":"Dewey Moore easternterrace","old":[5.75,0.45],"new":[6.35,0.6],"reason":"SeparateDewey fromnewPage finalandairportcentreby>=0.8km, preserve2km inlandshoreendsetback andbothclear20:1 fans."}
```

</details>

<details>
<summary>647. backcountry_strips/Dewey Moore/centre</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Dewey Moore/centre","old":[6.35,0.6],"new":[-0.8,-2.8],"reason":"Finalcorridorallocation: westernDewey terrace clearsPage/AP4 andV2 lowMEA; Thomas/Sulphur bracketPage crossrunway fan; AP11 southeastfan avoidsThomas. Lengths/elevations andone-way designation retained."}
```

</details>

<details>
<summary>648. backcountry_strips/Dewey Moore/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Dewey Moore/heading_true_deg","old":210,"new":215,"reason":"Finalcorridorallocation: westernDewey terrace clearsPage/AP4 andV2 lowMEA; Thomas/Sulphur bracketPage crossrunway fan; AP11 southeastfan avoidsThomas. Lengths/elevations andone-way designation retained."}
```

</details>

<details>
<summary>649. backcountry_strips/Dewey Moore/one_way_true_deg</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Dewey Moore/one_way_true_deg","old":210,"new":215,"reason":"Finalcorridorallocation: westernDewey terrace clearsPage/AP4 andV2 lowMEA; Thomas/Sulphur bracketPage crossrunway fan; AP11 southeastfan avoidsThomas. Lengths/elevations andone-way designation retained."}
```

</details>

<details>
<summary>650. backcountry_strips/Thomas Creek/centre</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Thomas Creek/centre","old":[4.15,0.35],"new":[4.15,0.45],"reason":"Finalcorridorallocation: westernDewey terrace clearsPage/AP4 andV2 lowMEA; Thomas/Sulphur bracketPage crossrunway fan; AP11 southeastfan avoidsThomas. Lengths/elevations andone-way designation retained."}
```

</details>

<details>
<summary>651. backcountry_strips/Sulphur Creek/centre</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Sulphur Creek/centre","old":[3.6,-0.5],"new":[3.6,-0.8],"reason":"Finalcorridorallocation: westernDewey terrace clearsPage/AP4 andV2 lowMEA; Thomas/Sulphur bracketPage crossrunway fan; AP11 southeastfan avoidsThomas. Lengths/elevations andone-way designation retained."}
```

</details>

<details>
<summary>652. airparks/AP11/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"airparks/AP11/heading_true_deg","old":132,"new":125,"reason":"Finalcorridorallocation: westernDewey terrace clearsPage/AP4 andV2 lowMEA; Thomas/Sulphur bracketPage crossrunway fan; AP11 southeastfan avoidsThomas. Lengths/elevations andone-way designation retained."}
```

</details>

<details>
<summary>653. airparks/AP11/numbers_magnetic</summary>

```json
{"phase":"1A.2","item":"airparks/AP11/numbers_magnetic","old":"13/31","new":"12/30","reason":"Finalcorridorallocation: westernDewey terrace clearsPage/AP4 andV2 lowMEA; Thomas/Sulphur bracketPage crossrunway fan; AP11 southeastfan avoidsThomas. Lengths/elevations andone-way designation retained."}
```

</details>

<details>
<summary>654. KPGA crossrunway/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"KPGA crossrunway/heading_true_deg","old":82,"new":93,"reason":"Orient crossrunway fanbetweenSulphur andThomas;086magnetic gives09/27. Locked2201ft length/4317ft datum preserved; IFR approaches remainprimary16/34."}
```

</details>

<details>
<summary>655. KPGA crossrunway/id</summary>

```json
{"phase":"1A.2","item":"KPGA crossrunway/id","old":"7/25","new":"9/27","reason":"Orient crossrunway fanbetweenSulphur andThomas;086magnetic gives09/27. Locked2201ft length/4317ft datum preserved; IFR approaches remainprimary16/34."}
```

</details>

<details>
<summary>656. Powell eastbank gridmargin</summary>

```json
{"phase":"1A.2","item":"Powell eastbank gridmargin","old":[4.275,-4.525],"new":[4.475,-4.525],"reason":"Additional200m east creates fullcoarse-grid bankclearance toKSNA20L, retaininglocked3700ft level andbasindimensions."}
```

</details>

<details>
<summary>657. AP11 finaldatum</summary>

```json
{"phase":"1A.2","item":"AP11 finaldatum","old":4000,"new":4025,"reason":"Computedfeasibleinterval4016.91–4055.56ft after125true reheading;4025ft keepsAP1/PHDH gradesunderlocked35%. Fictionalfieldtarget."}
```

</details>

<details>
<summary>658. gunnery inventory finalbinding</summary>

```json
{"phase":"1A.2","item":"gunnery inventory finalbinding","old":{"id":"gunnery:targets","name":"five 30 m bullseyes + one 14 m target; three target drones","destination":"six moored target rafts per CZ1-CZ3; three-drone orbit","source":"Combat/Gunnery.cs CombatZone.BuildGroundTargets; CombatController","owner":"Codex target geometry; Claude AI/hit/event logic"},"new":{"id":"gunnery:targets","name":"Island ground bullseyes and target drones","destination":"3 groundtargets ineachCZ1-CZ3,2 inCZ5;3 dronesperzone; HOT3Dentry only","source":"Combat/Gunnery.cs CombatZone.BuildGroundTargets; CombatController","owner":"Codex target geometry; Claude AI/hit/event logic"},"reason":"Retain originalgunneryfeature withfinal11groundtargets/12drones acrossfourzones; removeobsoletefloatingtargetbinding."}
```

</details>

<details>
<summary>659. airparks/AP11/heading_true_deg</summary>

```json
{"phase":"1A.2 full-fan coordination","item":"airparks/AP11/heading_true_deg","old":125,"new":122,"reason":"FitPage crossrunway corridorbetweenSulphur/Thomas andfixedFrankChurch summit; AP11/Payette separationincludes100m gridfringe; keep0.8/1.5km spacing and35%grade with4035ft fictionalAP11 datum."}
```

</details>

<details>
<summary>660. airparks/AP11/numbers_magnetic</summary>

```json
{"phase":"1A.2 full-fan coordination","item":"airparks/AP11/numbers_magnetic","old":"12/30","new":"12/30","reason":"FitPage crossrunway corridorbetweenSulphur/Thomas andfixedFrankChurch summit; AP11/Payette separationincludes100m gridfringe; keep0.8/1.5km spacing and35%grade with4035ft fictionalAP11 datum."}
```

</details>

<details>
<summary>661. airparks/AP11/elevation_ft</summary>

```json
{"phase":"1A.2 full-fan coordination","item":"airparks/AP11/elevation_ft","old":4025,"new":4035,"reason":"FitPage crossrunway corridorbetweenSulphur/Thomas andfixedFrankChurch summit; AP11/Payette separationincludes100m gridfringe; keep0.8/1.5km spacing and35%grade with4035ft fictionalAP11 datum."}
```

</details>

<details>
<summary>662. backcountry_strips/Thomas Creek/centre</summary>

```json
{"phase":"1A.2 full-fan coordination","item":"backcountry_strips/Thomas Creek/centre","old":[4.15,0.45],"new":[4.6,0.6],"reason":"FitPage crossrunway corridorbetweenSulphur/Thomas andfixedFrankChurch summit; AP11/Payette separationincludes100m gridfringe; keep0.8/1.5km spacing and35%grade with4035ft fictionalAP11 datum."}
```

</details>

<details>
<summary>663. backcountry_strips/Sulphur Creek/centre</summary>

```json
{"phase":"1A.2 full-fan coordination","item":"backcountry_strips/Sulphur Creek/centre","old":[3.6,-0.8],"new":[3.4,-0.4],"reason":"FitPage crossrunway corridorbetweenSulphur/Thomas andfixedFrankChurch summit; AP11/Payette separationincludes100m gridfringe; keep0.8/1.5km spacing and35%grade with4035ft fictionalAP11 datum."}
```

</details>

<details>
<summary>664. backcountry_strips/Sulphur Creek/heading_true_deg</summary>

```json
{"phase":"1A.2 full-fan coordination","item":"backcountry_strips/Sulphur Creek/heading_true_deg","old":70,"new":78,"reason":"FitPage crossrunway corridorbetweenSulphur/Thomas andfixedFrankChurch summit; AP11/Payette separationincludes100m gridfringe; keep0.8/1.5km spacing and35%grade with4035ft fictionalAP11 datum."}
```

</details>

<details>
<summary>665. backcountry_strips/Big Creek/centre</summary>

```json
{"phase":"1A.2 full-fan coordination","item":"backcountry_strips/Big Creek/centre","old":[2.7,-0.4],"new":[2.5,-0.3],"reason":"FitPage crossrunway corridorbetweenSulphur/Thomas andfixedFrankChurch summit; AP11/Payette separationincludes100m gridfringe; keep0.8/1.5km spacing and35%grade with4035ft fictionalAP11 datum."}
```

</details>

<details>
<summary>666. water/Payette Lake/centre</summary>

```json
{"phase":"1A.2 full-fan coordination","item":"water/Payette Lake/centre","old":[-0.6,4.1],"new":[-0.6,3.9],"reason":"FitPage crossrunway corridorbetweenSulphur/Thomas andfixedFrankChurch summit; AP11/Payette separationincludes100m gridfringe; keep0.8/1.5km spacing and35%grade with4035ft fictionalAP11 datum."}
```

</details>

<details>
<summary>667. KPGA crossrunway finalaxis</summary>

```json
{"phase":"1A.2","item":"KPGA crossrunway finalaxis","old":93,"new":104,"reason":"Westfinal clears fixedFrankChurch summit;097magnetic gives10/28. Lockedlength2201ft/field4317ft unchanged."}
```

</details>

<details>
<summary>668. airparks/AP12/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"airparks/AP12/heading_true_deg","old":90,"new":87,"reason":"Separate complete final fans from all pad planes, elevated water and the McCall aerobatic box; Cabin direction reversed with its 3% design slope. Synchronize canyon endpoints and correctly sized practice-line shoulder offsets."}
```

</details>

<details>
<summary>669. airparks/AP12/numbers_magnetic</summary>

```json
{"phase":"1A.2","item":"airparks/AP12/numbers_magnetic","old":"8/26","new":"08/26","reason":"Separate complete final fans from all pad planes, elevated water and the McCall aerobatic box; Cabin direction reversed with its 3% design slope. Synchronize canyon endpoints and correctly sized practice-line shoulder offsets."}
```

</details>

<details>
<summary>670. backcountry_strips/Thomas Creek/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Thomas Creek/heading_true_deg","old":100,"new":98,"reason":"Separate complete final fans from all pad planes, elevated water and the McCall aerobatic box; Cabin direction reversed with its 3% design slope. Synchronize canyon endpoints and correctly sized practice-line shoulder offsets."}
```

</details>

<details>
<summary>671. backcountry_strips/Cabin Creek/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Cabin Creek/heading_true_deg","old":0,"new":179,"reason":"Separate complete final fans from all pad planes, elevated water and the McCall aerobatic box; Cabin direction reversed with its 3% design slope. Synchronize canyon endpoints and correctly sized practice-line shoulder offsets."}
```

</details>

<details>
<summary>672. backcountry_strips/Cabin Creek/one_way_true_deg</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Cabin Creek/one_way_true_deg","old":0,"new":179,"reason":"Separate complete final fans from all pad planes, elevated water and the McCall aerobatic box; Cabin direction reversed with its 3% design slope. Synchronize canyon endpoints and correctly sized practice-line shoulder offsets."}
```

</details>

<details>
<summary>673. backcountry_strips/Mackay Bar/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Mackay Bar/heading_true_deg","old":350,"new":333,"reason":"Separate complete final fans from all pad planes, elevated water and the McCall aerobatic box; Cabin direction reversed with its 3% design slope. Synchronize canyon endpoints and correctly sized practice-line shoulder offsets."}
```

</details>

<details>
<summary>674. canyons/C2/path</summary>

```json
{"phase":"1A.2","item":"canyons/C2/path","old":[[0.7,-1.8],[2.7,-0.4],[3.1,0.55],[3.5,2],[4,1.25],[5.1,1.3],[3.6,-0.8],[4.15,0.45],[-0.8,-2.8]],"new":[[0.7,-1.8],[2.5,-0.3],[3.1,0.55],[3.5,2],[4,1.25],[5.1,1.3],[3.4,-0.4],[4.6,0.6],[-0.8,-2.8]],"reason":"Separate complete final fans from all pad planes, elevated water and the McCall aerobatic box; Cabin direction reversed with its 3% design slope. Synchronize canyon endpoints and correctly sized practice-line shoulder offsets."}
```

</details>

<details>
<summary>675. practice_lines/ST-PATK/path</summary>

```json
{"phase":"1A.2","item":"practice_lines/ST-PATK/path","old":[[-6.57548,4.436587],[-6.030691,5.505795]],"new":[[-6.536164,4.513748],[-6.070007,5.428634]],"reason":"Separate complete final fans from all pad planes, elevated water and the McCall aerobatic box; Cabin direction reversed with its 3% design slope. Synchronize canyon endpoints and correctly sized practice-line shoulder offsets."}
```

</details>

<details>
<summary>676. practice_lines/ST-3U2/path</summary>

```json
{"phase":"1A.2","item":"practice_lines/ST-3U2/path","old":[[1.893042,-0.262121],[1.643548,-1.435898]],"new":[[1.871868,-0.261736],[1.664722,-1.236284]],"reason":"Separate complete final fans from all pad planes, elevated water and the McCall aerobatic box; Cabin direction reversed with its 3% design slope. Synchronize canyon endpoints and correctly sized practice-line shoulder offsets."}
```

</details>

<details>
<summary>677. Mackay Bar final heading</summary>

```json
{"item":"Mackay Bar final heading","old":333,"new":330,"reason":"Separate full fan from final rotated Thomas Creek pad, preserving1.8% slope direction."}
```

</details>

<details>
<summary>678. airparks/AP12/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"airparks/AP12/heading_true_deg","old":87,"new":112,"reason":"Full terrain sweep: clear protected peaks/Drake roof, avoid coarse-grid terrace spill, separate different-level lake footprints and lake-bank fans."}
```

</details>

<details>
<summary>679. airparks/AP12/numbers_magnetic</summary>

```json
{"phase":"1A.2","item":"airparks/AP12/numbers_magnetic","old":"08/26","new":"11/29","reason":"Full terrain sweep: clear protected peaks/Drake roof, avoid coarse-grid terrace spill, separate different-level lake footprints and lake-bank fans."}
```

</details>

<details>
<summary>680. backcountry_strips/Thomas Creek/centre</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Thomas Creek/centre","old":[4.6,0.6],"new":[4.4,0.5],"reason":"Full terrain sweep: clear protected peaks/Drake roof, avoid coarse-grid terrace spill, separate different-level lake footprints and lake-bank fans."}
```

</details>

<details>
<summary>681. backcountry_strips/Thomas Creek/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Thomas Creek/heading_true_deg","old":98,"new":94,"reason":"Full terrain sweep: clear protected peaks/Drake roof, avoid coarse-grid terrace spill, separate different-level lake footprints and lake-bank fans."}
```

</details>

<details>
<summary>682. backcountry_strips/Sulphur Creek/heading_true_deg</summary>

```json
{"phase":"1A.2","item":"backcountry_strips/Sulphur Creek/heading_true_deg","old":78,"new":70,"reason":"Full terrain sweep: clear protected peaks/Drake roof, avoid coarse-grid terrace spill, separate different-level lake footprints and lake-bank fans."}
```

</details>

<details>
<summary>683. water/Bighorn Outlet/centre</summary>

```json
{"phase":"1A.2","item":"water/Bighorn Outlet/centre","old":[0,2.2],"new":[0,2.4],"reason":"Full terrain sweep: clear protected peaks/Drake roof, avoid coarse-grid terrace spill, separate different-level lake footprints and lake-bank fans."}
```

</details>

<details>
<summary>684. water/Bighorn Shelf/centre</summary>

```json
{"phase":"1A.2","item":"water/Bighorn Shelf/centre","old":[0.15,3.5],"new":[0.3,3.5],"reason":"Full terrain sweep: clear protected peaks/Drake roof, avoid coarse-grid terrace spill, separate different-level lake footprints and lake-bank fans."}
```

</details>

<details>
<summary>685. canyons/C2/path</summary>

```json
{"phase":"1A.2","item":"canyons/C2/path","old":[[0.7,-1.8],[2.5,-0.3],[3.1,0.55],[3.5,2],[4,1.25],[5.1,1.3],[3.4,-0.4],[4.6,0.6],[-0.8,-2.8]],"new":[[0.7,-1.8],[2.5,-0.3],[3.1,0.55],[3.5,2],[4,1.25],[5.1,1.3],[3.4,-0.4],[4.4,0.5],[-0.8,-2.8]],"reason":"Full terrain sweep: clear protected peaks/Drake roof, avoid coarse-grid terrace spill, separate different-level lake footprints and lake-bank fans."}
```

</details>

<details>
<summary>686. AP12/heading_true_deg</summary>

```json
{"item":"AP12/heading_true_deg","old":112,"new":65,"reason":"Separate whole final footprints from terrain headwalls, Drake roof and neighbouring pads."}
```

</details>

<details>
<summary>687. AP12/numbers_magnetic</summary>

```json
{"item":"AP12/numbers_magnetic","old":"11/29","new":"06/24","reason":"Separate whole final footprints from terrain headwalls, Drake roof and neighbouring pads."}
```

</details>

<details>
<summary>688. Thomas Creek/heading_true_deg</summary>

```json
{"item":"Thomas Creek/heading_true_deg","old":94,"new":98,"reason":"Separate whole final footprints from terrain headwalls, Drake roof and neighbouring pads."}
```

</details>

<details>
<summary>689. Sulphur Creek/heading_true_deg</summary>

```json
{"item":"Sulphur Creek/heading_true_deg","old":70,"new":73,"reason":"Separate whole final footprints from terrain headwalls, Drake roof and neighbouring pads."}
```

</details>

<details>
<summary>690. W1 endpoints</summary>

```json
{"item":"W1 endpoints","old":[[1.55,0.25],[2.2,0.3]],"new":[[6.8,-3.8],[7.2,-3.75]],"reason":"Relocate charted40m wire clear of Indian Creek full final; shorten span while retaining sag/marker/lights."}
```

</details>

<details>
<summary>691. Sulphur Creek/heading_true_deg</summary>

```json
{"item":"Sulphur Creek/heading_true_deg","old":73,"new":70,"reason":"Turn west final away from fixed Chugach peak; raise fictional bench35ft to clear higher Thomas pad in opposite fan."}
```

</details>

<details>
<summary>692. Sulphur Creek/elevation_ft</summary>

```json
{"item":"Sulphur Creek/elevation_ft","old":4800,"new":4835,"reason":"Turn west final away from fixed Chugach peak; raise fictional bench35ft to clear higher Thomas pad in opposite fan."}
```

</details>

<details>
<summary>693. KPGA crossrunway heading</summary>

```json
{"item":"KPGA crossrunway heading","old":104,"new":102,"reason":"Separate full west fan and100m grid halo from Thomas terrace while retaining summit/water/box clearance; unchanged2201ft and4317ft."}
```

</details>

<details>
<summary>694. Thomas Creek/length_m</summary>

```json
{"item":"Thomas Creek/length_m","old":1220,"new":1000,"reason":"Use nonlocked strip design targets to clear full protected fans including100m grid-edge spill; keep usable strip and60m shoulders. AllICAO locked lengths/datums remain unchanged."}
```

</details>

<details>
<summary>695. Sulphur Creek/elevation_ft</summary>

```json
{"item":"Sulphur Creek/elevation_ft","old":4835,"new":4830,"reason":"Use nonlocked strip design targets to clear full protected fans including100m grid-edge spill; keep usable strip and60m shoulders. AllICAO locked lengths/datums remain unchanged."}
```

</details>

<details>
<summary>696. Thomas Creek/centre</summary>

```json
{"item":"Thomas Creek/centre","old":[4.4,0.5],"new":[4.4,0.65],"reason":"Increase terrain-grid clearance between Page final and Thomas pad while retaining original1220m strip;100m Soldier northshift retains0.8km centre spacing."}
```

</details>

<details>
<summary>697. Thomas Creek/length_m</summary>

```json
{"item":"Thomas Creek/length_m","old":1000,"new":1220,"reason":"Increase terrain-grid clearance between Page final and Thomas pad while retaining original1220m strip;100m Soldier northshift retains0.8km centre spacing."}
```

</details>

<details>
<summary>698. Soldier Bar/centre</summary>

```json
{"item":"Soldier Bar/centre","old":[4,1.25],"new":[4,1.35],"reason":"Increase terrain-grid clearance between Page final and Thomas pad while retaining original1220m strip;100m Soldier northshift retains0.8km centre spacing."}
```

</details>

<details>
<summary>699. Soldier Bar heading and one-way course</summary>

```json
{"item":"Soldier Bar heading and one-way course","old":88,"new":86,"reason":"Keep west approach south of full high-lake bank after100m north relocation; preserve strip/slope."}
```

</details>

<details>
<summary>700. W4 endpoints</summary>

```json
{"item":"W4 endpoints","old":[[-1.5,-4.5],[-0.3,-4.5]],"new":[[5.7,-5.5],[6.1,-5.5]],"reason":"Final AP12 reheading exposed conservative wire penetration. Relocate charted18m wire to separate400m southern span with no intersection with any full final fan and5065ft minimum known airway margin including sag."}
```

</details>

<details>
<summary>701. PALH/16W/34W/numbers_magnetic</summary>

```json
{"item":"PALH/16W/34W/numbers_magnetic","old":"8W/26W","new":"16W/34W","reason":"Synchronize explicit magnetic-number metadata with the final charted runway ID after reheading; position/true heading/length/elevation unchanged."}
```

</details>

<details>
<summary>702. PALH/16/34/numbers_magnetic</summary>

```json
{"item":"PALH/16/34/numbers_magnetic","old":"15/33","new":"16/34","reason":"Synchronize explicit magnetic-number metadata with the final charted runway ID after reheading; position/true heading/length/elevation unchanged."}
```

</details>

<details>
<summary>703. KPGA/10/28/numbers_magnetic</summary>

```json
{"item":"KPGA/10/28/numbers_magnetic","old":"7/25","new":"10/28","reason":"Synchronize explicit magnetic-number metadata with the final charted runway ID after reheading; position/true heading/length/elevation unchanged."}
```

</details>

<details>
<summary>704. SITX/35/17/numbers_magnetic</summary>

```json
{"item":"SITX/35/17/numbers_magnetic","old":null,"new":"35/17","reason":"Synchronize explicit magnetic-number metadata with the final charted runway ID after reheading; position/true heading/length/elevation unchanged."}
```

</details>

<details>
<summary>705. TERN/35/17/numbers_magnetic</summary>

```json
{"item":"TERN/35/17/numbers_magnetic","old":null,"new":"35/17","reason":"Synchronize explicit magnetic-number metadata with the final charted runway ID after reheading; position/true heading/length/elevation unchanged."}
```

</details>

<details>
<summary>706. NHVN/8/26/numbers_magnetic</summary>

```json
{"item":"NHVN/8/26/numbers_magnetic","old":null,"new":"8/26","reason":"Synchronize explicit magnetic-number metadata with the final charted runway ID after reheading; position/true heading/length/elevation unchanged."}
```

</details>

<details>
<summary>707. KITE/11/29/numbers_magnetic</summary>

```json
{"item":"KITE/11/29/numbers_magnetic","old":null,"new":"11/29","reason":"Synchronize explicit magnetic-number metadata with the final charted runway ID after reheading; position/true heading/length/elevation unchanged."}
```

</details>

<details>
<summary>708. RIME/5/23/numbers_magnetic</summary>

```json
{"item":"RIME/5/23/numbers_magnetic","old":null,"new":"5/23","reason":"Synchronize explicit magnetic-number metadata with the final charted runway ID after reheading; position/true heading/length/elevation unchanged."}
```

</details>

<details>
<summary>709. /world/aerobatic_boxes/AB1/ceiling_msl_ft_target</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/aerobatic_boxes/AB1/ceiling_msl_ft_target","old":3609,"new":3608.924,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>710. /world/aerobatic_boxes/AB1/corner_buoys</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/aerobatic_boxes/AB1/corner_buoys","old":null,"new":[{"centre":[6.4,7.1],"colour":"orange","moored":true},{"centre":[6.4,8.1],"colour":"orange","moored":true},{"centre":[7.4,7.1],"colour":"orange","moored":true},{"centre":[7.4,8.1],"colour":"orange","moored":true}],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>711. /world/aerobatic_boxes/AB1/floor_msl_ft_target</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/aerobatic_boxes/AB1/floor_msl_ft_target","old":328,"new":328.084,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>712. /world/aerobatic_boxes/AB1/judge_raft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/aerobatic_boxes/AB1/judge_raft","old":null,"new":{"id":"AB1-JR","centre":[6.9,6.95],"size_m":[12,8],"deck_msl_m":0.5,"moored":true,"colour":"white","role":"judges reference raft, separatefrom9 standardcross/L/T markerrafts"},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>713. /world/aerobatic_boxes/AB1/judge_raft_count</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/aerobatic_boxes/AB1/judge_raft_count","old":null,"new":1,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>714. /world/aerobatic_boxes/AB1/judges_line</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/aerobatic_boxes/AB1/judges_line","old":null,"new":{"centre":[6.9,6.95],"length_m":1000,"orientation_true_deg":90,"marker":"moored judge-reference raft on line; matching shore reference aligned along airport-facing axis","shore_extension_path":[[6.9,6.95],[3.1,6.95]]},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>715. /world/aerobatic_boxes/AB1/judges_shore_marker_status</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/aerobatic_boxes/AB1/judges_shore_marker_status","old":"target: align shore reference with judges line; exact intersection pending shore geometry review","new":"resolved: line raft plus aligned shore reference; no false claim that offshore judges line intersects land","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>716. /world/aerobatic_boxes/AB1/marker_raft_count</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/aerobatic_boxes/AB1/marker_raft_count","old":null,"new":9,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>717. /world/aerobatic_boxes/AB1/rafts</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/aerobatic_boxes/AB1/rafts","old":null,"new":[{"id":"AB1-R1","shape":"cross","centre":[6.9,7.6],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB1-R2","shape":"L","centre":[6.4,7.1],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB1-R3","shape":"L","centre":[6.4,8.1],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB1-R4","shape":"L","centre":[7.4,7.1],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB1-R5","shape":"L","centre":[7.4,8.1],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB1-R6","shape":"T","centre":[6.9,8.1],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB1-R7","shape":"T","centre":[6.9,7.1],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB1-R8","shape":"T","centre":[7.4,7.6],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB1-R9","shape":"T","centre":[6.4,7.6],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"}],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>718. /world/aerobatic_boxes/AB1/shore_reference_marker</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/aerobatic_boxes/AB1/shore_reference_marker","old":null,"new":{"centre":[3.1,6.95],"meaning":"Dryshore marker on actual extended judge-line axis; judging line remains150m outside airport-facing side."},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>719. /world/aerobatic_boxes/AB1/total_raft_count</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/aerobatic_boxes/AB1/total_raft_count","old":null,"new":10,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>720. /world/aerobatic_boxes/AB2/ceiling_msl_ft_target</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/aerobatic_boxes/AB2/ceiling_msl_ft_target","old":3609,"new":3608.924,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>721. /world/aerobatic_boxes/AB2/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/aerobatic_boxes/AB2/centre","old":[-10.1,-3.6],"new":[-12.2,-6.3],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>722. /world/aerobatic_boxes/AB2/corner_buoys</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/aerobatic_boxes/AB2/corner_buoys","old":null,"new":[{"centre":[-12.7,-6.8],"colour":"orange","moored":true},{"centre":[-12.7,-5.8],"colour":"orange","moored":true},{"centre":[-11.7,-6.8],"colour":"orange","moored":true},{"centre":[-11.7,-5.8],"colour":"orange","moored":true}],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>723. /world/aerobatic_boxes/AB2/floor_msl_ft_target</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/aerobatic_boxes/AB2/floor_msl_ft_target","old":328,"new":328.084,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>724. /world/aerobatic_boxes/AB2/judge_raft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/aerobatic_boxes/AB2/judge_raft","old":null,"new":{"id":"AB2-JR","centre":[-11.55,-6.3],"size_m":[12,8],"deck_msl_m":0.5,"moored":true,"colour":"white","role":"judges reference raft, separatefrom9 standardcross/L/T markerrafts"},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>725. /world/aerobatic_boxes/AB2/judge_raft_count</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/aerobatic_boxes/AB2/judge_raft_count","old":null,"new":1,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>726. /world/aerobatic_boxes/AB2/judges_line</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/aerobatic_boxes/AB2/judges_line","old":null,"new":{"centre":[-11.55,-6.3],"length_m":1000,"orientation_true_deg":0,"marker":"moored judge-reference raft on line; matching shore reference aligned along airport-facing axis","shore_extension_path":[[-11.55,-6.3],[-11.55,-5.65]]},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>727. /world/aerobatic_boxes/AB2/judges_shore_marker_status</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/aerobatic_boxes/AB2/judges_shore_marker_status","old":null,"new":"resolved: line raft plus aligned shore reference; no false claim that offshore judges line intersects land","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>728. /world/aerobatic_boxes/AB2/marker_raft_count</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/aerobatic_boxes/AB2/marker_raft_count","old":null,"new":9,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>729. /world/aerobatic_boxes/AB2/rafts</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/aerobatic_boxes/AB2/rafts","old":null,"new":[{"id":"AB2-R1","shape":"cross","centre":[-12.2,-6.3],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R2","shape":"L","centre":[-12.7,-6.8],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R3","shape":"L","centre":[-12.7,-5.8],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R4","shape":"L","centre":[-11.7,-6.8],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R5","shape":"L","centre":[-11.7,-5.8],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R6","shape":"T","centre":[-12.2,-5.8],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R7","shape":"T","centre":[-12.2,-6.8],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R8","shape":"T","centre":[-11.7,-6.3],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"},{"id":"AB2-R9","shape":"T","centre":[-12.7,-6.3],"deck_msl_m":0.5,"moored":true,"dimensions":"cross 40 m arms x4 m; L/T 30 m legs x4 m"}],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>730. /world/aerobatic_boxes/AB2/shore_reference_marker</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/aerobatic_boxes/AB2/shore_reference_marker","old":null,"new":{"centre":[-11.55,-5.65],"meaning":"Dryshore marker on actual extended judge-line axis; judging line remains150m outside airport-facing side."},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>731. /world/aerobatic_boxes/AB2/total_raft_count</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/aerobatic_boxes/AB2/total_raft_count","old":null,"new":10,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>732. /world/aerobatic_boxes/AB3/ceiling_msl_ft_target</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/aerobatic_boxes/AB3/ceiling_msl_ft_target","old":8609,"new":8608.924,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>733. /world/aerobatic_boxes/AB3/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/aerobatic_boxes/AB3/centre","old":[-2.5,4],"new":[-1.8,2.8],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>734. /world/aerobatic_boxes/AB3/floor_msl_ft_target</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/aerobatic_boxes/AB3/floor_msl_ft_target","old":5328,"new":5328.084,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>735. /world/airparks/AP1/elevation_ft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP1/elevation_ft","old":5024,"new":4925,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>736. /world/airparks/AP1/heading_true_deg</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP1/heading_true_deg","old":167,"new":90,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>737. /world/airparks/AP1/numbers_magnetic</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP1/numbers_magnetic","old":"16/34","new":"8/26","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>738. /world/airparks/AP1/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP1/shore_class","old":null,"new":"inland","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>739. /world/airparks/AP10/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP10/centre","old":[2.7,1.2],"new":[1.8,1.5],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>740. /world/airparks/AP10/heading_true_deg</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP10/heading_true_deg","old":17,"new":25,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>741. /world/airparks/AP10/numbers_magnetic</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP10/numbers_magnetic","old":"1/19","new":"2/20","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>742. /world/airparks/AP10/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP10/shore_class","old":null,"new":"inland","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>743. /world/airparks/AP11/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP11/centre","old":[0.8,4.6],"new":[0,4.6],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>744. /world/airparks/AP11/elevation_ft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP11/elevation_ft","old":5024,"new":4035,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>745. /world/airparks/AP11/heading_true_deg</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP11/heading_true_deg","old":147,"new":122,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>746. /world/airparks/AP11/numbers_magnetic</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP11/numbers_magnetic","old":"14/32","new":"12/30","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>747. /world/airparks/AP11/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP11/shore_class","old":null,"new":"inland","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>748. /world/airparks/AP12/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP12/centre","old":[1.8,-4.4],"new":[1.4,-3.2],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>749. /world/airparks/AP12/elevation_ft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP12/elevation_ft","old":3800,"new":4300,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>750. /world/airparks/AP12/heading_true_deg</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP12/heading_true_deg","old":167,"new":65,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>751. /world/airparks/AP12/numbers_magnetic</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP12/numbers_magnetic","old":"16/34","new":"06/24","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>752. /world/airparks/AP12/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP12/shore_class","old":null,"new":"inland","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>753. /world/airparks/AP13/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP13/centre","old":[5.8,-2],"new":[5.5,-5],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>754. /world/airparks/AP13/elevation_ft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP13/elevation_ft","old":4500,"new":4000,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>755. /world/airparks/AP13/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP13/shore_class","old":null,"new":"coastal","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>756. /world/airparks/AP14/heading_true_deg</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP14/heading_true_deg","old":27,"new":75,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>757. /world/airparks/AP14/numbers_magnetic</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP14/numbers_magnetic","old":"2/20","new":"7/25","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>758. /world/airparks/AP14/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP14/shore_class","old":null,"new":"coastal","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>759. /world/airparks/AP2/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP2/centre","old":[-6.9,6.8],"new":[-6.9,6.6],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>760. /world/airparks/AP2/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP2/shore_class","old":null,"new":"coastal","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>761. /world/airparks/AP3/heading_true_deg</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP3/heading_true_deg","old":180,"new":164,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>762. /world/airparks/AP3/numbers_magnetic</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP3/numbers_magnetic","old":"17/35","new":"16/34","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>763. /world/airparks/AP3/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP3/shore_class","old":null,"new":"inland","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>764. /world/airparks/AP4/elevation_ft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP4/elevation_ft","old":1350,"new":900,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>765. /world/airparks/AP4/heading_true_deg</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP4/heading_true_deg","old":67,"new":144,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>766. /world/airparks/AP4/numbers_magnetic</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP4/numbers_magnetic","old":"6/24","new":"14/32","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>767. /world/airparks/AP4/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP4/shore_class","old":null,"new":"coastal","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>768. /world/airparks/AP5/elevation_ft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP5/elevation_ft","old":900,"new":1200,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>769. /world/airparks/AP5/heading_true_deg</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP5/heading_true_deg","old":40,"new":335,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>770. /world/airparks/AP5/numbers_magnetic</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP5/numbers_magnetic","old":"3/21","new":"33/15","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>771. /world/airparks/AP5/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP5/shore_class","old":null,"new":"coastal","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>772. /world/airparks/AP6/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP6/centre","old":[4.2,2.6],"new":[5.1,4.3],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>773. /world/airparks/AP6/heading_true_deg</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP6/heading_true_deg","old":60,"new":110,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>774. /world/airparks/AP6/numbers_magnetic</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP6/numbers_magnetic","old":"5/23","new":"10/28","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>775. /world/airparks/AP6/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP6/shore_class","old":null,"new":"inland","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>776. /world/airparks/AP7/heading_true_deg</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP7/heading_true_deg","old":97,"new":94,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>777. /world/airparks/AP7/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP7/shore_class","old":null,"new":"coastal","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>778. /world/airparks/AP8/float_pond/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP8/float_pond/centre","old":[-8,3.7],"new":[-7.8,3.7],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>779. /world/airparks/AP8/float_pond/heading_true_deg</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP8/float_pond/heading_true_deg","old":null,"new":0,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>780. /world/airparks/AP8/float_pond/note</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP8/float_pond/note","old":null,"new":"600x120m rectangular floatlane plus60m shoulders, grass runway remainsseparate; sameAP8 site, no newstart.","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>781. /world/airparks/AP8/float_pond/numbers_magnetic</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP8/float_pond/numbers_magnetic","old":null,"new":"35W/17W","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>782. /world/airparks/AP8/float_pond/pad</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP8/float_pond/pad","old":null,"new":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>783. /world/airparks/AP8/float_pond/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP8/float_pond/shore_class","old":null,"new":"coastal","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>784. /world/airparks/AP8/float_pond/slope_percent</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP8/float_pond/slope_percent","old":null,"new":0,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>785. /world/airparks/AP8/float_pond/surface</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP8/float_pond/surface","old":null,"new":"water","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>786. /world/airparks/AP8/float_pond/water_bed_offset_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP8/float_pond/water_bed_offset_m","old":null,"new":3,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>787. /world/airparks/AP8/float_pond/water_surface_ft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP8/float_pond/water_surface_ft","old":null,"new":250,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>788. /world/airparks/AP8/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP8/shore_class","old":null,"new":"coastal","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>789. /world/airparks/AP9/elevation_ft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP9/elevation_ft","old":2900,"new":3200,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>790. /world/airparks/AP9/heading_true_deg</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP9/heading_true_deg","old":47,"new":103,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>791. /world/airparks/AP9/numbers_magnetic</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP9/numbers_magnetic","old":"4/22","new":"10/28","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>792. /world/airparks/AP9/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airparks/AP9/shore_class","old":null,"new":"coastal","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>793. /world/airports/3U2/runways/19/1/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airports/3U2/runways/19/1/centre","old":[1.82,-0.86],"new":[1.82,-0.76],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>794. /world/airports/3U2/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airports/3U2/shore_class","old":null,"new":"inland","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>795. /world/airports/DRAKE/runways/17/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airports/DRAKE/runways/17/centre","old":[-4.9,-3.98],"new":[-4.9,-4.18],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>796. /world/airports/DRAKE/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airports/DRAKE/shore_class","old":null,"new":"inland","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>797. /world/airports/KAVX/runways/5/23/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airports/KAVX/runways/5/23/centre","old":[3.6,-14.4],"new":[3.57,-14.36],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>798. /world/airports/KAVX/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airports/KAVX/shore_class","old":null,"new":"coastal","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>799. /world/airports/KITE</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airports/KITE","old":null,"new":{"id":"KITE","name":"Kite Anchorage","elevation_ft":50,"start_group":"non-start","shore_class":"coastal","island":"OI4","runways":[{"id":"11/29","centre":[8,15.3],"heading_true_deg":118,"length_m":750,"width_m":24,"surface":"gravel","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":180},"numbers_magnetic":"11/29"}],"note":"Fictional outer-island field; full two-end protection retained. Under MOA raised-floor traffic reservation, no new start."},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>800. /world/airports/KMEV/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airports/KMEV/shore_class","old":null,"new":"inland","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>801. /world/airports/KMYL/runways/17/35/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airports/KMYL/runways/17/35/centre","old":[-0.58,2.78],"new":[-0.88,2.78],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>802. /world/airports/KMYL/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airports/KMYL/shore_class","old":null,"new":"inland","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>803. /world/airports/KPGA/runways/10/28</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airports/KPGA/runways/10/28","old":null,"new":{"id":"10/28","centre":[5.95,-0.3],"heading_true_deg":102,"numbers_magnetic":"10/28","length_ft":2201,"width_ft":75,"surface":"asphalt","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":300}},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>804. /world/airports/KPGA/runways/16/34/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airports/KPGA/runways/16/34/centre","old":[3.74,-3.26],"new":[5.79,-1.06],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>805. /world/airports/KPGA/runways/7/25</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airports/KPGA/runways/7/25","old":{"id":"7/25","centre":[4.18,-2.5],"heading_true_deg":82,"numbers_magnetic":"7/25","length_ft":2201,"width_ft":75,"surface":"asphalt","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":300}},"new":null,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>806. /world/airports/KPGA/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airports/KPGA/shore_class","old":null,"new":"inland","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>807. /world/airports/KSNA/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airports/KSNA/shore_class","old":null,"new":"coastal","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>808. /world/airports/NHVN</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airports/NHVN","old":null,"new":{"id":"NHVN","name":"North Haven","elevation_ft":55,"elevation_status":"fictional design target","start_group":"outer island non-start","runways":[{"id":"8/26","centre":[0,10.3],"heading_true_deg":90,"length_ft":2952.755906,"width_ft":98.425197,"surface":"gravel","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"shore_class":"coastal","island":"OI3","numbers_magnetic":"8/26"}],"shore_class":"coastal"},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>809. /world/airports/PALH/runways/15/33</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airports/PALH/runways/15/33","old":{"id":"15/33","centre":[-7.85,-1.25],"heading_true_deg":153,"numbers_magnetic":"15/33","length_ft":2200,"width_ft":75,"surface":"gravel","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":300}},"new":null,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>810. /world/airports/PALH/runways/16/34</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airports/PALH/runways/16/34","old":null,"new":{"id":"16/34","centre":[-9.5,-1.3],"heading_true_deg":165,"numbers_magnetic":"16/34","length_ft":2200,"width_ft":75,"surface":"gravel","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":300}},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>811. /world/airports/PALH/runways/16W/34W</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airports/PALH/runways/16W/34W","old":null,"new":{"id":"16W/34W","centre":[-10.18,-1.8],"heading_true_deg":165,"numbers_magnetic":"16W/34W","length_ft":4541,"width_ft":188,"surface":"water","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":300}},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>812. /world/airports/PALH/runways/8W/26W</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airports/PALH/runways/8W/26W","old":{"id":"8W/26W","centre":[-7.96,-1.8],"heading_true_deg":90,"numbers_magnetic":"8W/26W","length_ft":4541,"width_ft":188,"surface":"water","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":300}},"new":null,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>813. /world/airports/PALH/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airports/PALH/shore_class","old":null,"new":"coastal","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>814. /world/airports/PANC/runways/16/34/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airports/PANC/runways/16/34/centre","old":[-8.64,-3.07],"new":[-10.24,-3.07],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>815. /world/airports/PANC/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airports/PANC/shore_class","old":null,"new":"coastal","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>816. /world/airports/PATK/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airports/PATK/shore_class","old":null,"new":"coastal","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>817. /world/airports/PHDH/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airports/PHDH/shore_class","old":null,"new":"coastal","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>818. /world/airports/RIME</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airports/RIME","old":null,"new":{"id":"RIME","name":"Rime Harbour","elevation_ft":70,"start_group":"non-start","shore_class":"coastal","island":"OI5","runways":[{"id":"5/23","centre":[-9.5,14.8],"heading_true_deg":56,"length_m":750,"width_m":24,"surface":"gravel","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":180},"numbers_magnetic":"5/23"}],"note":"Fictional outer-island field; full two-end protection retained. Under MOA raised-floor traffic reservation, no new start."},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>819. /world/airports/SITX</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airports/SITX","old":null,"new":{"id":"SITX","name":"Sitka Sound waterfront","elevation_ft":30,"elevation_status":"fictional design target","start_group":"outer island non-start","runways":[{"id":"35/17","centre":[-15,1.1],"heading_true_deg":0,"length_ft":3608.923885,"width_ft":98.425197,"surface":"asphalt","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"shore_class":"coastal","island":"OI1","numbers_magnetic":"35/17"}],"shore_class":"coastal"},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>820. /world/airports/TERN</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airports/TERN","old":null,"new":{"id":"TERN","name":"Tern perimeter","elevation_ft":45,"elevation_status":"fictional design target","start_group":"outer island non-start","runways":[{"id":"35/17","centre":[11.3,2],"heading_true_deg":0,"length_ft":2952.755906,"width_ft":98.425197,"surface":"gravel","slope_percent":0,"pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"shore_class":"coastal","island":"OI2","numbers_magnetic":"35/17"}],"shore_class":"coastal"},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>821. /world/airspace_reservations</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/airspace_reservations","old":null,"new":{"owner":"Codex world design proposals for Claude acceptance; not published IFR","unknown_geometry":["T201 fix positions/segment MEAs","11 non-KSNA missed approaches and holds","BALBO hold speed/turn radius","KAVX circling area","Reconcile relocatedKPGA/Powell world withsuppliedPGA station/procedure inputs afterG1a; IFR JSON remainsunchanged"],"rule":"No full-airspace PASS until Claude supplies missingproceduregeometry. LandMOA and other zones stay1.85km+0.20km from fullknownfinal boundaries and2nm airway/perimeter terrain swaths; ownairport overlays separate traffic<=3000ft fromcombat>=5000ft.","new_outer_procedures":"Claude to fit perimeter instrument routes in protected reservations; no additions to ifr section","box_final_guard_km":0.15},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>822. /world/backcountry_strips/Big Creek/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Big Creek/centre","old":[3,-0.8],"new":[2.5,-0.3],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>823. /world/backcountry_strips/Big Creek/elevation_ft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Big Creek/elevation_ft","old":7000,"new":4800,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>824. /world/backcountry_strips/Big Creek/heading_true_deg</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Big Creek/heading_true_deg","old":108,"new":0,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>825. /world/backcountry_strips/Big Creek/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Big Creek/shore_class","old":null,"new":"inland","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>826. /world/backcountry_strips/Cabin Creek/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Cabin Creek/centre","old":[3.2,0.3],"new":[3.1,0.55],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>827. /world/backcountry_strips/Cabin Creek/elevation_ft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Cabin Creek/elevation_ft","old":6259,"new":4700,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>828. /world/backcountry_strips/Cabin Creek/heading_true_deg</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Cabin Creek/heading_true_deg","old":70,"new":179,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>829. /world/backcountry_strips/Cabin Creek/one_way_true_deg</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Cabin Creek/one_way_true_deg","old":70,"new":179,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>830. /world/backcountry_strips/Cabin Creek/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Cabin Creek/shore_class","old":null,"new":"inland","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>831. /world/backcountry_strips/Chamberlain/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Chamberlain/centre","old":[2.8,-1.5],"new":[0.7,-1.8],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>832. /world/backcountry_strips/Chamberlain/elevation_ft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Chamberlain/elevation_ft","old":7000,"new":4800,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>833. /world/backcountry_strips/Chamberlain/heading_true_deg</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Chamberlain/heading_true_deg","old":85,"new":31,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>834. /world/backcountry_strips/Chamberlain/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Chamberlain/shore_class","old":null,"new":"inland","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>835. /world/backcountry_strips/Dewey Moore/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Dewey Moore/centre","old":[4.8,1.1],"new":[-0.8,-2.8],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>836. /world/backcountry_strips/Dewey Moore/elevation_ft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Dewey Moore/elevation_ft","old":6094,"new":4600,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>837. /world/backcountry_strips/Dewey Moore/heading_true_deg</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Dewey Moore/heading_true_deg","old":290,"new":215,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>838. /world/backcountry_strips/Dewey Moore/one_way_true_deg</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Dewey Moore/one_way_true_deg","old":290,"new":215,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>839. /world/backcountry_strips/Dewey Moore/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Dewey Moore/shore_class","old":null,"new":"inland","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>840. /world/backcountry_strips/Indian Creek/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Indian Creek/centre","old":[3.8,1.6],"new":[3.5,2],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>841. /world/backcountry_strips/Indian Creek/elevation_ft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Indian Creek/elevation_ft","old":4841,"new":4500,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>842. /world/backcountry_strips/Indian Creek/heading_true_deg</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Indian Creek/heading_true_deg","old":34,"new":47,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>843. /world/backcountry_strips/Indian Creek/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Indian Creek/shore_class","old":null,"new":"inland","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>844. /world/backcountry_strips/Mackay Bar/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Mackay Bar/centre","old":[5,1.8],"new":[5.1,1.3],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>845. /world/backcountry_strips/Mackay Bar/elevation_ft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Mackay Bar/elevation_ft","old":4917,"new":4500,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>846. /world/backcountry_strips/Mackay Bar/heading_true_deg</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Mackay Bar/heading_true_deg","old":8,"new":330,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>847. /world/backcountry_strips/Mackay Bar/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Mackay Bar/shore_class","old":null,"new":"inland","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>848. /world/backcountry_strips/Soldier Bar/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Soldier Bar/centre","old":[4.4,0.6],"new":[4,1.35],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>849. /world/backcountry_strips/Soldier Bar/elevation_ft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Soldier Bar/elevation_ft","old":5865,"new":4400,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>850. /world/backcountry_strips/Soldier Bar/heading_true_deg</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Soldier Bar/heading_true_deg","old":250,"new":86,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>851. /world/backcountry_strips/Soldier Bar/one_way_true_deg</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Soldier Bar/one_way_true_deg","old":250,"new":86,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>852. /world/backcountry_strips/Soldier Bar/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Soldier Bar/shore_class","old":null,"new":"inland","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>853. /world/backcountry_strips/Sulphur Creek/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Sulphur Creek/centre","old":[4.6,-0.3],"new":[3.4,-0.4],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>854. /world/backcountry_strips/Sulphur Creek/elevation_ft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Sulphur Creek/elevation_ft","old":7000,"new":4830,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>855. /world/backcountry_strips/Sulphur Creek/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Sulphur Creek/shore_class","old":null,"new":"inland","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>856. /world/backcountry_strips/Thomas Creek/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Thomas Creek/centre","old":[3.8,-0.7],"new":[4.4,0.65],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>857. /world/backcountry_strips/Thomas Creek/elevation_ft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Thomas Creek/elevation_ft","old":7000,"new":5000,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>858. /world/backcountry_strips/Thomas Creek/heading_true_deg</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Thomas Creek/heading_true_deg","old":25,"new":98,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>859. /world/backcountry_strips/Thomas Creek/shore_class</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/backcountry_strips/Thomas Creek/shore_class","old":null,"new":"inland","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>860. /world/canyons/C1/path</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/canyons/C1/path","old":[[0.6,1.7],[1.82,-0.86],[2.5,-1.8]],"new":[[1.05,2.1],[1.85,0.25],[1.82,-0.76],[0.7,-1.8]],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>861. /world/canyons/C2/path</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/canyons/C2/path","old":[[2.5,-1.8],[3,-0.2],[4,1.2],[5,1.8]],"new":[[0.7,-1.8],[2.5,-0.3],[3.1,0.55],[3.5,2],[4,1.35],[5.1,1.3],[3.4,-0.4],[4.4,0.65],[-0.8,-2.8]],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>862. /world/canyons/C3/path</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/canyons/C3/path","old":[[2.88,-5.38],[1.92,-6.34],[0,-6.72],[-1.15,-7.87],[-1.54,-9.6]],"new":[[4.58,-5.38],[2.9,-6.3],[0,-6.72],[-1.15,-7.87],[-1.54,-9.6]],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>863. /world/coast_polygon</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/coast_polygon","old":[[-9.7,-5.7],[-9.6,-3.8],[-9.5,-1],[-9.1,2],[-8.8,5.7],[-7,7.3],[-4.5,7.8],[-1,7.7],[2,7.4],[4.5,6.5],[6.6,6.4],[7.9,4.5],[8.6,2],[8.4,-1],[7.5,-3.8],[6.2,-6],[4,-8.4],[2,-9.7],[0,-9.8],[-1.54,-9.6],[-3.3,-8.7],[-5.3,-7.4],[-7.8,-6.6]],"new":[[-11.6,-5.7],[-11.5,-3.8],[-11.4,-1],[-11.0,2],[-9.700000000000001,5.7],[-7,7.3],[-4.5,7.8],[-1,7.7],[2,7.4],[4.5,6.5],[6.6,6.4],[7.9,4.5],[8.6,2],[8.4,-1],[7.5,-3.8],[6.2,-6],[4,-8.4],[2,-9.7],[0,-9.8],[-1.54,-9.6],[-3.3,-8.7],[-5.3,-7.4],[-7.8,-6.6]],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>864. /world/combat_zones/CZ1/airport_overlays</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/airport_overlays","old":null,"new":[{"airport":"KITE","island":"OI4","floor_msl_ft":5000,"ceiling_msl_ft":12500,"traffic_floor_msl_ft":50,"traffic_ceiling_msl_ft":3000,"pattern_altitude_msl_ft":1050,"vertical_buffer_ft":2000,"pattern_radius_km":1.2,"approach_guard_km":0.3,"scope":"Full both-end4nm final fans +300m lateral guard and1.2km pattern disk; traffic at/below3000ft, combat at/above5000ft. No low-floor targets/engagement in reservation.2000ft vertical margin.","floor_priority":"raised floor overrides base floor in union of protection polygons"}],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>865. /world/combat_zones/CZ1/boundary_discretization</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/boundary_discretization","old":null,"new":"100 convex angular quadrilaterals; outerarc inscribed, inner radius inflated by1/cos(half angularstep). Polygonal review domain is authoritative, sector radii are chart targets. Maximum radial arc difference<0.12m. Exclusion policy constructs exact active polygon union.","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>866. /world/combat_zones/CZ1/boundary_mode</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/boundary_mode","old":null,"new":"annular sector minus charted horizontal exclusion polygons; raised floor on airport traffic overlays","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>867. /world/combat_zones/CZ1/combat_features</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/combat_features","old":null,"new":[{"id":"CZ1-CHANNEL","type":"island cluster and channel","centre":[9.3,16.0],"channel_width_m":300,"channel_length_m":1100,"water_msl_ft":0,"flight_half_width_m":40,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"terrain":"five islands with smooth ridges/sea stacks, two 300m channels; hide-and-seek and strafe route"},{"id":"CZ1-BRIDGE","type":"bridge","centre":[9.3,16.0],"heading_true_deg":90,"span_m":300,"deck_width_m":14,"under_clearance_m":120,"under_base_msl_ft":0,"deck_thickness_m":4,"flight_opening_width_m":240,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids bridge deck/piers; no heightfield bridge deck"},{"id":"CZ1-ARCH","type":"rock arch","centre":[9.8,16.4],"axis_true_deg":0,"opening_width_m":100,"opening_height_m":110,"base_msl_ft":60,"rock_top_msl_ft":460,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids overhang/opening; footprint pedestals only in shared heightfield"}],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>868. /world/combat_zones/CZ1/drone_orbit/altitude_ft_target</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/drone_orbit/altitude_ft_target","old":3000,"new":900,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>869. /world/combat_zones/CZ1/drone_orbit/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/drone_orbit/centre","old":null,"new":[9.3,16.0],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>870. /world/combat_zones/CZ1/drone_orbit/radius_m_target</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/drone_orbit/radius_m_target","old":500,"new":150,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>871. /world/combat_zones/CZ1/exclusion_policy</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/exclusion_policy","old":null,"new":{"guard_km":2.05,"protected_sources":"All known IFR finals AND every runway/beach end fan except own under-zone airport; full2nm airway swaths and proposed perimeter reservation; subtract Minkowski-expanded convex polygons, no combat in excluded volume at any altitude","unknown_geometry":"Reserve against future T201/misses/holds; pending Claude geometry remains unverified"},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>872. /world/combat_zones/CZ1/floor_msl_ft_target</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/floor_msl_ft_target","old":500,"new":150,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>873. /world/combat_zones/CZ1/gunnery_targets/CZ1-GT1/appearance</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/gunnery_targets/CZ1-GT1/appearance","old":null,"new":"HOT entry only","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>874. /world/combat_zones/CZ1/gunnery_targets/CZ1-GT1/attack_min_msl_ft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/gunnery_targets/CZ1-GT1/attack_min_msl_ft","old":null,"new":170,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>875. /world/combat_zones/CZ1/gunnery_targets/CZ1-GT1/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/gunnery_targets/CZ1-GT1/centre","old":[8.056562,14.685173],"new":[8.8,16.4],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>876. /world/combat_zones/CZ1/gunnery_targets/CZ1-GT1/platform</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/gunnery_targets/CZ1-GT1/platform","old":"moored floating bullseye raft","new":"ground target on island/terrace, no floating raft","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>877. /world/combat_zones/CZ1/gunnery_targets/CZ1-GT1/radius_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/gunnery_targets/CZ1-GT1/radius_m","old":30,"new":8,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>878. /world/combat_zones/CZ1/gunnery_targets/CZ1-GT1/target_top_msl_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/gunnery_targets/CZ1-GT1/target_top_msl_m","old":1,"new":null,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>879. /world/combat_zones/CZ1/gunnery_targets/CZ1-GT1/top_agl_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/gunnery_targets/CZ1-GT1/top_agl_m","old":null,"new":2,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>880. /world/combat_zones/CZ1/gunnery_targets/CZ1-GT2/appearance</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/gunnery_targets/CZ1-GT2/appearance","old":null,"new":"HOT entry only","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>881. /world/combat_zones/CZ1/gunnery_targets/CZ1-GT2/attack_min_msl_ft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/gunnery_targets/CZ1-GT2/attack_min_msl_ft","old":null,"new":170,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>882. /world/combat_zones/CZ1/gunnery_targets/CZ1-GT2/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/gunnery_targets/CZ1-GT2/centre","old":[8.589346,14.728395],"new":[9.8,16.0],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>883. /world/combat_zones/CZ1/gunnery_targets/CZ1-GT2/platform</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/gunnery_targets/CZ1-GT2/platform","old":"moored floating bullseye raft","new":"ground target on island/terrace, no floating raft","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>884. /world/combat_zones/CZ1/gunnery_targets/CZ1-GT2/radius_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/gunnery_targets/CZ1-GT2/radius_m","old":30,"new":8,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>885. /world/combat_zones/CZ1/gunnery_targets/CZ1-GT2/target_top_msl_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/gunnery_targets/CZ1-GT2/target_top_msl_m","old":1,"new":null,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>886. /world/combat_zones/CZ1/gunnery_targets/CZ1-GT2/top_agl_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/gunnery_targets/CZ1-GT2/top_agl_m","old":null,"new":2,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>887. /world/combat_zones/CZ1/gunnery_targets/CZ1-GT3/appearance</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/gunnery_targets/CZ1-GT3/appearance","old":null,"new":"HOT entry only","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>888. /world/combat_zones/CZ1/gunnery_targets/CZ1-GT3/attack_min_msl_ft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/gunnery_targets/CZ1-GT3/attack_min_msl_ft","old":null,"new":170,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>889. /world/combat_zones/CZ1/gunnery_targets/CZ1-GT3/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/gunnery_targets/CZ1-GT3/centre","old":[8.814083,14.2434],"new":[9.3,16.8],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>890. /world/combat_zones/CZ1/gunnery_targets/CZ1-GT3/platform</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/gunnery_targets/CZ1-GT3/platform","old":"moored floating bullseye raft","new":"ground target on island/terrace, no floating raft","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>891. /world/combat_zones/CZ1/gunnery_targets/CZ1-GT3/radius_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/gunnery_targets/CZ1-GT3/radius_m","old":30,"new":8,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>892. /world/combat_zones/CZ1/gunnery_targets/CZ1-GT3/target_top_msl_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/gunnery_targets/CZ1-GT3/target_top_msl_m","old":1,"new":null,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>893. /world/combat_zones/CZ1/gunnery_targets/CZ1-GT3/top_agl_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/gunnery_targets/CZ1-GT3/top_agl_m","old":null,"new":2,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>894. /world/combat_zones/CZ1/gunnery_targets/CZ1-GT4</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/gunnery_targets/CZ1-GT4","old":{"id":"CZ1-GT4","centre":[9.3484,14.258679],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},"new":null,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>895. /world/combat_zones/CZ1/gunnery_targets/CZ1-GT5</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/gunnery_targets/CZ1-GT5","old":{"id":"CZ1-GT5","centre":[9.547446,13.762586],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},"new":null,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>896. /world/combat_zones/CZ1/gunnery_targets/CZ1-GT6</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/gunnery_targets/CZ1-GT6","old":{"id":"CZ1-GT6","centre":[10.08183,13.749881],"radius_m":14,"platform":"moored floating bullseye raft","target_top_msl_m":1},"new":null,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>897. /world/combat_zones/CZ1/hook_owner</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/hook_owner","old":"Claude: HOT may trigger event:combat and dogfight; COLD never triggers","new":"Claude implements explicit HOT-entry targets/drones spawn; COLD never spawns.","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>898. /world/combat_zones/CZ1/hot_entry_behavior</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/hot_entry_behavior","old":null,"new":{"HOT":"On user entry, spawn this zone’s targets and drones; engagement hook belongs to Claude.","COLD":"No targets/drones appear on entry and no combat triggers.","entry_definition":"Enter active3D combat volume after horizontal exclusions and localraisedfloors. Airporttraffic belowraisedfloor does not entercombat airspace; HOT entry creates targets/drones onislands, COLD entry createsnone."},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>899. /world/combat_zones/CZ1/low_level_training</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/low_level_training","old":null,"new":{"floor_msl_ft":150,"flight_tube_ceiling_ft":300,"clear_of_traffic_overlays":true,"feature_solids":"All arch/bridge solids charted; no invisible heightfield ceilings; verified as analytic design envelopes"},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>900. /world/combat_zones/CZ1/name</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/name","old":"KAIWI MOA","new":"AP KAIWI MOA","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>901. /world/combat_zones/CZ1/name_audit_source</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/name_audit_source","old":null,"new":"https://aeronav.faa.gov/afd/02OCT2025/AK_rear_02OCT2025.pdf","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>902. /world/combat_zones/CZ1/name_status</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/name_status","old":"placeholder; unverified against official MOA list","new":"Fictional simulator-prefixed name; not a depiction of real SUA. Susitna is a real MOA name (FAA chart supplement), hence AP prefix on all zones.","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>903. /world/combat_zones/CZ1/schedule</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ1/schedule","old":null,"new":"Default HOT; pilot may select COLD at any time; no timed automatic schedule.","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>904. /world/combat_zones/CZ2/boundary_discretization</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/boundary_discretization","old":null,"new":"100 convex angular quadrilaterals; outerarc inscribed, inner radius inflated by1/cos(half angularstep). Polygonal review domain is authoritative, sector radii are chart targets. Maximum radial arc difference<0.12m. Exclusion policy constructs exact active polygon union.","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>905. /world/combat_zones/CZ2/boundary_mode</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/boundary_mode","old":null,"new":"annular sector minus charted horizontal exclusion polygons; raised floor on airport traffic overlays","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>906. /world/combat_zones/CZ2/combat_features</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/combat_features","old":null,"new":[{"id":"CZ2-CHANNEL","type":"island cluster and channel","centre":[16.5,-7.5],"channel_width_m":300,"channel_length_m":1100,"water_msl_ft":0,"flight_half_width_m":40,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"terrain":"five islands with smooth ridges/sea stacks, two 300m channels; hide-and-seek and strafe route"},{"id":"CZ2-BRIDGE","type":"bridge","centre":[16.5,-7.5],"heading_true_deg":90,"span_m":300,"deck_width_m":14,"under_clearance_m":120,"under_base_msl_ft":0,"deck_thickness_m":4,"flight_opening_width_m":240,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids bridge deck/piers; no heightfield bridge deck"},{"id":"CZ2-ARCH","type":"rock arch","centre":[17.0,-7.1],"axis_true_deg":0,"opening_width_m":100,"opening_height_m":110,"base_msl_ft":60,"rock_top_msl_ft":460,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids overhang/opening; footprint pedestals only in shared heightfield"}],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>907. /world/combat_zones/CZ2/drone_orbit/altitude_ft_target</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/drone_orbit/altitude_ft_target","old":3000,"new":900,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>908. /world/combat_zones/CZ2/drone_orbit/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/drone_orbit/centre","old":null,"new":[16.5,-7.5],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>909. /world/combat_zones/CZ2/drone_orbit/radius_m_target</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/drone_orbit/radius_m_target","old":500,"new":150,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>910. /world/combat_zones/CZ2/exclusion_policy</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/exclusion_policy","old":null,"new":{"guard_km":2.05,"protected_sources":"All known IFR finals AND every runway/beach end fan except own under-zone airport; full2nm airway swaths and proposed perimeter reservation; subtract Minkowski-expanded convex polygons, no combat in excluded volume at any altitude","unknown_geometry":"Reserve against future T201/misses/holds; pending Claude geometry remains unverified"},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>911. /world/combat_zones/CZ2/floor_msl_ft_target</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/floor_msl_ft_target","old":500,"new":150,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>912. /world/combat_zones/CZ2/gunnery_targets/CZ2-GT1/appearance</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/gunnery_targets/CZ2-GT1/appearance","old":null,"new":"HOT entry only","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>913. /world/combat_zones/CZ2/gunnery_targets/CZ2-GT1/attack_min_msl_ft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/gunnery_targets/CZ2-GT1/attack_min_msl_ft","old":null,"new":170,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>914. /world/combat_zones/CZ2/gunnery_targets/CZ2-GT1/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/gunnery_targets/CZ2-GT1/centre","old":[15.37813,-5.980228],"new":[16.0,-7.5],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>915. /world/combat_zones/CZ2/gunnery_targets/CZ2-GT1/platform</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/gunnery_targets/CZ2-GT1/platform","old":"moored floating bullseye raft","new":"ground target on island/terrace, no floating raft","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>916. /world/combat_zones/CZ2/gunnery_targets/CZ2-GT1/radius_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/gunnery_targets/CZ2-GT1/radius_m","old":30,"new":8,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>917. /world/combat_zones/CZ2/gunnery_targets/CZ2-GT1/target_top_msl_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/gunnery_targets/CZ2-GT1/target_top_msl_m","old":1,"new":null,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>918. /world/combat_zones/CZ2/gunnery_targets/CZ2-GT1/top_agl_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/gunnery_targets/CZ2-GT1/top_agl_m","old":null,"new":2,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>919. /world/combat_zones/CZ2/gunnery_targets/CZ2-GT2/appearance</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/gunnery_targets/CZ2-GT2/appearance","old":null,"new":"HOT entry only","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>920. /world/combat_zones/CZ2/gunnery_targets/CZ2-GT2/attack_min_msl_ft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/gunnery_targets/CZ2-GT2/attack_min_msl_ft","old":null,"new":170,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>921. /world/combat_zones/CZ2/gunnery_targets/CZ2-GT2/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/gunnery_targets/CZ2-GT2/centre","old":[15.492976,-6.496744],"new":[17.0,-7.5],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>922. /world/combat_zones/CZ2/gunnery_targets/CZ2-GT2/platform</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/gunnery_targets/CZ2-GT2/platform","old":"moored floating bullseye raft","new":"ground target on island/terrace, no floating raft","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>923. /world/combat_zones/CZ2/gunnery_targets/CZ2-GT2/radius_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/gunnery_targets/CZ2-GT2/radius_m","old":30,"new":8,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>924. /world/combat_zones/CZ2/gunnery_targets/CZ2-GT2/target_top_msl_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/gunnery_targets/CZ2-GT2/target_top_msl_m","old":1,"new":null,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>925. /world/combat_zones/CZ2/gunnery_targets/CZ2-GT2/top_agl_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/gunnery_targets/CZ2-GT2/top_agl_m","old":null,"new":2,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>926. /world/combat_zones/CZ2/gunnery_targets/CZ2-GT3/appearance</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/gunnery_targets/CZ2-GT3/appearance","old":null,"new":"HOT entry only","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>927. /world/combat_zones/CZ2/gunnery_targets/CZ2-GT3/attack_min_msl_ft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/gunnery_targets/CZ2-GT3/attack_min_msl_ft","old":null,"new":170,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>928. /world/combat_zones/CZ2/gunnery_targets/CZ2-GT3/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/gunnery_targets/CZ2-GT3/centre","old":[15.044074,-6.776861],"new":[16.5,-6.7],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>929. /world/combat_zones/CZ2/gunnery_targets/CZ2-GT3/platform</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/gunnery_targets/CZ2-GT3/platform","old":"moored floating bullseye raft","new":"ground target on island/terrace, no floating raft","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>930. /world/combat_zones/CZ2/gunnery_targets/CZ2-GT3/radius_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/gunnery_targets/CZ2-GT3/radius_m","old":30,"new":8,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>931. /world/combat_zones/CZ2/gunnery_targets/CZ2-GT3/target_top_msl_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/gunnery_targets/CZ2-GT3/target_top_msl_m","old":1,"new":null,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>932. /world/combat_zones/CZ2/gunnery_targets/CZ2-GT3/top_agl_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/gunnery_targets/CZ2-GT3/top_agl_m","old":null,"new":2,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>933. /world/combat_zones/CZ2/gunnery_targets/CZ2-GT4</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/gunnery_targets/CZ2-GT4","old":{"id":"CZ2-GT4","centre":[15.13173,-7.29868],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},"new":null,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>934. /world/combat_zones/CZ2/gunnery_targets/CZ2-GT5</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/gunnery_targets/CZ2-GT5","old":{"id":"CZ2-GT5","centre":[14.668783,-7.55492],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},"new":null,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>935. /world/combat_zones/CZ2/gunnery_targets/CZ2-GT6</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/gunnery_targets/CZ2-GT6","old":{"id":"CZ2-GT6","centre":[14.729009,-8.080611],"radius_m":14,"platform":"moored floating bullseye raft","target_top_msl_m":1},"new":null,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>936. /world/combat_zones/CZ2/hook_owner</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/hook_owner","old":"Claude: HOT may trigger event:combat and dogfight; COLD never triggers","new":"Claude implements explicit HOT-entry targets/drones spawn; COLD never spawns.","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>937. /world/combat_zones/CZ2/hot_entry_behavior</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/hot_entry_behavior","old":null,"new":{"HOT":"On user entry, spawn this zone’s targets and drones; engagement hook belongs to Claude.","COLD":"No targets/drones appear on entry and no combat triggers.","entry_definition":"Enter active3D combat volume after horizontal exclusions and localraisedfloors. Airporttraffic belowraisedfloor does not entercombat airspace; HOT entry creates targets/drones onislands, COLD entry createsnone."},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>938. /world/combat_zones/CZ2/low_level_training</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/low_level_training","old":null,"new":{"floor_msl_ft":150,"flight_tube_ceiling_ft":300,"clear_of_traffic_overlays":true,"feature_solids":"All arch/bridge solids charted; no invisible heightfield ceilings; verified as analytic design envelopes"},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>939. /world/combat_zones/CZ2/name</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/name","old":"PAINTED MOA","new":"AP PAINTED MOA","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>940. /world/combat_zones/CZ2/name_audit_source</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/name_audit_source","old":null,"new":"https://aeronav.faa.gov/afd/02OCT2025/AK_rear_02OCT2025.pdf","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>941. /world/combat_zones/CZ2/name_status</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/name_status","old":"placeholder; unverified against official MOA list","new":"Fictional simulator-prefixed name; not a depiction of real SUA. Susitna is a real MOA name (FAA chart supplement), hence AP prefix on all zones.","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>942. /world/combat_zones/CZ2/schedule</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ2/schedule","old":null,"new":"Default HOT; pilot may select COLD at any time; no timed automatic schedule.","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>943. /world/combat_zones/CZ3/airport_overlays</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/airport_overlays","old":null,"new":[{"airport":"RIME","island":"OI5","floor_msl_ft":5000,"ceiling_msl_ft":12500,"traffic_floor_msl_ft":70,"traffic_ceiling_msl_ft":3000,"pattern_altitude_msl_ft":1070,"vertical_buffer_ft":2000,"pattern_radius_km":1.2,"approach_guard_km":0.3,"scope":"Full both-end4nm final fans +300m lateral guard and1.2km pattern disk; traffic at/below3000ft, combat at/above5000ft. No low-floor targets/engagement in reservation.2000ft vertical margin.","floor_priority":"raised floor overrides base floor in union of protection polygons"}],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>944. /world/combat_zones/CZ3/boundary_discretization</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/boundary_discretization","old":null,"new":"100 convex angular quadrilaterals; outerarc inscribed, inner radius inflated by1/cos(half angularstep). Polygonal review domain is authoritative, sector radii are chart targets. Maximum radial arc difference<0.12m. Exclusion policy constructs exact active polygon union.","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>945. /world/combat_zones/CZ3/boundary_mode</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/boundary_mode","old":null,"new":"annular sector minus charted horizontal exclusion polygons; raised floor on airport traffic overlays","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>946. /world/combat_zones/CZ3/combat_features</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/combat_features","old":null,"new":[{"id":"CZ3-CHANNEL","type":"island cluster and channel","centre":[-7.9,17.05],"channel_width_m":300,"channel_length_m":600,"water_msl_ft":0,"flight_half_width_m":40,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"terrain":"five islands with smooth ridges/sea stacks, two 300m channels; hide-and-seek and strafe route"},{"id":"CZ3-BRIDGE","type":"bridge","centre":[-7.9,17.0],"heading_true_deg":90,"span_m":300,"deck_width_m":14,"under_clearance_m":120,"under_base_msl_ft":0,"deck_thickness_m":4,"flight_opening_width_m":240,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids bridge deck/piers; no heightfield bridge deck"},{"id":"CZ3-ARCH","type":"rock arch","centre":[-7.4,17.4],"axis_true_deg":0,"opening_width_m":100,"opening_height_m":110,"base_msl_ft":60,"rock_top_msl_ft":460,"flight_floor_msl_ft":170,"flight_ceiling_msl_ft":300,"collision":"WorldSolids overhang/opening; footprint pedestals only in shared heightfield"}],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>947. /world/combat_zones/CZ3/drone_orbit/altitude_ft_target</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/drone_orbit/altitude_ft_target","old":3000,"new":900,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>948. /world/combat_zones/CZ3/drone_orbit/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/drone_orbit/centre","old":null,"new":[-7.9,17.0],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>949. /world/combat_zones/CZ3/drone_orbit/radius_m_target</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/drone_orbit/radius_m_target","old":500,"new":150,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>950. /world/combat_zones/CZ3/exclusion_policy</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/exclusion_policy","old":null,"new":{"guard_km":2.05,"protected_sources":"All known IFR finals AND every runway/beach end fan except own under-zone airport; full2nm airway swaths and proposed perimeter reservation; subtract Minkowski-expanded convex polygons, no combat in excluded volume at any altitude","unknown_geometry":"Reserve against future T201/misses/holds; pending Claude geometry remains unverified"},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>951. /world/combat_zones/CZ3/floor_msl_ft_target</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/floor_msl_ft_target","old":500,"new":150,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>952. /world/combat_zones/CZ3/gunnery_targets/CZ3-GT1/appearance</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/gunnery_targets/CZ3-GT1/appearance","old":null,"new":"HOT entry only","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>953. /world/combat_zones/CZ3/gunnery_targets/CZ3-GT1/attack_min_msl_ft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/gunnery_targets/CZ3-GT1/attack_min_msl_ft","old":null,"new":170,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>954. /world/combat_zones/CZ3/gunnery_targets/CZ3-GT1/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/gunnery_targets/CZ3-GT1/centre","old":[-8.819145,14.533502],"new":[-8.4,17.0],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>955. /world/combat_zones/CZ3/gunnery_targets/CZ3-GT1/platform</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/gunnery_targets/CZ3-GT1/platform","old":"moored floating bullseye raft","new":"ground target on island/terrace, no floating raft","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>956. /world/combat_zones/CZ3/gunnery_targets/CZ3-GT1/radius_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/gunnery_targets/CZ3-GT1/radius_m","old":30,"new":8,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>957. /world/combat_zones/CZ3/gunnery_targets/CZ3-GT1/target_top_msl_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/gunnery_targets/CZ3-GT1/target_top_msl_m","old":1,"new":null,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>958. /world/combat_zones/CZ3/gunnery_targets/CZ3-GT1/top_agl_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/gunnery_targets/CZ3-GT1/top_agl_m","old":null,"new":2,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>959. /world/combat_zones/CZ3/gunnery_targets/CZ3-GT2/appearance</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/gunnery_targets/CZ3-GT2/appearance","old":null,"new":"HOT entry only","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>960. /world/combat_zones/CZ3/gunnery_targets/CZ3-GT2/attack_min_msl_ft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/gunnery_targets/CZ3-GT2/attack_min_msl_ft","old":null,"new":170,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>961. /world/combat_zones/CZ3/gunnery_targets/CZ3-GT2/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/gunnery_targets/CZ3-GT2/centre","old":[-8.584546,15.019839],"new":[-7.4,17.15],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>962. /world/combat_zones/CZ3/gunnery_targets/CZ3-GT2/platform</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/gunnery_targets/CZ3-GT2/platform","old":"moored floating bullseye raft","new":"ground target on island/terrace, no floating raft","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>963. /world/combat_zones/CZ3/gunnery_targets/CZ3-GT2/radius_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/gunnery_targets/CZ3-GT2/radius_m","old":30,"new":8,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>964. /world/combat_zones/CZ3/gunnery_targets/CZ3-GT2/target_top_msl_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/gunnery_targets/CZ3-GT2/target_top_msl_m","old":1,"new":null,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>965. /world/combat_zones/CZ3/gunnery_targets/CZ3-GT2/top_agl_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/gunnery_targets/CZ3-GT2/top_agl_m","old":null,"new":2,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>966. /world/combat_zones/CZ3/gunnery_targets/CZ3-GT3/appearance</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/gunnery_targets/CZ3-GT3/appearance","old":null,"new":"HOT entry only","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>967. /world/combat_zones/CZ3/gunnery_targets/CZ3-GT3/attack_min_msl_ft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/gunnery_targets/CZ3-GT3/attack_min_msl_ft","old":null,"new":170,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>968. /world/combat_zones/CZ3/gunnery_targets/CZ3-GT3/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/gunnery_targets/CZ3-GT3/centre","old":[-8.046434,14.975143],"new":[-7.9,17.4],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>969. /world/combat_zones/CZ3/gunnery_targets/CZ3-GT3/platform</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/gunnery_targets/CZ3-GT3/platform","old":"moored floating bullseye raft","new":"ground target on island/terrace, no floating raft","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>970. /world/combat_zones/CZ3/gunnery_targets/CZ3-GT3/radius_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/gunnery_targets/CZ3-GT3/radius_m","old":30,"new":8,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>971. /world/combat_zones/CZ3/gunnery_targets/CZ3-GT3/target_top_msl_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/gunnery_targets/CZ3-GT3/target_top_msl_m","old":1,"new":null,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>972. /world/combat_zones/CZ3/gunnery_targets/CZ3-GT3/top_agl_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/gunnery_targets/CZ3-GT3/top_agl_m","old":null,"new":2,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>973. /world/combat_zones/CZ3/gunnery_targets/CZ3-GT4</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/gunnery_targets/CZ3-GT4","old":{"id":"CZ3-GT4","centre":[-7.786703,15.448536],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},"new":null,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>974. /world/combat_zones/CZ3/gunnery_targets/CZ3-GT5</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/gunnery_targets/CZ3-GT5","old":{"id":"CZ3-GT5","centre":[-7.251669,15.375737],"radius_m":30,"platform":"moored floating bullseye raft","target_top_msl_m":1},"new":null,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>975. /world/combat_zones/CZ3/gunnery_targets/CZ3-GT6</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/gunnery_targets/CZ3-GT6","old":{"id":"CZ3-GT6","centre":[-6.967518,15.834889],"radius_m":14,"platform":"moored floating bullseye raft","target_top_msl_m":1},"new":null,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>976. /world/combat_zones/CZ3/hook_owner</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/hook_owner","old":"Claude: HOT may trigger event:combat and dogfight; COLD never triggers","new":"Claude implements explicit HOT-entry targets/drones spawn; COLD never spawns.","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>977. /world/combat_zones/CZ3/hot_entry_behavior</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/hot_entry_behavior","old":null,"new":{"HOT":"On user entry, spawn this zone’s targets and drones; engagement hook belongs to Claude.","COLD":"No targets/drones appear on entry and no combat triggers.","entry_definition":"Enter active3D combat volume after horizontal exclusions and localraisedfloors. Airporttraffic belowraisedfloor does not entercombat airspace; HOT entry creates targets/drones onislands, COLD entry createsnone."},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>978. /world/combat_zones/CZ3/low_level_training</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/low_level_training","old":null,"new":{"floor_msl_ft":150,"flight_tube_ceiling_ft":300,"clear_of_traffic_overlays":true,"feature_solids":"All arch/bridge solids charted; no invisible heightfield ceilings; verified as analytic design envelopes"},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>979. /world/combat_zones/CZ3/name</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/name","old":"SUSITNA MOA","new":"AP SUSITNA MOA","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>980. /world/combat_zones/CZ3/name_audit_source</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/name_audit_source","old":null,"new":"https://aeronav.faa.gov/afd/02OCT2025/AK_rear_02OCT2025.pdf","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>981. /world/combat_zones/CZ3/name_status</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/name_status","old":"placeholder; unverified against official MOA list","new":"Fictional simulator-prefixed name; not a depiction of real SUA. Susitna is a real MOA name (FAA chart supplement), hence AP prefix on all zones.","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>982. /world/combat_zones/CZ3/schedule</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ3/schedule","old":null,"new":"Default HOT; pilot may select COLD at any time; no timed automatic schedule.","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>983. /world/combat_zones/CZ4</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ4","old":{"id":"CZ4","name":"KACHEMAK MOA","bearings_true_deg":[228,262],"radii_km":[14,19.5],"floor_msl_ft_target":500,"ceiling_msl_ft_target":12500,"default_status":"HOT","available_statuses":["HOT","COLD"],"optional":true,"enabled":false,"minimum_clearance_km":1.85,"chart":"magenta hatched boundary; floor-ceiling-status","name_status":"placeholder; unverified against official MOA list","hook_owner":"Claude: HOT may trigger event:combat and dogfight; COLD never triggers"},"new":null,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>984. /world/combat_zones/CZ5</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/combat_zones/CZ5","old":null,"new":{"id":"CZ5","name":"AP TERN MESA MOA","boundary":[[17.1,2.2],[17.5,2.2],[17.5,2.6],[17.1,2.6]],"over_land":true,"floor_msl_ft_target":200,"ceiling_msl_ft_target":2500,"default_status":"HOT","schedule":"Default HOT; pilot may select COLD at any time; no timed automatic schedule.","available_statuses":["HOT","COLD"],"optional":false,"enabled":true,"minimum_clearance_km":1.85,"additional_margin_km":0.2,"clearance_reference":">=1.85 km plus 0.20 km margin from final fan boundaries and FULL 2 nm airway terrain-protection swaths, including proposed perimeter reservation.","chart":"magenta hatched boundary; name, floor/ceiling, status; full airspace acceptance pending","name_status":"Fictional simulator-prefixed name; not a depiction of real SUA. Susitna is a real MOA name (FAA chart supplement), hence AP prefix on all zones.","hook_owner":"Claude implements explicit HOT-entry targets/drones spawn; COLD never spawns.","gunnery_targets":[{"id":"CZ5-GT1","centre":[17.16,2.28],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":220},{"id":"CZ5-GT2","centre":[17.44,2.52],"radius_m":8,"platform":"ground target on island/terrace, no floating raft","top_agl_m":2,"appearance":"HOT entry only","attack_min_msl_ft":220}],"drone_orbit":{"centre_start":"CZ5","count":3,"radius_m_target":70,"altitude_ft_target":600,"owner":"Claude opponent/AI logic","centre":[17.3,2.4]},"hot_entry_behavior":{"HOT":"On user entry, spawn this zone’s targets and drones; engagement hook belongs to Claude.","COLD":"No targets/drones appear on entry and no combat triggers.","entry_definition":"Enter active3D combat volume after horizontal exclusions and localraisedfloors. Airporttraffic belowraisedfloor does not entercombat airspace; HOT entry creates targets/drones onislands, COLD entry createsnone."},"name_audit_source":"https://aeronav.faa.gov/afd/02OCT2025/AK_rear_02OCT2025.pdf","combat_features":[{"id":"CZ5-RIDGE","type":"paired ridges and dry canyon","centre":[17.3,2.4],"axis_true_deg":0,"length_m":300,"canyon_width_m":100,"canyon_depth_m":39.0144,"terrace_floor_ft":180,"rim_ft":188,"ridge_half_width_m":35,"flight_half_width_m":20,"flight_floor_msl_ft":220,"flight_ceiling_msl_ft":300,"canyon_floor_ft":60,"canyon_floor_width_m":60},{"id":"CZ5-BRIDGE","type":"bridge","centre":[17.3,2.4],"heading_true_deg":90,"span_m":120,"deck_width_m":14,"under_clearance_m":100,"under_base_msl_ft":60,"deck_thickness_m":4,"flight_opening_width_m":100,"flight_floor_msl_ft":220,"flight_ceiling_msl_ft":300,"collision":"WorldSolids bridge deck/piers; no heightfield bridge deck"},{"id":"CZ5-ARCH","type":"rock arch","centre":[17.41,2.488],"axis_true_deg":0,"opening_width_m":60,"opening_height_m":55,"base_msl_ft":188,"rock_top_msl_ft":400,"flight_floor_msl_ft":220,"flight_ceiling_msl_ft":300,"collision":"WorldSolids overhang/opening; footprint pedestals only in shared heightfield"},{"id":"CZ5-STACKS","type":"rock spire pair","centre":[17.3,2.4],"stack_centres":[[17.24,2.3],[17.36,2.5]],"radius_m":10,"base_msl_ft":60,"top_msl_ft":188,"height_m":39.0144,"flight_floor_msl_ft":220,"flight_ceiling_msl_ft":300,"flight_half_width_m":20,"length_m":280,"terrain":"Two isolatederoded columns beside drycanyon; smooth radial cones insharedheightfield, no overhang."}],"low_level_training":{"floor_msl_ft":200,"flight_tube_ceiling_ft":300,"clear_of_traffic_overlays":true,"feature_solids":"All arch/bridge solids charted; no invisible heightfield ceilings; verified as analytic design envelopes"},"agl_range_ft_bound":{"floor":[12,140],"ceiling":[2312,2440],"terrain":[60,188],"meaning":"Analytic continuous extrema include60ft drycanyon floor and188ft rims/spires. Aircraft/drone volumes remainabovefloor; targets aregroundfixtures."}},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>985. /world/features/F1/exterior_strip_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/features/F1/exterior_strip_m","old":152,"new":152.4,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>986. /world/features/F1/interior_strip_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/features/F1/interior_strip_m","old":760,"new":759.5616000000001,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>987. /world/features/F1/shell_geometry</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/features/F1/shell_geometry","old":null,"new":{"mouth":[-4.9,-3.8764192],"chamber_centre":[-4.9,-4.2864192],"chamber_size_m":[820,420,140],"floor_msl_m":1432.5600000000002,"ceiling_msl_m":1572.5600000000002,"minimum_roof_msl_m":1632.5600000000002,"minimum_rock_cover_m":60,"hill_centre":[-4.9,-4.5],"hill_radius_m":700,"crown_centre":[-4.9,-4.5],"crown_msl_ft":5750,"crown_radius_m":250,"roof_blend_m":20,"opening_m":[60,26],"operational_rule":"Runway17 only; reversefinal meets closedfarwall/roof and remainsFAIL until Ty approves explicitone-way exemption. No secondentrance/tunnel added.","representation":"Analytic closed3D shell + floor deck beneath shared upperterrain. Full footprint roof-cover proof; runtime collider/height-aware contact routing deferredPhase1B.","nav_site":[-4.9,-4.3]},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>988. /world/features/F2/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/features/F2/centre","old":[2.7,-14.6],"new":[2.633604444,-14.638333492],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>989. /world/features/F2/interior</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/features/F2/interior","old":null,"new":{"chamber_centre":[2.806809525,-14.538333492],"floor_msl_m":3,"ceiling_msl_m":40,"roof_top_msl_m":60,"roof_min_cover_m":15,"mouth_floor_msl_m":3,"mouth_ceiling_msl_m":29,"throat_length_m":200,"throat_width_m":60,"throat_ceiling_msl_m":29,"skylight_centre":[2.806809525,-14.538333492],"skylight_diameter_m":10,"pads":[{"id":"landing","offset_m":[0,-35],"size_m":[55,20]},{"id":"parking","offset_m":[0,35],"size_m":[40,20]}],"flight_lane":{"axis":"local inward distance from mouth; chamber pads local east/north","half_width_m":10,"floor_msl_m":5,"ceiling_msl_m":23},"contents_bounds":[{"id":"wreck","offset_m":[-65,0],"radius_m":8,"top_msl_m":11},{"id":"beach-driftwood","offset_m":[65,0],"radius_m":10,"top_msl_m":5}],"representation":"closed 3D shell; planar deck, rectangular throat, horizontal chamber roof except open skylight; phase1A.2 analytically verified design solids; no runtime implementation","throat_roof_top_msl_m_min":35,"throat_min_rock_cover_m":6,"portal_shell":"Physicalportalcliff/overhang inWorldSolids, with flat3mdeck and29m throatceiling; inlandchamber upperrock envelope insharedheightfield. No secondgroundsurface. Runtime contact routing remainsPhase1B."},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>990. /world/features/F6/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/features/F6/centre","old":[2.88,-5.15],"new":[4.58,-5.15],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>991. /world/features/RACE/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/features/RACE/centre","old":[7,-0.3],"new":[10,-13],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>992. /world/features/RACE/elements</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/features/RACE/elements","old":[{"sequence":1,"kind":"Gate","old_sim_xy_m":[45,1800],"centre":[6.4675,-0.175],"heading_true_deg":90},{"sequence":2,"kind":"Gate","old_sim_xy_m":[345,1800],"centre":[6.7675,-0.175],"heading_true_deg":90},{"sequence":3,"kind":"PylonOnRight","old_sim_xy_m":[540,1755],"centre":[6.9625,-0.13],"heading_true_deg":90},{"sequence":4,"kind":"PylonOnLeft","old_sim_xy_m":[720,1845],"centre":[7.1425,-0.22],"heading_true_deg":90},{"sequence":5,"kind":"PylonOnRight","old_sim_xy_m":[900,1755],"centre":[7.3225,-0.13],"heading_true_deg":90},{"sequence":6,"kind":"Gate","old_sim_xy_m":[1050,1800],"centre":[7.4725,-0.175],"heading_true_deg":90},{"sequence":7,"kind":"PylonOnRight","old_sim_xy_m":[1110,1920],"centre":[7.5325,-0.295],"heading_true_deg":180},{"sequence":8,"kind":"Gate","old_sim_xy_m":[1020,2050],"centre":[7.4425,-0.425],"heading_true_deg":270},{"sequence":9,"kind":"Gate","old_sim_xy_m":[720,2050],"centre":[7.1425,-0.425],"heading_true_deg":270},{"sequence":10,"kind":"PylonOnLeft","old_sim_xy_m":[540,2095],"centre":[6.9625,-0.47],"heading_true_deg":270},{"sequence":11,"kind":"PylonOnRight","old_sim_xy_m":[360,2005],"centre":[6.7825,-0.38],"heading_true_deg":270},{"sequence":12,"kind":"Gate","old_sim_xy_m":[120,2050],"centre":[6.5425,-0.425],"heading_true_deg":270}],"new":[{"sequence":1,"kind":"Gate","old_sim_xy_m":[45,1800],"centre":[9.4675,-12.875],"heading_true_deg":90},{"sequence":2,"kind":"Gate","old_sim_xy_m":[345,1800],"centre":[9.7675,-12.875],"heading_true_deg":90},{"sequence":3,"kind":"PylonOnRight","old_sim_xy_m":[540,1755],"centre":[9.9625,-12.83],"heading_true_deg":90},{"sequence":4,"kind":"PylonOnLeft","old_sim_xy_m":[720,1845],"centre":[10.1425,-12.92],"heading_true_deg":90},{"sequence":5,"kind":"PylonOnRight","old_sim_xy_m":[900,1755],"centre":[10.3225,-12.83],"heading_true_deg":90},{"sequence":6,"kind":"Gate","old_sim_xy_m":[1050,1800],"centre":[10.4725,-12.875],"heading_true_deg":90},{"sequence":7,"kind":"PylonOnRight","old_sim_xy_m":[1110,1920],"centre":[10.5325,-12.995],"heading_true_deg":180},{"sequence":8,"kind":"Gate","old_sim_xy_m":[1020,2050],"centre":[10.4425,-13.125],"heading_true_deg":270},{"sequence":9,"kind":"Gate","old_sim_xy_m":[720,2050],"centre":[10.1425,-13.125],"heading_true_deg":270},{"sequence":10,"kind":"PylonOnLeft","old_sim_xy_m":[540,2095],"centre":[9.9625,-13.17],"heading_true_deg":270},{"sequence":11,"kind":"PylonOnRight","old_sim_xy_m":[360,2005],"centre":[9.7825,-13.08],"heading_true_deg":270},{"sequence":12,"kind":"Gate","old_sim_xy_m":[120,2050],"centre":[9.5425,-13.125],"heading_true_deg":270}],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>993. /world/features/RACE/platform</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/features/RACE/platform","old":null,"new":"moored offshore racepylons; collisiongeometryanchored atwater0MSL, no land made; allseeds retained","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>994. /world/gameplay_inventory/event:combat/destination</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/gameplay_inventory/event:combat/destination","old":"CZ1-CZ3","new":"CZ1/CZ2/CZ3/CZ5; active3D HOT entry spawnsgroundtargets and3drones; COLD none","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>995. /world/gameplay_inventory/event:dogfight/destination</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/gameplay_inventory/event:dogfight/destination","old":"CZ1-CZ3","new":"CZ1/CZ2/CZ3/CZ5; active3D HOT entry spawnsgroundtargets and3drones; COLD none","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>996. /world/gameplay_inventory/gunnery:targets/destination</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/gameplay_inventory/gunnery:targets/destination","old":"six moored target rafts per CZ1-CZ3; three-drone orbit","new":"3 groundtargets ineachCZ1-CZ3,2 inCZ5;3 dronesperzone; HOT3Dentry only","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>997. /world/gameplay_inventory/gunnery:targets/name</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/gameplay_inventory/gunnery:targets/name","old":"five 30 m bullseyes + one 14 m target; three target drones","new":"Island ground bullseyes and target drones","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>998. /world/hazards/towers/T1/horizontal_envelope_radius_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/hazards/towers/T1/horizontal_envelope_radius_m","old":null,"new":65.0,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>999. /world/hazards/towers/T2/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/hazards/towers/T2/centre","old":[-1.7,-1],"new":[-1.7,-0.65],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1000. /world/hazards/towers/T2/horizontal_envelope_radius_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/hazards/towers/T2/horizontal_envelope_radius_m","old":null,"new":5,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1001. /world/hazards/towers/T3/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/hazards/towers/T3/centre","old":[2.9,-7.4],"new":[5.2,-6.3],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1002. /world/hazards/towers/T3/horizontal_envelope_radius_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/hazards/towers/T3/horizontal_envelope_radius_m","old":null,"new":149.0,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1003. /world/hazards/towers/T4/horizontal_envelope_radius_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/hazards/towers/T4/horizontal_envelope_radius_m","old":null,"new":45.0,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1004. /world/hazards/towers/T5/horizontal_envelope_radius_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/hazards/towers/T5/horizontal_envelope_radius_m","old":null,"new":49.0,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1005. /world/hazards/towers/T6/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/hazards/towers/T6/centre","old":[4.6,-3.95],"new":[6.5,-4.8],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1006. /world/hazards/towers/T6/horizontal_envelope_radius_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/hazards/towers/T6/horizontal_envelope_radius_m","old":null,"new":41.0,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1007. /world/hazards/wires/W1/endpoints</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/hazards/wires/W1/endpoints","old":[[1.55,0.25],[2.2,0.3]],"new":[[6.8,-3.8],[7.2,-3.75]],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1008. /world/hazards/wires/W2/endpoints</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/hazards/wires/W2/endpoints","old":[[2.4,-5.55],[3.3,-5.25]],"new":[[5.8,-5.9],[6.1,-5.6]],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1009. /world/hazards/wires/W3/endpoints</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/hazards/wires/W3/endpoints","old":[[-6.55,-1.1],[-6.65,-0.05]],"new":[[-6.55,-0.65],[-6.65,-0.15]],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1010. /world/hazards/wires/W4/endpoints</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/hazards/wires/W4/endpoints","old":[[-3.9,-3.45],[-2.5,-3.45]],"new":[[5.7,-5.5],[6.1,-5.5]],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1011. /world/hazards/wires/W5/endpoints</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/hazards/wires/W5/endpoints","old":[[-2.2,-8.5],[-0.2,-8.9]],"new":[[1.3,-9.4],[2,-9.3]],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1012. /world/height_field/algorithm</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/height_field/algorithm","old":"island-review-v1","new":"island-review-v2-1a2","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1013. /world/height_field/canyon_composition</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/height_field/canyon_composition","old":null,"new":"Eachfinite polyline segment applies its owncontinuous cut using globalalong-pathfloor interpolation; sequentialcontinuous cuts avoid nearest-segment elevation-owner jumps atmeanders.","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1014. /world/height_field/cavities/DRAKE</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/height_field/cavities/DRAKE","old":"height field is upper rock envelope; only exterior152m uses ground pad; interior floor is separate height-aware deck/shell at4700ft; never flatten full hill to cave floor","new":"Full820x420x140m analytic chamber under1400m hill at4700ft floor; minimum60m cover. Only exterior500ft runway is upperterrain;2992ft exacttotal preserved, interior759.5616m separate deck. CrownDRK stays(-4.9,-4.3),5750ft.","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1015. /world/height_field/clearance_envelopes</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/height_field/clearance_envelopes","old":null,"new":{"finals":{"length_km":7.408,"half_width_at_threshold_km":0.15,"widen_per_km":0.05,"outer_blend_km":0.2,"ils_loc_lda_ratio":34,"other_ratio":20,"vertical_margin_m":30,"constant_guard_km":0.25},"airways":{"half_width_km":3.704,"outer_blend_km":0.3,"clearance_ft":2025,"constant_guard_km":0.15},"order":"cut terrain after landforms; reassert locked field pads and authored peak cores; conflicts remain failures, never lowered by stealth","protected_volcano":"Exact radialdisk skipped bydiscretionaryerosionguards; wholeactualfinaldiskgap>=100m andfullgridsweep mandatory"},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1016. /world/height_field/continuity</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/height_field/continuity","old":"Every local influence has zero weight at its footprint boundary using cubic smoothstep; no boolean relief cutoff. Cavities require WorldSolids shell, not a second ground field.","new":"Continuous compact smoothstep/max island contributions, inverse-distance pad blends tending exactly to ownplane. Only namedDrake northcliff is intentionallyvertical; WorldSolids shell models60x26m hologram opening. Ocean/worldedge fades have no wall.","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1017. /world/height_field/grid/format</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/height_field/grid/format","old":"little-endian float32 metres MSL; row 0 south; column 0 west; x fastest; bilinear interpolation; JSON sidecar","new":"little-endian float32 metres MSL; row0south; column0west; xfastest; fixed SW-NE diagonal linear interpolation; analytic same-source pads/cavities/solids detail","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1018. /world/height_field/grid/interpolation</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/height_field/grid/interpolation","old":null,"new":"fixed SW-NE diagonal, piecewise-linear triangles; explicit analytic flat/sloped pad detail overlays are part of same authoring evaluator","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1019. /world/height_field/hazard_benches</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/height_field/hazard_benches","old":null,"new":[{"id":"T4 foundation knoll","centre":[-6.9,-2.9],"elevation_ft":2700,"core_radius_m":200,"blend_m":150,"reason":"50m tower top2864ft MSL + fullguy footprint belowA2 budget3000ft; no field datum changes"}],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1020. /world/height_field/land_moa_terrace</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/height_field/land_moa_terrace","old":null,"new":{"boundary":[[17.1,2.2],[17.5,2.2],[17.5,2.6],[17.1,2.6]],"elevation_ft":180,"blend_m":150},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1021. /world/height_field/lesson_ramp_order</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/height_field/lesson_ramp_order","old":null,"new":"after full terrain clearance cuts, before peak cores and pads; finite ramps/headwalls fade continuously; full conflict validator includes them","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1022. /world/height_field/order</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/height_field/order","old":["coast and bed","regional base","Mokuleia regional lowland override","smooth crest and summit envelopes","lakes and glacier tongues","canyon cuts","pass and box-fork cuts","reassert peak centre elevations with compact smooth envelopes","flat/sloped pads with continuous blend","coast fade to ocean bed"],"new":["continuous coast/oceanbed","regional envelopes, lakes/glaciers/canyons","independent continuous outerislands and radialSitkavolcano","continuousMOA ridges and hazardknoll","fullfinal/airway/perimeterterrainbudget cuts","finite box-canyon ramp/headwall reassertion","preserve summit cores and flatMcCall bench","Drake roof/crown and pirateupperroof","analyticpad detail and continuous inverse-distance exteriorblends","clip exteriorblends to final/airway envelopes","coastfade; namedDrake northcliff/opening asheight-aware shell"],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1023. /world/height_field/pad_blend</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/height_field/pad_blend","old":null,"new":"inverse-distance squared weighting outside exactpad rectangles, maxinfluence envelope; final/airway exteriorblends capped with30m fade backto eachplane edge","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1024. /world/height_field/peaks/Chugach/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/height_field/peaks/Chugach/centre","old":[-5.4,-1.4],"new":[-3.8,-1.8],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1025. /world/height_field/peaks/Denali-like/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/height_field/peaks/Denali-like/centre","old":[-3.46,5.86],"new":[0.5,7.1],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1026. /world/height_field/peaks/Mokuleia/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/height_field/peaks/Mokuleia/centre","old":[5.6,4],"new":[5.8,3.3],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1027. /world/height_field/peaks/Sierra/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/height_field/peaks/Sierra/centre","old":[-1.54,-0.58],"new":[-1.6,-0.3],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1028. /world/height_field/water_surface_checks</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/height_field/water_surface_checks","old":null,"new":"All water planes use circumscribed64-gon ellipse enclosures, tested against everyfinal and2nm airway swath; waterheight overridesbedheight forclearance. Highlakebeds/rims restored afterbroadcuts tosupport thewater.","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1029. /world/islets</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/islets","old":null,"new":[{"id":"SS01","centre":[-12.6,1.2],"radii_km":[0.13,0.091],"elevation_ft":35},{"id":"SS02","centre":[-12.4,1.7],"radii_km":[0.1,0.06999999999999999],"elevation_ft":45},{"id":"SS03","centre":[-12.8,2],"radii_km":[0.09,0.063],"elevation_ft":55},{"id":"SS04","centre":[-12.2,0.8],"radii_km":[0.13,0.091],"elevation_ft":35},{"id":"SS05","centre":[-12.9,0.3],"radii_km":[0.12,0.08399999999999999],"elevation_ft":45},{"id":"SS06","centre":[-13.2,-1.3],"radii_km":[0.15,0.105],"elevation_ft":55},{"id":"SS07","centre":[-14,-1.6],"radii_km":[0.1,0.06999999999999999],"elevation_ft":35},{"id":"SS08","centre":[-15.8,0.2],"radii_km":[0.09,0.063],"elevation_ft":45},{"id":"SS09","centre":[-12,2.3],"radii_km":[0.09,0.063],"elevation_ft":55},{"id":"SS10","centre":[-12.3,3.3],"radii_km":[0.12,0.08399999999999999],"elevation_ft":35},{"id":"SS11","centre":[-11.9,1.3],"radii_km":[0.08,0.055999999999999994],"elevation_ft":45},{"id":"SS12","centre":[-13.2,4],"radii_km":[0.11,0.077],"elevation_ft":55},{"id":"CZ1-IS1","centre":[8.8,16.0],"radii_km":[0.35,0.55],"elevation_ft":60,"combat_zone":"CZ1","relief":{"type":"ridge","top_ft":110,"radius_km":0.24499999999999997}},{"id":"CZ1-IS2","centre":[9.8,16.0],"radii_km":[0.35,0.55],"elevation_ft":60,"combat_zone":"CZ1","relief":{"type":"rolling island","top_ft":110,"radius_km":0.24499999999999997}},{"id":"CZ1-IS3","centre":[9.3,16.8],"radii_km":[0.34,0.25],"elevation_ft":60,"combat_zone":"CZ1","relief":{"type":"rolling island","top_ft":110,"radius_km":0.175}},{"id":"CZ1-IS4","centre":[8.5,15.4],"radii_km":[0.2,0.24],"elevation_ft":60,"combat_zone":"CZ1","relief":{"type":"rolling island","top_ft":110,"radius_km":0.13999999999999999}},{"id":"CZ1-IS5","centre":[10.05,16.35],"radii_km":[0.17,0.2],"elevation_ft":60,"combat_zone":"CZ1","relief":{"type":"spire","top_ft":110,"radius_km":0.119}},{"id":"CZ2-IS1","centre":[16.0,-7.5],"radii_km":[0.35,0.55],"elevation_ft":60,"combat_zone":"CZ2","relief":{"type":"ridge","top_ft":110,"radius_km":0.24499999999999997}},{"id":"CZ2-IS2","centre":[17.0,-7.5],"radii_km":[0.35,0.55],"elevation_ft":60,"combat_zone":"CZ2","relief":{"type":"rolling island","top_ft":110,"radius_km":0.24499999999999997}},{"id":"CZ2-IS3","centre":[16.5,-6.7],"radii_km":[0.34,0.25],"elevation_ft":60,"combat_zone":"CZ2","relief":{"type":"rolling island","top_ft":110,"radius_km":0.175}},{"id":"CZ2-IS4","centre":[15.7,-8.1],"radii_km":[0.2,0.24],"elevation_ft":60,"combat_zone":"CZ2","relief":{"type":"rolling island","top_ft":110,"radius_km":0.13999999999999999}},{"id":"CZ2-IS5","centre":[17.35,-6.85],"radii_km":[0.17,0.2],"elevation_ft":60,"combat_zone":"CZ2","relief":{"type":"spire","top_ft":110,"radius_km":0.119}},{"id":"CZ3-IS1","centre":[-8.4,17.0],"radii_km":[0.35,0.55],"elevation_ft":60,"combat_zone":"CZ3","relief":{"type":"ridge","top_ft":110,"radius_km":0.24499999999999997}},{"id":"CZ3-IS2","centre":[-7.4,17.0],"radii_km":[0.35,0.55],"elevation_ft":60,"combat_zone":"CZ3","relief":{"type":"rolling island","top_ft":110,"radius_km":0.24499999999999997}},{"id":"CZ3-IS3","centre":[-7.9,17.4],"radii_km":[0.34,0.18],"elevation_ft":60,"combat_zone":"CZ3","relief":{"type":"rolling island","top_ft":110,"radius_km":0.175}},{"id":"CZ3-IS4","centre":[-8.7,16.4],"radii_km":[0.2,0.24],"elevation_ft":60,"combat_zone":"CZ3","relief":{"type":"rolling island","top_ft":110,"radius_km":0.13999999999999999}},{"id":"CZ3-IS5","centre":[-7.05,17.65],"radii_km":[0.17,0.2],"elevation_ft":60,"combat_zone":"CZ3","relief":{"type":"spire","top_ft":110,"radius_km":0.119}},{"id":"CZ1-ARCH-W","centre":[9.725,16.4],"radii_km":[0.025,0.04],"elevation_ft":110,"combat_zone":"CZ1","pedestal_only":true},{"id":"CZ1-ARCH-E","centre":[9.875,16.4],"radii_km":[0.025,0.04],"elevation_ft":110,"combat_zone":"CZ1","pedestal_only":true},{"id":"CZ2-ARCH-W","centre":[16.925,-7.1],"radii_km":[0.025,0.04],"elevation_ft":110,"combat_zone":"CZ2","pedestal_only":true},{"id":"CZ2-ARCH-E","centre":[17.075,-7.1],"radii_km":[0.025,0.04],"elevation_ft":110,"combat_zone":"CZ2","pedestal_only":true},{"id":"CZ3-ARCH-W","centre":[-7.475,17.4],"radii_km":[0.025,0.04],"elevation_ft":110,"combat_zone":"CZ3","pedestal_only":true},{"id":"CZ3-ARCH-E","centre":[-7.325,17.4],"radii_km":[0.025,0.04],"elevation_ft":110,"combat_zone":"CZ3","pedestal_only":true},{"id":"CZ5-ARCH-W","centre":[17.355,2.488],"radii_km":[0.025,0.04],"elevation_ft":188,"combat_zone":"CZ5","pedestal_only":true},{"id":"CZ5-ARCH-E","centre":[17.465,2.488],"radii_km":[0.025,0.04],"elevation_ft":188,"combat_zone":"CZ5","pedestal_only":true}],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1030. /world/landable_beaches</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/landable_beaches","old":null,"new":[{"id":"B1","name":"B1 hard-sand berm","centre":[2.8,-8.5],"heading_true_deg":91,"length_m":350,"width_m":35,"elevation_ft":12,"slope_percent":0,"surface":"hard sand","pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"island":"main","shore_class":"coastal","note":"Dry upper beach on broad berm; usable strip set back >=300 m from shoreline, 60 m shoulders; wet foreshore not usable runway."},{"id":"B2","name":"B2 hard-sand berm","centre":[-8.6,5.9],"heading_true_deg":35,"length_m":300,"width_m":35,"elevation_ft":12,"slope_percent":0,"surface":"hard sand","pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"island":"main","shore_class":"coastal","note":"Dry upper beach on broad berm; usable strip set back >=300 m from shoreline, 60 m shoulders; wet foreshore not usable runway."},{"id":"B3","name":"B3 hard-sand berm","centre":[-10.9,-5.4],"heading_true_deg":0,"length_m":300,"width_m":35,"elevation_ft":12,"slope_percent":0,"surface":"hard sand","pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"island":"main","shore_class":"coastal","note":"Dry upper beach on broad berm; usable strip set back >=300 m from shoreline, 60 m shoulders; wet foreshore not usable runway."},{"id":"B4","name":"B4 hard-sand berm","centre":[-13.6,-0.3],"heading_true_deg":40,"length_m":300,"width_m":35,"elevation_ft":12,"slope_percent":0,"surface":"hard sand","pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"island":"OI1","shore_class":"coastal","note":"Dry upper beach on broad berm; usable strip set back >=300 m from shoreline, 60 m shoulders; wet foreshore not usable runway."},{"id":"B5","name":"B5 hard-sand berm","centre":[11.3,3.7],"heading_true_deg":0,"length_m":350,"width_m":35,"elevation_ft":12,"slope_percent":0,"surface":"hard sand","pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"island":"OI2","shore_class":"coastal","note":"Dry upper beach on broad berm; usable strip set back >=300 m from shoreline, 60 m shoulders; wet foreshore not usable runway."},{"id":"B6","name":"B6 hard-sand berm","centre":[0.6,11.8],"heading_true_deg":90,"length_m":300,"width_m":35,"elevation_ft":12,"slope_percent":0,"surface":"hard sand","pad":{"shoulder_m":60,"end_shoulder_m":60,"blend_m":200},"island":"OI3","shore_class":"coastal","note":"Dry upper beach on broad berm; usable strip set back >=300 m from shoreline, 60 m shoulders; wet foreshore not usable runway."}],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1031. /world/lesson_terrain/L1/box_floor_rise_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L1/box_floor_rise_m","old":250,"new":400,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1032. /world/lesson_terrain/L1/box_length_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L1/box_length_m","old":600,"new":602.08,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1033. /world/lesson_terrain/L1/climb_reference</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L1/climb_reference","old":"loaded trainer 300 ft/min at 80 kt; target only; Claude verifies aircraft/DA","new":"C172-like1043kg/134000W loaded reference; absolute sustained engine-power ROC upper boundP/(mg) at100%efficiency, zero drag, no DA power loss. Full actual-grid path checked at80kt ground speed; flight/turnback/zoom behavior remains Phase1B.","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1034. /world/lesson_terrain/L1/floor_half_width_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L1/floor_half_width_m","old":null,"new":100,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1035. /world/lesson_terrain/L1/headwall_ft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L1/headwall_ft","old":7500,"new":8000,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1036. /world/lesson_terrain/L1/headwall_geometry</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L1/headwall_geometry","old":null,"new":{"centre":[1.1396891182420392,2.1797236606595907],"top_ft":8000,"radius_m":180,"core_radius_m":50,"blend_m":130},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1037. /world/lesson_terrain/L1/outer_blend_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L1/outer_blend_m","old":null,"new":100,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1038. /world/lesson_terrain/L1/profile_protection</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L1/profile_protection","old":null,"new":"Reassert lesson box floor ramp after broad terrain cuts; approach/airway conflicts remain checked, not silently cut flat.","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1039. /world/lesson_terrain/L1/safe_path</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L1/safe_path","old":[[-0.58,2.78],[0.6,1.7],[1.82,-0.86]],"new":[[-0.88,2.78],[0.6,1.7],[1.85,0.25],[1.82,-0.76]],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1040. /world/lesson_terrain/L2/box_floor_rise_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L2/box_floor_rise_m","old":300,"new":400,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1041. /world/lesson_terrain/L2/box_length_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L2/box_length_m","old":500,"new":353.553,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1042. /world/lesson_terrain/L2/box_path</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L2/box_path","old":[[-4.6,-0.9],[-4.95,-1.25]],"new":[[-4.2,-0.9],[-4.25,-1.25]],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1043. /world/lesson_terrain/L2/climb_reference</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L2/climb_reference","old":"loaded trainer 300 ft/min at 80 kt; target only; Claude verifies aircraft/DA","new":"C172-like1043kg/134000W loaded reference; absolute sustained engine-power ROC upper boundP/(mg) at100%efficiency, zero drag, no DA power loss. Full actual-grid path checked at80kt ground speed; flight/turnback/zoom behavior remains Phase1B.","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1044. /world/lesson_terrain/L2/floor_half_width_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L2/floor_half_width_m","old":null,"new":100,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1045. /world/lesson_terrain/L2/fork</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L2/fork","old":[-4.6,-0.9],"new":[-4.2,-0.9],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1046. /world/lesson_terrain/L2/headwall_ft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L2/headwall_ft","old":6890,"new":6950,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1047. /world/lesson_terrain/L2/headwall_geometry</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L2/headwall_geometry","old":null,"new":{"centre":[-4.266970562748477,-1.36879393923934],"top_ft":6950,"radius_m":180,"core_radius_m":50,"blend_m":130},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1048. /world/lesson_terrain/L2/outer_blend_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L2/outer_blend_m","old":null,"new":100,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1049. /world/lesson_terrain/L2/profile_protection</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L2/profile_protection","old":null,"new":"Reassert lesson box floor ramp after broad terrain cuts; approach/airway conflicts remain checked, not silently cut flat.","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1050. /world/lesson_terrain/L2/saddle_ft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L2/saddle_ft","old":5740,"new":5500,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1051. /world/lesson_terrain/L2/safe_path</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L2/safe_path","old":[[-3.17,-1.73],[-4.6,-0.9],[-5.8,-0.3],[-7.96,-1.8]],"new":[[-3.02,-1.73],[-4.2,-0.9],[-5.8,-0.3],[-10.18,-1.8]],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1052. /world/lesson_terrain/L3/box_floor_rise_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L3/box_floor_rise_m","old":150,"new":250,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1053. /world/lesson_terrain/L3/box_path</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L3/box_path","old":[[1.92,-6.34],[2.16,-6.52]],"new":[[2.9,-6.3],[3.14,-6.48]],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1054. /world/lesson_terrain/L3/climb_reference</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L3/climb_reference","old":"loaded trainer 300 ft/min at 80 kt; target only; Claude verifies aircraft/DA","new":"C172-like1043kg/134000W loaded reference; absolute sustained engine-power ROC upper boundP/(mg) at100%efficiency, zero drag, no DA power loss. Full actual-grid path checked at80kt ground speed; flight/turnback/zoom behavior remains Phase1B.","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1055. /world/lesson_terrain/L3/floor_half_width_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L3/floor_half_width_m","old":null,"new":100,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1056. /world/lesson_terrain/L3/fork</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L3/fork","old":[1.92,-6.34],"new":[2.9,-6.3],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1057. /world/lesson_terrain/L3/headwall_ft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L3/headwall_ft","old":5000,"new":2450,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1058. /world/lesson_terrain/L3/headwall_geometry</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L3/headwall_geometry","old":null,"new":{"centre":[3.236,-6.5520000000000005],"top_ft":2450,"radius_m":180,"core_radius_m":50,"blend_m":130},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1059. /world/lesson_terrain/L3/outer_blend_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L3/outer_blend_m","old":null,"new":100,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1060. /world/lesson_terrain/L3/profile_protection</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L3/profile_protection","old":null,"new":"Reassert lesson box floor ramp after broad terrain cuts; approach/airway conflicts remain checked, not silently cut flat.","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1061. /world/lesson_terrain/L3/saddle_ft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L3/saddle_ft","old":3200,"new":1500,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1062. /world/lesson_terrain/L3/safe_path</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L3/safe_path","old":[[2.88,-5.38],[1.92,-6.34],[0,-6.72],[-1.54,-9.6]],"new":[[4.58,-5.38],[2.9,-6.3],[0,-6.72],[-1.54,-9.6]],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1063. /world/lesson_terrain/L4/climb_reference</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L4/climb_reference","old":"loaded trainer 300 ft/min at 80 kt; target only; Claude verifies aircraft/DA","new":"C172-like1043kg/134000W loaded reference; absolute sustained engine-power ROC upper boundP/(mg) at100%efficiency, zero drag, no DA power loss. Full actual-grid path checked at80kt ground speed; flight/turnback/zoom behavior remains Phase1B.","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1064. /world/lesson_terrain/L4/floor_half_width_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L4/floor_half_width_m","old":null,"new":100,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1065. /world/lesson_terrain/L4/headwall_ft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L4/headwall_ft","old":8530,"new":2900,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1066. /world/lesson_terrain/L4/headwall_geometry</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L4/headwall_geometry","old":null,"new":{"centre":[-4.5,5.22],"top_ft":2900,"radius_m":180,"core_radius_m":50,"blend_m":130},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1067. /world/lesson_terrain/L4/outer_blend_m</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L4/outer_blend_m","old":null,"new":100,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1068. /world/lesson_terrain/L4/profile_protection</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lesson_terrain/L4/profile_protection","old":null,"new":"Reassert lesson box floor ramp after broad terrain cuts; approach/airway conflicts remain checked, not silently cut flat.","reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1069. /world/lift_terrain/S3/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/lift_terrain/S3/centre","old":[4.5,-4.6],"new":[5.7,-4.6],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1070. /world/main_island_size_km_computed</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/main_island_size_km_computed","old":null,"new":[20.2,17.6],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1071. /world/outer_islands</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/outer_islands","old":null,"new":[{"id":"OI1","name":"Sitka Sound","coast_polygon":[[-15.7,-0.8],[-15.7,0.55],[-15.2,0.9],[-15.2,1.3],[-15.7,1.65],[-15.7,3.9],[-14.7,4.7],[-12.4,4.5],[-12.1,2.5],[-13.1,2.1],[-13.7,1.6],[-14.8,1.25],[-14.8,0.95],[-13.3,0.9],[-12.5,1],[-12.7,-0.6],[-14,-1.2]],"base_ft":60,"airport":"SITX","cove":{"mouth":[-12.7,1.6],"head":[-14.8,1.1],"shelter":"east-facing cove behind southern arm and islets"},"volcano":{"centre":[-13.35,3.2],"radius_km":0.85,"rim_radius_km":0.14,"rim_ft":2600,"crater_floor_ft":2250,"shape":"symmetric stratovolcano: linear radial cone, 280 m summit crater; continuous radial crater bowl","clearance_reservation":{"minimum_full_final_boundary_km":0.1,"preserve_radial_formula_from_outer_erosion_guards":true,"rule":"No terraincut inside cone disk; entire cone is at least100m outsideactual protected finalfans. Nonessential outer guard doesnot deform it. Fullarea terrain and obstacle checks remain mandatory."}},"runway_note":"1100x30m asphalt waterfront fill at30ft; causeway midsection has water200m from each side ofcentreline, with150m total runway+shoulders and125m land margin beyondpad. Ends open into widefill lobes to preserve>=300m end/shore setback. Volcano full footprint kept clear of all final cuts."},{"id":"OI2","name":"Tern Island","coast_polygon":[[10.6,0.3],[10.6,4],[11.5,4.6],[17.4,4.3],[18.2,3.5],[18.2,0.3],[13,-0.3],[11.4,-0.2]],"base_ft":180,"airport":"TERN"},{"id":"OI3","name":"North Haven","coast_polygon":[[-1.3,8.8],[-1.3,12.1],[-0.4,12.7],[1.4,12.2],[1.4,9],[0.5,8.6]],"base_ft":120,"airport":"NHVN"},{"id":"OI4","name":"Kite Anchorage","coast_polygon":[[9.059537111430712,14.736634124656932],[9.007577789750602,14.660780346759742],[8.938378532734522,14.595863794354662],[8.853123358855669,14.542995207651611],[8.753271007776318,14.503079182827022],[8.640529980907736,14.476798694132455],[8.516829308477357,14.464603408015801],[8.38428554328678,14.46670198920369],[8.245166545906484,14.48305853039016],[8.101852680952717,14.513393166620622],[7.956796088388952,14.557186863858819],[7.812478726731241,14.613690299803103],[7.671369906049876,14.681936684998751],[7.535884037389474,14.760758304873598],[7.408339321526593,14.848806499658098],[7.2909180839117225,14.944574740327921],[7.18562943447577,15.04642440573323],[7.094274891202115,15.152612819860883],[7.018417555654432,15.26132306950416],[6.95935536787557,15.370695092151493],[6.918098898273558,15.478857502171731],[6.8953540564817395,15.583959610741143],[6.891510013049234,15.684203091642297],[6.906632540624942,15.77787275112423],[6.940462888569288,15.863365875343069],[6.992422210249398,15.93921965324026],[7.061621467265479,16.00413620564534],[7.146876641144329,16.05700479234839],[7.246728992223681,16.09692081717298],[7.3594700190922655,16.123201305867546],[7.483170691522641,16.1353965919842],[7.61571445671322,16.133298010796313],[7.754833454093518,16.11694146960984],[7.8981473190472835,16.08660683337938],[8.043203911611048,16.04281313614118],[8.187521273268759,15.986309700196898],[8.328630093950123,15.91806331500125],[8.464115962610526,15.839241695126404],[8.591660678473406,15.751193500341904],[8.709081916088277,15.655425259672082],[8.81437056552423,15.553575594266771],[8.905725108797885,15.44738718013912],[8.981582444345568,15.338676930495843],[9.040644632124428,15.229304907848508],[9.081901101726443,15.12114249782827],[9.104645943518262,15.016040389258858],[9.108489986950767,14.915796908357706],[9.093367459375056,14.822127248875772]],"base_ft":50,"airport":"KITE","under_zone":"CZ1"},{"id":"OI5","name":"Rime Harbour","coast_polygon":[[-8.50515491293395,15.471031484164897],[-8.455274494240175,15.378721822752956],[-8.423269618602582,15.276510070501713],[-8.409687898316758,15.166145098830631],[-8.41476172038236,15.04951528300396],[-8.438404270297115,14.928616191506384],[-8.480211017477027,14.805516441312989],[-8.539466636886829,14.682322303279282],[-8.615157248449657,14.561141663262887],[-8.705987764816271,14.444047955612108],[-8.810404050668659,14.333044686129357],[-8.926619514406225,14.230031151531405],[-9.052645677223403,14.136769941955967],[-9.186326196533338,14.054856782555776],[-9.325373761587397,13.985693230198786],[-9.467409229996235,13.930462692441582],[-9.610002335515707,13.890110179097992],[-9.750713270576096,13.865326132859153],[-9.887134432066079,13.856534615627645],[-10.016931616089465,13.863886052700773],[-10.137883956841042,13.88725465895201],[-10.247921926236224,13.926240591049394],[-10.345162744110372,13.980176788885796],[-10.427942593109407,14.0481403891623],[-10.49484508706605,14.128968515835105],[-10.544725505759825,14.221278177247045],[-10.576730381397418,14.323489929498288],[-10.590312101683242,14.433854901169369],[-10.58523827961764,14.550484716996042],[-10.561595729702885,14.671383808493617],[-10.519788982522973,14.794483558687013],[-10.460533363113171,14.91767769672072],[-10.384842751550341,15.038858336737114],[-10.294012235183729,15.155952044387893],[-10.189595949331341,15.266955313870644],[-10.073380485593775,15.369968848468597],[-9.947354322776597,15.463230058044035],[-9.813673803466662,15.545143217444224],[-9.674626238412603,15.614306769801216],[-9.532590770003765,15.66953730755842],[-9.389997664484293,15.70988982090201],[-9.249286729423904,15.734673867140849],[-9.112865567933921,15.743465384372357],[-8.983068383910535,15.736113947299229],[-8.862116043158958,15.712745341047992],[-8.752078073763776,15.673759408950609],[-8.65483725588963,15.619823211114205],[-8.572057406890593,15.5518596108377]],"base_ft":70,"airport":"RIME","under_zone":"CZ3"}],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1072. /world/outer_navaid_sites</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/outer_navaid_sites","old":null,"new":[{"id":"SITE-SITX","centre":[-15.05,1.1],"elevation_ft":30,"antenna_height_m":0,"owner":"Claude assigns equipment, ident, frequency and procedure; position/elevation only"},{"id":"SITE-TERN","centre":[12.1,2],"elevation_ft":180,"antenna_height_m":0,"owner":"Claude assigns equipment, ident, frequency and procedure; position/elevation only"},{"id":"SITE-NHVN","centre":[0,10.65],"elevation_ft":120,"antenna_height_m":0,"owner":"Claude assigns equipment, ident, frequency and procedure; position/elevation only"},{"id":"SITE-KITE","centre":[8.05,15.3],"elevation_ft":50,"owner":"Claude equipment/ident/frequency; position/elevation only"},{"id":"SITE-RIME","centre":[-9.45,14.8],"elevation_ft":70,"owner":"Claude equipment/ident/frequency; position/elevation only"}],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1073. /world/perimeter_traffic_reservation</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/perimeter_traffic_reservation","old":null,"new":{"owner":"Codex proposed terrain/land-use reservation for Claude; not T201 geometry","purpose":"Outer-airport traffic runs around main island via water perimeter; avoid interior crossings, combat and box reservations","path":[[-15,1.1],[-13,-5.5],[-8,-10],[0.5,-11],[8.8,-9],[11.3,2],[8,8],[0,10.3],[-7,9],[-15,1.1]],"half_width_km":0.35,"floor_ft":6000,"ceiling_ft":7000,"note":"Claude-owned IFR not built: these are proposed terrain/traffic reservations.2nm terrain swath for clearance;350m flight half-width. Northern segments target11000ft to clear8530ft Denali; other segments6000ft. Descend below3000ft outside combat before entering protected KITE/RIME arrival corridors.","terrain_half_width_km":3.704,"mea_ft_targets":[6000,6000,6000,6000,6000,6000,11000,11000,6000],"ceiling_above_mea_ft":1000},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1074. /world/practice_lines/ST-3U2/path</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/practice_lines/ST-3U2/path","old":[[1.871868,-0.361736],[1.664722,-1.336284]],"new":[[1.871868,-0.261736],[1.664722,-1.236284]],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1075. /world/practice_lines/ST-KMYL/path</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/practice_lines/ST-KMYL/path","old":[[-0.631895,3.379186],[-0.610952,2.179368]],"new":[[-0.931895,3.379186],[-0.910952,2.179368]],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1076. /world/practice_lines/ST-KPGA/path</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/practice_lines/ST-KPGA/path","old":[[3.583754,-2.678294],[3.792132,-3.860064]],"new":[[5.633754,-0.478294],[5.842132,-1.660064]],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1077. /world/practice_lines/ST-PALH/path</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/practice_lines/ST-PALH/path","old":[[-8.56,-1.858651],[-7.36,-1.858651]],"new":[[-10.391944,-1.235625],[-10.081361,-2.394736]],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1078. /world/practice_lines/ST-PANC/path</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/practice_lines/ST-PANC/path","old":[[-8.853711,-2.506098],[-8.543128,-3.665209]],"new":[[-10.453711,-2.506098],[-10.143128,-3.665209]],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1079. /world/starts/3U2:InTheAir/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/starts/3U2:InTheAir/centre","old":[2.135643,0.624985],"new":[2.135643,0.724985],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1080. /world/starts/3U2:OnFinal/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/starts/3U2:OnFinal/centre","old":[2.094061,0.429355],"new":[2.094061,0.529355],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1081. /world/starts/3U2:OnTheRunway/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/starts/3U2:OnTheRunway/centre","old":[1.911099,-0.431415],"new":[1.911099,-0.331415],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1082. /world/starts/AB2/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/starts/AB2/centre","old":[-10.1,-3.6],"new":[-12.2,-6.3],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1083. /world/starts/AB3/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/starts/AB3/centre","old":[-2.5,4],"new":[-1.8,2.8],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1084. /world/starts/CZ1/altitude_ft_target</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/starts/CZ1/altitude_ft_target","old":5000,"new":900,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1085. /world/starts/CZ1/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/starts/CZ1/centre","old":[8.999768,14.126807],"new":[9.3,16],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1086. /world/starts/CZ2/altitude_ft_target</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/starts/CZ2/altitude_ft_target","old":5000,"new":900,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1087. /world/starts/CZ2/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/starts/CZ2/centre","old":[14.954078,-6.973201],"new":[16.5,-7.5],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1088. /world/starts/CZ3/altitude_ft_target</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/starts/CZ3/altitude_ft_target","old":5000,"new":900,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1089. /world/starts/CZ3/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/starts/CZ3/centre","old":[-7.849726,15.079184],"new":[-7.9,17],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1090. /world/starts/CZ4</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/starts/CZ4","old":{"id":"CZ4","mode":"InCombatZone","enabled":false,"centre":[-15.180655,-7.078856],"altitude_ft_target":5000,"heading_true_deg":335.0},"new":null,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1091. /world/starts/CZ5</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/starts/CZ5","old":null,"new":{"id":"CZ5","mode":"InCombatZone","enabled":true,"centre":[17.3,2.4],"altitude_ft_target":600,"heading_true_deg":0},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1092. /world/starts/KMYL:InTheAir/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/starts/KMYL:InTheAir/centre","old":[-0.61368,4.709498],"new":[-0.91368,4.709498],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1093. /world/starts/KMYL:OnFinal/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/starts/KMYL:OnFinal/centre","old":[-0.610189,4.509529],"new":[-0.910189,4.509529],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1094. /world/starts/KMYL:OnTheRunway/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/starts/KMYL:OnTheRunway/centre","old":[-0.594831,3.629663],"new":[-0.894831,3.629663],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1095. /world/starts/KPGA:InTheAir/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/starts/KPGA:InTheAir/centre","old":[3.408891,-1.382188],"new":[5.458891,0.817812],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1096. /world/starts/KPGA:OnFinal/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/starts/KPGA:OnFinal/centre","old":[3.443621,-1.57915],"new":[5.493621,0.62085],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1097. /world/starts/KPGA:OnTheRunway/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/starts/KPGA:OnTheRunway/centre","old":[3.596431,-2.445781],"new":[5.646431,-0.245781],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1098. /world/starts/PALH:InTheAir/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/starts/PALH:InTheAir/centre","old":[-9.652048,-1.8],"new":[-10.617934,-0.165607],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1099. /world/starts/PALH:InTheAir/heading_true_deg</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/starts/PALH:InTheAir/heading_true_deg","old":90,"new":165,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1100. /world/starts/PALH:OnFinal/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/starts/PALH:OnFinal/centre","old":[-9.452048,-1.8],"new":[-10.566171,-0.358792],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1101. /world/starts/PALH:OnFinal/heading_true_deg</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/starts/PALH:OnFinal/heading_true_deg","old":90,"new":165,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1102. /world/starts/PALH:OnTheRunway/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/starts/PALH:OnTheRunway/centre","old":[-8.572048,-1.8],"new":[-10.33841,-1.208807],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1103. /world/starts/PALH:OnTheRunway/heading_true_deg</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/starts/PALH:OnTheRunway/heading_true_deg","old":90,"new":165,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1104. /world/starts/PANC:InTheAir/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/starts/PANC:InTheAir/centre","old":[-9.327378,-0.504669],"new":[-10.927378,-0.504669],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1105. /world/starts/PANC:OnFinal/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/starts/PANC:OnFinal/centre","old":[-9.275615,-0.697854],"new":[-10.875615,-0.697854],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1106. /world/starts/PANC:OnTheRunway/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/starts/PANC:OnTheRunway/centre","old":[-9.047854,-1.547869],"new":[-10.647854,-1.547869],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1107. /world/water/Bighorn Cirque/basin_detail</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/water/Bighorn Cirque/basin_detail","old":null,"new":{"bank_width_m":40,"ridge_offset_m":10,"ridge_height_m":5,"bed_rule":"Waterminus depth atcentre, smoothly towater atshore; narrow5m closedrockrim, thenfade to surroundingterrain. Opening/outletsolidshape awaitsPhase1B."},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1108. /world/water/Bighorn Cirque/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/water/Bighorn Cirque/centre","old":[1.2,0.8],"new":[0.3,0.1],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1109. /world/water/Bighorn Crown/basin_detail</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/water/Bighorn Crown/basin_detail","old":null,"new":{"bank_width_m":40,"ridge_offset_m":10,"ridge_height_m":5,"bed_rule":"Waterminus depth atcentre, smoothly towater atshore; narrow5m closedrockrim, thenfade to surroundingterrain. Opening/outletsolidshape awaitsPhase1B."},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1110. /world/water/Bighorn Crown/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/water/Bighorn Crown/centre","old":[2.7,1.9],"new":[0.7,3.2],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1111. /world/water/Bighorn Hidden/basin_detail</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/water/Bighorn Hidden/basin_detail","old":null,"new":{"bank_width_m":40,"ridge_offset_m":10,"ridge_height_m":5,"bed_rule":"Waterminus depth atcentre, smoothly towater atshore; narrow5m closedrockrim, thenfade to surroundingterrain. Opening/outletsolidshape awaitsPhase1B."},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1112. /world/water/Bighorn Hidden/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/water/Bighorn Hidden/centre","old":[0.2,1.3],"new":[0.2,2.95],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1113. /world/water/Bighorn Larch/basin_detail</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/water/Bighorn Larch/basin_detail","old":null,"new":{"bank_width_m":40,"ridge_offset_m":10,"ridge_height_m":5,"bed_rule":"Waterminus depth atcentre, smoothly towater atshore; narrow5m closedrockrim, thenfade to surroundingterrain. Opening/outletsolidshape awaitsPhase1B."},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1114. /world/water/Bighorn Larch/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/water/Bighorn Larch/centre","old":[3.3,2],"new":[0.4,2.05],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1115. /world/water/Bighorn Larch/long_axis_true_deg</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/water/Bighorn Larch/long_axis_true_deg","old":null,"new":0,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1116. /world/water/Bighorn Outlet/basin_detail</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/water/Bighorn Outlet/basin_detail","old":null,"new":{"bank_width_m":40,"ridge_offset_m":10,"ridge_height_m":5,"bed_rule":"Waterminus depth atcentre, smoothly towater atshore; narrow5m closedrockrim, thenfade to surroundingterrain. Opening/outletsolidshape awaitsPhase1B."},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1117. /world/water/Bighorn Outlet/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/water/Bighorn Outlet/centre","old":[0.7,0.9],"new":[0,2.4],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1118. /world/water/Bighorn Outlet/long_axis_true_deg</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/water/Bighorn Outlet/long_axis_true_deg","old":null,"new":0,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1119. /world/water/Bighorn Shelf/basin_detail</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/water/Bighorn Shelf/basin_detail","old":null,"new":{"bank_width_m":40,"ridge_offset_m":10,"ridge_height_m":5,"bed_rule":"Waterminus depth atcentre, smoothly towater atshore; narrow5m closedrockrim, thenfade to surroundingterrain. Opening/outletsolidshape awaitsPhase1B."},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1120. /world/water/Bighorn Shelf/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/water/Bighorn Shelf/centre","old":[1.8,2.7],"new":[0.3,3.5],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1121. /world/water/Lake Hood/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/water/Lake Hood/centre","old":[-7.96,-1.8],"new":[-10.18,-1.8],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1122. /world/water/Lake Hood/long_axis_true_deg</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/water/Lake Hood/long_axis_true_deg","old":null,"new":165,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1123. /world/water/Lake Hood/radii_km</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/water/Lake Hood/radii_km","old":[0.85,0.18],"new":[0.78,0.16],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1124. /world/water/Lake Powell/basin_detail</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/water/Lake Powell/basin_detail","old":null,"new":{"bank_width_m":40,"ridge_offset_m":10,"ridge_height_m":5,"bed_rule":"Waterminus depth atcentre, smoothly towater atshore; narrow5m closedrockrim, thenfade to surroundingterrain. Opening/outletsolidshape awaitsPhase1B."},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1125. /world/water/Lake Powell/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/water/Lake Powell/centre","old":[2.775,-4.525],"new":[4.475,-4.525],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1126. /world/water/Payette Lake/basin_detail</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/water/Payette Lake/basin_detail","old":null,"new":{"bank_width_m":40,"ridge_offset_m":10,"ridge_height_m":5,"bed_rule":"Waterminus depth atcentre, smoothly towater atshore; narrow5m closedrockrim, thenfade to surroundingterrain. Opening/outletsolidshape awaitsPhase1B."},"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1127. /world/water/Payette Lake/centre</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/water/Payette Lake/centre","old":[-0.48,4.42],"new":[-0.6,3.9],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1128. /world/water/Payette Lake/elevation_ft</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/water/Payette Lake/elevation_ft","old":4990,"new":4900,"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1129. /world/water/Payette Lake/radii_km</summary>

```json
{"phase":"1A.2 final reconciliation","item":"/world/water/Payette Lake/radii_km","old":[0.55,0.8],"new":[0.5,0.6],"reason":"Final design allocation after full pad, water, terrain, final, airway, ring and combat sweeps. Ty decisions1–9 applied; original ICAO elevations/lengths,35% cap,20km ring and cave dimensions retained. Western mainland expansion remains TY1 pending. Chronological entries above explain individual layout iterations."}
```

</details>

<details>
<summary>1130. DRAKE17 operational geometry</summary>

```json
{"phase":"1B","item":"DRAKE17 operational geometry","old":"Runway17 only; reversefinal meets closedfarwall/roof and remainsFAIL until Ty approves explicitone-way exemption. No secondentrance/tunnel added.","new":"ONE WAY IN / ONE WAY OUT through the single north portal. Land runway17; depart back through the same north portal. No reverse final, no second portal.","reason":"Ty ruling 2026-10-30 20:25 PDT; remove the nonoperational reverse fan and its failure, not a clearance exemption."}
```

</details>

<details>
<summary>1131. GATEWAY/centre</summary>

```json
{"phase":"1B","item":"GATEWAY/centre","old":[0.5,-6.7],"new":[0.3,-6.5],"reason":"Built arch whole-footprint audit found AP14 endB conflict; move280m northwest along the C3 neighbourhood to clear all operational finals."}
```

</details>

<details>
<summary>1132. world/runtime_geometry</summary>

```json
{"phase":"1B","item":"world/runtime_geometry","old":null,"new":{"representation":"Compiled C# data plus same-source continuous evaluator; WorldTerrain is shared by renderer/contact. No StreamingAssets read.","implementation":"src/FlyingGame.Core/IslandGeometry.cs; unity/Assets/_Project/Scripts/Bridge/IslandWorldBuilder.cs","city":"25 deterministic building sites inside approved CITY extent. Height caps against whole known final and airway reservations; roof rings only where whole radius fits. Central300m hole tower retained if budget allows; spinning ring uses shared WorldClock.","powell_marina":{"dock_reference":[4.295,-4.525],"reason":"Lake centre minus180m east; docks/boats advance120–220m east so full bodies float inside actual approved lake, away from AP14 protection."},"float_bases":"Lake Hood and Powell ellipse-shore docks and90x24m continuous ramps from1.5m below water to actual drybank height. PALH open-front hangar.","drake":"Continuous260x80m interior slope rising70m, pillowpool70x45m, westapron240x90m, six glasslabs spanning240m; same single northportal only.","markers":"Cross80x4m overall beams (40m arms each side); inward L30m legs; sideT30m inward stem/crossbar. Moored0.5m water decks and orange cornerbuoys.","scenery_randomness":"Explicit fixed uint hash against approved building/parked/driftwood seeds; no process/global random state.","hot_entry_handoff":"Dormant per-zone target/drone geometry and SetHot/SetContentsVisible adapters. Claude implements entry/AI/scoring/ATIS/chart menu bindings.","starts_handoff":"Approved world.starts exposes airport/final/runway/thermal/AB/CZ candidates. Claude implements runway choice, trim and event spawn routing."},"reason":"Concrete solid/mesh placement details for approved inventory. City silhouettes and marina allocation changed after actual built-solid clearance screening; no other field/coast/terrain datum changed."}
```

</details>

<details>
<summary>1133. runtime streams and adapters</summary>

```json
{"phase":"1B","item":"runtime streams and adapters","old":null,"new":{"water":{"paths":"C1-C4 thalwegs and8 meandering shallow braids at each F13/F14/F15 bar complex","depth_m":0.15,"half_width_m":{"C3":18,"other_canyons":10,"braids":4},"bar_clean_lane_m":[520,52],"pad_guard":"No stream on any runway/pad/60m shoulders or inside cavities; float lanes/lakes retain fixed water planes.","representation":"Same IslandWater.StreamAt domain read by water queries and renderer. Stream surface follows approved terrain bed +0.15m; shallow riverbeds are not seaplane float lanes."},"adapters":"IslandSites.Starts/Fields/Boxes/NavaidSitesOnly/Obstructions; IslandCombatZone.ContainsActive/FloorAt/Hot; IslandZoneView.SetHot/SetContentsVisible"},"reason":"Build approved canyon and braided river water, clean gravel lanes and all chart/spawn handoff positions. Preserve Claude ownership of navigation, menus, event/AI/scoring/weather."}
```

</details>

<details>
<summary>1134. F12 landable mesa detail planes</summary>

```json
{"phase":"1B","item":"F12 landable mesa detail planes","old":"Target only: landable mesas and46m cleanlanes atF12, no explicitplane implementation.","new":[{"id":"PAGE-M1","centre":[5,-3.7],"heading_true_deg":0,"length_m":360,"width_m":120,"elevation_ft":4475,"blend_m":150,"clean_lane_width_m":46},{"id":"PAGE-M2","centre":[5.4,-3.7],"heading_true_deg":0,"length_m":360,"width_m":120,"elevation_ft":4075,"blend_m":150,"clean_lane_width_m":46}],"reason":"Build two continuous flat mesa tops and rock-pile/rut strips for the approvedF12 feature. Exact planes and fullknownfan/airway screening; no field elevations/lengths changed."}
```

</details>

<details>
<summary>1135. height_field/pad_blend exterior clearance correction</summary>

```json
{"phase":"1B validation repair","item":"height_field/pad_blend exterior clearance correction","old":"inverse-distance squared weighting outside exactpad rectangles, maxinfluence envelope; final/airway exteriorblends capped with30m fade backto eachplane edge","new":"Inverse-distance squared exterior plane weighting; final/airway correction fades tozero at both exactpad edge (30m release) and outerblend boundary (max pad influence); continuous0.12–0.4km peakcore release, never a boolean gate. Airway budget also applies as a continuous global layer after details, with actual2nm core/continuousouterfade, exactpad30m release and peakcore release; no conditional outer-edge cap switch.","reason":"Existing WorldContinuityTests exposed a238m discontinuity at(E-0.3622,N4.45)km where a zero-influence exteriorpad claim abruptly activated a full-strength clearance cut. Fade by pad influence at the outer edge and continuously release peakcore exclusion. Preserve every exact pad datum and summit. Restored the V2 clearance budget as a global continuous layer after details rather than a conditional pad-claim cut; full pads/summits remain exact."}
```

</details>

<details>
<summary>1136. C2 canyon</summary>

```json
{"item":"C2 canyon","old":"{\"id\":\"C2\",\"path\":[[0.7,-1.8],[2.5,-0.3],[3.1,0.55],[3.5,2],[4,1.35],[5.1,1.3],[3.4,-0.4],[4.4,0.65],[-0.8,-2.8]],\"rim_width_m\":[900,600,400],\"floor_width_m\":[300,225,150],\"depth_m\":[600,600],\"floor_ft\":[4400,3450]}","new":"deleted; Middle Fork valleys come from the generated relief (tools/island/TerrainGen)","reason":"Physically impossible as authored: a single linear floor profile 4,400->3,450 ft along a path that zig-zags through nine strips at 4,400-5,000 ft; see docs/pending/ISLAND-TERRAIN-CLAUDE.md"}
```

</details>

<details>
<summary>1137. summit Frank Church</summary>

```json
{"item":"summit Frank Church","old":"9020 ft","new":"8072.2 ft","reason":"Claude 2026-11-02 island terrain rebuild: its summit stands on the ground the Bighorn Cirque tarn (7,580 ft, 320 m away) holds up; Yellow Pine's (AP10) runway-end surface 0.7 km south-east made 9,020 ft a 1,300 m wall. docs/pending/ISLAND-TERRAIN-CLAUDE.md"}
```

</details>

<details>
<summary>1138. summit Sierra</summary>

```json
{"item":"summit Sierra","old":"7550 ft","new":"7328.7 ft","reason":"Claude 2026-11-02 island terrain rebuild: runway-end surfaces of KMEV/Chamberlain and the AP7 strip within 1.2 km allow this much at a 42-degree mountainside. docs/pending/ISLAND-TERRAIN-CLAUDE.md"}
```

</details>

<details>
<summary>1139. summit Chugach</summary>

```json
{"item":"summit Chugach","old":"6890 ft","new":"5914.9 ft","reason":"Claude 2026-11-02 island terrain rebuild: canyon C4's sea-level floor heads 0.95 km away and the A2 airway swath lies 1 km west; the summit stands on the ground the KMEV/L2 benches hold up beside it. docs/pending/ISLAND-TERRAIN-CLAUDE.md"}
```

</details>

<details>
<summary>1140. summit Denali-like</summary>

```json
{"item":"summit Denali-like","old":"8530 ft","new":"2175.9 ft","reason":"Claude 2026-11-02 island terrain rebuild: the coast is 0.45 km north: 8,530 ft there was a 2,000 m needle (before/22); this is what a 55-degree sea-cliff face allows. docs/pending/ISLAND-TERRAIN-CLAUDE.md"}
```

</details>

<details>
<summary>1141. summit Denali-like -> Fairweather</summary>

```json
{"item":"summit Denali-like -> Fairweather","old":"Denali-like at [0.5, 7.1] 2175.9 ft","new":"Fairweather at [-0.5, 6.3] (top from the relief)","reason":"Claude 2026-11-02 real-topo pass (Ty: the Fairweather area replaces the Ruth feature): the north summit is the compacted Mount Fairweather (tools/island/topo, c = 2.6). It moved 1.2 km SW of the old Denali-like point (0.45 km from the sea, which could hold only 2,176 ft) to the one place near the north coast where ~1,600 m can stand clear of the A2 swath and the PHDH 8 visual surface; docs/pending/ISLAND-TERRAIN-CLAUDE.md"}
```

</details>

<details>
<summary>1142. glacier Ruth -> Fairweather</summary>

```json
{"item":"glacier Ruth -> Fairweather","old":"{\"id\": \"Ruth\", \"path\": [[-3.46, 5.86], [-4.1, 5.2], [-3.9, 4.7]], \"head_ft\": 7300, \"ski_zone\": {\"centre\": [-3.9, 4.7], \"elevation_ft\": 1300, \"length_m\": 450, \"width_m\": 100}, \"half_width_m\": 200}","new":"{\"id\": \"Fairweather\", \"path\": [[-1.2, 5.6], [-3.0, 4.15], [-3.9, 4.7]], \"head_ft\": 5187, \"ski_zone\": {\"centre\": [-3.9, 4.7], \"elevation_ft\": 1300, \"length_m\": 450, \"width_m\": 100}, \"half_width_m\": 200}","reason":"Claude 2026-11-02 real-topo pass, Ty: the Fairweather area replaces the Ruth Glacier feature. The ski zone is CONVERTED, not moved: same centre, 1,300 ft, 450 x 100 m, still Snow and flat. Its glacier is now the SW outlet of the Fairweather icefield, whose lobe ends 0.6 km SSE of the zone at ~1,400 ft; the path follows that outlet. Locked decision 7 (ski zones 1,000-1,300 ft) kept. docs/pending/ISLAND-TERRAIN-CLAUDE.md"}
```

</details>
