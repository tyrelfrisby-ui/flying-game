using System;
using System.Collections.Generic;

namespace FlyingGame.Core.Instruments;

/// <summary>The shared instruments (Ty 2026-11-05 06:53: "we don't need 6 different coordinators ... we just need one and
/// it gets used 6 times"). One GLB each, built by tools/own_models/instruments/ and loaded from
/// Resources/Models/instruments/&lt;<see cref="InstrumentDesign.Resource"/>&gt;.</summary>
public enum InstrumentKind { TurnCoordinator, Airspeed, Altimeter, VerticalSpeed, Attitude, Heading, GMeter }

/// <summary>One moving part of a library instrument: a node of its GLB whose origin is the dial centre.</summary>
public sealed class InstrumentPart
{
    /// <summary>The node in the instrument's GLB.</summary>
    public readonly string Node;
    /// <summary>The <see cref="RigInputs"/> parameter that drives it.</summary>
    public readonly string Param;
    /// <summary>false: degrees CLOCKWISE AS THE PILOT SEES THE DIAL, about the face normal. true: face radii along the
    /// face's own up (the attitude indicator's pitch card).</summary>
    public readonly bool Slide;
    private readonly double[] _p, _v;
    /// <summary>The map runs on past its ends (the altimeter's hands go round and round).</summary>
    public readonly bool Unbounded;

    public InstrumentPart(string node, string param, double[] mapParam, double[] mapValue, bool slide = false, bool unbounded = false)
    {
        if (mapParam is null || mapValue is null || mapParam.Length != mapValue.Length || mapParam.Length < 2)
            throw new ArgumentException($"instrument part '{node}': the map needs two or more (param, value) pairs");
        for (int i = 1; i < mapParam.Length; i++)
            if (mapParam[i] <= mapParam[i - 1]) throw new ArgumentException($"instrument part '{node}': the map's params must ascend");
        Node = node; Param = param; _p = mapParam; _v = mapValue; Slide = slide; Unbounded = unbounded;
    }

    /// <summary>The same part read from another parameter (an aeroplane whose gyro instruments have their own model:
    /// the 172's <c>tc_needle</c> in rates, <c>ai_bank_deg</c>, <c>dg_card_deg</c>).</summary>
    public InstrumentPart With(string param, double[] mapParam, double[] mapValue) => new(Node, param, mapParam, mapValue, Slide, Unbounded);

    public IReadOnlyList<double> MapParam => _p;
    public IReadOnlyList<double> MapValue => _v;

    /// <summary>Degrees (or face radii) for a reading: piecewise linear, clamped at the ends unless <see cref="Unbounded"/>.
    /// NaN reads as the map's first value.</summary>
    public double Value(double x)
    {
        int n = _p.Length;
        if (double.IsNaN(x)) return _v[0];
        int i;
        if (x <= _p[0]) { if (!Unbounded) return _v[0]; i = 0; }
        else if (x >= _p[n - 1]) { if (!Unbounded) return _v[n - 1]; i = n - 2; }
        else { i = 0; while (i < n - 2 && x > _p[i + 1]) i++; }
        return _v[i] + (_v[i + 1] - _v[i]) * (x - _p[i]) / (_p[i + 1] - _p[i]);
    }
}

/// <summary>A library instrument: which GLB, which node is its face, and its moving parts.</summary>
public sealed class InstrumentDesign
{
    public readonly InstrumentKind Kind;
    /// <summary>Resources path under Models/instruments/.</summary>
    public readonly string Resource;
    /// <summary>The root node of the GLB (the instrument's own name).</summary>
    public readonly string Root;
    /// <summary>The face disc node; an aeroplane with its own markings gets its texture swapped
    /// (<see cref="InstrumentMount.FaceArt"/>).</summary>
    public readonly string FaceNode;
    /// <summary>The bezel ring node, switched off on a panel that brings its own bezel.</summary>
    public readonly string BezelNode;
    public readonly IReadOnlyList<InstrumentPart> Parts;

