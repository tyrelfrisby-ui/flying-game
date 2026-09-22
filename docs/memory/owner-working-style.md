---
name: feedback_agent_driven_async_workflow
description: "Tyrel has limited hands-on time (kids, not his day job) — favor async agent-driven progress, capture-and-steer from phone, results pushed/logged"
metadata: 
  node_type: memory
  type: user
  originSessionId: 3824f082-e98e-4a39-8f9e-2a4352db1380
---

Tyrel (2026-09-06) on how he wants to work: he got into AI via agent tools ("OpenClaw" was a hassle, "Hermes" better, now "Grok bot" best) precisely because agents can "run the AI" for him. His constraint is TIME — this isn't his primary job, he has kids/a life, and the project stalls for weeks when it needs him at the keyboard. The whole point is to LOWER activation energy: drop an idea from his phone, have work happen without him, come back to shipped progress + a status log he glances at.

**Why:** the blocker was never AI capability — it was the friction of sitting down and context-switching back in.

**How to apply:**
- Default to the async loop: [[reference_glass_overlay_studio_worker_and_grok_bot_2026_09_06]] (Grok bot captures to BACKLOG.md → durable Studio worker does it 4×/day → AGENT-LOG.md). Keep that loop healthy and feed it.
- Interface vs executor: Grok bot / phone chat is the low-friction FRONT DOOR; the Studio Claude (with repo context + build tools) is the EXECUTOR. Don't make him drive builds by hand.
- He is decisive and action-biased: when he says "build it" / "both" / "build all three", BUILD — brief framing + one real fork max, then act; don't run long option surveys (he rejected AskUserQuestion twice when the path was clear). Map unknowns with subagents, then ship, deploy, and give him a concrete test.
- Reporting: he chose "a running log/doc I check" over push — surface NEEDS-YOU decisions at the top of AGENT-LOG.md; keep summaries phone-short.
- He also runs a separate iOS "flying game" project via the same Grok-bot pattern ([[project_flying_game_setup_2026_09_06]]).
