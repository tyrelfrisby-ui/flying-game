namespace FlyingGame.Core.Lan;

/// <summary>What a device-to-device link reported: the same three kinds the wifi mesh reports.</summary>
public enum PeerEventKind { None, Peer, Bye, State, Error }

/// <summary>
/// One event from Apple's peer-to-peer wifi, taken apart (Plugins/iOS/AeroPeer.mm hands them over as one
/// string each). This lives in Core rather than in the bridge for one reason: it is the only part of that
/// link whose logic can be WRONG in a way a compiler cannot see — a mis-split display name means every pilot
/// in a field group is called something else — and nothing in the bridge can be tested headlessly.
///
/// The wire shape, decided by what MultipeerConnectivity will carry for us:
///   kind \x1F displayName \x1F payload        with displayName = "id|name|aircraft"
/// 0x1F separates the fields because a display name has '|' inside it.
/// </summary>
public static class PeerEvent
{
    public const char FieldSep = '\u001f';
    public const char NameSep = '|';

    /// <summary>
    /// Pull one event apart. False only when there is nothing usable in it at all; a display name that is not
    /// of our making still yields a pilot, because somebody is there whatever they chose to call themselves.
    /// </summary>
    public static bool TryParse(string? line, out PeerEventKind kind, out string id, out string name,
                                out string? aircraft, out string? payload)
    {
        kind = PeerEventKind.None;
        id = name = "";
        aircraft = payload = null;
        if (string.IsNullOrEmpty(line)) return false;

        string[] f = line!.Split(FieldSep);
        if (f.Length < 2) return false;
        kind = f[0] switch
        {
            "p" => PeerEventKind.Peer,
            "b" => PeerEventKind.Bye,
            "s" => PeerEventKind.State,
            "e" => PeerEventKind.Error,
            _ => PeerEventKind.None,
        };
        if (kind == PeerEventKind.None) return false;

        string display = f[1];
        // A payload may itself contain the field separator — only the first two fields are ours.
        payload = f.Length > 2 ? string.Join(FieldSep.ToString(), f, 2, f.Length - 2) : null;
        if (kind == PeerEventKind.Error) return true;

        string[] who = display.Split(NameSep);
        id = who.Length > 0 && who[0].Length > 0 ? who[0] : display;
        name = who.Length > 1 && who[1].Length > 0 ? who[1] : display;
        // A name with a '|' typed in it is given back whole rather than cut in half; the aeroplane is the LAST
        // field, because that is the one we put there.
        if (who.Length > 3) name = string.Join(NameSep.ToString(), who, 1, who.Length - 2);
        aircraft = who.Length > 2 ? who[who.Length - 1] : null;
        if (aircraft != null && aircraft.Length == 0) aircraft = null;
        return id.Length > 0;
    }

    /// <summary>Our own line in everybody else's roster. The framework allows 63 bytes, so it is trimmed to fit.</summary>
    public static string Display(string selfId, string name, string aircraft, int maxBytes = 63)
    {
        string s = $"{selfId}{NameSep}{name ?? ""}{NameSep}{aircraft ?? ""}";
        while (System.Text.Encoding.UTF8.GetByteCount(s) > maxBytes && s.Length > 1) s = s.Substring(0, s.Length - 1);
        return s;
    }
}
