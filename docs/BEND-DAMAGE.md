# Bends before it breaks (Ty 2026-11-05)

> "The wing hit relatively slowly and immediately snapped off … the airplanes are all like porcelain dolls … make them
> more resilient in the sense that they stay together but still use realistic damage thresholds for bending surfaces and
> model the bent surfaces reduced strength and aerodynamic impacts if it continues flying."

## What it was

- A wing panel (outboard of its structural station) came off the **first step** its tip's contact moment passed the
  ultimate. The contact is a stiff spring-damper, so a 5 m/s tip arrival on the J-3 rang to 9.5 kN·m for a step against
  an ultimate of 6.2 kN·m: gone.
- A strike between limit and ultimate left a fixed 5° set; a bent panel then snapped at **2 × its 1 g moment**, a fifth
  of its ultimate (Ty's 2026-11-03 rule, replaced by this lane).
- Tail surfaces, the wing's mid-span point, nacelles and gear legs came off at the first arrival over their break speed.

## What it is now

**Wing panels: two plastic hinges at the station** (`src/FlyingGame.Core/PlasticHinge.cs`, `WingStructure`).

- Bending (tip up/down): yield = 1.5 × the limit moment (Ty's overstress pattern below: "after 1.5 limit load permanent
  deformation begins"; it was the limit moment, 14 CFR 23.305), peak = the ultimate (2.0 × limit). Torsion (nose up/down about the
  flexural axis at 0.35 c): fabric over two spars `T_u = M_u·b/(2L)` (differential spar bending, b = 0.45 c); stressed
  skin `T_u = 2·F_su/F_tu·M_u = 1.28 M_u` (2024-T3).
- Backbone in accumulated plastic rotation κ, from `θ_p ≈ 2·ε_p` (plastic hinge length ≈ section depth):

  | construction | peak at | falls to | by | tears at | fleet |
  |---|---|---|---|---|---|
  | wood / fabric (spruce) | 0.007 rad | 35 % | 0.05 | 0.40 (fabric 20 %) | J-3, Pitts, Great Lakes, Stearman, Decathlon, Cassutt, Jenny, Gee Bee |
  | aluminium / fabric | 0.02 | 50 % | 0.10 | 0.36 (2024 18 %) | PA-18 ×3, PA-25 |
  | aluminium skin | 0.02 | 50 % | 0.10 | 0.36 | C172, PA-28, Beaver, 2-33, twins, jets |
  | composite | 0.005 | 15 % | 0.02 | 0.10 | SR22, Extra 300, composite gliders |

  New config key `wingSpar` (+ `wingSparSource`) on the 8 types that differ from what the fuselage implies.
- **The ground cannot push a tip harder than the hinges carry** (`AirframeContact.CapPanels`): the panel's points are
  scaled together to the cap. What they asked for, with Σ(k + c/dt)·r² of the push, goes to `PlasticHinge.Load`, which
  turns the hinge until the relieved push meets the backbone. The strike's energy becomes plastic work, not a moment spike.
- Every yield adds to κ; past the peak each new yield leaves it weaker. Off only at the rupture rotation (`PanelBroken`).
- The set is geometry (`WingPanel.Deform`: twist then bend): hard points, strip load points, dihedral (+bend), incidence
  (+twist × span fraction, `IncidenceDeltaRad`) and a buckled-skin damage `D = (κ − peak)/(rupture − peak)` through the
  branch-strike surface law (1 − 0.4 D lift, +0.06 D profile drag; the worse of the two damages counts).
- In flight a panel a STRIKE has damaged carries `n_aero·M1` against what its hinges have left. A panel bent only by an
  overstress is the wings' member's (below), not its hinges' twice over.
- The drawn mesh: `AirframeVisual.ShowPanelBend` puts every vertex where `Deform` puts it.

**Other members crush before they break** (`MemberMaterials.CrushCapacityM`, the fuselage's δ₀·(v²/v_b² − 1) rule):
tail surfaces / rudder steel 0.20 m, aluminium 0.15, wood 0.08, composite 0.05; mid-span by wing construction; nacelles
and gear legs 0.15 m. Untagged airframes keep the old first-overload rule. Event `MemberBent`.

## Measured (BendDamageTests, ContactPatchTests)

| J-3, right tip | result |
|---|---|
| 20 m/s, tip down at 5 m/s | twist −2.4°, bend 0.4°, cracked in torsion, **stays on** (old: snapped) |
| 25 m/s, tip 10 m/s, sink 3 | bend 8.8°, twist 3.4°, stays on; in flight 1.1° of aileron to hold level, +18 % drag |
| 30 m/s, tip 15 m/s, sink 6 | torsion tears after 712 J: **separates** |
| 35 m/s, 25° bank, sink 6 | folds 20°, then separates |
| Gear drop 5 m/s / 7 m/s | legs bend and stay / legs go |

Pitts lower panel, 35° bank at 20 m/s: sink 0.8 → set < 0.4°; sink 3 → folds ~21° and hangs on; sink 6 → off.

## The overstress pattern: wings, horizontal tail, engine mount (Ty 2026-11-05 06:28, binding)

> "At the limit load the wing begins to visibly flex with 10 degrees flex At 1.5x the limit (ultimate load) this accounts
> for actual weight … The engine mount is excluded from the weight allowance because it is fixed and it sags 10 degrees at
> the ultimate g load … After 1.5 limit load permanent deformation begins … 50% of any bending after the ultimate load is
> permanent … 2% per .1 overage … the flex should start at the limit load and be 10 degrees at 1.5 and 20 degrees at 2.0
> where they would snap and depart."  (Full text: ~/Developer/agent-lanes/prompts/ty-overstress-pattern-1105.md.)

`src/FlyingGame.Core/OverstressMember.cs`, three of them on `Aircraft.Structure` (`StructuralState.Wings`,
`.HorizontalTail`, `.EngineMount`), wired in `src/FlyingGame.Sim/AircraftOverstress.cs`.

| r (× limit, of what is left) | shape | when the load comes off |
|---|---|---|
| ≤ 1.0 | straight | — |
| 1.0 – 1.5 | flexes 20° per limit: 10° at 1.5 | springs back |
| 1.5 – 2.0 | 10° → 20° | keeps 50 % of the peak bend; next time 2 % weaker per 0.1 of overage |
| > 2.0 | — | snaps and departs |

- **Load.** Wings and horizontal tail: `r = n / n_limit × W / W_gross` (`LimitsConfig.WingWeightRatio`; a sailplane that
  states its non-lifting mass uses `(m − wings) / max non-lifting`; W_gross is the placard's maximum take-off mass, else
  the config's authored mass). Engine mount: `r = n / n_limit`, no weight allowance. Positive and negative sides each on
  their own limit (Pitts −5 g, Ty 06:29). `n` is the load factor the AIR makes (`AeroLoadFactorZ`): a ground, water or
  solid push is the contact model's (plastic hinges, FuselageCrush), not a load factor on the mount.
- **Response.** The member follows the load through a 50 ms first-order lag (a light wing's first bending mode, 3–8 Hz;
  the same 50 ms the old wing failure waited out): one RK4 spike bends and breaks nothing.
- **Set.** An occurrence starts past 1.5 and is booked when the load is back inside the limit: set = half the bend at its
  peak (a same-way set already as large stays; a pull the other way bends it back by half that pull's bend; never more
  than 10°). Reloading the same way runs straight from the set back to the peak that left it, then on along the curve.
- **Weakening.** Each occurrence multiplies the whole curve (limit, 1.5, 2.0) by `1 − 0.02 × (peak − 1.5)/0.1`. The
  wings' weakening is also taken off every wing panel's hinges, so a weakened wing is weaker in a ground strike too.
- **Geometry.** Wings: every panel outboard of its station turns tip-up by set + flex (`WingPanel.OverstressSetRad`,
  `FlightFlexRad`): dihedral, load points, hard points, the drawn mesh. Tail: each half turns about the centreline, tips
  DOWN under positive g (a pull is a download on the tail, and its own mass hangs that way): the rows' dihedral and load
  points, and the stab/elevator meshes (`AirframeVisual.ShowTailBend`). Engine: droops nose-DOWN under positive g about
  the firewall (`Aircraft.EngineSagRad`, which also carries a crash's `NoseBendRad`): the prop runs in the drooped frame
  (thrust line, disc angle to the air, hub position) and the nose section is drawn on its pivot once the mount is past
  1.5 × or has a set (the cut freezes the model's rig, as the crash bend does; below that the droop is in the physics only).
- **Departure.** Wings: `FailWings`. Tail: `LoseComponent(TailHorizontal)`. Engine: the nose section (`Nose`, which takes
  the nose leg), or a wing engine's nacelle.

Measured (`OverstressPatternTests`, J-3 limit +3.8 g):

| case | result |
|---|---|
| hold 0.99 × limit | nothing flexes, no pops |
| 1.6 × limit and back | 12° at the peak, 6° set, 2 % weaker |
| 2.0 × limit at gross | wings, engine and tail 10° set, strength 0.90, nothing departs |
| 2.0 × limit at 110 % gross | wings (and tail, weight-adjusted) depart, engine stays with 10° droop |
| 2.01 × limit at 80 % gross | wings 6° set (the engine's mass leaves halfway through), engine departs |
| repeated 1.6 pulls | strength 0.98, 0.954, 0.920, 0.876, 0.819, 0.745: breaks at 1.49 × the original limit |
| Pitts −10 g at gross | wings −10° (tips down), stays; −10.25 g: wings go |
| wings 10° set | dihedral effect (roll per rad of slip) −8,846 → −17,723 N·m/rad at 30 m/s |
| engine drooped 10°, full power | pitch rate after 50 ms −0.0068 → −0.0149 rad/s (nose down) |

## Not done

- A bent gear leg absorbs and stays, but its geometry does not change (the aeroplane does not sit lower on it).
- No fatigue below the backbone: a cracked member under loads it can carry does not get weaker.
- Ailerons on a bent panel are not redrawn bent (placeholder, as before).
- Overstress: a type with no wing panels (no hinged ailerons) flies its wings' set and flex nowhere (the member still
  breaks it); twin engines' nacelles are not drawn drooped and their thrust arms are turned about the CG (approximate);
  a one-mesh downloaded model's tail does not bend on screen. A hard gear-first landing loads no member (the contact
  model's), so it cannot tear the engine off by its inertia.
