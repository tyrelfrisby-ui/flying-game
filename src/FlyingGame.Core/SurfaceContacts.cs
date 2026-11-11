using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>
/// What is touching the surface (Ty 2026-11-04, surface tracks: "anything contacting a surface leaves tracks"). APPEND
/// ONLY: a renderer may index tables by it.
/// </summary>
public enum SurfaceContactType
{
    /// <summary>A tyre: a track of its footprint's width, a rut where the ground gives, a skid mark where it slides.</summary>
    Tire,
    /// <summary>A ski (a wheel-ski on its ski, or a ski leg): a broad track of the ski's width.</summary>
    Ski,
    /// <summary>A float or a hull: a wake on water, a V-shaped mark behind it on snow or land.</summary>
    Float,
    /// <summary>A skid shoe (nose, tail or wingtip skid): metal or wood sliding on the ground.</summary>
    Skid,
    /// <summary>A foot-launched pilot's feet.</summary>
    Foot,
}

/// <summary>
/// One contact with the surface, as one physics step found it (docs: surface-tracks spec, PART A). All vectors are world
/// (NED: z down). A value the model does not have is 0, never a guess, and each field says where it comes from.
/// </summary>
public struct SurfaceContact
{
    public SurfaceContactType Type;
    /// <summary>Which part: the gear leg's index in <see cref="AircraftConfig.Gear"/> (tyre, skid, keel, foot, on-ski
    /// wheel); the <see cref="FloatHydro.Floats"/> slot for a float on water (0 left, 1 right, 2/3 wingtip floats); the
    /// ski's own leg for <see cref="SurfaceContacts.AddSki"/>.</summary>
    public int Source;
    /// <summary>The contact point on the surface: the tyre's bottom, the ski's centre of pressure, a float's step.</summary>
    public Vec3 PositionWorld;
    /// <summary>The UNDISTURBED surface at the contact (<see cref="WheelState.SurfaceWorld"/>): the same point carried
    /// back up <see cref="SurfaceUpWorld"/> by the whole contact compression, so it is on the ground rather than inside
    /// it. What a mark in the ground is drawn at (<see cref="GroundImprints"/>); falls back to
    /// <see cref="PositionWorld"/> where the model has no compression (a float on water, a ski from
    /// <see cref="SurfaceContacts.AddSki"/>).</summary>
    public Vec3 SurfaceWorld;
    /// <summary>The surface's outward normal there (world-up, (0, 0, −1), on level ground and on water).</summary>
    public Vec3 SurfaceUpWorld;
    /// <summary>The way the part points along the surface (unit, in the surface's plane): the tyre's rolling direction
    /// (steered), the ski's or the float's keel line.</summary>
    public Vec3 HeadingWorld;
    /// <summary>The contact point's velocity over the surface, in its plane, m/s: the direction the track is laid in.
    /// Relative to a moving deck, and to the water's own current for a float.</summary>
    public Vec3 GroundVelocityWorld;
    /// <summary>How fast the part's own surface slides over the ground, m/s, in the surface's plane: for a tyre its rubber
    /// (fore and aft the speed past the rim, v − ωr; sideways its side slip), so a rolling tyre has ~0 and a locked or
    /// scrubbing one has its whole speed — the skid mark. A ski, skid, keel or foot always slides: the whole
    /// <see cref="GroundVelocityWorld"/>.</summary>
    public Vec3 SlipWorld;
    /// <summary>Width of the mark, m: the tyre's footprint width at its deflection (TireModel; a rigid legacy tyre's section
    /// width), the ski's running
    /// surface, the float's beam (chine to chine), a skid's shoe.</summary>
    public double WidthM;
    /// <summary>Length of the patch along <see cref="HeadingWorld"/>, m: the tyre's footprint, the ski, the float's wetted
    /// keel (λ × beam). 0 where the model has none (a skid, a keel on land).</summary>
    public double LengthM;
    /// <summary>How deep the part is in the surface, m: a tyre's rut on soft ground (the Bekker sinkage its rolling
    /// resistance charged for, <see cref="TireModel.GroundSinkageM"/>; 0 on pavement and ice), a tyre's depth in water, a
    /// ski's sinkage (from the ski model, when it supplies one), a float's draft at the step.</summary>
    public double DepthM;
    /// <summary>
    /// The whole contact compression at this contact, m, along <see cref="SurfaceUpWorld"/>: how far the part's lowest
    /// point is inside the ground (<see cref="WheelState.PenetrationM"/>). ELASTIC — a spring holds it, and it returns
    /// when the load leaves — so a permanent mark takes only <see cref="GroundImprints.PlasticRetention01"/> of it.
    ///
    /// It is the only depth a leg with NO sinkage model has: a bearing ski, a skid shoe, a float keel on land, feet.
    /// Where <see cref="DepthM"/> is non-zero, that is a sinkage and is the better number — it is the soil's permanent
    /// set already.
    /// </summary>
    public double PenetrationM;
    /// <summary>The surface's push on the part, N (normal load; the water's whole upward push for a float).</summary>
    public double LoadN;
    /// <summary>Mean pressure on the surface, Pa: the tyre's ground pressure (TireModel), the ski's load over its area;
    /// 0 where there is no patch area.</summary>
    public double PressurePa;
    /// <summary>Friction power the slip turns into heat and mark, W (a tyre's fore-aft and side slip); 0 for the rest.</summary>
    public double SlipPowerW;
    /// <summary>A wheel the brake has stopped while the aeroplane moves: the classic skid mark.</summary>
    public bool Locked;
    /// <summary>What the surface is, one key shared with the physics and the sound (<see cref="GroundMaterials"/>).</summary>
    public GroundMaterial Material;
    /// <summary>What the tyre physics called the ground (water: the bed under it, not meaningful).</summary>
    public WorldTerrain.Surface Surface;
    /// <summary>Snow wetness 0–1 (dry to soaking) on <see cref="GroundMaterial.Snow"/> / <see cref="GroundMaterial.PackedSnow"/>,
    /// 0 on everything else: <see cref="SurfaceContacts.WetnessFromLiquidWater"/> of the snow model's liquid water
    /// content (<see cref="SurfaceContacts.SnowLiquidWaterPercent"/>).</summary>
    public double SnowWetness01;
    /// <summary>Sim time of the step (<see cref="Atmosphere.SimTimeSec"/>), s.</summary>
    public double TimeSec;

