# Finding your way without a map that knows where you are

2026-10-18. The owner's brief, in one message:

> *"create a map of the 'playground' that the user can call up at anytime. don't show the user their location
> on the map they have to navigate by ded reconing or vor only. add two more vors on the map on hills so they
> get reasonable reception and make all of the vor's line of sight in function and analize the shaddows. give
> a few runways ils and build instrument approaches based on the terps that allow the user to get from an
> airway (also build airways based on the terps) to the approach. at least one should have a procedure turn.
> create an ifr low chart to go along with the vfr chart and a 'satalite image' so the user can see all three
> but no gps type positions ever."*

And his follow-up, a few hours later, which settled the one thing I had been unsure of:

> *"Add two ndb's and build a few vor and adf approaches to put in the approach plate list. Airways don't
> need to be the full width this is a 'micro' world and terms can be fudged a bit too but we need semi
> realistic IFR capability for future IFR lessons"*

Four VORs, two of them on hills; **two NDBs, which are a different animal on purpose**; a line-of-sight
reception model and the shadow analysis it makes possible; two airways and **seven approach plates** — three
ILS, two VOR and two ADF — with every altitude derived from this world's own ground; three charts; and no
own-ship symbol anywhere, ever.

## The one rule, and how it is kept

**No chart in this game shows you where you are.** Not a triangle, not a track line, not a breadcrumb, not a
"centre on aircraft" button.

It is enforced by structure rather than by discipline. `ChartPanel` is handed no reference to the aeroplane —
no `FlightSimDriver`, no transform, no `Sim` — and `ChartRaster` takes a rectangle of the world and returns
pixels. Neither of them *can* draw your position, because neither of them can find out. Adding the triangle
would mean adding a dependency first, and that is the moment somebody should stop.

The other half of the rule is that you are given what you need instead. `NavRadios` puts two VOR/ILS
receivers, a heading card and a leg clock on the screen. Those **do** read the aeroplane's position, because
that is what a receiver does — but they give you a radial, a needle and a distance, and the triangulation is
yours. Ground speed is deliberately not displayed: knowing it would make the clock pointless, and working the
leg out from the airspeed you are flying and the wind you were given is the exercise.

## Where the stations are, and why

Measured, not chosen. A probe swept the world for local maxima with prominence, kept only ground flat enough
to stand a counterpoise on, and scored each candidate by what fraction of the world can see it through the
terrain:

| Site | Antenna | Sees, 1,000 ft AGL | 3,000 ft AGL |
|---|---|---:|---:|
| Valley airport (400, 0) | 1,000 ft | 24.0 % | 31.7 % |
| Bench airport (400, −3450) | 3,000 ft | 22.6 % | 42.3 % |
| Mesa airport (400, −6750) | 5,000 ft | **14.3 %** | 33.5 % |
| Summit airport (400, −10050) | 7,000 ft | 26.7 % | 43.0 % |
| **first escarpment crest (600, −1180)** | **3,472 ft** | **48.1 %** | **68.5 %** |
| **third escarpment crest (400, −7920)** | **8,135 ft** | **53.5 %** | **76.4 %** |

The owner's instinct was right and the numbers say by how much. Every airport in this world sits in the
middle of a plateau with a 2,000 ft wall along one side of it, so **a station on the field is in a hole**: a
VOR on the Mesa field is heard over a seventh of the world. A station on the CREST of a wall is heard over
half of it, because the wall it stands on was the thing in the way.

It also matters *which part* of the crest. The crest at (400, −1180) is 3,039 ft and sees 23.4 %; two hundred
metres north, at (600, −1180), it is 3,472 ft and sees 48.1 %. Four hundred feet of hill doubles the coverage.

So: **VLY** and **SUM** on the Valley and Summit fields, for their own approaches, and **RIM** (Rimrock) and
**CRS** (Crestline) on the two crests — the two the owner asked for.

Together:

| | one or more stations | two or more (a cross fix) |
|---|---:|---:|
| 1,000 ft AGL | 78.9 % | 58.6 % |
| 2,000 ft AGL | 86.2 % | 72.9 % |
| 3,000 ft AGL | 89.8 % | 81.1 % |
| 5,000 ft AGL | 94.9 % | 89.8 % |

