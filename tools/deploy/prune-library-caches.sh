#!/bin/zsh
# Cap the per-lane Unity Library caches in ~/Library/Application Support/FlyingGame/unity-Library-<lane>.
#
# Every lane worktree's unity/Library is a symlink to its own cache there (3-9 GB each, ~24 GB for an Android /
# Quest lane, because Bee holds the gradle build). Nothing ever removed one, and on 2026-11-02 the folder was
# 165 GB on a disk 97% full. deploy.sh runs this before every build; it can also be run by hand.
#
#   tools/deploy/prune-library-caches.sh [--dry-run] [--keep N] [--max-gb G] [--current DIR] [--root DIR]
#                                        [--no-trim] [--trim-idle-hours H]
#
#   FG_CACHE_KEEP=8          keep at most this many caches (the most recently used, by the cache dir's mtime --
#                            deploy.sh touches the current lane's cache, so "used" means "built from")
#   FG_CACHE_MAX_GB=80       and keep their total under this many GB (du)
#   FG_CACHE_TRIM=1          allow TRIMMING: emptying a cache's build output -- PlayerDataCache Bee il2cpp_cache
#                            BuildPlayerData -- which deploy.sh and deploy-quest.sh `rm -rf` at the start of EVERY
#                            build anyway (17 of an Android cache's 24 GB is Bee). Artifacts, PackageCache and the
#                            asset databases -- the slow parts to rebuild -- are never trimmed. 0 = never trim.
#   FG_CACHE_TRIM_IDLE_HOURS=6  caches idle this long are trimmed even when under the size cap
#
# Order, cheapest loss first:
#   1. count cap: past the KEEP most recently used, delete (oldest first);
#   2. trim the survivors idle >= FG_CACHE_TRIM_IDLE_HOURS;
#   3. still over MAX_GB: trim more survivors, oldest first (costs that lane one script recompile);
#   4. still over MAX_GB: delete whole survivors, oldest first (costs that lane a full re-import).
#   FG_CACHE_ROOT            the folder (default ~/Library/Application Support/FlyingGame)
#   FG_CACHE_DRY_RUN=1       same as --dry-run: print what would go, touch nothing
#
# NEVER removed or trimmed:
#   - the current lane's cache (--current, default: where ./unity/Library points);
#   - any cache in use by a running Unity / xcodebuild / bee_backend / gradle / java / dotnet: its pid's argv or
#     cwd names the cache or a worktree whose unity/Library points at it (Unity's -projectPath), or lsof shows it
#     holding a file inside the cache. Checked once up front and AGAIN right before each removal.
#   - anything that is not a real directory named unity-Library-* (the main checkout's plain unity-Library is
#     not matched; symlinks are skipped).
# A cache is renamed to .pruning-* first (atomic) and only then removed, so nothing can open a half-deleted one.
# Sizes are du sizes; caches made with `cp -c` are APFS clones that share blocks, so the disk space actually
# returned (measured with df, printed as "disk freed") can be less than the du total.
# A lane whose cache was removed is not broken: deploy.sh recreates a missing cache dir, and Unity re-imports.
# Exit 0 always unless the arguments are bad -- a prune must never stop a deploy.
emulate -L zsh
setopt null_glob

ROOT=${FG_CACHE_ROOT:-"$HOME/Library/Application Support/FlyingGame"}
KEEP=${FG_CACHE_KEEP:-8}
MAX_GB=${FG_CACHE_MAX_GB:-80}
TRIM=${FG_CACHE_TRIM:-1}
TRIM_IDLE_H=${FG_CACHE_TRIM_IDLE_HOURS:-6}
DRY=${FG_CACHE_DRY_RUN:-0}
CURRENT=""
while (( $# )); do
  case $1 in
    --dry-run) DRY=1 ;;
    --keep) KEEP=$2; shift ;;
    --max-gb) MAX_GB=$2; shift ;;
    --current) CURRENT=$2; shift ;;
    --root) ROOT=$2; shift ;;
    --no-trim) TRIM=0 ;;
    --trim-idle-hours) TRIM_IDLE_H=$2; shift ;;
    -h|--help) sed -n '2,40p' "${0:A}"; exit 0 ;;
    *) echo "prune-library-caches: unknown argument $1" >&2; exit 64 ;;
  esac
  shift
