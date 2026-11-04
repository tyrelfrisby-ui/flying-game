namespace FlyingGame.Core;

/// <summary>What Options › TV SCREEN asks for when a TV is connected (docs/TV-COMPANION-PLAN.md §1.5).</summary>
public enum TvSetting
{
    /// <summary>The game full size on the TV, the phone becomes the controller.</summary>
    GameOnTv = 0,
    /// <summary>Leave the TV to iOS: it mirrors the phone, letterboxed.</summary>
    Mirror = 1,
}

/// <summary>What the screens are doing right now.</summary>
public enum TvMode
{
    /// <summary>No TV (or a platform without TV mode): the phone shows the game.</summary>
    Handheld,
    /// <summary>A TV is connected and iOS mirrors the phone onto it. Nothing of ours runs.</summary>
    Mirror,
    /// <summary>The world and the instruments are on the TV; the phone shows only the controls and menus.</summary>
    GameOnTv,
}

/// <summary>
/// TV MODE (Ty, 2026-11-04: "when a second screen connects the game renders full size on the TV and the phone becomes a
/// full-screen touch controller"). The decisions that do not need Unity: which mode, at what resolution, inside what
/// margin. The Unity side (Bridge/TvOutput.cs) only reads the displays and carries these out. Shared by every platform;
/// only iOS ever has <c>supported</c> true.
/// </summary>
public static class TvModeLogic
{
    /// <summary>
    /// The mode for this frame. A TV needs a platform that can drive it, and the Aero Widget app never uses one (it
    /// streams over Syphon / NDI and its window is a preview). With no second display there is nothing to decide.
    /// </summary>
    public static TvMode Decide(bool supported, int displayCount, TvSetting setting, bool widgetApp)
    {
        if (!supported || widgetApp || displayCount < 2) return TvMode.Handheld;
        return setting == TvSetting.Mirror ? TvMode.Mirror : TvMode.GameOnTv;
    }
}

/// <summary>The size the game renders at for a TV.</summary>
public static class TvResolution
{
    /// <summary>Longest side we render (px). A 4K TV is fed 1080p and upscales it itself: a phone GPU cannot draw the
    /// game at 3840 × 2160 and its own screen as well.</summary>
    public const int MaxLongSide = 1920;

    /// <summary>
    /// The TV's own shape, scaled down only if its long side is over <paramref name="maxLong"/>, with even dimensions
    /// (video scalers and encoders want them) and never larger than the TV itself. The aspect ratio is the TV's to the
    /// nearest pixel, so the camera's projection matches the screen it is shown on and nothing is stretched.
    /// Returns (0, 0) for a size that is not a size, which tells the caller to leave the display at its native mode.
    /// </summary>
    public static (int W, int H) Choose(int systemW, int systemH, int maxLong = MaxLongSide)
    {
        if (systemW <= 0 || systemH <= 0) return (0, 0);
        int longSide = Math.Max(systemW, systemH);
        double k = Math.Min(1.0, Math.Max(2, maxLong) / (double)longSide);
        return (Even(systemW * k, systemW), Even(systemH * k, systemH));
    }

    /// <summary>The nearest even number to <paramref name="x"/>, at least 2 and no more than <paramref name="limit"/>
    /// (rounded down to even, so an odd native size is never exceeded).</summary>
    private static int Even(double x, int limit)
    {
        int e = 2 * (int)Math.Round(x / 2.0, MidpointRounding.AwayFromZero);
        int cap = limit - (limit & 1);
        return Math.Max(2, Math.Min(e, cap));
    }
}

/// <summary>Where on the TV the instruments may go.</summary>
public static class TvSafeArea
{
    /// <summary>
    /// Title-safe inset on each edge, as a fraction of that edge (Q5 in the plan). Unity turns overscan compensation off
    /// on an external screen (DisplayManager.mm), so an HDMI TV that overscans crops whatever is at the very edge; Apple
    /// TV and modern sets do not overscan, so this is kept small.
    /// </summary>
    public const double InsetFraction = 0.025;

    /// <summary>The inset in pixels for an edge of <paramref name="edgePx"/>.</summary>
    public static double Inset(double edgePx) => Math.Max(0.0, edgePx) * InsetFraction;
}

/// <summary>A button on the TV-mode button pad (feature 3): the phone's screen when a TV and a game controller are both
/// connected. Every one opens a door that already exists (the toolbar's, the touch controls' or the pad's own).</summary>
public enum PadButton
{
    ViewChase, ViewCockpit, ViewTail, ViewSide, ViewFlyby, ViewLock,
    Smoke, FlapsUp, FlapsDown, Gear, CamRef, LookCentre,
    Pause, Reset, Aircraft, Clip, Rec, Aero,
}

