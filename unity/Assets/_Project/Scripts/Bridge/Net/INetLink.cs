namespace FlyingGame.Bridge.Net
{
    /// <summary>
    /// Where the other aeroplanes come from. <see cref="NetSession"/> speaks one protocol and does not care
    /// whether the packets crossed a datacentre or a living room: <see cref="RelayClient"/> carries them over
    /// a WebSocket to the relay, and <see cref="LanLink"/> carries them straight to the other aeroplanes over
    /// UDP with nothing in between — no server, no internet, nothing billed.
    ///
    /// The implementations are deliberately given the same shape as the relay's messages rather than a tidier
    /// abstraction, because the relay's wire format is the one the game already knows how to fly on. A LAN
    /// link makes its own "hello", "peer" and "bye" out of what it sees on the network.
    /// </summary>
    public interface INetLink : System.IDisposable
    {
        bool IsOpen { get; }
        bool IsClosed { get; }
        string Error { get; }

        /// <summary>Next message in, or false. Drained on the main thread.</summary>
        bool TryDequeue(out NetMsg m);

        /// <summary>This aeroplane's state. Latest wins: an old one is never worth waiting behind.</summary>
        void Send(string json);

        /// <summary>Something that must not be dropped (a radio call).</summary>
        void SendQueued(string json);

        /// <summary>Once a frame on the main thread, before the inbox is drained. The relay needs nothing here.</summary>
        void Service();

        void Close();
    }

    /// <summary>
    /// A link with no server in it: the aeroplanes are talking to each other directly, either over the wifi
    /// everybody is already on (<see cref="LanLink"/>) or over a link the devices make themselves with no
    /// router at all (<see cref="PeerLink"/>). The session needs a few things from these that mean nothing to
    /// the relay, which keeps its own copy of a pilot's name and does its own fan-out.
    /// </summary>
    public interface ILocalLink : INetLink
    {
        /// <summary>The pilot changed their name or their aeroplane: it travels with us, so tell the link.</summary>
        void SetSelf(string name, string aircraft);

        /// <summary>Who is out of sight and can have state at a slower rate (a direct link has no relay to fan out).</summary>
        void SetFarPeers(System.Collections.Generic.ICollection<string> ids);

        int PeerCount { get; }

        /// <summary>
        /// Nobody has ever answered, for long enough that the local-network permission is the likeliest
        /// reason — the one failure a pilot cannot see, because a refused permission fails silently for ever.
        /// </summary>
        bool SuspectPermission { get; }

        /// <summary>Where we are listening, for the HUD: "port 47801", "device to device".</summary>
        string Where { get; }
    }
}
