using System;
using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Recording;

namespace FlyingGame.Sim.Recording;

/// <summary>
/// Everything one sample needs that is not already on the <see cref="Aircraft"/>: the wind the Bridge sampled, the
/// few quantities only the Bridge owns, and the differenced angular acceleration.
///
/// A CLASS and not a struct on purpose — it is handed to 150 channel delegates per sample, and a struct would be
/// copied or boxed 150 times in the stepping path.
/// </summary>
public sealed class RecorderContext
{
    public Aircraft Aircraft = null!;
    public Vec3 WindWorld;

    /// <summary>The pilot's raw stick and lever positions for this step. They are NOT kept on the Aircraft — they
    /// are passed into Step and dropped — so the recorder is handed the same struct the sim was given, which is
    /// also the only way the input and the resulting deflection are guaranteed to be from the same step.</summary>
    public ControlInputs Inputs;

    /// <summary>Angular acceleration, body axes, rad/s². Differenced by the recorder because the sim never stores
    /// it: exact at a fixed 5 ms step, and the alternative was a field in the integrator.</summary>
    public Vec3 RatesDot;

    /// <summary>OIL TEMPERATURE lives in the Bridge, not the sim (CockpitRigState owns the first-order state), so
    /// it is pushed in here. On a headless build it stays 0 and the channel is honestly empty rather than wrong.</summary>
    public double OilTempF;

    /// <summary>Recorder health, so a reader can tell a bad sample from a bad aeroplane: physics steps the frame
    /// had to throw away, and the recorder's own measured cost.</summary>
    public double CatchUpCappedFrames;
    public double RecorderMicros;

    // ---------------------------------------------------------------- derived ONCE per sample
    //
    // These were originally computed inside the channel readers, and the recorder's own cost measurement caught
    // what that was worth: 2.9 MILLISECONDS a sample, 58 % of a 5 ms physics step, because seven channels each
    // re-derived the air-relative velocity (two quaternion rotations apiece), six re-derived the earth velocity,
    // and two sampled the atmosphere. Deriving them once here and letting the readers read a field is the whole
    // difference between a recorder that measures the aeroplane and one that changes it.
    public Vec3 AirVelBody;     // air-relative velocity, body axes
    public Vec3 EarthVel;       // ground velocity, world axes
    public double Density;
    public double PressurePa;
    public double OatK;
    public double AlphaRad;
    public double BetaRad;
    public double TasMs;
    public Vec3 Forward;        // body x in world axes
    public Vec3 Right;          // body y in world axes

    /// <summary>Work out everything several channels share, once. Called by the recorder before it reads any
    /// channel; allocation-free.</summary>
    public void Derive()
    {
        Aircraft a = Aircraft;
        double alt = -a.State.Position.Z;
        EarthVel = a.State.Attitude.Rotate(a.State.Velocity);
        AirVelBody = a.State.Attitude.Conjugate().Rotate(EarthVel - WindWorld);
        TasMs = AirVelBody.Length;
        AlphaRad = Math.Atan2(AirVelBody.Z, AirVelBody.X);
        BetaRad = TasMs < 1e-6 ? 0.0 : Math.Asin(Math.Clamp(AirVelBody.Y / TasMs, -1.0, 1.0));
        Density = Atmosphere.DensityAt(a.State.Position);
        PressurePa = Atmosphere.PressureAtAltitude(alt);
        OatK = Atmosphere.TemperatureAtAltitudeK(alt);
        Forward = a.State.Attitude.Rotate(new Vec3(1, 0, 0));
        Right = a.State.Attitude.Rotate(new Vec3(0, 1, 0));
    }
}

