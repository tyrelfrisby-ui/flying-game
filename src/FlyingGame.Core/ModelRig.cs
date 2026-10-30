namespace FlyingGame.Core;

/// <summary>
/// A model's own moving parts, as its build script wrote them down (docs/models/&lt;model&gt;-rig.json, mirrored
/// into StreamingAssets/rigs by the editor's ConfigSync). Every rigged node in the GLB has its origin ON its
/// pivot, so driving it is one of two things:
///   kind "rot"  — rotate the node's rest pose by <c>Value(param)</c> degrees about <see cref="Ax"/>..<see cref="Az"/>;
///   kind "move" — slide it <c>Value(param)</c> metres along the same axis.
/// The axis is a direction in the MODEL's own glTF frame at rest; whoever applies it converts to the engine's
/// frame the same way the importer converted the mesh (the Unity side does that in AirframeRig).
///
/// This half is deliberately engine-free: the piecewise-linear map, the wrap, the run past the ends and the
/// lever detents are the parts that can be wrong in a way no render would show, so they are testable on their
/// own (tools/FlightTests/ModelRigTests.cs).
/// </summary>
public sealed class RigNode
{
    public readonly string Node;
    /// <summary>The node this one hangs under in the GLB (already parented there) — "" for a top-level node.
    /// Kept so a check can confirm the file and the imported hierarchy still agree.</summary>
    public readonly string Parent;
    /// <summary>Which of <see cref="RigInputs"/> drives it.</summary>
    public readonly string Param;
    /// <summary>true = slide along the axis (metres); false = turn about it (degrees).</summary>
    public readonly bool Move;
    /// <summary>Axis, unit, in the model's glTF frame at rest.</summary>
    public readonly double Ax, Ay, Az;
    /// <summary>The pivot the node's origin sits on, model glTF frame — documentation, and what a check measures against.</summary>
    public readonly double Px, Py, Pz;
    /// <summary>The param value the model was DRAWN at (so a rig whose rest pose isn't neutral can be told).</summary>
    public readonly double ModelAt;
    public readonly string Desc;

    private readonly double[] _param, _value;   // the map, _param ascending
    /// <summary>&gt; 0: the param runs round and round with this period (the compass card's 360°).</summary>
    public readonly double WrapPeriod;
    /// <summary>Run past the ends of the map on the end segments' slopes instead of clamping (the altimeter hands).</summary>
    public readonly bool Extrapolate;
    /// <summary>Param positions the control notches into (the trim quadrant's four), or null for a free one.</summary>
    public readonly double[]? Detents;

    public RigNode(string node, string parent, string param, bool move,
                   double ax, double ay, double az, double px, double py, double pz,
                   double[] mapParam, double[] mapValue,
                   double wrapPeriod = 0.0, bool extrapolate = false, double[]? detents = null,
                   double modelAt = 0.0, string desc = "")
    {
        if (mapParam is null || mapValue is null || mapParam.Length != mapValue.Length || mapParam.Length == 0)
            throw new ArgumentException($"rig node '{node}': the map needs the same number of params and values, and at least one of each.");
        for (int i = 1; i < mapParam.Length; i++)
            if (mapParam[i] <= mapParam[i - 1])
                throw new ArgumentException($"rig node '{node}': the map's params must ascend ({mapParam[i - 1]} then {mapParam[i]}).");
        Node = node; Parent = parent ?? ""; Param = param; Move = move;
        Ax = ax; Ay = ay; Az = az; Px = px; Py = py; Pz = pz;
        _param = mapParam; _value = mapValue;
        WrapPeriod = wrapPeriod; Extrapolate = extrapolate;
        Detents = detents is { Length: > 0 } ? detents : null;
        ModelAt = modelAt; Desc = desc ?? "";
    }

    /// <summary>Degrees (rot) or metres (move) for this param value.</summary>
    public double Value(double param) => Interp(Snap(param));

    /// <summary>A notched control sits in a notch: the nearest one. A free control is returned unchanged.</summary>
    public double Snap(double param)
    {
        if (Detents is null) return param;
        double best = Detents[0], bestD = Math.Abs(param - best);
        for (int i = 1; i < Detents.Length; i++)
        {
            double d = Math.Abs(param - Detents[i]);
            if (d < bestD) { bestD = d; best = Detents[i]; }
        }
        return best;
    }

    private double Interp(double param)
    {
        double[] p = _param, v = _value;
        int n = p.Length;
        if (n == 1) return v[0];
        if (WrapPeriod > 0.0) param = p[0] + Mod(param - p[0], WrapPeriod);
        if (param <= p[0])
            return Extrapolate ? v[0] + (v[1] - v[0]) * (param - p[0]) / (p[1] - p[0]) : v[0];
        if (param >= p[n - 1])
            return Extrapolate ? v[n - 1] + (v[n - 1] - v[n - 2]) * (param - p[n - 1]) / (p[n - 1] - p[n - 2]) : v[n - 1];
        for (int i = 1; i < n; i++)
            if (param <= p[i]) return v[i - 1] + (v[i] - v[i - 1]) * (param - p[i - 1]) / (p[i] - p[i - 1]);
        return v[n - 1];
    }

    private static double Mod(double a, double m) { double r = a % m; return r < 0 ? r + m : r; }

