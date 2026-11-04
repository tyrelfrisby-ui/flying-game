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

### When something looks broken, measure what it TOUCHES
Ty saw the chrome bezel on the Great Lakes front oil-pressure gauge as a broken ring. I measured the ring: 384
triangles, every one of 360 degrees covered, no hole — so I was one step from reporting "it is only reflection"
and changing nothing. The ring was whole. Its NEIGHBOURS were eating it: every adjacent pair in that row of six
instruments interpenetrated, by 8 to 16 mm, and the tachometer's face was simply drawn over the chrome. The five
dark bezels had the same fault and hid it, because a dark ring cut by a dark face still looks like a dark ring.
A bright part is a fault detector for everything around it. When a part looks wrong and the part measures right,
measure the things in contact with it before concluding the render is lying.

### Scaling a layout guide to fit a span does not scale the objects in it
The row spacing came from photo ratios — 1.54, 1.31, 1.05, 1.05, 1.06 of an ASI diameter — and I scaled them to
the panel row's existing 290 mm width, reasoning that an oblique photograph does not fix absolute scale. True,
but the instruments do: they are real 3-1/8 in and 2-1/4 in cases, and six of them cannot fit in 290 mm. The
ratios were right and wanted a 477 mm row. Whenever a proportional guide is fitted to a span, check that the
things being laid out still fit in it, and assert it — `check_great_lakes_rig` now measures the exported dial
faces and fails if two instruments share space.

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

### An image material with a transparent PNG is not transparent by itself
`paint.mat_image` links the texture's COLOUR and nothing else, which is right for a dial face that fills its
quad. Give it a cut-out PNG — the front compass's case, whose padding and whose window are both transparent —
and the quad renders as an opaque slab over whatever is behind it, which is how the new compass came to hide the
combined oil gauge. Link the texture's Alpha AND set the blend method (`paint.mat_image_alpha`, which follows
`mat_glass`); confirm it took by reading `alphaMode: BLEND` back out of the exported GLB, not by assuming.

### Check a cockpit change by rendering it, then LOOK at the render
The panel layout, the lever order and this compass were all verified by eye from a seat-eye render, and each
time the render showed something the numbers could not: a mirrored lever order, an opaque slab, a gauge hidden
behind a case. Geometry that passes every assertion can still be wrong on screen. Budget a render and actually
open it.

### A slider's floor is not its default
Device look-around shipped at gain 1.0 because every default read `MinDeviceGain` — the bottom of the slider's
travel — as though "the smallest legal value" and "the value you get before you choose" were the same question.
They are not: Ty flew it at 1.0, 7.0 and 5.0 and asked for 3 to 4. Give the default its own named constant
(`CockpitLook.DefaultDeviceGain`), and when you move it, check which tests were reading it IMPLICITLY — a dozen
tests here were about direction, not gain, and constructing the object with no gain quietly multiplied all their
expectations. Pin those at an explicit value (`OneToOne()`) so they keep testing what they are named after.

And changing a default must not touch anybody's saved setting: `GetFloat(key, default)` already does the right
thing, so leave the pref alone rather than "migrating" it.

### Tune the half that is wrong, not the half that is easy
A steady roll rate is aileron moment divided by roll damping, and either one will buy you the number. The Pitts
was rolling 392 deg/s against Ty's 270: adding `fuselage.damping.p` would have hit 270 exactly and made the
onset mushy, because this model's roll damping is not a fudge factor — it falls out of the wing strips' own
geometry (p·y/V per strip) and was already right. Before turning a knob, work out which side of the ratio is
the wrong one and say so in the test, or the next person will "fix" it from the other end.

### Before adding a factor, find out whether the model already applies it
This one cost three attempts at the same number and two rejected commits, so it is worth the length.

The Pitts rolled 392 deg/s against Ty's 260–300. Attempt one shrank the aileron TRAVEL to 16.5°, which is less
than this model gives a J-3 Cub and is a certificated figure besides (FAA TCDS A8SO Rev. 21 p. 9: 25° ± 2°).
Attempt two added `controls.aileron.effectiveness = 0.66`, called it the flap effectiveness τ, and defended it
with "thin-aerofoil theory independently predicts τ ≈ 0.60, so a calibration constant another calculation also
predicts is a measurement."

