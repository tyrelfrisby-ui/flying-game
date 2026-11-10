#!/bin/bash
# iOS/Mac and Quest must stay in sync at all times (Ty 2026-11-04, after the Quest build turned out to have none of
# the Pitts panel and canopy work). This checks the two lanes against each other. Read-only: it never merges,
# checks out or fetches; it reads branch refs, so it works from any worktree. docs/PARITY.md has the rule.
#
#   tools/check-parity.sh [--strict] [--brief] [IOS_BRANCH [QUEST_BRANCH]]
#
#   1. Every commit of the iOS lane must be in the Quest lane.            FAIL (exit 1) if not, with the list.
#   2. Quest-lane commits not in the iOS lane, split by what they touch:   WARN; FAIL with --strict if any touches
#      SHARED paths (configs/, src/, Resources/Models, rig json, tools/own_models, shared Bridge scripts and shaders).
#      QUEST-ONLY paths (XR packages and settings, Android plugins, Scripts/Quest, the Quest*/Vr*/Xr* scripts, the
#      Quest deploy scripts) are allowed to live on the Quest lane alone.
#   3. Every GLB under unity/Assets/_Project/Resources/Models, blob against blob.  WARN; FAIL with --strict.
#   4. The Aero Widget's surface (Scripts/Bridge/Widget, _Project/Widget, tools/deploy/widget.sh, tools/native-mac,
#      docs/WIDGET.md), blob against blob. WARN; FAIL with --strict. The widget is the same project, scene and src/
#      as the app, so it cannot be a third branch target: see the long note at check 4 for what that does and does not
#      catch, and docs/PARITY.md "The widget".
#   5. Cockpit/art lanes with commits in NEITHER lane (info: art waiting to be merged into both).
#
#   tools/check-parity.sh --hook   the parity LOCK, run by tools/githooks/pre-commit and pre-merge-commit on the commit
#                                  being made. On a lane/* branch: keep the two lanes in sync. On a TRUNK
#                                  ($PARITY_TRUNK_RE: main, master, split-surfaces): no shared change may ORIGINATE
#                                  there. Anything else: no-op. See the --hook block below and docs/PARITY.md,
#                                  "The lock" and "Trunk branches".
#
# Branches: arguments, else $PARITY_IOS_BRANCH / $PARITY_QUEST_BRANCH, else this worktree's own branch for the iOS
# lane (from a Quest worktree OR a trunk, the lane most recently merged into the Quest lane) and
# lane/claude-quest-islandnav for the Quest lane. Art lanes: local lane/* branches matching $PARITY_ART_RE.
# Exit: 0 in sync (warnings allowed), 1 out of sync, 2 a branch does not exist.
set -u
STRICT=0; BRIEF=0; HOOK=0; ARGS=()
for a in "$@"; do
  case "$a" in
    --strict) STRICT=1 ;;
    --brief) BRIEF=1 ;;
    --hook) HOOK=1 ;;
    -h|--help) sed -n '2,23p' "$0"; exit 0 ;;
    *) ARGS+=("$a") ;;
  esac
done
# The iOS lane defaults to THE BRANCH THIS WORKTREE IS ON, not to a lane named here. Ty 2026-11-08: the default
# was lane/claude-island-nav, which had stopped being the iOS lane weeks earlier, so every deploy printed
# "PARITY: OK" while comparing two branches nobody was building — a day's work drifted out of the Quest lane
# with the gate green the whole time. A name in a script goes stale; the branch you are building cannot.
# (A Quest lane building itself still compares against the named iOS lane: there is nothing else to mean.)
#
# A QUEST WORKTREE CANNOT NAME ITSELF, so it used to fall back to the literal lane/claude-island-nav -- the very name
# the paragraph above says goes stale, and it does: on 2026-11-09 a merge FROM the Quest side was blocked against that
# dead branch, and the only way through was to name the real lane by hand. So the Quest side now READS the answer out
# of its own history: the iOS lane is the local lane/* branch that was most recently merged into the Quest lane, which
# is by definition the lane it is being kept in sync with. If that cannot be determined, this exits 2 and says so
# rather than comparing two branches nobody is building: a gate that cannot tell what it is guarding must not go green.
HEAD_BRANCH=$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo "")
QUEST=${ARGS[1]:-${PARITY_QUEST_BRANCH:-lane/claude-quest-islandnav}}

