using System.Collections.Generic;
using System.IO;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.Vr;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// WHETHER A TYPE CAN JETTISON ITS CANOPY IS AUTHORED, PER TYPE, IN ITS OWN CONFIG — and these hold that it stays
/// that way.
///
/// Ty, 2026-11-11, having bailed out of the Extra after a hard landing: *"the canopy shattered even though it didn't
/// hit anything … And on the ground it should not fall off it should stay open. The extra has no jettison feature."*
/// Until then the bail-out pulled the hinge pins of every canopy in the fleet and <see cref="CanopySpec.For(string)"/>
/// answered "yes" to both aerobatic types from a lookup on the aircraft id, where nobody could see it and nobody
/// could correct it.
///
/// The three things worth nailing down:
///  1. every config in the fleet answers, and says where the answer came from. A missing section is not an answer;
///  2. the code cannot invent one: a spec built from an id, or from a measured part, has no jettison;
///  3. the two Ty has ruled on are what he said.
/// </summary>
public class CanopyJettisonTests
{
    private readonly ITestOutputHelper _o;
    public CanopyJettisonTests(ITestOutputHelper o) { _o = o; }

    private static string ConfigDir()
    {
        var d = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent;
        Assert.NotNull(d);
        return Path.Combine(d!.FullName, "configs", "aircraft");
    }

    private static IEnumerable<AircraftConfig> Fleet()
    {
        foreach (string f in Directory.GetFiles(ConfigDir(), "*.json"))
            yield return AircraftConfigLoader.LoadFromFile(f);
    }

    /// <summary>
    /// EVERY TYPE ANSWERS, AND SOURCES ITS ANSWER. The point of moving this out of code was that it be visible and
    /// auditable; an unsourced answer is a guess in hiding, and a missing section is a guess nobody wrote down.
    /// "ASSUMED" is a perfectly good source — it says so, and it is a question for Ty rather than a fact.
    /// </summary>
    [Fact]
    public void EveryTypeAuthorsItsCanopyJettisonWithASource()
    {
        var bad = new List<string>();
        int yes = 0, n = 0;
        foreach (AircraftConfig c in Fleet())
        {
            n++;
            if (c.Canopy == null) { bad.Add($"{c.Id}: no canopy section"); continue; }
            if (string.IsNullOrWhiteSpace(c.Canopy.JettisonSource)) { bad.Add($"{c.Id}: no jettisonSource"); continue; }
            if (c.Canopy.Jettison) yes++;
            _o.WriteLine($"  {c.Id,-28} jettison {(c.Canopy.Jettison ? "YES" : "no ")}  {c.Canopy.JettisonSource}");
        }
        Assert.True(n >= 30, $"only {n} configs found");
        Assert.True(bad.Count == 0, string.Join("; ", bad));
        _o.WriteLine($"  {yes} of {n} types can jettison a canopy");
    }

    /// <summary>
    /// THE GLIDER FLEET, ALL FOUR, on Ty's own decision 2026-11-11: asked whether the gliders should have a
    /// jettisonable canopy he said <em>"Yes — all four gliders"</em>. They had been authored false the same day as
    /// the conservative guess with "QUESTION FOR TY" in the source field; this pins his answer so nobody re-guesses
    /// it. A sailplane is flown with a parachute on and the canopy is the only way out.
    /// </summary>
    [Fact]
    public void EveryGliderCanJettisonItsCanopy()
    {
        var by = new Dictionary<string, bool>();
        foreach (AircraftConfig c in Fleet()) by[c.Id] = c.Canopy!.Jettison;
        foreach (string id in new[] { "glider-2-33-like", "glider-grob103-acro-like",
                                      "glider-swift-s1-like", "glider-eb29r-like" })
        {
            Assert.True(by.ContainsKey(id), id);
            Assert.True(by[id], $"{id}: Ty 2026-11-11 said all four gliders jettison");
        }
    }

    /// <summary>The two Ty has ruled on, and the conservative majority. The Extra is the one he flew.</summary>
    [Fact]
    public void TheExtraHasNoJettisonAndThePittsDoes()
    {
        var by = new Dictionary<string, bool>();
        foreach (AircraftConfig c in Fleet()) by[c.Id] = c.Canopy!.Jettison;
        Assert.False(by["extra-300-like"]);          // Ty 2026-11-11: "The extra has no jettison feature"
        Assert.True(by["pitts-s2b-like"]);           // Ty 2026-11-02: the red knob pulls the hinge pins
        Assert.True(by["f86-sabre-like"]);           // the seat cannot fire through an attached canopy
        // Types with no canopy at all cannot have a canopy jettison.
        foreach (string id in new[] { "j3-cub-like", "great-lakes-like", "stearman-pt17-like", "aircam-like",
                                      "hang-glider-like", "paraglider-en-b-like", "c172-like" })
            Assert.False(by[id], id);
    }

    /// <summary>
    /// THE CODE CANNOT INVENT A JETTISON. A spec built from an aircraft id, or measured off a model's part, has
    /// none: only a config can give it one. This is the structural half of the fix — the bug was that the id knew.
    /// </summary>
    [Fact]
    public void NoSpecBuiltWithoutAConfigHasAJettison()
    {
        Assert.False(CanopySpec.For("pitts-s2b-like").HasJettison);
        Assert.False(CanopySpec.For("extra-300-like").HasJettison);
        Assert.False(CanopySpec.For("anything-else").HasJettison);
        Assert.False(CanopySpec.ForPart(1.6, 0.7, 0.45).HasJettison);
        Assert.False(CanopySpec.HasJettisonIn(null));
        Assert.False(CanopySpec.HasJettisonIn(new AircraftConfig { Id = "pitts-s2b-like" }));
    }

    /// <summary>And a spec built WITH one carries what the config says, geometry untouched.</summary>
    [Fact]
    public void TheConfigIsWhatGivesASpecItsJettison()
    {
        var with = new AircraftConfig { Id = "pitts-s2b-like", Canopy = new CanopyConfig { Jettison = true, JettisonSource = "test" } };
        CanopySpec s = CanopySpec.For(with);
        Assert.True(s.HasJettison);
        Assert.Equal(CanopySpec.For("pitts-s2b-like").MassKg, s.MassKg, 9);
        Assert.Equal(CanopyMotion.SlideThenHingeRight, s.Motion);
        Assert.NotNull(s.StopTab);

        var without = new AircraftConfig { Id = "pitts-s2b-like", Canopy = new CanopyConfig { Jettison = false, JettisonSource = "test" } };
        Assert.False(CanopySpec.For(without).HasJettison);
    }

    /// <summary>A fleet config read off disk gives the same answer as the file says: the loader carries the section.</summary>
    [Fact]
    public void TheLoaderCarriesTheCanopySection()
    {
        AircraftConfig extra = AircraftConfigLoader.LoadFromFile(Path.Combine(ConfigDir(), "extra-300-like.json"));
        Assert.NotNull(extra.Canopy);
        Assert.False(CanopySpec.For(extra).HasJettison);
        Assert.Contains("no jettison feature", extra.Canopy!.JettisonSource);

        AircraftConfig pitts = AircraftConfigLoader.LoadFromFile(Path.Combine(ConfigDir(), "pitts-s2b-like.json"));
        Assert.True(CanopySpec.For(pitts).HasJettison);
    }
}
