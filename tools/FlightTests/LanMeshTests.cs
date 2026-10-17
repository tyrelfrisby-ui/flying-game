using System.Diagnostics;
using FlyingGame.Core.Lan;
using Xunit;
using Xunit.Abstractions;

namespace FlightTests;

/// <summary>
/// Flying together with no internet (owner 2026-10-17: "i want to be sure the ios devices and mac app can all
/// join in the same local network with no external internet"). These run REAL UDP sockets on loopback — the
/// thing shipped, not a mock of it — so what they prove is that two copies of the game with no server between
/// them find each other, agree who is in the room, and pass packets through unchanged.
///
/// Every mesh here is <see cref="LanMesh.LoopbackOnly"/>: a test that swept the subnet would put a port scan
/// across the owner's house on every run. Each test gets its own port block so a game running on the machine
/// is neither found nor disturbed, and so the tests do not find each other.
/// </summary>
public class LanMeshTests
{
    private readonly ITestOutputHelper _o;
    public LanMeshTests(ITestOutputHelper o) { _o = o; }

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static double Now => Clock.Elapsed.TotalSeconds;

    /// <summary>A mesh on its own port block, timings wound down so a test is a second rather than ten.</summary>
    private static LanMesh Mesh(string room, string id, string name, int basePort, string ac = "cub-like")
    {
        var m = new LanMesh(room, id, name, ac, basePort)
        {
            LoopbackOnly = true,
            TimeoutS = 0.8,
            AnnounceS = 0.1,
            SweepS = 0.2,
        };
        Assert.True(m.Start(), m.Error);
        return m;
    }

    /// <summary>Run the meshes for <paramref name="seconds"/>, collecting everything each one sees.</summary>
    private static Dictionary<LanMesh, List<LanEvent>> Pump(double seconds, params LanMesh[] meshes)
    {
        var seen = new Dictionary<LanMesh, List<LanEvent>>();
        foreach (LanMesh m in meshes) seen[m] = new List<LanEvent>();
        double until = Now + seconds;
        while (Now < until)
        {
            foreach (LanMesh m in meshes)
            {
                m.Tick();
                for (int k = 0; k < 512 && m.TryDequeue(out LanEvent e); k++) seen[m].Add(e);
            }
            System.Threading.Thread.Sleep(5);
        }
        return seen;
    }

    private static List<LanEvent> Of(Dictionary<LanMesh, List<LanEvent>> all, LanMesh m, LanEventKind k)
        => all[m].Where(e => e.Kind == k).ToList();

    /// <summary>
    /// The whole point: two copies of the game on one network, no server anywhere, each finds the other by
    /// probing and then carries the other's state.
    /// </summary>
    [Fact]
    public void TwoPilotsFindEachOtherWithNoServerAtAll()
    {
        using var a = Mesh("LAN", "aaa", "Ty", 48310);
        using var b = Mesh("LAN", "bbb", "Sam", 48310);

        var found = Pump(1.5, a, b);
        Assert.Contains(Of(found, a, LanEventKind.Peer), e => e.PeerId == "bbb" && e.Name == "Sam");
        Assert.Contains(Of(found, b, LanEventKind.Peer), e => e.PeerId == "aaa" && e.Name == "Ty");
        Assert.Equal(1, a.PeerCount);
        Assert.Equal(1, b.PeerCount);
        _o.WriteLine($"a on {a.Port} found {b.Peers()[0].Name}; {a.ProbesSent} probes sent");

        // And the state goes through, exactly as handed over.
        const string state = "{\"t\":\"s\",\"p\":[1.5,2,3]}";
        a.SendState(state);
        var got = Pump(0.4, a, b);
        Assert.Contains(Of(got, b, LanEventKind.State), e => e.PeerId == "aaa" && e.Payload == state);
    }

