# PARITY: iOS/Mac and Quest stay in sync at all times

Ty, 2026-11-04 05:46 PST, after flying the Quest: *"it seems like all of the pitts panel and canopy work did not make it
to the quest build either put a parity rule in place ios/mac and quest must stay in sync at all times."*

This is a standing rule, not a lane. `tools/check-parity.sh` measures it, and the deploy scripts run that check.

## The rule

1. **One game.** iOS, Mac and Quest build from the same code, configs and art. Shared code and assets only.
2. **Platform differences only behind build-time flags**: `UNITY_IOS`, `UNITY_STANDALONE_OSX`, `UNITY_ANDROID`, the XR
   packages and their defines, plus the Quest-only files listed below. A Quest-only file may only do what *only* a
   headset can do (tracking, controllers, hands, XR settings, the Quest deploy). Anything a pilot sees on both platforms
   (an aeroplane, a cockpit, a HUD rule, a config, a sim change) is shared.
3. **Every change lands on `lane/claude-island-nav` first and is merged into `lane/claude-quest-islandnav` the SAME
   DAY**, or it lands on a shared lane that is merged into both. Nothing shared is committed on the Quest lane alone.
4. **Art for both platforms comes from one GLB and one config.** A windshield, panel or canopy that is "Quest-only" is a
   fork. Fix it on the shared lane and merge it into both. (The Great Lakes on the Quest has had different
   windshields from the iPhone since 2026-11-02. See `_xfer/gl-windshields/README.txt`.)
5. **Before any deploy** run `tools/check-parity.sh`. `deploy.sh` (iOS) prints it and never blocks. `deploy-quest.sh`
   refuses to build or install while it fails, unless `PARITY_OVERRIDE=1`. That override is for a Quest-only
   experiment Ty asked for, and it gets an AGENT-LOG line saying so.

### Paths

| Shared: must be on both lanes | Quest-only: allowed on the Quest lane alone |
|---|---|
| `configs/`, `src/` (including `src/FlyingGame.Core/Vr/`, which is pure logic) | `unity/Assets/XR/`, `unity/ProjectSettings/XR*`, `unity/Packages/` (XR packages) |
| `unity/Assets/_Project/Resources/Models/` (every GLB) | `unity/Assets/Plugins/Android/` |
| `docs/models/*rig*.json`, `tools/own_models/` | `unity/Assets/_Project/Scripts/Quest/` |
| `unity/Assets/_Project/Scripts/` (Bridge, Editor, ...) and `Shaders/` | `Scripts/Bridge/Quest*`, `Vr*`, `Xr*`, `UnityVrLensLoader*`; `Scripts/Editor/Quest*`, `Vr*` |
| `unity/Assets/Plugins/iOS/`, `Plugins/macOS/` | `tools/deploy/*quest*` |

Anything else (AGENT-LOG.md, other docs, renders) is neutral.

## The lock (Ty 2026-11-04 07:29: "lock them in parity from then on")

Every commit to a lane must keep the two shipping lanes in sync. A git hook enforces it.

**How it is wired.** `tools/githooks/pre-commit` and `tools/githooks/pre-merge-commit` (tracked) run
`tools/check-parity.sh --hook` on the commit being made, against the index. `tools/githooks/install.sh` puts a small
dispatcher for each in the repository's COMMON hooks directory (`$(git rev-parse --git-common-dir)/hooks`), which every
worktree shares. The dispatcher runs the worktree's own `tools/githooks/<hook>` if the checkout has one and **does
nothing otherwise** — so the apparatus has to exist on the branch that is checked out for the lock to fire at all, and
on `main` and `split-surfaces` it does not exist (see the note at the end of *The widget*). `core.hooksPath` is not used.
`install.sh --check` says whether the dispatcher is installed.

### Which branches it fires on

**It fires on every `lane/*` branch — all ~224 of them, not just the two shipping lanes.** This document said for six
days that it "does nothing on any branch but `lane/claude-island-nav` and `lane/claude-quest-islandnav`". That stopped
being true on **2026-11-08**, when the iOS lane started defaulting to *the branch the worktree is on* rather than a name
written in the script (a name goes stale; the branch you are building cannot). The `--hook` block's branch filter
accepts any `lane/*` branch, so on `lane/anything` the lock reads "I am the iOS lane" and holds that lane against
`lane/claude-quest-islandnav`.

Asked on 2026-11-10 whether to narrow the hook to the two lanes or fix the document, **Ty chose to fix the document**:
*the lock being live on every lane is arguably right — it is what stopped several agents shipping unsynced work that
day.* So the scope below is the rule, not a bug to be worked around.

| branch | what the lock does |
|---|---|
| `lane/*` (~224) | the two-lane rule below, with this lane as "the iOS lane" |
| a branch named in `$PARITY_IOS_BRANCH` | the same, as the named iOS lane |
| `main`, `split-surfaces` | **nothing** — and the apparatus is not even on those branches, so the dispatcher exits 0 |
| `codex-*`, `wip/*`, `spin-*`, a detached HEAD | **nothing.** No-op, silently |

`wip/*` is exempt and `lane/*` is not. That asymmetry is real and it surprises people: the same shared change that is
refused on `lane/my-thing` goes through on `wip/my-thing` without a word. The reason is that a feature branch's shared
work meets the lock when it is merged into a lane, and nobody builds from a `wip/` branch — but the practical effect is
that `wip/` is the unlocked naming convention, and it should be used for scratch work rather than used to dodge the lock.

### The two-lane rule

On lane X with the other lane Y, "shared" means the paths in the table above plus
`unity/Assets/StreamingAssets/aircraft/`, minus the Quest-only ones.

- **allowed:** a commit that changes no shared path (logs, docs, renders, `tools/deploy/`, `tools/FlightTests/`,
  Quest-only files);
- **allowed:** a commit that merges Y's tip in (`MERGE_HEAD` = Y). That is how the lanes are synced, so the lock can
  never deadlock;
