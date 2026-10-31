namespace FlyingGame.Core.DataContracts;

/// <summary>
/// WEIGHT AND BALANCE (owner 2026-10-30: "load the airplane with occupants, luggage, or cargo and fuel, and the
/// weight and balance is plotted and the flight characteristics are adjusted appropriately").
///
/// Everything here is about the EMPTY aeroplane plus the things you put in it. <see cref="MassConfig"/> keeps the
/// mass, CG and inertia the aircraft is actually flying at; this block is what those are built up FROM, and
/// <see cref="FlyingGame.Core.Loading.LoadSheet"/> writes the result back into <see cref="MassConfig"/> so that all
/// ninety-odd places which read the CG pick it up with no change.
///
/// Optional, and absent on most types: a config without a loading block flies exactly as it always has, at its
/// tuned mass and CG, and simply cannot be loaded. The derivation rule for adding one is in
/// docs/WEIGHT-AND-BALANCE.md: the DEFAULT load must reproduce the type's tuned mass, CG and inertia exactly, or
/// adding the block silently retunes an aeroplane that was already calibrated against its published numbers.
/// </summary>
public sealed class LoadingConfig
{
    /// <summary>Empty (ready-to-fly, no people, no bags, no usable fuel) mass in kg.</summary>
    public double EmptyMassKg { get; set; }

    /// <summary>Empty-weight CG in body axes (x forward, y right, z down), metres, same frame as the strips.</summary>
    public double[] EmptyCg { get; set; } = { 0, 0, 0 };

    /// <summary>Inertia of the empty aeroplane ABOUT ITS OWN EMPTY CG. The load sheet moves it by parallel axis.</summary>
    public InertiaConfig EmptyInertia { get; set; } = new();

    /// <summary>Maximum certificated take-off mass, kg. The load sheet reports a load above this as over gross.</summary>
    public double MaxTakeoffMassKg { get; set; }

    /// <summary>Seats, baggage holds, cargo bays — anywhere a mass can be put that is not fuel.</summary>
    public List<StationConfig> Stations { get; set; } = new();

    /// <summary>Fuel tanks, or water ballast on a glider.</summary>
    public List<TankConfig> Tanks { get; set; } = new();

    /// <summary>
    /// The certificated CG envelope, as a closed polygon of (percent MAC, mass kg) vertices in order.
    ///
    /// Percent MAC, not an arm, and this is the one decision in the block worth explaining. A published envelope is
    /// quoted in inches or millimetres from that type's own datum — a firewall, a wing leading edge, a point in
    /// space ahead of the propeller — and this project's body x has nothing to do with any of them. Percent MAC is
    /// the frame both ends share: it is what the POH limits mean physically, and
    /// <see cref="FlyingGame.Core.Loading.WeightAndBalance.PercentMac"/> measures it from the strips the aeroplane
    /// actually flies with. Every type's tuned CG lands between 10 and 32 % MAC when measured that way, which is
    /// where real light aircraft sit, so the frame is sound before any envelope is written into it.
    /// </summary>
    public List<double[]> Envelope { get; set; } = new();
}

/// <summary>A place to put people or things. Arms are body axes (x forward, y right, z down), metres.</summary>
public sealed class StationConfig
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public double[] Arm { get; set; } = { 0, 0, 0 };
    public double MaxKg { get; set; }

    /// <summary>What is in it when nobody has said otherwise. The defaults together with
    /// <see cref="TankConfig.DefaultKg"/> must reproduce the type's tuned mass, CG and inertia.</summary>
    public double DefaultKg { get; set; }

    /// <summary>True for a seat: a station that holds a person, and so can be occupied or not rather than filled part way.</summary>
    public bool IsSeat { get; set; }
}

/// <summary>A tank. Fuel moves the CG as it burns, which is half of why weight and balance is interesting in flight.</summary>
public sealed class TankConfig
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public double[] Arm { get; set; } = { 0, 0, 0 };

    /// <summary>Usable capacity in litres. Unusable fuel belongs in the empty weight, as the POH counts it.</summary>
    public double CapacityL { get; set; }

    /// <summary>0.72 for avgas, 0.80 for jet A, 1.0 for a glider's water ballast. kg per litre.</summary>
    public double DensityKgPerL { get; set; } = 0.72;

    /// <summary>How much fuel is in it on a fresh aircraft, kg.</summary>
    public double DefaultKg { get; set; }

    /// <summary>False for a tank the pilot cannot select — a header tank, or wing ballast that only dumps.</summary>
    public bool Selectable { get; set; } = true;
}
