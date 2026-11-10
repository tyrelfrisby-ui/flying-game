using System;
namespace FlyingGame.Core;

/// <summary>Native-pixel exterior gauge geometry. One value/unit line sits below each face.</summary>
public static class HudInstrumentLayout
{
    public static double ToolbarGap(double s) => s * 12 / 1206;
    public static double DialRadius(double s, bool gamepad = false) => s * (gamepad ? 92.5 : 75) / 1206;
    public static double RowWidth(double s, double available, bool gamepad = false) => Math.Min(available, s * (gamepad ? 820.0 / 1206 : 700.0 / 1206));
    public static double ValueHeight(double s) => Math.Max(22, s * 33 / 1206) * 1.05;
    public static double EngineRadius(double s, bool gamepad = false) => s * (gamepad ? 67.5 : 55) / 1206;
    public static double NumeralText(double s, bool gamepad = false) => s * (gamepad ? 24 : 20) / 1206;
    public static double RowGap(double s) => s * 6 / 1206;
    public static double RowHeight(double s, bool gamepad = false) => 2 * DialRadius(s, gamepad) + ValueHeight(s);
    public static double EngineTop(double toolbarBottom, double gap, double s, bool gamepad = false) => toolbarBottom + ToolbarGap(s) + RowHeight(s, gamepad) + RowGap(s);
    public static double Bottom(double toolbarBottom, double gap, double s, bool gamepad, bool engine) =>
        toolbarBottom + ToolbarGap(s) + RowHeight(s, gamepad) + (engine ? RowGap(s) + 2 * EngineRadius(s, gamepad) + ValueHeight(s) : 0);
    public static double FuelGallons(double kg) => kg / (0.72 * 3.785);
    /// <summary>Height of the engine row: dial face plus its one value/unit line.</summary>
    public static double EngineBlockHeight(double s, bool gamepad = false, int valueLines = 1) => 2 * EngineRadius(s, gamepad) + valueLines * ValueHeight(s);
    /// <summary>Lines the engine values need: the widest ("1822 RPM", "29.3 GAL") is 8 bold glyphs at ~0.6 em each, so
    /// a cell narrower than 4.8 em puts the unit on a second line rather than shrinking text below MinText or
    /// letting neighbours overlap (the narrow touch band between the iPad's pads).</summary>
    public static int EngineValueLines(double cellWidth, double minText) => cellWidth >= minText * 4.8 ? 1 : 2;
    /// <summary>
    /// WHETHER THE FOUR-DIAL FLIGHT ROW (ASI / ALT / VSI / G) IS DRAWN AT ALL.
    ///
    /// Ty 2026-11-10, with a screenshot of the Pitts cockpit view: "The instrument row above the touch pads in cockpit
    /// view never got fixed. Cockpits have instruments to look at and it takes up valuable space get rid of it". It is a
    /// REPEAT report — "never got fixed" — so the rule lives here, in Core, where the headless suite locks it
    /// (HudInstrumentLayoutTests), rather than only as a branch inside a Unity MonoBehaviour that no test can reach. A
    /// lane that merges the row back now fails a test instead of reaching his phone a third time.
    ///
    /// The row cost a third of the screen: on his 1206 x 2622 portrait shot the dark band ran from the bottom of the
    /// rendered cockpit to the button strip, about 325 px of a ~1275 px camera view, and it DUPLICATED the Pitts's own
    /// panel — its ASI, altimeter and engine gauge are drawn on the glareshield a few centimetres above it. Deleting it
    /// gives the space straight back to the 3-D view: the band was painted INSIDE the camera's rect, over the lower
    /// third of the cockpit, so nothing in UiLayout has to be re-measured for those pixels to come back.
    ///
    /// NOT an unconditional "never in the cockpit", and this is the part worth keeping. Cockpit view is reachable for
    /// EVERY type — ChaseCamera.View.Cockpit is not gated on a model — including the procedural shell (OPTIONS →
    /// Airframe → Drawn) and any downloaded model with no panel of its own. Taking the row away there would leave that
    /// pilot with no instruments at all, so the second argument asks the aeroplane on screen whether it draws its own
    /// (AirframeVisual.CockpitInstrumentsDrawn: the placed model brought a rig, or live cockpit parts were found in it —
    /// the same pair AirframeVisual already uses to decide whether to drive a cockpit). Outside the cockpit the row is
    /// the ONLY instrumentation there is and is always drawn: that case is untouched.
    /// </summary>
    /// <param name="inCockpit">The view being drawn is the cockpit (not an outside view, not the Mac's panel surface).</param>
    /// <param name="cockpitHasOwnInstruments">The model on screen draws instruments on its own panel.</param>
    public static bool ShowFlightDials(bool inCockpit, bool cockpitHasOwnInstruments) =>
        !inCockpit || !cockpitHasOwnInstruments;

    /// <summary>Left edge of a row of <paramref name="width"/> centred on <paramref name="centreX"/>, pushed inside [minX, maxX]
    /// (owner 2026-11-02: the engine row sits centred below the airplane but never on a button column or a pad).</summary>
    public static double CentredRowLeft(double centreX, double width, double minX, double maxX) =>
        Math.Max(minX, Math.Min(maxX - width, centreX - width / 2));
}
