# Portrait iPhone HUD layout — v1 review

Pending Ty's visual approval. No merge, push, install, or deployment.

- `hud-analog-gamepad-portrait.png`: gauges enabled; simulated gamepad; touch sticks hidden.
- `hud-analog-touch-portrait.png`: gauges enabled; touch sticks and trim visible.

Both images are unmodified Unity runtime captures at **1206 × 2622**. The existing HUD self-test forces mobile controls and iPhone safe-area insets (top 186 px, bottom 63 px, sides 0 px). These are desktop runtime previews of the iPhone layout, not photographs or captures from a physical iPhone. The capture player uses Mono/ARM64 with Burst disabled; iOS is built separately with the normal IL2CPP/Burst build.

Flight gauges stay at the top. Tach, throttle, fuel flow, and fuel form one lower horizontal row. Buttons form one horizontal row immediately below the engine gauges, above the sticks/trim in touch mode and above the bottom safe area with a gamepad.

Reproduce using a Mac player built from this lane and a display with enough vertical space:

```sh
AERO_SELFTEST=hudshots AERO_SELFTEST_QUIT=1 \
AERO_HUDSHOTS_PORTRAIT=1 AERO_HUDSHOTS_DISPLAY='HUD render' \
AERO_SHOT_DIR=/absolute/output/path \
'/path/to/Aero Playground.app/Contents/MacOS/Aero Playground' \
-logFile /absolute/output/path/hudshots.log
```

`AERO_HUDSHOTS_DISPLAY` is optional and names an existing display. The harness rejects clamped window dimensions rather than saving misleading captures. The temporary virtual capture display was removed after capture. Glass Overlay code and assets were not modified.
