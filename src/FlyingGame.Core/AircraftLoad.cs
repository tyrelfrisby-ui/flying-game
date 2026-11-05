using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>
/// What is in the aeroplane, and the mass, centre of gravity and inertia that follow from it (owner 2026-10-31:
/// "loading the airplane with fuel bags and people then model the resulting weight and cg while they fly").
///
/// THE INVARIANT THAT MAKES THIS SAFE. Every stall speed, book takeoff roll and trim number in the suite is tuned
/// against the authored <see cref="MassConfig"/>. So the reference loading — each station at its
/// <see cref="StationConfig.ReferenceKg"/> — reproduces the authored mass, CG and inertia EXACTLY, not nearly. It
/// does that by construction rather than by careful authoring: the empty airframe's own mass, CG and inertia are
/// SOLVED BACKWARDS from the authored figures and the reference stations, so whatever is authored is by definition
/// what the aeroplane weighs when loaded as authored. An aeroplane with no loading block has no stations at all and
/// comes straight back out with the authored properties untouched.
///
/// Frames: positions are the body reference frame used by <see cref="GearConfig.Pos"/> and
/// <see cref="StripConfig.Pos"/> — +x forward, +y right, +z DOWN — and <see cref="MassConfig.Cg"/> is the CG's
/// position in it. Inertia is about the CG, with Ixz the product of inertia (the tensor carries -Ixz off-diagonal,
/// per <see cref="RigidBody6DOF"/>), so a point mass adds +m·dx·dz to it.
/// </summary>
public sealed class AircraftLoad
{
    /// <summary>A station with the mass currently in it.</summary>
    public sealed class Slot
    {
        public readonly StationConfig Config;
        public readonly StationKind Kind;
        public double Kg;

        internal Slot(StationConfig config, StationKind kind) { Config = config; Kind = kind; Kg = config.ReferenceKg; }

        public string Id => Config.Id;
        public string Label => Config.Label;
        public double MaxKg => Config.MaxKg;
        public Vec3 Station => Config.StationVec();
    }

    public enum StationKind { Seat, Fuel, Baggage }

    private readonly AircraftConfig _config;
    private readonly List<Slot> _slots = new();
    private readonly double _emptyKg;
    private readonly Vec3 _emptyCg;
    private readonly double _i0xx, _i0yy, _i0zz, _i0xz;   // empty airframe, about its own CG
    private readonly bool _degenerate;

    public IReadOnlyList<Slot> Slots => _slots;
    /// <summary>True when the authoring does not add up (reference masses at or above the total) and the authored
    /// mass properties are being reported unchanged rather than nonsense derived from them.</summary>
    public bool Degenerate => _degenerate;
    public double EmptyMassKg => _emptyKg;

    public AircraftLoad(AircraftConfig config)
    {
        _config = config;
        LoadingConfig l = config.Loading ?? new LoadingConfig();
        foreach (StationConfig s in l.Seats) _slots.Add(new Slot(s, StationKind.Seat));
        foreach (StationConfig s in l.Tanks) _slots.Add(new Slot(s, StationKind.Fuel));
        foreach (StationConfig s in l.Baggage) _slots.Add(new Slot(s, StationKind.Baggage));

        double authoredKg = config.Mass.MassKg;
        Vec3 authoredCg = config.Mass.CgVec();
        double refKg = 0.0;
        foreach (Slot s in _slots) refKg += s.Config.ReferenceKg;

        double empty = l.EmptyMassKg > 0.0 ? l.EmptyMassKg : authoredKg - refKg;
        // Authoring that does not add up must not produce a negative empty mass or a divide by zero; report the
        // authored properties unchanged and say so, rather than flying something invented.
        _degenerate = _slots.Count == 0 || empty <= 1.0 || authoredKg <= 0.0;
        if (_degenerate)
        {
            _emptyKg = authoredKg;
            _emptyCg = authoredCg;
            _i0xx = config.Mass.Inertia.Ixx; _i0yy = config.Mass.Inertia.Iyy;
            _i0zz = config.Mass.Inertia.Izz; _i0xz = config.Mass.Inertia.Ixz;
            return;
        }

        _emptyKg = empty;
        // Solve the empty CG so that the reference loading lands on the authored CG:
        //   authoredKg * authoredCg = empty * emptyCg + sum(refKg_i * station_i)
        Vec3 moments = authoredCg * authoredKg;
        foreach (Slot s in _slots) moments -= s.Station * s.Config.ReferenceKg;
        _emptyCg = moments * (1.0 / empty);

        // Solve the empty inertia about the empty CG so that the reference loading lands on the authored inertia
        // about the authored CG.
        double ixx = config.Mass.Inertia.Ixx, iyy = config.Mass.Inertia.Iyy;
        double izz = config.Mass.Inertia.Izz, ixz = config.Mass.Inertia.Ixz;
        Subtract(ref ixx, ref iyy, ref izz, ref ixz, empty, _emptyCg - authoredCg);
        foreach (Slot s in _slots) Subtract(ref ixx, ref iyy, ref izz, ref ixz, s.Config.ReferenceKg, s.Station - authoredCg);
        // Silly authoring can drive a principal moment to or below zero, which would divide by zero in the angular
        // solve. Hold a floor and let a test complain about the authoring instead of the aeroplane exploding.
        const double floor = 1.0;
        _i0xx = Math.Max(floor, ixx); _i0yy = Math.Max(floor, iyy); _i0zz = Math.Max(floor, izz); _i0xz = ixz;
    }

