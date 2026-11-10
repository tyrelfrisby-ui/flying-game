using System.Security.Cryptography;
using System.Text;

namespace FlyingGame.Core;

/// <summary>HKDF with HMAC-SHA256 (RFC 5869). The companion link's keys are derived here once, in tested C#, and handed
/// to the transport (Plugins/iOS/AeroLink.swift) as raw bytes, so the two ends can never derive them differently.</summary>
public static class Hkdf
{
    public static byte[] Extract(byte[] salt, byte[] ikm)
    {
        using var h = new HMACSHA256(salt.Length == 0 ? new byte[32] : salt);
        return h.ComputeHash(ikm);
    }

    public static byte[] Expand(byte[] prk, byte[] info, int length)
    {
        if (length <= 0 || length > 255 * 32) throw new ArgumentOutOfRangeException(nameof(length));
        var okm = new byte[length];
        var t = Array.Empty<byte>();
        using var h = new HMACSHA256(prk);
        for (int i = 1, done = 0; done < length; i++)
        {
            var input = new byte[t.Length + info.Length + 1];
            Buffer.BlockCopy(t, 0, input, 0, t.Length);
            Buffer.BlockCopy(info, 0, input, t.Length, info.Length);
            input[input.Length - 1] = (byte)i;
            t = h.ComputeHash(input);
            int n = Math.Min(t.Length, length - done);
            Buffer.BlockCopy(t, 0, okm, done, n);
            done += n;
        }
        return okm;
    }

    public static byte[] DeriveKey(byte[] ikm, byte[] salt, byte[] info, int length) => Expand(Extract(salt, ikm), info, length);
}

/// <summary>What a pairing QR code carries: which Mac, what it is called, and the key the two will share.</summary>
public sealed class PairingOffer
{
    public int Version { get; }
    public string MacId { get; }
    public string MacName { get; }
    public byte[] Key { get; }

    public PairingOffer(int version, string macId, string macName, byte[] key)
    {
        Version = version; MacId = macId; MacName = macName; Key = key;
    }
}

/// <summary>
/// COMPANION PAIRING (Ty 2026-11-04: Q9 "both QR and PIN, QR recommended"; Q10 "TLS-PSK encryption in v1").
/// docs/TV-COMPANION-PLAN.md §5.2 has the reasoning; in short:
///
///   • Every connection is TLS 1.2 with a pre-shared key (Network.framework, TLS_PSK_WITH_AES_128_GCM_SHA256): the
///     handshake proves both ends hold a key the Mac accepts and everything after it is encrypted. There is no
///     plaintext phase.
///   • PAIR A DEVICE on the Mac opens one <see cref="PairingWindow"/> with both a QR code and a PIN.
///   • QR (recommended): the code is <see cref="OfferUri"/>, a random 32-byte one-time secret in an
///     <c>aeroplay://pair</c> link. The iPad's Camera opens it in the app, which connects with <see cref="QrPsk"/>.
///   • PIN: the device types the Mac's 6 digits and connects with <see cref="PinPsk"/>. A 6-digit PIN can be brute-forced
///     offline from a captured handshake, which is why it is single use, lasts <see cref="WindowSeconds"/>, allows
///     <see cref="MaxPinTries"/> failed handshakes, and why QR is the recommended way.
///   • Either way, inside that tunnel the device proves which secret it used (<see cref="CompanionAuth.PairProof"/>) and
///     the Mac issues a fresh 32-byte link key. Every later connection uses <see cref="LinkPsk"/> of it, and inside the
///     tunnel the device proves WHICH device it is (<see cref="CompanionAuth.LinkProof"/>, Glass's ControlLinkAuth shape):
///     a TLS server cannot tell which of its pre-shared keys a client used, so without the proof a pairing client could
///     claim to be a paired device.
///
/// Pure: the Swift side only runs the sockets with the keys and identities computed here.
/// </summary>
public static class CompanionPairing
{
    public const string Scheme = "aeroplay";
    public const int ProtocolVersion = 1;
    public const int KeyBytes = 32;
    public const int PinDigits = 6;
    public const double WindowSeconds = 120;
    public const int MaxPinTries = 5;
    /// <summary>The one Bonjour type for every role (declared in both apps' Info.plist since e5051bdc).</summary>
    public const string ServiceType = "_aeroplayground._tcp";

