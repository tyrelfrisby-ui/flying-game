using System.Text.Json;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.Instruments;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE FLEET'S AIRSPEED UNITS. Ty 2026-11-10, asked what rule the 27 undecided types follow after settling five by
/// hand ("mph on the J-3, the 2-33, the Great Lakes and the Pitts; knots on the Extra"), chose "by era and category":
///
///     "Vintage, fabric and experimental types read mph; anything certified modern or turbine reads knots."
///
/// The rule lives in the configs (<c>instruments.airspeed.units</c>), so these tests are what stops it drifting: every
/// type declares a unit, the five he settled by hand read what he said, and the face numbers and the structural limits
/// are in the same unit as each other.
/// </summary>
public class AsiUnitsTests
{
    private readonly ITestOutputHelper _o;
    public AsiUnitsTests(ITestOutputHelper o) { _o = o; }

    private static string Repo
    {
        get { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent; return d!.FullName; }
    }
    private static string[] Files() => Directory.GetFiles(Path.Combine(Repo, "configs", "aircraft"), "*.json").OrderBy(f => f).ToArray();
    private static AircraftConfig Load(string f) => AircraftConfigLoader.LoadFromFile(f);

    /// <summary>NO TYPE MAY BE SILENT. A missing key reads as knots (AsiUnits.Parse), which is a safe fallback but a
    /// terrible default to ship by accident: it would put a new fabric taildragger on a knots dial without anyone
    /// deciding. So a config that leaves the key out fails here, and whoever adds the aeroplane has to answer the
    /// question. The _unitsSource string is required with it: the rule has judgement calls in it and the reason has
    /// to travel with the number.</summary>
    [Fact]
    public void EveryTypeDeclaresWhatItsAirspeedIndicatorReads()
    {
        string[] files = Files();
        Assert.True(files.Length >= 30, $"only {files.Length} configs found — is Repo right?");
        int mph = 0, kt = 0;
        foreach (string f in files)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(f));
            string id = Path.GetFileNameWithoutExtension(f);
            Assert.True(doc.RootElement.TryGetProperty("instruments", out JsonElement ins), $"{id}: no instruments block (needs instruments.airspeed.units)");
            Assert.True(ins.TryGetProperty("airspeed", out JsonElement asi), $"{id}: instruments has no airspeed block");
            Assert.True(asi.TryGetProperty("units", out JsonElement units), $"{id}: instruments.airspeed has no units");
            string u = units.GetString() ?? "";
            Assert.True(u is "mph" or "kt", $"{id}: units \"{u}\" is neither \"mph\" nor \"kt\" (the fleet has no third unit; the Grob's km/h face art is noted in its config)");
            Assert.True(asi.TryGetProperty("_unitsSource", out _), $"{id}: instruments.airspeed has no _unitsSource — say WHY this type reads {u}");
            if (u == "mph") mph++; else kt++;
        }
        _o.WriteLine($"{files.Length} types: {mph} mph, {kt} kt");
        // Both buckets are populated — a rule that put the whole fleet in one unit would not be this rule.
        Assert.True(mph > 5 && kt > 5, $"{mph} mph / {kt} kt: the era-and-category rule should split the fleet");
    }

    /// <summary>THE FIVE TY SETTLED BY HAND, verbatim: "mph on the J-3, the 2-33, the Great Lakes and the Pitts; knots
    /// on the Extra". The Great Lakes and the Extra are the two his word fixed AGAINST what the rule alone would say,
    /// so they are the two most likely to be "corrected" by someone reading the rule and not the history.</summary>
    [Theory]
    [InlineData("j3-cub-like", AsiUnit.Mph)]
    [InlineData("j3-cub-floats-like", AsiUnit.Mph)]
    [InlineData("glider-2-33-like", AsiUnit.Mph)]
    [InlineData("great-lakes-like", AsiUnit.Mph)]     // his explicit call; a modern 2T-1A-2 could be either
    [InlineData("pitts-s2b-like", AsiUnit.Mph)]
    [InlineData("extra-300-like", AsiUnit.Knots)]     // his explicit call; the era rule alone would say mph
    public void TheTypesTheOwnerSettledByHandReadWhatHeSaid(string id, AsiUnit expected)
    {
        AircraftConfig c = Load(Path.Combine(Repo, "configs", "aircraft", id + ".json"));
        Assert.Equal(expected, AsiUnits.For(c));
    }

    /// <summary>The conversions, once, in one place: 1 m/s is 1.943844 kt and 2.2369363 mph, and the round trip
    /// returns the speed it started with. Four copies of 1.9438 at three precisions used to be scattered through the
    /// drawing code.</summary>
    [Fact]
    public void TheConversionsAreTheConversions()
    {
        Assert.Equal(100.0 * 1.943844, AsiUnits.FromMs(AsiUnit.Knots, 100.0), 9);
        Assert.Equal(100.0 * 2.2369363, AsiUnits.FromMs(AsiUnit.Mph, 100.0), 9);
        Assert.Equal(60.0, AsiUnits.ToMs(AsiUnit.Mph, AsiUnits.FromMs(AsiUnit.Mph, 60.0)), 9);
        Assert.Equal("MPH", AsiUnits.Caption(AsiUnit.Mph));
        Assert.Equal("KT", AsiUnits.Caption(AsiUnit.Knots));
        Assert.Equal(AsiUnit.Mph, AsiUnits.Parse("MPH"));      // case does not matter
        Assert.Equal(AsiUnit.Knots, AsiUnits.Parse("kt"));
        Assert.Equal(AsiUnit.Knots, AsiUnits.Parse(null));     // the conservative fallback
    }

    /// <summary>A FACE IS ONE AEROPLANE IN ONE UNIT. Where a type declares arcs and a red line (the four with
    /// modelled cockpits), every one of those numbers must be in the declared unit, which means they must agree with
    /// limits.vneMs once converted. This is the test that would have caught the 172, whose white arc and green floor
    /// were a 172M's mph while its green top, yellow arc and red line were a 172S's knots: its red line, 164, was
    /// Vne in KNOTS on a face labelled MPH — 25 % fast.</summary>
    [Fact]
    public void ADeclaredRedLineIsVneInTheDialsOwnUnit()
    {
        int checked_ = 0;
        foreach (string f in Files())
        {
            AircraftConfig c = Load(f);
            AirspeedInstrumentConfig? spec = c.Instruments?.Airspeed;
            if (spec is not { Redline: > 0 }) continue;
            double vne = AsiUnits.FromMs(AsiUnits.For(c), c.Limits.VneMs);
            // 3 %: a TCDS publishes Vne in CAS and limits.vneMs is an IAS the sim flies to, so they are allowed to
            // differ a little. They are NOT allowed to differ by the 1.15 between knots and mph.
            Assert.True(Math.Abs(spec.Redline - vne) / vne < 0.03,
                $"{c.Id}: red line {spec.Redline:F0} {AsiUnits.Word(AsiUnits.For(c))} vs limits.vneMs {c.Limits.VneMs} m/s = {vne:F0} — are they the same unit?");
            // The arcs must sit under the red line and in order.
            if (spec.Arcs?.Green is { Length: >= 2 } g) Assert.True(g[0] < g[1] && g[1] <= spec.Redline, $"{c.Id}: green arc {g[0]}-{g[1]} against red line {spec.Redline}");
            if (spec.Arcs?.Yellow is { Length: >= 2 } y) Assert.True(y[0] < y[1] && y[1] <= spec.Redline + 0.01, $"{c.Id}: yellow arc {y[0]}-{y[1]} against red line {spec.Redline}");
            if (spec.Arcs?.White is { Length: >= 2 } w) Assert.True(w[0] < w[1] && w[1] < spec.Redline, $"{c.Id}: white arc {w[0]}-{w[1]} against red line {spec.Redline}");
            Assert.True(spec.ScaleMax >= spec.Redline, $"{c.Id}: the red line {spec.Redline} is off the top of the scale ({spec.ScaleMax})");
            checked_++;
            _o.WriteLine($"{c.Id,-22} {AsiUnits.Word(AsiUnits.For(c)),-3}  scale {spec.ScaleMin}-{spec.ScaleMax}  red line {spec.Redline:F0} (Vne {vne:F0})");
        }
        Assert.True(checked_ >= 4, $"only {checked_} types declare a red line — expected the four with modelled cockpits");
    }

    /// <summary>THE DIAL REACHES VNE. A scale that stops short of the red line has nowhere to put it, and the dial
    /// this was built for had no red line at all: its top was 2.2 × the spawn speed and nothing checked it against
    /// the airframe. Every type in the fleet, both units.</summary>
    [Fact]
    public void EveryDialsScaleReachesItsOwnRedLine()
    {
        foreach (string f in Files())
        {
            AircraftConfig c = Load(f);
            AsiDial d = AsiDial.For(c, c.SpawnIasMs);
            double vne = AsiUnits.FromMs(d.Unit, c.Limits.VneMs);
            Assert.True(d.Top >= vne - 0.5, $"{c.Id}: scale top {d.Top:F0} {AsiUnits.Word(d.Unit)} does not reach Vne {vne:F0}");
            Assert.True(d.Redline > 0, $"{c.Id}: no red line on the dial");
            Assert.True(d.TickStep > 0 && d.LabelStep >= d.TickStep, $"{c.Id}: tick {d.TickStep} label {d.LabelStep}");
            Assert.True(d.StallTop >= 0 && d.StallTop < d.Top, $"{c.Id}: low-speed arc top {d.StallTop:F0} against scale top {d.Top:F0}");
            _o.WriteLine($"{c.Id,-26} {AsiUnits.Word(d.Unit),-3}  0-{d.Top:F0} every {d.TickStep:F0} (numerals {d.LabelStep:F0})  red line {d.Redline:F0}  low-speed arc to {d.StallTop:F0}");
        }
    }

    /// <summary>A DECLARED FACE IS USED VERBATIM by the flat dials, so the exterior dial and the modelled panel's own
    /// face mark the same speeds. Before this the flat dial guessed its scale off the spawn speed and read knots even
    /// on the Pitts, whose panel ASI has read mph (TCDS A8SO, 212 mph CAS) since 2026-11-02.</summary>
    [Fact]
    public void TheFlatDialUsesTheFaceTheConfigPublishes()
    {
        AircraftConfig pitts = Load(Path.Combine(Repo, "configs", "aircraft", "pitts-s2b-like.json"));
        AsiDial d = AsiDial.For(pitts, pitts.SpawnIasMs);
        Assert.Equal(AsiUnit.Mph, d.Unit);
        Assert.Equal(240.0, d.Top, 6);        // the published scale, not a derivation
        Assert.Equal(212.0, d.Redline, 6);    // TCDS A8SO rev 28, mph CAS
        Assert.Equal(63.0, d.GreenLo, 6);     // Aviat S-2B Owner's Manual STALLING SPEED (Ty t904u)
        Assert.Equal(63.0, d.StallTop, 6);    // the low-speed arc ends at the published stall, not at a guess
        Assert.False(d.HasWhite);             // an S-2B has no flaps, so no white arc

        AircraftConfig c172 = Load(Path.Combine(Repo, "configs", "aircraft", "c172-like.json"));
        AsiDial k = AsiDial.For(c172, c172.SpawnIasMs);
        Assert.Equal(AsiUnit.Knots, k.Unit);
        Assert.Equal(163.0, k.Redline, 6);    // 172S POH, KIAS — and limits.vneMs 84 m/s = 163.3 kt
        Assert.True(k.HasWhite && k.WhiteLo == 40.0 && k.WhiteHi == 85.0, "the 172's white arc is VS0 40 to VFE 85 KIAS");
    }

    /// <summary>A type with no authored face still gets a sane one: the J-3, in mph, with a red line at its Vne.</summary>
    [Fact]
    public void ADerivedFaceIsStillInTheTypesOwnUnit()
    {
        AircraftConfig j3 = Load(Path.Combine(Repo, "configs", "aircraft", "j3-cub-like.json"));
        AsiDial d = AsiDial.For(j3, j3.SpawnIasMs);
        Assert.Equal(AsiUnit.Mph, d.Unit);
        Assert.Equal(54.5 * AsiUnits.MsToMph, d.Redline, 6);                 // 122 mph
        Assert.True(d.Top >= d.Redline, "the J-3's dial reaches its Vne");
        Assert.Equal(100.0 * AsiUnits.MsToMph, d.Read(100.0), 9);            // the needle reads mph
        Assert.Equal("MPH", d.Caption);
        Assert.False(d.HasGreen);                                            // the J-3's arcs are not authored here
    }
}
