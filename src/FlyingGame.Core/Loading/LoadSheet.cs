using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Loading;

/// <summary>
/// What is actually in the aeroplane right now: who is in which seat, what is in the baggage hold, how much fuel is
/// in each tank. One of these per flight.
///
/// It computes the mass, CG and inertia and WRITES THEM INTO THE AIRCRAFT'S OWN CONFIG. That is deliberate and it
/// is safe: <c>UnityAircraftConfigLoader.LoadFromStreamingAssets</c> deserializes a fresh instance on every call and
/// the session already mutates it per flight (bushwheels, skis), and the CG is read in ninety-odd places across
/// forty-odd files — threading a load sheet through all of them would be a far bigger and more fragile change than
/// putting the answer where they already look.
///
/// One cache in the whole tree depended on the CG and would have gone stale when it moved: AeroModel's wing height
/// above the CG, which is the ground-effect geometry. It now caches the wing's own body z and subtracts the live CG
/// at the point of use, so this cannot bite. Nothing else keyed on a config touches mass.
/// </summary>
public sealed class LoadSheet
{
    private readonly AircraftConfig _config;
    private readonly LoadingConfig _loading;
    private readonly Dictionary<string, double> _stationKg = new();
    private readonly Dictionary<string, double> _tankKg = new();

    /// <summary>Total fuel burned this flight, kg — for the fuel-used counter, not the physics.</summary>
    public double FuelBurnedKg { get; private set; }

    public LoadSheet(AircraftConfig config)
    {
        _config = config;
        _loading = config.Loading ?? throw new InvalidOperationException(
            $"{config.Id} has no loading block, so it cannot be loaded. Check Loadable first.");
        Reset();
    }

    /// <summary>True for a type that has weight and balance data at all. Most do not, and fly at their tuned mass.</summary>
    public static bool Loadable(AircraftConfig c) => c.Loading is not null && c.Loading.EmptyMassKg > 0;

    public LoadingConfig Loading => _loading;
    public IReadOnlyList<StationConfig> Stations => _loading.Stations;
    public IReadOnlyList<TankConfig> Tanks => _loading.Tanks;

    /// <summary>Back to the type's default load — which by construction is the mass and CG it has always flown at.</summary>
    public void Reset()
    {
        _stationKg.Clear();
        _tankKg.Clear();
        foreach (StationConfig s in _loading.Stations) _stationKg[s.Id] = s.DefaultKg;
        foreach (TankConfig t in _loading.Tanks) _tankKg[t.Id] = t.DefaultKg;
        FuelBurnedKg = 0;
        Apply();
    }

    public double StationKg(string id) => _stationKg.TryGetValue(id, out double kg) ? kg : 0;
    public double TankKg(string id) => _tankKg.TryGetValue(id, out double kg) ? kg : 0;

    /// <summary>Put a mass in a station, clamped to what it will hold. Returns what actually went in.</summary>
    public double SetStation(string id, double kg)
    {
        StationConfig? s = _loading.Stations.FirstOrDefault(x => x.Id == id);
        if (s is null) return 0;
        double v = Math.Clamp(double.IsNaN(kg) ? 0 : kg, 0, s.MaxKg);
        _stationKg[id] = v;
        Apply();
        return v;
    }

    /// <summary>Put fuel in a tank, clamped to its usable capacity. Returns what actually went in, kg.</summary>
    public double SetTankKg(string id, double kg)
    {
        TankConfig? t = _loading.Tanks.FirstOrDefault(x => x.Id == id);
        if (t is null) return 0;
        double v = Math.Clamp(double.IsNaN(kg) ? 0 : kg, 0, t.CapacityL * t.DensityKgPerL);
        _tankKg[id] = v;
        Apply();
        return v;
    }

    /// <summary>Fill or drain every tank to the same fraction of capacity.</summary>
    public void SetFuelFraction(double fraction)
    {
        double f = Math.Clamp(double.IsNaN(fraction) ? 0 : fraction, 0, 1);
        foreach (TankConfig t in _loading.Tanks) _tankKg[t.Id] = f * t.CapacityL * t.DensityKgPerL;
        Apply();
    }

