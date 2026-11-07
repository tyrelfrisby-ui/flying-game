using System.Text.RegularExpressions;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty 2026-11-05 15:21 (pt813u): "The feature where it zooms out to capture the whole crash is a bug because it is deciding
/// when it is a crash but pieces can fall off well before a "crash" so remove that feature and just follow the main
/// fuselage with the seats." And 13:45: a J-3's outboard wing panel snapped off on takeoff in the flight-path view, and the
/// camera "stopped following the airplane and stayed with the broken wing".
///
/// What did that was the crash view (CrashFraming / ChaseCamera.CrashView, 2026-11-05 06:05-08:01): the first broken-off
/// piece to land latched it, and it parked the camera 30 degrees above the mass-weighted cluster of airframe and pieces,
/// so an aeroplane that flew on left the camera behind over its wing. It is gone. The chase camera follows its Target, the
/// sim's own Aircraft, whatever falls off; only the pilot's view choice and the bail-out (OverrideTarget) move it.
///
/// ChaseCamera is Unity code this suite does not compile, so the camera half is held by reading its source. The sim half —
/// that the Aircraft the camera follows IS the fuselage with the seats, and a shed part never takes its place — runs here.
/// </summary>
public class FuselageFollowCameraTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent;
        return d!.FullName;
    }
    private static string Bridge => Path.Combine(Root(), "unity", "Assets", "_Project", "Scripts", "Bridge");
    private static string ChaseSource() => File.ReadAllText(Path.Combine(Bridge, "ChaseCamera.cs"));

    /// <summary>Nothing in the camera listens for an impact, a flood or a piece, or looks at debris.</summary>
    [Fact]
    public void TheChaseCameraHasNoCrashView()
    {
        string cam = ChaseSource();
        foreach (string banned in new[] { "CrashEvents", "CrashFraming", "CrashView", "_crash", "WingDebris", "DebrisBody", "WreckPiece", "SceneManager" })
            Assert.DoesNotContain(banned, cam);
    }

    /// <summary>The whole-wreck framing is not left anywhere to be wired back in (Q076 was rejected, not deferred).</summary>
    [Fact]
    public void CrashFramingIsGone()
    {
        string root = Root();
        Assert.False(File.Exists(Path.Combine(root, "src", "FlyingGame.Core", "CrashFraming.cs")));
        var dirs = new[] { Path.Combine(root, "src"), Path.Combine(root, "unity", "Assets", "_Project", "Scripts") };
        foreach (string dir in dirs)
            foreach (string f in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                string s = File.ReadAllText(f);
                Assert.False(s.Contains("CrashFraming") || s.Contains("OpensCrashView") || s.Contains("OpensWaterCrashView"), f);
            }
    }

    /// <summary>Only the scene bootstrap gives the chase camera a Target (the aircraft's root), and only the bail-out and
    /// its replay give it an OverrideTarget (the pilot). Nothing hands it a piece.</summary>
    [Fact]
    public void OnlyTheAircraftAndTheBailedOutPilotAreEverFollowed()
    {
        var target = new Regex(@"\bchase\.Target\s*=(?!=)\s*([^;]+);");
        var overrideTarget = new Regex(@"\.OverrideTarget\s*=(?!=)");
        var targets = new List<string>();
        var overrides = new SortedSet<string>();
        foreach (string f in Directory.EnumerateFiles(Bridge, "*.cs", SearchOption.AllDirectories))
        {
            string s = File.ReadAllText(f);
            foreach (Match m in target.Matches(s)) targets.Add(Path.GetFileName(f) + ": " + m.Groups[1].Value.Trim());
            if (overrideTarget.IsMatch(s)) overrides.Add(Path.GetFileName(f));
        }
        Assert.Equal(new[] { "SceneBootstrap.cs: aircraft.transform" }, targets);
        Assert.Equal(new[] { "FlightReplay.cs", "PilotEgress.cs" }, overrides);
    }

    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    /// <summary>The Cub from the report, climbing out at 30 m/s: shedding an outboard panel (or a whole wing, the tail, the
    /// prop) leaves the cabin and the seat on the Aircraft — the camera's Target — and does not move it. It flies on
    /// from where it was; the part goes off as its own piece.</summary>
    [Theory]
    [InlineData("j3-cub-like")]
    [InlineData("pa18-cub-like")]
    [InlineData("pitts-s2b-like")]
    public void ASheddingPartNeverTakesTheSeatsOrTheCameraTarget(string id)
    {
        foreach (AirframeComponent part in new[] { AirframeComponent.WingLeftOuter, AirframeComponent.WingRight,
                     AirframeComponent.TailHorizontal, AirframeComponent.Propeller })
        {
            var ac = new Aircraft(Load(id), new RigidBodyState(new Vec3(0, 0, -60), new Quat(0, 0, 0, 1), new Vec3(30, 0, 0), Vec3.Zero),
                ControlDeflections.Neutral);
            Vec3 seat = ac.OccupantSeatBody;
            ac.Step(new ControlInputs(0, 0, 0, 1), 0.005);
            Vec3 before = ac.State.Position;
            ac.LoseComponent(part);
            Assert.True(ac.IsLost(part), $"{id} {part}: not shed");
            Assert.False(ac.IsLost(AirframeComponent.Cabin), $"{id} {part}: the cabin went with it");
            Assert.Equal(seat, ac.OccupantSeatBody);
            Assert.Equal(before, ac.State.Position);
            for (int i = 0; i < 100; i++) ac.Step(new ControlInputs(0, 0, 0, 1), 0.005);
            double moved = (ac.State.Position - before).Length;
            Assert.InRange(moved, 10.0, 20.0);   // half a second at ~30 m/s: the same airframe, flying on
        }
    }
}