    public override readonly string ToString() =>
        $"{Type}[{Source}] {Material} w {WidthM:F3} m d {DepthM:F3} m load {LoadN:F0} N slip {SlipWorld.Length:F2} m/s wet {SnowWetness01:F2}";
}

/// <summary>
/// The surface contacts of one aeroplane's last physics step. Fixed capacity, filled in place every step: nothing is
/// allocated after construction (<see cref="SurfaceContacts.Collect"/>). Read it after the step, or subscribe to the
/// aeroplane's per-step notification — every step, so a touchdown that lasts one step is not lost between frames.
/// </summary>
public sealed class SurfaceContactBuffer
{
    private readonly SurfaceContact[] _items;
    /// <summary>Per gear leg, set once: the mark's width for a leg that is not a tyre (float beam, skid shoe, feet), and for a
    /// tyre with no carcass model (a legacy size, rigid: no footprint) its section width.</summary>
    internal readonly double[] LegWidthM;
    /// <summary>The main float's beam (m), 0 without floats; the tip floats' beam.</summary>
    internal readonly double FloatBeamM, TipFloatBeamM;

    public int Count { get; private set; }
    public int Capacity => _items.Length;
    /// <summary>Contacts that did not fit this step (should stay 0: the capacity is every leg, every float and spare skis).</summary>
    public int Dropped { get; private set; }
    /// <summary>Steps collected since construction, so a reader can tell a new step from the same one read twice.</summary>
    public long Step { get; internal set; }

    public SurfaceContactBuffer(int capacity)
    {
        _items = new SurfaceContact[System.Math.Max(1, capacity)];
        LegWidthM = System.Array.Empty<double>();
    }

    /// <summary>Sized for this aeroplane: one slot per gear leg, four floats, four skis spare.</summary>
    public SurfaceContactBuffer(AircraftConfig c) : this(c.Gear.Count + 4 + SurfaceContacts.SpareSkiSlots)
    {
        LegWidthM = new double[c.Gear.Count];
        if (c.Floats != null)
        {
            FloatBeamM = FloatHull.For(c.Floats).Beam;
            if (c.Floats.TipFloats != null) TipFloatBeamM = FloatHull.For(c.Floats.TipFloats).Beam;
        }
        for (int i = 0; i < c.Gear.Count; i++)
        {
            GearConfig g = c.Gear[i];
            LegWidthM[i] = g.GearType == "float-keel" ? FloatBeamM
                         : g.GearType == "feet" ? SurfaceContacts.FeetWidthM
                         : WheelSpin.IsWheel(g) ? SurfaceContacts.SectionWidthM(c, g)
                         : SurfaceContacts.SkidShoeWidthM;
        }
    }

