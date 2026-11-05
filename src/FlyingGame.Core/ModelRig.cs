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
    /// <summary>The rudder-linked nose wheel's yaw, degrees, + = turned RIGHT: the angle LandingGear steers the tyre by
    /// (rudder x the type's steering lock), so the drawn fork and the sim's tyre cannot disagree. 0 without one.</summary>
    public double NoseWheelSteerDeg;
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
    /// <summary>Fuel shut-off valve (the Great Lakes' and the Extra's red lever): 1 ON, 0 OFF. One param for both
    /// cockpits' levers, which are on one shaft: moving either moves both.</summary>
    public double FuelValve;
    /// <summary>The Pitts' cowl flap 0 CLOSED .. 1 OPEN, and its blue prop lever's lock: 0 down (LOCKED) .. 1 horizontal
    /// (released, free to slide).</summary>
    public double CowlFlap, PropLock;
    /// <summary>Ignition key position: 0 OFF, 1 R, 2 L, 3 BOTH, 4 START.</summary>
    public double Magneto;
    /// <summary>The two magneto toggles where a type has them instead of a key (the Great Lakes, Ty 2026-11-05): 0 OFF, 1 ON.</summary>
    public double MagLeft, MagRight;
    /// <summary>J-3 primer plunger: 0 in and locked .. 1 pulled out 50 mm. No priming system reads it yet; it stays 0
    /// (in) unless a hand pulls the knob (Ty 2026-11-02, v18).</summary>
    public double Primer;
    /// <summary>Battery master, 0 or 1.</summary>
    public double Master;
    /// <summary>Alternator field switch and electric boost pump, 0 or 1 (FlyingGame.Core.Engine.ElectricalSystem).</summary>
    public double Alternator, FuelPump;

    /// <summary>0..1 canopy, 1 = fully open (the Pitts v2: its swing alone, 0..175°).</summary>
    public double CanopyOpen;
    /// <summary>The Pitts' slide along its track, 0..1 of the whole track (CanopyDynamics.Slide01, v2 2026-11-05).</summary>
    public double CanopySlide;
    /// <summary>The seat harness (CONTRACT.md phase 2): 0 latched .. 1 released; the straps fall open on it.</summary>
    public double HarnessLatch;
    /// <summary>0 or 1, 1 = locked.</summary>
    public double CanopyLock;
    /// <summary>0..1 pull of the emergency release.</summary>
    public double CanopyJettison;
    /// <summary>The canopy latch, 0 locked .. 1 fully open (the Pitts' ball slid left) / squeezed (the Extra's two
    /// handles) — CONTRACT.md `canopy_latch`, driven by the hand that holds it (Core/Vr/CanopyLatch.cs).</summary>
    public double CanopyLatch;

    /// <summary>The `canopy_latch` a cockpit draws: the hand's own latch where there is one (0 pinned .. 1 released), and
    /// otherwise Codex's visual contract, the inverse of the lock (0 locked, 1 released).</summary>
    public static double CanopyLatchFor(bool locked, double? handLatch01) =>
        handLatch01 ?? (locked ? 0.0 : 1.0);

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
    /// <summary>The Great Lakes' top-wing tank, 0 empty .. 1 full, what its glass sight gauge's cork float reads (Ty
    /// 2026-11-05): <see cref="TankFraction"/> of the loading's "topWing" tank.</summary>
    public double FuelTopWingFrac;

    /// <summary>One tank's fill, 0..1 of its placard, from the loading (the fuel system: the sim burns it out of the
    /// tanks every step). An aeroplane flown with no fuel in ANY tank (the reference loading of most types, the open W&amp;B
    /// decision) reads full, as <see cref="FuelCentreFrac"/> does, rather than empty under a running engine. A tank the
    /// loading does not have reads full.</summary>
    public static double TankFraction(AircraftLoad? load, string id)
    {
        if (load is null || load.FuelKg <= 1e-6) return 1.0;
        AircraftLoad.Slot? s = load.Find(id);
        if (s is null || s.MaxKg <= 0.0) return 1.0;
        return System.Math.Clamp(s.Kg / s.MaxKg, 0.0, 1.0);
    }
    /// <summary>Rate of turn, degrees per second, + = turning right (3 = standard rate).</summary>
    public double TurnRateDps;
    /// <summary>Inclinometer ball, -1..+1, + = ball to the pilot's RIGHT.</summary>
    public double SlipBall;
    /// <summary>Normal load factor, g, + = positive.</summary>
    public double GLoad;
    /// <summary>The accelerometer's tell-tales: the running maximum and minimum since they were last reset.</summary>
    public double GMaxSeen, GMinSeen;
    /// <summary>Attitude, degrees: bank + = right wing down (-180..180), pitch + = nose up (-90..90). What an
    /// attitude indicator shows.</summary>
    public double BankDeg, PitchDeg;
    /// <summary>Floatplane water-rudder retract, 0 = down (spring; the J-3's red handle let go on the floor) .. 1 = up
    /// (handle hung on its hook). Aircraft.WaterRudderUp01: the blade's own travel, slewed, not the switch.</summary>
    public double WaterRudderUp;

    /// <summary>COM, MHz: the tuned (active) and the standby frequency.</summary>
    public double ComFreqMhz, ComStandbyMhz;
    /// <summary>0..1 radio volume (0 = off).</summary>
    public double ComVolume;
    /// <summary>Transponder code as a four-digit octal NUMBER (1200 is 1200), and its mode
    /// (0 OFF, 1 SBY, 2 ON, 3 ALT).</summary>
    public double XpdrSquawk, XpdrMode;
    /// <summary>Free-running count of transponder knob clicks (the knob turns on, never back to a stop).</summary>
    public double XpdrKnobClicks;

    // ---- the 172's vacuum, gyro and radio panel (FlyingGame.Core.Instruments.C172Panel; docs/C172-INSTRUMENTS.md) ----
    /// <summary>Suction gauge, inHg below ambient.</summary>
    public double SuctionInHg;
    /// <summary>Attitude indicator: indicated pitch (+ nose up) and bank (+ right wing down), degrees — the gimbal angles.</summary>
    public double AiPitchDeg, AiBankDeg;
    /// <summary>Directional gyro: the number under the lubber line, and the knob's total turns, degrees.</summary>
    public double DgCardDeg, DgKnobDeg;
    /// <summary>Magnetic compass: the reading under the lubber line, and the float's tilt in the bowl
    /// (the case's attitude relative to the float: + nose up, + right wing down), degrees.</summary>
    public double CompassCardDeg, CompassCardPitchDeg, CompassCardRollDeg;
    /// <summary>Turn coordinator: the little aeroplane in "rates" (+1 = right wing on the R mark), and its power flag (1 = in view).</summary>
    public double TcNeedle, TcFlag;
    /// <summary>NAV1: CDI −1…+1 (+ = course right), glideslope −1…+1 (+ = fly up), TO/FROM (+1 TO, −1 FROM, 0 OFF),
    /// the NAV and GS flags (1 = in view), the OBS and the tuned frequency.</summary>
    public double Nav1Cdi, Nav1Gs, Nav1ToFrom, Nav1Flag, Nav1GsFlag, Nav1ObsDeg, Nav1FreqMhz;
    /// <summary>NAV2: course only.</summary>
    public double Nav2Cdi, Nav2ToFrom, Nav2Flag, Nav2ObsDeg, Nav2FreqMhz;
    /// <summary>ADF: the needle, degrees clockwise from the nose; the hand-set card (the number at the top index); kHz.</summary>
    public double AdfNeedleDeg, AdfCardDeg, AdfFreqKhz;
    /// <summary>DME: slant range NM, ground speed kt, minutes to the station, and 1 when locked (0 = dashes).</summary>
    public double DmeNm, DmeKt, DmeMin, DmeValid;
    /// <summary>Rotor speed as a fraction of rated, for the gyro whine and a check.</summary>
    public double AiRotor, DgRotor, TcRotor;

    // ---- the engine gauges the shared Extra panel adds (lane/claude-extra-cockpit-shared, 2026-11-04) -----------
    // The P3b panel already carried needles for these (Needle_Amps_*, Needle_EGT_*, Needle_Fuel_Wing/Akro_*), and no
    // param fed them on either platform, so they sat at rest; Ty's new layout adds the three-in-one and a fuel
    // computer. Filled by Bridge CockpitRigState. The three-in-one's third needle is FUEL FLOW (Ty 2026-11-04 15:23:
    // the real 300L, EASA.A.362, has no fuel pressure gauge; its UI6331 is MP/FF), so there is no fuel pressure here.

    /// <summary>Ammeter, amperes, + = charging (a centre-zero ammeter).</summary>
    public double Amps;
    /// <summary>Exhaust gas temperature, deg F.</summary>
    public double EgtF;
    /// <summary>US gallons in the wing tanks together, and in the centre (aerobatic) tank.</summary>
    public double FuelWingGal, FuelAkroGal;
    /// <summary>The fuel computer: flow now (US gal/h) and burned since the aeroplane was reset (US gal).</summary>
    public double FuelFlowGph, FuelUsedGal;

    /// <summary>Avgas, 6.0 lb per US gallon.</summary>
    public const double KgPerUsGal = 2.7216;
    /// <summary>The sim's burn (Aircraft.FuelFlowKgPerSec, kg/s) as a fuel-flow gauge reads it, US gal/h.</summary>
    public static double GphFromKgPerSec(double kgPerSec) => kgPerSec * 3600.0 / KgPerUsGal;

    /// <summary>The CONTROL half, from the sim's deflections and the type's travel limits. The instrument
    /// readings are the caller's to fill in — a remote aeroplane's panel is nobody's to read.
    ///
    /// The one sign that has to be got right in exactly one place: the sim's elevator deflection is positive
    /// trailing-edge DOWN (ControlPolarityTests), and the rig's elevator is positive STICK AFT.
    ///
    /// <paramref name="elevatorUpMaxRad"/> is the stop on the trailing-edge-UP side (ControlAxisConfig.LimitFor(-1),
    /// the config's maxDeflNegRad), for an elevator that travels further up than down: the J-3's is 34 up / 29 down
    /// (TCDS A-691), and its rig maps +1 to 34 and -1 to 29. Normalising both sides by the down stop would draw full
    /// aft as 29 up and every part-aft stick 17 % too far. 0 = symmetric, as every other type is.</summary>
    public static RigInputs FromDeflections(double aileronRad, double elevatorRad, double rudderRad, double spoilerFraction,
                                            double aileronMaxRad, double elevatorMaxRad, double rudderMaxRad,
                                            double elevatorUpMaxRad = 0.0, double noseSteerMaxRad = 0.0)
    {
        static double N(double v, double max) => max > 1e-6 ? Math.Clamp(v / max, -1.0, 1.0) : 0.0;
        double aft = -elevatorRad;   // + = trailing edge up = stick aft
        double rudder = N(rudderRad, rudderMaxRad);
        return new RigInputs
        {
            Aileron = N(aileronRad, aileronMaxRad),
            Elevator = N(aft, aft > 0.0 && elevatorUpMaxRad > 1e-6 ? elevatorUpMaxRad : elevatorMaxRad),
            Rudder = rudder,
            Spoiler = Math.Clamp(spoilerFraction, 0.0, 1.0),
            // The same product LandingGear.SteerRad makes (rudder deflection / rudder max x lock), sign + = right.
            NoseWheelSteerDeg = rudder * noseSteerMaxRad * 180.0 / Math.PI,
        };
    }

    /// <summary>Take only the stick-and-handle part of <paramref name="controls"/>, leaving the instruments
    /// reading whatever they were.</summary>
    public void TakeControlsFrom(in RigInputs controls)
    {
        Aileron = controls.Aileron; Elevator = controls.Elevator;
        Rudder = controls.Rudder; Spoiler = controls.Spoiler;
        NoseWheelSteerDeg = controls.NoseWheelSteerDeg;
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
            FuelValve = 1.0,        // on
            Magneto = 3.0,          // BOTH
            MagLeft = 1.0, MagRight = 1.0,
            Master = 1.0,
            Alternator = 1.0,
            CanopyLock = 1.0,       // closed and locked
            OilPressPsi = 0.0,
            OilTempF = 100.0,       // the bottom of the dial, as drawn
            ChtF = 200.0,
            FuelCentreFrac = 1.0, FuelTopWingFrac = 1.0,
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
            case "nose_wheel_steer_deg": value = NoseWheelSteerDeg; return true;
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
            case "fuel_valve": value = FuelValve; return true;
            case "cowl_flap": value = CowlFlap; return true;
            case "prop_lock": value = PropLock; return true;
            case "magneto": value = Magneto; return true;
            case "mag_left": value = MagLeft; return true;
            case "mag_right": value = MagRight; return true;
            case "primer": value = Primer; return true;
            case "master": value = Master; return true;
            case "alternator": value = Alternator; return true;
            case "fuel_pump": value = FuelPump; return true;
            case "canopy_open": value = CanopyOpen; return true;
            case "canopy_slide": value = CanopySlide; return true;
            case "canopy_lock": value = CanopyLock; return true;
            case "canopy_jettison": value = CanopyJettison; return true;
            case "canopy_latch": value = CanopyLatch; return true;
            case "harness_latch": value = HarnessLatch; return true;
            case "airspeed_kias": value = AirspeedKias; return true;
            case "vsi_fpm": value = VsiFpm; return true;
            case "rpm": value = Rpm; return true;
            case "manifold_inhg": value = ManifoldInHg; return true;
            case "oil_press_psi": value = OilPressPsi; return true;
            case "oil_temp_f": value = OilTempF; return true;
            case "cht_f": value = ChtF; return true;
            case "fuel_centre_frac": value = FuelCentreFrac; return true;
            case "fuel_top_wing_frac": value = FuelTopWingFrac; return true;
            case "turn_rate_dps": value = TurnRateDps; return true;
            case "slip_ball": value = SlipBall; return true;
            case "g_load": value = GLoad; return true;
            case "g_max_seen": value = GMaxSeen; return true;
            case "g_min_seen": value = GMinSeen; return true;
            case "bank_deg": value = BankDeg; return true;
            case "pitch_deg": value = PitchDeg; return true;
            case "water_rudder_up": value = WaterRudderUp; return true;
            case "com_freq_mhz": value = ComFreqMhz; return true;
            case "com_standby_mhz": value = ComStandbyMhz; return true;
            case "com_volume": value = ComVolume; return true;
            case "xpdr_squawk": value = XpdrSquawk; return true;
            case "xpdr_mode": value = XpdrMode; return true;
            case "xpdr_knob_clicks": value = XpdrKnobClicks; return true;
            case "suction_inhg": value = SuctionInHg; return true;
            case "ai_pitch_deg": value = AiPitchDeg; return true;
            case "ai_bank_deg": value = AiBankDeg; return true;
            case "dg_card_deg": value = DgCardDeg; return true;
            case "dg_knob_deg": value = DgKnobDeg; return true;
            case "compass_card_deg": value = CompassCardDeg; return true;
            case "compass_card_pitch_deg": value = CompassCardPitchDeg; return true;
            case "compass_card_roll_deg": value = CompassCardRollDeg; return true;
            case "tc_needle": value = TcNeedle; return true;
            case "tc_flag": value = TcFlag; return true;
            case "nav1_cdi": value = Nav1Cdi; return true;
            case "nav1_gs": value = Nav1Gs; return true;
            case "nav1_tofrom": value = Nav1ToFrom; return true;
            case "nav1_flag": value = Nav1Flag; return true;
            case "nav1_gs_flag": value = Nav1GsFlag; return true;
            case "nav1_obs_deg": value = Nav1ObsDeg; return true;
            case "nav1_freq_mhz": value = Nav1FreqMhz; return true;
            case "nav2_cdi": value = Nav2Cdi; return true;
            case "nav2_tofrom": value = Nav2ToFrom; return true;
            case "nav2_flag": value = Nav2Flag; return true;
            case "nav2_obs_deg": value = Nav2ObsDeg; return true;
            case "nav2_freq_mhz": value = Nav2FreqMhz; return true;
            case "adf_needle_deg": value = AdfNeedleDeg; return true;
            case "adf_card_deg": value = AdfCardDeg; return true;
            case "adf_freq_khz": value = AdfFreqKhz; return true;
            case "dme_nm": value = DmeNm; return true;
            case "dme_kt": value = DmeKt; return true;
            case "dme_min": value = DmeMin; return true;
            case "dme_valid": value = DmeValid; return true;
            case "ai_rotor": value = AiRotor; return true;
            case "dg_rotor": value = DgRotor; return true;
            case "tc_rotor": value = TcRotor; return true;

            case "amps": value = Amps; return true;
            case "egt_f": value = EgtF; return true;
            case "fuel_wing_gal": value = FuelWingGal; return true;
            case "fuel_akro_gal": value = FuelAkroGal; return true;
            case "fuel_flow_gph": value = FuelFlowGph; return true;
            case "fuel_used_gal": value = FuelUsedGal; return true;
            default: value = 0.0; return false;
        }
    }
}

/// <summary>One model's rig: the nodes, in the order the file lists them (parents before children).</summary>
public sealed class ModelRig
{
    public readonly string Model;
    public readonly IReadOnlyList<RigNode> Nodes;
    /// <summary>The table's `controls`: the switches and knobs a hand works (Codex's Ctl_* nodes), or none.</summary>
    public readonly IReadOnlyList<FlyingGame.Core.Vr.PanelControlSpec> Controls;

    public ModelRig(string model, IReadOnlyList<RigNode> nodes, IReadOnlyList<FlyingGame.Core.Vr.PanelControlSpec>? controls = null)
    {
        Model = model ?? "";
        Nodes = nodes ?? Array.Empty<RigNode>();
        Controls = controls ?? Array.Empty<FlyingGame.Core.Vr.PanelControlSpec>();
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
