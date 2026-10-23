#!/bin/zsh
# Snapshot assets-incoming/ to a tarball on two disks, or check whether the newest snapshot is still current.
#
#   tools/backup-assets.sh          check the newest snapshot against what is on disk (read-only)
#   tools/backup-assets.sh make     make a new snapshot, dated today, on both disks
#
# WHY THIS EXISTS. assets-incoming/ has zero tracked files, so git and GitHub do not have it. It is the
# downloaded source meshes plus the .txt licence file beside each one, and those licence files are the only
# record of what terms a mesh came under -- re-downloading a .glb is easy, reconstructing whether helijah's
# Read-Me said GPL while Sketchfab said CC BY is not. build/ is deliberately NOT backed up: it regenerates
# from these sources plus tools/.
#
# A snapshot is not a sync. Download a new mesh and the newest tarball silently stops being the truth, which
# is the failure this script's `check` mode exists to make loud instead of silent.
set -e
set -o pipefail

# Derive the repo root from where this script actually lives, rather than hardcoding it. ${0:A} resolves
# symlinks, so this gives /Users/tyfrisby/Developer/flying-game even when the script is reached through the
# ~/Documents/flying-game symlink -- which is the point. Eight other tracked scripts hardcode the old
# Documents path and work only while that symlink resolves; this is the pattern that fixes them.
REPO="${0:A:h:h}"
cd "$REPO"

SRC=assets-incoming
# Overridable only so this script can be exercised against a scratch directory; the defaults are the real
# destinations and nothing in the repo sets these.
LOCAL="${ASSETS_BACKUP_DIR:-$HOME/Developer/backups}"
EXT="${ASSETS_BACKUP_EXT:-/Volumes/T5 EVO/flying-game-backups}"
PREFIX=assets-incoming

[ -d "$SRC" ] || { print -u2 "no $SRC in $REPO -- wrong repo?"; exit 2; }

# Hash every file, relative to the repo root, in a stable order. Both modes use this, so a check compares
# like with like.
hash_tree() { find "$SRC" -type f -print0 | sort -z | xargs -0 shasum -a 256; }

# (N) is zsh's null-glob qualifier and (om) sorts newest first. Without (N) zsh fails the glob itself and
# prints its own "no matches found" before this function's own message gets a chance to say it clearly.
newest_manifest() {
  local -a m
  m=( "$LOCAL/$PREFIX-"*.manifest.txt(Nom) )
  (( $#m )) && print -r -- "$m[1]"
}

case "${1:-check}" in

check)
  MAN="$(newest_manifest)" || true
  if [ -z "$MAN" ]; then
    print -u2 "NO SNAPSHOT EXISTS in $LOCAL -- run: tools/backup-assets.sh make"
    exit 2
  fi
  NOW=$(mktemp -t assets-now)
  hash_tree > "$NOW"
  if cmp -s "$NOW" "$MAN"; then
    print "assets-incoming matches $(basename $MAN) -- $(wc -l < "$MAN" | tr -d ' ') files, snapshot is current"
    rm -f "$NOW"
    exit 0
  fi
  # Report by PATH, so a changed file is distinguished from a new one. Comparing field 2 of each line.
  print -u2 "SNAPSHOT IS STALE -- $(basename $MAN) no longer describes assets-incoming:"
  join -v2 -j2 <(sort -k2 "$MAN") <(sort -k2 "$NOW") | awk '{ print "  new:     " $1 }' >&2 || true
  join -v1 -j2 <(sort -k2 "$MAN") <(sort -k2 "$NOW") | awk '{ print "  gone:    " $1 }' >&2 || true
  join -j2 <(sort -k2 "$MAN") <(sort -k2 "$NOW") | awk '$2 != $3 { print "  changed: " $1 }' >&2 || true
  print -u2 "run: tools/backup-assets.sh make"
  rm -f "$NOW"
  exit 1
  ;;

make)
  STAMP=$(date +%Y-%m-%d)
  mkdir -p "$LOCAL"
  TAR="$LOCAL/$PREFIX-$STAMP.tar.gz"
  MAN="$LOCAL/$PREFIX-$STAMP.manifest.txt"

  hash_tree > "$MAN"
  print "manifest: $(wc -l < "$MAN" | tr -d ' ') files"

  # COPYFILE_DISABLE + --no-mac-metadata: without them tar embeds ._AppleDouble members and the extracted tree
  # no longer matches the manifest.
  # gzip -n, and gzip piped rather than tar -z: gzip writes the CURRENT TIME into its header by default, so
  # two runs over byte-identical input produce archives of identical size and different sha256. Measured here
  # on 2026-10-23: 127448134 bytes both times, 35e66778... vs 17a3b89d... -n drops that timestamp, which makes
  # the archive hash mean "these are the same assets" instead of "these were packed at the same second".
  COPYFILE_DISABLE=1 tar --no-mac-metadata -cf - "$SRC" | gzip -n > "$TAR"
  (cd "$LOCAL" && shasum -a 256 "$(basename $TAR)" > "$(basename $TAR).sha256")

  # Verify by actually extracting and checking all of it. An archive nobody has restored is a guess.
  V=$(mktemp -d -t assets-verify)
  tar -xzf "$TAR" -C "$V"
  FAILED=$(cd "$V" && shasum -a 256 -c "$MAN" 2>&1 | grep -c FAILED || true)
  rm -rf "$V"
  [ "$FAILED" -eq 0 ] || { print -u2 "ARCHIVE DOES NOT MATCH THE MANIFEST ($FAILED files) -- not trusting it"; exit 1; }
  print "local:    $TAR ($(du -h "$TAR" | cut -f1)) verified, all files match"

  # The external drive is the copy that survives this Mac. Local-only is better than nothing but is not the
  # point, so say so loudly and exit non-zero rather than let a half-done backup look finished.
  if [ ! -d "${EXT:h}" ]; then
    print -u2 "T5 EVO NOT MOUNTED -- local copy only. Plug it in and re-run to get the off-machine copy."
    exit 3
  fi
  mkdir -p "$EXT"
  for f in "$PREFIX-$STAMP.tar.gz" "$PREFIX-$STAMP.manifest.txt" "$PREFIX-$STAMP.tar.gz.sha256"; do
    cp -p "$LOCAL/$f" "$EXT/$f"
  done
  # if/then, not `[ -f ] && cp`: a bare failing test returns 1 and `set -e` would abort a good run here.
  if [ -f "$LOCAL/README.txt" ]; then cp -p "$LOCAL/README.txt" "$EXT/README.txt"; fi
  # Re-hash the copy where it now sits. cp reporting success is not evidence the bytes landed intact, and a
  # .sha256 holding an absolute path would quietly re-verify the local file instead of this one.
  (cd "$EXT" && shasum -a 256 -c "$PREFIX-$STAMP.tar.gz.sha256") >/dev/null
  print "external: $EXT/$PREFIX-$STAMP.tar.gz verified off the drive"

  N=$(ls "$LOCAL/$PREFIX-"*.tar.gz 2>/dev/null | wc -l | tr -d ' ')
  print "$N snapshot(s) kept, $(du -sh "$LOCAL" | cut -f1) in $LOCAL -- nothing is pruned automatically"
  ;;

*)
  print -u2 "usage: tools/backup-assets.sh [check|make]"; exit 2 ;;
esac