    public ref readonly SurfaceContact this[int i]
    {
        get
        {
            if ((uint)i >= (uint)Count) throw new System.ArgumentOutOfRangeException(nameof(i));
            return ref _items[i];
        }
    }

    public void Clear() { Count = 0; Dropped = 0; }

    /// <summary>Add one contact; false (and counted in <see cref="Dropped"/>) when full.</summary>
    public bool Add(in SurfaceContact c)
    {
        if (Count >= _items.Length) { Dropped++; return false; }
        _items[Count++] = c;
        return true;
    }
}

/// <summary>
/// Builds the per-step <see cref="SurfaceContact"/> list from what the contact physics already recorded: the gear legs'
/// <see cref="WheelState"/> (tyres, wheels on their skis, float keels on land, skids, feet) and the floats'
/// <see cref="FloatHydro.Floats"/> on water. It reads, it never changes the physics. Ski legs from a ski model come in
/// through <see cref="AddSki"/>.
/// </summary>
public static class SurfaceContacts
{
    public const int SpareSkiSlots = 4;

    /// <summary>A skid shoe's width, m. ESTIMATE, flagged: no config carries it; a glider's nose skid and a tailskid's
    /// shoe are a few centimetres wide.</summary>
    public const double SkidShoeWidthM = 0.06;
    /// <summary>A foot-launched pilot's two feet side by side, m. ESTIMATE, flagged.</summary>
    public const double FeetWidthM = 0.25;
    /// <summary>A wheel-ski's running surface where the gear has no ski config of its own, m: the default of the ski
    /// model's <c>SkiConfig.WidthM</c> (lane/claude-ski-physics), and its length.</summary>
    public const double DefaultSkiWidthM = 0.25, DefaultSkiLengthM = 1.8;

    /// <summary>A tyre's section width, m: the catalog's, else 0.35 of its diameter (the ski model's default for a tyre
    /// with no catalog size, <c>SkiConfig.TyreWidthM</c>).</summary>
    public static double SectionWidthM(AircraftConfig c, GearConfig g)
    {
        TireSpec? t = TireCatalog.Of(g);
        return t != null && t.SectionWidthM > 0 ? t.SectionWidthM : 0.35 * 2 * LandingGear.TireRadiusOf(c, g);
    }

    /// <summary>
    /// The snow's liquid water content, % by volume, at a contact on snow: (surface, contact point world, surface up world)
    /// → LWC. Set by the snow model (lane/claude-ski-physics: <c>SnowConditions.For(...).LiquidWaterPercent</c>); null on a
    /// tip without one, where nothing in the physics makes snow wet either, so every contact reads dry (0). Called only
    /// for contacts on snow. Process-wide; a test that sets it puts it back.
    /// </summary>
    public static System.Func<WorldTerrain.Surface, Vec3, Vec3, double>? SnowLiquidWaterPercent { get; set; }

    /// <summary>Liquid water content at which snow is "soaking" (ICSSG, Fierz et al. 2009, Table 2.1: dry 0, moist 0–3,
    /// wet 3–8, very wet 8–15, soaking &gt; 15 % by volume): wetness 1, the darkest track.</summary>
    public const double SoakingLiquidWaterPercent = 15.0;

    /// <summary>Snow wetness 0–1 from liquid water content (% by volume): linear to the ICSSG "soaking" class.</summary>
    public static double WetnessFromLiquidWater(double lwcPercent) =>
        double.IsFinite(lwcPercent) ? System.Math.Clamp(lwcPercent / SoakingLiquidWaterPercent, 0.0, 1.0) : 0.0;

    public static bool IsSnow(GroundMaterial m) => m == GroundMaterial.Snow || m == GroundMaterial.PackedSnow;

    private static double SnowWetness(GroundMaterial m, WorldTerrain.Surface s, Vec3 at, Vec3 up)
    {
        if (!IsSnow(m)) return 0.0;
        var f = SnowLiquidWaterPercent;
        return f == null ? 0.0 : WetnessFromLiquidWater(f(s, at, up));
    }

    private static readonly Vec3 WorldUp = new(0, 0, -1);

