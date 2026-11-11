using FlyingGame.Core;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE AERO WIDGET'S CONTROL SURFACE, from the pre-ship audit of 2026-11-11. Three findings, all real on the lane
/// tip: the server bound the wildcard address with no authentication, it sent wildcard CORS so any web page the
/// presenter had open could drive it, and <c>snapshot</c> took a file path straight from the request.
///
/// These tests hold the RULES (<see cref="WidgetAccess"/>). The Unity component that wires sockets to them is not
/// compiled here — <c>WidgetServerSurfaceTests</c> reads its source and checks the wiring is still the rules.
/// </summary>
public sealed class WidgetAccessTests
{
    // ---- the snapshot path: the worst of the three ----------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("shot.png")]
    [InlineData("../../../../../../etc/passwd")]
    [InlineData("/etc/cron.d/widget")]
    [InlineData("/Library/LaunchAgents/evil.plist")]
    [InlineData("..")]
    [InlineData("../..")]
    [InlineData("....//....//shot.png")]
    [InlineData("sub/dir/shot.png")]
    [InlineData(@"..\..\windows\system32\x.png")]
    [InlineData(@"C:\Windows\System32\drivers\etc\hosts")]
    [InlineData("/dev/null")]
    [InlineData("~/.zshrc")]
    [InlineData("shot.png\u0000/../../etc/passwd")]
    [InlineData(".")]
    [InlineData("...")]
    [InlineData("//server/share/x.png")]
    public void ASnapshotNeverLeavesItsOwnDirectory(string? requested)
    {
        string dir = Path.Combine(Path.GetTempPath(), "aero-widget-snapshot-test");
        string path = WidgetAccess.SnapshotPath(dir, requested);
        Assert.True(WidgetAccess.IsInside(dir, path), $"escaped: {requested} -> {path}");
        // Not merely inside: inside at the TOP, with no directory component of its own to create.
        Assert.Equal(Path.GetFullPath(Path.Combine(dir, Path.GetFileName(path))), Path.GetFullPath(path));
        Assert.DoesNotContain("..", Path.GetFileName(path), StringComparison.Ordinal);
    }

    [Fact]
    public void ASnapshotIsAlwaysAPng()
    {
        Assert.EndsWith(".png", WidgetAccess.SnapshotName("x.command"), StringComparison.Ordinal);
        Assert.EndsWith(".png", WidgetAccess.SnapshotName("x.sh"), StringComparison.Ordinal);
        Assert.EndsWith(".png", WidgetAccess.SnapshotName("noextension"), StringComparison.Ordinal);
        Assert.Equal("shot.png", WidgetAccess.SnapshotName("shot.png"));
        Assert.Equal("shot.PNG", WidgetAccess.SnapshotName("shot.PNG"));   // an extension it already has is left alone
    }

    [Fact]
    public void ANamedSnapshotKeepsItsName()
    {
        // The confinement must not cost the feature: a render script that asks for "spin-03.png" gets it.
        Assert.Equal("spin-03.png", WidgetAccess.SnapshotName("/tmp/anywhere/spin-03.png"));
        Assert.Equal("aero-widget.png", WidgetAccess.SnapshotName(null));
    }

    [Fact]
    public void ASnapshotNameCannotGrowWithoutBound()
    {
        Assert.True(WidgetAccess.SnapshotName(new string('a', 5000)).Length <= 110);
    }

    // ---- the token ------------------------------------------------------------------------------------------

