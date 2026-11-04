#!/bin/bash
# Install the parity lock for EVERY worktree of this repository: one small dispatcher per hook in the common git
# directory (worktrees share it). The dispatcher runs the checked-out worktree's own tools/githooks/<hook> when that
# file exists, and does nothing otherwise, so branches that predate the lock (every other lane) are unaffected.
# core.hooksPath is deliberately NOT used: it would turn off any hook in .git/hooks for every branch.
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
