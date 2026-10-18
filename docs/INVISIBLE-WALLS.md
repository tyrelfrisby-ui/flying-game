# Invisible walls

2026-10-17. What started as one line in `docs/TIMBER-AND-EROSION.md` — *"Frank Church has no margin fade … it
wants doing carefully"* — turned out to be a family. **Twelve bugs, in fifteen places**, from 37 m to 2,235 m,
none of them visible as anything from the air until you are in it, every one of them the same mistake.

## The mistake

A feature computes its contribution inside a boolean:

```csharp
if (!InBlock(x, y)) return baseH;                  // a footprint rectangle
if (d >= p.RadiusM * SummitFraction) continue;     // a radius
if (t < -0.05 || t > 1.25) continue;               // a range of a parameter
if (dir > 0 ? k.Y1 <= crestY : k.Y0 >= crestY) continue;   // which side of a line something is on
```

Inside the test the feature is applied; outside it is not. That is only safe if the feature has **no relief
left** at the line where the test flips, and nothing was checking that. Where it had relief, the ground steps
by exactly that much, vertically, in whatever direction you cross it.

The fix is the same shape every time: **the claim fades to nothing before the boolean ends**, so the boolean
only ever switches off something already zero. Where the room for a fade had to be found, it was measured.

## What was wrong, and what it is now

| Drop | Where | The boolean | Now |
|---:|---|---|---|
| 2,235 m | head of the glacier on the 19,000 ft peak | the ice's claim stopped at `t = −0.05` | `AlaskaRange.HeadFadeT` / `ToeFadeT` |
| 853 m | the end of Hidden's outlet valley | the valley's claim stopped at its run + 260 m | `AlpineRange.OutletTailM` |
| 759 m | west mouth of the Frank Church canyon | the cut was not applied past the footprint | `FrankChurch.EndTaper` |
| 639 m | east mouth of the same canyon | the same line at the other end | `FrankChurch.EndTaper` |
| 613 m | a circle of radius 330 m round Sentinel | the summit's claim stopped instead of fading | `AlpineRange.SummitFadeFraction` |
| 234 m | 260 m off the end of Dewey Moore | the nearest strip owned the ground outright | weighted blends in `FrankChurch.HeightAt` |
| 220 m | x = −171 on the Valley's front | a keepout's hold flipped as the crest wandered | `WorldTerrain.FrontRoomFadeY` |
| 162 m | a radial line off every peak in both ranges | `floor(u)` jumped by `channels` where `atan2` wraps | periodic channel id in `ConeErosion` |
| 98 m | 600 m west of the alpine range's block | a peak's skirt cut off by a 600 m footprint pad | `AlpineRange.FootprintPadM` = 900 |
| 37 m | every channel boundary on every cone | the spur carried the left-hand channel's profile | mean-reach spur profile in `ConeErosion` |
| 89 m | three of the four aerodrome lakes | the bed is cut from ground the hills had raised | `WorldTerrain.LakeShoreR` |
| 60 m | Foraker's southern flank | its skirt cut off 150 m short by a 900 m pad | `AlaskaRange.PadSouthM` |
| — | the foot of every alpine peak | roughness was 0.35 of itself at the foot, not 0 | `sqrt(sin(πt))` |

Two were known about and written down. The other ten nobody knew were there, including the 2.2 km one across
the head of a glacier on the mountain the Alaska range is named for, and three of the four lakes beside the
aerodromes — which have been sitting at the bottom of 60 to 89 m shafts, with the water visible down them,
for as long as the grass hills have been there.

## The two that cost something

**The canyon's west end is a lie, and worth naming as one.** A river does not shallow out as it goes
downstream, and west is downstream — the floor runs from 1,498 m at the head down to 1,378 m there. But
nothing is out there to drain into; the plateau runs flat at 2,134 m for at least 3 km. The choice was between
a canyon that opens out into a broad swale and a 759 m wall, so it gets the long taper, 1,200 m, which is the
same 1:1 slope as the canyon's own walls and at least reads as country. The east end needs no apology: it is
upstream, and upstream is where a canyon heads into the mountains.

**The lakes had to move in the ORDER, not be fenced off.** Giving the aerodrome lakes a bank broke a strip:
the bank reaches 675 m in x, the lake north-west of the Bench field shares its x with Big Creek 900 m away,
and because the lakes were cut after the backcountry lays its benches the bank took the last 59 m of a
3,500 ft strip with it. The first fix was to skip the bank over any ground a strip has shaped — which left an
8.5 m step in one metre at the edge of that ground, which is precisely the mistake this whole day was spent
removing. A boolean guard against a boolean bug is not a fix. What works is cutting the lakes BEFORE the five
blocks and letting the blocks lay their own ground over the top: `FrankChurch.HeightAt` hands back whatever
it is given anywhere it has nothing of its own to say, so the lake survives everywhere it should and the
bench wins where the two meet. 8.52 m became 0.71 m.

**The keepout fade is 60 m, and that number was argued down.** A block's hold on a front releases over 60 m
of crest wander. The first attempt used 250 m, which removed the wall and also stood a 290 m summit up behind
the Valley aerodrome where none had been — `WorldTerrainTests.WallsAreSteepAndIrregular` caught it, reading
891 m of climb across a wall that is meant to be one 610 m step. At 60 m the wall is gone and nothing else
moves: the relief at the old wall site climbs 202 → 234 m across it, the worst step in the 500 m around it is
**1.02 m walked at 10 cm**, and the summit behind the Valley is back to nothing. The lesson is the obvious one
— a fade should be the smallest one that does the job, because every metre of it is a change to a world
somebody has already looked at.

