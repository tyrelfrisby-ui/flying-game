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
    "clarkY-like": { "alphaRad": [...], "cl": [...], "cd": [...], "cm": [...] }
  },

  "controls": {                       // per surface: travel + input shaping
    "aileron":  { "maxDeflRad": 0.35, "rateRadPerSec": 3.0, "expo": 0.3, "deadZone": 0.05 },
    "elevator": { "...": "same shape" },
    "rudder":   { "...": "same shape" },
    "spoiler":  { "maxDeflRad": 0.9,  "dragOnly": true }
  },

  "fuselage": { "cd0Area": 0.08, "sideForceArea": 1.5, "damping": {"p":0,"q":0,"r":0} },
  "propulsion": null,                 // arena 2+: prop table, P-factor, torque, slipstream
  "gear": [ { "pos": [0,0,0.9], "springN": 20000, "dampNs": 2000, "brake": false } ],
  "limits": { "vneMs": 60, "gMax": 4.7, "gMin": -2.3 }   // structural fail → crash
}
```

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