    /// <summary>A 6-digit PIN from random bytes, uniformly: each 4-byte word below 4 294 000 000 (a multiple of 10⁶) is
    /// reduced mod 10⁶, the rest are skipped. Null if every word was skipped (practically never with 16 bytes).</summary>
    public static string? PinFromBytes(byte[] random)
    {
        for (int i = 0; i + 4 <= random.Length; i += 4)
        {
            uint v = (uint)(random[i] | random[i + 1] << 8 | random[i + 2] << 16 | random[i + 3] << 24);
            if (v < 4_294_000_000u) return (v % 1_000_000u).ToString("D6");
        }
        return null;
    }

    public static string NewPin()
    {
        var b = new byte[16];
        using var rng = RandomNumberGenerator.Create();
        while (true) { rng.GetBytes(b); string? pin = PinFromBytes(b); if (pin != null) return pin; }
    }

    public static byte[] NewKey()
    {
        var k = new byte[KeyBytes];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(k);
        return k;
    }

    /// <summary>A stable random id for this install: 32 lower-case hex digits.</summary>
    public static string NewDeviceId()
    {
        var b = new byte[16];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(b);
        return Hex(b);
    }

    public static bool IsDeviceId(string? s)
    {
        if (s == null || s.Length < 16 || s.Length > 64) return false;
        foreach (char c in s) if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f')) return false;
        return true;
    }

    public static bool IsPin(string? s)
    {
        if (s == null || s.Length != PinDigits) return false;
        foreach (char c in s) if (c < '0' || c > '9') return false;
        return true;
    }

    // ---- QR ------------------------------------------------------------------------------------------------------

    /// <summary>The link the Mac's pairing QR code shows.</summary>
    public static string OfferUri(string macId, string macName, byte[] key)
        => $"{Scheme}://pair?v={ProtocolVersion}&id={macId}&name={Uri.EscapeDataString(macName)}&k={Base64Url(key)}";

    /// <summary>
    /// Read a scanned link. Refuses anything that is not exactly an <c>aeroplay://pair</c> link of this protocol
    /// version with a well-formed Mac id and a 32-byte key: a QR code is input from a camera, so it is checked like any
    /// other untrusted input.
    /// </summary>
    public static bool TryParseOffer(string? uri, out PairingOffer? offer)
    {
        offer = null;
        if (string.IsNullOrEmpty(uri)) return false;
        string prefix = Scheme + "://pair?";
        if (!uri!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var q = new Dictionary<string, string>();
        foreach (string part in uri.Substring(prefix.Length).Split('&'))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) return false;
            string k = part.Substring(0, eq);
            if (q.ContainsKey(k)) return false;   // a repeated field is not ours
            try { q[k] = Uri.UnescapeDataString(part.Substring(eq + 1)); }
            catch (UriFormatException) { return false; }
        }
        if (!q.TryGetValue("v", out string? v) || v != ProtocolVersion.ToString()) return false;
        if (!q.TryGetValue("id", out string? id) || !IsDeviceId(id)) return false;
        if (!q.TryGetValue("k", out string? k64)) return false;
        byte[]? key = FromBase64Url(k64);
        if (key == null || key.Length != KeyBytes) return false;
        q.TryGetValue("name", out string? name);
        name = string.IsNullOrWhiteSpace(name) ? "Mac" : name!.Length > 64 ? name.Substring(0, 64) : name;
        offer = new PairingOffer(ProtocolVersion, id!, name!, key);
        return true;
    }

    // ---- keys and identities ---------------------------------------------------------------------------------------

    /// <summary>The TLS pre-shared key for a QR pairing: the code's one-time secret, salted with the Mac's id.</summary>
    public static byte[] QrPsk(byte[] secret, string macId)
        => Hkdf.DeriveKey(secret, Encoding.UTF8.GetBytes("aero-pair v1|" + macId), Encoding.UTF8.GetBytes("aero-qr-psk"), 32);

    /// <summary>The TLS pre-shared key for a PIN pairing with <paramref name="macId"/>. Salting with the Mac's id means a
    /// table of PIN keys made for one Mac is useless against another.</summary>
    public static byte[] PinPsk(string pin, string macId)
        => Hkdf.DeriveKey(Encoding.UTF8.GetBytes(pin), Encoding.UTF8.GetBytes("aero-pair v1|" + macId), Encoding.UTF8.GetBytes("aero-pin-psk"), 32);

    /// <summary>The TLS pre-shared key for the link between <paramref name="macId"/> and <paramref name="deviceId"/> under
    /// their shared <paramref name="key"/>. Per pair of ids, so the key is never used raw.</summary>
    public static byte[] LinkPsk(byte[] key, string macId, string deviceId)
        => Hkdf.DeriveKey(key, Encoding.UTF8.GetBytes(macId + "|" + deviceId), Encoding.UTF8.GetBytes("aero-link-psk v1"), 32);

    /// <summary>The PSK identity a PIN pairing presents (the Mac has one window open at a time).</summary>
    public const string PinIdentity = "aero-pin/1";

    /// <summary>The PSK identity a QR pairing presents.</summary>
    public const string QrIdentity = "aero-qr/1";

    /// <summary>The PSK identity a paired device presents: the Mac picks that device's key by it.</summary>
    public static string LinkIdentity(string deviceId) => "aero-link/1/" + deviceId;

    /// <summary>The device id in a link identity, or null.</summary>
    public static string? DeviceOfIdentity(string? identity)
    {
        const string p = "aero-link/1/";
        if (identity == null || !identity.StartsWith(p, StringComparison.Ordinal)) return null;
        string id = identity.Substring(p.Length);
        return IsDeviceId(id) ? id : null;
    }

    // ---- encoding ----------------------------------------------------------------------------------------------------

    public static string Hex(byte[] b)
    {
        var sb = new StringBuilder(b.Length * 2);
        foreach (byte x in b) sb.Append(x.ToString("x2"));
        return sb.ToString();
    }

    public static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[]? FromBase64Url(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        string t = s!.Replace('-', '+').Replace('_', '/');
        switch (t.Length % 4) { case 2: t += "=="; break; case 3: t += "="; break; case 1: return null; }
        try { return Convert.FromBase64String(t); }
        catch (FormatException) { return null; }
    }
}