/// <summary>
/// TV + GAME CONTROLLER (Ty, 2026-11-04: "the phone screen becomes a touchpad of extra buttons and controls (quick
/// access to views, switches, menus)"). With a pad flying the aeroplane and the picture on the TV, the whole phone is
/// free; this lays it out as a grid of big targets in three rows of purpose: views, switches, actions. Only buttons
/// that do something on this aeroplane, in this view, are listed, and the grid re-flows around the missing ones.
/// Pure: the Bridge (TvButtonPad.cs) draws it and acts on it.
/// </summary>
public static class TvPadLayout
{
    /// <summary>The three groups, in order. Each group starts a new row.</summary>
    public static List<List<PadButton>> Groups(bool flaps, bool gear, bool smoke, bool inCockpit)
    {
        var views = new List<PadButton> { PadButton.ViewChase, PadButton.ViewCockpit, PadButton.ViewTail, PadButton.ViewSide, PadButton.ViewFlyby, PadButton.ViewLock };
        var switches = new List<PadButton>();
        if (smoke) switches.Add(PadButton.Smoke);
        if (flaps) { switches.Add(PadButton.FlapsUp); switches.Add(PadButton.FlapsDown); }
        if (gear) switches.Add(PadButton.Gear);
        switches.Add(PadButton.CamRef);
        if (inCockpit) switches.Add(PadButton.LookCentre);
        var actions = new List<PadButton> { PadButton.Pause, PadButton.Reset, PadButton.Aircraft, PadButton.Clip, PadButton.Rec, PadButton.Aero };
        return new List<List<PadButton>> { views, switches, actions };
    }

    /// <summary>Columns: six across a landscape phone, three down a portrait one.</summary>
    public static int Columns(bool portrait) => portrait ? 3 : 6;

    /// <summary>
    /// The grid in the box (<paramref name="x"/>, <paramref name="y"/>, <paramref name="w"/>, <paramref name="h"/>),
    /// top-left origin, px. Cells are all one size, no taller than they are wide, with <paramref name="gap"/> between
    /// cells and twice that between groups; the grid is centred across and starts at the top.
    /// </summary>
    public static List<(PadButton Button, double X, double Y, double W, double H)> Layout(
        List<List<PadButton>> groups, bool portrait, double x, double y, double w, double h, double gap)
    {
        int cols = Columns(portrait);
        int rows = 0, nonEmpty = 0;
        foreach (var g in groups) if (g.Count > 0) { rows += (g.Count + cols - 1) / cols; nonEmpty++; }
        var cells = new List<(PadButton, double, double, double, double)>();
        if (rows == 0 || w <= 0 || h <= 0) return cells;
        double cw = (w - (cols - 1) * gap) / cols;
        double ch = Math.Min(cw, (h - (rows - 1) * gap - Math.Max(0, nonEmpty - 1) * gap) / rows);
        if (cw <= 0 || ch <= 0) return cells;
        double x0 = x + (w - (cols * cw + (cols - 1) * gap)) * 0.5;
        double cy = y;
        foreach (var g in groups)
        {
            if (g.Count == 0) continue;
            for (int i = 0; i < g.Count; i++)
            {
                int r = i / cols, c = i % cols;
                cells.Add((g[i], x0 + c * (cw + gap), cy + r * (ch + gap), cw, ch));
            }
            int groupRows = (g.Count + cols - 1) / cols;
            cy += groupRows * (ch + gap) + gap;   // the extra gap sets the groups apart
        }
        return cells;
    }

    /// <summary>The button's label (upper case, as the rest of the game's buttons).</summary>
    public static string Label(PadButton b) => b switch
    {
        PadButton.ViewChase => "CHASE",
        PadButton.ViewCockpit => "COCKPIT",
        PadButton.ViewTail => "TAIL",
        PadButton.ViewSide => "SIDE",
        PadButton.ViewFlyby => "FLY-BY",
        PadButton.ViewLock => "LOCK",
        PadButton.Smoke => "SMOKE",
        PadButton.FlapsUp => "FLAPS UP",
        PadButton.FlapsDown => "FLAPS DOWN",
        PadButton.Gear => "GEAR",
        PadButton.CamRef => "CAM",
        PadButton.LookCentre => "LOOK AHEAD",
        PadButton.Pause => "PAUSE",
        PadButton.Reset => "RESET",
        PadButton.Aircraft => "AIRCRAFT",
        PadButton.Clip => "CLIP",
        PadButton.Rec => "REC",
        PadButton.Aero => "AERO",
        _ => b.ToString().ToUpperInvariant(),
    };
}
