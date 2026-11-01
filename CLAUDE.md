# CLAUDE.md

Working notes for agents on this repo. `AGENT-LOG.md` is the running log and carries **NEEDS YOU** at the top;
this file is the standing guidance that does not belong to any one lane.

## How work lands here

- One `lane/<agent>-<topic>` branch per piece of work, in its own worktree under
  `~/Developer/flying-game-worktrees/`. **Local commits only — never push.**
- `git ls-files | git check-ignore --no-index --stdin -v` must print **nothing** before every commit.
  Never `git add -f`, above all under `unity/Assets/StreamingAssets/`.
- Anything in `src/` or `configs/` needs `zsh tools/deploy/deploy.sh` to reach `** BUILD SUCCEEDED **` before it
  is merged. Do not commit the `ProjectSettings` `bundleVersion` bump the build makes.
- Never mark a gate approved. If a checker is red, say so and say why.
- Finished lanes merge into `lane/claude-island-nav`.

## Lessons learned

Append to this list whenever something here costs a rework. Each entry: what went wrong, why, what to do instead.

### A green test suite is not a build
`tools/FlightTests` is .NET 8 and compiles `src/` only. Everything under `unity/Assets/_Project/Scripts/` is
compiled only by `tools/deploy/deploy.sh` or a Unity batch run. A change that is green in the suite can break the
iOS build outright (`init` passed 776 tests and did exactly that). Run a Unity batch with 0 `error CS` before
calling Unity-side work done.

### Removing a glTF node: drop the references, never shift them
Deleting node *k* and then renumbering every index with `i - (i > k)` leaves any reference that WAS *k* pointing
at whatever slid into that slot. The file still parses, every index is in range, and the only symptom is that one
node now has two parents. glTFast rejects such a file **without an error in the log**, and
`AirframeBuilder.TryPlaceModel` quietly falls back to the downloaded model — so the aeroplane still appears, its
own rig does not bind, the hinge carve runs on geometry it was never meant to carve, and the only visible clue is
a slightly different alignment shift in the log. Drop references equal to the deleted index, then shift; and
validate the node graph (every index in range, every node at most one parent, no orphans) before saving. See
`tools/own_models/drop_card_slot.py:validate`.

### One Unity build at a time, one test run at a time
Running `dotnet test` while a Unity batch job is working produced 211 failures, including whole classes going
`NaN`, that vanished on a clean re-run. Two deploys at once will also fight over the same archive. Serialise
them, and distrust any suite result taken while something else was building.

### This suite is globally coupled — compare, never count
`WorldTerrain.Active`, `Atmosphere.SteadyWind/ActiveTurbulence/Field` and `FloatHydro.FlatWaterOverride` are
process-wide, so a long run leaves failures that pass in isolation. The number of failures means nothing on its
own. Run the same suite on the branch point, diff the unique failing names, and re-run anything "introduced"
alone before calling it a regression.

### A test that reads the file it is checking proves nothing
Expected values belong in the test as literals, copied by hand from the source document. `AircraftLoadTests.ArmTable`
and `CockpitEyeTests.ApprovedEyes` are the pattern: moving a number in a config then has to be a deliberate edit
in two places.

### A check must measure the mechanism, not the symptom you expected
`PropSpinCheck` v1 asked "did the Prop node's transform rotate?" and reported four aeroplanes static that were
turning perfectly well: a carved model's blades are SKINNED to the hub's bones, so the vertices move and the node
never does. Two mechanisms produce the same visible result, and a check written against one of them fails the
other. Before trusting a red check, find out which mechanism the passing case uses.

### A silent exclusive mode is usually a bug in disguise
Turning on "look around by moving the device" used to `return` out of the frame before the hat ran, which quietly
removed the on-screen look pad — no message, no setting, the buttons simply stopped working. Where two inputs
write the same state, give each its own offset and sum them (`CockpitLook` keeps a manual and a device half and
clamps only the total) rather than letting whichever runs first win.

### Put a setting in the tab a pilot would look in
"Move the device to look around" and its gain slider shipped on the **Audio** tab, next to the radio volume,
because that was where the other looking-around options already happened to be. Ty found it and said it made no
sense there. It is an input: it decides where the pilot's head points, the same way expo and dead zone decide how
his hands reach the stick, so it belongs under **Controls**. Same rule for visibility: a setting drawn only when
`FindFirstObjectByType<CockpitView>()` happens to find one is invisible from the menu and from any outside view.
Keep the setting static and persisted, show it always, and let whatever component exists apply it.

### A delta in the sensor's own frame is not a measurement of the world
Device look-around shipped reading yaw and pitch straight off `reference⁻¹ · current` from the gyro. That delta is
expressed in the PHONE'S axes, so what a movement did depended on how the phone happened to be held when it was
zeroed: from a real pose one rotation spilled into two components (every movement came out diagonal), two
different rotations overlapped (turning and rolling felt identical), and the third landed on the component that
was discarded (tilting did nothing). No axis swap or handedness flip fixes a frame that moves with the sensor.
Resolve the thing you actually care about — here, where the phone is AIMED — against a world axis, and the
components come out independent by construction.