    /// <summary>Parallel-axis contribution of a point mass offset by d from the reference point.</summary>
    private static void Add(ref double ixx, ref double iyy, ref double izz, ref double ixz, double m, Vec3 d)
    {
        ixx += m * (d.Y * d.Y + d.Z * d.Z);
        iyy += m * (d.X * d.X + d.Z * d.Z);
        izz += m * (d.X * d.X + d.Y * d.Y);
        ixz += m * d.X * d.Z;
    }

    private static void Subtract(ref double ixx, ref double iyy, ref double izz, ref double ixz, double m, Vec3 d)
    {
        ixx -= m * (d.Y * d.Y + d.Z * d.Z);
        iyy -= m * (d.X * d.X + d.Z * d.Z);
        izz -= m * (d.X * d.X + d.Y * d.Y);
        ixz -= m * d.X * d.Z;
    }

    // ---- what is loaded ----

    public Slot? Find(string id)
    {
        foreach (Slot s in _slots) if (s.Id == id) return s;
        return null;
    }

    /// <summary>Put this much in a station, clamped to its placard.</summary>
    public void Set(string id, double kg)
    {
        Slot? s = Find(id);
        if (s is null) return;
        s.Kg = Math.Clamp(kg, 0.0, s.MaxKg > 0.0 ? s.MaxKg : kg);
    }

    /// <summary>Back to the loading the aeroplane was authored at — the one that flies exactly as tuned.</summary>
    public void Reset()
    {
        foreach (Slot s in _slots) s.Kg = s.Config.ReferenceKg;
    }

    public double FuelKg
    {
        get { double t = 0; foreach (Slot s in _slots) if (s.Kind == StationKind.Fuel) t += s.Kg; return t; }
    }

    public double FuelCapacityKg
    {
        get { double t = 0; foreach (Slot s in _slots) if (s.Kind == StationKind.Fuel) t += s.MaxKg; return t; }
    }

    public double OccupantsKg
    {
        get { double t = 0; foreach (Slot s in _slots) if (s.Kind == StationKind.Seat) t += s.Kg; return t; }
    }

    public double BaggageKg
    {
        get { double t = 0; foreach (Slot s in _slots) if (s.Kind == StationKind.Baggage) t += s.Kg; return t; }
    }

    /// <summary>Fill every tank to its placard.</summary>
    public void FillFuel()
    {
        foreach (Slot s in _slots) if (s.Kind == StationKind.Fuel) s.Kg = s.MaxKg;
    }

    /// <summary>Take this much fuel out, lowest <see cref="StationConfig.BurnOrder"/> first and evenly within a
    /// group, which is what a selector on BOTH does. Returns how much was actually drawn — less than asked for
    /// means the tanks are dry.</summary>
    public double BurnFuel(double kg)
    {
        if (kg <= 0.0) return 0.0;
        double drawn = 0.0;
        foreach (int order in BurnOrders())
        {
            double want = kg - drawn;
            if (want <= 1e-12) break;
            // Share the draw across the tanks in this group in proportion to what each still holds, so a group
            // empties together instead of one tank going dry first and shifting the CG sideways.
            double available = 0.0;
            foreach (Slot s in _slots) if (s.Kind == StationKind.Fuel && s.Config.BurnOrder == order) available += s.Kg;
            if (available <= 1e-12) continue;
            double take = Math.Min(want, available);
            foreach (Slot s in _slots)
            {
                if (s.Kind != StationKind.Fuel || s.Config.BurnOrder != order || s.Kg <= 0.0) continue;
                s.Kg = Math.Max(0.0, s.Kg - take * (s.Kg / available));
            }
            drawn += take;
        }
        return drawn;
    }