# TRUNKS (Ty 2026-11-10, asked what to do about it: "Install enforcement on main too"). main and split-surfaces are
# neither a lane nor a feature branch, and until this change none of this apparatus existed on them at all: no
# docs/PARITY.md, no check-parity.sh, no tools/githooks/, so the dispatcher in the common hooks directory found nothing
# to run and exited 0. That is worse than it sounds, because the MAIN CHECKOUT is where the iOS build and the OTA
# publish are run from, and it sits on split-surfaces. Shared code committed there can be built, flown and installed
# on a phone without ever having been in either lane -- which is precisely the failure the rule was written for
# ("it seems like all of the pitts panel and canopy work did not make it to the quest build", 2026-11-04).
#
# A FEATURE BRANCH IS NOT A TRUNK, and the 15 codex-* branches, wip/* and the spin-* experiments are deliberately left
# out: a feature branch's shared work meets the lock at the moment it is merged into a lane, and nobody builds from
# it. A trunk has neither protection -- no lane merge will ever see it, and it is the thing being built. So the trunks
# are NAMED, not guessed: a guess wide enough to catch split-surfaces catches all 15 codex branches with it, and this
# rule is not for them. $PARITY_TRUNK_RE widens the list without editing this file.
TRUNK_RE=${PARITY_TRUNK_RE:-'^(main|master|split-surfaces)$'}
IS_TRUNK=0
if [ -n "$HEAD_BRANCH" ] && printf '%s' "$HEAD_BRANCH" | grep -Eq "$TRUNK_RE"; then IS_TRUNK=1; fi

# The lane most recently merged into $1: walk its merge commits newest first and take the first second parent that a
# local non-Quest lane/* branch contains.
parity_lane_merged_into() {
  local q=$1 m sp b
  for m in $(git rev-list --merges --max-count=60 "$q" 2>/dev/null); do
    sp=$(git rev-parse -q --verify "$m^2" 2>/dev/null) || continue
    for b in $(git for-each-ref --format='%(refname:short)' 'refs/heads/lane/*' 2>/dev/null); do
      case "$b" in *quest*) continue ;; esac
      if git merge-base --is-ancestor "$sp" "$b" 2>/dev/null; then echo "$b"; return 0; fi
    done
  done
  return 1
}

