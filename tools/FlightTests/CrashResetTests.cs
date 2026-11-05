using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty 2026-11-04 5:22 PM PT (iPhone): "I crashed and every time I restart it is still crashed, even at different locations
/// or in different airplanes." The game's Restart, Relocate and aircraft change all build a NEW sim Aircraft
/// (FlightSimDriver.Spawn, through its Sim setter, which raises AirframeReplaced for the Unity side). These tests hold the sim
/// half of that promise: after a real crash, the aeroplane built for each of the three paths is intact, its engine runs and
/// its controls answer — including when it is built from the very AircraftConfig object the crashed one flew with, so a
/// crash that wrote into the config would be caught here.
///
/// The crash is the game's own scripted one (CrashScenario, the <c>-crashtest</c> launch): on the first island runway at
/// 35 m/s, banked 60°, nose 15° down, sinking 8 m/s. Every threshold is a literal.
/// </summary>
[Collection("WorldTerrainActive")]
public class CrashResetTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public CrashResetTests(ITestOutputHelper o) { _o = o; WorldTerrain.Active = new WorldTerrain(); WorldTerrain.SurfaceOverride = null; CrashEvents.ClearForTests(); }
    public void Dispose() { WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null; CrashEvents.ClearForTests(); }

    private static AircraftConfig Load(string n) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", n + ".json"));

    /// <summary>The lever is +1 closed, −1 wide open (TouchFlightControls).</summary>
    private const double Idle = 1.0, FullPower = -1.0;

    /// <summary>Fly the scripted crash for 6 s and return the wreck (it must actually have broken or crushed something).</summary>
    private Aircraft Crash(AircraftConfig c)
    {
        var ac = new Aircraft(c, CrashScenario.PittsStart(c, grass: false), ControlDeflections.Neutral);
        for (int i = 0; i < 1200; i++) ac.Step(new ControlInputs(0, 0, 0, FullPower), 1.0 / 200);
        bool damaged = ac.LostComponents.Count > 0 || ac.Crush.AbsorbedJ > 0 || ac.Panels.Any(p => p.State != PanelState.Intact);
        _o.WriteLine($"{c.Id} crash: lost [{string.Join(",", ac.LostComponents)}] crush {ac.Crush.AbsorbedJ:F0} J engine stopped {ac.EngineStopped}");
        Assert.True(damaged, $"{c.Id}: the scripted crash did no damage, so it tests nothing");
        return ac;
    }

    /// <summary>The runway start FlightSimDriver builds for "on the runway": at rest on all its wheels.</summary>
    private static Aircraft OnRunway(AircraftConfig c, WorldTerrain.RunwayEnd rw) =>
        new(c, LandingGear.RestingState(c, rw.CentreX, rw.CentreY, WorldTerrain.GroundHeightAt(rw.CentreX, rw.CentreY), rw.HeadingRad), ControlDeflections.Neutral);

    private static WorldTerrain.RunwayEnd FirstRunway() => IslandRunways.Ends(WorldTerrain.Airports.First(a => IslandRunways.Ends(a).Count > 0))[0];

    /// <summary>A runway at a DIFFERENT field from the crash's.</summary>
    private static WorldTerrain.RunwayEnd OtherRunway()
    {
        WorldTerrain.Airport first = WorldTerrain.Airports.First(a => IslandRunways.Ends(a).Count > 0);
        return IslandRunways.Ends(WorldTerrain.Airports.First(a => a.Name != first.Name && IslandRunways.Ends(a).Count > 0))[0];
    }

    /// <summary>Intact, engine running, controls answering — the three things "fully fresh" means to the pilot.</summary>
    private void AssertFresh(Aircraft ac, AircraftConfig freshlyLoaded, string what)
    {
        Assert.Empty(ac.LostComponents);
        Assert.False(ac.Structure.WingsFailed, $"{what}: wings failed");
        Assert.False(ac.EngineStopped, $"{what}: engine stopped");
        Assert.Equal(0.0, ac.Crush.NoseCrushM); Assert.Equal(0.0, ac.Crush.CabinCrushM); Assert.Equal(0.0, ac.Crush.TailCrushM);
        Assert.Equal(0.0, ac.Crush.TailBendPitchRad); Assert.Equal(0.0, ac.Crush.NoseBendRad);
        Assert.All(ac.Panels, p => { Assert.Equal(PanelState.Intact, p.State); Assert.Equal(0.0, p.BendRad); });
        // Every hard point where an untouched aeroplane of the type has it (a crushed cabin moves its points in).
        var reference = new Aircraft(freshlyLoaded, ac.State, ControlDeflections.Neutral);
        Assert.Equal(reference.ContactPoints.Count, ac.ContactPoints.Count);
        for (int i = 0; i < ac.ContactPoints.Count; i++)
        {
            Assert.Equal(reference.ContactPoints[i].Name, ac.ContactPoints[i].Name);
            Assert.True((reference.ContactPoints[i].Body - ac.ContactPoints[i].Body).Length < 1e-12, $"{what}: {ac.ContactPoints[i].Name} moved");
        }
    }

    /// <summary>Full power on the runway for 4 s: the engine runs up and the aeroplane rolls.</summary>
    private void AssertEngineRuns(Aircraft ac, string what)
    {
        Vec3 p0 = ac.State.Position;
        for (int i = 0; i < 800; i++) ac.Step(new ControlInputs(0, 0, 0, FullPower), 1.0 / 200);
        double rolled = Math.Sqrt((ac.State.Position.X - p0.X) * (ac.State.Position.X - p0.X) + (ac.State.Position.Y - p0.Y) * (ac.State.Position.Y - p0.Y));
        _o.WriteLine($"{what}: {ac.EngineRpm:F0} rpm, rolled {rolled:F1} m in 4 s, lost [{string.Join(",", ac.LostComponents)}]");
        Assert.True(ac.EngineRpm > 1500, $"{what}: {ac.EngineRpm:F0} rpm at full power");
        Assert.True(rolled > 5.0, $"{what}: rolled only {rolled:F1} m in 4 s at full power");
        Assert.Empty(ac.LostComponents);
    }

    /// <summary>In the air at the type's own start, full right aileron for 1 s rolls it right.</summary>
    private void AssertControlsAnswer(AircraftConfig c, string what)
    {
        WorldTerrain.RunwayEnd rw = FirstRunway();
        double alt = WorldTerrain.GroundHeightAt(rw.CentreX, rw.CentreY) + 600;
        AirStart.Plan plan = AirStart.Compute(c, rw.CentreX, rw.CentreY, alt, rw.HeadingRad, c.SpawnIasMs > 0 ? c.SpawnIasMs : 22.0);
        Aircraft ac = AirStart.Build(c, plan);
        double maxP = 0;
        for (int i = 0; i < 200; i++) { ac.Step(new ControlInputs(1, plan.TrimStick, 0, 0), 1.0 / 200); maxP = Math.Max(maxP, ac.State.Rates.X); }
        _o.WriteLine($"{what}: peak roll rate {maxP * 180 / Math.PI:F0} deg/s");
        Assert.True(maxP * 180 / Math.PI > 20, $"{what}: full aileron gave only {maxP * 180 / Math.PI:F1} deg/s");
    }

    [Theory]
    [InlineData("c172-like")]
    [InlineData("pitts-s2b-like")]
    public void RestartAtTheSameSpotAfterACrashIsAFreshFlyableAeroplane(string id)
    {
        AircraftConfig c = Load(id);
        Crash(c);
        Aircraft fresh = OnRunway(c, FirstRunway());   // the same config object the wreck flew with
        AssertFresh(fresh, Load(id), "restart");
        AssertEngineRuns(fresh, $"{id} restart");
        AssertControlsAnswer(c, $"{id} restart");
    }

    [Theory]
    [InlineData("c172-like")]
    [InlineData("pitts-s2b-like")]
    public void RelocateAfterACrashIsAFreshFlyableAeroplane(string id)
    {
        AircraftConfig c = Load(id);
        Crash(c);
        Aircraft fresh = OnRunway(c, OtherRunway());
        AssertFresh(fresh, Load(id), "relocate");
        AssertEngineRuns(fresh, $"{id} relocate");
        AssertControlsAnswer(c, $"{id} relocate");
    }

    [Theory]
    [InlineData("c172-like", "pitts-s2b-like")]
    [InlineData("pitts-s2b-like", "c172-like")]
    public void ANewAircraftAfterACrashIsFreshAndFlyable(string crashed, string next)
    {
        Crash(Load(crashed));
        AircraftConfig n = Load(next);
        Aircraft fresh = OnRunway(n, FirstRunway());
        AssertFresh(fresh, Load(next), "new aircraft");
        AssertEngineRuns(fresh, $"{crashed} -> {next}");
        AssertControlsAnswer(n, $"{crashed} -> {next}");
    }

    /// <summary>Nothing a crash leaves may outlive the app: no runtime code stores a crash, a wreck or damage in PlayerPrefs
    /// or a file, so a force-quit and relaunch always starts intact. (support.lastCrash is the last SOFTWARE exception, for
    /// the help page; it restores nothing.) If this fails, whatever was added must also be cleared on spawn and on launch.</summary>
    [Fact]
    public void NoCrashStateIsPersisted()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent;
        string scripts = Path.Combine(d!.FullName, "unity", "Assets", "_Project", "Scripts");
        Assert.True(Directory.Exists(scripts), scripts);
        var offenders = new List<string>();
        // Pref keys written as literals at the call, pref-key constants, and file writes that mention a crash.
        var write = new System.Text.RegularExpressions.Regex(@"PlayerPrefs\.Set\w+\(\s*""([^""]+)""|const string Pref\w*\s*=\s*""([^""]+)""|File\.Write\w*\([^;]*(crash|wreck|damage|debris)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (string f in Directory.EnumerateFiles(scripts, "*.cs", SearchOption.AllDirectories))
        {
            if (f.Contains($"{Path.DirectorySeparatorChar}Editor{Path.DirectorySeparatorChar}")) continue;
            foreach (System.Text.RegularExpressions.Match m in write.Matches(File.ReadAllText(f)))
            {
                string key = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Value;
                if (key == "support.lastCrash") continue;
                if (System.Text.RegularExpressions.Regex.IsMatch(key, "crash|wreck|damage|debris|broken|lost|struck", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    offenders.Add($"{Path.GetFileName(f)}: {key}");
            }
        }
        Assert.Empty(offenders);
        // The one pref key with "crash" in it is a constant, not a literal at the call: hold it to the help page's use.
        string support = File.ReadAllText(Path.Combine(scripts, "Bridge", "SupportDesk.cs"));
        Assert.Contains("PrefLastCrash = \"support.lastCrash\"", support);
    }
}
