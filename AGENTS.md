# AGENTS.md

Any agent (Claude, Codex, Grok, …) working in this repository: read `CLAUDE.md` (standing guidance, lessons learned) and
the top of `AGENT-LOG.md` (NEEDS YOU) first.

## THE RED 2-33 IS BANNED: never reintroduce it (Ty, 2026-11-04)

- Only the yellow SGS 2-33 exists, on every platform: no red-and-white / white / cream 2-33 model, livery, tint, fallback
  or generator entry. `tools/FlightTests/NoRed233Tests` enforces this; never weaken it.
- Lanes forked before 2026-11-04 08:10 still draw the red one. Merge `lane/claude-island-nav` into an old lane before
  you build or install it, and never resolve a merge toward the old 2-33 code. Details: CLAUDE.md, same heading.

## iOS/Mac and Quest parity: LOCKED (Ty, 2026-11-04)

- `lane/claude-island-nav` (iOS/Mac) and `lane/claude-quest-islandnav` (Quest) are one game and must stay in sync at
  every commit. A shared change goes onto one lane and the other lane merges it, the same day. Merging the other lane's
  tip in is always allowed.
- Platform-only code goes only behind `#if UNITY_IOS` / `UNITY_ANDROID` (or the XR defines), or in the documented
  platform folders: `Scripts/Quest/`, `Assets/XR/`, `Plugins/Android/`, `Bridge/Quest*`/`Vr*`/`Xr*`, `tools/deploy/*quest*`.
  The full list is in `docs/PARITY.md`.
- The lock is a git hook: `tools/githooks/pre-commit` and `pre-merge-commit` run `tools/check-parity.sh --hook`.
  `tools/githooks/install.sh` installs it for every worktree. It blocks a shared change on a lane that already differs
  from the other lane, and it does nothing on any other branch. **Never `--no-verify`.**
- `tools/check-parity.sh --strict` is the full check, and the deploy scripts run it. Rules and paths: `docs/PARITY.md`.

Questions for Ty: append one line to `~/Developer/agent-lanes/QUESTIONS-FOR-TY.md` (Open section) — see CLAUDE.md "QUESTIONS FOR TY".
