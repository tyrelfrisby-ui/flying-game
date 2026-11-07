# Flight recorder: recorded ships and multitrack sessions (2026-11-04)

Ty, 20:38: *"Recording" feature. Player records a flight; later it plays back as an AI-free replay airplane in their
own sim ("play xyz"): fly formation with it, or in a combat zone shoot it down, etc. Make it general so it works many
ways.*

Ty, 20:40, the core requirement: *MULTITRACK. A "session/formation" holds several tracks on one shared timeline, like a
solo musician layering tracks. Pilot records track 1 (lead), then records track 2 while track 1 plays back synced from a
common start cue/countdown, and so on to 4-6+ ships (design for N).*

**Status: phase 1, for Ty's review.** The core is built and tested (`src/FlyingGame.Sim/Replay/`, 27 tests in
`tools/FlightTests/FlightRecorderTests.cs`). The Unity side (`Bridge/RecordedShips.cs`) compiles in the iOS build but is
**not attached to any scene, button or menu**. Nothing a pilot can reach has changed. The UI below is a proposal.
Lane `lane/claude-flight-recorder`. It is not merged into either playable lane.

## What already exists, and how this differs from it

| | in-game REPLAY (`FlightRecorder`, 2026-10-01) | this |
|---|---|---|
| what | the last 10 min of YOUR flight, in a ring buffer | named recordings, kept until deleted |
| plays on | your own aeroplane (the world is frozen) | other aeroplanes, flying alongside you while you fly |
| stored | memory only, full doubles (~250 B a frame) | the library, 54 B a frame, the same in memory and on disk |
| ships | one | any number of sessions, each with any number of tracks |

The REPLAY recorder is left exactly as it was. The new code reuses its slerp. A track can later be cut out of the
REPLAY buffer ("save the last 2 minutes"), see open question Q4.

## Words

- **Session**: what the library lists and what a pilot names. A set of tracks on one shared timeline. Time 0 is the
  cue. A single recorded flight is a session with one track.
- **Track**: one ship's flight: aircraft type, livery, smoke colour, where it sits on the timeline (`StartSec`, plus a
  `NudgeSec` the pilot can slide it by), mute / solo, and its frames at a fixed 30 Hz.
- **Take**: one pass at recording a track while the others play. A new ship, or an overdub of an existing one from a
  punch-in point.
- **Playback**: one session being played. It has its own clock, options and placement, and its own ships (actors).
- **Actor**: one recorded ship in the world. It is a real sim `Aircraft`.
- **Director**: everything that is playing. Several playbacks at once.

## Recording

`TrackRecorder` is fed the live aircraft once per display frame, at whatever rate the device runs: 72 Hz on the Quest,
60 or 120 on an iPhone, with the odd hitch. Each grid instant that frame passed (k / 30 s) is filled by interpolating
between this feed and the last one. So the file really is fixed-rate, and its timing does not depend on the device that
flew it. Tested at a jittery 72 Hz and at 13 Hz: each grid frame is within 5 mm and 50 mm of the flight.

Each frame records: position, attitude quaternion, body velocity, body rates, aileron / elevator / rudder (rad),
spoiler / flap / slat (fraction), throttle, gear extension, engine rpm, load factor, local wind (world), and flags
(smoke, pilot out, nav lights, strobes, landing light, guns firing, on ground). Things that happen once are **events**.
A part coming off (`PartLost`, so a recorded crash or shoot-down sheds the same parts at the same moment) and a smoke
colour change are events.

The aircraft id and livery are per track. Wind is recorded per frame, so the playback's relative-wind views and airspeed
can be correct, but it is never pushed back into the world's weather.

### The frame (54 bytes)

Quantised **once, at recording**. A track played back straight after recording and the same track loaded from a file
next week are the same numbers.

