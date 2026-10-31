using System.Collections.Generic;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.Loading;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// What the player has put in the aeroplane, kept per type for as long as the game is running.
    ///
    /// The load sheet writes its answer into the aircraft's own config, so every force and moment follows it with no
    /// further plumbing. Two things do need doing by hand, and this is the one place that remembers to do both:
    ///
    ///   1. <see cref="FlyingGame.Sim.Aircraft"/> copies mass and inertia ONCE at construction, so a change after
    ///      take-off has to be pushed in with <c>SetMassProperties</c>. The CG does not: it is read live from the
    ///      config on every step.
    ///   2. A respawn builds a fresh config, which starts at the type's defaults again — so the chosen load is kept
    ///      here and re-applied in <see cref="FlightSimDriver"/>'s spawn.
    ///
    /// The defaults are the loading each type has always flown at (see docs/WEIGHT-AND-BALANCE.md), so a player who
    /// never opens the load sheet gets exactly the aeroplane that was tuned and tested. Nothing is persisted across
    /// launches on purpose: a fresh start is the known-good aeroplane.
    /// </summary>
    public static class SessionLoading
    {
        private static readonly Dictionary<string, Dictionary<string, double>> Stations = new();
        private static readonly Dictionary<string, Dictionary<string, double>> Tanks = new();

        /// <summary>The live sheet for the aeroplane now spawned, or null for a type with no loading data.</summary>
        public static LoadSheet Current { get; private set; }

        /// <summary>
        /// Build the sheet for a freshly loaded config, restore whatever the player chose for this type, and write the
        /// result into the config. Call it after the config is loaded and BEFORE the Aircraft is constructed.
        /// </summary>
        public static LoadSheet Apply(AircraftConfig cfg)
        {
            if (cfg == null || !LoadSheet.Loadable(cfg)) { Current = null; return null; }
            var sheet = new LoadSheet(cfg);                 // the type's default load
            if (Stations.TryGetValue(cfg.Id, out var st))
                foreach (var kv in st) sheet.SetStation(kv.Key, kv.Value);
            if (Tanks.TryGetValue(cfg.Id, out var tk))
                foreach (var kv in tk) sheet.SetTankKg(kv.Key, kv.Value);
            Current = sheet;
            return sheet;
        }

        private static Dictionary<string, double> Slot(Dictionary<string, Dictionary<string, double>> d, string id)
        {
            if (!d.TryGetValue(id, out var inner)) d[id] = inner = new Dictionary<string, double>();
            return inner;
        }

        public static void SetStation(string stationId, double kg, FlightSimDriver driver)
        {
            if (Current == null) return;
            double got = Current.SetStation(stationId, kg);
            Slot(Stations, Current.Config.Id)[stationId] = got;
            Push(driver);
        }

        public static void SetTank(string tankId, double kg, FlightSimDriver driver)
        {
            if (Current == null) return;
            double got = Current.SetTankKg(tankId, kg);
            Slot(Tanks, Current.Config.Id)[tankId] = got;
            Push(driver);
        }

        public static void SetFuelFraction(double fraction, FlightSimDriver driver)
        {
            if (Current == null) return;
            Current.SetFuelFraction(fraction);
            var slot = Slot(Tanks, Current.Config.Id);
            foreach (TankConfig t in Current.Tanks) slot[t.Id] = Current.TankKg(t.Id);
            Push(driver);
        }

        /// <summary>Back to the loading the type was tuned at, and forget the player's choice for it.</summary>
        public static void ResetToDefault(FlightSimDriver driver)
        {
            if (Current == null) return;
            string id = Current.Config.Id;
            Stations.Remove(id);
            Tanks.Remove(id);
            Current.Reset();
            Push(driver);
        }

        /// <summary>Hand the new mass and inertia to the flying aeroplane; the CG it already reads live.</summary>
        private static void Push(FlightSimDriver driver)
        {
            WeightAndBalance.Result r = Current.Apply();
            driver?.Sim?.Aircraft?.SetMassProperties(r.MassKg, r.Ixx, r.Iyy, r.Izz, r.Ixz);
        }
    }
}
