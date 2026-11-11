# The island weather

Owner: lane A (lane/claude-wake-vortex), 2026-11-05. Code: `src/FlyingGame.Core/Weather/`. Tests:
`unity/Assets/_Project/Tests/EditMode/IslandWeatherTests.cs` (EditMode) and `tools/FlightTests/CloudSitesTests.cs`.

> "the weather should be somewhat realistic in the sense that fog tends to sit in valleys when present etc and it should
> be loosely based on the real weather at that location that day but the game needs to have enough independence to keep
> it cohesive and for the "local weather" from places that are thousands of miles apart not to mess up the realism of the
> actual weather on the island..." (Ty, 08:41)
>
> "each airport should have a metar station with a frequency on the map to check conditions standard wind clouds
> temp/dewpoint and altimeter plus precipitation model real thunderstorms with columns of rain and downbursts/microbursts
> and virga etc if the conditions are right  not required just possible" (Ty, 08:42)

What is built (this lane): the island-wide state, the fog model and condition-driven fog placement, the real-weather
seeding interface with an offline provider only, and the airport stations with METAR-style reports. What is only designed
here: the live real-weather source, the multiplayer seed exchange, the station audio, and the whole convective model
(phased tasks at the end).

## 1. One island, one weather

`IslandWeather` is a single deterministic state for the whole island. Everything it reports is a pure function of

    (WeatherSeed, island terrain, position, sim time)

so every device in a session computes the same weather and nothing is exchanged but the seed. There is no per-player
weather and no per-region randomness that could disagree with itself.

- **WeatherSeed**: `Seed` (int), `Date` (the island's local calendar date), `StartHour` (local solar hour at sim time 0,
  default 07:00) and an optional `RealWeatherBias`. Wire form: `Encode()` / `Decode()`,
  e.g. `77;20261105;6.5;0,950,90,40,45,45,1|...`. It carries numbers only: no key, URL, account or player location.
- **IslandLocation**: latitude, longitude and UTC offset. It sets the sun's path, the season and the METAR time
  group. The default is a **placeholder** (45 N, 116 W, UTC-8, roughly Idaho, to match the Frank Church and Payette
  names) until Ty names the place (NEEDS YOU).
- **Air mass**: every 6 hours a set of stable hashed draws (FNV-1a + murmur3 fmix; `System.Random` is never used, because
  its sequence is not specified across runtimes): QNH (1013 ± 12 hPa), 10 m wind (0.5 + 9u² m/s, any direction), gust
  factor, daily mean temperature (a mid-latitude maritime climatology ± 3 K), the spread at dawn (1 + 9u² K: humid
  mornings are common, saturated ones are not), low cloud cover (more of it in humid air), an upper layer and a rain draw.
  Successive keys blend on a smoothstep (wind blended as a vector), so the weather evolves through a session and never
  jumps. Each key is quantised (0.1 hPa, 0.01 K and so on) so devices agree on the state rather than on the last ulp of a `Sin`.
- **Temperature and dewpoint**: a diurnal curve around the daily mean, minimum at sunrise and maximum at 15:00, half-range
  5 K under a clear sky and 1.5 K under overcast. The dewpoint holds through the day, so the spread is smallest at dawn.
  Environmental lapse rates are 6.5 K/km (temperature) and 7.5 K/km (dewpoint), so the spread grows 1 K/km and the
  highlands are a little drier. A rising parcel's 1.8 K/km dewpoint lapse is used only for the cloud base (Espy: 125 m
  per kelvin of spread). Applied to the environment, it would saturate every station above about 400 m, which the first
  probe did.
- **Cloud**: a low layer at the local LCL (it follows the terrain it grows from), in oktas from the cover draw, and an
  optional upper layer at 2.6 to 5 km. A station that sits inside a BKN or OVC layer is in cloud and reports fog: "hill fog".
