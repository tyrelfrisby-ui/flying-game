# Landing page v2: flow, view models, thumbnails

Ty 2026-11-05 05:48 (verbatim in `~/Developer/agent-lanes/prompts/ty-landing-page-spec-1105.md`) and the 05:50 addendum:
start on the aeroplane (last flown; the 2-33 the first time), thumbnail menus you open and scroll, challenges and lessons
on the right with restricted options, a second page for people / fuel / baggage / settings, then LAUNCH. The aeroplane
stays visible on every page with its configuration fed back; floats get ~20 % more gross; W&B warns, never blocks.

Two lanes: **lane/claude-landing-v2** owns the flow, state, persistence, catalogs, thumbnails pipeline and tests;
**lane/codex-landing-v2** owns the look (cards, carousels, icons, light/dark, layout per device). Codex binds to the
types below and changes nothing in them; ask Claude (AGENT-LOG) for anything missing.

## The contract (view models)

All in `src/FlyingGame.Core/Landing/` (namespace `FlyingGame.Core.Landing`), pure C#, no Unity:

| Type | What the view reads / calls |
|---|---|
| `LandingFlow` | THE state machine. `Page` (`Choose`/`Load`), `OpenPicker` (`PickerKind`), `Version` (bumps on every change; redraw/refresh on change), `Changed` event. Navigation: `Next()`, `Back()` (closes an open menu first), `GoTo(page)`, `Open(PickerKind)` (toggles), `ClosePicker()`, `CanGoNext/CanGoBack/CanLaunch`, `RequestLaunch()` (what buttons call: asks first when the W&B is out of limits), `ConfirmLaunch(fly)`, `LaunchConfirmPending`, `Launch()` (unconditional). |
| page 1 | `Items(PickerKind)` → `List<PickerItem>` for Aircraft / Configuration / Location / Viewpoint / Challenge / Lesson (already RESTRICTED by the activity's rules; disallowed cards are not in the list). `Pick(kind, id)` (closes the menu). `IsFixed(kind)`: a menu with nothing to choose (draw a fixed line). Hero: `HeroTitle`, `HeroSubtitle`, `HeroThumbnails`; current choice: `Family`, `Gear`, `Location`, `Viewpoint`, `ViewpointName`, `Activity`, `ActivityKind`, `Rules` (`AircraftNote`, `FixedViewpointNote`, `ConditionsLocked`). |
| `PickerItem` | `Kind, Id, Title, Subtitle, Selected, Enabled, DisabledReason, Thumbnails` (keys, most specific first), `IconKey` (`icon/<group>/<id>`, for the visual lane's drawn icons), `ThumbnailKey`. |
| page 2 seats | `Seats` → `SeatRow { SeatId, Label, HasControls, Occupant (Empty/Me/DefaultPerson/InvitedPlayer), Role (Passenger/DualControl), Kg, MaxKg, InviteName, IconKey }`. `CycleSeat(id)` (one tap: empty → person → invited passenger → invited on the controls → empty), `SetSeat(id, who, role)`, `SetSeatKg(id, kg)`, `SetInviteName(name)`. Exactly one seat is `Me`. |
| page 2 load | `Stations` → `StationRow { StationId, Label, Kind (Fuel/Baggage), Kg, MaxKg, Fraction }`, `SetStationKg`, `SetFuelFraction(0..1)`, `FuelFraction`. |
| W&B | `WeightBalance` → `{ TotalKg, MaxGrossKg, HasGross, HasEnvelope, CgM / ZeroFuelCgM, FwdLimitM / AftLimitM (at the loaded weight), PointX/PointY, ZeroFuelX/ZeroFuelY, Envelope / EnvelopeCg (closed), Limits (named lines: "Forward c.g. limit", "Aft c.g. limit", "Max gross"), XTicks / YTicks + XAxisTitle / YAxisTitle (Standard: in aft of datum, lb; Metric: mm, kg), Warnings (factual labels), Details (with amounts), InLimits, TakeoffInside / ZeroFuelInside, CanLaunch (always true) }`. Handbook layout: CG across (x 0 = forward edge, nose left), weight up (empty → 110 % gross). Limits vary with weight (`AircraftLoad.CgLimitsAt`). See "W&B envelope" below. |
| settings | `TimeItems()`, `ReliabilityItems()`, `WeatherItems()` (each a `List<PickerItem>`), `SetTimeOfDay`, `SetReliability`, `SetWeather(id)`; `Light` (0..1), `IsDark` (use for `LandingVisuals.Theme(night)`), `ConditionsLocked`. |
| units | `Units` (`UnitSystem.Standard` / `Metric`), `UnitItems()` (two `PickerItem`s), `SetUnits(u)`; for labels `MassText(kg)` ("170 lb" / "77 kg"), `MassNumber(kg)`, `MassUnit`, and `MassStepKg` (one − / + tap: 10 lb or 5 kg, in kg). Formatting lives in `DisplayUnits`. |
| summary | `Summary()` → the lines LAUNCH will do. |

Bridge side (`unity/Assets/_Project/Scripts/Bridge/Landing/`):

| Type | Use |
|---|---|
| `LandingSession.Flow` | the one `LandingFlow` instance. |
| `LandingThumbnails.Get(PickerItem)` / `Get(IEnumerable<string> keys)` | `Texture2D` or null (then draw the icon). Captured on device, else bundled. |
| `LandingPreview.Instance.Texture` | the LIVE 3-D aeroplane (RenderTexture) for the hero, every page. `LandingPreview.Instance.Framing` = `Hero` or `Load`. |
| `LandingView` | the flat (iPhone/iPad/Mac) page. `Draw()` is called from `StartMenu.OnGUI`. Codex owns how it looks; the calls into `LandingFlow` are the binding. |
| `QuestMenu` | the Quest panel's landing rows read the same flow. |

## Rules decided in code (tests hold them)

- Families and configurations: `LandingCatalog.Variants` (literal). Floats / amphibian / the PA-18's bushwheels fly their own
  files; any other powered landplane gets generic bushwheels and wheel-skis (the sim's existing flags). Gliders, hang
  glider, paraglider, H-4: one configuration.
- Float gross = 1.2 × the wheel config's placard (W&B warning only; physics untouched).
- Activities restrict: lessons → `LessonTrainers`, standard gear, the lesson's own start; config challenges → their own
  aeroplane and start; aerobox → `Aerobats`; events → powered; scored contests → standard gear + locked conditions.
- Midday = the scene's existing sun, unchanged for everybody who never touches the setting.
- Reliability is STORED (`SessionSettings.EngineUnreliability`) but nothing in the sim reads it yet (Q queue).
- Invite player is a stub: it weighs a default person and is listed in the summary; nothing is sent.
- **Units (Ty 2026-11-05 10:56: "give page two a standard or metric option; most users will be American/standard").**
  Page 2's settings column ends in a UNITS row, **Standard | Metric**; the Quest panel has a `Units:` row. **Default
  Standard** when nothing is saved (or the value is unreadable). ONE preference for the whole game: PlayerPrefs key
  **`landing.units`** = `standard` | `metric` (`DisplayUnits.PrefKey`), written the moment it is changed (not at LAUNCH:
  it is how the game talks to the pilot, not a choice about one flight), and again at LAUNCH. Outside the landing page,
  read it with `LandingSession.SavedUnits`; a HUD or instrument that grows a units choice uses this key, never a second
  setting. There was no units setting anywhere in the game before this.
  Display only: the flow, the store, `SessionSettings` and the sim all stay kg / m / m/s. What it changes: seat and
  baggage weights (lb / kg), the − / + step (10 lb / 5 kg), the W&B heading and over-gross warning, the summary line,
  the Quest W&B line, the location card's elevation (ft / m) and the weather cards' wind (kt / km/h). Fuel is shown as a
  fraction and the CG axis as fwd / aft, so neither has a unit to change. The classic chapters behind "More settings"
  still show kg and L.

## Thumbnails

`ThumbnailKeys`: `ac/<aircraft>/<gear>`, `loc/<aircraft>/<gear>/<airport>/<Viewpoint>`, fallbacks down to
`loc/any/<airport>/InTheAir`. Sources, first found wins: (1) captured on this device under
`persistentDataPath/landing-thumbs/` — the preview captures `ac/…` and the main camera's view of the launch pose
captures `loc/…` whenever the page shows a combination that has no picture yet, and again from the first frame
after LAUNCH (what the pilot really first sees); (2) bundled in `Resources/LandingThumbs/` (made on the bench with
`AERO_SELFTEST=landing-thumbs`, which walks the fleet and every location through the same capture code).

## W&B envelope, labels and the launch question (Ty t880u / t881u / t882u, 2026-11-06)

- **Only verified limits are drawn.** `loading.cgLimits` (rows of `massKg / forwardM / aftM`, body x, +forward; straight
  line between rows, held beyond the end rows) or the constant `cgForwardLimitM / cgAftLimitM`. Each config cites its
  certificate in `_cgLimitsSource`; `cgDatumM` + `hasCgDatum` let the axis read "aft of datum" as the handbook does.
  Done: Pitts S-2B (A8SO, sloped forward limit 1475 → 1625 lb), J-3 (A-691), Great Lakes (A18EA, anchor ±25 mm),
  Swift S-1 (EASA A.038), Grob 103 (POH 2.7). `OnlyTheVerifiedTypesHaveAnEnvelope` pins the list; everything else shows
  `No envelope authored` (and its gross line when it has a gross). Missing types: AGENT-LOG NEEDS YOU.
- **Points:** takeoff (as loaded) and zero fuel (every tank drained), each checked against the limits AT ITS OWN WEIGHT.
- **Labels are factual only:** `Over gross`, `Forward c.g.`, `Aft c.g.`, `Forward c.g. at zero fuel`, `Aft c.g. at zero
  fuel`. No coaxing copy anywhere (a test scans the landing code for "still fly").
- **LAUNCH / FLY out of limits** (`RequestLaunch` → `NeedsConfirmation`): the page shows exactly
  `LandingFlow.WbOutOfLimitsMessage` = "Your W&B is out of limits which will negatively affect aircraft handling and
  performance." with **Go back** (`ConfirmLaunch(false)`: back to the loading, nothing launched or saved) and **Fly**
  (`ConfirmLaunch(true)`: launches as loaded). In limits it launches at once. Flat page: `LandingView.DrawLaunchConfirm`;
  classic FLY routes through the same flow; Quest: confirm rows in `QuestMenu` (B / Y = Go back).
