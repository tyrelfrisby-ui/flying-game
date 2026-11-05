using System;
using System.Collections.Generic;

namespace FlyingGame.Core;

/// <summary>
/// The cockpit parts a model drew as PICTURES that the game has to make move: the rear (PIC) panels that
/// tools/own_models/rear_panel.py lays into the Pitts and the Extra ("procedural visual assignments, all
/// static" — every dial a painted face with a painted needle), and the Pitts' sticks and rudder pedals, which
/// are part of its one static <c>Cockpit</c> mesh. On an iPhone the flat dials (AnalogGauges) carried the
/// readings, so nobody noticed; in a headset the flat overlays stand down and the panel is all there is.
///
/// Everything here is data and arithmetic, engine-free and tested (CockpitLiveTests). Coordinates are in the
/// GLB's own glTF frame (+x aft, +y up, +z to the pilot's LEFT; the importer mirrors x), in metres, exactly as
/// docs/models/*-rig.json gives its pivots — the numbers are read off the GLB, not guessed, and the Bridge
/// (CockpitLive.cs) logs how many triangles each volume took so a rebuilt model that no longer matches says so.
///
/// The needle maps are the EA 300 rig's own (docs/models/ea300-own-rig.json): the dial art they turn over
/// (tools/own_models/art/ea-dial-*.png) was drawn for exactly those maps, and an angle is
/// <c>Ang0 + map(value)</c>, degrees clockwise from 12 o'clock as the pilot sees the dial.
/// </summary>
public enum NeedleShape
{
    /// <summary>A long pointer with a short tail through the hub.</summary>
    Pointer,
    /// <summary>The altimeter's short, broad thousands hand.</summary>
    Hand,
    /// <summary>A thin max/min tell-tale that rides out with the main needle and stays.</summary>
    TellTale,
    /// <summary>A fixed index mark at the rim (the compass lubber line).</summary>
    Lubber,
    /// <summary>A fixed bar across the hub (the attitude indicator's miniature aeroplane).</summary>
    Bar,
}

/// <summary>One needle on a live dial.</summary>
public sealed class DialNeedle
{
    /// <summary>The <see cref="RigInputs"/> parameter it reads, or "" for a fixed mark.</summary>
    public readonly string Param;
    /// <summary>Where the needle points at the map's zero, degrees clockwise from 12 o'clock.</summary>
    public readonly double Ang0Deg;
    private readonly double[] _p, _d;
    /// <summary>The map runs on past its ends (the altimeter's hands go round and round).</summary>
    public readonly bool Unbounded;
    public readonly NeedleShape Shape;
    /// <summary>Linear RGB, 0..1.</summary>
    public readonly float R, G, B;

    public DialNeedle(string param, double ang0Deg, double[] mapParam, double[] mapDeg, NeedleShape shape,
                      float r = 0.95f, float g = 0.95f, float b = 0.92f, bool unbounded = false)
    {
        if (mapParam == null || mapDeg == null || mapParam.Length != mapDeg.Length || mapParam.Length == 0)
            throw new ArgumentException("needle map: parameter and degree columns must be the same non-empty length");
        Param = param ?? ""; Ang0Deg = ang0Deg; _p = mapParam; _d = mapDeg;
        Shape = shape; R = r; G = g; B = b; Unbounded = unbounded;
    }

    /// <summary>A mark that never moves.</summary>
    public static DialNeedle Fixed(double angDeg, NeedleShape shape, float r, float g, float b) =>
        new("", angDeg, new[] { 0.0 }, new[] { 0.0 }, shape, r, g, b);

