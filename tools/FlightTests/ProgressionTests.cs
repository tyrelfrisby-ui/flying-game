using System.Text.Json;
using System.Text.RegularExpressions;
using FlyingGame.Core;
using FlyingGame.Sim.Jobs;
using FlyingGame.Sim.Ratings;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The 2026-11-01 progression, its placeholders, the retired 737-800, and the job stubs.
///
/// The graph is pinned with LITERALS copied from the owner's brief — glider solo, Cub solo, Decathlon or Great
/// Lakes for Pilot, the aerobatic glider, Blind Flying behind both glider stages, Commercial, Instructor — so a
/// change to RatingBook has to be made here as well, on purpose.
/// </summary>
public class ProgressionTests
{
    private readonly ITestOutputHelper _o;
    public ProgressionTests(ITestOutputHelper o) { _o = o; }

    private static readonly RatingBook Book = RatingBook.Default();

    private static string Repo
    {
        get
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent;
            return d!.FullName;
        }
    }

    private static bool HasConfig(string id) => File.Exists(Path.Combine(Repo, "configs", "aircraft", id + ".json"));

    [Fact]
    public void TheGraphIsTheOwnersGraph()
    {
        var expect = new (string id, string[] aircraft, double score, string[] requires, CertLevel grants)[]
        {
            ("glider-solo", new[] { "glider-2-33-like" }, 70, new string[0], CertLevel.StudentSolo),
            ("cub-solo", new[] { "j3-cub-like" }, 70, new[] { "glider-solo" }, CertLevel.None),
            ("pilot", new[] { "decathlon-8kcab-like", "great-lakes-like" }, 78, new[] { "cub-solo" }, CertLevel.Pilot),
            ("aerobatic-glider", new[] { "glider-grob103-acro-like", "glider-dg-acro-like" }, 78, new[] { "pilot" }, CertLevel.None),
            ("instrument", new string[0], 83, new[] { "glider-solo", "aerobatic-glider" }, CertLevel.Instrument),
            ("commercial", new string[0], 88, new[] { "instrument" }, CertLevel.Commercial),
            ("flight-instructor", new string[0], 93, new[] { "commercial" }, CertLevel.FlightInstructor),
        };
        Assert.Equal(expect.Select(e => e.id).ToArray(), Book.Stages.Select(s => s.Id).ToArray());
        foreach (var e in expect)
        {
            Stage s = Book.Stage(e.id)!;
            _o.WriteLine($"  {s.Id,-18} {string.Join("|", s.Aircraft),-48} {s.PassScore,3} % after [{string.Join(", ", s.Requires)}] -> {s.Grants}");
            Assert.Equal(e.aircraft, s.Aircraft);
            Assert.Equal(e.score, s.PassScore);
            Assert.Equal(e.requires, s.Requires);
            Assert.Equal(e.grants, s.Grants);
        }
        // Every level on the ladder is granted by exactly one stage, except pre-solo, which is "flown anything".
        foreach (CertLevel l in RatingBook.Ladder.Skip(1))
            Assert.Single(Book.Stages, s => s.Grants == l);
        // No stage requires one that comes after it (the resolver walks the book once, in order).
        for (int i = 0; i < Book.Stages.Count; i++)
            foreach (string r in Book.Stages[i].Requires)
                Assert.True(Book.Stages.FindIndex(s => s.Id == r) < i, $"{Book.Stages[i].Id} requires {r}, which comes later");
    }

    [Fact]
    public void EveryAircraftAStageNamesIsBuiltOrIsAPlaceholderAndNoneIsRetired()
    {
        foreach (Stage s in Book.Stages)
            foreach (string ac in s.Aircraft)
            {
                Assert.False(Hangar.IsRetired(ac), $"{s.Id} names the retired {ac}");
                Assert.True(HasConfig(ac) || Hangar.IsPlaceholder(ac), $"{s.Id} names {ac}, which is neither built nor a placeholder");
            }
    }

    [Fact]
    public void APlaceholderHasNoConfigSoNothingCanTryToLoadIt()
    {
        // The Grob 103 Acro (now the Twin III Acro) came off this list on 2026-11-01 when its config was built; the DG is still on it.
        Assert.Equal(new[] { "glider-dg-acro-like", "beech18-like", "boeing-737-200-like" },
                     Hangar.Placeholders.Select(p => p.Id).ToArray());
        foreach (PlaceholderAircraft p in Hangar.Placeholders)
        {
            _o.WriteLine($"  GAP: {p.Id,-26} {p.Name} — for {p.WantedFor}");
            Assert.False(HasConfig(p.Id), $"{p.Id} has a config now: take it off the placeholder list");
            Assert.False(Hangar.IsPickable(p.Id));
            Assert.Contains("coming soon", p.Name);
        }
        // The Swift is an aerobatic glider, but it is not a Grob 103 Acro or a DG.
        Assert.DoesNotContain("glider-swift-s1-like", Hangar.AerobaticGliders);
    }

    [Fact]
    public void The737800IsRetiredButNothingOfItIsDeleted()
    {
        Assert.Equal(new[] { "boeing-737-like" }, Hangar.Retired);
        Assert.True(HasConfig("boeing-737-like"), "the retired 737-800's config must stay on disk");
        Assert.True(File.Exists(Path.Combine(Repo, "unity", "Assets", "_Project", "Resources", "Models", "boeing-737-like", "LICENSE.txt")),
                    "the retired 737-800's model folder must stay on disk");
        Assert.False(Hangar.IsPickable("boeing-737-like"));
        // And out of the points table, which is the progression's price list.
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repo, "configs", "progression.json")));
        Assert.False(doc.RootElement.GetProperty("aircraftCost").TryGetProperty("boeing-737-like", out _));
    }

    /// <summary>The ids in a C# initialiser: every "something-like" string literal between two markers.</summary>
    private static string[] IdsIn(string file, string from, string to, string pattern = @"""([a-z0-9-]+-like)""")
    {
        string text = File.ReadAllText(file);
        int a = text.IndexOf(from, StringComparison.Ordinal);
        Assert.True(a >= 0, $"{from} not found in {file}");
        int b = text.IndexOf(to, a + from.Length, StringComparison.Ordinal);
        return Regex.Matches(text.Substring(a, b - a), pattern).Select(m => m.Groups[1].Value).ToArray();
    }

    [Fact]
    public void NoPickerOffersARetiredOrPlaceholderAircraft()
    {
        string bridge = Path.Combine(Repo, "unity", "Assets", "_Project", "Scripts", "Bridge");
        var pickers = new (string what, string[] ids)[]
        {
            ("the start menu fleet (SessionSettings.Fleet)", IdsIn(Path.Combine(bridge, "SessionSettings.cs"), "public static readonly (string id, string name)[] Fleet", "};",
                                                                  @"\(""([a-z0-9-]+-like)"",")),   // the id, not the display name
            ("the on-screen Aircraft button", IdsIn(Path.Combine(bridge, "TouchFlightControls.cs"), "private static readonly string[] Fleet", "};")),
            ("the keyboard number keys", IdsIn(Path.Combine(bridge, "KeyboardTestControls.cs"), "KeyCode.Alpha1", "KeyCode.V")),
        };
        foreach (var (what, ids) in pickers)
        {
            _o.WriteLine($"{what}: {ids.Length} types");
            Assert.True(ids.Length >= 8, $"{what}: found only {ids.Length} ids, the parse is wrong");
            foreach (string id in ids)
            {
                Assert.False(Hangar.IsRetired(id), $"{what} offers the retired {id}");
                Assert.False(Hangar.IsPlaceholder(id), $"{what} offers the placeholder {id}");
                Assert.True(HasConfig(id), $"{what} offers {id}, which has no config");
            }
        }
        // The fleet still has every type the ladder and the jobs fly.
        string[] fleet = pickers[0].ids;
        foreach (string id in new[] { "glider-2-33-like", "j3-cub-like", "decathlon-8kcab-like", "great-lakes-like", "glider-grob103-acro-like", "dc3-like",
                                      "pa18-bush-like", "dhc2-beaver-floats-like" })
            Assert.Contains(id, fleet);
    }

    [Fact]
    public void ConfigsProgressionJsonSaysWhatTheCodeDoes()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repo, "configs", "progression.json")));
        JsonElement root = doc.RootElement;

        var levels = root.GetProperty("levels").EnumerateArray().ToArray();
        Assert.Equal(RatingBook.Ladder.Select(l => l.ToString()).ToArray(), levels.Select(l => l.GetProperty("id").GetString()).ToArray());
        Assert.Equal(RatingBook.Ladder.Select(Certificate.LevelName).ToArray(), levels.Select(l => l.GetProperty("display").GetString()).ToArray());

        var stages = root.GetProperty("stages").EnumerateArray().ToArray();
        Assert.Equal(Book.Stages.Select(s => s.Id).ToArray(), stages.Select(s => s.GetProperty("id").GetString()).ToArray());
        foreach (JsonElement j in stages)
        {
            Stage s = Book.Stage(j.GetProperty("id").GetString()!)!;
            Assert.Equal(s.Aircraft, j.GetProperty("aircraft").EnumerateArray().Select(a => a.GetString()).ToArray());
            Assert.Equal(s.PassScore, j.GetProperty("passScore").GetDouble());
            Assert.Equal(s.Requires, j.GetProperty("requires").EnumerateArray().Select(a => a.GetString()).ToArray());
            JsonElement g = j.GetProperty("grants");
            Assert.Equal(s.Grants == CertLevel.None ? null : s.Grants.ToString(), g.ValueKind == JsonValueKind.Null ? null : g.GetString());
        }
        Assert.Equal(Hangar.Placeholders.Select(p => p.Id).ToArray(),
                     root.GetProperty("placeholderAircraft").EnumerateArray().Select(a => a.GetString()).ToArray());
        Assert.Equal(Hangar.Retired, root.GetProperty("retiredAircraft").EnumerateArray().Select(a => a.GetString()).ToArray());
        var jobs = root.GetProperty("jobs").EnumerateArray().ToArray();
        Assert.Equal(Jobs.All.Select(j => j.Id).ToArray(), jobs.Select(j => j.GetProperty("id").GetString()).ToArray());
        foreach (JsonElement j in jobs)
        {
            JobScenario job = Jobs.ById(j.GetProperty("id").GetString()!)!;
            Assert.Equal(job.Aircraft, j.GetProperty("aircraft").EnumerateArray().Select(a => a.GetString()).ToArray());
            Assert.Equal(job.From, j.GetProperty("from").GetString());
            Assert.Equal(job.To, j.GetProperty("to").GetString());
            Assert.Equal(job.MinLevel.ToString(), j.GetProperty("minLevel").GetString());
            Assert.Equal(job.Kind.ToString(), j.GetProperty("kind").GetString());
        }
    }

    // ---------------------------------------------------------------- jobs

    [Fact]
    public void TheJobStubsAreTheOnesAskedForAndNoneIsMilitary()
    {
        Assert.Equal(new[] { "backcountry-supply", "bush-mail-cub", "float-freight-beaver", "float-hop-super-cub",
                             "air-mail-beech18", "airline-dc3", "airline-737-200" },
                     Jobs.All.Select(j => j.Id).ToArray());
        string[] military = { "p51d-like", "f86-sabre-like", "target-drone-like" };
        foreach (JobScenario j in Jobs.All)
        {
            _o.WriteLine($"  {j.Id,-22} {j.Kind,-11} {string.Join("|", j.Aircraft),-40} {j.From}->{j.To} "
                         + $"by {string.Join("+", j.Navigation)}, {j.MinLevel}{(Jobs.ComingSoon(j) ? "  COMING SOON" : "")}");
            Assert.NotEmpty(j.Aircraft);
            foreach (string ac in j.Aircraft)
            {
                Assert.DoesNotContain(ac, military);
                Assert.False(Hangar.IsRetired(ac), $"{j.Id} names the retired {ac}");
                Assert.True(HasConfig(ac) || Hangar.IsPlaceholder(ac), $"{j.Id} names {ac}, which is neither built nor a placeholder");
            }
        }
        // The two that wait on an aeroplane say so; the DC-3 comes before the 737-200.
        Assert.True(Jobs.ComingSoon(Jobs.ById("air-mail-beech18")!));
        Assert.True(Jobs.ComingSoon(Jobs.ById("airline-737-200")!));
        Assert.False(Jobs.ComingSoon(Jobs.ById("airline-dc3")!));
        Assert.False(Jobs.Open(Jobs.ById("airline-737-200")!, CertLevel.Commercial, new List<string>()));
        Assert.True(Jobs.Open(Jobs.ById("airline-737-200")!, CertLevel.Commercial, new List<string> { "airline-dc3" }));
        Assert.False(Jobs.Open(Jobs.ById("backcountry-supply")!, CertLevel.StudentSolo, new List<string>()));
    }

    [Fact]
    public void EveryJobRunsBetweenRealFields()
    {
        var ids = IslandTerrain.World["airports"].Items.Select(a => a.S("id")).ToHashSet();
        foreach (JobScenario j in Jobs.All)
        {
            Assert.Contains(j.From, ids);
            Assert.Contains(j.To, ids);
            Assert.NotEqual(j.From, j.To);
        }
    }

    /// <summary>
    /// THE ABSOLUTE RULE: the player's position is never shown. A job is navigated by the window, the paper map,
    /// the compass and clock, and the simulated ground stations — so the enum of ways to navigate has nothing
    /// else in it, and no brief so much as mentions the alternative.
    /// </summary>
    [Fact]
    public void NoJobCanBeFlownByKnowingWhereTheAeroplaneIs()
    {
        Assert.Equal(new[] { "Pilotage", "PaperMap", "CompassAndClock", "FourCourseRange", "Ndb", "Vor", "Dme" },
                     Enum.GetNames<NavMeans>());
        var giveaway = new Regex(@"\bgps\b|moving map|own-?ship|you are here|coordinates|lat(itude)?\b|distance to (the )?(waypoint|destination)",
                                 RegexOptions.IgnoreCase);
        foreach (JobScenario j in Jobs.All)
        {
            Assert.NotEmpty(j.Navigation);
            Assert.DoesNotMatch(giveaway, j.Title + " " + j.Brief);
        }
    }
}
