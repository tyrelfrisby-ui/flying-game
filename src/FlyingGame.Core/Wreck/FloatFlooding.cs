using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Wreck;

/// <summary>One puncture a lesson or scenario puts in a float: which float (0 = left, 1 = right; a flying-boat hull
/// is 0), which compartment (0 = bow .. 3 = stern), the hole's area, and the area of any way out for the air above it
/// (0 = the compartment is otherwise sealed, so water only squeezes the air and very little gets in).</summary>
public readonly struct FloatPuncture
{
    public readonly int Float, Compartment;
    public readonly double HoleM2, VentM2;
    public readonly string Why;
    public FloatPuncture(int floatIndex, int compartment, double holeM2, double ventM2, string why)
    { Float = floatIndex; Compartment = compartment; HoleM2 = holeM2; VentM2 = ventM2; Why = why; }

    /// <summary>A rock through the keel: a 50 × 40 mm gash, and the compartment's pump-out cup cap knocked off, which
    /// opens its 1 in (25.4 mm) standpipe at the deck so the air can leave.</summary>
    public static FloatPuncture RockStrike(int floatIndex, int compartment) =>
        new(floatIndex, compartment, 0.050 * 0.040, WaterPhysics.CircleArea(0.0254), "rock through the keel, pump-out cap gone");
}

/// <summary>
/// Watertight compartments in seaplane floats. Each main float is <see cref="WaterPhysics.FloatCompartments"/>
/// compartments of equal volume (14 CFR 23.751(b): at least four, approximately equal), cut from FloatHydro's own
/// tapered V-section hull. Undamaged they are sealed: nothing gets in, the float floats for ever. A puncture floods
/// its compartment by the same orifice model as a wreck (<see cref="HydroElement.Flood"/>); the hull still displaces
/// its whole envelope (that is FloatHydro's), and the water inside is carried as weight at the compartment — so the
/// aeroplane settles and lists toward the damaged float.
/// </summary>
public sealed class FloatFlooding
{
    /// <summary>Compartments per float slot (0 left, 1 right; a hull uses 0 only), bow first.</summary>
    public readonly List<HydroElement>[] Floats;
    private readonly Vec3 _cg;
    private readonly List<HydroElement> _all = new();

    public FloatFlooding(AircraftConfig c)
    {
        FloatsConfig f = c.Floats!;
        _cg = c.Mass.CgVec();
        int nFloats = f.Count == 1 ? 1 : 2;
        Floats = new List<HydroElement>[nFloats];
        for (int i = 0; i < nFloats; i++)
        {
            double yF = f.Count == 1 ? 0.0 : (i == 0 ? -0.5 : 0.5) * f.SpreadM;
            Floats[i] = Compartments(f, yF, i);
            _all.AddRange(Floats[i]);
        }
    }

    /// <summary>Full-depth V-section area at a station (the same section FloatHydro integrates its buoyancy over).</summary>
    public static double SectionArea(FloatsConfig f, double x)
    {
        double xStep = f.BowX - f.StepFraction * f.LengthM, xStern = f.BowX - f.LengthM;
        bool fore = x >= xStep;
        double u = fore ? (x - xStep) / System.Math.Max(f.BowX - xStep, 1e-6) : (xStep - x) / System.Math.Max(xStep - xStern, 1e-6);
        (double bF, double dF) = FloatHydro.Taper(f, fore, u);
        double tanB = System.Math.Tan(f.DeadriseDeg * System.Math.PI / 180);
        double bL = f.BeamM * bF, chineL = 0.5 * bL * tanB, d = f.DepthM * dF;
        return d <= chineL ? d * d / tanB : 0.5 * bL * chineL + bL * (d - chineL);
    }

    private static List<HydroElement> Compartments(FloatsConfig f, double yF, int slot)
    {
        const int n = 200;
        double x0 = f.BowX, dx = f.LengthM / n;
        var cum = new double[n + 1];
        for (int i = 0; i < n; i++) cum[i + 1] = cum[i] + SectionArea(f, x0 - (i + 0.5) * dx) * dx;
        double total = cum[n];
        int nc = WaterPhysics.FloatCompartments;
        var list = new List<HydroElement>(nc);
        int start = 0;
        for (int c = 0; c < nc; c++)
        {
            int end = c == nc - 1 ? n : start;
            while (c < nc - 1 && end < n && cum[end] < total * (c + 1) / nc) end++;
            double xa = x0 - start * dx, xb = x0 - end * dx, vol = cum[end] - cum[start];
            var e = new HydroElement($"float{slot}-c{c}", WreckAtom.Float,
                new Vec3(0.5 * (xa + xb), yF, f.KeelZ - 0.5 * f.DepthM), new Vec3(0.5 * (xa - xb), 0.5 * f.BeamM, 0.5 * f.DepthM),
                vol, 0.0, 0.0) { BuoyancyElsewhere = true, NoDrag = true };   // skin volume (~1 %) neglected
            list.Add(e);
            start = end;
        }
        return list;
    }

    /// <summary>Put a hole in a compartment: the hole at its keel, any vent at its deck. Returns false if there is no
    /// such float or compartment.</summary>
    public bool Puncture(FloatPuncture p)
    {
        if (p.Float < 0 || p.Float >= Floats.Length || p.Compartment < 0 || p.Compartment >= Floats[p.Float].Count) return false;
        HydroElement e = Floats[p.Float][p.Compartment];
        if (p.HoleM2 > 0) e.Openings.Add(new Opening(new Vec3(e.Centre.X, e.Centre.Y, e.Centre.Z + e.Half.Z), p.HoleM2, p.Why));
        if (p.VentM2 > 0) e.Openings.Add(new Opening(new Vec3(e.Centre.X, e.Centre.Y, e.Centre.Z - e.Half.Z), p.VentM2, p.Why + " (vent)"));
        return true;
    }

    public IReadOnlyList<HydroElement> All => _all;

    /// <summary>Weight of the water inside, body frame, about the CG.</summary>
    public (Vec3 Force, Vec3 Moment) Forces(RigidBodyState s, double waterUp)
    {
        return HydroBody.Hydrostatics(_all, _cg, s, double.IsNaN(waterUp) ? 0.0 : waterUp);
    }

    public void Flood(RigidBodyState s, double waterUp, double dt)
    {
        foreach (HydroElement e in All) if (e.Openings.Count > 0) e.Flood(_cg, s, waterUp, dt);
    }

    public double WaterKg { get { double w = 0; foreach (HydroElement e in All) w += e.WaterM3; return w * WaterPhysics.RhoWater; } }
    public double WaterKgIn(int slot) { double w = 0; foreach (HydroElement e in Floats[slot]) w += e.WaterM3; return w * WaterPhysics.RhoWater; }
    public double EnvelopeM3 { get { double v = 0; foreach (HydroElement e in All) v += e.EnvelopeM3; return v; } }
    public double TakeEscapedFreeM3() { double s = 0; foreach (HydroElement e in All) s += e.TakeEscaped(); return s; }
    public double TrappedAirM3 { get { double s = 0; foreach (HydroElement e in All) s += e.AirM3; return s; } }
}
