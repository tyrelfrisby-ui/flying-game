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
}