- **allowed:** a commit whose shared paths come out identical to Y's;
- **allowed, with a message:** a commit while X and Y are identical in shared paths. X now leads, and Y must merge X
  next. **Only ONE such leading commit at a time** — the allowance is "X and Y were identical *before* this commit", so
  the moment it lands X differs and the next shared commit is blocked until Y catches up. Plan a run of shared work as
  *one* commit, or sync in between;
- **BLOCKED:** a shared change while X already differs from Y. Merge the lanes first; the message says how.

So the lanes are never more than one shared change apart. The normal flow: land the change on one lane (directly or by
merging a feature lane into it), then `git merge --no-ff <that lane>` in the other lane's worktree, the same day. Once
both lanes hold the same tree, fast-forwarding one to the other is also fine.

### The consequence that catches people: being BEHIND is enough

**A `lane/*` feature branch that has changed nothing shared at all is blocked as soon as the Quest lane moves.** The
rule is symmetric in a way the wording hides: "X already differs from Y in shared code" does not ask *who* moved. Branch
a fresh lane, let the Quest lane take a shared commit from somewhere else, and your first shared commit is refused with
a list of files you never touched. This bit several agents on 2026-11-10.

The remedy is a merge, in your own worktree, and it is cheap:

```
git merge --no-ff lane/claude-quest-islandnav
```

(git will not merge over a staged change, so `git restore --staged -- <files>` first if the blocked commit is already
staged; the files stay in the working tree.) Then stage and commit as normal. You are not merging *into* a shipping
lane — that is Ty's — you are catching your own lane up, which is what the lock is asking for.

### Naming the partner: `PARITY_IOS_BRANCH`

The iOS lane is auto-detected, and the detection can pick the wrong lane. From a Quest worktree (or a trunk) it cannot
use "the branch I am on", so it reads the answer out of the Quest lane's history: **the first local non-Quest `lane/*`
branch that is an ancestor of the Quest lane's most recent merges** — i.e. a guess at which lane the Quest lane is being
kept in sync with. With 224 lane branches that guess can land on a lane that merely happens to be contained in the
Quest lane.

`PARITY_IOS_BRANCH=lane/<name>` names the partner outright, and **naming the real partner is legitimate** — on
2026-11-09 a merge from the Quest side was blocked against a dead branch and naming the lane by hand was the only way
through.

**Using it to make the hook exit 0 is not.** Setting `PARITY_IOS_BRANCH` to some unrelated branch so that
`$branch != $IOS` and `$branch != $QUEST` and the hook returns 0 is `--no-verify` with extra steps: the commit lands,
the lanes drift, and nothing says so. If the lock blocks, sync; if it is comparing against the wrong lane, name the
**right** one.

### Not covered

- Fast-forward merges and `git reset` / `update-ref` run no commit hook. Do not move a lane's ref by hand.
- **Never bypass it with `--no-verify`.** If the hook blocks, sync the lanes. If a Quest-only experiment really cannot
  wait, put it in a Quest-only path or behind `#if UNITY_ANDROID`, and log it.

## The widget (Ty 2026-11-10: "Keep the aero widget in parity with the app" — "Enforce it like the others")

Asked whether to enforce widget parity the way the Quest lanes are enforced, Ty said **"Enforce it like the others."**
What follows is what that turned out to mean once the widget was actually looked at, because it is not what the phrasing
suggests, and the difference matters: enforced the literal way, the check could never fire.

**The Aero Widget is not a third target.** It is the *same* Unity project, the *same* scene
(`Assets/Scenes/Main.unity`) and the *same* `src/` as the game. `BuildScript.BuildWidget` builds it by flipping
PlayerSettings — product name `Aero Widget`, bundle id `com.tyrelfrisby.aerowidget`, 720 × 720, `runInBackground` — over
the identical `ScenePath` the game build uses, and `SceneBootstrap` picks the widget at *runtime* off the bundle id
(`AeroWidget.IsWidget`). There is no second scene, no second project, no Xcode target, no app extension and no WidgetKit
anywhere in the repo. `docs/WIDGET.md` is the contract; `tools/deploy/widget.sh` is the whole build.

**So the Quest rule's mechanism has no widget analogue.** What `check-parity.sh` measures for the Quest is *commit
reachability between two branches*: the Quest build fell behind because the Quest lane is a separate branch that can
lag. The widget has no branch. And the premise behind "a shared change that skips the widget" does not hold either:
there is no widget-side copy of a config, a GLB, a physics file or a shader for a shared change to forget — the widget
reads `unity/Assets/StreamingAssets/aircraft/` and `Resources/Models/` like the game does, and flies `FlyingGame.Core` /
`FlyingGame.Sim` like the game does. **A path regex cannot express widget parity, because the widget shares not *some*
paths with the app but *all* of them.** A third `SHARED_RE` arm would have been a check that could not fire, which is
worse than no check at all: it reads green forever.

**What can actually drift, and what now catches it.** Two things, and they need different instruments.

1. **The widget's own files, between the two lanes** — the same failure mode as a GLB, and not hypothetical. Three of
   the paths the widget is built from are **neutral** to the lock above (matched by neither regex, so the hook never
   sees them): `unity/Assets/_Project/Widget/` (`WidgetAssets.asset`, `WidgetCopy.shader`), `tools/deploy/widget.sh`,
   and `tools/native-mac/` — the last being the *source* of `unity/Assets/Plugins/macOS/AeroNative.bundle`, whose
   committed *product* is shared. Source neutral, product locked. On 2026-11-10 `lane/claude-island-nav` and
   `lane/claude-quest-islandnav` differed by 98 lines of `tools/native-mac/` and one line of `GameAeroPanel.cs` with
   the parity gate green throughout.

   **Check 4 of `tools/check-parity.sh` now compares the widget's surface blob against blob**, exactly as check 3 does
   for the GLBs: `Scripts/Bridge/Widget/`, `_Project/Widget/`, `tools/deploy/widget.sh`, `tools/native-mac/` and
   `docs/WIDGET.md`. WARN by default, **FAIL under `--strict`** — which is what `deploy-quest.sh` and the lanes run. On
   the current pair (`lane/claude-tv-companion` / `lane/claude-quest-islandnav`) it reads `OK   widget: all 57 files
   identical`; on the stale `island-nav` pair it names the four files above and fails `--strict`.

   **Those paths were deliberately NOT added to `SHARED_RE`,** and that is the whole design decision. The `--hook`
   block treats *every* `lane/*` branch as the iOS lane, so widening the regex would make every lane in the repo stop
   and sync with the Quest lane before it could touch a Mac-only build script. Mac is one of the three platforms, so
   Mac-only code belongs in the parity **report**; only code that ships to iOS and Quest belongs in the commit
   **lock**. A hook that blocks more than the rule needs blocks everyone.

