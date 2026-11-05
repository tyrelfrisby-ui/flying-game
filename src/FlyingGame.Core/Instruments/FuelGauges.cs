using System;

namespace FlyingGame.Core.Instruments
{
    /// <summary>
    /// A FLOAT GAUGE IN THE TANK (Ty 2026-11-05 ADDENDUM: "CUBS (J-3 and Super Cub): the fuel wire gauge must actually
    /// MOVE and show fuel quantity (the wire on the cork float rises and falls with the fuel actually in the tank, live
    /// from the fuel system)"). The cork floats on the fuel and the wire on it shows above the cap; the J-3's nose tank is
    /// near enough a box, so the level, and with it the wire, is proportional to the fuel in it.
    /// </summary>
    public static class FuelGauges
    {
        /// <summary>The fraction of <paramref name="tankId"/> (or, with null, of the first fuel tank) that is fuel, 0..1,
        /// from the live loading the sim burns from (Aircraft.UpdateLoad → AircraftLoad.BurnFuel). An aeroplane whose loading
        /// carries no fuel at all (the reference loading on most types, where the engine runs anyway) reads FULL, as the
        /// panel's other fuel gauges do (CockpitRigState: an empty loading is not shown as empty tanks under a running
        /// engine).</summary>
        public static double TankFraction(AircraftLoad load, string tankId = null)
        {
            if (load == null || load.FuelKg <= 1e-6) return 1.0;
            foreach (AircraftLoad.Slot s in load.Slots)
            {
                if (s.Kind != AircraftLoad.StationKind.Fuel) continue;
                if (tankId != null && s.Id != tankId) continue;
                return s.MaxKg > 1e-9 ? Math.Clamp(s.Kg / s.MaxKg, 0.0, 1.0) : 1.0;
            }
            return 1.0;
        }
    }
}