/// <summary>
/// WHAT THE RECORDER RECORDS. One list of channels, each carrying its own name, unit AND reader — so the column
/// header and the number under it cannot drift apart. That is not tidiness: a recorder whose channel table is
/// maintained separately from its filler shears every column the day someone inserts a channel in the middle, and
/// the file still loads and still looks plausible.
///
/// WHAT IS DELIBERATELY NOT HERE, and why — this is the one place the brief and the constraints conflicted:
///
///   PER-SURFACE-GROUP FORCES AND MOMENTS (wing / hStab / vStab / fuselage / gear / floats). They do not exist
///   cheaply. The only breakdown in the sim is <c>ForceDebug</c>, which allocates a fresh List&lt;ForceSample&gt;(160)
///   on every captured step, buckets only by a <c>string Kind</c> with no surface id, and is a process-wide static
///   that the force-vector overlay also uses. Recording it at 50 Hz would mean fifty list allocations a second in
///   the physics step — which is exactly the failure that makes the data describe the recorder instead of the
///   aeroplane, and that constraint outranks the feature. SimLoop itself already works around the same cost by
///   capturing on the frame's last step only.
///
///   What is recorded instead is every cheap aggregate the sim already keeps: specific force, both load factors,
///   the aero rolling moment, the stalled fraction, the slip over the tail and the tail slipstream. A per-group
///   breakdown wants a separate, explicitly opt-in, much lower-rate mode that accepts the allocation and says so.
/// </summary>
public static class FlightTestChannels
{
    public sealed class Channel
    {
        public readonly string Name;
        public readonly string Unit;
        public readonly Func<RecorderContext, double> Read;
        public Channel(string name, string unit, Func<RecorderContext, double> read) { Name = name; Unit = unit; Read = read; }
    }

    /// <summary>Gear legs are padded to a fixed count so every file of every type has the same width — a recorder
    /// whose record size depends on the aeroplane is one the converter has to special-case per type.</summary>
    public const int GearSlots = 5;

    private const double R2D = 180.0 / Math.PI;

    private static readonly List<Channel> _all = Build();
    public static IReadOnlyList<Channel> All => _all;
    public static int Count => _all.Count;

    public static string[] Names()
    {
        var a = new string[_all.Count];
        for (int i = 0; i < _all.Count; i++) a[i] = _all[i].Name;
        return a;
    }

    public static string[] Units()
    {
        var a = new string[_all.Count];
        for (int i = 0; i < _all.Count; i++) a[i] = _all[i].Unit;
        return a;
    }

    private static void Add(List<Channel> list, string name, string unit, Func<RecorderContext, double> read)
        => list.Add(new Channel(name, unit, read));