    public InstrumentDesign(InstrumentKind kind, string resource, string root, string prefix, IReadOnlyList<InstrumentPart> parts)
    {
        Kind = kind; Resource = resource; Root = root; FaceNode = prefix + "_Face"; BezelNode = prefix + "_Bezel"; Parts = parts;
    }
}

/// <summary>Where one aeroplane has one library instrument. Coordinates are the model's own glTF frame, metres, read
/// off the GLB (tools/own_models/instruments/measure_mounts.py prints them; the numbers are copied here by hand).</summary>
public sealed class InstrumentMount
{
    /// <summary>"TC", "ASI_R", ... unique per model.</summary>
    public readonly string Slot;
    public readonly InstrumentKind Kind;
    /// <summary>The front seat's panel (the shared state of the Kollsman popup and the card decide by seat).</summary>
    public readonly bool Front;
    /// <summary>Dial centre (on the old needle pivot), glTF metres.</summary>
    public readonly double Cx, Cy, Cz;
    /// <summary>The face normal toward the pilot, glTF (unit; the old rig's needle axis reversed).</summary>
    public readonly double Nx, Ny, Nz;
    /// <summary>Face radius, metres.</summary>
    public readonly double RadiusM;
    /// <summary>How far the library face sits in front of the centre, metres: just clear of whatever the old model drew
    /// there that cannot be switched off (a face painted into one big interior mesh).</summary>
    public readonly double FaceProudM;
    /// <summary>The library bezel: off where the panel's own bezel stays (the Great Lakes', the 172's cases).</summary>
    public readonly bool Bezel;
    /// <summary>The old model's own nodes of this instrument, switched off (renderers only: the rig may still turn them).</summary>
    public readonly IReadOnlyList<string> Hide;
    /// <summary>Knobs of the old model that stay and must stand in front of the library face (moved toward the pilot).</summary>
    public readonly IReadOnlyList<string> Lift;
    /// <summary>Resources path of the aeroplane's own face art (InstrumentFaces/airspeed-&lt;aircraft&gt;), or "".</summary>
    public readonly string FaceArt;
    /// <summary>Moving parts that read another parameter on this aeroplane, by node.</summary>
    public readonly IReadOnlyDictionary<string, InstrumentPart> Override;

    public InstrumentMount(string slot, InstrumentKind kind, bool front, double cx, double cy, double cz, double nx, double ny, double nz,
                           double radiusM, double faceProudM, bool bezel, string[] hide, string[]? lift = null, string faceArt = "",
                           IReadOnlyDictionary<string, InstrumentPart>? overrides = null)
    {
        Slot = slot; Kind = kind; Front = front; Cx = cx; Cy = cy; Cz = cz;
        double l = Math.Sqrt(nx * nx + ny * ny + nz * nz);
        Nx = nx / l; Ny = ny / l; Nz = nz / l;
        RadiusM = radiusM; FaceProudM = faceProudM; Bezel = bezel;
        Hide = hide ?? Array.Empty<string>(); Lift = lift ?? Array.Empty<string>(); FaceArt = faceArt ?? "";
        Override = overrides ?? new Dictionary<string, InstrumentPart>();
    }

    /// <summary>The part as THIS aeroplane drives it.</summary>
    public InstrumentPart PartFor(InstrumentPart p) => Override.TryGetValue(p.Node, out InstrumentPart? o) ? o : p;
}

/// <summary>The library and the table of where each aeroplane has which instrument. Engine-free and tested
/// (InstrumentLibraryTests); the Bridge (SharedInstruments.cs) places and drives them.</summary>
public static class InstrumentLibrary
{
    // ---- the designs (maps: tools/own_models/instruments/library.py, copied by hand) --------------------------------

    /// <summary>Standard rate (3 deg/s) puts the wings on the 20 deg index; the stops are at 2 rates.</summary>
    public static readonly InstrumentDesign TurnCoordinator = new(InstrumentKind.TurnCoordinator, "turn-coordinator", "TurnCoordinator", "TC", new[]
    {
        new InstrumentPart("TC_Airplane", "turn_rate_dps", new[] { -6.0, 0.0, 6.0 }, new[] { -40.0, 0.0, 40.0 }),
        // +1 = ball to the pilot's RIGHT; the ball sits at 6 o'clock, so right is ANTI-clockwise.
        new InstrumentPart("TC_Ball", "slip_ball", new[] { -1.0, 0.0, 1.0 }, new[] { 18.0, 0.0, -18.0 }),
    });