    [Fact]
    public void ATokenIs128BitsOfHexAndNeverRepeats()
    {
        var seen = new HashSet<string>();
        for (int i = 0; i < 200; i++)
        {
            string t = WidgetAccess.NewToken();
            Assert.Equal(WidgetAccess.TokenBytes * 2, t.Length);
            Assert.True(WidgetAccess.IsToken(t), t);
            Assert.True(seen.Add(t), "a token repeated");
        }
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0123456789abcdef0123456789abcde", false)]    // one short
    [InlineData("0123456789abcdef0123456789abcdef0", false)]  // one long
    [InlineData("0123456789ABCDEF0123456789ABCDEF", false)]   // upper case is not the form we write
    [InlineData("0123456789abcdef0123456789abcdeg", false)]   // not hex
    [InlineData("0123456789abcdef0123456789abcdef", true)]
    public void OnlyAWellFormedTokenIsAToken(string? s, bool ok) => Assert.Equal(ok, WidgetAccess.IsToken(s));

    [Fact]
    public void TokensAreComparedWholeAndNotByPrefix()
    {
        string a = WidgetAccess.NewToken();
        string lastDigitChanged = a.Substring(0, a.Length - 1) + (a[^1] == 'f' ? 'e' : 'f');
        Assert.True(WidgetAccess.SameToken(a, a));
        Assert.False(WidgetAccess.SameToken(a, lastDigitChanged));   // not compared by prefix
        Assert.False(WidgetAccess.SameToken(a, a.Substring(0, 16))); // nor by the part that matches
        Assert.False(WidgetAccess.SameToken(a, null));
        Assert.False(WidgetAccess.SameToken(null, null));
    }

    [Fact]
    public void TheTokenIsMadeOnceAndReadBackAfterwards()
    {
        string dir = Path.Combine(Path.GetTempPath(), "aero-widget-token-" + Guid.NewGuid().ToString("N"));
        try
        {
            string first = WidgetAccess.ReadOrCreateToken(dir);
            Assert.True(WidgetAccess.IsToken(first));
            Assert.Equal(first, WidgetAccess.ReadOrCreateToken(dir));          // a second run reads the same one
            File.WriteAllText(Path.Combine(dir, WidgetAccess.TokenFileName), "junk");
            Assert.NotEqual("junk", WidgetAccess.ReadOrCreateToken(dir));      // a damaged file is replaced, not trusted
            Assert.True(WidgetAccess.IsToken(WidgetAccess.ReadOrCreateToken(dir)));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // ---- loopback -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("127.0.0.2", true)]
    [InlineData("127.255.255.254", true)]
    [InlineData("::1", true)]
    [InlineData("0:0:0:0:0:0:0:1", true)]
    [InlineData("[::1]", true)]
    [InlineData("::ffff:127.0.0.1", true)]     // an IPv4 client on the dual-stack listener: the case that matters
    [InlineData("::ffff:7f00:1", false)]       // the hex form is NOT read as loopback: better to ask for a token
    [InlineData("192.168.1.42", false)]
    [InlineData("10.0.0.1", false)]
    [InlineData("169.254.3.4", false)]
    [InlineData("fe80::1%en0", false)]
    [InlineData("::", false)]
    [InlineData("128.0.0.1", false)]
    [InlineData("1270.0.0.1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LoopbackIsThisMachineAndNothingElse(string? addr, bool loop)
        => Assert.Equal(loop, WidgetAccess.IsLoopback(addr));

    // ---- the gate -------------------------------------------------------------------------------------------

    // THE REPOSITORY IS PUBLIC, so there is no example token written down anywhere in this file: a test that needs
    // one makes its own, which is also the only honest way to test a secret generator.
    private static readonly string Good = WidgetAccess.NewToken();
    private static readonly string Other = WidgetAccess.NewToken();

    [Fact]
    public void LoopbackNeedsNoTokenAtAll()
    {
        // Rule 4, and the reason the change is safe: every caller on the Mac keeps working unchanged.
        Assert.Null(WidgetAccess.HelloRefusal(loopback: true, presented: null, expected: Good));
        Assert.Null(WidgetAccess.HelloRefusal(loopback: true, presented: "nonsense", expected: Good));
        Assert.Null(WidgetAccess.CommandRefusal(loopback: true, authenticated: false, cmd: "snapshot"));
    }

    [Fact]
    public void AProtocol8ClientWithNoTokenIsTOLDWhyOnTheLan()
    {
        // Rule 5. Glass Overlay connects over the LAN; a silent drop there would be invisible.
        string? why = WidgetAccess.HelloRefusal(loopback: false, presented: null, expected: Good);
        Assert.NotNull(why);
        Assert.Contains("token", why!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hello", why!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("127.0.0.1", why!, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRightTokenLetsALanClientIn()
    {
        Assert.Null(WidgetAccess.HelloRefusal(loopback: false, presented: Good, expected: Good));
        Assert.Null(WidgetAccess.CommandRefusal(loopback: false, authenticated: true, cmd: "controls"));
    }

    [Fact]
    public void TheWrongTokenDoesNot()
    {
        Assert.Equal(WidgetAccess.RefusalBadToken, WidgetAccess.HelloRefusal(false, Other, Good));
        Assert.Equal(WidgetAccess.RefusalBadToken, WidgetAccess.HelloRefusal(false, "short", Good));
        // No usable token on this side cannot mean "let everyone in".
        Assert.Equal(WidgetAccess.RefusalBadToken, WidgetAccess.HelloRefusal(false, Good, null));
        Assert.Equal(WidgetAccess.RefusalBadToken, WidgetAccess.HelloRefusal(false, Good, "junk"));
    }

    [Fact]
    public void AnUnauthenticatedLanConnectionMaySendHelloAndNothingElse()
    {
        Assert.Null(WidgetAccess.CommandRefusal(false, false, "hello"));
        foreach (string cmd in new[] { "controls", "snapshot", "start", "subscribe", "state", "quit", "" })
            Assert.Equal(WidgetAccess.RefusalNotYet, WidgetAccess.CommandRefusal(false, false, cmd));
    }

    // ---- numbers off the wire -------------------------------------------------------------------------------

    [Fact]
    public void ClampingIsNotAGuardAgainstNaN()
    {
        // The reason the Finite() calls exist at all. Both clamps are "if (v < min) … else if (v > max) … else v",
        // and NaN compares false against everything, so it passes straight through what reads like a limit.
        Assert.True(double.IsNaN(Math.Clamp(double.NaN, -1.0, 1.0)));
        Assert.True(float.IsNaN(Math.Clamp(float.NaN, -1f, 1f)));
    }

    [Fact]
    public void NonFiniteNumbersAreNotNumbers()
    {
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.False(WidgetAccess.Finite(bad));
            Assert.Null(WidgetAccess.Finite((double?)bad));
            Assert.Equal(0f, WidgetAccess.FiniteOr((float)bad, 0f));
        }
        foreach (double good in new[] { 0.0, -1.0, 1.0, 1e300, -1e-300 })
        {
            Assert.True(WidgetAccess.Finite(good));
            Assert.Equal(good, WidgetAccess.Finite((double?)good));
        }
        Assert.Null(WidgetAccess.Finite((double?)null));
    }

    [Fact]
    public void ARequestIsAllowedToBeBigButNotUnbounded()
    {
        Assert.True(WidgetAccess.MaxLineBytes >= 8 * 1024);     // room for every command the protocol has
        Assert.True(WidgetAccess.MaxLineBytes <= 1024 * 1024);  // and not a way to spend the machine's memory
        Assert.True(WidgetAccess.MaxBodyBytes >= 32 * 1024);
        Assert.True(WidgetAccess.MaxBodyBytes <= 4 * 1024 * 1024);
        Assert.True(WidgetAccess.MaxHeadBytes >= 4 * 1024);
    }
}