    public double TotalFuelKg => _loading.Tanks.Sum(t => TankKg(t.Id));
    public double UsableFuelCapacityKg => _loading.Tanks.Sum(t => t.CapacityL * t.DensityKgPerL);

    /// <summary>
    /// Burn fuel, taken from every selectable tank in proportion to what is in it. That is the "both" selector of a
    /// high-wing single and the crossfeed-balanced twin: the wings stay symmetric, so the lateral CG does not drift
    /// on its own, and an asymmetric load the pilot chose stays asymmetric in the same proportion. A per-tank
    /// selector can come later and will only change which tanks are in this list.
    ///
    /// Iterates, because a tank that runs dry mid-burn has to hand its share back to the others. Returns what was
    /// actually burned; less than asked means the tanks are dry, which is fuel exhaustion and the engine's problem.
    /// </summary>
    public double BurnFuelKg(double kg)
    {
        if (kg <= 0 || double.IsNaN(kg)) return 0;
        double left = kg;
        for (int pass = 0; pass < 32 && left > 1e-9; pass++)
        {
            double available = _loading.Tanks.Where(t => t.Selectable).Sum(t => TankKg(t.Id));
            if (available <= 1e-9) break;
            double share = Math.Min(left, available) / available;    // fraction of every tank's contents to take
            double taken = 0;
            foreach (TankConfig t in _loading.Tanks)
            {
                if (!t.Selectable) continue;
                double have = TankKg(t.Id);
                double take = Math.Min(have, have * share);
                _tankKg[t.Id] = have - take;
                taken += take;
            }
            if (taken <= 1e-12) break;
            left -= taken;
        }
        double burned = kg - left;
        FuelBurnedKg += burned;
        Apply();
        return burned;
    }

    /// <summary>The computed mass, CG and inertia of the aeroplane as loaded.</summary>
    public WeightAndBalance.Result Compute() => WeightAndBalance.Solve(_loading, Items());

    private IEnumerable<(Vec3 Arm, double MassKg)> Items()
    {
        foreach (StationConfig s in _loading.Stations)
            yield return (new Vec3(s.Arm[0], s.Arm[1], s.Arm[2]), StationKg(s.Id));
        foreach (TankConfig t in _loading.Tanks)
            yield return (new Vec3(t.Arm[0], t.Arm[1], t.Arm[2]), TankKg(t.Id));
    }

    /// <summary>Percent MAC of the loaded CG — the x axis of the envelope chart.</summary>
    public double PercentMac => WeightAndBalance.PercentMac(_config, Compute().Cg.X);

    /// <summary>Is the aeroplane legal as loaded: inside the CG envelope and not over gross.</summary>
    public bool InEnvelope
    {
        get
        {
            WeightAndBalance.Result r = Compute();
            return r.MassKg <= _loading.MaxTakeoffMassKg + 1e-6
                   && WeightAndBalance.InEnvelope(_loading, WeightAndBalance.PercentMac(_config, r.Cg.X), r.MassKg);
        }
    }

    public bool OverGross => Compute().MassKg > _loading.MaxTakeoffMassKg + 1e-6;

    /// <summary>
    /// Writes the computed mass, CG and inertia into the config, so every existing read site flies the loaded
    /// aeroplane. Called by every change above; an <see cref="FlyingGame.Sim"/> aircraft also needs its own
    /// <c>SetMassProperties</c> called, because it copies mass and inertia once at construction.
    /// </summary>
    public WeightAndBalance.Result Apply()
    {
        WeightAndBalance.Result r = Compute();
        _config.Mass.MassKg = r.MassKg;
        _config.Mass.Cg = new[] { r.Cg.X, r.Cg.Y, r.Cg.Z };
        _config.Mass.Inertia = new InertiaConfig { Ixx = r.Ixx, Iyy = r.Iyy, Izz = r.Izz, Ixz = r.Ixz };
        return r;
    }
}
