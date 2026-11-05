# Cockpit systems shared by every aircraft (Ty's spec 2026-11-05)

Lane `lane/claude-cockpit-common`. Spec: `~/Developer/agent-lanes/prompts/ty-cockpit-spec-1105.md` (IAC CARD SYSTEM,
ALL AIRCRAFT, ADDENDUM Cubs). Contract: `~/Developer/agent-lanes/briefs/cockpit-1105-contract.md`.

## A. No seat belts, no headset cords; bail-out is one press

- **What a belt is** is decided in one place, `src/FlyingGame.Core/Vr/BeltsAndCords.cs`: node names (Harness_*,
  Grip_Harness_*, Shoulder_/Lap_Harness_*, Buckle_*, Lap_belt_*, Belt_buckle_*, Headset_Plug/Cable/Cord) and belt-only
  materials (Cockpit_harness, Cockpit_lap_belt, Harness_Webbing, Harness_Release, Int_Webbing, belt, HeadsetCord).
  Kept: the headset JACKS (a socket in the sidewall), the parachute's harness, every control cable, and the material
  Harness_Steel (the Pitts' and Extra's jacks are drawn in it).
- **Old GLBs are clean too**: `Bridge/CockpitBeltStrip` runs once per placed model (AirframeVisual, before
  CockpitLiveParts cuts meshes): belt nodes' renderers off; a belt sub-mesh inside a merged cockpit mesh (Great Lakes
  `Cockpit_Interior`, Pitts `Cockpit`) emptied in a copy of the mesh. Log line `[Belts]`.
- **Sources**: `vr_phase2.py` (shared) builds only the jacks (`BELTS = False`, the webbing builder kept as `_belts`),
  `vr_cockpit.py` no longer makes `Harness_Buckle_*`; the unowned 2-33, Grob 103 and C172 builders have `BELTS = False`.
  No GLB was rebuilt on this lane: the aircraft lanes merge this before their final rebuild; the runtime strip covers
  every GLB until then.
- **Nobody is strapped in**: `SeatHarness.ForCockpit` is an empty harness while `BeltsAndCords.BeltsWorn` is false.
- **Bail-out = one press**: glass BAIL OUT button, the Quest menu's "Bail out" row (asks twice, as before), and the new
  **E** key on the Mac / desktop (the handle: bail out, or eject / BRS where fitted). In a headset the button bail-out
  now takes the view with the jumper (first person, ripcord on the left chest strap) and the chute opens by itself
  3 s after leaving, the same as on the glass; a hands-on climb-out still leaves the ripcord to the pilot.

## B. IAC cards

- **Deck** (`Core IacCardDeck`): tap/click/poke a card → next. Order: Primary wind ←, Primary wind →, Sportsman ←, →,
  Intermediate ←, →, Advanced ←, →, Unlimited ←, →, then the pilot's own cards, then back to Primary. ONE selection for
  every aeroplane and both seats (`SequenceCard_Face` and `FrontPanel_SequenceCard_Face` always show the same card),
  kept by card id in `persistentDataPath/sequence-cards/library/deck.txt`.
