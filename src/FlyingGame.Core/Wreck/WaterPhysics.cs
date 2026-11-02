namespace FlyingGame.Core.Wreck;

/// <summary>
/// Every number the crash-in-water physics uses, each with where it comes from. Nothing in the wreck/flooding code
/// carries a coefficient of its own: if it is not geometry, mass or a state variable, it is here.
///
/// Things marked ASSUMED are dimensions nobody has measured for these aeroplanes (a door-seal gap, a drain-hole
/// size). They are physical quantities with physical units, not tuning knobs, and they are listed for Ty in
/// AGENT-LOG.md so a measurement can replace each one.
/// </summary>
public static class WaterPhysics
{
    // ---- fluids ------------------------------------------------------------------------------------------------
    /// <summary>Fresh water, kg/m^3 — the same value <see cref="FloatHydro.WaterDensity"/> floats on.</summary>
    public const double RhoWater = FloatHydro.WaterDensity;
    /// <summary>ISA sea-level air density, kg/m^3 (ICAO Doc 7488). Air in a wreck at the surface.</summary>
    public const double RhoAirSeaLevel = 1.225;
    /// <summary>ISA sea-level pressure, Pa (ICAO Doc 7488). The trapped air's reference pressure.</summary>
    public const double AtmospherePa = 101325.0;
    public const double G = Atmosphere.GravityMs2;

    // ---- orifice flow ------------------------------------------------------------------------------------------
    /// <summary>Discharge coefficient of a sharp-edged orifice, Q = Cd·A·sqrt(2·Δp/ρ). 0.61 is the classical
    /// vena-contracta value (e.g. White, Fluid Mechanics, 7th ed., §6.12; Idelchik, Handbook of Hydraulic
    /// Resistance, diagram 4-9). Torn skin, drain holes and seal gaps are all sharp-edged openings.</summary>
    public const double OrificeCd = 0.61;

    // ---- drag (Hoerner, Fluid-Dynamic Drag, 1965) --------------------------------------------------------------
    /// <summary>Flat plate normal to the flow, aspect ratio up to ~5 (Hoerner ch. 3, fig. 22: 1.17-1.2).</summary>
    public const double CdPlateNormal = 1.17;
    /// <summary>Cube face-on (Hoerner ch. 3: 1.05).</summary>
    public const double CdCube = 1.05;
    /// <summary>Flat-faced cylinder in axial flow, length/diameter 2..7 (Hoerner ch. 3: 0.8-0.9).</summary>
    public const double CdAxialBody = 0.85;

    /// <summary>
    /// Pressure-drag coefficient of a box-shaped element moving along one of its axes, from its fineness along that
    /// axis f = (length along the motion) / sqrt(frontal area): a thin plate (f→0) is Hoerner's normal plate, f = 1 is
    /// his cube, f ≥ 2 his axial cylinder; linear between those three published points.
    /// </summary>
    public static double CdForFineness(double f)
    {
        if (f <= 0) return CdPlateNormal;
        if (f <= 1) return CdPlateNormal + (CdCube - CdPlateNormal) * f;
        if (f <= 2) return CdCube + (CdAxialBody - CdCube) * (f - 1);
        return CdAxialBody;
    }

    // ---- materials ---------------------------------------------------------------------------------------------
    /// <summary>2024-T3 aluminium alloy, kg/m^3 (ASM handbook / MMPDS: 2.78 g/cc). Airframe structure.</summary>
    public const double RhoAluminium = 2780.0;
    /// <summary>Carbon/4130 steel, kg/m^3 (MMPDS: 7.85 g/cc). Landing-gear legs.</summary>
    public const double RhoSteel = 7850.0;
    /// <summary>
    /// A piston engine taken as a solid: half aluminium (crankcase, heads, accessory case) and half steel
    /// (crankshaft, cylinders barrels, gears) by mass. Mass-weighted harmonic mean: 1/(0.5/2780 + 0.5/7850) =
    /// 4106 kg/m^3. The crankcase's internal air is a few litres and is ignored. ASSUMED split.
    /// </summary>
    public const double RhoEngine = 1.0 / (0.5 / RhoAluminium + 0.5 / RhoSteel);
    /// <summary>Aviation gasoline, kg/m^3 (ASTM D910 grade 100LL, typical 0.72 kg/L at 15 C).</summary>
    public const double RhoAvgas = 720.0;
    /// <summary>Human body with normal lung volume, kg/m^3 (whole-body density 1.01-1.07 g/cc in
    /// hydrodensitometry; 1.01 is the lean-adult figure with residual lung air, Siri 1961).</summary>
    public const double RhoHuman = 1010.0;
    /// <summary>Baggage: unknown contents, taken as neutrally buoyant. ASSUMED.</summary>
    public const double RhoBaggage = 1000.0;

