# The timber and the erosion (2026-10-16)

Owner, after the alpine lakes went in, asked for three things in this order: **forests and a treeline**,
**erosion**, and then **finish the fronts**. He also answered two shape-of-the-world questions that had been
open: leave the 183 m cliff where the first front runs behind the high desert, and do not put a fine mesh
band along the fronts.

## 1. The forest is a function, not a list

`FlyingGame.Core.Forest`. The world is 440 km²; a list of every stem in it is tens of millions of entries
that nothing could store, draw or search. So the world is cut into 18 m cells and each cell is asked whether
it has a tree, with the answer hashed out of the cell's own index. The same cell always answers the same way,
so the renderer and the airframe see the same forest without having to agree in advance.

What that buys:

* **Collision costs one height query** in the common case — you are higher than the tallest tree that could
  stand here, so there is nothing to test. `Forest.Solids` is a `WorldSolids.IShape`; only when you are
  within 42 m of the ground does it look at the nine cells around you.
* **The renderer draws what it can afford.** `ForestStreamer` keeps about 1,250 m of timber built round the
  camera, one chunk a frame, and throws it away behind. The trees it leaves out still exist and still hurt.
  Beyond that radius the GROUND CARRIES THE FOREST: `TerrainColor` tints it with the same cover function, so
  the treeline is in the same place in the picture as it is in the collision.

Where it grows, which is all subtraction:

| rule | why |
|---|---|
| below the treeline | 2,350 m at x = 0, falling 450 m per 8 km of northing, +130 m on a sun-facing slope, with 70 m of hashed wander |
| only on `Surface.Rough` | keeps it off every runway, apron, backcountry strip, glacier, groomed lawn, and off the alpine and Alaska rock |
| not on the graded aprons | `FrankChurch.OnWorkedGround(x, y, −BlendM)`: nobody cuts a bench out of a canyon side and leaves firs on the overrun. The BLEND beyond the apron keeps its timber, which is what makes those strips what they are |
| not over water, below 6 m, in the high desert, on an airport pad, in a town or a crop field | |
| slope up to 63° | **this number has been wrong twice** — see below |
| in stands | two lattices (760 m and 185 m) through a soft threshold, so there are meadows to land in and edges to read |

Measured: 25 % of the world carries timber, 13 % of it heavy, ~229,000 stems, and the longest clearing on an
11 km transect is 2.2 km. Every backcountry strip has timber within 100 m of the middle of the runway.

### The slope limit, twice wrong

49° (the textbook figure) left every mountain front in the world bare brown rock. 58° left a dead straight
horizontal line of timber across the foot of each one — because a front's upper band sits at 44° whatever its
width, since `WallProfile` puts 82 % of the climb in the top 45 % of the run. What makes a treeline look like
a treeline is HEIGHT. Slope is only there to keep trees off a cliff, and the escarpments that really are
cliffs stand at better than 3 : 1.

## 2. Erosion

**The fronts** (`WorldTerrain`): gullies down the fall line — which here IS ±y, because the fronts run along
x — with the spurs between them standing proud, and the material that came out lying in fans along the toe.
340 m crest to crest, up to 86 m deep at mid-face, each channel its own size. A front narrower than about
880 m gets none of it: a bare 183 m cliff has chutes, not valleys. Nothing is cut inside a block that
flattens its own ground (`ErosionRoomAt`).

The old "rock texture" on the face went with it: it was 144 m and 107 m sine waves against a 60 m mesh, which
is what the alpine range taught us not to do. Detail shorter than about twice the sample spacing does not
read as rock, it reads as corduroy.

**The ranges** (`WorldTerrain.ConeErosion`, used by `AlaskaRange` and `AlpineRange`): the same thing in the
round — couloirs down the flanks, closing as they climb, each with its own size and its own height to reach.
The wander is made of sines rather than the lattice the fronts use, because a lattice in the bearing leaves a
seam down one side of every mountain. Both of the alpine passes over the peaks cut the same couloirs, or the
summit re-assertion fills them in inside `SummitFraction` and draws a ring round the mountain.

Two colour changes went with it:

* The snow's slope cutoff moved from 2.8 to **2.6** — and NOT to the 2.1 that was tried first, which stripped
  the snow off the Alaska flanks and gave back the bare brown range the owner had already objected to once.
  At 2.6 the ribs stay white and only the couloir walls show rock.
* The field grid lines now die out at the treeline. They are pasture fences and section lines, and they were
  being drawn on every flat shelf in the world, including the rims of the alpine basins at 9,000 ft, where
  they read as grey staircases cut into the rock.

## 3. A 900 m wall at the third front (pre-existing)

Found by probing a render that showed a flat dark-green wall where a mountainside should have been.

`HighDesert.InBlock` is padded 600 m, and `HighDesert.HeightAt` laid the desert's own floor down across that
whole padded footprint and then stopped dead. On three sides of the block the plateau is at the same height
and nothing showed. On the fourth the third mountain front climbs — so the floor was laid across its face for
560 m and ended in a 900 m cliff at x −4,000. The braided plain had the same fault on its own south side.

The fix is that a block hands back to the ground underneath across its margin:

```csharp
if (baseH > h) h += (baseH - h) * (1 - WorldTerrain.BlockFade(x, y, X0, Y0, X1, Y1, FootprintPadM));
```

Only where the ground underneath is HIGHER, so a mesa near the block edge keeps its height.
`NoBlockThatFlattensItsGroundEndsInACliff` now walks right across each margin at 5 m — fine enough that a
real escarpment, which is 3.3 : 1 by design, cannot be mistaken for a discontinuity.

## Still open

* ~~**Frank Church has no margin fade.**~~ Done 2026-10-17, and it was not what this said: the ground was
  NOT the plateau in the margin. The canyon runs east–west and its cut simply stopped at the footprint, which
  left a 639 m vertical step across the east mouth and 759 m across the west one. Chasing that found five more
  walls of the same kind elsewhere in the world — eleven in all, from 37 m to 2,235 m, the biggest across the
  head of a glacier on the mountain the Alaska range is named for. All eleven are fixed and the world is now
  swept for them by a test. See `docs/INVISIBLE-WALLS.md`.
* **The forest is drawn only round the camera.** `ForestStreamer` has not been seen on a device, so it no
  longer counts chunks: it spends a MILLISECOND BUDGET (2 ms of a frame, 4 chunks at most), builds nothing at
  all after a frame that already ran long, and keeps `WorstFrameMs` so a device can be asked what it cost.
  The radius (1,250 m) and the chunk size (216 m) are still guesses — what wants checking now is whether the
  timber arrives fast enough when you are doing 140 kt, not whether it stalls.
* The fronts' gullies are sampled by a 60 m mesh (owner's call, 2026-10-16: no fine band along the fronts),
  so they are soft. They would be sharp at 30 m.