    /// <summary>One ASI law: 0 at 12 o'clock, the scale floor at 30 deg, the top at 330 deg. The generic GLB is drawn for
    /// 40..200 kt; an aeroplane brings its own units and scale (<see cref="Airspeed"/>).</summary>
    public static readonly InstrumentDesign AirspeedGeneric = new(InstrumentKind.Airspeed, "airspeed", "Airspeed", "ASI", new[]
    {
        AsiNeedle("kt", 40, 200),
    });

    public static readonly InstrumentDesign Altimeter = new(InstrumentKind.Altimeter, "altimeter", "Altimeter", "ALT", new[]
    {
        new InstrumentPart("ALT_Hand10000", "altitude_ft", new[] { 0.0, 100000.0 }, new[] { 0.0, 360.0 }, unbounded: true),
        new InstrumentPart("ALT_Hand1000", "altitude_ft", new[] { 0.0, 10000.0 }, new[] { 0.0, 360.0 }, unbounded: true),
        new InstrumentPart("ALT_Hand100", "altitude_ft", new[] { 0.0, 1000.0 }, new[] { 0.0, 360.0 }, unbounded: true),
    });

    /// <summary>Zero at 9 o'clock; +1,000 ft/min at 12, +2,000 at 80 deg; -1,000 at 6, -2,000 at 100 deg.</summary>
    public static readonly InstrumentDesign VerticalSpeed = new(InstrumentKind.VerticalSpeed, "vertical-speed", "VerticalSpeed", "VSI", new[]
    {
        new InstrumentPart("VSI_Needle", "vsi_fpm", new[] { -2000.0, -1000.0, 0.0, 1000.0, 2000.0 }, new[] { -260.0, -180.0, -90.0, 0.0, 80.0 }),
    });

    /// <summary>The roll card turns opposite the bank; the pitch card slides 0.015 face radii per degree, horizon DOWN for
    /// nose up, to 20 deg.</summary>
    public static readonly InstrumentDesign Attitude = new(InstrumentKind.Attitude, "attitude", "Attitude", "AI", new[]
    {
        new InstrumentPart("AI_Roll", "bank_deg", new[] { -180.0, 180.0 }, new[] { 180.0, -180.0 }),
        new InstrumentPart("AI_Pitch", "pitch_deg", new[] { -20.0, 20.0 }, new[] { 0.3, -0.3 }, slide: true),
    });

    /// <summary>The card turns so the heading sits under the lubber line at 12 o'clock.</summary>
    public static readonly InstrumentDesign Heading = new(InstrumentKind.Heading, "heading", "Heading", "DG", new[]
    {
        new InstrumentPart("DG_Card", "heading_deg", new[] { 0.0, 360.0 }, new[] { 0.0, -360.0 }, unbounded: true),
    });

    /// <summary>Ty 2026-11-05 07:26: "G meter should be standardized too". 0 g at 9 o'clock, 12 deg per g, -10..+10; the max
    /// and min followers (Core GMeterFollowers) ride the same law and stay until reset. An aeroplane gives only its red
    /// lines (its structural limits: face art <see cref="GMeterFace"/>).</summary>
    public static readonly InstrumentDesign GMeter = new(InstrumentKind.GMeter, "g-meter", "GMeter", "GM", new[]
    {
        new InstrumentPart("GM_Min", "g_min_seen", new[] { -10.0, 0.0, 10.0 }, new[] { -210.0, -90.0, 30.0 }),
        new InstrumentPart("GM_Max", "g_max_seen", new[] { -10.0, 0.0, 10.0 }, new[] { -210.0, -90.0, 30.0 }),
        new InstrumentPart("GM_Needle", "g_load", new[] { -10.0, 0.0, 10.0 }, new[] { -210.0, -90.0, 30.0 }),
    });

