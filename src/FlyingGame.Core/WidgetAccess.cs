using System.Security.Cryptography;
using System.Text;

namespace FlyingGame.Core;

/// <summary>
/// WHO MAY DRIVE THE AERO WIDGET, AND WHAT A REQUEST MAY ASK FOR (pre-ship audit, 2026-11-11).
///
/// The widget's control surface (TCP 47830 / HTTP 47831, docs/WIDGET.md) was written for one machine on a podcast
/// desk and had no authentication of any kind: it bound the wildcard address on both ports, answered every command
/// from anyone who could open the socket, and sent <c>Access-Control-Allow-Origin: *</c> so that ANY web page the
/// presenter happened to have open could drive the aeroplane from JavaScript. The worst of it was the
/// <c>snapshot</c> command, which took a <c>path</c> straight out of the request and handed it to
/// <c>File.WriteAllBytes</c>: an arbitrary file write anywhere the process could reach, from any origin.
///
/// The rules are here, in Core, rather than in the Unity component for one reason: they are the part that must be
/// RIGHT, and Core is the part that has tests. <c>tools/FlightTests/WidgetAccessTests.cs</c> exercises every branch
/// below; <c>WidgetServer</c> only wires sockets to them.
///
/// THE SHAPE (Ty, 2026-11-11), and why each piece is the way it is:
///
///   1. A TOKEN, generated on first run into the widget's OWN config file (Application.persistentDataPath — the
///      widget has its own bundle id, so its own directory). Never the repository, which is public, and never a
///      tracked path.
///   2. The widget SHOWS the token, so the person at the Mac can read it off and type it into a LAN client. A
///      secret nobody can see is a secret nobody can use.
///   3. A LAN client presents it as a <c>token</c> field in <c>hello</c>.
///   4. LOOPBACK SKIPS THE CHECK ENTIRELY. Everything on the Mac itself — the MCP layer, a render script, Safari on
///      127.0.0.1 — keeps working with no change at all. The token exists to keep the LAN out, not the desk.
///   5. A protocol 8 client that sends no token still works on loopback, and on the LAN gets a REFUSAL THAT SAYS
///      WHY, never a silent drop. Glass Overlay connects over the LAN; a silent break there is invisible, and an
///      invisible break is the expensive kind.
///   6. Loopback-only binding is the DEFAULT. LAN binding is opt-in (AERO_WIDGET_LAN=1) and then needs the token.
///      Binding is the part that matters for a laptop on a conference Wi-Fi: a port that is not listening cannot be
///      scanned, and nothing on the network sees the widget at all unless it was asked for.
/// </summary>
public static class WidgetAccess
{
    /// <summary>128 bits, as 32 lower-case hex digits. Long enough that guessing is not a strategy, short enough
    /// that a person can read it off a screen and type it into a phone — which is the whole point of showing it.</summary>
    public const int TokenBytes = 16;

    /// <summary>The widget's own config file, inside its persistent data directory. Plain text, one line.</summary>
    public const string TokenFileName = "widget-token.txt";

    /// <summary>The environment switch that opens the ports to the local network. Off = loopback only.</summary>
    public const string LanEnvVar = "AERO_WIDGET_LAN";

    /// <summary>Where <c>snapshot</c> may write, relative to the widget's persistent data directory. A caller
    /// cannot leave it, name it, or reach past it.</summary>
    public const string SnapshotDirName = "snapshots";

    /// <summary>The snapshot written when the caller names no file.</summary>
    public const string SnapshotDefaultName = "aero-widget.png";

    // ---- the token ---------------------------------------------------------------------------------------------

    public static string NewToken()
    {
        var b = new byte[TokenBytes];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(b);
        var sb = new StringBuilder(TokenBytes * 2);
        foreach (byte x in b) sb.Append(x.ToString("x2"));
        return sb.ToString();
    }

