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
| `LandingFlow` | THE state machine. `Page` (`Choose`/`Load`), `OpenPicker` (`PickerKind`), `Version` (bumps on every change; redraw/refresh on change), `Changed` event. Navigation: `Next()`, `Back()` (closes an open menu first), `GoTo(page)`, `Open(PickerKind)` (toggles), `ClosePicker()`, `CanGoNext/CanGoBack/CanLaunch`, `Launch()`. |
| page 1 | `Items(PickerKind)` → `List<PickerItem>` for Aircraft / Configuration / Location / Viewpoint / Challenge / Lesson (already RESTRICTED by the activity's rules; disallowed cards are not in the list). `Pick(kind, id)` (closes the menu). `IsFixed(kind)`: a menu with nothing to choose (draw a fixed line). Hero: `HeroTitle`, `HeroSubtitle`, `HeroThumbnails`; current choice: `Family`, `Gear`, `Location`, `Viewpoint`, `ViewpointName`, `Activity`, `ActivityKind`, `Rules` (`AircraftNote`, `FixedViewpointNote`, `ConditionsLocked`). |
| `PickerItem` | `Kind, Id, Title, Subtitle, Selected, Enabled, DisabledReason, Thumbnails` (keys, most specific first), `IconKey` (`icon/<group>/<id>`, for the visual lane's drawn icons), `ThumbnailKey`. |
| page 2 seats | `Seats` → `SeatRow { SeatId, Label, HasControls, Occupant (Empty/Me/DefaultPerson/InvitedPlayer), Role (Passenger/DualControl), Kg, MaxKg, InviteName, IconKey }`. `CycleSeat(id)` (one tap: empty → person → invited passenger → invited on the controls → empty), `SetSeat(id, who, role)`, `SetSeatKg(id, kg)`, `SetInviteName(name)`. Exactly one seat is `Me`. |
| page 2 load | `Stations` → `StationRow { StationId, Label, Kind (Fuel/Baggage), Kg, MaxKg, Fraction }`, `SetStationKg`, `SetFuelFraction(0..1)`, `FuelFraction`. |
| W&B | `WeightBalance` → `{ TotalKg, MaxGrossKg, HasGross, HasEnvelope, PointX/PointY, ZeroFuelX/ZeroFuelY, Envelope (closed polyline), Warnings, InLimits, CanLaunch (always true) }`. Plot space 0..1, x = forward limit → aft limit (nose left), y = empty → 110 % gross. |
| settings | `TimeItems()`, `ReliabilityItems()`, `WeatherItems()` (each a `List<PickerItem>`), `SetTimeOfDay`, `SetReliability`, `SetWeather(id)`; `Light` (0..1), `IsDark` (use for `LandingVisuals.Theme(night)`), `ConditionsLocked`. |
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

## Thumbnails

`ThumbnailKeys`: `ac/<aircraft>/<gear>`, `loc/<aircraft>/<gear>/<airport>/<Viewpoint>`, fallbacks down to
`loc/any/<airport>/InTheAir`. Sources, first found wins: (1) captured on this device under
`persistentDataPath/landing-thumbs/` — the preview captures `ac/…` and the main camera's view of the launch pose
captures `loc/…` whenever the page shows a combination that has no picture yet, and again from the first frame
after LAUNCH (what the pilot really first sees); (2) bundled in `Resources/LandingThumbs/` (made on the bench with
`AERO_SELFTEST=landing-thumbs`, which walks the fleet and every location through the same capture code).