    /// <summary>The g-meter face with this aeroplane's red lines (configs/aircraft/&lt;id&gt;.json limits.gMax / gMin, drawn
    /// by tools/own_models/instruments/build.py where the config has instruments.gMeter).</summary>
    public static string GMeterFace(string aircraft) => "InstrumentFaces/g-meter-" + aircraft;

    public static IReadOnlyList<InstrumentDesign> Designs { get; } = new[] { TurnCoordinator, AirspeedGeneric, Altimeter, VerticalSpeed, Attitude, Heading, GMeter };

    public static InstrumentDesign Design(InstrumentKind k) => k switch
    {
        InstrumentKind.TurnCoordinator => TurnCoordinator,
        InstrumentKind.Airspeed => AirspeedGeneric,
        InstrumentKind.Altimeter => Altimeter,
        InstrumentKind.VerticalSpeed => VerticalSpeed,
        InstrumentKind.Attitude => Attitude,
        InstrumentKind.GMeter => GMeter,
        _ => Heading,
    };

    /// <summary>The ASI needle for a scale: "mph" reads <c>airspeed_mph</c>, "kt" <c>airspeed_kias</c>.</summary>
    public static InstrumentPart AsiNeedle(string units, double floor, double top) =>
        new("ASI_Needle", units == "mph" ? "airspeed_mph" : "airspeed_kias", new[] { 0.0, floor, top }, new[] { 0.0, 30.0, 330.0 });

    /// <summary>An aeroplane's ASI: the generic instrument with its own face (configs/aircraft/&lt;id&gt;.json
    /// instruments.airspeed, drawn by tools/own_models/instruments/build.py) and its own scale. These numbers are that
    /// block's, copied by hand; InstrumentLibraryTests reads the config and holds the two together.</summary>
    public sealed class AsiScale
    {
        public readonly string Aircraft, Units; public readonly double Floor, Top;
        public AsiScale(string aircraft, string units, double floor, double top) { Aircraft = aircraft; Units = units; Floor = floor; Top = top; }
        public string FaceArt => "InstrumentFaces/airspeed-" + Aircraft;
        public InstrumentPart Needle => AsiNeedle(Units, Floor, Top);
    }

    public static readonly AsiScale C172Asi = new("c172-like", "mph", 40, 200);
    public static readonly AsiScale ExtraAsi = new("extra-300-like", "kt", 40, 260);
    public static readonly AsiScale GreatLakesAsi = new("great-lakes-like", "mph", 40, 200);
    public static readonly AsiScale PittsAsi = new("pitts-s2b-like", "mph", 40, 200);
    public static IReadOnlyList<AsiScale> AsiScales { get; } = new[] { C172Asi, ExtraAsi, GreatLakesAsi, PittsAsi };

    private static IReadOnlyDictionary<string, InstrumentPart> Asi(AsiScale s) => new Dictionary<string, InstrumentPart> { ["ASI_Needle"] = s.Needle };

    private static InstrumentMount AsiMount(string slot, AsiScale s, bool front, double cx, double cy, double cz, double nx, double r, double proud, bool bezel, params string[] hide) =>
        new(slot, InstrumentKind.Airspeed, front, cx, cy, cz, nx, 0, 0, r, proud, bezel, hide, faceArt: s.FaceArt, overrides: Asi(s));

    // ---- the mounts (numbers: tools/own_models/instruments/measure_mounts.py over the GLBs at this commit) -----------