    /// <summary>
    /// The payload is the game's business, not the mesh's: the header is separated by characters the payload
    /// is allowed to contain, so anything that goes in must come out byte for byte — including the pipe that
    /// separates the header's own fields, a newline, and a pilot who types their name in Japanese.
    /// </summary>
    [Fact]
    public void APayloadComesThroughUntouchedWhateverIsInIt()
    {
        using var a = Mesh("LAN", "aaa", "パイロット|Ty", 48320);
        using var b = Mesh("LAN", "bbb", "Sam", 48320);
        Pump(1.0, a, b);
        Assert.Equal(1, b.PeerCount);

        const string nasty = "{\"t\":\"v\",\"a\":\"AB|CD\\nEF\",\"n\":\"a|b|c\"}\nsecond line";
        a.SendState(nasty);
        var got = Pump(0.4, a, b);
        LanEvent s = Of(got, b, LanEventKind.State).Single();
        Assert.Equal(nasty, s.Payload);
        Assert.Equal("パイロット|Ty", b.Peers()[0].Name);     // and the name survives the separator too
    }

    /// <summary>
    /// What makes a dorm-sized room possible: a newcomer who finds ONE pilot is handed everybody that pilot
    /// knows. C sweeps its own port block only (a different one), so the single hint at A is its only way in —
    /// and B must arrive by gossip or not at all.
    /// </summary>
    [Fact]
    public void OnePilotFoundIsTheWholeRoomFound()
    {
        using var a = Mesh("LAN", "aaa", "Ty", 48330);
        using var b = Mesh("LAN", "bbb", "Sam", 48330);
        Pump(1.0, a, b);
        Assert.Equal(1, a.PeerCount);

        using var c = Mesh("LAN", "ccc", "Jen", 48340);          // another block: it cannot sweep its way to A
        Pump(0.4, a, b, c);
        Assert.Equal(0, c.PeerCount);
        Assert.True(c.AddHint($"127.0.0.1:{a.Port}"), "the hint was not accepted");

        var found = Pump(1.2, a, b, c);
        Assert.Contains(Of(found, c, LanEventKind.Peer), e => e.PeerId == "aaa");
        Assert.Contains(Of(found, c, LanEventKind.Peer), e => e.PeerId == "bbb");
        Assert.Equal(2, c.PeerCount);
        Assert.Equal(2, b.PeerCount);                             // and B learned C without being told either
        _o.WriteLine($"c found {string.Join(", ", c.Peers().Select(p => p.Name))} from one address");
    }

    /// <summary>Two games in one building do not fly into each other.</summary>
    [Fact]
    public void ARoomOnlyHearsItself()
    {
        using var a = Mesh("LAN", "aaa", "Ty", 48350);
        using var b = Mesh("K7Q2ZP", "bbb", "Sam", 48350);
        var found = Pump(1.2, a, b);
        Assert.Empty(Of(found, a, LanEventKind.Peer));
        Assert.Empty(Of(found, b, LanEventKind.Peer));
        Assert.Equal(0, a.PeerCount);
    }

    /// <summary>
    /// A pilot who walks out of the building has to be NOTICED missing — nothing arrives to say so. A pilot
    /// who goes back to the menu says goodbye, and must go at once rather than after the timeout.
    ///
    /// Note what silence is not: a mesh that has merely stopped announcing still answers probes from its
    /// listening thread, so the moment the room empties and the sweep starts again it is found and comes
    /// straight back — which is right, a pilot who is still listening is still there. Vanishing is the socket
    /// going away, which is what <see cref="LanMesh.Abandon"/> is.
    /// </summary>
    [Fact]
    public void APilotWhoVanishesIsNoticedAndOneWhoLeavesGoesAtOnce()
    {
        using var a = Mesh("LAN", "aaa", "Ty", 48360);
        var b = Mesh("LAN", "bbb", "Sam", 48360);
        Pump(1.0, a, b);
        Assert.Equal(1, a.PeerCount);

        b.Abandon();                           // the phone is gone: no bye, no replies, nothing
        var lost = Pump(1.4, a);
        Assert.Contains(Of(lost, a, LanEventKind.Bye), e => e.PeerId == "bbb");
        Assert.Equal(0, a.PeerCount);

        // A fresh session from the same pilot, and this time it leaves properly.
        var b2 = Mesh("LAN", "bbb", "Sam", 48360);
        Pump(1.0, a, b2);
        Assert.Equal(1, a.PeerCount);
        b2.Dispose();
        var gone = Pump(0.3, a);
        Assert.Contains(Of(gone, a, LanEventKind.Bye), e => e.PeerId == "bbb");
        Assert.Equal(0, a.PeerCount);
        b.Dispose();
    }