And the promise that makes the world navigable: **along the corridor the four airports sit in, at 2,000 ft
above the ground, there are always at least two stations in view**, from the coast to the top field. You can
always fix your position by crossing two radials. `NavigationTests.TwoStationsAllTheWayDownTheCorridor`
asserts it, so a terrain change cannot quietly take it away.

## The reception model

`NavReception`. A VOR here is an antenna on the ground radiating at about 113 MHz with mountains in the way,
not a circle on a map.

* **Terrain between the ends.** The straight line from antenna to aeroplane is marched at 60 m and the ground
  sampled along it. This is what makes the shadows.
* **Fresnel clearance, not a bare ray.** A path does not switch off the instant the ray grazes a ridge. What
  matters is the first Fresnel zone, radius √(λ·d₁·d₂/d); at 113 MHz λ is 2.65 m, so a 15 km path with the
  obstacle in the middle wants about 100 m of clearance, and the planning criterion is 60 % of that. Below
  that it fades; at the ray itself it is gone. The fade is narrow — behind the escarpment at (400, −2500) the
  whole transition happens between 2,690 and 2,840 ft AGL — but it is a ramp, and a ramp is what stops the
  needle chattering as you cross a ridge line.
* **The cone of confusion.** 25° either side of vertical, so about 0.7 NM across at 3,000 ft above the
  station. Shown as OVER THE STATION rather than as a thrashing needle, because that is a fix and a pilot
  reads station passage from it.
* **Service volume.** 25 NM terminal, 40 NM low. This world is 9 × 15 NM, so range never refuses anybody
  here; the check is written down anyway, because leaving it out would be a lie about what a VOR is.

Not modelled, and said out loud: **the Earth's curvature** (this world's ground is a flat height field by
design; inventing the curve would put the radio on a different planet from the altimeter), and knife-edge
diffraction beyond the Fresnel stand-in.

## The shadows

`NavigationTests.TheShadows` is the analysis, as a test rather than a document, so that moving a mountain
breaks it. It prints the table above and asserts the coverage has not fallen below it.

Two findings worth keeping:

* **No airway minimum altitude in this world is set by the radio.** Every MEA is set by terrain. That is the
  payoff of the hill siting, and `TheHillStationsAreWhyNoAirwayMeaIsSetByTheRadio` asserts it — if a station
  moved down off its hill, some leg would start being limited by reception and the chart would have to say so.
* **Every point in this world is mountainous, including four miles out to sea.** ICAO's test is relief of more
  than 900 m within 10 NM; 10 NM is 18.5 km and this world is 16 by 27, so a circle anywhere in it reaches
  from the beach into country kilometres higher. Relief at the Valley field is 3,170 m; at a fix 4 NM offshore
  it is still 2,676 m. The consequence shows on the chart: **en-route obstacle clearance here is 2,000 ft
  everywhere, never 1,000**, which is most of why short legs carry high MEAs.

## TERPS, and the one place this is not full scale

`Terps` holds the criteria; `ApproachGeometry` and `Airways` do the measuring. The required obstacle
clearances are the standard ones — 1,000 ft en route (2,000 ft mountainous, which here means everywhere),
1,000 ft initial, 500 ft intermediate, 250 ft on a non-precision final, 1,000 ft in a procedure turn; a
precision final is the 34:1 surface (102 ÷ 3) with a 200 ft Category I decision height; a procedure turn is
flown within 10 NM at 200 KIAS or less.

**The lateral protected areas are divided by four** — a decision the owner then sanctioned directly
(*"Airways don't need to be the full width this is a 'micro' world and terms can be fudged a bit too"*), and
one that had to be measured to be believed. A Victor
airway's primary area is 4 NM each side of the centreline — eight nautical miles wide. This world is 8.6 NM
across. A full-size airway down the airport corridor therefore reaches 4 NM north, far enough to include the
17,644 ft peak in the Alaska range, and the MEA between the two hill stations came out at **19,800 ft**:
correct arithmetic, useless airway, and no routing fixes it because nowhere in this world is 4 NM from a
mountain. So the lateral areas are quartered and the real figure is kept beside each one. The **vertical**
criteria are not scaled — these are full-size mountains and 1,000 ft of clearance is 1,000 ft.

The other simplification: a segment's protected area here is a corridor of constant width (a splaying one for
a final, which is the shape of the real thing), with no secondary area in which the clearance tapers to zero.
Both are stated in `Terps` rather than hidden, because one day somebody will want to know whether these charts
could be trusted in a real aeroplane, and the answer needs to be easy to find: **no.**