    // ---- section geometry --------------------------------------------------------------------------------------
    /// <summary>Area of an airfoil section as a fraction of thickness × chord. Integrating the NACA 4-digit
    /// thickness distribution y_t = 5t(0.2969√x − 0.1260x − 0.3516x² + 0.2843x³ − 0.1015x⁴) over the chord gives
    /// exactly 10·t·0.068505 = 0.685·t·c (Abbott and von Doenhoff, Theory of Wing Sections, eq. 6.2). Clark-Y-type
    /// sections are within a few per cent of it.</summary>
    public const double AirfoilAreaFactor = 0.685;

    // ---- empty-weight buildup (Raymer, Aircraft Design: A Conceptual Approach, Table 15.2, general aviation) ----
    /// <summary>Wing structure per exposed planform area: 2.5 lb/ft^2 = 12.2 kg/m^2.</summary>
    public const double RaymerWingKgPerM2 = 2.5 * 4.88243;
    /// <summary>Horizontal and vertical tail per exposed planform area: 2.0 lb/ft^2 = 9.76 kg/m^2.</summary>
    public const double RaymerTailKgPerM2 = 2.0 * 4.88243;
    /// <summary>Fuselage per wetted area: 1.4 lb/ft^2 = 6.84 kg/m^2.</summary>
    public const double RaymerFuselageKgPerM2 = 1.4 * 4.88243;
    /// <summary>Landing gear as a fraction of design gross weight.</summary>
    public const double RaymerGearFraction = 0.057;
    /// <summary>Installed engine = this × dry engine (mount, cowl, baffles, exhaust, accessories).</summary>
    public const double RaymerInstalledEngineFactor = 1.4;
    /// <summary>"All else empty" (systems, avionics, interior) as a fraction of design gross weight.</summary>
    public const double RaymerAllElseFraction = 0.1;

    /// <summary>
    /// Dry mass of a horizontally-opposed piston engine from its rated power. Two-point power law through
    /// Lycoming's published dry weights: O-320 (150 hp, 111.9 kW) 272 lb = 123.4 kg and IO-540-K (300 hp,
    /// 223.7 kW) 443 lb = 200.9 kg, so m = 123.4·(P/111.9 kW)^0.704. Checked against the O-360-A (180 hp,
    /// 134 kW, 285 lb = 129 kg): the law gives 140 kg, 8 % heavy.
    /// </summary>
    public static double PistonEngineDryKg(double powerW)
    {
        if (powerW <= 0) return 0;
        double b = System.Math.Log(200.9 / 123.4) / System.Math.Log(223.7 / 111.9);
        return 123.4 * System.Math.Pow(powerW / 111900.0, b);
    }

    // ---- openings (ASSUMED dimensions; each is a measurable thing) ---------------------------------------------
    /// <summary>Drain hole diameter: 1/4 in (6.35 mm), the common size in light-aircraft skins. ASSUMED.</summary>
    public const double DrainHoleDiaM = 0.00635;
    /// <summary>Rib pitch: one drain hole per rib bay, bays 0.4 m apart. ASSUMED.</summary>
    public const double RibPitchM = 0.4;
    /// <summary>Effective gap of a cabin door seal, m. ASSUMED: 1 mm around the whole door edge.</summary>
    public const double DoorSealGapM = 0.001;
    /// <summary>Fuel-tank vent line and fuel feed line inside diameter: 3/8 in (9.5 mm). ASSUMED.</summary>
    public const double FuelLineDiaM = 0.0095;
    /// <summary>Cowling cooling inlets, total, m^2, for one engine (two ~0.17 × 0.17 m inlets). ASSUMED.</summary>
    public const double CowlInletM2 = 0.06;
    /// <summary>Cowling exit at the bottom rear, m^2, for one engine. ASSUMED equal to the inlets.</summary>
    public const double CowlExitM2 = 0.06;

    public static double CircleArea(double dia) => System.Math.PI * dia * dia * 0.25;

    /// <summary>Watertight compartments per main float: 14 CFR 23.751(b) (pre-amendment 23-64) requires at least
    /// four, of approximately equal volume.</summary>
    public const int FloatCompartments = 4;
}