case "$HEAD_BRANCH" in
  ""|HEAD|*quest*)
    IOS_DEFAULT=${PARITY_IOS_BRANCH:-}
    if [ -z "$IOS_DEFAULT" ]; then IOS_DEFAULT=$(parity_lane_merged_into "$QUEST" || true); fi
    if [ -z "$IOS_DEFAULT" ] && [ ${#ARGS[@]} -eq 0 ]; then
      echo "parity: cannot tell which lane is the iOS lane from here ($HEAD_BRANCH): no lane/* branch has been merged" >&2
      echo "parity: into $QUEST. Name it: PARITY_IOS_BRANCH=lane/<name> $0 ... (docs/PARITY.md)" >&2
      exit 2
    fi
    ;;
  *)
    # A TRUNK CANNOT NAME ITSELF EITHER, for the same reason a Quest worktree cannot: "the iOS lane" would come out
    # split-surfaces, and the report would then list the hundreds of lane commits split-surfaces is behind and tell
    # the reader to merge split-surfaces into the Quest lane. That is not advice, it is noise, and deploy.sh prints
    # this on every iOS build from the main checkout. So a trunk reads the answer out of the Quest lane's history,
    # exactly as a Quest worktree does.
    # Not in --hook: resolving it walks 60 merges against 224 branches and measures 4.2 s on this repository, which is
    # 4.2 s added to EVERY commit in the main checkout. The trunk lock below compares nothing against a lane, so it
    # does not need the answer; the report does.
    if [ "$IS_TRUNK" = 1 ] && [ "$HOOK" = 0 ] && [ -z "${PARITY_IOS_BRANCH:-}" ] && [ ${#ARGS[@]} -eq 0 ]; then
      IOS_DEFAULT=$(parity_lane_merged_into "$QUEST" || true)
      if [ -z "$IOS_DEFAULT" ]; then
        echo "parity: cannot tell which lane is the iOS lane from here ($HEAD_BRANCH is a trunk, not a lane): no" >&2
        echo "parity: lane/* branch has been merged into $QUEST. Name it: PARITY_IOS_BRANCH=lane/<name> $0 ..." >&2
        exit 2
      fi
    else
      IOS_DEFAULT=${PARITY_IOS_BRANCH:-$HEAD_BRANCH}
    fi
    ;;
esac
IOS=${ARGS[0]:-$IOS_DEFAULT}
ART_RE=${PARITY_ART_RE:-'cockpit|instrument|pitts|greatlakes|great-lakes|grob|extra|j3|233|swift|beaver|172|eyepoint|floor-windows|front-sight|livery|model'}
MODELS=unity/Assets/_Project/Resources/Models

QUEST_ONLY_RE='^(unity/Assets/XR/|unity/Assets/Plugins/Android/|unity/Assets/_Project/Scripts/Quest/|unity/Assets/_Project/Scripts/Bridge/(Quest|Vr|Xr|UnityVrLens)[^/]*$|unity/Assets/_Project/Scripts/Editor/(Quest|Vr)[^/]*$|unity/Packages/|unity/ProjectSettings/XR|tools/deploy/[^/]*quest[^/]*$)'
SHARED_RE='^(configs/|src/|unity/Assets/StreamingAssets/aircraft/|unity/Assets/_Project/Resources/Models/|tools/own_models/|docs/models/[^/]*rig[^/]*\.json$|unity/Assets/_Project/Scripts/|unity/Assets/_Project/Shaders/|unity/Assets/Plugins/iOS/|unity/Assets/Plugins/macOS/)'

# ---- --hook: the parity lock (tools/githooks/pre-commit and pre-merge-commit) ----------------------------------------
# Runs on a commit being made, against the INDEX. There are THREE classes of branch and two different rules.
#
# A LANE (any lane/* branch, or a branch named outright in $PARITY_IOS_BRANCH). On lane X, with the other lane Y:
#   allow  the commit changes no shared path (logs, docs, renders, Quest-only files);
#   allow  it merges Y's tip in (MERGE_HEAD == Y): that is how the lanes are synced, so it can never deadlock;
#   allow  its shared paths come out identical to Y's;
#   allow  X and Y were identical in shared paths before it (a lead commit: Y must merge X next);
#   BLOCK  otherwise: X already differs from Y in shared code and this commit adds more. Merge Y into X (or X into Y)
#          first. Never bypass with --no-verify (docs/PARITY.md).
# This fires on ALL ~224 lane/* branches, not only the two shipping lanes, because the iOS lane defaults to the branch
# the worktree is on. That is deliberate as of 2026-11-10: it is what stopped several agents shipping unsynced work.
# Its surprising consequence -- a feature lane that is merely BEHIND the Quest lane is blocked -- and the remedy are
# written up in docs/PARITY.md, "The lock".
#
# A TRUNK ($PARITY_TRUNK_RE: main, master, split-surfaces). A DIFFERENT rule, because the lane rule is unanswerable
# here. The lane rule's remedy is always "sync with the other lane", and a trunk has no other lane: it is not one of
# the pair, it is hundreds of commits behind both, and merging a trunk into a shipping lane is not a thing anyone
# wants. What a trunk CAN be held to is the rule Ty already wrote in prose (docs/PARITY.md rule 3: "Every change lands
# on [the iOS lane] first and is merged into [the Quest lane] the SAME DAY, or it lands on a shared lane that is
# merged into both"): shared work does not ORIGINATE on a trunk.
#   allow  the commit changes no shared path. This is the common case in the main checkout and it is why the rule is
#          affordable: AGENT-LOG.md, docs, renders and tools/deploy/ are all outside SHARED_RE, so the log entry and
#          the deploy scripts that checkout exists to run are untouched by this;
#   allow  it is a merge (MERGE_HEAD set, whatever the other side is): the shared content is arriving from a branch
#          that met the lock on its own way in, and a hook that can block a merge can deadlock a repository;
#   allow  PARITY_OVERRIDE=1, which is the escape hatch deploy-quest.sh already uses for the same rule, and which
#          like that one is supposed to come with an AGENT-LOG line saying why;
#   BLOCK  otherwise: make the change on a lane. There is no "identical to the other lane" allowance here on purpose:
#          a trunk is never identical to a shipping lane in shared paths, so such an allowance would be a branch that
#          can never be taken -- a check that reads green forever, which is worse than no check (see the widget note
#          at check 4 for the same argument made about SHARED_RE).
#
# ANYTHING ELSE (codex-*, wip/*, the spin-* experiments, a detached HEAD): no-op. A feature branch's shared work meets
# the lock when it is merged into a lane, and nobody builds from one.
if [ "$HOOK" = 1 ]; then
  branch=$(git symbolic-ref -q --short HEAD) || exit 0
  shared() { grep -Ev "$QUEST_ONLY_RE" | grep -E "$SHARED_RE"; }
  case "$branch" in
    lane/*) ;;
    *)
      if [ -n "${PARITY_IOS_BRANCH:-}" ] && [ "$branch" = "$PARITY_IOS_BRANCH" ]; then
        : # named outright as the iOS lane: it gets the lane rule below, as it always has
      elif [ "$IS_TRUNK" = 1 ]; then
        git rev-parse -q --verify HEAD >/dev/null || exit 0
        trunk_changed=$(git diff --cached --name-only HEAD | shared)
        [ -z "$trunk_changed" ] && exit 0
        git rev-parse -q --verify MERGE_HEAD >/dev/null 2>&1 && exit 0
        if [ "${PARITY_OVERRIDE:-}" = 1 ]; then
          echo "parity hook: PARITY_OVERRIDE=1 — shared change committed on the trunk $branch anyway" >&2
          echo "parity hook: ($(printf '%s\n' "$trunk_changed" | grep -c .) file(s)). Put the reason in AGENT-LOG.md, and get it onto both lanes today." >&2
          exit 0
        fi
        {
          echo "PARITY HOOK: BLOCKED — $branch is a TRUNK, not a lane, and this commit changes shared code."
          echo "Shared work does not start here: it lands on a lane and is merged into both shipping lanes the same"
          echo "day (docs/PARITY.md rule 3). A commit on a trunk reaches neither lane, and this checkout is the one"
          echo "that builds and publishes — so it would fly on a phone and be in no lane at all."
          echo "  shared files in this commit ($(printf '%s\n' "$trunk_changed" | grep -c .)):"
          printf '%s\n' "$trunk_changed" | head -10 | sed 's/^/      /'
          echo "  Do it on a lane instead:  git worktree add /private/tmp/<name> -b lane/<name> <the iOS lane>"
          echo "  (tools/check-parity.sh, run with no arguments from here, names the lane pair.)"
          echo "  Everything NOT in SHARED_RE still commits here freely: AGENT-LOG.md, docs/, renders, tools/deploy/."
          echo "  PARITY_OVERRIDE=1 git commit ... forces it, with an AGENT-LOG line. Never --no-verify."
        } >&2
        exit 1
      else
        exit 0
      fi
      ;;
  esac
  if [ "$branch" = "$IOS" ]; then other=$QUEST; elif [ "$branch" = "$QUEST" ]; then other=$IOS; else exit 0; fi
  if ! git rev-parse -q --verify "$other^{commit}" >/dev/null; then echo "parity hook: $other does not exist; not checked" >&2; exit 0; fi
  git rev-parse -q --verify HEAD >/dev/null || exit 0
  changed=$(git diff --cached --name-only HEAD | shared)
  [ -z "$changed" ] && exit 0
  mh=$(git rev-parse -q --verify MERGE_HEAD 2>/dev/null)
  [ -n "$mh" ] && [ "$mh" = "$(git rev-parse "$other")" ] && exit 0
  after=$(git diff --cached --name-only "$other" | shared)
  [ -z "$after" ] && exit 0
  before=$(git diff --name-only HEAD "$other" | shared)
  if [ -z "$before" ]; then
    echo "parity hook: $branch leads $other by this commit's shared change ($(printf '%s\n' "$after" | grep -c .) file(s))." >&2
    echo "parity hook: merge $branch into $other next (git -C <$other worktree> merge --no-ff $branch); until then" >&2
    echo "parity hook: $branch takes no further shared change. docs/PARITY.md" >&2
    exit 0
  fi
  {
    echo "PARITY HOOK: BLOCKED — $branch already differs from $other in shared code, and this commit changes shared"
    echo "paths too. Every commit to either lane must keep them in sync (docs/PARITY.md)."
    echo "  shared files that differ from $other now ($(printf '%s\n' "$before" | grep -c .)):"
    printf '%s\n' "$before" | head -10 | sed 's/^/      /'
    echo "  To sync: commit nothing shared here; in the $other worktree run  git merge --no-ff $branch"
    echo "  (or here:  git merge --no-ff $other), resolve, commit. Then commit this change. Never --no-verify."
  } >&2
  exit 1
fi

for b in "$IOS" "$QUEST"; do
  if ! git rev-parse -q --verify "$b^{commit}" >/dev/null; then echo "check-parity: no such branch '$b'" >&2; exit 2; fi
done
say() { [ "$BRIEF" = 1 ] || echo "$@"; }

echo "== parity: iOS $IOS ($(git rev-parse --short "$IOS")) vs Quest $QUEST ($(git rev-parse --short "$QUEST"))"
# Printed even in --brief, which is what deploy.sh shows, because for a build out of the main checkout this is the
# line that matters: the lane pair below can be perfectly in sync and still have nothing to do with what is about to
# be put on a phone.
if [ "$IS_TRUNK" = 1 ]; then
  echo "WARN  this checkout is on $HEAD_BRANCH, a TRUNK and not a lane: whatever is built here is in NEITHER lane."
  echo "      The pair below is the lane pair, not what you are building. docs/PARITY.md, \"Trunk branches\"."
fi

# ---- 1. iOS commits the Quest lane does not have ----------------------------------------------------------------
MISSING_ALL=$(git rev-list --count "$QUEST..$IOS")
MISSING=$(git rev-list --count --no-merges "$QUEST..$IOS")
if [ "$MISSING_ALL" -gt 0 ]; then
  echo "FAIL  $MISSING commit(s) of $IOS are not in $QUEST ($MISSING_ALL counting merges):"
  if [ "$BRIEF" = 1 ]; then git log --no-merges --format='        %h %s' "$QUEST..$IOS" | head -8; [ "$MISSING" -gt 8 ] && echo "        ... and $((MISSING - 8)) more"
  else git log --no-merges --format='        %h %ad %s' --date=short "$QUEST..$IOS"; fi
else
  echo "OK    every commit of $IOS is in $QUEST"
fi

# ---- 2. Quest-lane commits not on iOS, by what they touch ------------------------------------------------------
SHARED_N=0; QONLY_N=0; OTHER_N=0; SHARED_LIST=""
for c in $(git rev-list --reverse --no-merges "$IOS..$QUEST"); do
  files=$(git diff-tree --no-commit-id --name-only -r "$c")
  shared=$(printf '%s\n' "$files" | grep -Ev "$QUEST_ONLY_RE" | grep -E "$SHARED_RE")
  if [ -n "$shared" ]; then
    SHARED_N=$((SHARED_N + 1))
    n=$(printf '%s\n' "$shared" | grep -c .)
    SHARED_LIST+="        $(git log -1 --format='%h %ad %s' --date=short "$c" | cut -c1-120)"$'\n'
    SHARED_LIST+="            $n shared file(s): $(printf '%s\n' "$shared" | head -3 | tr '\n' ' ')"$'\n'
  elif printf '%s\n' "$files" | grep -Eq "$QUEST_ONLY_RE"; then QONLY_N=$((QONLY_N + 1))
  else OTHER_N=$((OTHER_N + 1)); fi
done
if [ "$SHARED_N" -gt 0 ]; then
  [ "$STRICT" = 1 ] && tag=FAIL || tag=WARN
  echo "$tag  $SHARED_N commit(s) on $QUEST touch SHARED paths and are not on $IOS (iOS is missing them)"
  if [ "$BRIEF" = 0 ]; then printf '%s' "$SHARED_LIST"; fi
else
  echo "OK    no commit on $QUEST alone touches shared paths"
fi
say "INFO  $QONLY_N Quest-only commit(s) (XR/Quest paths only), $OTHER_N touching neither (log, docs, renders)"

# ---- 3. GLBs, blob against blob ----------------------------------------------------------------------------------
glbs() { git ls-tree -r "$1" -- "$MODELS" | awk '$4 ~ /\.glb$/ { print $4 "\t" $3 }' | sort; }
G_IOS=$(mktemp); G_Q=$(mktemp); trap 'rm -f "$G_IOS" "$G_Q"' EXIT
glbs "$IOS" > "$G_IOS"; glbs "$QUEST" > "$G_Q"
GLB_SAME=0; GLB_DIFF=0; GLB_ONLY=0; GLB_LIST=""
while IFS=$'\t' read -r path blob; do
  other=$(awk -F'\t' -v p="$path" '$1 == p { print $2 }' "$G_Q")
  if [ -z "$other" ]; then GLB_ONLY=$((GLB_ONLY + 1)); GLB_LIST+="        only on iOS    ${path#$MODELS/}"$'\n'
  elif [ "$other" = "$blob" ]; then GLB_SAME=$((GLB_SAME + 1))
  else GLB_DIFF=$((GLB_DIFF + 1)); GLB_LIST+="        DIFFERENT      ${path#$MODELS/}  iOS ${blob:0:8}  Quest ${other:0:8}"$'\n'; fi
done < "$G_IOS"
while IFS=$'\t' read -r path blob; do
  grep -q "^$path	" "$G_IOS" || { GLB_ONLY=$((GLB_ONLY + 1)); GLB_LIST+="        only on Quest  ${path#$MODELS/}"$'\n'; }
done < "$G_Q"
if [ $((GLB_DIFF + GLB_ONLY)) -gt 0 ]; then
  [ "$STRICT" = 1 ] && tag=FAIL || tag=WARN
  echo "$tag  GLBs: $GLB_SAME identical, $GLB_DIFF different, $GLB_ONLY on one lane only"
  if [ "$BRIEF" = 0 ]; then printf '%s' "$GLB_LIST"; fi
else
  echo "OK    GLBs: all $GLB_SAME identical"
fi

# ---- 4. the Aero Widget's surface, blob against blob -------------------------------------------------------------
# Ty 2026-11-10: "Keep the aero widget in parity with the app" ... "Enforce it like the others."
#
# WHY THIS IS A BLOB CHECK AND NOT A THIRD REGEX. The widget is not a third target. It is the SAME Unity project, the
# SAME scene (Assets/Scenes/Main.unity) and the SAME src/, built by BuildScript.BuildWidget with PlayerSettings flipped,
# and chosen at runtime by bundle id (SceneBootstrap -> AeroWidget.IsWidget, ".aerowidget"). So there is no widget-side
# copy of anything for a shared change to SKIP, and the thing the Quest half of this script measures -- commit
# reachability between two branches -- has no widget analogue, because the widget has no branch. A third SHARED_RE arm
# would be a check that could not fire. What CAN drift is the widget's own files between the two lanes, exactly as a GLB
# can; and three of the paths it is built from are NEUTRAL to the lock above (matched by neither regex, so the hook
# never sees them): unity/Assets/_Project/Widget/, tools/deploy/widget.sh and tools/native-mac/ -- the last being the
# SOURCE of unity/Assets/Plugins/macOS/AeroNative.bundle, whose committed product IS shared. Source neutral, product
# locked. The gap is not hypothetical: on 2026-11-10 lane/claude-island-nav and the Quest lane differed by 98 lines of
# tools/native-mac/ and one line of GameAeroPanel.cs with the lock green throughout. So measure the surface instead.
#
# Deliberately a WARN (FAIL only under --strict, which is what deploy-quest.sh and the lanes run), and deliberately NOT
# added to SHARED_RE: the --hook block treats EVERY lane/* branch as the iOS lane, so widening the regex would make
# every lane in the repo stop and sync with the Quest lane before it could touch a Mac-only build script. That is far
# more than this rule needs. Mac is one of the three platforms, so Mac-only code belongs in the parity REPORT; only
# code that ships to iOS and Quest belongs in the commit LOCK.
#
# STILL NOT COVERED, and no path rule can cover it: Ty's earlier rule (2026-10-09, quoted in AeroHost.cs and
# GameAeroPanel.cs) that "the GAME and the WIDGET stay in PARITY -- one implementation, two hosts", i.e. a feature added
# to AeroWidget/WidgetServer and never surfaced in GameAeroHost/GameAeroPanel. Those files sit side by side in ONE
# directory on ONE branch, so no path set can tell "added to the widget host" from "added to both". That needs a
# set-equality check over the capability surface both hosts publish -- WidgetServer's hello reply (features, show keys,
# inset ids, autopilotModes, lessons) against GameAeroPanel's switch ids -- with WidgetServer.Protocol bumped when the
# set changes. docs/PARITY.md, "The widget", records that as the open piece and why it is not this script's job.
WIDGET_PATHS="unity/Assets/_Project/Scripts/Bridge/Widget unity/Assets/_Project/Widget tools/deploy/widget.sh tools/native-mac docs/WIDGET.md"
surface() { git ls-tree -r "$1" -- $WIDGET_PATHS | awk '{ print $4 "\t" $3 }' | sort; }
W_IOS=$(mktemp); W_Q=$(mktemp); trap 'rm -f "$G_IOS" "$G_Q" "$W_IOS" "$W_Q"' EXIT
surface "$IOS" > "$W_IOS"; surface "$QUEST" > "$W_Q"
WIDGET_SAME=0; WIDGET_DIFF=0; WIDGET_ONLY=0; WIDGET_LIST=""
while IFS=$'\t' read -r path blob; do
  other=$(awk -F'\t' -v p="$path" '$1 == p { print $2 }' "$W_Q")
  if [ -z "$other" ]; then WIDGET_ONLY=$((WIDGET_ONLY + 1)); WIDGET_LIST+="        only on iOS    $path"$'\n'
  elif [ "$other" = "$blob" ]; then WIDGET_SAME=$((WIDGET_SAME + 1))
  else WIDGET_DIFF=$((WIDGET_DIFF + 1)); WIDGET_LIST+="        DIFFERENT      $path  iOS ${blob:0:8}  Quest ${other:0:8}"$'\n'; fi
done < "$W_IOS"
while IFS=$'\t' read -r path blob; do
  grep -q "^$path	" "$W_IOS" || { WIDGET_ONLY=$((WIDGET_ONLY + 1)); WIDGET_LIST+="        only on Quest  $path"$'\n'; }
done < "$W_Q"
if [ $((WIDGET_DIFF + WIDGET_ONLY)) -gt 0 ]; then
  [ "$STRICT" = 1 ] && tag=FAIL || tag=WARN
  echo "$tag  widget: $WIDGET_SAME identical, $WIDGET_DIFF different, $WIDGET_ONLY on one lane only"
  if [ "$BRIEF" = 0 ]; then printf '%s' "$WIDGET_LIST"; fi
  echo "      the widget is built from the same scene and src/ as the app, so the lanes must agree on it. docs/PARITY.md"
else
  echo "OK    widget: all $WIDGET_SAME files identical"
fi

# ---- 5. art lanes in neither -------------------------------------------------------------------------------------
if [ "$BRIEF" = 0 ]; then
  echo "INFO  cockpit/art lanes with commits in neither lane (\$PARITY_ART_RE):"
  ANY=0
  for b in $(git for-each-ref --format='%(refname:short)' refs/heads/lane/ | grep -E "$ART_RE"); do
    [ "$b" = "$IOS" ] || [ "$b" = "$QUEST" ] || {
      n=$(git rev-list --count --no-merges "$b" "^$IOS" "^$QUEST")
      [ "$n" -gt 0 ] && { ANY=1; printf '        %-44s %3d  %s\n' "$b" "$n" "$(git log -1 --format='%h %ad' --date=short "$b")"; }
    }
  done
  [ "$ANY" = 1 ] || echo "        none"
fi

# ---- verdict -----------------------------------------------------------------------------------------------------
WIDGET_OFF=$((WIDGET_DIFF + WIDGET_ONLY))
if [ "$MISSING_ALL" -gt 0 ] || { [ "$STRICT" = 1 ] && [ $((SHARED_N + GLB_DIFF + GLB_ONLY + WIDGET_OFF)) -gt 0 ]; }; then
  echo "PARITY: FAIL — merge $IOS into $QUEST (and port the shared Quest-only work to $IOS) before a Quest build. docs/PARITY.md"
  exit 1
fi
if [ $((SHARED_N + GLB_DIFF + GLB_ONLY + WIDGET_OFF)) -gt 0 ]; then
  echo "PARITY: OK with warnings — $SHARED_N shared commit(s), $((GLB_DIFF + GLB_ONLY)) GLB(s) and $WIDGET_OFF widget file(s) differ. docs/PARITY.md"
else
  echo "PARITY: OK"
fi
exit 0