    /// <summary>Degrees clockwise from 12 o'clock for a reading: piecewise-linear, clamped at the ends unless
    /// <see cref="Unbounded"/>, where the last segment carries on (the result may then pass 360).</summary>
    public double AngleDeg(double value)
    {
        if (Param.Length == 0 || double.IsNaN(value)) return Ang0Deg + _d[0];
        int n = _p.Length;
        if (n == 1) return Ang0Deg + _d[0];
        int i;
        if (value <= _p[0]) { if (!Unbounded) return Ang0Deg + _d[0]; i = 0; }
        else if (value >= _p[n - 1]) { if (!Unbounded) return Ang0Deg + _d[n - 1]; i = n - 2; }
        else { i = 0; while (i < n - 2 && value > _p[i + 1]) i++; }
        double u = (value - _p[i]) / (_p[i + 1] - _p[i]);
        return Ang0Deg + _d[i] + (_d[i + 1] - _d[i]) * u;
    }
}

/// <summary>A painted dial face that becomes a live instrument.</summary>
public sealed class LiveDial
{
    /// <summary>The face node in the GLB (RearPanel_&lt;Name&gt;_Face).</summary>
    public readonly string Face;
    /// <summary>The art that replaces the painted face: Resources/CockpitDials/&lt;Art&gt; (no needle painted on it).</summary>
    public readonly string Art;
    /// <summary>"" for a fixed face; otherwise the face ITSELF turns by <see cref="CardSign"/> × this reading
    /// (the compass card, the attitude indicator's horizon), clockwise as the pilot sees it.</summary>
    public readonly string CardParam;
    public readonly double CardSign;
    public readonly IReadOnlyList<DialNeedle> Needles;

    public LiveDial(string name, string art, IReadOnlyList<DialNeedle> needles, string cardParam = "", double cardSign = 0.0, string facePrefix = "RearPanel_")
    {
        Face = facePrefix + name + "_Face";
        Art = art; Needles = needles ?? Array.Empty<DialNeedle>(); CardParam = cardParam ?? ""; CardSign = cardSign;
    }

    /// <summary>Degrees the face is turned for a reading (clockwise as the pilot sees it).</summary>
    public double CardDeg(double value) => CardParam.Length == 0 || double.IsNaN(value) ? 0.0 : CardSign * value;
}

/// <summary>A stick cut out of a static cockpit mesh: every triangle wholly inside the capsule A–B, turned
/// about <see cref="Pivot"/> (its root at the floor).</summary>
public sealed class StickCut
{
    public readonly string Name;
    public readonly double Ax, Ay, Az, Bx, By, Bz, Radius, Px, Py, Pz;
    public StickCut(string name, double ax, double ay, double az, double bx, double by, double bz, double radius,
                    double px, double py, double pz)
    {
        Name = name; Ax = ax; Ay = ay; Az = az; Bx = bx; By = by; Bz = bz; Radius = radius; Px = px; Py = py; Pz = pz;
    }

    public bool Contains(double x, double y, double z)
    {
        double dx = Bx - Ax, dy = By - Ay, dz = Bz - Az;
        double t = ((x - Ax) * dx + (y - Ay) * dy + (z - Az) * dz) / (dx * dx + dy * dy + dz * dz);
        t = Math.Clamp(t, 0.0, 1.0);
        double ex = x - (Ax + t * dx), ey = y - (Ay + t * dy), ez = z - (Az + t * dz);
        return ex * ex + ey * ey + ez * ez <= Radius * Radius;
    }
}

/// <summary>A rudder pedal cut out of a static cockpit mesh: every triangle wholly inside the box, hinged at
/// the floor about the lateral axis through <see cref="Pivot"/>.</summary>
public sealed class PedalCut
{
    public readonly string Name;
    public readonly double MinX, MinY, MinZ, MaxX, MaxY, MaxZ, Px, Py, Pz;
    /// <summary>True for the RIGHT pedal (glTF z &lt; 0: +z is the pilot's left).</summary>
    public bool Right => Pz < 0.0;
    public PedalCut(string name, double minX, double minY, double minZ, double maxX, double maxY, double maxZ,
                    double px, double py, double pz)
    {
        Name = name; MinX = minX; MinY = minY; MinZ = minZ; MaxX = maxX; MaxY = maxY; MaxZ = maxZ; Px = px; Py = py; Pz = pz;
    }

