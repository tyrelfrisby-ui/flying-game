using System.Text.RegularExpressions;
using FlyingGame.Sim.Jobs;
using FlyingGame.Sim.Ratings;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Owner, 2026-11-01: the player never reads the words "private", "certified" or "license"/"licence". Not on a
/// badge, a lesson, a debrief, an achievement, a menu or a config the game shows.
///
/// Two halves. The first asks every generator of level and badge text for everything it can say and checks
/// it. The second scans the SOURCE: every string literal in the Sim and the Unity Bridge scripts (comments and
/// identifiers are not player text and are skipped), and every config the player can be shown.
///
/// THE EXEMPTIONS ARE LISTED, not implied, and each one says why:
///  * docs/CREDITS.md (the Credits screen) and the models' LICENSE.txt files — third-party legal attribution.
///    CC BY requires naming the licence and linking it; rewording somebody else's terms is not ours to do.
///  * src/FlyingGame.Core/IslandData.Generated.cs — generated design data ("No certified IFR values"), never drawn.
///  * The C# keyword: `private` as a modifier is code, not text.
///  * LegacyLevels.FromName's `case "private":` — it READS the old enum name out of an old save and maps it to
///    Pilot; it is the migration, and it is never shown.
/// </summary>
public class PlayerTextTests
{
    private readonly ITestOutputHelper _o;
    public PlayerTextTests(ITestOutputHelper o) { _o = o; }

    private static readonly Regex Banned = new(@"\bprivate\b|certified|licen[cs]e", RegexOptions.IgnoreCase);

