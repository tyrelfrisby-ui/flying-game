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

### A number attributed to a tool must have come out of the tool
Building the Grob 103's airbrakes I wrote "XFOIL's edge velocity at 0.42 c runs 1.20-1.30 V" into the builder and
used 1.25 -- before running XFOIL for it. When I did (`DUMP`, x/c 0.428), the E603's rooftop runs 1.35-1.55 V: the
plate's dynamic pressure was 29 % low, and it decided whether the brakes hold a 45-degree dive under VNE as the manual
says. A full suite run was thrown away. If a comment says where a number came from, produce the number from there
first, and print the reading beside it in the builder.

Related, for anyone running XFOIL from a script: it writes its scratch files (`:00.bl`) into the CURRENT directory.
Run it with `cwd=` a temp dir, not the worktree.