done
[[ $KEEP == <-> && $MAX_GB == <-> && $TRIM_IDLE_H == <-> ]] || { echo "prune-library-caches: --keep, --max-gb and --trim-idle-hours take whole numbers" >&2; exit 64; }
[ -d "$ROOT" ] || { echo "== cache-prune: no cache folder at $ROOT, nothing to do"; exit 0; }
ROOT=${ROOT:A}
TAG="== cache-prune"; (( DRY )) && TAG="== cache-prune [dry-run]"
TRIM_DIRS=(PlayerDataCache Bee il2cpp_cache BuildPlayerData)

# The current lane: --current, else ./unity/Library's target if it is under ROOT.
if [ -z "$CURRENT" ] && [ -e unity/Library ]; then CURRENT=unity/Library; fi
[ -n "$CURRENT" ] && CURRENT=${CURRENT:A}

# One prune at a time (two lanes deploying at once must not race over the same folder).
LOCK="$ROOT/.prune-lock"
if (( ! DRY )); then
  if ! mkdir "$LOCK" 2>/dev/null; then
    # A lock older than an hour is a crashed run.
    if [[ -n $(find "$LOCK" -maxdepth 0 -mmin +60 2>/dev/null) ]]; then rmdir "$LOCK" 2>/dev/null; mkdir "$LOCK" 2>/dev/null || exit 0
    else echo "$TAG: another prune is running, skipping"; exit 0; fi
  fi
  trap 'rmdir "$LOCK" 2>/dev/null' EXIT
fi
# Leftovers from an interrupted removal.
for z in "$ROOT"/.pruning-*(N/); do (( DRY )) || rm -rf "$z"; done

# ---- which caches are in use right now ----
# Worktree -> cache map, from every git worktree's unity/Library symlink (a running Unity names the worktree in
# -projectPath, not the cache).
typeset -A WT_CACHE
REPO=${0:A:h:h:h}
for wt in ${(f)"$(git -C "$REPO" worktree list --porcelain 2>/dev/null | sed -n 's/^worktree //p')"}; do
  [ -L "$wt/unity/Library" ] || continue
  WT_CACHE[$wt]=${$(readlink "$wt/unity/Library"):A}
