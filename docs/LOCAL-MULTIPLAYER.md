# Local multiplayer (Ty 2026-11-05)

Ty: "Yes start local multi player on same device for iOS and quest", then (10:57–10:59) the authoritative split:

| Mode | Where | What |
|---|---|---|
| **1. Same-device split screen** | iPhone, iPad, Mac (flat screens only) | One device, two gamepads (8BitDo), two aeroplanes, one view each |
| **2. LAN full view** | phone + iPad + Quest + Mac, any mix | Each device flies its own aeroplane full screen / full stereo; all on one Wi-Fi |

There is **no** split view inside a headset and no mirrored TV as a second player. Two Quest players fly two headsets
in Mode 2.

## The world clock (both modes, and a bug fixed for single player)

`Atmosphere.SimTimeSec` drives the turbulence field, the tide (`WaterCurrent`), thermals and every clocked effect. Every
`Aircraft.Step` used to add its own dt, so the clock ran once per aircraft: the player plus three combat drones measured
**3.995 s per real second**; fifteen drones, sixteen times. With `Atmosphere.ClockOwner` set (FlightSimDriver sets it to
player 1's aeroplane on every new Aircraft) only the owner's steps advance it: P1 + P2 + three AI = **0.995 s** per
second (`LocalMultiplayerTests`). With no owner (headless tools, tests, the Aero Widget) the old rule stands. A single
player with nothing else flying is bit-for-bit the same flight with or without the owner
(`OnePlayerFliesBitForBitTheSameWithTheOwnerSet`). The tow tug, combat drones, formations and the dogfight opponent no
longer speed the world up.

## Mode 1: split screen

**Turn on:** OPTIONS › Controls › "Local 2-player (split screen)". Saved, **off by default**, takes effect on the next
FLY / restart. Not offered in a headset. Free flight and the combat-zone start only, multiplayer Solo; otherwise the
option shows why the second player did not start (`LocalSplit.NotStartedReason`) and the game flies one player.

**Off means off:** no second root, no second camera, no `LocalPlayerView` anywhere, `GamepadInput.Now` is the
single-player read, `ChaseCamera` uses `ScreenLayout.CameraViewport`, every per-player lookup falls back to the
single-player value (`ChaseCamera.InCockpit`, `Camera.main`'s chase).

**What P2 gets** (`LocalCoop`): a full aeroplane of the same type (`FlightSimDriver` with `PlayerIndex = 1`,
`AirframeVisual`, `GroundShadow`, `StructuralDamage`, `CockpitView`, `SmokeController`, `FlightAudio`) flown by
`LocalPadControls` (Core `LocalPadPilot`, the same shaping, rates and trim as P1's pad path); a camera of its own with
its own views (Y), the green HUD on its half when instruments are HUD, and a status line (`LocalPlayerHud`).

**Start:** air start abreast on P1's right, 30 m tip to tip (centre spacing = span + 30), same heading, height, speed and
trim. Runway / water start: in line AHEAD on the centreline, 60 m + a length (`FormationSlot`).

**Pads** (`GamepadInput.For(i)`, Core `PadBinding`, by Input System device id): first pad = P1, second = P2. Sticky
while connected: pad 2 never takes P1's aeroplane, whoever pressed a button last. A pad that drops out frees its seat
and gets it back when the same device returns; a new pad takes an empty seat. **One pad:** P1 has it (and the touch
controls); P2 is PARKED: hands off on its trimmed start, flying on level until a second pad is paired, which takes the
seat on its first frame. Logged as `[Gamepad] split screen: P1 …, P2 …`.

**Screen:** landscape side by side (P1 left, P2 right), portrait P1 top, P2 bottom, 0.4 % gap, black backdrop camera
behind (`SplitLayout`).

**Stays P1's:** world clock, touch controls, menus (P2's Menu button opens the same OPTIONS, which pauses both), the
main camera and everything that asks for it (clouds' whiteout, the clip recorder, smoke billboards), the one
`AudioListener`, combat guns and score, the network session.

**Audio:** one listener (P1's camera). Both `FlightAudio`s are 2-D synths heard together, each at `MixGain = 0.72`
(−2.9 dB) while split, 1.0 otherwise.

**Perf:** `WaterReflection.SuppressedForSplit` turns the planar reflection off while split (it is one texture mirrored
from P1's camera and read in screen space; doubled it is also the most expensive thing on the GPU). Every 5 s the log has
`[Perf] split 2P: <fps>, frame mean <ms> max <ms>, sim (both drivers) <ms>/frame, <w>x<h>, reflection off`.

**Crash view:** none (removed for everyone 2026-11-05, Ty pt813u): each camera follows its own aeroplane's fuselage
whatever breaks off, so the other player's wreck can never swing this player's view.

### Per-player HUD API (for `codex-local-mp-hud`, split screen only)

A component on a player's camera asks `LocalPlayerView.Of(this)`; **null = single player, draw exactly as always.**

| Member | Meaning |
|---|---|
| `LocalSplit.Active` | the screen is split now |
| `LocalSplit.Players` | 1 or 2 |
| `LocalSplit.ViewportFor(i)` / `GuiRectFor(i)` | player i's `Camera.rect` / OnGUI pixel rect (top-left origin) |
| `LocalPlayerView.All` | the views in player order (empty when not split) |
| `view.PlayerIndex` | 0 = P1 (left / top), 1 = P2 (right / bottom) |
| `view.Driver` | that player's `FlightSimDriver` (IasMs, AltitudeM, Sim.Aircraft …) |
| `view.Camera`, `view.Chase` | that player's camera and `ChaseCamera` |
| `view.GuiRect` | draw OnGUI inside this, never against `Screen.width` |
| `view.InCockpit` | that camera's view (the static `ChaseCamera.InCockpit` is P1's) |
| `LocalPlayerView.ForDriver(d)`, `InCockpitFor(d)`, `ChaseFor(d)` | lookups by aeroplane, with single-player fallbacks |
| `GamepadInput.For(i)`, `GamepadInput.NameFor(i)` | player i's pad this frame / its name (null = none) |
| `FlightSimDriver.PlayerIndex` | 0 / 1 on the aeroplane itself |

GL drawn in `OnPostRender` is already clipped to its camera's rect (`HudOverlay` works per camera as it is). P1's
full-screen OnGUI (analog dials, FlightHud, combat HUD, AERO) stands down in the compact split (Codex,
`LocalPlayerHud.CompactActive`, iOS); `LocalPlayerHud` draws each player's instruments in its own half (visuals approved,
Ty 2026-11-05 pt818u).

### Touch tray and the per-player RESET / BAIL / FIRE (2026-11-06)

The single-player touch tray is laid out across the whole screen, so under a split its RESET, BAIL and FIRE sat on P2's
half; hiding only the drawing would have left them there, invisible, for P2's thumb to press for P1. Core
`SplitControls` decides, `TouchFlightControls.TrayLive` applies it, one flag for the drawing, the touch loop and the hit
test (`IsControlAt`, which CockpitTaps also asks):

| State | Tray | P1 fires | RESET / handle |
|---|---|---|---|
| not split | as always | tray FIRE, A, space | tray, R / E keys |
| compact split, P1 on a pad | **off** (no drawing, no hit areas) | A (P1's pad only) | FLIGHT panel |
| compact split, no pad at all | as single player (else nobody flies P1; Q114 open) | tray FIRE | tray and FLIGHT panel |

- **FLIGHT panel** (`ViewPanel`, compact split only): the toolbar row (VIEW, REPLAY, CLIP, REC, AERO, TIME, CHART) is
  replaced by one FLIGHT button on the seam (landscape: middle of the seam; portrait: left edge, level with P1's middle),
  clear of the compact HUD's rows. It opens one column per player: **P1 RESET** / **P2 RESET**, **P1 BAIL OUT** (or
  EJECT / DEPLOY BRS, whatever P1's handle is), **VIEW: <name>** for that player's camera, CLOSE. MENU and OPTIONS stay.
- **RESET is per player.** P1's own RESET (tray, R, joystick, FLIGHT) is marked (`LocalCoop.MarkP1OwnReset`) and no
  longer restarts P2, which it used to (every P1 AircraftChanged restarted the pair). FLY / restart from the menus still
  starts both. P2's RESET (`LocalCoop.ResetP2`) puts P2 back in its slot beside the session start; P1 is not touched.
- **BAIL is P1's only.** P2's aeroplane has no PilotEgress yet; its column offers no handle (P1's handle in P2's column
  would be the cross-player press this exists to stop).
- **FIRE:** P1's guns fire from P1's pad (`GamepadInput.Now` is seat 0 while split); the legacy joystick buttons only count
  with no P1 pad, so P2's A never fires P1. P2 has no guns yet.
- **Cockpit glass taps** (`CockpitTaps`): each aeroplane's through its own camera and only on its own half.
- Logged: `[Touch] split screen, P1 on a pad: tray off ...`, `[LocalMP] P1 reset: P1's flight only, P2 flies on`,
  `[LocalMP] P2 reset: P2's flight only`.

## Mode 2: LAN, full view on every device

One networking core for every platform, the one already in the game: `NetSession` → `LanLink` → Core `LanMesh` (UDP, no
server, no host, no internet; unicast sweep of the subnet + gossip; never billed and never gated by the online pass).
Each device flies its own `Aircraft` full screen / full stereo; the others are `RemoteAircraft`.

- **Join:** flat screens: landing page › Multiplayer › "Same network" (already there). Quest: VR menu ›
  "Multiplayer: Solo / Same network (Wi-Fi)" (new), and in flight a status row ("Same network · 2 pilots"). Takes effect
  on FLY / restart; saved.
- **Quest manifest:** the Quest build used to strip the INTERNET permission because nothing in the headset used the
  network; every Android socket needs it, so it is now kept (`QuestBuild`, `OpenXRPackageSettings.asset`).
- **Spawn spread** (Core `SpawnSpread`): everyone spawns at the session's start, the same point for the same field and
  start, so the second pilot used to appear inside the first. Each state's flags now carry the pilot's flight age
  (`NetFlags.a`, tenths of a second; older builds send none and the relay forwards `f` whole). In the first 4 s after
  the first other pilot is seen (if that is within 15 s of FLY), a pilot within span + 15 m of somebody moves itself
  ONCE to the first free formation slot (abreast right in the air, in line ahead on a runway or the water): the newcomer
  moves when the other has been flying longer (or sends no age); when both are new the one whose id sorts later moves,
  so the two devices agree without talking. Logged `[Net] spawn spread: …`.
- **Names:** an unnamed pilot (always so in a headset) is "Pilot XXXX" from the device id, so two headsets are told
  apart.

## Not done / not verified

- Nothing here has run on a device. Split screen and the Quest LAN join have not been seen by anyone.
- P2 has no guns, no score, no bail-out, no BRS, no gear button. The FLIGHT panel and the tray-off state have not been
  seen on a device.
- No wake vortex between aircraft (that is lane/claude-wake-vortex, not merged; nothing here makes it harder).
- Mixed LAN (phone + iPad + Quest + Mac) uses the existing mesh unchanged; it has been tested on the bench
  (`LanMeshTests`) but never with a Quest in the room.
