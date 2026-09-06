# Flying Game

Working title — final name TBD. A new iOS game.

## How this project is built

Three-party workflow:

- **Tyrel** — owner, final say on all decisions.
- **Grok bot** — brainstorming + design partner; primary channel for direction. Prompts from Grok arrive relayed by Tyrel and drive architecture/build tasks. The repo is public so Grok can read docs and commits directly; Claude's work lands here as the shared record.
- **Claude Code** — builds the game: architecture, code, Xcode project, testing, deploys.

Design decisions and architecture prompts from Grok live in `docs/`:

- [ARCHITECTURE.md](docs/ARCHITECTURE.md) — engine-agnostic aero core, Unity assemblies, core systems, risks
- [VERTICAL-SLICE.md](docs/VERTICAL-SLICE.md) — v0 scope: Arena 1 Challenges 1–2 (wings level + best glide)
- [DATA-CONTRACTS.md](docs/DATA-CONTRACTS.md) — AircraftConfig / ChallengeDefinition / ProgressSave
- [GROK-PROMPTS.md](docs/GROK-PROMPTS.md) — log of relayed Grok prompts

## Status

- 2026-09-06: Repo created; architecture + vertical-slice planning docs written. **Waiting on owner review — next trigger: "build the slice".**
