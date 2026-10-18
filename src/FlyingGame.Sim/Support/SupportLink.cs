using System.Text;

namespace FlyingGame.Sim.Support;

/// <summary>Which build of the app is asking. Only ever sent as the three words below.</summary>
public enum SupportPlatform { Ios, IpadOs, MacOs }

/// <summary>
/// The HELP link and the diagnostics block, with no Unity in them so both can be tested (handoff
/// 2026-10-17, Glass ↔ Aero; docs/support/).
///
/// Two rules decide everything in this file, and both come from the App Store privacy answer the project
/// has to keep telling the truth about — **Data Not Collected**:
///
///  1. **The app sends nothing to the support system.** Opening HELP hands the operating system a URL and
///     that is the whole of it. The URL carries the build and the platform, because a question about a
///     version we shipped three weeks ago is a different question, and nothing else. No identifier, no
///     device, no pilot, not even a ticket reference — the conversation does not exist until the person
///     starts it themselves on the web.
///  2. **The diagnostics block is the customer's to send or not.** <see cref="Diagnostics"/> only ever
///     returns text. Nothing here transmits it; the button that calls this puts it on the clipboard and
///     says so, and if they never paste it, it dies with the app.
///
/// So the scrubbing in <see cref="Scrub"/> is not decoration. The lines that go in the block are real log
/// lines from the running game, and the running game knows the pilot's chosen name and the StoreKit product
/// identifiers. A log line is written to be read by us, not by a stranger, so anything that could identify
/// the person or what they have bought comes out before the text is handed over.
/// </summary>
public static class SupportLink
{
    /// <summary>Where Glass's support chat lives. The page, and the AI that answers on it, are not ours.</summary>
    public const string BaseUrl = "https://aeroplayground.app/support";

    /// <summary>The product identifier prefix: everything under it is a purchase and never leaves the device.</summary>
    public const string ProductPrefix = "com.tyfrisby.aeroplayground";

    /// <summary>How many log lines the block carries. Enough for a crash and what led to it.</summary>
    public const int MaxNotes = 12;

    public static string Tag(SupportPlatform p) => p switch
    {
        SupportPlatform.MacOs => "macos",
        SupportPlatform.IpadOs => "ipados",
        _ => "ios",
    };

    /// <summary>
    /// The URL the HELP button opens. Two parameters, both of them about the BUILD and neither about the
    /// person: <c>?v=&lt;version&gt;&amp;p=&lt;platform&gt;</c>.
    /// </summary>
    public static string Url(string? version, SupportPlatform platform)
    {
        string v = Token(version);
        return v.Length == 0 ? $"{BaseUrl}?p={Tag(platform)}" : $"{BaseUrl}?v={v}&p={Tag(platform)}";
    }

