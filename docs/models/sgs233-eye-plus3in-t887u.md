# SGS 2-33 eye +3 in (Ty t887u 2026-11-06)

## Axis
Body frame: +x forward, +y right, **+z DOWN**. Raising the eye = decreasing z.

## Baseline (playable before this commit)
- Front (primary) `loading.seats[0].eye` = **[0.971, 0, −0.136]** (eyepoints v6, Ty approved 2026-11-02 +25 mm over v5).
- Rear `loading.seats[1].eye` = **[0.164, 0, −0.06]** (unchanged).

## Delta
- Vertical only: **3 in = 0.0762 m up** → z: −0.136 − 0.0762 = **−0.2122**.
- New front eye: **[0.971, 0, −0.2122]**.
- Did **not** apply unmerged lane tip `6d866b6b` (−0.187 / +2 in).

## Authority
Ty t887u: "Move eye position up 3 inches on 2-33". Implementation authorized; **visual/fly approval not invented** — before/after renders for review.
