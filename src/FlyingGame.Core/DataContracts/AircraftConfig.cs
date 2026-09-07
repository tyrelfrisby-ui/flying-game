namespace FlyingGame.Core.DataContracts;

/// <summary>
/// Plain-data mirror of the AircraftConfig JSON schema (see docs/DATA-CONTRACTS.md). The physics reads
/// ONLY this — no aircraft-specific C#. Every field added after schemaVersion 1 must be optional with a
/// sensible default so old files keep loading.
/// </summary>
public sealed class AircraftConfig
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";

    public MassConfig Mass { get; set; } = new();
    public List<SurfaceConfig> Surfaces { get; set; } = new();
    public Dictionary<string, AirfoilTableData> AirfoilTables { get; set; } = new();
    public ControlsConfig Controls { get; set; } = new();
    public FuselageConfig Fuselage { get; set; } = new();
    public PropulsionConfig? Propulsion { get; set; }

    /// <summary>Time constants of separation dynamics (optional; defaults = tuned values). The
    /// oscillation damping of the deep spin lives here, not in gains/areas.</summary>
    public StallDynamicsConfig StallDynamics { get; set; } = new();

    /// <summary>
    /// Max fraction of dynamic pressure the tail loses when fully inside a fully-stalled wing's wake
    /// (tail blanketing — the mechanism that lets a spin's nose ride high). 0 disables. Optional,
    /// schema-safe default.
    /// </summary>
    public double WakeBlanketMaxLoss { get; set; } = 0.7;
    public List<GearConfig> Gear { get; set; } = new();
    public LimitsConfig Limits { get; set; } = new();
}

public sealed class MassConfig
{
    public double MassKg { get; set; }
    public double[] Cg { get; set; } = { 0, 0, 0 };
    public InertiaConfig Inertia { get; set; } = new();

    public MathTypes.Vec3 CgVec() => new(Cg[0], Cg[1], Cg[2]);
}

public sealed class InertiaConfig
{
    public double Ixx { get; set; }
    public double Iyy { get; set; }
    public double Izz { get; set; }
    public double Ixz { get; set; }
}

public sealed class SurfaceConfig
{
    public string Id { get; set; } = "";
    public List<StripConfig> Strips { get; set; } = new();

    /// <summary>
    /// Oswald span-efficiency factor for this surface's induced drag (Cd_i = Cl²/(π·AR·e)).
    /// Airfoil tables carry PROFILE drag only; induced drag is computed per strip from this.
    /// </summary>
    public double OswaldE { get; set; } = 0.85;
}

public sealed class StripConfig
{
    public double[] Pos { get; set; } = { 0, 0, 0 };
    public double Chord { get; set; }
    public double Area { get; set; }
    public double IncidenceRad { get; set; }
    public double DihedralRad { get; set; }
    public string Airfoil { get; set; } = "";
    public StripControlConfig? Control { get; set; }

    public MathTypes.Vec3 PosVec() => new(Pos[0], Pos[1], Pos[2]);
}

public sealed class StripControlConfig
{
    public string Surface { get; set; } = "";
    public double Gain { get; set; }
}

public sealed class AirfoilTableData
{
    public double[] AlphaRad { get; set; } = System.Array.Empty<double>();
    public double[] Cl { get; set; } = System.Array.Empty<double>();
    public double[] Cd { get; set; } = System.Array.Empty<double>();
    public double[] Cm { get; set; } = System.Array.Empty<double>();
}

public sealed class ControlsConfig
{
    public ControlAxisConfig Aileron { get; set; } = new();
    public ControlAxisConfig Elevator { get; set; } = new();
    public ControlAxisConfig Rudder { get; set; } = new();
    public SpoilerConfig Spoiler { get; set; } = new();
}

public sealed class ControlAxisConfig
{
    public double MaxDeflRad { get; set; }
    public double RateRadPerSec { get; set; } = 1000; // effectively unlimited unless configured
    public double Expo { get; set; }
    public double DeadZone { get; set; }
}

public sealed class SpoilerConfig
{
    public double MaxDeflRad { get; set; }
    public bool DragOnly { get; set; } = true;
    public string Axis { get; set; } = "throttleLever";
    public string AxisMap { get; set; } = "aftOnly";
}

public sealed class FuselageConfig
{
    public double Cd0Area { get; set; }
    public double SideForceArea { get; set; }
    public DampingConfig Damping { get; set; } = new();

    /// <summary>
    /// Slender-body crossflow drag (optional; zero areas disable). At high alpha/beta the fuselage is
    /// broadside to the flow: plan-view area produces normal force (arrests spin flattening), side-view
    /// area produces yaw-restoring side force when the fin is stalled/blanketed. Forces act at the
    /// respective area centers (x vs CG), giving the moments a point-model fuselage cannot.
    /// </summary>
    public CrossflowConfig Crossflow { get; set; } = new();
}

public sealed class PropulsionConfig
{
    public double MaxPowerW { get; set; }
    public double PropDiameterM { get; set; }
    public double IdleRpm { get; set; } = 700;
    public double MaxRpm { get; set; } = 2700;
    public double PropInertia { get; set; } = 1.7;      // prop + crank about x, kg m^2
    public int RotationSign { get; set; } = 1;          // +1 = right-hand (clockwise from behind)
    public double Efficiency { get; set; } = 0.75;
    public double ThrustLineZ { get; set; } = 0.0;      // + below CG (z down)
    public double PFactorK { get; set; } = 0.35;        // lateral thrust offset fraction of radius per sin(alpha)
    public double SlipstreamK { get; set; } = 0.12;     // spiral-slipstream yaw moment coefficient
}

public sealed class StallDynamicsConfig
{
    public double StripSepTau { get; set; } = 0.15;      // s, per-strip separation growth
    public double StripReattachTau { get; set; } = 0.6;  // s, per-strip reattachment
    public double WakeGrowTau { get; set; } = 0.25;      // s, whole-wing wake development
    public double WakeDecayTau { get; set; } = 1.0;      // s, wake washout
    public double WakeSpreadDeg { get; set; } = 6.0;     // wake band edge softness

    /// <summary>Proposal 1: downwash transport lag (Cm-alphadot). Off = instantaneous downwash.</summary>
    public bool DownwashLagEnabled { get; set; }

    /// <summary>Proposal 3: per-strip unsteady force lag (~3 chords/V). Off = quasi-steady.</summary>
    public bool UnsteadyLagEnabled { get; set; }
}

public sealed class CrossflowConfig
{
    public double PlanArea { get; set; }
    public double PlanCenterX { get; set; }
    public double SideArea { get; set; }
    public double SideCenterX { get; set; }
    public double Cd { get; set; } = 1.2; // circular-cylinder crossflow drag coefficient

    /// <summary>Proposal 2: fuselage length for DISTRIBUTED crossflow (5 stations -> real Cm_q/N_r
    /// damping at spin attitudes). 0 = legacy single-point.</summary>
    public double LengthM { get; set; }
}

public sealed class DampingConfig
{
    public double P { get; set; }
    public double Q { get; set; }
    public double R { get; set; }
}

public sealed class GearConfig
{
    public double[] Pos { get; set; } = { 0, 0, 0 };
    public double SpringN { get; set; }
    public double DampNs { get; set; }
    public bool Brake { get; set; }
}

public sealed class LimitsConfig
{
    public double VneMs { get; set; } = 1000;
    public double GMax { get; set; } = 10;
    public double GMin { get; set; } = -10;
}