| channel | stored as | step | range | why that step |
|---|---|---|---|---|
| position | 3 × float, offset from the track's double origin | 4 mm at 50 km | ±10⁷ m | the island is ~100 km |
| attitude | 4 × int16 | 1/32767 | unit | 0.03 mrad: 0.15 mm at a 5 m tip |
| body velocity | 3 × int16 | 0.02 m/s | ±655 m/s | an F-86 is ~300 m/s |
| body rates | 3 × int16 | 0.001 rad/s | ±32.7 rad/s | a snap roll is ~7 rad/s |
| ail / ele / rud | 3 × int16 | 0.1 mrad | ±3.27 rad | |
| spoiler, flap, slat, throttle, gear | 5 × byte | 1/255 | 0..1 | |
| rpm | uint16 | 1 rpm | 0..65535 | |
| load factor | int16 | 0.001 g | ±32 g | |
| wind | 3 × int16 | 0.01 m/s | ±327 m/s | |
| flags | byte | | 7 bits used | |

`APackedFrameKeepsEveryChannelToItsStep` checks each channel against its step.

### Interpolation

Between frames the position follows a **cubic Hermite** through both ends' world velocities. A 30 Hz straight chord
cuts inside a turn. The Hermite stays on it: under 2 cm in a rolling, diving Cub, which is better than the chord
(`BetweenFramesTheHermiteFollowsATurnTheChordCuts`). Attitude is slerped. Everything else is linear. Flags are stepped
(a frame's flags hold until the next frame).

## The file and the library

`RecordingCodec`, `.fgrec`: little-endian, `BinaryWriter` / `BinaryReader` only. There is no JSON library
(`System.Text.Json` is not in Unity, CLAUDE.md) and no compression stream, so the same bytes are read on iOS, the Mac,
the Quest and in the suite.

```
"FGRC" u16 version
session: id, name, worldId (strings), createdUtc i64, countdownSec f64, collideRecordedShips bool, trackCount i32
per track: name, aircraftId, hasLivery bool [livery], smokeRgb i32, sampleHz f64, origin 3×f64,
           startSec f64, nudgeSec f64, muted bool, solo bool, recordedUtc i64,
           frameCount i32, frames (54 B each), eventCount i32, events (frame i32, kind u8, arg i32)
"END!"
```

A file cut short (full disk, app killed mid-save) is refused, not half-played (`ATruncatedOrForeignFileIsRefused`). Saves
go to `<id>.fgrec.tmp` and are then moved into place. The file name is the session's stable id, so a rename rewrites
only the name inside it. Ids are letters, digits, `-` and `_` only, so no path can ever get into one.

`RecordingLibrary` handles List (newest first, read from headers only, frames skipped), Save, Load, Rename, Delete, and
NextName ("Recording 3"). It lives in `Application.persistentDataPath/Recordings` on every platform. It is never in
StreamingAssets, which is read-only, and a zip on Android.

### Size

1,620 B/s a ship (54 B × 30 Hz). Header is about 100 B a session plus about 80 B a track. In memory it is the same,
because frames stay packed.

| | size |
|---|---|
| 1 minute, 1 ship | 97 kB (`AMinuteOfOneShipIsAboutNinetySevenKilobytes`) |
| 10 minutes, 1 ship | 0.97 MB |
| 1 hour, 1 ship | 5.8 MB |
| 5-minute airshow, 6 ships | 2.9 MB |

Uncompressed on purpose. Deflate would roughly halve it, but `System.IO.Compression` on IL2CPP / Android is one more
thing to prove on three platforms, and 6 MB an hour is nothing on any of them. A version-2 format can add it.

## Playback

`ReplayDirector.Play(session, options, live)` starts a `SessionPlayback`. Any number can play at once, each on its own
clock (`SeveralSessionsPlayAtOnceWithTheirOwnClocks`).

Options (`PlaybackOptions`):
- **Loop.** At the end, go back to the start offset. A ship that was shot down comes back whole (`RespawnOnLoop`, on by
  default).
- **Start offset (s).**
- **Where:** `Original` plays where it was flown. `AtMyPosition` puts the session's lead 150 m (`AheadM`) ahead of you,
  at your height, on your heading. It is a rigid move of the whole session, so a formation keeps its shape
  (`AtMyPositionPutsTheLeadAheadOnMyHeadingAndKeepsTheShape`). Recorded wind turns with it.
- **Targets:** bullets can hit it (on by default).
- **Collide with me:** on by default (see Collisions).

### The actor is a real aeroplane

Each recorded ship is a sim `Aircraft` built from its config. Each frame it is put into its recorded pose with
`Aircraft.SetReplayPose`, the same call the REPLAY uses. So its hit volumes, its damage model (three hits and a part
goes, one hit on a control surface, fuel then fire in the inner panels) and its visual are the same code as a combat
drone's. An actor is **Absent** before its track begins and after it ends. While it follows the recording it is
**Kinematic**.

**Shot down.** Gunnery hits go through the ship's own `DamageState`, exactly as for a drone. A lost control surface or
outer panel is shown (the part comes off), and the ship carries on along its recording. A ship with an inner panel, a
whole wing or the tail boom gone is **crippled**, using the same rule as the drones. It is then released to the physics
(**Wreck**) with the velocity and rates it had at that instant. From there it flies itself: controls free, power off,
stepped at the AI rate (50 Hz). Once it is down it is **Settled**. A settled wreck is no longer stepped, because a
wreck resting on the island costs four times a flying aeroplane (the 2026-10-31 freeze). Tested in
`AShipThatIsShotDownFallsWithItsRecordedMomentum` and `GunneryHitsARecordedShipAndTheDirectorScoresIt`. The score is
per playback: `Hits` and `Kills`.

Gun target ids start at 1000 (the player is 0, drones are 100+, the dogfight opponent is 999).

**Moved somewhere it was not flown.** With `AtMyPosition` the recorded path can pass through a hill. The ship then hits
the hill and becomes a wreck. It does not fly through the island. At `Original` this check is off, because the
recording already flew there (including take-offs and landings, flagged `OnGround`).

**Seeking.** Going back past a recorded part loss rebuilds the ship whole
(`ARecordedCrashShedsItsPartsAndASeekBackPutsThemBack`). The visual is told by `Actor.Generation`.

**An aeroplane this build no longer has** (a renamed config): that ship is left out and the rest still play.

### Determinism notes

- **The recording is the truth. Nothing is re-simulated.** A playback does not replay stick inputs through the physics.
  That would need bit-identical physics across the iPhone, the Mac, the Quest and every future tuning change.
  `Atmosphere` turbulence, the per-strip stall hysteresis, `SimLoop` catch-up caps that depend on frame rate, and every
  config edit would all make it diverge within seconds. State at 30 Hz needs none of that, and it survives a re-tuned
  aeroplane.
- **Quantised once.** Frames are packed when they are recorded, and the playback reads the packed values. So "just
  recorded" and "loaded from disk" are identical (`ASessionFileReadsBackByteForByte` compares the samples between
  frames, not only the bytes). Decoding is double arithmetic on integers and floats. IEEE gives the same answer on
  ARM64 iOS, ARM64 Quest and the Mac.