- **Precipitation**: rain (or snow below 1 °C) only under 7/8 or more of cover, with a humid air mass and the rain draw.
  It is 0.4 to 8 mm/h and cuts visibility (≈ 11 km · R^-0.67 for rain).
- **Wind**: with an `AtmosphereField` attached (the regional air the aeroplane actually flies, `IslandWeather.Field`),
  the wind at a point IS that field's mean wind and gust, including its terrain speed-ups and its calm zone. Without
  one, it is the air mass's 10 m wind on a 1/7 power law. The landing page's wind and hot/cold day override the air
  mass's (`WindOverride`, `IsaDeviationOverrideK`), so what the menu promises is what is reported.
- **API**: `Sample(north, east, altMsl, t)`, `SampleSurface(north, east, t)` (2 m, or the water's surface) →
  `WeatherPoint` (T, Td, pressure, QNH, wind, peak gust, visibility, fog density and top, precipitation, sun elevation,
  local hour). `FogAt`, `Layers(t, ground)`, `AirMassAt(t)`, `SunTimes(t)`. Sim frame: x north, y east, z down;
  Unity's x east / z north appear only in `CloudSites`.
- **In the game**: `SessionSettings.ApplyWeather` builds `IslandWeather.Current` from the default seed and today's
  island-local date, with the landing page's overrides and the regional field.

**Not yet wired (W1)**: the flight model does not read the state's temperature curve or its QNH. It still reads the
session's fixed ISA deviation and the field's configured QNH. So a station's temperature and altimeter are the island
weather's, and can differ from the aeroplane's air by the diurnal swing (up to ±5 K) and by the QNH draw. The wind
does not differ, because it comes from the same field. W1 below closes this. It is not done here, because it changes
density altitude during a flight for every lesson and challenge, and that wants Ty's agreement first.

## 2. Fog

Fog is where it can lie (`CloudSites.Fog`) times whether the weather puts it there (`FogModel`, via
`IslandWeather.FogAt`). There are no seeded "fog on 4 of 13 strips" choices any more: the weather decides.

**Where it can lie** (30 sites): every grass and turf strip except Drake's cave strip (13), every lake (10), and the seven
deepest valley floors found in the terrain (`CloudSites.PickValleys`). Valleys are found on a 1 km grid over dry land,
must lie at least 80 m below their surroundings, must be 3 km apart and clear of the strips and lakes, and must not run
into the sea. The rings are read off a 500 m grid of the ground sampled once (6,561 lookups instead of 40,000:
the exact rings would stall the Quest's world build). A valley pool is a 700 m disc whose box starts at the floor's lowest ground. The fog fills it from the
bottom with a level top, so it shows only in the low parts of the floor, as real valley fog does.

**Radiation fog** (`FogModel.RadiationDensity`) needs all four of:
- A small spread at the point: dense below 0.5 K, gone at 2.5 K (`RadiationSpreadMaxK`).
- Light wind: full below 1.5 m/s, mixed out by 4.5 m/s.
- A clear sky: cloud cover above 0.4 starts to cut it, and by 0.85 cover the cloud radiates the heat back and there is none.
- The night's cooling (`NightCooling`): it builds over 3 h after sunset, holds to sunrise, then burns off over
  `BurnOffHours`. That is 2.5 h under a 60° noon sun and longer as the sun weakens: 4.5 h on 5 November at 45 N.

**The terrain** decides where the spread closes first. `ValleyDepth` is how far the ground lies below the mean of two
rings (1.5 and 3 km, 16 points each). On a clear, calm, cooling night:
- A valley pools cold air, up to 6 K once it is 220 m deep (`ColdPoolK`).
- A ridge or summit sheds it, so the slopes' "thermal belt" stays up to 3 K warmer than the free air.

Both effects fade over the lowest 20 to 150 m. A lake adds 1 K to the dewpoint just above it on a cooling night
(evaporation). So on a fog morning the fog sits in the valleys, over low ground and on the lakes, and the ridges stay
clear. The sun burns it off two ways: the night's cooling decays, and the diurnal warming opens the spread.

**Advection fog** (`AdvectionDensity`) forms over water (lakes and the sea) when the air's dewpoint is at or above the
water's temperature. It needs a 1 to 3 m/s breeze to bring the moist air in, and lifts into stratus by 10 to 14 m/s.
Lakes sit 1 K under the day's mean air at their height; the sea follows the season a month late. Unlike radiation fog,
wind feeds it.

**Visibility** (Koschmieder, 3.912/σ): fog of density 1 is about 100 m. Humid haze runs from 15 km (spread ≤ 1 K) to
60 km (≥ 8 K). Rain and snow cut it further.

**Measured on the island** (seed 1103, 5 November 07:00: calm 0.67 m/s, cover 0.30, dawn spread 1.08 K):
- Density 1.0 in the Frank Church canyon strips (Big Creek, Thomas Creek, Sulphur Creek) and all seven valley pools.
- 0.45 to 0.94 on the low coastal strips and lakes.
- 0.00 on the 1500 m plateau strips (AP1, AP3, 3U2, KMEV: 150 to 475 m above their surroundings) and 0.00 to 0.14 on
  the 2000 to 2400 m Bighorn cirque lakes.
- At 09:00 the valley pools are at 0.85 and the open strips are gone. By 10:00 all of it is gone.
- The same morning with a 6 m/s wind has none.
- Across twelve seeds on that date, five mornings had fog somewhere and seven had none. That probe ran before the
  ridge-drainage term was added, which only takes fog off ridges.

**Drawn**: `GroundFog` builds a box per site as before and re-reads every site's amount every 5 s (`PollWeather`). It
draws only sites with an amount ≥ 0.02 (`_FogAmount` scales the density and lowers the top), logs when fog forms, and
runs the wake-stirred patch only on a foggy site. Editor tools can force an amount (`GroundFog.AmountOverride`;
CloudWakeRender uses 1, so its renders show the wake, not the weather).

**Default session consequence**: the landing page's default wind is 5 m/s, which mixes out radiation fog everywhere. A
default session now shows fog only where advection fog forms. Fog Ty saw in the earlier renders appears in play only on
a calm session (wind ≤ ~3 m/s) on a humid morning, or once W1 lets the island weather choose the wind (NEEDS YOU).

**Limits, stated**:
- Fog depth is a heuristic: 4 + 10·density + 0.15·valley depth m for radiation fog, and 30 to 150 m for advection fog.
- No cold-pool persistence: in reality a deep valley inversion can hold fog to noon under a weak sun.
- No sea-fog sites drawn: the model computes it over the sea, but there is no box for it yet.
- Fog is not advected by the wind between sites.

## 3. Real-weather seeding

The island is LOOSELY based on real weather for a chosen place and date. Real data only biases the island's own draws;
it never replaces them.

- **Interface**: `IRealWeatherProvider.TryGet(RealWeatherRequest(lat, lon, date), out hourly)` returns hourly
  `RealWeatherObservation`s (QNH, wind, T, Td, cover; any of them may be missing) or false. It must not throw, and a
  provider that does throw is treated as offline (`RealWeatherBias.From` catches it).
- **Only provider so far**: `OfflineWeatherProvider`, which never has data. No network code exists. Which live source to
  use (Open-Meteo, NOAA/aviationweather.gov METARs, or another) is Ty's call (NEEDS YOU).
- **The bias**, `RealWeatherBias.Apply(ownDraw, hour)`:
  1. The observations are sorted and **rate-limited** hour to hour: 2 hPa/h, 3 K/h, 3 m/s/h, 0.25 cover/h. Any dewpoint
     above its temperature is cut to it.
  2. Each quantity moves only **60 %** of the way to the observation, and never more than ±12 hPa, ±8 K, ±6 m/s,
     ±4 K spread or ±0.5 cover from the island's own draw.
  3. Absolute limits apply: QNH 975 to 1045 hPa and wind ≤ 15 m/s.
  4. Wind direction blends on the circle.
  5. Temperature is compared by the day's **mean** and humidity by the day's **smallest spread**, so the island's own
     diurnal curve is not pulled twice.

  The result is always an island air mass: Td ≤ T, cover 0..1, a sane pressure. A wild feed (950 hPa, 45 °C, Td above
  T, 40 m/s) moves the island by exactly the clamps (EditMode test).
- **Which place**: the island's notional location (`IslandLocation`) by default. Using the player's own location is
  possible but is the host's choice for the whole session, never a per-player one.
- **Secrets**: a key, if the chosen source needs one, is read by the host from the environment or the platform keychain
  and is never written to a log, a config in the repo, the seed, a crash report or the screen. On failure a provider logs
  one generic line ("real weather unavailable, island climatology") with no URL, status body or key. A test should grep
  the log output for the key when a real provider lands.

## 4. Multiplayer

- **The host (or server) is authoritative.** It builds the `WeatherSeed`, which includes the bias if the host fetched
  one, and sends `Encode()` with the session start. Clients `Decode()` it and build the identical `IslandWeather`. A
  client never queries a real-weather provider, and its own location or local weather never enters the state. So a
  player thousands of miles away cannot fragment the island's weather.
- **Shared clock**: the state is a function of sim time, so the host also sends its session clock (sim seconds since
  the seed's StartHour) and late joiners take it. The LAN mesh (`Core/Lan`) is the carrier; nothing is sent yet (W4).
- **Determinism across platforms**: all draws are integer hashes; the air mass is quantised per key; the remaining
  floating maths (Sin/Exp in the sun and fog) can differ by an ulp between iOS and Quest runtimes. That can move a
  value across a METAR rounding boundary in rare cases, but not the weather itself. If it matters, quantise
  `WeatherPoint` before formatting.
- **Offline / solo**: exactly the same code path with a null bias: the island's climatology, still deterministic for the
  seed and the date.

## 5. Airport weather stations (built)

`WeatherStations.All`: one station per airport in the island data, 16 in all.
- **ASOS** at the eight airports the chart draws (`WorldTerrain.Airports`: PHDH, PANC, PATK, KSNA, KMYL, 3U2, KMEV,
  KPGA).
- **AWOS-3** at the other eight (PALH, KAVX, DRAKE, SITX, TERN, NHVN, KITE, RIME).
- Sensor site: the airport's first runway's centre. Elevation: the field elevation.
- Airparks and backcountry strips have no station, as in the real world. Say if they should.

**Frequencies**:
- 118.000 to 135.975 MHz on 25 kHz channels. Each airport id hashes to a channel and takes the next free one, in
  ordinal id order, so the plan depends only on the set of ids (not on list order) and never repeats.
- Reserved channels: 121.400 to 121.975 (guard and ground), 122.000 to 123.100 (flight service, CTAF/UNICOM/MULTICOM;
  122.8 is the game's default COM) and 123.450.
- Today, for example: KMYL 123.125 and PHDH 120.900. `ByFrequency(mhz)` finds the station on a tuned COM.
- The chart's airport tag now carries "ASOS 123.125" under the name and elevation.

**A report** (`WeatherStations.Observe(station, weather, t)`) samples `IslandWeather.SampleSurface` AT the station's
point. So it reports the wind the aeroplane will meet there (from the same field), the valley's fog, the hill station's
cloud. It then rounds and formats the way an automated station does (FMH-1, AC 00-45H):
- **Wind**: direction true (magnetic = true on this island), to 10°. Under 3 kt is `00000KT`. A gust is reported when
  the peak is ≥ 10 kt above the mean.
- **Visibility**: the largest ASOS reportable value not above what is there: M1/4, 1/4 … 2 1/2, 3, 4 … 10 SM.
- **Weather**: `-RA`/`RA`/`+RA` (and SN) at < 2.5, < 7.6 and ≥ 7.6 mm/h. `FG` under 5/8 SM in fog. `BR` under 7 SM with the
  spread ≤ 2.5 K.
- **Sky**: `FEW/SCT/BKN/OVC` + hundreds of feet AGL, rounded to 100 ft up to 5,000, 500 ft to 10,000 and 1,000 ft above,
  and reported as summation layers. Layers below the field or above 12,000 ft are left out. `CLR` when there are none.
  `VVnnn` when the station sits in fog thick enough to hide the sky.
- **Temperature/dewpoint**: whole °C, `M` for minus. **Altimeter**: QNH in inHg rounded down to the hundredth (`A2999`).
- **Time**: `DDHHMMZ` from the island's local time and its UTC offset, at the top of the minute, then `AUTO` and
  `RMK AO2` (ASOS) or `AO1` (AWOS).
- `Raw` gives the raw METAR, e.g. `METAR KMYL 051500Z AUTO 18007KT 10SM FEW021 00/M05 A2999 RMK AO2`. `Spoken` gives
  the AWOS voice as text ("KMYL automated weather observation, one five zero zero zulu. Wind one eight zero at seven…").

**Next for the stations (S-phases)**:
- S1: draw every station on the chart (the outer-island ones are off the current chart's airport set), and show a
  station's `Raw` in the chart's info list.
- S2: tuning a COM to a station frequency plays `Spoken`. Use the platform TTS (AVSpeechSynthesizer on iOS, Android TTS
  on the Quest), with range from `NavReception`'s VHF line-of-sight. The report refreshes each minute as an ASOS does.
  It needs the radio to know when it is tuned to a station: `ByFrequency`.
- S3: once W1 is in, add a test that the aeroplane's own outside-air temperature on the ramp equals the report's.

## 6. Thunderstorms and the convective model (designed, not built)

"Possible, not required": a storm forms only when the island's own state allows it, and most days it does not. Every
phase keeps the physics emergent: storms move air, and the aeroplane meets that air. Nothing applies a "microburst
force".

- **C0 Instability.** From the state's own profile, compute the surface parcel's LCL, LFC, CAPE and CIN, and the
  0 to 6 km shear. This needs a free-atmosphere temperature profile above the mixed layer, which the state does not
  yet have (one lapse rate). Add a stability draw per key (lapse 5.5 to 8 K/km aloft) and a mid-level moisture draw.
  Tests: a known sounding gives a hand-computed CAPE.
- **C1 Cells.** Spawn deterministically from the seed and time when CAPE and a trigger allow it: afternoon heating over
  sun-facing terrain (`AtmosphereField.HeatingAt`), orographic lift on the windward slopes, or convergence lines. Each cell
  has a hashed id and a lifecycle: towering cumulus (10 to 15 min) → mature (precipitation, downdraft, 15 to 30 min) →
  dissipating. It is advected by the 700 hPa wind, so every device sees the same cells. Tests: no cells when CAPE < 100
  J/kg or CIN is large; the same cells for the same seed.
- **C2 Rain shafts and virga.** Rain rate from the cell's updraft and moisture. Falling rain evaporates in sub-cloud air
  at a rate set by the sub-cloud spread, so a dry sub-cloud layer (spread above ~15 to 20 K, a high base) evaporates the
  shaft before it lands: virga. That evaporation's cooling is what drives C3. Visual: a lit column (and a curtain that
  stops short when it is virga). Mobile budget: textured cylinders and soft particles, no raymarch on Quest or iPhone.
  Tests: shaft reaches the ground at a small spread, stops short at a large one.
- **C3 Downbursts and microbursts in the flow field.** Each mature cell (and each virga shaft with strong evaporative
  cooling, the dry microburst) contributes a downdraft column and its outflow, from an analytical axisymmetric model
  (Oseguera and Bowles 1988, NASA TM-100632; Vicroy 1991). Its strength comes from the cell's precipitation loading and
  evaporative cooling. It is registered as a `WeatherFlowField` that `Atmosphere.WindAt` sums, just as the wake is summed
  through `Aero.Wake.WakeField`, so every strip of every aeroplane meets it and the classic sequence emerges on its own:
  headwind gain on entry, the downdraft core, then the tailwind and sink on exit. Microburst < 4 km, downburst larger;
  10 to 20 min life with a peak a few minutes after touchdown. Tests: a level pass through the core shows +ΔV then
  downdraft then −ΔV in the airspeed trace, and a C172 at approach power loses height through it.
- **C4 Gust fronts.** The outflow spreads as a density current at ≈ 0.7·√(g′h): a wind shift, a gust and a temperature
  drop moving out from the cell. It can trigger new cells (C1). It is added to the same flow field. Tests: front speed
  against the density-current formula; stations report the shift.
- **C5 Stations report storms.** `TS`, `+TSRA`, `VCTS`, `CB` in the sky groups, `PK WND` and `WSHFT` remarks, gusts,
  lightning distance (`LTG DSNT`). The AWOS voice says "thunderstorm in the vicinity". Tests: report consistency at the
  station point, as now.
- **C6 Audio and effects.** Rain on the airframe, thunder with travel delay (the integer sample countdown, per the
  crash-audio lesson), and lightning flashes.
- **C7 Budget.** CPU: cells ≤ 8 and the flow-field query O(cells) per strip sample. Measure on the Quest at 90 Hz and the
  iPhone at 60 Hz before merge.

## 7. Wiring phases (after this lane)

- **W1**: the flight model reads the state. Push `IsaDeviation(t)` and QNH into `Atmosphere` and the regional field
  every few seconds, so the aeroplane's air temperature and the station's agree. Needs Ty's OK (it changes density
  altitude during lessons and challenges).
- **W2**: `CloudPlayground` draws the state's layers: bases from `Layers()`, cover from the oktas.
- **W3**: the station audio and COM tuning (S2).
- **W4**: the multiplayer seed and clock exchange over the LAN mesh.
- **W5**: a real-weather provider, once Ty picks the source and the place. Host-only, keys never logged.
- **W6**: sea-fog boxes along the coast, and fog that drifts with the wind.

## 8. Tests

`IslandWeatherTests` (EditMode, NUnit; literals copied by hand from the first run, 2026-11-05):
- **Fog**: valley against ridge on a synthetic 400 m bowl and hill (184.1 m deep / −184.1 m: fog 1.0 against 0.0) and
  on the island (Thomas Creek and Valley 1 fog, AP1 and Bighorn Crown clear). The dewpoint-spread threshold (1.0 at
  0.5 K, 0.5 at 1.5 K, 0 at 2.5 K; none under broken cloud). Burn-off (1.0 at 07:00, 0.848 at 09:00, 0 at 10:00) and
  wind (none at 6 m/s). Advection fog's dewpoint/water and breeze rules.
- **Determinism**: the same seed gives bit-identical samples at every fog site, another seed differs, and the seed
  survives the wire.
- **Real-weather bias**: the rate limits, the clamps and coherence over a day under a wild feed. Offline (and a
  throwing provider) give the island's own climatology.
- **Stations**: a station per airport with a unique, unreserved 25 kHz frequency, independent of list order. The
  report agrees with the state at the station point for four seeds, sixteen stations and three times (wind, T/Td,
  altimeter, visibility, FG, precipitation, time group). Reports are deterministic (one literal raw METAR). The
  formatting of every group, the raw METAR and the spoken text.

`CloudSitesTests` (xUnit): the 30 places fog can lie, and the seven valley pools' positions and floors.