**The summit fade eats a little of the glacial troughs.** The claim now fades over a further 0.15 of each
peak's radius — 165 m on Sentinel — so a trough running close under a summit loses some of its depth. That is
the right way round: the whole point of the summit re-assertion is that a valley may cut a flank but may not
take the top off a mountain.

## The test that does not know any of their names

`WorldContinuityTests.NowhereDoesTheGroundStopDead`.

Telling a wall from a steep face is the whole difficulty, and comparing two probes 5 m apart cannot do it —
this world is allowed cliffs, and an escarpment crossed at a grazing angle shows 570 m of difference in x
while being perfectly continuous ground. That false positive cost an hour on the first attempt.

So the sweep is **two passes**. The first walks the world (x −7,200…9,200, y −15,600…3,600) on lines 60 m
apart, 5 m at a time, in both directions, and notes every crossing where the ground moves more than 40 m. The
second goes back to each and closes the probes down to 10 cm by bisection, following whichever half still
holds the drop. A steep face's height difference falls away in proportion; a discontinuity's does not. On the
current world, 4,565 crossings are worth a second look and 32 square kilometres hold a step the ground
actually takes.

Three things it has to know:

* **The tunnel.** It measures the ground as if the runway tunnel behind the first fall had never been bored.
  The far end of that tunnel is 570 m of rock, because that is what the end of a tunnel is.
* **The plunge falls and the dam.** A plunge fall is a vertical face, and behind each is an amphitheater with
  a vertical back wall. Four circles are named in `Allowed`, each with its reason.
* **What is known and not fixed.** `Known` carries the rest, so the assertion can be *zero* — any step
  anywhere else fails the test. A new wall cannot hide behind an old one.

The older test it grew out of — `MountainFrontTests.NoBlockThatFlattensItsGroundEndsInACliff` — now walks all
four margins of each block rather than only the north and south ones. That is how the canyon's two mouths
survived: the canyon runs east–west and left the block between the two edges the test was looking at.

`AlpineRange.HeightAt` and `AlaskaRange.HeightAt` take an optional trace that records the height after each
stage. Six one-line additions, and the only cheap way to ask the two sides of a step which stage disagreed —
it found the outlet valley in one run after an hour of guessing had got nowhere.

## Still open, in the order I would take them

1. **The alpine lakes are not held up by anything** where no peak happens to sit under them. Hidden's eastern
   end has its water 602 m above its own bed; Outlet, Shelf, Larch, Cirque and Crown all step at their
   shorelines. An open lake's rim is deliberately left out across its outlet notch so the valley can fall
   away, the outlet valley carries the ground at the lake's level but refuses to act inside the shoreline, and
   the basin's walls are applied with a `Min` — so they cannot build ground UP out of a plateau lying below
   the water. **Making the bed the dish outright was tried and is worse**: it moves the step to the shoreline
   and every lake in the range grows a 600–740 m cliff at its own edge. The fix is a decision about where the
   lakes are, or about whether a basin builds its own mountain, and it is Ty's.

   Worth saying what is DIFFERENT about the world's own four airport lakes, which were the same shape of bug
   and were fixed the same evening: those sit on open ground, so giving them a bank that runs from the water's
   edge up to whatever is around them is the whole fix. An alpine lake cannot have that bank across its outlet
   notch, because the notch is the way in — it is deliberately the one bearing on which the ground falls AWAY
   from the water. So the alpine case needs the mountain under the lake to exist, not a bank around it.
2. **A second tier, 4 to 33 m, about thirty of them.** The sweep is set at the big ones: it only looks closely
   where the coarse walk moves more than 40 m, and only calls something a step at 12 m in 10 cm. Drop those
   two numbers to 10 and 4 — they are `Candidate` and `Wall` in the test — and it takes 16 s instead of 8 and
   finds a second tier. None is diagnosed. The worst dozen:

   | Step | Where | | Step | Where |
   |---:|---|---|---:|---|
   | 32.5 m | (1,680, 590) | | 21.9 m | (6,180, −55) |
   | 32.3 m | (−5,460, −3,245) | | 21.9 m | (4,860, −505) |
   | 31.1 m | (1,980, 2,790) | | 21.8 m | (−7,020, −50) |
   | 30.3 m | (5,880, −375) | | 21.6 m | (6,560, 60) |
   | 30.0 m | (8,040, −6,750) | | 21.2 m | (8,460, −3,480) |
   | 26.1 m | (−6,120, −3,245) | | 21.2 m | (−3,660, −495) |
   | 25.4 m | (−7,140, −3,565) | | 20.9 m | (7,710, −6,960) |
   | 22.0 m | (1,680, −10,410) | | 20.9 m | (9,180, −3,880) |

   Two patterns are visible without looking at any code: a line of them along y ≈ 0 to 60, which is the
   Valley plateau's own latitude, and a cluster out east of x = 6,000. An 8.5 m step at the edge of Big
   Creek's ground slipped under the current setting earlier in the day and was only caught by hand, so this
   band is not academic — it is simply smaller than one evening could reach.

3. **The grid is a grid.** Lines 60 m apart find a wall that runs for any distance at all, which is what all
   fifteen of these did, but a wall shorter than 60 m can hide between two lines. Nothing here says the grid is
   fine enough; it says there is nothing coarser than it.
4. **Nothing checks the solids against the ground** — the collision boxes behind the falls, the tunnel roof,
   the bridge. A wall in the ground is what this is about; a wall in the collision mesh would show up
   differently and nothing is looking.