- **What is not deterministic:** a ship after it is released as a wreck. From that moment it is live physics, like a
  drone, and two playbacks of the same shoot-down will fall differently. That is the point of it.
- **Time.** Session time is advanced by the display's `deltaTime`, and the frames are on an absolute grid. A hitch skips
  ahead; it never stretches the recording. Slow motion (`SimSpeed`) slows the playback with the world.
- **A bench lesson worth keeping:** `SimLoop.RunFor(d)` runs `round(d / 5 ms)` whole steps, so `RunFor(1/60)` runs
  15 ms, not 16.7 ms. The first version of the tests fed the recorder a 1/120 s clock with 10 ms of physics per frame.
  The recorded "velocity" then disagreed with the recorded positions by 20 %, and the Hermite looked worse than a chord.
  The tests now step whole 5 ms steps.

## Multitrack

```
session time  0 ──────────────────────────────────────────────▶
track 1 lead  |███████████████████████████████|
track 2       |████████████████████████|            (flown while 1 played)
track 3            |███████████████████████████|    (punched in at 4 s, nudged −0.2 s)
track 4  muted|██████████████|
```

- **Track 1** has no count-in. It is recorded wherever the pilot is, and it sets where the session is.
- **A new ship** (`RecordingTake.NewTrack`): the session clock is held at the punch-in for the count-in
  (`CountdownSec`, 5 s by default). Every other track waits at its punch-in pose. The live aircraft is put at
  `StartPose`: in a slot on whichever ship is flying at the punch-in. The default slot is echelon right, one span back
  and one span out. On GO the clock runs, the others play, and the live aircraft is recorded on the same clock. In the
  test the second ship starts in its slot, and with the same stick it holds the world offset to within 5 cm over 5 s
  (`LayeringASecondShipStartsItInSlotOnTheLeadAfterTheCountIn`).
