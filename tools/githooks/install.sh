#!/bin/bash
# Install the parity lock for EVERY worktree of this repository: one small dispatcher per hook in the common git
# directory (worktrees share it). The dispatcher runs the checked-out worktree's own tools/githooks/<hook> when that
# file exists, and does nothing otherwise, so branches that predate the lock are unaffected.
# core.hooksPath is deliberately NOT used: it would turn off any hook in .git/hooks for every branch.
#
# "DOES NOTHING OTHERWISE" IS THE WHOLE INSTALL PROBLEM, and on 2026-11-10 it was the hole. The dispatcher was
# installed, but main and split-surfaces carried no tools/githooks/ and no tools/check-parity.sh, so in the MAIN
# CHECKOUT -- the one that runs the iOS build and the OTA publish -- it found nothing to run and exited 0. Installing
# the dispatcher is therefore only half of installing the lock: this file, tools/githooks/pre-commit,
# tools/githooks/pre-merge-commit, tools/check-parity.sh and docs/PARITY.md have to BE ON the branch that is checked
# out. docs/PARITY.md, "Trunk branches", has the four commands.
#   tools/githooks/install.sh          install (idempotent)
#   tools/githooks/install.sh --check  exit 0 if installed, 1 if not
set -eu
common=$(git rev-parse --path-format=absolute --git-common-dir)
dir="$common/hooks"
marker='# parity-lock dispatcher (tools/githooks/install.sh)'
if [ "${1:-}" = "--check" ]; then
  for h in pre-commit pre-merge-commit; do grep -qF "$marker" "$dir/$h" 2>/dev/null || { echo "parity lock: $dir/$h not installed"; exit 1; }; done
  echo "parity lock: installed in $dir"; exit 0
fi
mkdir -p "$dir"
for h in pre-commit pre-merge-commit; do
  if [ -e "$dir/$h" ] && ! grep -qF "$marker" "$dir/$h"; then echo "parity lock: $dir/$h exists and is not ours; not replaced" >&2; exit 1; fi
  cat > "$dir/$h" <<HOOK
#!/bin/bash
$marker
h="\$(git rev-parse --show-toplevel 2>/dev/null)/tools/githooks/$h"
[ -x "\$h" ] && exec "\$h" "\$@"
exit 0
HOOK
  chmod +x "$dir/$h"
done
echo "parity lock: installed pre-commit and pre-merge-commit in $dir"
