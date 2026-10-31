# Weight and balance

Owner request, 2026-10-30: *"a true weight and balance system where you can load the airplane with occupants,
luggage, or cargo and fuel, and the weight and balance is plotted and the flight characteristics are adjusted
appropriately."*

This page is how it works, where the numbers came from, how to add a type, and the two things found while building
it that are the owner's call rather than mine.

## How it works

`LoadingConfig` on an aircraft config holds the EMPTY aeroplane — its mass, CG and inertia — plus its seats,
baggage holds, tanks and certificated CG envelope. `LoadSheet` holds what is in it right now, adds it all up, and
**writes the result into the aircraft's own `mass` block**. Every one of the ninety-odd places that read the CG then
flies the loaded aeroplane with no change of their own. That is safe because config instances are not shared —
`UnityAircraftConfigLoader.LoadFromStreamingAssets` deserializes a fresh one per call, and the session already
mutates it per flight for bushwheels and skis.

`WeightAndBalance` is the arithmetic: the MAC and where a CG sits on it, the mass/CG/inertia solve (parallel axis,
all three axes, so one full wing tank against one empty one is felt as a rolling moment), and the envelope as a
polygon with point-in-polygon and fore/aft limits at a weight.

A type without a `loading` block flies exactly as it always has, and simply cannot be loaded. Switching weight and
balance on for a type never changes how it flies at its default load — see the rule below.

### The frame: percent MAC

A POH quotes arms and CG limits in inches from that type's own datum — a firewall, a wing leading edge, a point in
space ahead of the propeller. This project's body x has nothing to do with any of them, so the envelope is stored in
**percent MAC**, which is what the limits mean physically and which `WeightAndBalance.PercentMac` measures from the
strips the aeroplane actually flies with.

Mapping a POH station into body x needs exactly **one anchor**, because the scale is already known (an inch is an
inch): `body x = anchor_x − (station − anchor_station) × 0.0254`. The anchor is a point whose station the type
certificate states and whose body x this project's geometry measures. For the 172 that is the leading edge of the
MAC (TCDS 3A12: MAC 58.8 in, its leading edge 25.9 in aft of datum). For the J-3 it is the wing leading edge, which
*is* the datum (TCDS A-691).

**The MAC must be measured from paired sections** — each wing strip plus the aileron hinged behind it. Taking the
strips as they come counts an aileron row as its own narrow little wing and drags the mean chord down: the 172
measured 50 in that way against the 58.8 in on its type certificate, so every percent-MAC figure built on it would
have been out by a sixth. Paired, it measures 59.9 in, which is 1.9 % high and good enough to build on.
`PitchStabilityTests` was computing its own unpaired MAC and so overstated every static margin in the suite by about
8.5 %; it now calls `WeightAndBalance.MeanAerodynamicChord`, and there is one definition of MAC in the tree.

### The rule for adding a type

> The DEFAULT load must reproduce the type's tuned mass, CG and inertia exactly.

Every type was calibrated against its published stall speeds and handling at the mass and CG in its config. If
adding a loading block moved either, it would silently retune a finished aeroplane. So the published figures are the
inputs and the default load is derived backwards from the tuned state:

1. **Empty mass, station arms, tank capacities and the CG envelope are published data.** Take them from the POH
   loading table or the type certificate, never from a calculator website — a third-party calculator gave the
   Archer's front-seat arm as 85.5 in where the factory POH says 80.5.
