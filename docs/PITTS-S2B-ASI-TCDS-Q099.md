# Pitts S-2B ASI markings from TCDS (Q099 / Ty t903u)

**Authority:** Ty t903u ~05:25 AKST Nov 6: "Use the tcds." Units **mph** (t897u). Existing Q099 scope — not new backlog. Do **not** reuse Great Lakes arcs. Do **not** invent limits.

## Authoritative source
- **TCDS:** FAA Type Certificate Data Sheet **A8SO**, Revision **28**, revised **18 FEB 2026**
- **Holder:** Aviat Aircraft, Inc.
- **Model section:** VI — Model **S-2B**, 2 POLB (Normal and Acrobatic), Approved 6 April 1983
- **Verified text layer:** OpenAirDocs transcription of the FAA sheet (`https://openairdocs.org/tcds/A8SO`); PDF download via OpenAirDocs “Download from FAA DRS”
- Mirror copy also cited historically at `http://www.pittss2b.com/a8sopitts.pdf` (same VNE/VNO/VA figures for S-2B)

## Airspeed Limits (CAS) — S-2B only (from TCDS)

| Symbol | Meaning | TCDS value (CAS) |
|---|---|---|
| **VNE** | never exceed | **212 MPH (184 kts)** |
| **VA** | maneuvering | **154 MPH (134 kts)** |
| **VNO** | maximum structural cruising | **154 MPH (134 kts)** |

TCDS note (verbatim sense): *For acrobatic maneuver entry speed see Airplane Flight Manual.*

## ASI color coding implied by TCDS + FAR/PHAK convention
- **Red line:** VNE = **212 mph**
- **Yellow arc:** VNO → VNE = **154–212 mph**
- **Green arc upper end:** VNO = **154 mph**
- **Green arc lower end (Vs1):** **NOT on TCDS**

## AFM reference (required for missing green floor)
TCDS “DATA PERTINENT TO ALL MODELS” / equipment for **S-2B**:
- **FAA approved flight manual revision I dated 14 January 1998, or later FAA approved revision**
- Approved equipment list in AFM Weight and Balance section

**Missing (do not invent; do not ask Ty to research):** Vs1 / green-arc floor mph from that AFM (and any ASI placard page). Secondary press sheets (e.g. AOPA fact sheet “Stall Speed Clean 52 KIAS”) are **not** TCDS/AFM and must **not** be applied as authoritative markings.

## What was wrong in shared-instruments tip `4e0d8575`
`configs/aircraft/pitts-s2b-like.json` `instruments.airspeed` currently copies **Great Lakes**:
- green 58–120, yellow 120–153, redline 153, scaleMax 200
- Explicit `_source` admits GL markings; Q099 left open

## Integration directive (fold into Q098 pending merge — same workstream)
When integrating `lane/claude-shared-instruments` **4e0d8575** (Q098 APPROVED):
1. Replace Pitts ASI with TCDS S-2B mph values above for yellow/red/VNO/VNE.
2. Raise `scaleMax` so dial can show **≥212** (current 200 cannot paint VNE).
3. Align `limits.vneMs` with 212 mph CAS (≈ 94.77 m/s) — current `vneMs: 90` ≈ 201 mph (S-2A-ish), wrong for S-2B.
4. **Green floor:** leave unresolved / omit inventing; document AFM gap in AGENT-LOG + INSTRUMENT-LIBRARY. Prefer holding green lower until AFM page is on file over copying GL 58 or AOPA 52 KIAS.
5. Units remain **mph**.
6. No new backlog item — this is Q099 closure of research + config correction inside existing shared-instruments integration.

## Out of scope
- Great Lakes ASI unchanged (their own arcs).
- Q100 g-meter, Q101 Extra ASI law, Q102 glider library — still open, untouched.

## Green floor (Ty t904u ~05:30 AKST Nov 6)
Ty: "It is just published stall speed."
**Published:** Aviat Aircraft Inc. *Pitts Special S-2B Aircraft Owner’s and Maintenance Manual* 70197-001 (1-1-97 / performance page dated 4-1-97): **STALLING SPEED — 63 MPH IAS**.
No CAS↔IAS conversion invented; figure already mph IAS. Not Great Lakes 58; not AOPA 52 KIAS.
ASI green arc: **63–154 mph**; yellow **154–212**; red **212**.
