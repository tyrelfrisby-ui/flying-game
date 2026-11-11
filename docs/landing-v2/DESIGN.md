# Landing v2 — Codex visual handoff

Status: design and shared presentation helpers only. Uncommitted. No runtime binding,
Unity compile, headset execution or visual approval is claimed. Claude's AGENT-LOG has
no landing-v2 view-model interface at the time of this handoff. Existing StartMenu,
QuestMenu, Glass Overlay, physics, catalogs and persistence are untouched.

Review `mockups/index.html`, or the paired SVG/PNG files. There are 24 boards: three
pages, an extra daylight conditions board, aircraft browse sheet and location browse
sheet for each of iPhone landscape, iPad, Mac and Quest panel. PNGs are offline
rasterizations of the vector design, not Unity screenshots. The yellow glider and
landscapes are illustrations; other aircraft explicitly say Capture pending.
Location cards are schematic categories, not a claim that a particular location or
viewpoint exists. The configuration sheet is an illustrative set; never offer skis
or floats on the 2-33 unless the authoritative catalog actually supports them.

## Page hierarchy

1. Aircraft & place: last-flown image (yellow SGS 2-33 first time), three big selectors
   for configuration, location and launch viewpoint. One open browse sheet at a time.
   Scroll cards horizontally with a partially exposed next card, plus Previous/Next
   buttons. Show selection with a border AND Selected label. Preserve current choice
   on cancel; only the host view model changes selections.
2. People & load: seat cards from the selected aircraft, occupant details, actual fuel
   tanks and baggage stations, and a link to the existing weight-and-balance view.
   Glider example suppresses the fuel slider and explains why. A seat editor offers
   Default person / Invite player; the role editor offers Passenger / Dual controls
   only where permitted by the host. Never imply an invitation was sent by the stub.
3. Conditions: time, weather and reliability have their own large cards. Day/night
   changes the full palette, scenery preview, icon and explicit label. Always show
   the aircraft/location/load summary from the host before enabling Launch. The
   mockup's generic review sentence is a placeholder for those authoritative values.

Challenges and lessons stay in a right rail. Selecting either opens the host's
restricted flow and filtered choices. Do not reuse unfiltered free-play data there.
Back/Next/Launch are host commands, not local state transitions. The first page's
Back affordance should be hidden or relabeled by the host where no parent exists.

## Binding boundary — awaiting Claude's published names

`LandingVisuals.cs` is a backend-neutral helper, NOT an alternative view-model API.
It supplies day/night tokens, safe-area layout, target sizing, carousel width and
selected-card reveal, and aspect-preserving thumbnail fitting. No scene automatically
instantiates it. Claude's published interfaces must be consumed before runtime views
are connected; do not create a second catalog or save format.

The adapter needs authoritative page/step, selected stable IDs, filtered option lists,
card title/subtitle/thumbnail, actual launch-view capture, enabled/disabled reason,
seat occupancy/role/capabilities, load/fuel/baggage/W&B state, weather/time/reliability,
summary and navigation/launch commands. These are data needs, not proposed signatures.

For flat views, convert Screen.safeArea from bottom-left pixels into top-left logical
units before creating Layout. Scroll long page content; keep the footer outside it.
Maintain 48-point minimum targets and 16-point body text where space allows; compact
secondary captions in these boards are 11–14 points. A real layout needs dynamic
text sizing and localization QA. Do not shrink all content to squeeze a wide page
onto a phone. Landscape reference is 852 × 393 points, with safe-area padding supplied
by the runtime host (not a hard-coded notch position).

For Quest, use the existing world-space renderer and its ray/poke routing. OnGUI
is not visible in the headset. Map the same top-left rectangles onto the panel;
64-unit minimum targets, clear hover/focus, no selection based solely on color.
Do not parent the panel to the rebuilt aircraft visual. These are panel design
boards, not stereo captures or a verified world-space implementation. Test at actual
headset distance before calling angular text size or interaction approved.

## Reproduction and validation

`python3 tools/landing-v2/mockups.py` regenerates the dependency-free SVG boards and
review index. Optional `rasterize.py OUTPUT_DIRECTORY` uses Pillow + fontTools to
make offline PNGs; it implements only this generator's SVG subset. On this machine,
Python 3.14 and already-cached packages were used without installs. Browser file
review was unavailable; offline PNGs were inspected instead.

The requested external renders directory is not writable in this sandbox. All
boards are retained in this worktree under docs/landing-v2/mockups. Copy them to
~/Developer/agent-lanes/renders/landing-v2/codex/ from an authorized session.

Validation: SVG XML parses, all 24 expected variants exist; offline visual review
covered every layout family; git diff --check clean; check-parity.sh --strict --brief
reported PARITY OK. That parity check compares the integration lane refs, not these
uncommitted helpers. Unity compile and real-device validation pending: process-list
access is denied, so the mandatory machine-wide build slot cannot be verified.
No merges, commits, pushes, installs, deployments or approvals performed.