    /// <summary>The axis to TURN about, in the frame the Unity importer leaves the model in.
    ///
    /// glTFast imports glTF by negating x (NodeExtension.GetTransform in com.unity.cloud.gltfast: a
    /// translation (x,y,z) lands at (-x,y,z), a rotation quaternion (x,y,z,w) at (x,-y,-z,w)). Reading that
    /// quaternion back as an axis and angle, turning by θ about the glTF axis (ax,ay,az) is turning by the
    /// SAME θ about (ax,-ay,-az). The reflection is improper, which is exactly why the two conversions here
    /// differ — getting them crossed rakes the rudder hinge the wrong way and slides every pedal backwards,
    /// and neither shows in a still.</summary>
    public (double X, double Y, double Z) TurnAxisUnity => (Ax, -Ay, -Az);

    /// <summary>The direction to SLIDE along, same frame. A direction to move in converts like a position.</summary>
    public (double X, double Y, double Z) SlideAxisUnity => (-Ax, Ay, Az);

    /// <summary>The map, for a check or a report.</summary>
    public (double Param, double Value) MapPoint(int i) => (_param[i], _value[i]);
    public int MapPoints => _param.Length;
}

/// <summary>What the sim hands the rig each frame: the control positions the AERO MODEL is using (not the raw
/// stick, so the drawn surfaces and the cockpit agree with the aeroplane) plus the instrument readings.</summary>
public struct RigInputs
{
    /// <summary>-1..+1, +1 = roll right.</summary>
    public double Aileron;
    /// <summary>-1..+1, +1 = stick AFT (nose up). The sim's elevator deflection is positive trailing-edge DOWN,
    /// so this is its negative — see tools/FlightTests/ControlPolarityTests.cs.</summary>
    public double Elevator;
    /// <summary>-1..+1, +1 = right pedal.</summary>
    public double Rudder;
    /// <summary>0..1 dive-brake handle.</summary>
    public double Spoiler;
    /// <summary>-1..+1, +1 = aft / nose up.</summary>
    public double Trim;
    /// <summary>0..1 release-knob pull.</summary>
    public double TowRelease;
    public double AirspeedMph;
    public double AltitudeFt;
    /// <summary>Total-energy rate, knots, + = climbing.</summary>
    public double VarioKt;
    /// <summary>Magnetic heading, degrees.</summary>
    public double HeadingDeg;
    /// <summary>Sideslip β, degrees, + = relative wind from the right.</summary>
    public double SideslipDeg;

    /// <summary>The CONTROL half, from the sim's deflections and the type's travel limits. The instrument
    /// readings are the caller's to fill in — a remote aeroplane's panel is nobody's to read.
    ///
    /// The one sign that has to be got right in exactly one place: the sim's elevator deflection is positive
    /// trailing-edge DOWN (ControlPolarityTests), and the rig's elevator is positive STICK AFT.</summary>
    public static RigInputs FromDeflections(double aileronRad, double elevatorRad, double rudderRad, double spoilerFraction,
                                            double aileronMaxRad, double elevatorMaxRad, double rudderMaxRad)
    {
        static double N(double v, double max) => max > 1e-6 ? Math.Clamp(v / max, -1.0, 1.0) : 0.0;
        return new RigInputs
        {
            Aileron = N(aileronRad, aileronMaxRad),
            Elevator = N(-elevatorRad, elevatorMaxRad),
            Rudder = N(rudderRad, rudderMaxRad),
            Spoiler = Math.Clamp(spoilerFraction, 0.0, 1.0),
        };
    }

    /// <summary>Take only the stick-and-handle part of <paramref name="controls"/>, leaving the instruments
    /// reading whatever they were.</summary>
    public void TakeControlsFrom(in RigInputs controls)
    {
        Aileron = controls.Aileron; Elevator = controls.Elevator;
        Rudder = controls.Rudder; Spoiler = controls.Spoiler;
    }

    /// <summary>false for a param this sim cannot supply — so a rig that grows one is reported, not silently still.</summary>
    public bool TryGet(string param, out double value)
    {
        switch (param)
        {
            case "aileron": value = Aileron; return true;
            case "elevator": value = Elevator; return true;
            case "rudder": value = Rudder; return true;
            case "spoiler": value = Spoiler; return true;
            case "trim": value = Trim; return true;
            case "tow_release": value = TowRelease; return true;
            case "airspeed_mph": value = AirspeedMph; return true;
            case "altitude_ft": value = AltitudeFt; return true;
            case "vario_kt": value = VarioKt; return true;
            case "heading_deg": value = HeadingDeg; return true;
            case "sideslip_deg": value = SideslipDeg; return true;
            default: value = 0.0; return false;
        }
    }
}

/// <summary>One model's rig: the nodes, in the order the file lists them (parents before children).</summary>
public sealed class ModelRig
{
    public readonly string Model;
    public readonly IReadOnlyList<RigNode> Nodes;

    public ModelRig(string model, IReadOnlyList<RigNode> nodes)
    {
        Model = model ?? "";
        Nodes = nodes ?? Array.Empty<RigNode>();
    }

    public RigNode? Find(string node)
    {
        foreach (RigNode n in Nodes) if (n.Node == node) return n;
        return null;
    }

    /// <summary>The params this rig asks for, each once.</summary>
    public List<string> Params()
    {
        var seen = new List<string>();
        foreach (RigNode n in Nodes) if (!seen.Contains(n.Param)) seen.Add(n.Param);
        return seen;
    }
}