## The airways

| | course | distance | MEA | MOCA |
|---|---:|---:|---:|---:|
| **V1** SEALS → VLY | 267° | 4.31 NM | 4,100 | 4,100 |
| V1 VLY → CRS | 270° | 4.28 NM | 10,200 | 10,200 |
| V1 CRS → SUM | 270° | 1.15 NM | 10,700 | 10,700 |
| **V2** TALUS → RIM | 180° | 3.78 NM | 5,600 | 5,600 |
| V2 RIM → SCREE | 180° | 3.67 NM | 6,700 | 6,700 |

V1 is the spine: in off the sea at SEALS, over the Valley field, up the escarpment staircase to the top field.
Every airport is on it or within two miles. V2 crosses it at Rimrock and runs north into the Alaska foothills
and south over the high desert; that crossing is the only place two airways meet here, so it is the fix
everything else is described from.

RIM is an **intersection on** the VLY–CRS leg rather than a bend in it: it lies 200 m (0.11 NM) off that line,
and a 0.65 NM airway leg from VLY to RIM would have been a bend nobody could fly.

MEA and MOCA are equal on every leg because the MOCA's weaker promise — a signal only within 22 NM of the
station — is no weaker in a world 15 NM across. That is a property of a small world, not a rule.

## The approaches — seven plates

| | type | minimums | notes |
|---|---|---|---|
| Valley RWY 27 | ILS | DA 1,580 (580 HAT) | the **procedure turn**, over the sea |
| Bench RWY 27 | ILS | DA 3,480 (480 HAT) | the Rimrock hill is the obstacle |
| Bench RWY 27 | NDB | MDA 3,760 (760 HAT) | ADF, beacon 1,000 ft below the field |
| Mesa RWY 27 | ILS | DA 5,200 (**200 HAT**) | nothing penetrates in ten miles |
| Mesa RWY 27 | VOR | MDA 5,260 (260 HAT) | the same runway, for the comparison |
| Summit RWY 36 | VOR | MDA 7,260 (260 HAT) | no course reversal possible |
| Summit RWY 09 | NDB | MDA 7,260 (260 HAT) | ADF, straight-in only |

**Every ILS here is on runway 27, and the terrain decided that, not me.** Every runway end in the world was
evaluated against the 34:1 surface. The land climbs westward in three 2,000 ft steps, so a final approach
course of 270 descends toward a threshold with lower ground behind it all the way, and one of 090 descends
toward a threshold with a 2,000 ft wall behind it:

| | runway 27 (from the low side) | runway 09 (from the high side) |
|---|---:|---:|
| Valley | 580 ft HAT | 9,580 ft HAT |
| Bench | 480 ft HAT | 6,420 ft HAT |
| Mesa | **200 ft HAT** | 3,880 ft HAT |
| Summit | 1,180 ft HAT | **200 ft HAT** |

Summit is the exception that proves it: it is the top plateau, so its low side is the west, and 09 is the one
that works there.

### Valley ILS RWY 27 — the one with the procedure turn

I-VLY 109.10, final 270°, TDZE 1,000 ft, **DA 1,580 ft (580 HAT)**. IAF and FAF both at **SEALS** (VLY R-087
at 4.3 DME), 4.0 NM out over the water. **Procedure turn at SEALS, remain within 2.0 NM, 3,100 ft**, then
cross SEALS inbound at 2,500 ft for the glideslope at 2,274.

This is the only course reversal in the world and the only place there was room for one: it is flown over open
sea, so the 2.0 NM protected area holds nothing but water. Arriving on V1 from the west the course change is
180°, which is exactly the case a procedure turn exists for.

The decision altitude is 580 ft above touchdown rather than 200 because **an island 3.6 NM out on the final
stands 948 ft above the field and pushes 380 ft through the 34:1 surface.**

### Bench ILS RWY 27

I-BCH 109.90, final 270°, TDZE 3,000 ft, **DA 3,480 ft (480 HAT)**. FAF **BUOYS** (RIM R-088 at 3.1 DME) at
4,300 ft.

The obstacle that costs this approach its 200 ft minimum is **the hill the Rimrock VOR stands on**: the crest
reaches 3,472 ft, 472 ft above the field, 0.9 NM from the threshold, and pushes 271 ft through the surface.
The station that gets you there is on the thing in your way, which is worth knowing and is why it is charted.