    public bool Contains(double x, double y, double z) =>
        x >= MinX && x <= MaxX && y >= MinY && y <= MaxY && z >= MinZ && z <= MaxZ;
}

/// <summary>A throttle lever the model does not have, drawn by the game: pivot, arm length, knob size, metres.</summary>
public sealed class LeverAdd
{
    public readonly string Name;
    public readonly double Px, Py, Pz, LengthM, KnobM;
    public LeverAdd(string name, double px, double py, double pz, double lengthM, double knobM)
    {
        Name = name; Px = px; Py = py; Pz = pz; LengthM = lengthM; KnobM = knobM;
    }
}

/// <summary>What one model needs made live.</summary>
public sealed class CockpitLiveSpec
{
    public IReadOnlyList<LiveDial> Dials = Array.Empty<LiveDial>();
    public IReadOnlyList<StickCut> Sticks = Array.Empty<StickCut>();
    public IReadOnlyList<PedalCut> Pedals = Array.Empty<PedalCut>();
    public IReadOnlyList<LeverAdd> Throttles = Array.Empty<LeverAdd>();
    /// <summary>Rig nodes that no longer sit on any dial and are hidden (the Extra's rear needles, left
    /// floating over a bare panel when rear_panel.py stripped the dials under them).</summary>
    public IReadOnlyList<string> Hide = Array.Empty<string>();
    /// <summary>How many triangles each cut is expected to take, for the log (0 = not checked).</summary>
    public int StickTris, PedalTris;
    public bool IsEmpty => Dials.Count == 0 && Sticks.Count == 0 && Pedals.Count == 0 && Throttles.Count == 0 && Hide.Count == 0;
}

/// <summary>The table, and the arithmetic that moves the parts.</summary>
public static class CockpitLive
{
    // ---- the needles (EA 300 rig maps, docs/models/ea300-own-rig.json; mph ASI from the Great Lakes rig) ----

    private static readonly float[] White = { 0.95f, 0.95f, 0.92f };
    private static DialNeedle N(string param, double ang0, double[] p, double[] d, NeedleShape s = NeedleShape.Pointer, bool unbounded = false) =>
        new(param, ang0, p, d, s, White[0], White[1], White[2], unbounded);
    private static DialNeedle Tell(string param, double ang0, double[] p, double[] d) =>
        new(param, ang0, p, d, NeedleShape.TellTale, 0.95f, 0.55f, 0.15f);

