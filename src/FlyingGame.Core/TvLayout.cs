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
    /// <summary>Mac (Ty 2026-11-04, Q8): the game stays in its window and the instruments fill the second monitor.</summary>
    InstrumentsOnMonitor,
}

/// <summary>Options › SECOND MONITOR on the Mac (Q8). Off is 0, so a Mac that never chose keeps its monitor: once
/// Unity has put a window on a display it cannot take it off again until the app quits.</summary>
public enum MonitorSetting
{
    Off = 0,
    Instruments = 1,
}

/// <summary>What kind of device the game is running on, as far as a second screen is concerned.</summary>
public enum TvDevice
{
    /// <summary>The Editor, the Aero Widget's host, anything not listed: no second-screen mode.</summary>
    Other,
    IPhone,
    /// <summary>Ty 2026-11-04 (Q2): GAME ON TV applies to the iPad too, AirPlay or HDMI, the same code as the iPhone.</summary>
    IPad,
    /// <summary>Ty 2026-11-04 (Q8): a second monitor shows the instruments.</summary>
    Mac,
    /// <summary>The headset is its own screen; nothing here applies.</summary>
    Quest,
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
        => Decide(supported ? TvDevice.IPhone : TvDevice.Other, displayCount, setting, MonitorSetting.Off, widgetApp);

    /// <summary>
    /// The mode for this frame on <paramref name="device"/>. iPhone and iPad (Q2) follow TV SCREEN; the Mac follows
    /// SECOND MONITOR (Q8) and never mirrors, because macOS has no mirroring of ours to give back; the Quest and
    /// everything else stay handheld.
    /// </summary>
    public static TvMode Decide(TvDevice device, int displayCount, TvSetting tv, MonitorSetting monitor, bool widgetApp)
    {
        if (widgetApp || displayCount < 2) return TvMode.Handheld;
        switch (device)
        {
            case TvDevice.IPhone:
            case TvDevice.IPad:
                return tv == TvSetting.Mirror ? TvMode.Mirror : TvMode.GameOnTv;
            case TvDevice.Mac:
                return monitor == MonitorSetting.Instruments ? TvMode.InstrumentsOnMonitor : TvMode.Handheld;
            default:
                return TvMode.Handheld;
        }
    }

    /// <summary>The devices that have a TV SCREEN choice in Options (and the Mac has SECOND MONITOR instead).</summary>
    public static bool HasTvSetting(TvDevice device) => device == TvDevice.IPhone || device == TvDevice.IPad;

    /// <summary>The devices that have a SECOND MONITOR choice in Options.</summary>
    public static bool HasMonitorSetting(TvDevice device) => device == TvDevice.Mac;
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

/// <summary>
/// Where the analog ENGINE row goes on the TV (Ty, 2026-11-04 07:33: "in TV mode with analog instruments, the engine
/// instruments must go BELOW the airplane/main view, not above — too crowded above"). The flight row keeps the top;
/// the engine row is centred along the TV's bottom edge inside the title-safe margin, at the full-width sizes the phone
/// uses with a pad (there are no sticks on a TV). The chase camera holds the aeroplane in the middle of the picture, so
/// the bottom edge is below it. GUI coordinates (top-left origin), px.
/// </summary>
public static class TvEngineRow
{
    /// <summary>
    /// The engine row's box on a <paramref name="tvW"/> × <paramref name="tvH"/> picture whose short edge is
    /// <paramref name="s"/>. <paramref name="flightRowBottom"/> is the bottom of the flight row (dials and their value
    /// line): on a picture too short for both, the engine row stops a <paramref name="gap"/> under it rather than
    /// overlapping it. <paramref name="availableWidth"/> is the width inside the side margins.
    /// </summary>
    public static (double X, double Y, double W, double H) Below(double tvW, double tvH, double s, double safeBottom,
        double margin, double gap, double availableWidth, double flightRowBottom)
    {
        double w = HudInstrumentLayout.RowWidth(s, availableWidth, gamepad: true);
        double h = HudInstrumentLayout.EngineBlockHeight(s, gamepad: true);
        double y = Math.Max(flightRowBottom + gap, tvH - safeBottom - margin - h);
        return ((tvW - w) / 2, y, w, h);
    }
}

/// <summary>
/// INSTRUMENTS ON A SECOND MONITOR (Mac, Ty 2026-11-04 Q8). The dials are drawn by the same code the TV pass runs, on a
/// square reference surface (<see cref="ReferenceSide"/>, the iPhone's short side the layout was drawn for) with the
/// engine row stacked straight under the flight row, and that block is then scaled, uniformly and centred, to fill the
/// monitor inside a margin. Uniform, so a dial is never an oval; the block keeps the shape it has on the phone.
/// GUI coordinates (top-left origin), px.
/// </summary>
public static class MonitorPanel
{
    /// <summary>Side of the square surface the panel is laid out on before it is scaled to the monitor.</summary>
    public const double ReferenceSide = 1206;

