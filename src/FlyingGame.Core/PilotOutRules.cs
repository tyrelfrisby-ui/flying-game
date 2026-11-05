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
        /// <summary>He did not survive the crash, and his ghost is going UP under its canopy (<see cref="GhostAscends"/>).</summary>
        Ascending,
    }

    /// <summary>May any HUD imagery (the green HUD, the floating dials, the flight-path line, the dev status
    /// lines) be drawn? Only while the pilot is aboard.</summary>
    public static bool ShowHud(Stage stage) => stage == Stage.Aboard;

    /// <summary>Is the landing one-liner up? Once he is down and has said it, until the next flight.</summary>
    public static bool ShowRemark(Stage stage, bool spoken, string? line) =>
        (stage == Stage.Landed || stage == Stage.Ascending) && spoken && !string.IsNullOrEmpty(line);

    // ---- the ghost (Ty 2026-11-05 06:35) ------------------------------------------------------------------------
    //
    // "when a pilot bails out after crashing a semi transparent image of them getting out and [deploying] their
    // parachute takes them up to the sky and it says something corny about them going to heaven". Whether he is a
    // ghost is the crash's verdict (OccupantInjury), taken when he leaves the aeroplane; nothing else decides it.

    /// <summary>Does the pilot leave as a ghost that rises? Only when the crash itself killed him.</summary>
    public static bool GhostAscends(LandingOutcome crash) => crash == LandingOutcome.Dead;

    /// <summary>The verdict once he is out: the crash's, made worse by the parachute landing if that was worse. A pilot
    /// who bailed out of a flying aeroplane has an Unhurt crash, so this is the landing's grade, as it always was.</summary>
    public static LandingOutcome Combined(LandingOutcome crash, LandingOutcome landing) => OccupantInjury.Worse(crash, landing);

    /// <summary>The label over the ghost's line (the flat status line and the headset's world text).</summary>
    public const string GhostLabel = "GONE WEST";

    /// <summary>The farewell, original lines in the spirit of a "blue skies and tailwinds" send-off. No song lyrics.</summary>
    public static readonly string[] HeavenLines =
    {
        "Blue skies and tailwinds, Captain. Next stop: the Good Ship Lollipop.",
        "Cleared direct to the pearly gates. Climb and maintain forever.",
        "Tailwinds all the way up. Save me a window seat on the Good Ship Lollipop.",
    };

    /// <summary>Seconds after he leaves the wreck that the ghost's canopy opens (no free fall: he is not falling).</summary>
    public const double GhostChuteSec = 1.2;

    /// <summary>The ghost's lift, in g, against his weight: net (this − 1) g upward until the canopy's drag balances it.
    /// Under the full 26 ft canopy (CdS ≈ 40 m², 105 kg, sea level) that is √(2·0.6·105·9.81 / (1.225·40)) ≈ 5 m/s of
    /// climb; the canopy fills over a distance (Knacke's n·D0), so a brisker rise also opens it sooner.</summary>
    public const double GhostLiftG = 1.6;

    /// <summary>Opacity of the ghost and his canopy: there, but you can see the sky through him.</summary>
    public const double GhostAlpha = 0.42;

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