### Mesa ILS RWY 27 — the clean one

I-MSA 110.30, final 270°, TDZE 5,000 ft, **DA 5,200 ft (200 HAT)**. FAF **SHELF** (RIM R-084 at 1.3 DME) at
6,300 ft. Straight in off V1 westbound; the airway and the final approach course are the same 270.

Nothing penetrates the 34:1 surface anywhere in ten miles, because the whole approach is flown over the
plateau staircase *below* the field — at the final approach fix you are 5,274 ft above the ground, and the
land climbs two 2,000 ft steps to come up and meet you. It also passes within half a mile of the Valley
airport at 5,955 ft, which is worth a look on the way past.

### Summit VOR RWY 36

SUM 113.40, final approach course the 180 radial flown inbound on 360°, TDZE 7,000 ft, **MDA 7,260 ft
(260 HAT)**. FAF **CEDAR** (SUM R-180 at 3.4 DME) at 7,600 ft — 136 ft/NM to the MDA, which is gentle.

The Summit VOR sits exactly on the main runway's centreline, so the radial *is* the centreline.

**No course reversal is published, and that is the terrain's decision rather than an omission.** A procedure
turn within 2.0 NM of the station would have to be flown at 11,500 ft, within 2.5 NM at 14,500 ft, within
3.0 NM at 18,600 ft, because Summit is ringed by the alpine range to the north and the Frank Church country
to the south-west. `NavProcedureTests.NoApproachClaimsAProcedureTurnTheTerrainWouldNotAllow` checks that
reason on every run — a note on a chart that nobody verifies is just a note.

## The beacons, which are the opposite kind of radio

`Navaids.Ndbs`. Two, both locators on a runway's final approach course, and they exist because they fail in a
completely different way from a VOR.

A VOR is VHF and travels in a straight line, so a 2,000 ft wall puts it in shadow. An NDB transmits at
353 and 365 kHz, where the signal **follows the ground**, so a ridge does not shadow it at all. At
(400, −2500) — on the deck behind the first escarpment — the Valley VOR reads **0.00** and the Bench beacon
reads **1.00**. That is asserted in `ABeaconIsHeardWhereAVorIsNot`, and it is the whole argument for having
them: they give this world a second kind of coverage in exactly the valleys the VORs cannot see into. A
beacon is readable from **100 %** of the world here, because the world is 15 NM across and a locator is good
for 15.

So they are deliberately **not** on the hills. **BN (Bench, 353 kHz)** sits at (850, 1742) on the Bench
runway 27 final, 2.5 NM out and *a thousand feet below the field it serves*, down on the Valley plateau with
the escarpment between the two. That is a hopeless site for a VOR and a good one for a beacon. (The first
site tried was 488 m past the stop end, which turned out to be 522 ft up the side of the second escarpment —
caught by the test that asserts a navaid stands on the ground its chart says it does.) **SM (Summit,
365 kHz)** sits at (850, −9000), 488 m past the far end of Summit's runway 09, so the needle stays on the
nose all the way down the final and station passage is the roll-out.

What you pay for the reach is accuracy. High ground beside the path re-radiates the signal and the needle
leans toward it — "mountain effect" — so `NdbBearingErrorDeg` measures how far the ground stands above the
direct line and which side of the track is higher, and bends the needle that way, up to 8°. It is
**deterministic**: the same error at the same place every time, so a pilot can learn where this world's
needles lie to them, which is more use in a lesson than a random wobble. Down the Bench 27 final over the
flat plateau the error is 0.3°, and the ADF panel says "terrain error" rather than a magnetic bearing
whenever it exceeds 2°.

The ADF itself shows **relative bearing only** — a needle pointing at the beacon, measured from the nose. No
course selector, no TO/FROM, no deviation bar. Turning that into a position is the skill, and it is why an
ADF approach is harder than a VOR one with a simpler instrument.

## The three charts

`ChartPanel`, from the CHART button on the toolbar or from the pause menu.

* **VFR SECTIONAL** — hypsometric tints in 2,000 ft bands with 500 ft contours (every fifth an index contour,
  darker), water in blue, airports drawn as their actual runways with the field elevation, VORs with a compass
  rose oriented to north.
* **IFR LOW** — no relief at all, because a real low-altitude en-route chart draws none: the altitudes are
  printed on the airways instead, which is the whole point of them. Water and the coastline are all the ground
  there is. Airways carry designator, course, distance, MEA and MOCA; fixes are triangles labelled with the
  radial and DME that identify them.