    /// <summary>Margin on every edge of the monitor, as a fraction of that edge.</summary>
    public const double MarginFraction = 0.04;

    /// <summary>The engine row on the panel: centred, a <paramref name="gap"/> under the flight row, at the pad sizes.</summary>
    public static (double X, double Y, double W, double H) EngineRow(double surfaceW, double s, double gap, double flightRowBottom)
    {
        double w = HudInstrumentLayout.RowWidth(s, double.MaxValue, gamepad: true);
        return ((surfaceW - w) / 2, flightRowBottom + gap, w, HudInstrumentLayout.EngineBlockHeight(s, gamepad: true));
    }

    /// <summary>
    /// The box the instruments occupy on an <paramref name="s"/> × <paramref name="s"/> surface whose (unused) toolbar
    /// ends at <paramref name="toolbarBottom"/>: the flight row (dials and their value line) and, with an engine, the
    /// engine row under it. This is what gets scaled to the monitor.
    /// </summary>
    public static (double X, double Y, double W, double H) Block(double s, double toolbarBottom, double gap, bool engine)
    {
        double w = HudInstrumentLayout.RowWidth(s, double.MaxValue, gamepad: true);
        double top = toolbarBottom + HudInstrumentLayout.ToolbarGap(s);
        double bottom = HudInstrumentLayout.Bottom(toolbarBottom, gap, s, true, false);
        if (engine) bottom = EngineRow(s, s, gap, bottom).Y + HudInstrumentLayout.EngineBlockHeight(s, gamepad: true);
        return ((s - w) / 2, top, w, bottom - top);
    }

    /// <summary>
    /// The square to lay the panel out on for a monitor that the <see cref="ReferenceSide"/> layout would fill at
    /// <paramref name="scaleAtReference"/>: big enough that the final scale is about 1, so the dial faces and the text
    /// are drawn at the monitor's own pixels rather than magnified (IMGUI rasterises text at its font size). 600–4096.
    /// </summary>
    public static int SideFor(double scaleAtReference)
        => (int)Math.Round(Math.Max(600, Math.Min(4096, ReferenceSide * (double.IsNaN(scaleAtReference) ? 1 : scaleAtReference))));

    /// <summary>
    /// Scale <paramref name="block"/> uniformly to fill a <paramref name="monitorW"/> × <paramref name="monitorH"/>
    /// monitor inside <see cref="MarginFraction"/>, centred. A point u of the surface lands on the monitor at
    /// <c>Offset + Scale · (u − block corner)</c>. A block or monitor that is not a size gives scale 0.
    /// </summary>
    public static (double Scale, double OffsetX, double OffsetY) Fit((double X, double Y, double W, double H) block, double monitorW, double monitorH)
    {
        if (block.W <= 0 || block.H <= 0 || monitorW <= 0 || monitorH <= 0) return (0, 0, 0);
        double f = Math.Min(monitorW * (1 - 2 * MarginFraction) / block.W, monitorH * (1 - 2 * MarginFraction) / block.H);
        return (f, (monitorW - block.W * f) / 2, (monitorH - block.H * f) / 2);
    }
}