    public static LiveDial AirspeedKt => new("Airspeed", "ea-dial-asi", new[]
        { N("airspeed_kias", 0, new[] { 0.0, 40.0, 260.0 }, new[] { 0.0, 25.0, 335.0 }) });
    public static LiveDial AirspeedMph => new("Airspeed", "gl-dial-asi", new[]
        { N("airspeed_mph", 0, new[] { 0.0, 40.0, 200.0 }, new[] { 0.0, 30.0, 330.0 }) });
    public static LiveDial Altimeter => new("Altimeter", "ea-dial-alt", new[]
    {
        N("altitude_ft", 0, new[] { 0.0, 10000.0 }, new[] { 0.0, 360.0 }, NeedleShape.Hand, unbounded: true),
        N("altitude_ft", 0, new[] { 0.0, 1000.0 }, new[] { 0.0, 360.0 }, NeedleShape.Pointer, unbounded: true),
    });
    public static LiveDial GMeter => new("G_meter", "ea-dial-g", new[]
    {
        Tell("g_max_seen", -90, new[] { 0.0, 10.0 }, new[] { 0.0, 120.0 }),
        Tell("g_min_seen", -90, new[] { -10.0, 0.0 }, new[] { -120.0, 0.0 }),
        N("g_load", -90, new[] { -10.0, 0.0, 10.0 }, new[] { -120.0, 0.0, 120.0 }),
    });
    public static LiveDial Tachometer => new("Tachometer", "ea-dial-tach", new[]
        { N("rpm", -150, new[] { 0.0, 3500.0 }, new[] { 0.0, 300.0 }) });
    /// <summary>The EA's oil gauge is one dial with two needles: pressure over the top, temperature under.</summary>
    public static LiveDial Oil(string slot) => new(slot, "ea-dial-oil", new[]
    {
        N("oil_press_psi", -80, new[] { 0.0, 120.0 }, new[] { 0.0, 160.0 }),
        N("oil_temp_f", 100, new[] { 100.0, 250.0 }, new[] { 0.0, 160.0 }),
    });
    public static LiveDial Manifold(string slot) => new(slot, "ea-dial-mp", new[]
        { N("manifold_inhg", -120, new[] { 10.0, 35.0 }, new[] { 0.0, 300.0 }) });
    public static LiveDial FuelQuantity => new("Fuel_quantity", "ea-dial-fuel", new[]
        { N("fuel_centre_frac", -60, new[] { 0.0, 1.0 }, new[] { 0.0, 120.0 }) });
    /// <summary>A vertical-card compass: the card turns the opposite way to the aeroplane so the heading sits
    /// under the fixed lubber line at 12 o'clock.</summary>
    public static LiveDial Compass => new("Compass", "live-compass-card", new[]
        { DialNeedle.Fixed(0.0, NeedleShape.Lubber, 1f, 0.55f, 0.1f) }, "heading_deg", -1.0);
    /// <summary>Bank only: the horizon card turns opposite the bank so it stays level with the real one. (The
    /// pitch ladder would need the card to slide too; the face is a disc on fixed UVs and cannot.)</summary>
    public static LiveDial AttitudeBank => new("Attitude", "live-attitude", new[]
    {
        DialNeedle.Fixed(0.0, NeedleShape.Lubber, 1f, 0.6f, 0.1f),    // the bank index the card's marks pass under
        DialNeedle.Fixed(90.0, NeedleShape.Bar, 1f, 0.6f, 0.1f),      // the miniature aeroplane
    }, "bank_deg", -1.0);

    // ---- the models ----

    /// <summary>The Pitts S-2B (tools/own_models/build_pitts_s2b.py, rear_panel.py). Glass-panel numbers from
    /// the build script, glTF x = −builder x, y = builder z, z = builder y; the volumes were checked against all
    /// four liveries' GLBs: 80 triangles per stick (shaft and grip cylinders) and 12 per pedal (one box), nothing
    /// else inside. Those counts are over the WHOLE Cockpit mesh, which is nine glTF primitives — and glTFast puts
    /// each primitive after the first on a child GameObject of its own, with the sticks' and pedals' triangles
    /// spread over several of them (Stick_R: 12 + 24 + 44). The first cut read only the node's own mesh, found
    /// nothing, and left every part static on the headset; CockpitLiveParts now reads them all.</summary>
    public static CockpitLiveSpec Pitts() => new()
    {
        Dials = new[]
        {
            AirspeedMph, Altimeter, GMeter, Tachometer, Oil("Oil_pressure"),
            new LiveDial("Airspeed", AirspeedMph.Art, AirspeedMph.Needles, facePrefix: "FrontPanel_"),
            new LiveDial("Altimeter", Altimeter.Art, Altimeter.Needles, facePrefix: "FrontPanel_"),
            new LiveDial("G_meter", GMeter.Art, GMeter.Needles, facePrefix: "FrontPanel_"),
            new LiveDial("Tachometer", Tachometer.Art, Tachometer.Needles, facePrefix: "FrontPanel_"),
            // The slot rear_panel.py labelled "oil temperature": the oil dial beside it already shows both oil
            // needles, so this one carries manifold pressure, the engine reading a constant-speed Pitts flies by.
            Manifold("Oil_temperature"), Compass,
        },
        // No stick or pedal cuts and no added throttle any more: since the v3 cockpit the model rigs its own sticks,
        // pedals and both throttles (Stick_Rear, Pedal_*_Rear, Throttle_Rear ...), and its one Cockpit mesh no longer
        // has a stick or pedal in it (measured 2026-11-04: one stray floor triangle in each old cut volume). The added
        // lever stood on the left wall as a SECOND rear throttle, over the prop and trim levers of Ty's photos.
    };