    /// <summary>
    /// Refill <paramref name="into"/> with this step's contacts: one per gear leg in contact (from
    /// <paramref name="wheels"/>, as the step's first stage recorded it) and, with <paramref name="floats"/>, one per
    /// float in the water (<see cref="FloatHydro.Floats"/>, as the step's last float computation left it — read it right
    /// after the integration, before another aeroplane's step overwrites it). <paramref name="s"/> is the body state the
    /// float velocities are taken from. No allocation.
    /// </summary>
    public static void Collect(AircraftConfig c, RigidBodyState s, System.Collections.Generic.IReadOnlyList<WheelState> wheels, bool floats,
        SurfaceContactBuffer into, double timeSec)
    {
        into.Clear();
        into.Step++;
        int n = System.Math.Min(wheels.Count, c.Gear.Count);
        for (int i = 0; i < n; i++)
        {
            WheelState w = wheels[i];
            if (!w.InContact || !(w.LoadN > 0)) continue;
            SurfaceContact k = default;
            k.Source = i;
            k.PositionWorld = w.PatchWorld;
            k.SurfaceWorld = w.SurfaceWorld.LengthSquared > 0 ? w.SurfaceWorld : w.PatchWorld;
            k.PenetrationM = System.Math.Max(0, w.PenetrationM);
            k.SurfaceUpWorld = w.GroundUpWorld.LengthSquared > 0.5 ? w.GroundUpWorld : WorldUp;
            k.HeadingWorld = w.HeadingWorld;
            k.GroundVelocityWorld = w.GroundVelocityWorld;
            k.LoadN = w.LoadN;
            k.Material = w.Material;
            k.Surface = w.Surface;
            k.TimeSec = timeSec;
            GearConfig g = c.Gear[i];
            if (g.GearType == "float-keel")
            {
                // A float's keel and bottom on land or snow (the V mark behind it).
                k.Type = SurfaceContactType.Float;
                k.WidthM = i < into.LegWidthM.Length ? into.LegWidthM[i] : into.FloatBeamM;
                k.SlipWorld = w.GroundVelocityWorld;
            }
            else if (w.OnSki)
            {
                // A wheel-ski on its ski (LandingGear.OnSki). This tip's model has no ski geometry or sinkage: the ski
                // model's default size, and no depth (the ski model's AddSki supplies both).
                k.Type = SurfaceContactType.Ski;
                // The ski the type actually carries, as LandingGear measured it from SkiGear.Geometry, when it has
                // one; the ski model's defaults only for a leg with no geometry (it had no geometry at all when this
                // was written on 2026-11-06, and lane/claude-quest-islandnav brought it on 2026-11-10).
                k.WidthM = w.FootprintWidthM > 0 ? w.FootprintWidthM : DefaultSkiWidthM;
                k.LengthM = w.FootprintLengthM > 0 ? w.FootprintLengthM : DefaultSkiLengthM;
                k.PressurePa = w.GroundPressurePa > 0 ? w.GroundPressurePa : w.LoadN / (DefaultSkiWidthM * DefaultSkiLengthM);
                k.SlipWorld = w.GroundVelocityWorld;
            }
            else if (w.IsWheel)
            {
                k.Type = SurfaceContactType.Tire;
                k.WidthM = w.FootprintWidthM > 0 ? w.FootprintWidthM : i < into.LegWidthM.Length ? into.LegWidthM[i] : 0.0;
                k.LengthM = w.FootprintLengthM;
                k.PressurePa = w.GroundPressurePa;
                k.DepthM = w.SinkM;
                // The rubber's own speed over the ground: fore and aft past the rim, sideways all of it.
                Vec3 right = Vec3.Cross(k.SurfaceUpWorld, k.HeadingWorld);
                double fwdSlip = w.OverWater ? 0.0 : w.VFwdMs - w.RimSpeedMs;
                k.SlipWorld = k.HeadingWorld * fwdSlip + right * w.VSideMs;
                k.SlipPowerW = w.SlipPowerW + w.SidePowerW;
                k.Locked = w.Locked;
            }
            else
            {
                k.Type = g.GearType == "feet" ? SurfaceContactType.Foot : SurfaceContactType.Skid;
                k.WidthM = i < into.LegWidthM.Length ? into.LegWidthM[i] : SkidShoeWidthM;
                k.SlipWorld = w.GroundVelocityWorld;
            }
            k.SnowWetness01 = SnowWetness(k.Material, k.Surface, k.PositionWorld, k.SurfaceUpWorld);
            into.Add(k);
        }
        if (floats && c.Floats != null)
        {
            int slots = c.Floats.Count == 1 ? 1 : 2;   // a hull fills both main slots with one state
            for (int slot = 0; slot < 4; slot++)
            {
                if (slot >= slots && slot < 2) continue;
                if (slot >= 2 && c.Floats.TipFloats == null) break;
                AddFloat(c, s, slot, slot >= 2 ? into.TipFloatBeamM : into.FloatBeamM, into, timeSec);
            }
        }
    }

