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

### Say which build is on the device, don't infer it
A missing button was diagnosed three ways before anyone read the build number on screen — the device was carrying
another lane's build, installed over the top. The build stamp is `bundleVersion` (`2026.MMDD.HHMM`); check it
against the build you made before reasoning about the code.

### zsh eats unquoted glob flags
`grep -rl "x" --include=*.unity .` dies with `no matches found: --include=*.unity`, and in a `&&` chain the echo
after it can still print, which reads as a passing check. Quote the pattern (`--include='*.unity'`) and check the
exit status rather than trusting the next line of output.

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

### A pre-flight check that only prints is not a gate
Before the C172 v4 build I ran the `ps … | grep -E "(xcodebuild|Unity)"` check, echoed its output and then started
`deploy.sh` in the SAME command — so when another lane's Unity batch appeared in that list, my build started
anyway and the two overlapped. Make the check the condition (`until ! ps … | grep -q .; do sleep 60; done && build`),
never a line of output to read afterwards. And know what it matches: an orphaned `il2cpp` from a dead Xcode build
(parent reparented to launchd, 0 % CPU, under `/tmp/flyinggame-dd/…/Unity-iPhone.build/`) matches forever, as does
`adb logcat` under the Unity path; exclude those by PID after checking them, not by loosening the pattern.