    /// <summary>The 172 (c172_cockpit.py): every instrument's case and knobs stay (bezel off), the face is painted into
    /// C172_Interior_static 1.5 mm behind the pivots with a ball tube 0.4 mm in front of them, so the library face sits
    /// 0.9 mm in front. Its gyro instruments have models of their own (C172Panel): the TC reads <c>tc_needle</c> in rates,
    /// the AI <c>ai_bank_deg</c> / <c>ai_pitch_deg</c>, the DG <c>dg_card_deg</c>. The pilot is at -x in this file.</summary>
    public static IReadOnlyList<InstrumentMount> C172() => new[]
    {
        AsiMount("ASI", C172Asi, false, 0.2959, 0.3230, -0.4230, -1, 0.0390, 0.0009, false, "C172_Airspeed_needle"),
        new InstrumentMount("AI", InstrumentKind.Attitude, false, 0.2959, 0.3230, -0.3210, -1, 0, 0, 0.0390, 0.0009, false,
            new[] { "C172_AI_roll" }, lift: new[] { "C172_AI_cage" },
            overrides: new Dictionary<string, InstrumentPart>
            {
                ["AI_Roll"] = Attitude.Parts[0].With("ai_bank_deg", new[] { -180.0, 180.0 }, new[] { 180.0, -180.0 }),
                ["AI_Pitch"] = Attitude.Parts[1].With("ai_pitch_deg", new[] { -20.0, 20.0 }, new[] { 0.3, -0.3 }),
            }),
        new InstrumentMount("ALT", InstrumentKind.Altimeter, false, 0.2959, 0.3230, -0.2190, -1, 0, 0, 0.0390, 0.0009, false,
            new[] { "C172_Altimeter_needle", "C172_Altimeter_1000", "C172_Altimeter_10000" }, lift: new[] { "C172_Altimeter_baro" }),
        new InstrumentMount("TC", InstrumentKind.TurnCoordinator, false, 0.2959, 0.2180, -0.4230, -1, 0, 0, 0.0390, 0.0009, false,
            new[] { "C172_TC_airplane", "C172_TC_rate_mark", "C172_TC_rate_mark.001", "C172_TC_slip_ball" },
            overrides: new Dictionary<string, InstrumentPart>
            {
                ["TC_Airplane"] = TurnCoordinator.Parts[0].With("tc_needle", new[] { -2.0, 0.0, 2.0 }, new[] { -40.0, 0.0, 40.0 }),
            }),
        new InstrumentMount("DG", InstrumentKind.Heading, false, 0.2959, 0.2180, -0.3210, -1, 0, 0, 0.0390, 0.0009, false,
            new[] { "C172_DG_card", "C172_DG_heading_bug" }, lift: new[] { "C172_DG_sync_adjust", "C172_DG_bug_adjust" },
            overrides: new Dictionary<string, InstrumentPart>
            {
                ["DG_Card"] = Heading.Parts[0].With("dg_card_deg", new[] { 0.0, 360.0 }, new[] { 0.0, -360.0 }),
            }),
        new InstrumentMount("VSI", InstrumentKind.VerticalSpeed, false, 0.2959, 0.2180, -0.2190, -1, 0, 0, 0.0390, 0.0009, false,
            new[] { "C172_VSI_needle" }),
    };

    /// <summary>The Extra (vr_phase3b / extra_cockpit_shared): every instrument is its own Bezel_/Face_/Needle_ nodes, all
    /// switched off; the library brings its bezel. Faces 78.4 mm (rear 3-1/8 in) and 58 mm (front 2-1/4 in).</summary>
    public static IReadOnlyList<InstrumentMount> Extra() => new[]
    {
        AsiMount("ASI_R", ExtraAsi, false, 1.1758, 0.3160, 0.1150, 1, 0.0392, 0.0, true, "Bezel_P3b_ASI_R", "Face_P3b_ASI_R", "Needle_ASI_R"),
        new InstrumentMount("ALT_R", InstrumentKind.Altimeter, false, 1.1758, 0.3160, -0.1150, 1, 0, 0, 0.0392, 0.0, true,
            new[] { "Bezel_P3b_Altimeter_R", "Face_P3b_Altimeter_R", "Needle_Alt100_R", "Needle_Alt1000_R" }),
        new InstrumentMount("TC_R", InstrumentKind.TurnCoordinator, false, 1.1758, 0.2255, -0.1150, 1, 0, 0, 0.0392, 0.0, true,
            new[] { "Bezel_P3b_TC_R", "Face_P3b_TC_R", "Needle_TC_R", "Ball_TC_R" }),
        new InstrumentMount("G_R", InstrumentKind.GMeter, false, 1.1758, 0.2255, 0.1150, 1, 0, 0, 0.0392, 0.0, true,
            new[] { "Bezel_P3b_G_R", "Face_P3b_G_R", "Needle_G_R", "Needle_GMax_R", "Needle_GMin_R" }, faceArt: GMeterFace("extra-300-like")),
        new InstrumentMount("G_F", InstrumentKind.GMeter, true, 0.1508, 0.3000, 0.0000, 1, 0, 0, 0.0290, 0.0, true,
            new[] { "Bezel_P3b_G_F", "Face_P3b_G_F", "Needle_G_F", "Needle_GMax_F", "Needle_GMin_F" }, faceArt: GMeterFace("extra-300-like")),
        AsiMount("ASI_F", ExtraAsi, true, 0.1508, 0.3000, 0.0750, 1, 0.0290, 0.0, true, "Bezel_P3b_ASI_F", "Face_P3b_ASI_F", "Needle_ASI_F"),
        new InstrumentMount("ALT_F", InstrumentKind.Altimeter, true, 0.1508, 0.3000, -0.0750, 1, 0, 0, 0.0290, 0.0, true,
            new[] { "Bezel_P3b_Altimeter_F", "Face_P3b_Altimeter_F", "Needle_Alt100_F", "Needle_Alt1000_F" }),
    };

