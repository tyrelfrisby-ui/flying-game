# Data Contracts

All three are versioned JSON. Rule learned the hard way on Glass Overlay: **every field added after v1 is optional with a default** — old files must always load. Units: SI (kg, m, m/s, radians in data; degrees only in UI). Body axes: x forward, y right, z down; CG-referenced.

## AircraftConfig
One file per aircraft. The physics reads ONLY this — no aircraft-specific code.

```jsonc
{
  "schemaVersion": 1,
  "id": "glider-2-33-like",
  "displayName": "Trainer Glider",

  "mass": { "massKg": 350, "cg": [0,0,0],
    "inertia": { "ixx": 1200, "iyy": 900, "izz": 1900, "ixz": 60 } },

  "surfaces": [                       // wing, hStab, vStab — same schema each
    { "id": "wing",
      "strips": [                     // per spanwise strip
        { "pos": [0.1, -7.5, 0],      // quarter-chord point vs CG (m)
          "chord": 1.2, "area": 1.1,
          "incidenceRad": 0.02,       // washout = varying incidence outboard
          "dihedralRad": 0.05,
          "airfoil": "clarkY-like",   // key into airfoilTables
          "control": { "surface": "aileron", "gain": -0.6 } } // null if none
      ] }
  ],

  "airfoilTables": {                  // full ±180° so post-stall is real
    // cd = PROFILE drag only. Induced drag is computed per strip in AeroModel as
    // Cl²/(π·AR·oswaldE) — AR from the surface's strip geometry, oswaldE per surface
    // (optional, default 0.85) — so the drag polar varies with local alpha and control
    // deflection instead of being baked flat into the table.
    "clarkY-like": { "alphaRad": [...], "cl": [...], "cd": [...], "cm": [...] }
  },

  "controls": {                       // per surface: travel + input shaping
    "aileron":  { "maxDeflRad": 0.35, "rateRadPerSec": 3.0, "expo": 0.3, "deadZone": 0.05 },
    "elevator": { "...": "same shape" },
    "rudder":   { "...": "same shape" },
    "spoiler":  { "maxDeflRad": 0.9,  "dragOnly": true,
                  "axis": "throttleLever",          // glider: throttle lever IS the speed brake
                  "axisMap": "aftOnly" }            // neutral=stowed, full aft=deployed, forward inert
  },

  "fuselage": { "cd0Area": 0.08, "sideForceArea": 1.5, "damping": {"p":0,"q":0,"r":0} },
  "propulsion": null,                 // arena 2+: prop table, P-factor, torque, slipstream
  "gear": [ { "pos": [0,0,0.9], "springN": 20000, "dampNs": 2000, "brake": false } ],
  "limits": { "vneMs": 43.81, "gMax": 4.67, "gMin": -2.33,     // structural fail → crash
                "_vneSource": "98 mph, FAA AFM 1040 lb + the type's own placard (Ty 2026-11-10)" }
}
```

### Smoke outlets

The optional `smoke` block sits beside `propulsion`. `aftOfNoseM` measures aft from the
spinner tip; `downDeg` is the outlet axis below the aft longitudinal direction. The
legacy hub estimate subtracts 0.25 m from `aftOfNoseM` and drops the outlet by one
body radius (at least 0.25 m). Without a smoke block it drops by 0.8 body radii.

`outletBodyM`, when present, overrides that estimated position: three finite metres
from the **CG**, in body axes **forward, right, down**. This is deliberately different
from a config reference-frame station such as a gear position. Measure the placed
visual, including its span fit, wing alignment and vertical placement. A missing or
malformed vector uses the legacy estimate. `downDeg` still supplies the jet direction;
it is the pipe's longitudinal/vertical projection, with no lateral jet angle.

`displacementCuIn` sets mass flow (0 estimates it from power); `pipeDiameterIn` defaults
to 4 and `exhaustTempK` to 900. One authored outlet produces one smoke stream, including
when selecting one smoke-fed pipe on an airframe with paired exhausts. Glider wingtip
canisters and configs without a measured outlet retain their existing paths.

`pipeDiameterIn` is **one 4 in stack, the fleet default** (Ty 2026-10-13). A type that
authors `smoke.pipes` does not use it and should not author it: its stacks *are* the pipes
the smoke comes out of, and `SmokeConfig.JetPipeDiameterM` / `JetPipeCount` are what the
jet model reads. Everything that needs an exit velocity goes through
`ExhaustJet.ExitVelocity(smoke, propulsion, …)`, so no path can read a different pipe from
the one the smoke leaves.

See [the measured fleet and annotated renders](pending/smoke-renders/README.md).

#### `smoke.pipes`: a measured bank, which supersedes all of the above

Ty, 2026-11-11: *"The extras smoke comes out too far forward it needs to come out about the
firewall and it comes out of two pipes 2 inch dia each separated by 1 ft on center pointed
down at 45 degrees with one puff per pipe every other combustion stroke"*, and of the Pitts
*"the same problem but the pipes are 1.5 dia each and separated by 8 inches on center"*,
*"two pipes centered at the firewall"*, *"1.5 dia each 45 degrees Down"*.

Where `smoke.pipes` is authored it is the whole answer for WHERE the smoke is born and WHICH
WAY it blows: `SmokeTrail.ExhaustsFor` reads it before `outletBodyM`, before `aftOfNoseM` and
before the prop-hub estimate, and its `downDeg` replaces the smoke block's. Fields: `count`,
`firewallXM`, `mouthZM`, `spacingIn` (on centre), `diameterIn`, `downDeg`,
`combustionStrokesPerPuff`, plus `source` (Ty's numbers) and `stationSource` (how the station
was measured). A half-authored or non-finite block is ignored whole, so the type falls back
rather than drawing smoke at NaN. `outletBodyM` and `aftOfNoseM` stay authored beside it
because `Wreck.AirframeHydroModel` locates the nose for the water model from the two of them.

