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