    /// <summary>The Great Lakes (gl_cockpit.py): faces and bezels are in Cockpit_Interior, so the panel's bezels stay. Each
    /// old dial is a closed DISC 3 mm thick whose pilot-side face is 3.0 mm in front of the pivot (measured on the Dial_*
    /// triangles; a vertex sample missed it, and the first library faces sat behind it), so the library face sits 3.5 mm
    /// in front: clear of it, inside the bezel's lip (6.5 mm), under the panel's own Kollsman window (3.8 mm).
    /// Faces 0.9 x the case radius: 3-1/8 in cases 35.7 mm (front) / 36 mm (rear), the front TC a 2-1/4 in case.</summary>
    public static IReadOnlyList<InstrumentMount> GreatLakes() => new[]
    {
        AsiMount("ASI_F", GreatLakesAsi, true, 0.9680, 0.0300, 0.1113, 1, 0.03573, 0.0035, false, "Needle_ASI_F"),
        new InstrumentMount("ALT_F", InstrumentKind.Altimeter, true, 0.9680, 0.0300, 0.2336, 1, 0, 0, 0.03573, 0.0035, false,
            new[] { "Needle_Alt100_F", "Needle_Alt1000_F" }),
        new InstrumentMount("TC_F", InstrumentKind.TurnCoordinator, true, 0.9680, 0.0300, 0.0073, 1, 0, 0, 0.02574, 0.0035, false,
            new[] { "TC_Symbol_F", "TC_Ball_F" }),
        AsiMount("ASI_R", GreatLakesAsi, false, 1.6380, 0.0590, 0.1180, 1, 0.0360, 0.0035, false, "Needle_ASI_R"),
        new InstrumentMount("ALT_R", InstrumentKind.Altimeter, false, 1.6380, 0.0590, 0.2090, 1, 0, 0, 0.0360, 0.0035, false,
            new[] { "Needle_Alt100_R", "Needle_Alt1000_R" }),
        new InstrumentMount("TC_R", InstrumentKind.TurnCoordinator, false, 1.6380, -0.0320, 0.2090, 1, 0, 0, 0.0360, 0.0035, false,
            new[] { "TC_Symbol_R", "TC_Ball_R" }),
        // The panel's own reset knob (G_meter_Reset, lower left) stays.
        new InstrumentMount("G_R", InstrumentKind.GMeter, false, 1.6380, -0.0320, 0.1180, 1, 0, 0, 0.0360, 0.0035, false,
            new[] { "Needle_G_R", "Needle_GMax_R", "Needle_GMin_R" }, faceArt: GMeterFace("great-lakes-like")),
    };