done
# A snapshot of every build-ish process: argv, cwd, and the files it holds open. Taken up front, and again right
# before each removal.
typeset -A P_ARGS P_CWD P_FILES
snapshot() {
  P_ARGS=(); P_CWD=(); P_FILES=()
  local pid args
  local pids=(${(f)"$(pgrep -x Unity; pgrep -x xcodebuild; pgrep -x bee_backend; pgrep -x java; pgrep -f gradle; pgrep -f 'dotnet.*Unity\.app'; pgrep -f 'FlyingGame/unity-Library')"})
  for pid in ${(u)pids}; do
    [[ $pid == $$ ]] && continue
    args=$(ps -o args= -p $pid 2>/dev/null) || continue
    [[ $args == *prune-library-caches* ]] && continue
    P_ARGS[$pid]=$args
    P_CWD[$pid]=$(lsof -a -p $pid -d cwd -Fn 2>/dev/null | sed -n 's/^n//p')
    P_FILES[$pid]=$(lsof -p $pid -Fn 2>/dev/null | sed -n 's/^n//p' | grep -F "$ROOT/unity-Library-")
  done
}
in_use() {  # $1 = cache dir (resolved). Prints the reason and returns 0 when in use.
  local c=$1 pid args cwd wt
  for pid in ${(k)P_ARGS}; do
    args=${P_ARGS[$pid]}; cwd=${P_CWD[$pid]}
    if [[ $args == *"$c"* || $cwd == "$c" || $cwd == "$c"/* ]]; then echo "pid $pid ${${(z)args}[1]:t}"; return 0; fi
    for wt in ${(k)WT_CACHE}; do
      [[ ${WT_CACHE[$wt]} == "$c" ]] || continue
      if [[ $args == *"$wt/unity"* || $cwd == "$wt" || $cwd == "$wt"/* ]]; then echo "pid $pid ${${(z)args}[1]:t} in ${wt:t}"; return 0; fi
    done
    if print -r -- "${P_FILES[$pid]}" | grep -qF "$c/"; then echo "pid $pid ${${(z)args}[1]:t} holds a file in it"; return 0; fi
  done
  return 1
}

gb() { printf '%.1f' $(( $1 / 1048576.0 )); }   # KB -> GB
free_kb() { df -k "$ROOT" | awk 'NR==2 { print $4 }'; }
mtime() { stat -f %m "$1"; }

# ---- the caches, most recently used first ----
typeset -A SIZE MT
CACHES=()
for d in "$ROOT"/unity-Library-*(N/); do
  [ -L "$d" ] && continue
  CACHES+=$d
  MT[$d]=$(mtime "$d")
done
(( ${#CACHES} )) || { echo "$TAG: no unity-Library-* caches in $ROOT"; exit 0; }
CACHES=(${(f)"$(for d in $CACHES; do print -r -- "${MT[$d]} $d"; done | sort -rn | cut -d' ' -f2-)"})
for d in $CACHES; do SIZE[$d]=$(du -sk "$d" 2>/dev/null | cut -f1); done

snapshot
typeset -A PROTECT
for d in $CACHES; do
  if [[ $d == "$CURRENT" ]]; then PROTECT[$d]="current lane"
  elif why=$(in_use "$d"); then PROTECT[$d]="in use: $why"; fi
done

TOTAL=0; for d in $CACHES; do (( TOTAL += SIZE[$d] )); done
echo "$TAG: ${#CACHES} caches, $(gb $TOTAL) GB (du) in $ROOT -- cap: $KEEP caches, $MAX_GB GB"
NOW=$(date +%s)
for d in $CACHES; do
  printf '     %6s GB  last used %s  %-36s %s\n' $(gb ${SIZE[$d]}) "$(date -r ${MT[$d]} '+%m-%d %H:%M')" "${d:t}" "${PROTECT[$d]:+[keep: ${PROTECT[$d]}]}"
done

# What each unprotected cache could shed by trimming its build output (see FG_CACHE_TRIM above).
typeset -A CAN_TRIM CAN_TXT TRIM_KB EFF
for d in $CACHES; do
  EFF[$d]=${SIZE[$d]}
  (( TRIM )) || continue
  [ -n "${PROTECT[$d]}" ] && continue
  t=0; parts=()
  for p in $TRIM_DIRS; do
    [ -d "$d/$p" ] || continue
    s=$(du -sk "$d/$p" | cut -f1); (( t += s )); parts+="$p $(gb $s)"
  done
  (( t >= 102400 )) || continue   # under 100 MB: not worth it
  CAN_TRIM[$d]=$t; CAN_TXT[$d]=${(j:, :)parts}
done
plan_trim() { TRIM_KB[$1]=${CAN_TRIM[$1]}; (( EFF[$1] = SIZE[$1] - CAN_TRIM[$1] )); }

FREED_KB=0
DF_BEFORE=$(free_kb)
remove() {  # $1 = cache, $2 = reason
  local d=$1 why
  (( DRY )) || snapshot
  if why=$(in_use "$d"); then echo "$TAG: KEEP ${d:t} -- it came into use ($why)"; return 1; fi
  echo "$TAG: delete ${d:t} ($(gb ${SIZE[$d]}) GB) -- $2"
  (( FREED_KB += SIZE[$d] ))
  (( DRY )) && return 0
  local z="$ROOT/.pruning-${d:t}-$$"
  mv "$d" "$z" && rm -rf "$z"
}

# 1. Count cap: the protected caches always stay and count toward KEEP; then the newest of the rest.
KEPT=()
for d in $CACHES; do [ -n "${PROTECT[$d]}" ] && KEPT+=$d; done
for d in $CACHES; do
  [ -n "${PROTECT[$d]}" ] && continue
  if (( ${#KEPT} < KEEP )); then KEPT+=$d
  else remove "$d" "older than the $KEEP most recently used" || KEPT+=$d; fi
done
kept_total() { local d t=0; for d in $KEPT; do (( t += EFF[$d] )); done; echo $t; }
# 2. Survivors idle >= TRIM_IDLE_H lose their build output regardless of the size cap.
for d in $KEPT; do
  (( ${CAN_TRIM[$d]:-0} && NOW - MT[$d] >= TRIM_IDLE_H * 3600 )) && plan_trim "$d"
done
MAX_KB=$(( MAX_GB * 1048576 ))
KEPT_TOTAL=$(kept_total)
# 3. Size cap, cheapest first: trim build output from the oldest survivors (a deploy deletes it anyway, so the
#    cost is one script recompile the next time that lane runs Unity) ...
for d in ${(Oa)CACHES}; do
  (( KEPT_TOTAL > MAX_KB )) || break
  (( ${KEPT[(Ie)$d]} && ${CAN_TRIM[$d]:-0} && ! ${TRIM_KB[$d]:-0} )) || continue
  plan_trim "$d"; KEPT_TOTAL=$(kept_total)
done
# 4. ... and only then delete whole caches, oldest first (that lane re-imports everything next time).
for d in ${(Oa)CACHES}; do   # oldest first
  (( KEPT_TOTAL > MAX_KB )) || break
  [ -n "${PROTECT[$d]}" ] && continue
  (( ${KEPT[(Ie)$d]} )) || continue
  if remove "$d" "kept total $(gb $KEPT_TOTAL) GB is over $MAX_GB GB"; then
    KEPT[${KEPT[(ie)$d]}]=(); KEPT_TOTAL=$(kept_total)
  fi
done
# 5. Carry out the planned trims on the survivors.
for d in $KEPT; do
  (( ${TRIM_KB[$d]:-0} )) || continue
  (( DRY )) || snapshot
  if why=$(in_use "$d"); then echo "$TAG: KEEP ${d:t} build output -- it came into use ($why)"; (( KEPT_TOTAL += TRIM_KB[$d] )); continue; fi
  echo "$TAG: trim ${d:t}: ${CAN_TXT[$d]} GB (idle $(( (NOW - MT[$d]) / 60 )) min; every deploy deletes these first anyway)"
  (( FREED_KB += TRIM_KB[$d] ))
  (( DRY )) && continue
  ref="$ROOT/.prune-mtime-$$"; touch -r "$d" "$ref"
  for p in $TRIM_DIRS; do rm -rf "$d/$p"; done
  touch -r "$ref" "$d"; rm -f "$ref"   # trimming must not make the cache look recently used
done
(( KEPT_TOTAL > MAX_KB )) && echo "$TAG: still $(gb $KEPT_TOTAL) GB -- what is left is current or in use"

if (( DRY )); then
  echo "$TAG: would free $(gb $FREED_KB) GB (du); would leave ${#KEPT} caches, $(gb $KEPT_TOTAL) GB"
else
  echo "$TAG: freed $(gb $FREED_KB) GB (du), disk freed $(gb $(( $(free_kb) - DF_BEFORE ))) GB (df); ${#KEPT} caches, $(gb $KEPT_TOTAL) GB left"
fi
exit 0
