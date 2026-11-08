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
#   4. Cockpit/art lanes with commits in NEITHER lane (info: art waiting to be merged into both).
#
#   tools/check-parity.sh --hook   the parity LOCK, run by tools/githooks/pre-commit and pre-merge-commit on the commit
#                                  being made (see the --hook block below and docs/PARITY.md, "The lock").
#
# Branches: arguments, else $PARITY_IOS_BRANCH / $PARITY_QUEST_BRANCH, else lane/claude-island-nav and
# lane/claude-quest-islandnav. Art lanes: local lane/* branches matching $PARITY_ART_RE.
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
HEAD_BRANCH=$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo "")
case "$HEAD_BRANCH" in
  ""|HEAD|*quest*) IOS_DEFAULT=${PARITY_IOS_BRANCH:-lane/claude-island-nav} ;;
  *)               IOS_DEFAULT=${PARITY_IOS_BRANCH:-$HEAD_BRANCH} ;;
esac
IOS=${ARGS[0]:-$IOS_DEFAULT}
QUEST=${ARGS[1]:-${PARITY_QUEST_BRANCH:-lane/claude-quest-islandnav}}
ART_RE=${PARITY_ART_RE:-'cockpit|instrument|pitts|greatlakes|great-lakes|grob|extra|j3|233|swift|beaver|172|eyepoint|floor-windows|front-sight|livery|model'}
MODELS=unity/Assets/_Project/Resources/Models

QUEST_ONLY_RE='^(unity/Assets/XR/|unity/Assets/Plugins/Android/|unity/Assets/_Project/Scripts/Quest/|unity/Assets/_Project/Scripts/Bridge/(Quest|Vr|Xr|UnityVrLens)[^/]*$|unity/Assets/_Project/Scripts/Editor/(Quest|Vr)[^/]*$|unity/Packages/|unity/ProjectSettings/XR|tools/deploy/[^/]*quest[^/]*$)'
SHARED_RE='^(configs/|src/|unity/Assets/StreamingAssets/aircraft/|unity/Assets/_Project/Resources/Models/|tools/own_models/|docs/models/[^/]*rig[^/]*\.json$|unity/Assets/_Project/Scripts/|unity/Assets/_Project/Shaders/|unity/Assets/Plugins/iOS/|unity/Assets/Plugins/macOS/)'

# ---- --hook: the parity lock (tools/githooks/pre-commit and pre-merge-commit) ----------------------------------------
# Runs on a commit being made, against the INDEX. No-op on every branch but the two lanes. On lane X (the other lane Y):
#   allow  the commit changes no shared path (logs, docs, renders, Quest-only files);
#   allow  it merges Y's tip in (MERGE_HEAD == Y): that is how the lanes are synced, so it can never deadlock;
#   allow  its shared paths come out identical to Y's;
#   allow  X and Y were identical in shared paths before it (a lead commit: Y must merge X next);
#   BLOCK  otherwise: X already differs from Y in shared code and this commit adds more. Merge Y into X (or X into Y)
#          first. Never bypass with --no-verify (docs/PARITY.md).
if [ "$HOOK" = 1 ]; then
  branch=$(git symbolic-ref -q --short HEAD) || exit 0
  if [ "$branch" = "$IOS" ]; then other=$QUEST; elif [ "$branch" = "$QUEST" ]; then other=$IOS; else exit 0; fi
  if ! git rev-parse -q --verify "$other^{commit}" >/dev/null; then echo "parity hook: $other does not exist; not checked" >&2; exit 0; fi
  git rev-parse -q --verify HEAD >/dev/null || exit 0
  shared() { grep -Ev "$QUEST_ONLY_RE" | grep -E "$SHARED_RE"; }
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

# ---- 4. art lanes in neither -------------------------------------------------------------------------------------
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
if [ "$MISSING_ALL" -gt 0 ] || { [ "$STRICT" = 1 ] && [ $((SHARED_N + GLB_DIFF + GLB_ONLY)) -gt 0 ]; }; then
  echo "PARITY: FAIL — merge $IOS into $QUEST (and port the shared Quest-only work to $IOS) before a Quest build. docs/PARITY.md"
  exit 1
fi
if [ $((SHARED_N + GLB_DIFF + GLB_ONLY)) -gt 0 ]; then
  echo "PARITY: OK with warnings — $SHARED_N shared commit(s) and $((GLB_DIFF + GLB_ONLY)) GLB(s) differ. docs/PARITY.md"
else
  echo "PARITY: OK"
fi
exit 0
