# The Pitts' flat spin: the rudder is not the problem, the aileron is

2026-11-11, `lane/claude-spin-rudder-tdpf`. Every number here was measured on this lane off
`lane/claude-quest-islandnav`; the probe is `SpinRudderBudgetProbe` (`RUD_BUD=1`) and the pins are
`PittsSpinRudderAuthorityTests`. **Which numbers are whose** is marked throughout: *Ty's* are his flight reports,
*published* are NACA/NASA, *measured* are this lane's.

---

## The finding

**Ty is right that the Pitts will not recover from its developed flat spin — but not for the reason on record.**
The rudder has real authority. What defeats it is the pro-spin aileron, and only while the aileron is held.

| full opposite rudder from the developed flat spin | *measured* |
|---|---|
| stick aft, ailerons neutral | recovered in **1.97 turns** |
| stick neutral (Beggs-Mueller) | recovered in **2.13 turns** |
| stick aft, **aileron held** | **did not recover in 20 s** — 10.3 turns, still 225 deg/s |
| Beggs, **aileron held** | 4.50 turns (recovers, but fails the 2-turn standard) |
| 2/3 rudder, 1/3 pro-spin aileron | **did not recover in 20 s** — 12.6 turns |

**The arithmetic behind that table.** At the developed flat spin, *measured*: taking the aileron out is worth
**199 N·m** of yaw, where reversing the entire rudder is worth **154 N·m**. The aileron is a bigger yaw term
than the whole rudder. Hold it and there is 199 N·m of pro-spin yaw against 154 N·m of rudder — net pro-spin,
and the rotation does not stop. Take it out and the rudder's 154 N·m acts unopposed.

The last row is NASA TP 1009's *criterion spin*, the published robustness case. Take the aileron out and the
aeroplane meets NACA TN 1329's standard of recovery in 2 turns by rudder reversal alone. Leave it in — which is
how the spin was entered, and what a pilot who has not consciously centred the stick is doing — and full
opposite rudder does not get it out.

This is the measurable form of why Beggs-Mueller insists on letting go of the stick, and it agrees with the
flat-spin aileron behaviour already pinned in `PittsFlatSpinAileronTests`: out-spin aileron flattens this spin
and speeds it up, which is the *published* behaviour of a fuselage-loaded aeroplane (NACA TR-379 / TN 1329), and
the Pitts is extremely fuselage-loaded.

**Open question for Ty, and the one thing that would settle this:** in your S-2B, with the stick held where it
was for the flat spin, does full opposite rudder alone bring it out? If it does not — if it takes releasing or
centring the stick — then the model is right and this is not a bug at all.

---

## The standing finding was wrong in three separate ways

> *"anti-spin rudder produces about 0.2 deg/s² where recovery needs roughly 135 — three orders of magnitude short"*

### 1. The 0.2 deg/s² is this model *before* two fixes that have since landed

It reproduces exactly. Set `ControlSpoilerK = 0` **and** `FinSidewashK = 0` and the rudder reversal at the
developed flat spin makes 4 N·m, which is 0.2 deg/s² on Izz 1185. Those terms landed 2026-11-09 and 2026-11-10,
the second being the one Ty flew and approved (*"That is it! Now it behaves like a Pitts really does"*). As
authored it makes **154 N·m / 7.5 deg/s²** — 38x the quoted figure.

The standing number is a pre-fix number that outlived its fix. `PittsSpinRudderAuthorityTests` now pins this so
it cannot quietly become true again.

### 2. The "required 135 deg/s²" does not follow from its own stated two turns

Stopping a rotation Ω in N turns at constant deceleration needs Ω²/(2·2πN). At the 269 deg/s the figure was
derived from, two turns needs **50 deg/s²**. 135 corresponds to about **three quarters of a turn**.

And constant deceleration is the wrong instrument: a spin recovery is self-accelerating. The nose drops, α falls,
q rises and the fin unblankets, so the moment grows as it works. *Measured:* the instantaneous authority at the
flat attitude is 7.5 deg/s², and the mean deceleration actually achieved over a recovery is **45.6 deg/s²**,
about six times it. **A static authority number at the flat attitude is not a recovery criterion.**

### 3. The old measurement condition no longer exists

`SpinAuthorityProbe`'s entry was measured at α 91.8 on 2026-11-09. On today's tip that same entry settles at
**α 11.1, 148 kt** — a spiral, not a spin — so everything it prints is a sweep of the relative wind at four times
a spin's dynamic pressure. **Its numbers should not be quoted until its entry is repaired.**

The flat spin lives at **full power with the aileron held**: `Developed(rudder −1, lever −1, aileron +1)`, which
is what `AftCamberSpinProbe` uses. At idle with ailerons neutral the same aeroplane sits at α 28 in a steep spin
where the rudder has 139 deg/s² and the whole question is uninteresting. **The condition is the measurement.**

---

## Where the rudder's moment comes from

Each factor switched off and the reversal re-measured, at the developed flat spin (*measured*):

| | reversal | that factor is worth |
|---|---|---|
| as authored | 154 N·m / 7.5 deg/s² | — |
| base-pressure spoiler term off | 25 N·m / 1.2 | **84 %** |
| fin sidewash off | 70 N·m / 3.4 | **54 %** |
| both off | 4 N·m / 0.2 | 97 % |
| `stabHighAlphaK` off | 154 N·m / 7.5 | 0 % (horizontal tail only) |

The section's own normal-force change with deflection contributes about 3 % — the documented arithmetic wall
(`fin-lowAR` holds Cl flat from 25° out). **Both load-bearing terms are ones Ty flew and approved; neither is
free to remove.** `ControlSpoilerK` was fitted to the elevator and was *not* re-fitted here: the measurement did
not ask for a separate rudder coefficient, so none was invented.

