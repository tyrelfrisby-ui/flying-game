using FlyingGame.Sim.Support;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The HELP link and the diagnostics block. Both of these exist on the promise that the app sends nothing
/// about the person to the support system, and a promise in a comment is not a promise — so what is in the
/// URL and what is in the block are asserted here.
/// </summary>
public class SupportLinkTests
{
    [Fact]
    public void TheHelpUrlCarriesTheBuildAndThePlatformAndNothingElse()
    {
        string url = SupportLink.Url("2026.1017.2007", SupportPlatform.IpadOs);
        Assert.Equal("https://aeroplayground.app/support?v=2026.1017.2007&p=ipados", url);
        // Exactly two parameters, and both of them are about the build.
        Assert.Equal(2, url.Split('?')[1].Split('&').Length);
        Assert.Equal("https://aeroplayground.app/support?v=2026.1017.2007&p=macos",
                     SupportLink.Url("2026.1017.2007", SupportPlatform.MacOs));
        Assert.Equal("https://aeroplayground.app/support?v=2026.1017.2007&p=ios",
                     SupportLink.Url("2026.1017.2007", SupportPlatform.Ios));
    }

    [Theory]
    // A version reaches a URL, so it is checked rather than trusted — whatever a build script ever puts in it.
    [InlineData("2026.1017.2007", "2026.1017.2007")]
    [InlineData("1.2.3-beta4", "1.2.3-beta4")]
    [InlineData("2026.1017 &pilot=Ty", "2026.10172007pilot-Ty")]   // no space, no & and no =: one parameter still
    [InlineData("", null)]
    public void TheVersionCannotAddAParameterOfItsOwn(string version, string? expect)
    {
        string url = SupportLink.Url(version, SupportPlatform.Ios);
        if (expect == null) { Assert.Equal("https://aeroplayground.app/support?p=ios", url); return; }
        Assert.DoesNotContain(" ", url);
        Assert.Equal(2, url.Split('?')[1].Split('&').Length);
    }

    private static SupportLink.Facts Flying() => new()
    {
        Version = "2026.1017.2007",
        Platform = SupportPlatform.IpadOs,
        DeviceModel = "iPad15,4",
        OperatingSystem = "iPadOS 26.0",
        Aircraft = "pa18-cub-like",
        PilotName = "Tyrel",
    };

    [Fact]
    public void TheDiagnosticsBlockSaysWhatIsWrongWithTheBuildAndNotWhoIsFlyingIt()
    {
        var f = Flying();
        f.Notes = new[]
        {
            "NetSession: Tyrel joined room ABC123",
            "StoreKit: finished com.tyfrisby.aeroplayground.unlock transaction 2000000812345678",
            "Trim solver did not converge for pa18-cub-like at 46 kt",
        };
        string d = SupportLink.Diagnostics(f);

        Assert.Contains("2026.1017.2007", d);
        Assert.Contains("iPad15,4", d);
        Assert.Contains("pa18-cub-like", d);
        Assert.Contains("Trim solver did not converge", d);      // the half a support agent needs

        Assert.DoesNotContain("Tyrel", d);                       // the half they must not have
        Assert.DoesNotContain("com.tyfrisby.aeroplayground", d);
        Assert.DoesNotContain("2000000812345678", d);
        Assert.Contains("<pilot>", d);
        Assert.Contains("<product>", d);
        Assert.Contains("<id>", d);
    }

    [Fact]
    public void ACrashIsTheFirstThingTheBlockSays()
    {
        var f = Flying();
        f.LastCrash = "NullReferenceException in TowController.Release\n  at FlyingGame.Bridge.TowController…";
        string d = SupportLink.Diagnostics(f);
        Assert.Contains("last run ended in a crash", d);
        Assert.Contains("NullReferenceException", d);
        // A stack trace arrives with newlines in it; one line each keeps the block readable to the person pasting it.
        foreach (string line in d.Split('\n')) Assert.True(line.Length <= 320, line);
    }

    [Fact]
    public void AQuietRunSaysSoRatherThanLookingBroken()
    {
        Assert.Contains("warnings: none this run", SupportLink.Diagnostics(Flying()));
    }

    [Fact]
    public void OnlyTheLastHandfulOfWarningsTravel()
    {
        var f = Flying();
        var many = new string[40];
        for (int i = 0; i < many.Length; i++) many[i] = $"warning number {i}";
        f.Notes = many;
        string d = SupportLink.Diagnostics(f);
        Assert.Contains("warning number 39", d);                 // the newest, which is the one that matters
        Assert.DoesNotContain("warning number 0\n", d);
        Assert.Contains($"(last {SupportLink.MaxNotes} of 40)", d);
    }

    [Theory]
    // Scrubbing must not eat the numbers a flight-model report is made of.
    [InlineData("stall at 46.2 kt, 1,423 m, 2026-10-17")]
    [InlineData("height 2134 ground 1378 step 602")]
    public void ScrubbingLeavesTheMeasurementsAlone(string line)
    {
        Assert.Equal(line, SupportLink.Scrub(line, "Tyrel"));
    }

    [Fact]
    public void AShortPilotNameIsNotUsedAsAScrubPattern()
    {
        // "Ty" inside "TyreLength" or "type" would redact half the log. Three characters minimum.
        Assert.Equal("the type of tyre", SupportLink.Scrub("the type of tyre", "Ty"));
    }
}