That defence was wrong, and the way it was wrong is the lesson. **τ was already in the model** — the fixed row
ahead of a control is cambered by `g = 0.85·τ(E)` and the whole scheme is written down in
docs/CONTROL-SURFACES.md. The "independent corroboration" was the model's own assumption handed back. A second
factor did not add physics, it counted the same physics twice, and it happened to land near the ratio needed to
reach a published number — which is exactly what a fudge looks like from the inside. Ty, who flies these, caught
it both times.

The same trap was waiting twice more in the same job and was only avoided by measuring first:
- **A DATCOM `tanh` saturation for ailerons.** The hinged strip already samples its section table at α + δ, and
  at 25° that is past the stall: the model's effectiveness at full aileron is 0.63 against DATCOM's 0.75, i.e.
  already saturating HARDER than the correction would have imposed. Adding it would have given ~0.47.
- **A ~0.9 hinge-gap loss.** Real physics, but nothing in any config records whether a gap is sealed, so the
  value would have been chosen rather than derived.

What the rate actually was: camber applied across the whole fixed-row span, including the part with no aileron
behind it — roll power with no roll damping to go with it. Coverage is 1.000 for 28 of the fleet's 32 systems
and they did not move at all; it was 0.68–0.88 on exactly the four surfaces rebuilt from GLB models, and
fixing it took the Pitts from 392 to 291 with nothing tuned.

So: before adding a coefficient, go and find where the model already handles that effect, and MEASURE what it
currently produces. If the measurement says the effect is already there, the coefficient is not a refinement,
it is a second helping. And a constant that matches an independent calculation is only corroborated if that
calculation is genuinely independent of the model — check that it is not just your own assumption returning.

### A correction can be right in theory and still make the aeroplane fly worse
Nine wing surfaces in this fleet carry 2-D section slopes (6.30/rad on the Pitts) with no finite-span
correction, while the tails have had the DATCOM/Helmbold rescale since 2026-10-06. Applying the same rescale to
those wings is unambiguously the better physics, and it was built, run against the whole fleet, and then taken
back out — because Helmbold keeps CLmax and moves the stall to a higher incidence, and a taildragger sitting on
its wheels is pinned at its ground attitude. The Pitts' lift-off went 76 mph → 90 mph and the Extra's 87 → 100,
against real rotation speeds nearer 60–70: an 18 % error on a number checked against the books, bought for a
7 % change in the roll rate that was the point of the job — and in the wrong direction there too.

The reason is worth remembering: those configs' wing area, incidence and CLmax were authored against the 2-D
slope and quietly absorb it. You cannot correct one term of a calibration that other terms were fitted around;
the wing has to be re-derived as a whole, which is a different piece of work. So when a theoretically-correct
change moves a validated number the wrong way, the change is not necessarily wrong — but shipping it alone is.
Measure the validated numbers too, not only the one you are chasing, and be willing to leave a correct change
on the shelf with the measurement written down.

And the converse, which nearly happened here: the Pitts sits at 291 deg/s partly because the model
over-saturates its ailerons by 18 % against DATCOM. Fixing that alone would put it at ~369. Two errors pointing
opposite ways can make a total look right, so say which effects you measured SEPARATELY and never treat a
matching total as proof that each part is right.

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

### A second camera inherits nothing it was not given
The Quest clip recorder renders the scene a second time from the head with its own camera, and two things the
pilot sees did not come with it. The aerobatic sight is shown only to `Camera.main` in the cockpit
(`CockpitSights`), so it was missing from every clip. The water's depth tint reads `_CameraDepthTexture`, which
only the XR camera renders, so the recording sampled another camera's foveated, tiled depth and painted a
flickering light-blue grid on the sea. Ty reported that grid as z-fighting. When you add a camera, list what
the scene's shaders and components key on per camera (Camera.main checks, onPreCull hooks, depth / reflection
textures, layers), and give each one to the new camera explicitly (`CockpitSights.MirrorCamera`,
`_WaterSceneDepthOff`) or switch it off for that render on purpose.
### A full procedural rebuild can revive content removed by an offline pass
The Extra wing-only change initially regenerated an obsolete cockpit card alongside the current card
(`SequenceCard_Face.001`). HEAD's GLB had been updated offline after the full builder. Compare exported node
sets and non-target meshes before calling a scoped rebuild complete. `build_ea300.py -- --wing-only` now
replaces just the three wing meshes in the approved existing GLB. Also keep the paint-placement reference
on its original chord grid when changing the geometry's sampling grid; mixing them caused an interpolation
length error on the first B build.

