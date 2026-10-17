using FlyingGame.Core.Lan;
using Xunit;

namespace FlightTests;

/// <summary>
/// The one part of device-to-device flying (owner 2026-10-17: "then the two ipads in a field") that can be
/// wrong without the compiler noticing: taking apart what the native side reports. Everything else in that
/// path is MultipeerConnectivity's own doing and can only be tried on two devices; this is string handling,
/// and a mistake here renames every pilot in the group.
/// </summary>
public class PeerEventTests
{
    [Fact]
    public void APilotArrivingCarriesTheirNameAndAeroplane()
    {
        Assert.True(PeerEvent.TryParse("p\u001fa1b2c3d4|Ty|cirrus-sr22-like\u001f", out PeerEventKind k,
                                       out string id, out string name, out string? ac, out string? payload));
        Assert.Equal(PeerEventKind.Peer, k);
        Assert.Equal("a1b2c3d4", id);
        Assert.Equal("Ty", name);
        Assert.Equal("cirrus-sr22-like", ac);
        Assert.Equal("", payload);
    }

    [Fact]
    public void AStatePacketComesThroughWhole()
    {
        const string json = "{\"t\":\"s\",\"p\":[1,2,3],\"note\":\"a|b\"}";
        Assert.True(PeerEvent.TryParse($"s\u001fa1b2c3d4|Ty|cub-like\u001f{json}", out PeerEventKind k,
                                       out string id, out _, out _, out string? payload));
        Assert.Equal(PeerEventKind.State, k);
        Assert.Equal("a1b2c3d4", id);
        Assert.Equal(json, payload);
    }

    /// <summary>A payload with the field separator in it must not be cut short — only the first two are ours.</summary>
    [Fact]
    public void ASeparatorInThePayloadIsNotASeparator()
    {
        Assert.True(PeerEvent.TryParse("s\u001fid1|Ty|cub-like\u001f{\"a\":\"x\u001fy\"}", out _, out _, out _, out _, out string? payload));
        Assert.Equal("{\"a\":\"x\u001fy\"}", payload);
    }

    /// <summary>A pilot who types a pipe into their name keeps it, and their aeroplane is still their aeroplane.</summary>
    [Fact]
    public void APipeInAPilotsNameDoesNotEatTheAeroplane()
    {
        Assert.True(PeerEvent.TryParse("p\u001fid1|Ty|the|Kid|pa18-cub-like\u001f", out _, out string id, out string name, out string? ac, out _));
        Assert.Equal("id1", id);
        Assert.Equal("Ty|the|Kid", name);
        Assert.Equal("pa18-cub-like", ac);
    }

    /// <summary>
    /// Somebody running something else, or an older build, is still a pilot who is there — their display name
    /// becomes their name and their id, rather than being thrown away.
    /// </summary>
    [Fact]
    public void ADisplayNameWeDidNotWriteIsStillAPilot()
    {
        Assert.True(PeerEvent.TryParse("p\u001fSomebody's iPad\u001f", out PeerEventKind k, out string id, out string name, out string? ac, out _));
        Assert.Equal(PeerEventKind.Peer, k);
        Assert.Equal("Somebody's iPad", id);
        Assert.Equal("Somebody's iPad", name);
        Assert.Null(ac);
    }

    [Fact]
    public void GoingAndFailingAreTheirOwnKinds()
    {
        Assert.True(PeerEvent.TryParse("b\u001fid1|Ty|cub-like\u001f", out PeerEventKind gone, out string id, out _, out _, out _));
        Assert.Equal(PeerEventKind.Bye, gone);
        Assert.Equal("id1", id);

        Assert.True(PeerEvent.TryParse("e\u001f\u001fbrowsing failed: no wifi", out PeerEventKind err, out _, out _, out _, out string? why));
        Assert.Equal(PeerEventKind.Error, err);
        Assert.Equal("browsing failed: no wifi", why);
    }

    [Fact]
    public void RubbishIsRefusedRatherThanGuessedAt()
    {
        Assert.False(PeerEvent.TryParse(null, out _, out _, out _, out _, out _));
        Assert.False(PeerEvent.TryParse("", out _, out _, out _, out _, out _));
        Assert.False(PeerEvent.TryParse("p", out _, out _, out _, out _, out _));              // no fields at all
        Assert.False(PeerEvent.TryParse("z\u001fid1|Ty|cub\u001f", out _, out _, out _, out _, out _));   // unknown kind
    }

    /// <summary>
    /// Our own line has to fit in the 63 bytes MCPeerID allows, or the framework throws instead of returning
    /// nil — and a Japanese pilot name is three bytes a character, so counting characters is not enough.
    /// </summary>
    [Fact]
    public void OurOwnNameIsTrimmedToWhatTheFrameworkAllows()
    {
        string d = PeerEvent.Display("a1b2c3d4", "Ty", "cirrus-sr22-like");
        Assert.Equal("a1b2c3d4|Ty|cirrus-sr22-like", d);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(d) <= 63);

        string big = PeerEvent.Display("a1b2c3d4", new string('パ', 30), "beaver-dhc2-like");
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(big) <= 63,
                    $"{System.Text.Encoding.UTF8.GetByteCount(big)} bytes is more than MCPeerID takes");

        // And what survives the trim still parses: the id is at the front for exactly this reason.
        Assert.True(PeerEvent.TryParse($"p\u001f{big}\u001f", out _, out string id, out _, out _, out _));
        Assert.Equal("a1b2c3d4", id);
    }
}