    private static void AddFloat(AircraftConfig c, RigidBodyState s, int slot, double beamM, SurfaceContactBuffer into, double timeSec)
    {
        FloatHydro.FloatState f = FloatHydro.Floats[slot];
        if (!f.Wet || !(f.WaterUpN > 0)) return;
        SurfaceContact k = default;
        k.Type = SurfaceContactType.Float;
        k.Source = slot;
        k.PositionWorld = f.StepWorld;
        k.SurfaceWorld = f.StepWorld;          // the step IS at the waterline the hydrodynamics found
        k.SurfaceUpWorld = WorldUp;
        Vec3 keel = f.BowWorld - f.SternWorld;
        keel = new Vec3(keel.X, keel.Y, 0);
        double kl = keel.Length;
        k.HeadingWorld = kl > 1e-6 ? keel / kl : FlatHeading(s);
        // The step's velocity through the water (the water's own current taken off, as the hydrodynamics takes it).
        Vec3 rBody = s.Attitude.Conjugate().Rotate(f.StepWorld - s.Position);
        Vec3 v = s.Attitude.Rotate(s.Velocity + Vec3.Cross(s.Rates, rBody));
        v -= WaterCurrent.At(f.StepWorld.X, f.StepWorld.Y, 0.5 * System.Math.Max(0, f.StepDraftM));
        v = new Vec3(v.X, v.Y, 0);
        k.GroundVelocityWorld = v;
        k.SlipWorld = v;
        k.WidthM = beamM;
        k.LengthM = f.Lambda > 0 ? f.Lambda * beamM : 0.0;
        k.DepthM = k.PenetrationM = System.Math.Max(0, f.StepDraftM);
        k.LoadN = f.WaterUpN;
        k.Material = GroundMaterial.Water;
        k.TimeSec = timeSec;
        into.Add(k);
    }

    private static Vec3 FlatHeading(RigidBodyState s)
    {
        Vec3 fwd = s.Attitude.Rotate(new Vec3(1, 0, 0));
        fwd = new Vec3(fwd.X, fwd.Y, 0);
        double l = fwd.Length;
        return l > 1e-6 ? fwd / l : new Vec3(1, 0, 0);
    }

    /// <summary>
    /// The hook for a ski model (lane/claude-ski-physics <c>SkiContact</c>, lane/codex-j3-skis): add one ski leg in
    /// contact, from its own state after the step. Call it after <see cref="Collect"/> in the same step; a wheel that
    /// is on that ski must not also be reported (the ski model's legs are not <see cref="WheelState.OnSki"/> wheels).
    /// Vectors world; <paramref name="velocityWorld"/> the ski's centre-of-pressure velocity over the surface.
    /// </summary>
    public static bool AddSki(SurfaceContactBuffer into, int leg, Vec3 cpWorld, Vec3 surfaceUpWorld, Vec3 skiForwardWorld,
        Vec3 velocityWorld, double widthM, double lengthM, double sinkageM, double normalN, WorldTerrain.Surface surface, double timeSec)
    {
        if (!(normalN > 0)) return false;
        Vec3 up = surfaceUpWorld.LengthSquared > 0.5 ? surfaceUpWorld : WorldUp;
        SurfaceContact k = default;
        k.Type = SurfaceContactType.Ski;
        k.Source = leg;
        k.PositionWorld = cpWorld;
        // A ski model gives its centre of pressure and its sinkage; the undisturbed snow is that much above it.
        k.SurfaceWorld = cpWorld + up * System.Math.Max(0, sinkageM);
        k.SurfaceUpWorld = up;
        k.HeadingWorld = InPlane(skiForwardWorld, up);
        k.GroundVelocityWorld = velocityWorld - up * Vec3.Dot(velocityWorld, up);
        k.SlipWorld = k.GroundVelocityWorld;
        k.WidthM = widthM;
        k.LengthM = lengthM;
        k.DepthM = k.PenetrationM = System.Math.Max(0, sinkageM);
        k.LoadN = normalN;
        k.PressurePa = widthM > 0 && lengthM > 0 ? normalN / (widthM * lengthM) : 0.0;
        k.Surface = surface;
        k.Material = GroundMaterials.Of(surface);
        k.SnowWetness01 = SnowWetness(k.Material, surface, cpWorld, up);
        k.TimeSec = timeSec;
        return into.Add(k);
    }

    /// <summary>A direction laid into the plane across <paramref name="up"/> and normalised; zero if it is along it.</summary>
    public static Vec3 InPlane(Vec3 d, Vec3 up)
    {
        Vec3 p = d - up * Vec3.Dot(d, up);
        double l = p.Length;
        return l > 1e-9 ? p / l : Vec3.Zero;
    }
}
