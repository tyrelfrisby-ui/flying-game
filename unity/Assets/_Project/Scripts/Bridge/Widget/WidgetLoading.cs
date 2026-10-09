using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using UnityEngine;

namespace FlyingGame.Bridge.Widget
{
    /// <summary>
    /// WEIGHT &amp; BALANCE (protocol 8, owner 2026-10-09: "increase/decrease weight and adjust the CG fore/aft") — live, in every
    /// condition and lesson, the spin included. Out of limits is ALLOWED on purpose (to show why the limits exist).
    ///   CG in % of the wing's mean aerodynamic chord (MAC, from the wing strips); the neutral point from the aero model.
    ///   Weight changes the mass and scales the inertia; a CG shift moves Config.Mass.Cg (every force and moment is taken
    ///   about it) and adds the parallel-axis term to pitch and yaw inertia.
    ///   Changes are slewed (weight 15 %/s, CG 6 % MAC/s) so a dragged slider (~4 updates/s) never jolts the airplane.
    /// Limits are this sim's approximations around the type's default loading (the configs carry no POH envelope):
    /// gross = the config's weight, empty ≈ 62 % of it, CG fwd = default − 9 % MAC, aft = default + 9 % MAC capped 5 % MAC ahead
    /// of the neutral point.
    /// </summary>
    public sealed class WidgetLoading
    {
        private readonly AeroHost _w;
        public WidgetLoading(AeroHost w) { _w = w; }

        public double MacM, LemacX;                // MAC length and its leading edge (body x, config coords; + forward)
        public double DefaultKg, EmptyKg, MaxGrossKg, DefaultCgMac, NpMac, FwdLimitMac, AftLimitMac;
        private double _cg0X; private InertiaConfig _i0;
        public double Cg0X => _cg0X;
        public double TargetKg, TargetCgMac;
        public double Kg => _w.Config.Mass.MassKg;
        public double CgMac => XToMac(_w.Config.Mass.Cg[0]);

        public double XToMac(double x) => (LemacX - x) / MacM * 100.0;
        public double MacToX(double pct) => LemacX - pct / 100.0 * MacM;

        /// <summary>A new aircraft: its geometry, default loading and limits.</summary>
        public void Configure(AircraftConfig cfg, bool keepCurrent, double keepKg, double keepCgMac)
        {
            double sc = 0, sc2 = 0, sle = 0;
            foreach (var sf in cfg.Surfaces)
            {
                string id = sf.Id.ToLowerInvariant();
                if (!id.Contains("wing")) continue;
                foreach (var st in sf.Strips)
                {
                    double c = st.Chord, span = st.Area / System.Math.Max(1e-6, c);
                    sc += c * span; sc2 += c * c * span; sle += (st.Pos[0] + 0.25 * c) * c * span;   // Pos is the quarter-chord
                }
            }
            MacM = sc > 0 ? sc2 / sc : 1.5; LemacX = sc > 0 ? sle / sc : cfg.Mass.Cg[0] + 0.375;
            DefaultKg = cfg.Mass.MassKg; EmptyKg = 0.62 * DefaultKg; MaxGrossKg = DefaultKg;
            _cg0X = cfg.Mass.Cg[0];
            _i0 = new InertiaConfig { Ixx = cfg.Mass.Inertia.Ixx, Iyy = cfg.Mass.Inertia.Iyy, Izz = cfg.Mass.Inertia.Izz, Ixz = cfg.Mass.Inertia.Ixz };
            DefaultCgMac = XToMac(_cg0X);
            NpMac = XToMac(_w.NeutralPointBody().X);
            FwdLimitMac = DefaultCgMac - 9.0;
            AftLimitMac = System.Math.Min(DefaultCgMac + 9.0, NpMac - 5.0);
            TargetKg = keepCurrent ? keepKg : DefaultKg;
            TargetCgMac = keepCurrent ? keepCgMac : DefaultCgMac;
            if (keepCurrent) ApplyNow();
        }

        public void Set(double? kg, double? cgMac, bool reset)
        {
            if (reset) { TargetKg = DefaultKg; TargetCgMac = DefaultCgMac; return; }
            if (kg.HasValue) TargetKg = System.Math.Clamp(kg.Value, 0.4 * DefaultKg, 2.0 * DefaultKg);
            if (cgMac.HasValue) TargetCgMac = System.Math.Clamp(cgMac.Value, DefaultCgMac - 40, DefaultCgMac + 40);
        }

        /// <summary>Slew toward the targets (call every live step).</summary>
        public void Tick(float dt)
        {
            var m = _w.Config.Mass;
            double kg = m.MassKg, cg = CgMac;
            double nk = Mathf.MoveTowards((float)kg, (float)TargetKg, (float)(0.15 * DefaultKg * dt));
            double nc = Mathf.MoveTowards((float)cg, (float)TargetCgMac, 6f * dt);
            if (System.Math.Abs(nk - kg) < 1e-6 && System.Math.Abs(nc - cg) < 1e-6) return;
            Apply(nk, nc);
        }

        public void ApplyNow() => Apply(TargetKg, TargetCgMac);

        private void Apply(double kg, double cgMac)
        {
            var m = _w.Config.Mass;
            double x = MacToX(cgMac), d = x - _cg0X, r = kg / DefaultKg;
            m.MassKg = kg; m.Cg[0] = x;
            // Inertia: scales with weight; a CG moved by moving mass adds roughly a quarter of m·d² in pitch and yaw.
            m.Inertia.Ixx = _i0.Ixx * r; m.Inertia.Iyy = _i0.Iyy * r + 0.25 * kg * d * d; m.Inertia.Izz = _i0.Izz * r + 0.25 * kg * d * d; m.Inertia.Ixz = _i0.Ixz * r;
            _w.Ac?.SetMassProperties(kg, m.Inertia.Ixx, m.Inertia.Iyy, m.Inertia.Izz, m.Inertia.Ixz);
        }

        public double StaticMarginMac => NpMac - CgMac;
        public bool Overweight => Kg > MaxGrossKg * 1.0005;
        public string CgOut => CgMac < FwdLimitMac - 0.05 ? "fwd" : CgMac > AftLimitMac + 0.05 ? "aft" : null;
        public bool WithinLimits => !Overweight && CgOut == null && Kg >= EmptyKg;
    }
}
