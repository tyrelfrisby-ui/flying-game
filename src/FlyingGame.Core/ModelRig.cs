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
    /// <summary>The param that THROWS THIS NODE AWAY when it reaches 1 (the Extra's canopy: `canopy_jettison`),
    /// or "" for a node that stays bolted on. The node and everything under it leaves with the airframe's
    /// velocity and does not come back until the aeroplane is reset.</summary>
    public readonly string JettisonParam;

    public RigNode(string node, string parent, string param, bool move,
                   double ax, double ay, double az, double px, double py, double pz,
                   double[] mapParam, double[] mapValue,
                   double wrapPeriod = 0.0, bool extrapolate = false, double[]? detents = null,
                   double modelAt = 0.0, string desc = "", string jettisonParam = "")
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
        ModelAt = modelAt; Desc = desc ?? ""; JettisonParam = jettisonParam ?? "";
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
/// stick, so the drawn surfaces and the cockpit agree with the aeroplane) plus the instrument readings.
///
/// One struct for every model's rig, because a param means the same thing in every cockpit: a rig that asks for
/// `rpm` gets the same number whichever aeroplane it is bolted to. <see cref="TryGet"/> is the whole contract —
/// it returns false for a param this sim has no value for, so a rig that grows one is REPORTED rather than
/// silently still. Fields the sim cannot compute (an Extra has oil temperature; the sim has no thermal model)
/// are filled by the presentation layer and marked as such here.</summary>
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

    // ---- the rest of a cockpit: everything an aerobatic panel asks for -------------------------------------
    // Added for the Extra 300 (docs/models/ea300-own-rig.json, 33 params against the 2-33's 11). The 2-33 rig
    // asks for none of these and is unaffected: a param no rig names costs one unread field.

    /// <summary>0..1 toe brakes, 1 = pressed.</summary>
    public double BrakeLeft, BrakeRight;
    /// <summary>0..1 throttle, 0 = idle / lever aft.</summary>
    public double Throttle;
    /// <summary>0..1 push-pull verniers, 1 = IN (full forward / fine / rich).</summary>
    public double PropLever, Mixture;
    /// <summary>Fuel selector detent (the Extra's 0 / 1 / 2).</summary>
    public double FuelSelector;
    /// <summary>Ignition key position: 0 OFF, 1 R, 2 L, 3 BOTH, 4 START.</summary>
    public double Magneto;
    /// <summary>Battery master, 0 or 1.</summary>
    public double Master;

    /// <summary>0..1 canopy, 1 = fully open.</summary>
    public double CanopyOpen;
    /// <summary>0 or 1, 1 = locked.</summary>
    public double CanopyLock;
    /// <summary>0..1 pull of the emergency release.</summary>
    public double CanopyJettison;

    /// <summary>Indicated airspeed in KNOTS (the Extra's ASI is in kt; the 2-33's in mph — both are here
    /// because the dial art is drawn in one or the other and converting in the rig would hide it).</summary>
    public double AirspeedKias;
    /// <summary>Vertical speed, feet per minute, + = climbing.</summary>
    public double VsiFpm;
    public double Rpm;
    public double ManifoldInHg;
    public double OilPressPsi, OilTempF, ChtF;
    /// <summary>Fuel remaining in the tank the gauge is plumbed to, 0..1.</summary>
    public double FuelCentreFrac;
    /// <summary>Rate of turn, degrees per second, + = turning right (3 = standard rate).</summary>
    public double TurnRateDps;
    /// <summary>Inclinometer ball, -1..+1, + = ball to the pilot's RIGHT.</summary>
    public double SlipBall;
    /// <summary>Normal load factor, g, + = positive.</summary>
    public double GLoad;
    /// <summary>The accelerometer's tell-tales: the running maximum and minimum since they were last reset.</summary>
    public double GMaxSeen, GMinSeen;

    /// <summary>COM, MHz: the tuned (active) and the standby frequency.</summary>
    public double ComFreqMhz, ComStandbyMhz;
    /// <summary>0..1 radio volume (0 = off).</summary>
    public double ComVolume;
    /// <summary>Transponder code as a four-digit octal NUMBER (1200 is 1200), and its mode
    /// (0 OFF, 1 SBY, 2 ON, 3 ALT).</summary>
    public double XpdrSquawk, XpdrMode;
    /// <summary>Free-running count of transponder knob clicks (the knob turns on, never back to a stop).</summary>
    public double XpdrKnobClicks;

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

    /// <summary>A cockpit at rest on the ramp with the aeroplane ready to fly: the switches and levers where a
    /// pilot leaves them, so a panel nobody is reading is not drawn with the master off and the fuel shut. The
    /// readings stay zero — a remote aeroplane's instruments are nobody's to read.</summary>
    public static RigInputs Parked()
    {
        return new RigInputs
        {
            PropLever = 1.0,        // fine pitch
            Mixture = 1.0,          // rich
            FuelSelector = 1.0,     // on
            Magneto = 3.0,          // BOTH
            Master = 1.0,
            CanopyLock = 1.0,       // closed and locked
            OilPressPsi = 0.0,
            OilTempF = 100.0,       // the bottom of the dial, as drawn
            ChtF = 200.0,
            FuelCentreFrac = 1.0,
            ComFreqMhz = 122.8, ComStandbyMhz = 121.5, ComVolume = 0.5,
            XpdrSquawk = 1200, XpdrMode = 1,
        };
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
            case "brake_left": value = BrakeLeft; return true;
            case "brake_right": value = BrakeRight; return true;
            case "throttle": value = Throttle; return true;
            case "prop": value = PropLever; return true;
            case "mixture": value = Mixture; return true;
            case "fuel_selector": value = FuelSelector; return true;
            case "magneto": value = Magneto; return true;
            case "master": value = Master; return true;
            case "canopy_open": value = CanopyOpen; return true;
            case "canopy_lock": value = CanopyLock; return true;
            case "canopy_jettison": value = CanopyJettison; return true;
            case "airspeed_kias": value = AirspeedKias; return true;
            case "vsi_fpm": value = VsiFpm; return true;
            case "rpm": value = Rpm; return true;
            case "manifold_inhg": value = ManifoldInHg; return true;
            case "oil_press_psi": value = OilPressPsi; return true;
            case "oil_temp_f": value = OilTempF; return true;
            case "cht_f": value = ChtF; return true;
            case "fuel_centre_frac": value = FuelCentreFrac; return true;
            case "turn_rate_dps": value = TurnRateDps; return true;
            case "slip_ball": value = SlipBall; return true;
            case "g_load": value = GLoad; return true;
            case "g_max_seen": value = GMaxSeen; return true;
            case "g_min_seen": value = GMinSeen; return true;
            case "com_freq_mhz": value = ComFreqMhz; return true;
            case "com_standby_mhz": value = ComStandbyMhz; return true;
            case "com_volume": value = ComVolume; return true;
            case "xpdr_squawk": value = XpdrSquawk; return true;
            case "xpdr_mode": value = XpdrMode; return true;
            case "xpdr_knob_clicks": value = XpdrKnobClicks; return true;
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