- **Built-in cards**: the IAC's published **2026 Power Known** PDFs, Form B (page 2, wind arrow ←) and Form C (page 3,
  wind arrow →), rasterised unchanged at 1275 x 1650 by `tools/own_models/make_iac_known_cards.py`. SHA-256 of the
  PDFs on 2026-11-05: PRIP 7992e87a…, SPTP 4f110bf7… (same as the GLBs' default card), INTP 5a848f3a…, ADVP 5e2630bd…,
  UNLP 380fbf88…. Not committed; the script prints the curl lines.
- **Upload / remove** (Options → Display → Card row: < Prev, Next >, Add card…, Remove this):
  - iPhone / iPad: the system document picker; every page of the PDF (up to 6) becomes a card, letterboxed onto the
    half-size letter card (PDFKit, `Plugins/iOS/SequenceCard.mm`).
  - Mac: the system "choose file" dialog (PDF, PNG or JPEG); a PDF is drawn by the system's `sips`, **first page only**.
  - Quest: **no document picker** (documented limitation). Copy files into
    `/sdcard/Android/data/<package>/files/sequence-cards/library/inbox/` (e.g. `adb push card.pdf …`) and choose
    "Import cards from the inbox" in the headset menu; PDFs are drawn by Android's PdfRenderer, every page. "Remove this
    card" and "IAC card < >" are rows there too.
  - Any page shape is fitted whole onto the card (`Core LetterCard.Fit`), a landscape page turned to portrait.
  - Cards picked per aeroplane before today are moved into the shared deck once (their folder renamed `.imported`).

## C. Altimeters: knob, Kollsman window, popup

- Every altimeter reads the **static pressure through its setting** (`Core KollsmanAltimeter`):
  `indicated ft = 145366.45 · (1 − (Ps / Pset)^0.190263)`. At the start of each flight the setting is turned to read
  the field's elevation (QNH), as a pilot does before take-off.
- **Windows (per type)**:

  | types | window | zeroes a field up to (standard day) |
  |---|---|---|
  | standard (C172, J-3, 2-33, everything not below) | 28.10 – 31.00 inHg | 1,730 ft |
  | aerobatic: Pitts, Extra, Great Lakes, Decathlon, Grob 103 Acro, Swift, Stearman, Cassutt, JN-4 | **22.00 – 31.00 inHg** | 8,260 ft (7,480 ft on a day 30 hPa low) |

  The aerobatic window is OUR choice (Ty: "enough range … up to ~6000 ft at least"), not a copied instrument spec.
  A field at 6,000 ft on a standard day needs 23.98.
- **Touching it**: tap/click anywhere on the altimeter → popup: the window, hPa, what it reads, −0.10/−0.01/+0.01/+0.10,
  a text box (29.92, 2992, 1013 hPa all accepted), Field elevation (QNH), Zero here (QFE), Done; the scroll wheel over
  the popup turns the knob a hundredth a click. A value outside the window is **refused with the reason**; the buttons
  stop at the ends like the knob. In a headset a poke on the altimeter opens the menu on its "Altimeter" row
  (< > a hundredth; select toggles field elevation / zero). Also in Options → Display ("Altimeter" row).
- **Knob and window on the panel**: the model's `<prefix>Altimeter_Knob` / `<prefix>Altimeter_Kollsman` if it has them
  (contract); otherwise a small black knob at the bezel's lower left and a digit window at 3 o'clock are added at load
  (`CockpitTaps`, drawn with the LCD font; `[CockpitTaps]` log line).
- Note (real behaviour, kept): zeroed at a high field the altimeter over-reads height above it (1,048 ft for a 1,000 ft
  climb from a 6,000 ft field) because it is calibrated in the standard atmosphere from its setting down.

## D. G-meter followers and reset

`Core GMeterFollowers`: max and min followers pushed by the pointer, held until reset; reset brings both back to the
pointer (1 g at rest). Tap/click/poke the g-meter (or its `<prefix>G_meter_Reset` knob) to push the reset; a knob is
added at the bezel's lower left when the model has none, and it moves in and back when pushed.

## E. J-3 fuel wire (ADDENDUM)

`RigInputs.Fuel` = the first tank's fuel / its capacity, live from `AircraftLoad` (the tank the sim burns from), via
`FuelGauges.TankFraction`. The J-3 (wheels and floats) rig tables now drive `FuelGauge_Wire` on `fuel`: 0.221 m of
travel, full = 10 in showing above the cap, empty = only the hook on the guide. **Super Cub**: the PA-18 has no model of
ours (it draws the downloaded OBJ, no float wire node), so there is nothing to move yet; the feed is generic and a PA-18
own model with a `FuelGauge_Wire` rig row on `fuel` would work unchanged.

## Not compiled / not verified here

- The Android-only branch (`SequenceCardPicker` PdfRenderer via JNI) is compiled only by a Quest build, which this lane
  did not run (the Quest lane's deploy does). The Mac-player branch (osascript/sips) is the same code the editor runs.
