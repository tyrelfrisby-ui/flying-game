# The plateau migration

Owner, 2026-10-16: *"Keep the climb from the coast to ~7,000 ft, but reshape the risers as mountain fronts
rather than flat shelves. The waterfalls and the tunnel maze survive roughly in place. Airports at 3,000 /
5,000 / 7,000 ft for density-altitude practice."* And, on the repeated landscape: *"Thin them out — keep some,
drop some."*

Three pieces. The first two are done; the third is waiting on the owner's yes.

## 1. The climb to 7,000 ft — done

A step was 1,500 ft and the top field was at 5,500. A step is now **2,000 ft** and the four fields are

| | plateau | field elevation |
|---|---|---|
| A0 | Valley | 1,000 ft (the coastal tableland, unchanged) |
| A1 | Bench | 3,000 ft |
| A2 | Mesa | 5,000 ft |
| A3 | Summit | 7,000 ft |

7,000 ft is the one that teaches: a Cub up there on a warm afternoon has a density altitude near 10,000 ft and
climbs at a couple of hundred feet a minute, which no amount of being told conveys.

Everything standing on a plateau had to come up with it. Four regions had their heights written as metres above
the SEA, and raising the plateau 1,500 ft would have buried them — the plateau floor came up past the top of
Hungry Ridge, 400 ft of high-desert bench, and swallowed it whole. They are now written as **relief above the
plateau** (or, for the Frank Church, **depth below the rim**), so the next time a plateau moves they move with
it:

* `HighDesert.Hills` — relief above the Mesa.
* `AlaskaRange.Peaks`, `AlaskaRange.Glaciers` — relief above the Mesa. The High One is now 17,700 ft.
* `AlpineRange.Peaks`, `AlpineRange.Lakes` — relief above the Summit. Cathedral is now 11,000 ft and the stair
  of lakes sits at 8,300 to 9,200 ft.
* `FrankChurch.Strips` and the canyon floor — depth below the rim, so the canyon stays a kilometre deep with
  35° walls instead of becoming a 4,800 ft slot with 58° sides. The rim strips are at 7,000 ft now, Johnson
  Creek at 6,400, and the river bars at about 4,900.
* `BraidedRiver.FootM` — the Bench plateau itself, not an absolute 762 m that would have put the whole outwash
  plain 150 m underground.

The waterfalls are 2,000 ft plunges. The tunnel maze did not move: its floors are tied to the Valley datum and
its high mouth to an absolute 430 m, so all that changed is that there is more rock above it.

## 2. The mountain fronts — done

A riser was a 183 m ramp: 2,000 ft of rise in 600 ft of run, a 73° wall with a flat top, and from the air
unmistakably a step in a staircase. A real mountain front is **embayed** — great spurs reach out onto the plain
and bays cut back between them, and along the crest there are summits with saddles between, and the saddles are
how you get over it without climbing to the summits. So the fronts now have two things the wall did not.

**Width that varies along x.** The crest stays exactly where the wall's top was — which is why the waterfalls,
their amphitheaters and the maze did not move — and the toe reaches forward onto the lower plateau by up to a
further 980 m. Measured: all three fronts run between 183 m and 1,163 m, and their toes wander 1,200 to 1,360 m
along the front. Where a front reaches far it is a talus fan and then rock at about 44°; where it reaches not at
all it is still the old cliff.

**Relief above the crest.** Summits about every 1.6 km, up to 520 m over the plateau behind, with their back
slopes running down onto it. Measured: 429 to 458 m of summit on each front. Between them the crest falls to
plateau level, and a test holds that there is never 3 km of front without a pass.

### Where a front may grow

This was the whole problem, because the plateaus are 3.3 km apart and most of that is already spoken for. Both
directions of growth are gated by `WorldTerrain.Keepouts`: the airport pads with their blend margins, the
repeated Valley features, and the landscape blocks that fill their plateaus edge to edge.

All four aerodromes share an x, so **every front keeps the old cliff across the middle 3.5 km** and becomes
mountain out to either side, where there are 5 km to the west and 7 km to the east with nothing on them. The
aerodrome sits in a bay in the range, which is the usual reason an aerodrome is where it is. It is also why the
three waterfalls — the river crosses at x ≈ 1,500, inside the bay — did not move a metre.

Two kinds of keepout. A block that **flattens** its ground (the braided plain, the high desert, the Frank
Church, an airport pad) also holds the CREST back, because a floor needs a finished plateau under it. The rest
(a town, a race course, a crop field) only keep a summit's back slope off, since they take the ground they are
given. The Alaska and alpine ranges are not keepouts at all: their peaks are a max over whatever is underneath
and stand two to four thousand metres above it, so a front's summits simply become their northern foothills,
which is what foothills are.

### Two bugs this found

* **A cliff along the high desert's northern boundary**, up to 560 m of it, for three kilometres — and it was
  there before the fronts went in. The first wall's edge wanders ±210 m along x, and where it wandered south it
  took the crest up to 138 m past the desert's edge, so the desert's floor stood over a half-built escarpment.
  No height probe inside the block could ever see it, because inside the block the ground is exactly what it
  should be. The crest is now held forward of any block that flattens its ground, and a test walks the boundary
  of every such block and compares the two sides of the line (worst case now 18 m over 50 m).
* **Pylon 8 of the Bench race course, sixty-nine metres up a mountainside.** The race course is the furthest
  east of the repeated features — 2.1 km out, where the next plateau's crest is 1.2 km out — so it is the one a
  summit's back slope reaches. It is a keepout now, which is also why the thinning below gives the fronts room.

## 3. The thinning — PROPOSED, not built

Every plateau currently carries a copy of the whole Valley landscape: airport, lake, town, tower, arch, bridge,
crop field, aerobatic box, race course. Four of everything. The owner asked to thin them out, keeping a couple
of things on each level that suit its height.

Each upper level would keep **its airport, its lake, and the river and gorge** — those are the spine, and a
float take-off at 7,000 ft is a lesson of its own — plus **its own signature region**, which it already has.
Beyond that, two things each:

| | level | keeps | and why |
|---|---|---|---|
| A1 | 3,000 ft | **crop field** + **bridge** | Ag flying with a density altitude to think about, and the gorge is at its best here, between two 2,000 ft falls. Already has: the braided outwash plain, and the tunnel maze under it. |
| A2 | 5,000 ft | **race course** + **town** | Reno is the air races, and the pylon course belongs on the plateau that carries the Reno desert. Reno is also a city at 4,500 ft, which is the one good reason for a town up here, and it gives the course a grandstand. Already has: the high desert, the Alaska range, Denali and the glaciers. |
| A3 | 7,000 ft | **aerobatic box** + **arch** | A box at 7,000 ft is the density-altitude lesson in aerobatics — the same figure costs more height. The arch spans the gorge just upstream of the top waterfall, which is the most dramatic place in the world to put one. Already has: the alpine range and its stair of lakes, and the Frank Church. |

Dropped above the Valley: three towns (two of them), three towers, three arches (two), three bridges (two),
three crop fields (two), three race courses (two), three aerobatic boxes (two).

The fronts get the room back automatically — the keepout list is built from the features that exist, so where a
feature goes away, the front grows into the space on its own.
