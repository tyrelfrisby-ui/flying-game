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

    // ---- crash damage: what the skin, the glazing and the door latches can carry ------------------------------
    /// <summary>2024-T3 sheet ultimate tensile strength, Pa: 64 ksi (MMPDS-01 Table 3.2.3.0(b), clad sheet
    /// 0.010-0.062 in, A-basis 62-64 ksi).</summary>
    public const double Al2024FtuPa = 64000 * 6894.757;
    /// <summary>Aluminium alloy Young's modulus, Pa: 10.5 Msi (MMPDS-01, 2024-T3).</summary>
    public const double Al2024EPa = 10.5e6 * 6894.757;
    /// <summary>Fuselage and wing skin gauge, m: 0.025 in. ASSUMED — light-aircraft skins run 0.016-0.032 in (Cessna
    /// 100-series fuselage skins are mostly 0.020-0.025 in, thicker round doors and the belly).</summary>
    public const double SkinThicknessM = 0.025 * 0.0254;
    /// <summary>Stringer pitch on a fuselage skin, m: the short span of a skin panel. ASSUMED 0.20 m.</summary>
    public const double FuselageStringerPitchM = 0.20;
    /// <summary>Solid rivet MS20470AD4 (1/8 in, 2117-T3) single-shear ultimate, N: 388 lbf (MMPDS-01 Table
    /// 8.1.2.1(b)).</summary>
    public const double RivetAd4ShearN = 388 * 4.448222;
    /// <summary>Rivet pitch in a skin seam, m: 6 diameters of a 1/8 in rivet. ASSUMED (AC 43.13-1B §4-58: pitch not
    /// less than 3 D; 6-8 D is the usual seam).</summary>
    public const double RivetPitchM = 6 * 0.125 * 0.0254;
    /// <summary>Fabric covering: minimum new tensile strength of Grade A cotton, 80 lb/in = 14.0 kN/m (TSO-C15 /
    /// AC 43.13-1B chapter 2, fabric covering). Polyester covering is stronger; this is the floor the rule sets.</summary>
    public const double FabricStrengthNPerM = 80 * 4.448222 / 0.0254;
    /// <summary>Strain of a doped fabric covering at break. ASSUMED 0.10 (cotton and polyester aircraft fabrics break
    /// at roughly 10-20 % elongation).</summary>
    public const double FabricBreakStrain = 0.10;
    /// <summary>Span of a fabric panel between ribs/stringers, m. ASSUMED 0.30 m.</summary>
    public const double FabricPanelSpanM = 0.30;
    /// <summary>Cast acrylic glazing (MIL-PRF-5425 / Plexiglas G) tensile strength, Pa: 10,000 psi.</summary>
    public const double AcrylicFtuPa = 10000 * 6894.757;
    /// <summary>Cast acrylic Young's modulus, Pa: 450,000 psi. Acrylic is linear to fracture, so its failure strain is
    /// Ftu/E = 0.022.</summary>
    public const double AcrylicEPa = 450000 * 6894.757;
    /// <summary>Windshield / canopy thickness, m: 3/16 in. ASSUMED (light-aircraft windshields are 1/8-1/4 in).</summary>
    public const double GlazingThicknessM = 0.1875 * 0.0254;
    /// <summary>Cabin door latch: two 1/4 in steel pins in single shear, 4130 Fsu 57 ksi (MMPDS-01 Table 2.3.1.0(b),
    /// Ftu 95 ksi normalised). 2 × π/4·(6.35 mm)²·393 MPa = 24.9 kN. ASSUMED two pins.</summary>
    public const double DoorLatchN = 2 * System.Math.PI * 0.25 * (0.25 * 0.0254) * (0.25 * 0.0254) * 57000 * 6894.757;

    /// <summary>
    /// Longitudinal added-mass coefficient of a prolate spheroid of length/diameter <paramref name="fineness"/>, as a
    /// fraction of the water it displaces (Lamb, Hydrodynamics, 6th ed., §373; Newman, Marine Hydrodynamics, §4.13):
    /// k1 = α0/(2 − α0), α0 = 2(1 − e²)/e³·(½·ln((1 + e)/(1 − e)) − e), e = √(1 − 1/f²). A sphere (f = 1) gives 0.5,
    /// f = 7 gives 0.036. A fuselage moving along its axis pushes aside only this much water — not the flat plate the
    /// strip formula would make of each section's face.
    /// </summary>
    public static double LambAxialCoefficient(double fineness)
    {
        if (fineness <= 1.0001) return 0.5;
        double e = System.Math.Sqrt(1 - 1 / (fineness * fineness));
        double a0 = 2 * (1 - e * e) / (e * e * e) * (0.5 * System.Math.Log((1 + e) / (1 - e)) - e);
        return a0 / (2 - a0);
    }

    /// <summary>Emergency-landing forward ultimate inertia factor, g: 9.0 (14 CFR 23.561(b)(3), pre-amendment 23-64;
    /// CAR 3.386). The fuselage joints are taken to carry what is beyond them at this along the fuselage.</summary>
    public const double EmergencyForwardG = 9.0;

    /// <summary>
    /// Slamming coefficient of a smooth circular cylinder entering calm water, Cs = 5.15 (DNV-RP-C205, Environmental
    /// conditions and environmental loads, 2010, §8.3: slamming force per unit length F = ½ρ·Cs·D·v²). The mean slam
    /// pressure over the entering face is then p = ½ρ·Cs·v². A fuselage is a cylinder; a wing's flat underside would
    /// slam harder, so this is the gentler end for the lifting surfaces.
    /// </summary>
    public const double SlamCoefficient = 5.15;

    /// <summary>
    /// Lateral pressure that bursts a skin panel, Pa: the panel is a membrane strip of span b held at its edges
    /// (rivet rows or rib stitching) that sags into a circular arc. At failure its tension per unit length is T and its
    /// strain ε; an arc whose length exceeds its chord by ε has half-angle θ with θ/sinθ = 1 + ε, so θ ≈ √(6ε); and a
    /// membrane under pressure p in an arc of radius R = b/(2 sinθ) carries T = p·R (Roark's Formulas for Stress and
    /// Strain, membrane / thin cylinder). So p = 2·T·sinθ / b.
    /// </summary>
    public static double MembraneBurstPa(double tensionNPerM, double strain, double spanM)
    {
        if (tensionNPerM <= 0 || spanM <= 0) return double.PositiveInfinity;
        double th = System.Math.Sqrt(6 * System.Math.Max(strain, 1e-9));
        return 2 * tensionNPerM * System.Math.Sin(System.Math.Min(th, System.Math.PI / 2)) / spanM;
    }

    /// <summary>Riveted 2024-T3 skin of <see cref="SkinThicknessM"/>: the rivet row fails first (T = 388 lbf per 3/4 in
    /// = 90.6 kN/m against the sheet's 280 kN/m), at an elastic strain T/(E·t).</summary>
    public static double MetalSkinBurstPa(double spanM)
    {
        double tSheet = Al2024FtuPa * SkinThicknessM, tRivets = RivetAd4ShearN / RivetPitchM;
        double t = System.Math.Min(tSheet, tRivets);
        return MembraneBurstPa(t, t / (Al2024EPa * SkinThicknessM), spanM);
    }

    /// <summary>Doped fabric covering over a steel-tube or wooden structure.</summary>
    public static double FabricSkinBurstPa(double spanM) => MembraneBurstPa(FabricStrengthNPerM, FabricBreakStrain, spanM);

    /// <summary>Acrylic glazing spanning <paramref name="spanM"/>, at its own fracture strength.</summary>
    public static double GlazingBurstPa(double spanM) => MembraneBurstPa(AcrylicFtuPa * GlazingThicknessM, AcrylicFtuPa / AcrylicEPa, spanM);

    /// <summary>Water pressure on a face whose outward normal advances into the water at <paramref name="vn"/> m/s:
    /// the slam pressure ½ρ·Cs·v² while the face is still going in (partly wet), the stagnation pressure ½ρ·v² once it
    /// is all under.</summary>
    public static double FacePressurePa(double vn, bool entering) =>
        vn <= 0 ? 0 : 0.5 * RhoWater * (entering ? SlamCoefficient : 1.0) * vn * vn;

    /// <summary>Watertight compartments per main float: 14 CFR 23.751(b) (pre-amendment 23-64) requires at least
    /// four, of approximately equal volume.</summary>
    public const int FloatCompartments = 4;
}
