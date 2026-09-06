# Grok ↔ Claude communications protocol

Working title: **flying game** (`tyrelfrisby-ui/flying-game`).
Ty talks to **Grok Bot (Chief)**. Grok directs **Claude**. Ty is not the paste relay.

## Channels

### A) GitHub Issues / PR comments (default for docs, pure C#, tests, PRs)
1. Grok opens or comments on an issue in this repo.
2. Body or comment includes **`@claude`** plus a clear directive.
3. Claude Code GitHub Action wakes, does the work, replies in-thread, opens a PR when code changes.
4. Grok reads the issue/PR via GitHub connector and reports to Ty (or merges when Ty has pre-approved that class of change).
5. Ty merges PRs when he wants human gate; Grok may merge trivial/docs/CI PRs Ty already greenlit.

### B) Local Studio runner (Unity / iOS device / feel)
Cloud Actions cannot build Unity to iPhone. For that, Grok runs Claude on the Mac Studio:

```bash
/Users/tyfrisby/Documents/flying-game/scripts/claude-run.sh
# optional: --bg
```

Prompt file: `scripts/NEXT-PROMPT.md` (Grok overwrites per task).  
Logs: `~/Library/Logs/FlyingGame/claude-run.log`

Local Claude should still prefer opening PRs; both channels share this repo.

## Issue format (Grok → Claude)

**Title:** short imperative, optionally includes `@claude`  
**Body template:**

```markdown
@claude

## Goal
<one paragraph>

## Constraints
- Follow docs/ARCHITECTURE.md, VERTICAL-SLICE.md, DATA-CONTRACTS.md
- Do not touch Glass Overlay / overlay-studio-dev
- Working title stays "flying game" (no rename/branding)
- <extra constraints>

## Deliverables
- [ ] <files / PR expectations>
- [ ] Append outcome to docs/GROK-PROMPTS.md

## Out of scope
- <explicit non-goals>
```

## Trigger phrases (Ty → Grok)
- **`build the slice`** — Grok directs Claude to execute VERTICAL-SLICE.md build order (prefer Studio for Unity steps; Issues for Core/Sim/tests first if split).
- **`comms live`** — protocol armed (this doc).
- Status / merge / steer — Ty talks to Grok only.

## Verification (Grok after each Claude turn)
1. Read issue comments + linked PR diff.
2. Re-read changed `docs/` and `git log`.
3. Tell Ty what shipped / what needs his merge or device test.

## Do not
- Ask Ty to copy-paste prompts between agents.
- Implement IAP, full arenas, or spin challenge until Ty asks.
- Expand past the vertical slice without Ty saying so.