/// <summary>The Mac's pairing window (PAIR A DEVICE): one PIN and one QR secret, <see cref="CompanionPairing.WindowSeconds"/>
/// long, at most <see cref="CompanionPairing.MaxPinTries"/> failed handshakes, and closed for good by the first success.</summary>
public sealed class PairingWindow
{
    public string Pin { get; }
    public byte[] QrSecret { get; }
    private readonly double _opened;
    private int _failures;
    private bool _used;

    public PairingWindow(string pin, byte[] qrSecret, double now) { Pin = pin; QrSecret = qrSecret; _opened = now; }

    public bool IsOpen(double now) => !_used && _failures < CompanionPairing.MaxPinTries && now >= _opened && now - _opened <= CompanionPairing.WindowSeconds;
    public double SecondsLeft(double now) => IsOpen(now) ? CompanionPairing.WindowSeconds - (now - _opened) : 0;
    public int TriesLeft => Math.Max(0, CompanionPairing.MaxPinTries - _failures);

    /// <summary>A pairing handshake failed (a wrong PIN, or someone guessing).</summary>
    public void Failed() => _failures++;

    /// <summary>A device paired: the window cannot be used again.</summary>
    public void Used() => _used = true;
}

/// <summary>The proofs exchanged inside the TLS tunnel (hex HMAC-SHA256), and a compare that takes the same time
/// whatever it is given.</summary>
public static class CompanionAuth
{
    public static string NewNonce()
    {
        var b = new byte[16];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(b);
        return CompanionPairing.Hex(b);
    }

    /// <summary>The device's answer to the Mac's challenge on a pairing connection, keyed by the pairing PSK it used.</summary>
    public static string PairProof(byte[] pairPsk, string nonce, string macId, string deviceId) => Mac(pairPsk, "aero-pair-C", nonce, macId, deviceId);

    /// <summary>The device's answer on a link connection, keyed by its link PSK: names the device.</summary>
    public static string LinkProof(byte[] linkPsk, string nonce, string macId, string deviceId) => Mac(linkPsk, "aero-link-C", nonce, macId, deviceId);

    /// <summary>The Mac's answer back, so the device knows it reached the Mac it paired with and not a replay.</summary>
    public static string ServerProof(byte[] psk, string nonce, string macId, string deviceId) => Mac(psk, "aero-link-S", nonce, macId, deviceId);

    private static string Mac(byte[] key, string label, string nonce, string macId, string deviceId)
    {
        using var h = new HMACSHA256(key);
        return CompanionPairing.Hex(h.ComputeHash(Encoding.UTF8.GetBytes(label + "|" + nonce + "|" + macId + "|" + deviceId)));
    }

    /// <summary>Equal strings, compared in a time that depends only on their lengths.</summary>
    public static bool Same(string? a, string? b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        int d = 0;
        for (int i = 0; i < a.Length; i++) d |= a[i] ^ b[i];
        return d == 0;
    }
}
