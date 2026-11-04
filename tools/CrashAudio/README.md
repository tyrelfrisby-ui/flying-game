# Crash audio review and reproduction

Run from this worktree only, without launching Unity:

```sh
export PYTHONDONTWRITEBYTECODE=1
export PATH="$HOME/.dotnet:$PATH"
# AUDIO_PYTHON must have numpy and matplotlib; normal python3 is the default.
AUDIO_PYTHON=/tmp/airfoil-plot-venv/bin/python zsh tools/CrashAudio/render.sh
```

Optional first argument: output directory. Default is
`/Users/tyfrisby/Developer/agent-lanes/aero-playground/crash-audio`.
Build intermediates and the old source snapshot are created in a fresh `/tmp` directory and removed on exit.
The old recipe is frozen to contract commit `564bcad66ad799184db28e000bac7c2fc53e164b`
(the audio lane's original HEAD). Only its namespace changes. This compiles the real old/new C#
`FxVoice`, `Dsp`, and new `CrashSound`/`FloodSound`; no Python synth port or Unity stub.
The Python stage only reads WAVs, measures them, and draws spectrograms. The runner also asserts the
legacy FxVoice Crash path matches the shared land DSP sample-for-sample (severity .7, mass 800 kg,
normal speed 10 m/s, seed 77). It cannot compile or validate FlightAudio/CrashWaterVisuals' Unity APIs.

All eleven WAVs are 48 kHz mono PCM16, **10 seconds each** (impact tails followed by silence):

- `crash-land-{OLD,NEW}-{0.3,0.7,1.0}.wav`: unchanged deterministic seed, severity comparison.
- `splash-water-small-piece.wav`: 8 kg, 7 m/s normal speed (196 J).
- `splash-water-engine.wav`: 220 kg, 18 m/s (35,640 J).
- `splash-water-intact-ditch.wav`: 1,100 kg, 5 m/s (13,750 J).
- `gurgle-flooding-fast.wav`: .012 m³/s, .08 m³ starting air; empties at 6.67 s.
- `gurgle-flooding-slow.wav`: .0006 m³/s, .008 m³ starting air; still flooding at 10 s.

The impact WAVs apply the **same fixed .52 gain and production soft clipper**, without individual
normalisation; this is a common listening reference, not a capture of the full in-game engine/wind mix.
The old crash strongly limits at this gain. New land intentionally loses its sustained body loudness;
listen for adequate crunch audibility over the engine in a later device build. Gurgles use .8 gain. They hold through 2.5 s after first water impact, then smoothstep to exact silence at 3.75 s; continued Flood events cannot restart them. The renders use impact time zero.

`measurements.md` reports FFT-power centroid and band fractions over the delivered PCM; crest uses RMS
through T99.9 (99.9% of cumulative energy). Duration means T99.9, not the padded 10 s file duration.
Spectrograms share a -95 to -20 dBFS colour scale, log frequency, 1024-sample Hann windows, 256-sample hop.
The comparison includes 200–450 Hz because the old "drum" is primarily its 290/330 Hz body, not sub bass.

## Synthesis and the limits of the model

Old Crash: 48 Hz oscillator under a 35 ms transient; 75 Hz low-pass noise follows a 100 ms envelope;
290 Hz oscillator is fed by a retriggered ring with 100 ms decay. Its 4 ms crunch grains excite a
330 Hz Q=1.4 body and a 750 Hz Q=.8 contact mode. Crunch rate begins at 93/137/170 events/s for
severity .3/.7/1, keeping the low body replenished, alongside the 2,800 Hz high-pass crinkle.

Land: irregular .7–4 ms noise grains, 650 Hz high-pass, low-Q tearing mode falling from 4,550 to
950 Hz, noise upper cutoff falling from 8,200 to 2,200 Hz. No pitched oscillator. A 100 Hz low-pass
noise weight lasts only an 8 ms e-fold. Severity increases duration, grain density, level. Mass scales
length/contact duration and density with m^(1/3), a constant-density characteristic length assumption.
Legacy `Crash(float)` uses nominal 800 kg; event impacts use actual mass. This is a material sound
recipe, not a model of metal fracture energy.

Water: normal impact energy **E = .5*m*v_n²**. Equating E with displaced water gravitational energy
**rho*g*L^4** gives cavity length **L = (E/(rho*g))^(1/4)**; gravity-collapse time is sqrt(L/g).
Duration is six collapse times, bounded .35–4.5 s. Pressure amplitude follows sqrt(E/duration),
referenced to 20 kJ/s, capped for headroom. A 12 ms broadband slap precedes a decaying filtered spray
sheet; noise cutoff falls from 7,400 to 900 Hz. Low displaced-water noise has cutoff 100/L Hz and
decay .025+.025L seconds; larger bodies therefore make lower, longer whumps. No metal grains.
The scene lacks impact geometry or fluid/acoustic coupling, so **20 kJ/s, six collapse times, gains,
filter constants, and clamps are explicit presentation calibrations**, not measured acoustic efficiencies.
Energy scaling is dimensionally motivated; absolute sound pressure is not predicted.

Flooding: characteristic vent bubble radius R=.004*(Q/.001)^(1/6) m, bounded 2–25 mm. This representative-vent approximation
shares increasing flux equally between bubble volume and detachment rate (both scale with sqrt(Q));
4 mm at 1 litre/s is an
explicit representative vent calibration (the contract has no hole geometry). Bubble frequency uses
**f=sqrt(3*gamma*p/rho)/(2*pi*R)**, gamma=1.4, rho=1000 kg/m³,
p=101325+9810*depth Pa. Each damped sine lasts ~12 cycles per e-fold, with irregular packet timing
and radii. Packet rate Q/(256*4*pi*R³/3), capped at 80 Hz, groups microscopic bubbles for audibility
and bounded CPU; this is not one sound per physical bubble. Gain falls with remaining air below
.01 m³ and with depth (12 m e-fold). Zero flow/air produces no new bubbles. Missing state updates
expire after .75 audio seconds, then existing rings/gain decay. Flow updates are consumed in blocks. Runtime passes age since each piece’s first water impact; no impact means no gurgle. Aircraft reset clears the impact timestamps.

## Wiring, rendering budget, and validation

`FlightAudio` subscribes Impact/Flood on enable and unsubscribes on disable/destroy. Impact position
uses `CoordinateMap.ToUnity`; gain is 25/(25+distance), delay distance/343 seconds in integer samples.
The ejected pilot position is used when out; otherwise the driver/aircraft position. Flood positions
also determine attenuation. Legacy `Crash(float)` retains `AtAircraft` and its existing pilot-out delay.
Both paths use the same land timbre; physics integration must replace direct Crash calls, not call both.
The new synth advances while menu-muted so old flood/impact state cannot resume after the menu.
Disable/re-enable starts a fresh bounded mixer. Impact sounds are mono at the listener with distance gain;
there is no directional panning or underwater listener propagation model.

A 256-slot SPSC ring feeds 8 impact voices, 12 flooding voices and 32 delayed arrivals. Full queues drop
new commands; impact voice exhaustion steals oldest; excess flood sources are ignored until a slot expires.
Fixed .65 headroom plus a bounded limiter avoids unbounded summation. The callback allocates nothing.
Envelope multipliers are precomputed; filter retargeting occurs every 128 samples.

FlightAudio installs `CrashWaterVisuals` at runtime. Its independent enable/disable subscriptions use the
same contract. It reuses `FlyingGame/Spray` and FloatSplash's tint, stretch and gravity style. Spray uses
L² coverage and sqrt(gL) launch speed with ballistic lifetime; pooled capacity 768, 180 particles/impact,
360/frame cap. Eight 48-segment rings spread at WakeWaves' 6 m/s and fade; they are cosmetic and do not
write to the GPU height solver. Foam projects body centre plus DepthM onto the surface; Q/bubble volume
drives emission, air fades it, and missing updates expire after .75 s. Capacity 384 particles, 24 vents,
16 emissions/frame with rotating fairness. No per-frame managed arrays/objects or per-impact GameObjects.
Materials, gradients and all objects are created once; material property blocks fade shared-material rings.

Standalone checks: exact legacy/shared-land agreement; zero flow; zero air; source expiry; Minnaert pitch;
distinct land/water synthesis; overload finite/bounded; sample-accurate delay; zero DSP allocations.
Analysis asserts lower <150 Hz and 200–450 Hz energy for each NEW severity (six assertions).
There were no existing audio/synth tests under `tools/FlightTests`. No `dotnet test` was run: required
`pgrep -x Unity` / `pgrep -x xcodebuild` both failed because the sandbox cannot get the process list.
No Unity, xcodebuild, deploy or device run. Unity compilation, visual appearance, audio over the running
engine, iOS performance, and post-merge event delivery still need verification. Physics is outside this lane.
