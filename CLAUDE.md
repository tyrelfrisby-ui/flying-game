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

### `src/` is compiled by Unity too, so it may only use what Unity has
`src/FlyingGame.Core` is not a library Unity consumes as a DLL — Unity compiles those .cs files itself (hence the
tracked `.meta` files beside them). So anything the .NET 8 suite happily links is not automatically available:
`System.Text.Json` is not in Unity, and a new `JsonSerializer.Deserialize` in Core is six `error CS0234`s in a
batch run while all 36 test cases stay green. The repo's answer is already written down in
`AtmosphereFieldConfigLoader` / `UnityAtmosphereLoader`: wrap the headless loader in `#if !UNITY_5_3_OR_NEWER`
and put a Newtonsoft counterpart in the Bridge assembly. Add POCOs and maths to Core freely; add a dependency to
Core only after checking Unity has it.

### An editor batch tool has to drive the component itself
`Awake`, `OnEnable`, `Start` and `Update` do NOT fire on a plain `MonoBehaviour` added in edit mode, so a tool
that adds a component and then reads what it built gets nothing — `VrStereoShots` reported "the rig built no
eyes", which reads like a gyro or a config problem and is neither. Give the component an explicit entry point
the tool calls (`VrRig.SetActiveForTesting`), resolve references lazily rather than in `Awake`, and don't reach
for `SendMessage`. The same applies to anything sized from `Screen`: the batch window is 640x480, so a rig that
sized its eye targets from it rendered 272x408 instead of 963x1265 — blurry shots and far too narrow a field of
view, with no error anywhere. A shot tool must state the device's pixels AND its physical size
(`VrRig.SetScreenForTesting`), the same way `CockpitEyeCheck` states the eye it is checking.

### A menu drawn with OnGUI does not exist inside a headset
Every menu in this game is IMGUI, and on a Quest IMGUI draws to a back buffer nobody sees. The first Quest build
would have opened on the landing page, paused, invisible, with no way out, and nothing in the log would have
said so: the app "runs". Anything a pilot must reach in VR needs a world-space counterpart (`QuestMenu`), and a
flight started from it must not lead into another OnGUI-only screen (a lesson card, a challenge brief), which is
why the VR menu starts free flight only. Ask "can he see this with the headset on?" of every new screen.

### OpenXR refuses to build on a validation error, and some of its rules are shared settings
`OpenXRProjectValidationBuildStep` throws `BuildFailedException` for any error-level rule, and Meta Quest Support
has one that iOS cares about: the default orientation must be Landscape Left, while iOS is AutoRotation. Changing
the shared setting in ProjectSettings would have changed the iPhone build. `QuestBuild.BuildQuest` sets it for the
duration of the Android build and restores it in `finally`, the same way the bundleVersion stamp is restored. Any
future rule that touches a shared setting gets the same treatment, never a committed change.

### `build/*.log` ignores logs, not everything you write into build/
The Quest deploy first wrote its logcat capture to `build/quest-logcat.txt`, which `git check-ignore` showed was
NOT ignored: the rules are `build/*/` (directories) and `build/*.log`. Put new artefacts inside a build/
subdirectory (`build/quest/logcat.txt`) or give them a .log name, and check with `git check-ignore -v` before the
first run, not after.

### A shader name in a list is not a shader in the build
The VR menu was written against `Shader.Find("GUI/3D Text Shader")`, because `EnsureAlwaysIncludedShaders` lists
that name and a comment in WorldText.shader mentions it. It does not exist in this editor: every build logs
`'GUI/3D Text Shader' not found in editor — skipped`, and `new Material(null)` threw inside the menu's Build(), so
the headset would have opened on an invisible, paused landing page with no menu at all. The shot tool
(`QuestMenuShots`) caught it on the bench. Before depending on a shader by name, grep the build log for
"not found" and render the thing once; prefer a shader of our own in `Shaders/` (`FlyingGame/XrOverlay`).

### StreamingAssets is a folder on iOS and a zip on Android
Every runtime loader read `Path.Combine(Application.streamingAssetsPath, …)` with `File.ReadAllText`, which works
in the editor and on iOS and throws on Android, where that path is `jar:file://…/base.apk!/assets`. On the Quest
the aircraft config never loaded, so `FlightSimDriver.Spawn` threw in `Awake` and again on FLY, the aeroplane stayed
at the world origin with no sim — 2,749 m inside the island's summit — and the chase camera, which may not go
underground, sat on the peak. Ty reported "on top of a mountain looking around"; no bench check could have seen it
because the bench has a real folder. Read StreamingAssets only through `StreamingFiles` (it reads inside the APK);
`StreamingAssetsTests.RuntimeCodeReadsStreamingAssetsOnlyThroughStreamingFiles` holds runtime code to that. For a
new platform, ask what its file system looks like before trusting a loader that "works everywhere".

### A choice stored as an index means something else in the next list
The cockpit seat was a bare index into whatever aeroplane was loaded. On the Quest landing page that was the 2-33
(front = 0, rear = 1), so "Rear seat" stored 1 and FLY put Ty in the Pitts' seat 1 — its FRONT seat. The clamp on
aircraft change made it look safe: every index stayed in range. Keep a choice by the thing's own stable ID
(`SeatChoice`, seat ids are "never renamed once shipped") and resolve it against the list it is applied to, with a
stated default when the ID is absent; and a menu that offers a choice for "the aeroplane you will fly" must read
THAT aeroplane, not the one that happens to be loaded.

