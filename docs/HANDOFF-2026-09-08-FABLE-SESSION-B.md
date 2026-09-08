# Handoff — flying-game work done in a SECOND session on 2026-09-08

A parallel Claude session (the Glass Overlay one) built the items below in this tree. All of it is
already committed (swept into commits `ced59d8`, `0a9976b`, `e9dc65b`) and the build on the phone
(16:15) contains everything. Sim tests: 133/133. Owner has not yet done eyes-on for most of it.

## 1. Portrait mode
- `Bridge/ScreenLayout.cs` splits the screen: in portrait a bottom control TRAY (pads in the bottom corners,
  trim slider between them, button row Reset·Aircraft·Flaps·Tow, second short row BAIL OUT·EJECT) and the
  3D view + HUD above it. `TouchFlightControls.LayOutPortrait`, `ChaseCamera` sets `cam.rect` from
  `ScreenLayout.CameraViewport` every frame, `HudOverlay` is VIEWPORT-relative (WorldToViewportPoint ×
  pixelWidth/Height; labels add `cam.pixelRect`). ⚠️ New HUD symbology must use viewport px, not Screen.*.
- `BuildScript.ConfigureiOS` + ProjectSettings: portrait allowed (upside-down off).

## 2. Sound (fully procedural, no assets) — `Bridge/Audio/*`
Dsp / EngineProfiles / EngineSynth / WindSynth / OneShotSynth / FlightAudioOutput / FlightAudio.
OnAudioFilterRead on a silent-clip child source; AudioListener added to the camera in SceneBootstrap.
Wind from IAS (+buffet near stall, spoiler roar on the glider). Engines per family: Lycoming flat-4/6,
R-670 7-cyl, R-1830 14-cyl (DC-3 ×2 detuned), MERLIN V-12 (`EngineProfiles.MerlinV12()`: f0 = rpm/10,
harmonics, formants 340/1150 Hz, supercharger ×7 crank × 8 vanes, 4-blade @ 0.479), J47 jet, 737 fans.
Mix knobs `FlightAudio.EngineMix/WindMix/FxMix`; `MasterVolume` in PlayerPrefs. One-shots:
StructuralGroan(sev), WingFailure, CanopyJettison, EjectionSeat, ChuteDeploy, ChuteInflate.
Shared telemetry added for it: `Aircraft.LoadFactorZ/Throttle01/EngineRpm`,
`FlightSimDriver.LoadFactorG/Throttle01/EngineRpm/Powered/IsJet/OnGround/AirVelocityUnity`.

## 3. Real g limits + wing failure
`Aircraft.Structure` (StructuralState): limit = `Limits.GMax/GMin`, ultimate = × `Limits.UltimateFactor`
(1.5). Severity 0..1 between limit and ultimate → `StructuralDamage` (Bridge) raises Groan + audio.
≥ 0.05 s over ultimate → `FailWings()`: AeroModel `surfaceMask` drops every surface whose id contains
"wing" (biplanes: both), mass ×0.76, Ixx ×0.30/Iyy ×0.90/Izz ×0.60. Fuselage, tail, gear, hydro and
propulsion keep working. `AirframeVisual.DetachWings` → `WingDebris`. Tests `StructuralTests.cs` (21).
⚠️ `LoadFactorZ` includes GEAR forces: a hard landing groans / a crash snaps wings
(knob: `StructuralState.FailureDwellSec`). (Your later AirframeContact per-strip breakage sits on top.)

## 4. Bail out / eject / parachute — `Bridge/Pilot/*`
PilotEgress (public API fixed: Phase, PilotOut, PilotTransform, events, BailOut/Eject/ResetToCockpit),
EgressAir, DebrisBody, CanopyJettison, Parachute, PilotBody, PilotFigure.
- BAIL OUT: t=0 start, 3 s canopy jettison (shell leaves up+aft), 5 s pilot over the RIGHT side, +3 s chute.
- EJECT: HOLD 0.4 s button; seat rocket 12 g 0.25 s then 4 g to 0.5 s, seat separates at 1 s, chute at 1.5 s.
- Chute: line stretch 0.6 s, inflation 1.8–2.5 s (Knacke 8·D0/v) with 1.3× opening shock for 0.3 s,
  CdS 42.6 m² → 6.2 m/s sink (C-9), 3 m/s drive, steer ≤ 30°/s with AILERON (right pad relabels CHUTE L/R).
- Camera: `ChaseCamera.OverrideTarget/OverrideVelocity` follow the pilot; abandoned aircraft flies
  hands-off (neutral + trim + last throttle). Reset returns to the cockpit.
- Pilot figure: 21-part articulated humanoid (2-bone IK arms); free-fall box pose → hanging with both hands
  at the toggles (0.8 s blend at inflation); LEFT stick pulls the LEFT arm down 0.40 m, right the right
  (0.15 s lag; if reversed flip `wantL/wantR` in `PilotFigure.Tick`); standing when landed.