2. **Where the empty CG is published as a point** (the 172's POH sample airplane: 1454 lb at station 39.6), use it,
   and solve the default station loads — two groups, one forward and one aft — for the tuned mass and CG.
3. **Where only an empty-CG range is published** (the J-3's type certificate gives 8.5 to 20.3 in), make the default
   load the plausible one — equal occupants, tanks full — and derive the empty CG from it. Then **check** that the
   derived empty CG falls inside the published range. The J-3's comes out at station 19.4 in, inside.
4. **Empty inertia is then the tuned inertia with that load's point masses taken back off**, and moved from the
   tuned CG to the empty CG by parallel axis.

`TheDefaultLoadIsTheAeroplaneWeHaveBeenFlying` holds all of that to 10 g and a tenth of a millimetre. If it ever
drifts, the data is wrong, not the physics.

Lateral and vertical arms are **estimated** from the config's own geometry, because no POH publishes them: occupants
level with the aircraft CG, baggage on the cabin floor below it, wing fuel at the wing's own body z and in the
inboard half of the span. They matter to Ixx and Izz and to the vertical CG, and the derived empty inertia depends
on them — the 172's empty Ixx moves by 200 kg·m² if the tanks are placed at half-span instead of 2 m out. Worth
revisiting per type, not worth blocking on.

### Types done

| type | source | empty | gross | envelope (%MAC) |
|---|---|---|---|---|
| `c172-like` | Cessna 172N POH 1979 §2.13, §6 figs 6-3/6-5; TCDS 3A12 | 1454 lb @ sta 39.6 | 2300 lb | 21.0–35.7 at gross, 15.2 fwd below 1950 lb |
| `j3-cub-like` | FAA TCDS A-691 (J3C-65) | 765 lb, CG derived @ sta 19.4 | 1220 lb | 14.6–34.4 |

The J-3's two seats are at stations +9 and +36 on a 61-inch chord, so which one the pilot sits in is worth 8 %MAC —
nearly half the whole range. That is why a Cub is soloed from the back seat, and it falls out of the published data
rather than being written in.

### `pa28-archer-like` is blocked, and why

The factory POH gives everything except the one thing the mapping needs. Arms (front 80.5, rear 118.1, fuel 95.0,
baggage 142.8), empty weight (1547 lb at 86.4), gross (2550 lb) and the envelope (82.0 in below 2050 lb to 88.6 in
at 2550; aft 93.0 throughout) are all in Section 6 and §2.13. But the datum is defined as *78.4 inches ahead of the
wing leading edge at the inboard intersection of the straight and tapered section*, and anchoring on that point puts
the published aft CG limit at about 18 %MAC and the config's own tuned CG about 6 %MAC **behind** the aft limit —
which would also mean an Archer flies its whole envelope with 24 % of static margin in hand. The 172's numbers hang
together under the same treatment, so the Archer's anchor is the suspect, not the method. It needs the Archer's MAC
and LEMAC station from TCDS 2A13, which the POH does not carry. Not guessed.

## Still to build

- **Fuel burn from power.** `LoadSheet.BurnFuelKg` exists and drains the selectable tanks in proportion, so the
  wings stay symmetric; nothing calls it yet. It wants a BSFC per engine, which belongs with the engine systems.
- **The plot.** The envelope and the loaded point are both computed; the diagram is not drawn. Charts in this
  project never show own-ship position, but a weight-and-balance envelope is not a navigation chart and is useless
  without the loaded point on it, so it shows there.
- **A loading screen** before flight, and a per-tank fuel selector.
- **The rest of the fleet.** Each type needs one anchor and its published loading table. The gliders want water
  ballast rather than fuel, which is the same mechanism with a density of 1.0 and a dump valve.

## Two things for the owner

**1. Every type is simulated at max gross, and most are a little forward of where a real one balances.**

The 172's config is 1043 kg, which is 2300 lb to the pound — its maximum take-off weight. So is the J-3's 553 kg
against 1220 lb. With the published empty weight and real arms, the tuned CG then implies a specific loading: the
172's works out as two 104 kg occupants in front, two 33 kg children behind, and full tanks. A *typical* gross 172 —
two adults in front, one behind, full fuel and 76 lb of bags — sits about 5 %MAC further aft, at 32 %MAC against the
config's 27 %MAC, and would have about 5 %MAC less static margin.

So the fleet is flying slightly nose-heavy and therefore slightly more stable than the real thing. Closing that gap
means moving the tuned CG, which retunes pitch stability on aeroplanes already calibrated against their published
numbers. It is a real improvement and it is not a change to make quietly. Measured, per type, by
`WhereTheTunedCgSitsInTheRealEnvelope`.

**2. The CG positions look authored by convention, not measured.**

Measured in percent MAC, seven types land within 0.1 % of each other at **27.0 %MAC** — the 172, the Archer, the
SR22, the Cub, the PA-18, the Glasair. Across the fleet the spread is 14 to 33 %MAC. That is the signature of
placing each CG at a fixed fraction of the chord while authoring, which is a perfectly sensible default and is not
the same thing as knowing where the aeroplane balances. Where published data exists it should replace the
convention; the loading blocks above are the first two.

## Sources

- Cessna 172N Pilot's Operating Handbook, 1979 — §2.13 Center of Gravity Limits, §6 Figure 6-3 Loading
  Arrangements, Figure 6-5 Sample Loading Problem.
  <https://topflightlessons.com/wp-content/uploads/2026/05/Cessna-172N-POH-1979-compressed.pdf>
- FAA TCDS 3A12 (Cessna 172), MAC 58.8 in with its leading edge 25.9 in aft of datum; corroborated by EASA
  TCDS IM.A.051. <http://www.aeroelectric.com/Reference_Docs/Cessna/cessna-misc/C172_FAA_typecert_3A12.pdf>
- FAA TCDS A-691 (Piper J3C-65) — datum "wing leading edge", C.G. range (+10.6) to (+22.7), seats "one at +9 and
  one at +36", baggage 20 lb at (+49), fuel 12 gal at (−18), maximum weight 1220 lb.
  <https://upload.wikimedia.org/wikipedia/commons/8/8a/US_FAA_A-691.pdf>
- Piper PA-28-181 Cherokee Archer II POH, Report VB-790, 1976 — §2.13 and §6 Figures 6-9/6-11.
  <https://aacit.org/wp-content/uploads/2016/02/PA28-181-POH.pdf>
- J-3 empty weight 765 lb is the type figure, not a particular aeroplane's; an individual Cub's empty weight and CG
  come from its own weight-and-balance record, as the type certificate says.
