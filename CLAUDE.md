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

### A "nothing is building" check must look at the process, not its command line
The wait-for-free check `ps -axo pid,command | grep -E '^ *[0-9]+ (/.*/)?(xcodebuild|Unity)( |$)'` reads whole command
lines, and a `claude -p` agent's command line is its prompt. One prompt said "…while Unity/xcodebuild is busy", which the
regex matches, so the TV lane waited from 19:03 to 22:58 for a build that did not exist. Keep the regex if a brief asks
for it, but count a match only when that pid's executable (`ps -o comm= -p PID`) is Unity or xcodebuild.

