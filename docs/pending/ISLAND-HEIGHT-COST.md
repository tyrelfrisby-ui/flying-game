# Continuous island height query — PENDING Ty / device play check

NEEDS YOU: the terrain fix is locally measured and preserves the authored heights. The required iPad mini play check remains unverified: this restricted session cannot start Unity Package Manager's local socket or initialize CoreDeviceService. No build success or device fps is claimed.

The implementation uses Option A. Half-kilometre feature cells contain ordered references to typed continuous operations; 125 m geometry cells contain conservative nearest-segment and ray-crossing candidates. Neither index contains elevations. There is no raster, interpolation, quantization, world-data edit, or change to the exact-coordinate memo. The old evaluator remains in `src/FlyingGame.Core/IslandTerrain.Reference.cs`; its method body matches `1fe347d` verbatim except for the entry-point/helper names.

All authoring dictionary reads, fixed feature lookups, headings, terrace vertices, path lengths and cumulative distances are resolved during initialization. Canyon segments become separate ordered operations. Footprints include each blend's full support, including the final 3.704 km airway pass. Feature ordering, overlapping-pad sums, outer-island overwrite, volcano protection and peak/pad releases remain intact.

Geometry rejection uses the distance function's 1-Lipschitz bound: a segment farther from a cell centre than the nearest segment plus twice the cell half-diagonal cannot be nearest anywhere in that cell. Ray candidates retain the original crossing arithmetic and edge orientation. Candidate order preserves nearest-path ties. A 1e-9 km guard covers boundary rounding. Skipped operations have exactly zero influence.

Each feature list is limited to 128 candidates and each geometry list to 64 segments. Exceeding either limit fails initialization with an authoring diagnostic rather than silently increasing query work. Current maximum occupancies are:

| List | Maximum candidates in any cell |
|---|---:|
| Before outer-island overwrite | 8 |
| Outer islands | 6 |
| After outer-island overwrite | 4 |
| Clearance budgets | 26 |
| After broad clearance budgets | 5 |
| Fields | 3 |
| Peak cores | 1 |
| Nearest geometry segments | 46 |
| Ray crossing edges | 22 |

The bound is on work, not a hard realtime wall-clock guarantee on a shared machine. Initialization scales with the description; a fresh height query visits only the bounded local lists.

The final fresh-sample mean is **0.782 us** for uncached `Evaluate` (zero allocated bytes), and **0.9 us** through `GroundHeightAt` in Claude's original random-square probe, versus **25.7 us** before. These are local arm64 macOS / .NET 8.0.30 Release measurements. `DOTNET_TieredCompilation=0` is set on both final runs so the much faster simulation does not leave the after-run aerodynamic probe at a different JIT tier. The supplied 70.4 us baseline came from a different run/build configuration; it is not used as the denominator of the local speedup.

`tools/IslandCost` preserves the three requested probes from Claude's `IslandStartupCostTests`, read with `git show`, and adds uncached measurements and allocation accounting. It has no xUnit dependency. Run it with:

```sh
dotnet build tools/IslandCost/IslandCost.csproj -c Release --disable-build-servers -m:1 -nr:false -p:UseSharedCompilation=false
DOTNET_TieredCompilation=0 dotnet tools/IslandCost/bin/Release/net8.0/IslandCost.dll
```

`IslandHeightIndexTests` supplies the exact-value acceptance test, zero-allocation check and fresh-sample timing output. The parity set includes 20,000 uniform-area pseudo-random points over the complete 20 km ring, all field centres and both thresholds, all mesa pad centres and starts, pad blend boundaries, grid seams and authored geometry vertices. It compares double bits against the frozen evaluator and checks that `HeightKm` routes to the same result.

S3 world decision: retain `lift_terrain[S3]` at (E5.7,N-4.6) km; the stale `starts[S3]` at (E4.5,N-4.6) should move to it in a separate world-data correction. This performance change preserves the currently authored starts and world tables.

