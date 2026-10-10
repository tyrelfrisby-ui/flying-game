# Vertical CG / wing height check — 2026-10-10

The owner asked for a check of the vertical CG on every airplane, after noticing that the Extra "seems to roll about a point below the aircraft".

**Tool:** `CgCheck` (editor) builds each type the way the game draws it and writes `build/cg-check.csv`.
- Run it with `Unity -batchmode -quit -projectPath unity -executeMethod FlyingGame.EditorTools.CgCheck.Run`.
- `AirframeRender` with `CG_MARK=1` draws the CG as a red ball.

## Fixed and shipped

- **Extra 300 (drawing only, ecfb3b9).** The model was placed from inflated bounds, which lifted it 0.7 m off its wheels.
  - It was also exported in the three-point attitude, so it is now levelled 11° nose-down.
  - The CG is now inside the fuselage. No physics change.
- **737, P-51, Seminole (physics): low wings were typed as above the CG.**
  - The cause was a sign slip: the strip `z` had the same sign as `heightAboveBodyAxisM`. Everywhere else the two signs are opposite.
  - The new values were checked against the drawn airframes:

| Type | Change |
|---|---|
| 737 | Wing z −1.5 → +1.0. Engines and thrust line −1.9 → +1.6: the engines hang below the wing, so power now pitches the nose up, as on the real airplane. |
| P-51 | Wing z −0.4 → +0.25. Thrust line −0.6 → −0.44 (the drawn prop hub). |
| Seminole | Wing z −0.3 → +0.2. |

All flight tests pass with these; the glide table was regenerated.

## Held for the calibration session (owner-led)

The patch is `dc3-f86-vertical-geometry.patch` (configs/aircraft, `git apply` it). It contains:
- **DC-3:** wing z −0.9 → +0.95, engines and thrust line 0 → +0.35.
- **F-86:** wing z −0.3 → +0.3.

The geometry is right, but these types' earlier tuning depended on the wrong wing height:

| Type | What breaks with the correct geometry |
|---|---|
| DC-3 | Hands-off speed band 4.6 m/s (the limit is 2.75): stick-free speed stability is worse, and formation slot-keeping fails. |
| F-86 | Full-aft balance 12.5° vs a 10° stall (the target is 9.5°). The low wing adds the real swept-wing pitch-up, and the elevator saturation is already at the calibration's 3° floor, so the tail/CG needs a protocol rework rather than a knob. |

Also for the session:
- **Extra 300 and Cirrus SR22:** the CG sits about 0.25–0.45 m below the drawn thrust line, which is low for a single (most types are 0–0.25 m). This affects spins, so it is part of the spin calibration.
- **GeeBee:** the sim's wing is about 0.4 m below the drawn wing.
- **Models to re-orient:** the EB-29R glider is pitched about 16° off, and the target drone model is mis-oriented.