    private List<int> BurnOrders()
    {
        var orders = new List<int>();
        foreach (Slot s in _slots)
            if (s.Kind == StationKind.Fuel && !orders.Contains(s.Config.BurnOrder)) orders.Add(s.Config.BurnOrder);
        orders.Sort();
        return orders;
    }

    // ---- what that weighs and where it balances ----

    public double TotalMassKg
    {
        get { double m = _emptyKg; if (!_degenerate) foreach (Slot s in _slots) m += s.Kg; return m; }
    }

    public Vec3 Cg
    {
        get
        {
            if (_degenerate) return _emptyCg;
            Vec3 moments = _emptyCg * _emptyKg;
            double m = _emptyKg;
            foreach (Slot s in _slots) { moments += s.Station * s.Kg; m += s.Kg; }
            return m > 1e-9 ? moments * (1.0 / m) : _emptyCg;
        }
    }

    /// <summary>Mass, CG and inertia as currently loaded. At the reference loading this is bit-for-bit the authored
    /// <see cref="MassConfig"/>, which is the whole point.</summary>
    public MassProperties Properties
    {
        get
        {
            if (_degenerate)
            {
                return new MassProperties(_emptyKg, _i0xx, _i0yy, _i0zz, _i0xz);
            }

            Vec3 cg = Cg;
            double ixx = _i0xx, iyy = _i0yy, izz = _i0zz, ixz = _i0xz;
            Add(ref ixx, ref iyy, ref izz, ref ixz, _emptyKg, _emptyCg - cg);
            foreach (Slot s in _slots) Add(ref ixx, ref iyy, ref izz, ref ixz, s.Kg, s.Station - cg);
            return new MassProperties(TotalMassKg, Math.Max(1.0, ixx), Math.Max(1.0, iyy), Math.Max(1.0, izz), ixz);
        }
    }

    // ---- is it legal ----

    public double MaxGrossMassKg => _config.Loading?.MaxGrossMassKg ?? 0.0;
    public bool HasWeightPlacard => MaxGrossMassKg > 0.0;
    /// <summary>The gross a placard shows: <see cref="MaxGrossMassKg"/>, else the config's maxTakeoffMassKg. For the
    /// landing page's plot; <see cref="Overweight"/> and <see cref="Placard"/> keep reading MaxGrossMassKg only.</summary>
    public double PlacardGrossKg => MaxGrossMassKg > 0.0 ? MaxGrossMassKg : _config.Loading?.MaxTakeoffMassKg ?? 0.0;
    public bool Overweight => HasWeightPlacard && TotalMassKg > MaxGrossMassKg + 1e-9;

    /// <summary>+x is FORWARD in this frame, so a published envelope has its forward limit ABOVE its aft limit.</summary>
    public bool HasCgEnvelope =>
        _config.Loading is { } l && l.CgForwardLimitM > l.CgAftLimitM;

    /// <summary>The authored CG limits, body x (m, +forward); 0 when the type has no envelope.</summary>
    public double CgForwardLimitM => _config.Loading?.CgForwardLimitM ?? 0.0;
    public double CgAftLimitM => _config.Loading?.CgAftLimitM ?? 0.0;

    public bool CgOutOfLimits
    {
        get
        {
            if (!HasCgEnvelope) return false;
            double x = Cg.X;
            return x > _config.Loading.CgForwardLimitM + 1e-9 || x < _config.Loading.CgAftLimitM - 1e-9;
        }
    }

    /// <summary>One line for the loading page, or null when the aeroplane is legal as loaded.</summary>
    public string? Placard
    {
        get
        {
            if (Overweight) return $"OVER GROSS by {TotalMassKg - MaxGrossMassKg:F0} kg";
            if (CgOutOfLimits)
            {
                double x = Cg.X;
                return x > _config.Loading.CgForwardLimitM ? "CG FORWARD of the limit" : "CG AFT of the limit";
            }
            return null;
        }
    }

    /// <summary>Where the CG sits across its envelope: 0 at the forward limit, 1 at the aft limit. Since +x is
    /// forward, that counts DOWN in x. NaN without an envelope to measure against.</summary>
    public double CgFraction =>
        HasCgEnvelope
            ? (_config.Loading.CgForwardLimitM - Cg.X) / (_config.Loading.CgForwardLimitM - _config.Loading.CgAftLimitM)
            : double.NaN;
}
