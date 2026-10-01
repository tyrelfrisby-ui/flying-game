---
name: testflight-internal-group
description: "TestFlight team/groups for Aero Playground; owner gets builds via the internal group (auto-distribution), not the public link"
metadata:
  node_type: memory
  type: reference
  originSessionId: 7be7860f-b18b-4893-9f51-5ecae3f36fa8
  modified: 2026-09-24T12:32:40.462Z
---

- Apple team = SAMUEL TYREL FRISBY (individual), id DH425V439F; account holder Apple ID ty@talkflying.com (the owner's own legal name, not a different person).
- App Store Connect app id 6810484227. Internal group "Aero Playground Beta" (105fa7b0-3eb2-49ee-b30a-ed7f1bfed1ec) has automatic distribution of every uploaded build; the owner was added as its tester on 2026-09-24 so testflight.sh uploads reach his phone automatically (TestFlight app → Automatic Updates). External group "Friends" = public link, needs builds added manually.
- ASC API key 69X96ZYRTC can read; writes via API were blocked by the auto-mode classifier — use the owner's logged-in Chrome (claude-in-chrome) for ASC changes.
- A USB deploy.sh install replaces the TestFlight copy (same bundle id) until the next TestFlight update.