### The fin is blanketed; the stabiliser is not

The standing note says *"it is NOT blanketing — eta_t is 1.3–1.5 … do not go looking at TailWake for this."*
That figure is the **horizontal tail's**, and for the horizontal tail it is correct. It was then applied to the
rudder. *Measured* at the same developed flat spin:

| | mean eta_t | q_local (q_inf 349 Pa) |
|---|---|---|
| horizontal tail | 1.16–1.22 | above free stream |
| **fin + rudder** | **0.26** (0.17–0.61) | **92 Pa** |

So the rudder's deficit *is* local dynamic pressure, and the instruction not to look at `TailWake` was derived
from the wrong surface. **The two known-wrong tail-shadow items — the shadow sheet needing deflected geometry,
and needing to be lee-side only — are therefore live for the rudder.** They were not touched here: the fix makes
forward elevator blanket *more* rudder, which moves *against* recovery, and this aeroplane currently sits exactly
on the 2-turn boundary. That is Ty's call, not a lane's.

### The ventral rudder does half the work, and NACA says it should

*Measured:* the one rudder strip below the tail plane (config z +0.089, area 0.159 m²) carries **79 of the 156
N·m** — 51 % of the reversal moment from 18 % of the area — and is the only strip with usable dynamic pressure
(eta 0.61 against 0.17–0.30 above the stabiliser).

*Derived from the authored three-view geometry,* NACA's unshielded-rudder construction says the same: **95 % of
the Pitts' unshielded rudder is that ventral area** (S_R2 0.144 m² of 0.150 m² unshielded). Two unrelated
methods, the same structural answer.

---

## The external check: TDPF

*Derived here from authored geometry; boundary is published* (NACA TN 1329 light-aeroplane chart, recovery by
rudder reversal alone in 2 turns). Total biplane wing area, b = upper span.

| | TDR | URVC | TDPF | boundary for its μ | verdict |
|---|---|---|---|---|---|
| Pitts | 0.0251 | 0.0151 | **380 ×10⁻⁶** | ~195 ×10⁻⁶ (μ 9.9) | **above, 1.95x** |
| Great Lakes | 0.0090 | 0.0076 | **69 ×10⁻⁶** | 50 ×10⁻⁶ (μ 5.5 floor) | marginal, 1.37x |

**The geometry says the Pitts should recover, and with the ailerons neutral it does.** There is no contradiction
with the aileron-held failure, because **TN 1329 computed every one of its boundaries with ailerons neutral**,
and TP 1009 lists aileron deflection first among the factors that invalidate TDPF as a predictor. The criterion
simply does not cover the case that is failing.

Caveats that must travel with these numbers: TN 1329's population was *conventional monoplanes, 1935–45*; the
Pitts' inertia yawing-moment parameter (−219×10⁻⁴) is 1.8x off the left edge of the published chart; NASA TP
1009 (1977) concluded the criterion *"cannot be used as a criterion for prediction of spin recovery; at best it
should be considered only as a guideline for tail design"*; and it is known to be **optimistic for short-tailed
aeroplanes**, which is exactly what a Pitts is. The biplane wing-area convention is a genuine judgement call —
upper-wing-only would give the Pitts 1383×10⁻⁶ and put μ at 16.2, outside the published population, which is why
total area is used.

### A real geometry bug found on the way

**The Great Lakes' rudder has no strip below its horizontal tail, but the real aeroplane has 0.090 m² down
there** — 16.5 % of its rudder area, and on the NACA construction 100 % of its unshielded rudder. Its four
`rudder-vstab` strips sit at config z −0.061, −0.304, −0.546, −0.789 against a tail plane at z **+0.140**: all
four are above it, and the ventral area is folded into the lowest strip, which is then placed 0.20 m *above* the
tail plane. The Pitts has this split correctly (and carries a config comment explaining it was done deliberately
on `lane/claude-pitts-spin`).

So any shielding term keyed to a strip's z shields the Great Lakes' ventral rudder when it should not. **The two
types are not in fact receiving identical tail treatment at strip level**, which undercuts using the Great Lakes
as the control experiment. Not fixed here — `configs/` is parity-locked and this is a geometry change that wants
its own lane and its own re-measurement.

---

## Found on the way: six flat-spin pins are already failing on the integration tip

Not this lane's doing, and **confirmed in isolation** — the run below was filtered to
`PittsFlatSpinAileronTests` alone, with none of this lane's test classes in it, and the assembly sets
`CollectionBehavior(DisableTestParallelization = true)` so no other class can reach them anyway.

`UprightSpinsWithAileronsNeutralAreUnchanged` fails on four of its cases:

```
Assert.InRange() Failure: Value not in range
Range:  (30 - 33)
Actual: 28.329465853011339
```

The upright idle spin now settles at **α 28.3** where the pin wants 30–33. That is the same 28.3 measured
here before any file on this lane existed, so the aeroplane moved, not the test harness.

**The indicated cause is the tip commit itself** — `89e74242`, "Turn the aft-fuselage broadside camber on, at
0.80, on the Pitts and the Extra" — whose own message says the term *"steepens an upright spin"*. It steepened
it by about 3° past the band and the pin was not re-pinned in the same commit. Stated as indicated rather than
verified: confirming it means re-flying the spin at that commit's parent, and the box was at load average 255
with other agents' suites when this was written.

This belongs to whoever lands that change, not here — but a pin that is failing is not protecting anything, and
these are the pins that would otherwise have caught the spin attitude moving under the rudder measurement above.