- Canopy: 28 gores alternating WHITE/RED (two sub-meshes, two Unlit/Color materials), fluted skirt.

## 5. Multiplayer — `server/relay/` + `Bridge/Net/*`
Cloudflare Worker + Durable Object `Room` (WebSocket hibernation). `POST /rooms` → 6-char code;
`ffa` room cap 200, private cap 16; `GET /rooms/{code}/ws?name=&ac=`; 10 Hz JSON state fan-out
(`{t:"s", p,q,v,d,f:{w,o}}`), hello/peer/bye. `npm test` (wrangler dev + node client) 10/10 PASS.
⚠️ NOT DEPLOYED — owner runs `cd server/relay && npx wrangler login && npx wrangler deploy`
→ `wss://flyinggame-relay.tyrel-frisby.workers.dev` (= `SessionSettings.RelayUrl`; editor default
ws://localhost:8787). Unity: RelayClient (ClientWebSocket, bg task → main-thread queue), NetSession
(on the aircraft root; joins in StartMenu.Fly, leaves in Open), RemoteAircraft (AirframeBuilder visual,
150 ms interpolation, ≤ 1 s dead-reckoning, name tag). StartMenu MULTIPLAYER section (Solo / FFA /
Private + pilot name + room code, PlayerPrefs). Challenges/events force Solo. FlightHud shows the room line.
Untested on hardware: ClientWebSocket TLS under IL2CPP.

## 6. Chase camera crab fix
Your default wind (5 m/s from 090) made the ground-track chase cam show a permanent right "yaw"
(crab). Camera now follows `FlightSimDriver.AirVelocityUnity` (ground velocity − `Atmosphere.WindAtPosition`);
HUD flight-path marker stays inertial.

## 7. Plunge waterfalls at the three canyon walls
Owner: clean lip, concave cliff behind, 150 ft flyable slot between water and rock.
- `WorldTerrain.Waterfalls[3]` (Step, X, LipY, UpperM, LowerM; fall 0 at sim (1849, −2002)).
  `FallRecessM` 60 (≈197 ft at centre, ≥150 ft across the river; plan (1−u²)^0.25), `FallNotchHalfSpanM` 300
  amphitheater notch (vertical wall; `NotchWeight` blend band to 1.5×), `BaseHeightAt(x,y,shelfTop)` —
  shelfTop=true is the un-recessed surface the rock SHELF carries (river table uses it so the surface drops
  AT the lip), `RiverSurfaceAt` snaps clear of the lip cell, airport-pad blend skipped inside a notch
  (Bench pad corner overhangs fall 0 → carried by the shelf), `RegisterWaterfallSolids()` (WorldSolids box =
  shelf; called in `WorldBuilder.BuildAll` after `Landmarks.RegisterSolids`). `FallShelfBottomM(f)`.
- WorldBuilder: fine 6 m terrain patches replace the 60 m cells around each fall (`FinePatchCells`,
  `TerrainColor` helper), shelf slab mesh, parabolic CURTAIN ×2 sheets (`FallLipSpeedMs` 4 → ~54 m arc over
  the ~880 m drop) with the new `FlyingGame/Waterfall` shader (streaks scroll faster with distance
  fallen, whitening; `_Mist=1` mode for 3 crossed mist quads at the pool), river ribbon breaks at drops
  > 100 m. Shader added to `EnsureAlwaysIncludedShaders`.
- Tests `WaterfallTests.cs` (5). `WorldRender` got views waterfall-front / -slot / -lip → build/world/*.png.

## Device build recipe used (Unity closed, phone on USB)
```
rm -f unity/Temp/UnityLockfile
FLYINGGAME_IOS_OUT=/private/tmp/flyinggame-ios /Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -quit -nographics -projectPath "$PWD/unity" -buildTarget iOS \
  -executeMethod FlyingGame.EditorTools.BuildScript.BuildiOS -logFile "$PWD/build/ios.log"
cd /private/tmp/flyinggame-ios && xattr -cr . && xcodebuild -project Unity-iPhone.xcodeproj -scheme Unity-iPhone \
  -configuration Release -destination 'generic/platform=iOS' -allowProvisioningUpdates \
  -derivedDataPath /private/tmp/flyinggame-dd DEVELOPMENT_TEAM=DH425V439F build
xcrun devicectl device install app --device 757C3F5F-062C-5070-9AF1-60E428B8E9C7 /private/tmp/flyinggame-dd/Build/Products/Release-iphoneos/FlyingGame.app
xcrun devicectl device process launch --device 757C3F5F-062C-5070-9AF1-60E428B8E9C7 com.flyinggame.dev
```
(devicectl wants the coredevice UUID above, not the ECID-style UDID, and the phone must be on USB.)

## Owner eyes-on still open
Sound + Merlin taste; landing groans not over-eager; wings off past ultimate with control retained;
bail/eject timings + camera + arm pull direction + red/white canopy; waterfall slot flyable from the
valley side; two-device FFA after the relay deploy.
