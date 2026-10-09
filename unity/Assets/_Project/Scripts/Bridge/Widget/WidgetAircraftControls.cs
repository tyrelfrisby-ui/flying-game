using FlyingGame.Core.DataContracts;

namespace FlyingGame.Bridge.Widget
{
    /// <summary>
    /// Each fleet type's cockpit controls for the widget's NTSB-style display (protocol 4, owner 2026-10-08): the flap
    /// handle's own detents (degrees, as the placard reads), how long the flaps take to run full travel, whether it has
    /// spoilers / speed brakes (only types whose config models the panels), fixed or retractable gear, engine levers.
    /// The handle position is a fraction of full travel (detent ÷ the last detent); the sim's flap model takes that fraction.
    /// </summary>
    public static class WidgetAircraftControls
    {
        public const double GearTransitSec = 6.0;

        public sealed class Spec
        {
            public double[] Flaps = { 0 };     // detents in degrees; { 0 } = no flaps
            public double FlapTravelSec = 8;   // full travel
            public bool Spoilers;
            public bool Retractable;
            public int Engines = 1;            // 0 = glider (no throttle)
            /// <summary>The normal approach flap setting (fraction of full travel): full flaps on the light types (C172 30°),
            /// 30 on the 737 (40 is the short-field setting). 0 = no flaps.</summary>
            public double ApproachFlaps;
            /// <summary>A stick shaker rather than a stall horn (the jets).</summary>
            public bool StickShaker;

            /// <summary>Snap a handle fraction 0…1 to the nearest detent (returned as a fraction of full travel).</summary>
            public double Snap(double frac)
            {
                if (Flaps.Length < 2) return 0;
                double full = Flaps[^1], deg = System.Math.Clamp(frac, 0, 1) * full, best = Flaps[0];
                foreach (double d in Flaps) if (System.Math.Abs(d - deg) < System.Math.Abs(best - deg)) best = d;
                return best / full;
            }
            public string Label(double frac) => Flaps.Length < 2 ? "N/A" : frac < 1e-3 ? "UP" : $"{frac * Flaps[^1]:0}";
        }

        public static readonly Spec None = new();

        /// <summary>Real detents (°) and full-travel times. Electric GA flaps ~2–3 s per 10°; Johnson bars and manual
        /// handles ~1.5 s; the 737's trailing-edge flaps run 0 → 40 in about 40 s.</summary>
        private static (double[] detents, double travelSec) Flaps(string id) => id switch
        {
            "c172-like" => (new double[] { 0, 10, 20, 30 }, 9),
            "pa28-archer-like" => (new double[] { 0, 10, 25, 40 }, 1.5),          // manual handle
            "seminole-like" => (new double[] { 0, 10, 25, 40 }, 1.5),             // manual handle
            "cirrus-sr22-like" => (new double[] { 0, 16, 32 }, 8),               // 0 / 50 % / 100 %
            "boeing-737-like" => (new double[] { 0, 1, 2, 5, 10, 15, 25, 30, 40 }, 40),
            "dc3-like" => (new double[] { 0, 15, 30, 45 }, 15),                  // hydraulic
            "pa18-cub-like" or "pa18-bush-like" or "pa18-floats-like" => (new double[] { 0, 25, 50 }, 1.5),
            "pa25-pawnee-like" => (new double[] { 0, 15, 30 }, 1.5),
            "dhc2-beaver-floats-like" => (new double[] { 0, 15, 30, 58 }, 6),    // hand-pumped hydraulic
            "aircam-like" or "aircam-amphib-like" => (new double[] { 0, 15, 30 }, 6),
            "f86-sabre-like" => (new double[] { 0, 20, 38 }, 8),
            "glasair3-like" => (new double[] { 0, 20, 40 }, 8),
            "p51d-like" => (new double[] { 0, 10, 20, 30, 40, 50 }, 10),
            "hughes-h4-like" => (new double[] { 0, 20, 40 }, 30),
            _ => (new double[] { 0, 10, 20, 30 }, 8),
        };

        private static bool HasFlaps(AircraftConfig cfg)
        {
            foreach (var sf in cfg.Surfaces) foreach (var st in sf.Strips) if (st.Flap != null) return true;
            return false;
        }

        public static Spec For(string id, AircraftConfig cfg)
        {
            var s = new Spec
            {
                Spoilers = cfg.Controls?.Spoiler != null && cfg.Controls.Spoiler.MaxDeflRad > 0,
                Retractable = cfg.RetractableGear,
                Engines = cfg.Propulsion == null ? 0 : System.Math.Max(1, cfg.Engines.Count),
            };
            if (HasFlaps(cfg)) (s.Flaps, s.FlapTravelSec) = Flaps(id);
            s.StickShaker = id == "boeing-737-like";
            if (s.Flaps.Length > 1) s.ApproachFlaps = id == "boeing-737-like" ? 30.0 / 40.0 : 1.0;
            return s;
        }
    }
}