The same three aircraft rows, with this lane's unchanged per-point contact solver (Claude's altitude gate was neither merged nor copied):

| CPU for 1.0 second of simulation | Frozen height path | Indexed height path |
|---|---:|---:|
| On the runway at KSNA | 581.8 ms | 61.8 ms |
| 500 ft over KSNA | 559.2 ms | 61.1 ms |
| 5,000 ft over mid-island | 622.1 ms | 80.5 ms |
| AeroModel.Compute, per call | 8.2 us | 7.9 us |
| AirframeContact.Compute, per call | 520.4 us | 61.9 us |

Fresh moving samples near KSNA, the mid-island summit, Catalina and open sea measured 0.395 / 0.379 / 0.250 / 0.126 us respectively. The original clustered probe still repeats coordinates and measures 0.1 us; that is not the fresh-query result. All timing assertions remain informational. These measurements do not establish device fps.

The mechanical changes were also measured cumulatively in temporary copies of the frozen evaluator, on the same 20,000 central-square points. The following times are medians of three optimized runs; tiny nonmonotonic changes are measurement noise, not claimed speedups. Raw runs and sums are retained in `island-height-cost-results.json`.

| Cumulative step | us/fresh sample | bytes/sample |
|---|---:|---:|
| Original evaluator | 25.213 | 413.8 |
| 1. Hoist terrace polygon | 24.959 | 308.4 |
| 2. Hoist F1/F2 and shell/interior | 24.686 | 132.9 |
| 3. Hoist ridge signs | 24.712 | 109.5 |
| 4a. Hoist H only | 24.826 | 109.5 |
| 5. Precompute path and canyon totals | 24.409 | 109.5 |
| 4b. Typed constants/paths, all features visited (diagnostic compiled path) | 3.986 | 0 |
| Feature and coast/segment indexes | 0.782 | 0 |

The compiled diagnostic already uses bounded polyline candidates but visits every feature stage; the final row adds feature bucketing and coast rejection. These rows are not an isolated attribution of every compiler-level change.

The total cached authored world plus indexes retained approximately 37.8 MB in the optimized .NET tool. Cold generated-world/index initialization plus JIT took 1.95 s in that run (0.78 s with default tiering in the earlier run); neither number isolates incremental index memory/time or predicts IL2CPP startup. Initialization is outside sample timings.

Validation completed: **46,161 exact bitwise comparisons**, including 20,000 random points across the full ring; all three new tests passed; zero allocations across 20,000 fresh samples after initialization. A full controlled comparison of this checkout's original 783 tests produced **671 passed / 109 failed / 3 skipped on both height paths**, with exactly the same failing test names. With the three new tests the indexed run is **674 / 109 / 3**. The frozen run used an isolated temporary Core assembly with only `HeightKm` routed to `EvaluateReference`; it ran the same tests/configs from the same worktree. New tests are excluded from the original-inventory totals. This lane is at `a0617a1` (Ty's G1 approval after `1fe347d`), and does not contain Claude's later cleanup producing the supplied 808 / 4 / 3 baseline. There was no merge, cherry-pick or edit to that worktree/lane.

The required script was attempted, sequentially. Its no-argument device discovery exited before export because CoreDeviceService could not initialize. Retrying `tools/deploy/deploy.sh E7648C42-7C18-5EB6-8467-442B07B65154` with the documented iPad mini ID reached Unity export and then failed:

```text
[Unity Package Manager (Upm)]
Application terminated due to an uncaught exception:
  Error: listen EPERM: operation not permitted /tmp/Unity-Upm-94252.sock
```

There is no fresh `** BUILD SUCCEEDED **`, install, `AERO_SELFTEST=perf` launch or `[Perf]` output. Device acceptance remains **PENDING**, requiring the same deploy script followed by the authorized perf launch in a session with working local IPC/device access. No gate is marked approved. The existing Unity version settings and generated StreamingAssets files were left outside the commit.
