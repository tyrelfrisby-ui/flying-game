# Island world checks

These commands only generate repository artefacts, compile locally, or run tests. They never build/export/install a device player, push, or deploy. Keep dotnet/Swift caches under `$TMPDIR`. Use the installed `dotnet` (on Ty's machine: `/Users/tyfrisby/.dotnet/dotnet`) and existing NuGet package cache.

```sh
python3 docs/pending/tools/verify_island.py --rebuild
python3 tools/island/generate_runtime.py --check
DOTNET_CLI_HOME="$TMPDIR/island-dotnet" dotnet build tools/IslandCheck/IslandCheck.csproj --disable-build-servers -m:1 -nr:false -p:UseSharedCompilation=false
dotnet tools/IslandCheck/bin/Debug/net8.0/IslandCheck.dll
dotnet tools/IslandCheck/bin/Debug/net8.0/IslandCheck.dll continuity
python3 tools/island/check_parity.py
python3 tools/island/check_unity_sources.py
```

To screen actual placed solids, export `IslandCheck.dll geometry` to a temporary JSON file, then pass that path to `python3 tools/island/audit_geometry.py`. The audit includes oriented blocks/ramps and conservative full arch/ring bounds, with no Drake approach exemption. The baked-grid checker separately screens tower/wire envelopes. The continuity command samples pad blend, peak release and lake-bank boundaries; it does not certify every point of continuous terrain.

`check_unity_sources.py` compiles the actual world builders and fixed WorldRender camera source against installed Unity engine/editor assemblies. External session, coordinate, weather and scoring adapters are stubbed. It cannot establish a Unity player BUILD SUCCEEDED or produce a screenshot.

The in-process xUnit runner preserves xUnit discovery, fixtures, collections, data rows and skip semantics, avoiding VSTest's sandbox-denied sockets. Build unchanged `tools/FlightTests/FlightTests.csproj`, then build `tools/island/TestRunner/TestRunner.csproj`, with the same local build flags. Its output intentionally goes beside the test assembly so legacy TestData/repository path lookups work. Run `TestRunner.dll` for all tests, `TestRunner.dll --world` for the WorldTerrainActive collection, or pass class-name substrings. LAN tests themselves still cannot bind sockets in this sandbox. Run a frozen copy of all test binaries/configs/docs for comparisons; never rebuild a loaded test assembly.

The only deliberately vertical terrain boundary is Drake's authored north cliff. Its single operational approach still receives the complete final/solid checks. The old continuity test's allow-list has legacy locations and needs Claude's world-coordinate update; no test source was changed here.

Water and analytic normals (2026-10-31): `IslandTerrain.WaterAt` uses ordered 0.5 km candidate lists
and precompiled canyon geometry; `WaterAtReference` retains the old scan as a validation oracle.
`WorldTerrain.GroundNormalAt(x,y)` returns the outward NED unit normal for exactly `GroundHeightAt`'s
surface; `IsWallAt(x,y,slopeTangent)` compares its slope without constructing/normalizing a vector.
`IslandTerrain.HeightAndGradientAt` returns height and both derivatives together (metres per sim metre).
These ground queries describe the upper terrain, including cavity roofs; height-aware cavity floors
remain the separate `IslandCavities.TryFloor` surface.

The analytic evaluator is generated from the scalar `IslandHeightIndex.cs` expressions using forward
differentiation; it retains the same candidate order and scalar operations, and shares the scalar
index's precomputed nearest-segment/ray cells. After editing the scalar
evaluator, run `python3 tools/island/generate_gradient.py`, then `--check` to verify synchronization.
Min/max ties choose the first branch; absolute value at zero, zero-distance norms, and clamped
endpoints use zero derivative. A true height jump has no unique normal; the selected branch's
one-sided derivative is reported. No finite differences or displaced height queries run in production.

`IslandWaterAndNormalTests` checks bitwise water/height parity, smooth analytic derivatives,
frame/predicate behavior, the Bighorn Crown bank profile, and zero allocations after initialization.
`DOTNET_TieredCompilation=0 dotnet run -c Release --project tools/IslandCost -- --queries` reports matched reference/indexed
water timings and normal/predicate timings at fresh coordinates (five trials; median), plus the
north-3245-m cliff profile. [Recorded query validation](../../docs/pending/island-water-normal-checks.json).
The required Unity/iOS deploy build is still a separate gate; a .NET test or source compile is not that build.