`firewallXM` and `mouthZM` are in the **config reference frame** — the frame the gear
stations, the surface strips and the drawn GLB are in, which is the frame the fleet's
`outletBodyM` values are actually authored in. They are measured off the model, never off a
scaled drawing: `extra-300-like` reads its `Cockpit_FirewallPlate` plane straight out of the
GLB, and `pitts-s2b-like` has to reject the plate its builder labels "firewall" (it is buried
inside the same model's engine, and moving the smoke onto it would push the outlet further
forward, not aft) and take the aft face of the drawn engine instead — the convention
`CONTROL-SURFACES.md` already uses on this fleet. Each config's `stationSource` carries the
whole derivation and its cross-checks against the AEIO-540's published length.

The mouths are placed symmetrically about the centreline (the bank's mean `y` is 0 whatever
`count` is), all at `firewallXM` and `mouthZM`, each aimed `downDeg` below the aft
longitudinal direction with no lateral angle.

`combustionStrokesPerPuff` is a **visual timing, derived from the engine, never a chosen
rate**. `ExhaustPulse.PuffHz(cylinders, rpm, strokesPerPuff)` divides the pipe's combustion
strokes among the puffs: a six at 2,700 rpm lets out 135 strokes a second, so 2 gives 67.5
puffs a second from each of two pipes, and the pair fire alternately, so no stroke is invented
or lost. 67 Hz is not resolvable, which is the point — the cadence is keyed to the crank, so
the column pulses visibly at idle (17.5 Hz), goes ragged when the engine does and stops when
it stops.

`diameterIn` and `count` are **the exhaust stacks**, not a separate set of smoke nozzles.
Asked on 2026-11-11 whether the pipes he measured are the stacks themselves, Ty said yes,
so there is one pipe per type and one bore: `SmokeTrail.PipeDiameterM` is it, and the exit
velocity, the jet's momentum, the pulses' penetration, the jet's spread and the drawn
column's starting width all come from it. The engine's mass flow is unchanged — it is the
same aeroplane — and so is every coefficient in the jet's fit (VE 0.85, AFR 12.5, γ 1.35,
`compressionRatio`, `exhaustTempK`, the round-jet shear coefficient 0.057, and the 6-diameter
potential core). The bore is an input to them, not one of them. What moves is the area:

| | total area | U₀ full power | U₀ idle | exit Mach at 900 K |
|---|---|---|---|---|
| one 4 in stack (the default) | 81.07 cm² | ≈ 70.5 m/s | ≈ 4.5 m/s | 0.12 |
| Extra, two 2.0 in | 40.54 cm² (2.00× less) | 140.8 m/s (2.00×) | 8.91 m/s | 0.23 |
| Pitts, two 1.5 in | 22.80 cm² (3.56× less) | 251.1 m/s (3.56×) | 15.88 m/s | 0.42 |

All subsonic: the speed of sound in 900 K exhaust is 601 m/s, not the ambient 343.
`SmokeStackJetProbe` prints the whole before/after table, including how far the plume
actually punches out — and the caveat that `ExhaustJet.Step`'s 4 ms march is not converged
at these speeds, so the drawn push is about twice what the model says. See the note on
`ExhaustJet.Step`.

## ChallengeDefinition
New challenges = new JSON, not new C#.

```jsonc
{
  "schemaVersion": 1,
  "id": "a1c1-wings-level",
  "arena": "arena1-farm-grid",
  "aircraftId": "glider-2-33-like",
  "titleKey": "challenge.a1c1.title",         // localization keys, not raw strings

  "spawn": { "pos": [0, 0, -600],             // 600 m AGL
             "headingRad": 0, "iasMs": 22, "attitudeTrim": true },
  "environment": { "windMs": [0,0,0], "gusts": null, "thermals": null },

  "phases": [                                  // sequential; most have one
    { "id": "hold",
      "durationSec": 30,
      "tolerances": [                          // graded live, each a band
        { "signal": "headingRad", "target": 0,    "band": 0.087 },   // ±5°
        { "signal": "bankRad",    "target": 0,    "band": 0.087 } ],
      "callouts": [ { "atSec": 0, "textKey": "callout.holdHeadingNorth" } ] }
  ],

  "scoring": { "mode": "accuracyPercent",      // or "points"
               "passThreshold": 80 },
  "fail": { "onGroundContact": "crash",        // crash | fail | ignore
            "onOverstress": "crash",
            "bailAllowed": true },              // bail = free restart, no life lost
  "rewards": { "credits": 10, "unlocks": [] }   // economy stubs; unused in v0
}
```

Signals available to `tolerances`: heading, bank, pitch, IAS, altitude, glide ratio, AoA, sideslip, roll/pitch/yaw rate, g — extendable list in one place (`ChallengeSignal` enum in Core).

## ProgressSave
Local JSON (iCloud sync later). Small on purpose.

```jsonc
{
  "schemaVersion": 1,
  "playerId": "local",
  "challenges": { "a1c1-wings-level": { "bestScore": 92.5, "attempts": 7,
                                        "passed": true, "firstPassedAt": "2026-09-06T00:00:00Z" } },
  "unlocks":   { "aircraft": ["glider-2-33-like"], "arenas": ["arena1-farm-grid"] },
  "economy":   { "credits": 0, "lives": 3 },     // v0: displayed nowhere, mutated nowhere
  "settings":  { "controlExpoOverride": null, "invertElevator": false }
}
```

Load rules: unknown fields preserved on rewrite (forward compat), missing fields defaulted, `schemaVersion` gates migrations. Corrupt file → rename `.bak` and start fresh, never crash.