- **The others play on the take's clock** (`ReplayDirector.PlayAlong(take)`). That playback has no clock of its own:
  it reads `take.Clock` every frame, so the other ships hold at the punch-in through the count-in and then run with
  the take, frame for frame, whatever the display rate does. This is in the core, not the Bridge, so it is tested
  (`TheOtherShipsPlayOnTheTakesClockThroughTheCountInAndAfterGo`, at a ragged 40-90 Hz). The game ticks the take,
  then the director. When the take ends, `ReleaseTake()` lets the same playback carry on from where the take stopped,
  with the new track in it.
- **Overdub / punch-in** (`RecordingTake.Overdub`): re-fly one track from any point. Everything before the punch-in is
  kept frame for frame. The pilot starts where that track was at the punch-in, and the splice is continuous. During the
  take, the track being re-flown is left out of the playback (`ThePlaybackDuringATakeLeavesOutTheTrackBeingReflown`).
- **Editing while it plays.** The mixer edits a session that may be playing. Mute, solo and nudge are read live. A
  committed take, a deleted track, an added track or a re-typed track bumps `FlightSession.Revision`, and the
  playback then rebuilds just the ships that changed (`SessionPlayback.Refresh`): an overdub's new take replaces the
  old ship, a deleted ship goes (`EditingASessionWhileItPlaysAddsAndDropsItsShips`).
- **Nothing changes until Commit.** A take that is cancelled, or stopped during its count-in, leaves the session byte
  for byte as it was (`AnOverdubPunchInKeepsWhatCameBeforeAndACancelChangesNothing`,
  `ATakeStoppedInItsCountInRecordsNothing`).
- **Per track:** aircraft type, livery, smoke colour (plus colour-change events), name, mute, solo, nudge, re-record
  (overdub), delete. Mute and solo work like a mixing desk: with any track soloed, only soloed tracks play
  (`MuteAndSoloWorkLikeAMixingDesk`). The nudge slides a track against the others without re-flying it.
- **Join as a live ship:** play the session, and `SessionPlayback.JoinPose(slot)` gives the world pose for flying a
  slot that was never recorded, on the ship leading at the play head, with the playback's placement applied (so an
  "ahead of me" session is joined where it is drawn). The player is then simply one more ship, and collides with
  the recorded ones (`AJoiningPilotStartsInASlotWhereTheSessionIsDrawn`). Joining with a count-in is a new-ship take
  that is discarded at the end; KEEP instead adds the pilot as the next track.
- **Spawn alignment:** a session plays at its recorded place and time base (`Original`): time 0 is the cue, every
  track's frames sit on that one absolute grid, and the world positions are the recorded ones. It can also be moved
  as a whole (`AtMyPosition`). The game has no time of day yet; when it gets one, the session header should record
  it (a version-2 field) so a sunset show plays at sunset.
- **N ships:** nothing has a fixed count. The file caps a session at 64 tracks as a sanity check on reading. Six ships
  layered one take at a time (each take flown while all the earlier ones play) keep their slots to within 5 cm after
  a save and load (`SixShipsLayeredOneAtATimePlayTogetherInTheirSlots`).

## Collisions

`ReplayDirector.Collisions(live)` tests the live aircraft's contact points (the same hard points that hit the ground)
against each recorded ship's hit volumes, and with `CollideRecordedShips` on, it also tests recorded ships against each
other.

- **Recorded with recorded: off by default.** The tracks were flown separately, so two ships that overlap by a wingspan
  in the playback never really touched.
- **The live ship: on by default.**

Phase 1 only **detects** collisions, and the test checks that (`TheLiveShipCollidesWithRecordedShipsButRecordedShipsDoNotByDefault`).
What a collision should cost is Ty's call (Q3 below). The proposal: the recorded ship loses the part that was struck
and becomes a wreck. The live aircraft takes the same contact damage it would take from a solid object at that closing
speed, through the existing `AircraftContactDamage` path.

## Smoke in the in-game REPLAY (Ty t912u, 2026-11-06)