    /// <summary>The EA 300 (rear_panel.py's eleven-dial PIC panel over the rig's own front panel).</summary>
    public static CockpitLiveSpec Extra() => new()
    {
        Dials = new[]
        {
            AirspeedKt, Altimeter, AttitudeBank, GMeter, Tachometer, Oil("Oil_pressure"),
            Manifold("Oil_temperature"), FuelQuantity, Compass,
            // Fuel pressure and the voltmeter stay painted: nothing in the sim moves them.
        },
        Hide = new[]
        {
            "Needle_ASI_R", "Needle_Alt100_R", "Needle_Alt1000_R", "Needle_G_R", "Needle_GMax_R", "Needle_GMin_R",
            "Needle_MP_R", "Needle_VSI_R", "Needle_CHT_R", "Needle_Fuel_R", "Needle_OilP_R", "Needle_OilT_R",
            "Needle_Tach_R", "TC_Symbol_R", "TC_Ball_R",
        },
    };

    /// <summary>By the placed own model's name (AirframeModels.PlacedOwnName: "&lt;model&gt;-own").</summary>
    public static CockpitLiveSpec For(string ownModelName) => ownModelName switch
    {
        "pitts-s2b-like-own" => Pitts(),
        "extra-300-like-own" => Extra(),
        _ => new CockpitLiveSpec(),
    };

    // ---- the motion (degrees; the Bridge turns them about the aeroplane's own axes) ----

    /// <summary>Stick travel: 18° aft, 12° forward, 18° each side — the EA 300 and Great Lakes rigs'.</summary>
    public const double StickAftDeg = 18.0, StickFwdDeg = 12.0, StickRollDeg = 18.0;
    /// <summary>Pedal travel about the floor hinge.</summary>
    public const double PedalDeg = 15.0;
    /// <summary>Throttle lever: 25° aft of upright closed, 25° forward open (50° of travel, the rigs' figure).</summary>
    public const double ThrottleClosedDeg = -25.0, ThrottleOpenDeg = 25.0;

    /// <summary>The stick's tilt, + = top FORWARD (nose down), from the rig's elevator (+1 = stick aft).</summary>
    public static double StickPitchFwdDeg(in RigInputs r) =>
        r.Elevator >= 0.0 ? -Math.Min(1.0, r.Elevator) * StickAftDeg : Math.Min(1.0, -r.Elevator) * StickFwdDeg;

    /// <summary>The stick's lean, + = top to the pilot's RIGHT, from the rig's aileron (+1 = roll right).</summary>
    public static double StickRightDeg(in RigInputs r) => Math.Clamp(r.Aileron, -1.0, 1.0) * StickRollDeg;

    /// <summary>A pedal's tilt about its floor hinge, + = top FORWARD. Right rudder pushes the right pedal
    /// forward and lets the left one come back.</summary>
    public static double PedalFwdDeg(in RigInputs r, bool right) =>
        (right ? 1.0 : -1.0) * Math.Clamp(r.Rudder, -1.0, 1.0) * PedalDeg;

    /// <summary>The throttle lever, + = top FORWARD (open).</summary>
    public static double ThrottleFwdDeg(in RigInputs r) =>
        ThrottleClosedDeg + (ThrottleOpenDeg - ThrottleClosedDeg) * Math.Clamp(r.Throttle, 0.0, 1.0);
}