    /// <summary>A well-formed token: exactly <see cref="TokenBytes"/> bytes as lower-case hex. Checked before a
    /// token is ever compared or written, so a truncated or hand-edited config file is noticed as a file problem
    /// rather than quietly becoming a password of three characters.</summary>
    public static bool IsToken(string? s)
    {
        if (s == null || s.Length != TokenBytes * 2) return false;
        foreach (char c in s) if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f')) return false;
        return true;
    }

    /// <summary>Equal tokens, compared in a time that depends only on their lengths. The widget answers on a LAN
    /// socket, and an early-exit compare on a secret is a timing oracle however small.</summary>
    public static bool SameToken(string? a, string? b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        int d = 0;
        for (int i = 0; i < a.Length; i++) d |= a[i] ^ b[i];
        return d == 0;
    }

    /// <summary>
    /// The token for this install, from <paramref name="dir"/>/<see cref="TokenFileName"/>, generated and written on
    /// first run. <paramref name="dir"/> is the widget's own persistent data directory — NEVER a path in the
    /// repository, which is public.
    ///
    /// A file that cannot be read or written leaves the widget with a token it holds only in memory for this run:
    /// the ports still bind, loopback still works, and a LAN client is told to read the token off the screen. That
    /// is better than either refusing to start or falling open.
    /// </summary>
    public static string ReadOrCreateToken(string dir)
    {
        string path = Path.Combine(dir, TokenFileName);
        try
        {
            if (File.Exists(path))
            {
                string s = File.ReadAllText(path).Trim();
                if (IsToken(s)) return s;
            }
        }
        catch { }
        string token = NewToken();
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, token + "\n");
        }
        catch { }
        return token;
    }

    // ---- loopback ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Is this remote address the machine the widget is running on? Written against the TEXT of an address rather
    /// than against <c>IPAddress.IsLoopback</c> because of the dual-stack socket: an IPv4 client arriving on an
    /// IPv6 listener shows up as <c>::ffff:127.0.0.1</c>, and the whole gate turns on getting that one case right.
    /// The IPv4 loopback is the entire 127.0.0.0/8 block, not just 127.0.0.1 — curl and the test harnesses use
    /// 127.0.0.1, but a second widget instance on 127.0.0.2 is still this machine.
    /// </summary>
    public static bool IsLoopback(string? address)
    {
        if (string.IsNullOrEmpty(address)) return false;
        string a = address!.Trim();
        int pct = a.IndexOf('%');                  // an IPv6 zone id ("fe80::1%en0")
        if (pct > 0) a = a.Substring(0, pct);
        if (a.Length > 1 && a[0] == '[' && a[a.Length - 1] == ']') a = a.Substring(1, a.Length - 2);
        if (a == "::1" || a == "0:0:0:0:0:0:0:1") return true;
        // IPv4-mapped and IPv4-compatible forms of a v4 address on a dual-stack socket.
        int colon = a.LastIndexOf(':');
        if (colon >= 0 && a.IndexOf('.') > colon) a = a.Substring(colon + 1);
        else if (colon >= 0) return false;
        string[] parts = a.Split('.');
        if (parts.Length != 4) return false;
        if (!int.TryParse(parts[0], out int first)) return false;
        for (int i = 1; i < 4; i++) if (!int.TryParse(parts[i], out int o) || o < 0 || o > 255) return false;
        return first == 127;
    }

    // ---- the gate ----------------------------------------------------------------------------------------------

    /// <summary>What a LAN client is told when it sends no token at all. Names the field, the command and where to
    /// read the value, because the client that hits this is Glass Overlay or a script, and whoever is reading the
    /// message is the person who can fix it.</summary>
    public const string RefusalNoToken =
        "this widget is answering on the local network, so a command from another machine needs its access token: "
        + "send {\"cmd\":\"hello\",\"token\":\"…\"} first. The token is printed on the widget's own window. "
        + "Connections from this machine (127.0.0.1 / ::1) need no token.";

    /// <summary>What a LAN client is told when the token it sent is not the one the widget holds.</summary>
    public const string RefusalBadToken =
        "that access token is not this widget's. Read the current one off the widget's own window and send it as "
        + "the \"token\" field of {\"cmd\":\"hello\"}.";

    /// <summary>What a LAN client is told when it sends anything other than <c>hello</c> before it has presented a
    /// token. Not a dropped connection: it stays open and says what to send.</summary>
    public const string RefusalNotYet =
        "not authenticated: send {\"cmd\":\"hello\",\"token\":\"…\"} on this connection first. The token is printed "
        + "on the widget's own window.";

    /// <summary>
    /// May this <c>hello</c> drive the widget? Returns null when it may, otherwise the sentence to send back as
    /// <c>error</c>.
    ///
    /// Loopback is decided before the token is even looked at (rule 4): a client on this machine can already read
    /// the token file, so asking it for the token protects nothing and would break every existing local caller.
    /// </summary>
    public static string? HelloRefusal(bool loopback, string? presented, string? expected)
    {
        if (loopback) return null;
        if (string.IsNullOrEmpty(presented)) return RefusalNoToken;
        if (!IsToken(expected)) return RefusalBadToken;   // no usable token on this side: nothing can match it
        return SameToken(presented, expected) ? null : RefusalBadToken;
    }

    /// <summary>
    /// May this command run? <paramref name="authenticated"/> is what a previous <c>hello</c> on the same connection
    /// established (always true on loopback). <c>hello</c> itself is always allowed through — it is how a client
    /// authenticates, and it is also how a refused client discovers it was refused and why.
    /// </summary>
    public static string? CommandRefusal(bool loopback, bool authenticated, string? cmd)
    {
        if (loopback || authenticated) return null;
        return cmd == "hello" ? null : RefusalNotYet;
    }

    /// <summary>What a LAN client is told over HTTP, where there is no connection to remember a <c>hello</c> on.
    /// Different words from <see cref="RefusalNoToken"/> on purpose: telling a browser to "send hello first" is
    /// advice it cannot take.</summary>
    public const string RefusalHttpNoToken =
        "this widget is answering on the local network, so a request from another device needs its access token: "
        + "add \"token\":\"…\" to the POST body, or ?token=… to /state. The token is printed on the widget's own "
        + "window, and the remote page will ask you for it. Requests from this machine need none.";

    /// <summary>
    /// May this single HTTP request run? HTTP has no connection to authenticate ONCE on, so every request from
    /// the LAN carries the token itself and is judged on its own.
    /// </summary>
    public static string? RequestRefusal(bool loopback, string? presented, string? expected)
    {
        if (loopback) return null;
        if (string.IsNullOrEmpty(presented)) return RefusalHttpNoToken;
        if (!IsToken(expected)) return RefusalBadToken;
        return SameToken(presented, expected) ? null : RefusalBadToken;
    }

    // ---- the snapshot path -------------------------------------------------------------------------------------

    /// <summary>
    /// Where a <c>snapshot</c> command may actually write. The caller's <c>path</c> is treated as a NAME, never as
    /// a path: every directory component, absolute root, <c>..</c> and drive letter is discarded before the result
    /// is joined to <paramref name="dir"/>, so there is no input that leaves the directory. The alternative —
    /// accepting a path and then checking it is inside — has been got wrong in this exact shape often enough
    /// (symlinks, <c>..</c> after normalisation, UNC paths) that it is not worth the cleverness: the widget's
    /// snapshot was never meant to choose a directory in the first place.
    ///
    /// The extension is forced to .png because that is what the command writes, and because a snapshots directory
    /// that can be made to hold a file called <c>something.command</c> is a worse thing than a mis-named image.
    /// </summary>
    public static string SnapshotPath(string dir, string? requested)
        => Path.Combine(dir, SnapshotName(requested));

    /// <summary>The file name <see cref="SnapshotPath"/> will use. Separate so the tests can read it without
    /// knowing the directory.</summary>
    public static string SnapshotName(string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested)) return SnapshotDefaultName;
        string s = requested!.Replace('\\', '/');
        int slash = s.LastIndexOf('/');
        if (slash >= 0) s = s.Substring(slash + 1);
        int colon = s.LastIndexOf(':');          // "C:name" and macOS resource-fork style names
        if (colon >= 0) s = s.Substring(colon + 1);
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
            sb.Append(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '.' || c == '-' || c == '_'
                      ? c : '_');
        string name = sb.ToString().Trim('.', '_', '-', ' ');
        if (name.Length == 0) return SnapshotDefaultName;
        if (name.Length > 100) name = name.Substring(0, 100);
        if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) name += ".png";
        return name;
    }

    /// <summary>Is <paramref name="path"/> inside <paramref name="dir"/>? Only used to ASSERT the rule above in the
    /// tests — the widget never relies on a check-after-the-fact, it builds the path it is willing to write.</summary>
    public static bool IsInside(string dir, string path)
    {
        string d = Path.GetFullPath(dir);
        if (!d.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)) d += Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(d, StringComparison.Ordinal);
    }

    // ---- numbers off the wire ----------------------------------------------------------------------------------

    /// <summary>
    /// NaN AND THE INFINITIES ARE NOT NUMBERS A CONTROL INPUT CAN HAVE, and both arrive easily: Json.NET parses the
    /// bare literals <c>NaN</c>, <c>Infinity</c> and <c>-Infinity</c> by default, <c>1e400</c> reads back as
    /// +∞, and JoyLink's axes are four raw bytes off a UDP packet reinterpreted as a float32.
    ///
    /// They matter because CLAMPING DOES NOT STOP THEM. Both <c>UnityEngine.Mathf.Clamp</c> and
    /// <c>System.Math.Clamp</c> are written as "if (v &lt; min) … else if (v &gt; max) … else return v", and NaN
    /// compares false against everything, so NaN goes in and NaN comes out of what reads like a guard. One NaN
    /// elevator deflection is a NaN moment, a NaN attitude quaternion and an aeroplane that is gone for the rest of
    /// the session — from one JSON field, from any origin.
    /// </summary>
    public static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

    public static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

    /// <summary>A network number, or null if it is not one. Null means "the field was not sent", which every caller
    /// of these optional fields already handles — so a non-finite value is IGNORED rather than substituted, and the
    /// control keeps the position the pilot last gave it.</summary>
    public static double? Finite(double? v) => v.HasValue && Finite(v.Value) ? v : null;

    /// <summary>A raw axis, or <paramref name="fallback"/> if it is not a number. An axis must have SOME value
    /// every frame, so here the non-finite case takes the centre rather than being ignored — which is what a radio
    /// does when it loses the transmitter.</summary>
    public static float FiniteOr(float v, float fallback) => Finite(v) ? v : fallback;

    // ---- request sizes -----------------------------------------------------------------------------------------

    /// <summary>The longest command line the TCP port will read. The reader used to be a bare
    /// <c>StreamReader.ReadLine()</c>, which has no limit at all: a peer that opens the socket and sends bytes
    /// without ever sending a newline grows a string until the process dies, and costs nothing to do. 64 KiB is
    /// about 60 times the largest command the protocol has (a <c>vectorStyle</c> with every field set measures
    /// ~1.1 KB), so no real client can reach it.</summary>
    public const int MaxLineBytes = 64 * 1024;

    /// <summary>The largest HTTP body the widget will read. The server used to allocate <c>new byte[len]</c>
    /// straight from the request's Content-Length — a 2 GB allocation for a 40-byte request. 256 KiB is far above
    /// anything the remote page posts.</summary>
    public const int MaxBodyBytes = 256 * 1024;

    /// <summary>The largest block of HTTP request line + headers the widget will read before giving up.</summary>
    public const int MaxHeadBytes = 16 * 1024;
}