Ty flew with smoke on; the replay showed none, and a clip recorded from the replay showed none. Two causes, both in the
REPLAY path (not the recorded-ships path below): `FlightRecorder`'s frames carried no smoke at all, and
`SmokeController.LateUpdate` returned the moment a replay began, under a comment saying "FlightReplay redraws" — it
never did. The trail on screen in a replay was simply not built.

- **Recorded:** every replay frame carries `ReplaySmoke`: the switch (`On`), the flow the oil made or would have made
  with the switch on (`FlowIfOn`: 0 with the engine stopped or the bus dead, `0.25 + 0.75 × power` otherwise, 1 for a
  glider's canisters — the live law, now `ReplaySmoke.FlowIfOnNow` in Sim and used by both), the jet's exit speed, the
  air density, the crank and the rpm (the pulse tubes are timed by them). Between frames the switch steps, the rest is
  linear.
- **Shown:** in a replay `SmokeController` rebuilds the trail at the play head from the recording
  (`ReplaySmokeView.Window`: the head's own interpolated frame, then 90 s of frames before it, newest first) in the mode
  the replay bar's **SMOKE** button shows: **AS FLOWN** (every replay opens on it: the switch's own timing), **ON** (add
  smoke after the fact, with the flow the engine really had at each moment), **OFF**. The window is a pure function of
  (recording, head, mode), so a seek, a rewind, a scrub or a mode change draws exactly that moment's trail: nothing is
  carried over. (`SmokeTrail` caches each puff's path; a cached path that had been collapsed to its last step is
  recomputed if the head goes back over it, so no puff is left stuck where its pass ended.) The live ring is not
  touched, so the flight's own trail carries on when the replay ends.
- **Never changes the recording:** the mode is a view setting on `FlightReplay`; frames are never rewritten
  (`ReplaySmokeTests.AModeNeverChangesTheRecording`).
- **Older recordings:** a frame without the channels (`ReplaySmoke.Recorded == false`: none exist on disk, because the
  REPLAY is memory only, but a recording that outlived the change in memory would have them) shows no smoke AS FLOWN —
  that is all it knows — and the replay bar says `AS FLOWN (none)`; ON estimates the flow from the recorded throttle
  (gliders full), with no jet or pulse tubes. Nothing throws, mixed recordings keep each frame's own.
- **Clips / export:** `ClipRecorder` renders a copy of the main camera (`CopyFrom`), and the Quest recorder renders
  the main camera's culling mask less the overlay layer; the smoke is one world-space mesh on the aircraft's layer, so
  a clip made from the replay records the same trail, in the same mode, as the screen (`SmokeAirframeRender` compares a
  CopyFrom camera with the main one pixel for pixel).
- **Quest:** the same code. The Quest has no REPLAY page today, so it has no SMOKE button either.
- Tests: `ReplaySmokeTests` (default, overrides, seek/rewind, telemetry untouched, old data, the flow law).

## Smoke, lights, surfaces

- **Surfaces, gear, prop:** `RecordedShips` sets the airframe builder's deflections, gear extension and prop rpm from
  the frame each frame. Parts lost (recorded, or shot off) come off through the existing `AirframeVisual.DetachStatic`,
  and `DamageFx` draws the fuel leak and fire.
- **Smoke:** the on/off flag and the colour are recorded. The trail is not drawn yet. `SmokeTrail.Build` takes a sample
  function over time, and a track is exactly such a function, so a recorded ship's trail can be rebuilt from its own
  recording at any play head (even after a seek). That is phase 2.
- **Lights:** the game has no nav, strobe or landing lights yet. The flags are reserved, so recordings made now will
  carry them once lights exist.
- **Livery:** recorded per track. Phase 1 draws the airframe builder's procedural model, as the LAN's remote aircraft
  do. Placing the livery's own GLB (`AirframeModels.PickOwn`) is phase 2.

## Parity

Everything is in `src/` (shared) and one shared Bridge file. There is no `#if` and no platform folder: iOS, the Mac and
the Quest run the same code. Two platform facts are designed in rather than patched later:
- **Files:** `persistentDataPath`, read with `File`, never StreamingAssets (a zip on Android; CLAUDE.md).
- **Menus:** every menu in this game is IMGUI, and IMGUI does not exist inside a headset (CLAUDE.md). So the recorder's
  pages need a QuestMenu (world-space) counterpart from day one (below).

## UI placement proposal (for Ty)

No Glass Overlay anywhere. It uses the existing toolbar, the existing HUD status line, the Flight Setup page and its
QuestMenu twin.

**The name.** The toolbar already has **REC** / **STOP REC**. That is the *video* clip recorder (`ClipRecorder`). Two
"REC"s would be confusing, so this proposal calls the feature **FLIGHTS**: a recorded flight, played back as ships.
Alternatives: TRACKS, SESSIONS, GHOSTS.

1. **In flight, toolbar (ViewPanel row):** one new button, **FLIGHTS**.
   - Not recording: a small sheet opens above it: **RECORD NEW** · **ADD SHIP TO…** (choose a session) · **PLAY…** ·
     **STOP ALL** (when something is playing).
   - While recording, the button reads **■ STOP 01:23** in the red `_btnRec` style the clip REC already uses. Stop asks
     **KEEP / DISCARD**. Keep saves the session as "Recording n", and it can be renamed in the library.
   - The count-in is large digits **5 4 3 2 1 GO** in the centre of the screen, the same treatment as the challenge
     start. The other ships are visible and holding at their punch-in poses.
   - While playing: one line in the existing HUD status line (where the combat line sits), e.g.
     `FLIGHTS  Diamond practice 01:12 / 03:40  ships 4  hits 3  kills 1`.
2. **Landing page (Flight Setup), a new section:** **RECORDINGS**, between START and AIRPORT. It is a list: name,
   aircraft, ships, length, date. Per row: **PLAY**, **ADD SHIP**, **RENAME**, **DELETE** (asks first), and **OPEN**,
   which goes to the session page.
3. **Session page (the "mixer"):** one row per track: ship n · aircraft · livery · smoke swatch · **M** · **S** ·
   nudge **−0.1 / +0.1 s** · **RE-FLY FROM…** (punch-in from the timeline marker) · **DELETE**. Under the rows: a
   timeline with a play head and a punch-in marker, **ADD SHIP**, and the play options (**LOOP**, **START AT** slider,
   **WHERE: ORIGINAL / AHEAD OF ME**, **TARGETS**, **RECORDED SHIPS COLLIDE**).
4. **Quest:** the same three pages as QuestMenu tabs (FLIGHTS). The in-flight start/stop is on the ☰ menu. There is no
   new controller binding, because everything the pilot needs in VR is reachable from the menu tab he already uses.

Pages 2 and 3 are menu pages, where the sim is paused anyway. Page 1 is the only in-flight UI.

## Not done in phase 1 (and why)

- **No UI, and `RecordedShips` is not attached anywhere.** This is waiting on Ty's review of the design and the UI.
- **The combat hook is not wired.** It is two lines in `CombatController`: add `RecordedShips.GunTargets()` to
  `_stepTargets`, and pass hits ≥ 1000 to `OnHit`. Held back because `CombatController` is shared playable code.
- **Count-in placement of the live aircraft** (teleport to `StartPose` and hold, the way an air start does) needs a
  FlightSimDriver entry point. It is not written yet.
- Smoke trail rendering, own-GLB liveries, the collision response, and the engine sound of recorded ships.
- Not run in the Unity editor or on a device. Compiled only: iOS `deploy.sh` build, see AGENT-LOG.

## Questions for Ty (queued as Q036-Q040 in ~/Developer/agent-lanes/QUESTIONS-FOR-TY.md)

1. The name: FLIGHTS, TRACKS, SESSIONS, or GHOSTS? ("REC" is taken by the video recorder.)
2. Are the UI placements above right: a toolbar button in flight, a RECORDINGS section on the landing page, a
   session/mixer page, and a QuestMenu tab?
3. Collisions with a recorded ship: (a) both take real contact damage (the proposal); (b) only the recorded ship
   breaks; (c) none, just a warning.
4. Also allow "save the last N minutes of REPLAY as a recording", so you can keep a flight you didn't think to record?
5. Default count-in 5 s, and default new slot echelon right, one span back and one span out: OK?
