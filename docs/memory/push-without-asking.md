---
name: flying-game-push-without-asking
description: Aero Playground / flying-game repo — owner authorized pushing new commits to GitHub as work is built, without asking each time
metadata:
  type: feedback
---

In the flying-game repo (Aero Playground, `~/Documents/flying-game`, branch `split-surfaces`, private repo tyrelfrisby-ui/flying-game), push new commits to `origin` as work is built — do not ask first.

**Why:** On 2026-10-01 the owner said "i don't mind pushing new work to github as we build without my permission". 104 commits had sat only on one Mac until that day; pushing is the off-machine backup, the repo is private and he is the only developer.

**How to apply:** After each committed change with the test suite passing (`~/.dotnet/dotnet test tools/FlightTests`), `git push origin split-surfaces`. Never force-push or rewrite pushed history. This covers the flying-game repo only — other projects (e.g. Glass Overlay, whose worker guard blocks pushes) keep their own rules. Related: [[project_flying_game_touch_ios_2026_09_07]].