    /// <summary>
    /// The dorm problem: a mesh is N² packets a tick. An aeroplane too far away to see does not need ten
    /// states a second, so it gets one in five — while staying in the roster, which is what keeps the pilot
    /// list honest and lets them be picked up again when they come close.
    /// </summary>
    [Fact]
    public void APilotTooFarAwayToSeeGetsASlowerRate()
    {
        using var a = Mesh("LAN", "aaa", "Ty", 48370);
        using var b = Mesh("LAN", "bbb", "Sam", 48370);
        Pump(1.0, a, b);
        Assert.Equal(1, b.PeerCount);

        a.SetFarPeers(new[] { "bbb" });
        for (int i = 0; i < 20; i++) { a.SendState("{\"i\":" + i + "}"); System.Threading.Thread.Sleep(2); }
        var got = Pump(0.4, a, b);
        int n = Of(got, b, LanEventKind.State).Count;
        _o.WriteLine($"20 ticks at a fifth of the rate delivered {n} states");
        Assert.InRange(n, 3, 5);

        // Back in sight: every tick again.
        a.SetFarPeers(null);
        for (int i = 0; i < 10; i++) { a.SendState("{}"); System.Threading.Thread.Sleep(2); }
        var all = Pump(0.4, a, b);
        Assert.Equal(10, Of(all, b, LanEventKind.State).Count);
    }

    /// <summary>
    /// A mesh alone sweeps for company; a mesh in a room stops, because gossip has taken over. The sweep is
    /// also bounded — it is a subnet, never the internet.
    /// </summary>
    [Fact]
    public void ItSweepsWhileItIsAloneAndStopsWhenItIsNot()
    {
        using var a = Mesh("LAN", "aaa", "Ty", 48380);
        Pump(0.8, a);
        int alone = a.ProbesSent;
        Assert.True(alone > 0, "a lone pilot never looked for anybody");
        Assert.True(alone <= LanMesh.MaxSweepHosts + LanMesh.PortsPerHost, $"{alone} probes is not a subnet");

        // And asking and asking is rude: a sweep that finds nothing waits longer before the next one.
        int second = a.ProbesSent;
        Pump(0.8, a);
        int inSecondWindow = a.ProbesSent - second;
        _o.WriteLine($"probes: {alone} in the first 0.8 s, {inSecondWindow} in the second");
        Assert.True(inSecondWindow < alone, $"the sweep never backed off ({alone} then {inSecondWindow})");

        using var b = Mesh("LAN", "bbb", "Sam", 48380);
        Pump(1.0, a, b);
        Assert.Equal(1, a.PeerCount);
        int withCompany = a.ProbesSent;
        Pump(0.8, a, b);
        _o.WriteLine($"and {a.ProbesSent - withCompany} more once the room had somebody in it");
        Assert.Equal(withCompany, a.ProbesSent);
    }

    /// <summary>A name or an aeroplane changed on the menu reaches the others without a rejoin.</summary>
    [Fact]
    public void ChangingAeroplaneTellsTheRoom()
    {
        using var a = Mesh("LAN", "aaa", "Ty", 48390, "cub-like");
        using var b = Mesh("LAN", "bbb", "Sam", 48390);
        Pump(1.0, a, b);
        Assert.Equal("cub-like", b.Peers()[0].Ac);

        a.SetSelf("Ty", "cirrus-sr22-like");
        var got = Pump(0.6, a, b);
        Assert.Contains(Of(got, b, LanEventKind.Peer), e => e.PeerId == "aaa" && e.Aircraft == "cirrus-sr22-like");
        Assert.Equal("cirrus-sr22-like", b.Peers()[0].Ac);
    }
}