2. **Game-vs-widget feature drift — still open, and no path rule can close it.** This is Ty's *earlier* rule, quoted in
   `AeroHost.cs` ("owner rule 2026-10-09: the GAME and the WIDGET stay in PARITY — one implementation, two hosts") and
   again in `GameAeroPanel.cs` (*"add all of the new features … from widget work to the game as well — keep game and
   widget in parity"*); `GameAeroHost` even names its prefs namespace `aero.parity.`. The failure is a capability added
   to `AeroWidget`/`WidgetServer` and never surfaced in `GameAeroHost`/`GameAeroPanel`. Those files sit **side by side
   in one directory on one branch**, so no path set can distinguish "added to the widget host" from "added to both".
   What would catch it is a **set-equality check over the capability surface the two hosts publish**: `WidgetServer`'s
   `hello` reply already enumerates `features`, `commands`, `show` keys, `insets`, `autopilotModes`, `views`, `presets`,
   `scenarios` and `conditions`, and `GameAeroPanel` carries the game-side switch ids; a test asserting the two sets
   match modulo a declared exemption list, with `WidgetServer.Protocol` required to bump when the set changes, is the
   instrument. That is a test, not a hook, and it is not built. **It needs Ty to say which reading he meant**, because
   it is the expensive one.

**Out of reach from this repo at all:** the widget's client is Glass Overlay's `shared/Widgets/AeroWidget.swift`, which
lives in another repository. The widget↔Glass contract is `WidgetServer.Protocol` (currently 8) plus `docs/WIDGET.md`,
and nothing here can verify the other side of it. `lane/claude-widget-idle` exists because Glass had been sending
`{"cmd":"fps","value":30}` on every connection and the widget silently dropped it.

**Two inaccuracies in this document were found while doing the above. The first is now fixed:** "It does nothing on
any branch but `lane/claude-island-nav` and `lane/claude-quest-islandnav`" was false — the lock is live on every
`lane/*` branch — and *The lock* above now says what the hook actually does, Ty having chosen on 2026-11-10 to fix the
document rather than narrow the hook. **The second is still open:** none of the parity apparatus (`docs/PARITY.md`,
`tools/check-parity.sh`, `tools/githooks/`) exists on `main` or on `split-surfaces`, so in those checkouts the
installed dispatcher exits 0 and there is no enforcement at all.

## How to check

```
tools/check-parity.sh            # full report; exit 1 if the Quest lane lacks any island-nav commit
tools/check-parity.sh --brief    # what deploy.sh prints
tools/check-parity.sh --strict   # also fail on shared Quest-only commits, any GLB that differs, any widget file that differs
tools/check-parity.sh lane/a lane/b     # other branches (or PARITY_IOS_BRANCH / PARITY_QUEST_BRANCH)
```

It reads branch refs only (no fetch, merge or checkout), so it runs from any worktree. `ParityCheckTests` (FlightTests)
runs it against a scratch repository with a known answer for each verdict.

## What to do before a deploy

- **iOS** (`deploy.sh`): read the parity lines. If they are red, the Quest lane is behind. Say so in the AGENT-LOG entry
  and get the merge into the Quest lane done the same day.
- **Quest** (`deploy-quest.sh`): it will not build while parity fails. Merge `lane/claude-island-nav` into
  `lane/claude-quest-islandnav` first. If the check then still warns about shared Quest-only commits or differing GLBs,
  list them in the AGENT-LOG entry. Those are what the iPhone is missing.

## Converging the art (plan, 2026-11-04). Nothing here is ported yet, and nothing is approved by this document

Since the merge base `510f987c`, **island-nav has changed no GLB**. Every one of the 14 differing GLBs and the 2
Quest-only ones is Quest-lane art. More art sits on lanes in neither. The Pitts "panel and canopy work" Ty is missing
on the Quest is most likely `lane/claude-quest-cockpit-switches` `62c54a41` (Pitts v3: one switch row, radios, front
canopy bows gone) and `033dd76d` (rear panel v3.1). Those are on neither lane, so no build has had them.

**Quest lane → island-nav** (port, so the iPhone gets the same aeroplanes). The status column says what the commit or
AGENT-LOG records. Ty to confirm each one before it moves.

| Type | Commits | Recorded status |
|---|---|---|
| Great Lakes | `d936b103`, `147d2682` (frameless windshields, acrylic, V-tube gear) | Ty on 2026-11-04: windshields "did not look good" on the Quest. **Superseded** by cockpit-switches GL v7 below. Do not port alone |
| Pitts | `bad3a452`, `0c0d882f`, `45d26542` (p2c), `e14b800a` (p2d), `911fb2f1` (p2e) | VR cockpit phases logged "NO GATE APPROVED"; p2d knobs "Ty-confirmed" positions |
| Extra 300 | `3a9e746b`, `5c96a4ff`, `d0517239` | no approval recorded |
| J-3 / J-3 floats | `2558d73d` (v16), `9ca7fdce` (v17), `29c55998` (v18), `67d087c2` (water rudder) | Ty-directed (commit subjects cite Ty); panel lettering "nothing approved" |
| 2-33 | `0f50686d` (canopy, Ty t295u) | Ty-directed |
| All rigged | `55954abb` (VR cockpit P3a: moving controls) | no gate approved |
| Pilot body / gear | `a726d882` (v3), `f3525594` (v2); `pilot-body-own.glb`, `pilot-gear-own.glb` | v3 Ty-approved 2026-11-03. Used by the headset only; harmless on iOS |

**Lanes in neither → both** (merge into island-nav, then into the Quest lane the same day):

| Lane | What | Recorded status |
|---|---|---|
| `lane/claude-quest-cockpit-switches` (32) | GL v7 merge `0e63e696` (codex-greatlakes-cockpit incl. windshields v2 `09bc3206`), Pitts v3 `62c54a41` + `033dd76d`, Extra P3f `60f3ff48`, GL ignition `be648602`, `74cb309f`, no headset cord `4f54176b` | GL v7 renders and wing surfaces Ty-approved (`32bda88b`, `9dd328dc`). Windshield pick pending (renders in `_xfer/gl-windshields/`). Rest unreviewed |
| `lane/claude-172-instruments` (17), `lane/codex-172-cockpit` (2) | 172 cockpit | unreviewed |
| `lane/claude-grob103` / `lane/codex-grob103` (13 each) | Grob | Ty "looks good add it"; merge into island-nav under way (see NEEDS YOU) |
| `lane/codex-own-swift` (9), `lane/claude-swift-gear` (6), `lane/codex-swift-gear` (7) | Swift | unreviewed |
| `lane/codex-beaver-cockpit` (4), `lane/codex-eyepoints` (7), `lane/claude-floor-windows` (5), `lane/codex-floor-windows` (1), `lane/claude-233-default` (2), `lane/claude-233-launch` (2), `lane/claude-j3-cockpit` (2), `lane/codex-pitts-debris` (2), `lane/claude-vr-cockpit` (1), `lane/claude-front-sight` (1), `lane/codex-front-sight` (3), `lane/claude-extra-cockpit-shared` (1) | assorted | see each lane's AGENT-LOG entry |

**island-nav → Quest lane**: 42 commits (crash physics, Frank Church strips, CAM button, contact patches, ...). The merge
is in progress in the `claude-quest-islandnav` worktree.

## Audit, 2026-11-04 (output of `tools/check-parity.sh`)

```
== parity: iOS lane/claude-island-nav (aeb9695a) vs Quest lane/claude-quest-islandnav (d784b102)
FAIL  42 commit(s) of lane/claude-island-nav are not in lane/claude-quest-islandnav (55 counting merges):
        aeb9695a 2026-11-04 AGENT-LOG: crash-physics merged (abeadac3), installed iPhone 2026.1104.0427
        da163a65 2026-11-04 Suite vs base by name; forest stand-in test checks Within() directly; calibrated wing may float on; AGENT-LOG
        9a24353c 2026-11-03 Wreck remnant stays stable; tree sweep refuses a non-finite state (the post-merge suite hang)
        95dbccf5 2026-11-03 AGENT-LOG: Frank Church strips unpaved (frank-church-valleys 35901e96 merged 6c24d8eb), installed iPhone 2026.1103.2218; v2 approach renders
        f0618178 2026-11-03 Frank Church strips: real dirt and turf, no runway markings
        f47db91f 2026-11-03 Forest.LiveTrees: compute no tree under a lock (it deadlocked two test threads)
        9b267623 2026-11-03 ModelReadableCheck: every aircraft model mesh must be Read/Write for the device to cut it (27 models PASS)
        a8ddd3c9 2026-11-03 Debris: pieces land and rest on the ground (never removed for reaching it); scripted Pitts crash; FreezeRig guard
        8917ca0a 2026-11-03 Frank Church strips: real unpaved surfaces, and no runway paint on anything unpaved
        2512703e 2026-11-03 AGENT-LOG: Ty approved the Grob; merge held — codex-grob103 carries the unapproved cert restructure and no config
        0802f392 2026-11-03 AGENT-LOG: crash-physics NEEDS YOU and Log entry; contact-patch decisions 1, 2, 4, 5 answered/done
        73c3ad9d 2026-11-03 AGENT-LOG: Pitts debris merge installed on Ty's iPhone, build 2026.1103.2111 (202611032111); targeted tests 26/26
        b0a55c55 2026-11-03 TreeStrikeRecord: a plain readonly struct, not a record struct (Unity compiles C# 9)
        f5694afa 2026-11-03 Water: the same localized break-up with momentum; panels feel the water; consistent strengths and inertia
        26468fee 2026-11-03 AGENT-LOG: TestFlight upload 2026.1103.2046 (build 202611032046) from island-nav d9730e24
        a0041367 2026-11-03 AGENT-LOG: N/S/E/W letters toggle (07558784), off by default, at infinity when on; built 2026.1103.2040, not installed
        07558784 2026-11-03 N/S/E/W letters: a setting (off by default, saved), drawn at infinity above the horizon
        e8fd1370 2026-11-03 AGENT-LOG: Frank Church valleys merged (d9730e24), Ty approved, on the iPhone as 2026.1103.2022
        9ff80523 2026-11-03 Taildraggers never touch a wingtip or stab tip when flown right (Ty decision 2); J-3/PA-18 stab at its real height
        962a98e2 2026-11-03 AGENT-LOG: Pitts spin #4 merged (868bdda1) and on the iPhone as 2026.1103.1951
        a0949d92 2026-11-03 Frank Church strips down in their river valleys; three one-way strips
        3bf6ed2f 2026-11-03 AGENT-LOG: Pitts spin run #4 — flat-spin aileron mirrored upright/inverted (separated aileron load normal to the plate), mirror test, StallAuthority bisected to run #1, build 2026.1103.1941; NEEDS YOU; CLAUDE.md lesson
        d8ab9861 2026-11-03 DebrisWiringTests + AGENT-LOG: debris from the aeroplane on screen; NEEDS YOU approve the debris renders
        fe232151 2026-11-03 Aileron in separated flow: the plate's load is normal to the plate (flat-spin aileron mirrors upright/inverted)
        325b367e 2026-11-03 Water: floats no longer knife under on a flat touchdown; wheels on water are a finite planing contact
        2adf3484 2026-11-03 Crash physics: trees are frangible structures; parts break by load, momentum conserved at separation
        9a967e17 2026-11-03 AGENT-LOG: Ty's camera decisions; marker follows CAM, on iPhone 2026.1103.1740
        b7d54117 2026-11-03 HUD flight-path marker follows the CAM button (Ty 2026-11-03): FLT PATH = air velocity, TRACK = ground track; no 500 ft auto switch; lesson-forced Track forces it too
        10d7ad6e 2026-11-03 Real strut stations for every braced type; Pitts slave-strut horns made parallel
        3a2c895f 2026-11-03 Camera: CameraReference.cs.meta (same guid as island-nav d8201d8d)
        d8201d8d 2026-11-03 Camera: CameraReference.cs.meta from the Unity build; AGENT-LOG: CAM button on iPhone 2026.1103.1715
        c7d60932 2026-11-03 Camera: Flight Path / Track is a full-time CAM button (Ty 2026-11-03), not an automatic switch below 500 ft
        94fe5aca 2026-11-03 Camera: Flight Path / Track is a full-time CAM button (Ty 2026-11-03), not an automatic switch below 500 ft
        53bb28e1 2026-11-03 AGENT-LOG: contact patches, wing-panel damage, slave struts, prop strike (NEEDS YOU, NEEDS-Codex, Log)
        07aa0b54 2026-11-03 Unity: tip-panel break and bend visuals, crunch, placeholder scrape sound and dust
        e899a77f 2026-11-03 Slave strut: exact 1:1 while both wings are straight; ground-loop probe stops at airframe contact
        b1c00bca 2026-11-03 Crash debris: cut only what is on screen, keep every UV and material slot, prebuilt-debris hook
        0a5a7e79 2026-11-03 DebrisPaintCheck: reproduce the Pitts break-up drawing the old procedural Pitts and losing paint
        fedf7209 2026-11-03 Contact patches: one stiffness per patch, flexible tips, dihedral convention, bracing
        d5e5617c 2026-11-03 Tests: contact patches, bend/break thresholds, slave strut, stick-free aileron, prop strike
        2e718862 2026-11-03 Sim: panels bend and break on strike load, slave struts, prop strike, scrape events
        fa706d9a 2026-11-03 Core: contact patches on every tip, wing panels and slave struts
WARN  57 commit(s) on lane/claude-quest-islandnav touch SHARED paths and are not on lane/claude-island-nav (iOS is missing them)
        bf0484a8 2026-11-01 Fly it from inside a Cardboard headset: side-by-side stereo with a real head
            21 shared file(s): configs/vr-cardboard.json src/FlyingGame.Core/Vr.meta src/FlyingGame.Core/Vr/VrHead.cs 
        ba390084 2026-11-02 Fly it in a Meta Quest 3S: native OpenXR on Android, the head at the seat's eye
            12 shared file(s): src/FlyingGame.Core/Vr/QuestControls.cs src/FlyingGame.Core/Vr/QuestControls.cs.meta src/FlyingGame.Core/Vr/XrHeadPose.cs 
        8e1fa871 2026-11-02 Put the pilot in the seat when he presses FLY on the Quest: read StreamingAssets from inside the APK
            8 shared file(s): unity/Assets/_Project/Scripts/Bridge/AirframeRig.cs unity/Assets/_Project/Scripts/Bridge/Commerce/EntitlementRuntime.cs unity/Assets/_Project/Scripts/Bridge/CreditsPanel.cs 
        235b2332 2026-11-02 Sit the pilot in the seat he chose: keep the seat by its ID per aircraft, not an index
            6 shared file(s): src/FlyingGame.Core/SeatChoice.cs src/FlyingGame.Core/SeatChoice.cs.meta unity/Assets/_Project/Scripts/Bridge/CockpitView.cs 
        0a1a7b47 2026-11-02 Quest: no HUD in the cockpit unless asked, a menu he can find, the Seat row for the aircraft that wi
            4 shared file(s): src/FlyingGame.Core/Vr/QuestCockpit.cs src/FlyingGame.Core/Vr/QuestCockpit.cs.meta src/FlyingGame.Core/Vr/QuestControls.cs 
        ef0c47ac 2026-11-02 Quest: record what you see from the controllers (hold A + B), saved in the headset, pulled with ques
            6 shared file(s): src/FlyingGame.Core/Vr/QuestControls.cs src/FlyingGame.Core/Vr/QuestRecording.cs src/FlyingGame.Core/Vr/QuestRecording.cs.meta 
        bad3a452 2026-11-02 VR cockpit phase 1 visuals: connected Pitts truss, rounded rear panel, canopy bows and sliding ball 
            20 shared file(s): docs/models/ea300-own-rig.json docs/models/pitts-s2b-own-rig.json src/FlyingGame.Core/ModelRig.cs 
        0421e36f 2026-11-02 Grab the aeroplane's own controls in the headset: stick, throttle, canopy latches, and a body you ca
            16 shared file(s): src/FlyingGame.Core/ModelRig.cs src/FlyingGame.Core/Vr/CanopyLatch.cs src/FlyingGame.Core/Vr/CanopyLatch.cs.meta 
        930fd5fb 2026-11-02 Quest: a steady frame, a cockpit that moves, a throttle you can find, a card that stays still
            20 shared file(s): src/FlyingGame.Core/CockpitLive.cs src/FlyingGame.Core/CockpitLive.cs.meta src/FlyingGame.Core/ModelRig.cs 
        0c0d882f 2026-11-02 VR cockpit phase 2 models: low Pitts canopy bows with a short latch pin, harnesses, headset jacks, h
            19 shared file(s): docs/models/ea300-own-rig.json docs/models/pitts-s2b-own-rig.json tools/own_models/check_vr_phase2.py 
        f238050a 2026-11-02 Quest: USB rudder pedals — rudder and differential toe brakes through the one control path, mapping 
            8 shared file(s): src/FlyingGame.Core/RudderPedals.cs src/FlyingGame.Core/RudderPedals.cs.meta unity/Assets/_Project/Scripts/Bridge/FlyingGame.Bridge.asmdef 
        45d26542 2026-11-02 vr-cockpit p2c (Codex): Pitts arched panels, polished rim, concentric bows, sliding ball latch; Extr
            14 shared file(s): docs/models/ea300-own-rig.json docs/models/pitts-s2b-own-rig.json tools/own_models/check_vr_phase2.py 
        43959565 2026-11-02 Bail out by hand in the headset: canopy in the airload, belts, headset cord, climb out, free fall, r
            17 shared file(s): src/FlyingGame.Core/ModelRig.cs src/FlyingGame.Core/Vr/CanopyHinge.cs src/FlyingGame.Core/Vr/CanopyHinge.cs.meta 
        e14b800a 2026-11-02 vr-cockpit p2d (Codex): detachable canopy roots with mass/inertia, split and torn hinges, Pitts red 
            14 shared file(s): docs/models/ea300-own-rig.json docs/models/pitts-s2b-own-rig.json tools/own_models/check_vr_phase2.py 
        911fb2f1 2026-11-02 vr-cockpit p2e (Codex): Quest triangle budget for canopy tubes, rims and bows (Pitts own glb 7.16 MB
            11 shared file(s): tools/own_models/vr_cockpit.py tools/own_models/vr_phase2.py tools/own_models/vr_phase2d.py 
        55954abb 2026-11-02 VR cockpit P3a: every control a moving part you can reach — toggles, rotaries, push-pulls with pivot
            17 shared file(s): docs/models/ea300-own-rig.json docs/models/great-lakes-own-rig.json docs/models/pitts-s2b-own-rig.json 
        796fad8f 2026-11-02 Quest: the VR menu opens in flight again — it rode the aeroplane and every FLY destroyed it; a MENU 
            2 shared file(s): src/FlyingGame.Core/Vr/QuestCockpit.cs src/FlyingGame.Core/Vr/QuestControls.cs 
        1ab29ee9 2026-11-02 Pitts: the stick and pedals are cut from every mesh glTFast made of the Cockpit node, not only its f
            3 shared file(s): src/FlyingGame.Core/CockpitLive.cs unity/Assets/_Project/Scripts/Bridge/AirframeVisual.cs unity/Assets/_Project/Scripts/Bridge/CockpitLiveParts.cs 
        3a9e746b 2026-11-02 Extra 300 panel rebuilt on one plane: ASI, altimeter, turn coordinator and G meter above; tach, MP, 
            9 shared file(s): docs/models/ea300-own-rig.json docs/models/great-lakes-own-rig.json tools/own_models/check_vr_phase3.py 
        9f7c0f81 2026-11-02 Extra canopy latch v2, the canopy flung open and torn away in flight, and the jettison knob
            14 shared file(s): src/FlyingGame.Core/Vr/CanopyHinge.cs src/FlyingGame.Core/Vr/CanopyLatch.cs src/FlyingGame.Core/Vr/CanopyRelease.cs 
        d936b103 2026-11-02 Great Lakes from the photos: frameless one-piece windshields (front lower and flatter), main gear on
            8 shared file(s): tools/own_models/check_gl_p3c.py tools/own_models/render_gl_p3c.py tools/own_models/vr_cockpit.py 
        159e5f46 2026-11-02 Quest: the slowdown near the ground — ground contact once a step, not at every RK4 stage; [QuestPerf
            9 shared file(s): src/FlyingGame.Core/AirframeContact.cs src/FlyingGame.Core/IslandCavities.cs src/FlyingGame.Core/IslandTerrain.cs 
        5d028681 2026-11-02 2-33: yellow only, everywhere — the red-and-white 2-33 is gone
            4 shared file(s): src/FlyingGame.Core/LiveryChoice.cs unity/Assets/_Project/Scripts/Bridge/AirframeModels.cs unity/Assets/_Project/Scripts/Bridge/AirframeVisual.cs 
        1084a620 2026-11-02 Quest: the headset lists only our own cockpits with a checked eye — 2-33, Extra, Pitts, Great Lakes
            3 shared file(s): src/FlyingGame.Core/Vr/QuestFleet.cs src/FlyingGame.Core/Vr/QuestFleet.cs.meta tools/own_models/check_eye_interiors.py 
        147d2682 2026-11-02 Great Lakes: main gear braced by a V of tubes from the axle up to the belly fittings, windshields as
            6 shared file(s): tools/own_models/check_gl_p3c.py tools/own_models/render_gl_p3c.py tools/own_models/vr_phase3c.py 
        eeb12758 2026-11-02 Quest: a gentler stick — heavy expo and a 0.05 dead zone by default; a Bluetooth gamepad flies throu
            3 shared file(s): src/FlyingGame.Core/Vr/QuestControls.cs unity/Assets/_Project/Scripts/Bridge/OptionsPanel.cs unity/Assets/_Project/Scripts/Bridge/SessionSettings.cs 
        8e7862eb 2026-11-02 Quest gamepad: right stick flies, left stick rudder + throttle — Ty's all-platform mapping
            1 shared file(s): src/FlyingGame.Core/Vr/QuestControls.cs 
        43ee4dbb 2026-11-02 Quest clips: the sight and true water in recordings, 48 kHz sound, adb record start/stop, a [QuestPe
            6 shared file(s): src/FlyingGame.Core/Vr/QuestRecording.cs unity/Assets/_Project/Scripts/Bridge/AirframeModels.cs unity/Assets/_Project/Scripts/Bridge/AirframeVisual.cs 
        6e05ff66 2026-11-02 Quest: see your own hands and work the 2-33's dive brakes, trim and tow release by hand (pinch, fist
            10 shared file(s): src/FlyingGame.Core/Vr/CockpitGrab.cs src/FlyingGame.Core/Vr/CockpitLevers.cs src/FlyingGame.Core/Vr/CockpitLevers.cs.meta 
        a8174695 2026-11-02 J-3: our own model on wheels and on floats, driven by its own rig
            7 shared file(s): docs/models/j3-cub-floats-own-rig.json docs/models/j3-cub-own-rig.json src/FlyingGame.Core/ModelRig.cs 
        9e5f31ad 2026-11-02 check_eye_interiors: the J-3 on wheels and floats, in the cabin, what it sees, and its floats agains
            1 shared file(s): tools/own_models/check_eye_interiors.py 
        e0c4d22c 2026-11-02 Quest: the J-3 on wheels and on floats, from the rear seat
            2 shared file(s): src/FlyingGame.Core/Vr/QuestFleet.cs unity/Assets/_Project/Scripts/Bridge/SessionSettings.cs 
        b2a53770 2026-11-02 Quest: the switches and knobs by hand on the Pitts, Extra and Great Lakes — poke a toggle, twist a k
            6 shared file(s): src/FlyingGame.Core/ModelRig.cs src/FlyingGame.Core/Vr/CockpitGrab.cs src/FlyingGame.Core/Vr/PanelControls.cs 
        7f40cbab 2026-11-02 J-3 panel lettering: larger, higher-contrast gauge text for the rear seat (Codex)
            2 shared file(s): tools/own_models/j3_textures.py tools/own_models/textures/j3_panel_atlas.png 
        2558d73d 2026-11-02 J-3 cockpit v16 (Ty): ceiling tubes under the skylight, mag switch on the left root rib, no rear car
            5 shared file(s): docs/models/j3-cub-floats-own-rig.json docs/models/j3-cub-own-rig.json tools/own_models/build_j3_cub.py 
        439649ba 2026-11-02 J-3 v16 consumers: a test pins Ty's cockpit changes; provenance and the eye check's note brought up 
            1 shared file(s): tools/own_models/check_eye_interiors.py 
        b23d2996 2026-11-02 J-3 v16 renders + docs: before/after from the approved rear eye, README, NEEDS YOU and the log
            1 shared file(s): tools/own_models/j3_cockpit_shots.py 
        42b0090c 2026-11-02 Quest: on the ground every hard point asked all 1,475 solids at every RK4 stage; solids and decks fi
            8 shared file(s): src/FlyingGame.Core/AirframeContact.cs src/FlyingGame.Core/Forest.cs src/FlyingGame.Core/IslandGeometry.cs 
        842e5610 2026-11-02 Quest startup: say where the first frame goes ([Startup] scene phases, [Airframe] model ready)
            2 shared file(s): unity/Assets/_Project/Scripts/Bridge/AirframeVisual.cs unity/Assets/_Project/Scripts/Bridge/SceneBootstrap.cs 
        9ca7fdce 2026-11-02 J-3 cockpit v17 (Ty): mag switch abeam the front seat back, 12 gal nose tank with beads and a clear 
            6 shared file(s): docs/models/j3-cub-floats-own-rig.json docs/models/j3-cub-own-rig.json tools/own_models/build_j3_cub.py 
        f8e20a3b 2026-11-02 J-3 floats water rudder: drawn-blade hydrodynamics (½ρV²·S_imm·a·α, Helmbold slope), retract state (
            5 shared file(s): configs/aircraft/j3-cub-floats-like.json src/FlyingGame.Core/DataContracts/AircraftConfig.cs src/FlyingGame.Core/FloatHydro.cs 
        67d087c2 2026-11-02 J-3 floats model: red WATER RUDDER handle, retract cable, seat-back hook; blade retract 140° on wate
            5 shared file(s): docs/models/j3-cub-floats-own-rig.json tools/own_models/build_j3_cub.py tools/own_models/j3_rig_json.py 
        35a453f4 2026-11-02 J-3 floats water rudder in the game: rig param, cable follows the handle, Hold/Release grab API, W.R
            4 shared file(s): unity/Assets/_Project/Scripts/Bridge/AirframeVisual.cs unity/Assets/_Project/Scripts/Bridge/CockpitWaterRudder.cs unity/Assets/_Project/Scripts/Bridge/CockpitWaterRudder.cs.meta 
        f1c85ad8 2026-11-02 VR hands: the stick and throttle bind to every aeroplane's own rig (raked 2-hinge gimbal, hinged thr
            5 shared file(s): src/FlyingGame.Core/Vr/CockpitGrab.cs src/FlyingGame.Core/Vr/HandControls.cs src/FlyingGame.Core/Vr/HandPadArbiter.cs 
        f3525594 2026-11-02 Pilot body v2: one smooth skinned normal-mapped mesh (8k tris, 5 mats); fix left shoulder in front o
            5 shared file(s): tools/own_models/pilot_body.py tools/own_models/pilot_body_smooth.py unity/Assets/_Project/Resources/Models/pilot-body/pilot-body-own.glb 
        5c96a4ff 2026-11-02 Extra 300: the rear panel top (hood + glareshield) P3b deleted is back, seated on the one-plane pane
            5 shared file(s): tools/own_models/vr_cockpit.py tools/own_models/vr_phase3d.py unity/Assets/_Project/Resources/Models/extra-300-like/extra-300-like-own-n300uy.glb 
        b4366b43 2026-11-02 2-33: front eye 25 mm higher (z -0.111 -> -0.136) per Ty; eyepoints v6, config + StreamingAssets + p
            1 shared file(s): configs/aircraft/glider-2-33-like.json 
        29c55998 2026-11-02 J-3 cockpit v18 (Ty t282u-t286u): black V tubes to the floor under the tank, panel TACH-ASI-COMPASS-
            10 shared file(s): configs/aircraft/j3-cub-floats-like.json docs/models/j3-cub-floats-own-rig.json docs/models/j3-cub-own-rig.json 
        fe843d2a 2026-11-02 Tracked hands: late grab for a hand that closed just short of the control, flight controls over the 
            2 shared file(s): src/FlyingGame.Core/Vr/CockpitGrab.cs unity/Assets/_Project/Scripts/Bridge/TouchFlightControls.cs 
        d0517239 2026-11-02 Extra 300: rear throttle down onto the left side wall with a gate plate; canopy handles 25x127 mm la
            9 shared file(s): docs/models/ea300-own-rig.json tools/own_models/check_vr_phase2.py tools/own_models/check_vr_phase3.py 
        0f50686d 2026-11-02 2-33 canopy: remove the yellow windscreen bow and forward sill rails, clear to the fuselage (Ty t295
            5 shared file(s): tools/own_models/build_sgs233.py tools/own_models/check_sgs233_canopy.py tools/own_models/render_233_canopy.py 
        5f5b6a85 2026-11-02 PilotBodyShots: BODY_ONLY / BODY_EYE_UP_M / BODY_HIDE / BODY_LEVEL for before/after shots (2-33 eye,
            1 shared file(s): unity/Assets/_Project/Scripts/Editor/PilotBodyShots.cs 
        7e42784c 2026-11-02 PilotBodyShots: BODY_SOLO (body alone + pedal close-ups, aircraft culled near the feet), BODY_PEDALS
            1 shared file(s): unity/Assets/_Project/Scripts/Editor/PilotBodyShots.cs 
        f32ebca5 2026-11-02 Pilot body v3 candidate (unshipped): feet flat on the pedal face, real boots, shoulders, glove detai
            4 shared file(s): tools/own_models/check_vr_phase1.py tools/own_models/pilot_body.py tools/own_models/pilot_body_smooth.py 
        0c4b3c1f 2026-11-03 Core: QuestCockpit.FlyByHandDefault (off) and HandsInCockpit rule, pinned in QuestCockpitTests
            1 shared file(s): src/FlyingGame.Core/Vr/QuestCockpit.cs 
        a726d882 2026-11-03 Ship pilot body v3 (Ty approved 2026-11-03): pilot_body.py --offline builds v3 into Resources (byte-
            3 shared file(s): tools/own_models/check_vr_phase1.py tools/own_models/pilot_body.py unity/Assets/_Project/Resources/Models/pilot-body/pilot-body-own.glb 
        88dcc1fa 2026-11-03 Quest garbage after the island-nav merge: no foreach over IReadOnlyList in BiplaneCellule.Covers / C
            3 shared file(s): src/FlyingGame.Core/Aero/BiplaneCellule.cs src/FlyingGame.Core/AirframeContact.cs src/FlyingGame.Sim/Aircraft.cs 
INFO  8 Quest-only commit(s) (XR/Quest paths only), 17 touching neither (log, docs, renders)
WARN  GLBs: 13 identical, 14 different, 2 on one lane only
        DIFFERENT      extra-300-like/extra-300-like-own-n300uy.glb  iOS ab063033  Quest e9ca8844
        DIFFERENT      extra-300-like/extra-300-like-own-unmarked.glb  iOS 3e1ae25e  Quest 5b4b81b8
        DIFFERENT      extra-300-like/extra-300-like-own.glb  iOS 1e4aa262  Quest 583853c0
        DIFFERENT      glider-2-33-like/glider-2-33-like-own-tf.glb  iOS 404eab60  Quest 72a8d946
        DIFFERENT      glider-2-33-like/glider-2-33-like-own.glb  iOS 2666ee98  Quest d0b479da
        DIFFERENT      great-lakes-like/great-lakes-like-own-red-sunburst.glb  iOS 0864297d  Quest 738318c4
        DIFFERENT      great-lakes-like/great-lakes-like-own-white-stripes.glb  iOS 864ae371  Quest b25c324e
        DIFFERENT      great-lakes-like/great-lakes-like-own.glb  iOS 3f9406b8  Quest 13c47afb
        DIFFERENT      j3-cub-floats-like/j3-cub-floats-like-own.glb  iOS 7b60636b  Quest b06d6f16
        DIFFERENT      j3-cub-like/j3-cub-like-own.glb  iOS 150a3340  Quest c62fa42a
        DIFFERENT      pitts-s2b-like/pitts-s2b-like-own-checker-unmarked.glb  iOS e120cbca  Quest a7233743
        DIFFERENT      pitts-s2b-like/pitts-s2b-like-own-factory-unmarked.glb  iOS 02cac6aa  Quest 69801a3c
        DIFFERENT      pitts-s2b-like/pitts-s2b-like-own-n1191.glb  iOS cee5c724  Quest e118ff4d
        DIFFERENT      pitts-s2b-like/pitts-s2b-like-own.glb  iOS 319bac6b  Quest 5ba509d6
        only on Quest  pilot-body/pilot-body-own.glb
        only on Quest  pilot-gear/pilot-gear-own.glb
INFO  cockpit/art lanes with commits in neither lane ($PARITY_ART_RE):
        lane/claude-172-instruments                   17  b2e84ee0 2026-11-04
        lane/claude-233-default                        2  da5c8f05 2026-11-02
        lane/claude-233-launch                         2  ea11fb0c 2026-11-03
        lane/claude-extra-cockpit-shared               3  a7803dde 2026-11-04
        lane/claude-floor-windows                      5  744bf584 2026-11-02
        lane/claude-front-sight                        1  9b220a99 2026-11-01
        lane/claude-grob103                           13  d7962cdd 2026-11-01
        lane/claude-j3-cockpit                         2  e718ee51 2026-11-02
        lane/claude-quest-cockpit-switches            32  033dd76d 2026-11-03
        lane/claude-swift-gear                         6  2cde4d70 2026-11-01
        lane/claude-vr-cockpit                         1  45bf124f 2026-11-02
        lane/codex-172-cockpit                         2  758c4955 2026-11-03
        lane/codex-beaver-cockpit                      4  75560f66 2026-11-03
        lane/codex-eyepoints                           7  aab015b8 2026-10-31
        lane/codex-floor-windows                       1  312885a8 2026-11-02
        lane/codex-front-sight                         3  c87e4464 2026-11-01
        lane/codex-greatlakes-cockpit                 25  9dd328dc 2026-11-03
        lane/codex-grob103                            13  919da6c7 2026-11-03
        lane/codex-own-swift                           9  07dddf4a 2026-11-03
        lane/codex-pitts-debris                        2  020655ef 2026-11-03
        lane/codex-swift-gear                          7  14a84728 2026-11-01
PARITY: FAIL — merge lane/claude-island-nav into lane/claude-quest-islandnav (and port the shared Quest-only work to lane/claude-island-nav) before a Quest build. docs/PARITY.md
```