    private static string Repo
    {
        get
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent;
            return d!.FullName;
        }
    }

    [Fact]
    public void TheBannedWordsAreCaughtByTheCheckItself()
    {
        // A scanner that matches nothing passes everything; prove it bites.
        Assert.Matches(Banned, "Private pilot");
        Assert.Matches(Banned, "GLIDER PRIVATE");
        Assert.Matches(Banned, "a certified instructor");
        Assert.Matches(Banned, "your licence");
        Assert.Matches(Banned, "pilot license");
        Assert.DoesNotMatch(Banned, "Blind Flying");
        Assert.DoesNotMatch(Banned, "Commercial pilot");
    }

    [Fact]
    public void EveryLevelAndBadgeTheGameCanDrawIsClean()
    {
        var said = new List<string>();
        RatingBook book = RatingBook.Default();
        foreach (CertLevel l in Enum.GetValues<CertLevel>())
        {
            said.Add(Certificate.LevelName(l));
            said.Add(Certificate.LevelAbbrev(l));
            var s = new PilotStanding { Level = l };
            foreach (RatingClass c in Certificate.BadgeOrder) s.Classes.Add(c);
            said.Add(Certificate.ShortBadge(s));
            said.Add(Certificate.Describe(s));
        }
        foreach (Stage st in book.Stages) { said.Add(st.Title); said.Add(st.Note); }
        foreach (PlaceholderAircraft p in Hangar.Placeholders) { said.Add(p.Name); said.Add(p.WantedFor); }
        foreach (JobScenario j in Jobs.All) { said.Add(j.Title); said.Add(j.Brief); }
        for (int k = 0; k < Achievements.IacCategories.Length; k++) said.Add(Achievements.Label(AchievementKind.Aerobatic, k));
        for (int k = 0; k < Achievements.SoaringTiers.Length; k++) said.Add(Achievements.Label(AchievementKind.Soaring, k));
        for (int k = 0; k < Achievements.StolTiers.Length; k++) said.Add(Achievements.Label(AchievementKind.Stol, k));
        // Old badges off the wire are drawn in the current words.
        said.Add(LegacyLevels.UpgradeBadge("GLIDER COMM · ASEL PVT"));
        said.Add(LegacyLevels.UpgradeBadge("ATP ASEL AMEL"));

        var bad = said.Where(t => Banned.IsMatch(t ?? "")).ToList();
        _o.WriteLine($"{said.Count} strings checked");
        Assert.True(bad.Count == 0, "banned word in player text: " + string.Join(" | ", bad));
        // And the instrument level is called what the owner calls it.
        Assert.Equal("Blind Flying", Certificate.LevelName(CertLevel.Instrument));
        Assert.Equal(new[] { "Student pilot, pre-solo", "Student pilot, solo", "Pilot", "Blind Flying", "Commercial pilot", "Flight Instructor" },
                     RatingBook.Ladder.Select(Certificate.LevelName).ToArray());
    }

    /// <summary>String literals on code lines (not comments) in a C# file, with their line numbers.</summary>
    private static IEnumerable<(int line, string text)> Literals(string file)
    {
        string[] lines = File.ReadAllLines(file);
        var lit = new Regex(@"@?\$?@?""(?:[^""\\]|\\.)*""");
        for (int i = 0; i < lines.Length; i++)
        {
            string code = lines[i].TrimStart();
            if (code.StartsWith("//") || code.StartsWith("*") || code.StartsWith("/*")) continue;
            foreach (Match m in lit.Matches(lines[i]))
            {
                // A literal after a // on the same line is inside a comment.
                int c = lines[i].IndexOf("//", StringComparison.Ordinal);
                if (c >= 0 && c < m.Index && lines[i].Substring(0, c).Count(ch => ch == '"') % 2 == 0) continue;
                yield return (i + 1, m.Value);
            }
        }
    }

    [Fact]
    public void NoStringLiteralTheGameCanShowCarriesABannedWord()
    {
        var roots = new[]
        {
            Path.Combine(Repo, "src", "FlyingGame.Sim"),
            Path.Combine(Repo, "src", "FlyingGame.Core"),
            Path.Combine(Repo, "unity", "Assets", "_Project", "Scripts", "Bridge"),
        };
        var exempt = new[] { "IslandData.Generated.cs" };
        var exemptLiterals = new[] { ("Certificate.cs", "\"private\"") };
        var bad = new List<string>();
        int files = 0;
        foreach (string root in roots)
            foreach (string f in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (exempt.Contains(Path.GetFileName(f)) || f.Contains($"{Path.DirectorySeparatorChar}obj")) continue;
                files++;
                foreach (var (line, text) in Literals(f))
                    if (Banned.IsMatch(text) && !exemptLiterals.Contains((Path.GetFileName(f), text))) bad.Add($"{Path.GetRelativePath(Repo, f)}:{line} {text}");
            }
        _o.WriteLine($"{files} files scanned");
        foreach (string b in bad) _o.WriteLine("  " + b);
        Assert.True(files > 100, "the scan found almost nothing to scan; the paths are wrong");
        Assert.True(bad.Count == 0, $"{bad.Count} string literal(s) with a banned word:\n" + string.Join("\n", bad));
    }

    [Fact]
    public void NoConfigThePlayerReadsCarriesABannedWord()
    {
        var files = new List<string> { Path.Combine(Repo, "configs", "progression.json"), Path.Combine(Repo, "configs", "glide-table.json") };
        foreach (string dir in new[] { "challenges", "aircraft", "atmosphere" })
            files.AddRange(Directory.GetFiles(Path.Combine(Repo, "configs", dir), "*.json"));
        var bad = new List<string>();
        foreach (string f in files)
        {
            string[] lines = File.ReadAllLines(f);
            for (int i = 0; i < lines.Length; i++)
            {
                // A LEADING UNDERSCORE MEANS AUTHORING PROVENANCE, NOT PLAYER TEXT. The configs carry their own
                // sources beside the numbers they justify — "_cgLimitsSource", "_unitsRule", "_armSource",
                // "_comment" — and that is where this project records WHY a figure is what it is. Those fields
                // quote type certificates and the owner's own words, and a type certificate says "certified"
                // because that is what the document is called. The player never sees a byte of it: nothing reads
                // an underscore key, and AircraftConfig has no property for one.
                //
                // Scanning them cost seven configs' worth of false positives (the ASI-units work, 2026-11-10) and
                // the only way to pass was to reword a quoted source, which is worse than the test it satisfies.
                if (IsAuthoringField(lines[i])) continue;
                if (Banned.IsMatch(lines[i])) bad.Add($"{Path.GetRelativePath(Repo, f)}:{i + 1} {lines[i].Trim()}");
            }
        }
        _o.WriteLine($"{files.Count} configs scanned");
        Assert.True(bad.Count == 0, "banned word in a config:\n" + string.Join("\n", bad));
    }

    /// <summary>A JSON line whose KEY begins with an underscore: this project's convention for the note that says
    /// where a number came from. Player text never lives in one.</summary>
    private static bool IsAuthoringField(string line) => AuthoringKey.IsMatch(line);
    private static readonly Regex AuthoringKey = new(@"^\s*""_[A-Za-z0-9_]*""\s*:");
}