### When a platform drops an overlay, ask what the overlay was covering for
The Pitts' and the Extra's rear panels are painted faces with painted needles (`rear_panel.py`: "all static"), and
in the Extra they sit in front of the rig's real needles. Nobody noticed on an iPhone, because the flat dials
(`AnalogGauges`) carried the readings; the Quest suppresses every flat overlay, and Ty's first report was "none of
the cockpit gauges move". Before standing an overlay down on a new platform, find what the pilot was reading from
it and check that the 3-D cockpit really shows the same thing on the aircraft he will fly.

### A fix that needs an optional node must say when it found nothing
`CockpitDisplays.SeparateCoplanar` (the 2026-11-01 card-flicker fix) returned silently without `Eye_Rear`, and ran
only for rigged models. The Pitts has neither an eye node nor a rig, so the fix never reached the aeroplane the
report came from, and the card "still flickers" a day later. Test a fix on the aircraft named in the report, fall
back to something every model has (the aeroplane's own axes), and log when a lookup comes back empty.

### A millimetre in world space is not a millimetre
There is no floating origin: the aeroplane is routinely kilometres from (0, 0, 0), where a float carries about half
a millimetre, so `t.position += dir * 0.001f` can round to nothing or to double. Separate coplanar surfaces, place
needles and pivot cut-out parts in the PARENT's local frame from local transforms (`CockpitLiveParts`), never by
reading and writing world positions.

### `Transform.name` allocates a new string on every read
`GroundShadow` read two names per ancestor of every airframe mesh, every frame: on the Quest that was the largest
source of garbage, and a non-generational collector turns steady garbage into a periodic stutter. Resolve anything a
name decides once (on build / `Refresh`) and keep the answer; never read `.name` in a per-frame loop.

### A device's layout is not the device
The Input System turns a USB joystick on Android into an `AndroidJoystick` whose layout has a `stick` (X, Y) and
nothing else. Rudder pedals put the rudder on Rz and may put the toe brakes on Android's BRAKE / GAS axes. None of
those are controls on that layout, so reading `allControls` finds two axes, and both are toe brakes. Nothing errors,
and the rudder is simply never there. The values are in the device's state (`AndroidGameControllerState.axis`, one
float per Android axis code), and the device description lists which ones it has (`motionAxes`). Read what the
description says the hardware has, not what the layout chose to name (`Bridge/RudderPedals.cs`). Also,
`InputControl.currentStatePtr` is not public: use `CopyState<T>`.

### Never parent your object to something another system rebuilds
The VR menu's panel was parented to the aeroplane so it would ride the cockpit. The airframe's rebuild (every FLY)
destroys every drawable child it did not build itself — a rule written to stop a model's fuselage riding along to the
next aircraft — so the menu worked on the landing page and was a destroyed object from the first flight on. `_built`
stayed true, every ☰ threw inside `Open`, and on the headset nothing happened at all; it was first blamed on the
input handler. Hang things on a root object you own (`QuestRig.HudAnchor`) and make anything a pilot cannot do
without rebuild itself when it finds its objects gone.

### A glTF mesh is not a Unity mesh
glTFast puts a mesh's first primitive on the node's GameObject and each further primitive (one per material, when
they do not share a vertex buffer) on a child of its own. The Pitts' `Cockpit` is nine Unity meshes; a cut that read
`cockpit.GetComponent<MeshFilter>()` saw only the seats and left the sticks static. The bench check summed triangles
over all the GLB's primitives, so it agreed with the table and proved nothing about the code. When code reads a
model's geometry, check it against the hierarchy glTFast BUILDS (`GetComponentsInChildren<MeshFilter>`), not the
file's mesh list.


### A shortcut with a threshold is a cliff in the frame budget
`AirframeContact` skipped its hard points only while the airframe was 60 m clear of the ground. Every bench run
flew above that, so the expensive path — 21 points × 4 RK4 stages × 200 Hz of ground, water, cave and surface
queries — was never timed, and on the Quest it took the frame from 72 fps to 20 the moment Ty came down to land.
Then the catch-up cap doubled the steps per frame, which is why it felt like "a crawl". When code has a cheap path
and an expensive one, time the expensive one on purpose (`QuestLowAltitudeProbe` flies 500 m, 50 m, 30 m and the
ground at the device's own coordinates), and keep a per-subsystem breakdown in the device log (`SimProfile`, the
`top:` field of `[QuestPerf]`): a build that cannot be sampled has to say where its time went.

### An eye point is only as good as the model the game draws
The 2-33 and the J-3 had approved eye points, measured on our own GLBs, and the Quest still put Ty inside the
fuselage: neither type was drawing our GLB. The 2-33's default livery drew the procedural trainer and the J-3's own
model is not wired in, and the procedural airframe is a closed tube with the eye buried in it. Before trusting an
eye (or a sight, or a cockpit part), find out which model `AirframeModels.PickOwn` actually places for that type and
livery, and measure against that one (`tools/own_models/check_eye_interiors.py`). Also: `AirframeModels.Place` puts
the model in the CG-rooted frame using the config's datum numbers, and the eyes were measured in that frame — adding
the CG to the eye "puts" the Great Lakes pilot 7 cm under his own turtle deck.