    private static List<Channel> Build()
    {
        var c = new List<Channel>(160);

        // ---- where it is and what it is doing -------------------------------------------------
        Add(c, "posN", "m", x => x.Aircraft.State.Position.X);
        Add(c, "posE", "m", x => x.Aircraft.State.Position.Y);
        Add(c, "posDown", "m", x => x.Aircraft.State.Position.Z);
        Add(c, "altitude", "m", x => -x.Aircraft.State.Position.Z);
        Add(c, "quatX", "", x => x.Aircraft.State.Attitude.X);
        Add(c, "quatY", "", x => x.Aircraft.State.Attitude.Y);
        Add(c, "quatZ", "", x => x.Aircraft.State.Attitude.Z);
        Add(c, "quatW", "", x => x.Aircraft.State.Attitude.W);
        Add(c, "heading", "deg", x => Heading(x));
        Add(c, "pitch", "deg", x => Pitch(x));
        Add(c, "bank", "deg", x => Bank(x));

        // Ground velocity in body and earth axes. Both, because the distinction between air-relative and
        // ground-relative has already caused one bug in the challenge signals and a recorder must not repeat it.
        Add(c, "velBodyU", "m/s", x => x.Aircraft.State.Velocity.X);
        Add(c, "velBodyV", "m/s", x => x.Aircraft.State.Velocity.Y);
        Add(c, "velBodyW", "m/s", x => x.Aircraft.State.Velocity.Z);
        Add(c, "velEarthN", "m/s", x => x.EarthVel.X);
        Add(c, "velEarthE", "m/s", x => x.EarthVel.Y);
        Add(c, "velEarthD", "m/s", x => x.EarthVel.Z);
        Add(c, "groundSpeed", "m/s", x => Math.Sqrt(x.EarthVel.X * x.EarthVel.X + x.EarthVel.Y * x.EarthVel.Y));
        Add(c, "verticalSpeed", "m/s", x => -x.EarthVel.Z);

        Add(c, "rateP", "rad/s", x => x.Aircraft.State.Rates.X);
        Add(c, "rateQ", "rad/s", x => x.Aircraft.State.Rates.Y);
        Add(c, "rateR", "rad/s", x => x.Aircraft.State.Rates.Z);
        Add(c, "rateDotP", "rad/s2", x => x.RatesDot.X);
        Add(c, "rateDotQ", "rad/s2", x => x.RatesDot.Y);
        Add(c, "rateDotR", "rad/s2", x => x.RatesDot.Z);

        // Specific force is what an accelerometer feels — the sim keeps it at the last force evaluation of the
        // step, which is better than differencing velocity and is the number a flight test card asks for.
        Add(c, "accelBodyX", "m/s2", x => x.Aircraft.SpecificForceBody.X);
        Add(c, "accelBodyY", "m/s2", x => x.Aircraft.SpecificForceBody.Y);
        Add(c, "accelBodyZ", "m/s2", x => x.Aircraft.SpecificForceBody.Z);
        Add(c, "loadFactorZ", "g", x => x.Aircraft.LoadFactorZ);
        Add(c, "aeroLoadFactorZ", "g", x => x.Aircraft.AeroLoadFactorZ);
        Add(c, "aeroRollMoment", "Nm", x => x.Aircraft.AeroRollMomentNm);

        // ---- air data ---------------------------------------------------------------------------
        // Recomputed here rather than read off FlightSimDriver, whose IasMs is (a) actually TAS and (b) only
        // updated once a frame — useless at 50 Hz, and silently stale while replaying.
        Add(c, "alpha", "deg", x => x.AlphaRad * R2D);
        Add(c, "beta", "deg", x => x.BetaRad * R2D);
        Add(c, "tas", "m/s", x => x.TasMs);
        // CAS does not exist in this model: IAS is the pure density ratio with no position error, so CAS ≡ IAS.
        Add(c, "ias", "m/s", x => x.TasMs * Math.Sqrt(x.Density / Atmosphere.SeaLevelDensityKgM3));
        Add(c, "airVelBodyU", "m/s", x => x.AirVelBody.X);
        Add(c, "airVelBodyV", "m/s", x => x.AirVelBody.Y);
        Add(c, "airVelBodyW", "m/s", x => x.AirVelBody.Z);
        Add(c, "windN", "m/s", x => x.WindWorld.X);
        Add(c, "windE", "m/s", x => x.WindWorld.Y);
        Add(c, "windD", "m/s", x => x.WindWorld.Z);
        Add(c, "density", "kg/m3", x => x.Density);
        Add(c, "pressure", "Pa", x => x.PressurePa);
        Add(c, "oat", "K", x => x.OatK);

        // ---- what the pilot asked for ------------------------------------------------------------
        // The pilot's INPUT and the surface's ACTUAL position are recorded separately, and that separation is the
        // whole diagnostic value: on a reversible control they differ by the hinge moment, which is what a free
        // elevator, a stick-free trail and the high-alpha control spoiler all show up in.
        Add(c, "inAileron", "", x => x.Inputs.Aileron);
        Add(c, "inElevator", "", x => x.Inputs.Elevator);
        Add(c, "inRudder", "", x => x.Inputs.Rudder);
        // RECORDED RAW, AND IT IS INVERTED: ThrottleLever is -1 full FORWARD, 0 neutral, +1 full AFT (on a glider
        // the same lever is the airbrake). That sign has caught people before, so the raw lever and the 0..1
        // throttle the engine actually runs on are both here, under names that cannot be confused.
        Add(c, "inThrottleLever", "", x => x.Inputs.ThrottleLever);
        Add(c, "throttle01", "", x => x.Aircraft.Throttle01);
        Add(c, "inAileronFree", "bool", x => x.Inputs.AileronFree ? 1 : 0);
        Add(c, "inElevatorFree", "bool", x => x.Inputs.ElevatorFree ? 1 : 0);
        Add(c, "inRudderFree", "bool", x => x.Inputs.RudderFree ? 1 : 0);
        Add(c, "inBrake", "", x => x.Aircraft.BrakeInput);
        Add(c, "inBrakeBias", "", x => x.Aircraft.BrakeBias);

        // ---- where the surfaces actually are ------------------------------------------------------
        Add(c, "defAileron", "deg", x => x.Aircraft.CurrentDeflections.AileronRad * R2D);
        Add(c, "defElevator", "deg", x => x.Aircraft.CurrentDeflections.ElevatorRad * R2D);
        Add(c, "defRudder", "deg", x => x.Aircraft.CurrentDeflections.RudderRad * R2D);
        Add(c, "defFlap", "", x => x.Aircraft.FlapFraction);
        Add(c, "defSpoiler", "", x => x.Aircraft.CurrentDeflections.SpoilerFraction);
        Add(c, "gearExtension", "", x => x.Aircraft.GearExtension);
        Add(c, "stalledFraction", "", x => x.Aircraft.StalledFraction);

        // ---- engine and propeller -------------------------------------------------------------------
        Add(c, "shaftRpm", "rpm", x => x.Aircraft.ShaftRpm);
        Add(c, "engineRpm", "rpm", x => x.Aircraft.EngineRpm);
        Add(c, "manifoldPressure", "inHg", x => Manifold(x));
        Add(c, "shaftPower01", "", x => x.Aircraft.ShaftPower01);
        Add(c, "powerW", "W", x => x.Aircraft.ShaftPower01 * (x.Aircraft.Config.Propulsion?.MaxPowerW ?? 0.0));
        Add(c, "bladePitch", "deg", x => x.Aircraft.BladePitchDeg);
        Add(c, "oilPressure", "psi", x => x.Aircraft.OilPressPsi);
        Add(c, "oilTempRise", "F", x => x.Aircraft.OilTempRiseF);
        Add(c, "oilTemp", "F", x => x.OilTempF);
        Add(c, "egt", "F", x => x.Aircraft.EgtF);
        Add(c, "mixturePhi", "", x => x.Aircraft.MixturePhi);
        Add(c, "engineRoughness", "", x => x.Aircraft.EngineRoughness);
        Add(c, "fuelFlow", "kg/s", x => x.Aircraft.FuelFlowKgPerSec);
        Add(c, "engineRunning", "bool", x => x.Aircraft.EngineRunning ? 1 : 0);
        Add(c, "engineStarved", "bool", x => x.Aircraft.EngineStarved ? 1 : 0);
        Add(c, "compressionPeak", "Nm", x => x.Aircraft.CompressionPeakNm);
        Add(c, "crankAngle", "rad", x => x.Aircraft.CrankAngleRad);

        // ---- the air at the tail ------------------------------------------------------------------
        Add(c, "slipOverTail", "", x => x.Aircraft.SlipOverTail);
        Add(c, "tailJetDeltaU", "m/s", x => x.Aircraft.LastTailSlipstream.DeltaU);
        Add(c, "tailJetRadius", "m", x => x.Aircraft.LastTailSlipstream.Radius);
        Add(c, "tailJetSwirl", "rad/s", x => x.Aircraft.LastTailSlipstream.Swirl);

        // ---- mass and balance as actually loaded ---------------------------------------------------
        Add(c, "massKg", "kg", x => x.Aircraft.MassProperties.MassKg);
        Add(c, "cgX", "m", x => x.Aircraft.Config.Mass.CgVec().X);
        Add(c, "cgY", "m", x => x.Aircraft.Config.Mass.CgVec().Y);
        Add(c, "cgZ", "m", x => x.Aircraft.Config.Mass.CgVec().Z);

        // ---- the ground and the water, per leg ------------------------------------------------------
        for (int i = 0; i < GearSlots; i++)
        {
            int leg = i;
            Add(c, "gear" + leg + "Contact", "bool", x => Wheel(x, leg) != null && Wheel(x, leg)!.InContact ? 1 : 0);
            Add(c, "gear" + leg + "Load", "N", x => Wheel(x, leg)?.LoadN ?? 0.0);
            Add(c, "gear" + leg + "Penetration", "m", x => Wheel(x, leg)?.PenetrationM ?? 0.0);
            Add(c, "gear" + leg + "Sink", "m", x => Wheel(x, leg)?.SinkM ?? 0.0);
            Add(c, "gear" + leg + "LegStroke", "m", x => Wheel(x, leg)?.LegStrokeM ?? 0.0);
            Add(c, "gear" + leg + "SlipRatio", "", x => Wheel(x, leg)?.SlipRatio ?? 0.0);
            Add(c, "gear" + leg + "Brake", "", x => Wheel(x, leg)?.Brake01 ?? 0.0);
            Add(c, "gear" + leg + "Omega", "rad/s", x => Wheel(x, leg)?.OmegaRadS ?? 0.0);
            Add(c, "gear" + leg + "OverWater", "bool", x => Wheel(x, leg) != null && Wheel(x, leg)!.OverWater ? 1 : 0);
        }
        Add(c, "anyGearTouching", "bool", x => x.Aircraft.AnyGearTouching ? 1 : 0);

        // ---- recorder health, so a bad sample can be told from a bad aeroplane -----------------------
        Add(c, "catchUpCappedFrames", "count", x => x.CatchUpCappedFrames);
        Add(c, "recorderCost", "us", x => x.RecorderMicros);

        return c;
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Indexed, never foreach: Aircraft.Wheels is an IReadOnlyList over an array, and iterating the
    /// interface boxes an enumerator on every sample.</summary>
    private static WheelState? Wheel(RecorderContext x, int i)
    {
        System.Collections.Generic.IReadOnlyList<WheelState> w = x.Aircraft.Wheels;
        return w != null && i < w.Count ? w[i] : null;
    }

    /// <summary>The manifold pressure the ENGINE actually flies on (PistonEngine's own law), not the cockpit
    /// presentation value in CockpitRigState — the two disagree, and a flight-test file wants the physical one.
    ///
    /// IT NEEDS THE RPM, and that is the whole of the FMEP/PMEP change in one line: a throttle plate is a restriction
    /// and the engine is a pump whose demand goes as its speed, so the same lever reads a healthy manifold at idle
    /// and a few inches of vacuum at 2,500 rpm (PistonMep.ManifoldPa). Recording it against the lever alone would
    /// have written a number no gauge in the aeroplane agrees with.</summary>
    private static double Manifold(RecorderContext x)
    {
        Aircraft a = x.Aircraft;
        double rated = a.Config.Propulsion?.MaxRpm ?? 0.0;
        return PistonEngine.ManifoldInHg(a.Throttle01, x.PressurePa / Atmosphere.SeaLevelPressurePa, a.EngineRpm, rated);
    }

    private static double Heading(RecorderContext x)
    {
        double h = Math.Atan2(x.Forward.Y, x.Forward.X) * R2D;
        return h < 0 ? h + 360.0 : h;
    }

    private static double Pitch(RecorderContext x) => Math.Asin(Math.Clamp(-x.Forward.Z, -1.0, 1.0)) * R2D;

    private static double Bank(RecorderContext x)
        => Math.Atan2(-x.Right.Z, Math.Sqrt(x.Right.X * x.Right.X + x.Right.Y * x.Right.Y)) * R2D * -1.0;
}