* **SATELLITE** — surface types from the terrain's own classification, the renderer's own snow line (white
  from 2,500 m, full by 3,100 m, and never on a face steeper than the shader allows, so the picture agrees
  with the view out of the window), depth-tinted water, and hillshading with the sun in the north-west.

The background is one terrain sample per pixel — about 650,000 of them — so it is built once on a worker
thread with a progress bar and then **cached to disk under the build number**, so every later launch of the
same build opens it instantly, and a new build redraws it. The symbology is drawn in IMGUI over the top so the
lines and numbers stay sharp at any zoom, the way the printing on a chart is sharp and the terrain tint is not.

Magnetic variation here is **zero**, by decision: nothing in this world models a magnetic field, so declaring
it zero makes magnetic and true the same number and keeps the compass, the charts and the radials honest with
each other. Every course on every chart is both.

### Mesa VOR RWY 27 — the same runway as the ILS, on purpose

RIM, final approach course 273°, **MDA 5,260 ft (260 HAT)**. The FAF is **RIM itself** — overhead the
station, 2.71 NM out — which is what lets this approach work with no DME at all: station passage *is* the
fix. The final approach course is the Rimrock 273 radial flown **outbound**; it passes within 250 m of the
Mesa 27 threshold and is 2.9° off the runway, inside the 30° a VOR final is allowed.

It duplicates the Mesa ILS deliberately. Flying a precision and a non-precision approach to one piece of
pavement — 200 ft of decision height against 260 ft of minimum descent altitude, a glideslope against a step
down and a timed missed approach point — is the comparison an IFR lesson is built on.

### Bench NDB RWY 27 — the ADF one

BN 353 kHz, final 270°, **MDA 3,760 ft (760 HAT)**. The beacon is the FAF, 2.5 NM out, crossed at 4,000 ft.

From the beacon you fly 270 and time it, and the ground comes up 2,000 ft underneath you in two and a half
miles — so the whole final is flown nearly level, 4,000 ft down to 3,760 at 96 ft/NM, and the 760 ft of
height above touchdown is the **escarpment's crest**, not the runway's.

### Summit NDB RWY 09 — straight in only

SM 365 kHz, final 090°, **MDA 7,260 ft (260 HAT)** — the lowest non-precision minimum in the world, because
the Frank Church country west of Summit lies *below* the plateau. IAF and FAF are both **CABIN** (SUM R-276
at 2.3 DME), 2.0 NM out, crossed at 7,700 ft, homing to the beacon on 090.

**No way to join it from overhead the field**, and that is arithmetic rather than an omission: a course
reversal within 2 NM of the station would have to be flown at 11,500 ft, and you cannot lose 3,000 ft in the
2.3 NM from the station to the fix. So the plate says straight-in only, established inbound — which is what
a mountain plate reads like.

## Still open

0. **There are no IFR lessons yet**, which is what all of this is for (*"we need semi realistic IFR
   capability for future IFR lessons"*). The syllabus in `src/FlyingGame.Sim/Syllabus` and
   `Practice/PracticeScenario.cs` has no instrument lessons in it: nothing teaches tracking a radial, timing
   a leg, intercepting a localizer, flying a procedure turn, or going around from a decision altitude. The
   plates and the radios are the equipment that such lessons would need, and that is as far as this goes.
1. **Nothing flies these procedures yet in a test.** The geometry is asserted; an autopilot coupled to the
   localizer and glideslope, flown down to the DA by the game's own law, would prove they are flyable rather
   than merely legal. That is the obvious next test and it is not written.
2. **No holding patterns.** Each missed approach ends "direct X and hold" and the hold is not charted: no
   inbound course, turn direction or leg length. A hold is also the one manoeuvre that would make the leg
   clock earn its keep.
3. **The procedure turn is not drawn on the chart.** The side panel states it in words; the barbed arrow that
   a plate uses is not drawn on the IFR layer.
4. **Marker beacons, DME arcs, step-down fixes, and a VOR approach with circling minimums** are all absent.
   Summit runway 09 would make a good circling approach — its final approach course from the on-field VOR is
   parallel to the runway but 450 m off it, which is exactly the case TERPS answers with a circling minimum.
5. **No ATIS, no clearances, no ATC.** You can fly the plates; nobody will tell you to.