### A comment is not a shell option
`deploy.sh` said "the installs are not under set -e" over a loop that WAS under `set -e`, so a locked iPad's
failed `OUT=$(devicectl …)` ended the script without a word and the iPhone after it never got the build. If a
block must survive failures, say `set +e` in the code above it, and when a script stops with no message, look at
its error options before the device.
### Audio timing and bubble rates need checks in samples and SI units
The crash-audio renderer caught a travel delay drifting early when each sample subtracted a float second,
and an overloaded soft clipper rounding slightly past 1. Use integer sample countdowns and explicitly bound
new voice-pool outputs. Review also caught R~Q^(1/3) cancelling Q/V, so bubble packet rate stayed constant:
check actual rates at representative SI flows as well as listening. The vent approximation now shares flow
between size and rate (R~Q^(1/6)); the runner asserts both rise before the cap.
### A thread-static override must be cleared on the thread that set it
`FloatHydro.FlatWaterOverride` is `[ThreadStatic]`. The crash-water tests set it inside the test and cleared it in
the class's `Dispose`, which xUnit can run on a different thread. The test's own thread then kept a 10 m lake over
flat ground for whatever ran on it next: aeroplanes "on the runway" were under water, taxi tests tore off gear, and
a full run showed fifty new failures that all passed alone. Set and clear it in the same synchronous scope (a
`using` helper or try/finally in the test method), never in a constructor/Dispose pair.

### One airframe on two undercarriages must sit in one place on its CG
`AirframeModels.Place` grounds a model by putting its lowest point on the deepest contact point. For the J-3 that
landed the floats GLB's keel on the physics keel to the millimetre, and moved the whole airframe 4.5 cm down under
the CG compared with the landplane, so the one approved eye (measured on the landplane, copied into both configs)
sat 4.5 cm higher in the cabin and failed the 12 cm check. The CG is a point IN the airframe: bolting floats on
does not move it. When two configs share an airframe and an eye, place the second so its airframe coincides with
the first (`Spec.SitHighM`), let the new gear be the thing that is off, and gate the coincidence
(`check_eye_interiors.py` prints `COINCIDE`). Before trusting an eye in a variant, measure it in that variant.

