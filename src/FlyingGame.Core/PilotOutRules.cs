namespace FlyingGame.Core;

/// <summary>
/// What may be on screen once the pilot has started to leave the aeroplane — the same on every platform.
///
/// Ty 2026-11-04, after bailing out of the Great Lakes on the Quest: "the hud imagery showed up which should not
/// happen". The HUD describes the AEROPLANE (its pitch ladder, its speed, its flight path), and once the pilot is
/// climbing out, falling, hanging under a canopy or standing on the ground it is describing a machine he is no
/// longer in. On the Quest it came back because its only gate was "in the cockpit view?" and the camera leaves the
/// cockpit with him; on a flat screen nothing hid it at all. One rule here, read by every HUD.
///
/// And the other way round (same message): "there was no corny comment when i got to the ground in my parachute i
/// like that make sure it is in the quest version as well". The landing one-liner is NOT HUD — it is the pilot
/// talking — so it stays after the HUD has gone. On a flat screen it is drawn on the egress status line; a headset
/// cannot see a flat overlay, so there it is drawn in the world, in front of his eyes.
///
/// Pure: no Unity. The bridge maps its egress phases onto <see cref="Stage"/> (PilotEgress.Stage).
/// </summary>
public static class PilotOutRules
{
    /// <summary>Where the pilot is, as far as the screen cares.</summary>
    public enum Stage
    {
        /// <summary>Strapped in and flying: the only stage with a HUD.</summary>
        Aboard,
        /// <summary>Bail-out started — canopy going, climbing over the side. Still in the cockpit, already leaving.</summary>
        Egressing,
        /// <summary>Clear of the aeroplane, chute not yet open (and while it deploys).</summary>
        FreeFall,
        /// <summary>Hanging under a full canopy.</summary>
        UnderCanopy,
        /// <summary>On the ground or in the water (tumbling down a slope counts: he has arrived).</summary>
        Landed,
    }

    /// <summary>May any HUD imagery (the green HUD, the floating dials, the flight-path line, the dev status
    /// lines) be drawn? Only while the pilot is aboard.</summary>
    public static bool ShowHud(Stage stage) => stage == Stage.Aboard;

    /// <summary>Is the landing one-liner up? Once he is down and has said it, until the next flight.</summary>
    public static bool ShowRemark(Stage stage, bool spoken, string? line) =>
        stage == Stage.Landed && spoken && !string.IsNullOrEmpty(line);

    /// <summary>Draw the one-liner in the world (in front of the eyes) rather than on the flat status line? Only
    /// where the flat overlay cannot be seen — a headset — so a flat screen never shows it twice.</summary>
    public static bool RemarkInWorld(bool flatOverlayVisible) => !flatOverlayVisible;

    /// <summary>Metres in front of the eye the world-space one-liner floats: comfortably inside a headset's focal
    /// range, beyond arm's reach so a hand never cuts it.</summary>
    public const double RemarkDistanceM = 2.0;

    /// <summary>Degrees below the line of sight it sits — read with a glance, not blocking the view of the ground.</summary>
    public const double RemarkBelowDeg = 8.0;

    /// <summary>Longest line of the world-space one-liner, characters: at ~3.5 cm a character and 2 m that is about
    /// 30 degrees, inside the clear middle of a headset's lens.</summary>
    public const int RemarkWrapChars = 32;

    /// <summary>The world-space text: the outcome label ("WALKED AWAY") on its own line, then the quote in curly
    /// quotes — what the flat status line says — word-wrapped at <see cref="RemarkWrapChars"/>.</summary>
    public static string RemarkText(string label, string line)
    {
        var lines = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrEmpty(label)) lines.Add(label);
        var cur = new System.Text.StringBuilder();
        foreach (string word in ("“" + line + "”").Split(' ', System.StringSplitOptions.RemoveEmptyEntries))
        {
            if (cur.Length > 0 && cur.Length + 1 + word.Length > RemarkWrapChars) { lines.Add(cur.ToString()); cur.Clear(); }
            if (cur.Length > 0) cur.Append(' ');
            cur.Append(word);
        }
        if (cur.Length > 0) lines.Add(cur.ToString());
        return string.Join("\n", lines);
    }
}