And the tests that missed it all zeroed from the same tidy pose. When a measurement is relative to a reference,
at least one test must take the reference from somewhere awkward: `FromAnyPoseAMovementIsStillPure` is the one
that would have caught this on the bench instead of on Ty's phone.

### Don't "correct" a cockpit from your own reading of a photograph
Ty flies a real Great Lakes. The lever order was throttle / prop / mixture left to right and it was RIGHT; I
reversed it on 2026-11-01 because I thought I could read the layout off a dark close-up photo, and shipped it.
When the owner states a layout, or his photograph is the reference, follow it or ask — do not infer a
"correction" from the picture and do not defend it with axis arithmetic afterwards.

And for a left/right question, a render is the only honest evidence — but only from the SEAT EYE. The first
quadrant renders were shot from in front of the levers, which mirrors left and right, and that mirrored picture
is how the order got reversed in the first place. I then got the glTF lateral sign wrong TWICE by reasoning
about it (it runs to the pilot's LEFT) and only settled it by looking at an eye-point render.
`check_great_lakes_rig` now asserts the left-to-right order so nobody can re-derive it wrongly.

### Say which build is on the device, don't infer it
A missing button was diagnosed three ways before anyone read the build number on screen — the device was carrying
another lane's build, installed over the top. The build stamp is `bundleVersion` (`2026.MMDD.HHMM`); check it
against the build you made before reasoning about the code.

### zsh eats unquoted glob flags
`grep -rl "x" --include=*.unity .` dies with `no matches found: --include=*.unity`, and in a `&&` chain the echo
after it can still print, which reads as a passing check. Quote the pattern (`--include='*.unity'`) and check the
exit status rather than trusting the next line of output.

### Half a fix for a global is no fix at all
`SoaringTests` sets `Atmosphere.SteadyWind / ActiveTurbulence / Field`, which are process-wide. It was given a
`Dispose` (5b5eb46, "Reset the weather after every test, not at the end of a passing one") and never given
`[Collection("WorldTerrainActive")]`, which 68 other classes carry. Those two guards stop DIFFERENT things:
the `Dispose` stops a throwing test leaking its weather FORWARD, and the collection stops xUnit running the
class ALONGSIDE another one. With only the first, the class still put a thermal under other people's
aeroplanes mid-flight: on 2026-11-01 that was **81 of 211 failures** — `StructuralTests` 39 of 40 and the
whole fleet of `SpawnSpeedTests` stalling, almost all of them NaN. The symptom was written down in that very
class's own `Dispose` comment ("That is what put the whole fleet into a stall in SpawnSpeedTests while all 34
passed in isolation") a commit before the cure was finished. If a class touches process-wide state it needs
BOTH, and the audit is one line: every class that assigns `Atmosphere.*`, `WorldTerrain.Active`,
`WorldTerrain.SurfaceOverride` or `FloatHydro.FlatWaterOverride` must carry the collection attribute and reset
in `Dispose`, not in the body of a test that might throw first.

### A process check that greps command lines matches the agents doing the checking
`pgrep -f 'tools/deploy/(deploy|testflight).sh'` reported a running deploy twice on 2026-11-01 when there was
none. The first hit was another agent's `claude` process whose PROMPT TEXT contained the path; the second was
another agent's own WAIT LOOP, a `zsh -c` whose `pgrep` pattern contained it. A checker that matches other
checkers will either block forever or, worse, be "cleared" by someone killing an unrelated process. Match the
real thing — `pgrep -x Unity`, `pgrep -x xcodebuild`, and for a script the invocation itself — then drop any
hit whose `comm` is `claude` or whose args contain `pgrep`. A genuine one looks like
`zsh tools/deploy/deploy.sh 00008130-...`, with the script as argv[1].

### A fixed port number is a global shared between PROCESSES
`LanMeshTests` gave each test its own UDP block (48310, 48320 … 48390), which is unique within one process and
not between them. Several worktrees run this suite at once here — four were running together on 2026-11-01 —
so the second arrival lost every port in a block and all eight tests reported "no free port in 48370..48373".
It had been written off as a sandbox socket restriction; binding `0.0.0.0` and `127.0.0.1` by hand proved it
was not. A block must be the SAME for every peer in one test (they find each other by sweeping
`basePort..basePort+PortsPerHost-1`) but need only be UNIQUE PER PROCESS, so offset the origin by the process
id — and keep the band below 49152 so the kernel's ephemeral range cannot take one first.

### A count from the log is not a baseline — re-measure, and say what was running
The brief for this triage quoted "109 failing of 783" from an AGENT-LOG entry. The suite was by then 1233
tests and failing 211. Worse, a first run taken while three other worktrees were running theirs gave 198, and
the same tree re-run on a quiet machine gave 211: the numbers disagree with each other, not just with the log.
Counts in this repo decay, and two runs of the same tree are not comparable unless both say what else was
running. Re-run the branch point yourself, record what the machine was doing, and compare BY NAME.