### A bench world without the game's world measures a different game
`QuestLowAltitudeProbe` timed contact on the island's terrain and called the slowdown fixed, while the headset still
sat at 27 fps on the ground: the game's `IslandGeometry.Build()` registers 1,475 solids and 128 decks, the probe built
none, and every hard point was asking all of them at every RK4 stage. The probe's 94 height queries a step were the
"fixed" number; the device's 771 were the real one. When a bench number disagrees with the device by a large factor,
find what the device has that the bench does not before tuning anything. Build the world the game builds
(`IslandGeometry.Build()`, cleared in `finally`), and count work, not only time (`SimProfile`'s `solids`).
### A Core type name can be shadowed in the Bridge, and only Unity sees it
`FlyingGame.Core.Vr.PilotBody` compiled and 32 tests passed in the .NET suite, then gave five `error CS0117` in the
Unity batch: the Bridge already has an `internal sealed class PilotBody` (the bailout's jumper, `Bridge/Pilot`), and
inside `namespace FlyingGame.Bridge` the Bridge's own type wins over a `using FlyingGame.Core.Vr`. The suite cannot
see it because it never compiles the Bridge. The same run found `AirframeVisual.ModelRoot`, which is on the
builder, not the visual. Before naming a new Core type, `grep -rn "class <Name>\b" unity/Assets` (with perl or
`grep -w`; BSD sed has no `\b`), and run the Unity batch before trusting any Bridge code that calls into it.

### Quote the shipped model's number, not the sketch's
The parachute's opening shock went into three comments as "≈ 7.4 g" because that is what a quick Python sketch of the
opening said. The Core model that ships blends the body's drag area differently and gives 6.7 g; the sketch had never
been the code. Every comment, doc table and NEEDS YOU line had to be found and changed. Sketch to choose the model, then
run the Core function itself (a throwaway probe test is enough) and copy THOSE numbers into docs and tests.

### Two points on a lever do not give its hinge
The 2-33's dive-brake knob sits 11 cm along its hinge from the pivot. A lever built from the pivot and the knob at its
two ends (`r₀ × r₁` as the axis) read a hinge tilted 19° off lateral and a 31° sweep for the rig's 33°, so the hand
would have needed 6 % more pull than the handle moved and the knob would have been drawn off its own slot. Nothing
errors; `VrHandleCheck` caught it by printing the sweep. Take the hinge direction from the rig (`RigHandle.HingeWorld`:
`RestRot · axis` in the parent) and project onto the plane across it; derive only the SENSE from the points.

### A clean merge is not a compiling merge
lane/claude-vr-cockpit and lane/codex-vr-cockpit each added `case "canopy_latch":` to `RigInputs.TryGet`, a few lines
apart. git merged them without a conflict and the result was a duplicate case label (and two contracts for one param).
After any merge, build before reading the conflict list as the list of problems — and grep the merged switch
statements for a repeated label when two lanes touched the same table.

### Unity writes the keystore password into files you never asked for
A release Android build with `PlayerSettings.Android.keystorePass` set leaves the password in plain text in the generated
Gradle project (`launcher/build.gradle`) and in Bee's own copies (`Library/Bee/artifacts/…/build.gradle(.xml)`,
`Library/Bee/Player*-inputdata.json`); deleting only `Library/Bee/Android` missed three of the four. Pass the password
through the environment, delete all of `Library/Bee` after a release build, and grep the tree for the password (file
names only) rather than trusting a list of places (`deploy-quest.sh --release`). Also: assigning `""` to
`PlayerSettings.Android.keystoreName` serialises as `'{inproject}: '`, so restore signing fields only when you set them.

### Check a force's parity with the mirror state before blaming a sign
"Opposite aileron lowers the nose inverted" looked like a sign error at negative α. There was none. The aileron's
separated-flow load was a DRAG along the wind, EVEN in δ, where a plate's load is normal to the plate and the
deflection's share is ODD in δ. An even term cannot mirror, and it slowed the spin with either stick. What found it was
the mirror test (`SpinBalanceProbe` part `mirror`): flip a developed spin 180° about x, hold both states, and compare
part by part. Symmetric physics maps (L, M, N) → (L, −M, −N); whatever doesn't is either geometry or a wrong-parity term.
Two traps on the way: a probe that rebuilds an `Aircraft` resets the per-strip separation memory, which is hysteretic
(11–16°), and read the TR-379 slip roll ~40 % low until the flown aircraft's memory was copied across. And a failure blamed
on the last lane to touch the code (StallAuthority on run #3) came from two runs earlier. Bisect before attributing.

### A weighted average with a zero weight is a NaN waiting for its geometry
The island pad blend skipped pads with `dd >= blend` but averaged the rest with weight `1 − Smooth(dd/blend)`, which
rounds to exactly 0 just inside the edge. With one such pad in range `sumZ/sumW` was 0/0, and `Mix(h, NaN, 0)` is
NaN, not h. It sat unnoticed until the Frank Church valleys put strip approaches across those edges. The moraine pass
had the same shape (`melt / u` with u = 0), and its box blur spread the NaN over whole rows of the island. Guard the
weight, not the result. When a terrain change makes far-off ground go strange, scan for NaN before suspecting the
change. And expect a regenerated relief to re-pick every generator-chosen site (snow patches, ledges, boulders): that
search is a global ranking, so its literal tests must be re-copied from the new report, not "fixed".