    /// <summary>The Pitts (rear_panel.py / pitts_cockpit_v2): painted RearPanel_/FrontPanel_ faces with their bezels,
    /// both switched off; the centre is the face's own. Its Kollsman windows and knobs stay (the common lane's).</summary>
    public static IReadOnlyList<InstrumentMount> Pitts() => new[]
    {
        AsiMount("ASI_R", PittsAsi, false, 0.8180, -0.0100, 0.1600, 1, 0.0419, 0.0003, true, "RearPanel_Airspeed_Face", "RearPanel_Airspeed_Bezel"),
        new InstrumentMount("ALT_R", InstrumentKind.Altimeter, false, 0.8180, 0.1120, 0.1600, 1, 0, 0, 0.0370, 0.0003, true,
            new[] { "RearPanel_Altimeter_Face", "RearPanel_Altimeter_Bezel" }),
        AsiMount("ASI_F", PittsAsi, true, 0.1635, -0.0140, 0.2000, 1, 0.0374, 0.0003, true, "FrontPanel_Airspeed_Face", "FrontPanel_Airspeed_Bezel"),
        new InstrumentMount("ALT_F", InstrumentKind.Altimeter, true, 0.1635, 0.1040, 0.1250, 1, 0, 0, 0.0330, 0.0003, true,
            new[] { "FrontPanel_Altimeter_Face", "FrontPanel_Altimeter_Bezel" }),
        // The panel's own reset knobs ([FrontPanel_]G_meter_Reset) stay: they stand 12 mm proud, in front of the bezel.
        new InstrumentMount("G_R", InstrumentKind.GMeter, false, 0.8180, -0.0520, 0.2850, 1, 0, 0, 0.0419, 0.0003, true,
            new[] { "RearPanel_G_meter_Face", "RearPanel_G_meter_Bezel" }, faceArt: GMeterFace("pitts-s2b-like")),
        new InstrumentMount("G_F", InstrumentKind.GMeter, true, 0.1635, 0.1040, -0.1250, 1, 0, 0, 0.0374, 0.0003, true,
            new[] { "FrontPanel_G_meter_Face", "FrontPanel_G_meter_Bezel" }, faceArt: GMeterFace("pitts-s2b-like")),
    };

    /// <summary>By the placed own model's name (AirframeModels.PlacedOwnName: "&lt;model&gt;-own"). Every livery of a model
    /// shares its node names. Empty for an aeroplane whose instruments are still its own: the J-3's are PERIOD instruments
    /// (2-pointer altimeter, mph ASI, no gyros; <see cref="PeriodVariants"/>), and the gliders' are next.</summary>
    public static IReadOnlyList<InstrumentMount> MountsFor(string? ownModelName) => ownModelName switch
    {
        "c172-like-own" => C172(),
        "extra-300-like-own" => Extra(),
        "great-lakes-like-own" => GreatLakes(),
        "pitts-s2b-like-own" => Pitts(),
        _ => Array.Empty<InstrumentMount>(),
    };

    public static IReadOnlyList<string> ModelsWithMounts { get; } = new[] { "c172-like-own", "extra-300-like-own", "great-lakes-like-own", "pitts-s2b-like-own" };

    /// <summary>Builders that still draw an instrument the library has, and why. Every entry is a period-correct face or
    /// an aeroplane not yet moved over; nothing else may draw a library instrument (InstrumentLibraryTests, the
    /// builder scan). Key: builder file under tools/own_models; value: what it may still draw.</summary>
    public static IReadOnlyDictionary<string, string> PeriodVariants { get; } = new Dictionary<string, string>
    {
        ["build_j3_cub.py"] = "J-3 Cub PERIOD panel: mph ASI, 2-pointer altimeter without Kollsman (Ty: period-correct where needed)",
        ["j3_textures.py"] = "J-3 Cub PERIOD dial faces",
        ["sgs233_cockpit.py"] = "SGS 2-33 glider panel (mph ASI, variometer): period-style faces, not yet moved to the library",
        ["make_sgs_dials.py"] = "SGS 2-33 dial faces (see sgs233_cockpit.py)",
        ["build_sgs233.py"] = "SGS 2-33 (see sgs233_cockpit.py)",
        ["grob103_cockpit.py"] = "Grob 103 glider panel (kt ASI, variometer, km/h): not yet moved to the library",
        ["make_grob103_dials.py"] = "Grob 103 dial faces (see grob103_cockpit.py)",
        ["build_grob103.py"] = "Grob 103 (see grob103_cockpit.py)",
    };
}