    /// <summary>
    /// A version as it is safe to put in a query string: the characters a version number is made of and
    /// nothing else. <c>bundleVersion</c> is set by a build script, so it is ours — but it reaches a URL
    /// here, and a value that reaches a URL gets checked rather than trusted.
    /// </summary>
    private static string Token(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var b = new StringBuilder();
        foreach (char c in s!)
        {
            if (b.Length >= 24) break;
            if ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '.' || c == '-') b.Append(c);
        }
        return b.ToString();
    }

    /// <summary>What the game knows about itself that is worth a support agent's time.</summary>
    public sealed class Facts
    {
        public string Version = "";
        public SupportPlatform Platform = SupportPlatform.Ios;
        /// <summary>e.g. "iPad15,4" or "Mac15,3" — the model, which is a class of hardware and not a device.</summary>
        public string DeviceModel = "";
        public string OperatingSystem = "";
        /// <summary>The aircraft they are flying, because half of all flight-model reports are about one type.</summary>
        public string Aircraft = "";
        /// <summary>Set when the last run ended in a crash rather than a quit.</summary>
        public string LastCrash = "";
        /// <summary>Warnings and errors the run has logged, oldest first.</summary>
        public IReadOnlyList<string> Notes = Array.Empty<string>();
        /// <summary>The pilot's chosen name, so it can be taken OUT. Never printed.</summary>
        public string PilotName = "";
    }

    /// <summary>
    /// The text the "Copy diagnostics" button puts on the clipboard. Plain, short, and readable by the
    /// person who is about to paste it — somebody who cannot read what they are sending has not been asked
    /// properly.
    /// </summary>
    public static string Diagnostics(Facts f)
    {
        var b = new StringBuilder();
        b.Append("Aero Playground diagnostics\n");
        b.Append("build: ").Append(Blank(f.Version)).Append('\n');
        b.Append("platform: ").Append(Tag(f.Platform)).Append('\n');
        b.Append("device: ").Append(Blank(f.DeviceModel)).Append('\n');
        b.Append("os: ").Append(Blank(f.OperatingSystem)).Append('\n');
        b.Append("aircraft: ").Append(Blank(f.Aircraft)).Append('\n');
        if (!string.IsNullOrEmpty(f.LastCrash))
            b.Append("last run ended in a crash: ").Append(Scrub(f.LastCrash, f.PilotName)).Append('\n');
        int n = f.Notes?.Count ?? 0;
        if (n == 0) b.Append("warnings: none this run\n");
        else
        {
            b.Append("warnings, newest last");
            if (n > MaxNotes) b.Append(" (last ").Append(MaxNotes).Append(" of ").Append(n).Append(')');
            b.Append(":\n");
            for (int i = Math.Max(0, n - MaxNotes); i < n; i++)
                b.Append("  ").Append(Scrub(f.Notes![i], f.PilotName)).Append('\n');
        }
        b.Append("(no pilot name and no purchase identifiers: this block is yours to paste or not)");
        return b.ToString();
    }

    private static string Blank(string? s) => string.IsNullOrEmpty(s) ? "unknown" : Line(s!);

    /// <summary>One line, bounded: a stack trace arrives with newlines in it and a block of them is unreadable.</summary>
    private static string Line(string s)
    {
        var b = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (b.Length >= 300) { b.Append('…'); break; }
            b.Append(c == '\n' || c == '\r' || c == '\t' ? ' ' : c);
        }
        return b.ToString().Trim();
    }

    /// <summary>
    /// Takes the person back out of a log line: their pilot name, any product or transaction identifier,
    /// and anything shaped like an e-mail address. Case-insensitive on the name, because the net code
    /// prints it as typed and the room code upper-cases it.
    /// </summary>
    public static string Scrub(string? s, string? pilotName)
    {
        if (string.IsNullOrEmpty(s)) return "";
        string line = Line(s!);
        var b = new StringBuilder(line.Length);
        int i = 0;
        while (i < line.Length)
        {
            if (!IsWord(line[i])) { b.Append(line[i++]); continue; }
            int j = i;
            while (j < line.Length && IsWord(line[j])) j++;
            string w = line.Substring(i, j - i);
            b.Append(Redact(w, pilotName));
            i = j;
        }
        return b.ToString();
    }

    // A "word" for scrubbing purposes holds the characters an identifier or address is made of, so
    // com.tyfrisby.aeroplayground.unlock and pilot@example.com each come out as ONE word.
    private static bool IsWord(char c)
        => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
           || c == '.' || c == '_' || c == '-' || c == '@' || c == '+';

    private static string Redact(string w, string? pilotName)
    {
        if (!string.IsNullOrEmpty(pilotName) && pilotName!.Length >= 3
            && w.IndexOf(pilotName, StringComparison.OrdinalIgnoreCase) >= 0) return "<pilot>";
        if (w.IndexOf(ProductPrefix, StringComparison.OrdinalIgnoreCase) >= 0) return "<product>";
        if (w.IndexOf('@') > 0 && w.IndexOf('.', w.IndexOf('@')) > 0) return "<address>";
        // A StoreKit transaction identifier is a long run of digits. Dates and heights are not.
        int digits = 0;
        foreach (char c in w) if (c >= '0' && c <= '9') digits++; else { digits = -1; break; }
        return digits >= 10 ? "<id>" : w;
    }
}
