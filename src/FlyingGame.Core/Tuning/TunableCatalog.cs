using System;
using System.Collections.Generic;
using FlyingGame.Core.DataContracts;

namespace FlyingGame.Core.Tuning;

/// <summary>
/// THE KNOBS THAT EXIST TODAY. One <see cref="TunableRegistry.Register"/> call each, and adding one means adding a
/// call here and nothing else — the Developer page renders whatever is registered.
///
/// WHAT IS DELIBERATELY NOT HERE, and must not be added: measured and certificated facts. Empty weights come from
/// published type data, CG limits and envelopes from the TCDS, inertias from a component mass build-up, strip
/// positions and areas from the models, tyre sizes from the TireCatalog (which TireCatalogTests holds every config
/// to). Those are not K factors, and making them adjustable invites someone to tune away a correct number and then
/// report handling against it. A number whose rule can still be stated — a prop inertia, a pitch stop, a solved fin
/// offset, a limit load factor — goes in the DERIVED group instead, where the row names the rule and says that
/// settling on a value means re-running the derivation rather than editing the number.
///
/// `controls.&lt;axis&gt;.effectiveness` is excluded on purpose although it looks exactly like a gain: the contract says
/// in bold that it is NOT a tuning knob but a hook for a future derived loss, and commit 4e5e1a7 already tuned it
/// once and double-counted the flap effectiveness tau with it.
///
/// The descriptions are written FROM the doc comments in AircraftConfig.cs, which in most cases quote Ty's own words
/// for which way the number moves the aeroplane. That is on purpose: a description written next to the physics is
/// the one that stays true when the physics changes.
/// </summary>
public static class TunableCatalog
{
    private const double Deg = 180.0 / Math.PI;

    internal static void Load()
    {
        Rigging();
        SpinAndHighAlpha();
        EngineAndPropeller();
        Fuselage();
        ControlThrows();
        GroundAndTyres();
        StallDynamics();
        Derived();
    }

    // ---------------------------------------------------------------- helpers

    private static SpinAeroConfig Spin(AircraftConfig c) => c.SpinAero ??= new SpinAeroConfig();
    private static bool HasEngine(AircraftConfig c) => c.Propulsion != null && c.Propulsion.MaxPowerW > 0.0;

    /// <summary>The legs this aeroplane rolls on: a wheel with a tyre, not a skid or a float keel. A glider on a
    /// keel has no tyre pressure, and a row offering one would only mislead.</summary>
    private static List<GearConfig> Wheels(AircraftConfig c)
    {
        var list = new List<GearConfig>();
        foreach (GearConfig g in c.Gear)
        {
            string kind = g.GearType ?? "";
            if (kind.IndexOf("skid", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("keel", StringComparison.OrdinalIgnoreCase) >= 0
                || kind.IndexOf("feet", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            list.Add(g);
        }
        return list;
    }

    /// <summary>Read a per-leg number off the first wheel that has one; write it to every wheel. These are
    /// fleet-wide feel numbers rather than per-leg geometry, and one slider for "the tyres" is what a pilot means.</summary>
    private static Func<AircraftConfig, double> GearGet(Func<GearConfig, double> read, double scale = 1.0)
        => c => { List<GearConfig> w = Wheels(c); return w.Count == 0 ? 0.0 : read(w[0]) * scale; };

    private static Action<AircraftConfig, double> GearSet(Action<GearConfig, double> write, double scale = 1.0)
        => (c, v) => { foreach (GearConfig g in Wheels(c)) write(g, v / scale); };

    private static Func<AircraftConfig, bool> HasWheels => c => Wheels(c).Count > 0;

    private static ControlAxisConfig? Axis(AircraftConfig c, string which) => which switch
    {
        "aileron" => c.Controls?.Aileron,
        "elevator" => c.Controls?.Elevator,
        _ => c.Controls?.Rudder,
    };

    // ---------------------------------------------------------------- rigging

    private static void Rigging()
    {
        TunableRegistry.Register(new Tunable
        {
            Id = "rig.aileronRad",
            Group = "Rigging",
            Label = "Aileron rig offset",
            Units = "°",
            Min = -3, Max = 3, Step = 0.1, FineStep = 0.01, Decimals = 2,
            Description = "A standing aileron deflection built into the rigging, added to whatever you are holding. "
                        + "It is what the rig solver found to make the aeroplane fly its wings level hands off at cruise. "
                        + "Move it one way and the aeroplane wants to roll left hands off; the other way, right. If you are "
                        + "chasing a wing that drops in cruise with your hands off the stick, this is the number.",
            Get = c => c.Rig.AileronRad * Deg,
            Set = (c, v) => c.Rig.AileronRad = v / Deg,
        });

        TunableRegistry.Register(new Tunable
        {
            Id = "gear.toeOutDeg",
            Group = "Rigging",
            Label = "Main wheel toe out",
            Units = "°",
            Min = -3, Max = 6, Step = 0.5, FineStep = 0.1, Decimals = 1,
            Description = "How far each main wheel points away from straight ahead. Positive is toe OUT, negative is toe IN. "
                        + "Out steadies the aeroplane on the ground: a toed-out wheel rolls at a standing slip angle and pushes "
                        + "outboard in proportion to the weight on it, so when one wheel loads up in a swerve its bigger push "
                        + "shoves you back toward the light side and the swerve dies. Toed IN does the reverse and digs the swerve "
                        + "in deeper. Sitting level the pair cancel exactly and you feel nothing. Ty, 2026-11-10, flying the Pitts "
                        + "and the Extra: \"How are the main gear tires aligned? It acts like it has toe in\" — and they were dead "
                        + "straight, which is the one thing a sprung taildragger's gear never is.",
            Applies = HasWheels,
            Get = GearGet(g => g.ToeOutDeg),
            Set = GearSet((g, v) => g.ToeOutDeg = v),
        });
    }

    // ------------------------------------------------- spin and high alpha

    private static void SpinAndHighAlpha()
    {
        const string g = "Spin and high alpha";

        TunableRegistry.Register(new Tunable
        {
            Id = "spinAero.controlSpoilerK",
            Group = g,
            Label = "Control power when the tail is broadside",
            Min = 0, Max = 2, Step = 0.1, FineStep = 0.02, Decimals = 2,
            Description = "How much elevator and rudder you still have once the tail is sideways to the air, as it is in a spin. "
                        + "Bigger means more control in the spin. At 0 the surfaces go nearly dead there, because a strip model "
                        + "lets a control turn the flow and at ninety degrees there is no flow left to turn — measured on the Pitts, "
                        + "the elevator's whole travel moves the tail's force by 0.39 at 30° and by 0.06 at 80°. The real surface "
                        + "shoves the separated wake aside instead, and this is the strength of that shove. Thirteen types fly 0.6.",
            Get = c => Spin(c).ControlSpoilerK,
            Set = (c, v) => Spin(c).ControlSpoilerK = v,
        });

        TunableRegistry.Register(new Tunable
        {
            Id = "spinAero.controlSpoilerOnsetDeg",
            Group = g,
            Label = "Angle that control power starts coming back",
            Units = "°",
            Min = 0, Max = 60, Step = 2, FineStep = 0.5, Decimals = 1,
            Description = "Where the knob above begins to bite, in degrees of the angle the tail's own air is coming from; it is "
                        + "fully in twenty degrees later. LOWER gives you the control back earlier in the stall — which is what a "
                        + "flat-plate tail wants, because it stalls at 9-12° and leaves a hole in elevator authority between there "
                        + "and 30°. Lowering it is not free: it moves a spin Ty has already flown and accepted, so it wants flying "
                        + "again on a type he knows. The fleet carries 10°; a type with no spin block at all falls back to 30°.",
            Get = c => Spin(c).ControlSpoilerOnsetDeg,
            Set = (c, v) => Spin(c).ControlSpoilerOnsetDeg = v,
        });

        TunableRegistry.Register(new Tunable
        {
            Id = "spinAero.stabHighAlphaK",
            Group = g,
            Label = "Stabiliser power at 60-90° angle of attack",
            Min = 0.5, Max = 4, Step = 0.1, FineStep = 0.02, Decimals = 2,
            Description = "How hard the horizontal tail works when the air is hitting it almost edge-on from below or above, which "
                        + "happens in a spin or a tumble and nowhere else. Bigger is a more effective stabiliser there, so a flatter "
                        + "spin is harder to hold and easier to get out of. Below 50° it does nothing at all, so it cannot touch "
                        + "ordinary flying. Ty after flying build 2026.1109.1912: \"I tested the pitts same flat spin problems make "
                        + "the stab more effective 60-90 aoa\". 1.0 leaves the type untouched; the Pitts flies 1.9.",
            Get = c => Spin(c).StabHighAlphaK,
            Set = (c, v) => Spin(c).StabHighAlphaK = v,
        });

        TunableRegistry.Register(new Tunable
        {
            Id = "spinAero.finSidewashK",
            Group = g,
            Label = "Rudder power in the fuselage's own crossflow",
            Min = 0, Max = 2, Step = 0.1, FineStep = 0.02, Decimals = 2,
            Description = "How much extra air speed the fin and rudder see because the fuselage is accelerating the sideways flow "
                        + "around itself. Bigger is a stronger rudder in the spin, where it matters — up to twice the sideways speed "
                        + "at the fin root and so four times the push, dying away a body-width out. Ty, 2026-11-09: \"The rudder is "
                        + "much more effective in real life during spin recovery\". 0 is off and changes nothing; 1 is full cylinder "
                        + "theory. It is zero whenever there is no crossflow, so ordinary flight and the spin entry are untouched.",
            Get = c => Spin(c).FinSidewashK,
            Set = (c, v) => Spin(c).FinSidewashK = v,
        });

        TunableRegistry.Register(new Tunable
        {
            Id = "spinAero.stabSidewashK",
            Group = g,
            Label = "Elevator power in the fuselage's own crossflow",
            Min = 0, Max = 2, Step = 0.1, FineStep = 0.02, Decimals = 2,
            Description = "The same fuselage effect as the knob above, but at the horizontal tail instead of the fin: bigger is a "
                        + "stronger elevator in the spin. Ty, 2026-11-10, after a clip of his own flat spin in which \"the elevator "
                        + "literally does nothing in the spin\": \"The elevator gets the same dynamic pressure power you just gave the "
                        + "rudder\". It is kept separate from the rudder's on purpose so the two can be swept against each other. "
                        + "It ramps in between 20° and 40° so that ordinary flight keeps the trim it has.",
            Get = c => Spin(c).StabSidewashK,
            Set = (c, v) => Spin(c).StabSidewashK = v,
        });

        TunableRegistry.Register(new Tunable
        {
            Id = "spinAero.shadowK",
            Group = g,
            Label = "How dead the air is behind a stalled surface",
            Min = 0, Max = 1, Step = 0.05, FineStep = 0.01, Decimals = 2,
            Description = "How much air speed a wing or tail loses when it is sitting in another surface's wake, worked out by "
                        + "following the real corkscrew path of the air upstream from each section of this airframe. Bigger is a "
                        + "deeper shadow, so a blanketed tail does less. 0 switches the whole geometric path off and falls back to "
                        + "the old NACA wake. Ty, 2026-11-08, asking for it: \"use reduced Q from a k factor in the shadow that we "
                        + "can tweak for fine tuning... it will break us out of this naca shadow trap\".",
            Get = c => Spin(c).ShadowK,
            Set = (c, v) => Spin(c).ShadowK = v,
        });

        TunableRegistry.Register(new Tunable
        {
            Id = "spinAero.shadowDecayChords",
            Group = g,
            Label = "How far downstream a shadow lasts",
            Units = " chords",
            Min = 0, Max = 6, Step = 0.25, FineStep = 0.05, Decimals = 2,
            Description = "How many chord-lengths of corkscrew path a surface stays shaded for before the wake refills and the air "
                        + "speed comes back. Bigger reaches further down the fuselage, so the tail stays blanketed longer. 0 or less "
                        + "means a shadow never washes out at all. The march itself stops at three metres, which on this fleet is "
                        + "three to four chords, so a number far above that cannot express anything.",
            Get = c => Spin(c).ShadowDecayChords,
            Set = (c, v) => Spin(c).ShadowDecayChords = v,
        });

        TunableRegistry.Register(new Tunable
        {
            Id = "spinAero.celluleWakeK",
            Group = g,
            Label = "How hard a biplane's wings shade each other",
            Min = 0, Max = 2, Step = 0.1, FineStep = 0.02, Decimals = 2,
            Description = "On a biplane, how much air speed one wing takes away from the other when it is upstream. 1 is the wake as "
                        + "computed, 0 means the two wings never shade each other at all. It matters most inverted, where the stagger "
                        + "puts the LOWER wing in the shade — and because the lower wing sits below the centre of gravity and the upper "
                        + "well above it, killing the lower one pitches the nose DOWN inverted. A real Pitts does the opposite: "
                        + "\"upright spins are more nose low and inverted flat spins are more nose high\". Only on a biplane.",
            Applies = c => c.Surfaces != null && HasUpperAndLower(c),
            Get = c => Spin(c).CelluleWakeK,
            Set = (c, v) => Spin(c).CelluleWakeK = v,
        });
    }

    private static bool HasUpperAndLower(AircraftConfig c)
    {
        bool upper = false, lower = false;
        foreach (SurfaceConfig s in c.Surfaces)
        {
            if (string.Equals(s.Id, "wing-upper", StringComparison.Ordinal)) upper = true;
            if (string.Equals(s.Id, "wing-lower", StringComparison.Ordinal)) lower = true;
        }
        return upper && lower;
    }

    // --------------------------------------------------- engine and propeller

    private static void EngineAndPropeller()
    {
        const string g = "Engine and propeller";

        TunableRegistry.Register(new Tunable
        {
            Id = "propulsion.idleRpm",
            Group = g,
            Label = "Idle rpm",
            Units = " rpm",
            Min = 0, Max = 1400, Step = 25, FineStep = 5, Decimals = 0,
            Description = "Where the engine sits with the throttle closed. Higher idles faster, so the aeroplane taxis and glides "
                        + "with more thrust still on and is harder to slow down on the approach; lower lets it come back further and "
                        + "makes the descent steeper. It is also the rpm the engine returns to when you close the throttle in the air, "
                        + "so it changes how much the propeller is dragging on a glide.",
            Applies = HasEngine,
            Get = c => c.Propulsion!.IdleRpm,
            Set = (c, v) => c.Propulsion!.IdleRpm = v,
        });

        TunableRegistry.Register(new Tunable
        {
            Id = "propulsion.maxRpm",
            Group = g,
            Label = "Maximum rpm",
            Units = " rpm",
            Min = 1000, Max = 3600, Step = 25, FineStep = 5, Decimals = 0,
            Description = "The rpm full throttle reaches. On a fixed-pitch type this is the top of the range the engine's power "
                        + "curve is stretched across, so raising it gives you more rpm and more power at the top end.",
            Applies = HasEngine,
            Get = c => c.Propulsion!.MaxRpm,
            Set = (c, v) => c.Propulsion!.MaxRpm = v,
        });

        TunableRegistry.Register(new Tunable
        {
            Id = "propulsion.governedRpm",
            Group = g,
            Label = "Governed rpm (constant speed)",
            Units = " rpm",
            Min = 1500, Max = 3000, Step = 25, FineStep = 5, Decimals = 0,
            Description = "The rpm a constant-speed propeller's governor holds with the blue lever forward. Raising it gives more "
                        + "power and more noise and a coarser bite; lowering it cruises quieter. Only on a type with a governor.",
            Applies = c => c.Propulsion != null && c.Propulsion.ConstantSpeed,
            Get = c => c.Propulsion!.GovernedRpm,
            Set = (c, v) => c.Propulsion!.GovernedRpm = v,
        });

        TunableRegistry.Register(new Tunable
        {
            Id = "propulsion.pFactorK",
            Group = g,
            Label = "P-factor",
            Min = 0, Max = 1.5, Step = 0.05, FineStep = 0.01, Decimals = 2,
            Description = "How hard the aeroplane yaws because the descending blade is biting more than the rising one when the nose "
                        + "is up. Bigger means more left yaw at high power and low speed — the pull you hold right rudder against on "
                        + "the climb-out and on take-off. 0 removes it entirely.",
            Applies = HasEngine,
            Get = c => c.Propulsion!.PFactorK,
            Set = (c, v) => c.Propulsion!.PFactorK = v,
        });

        TunableRegistry.Register(new Tunable
        {
            Id = "propulsion.slipstreamK",
            Group = g,
            Label = "Spiral slipstream",
            Min = 0, Max = 0.6, Step = 0.02, FineStep = 0.005, Decimals = 3,
            Description = "How hard the corkscrewing propeller wash pushes on the fin and yaws the aeroplane. Bigger means more left "
                        + "yaw with power on — the other half of what you hold right rudder against, and the part that is still there "
                        + "with the nose level. 0 removes it.",
            Applies = HasEngine,
            Get = c => c.Propulsion!.SlipstreamK,
            Set = (c, v) => c.Propulsion!.SlipstreamK = v,
        });

        TunableRegistry.Register(new Tunable
        {
            Id = "propulsion.normalForceK",
            Group = g,
            Label = "Propeller side force",
            Min = 0, Max = 1.5, Step = 0.05, FineStep = 0.01, Decimals = 2,
            Description = "The sideways pull of the propeller disc itself when the air meets it at an angle, acting well ahead of the "
                        + "centre of gravity — so it is destabilising in both pitch and yaw. Bigger makes the nose wander more at high "
                        + "angles. It is OFF (0) on every type, because the term is real in ordinary flight and every stored rig and "
                        + "trim was solved without it: turning it up means the aeroplane no longer flies hands-off where it used to.",
            Applies = HasEngine,
            Get = c => c.Propulsion!.NormalForceK,
            Set = (c, v) => c.Propulsion!.NormalForceK = v,
        });

        TunableRegistry.Register(new Tunable
        {
            Id = "propulsion.efficiency",
            Group = g,
            Label = "Propeller efficiency",
            Min = 0.3, Max = 1.0, Step = 0.02, FineStep = 0.005, Decimals = 3,
            Description = "How much of the engine's power the propeller turns into thrust. Bigger is more thrust everywhere: better "
                        + "climb, higher cruise, a shorter take-off run. It is the blunt instrument on this page — if an aeroplane "
                        + "simply feels underpowered rather than wrong in some particular way, this is the number.",
            Applies = HasEngine,
            Get = c => c.Propulsion!.Efficiency,
            Set = (c, v) => c.Propulsion!.Efficiency = v,
        });

        TunableRegistry.Register(new Tunable
        {
            Id = "propulsion.jetK",
            Group = g,
            Label = "Slipstream strength over the airframe",
            Min = 0, Max = 2, Step = 0.1, FineStep = 0.02, Decimals = 2,
            Description = "How strong the propeller's blown tube of fast air is where it washes over the wing roots and the tail. "
                        + "Bigger means the tail works harder with power on — more elevator and rudder authority at low speed and high "
                        + "power, which is what lets a taildragger pick its tail up and what holds the nose up in a power-on stall. "
                        + "0 removes the blown air entirely and leaves the tail in free stream.",
            Applies = HasEngine,
            Get = c => c.Propulsion!.JetK,
            Set = (c, v) => c.Propulsion!.JetK = v,
        });
    }

    // ------------------------------------------------------------- fuselage

    private static void Fuselage()
    {
        const string g = "Fuselage";

        TunableRegistry.Register(new Tunable
        {
            Id = "fuselage.cd0Area",
            Group = g,
            Label = "Parasite drag area",
            Units = " m²",
            Min = 0, Max = 2, Step = 0.02, FineStep = 0.005, Decimals = 3,
            Description = "The flat-plate area of everything that is not a wing: fuselage, struts, wires, exposed gear. Bigger is "
                        + "more drag, so a lower top speed, a steeper glide and a slower climb. This is the number to move if the "
                        + "aeroplane cruises faster or glides further than the real one.",
            Get = c => c.Fuselage.Cd0Area,
            Set = (c, v) => c.Fuselage.Cd0Area = v,
        });

        TunableRegistry.Register(new Tunable
        {
            Id = "fuselage.sideForceArea",
            Group = g,
            Label = "Side area",
            Units = " m²",
            Min = 0, Max = 12, Step = 0.1, FineStep = 0.02, Decimals = 2,
            Description = "How much flank the fuselage presents to a sideways wind. Bigger makes the aeroplane weathercock harder "
                        + "into the relative wind and feel more sensitive in a slip and in a crosswind.",
            Get = c => c.Fuselage.SideForceArea,
            Set = (c, v) => c.Fuselage.SideForceArea = v,
        });

        TunableRegistry.Register(new Tunable
        {
            Id = "fuselage.munkScale",
            Group = g,
            Label = "Fuselage destabilising moment",
            Min = 0, Max = 1.5, Step = 0.05, FineStep = 0.01, Decimals = 2,
            Description = "How strongly the bare fuselage tries to swap ends — a body in a flow always wants to turn broadside, and "
                        + "this is how much of that the model applies. Bigger makes the aeroplane less stable in both pitch and yaw, "
                        + "so it needs more tail to hold it straight. 0 removes it.",
            Live = true,
            Get = c => c.Fuselage.MunkScale,
            Set = (c, v) => c.Fuselage.MunkScale = v,
            AfterSet = TuningCaches.Invalidate,
        });

        TunableRegistry.Register(new Tunable
        {
            Id = "fuselage.crossflow.cd",
            Group = g,
            Label = "Fuselage crossflow drag",
            Min = 0, Max = 2.5, Step = 0.05, FineStep = 0.01, Decimals = 2,
            Description = "How much the fuselage resists air coming at it from the side or from below, as it does in a spin. Bigger "
                        + "is more damping in the spin: the rotation is slower and the aeroplane is harder to keep spinning. 1.2 is a "
                        + "round body; a square-sided one is higher.",
            Applies = c => c.Fuselage.Crossflow != null,
            Get = c => c.Fuselage.Crossflow!.Cd,
            Set = (c, v) => c.Fuselage.Crossflow!.Cd = v,
        });

        TunableRegistry.Register(new Tunable
        {
            Id = "fuselage.crossflow.sectionCamberCl",
            Group = g,
            Label = "Aft fuselage sideslip camber",
            Min = 0, Max = 2.5, Step = 0.05, FineStep = 0.01, Decimals = 2,
            Description = "How strongly the aft fuselage behaves like a cambered aerofoil when the air comes at it sideways, which "
                        + "pitches the nose DOWN in a slip and in a spin. Bigger is more nose-down with sideslip. Ty on his own "
                        + "aeroplane, 2026-11-01: \"The Pitts is similar but less pronounced\". Only on a type whose aft body has been "
                        + "measured for it.",
            Applies = c => c.Fuselage.Crossflow != null && c.Fuselage.Crossflow.SectionCamberArea > 0.0,
            Get = c => c.Fuselage.Crossflow!.SectionCamberCl,
            Set = (c, v) => c.Fuselage.Crossflow!.SectionCamberCl = v,
        });

        TunableRegistry.Register(new Tunable
        {
            Id = "fuselage.crossflow.aftCamberGain",
            Group = g,
            Label = "Aft fuselage sideslip camber gain",
            Min = 0, Max = 3, Step = 0.05, FineStep = 0.01, Decimals = 2,
            Description = "How hard the aft fuselage pitches the nose DOWN when the air comes at it sideways, as it does in a "
                        + "spin. Bigger flattens the inverted spin and steepens the upright one — one term, opposite effects, "
                        + "which is why an inverted spin can sit flatter than an upright one. 1.0 is as shipped; 0 switches the "
                        + "term off entirely. Measured on the Pitts at full power: 0 gives an inverted spin at 81.5° and an "
                        + "upright one at 78.8°, 1.0 gives 85.3° and 78.7°.",
            Live = true,
            Applies = c => c.Fuselage.Crossflow != null,
            Get = c => c.Fuselage.Crossflow!.AftCamberGain,
            Set = (c, v) => c.Fuselage.Crossflow!.AftCamberGain = v,
        });

        TunableRegistry.Register(new Tunable
        {
            Id = "fuselage.crossflow.aftCamberBroadsideCl",
            Group = g,
            Label = "Aft fuselage broadside lift",
            Min = 0, Max = 1.5, Step = 0.05, FineStep = 0.01, Decimals = 2,
            Description = "The same aft body, but lifting because it is lying BROADSIDE to the air rather than because it is "
                        + "sideslipping — a flat-bellied, round-topped fuselage does that whether or not it is slipping. It is the "
                        + "only thing measured so far that reaches a 95° inverted spin, and it is SHIPPED OFF at 0 because unlike "
                        + "the knob above it also acts with no sideslip at all, so it moves the stall break and the trim of every "
                        + "aeroplane. Measured on the Pitts, upright and inverted: 0 gives 78.7 and −85.3; 0.40 gives 76.0 and "
                        + "−89.1; 0.80 gives 74.1 and −91.7; 1.20 gives 72.4 and −95.6. Ty's targets are 78 and 95, and no single "
                        + "value here reaches both — 0.80 gives his 17° spread, 1.20 his inverted number.",
            Live = true,
            Applies = c => c.Fuselage.Crossflow != null,
            Get = c => c.Fuselage.Crossflow!.AftCamberBroadsideCl,
            Set = (c, v) => c.Fuselage.Crossflow!.AftCamberBroadsideCl = v,
        });


        TunableRegistry.Register(new Tunable
        {
            Id = "fuselage.crossflow.cowlCamberCl",
            Group = g,
            Label = "Cowl sideslip camber",
            Min = 0, Max = 2.5, Step = 0.05, FineStep = 0.01, Decimals = 2,
            Description = "The same effect as the knob above but at the cowling, ahead of the centre of gravity, so it pitches the "
                        + "opposite way: bigger is more nose-UP with sideslip. The two together decide which way the nose goes when "
                        + "you put a boot of rudder in.",
            Applies = c => c.Fuselage.Crossflow != null && c.Fuselage.Crossflow.CowlCamberArea > 0.0,
            Get = c => c.Fuselage.Crossflow!.CowlCamberCl,
            Set = (c, v) => c.Fuselage.Crossflow!.CowlCamberCl = v,
        });
    }

    // -------------------------------------------------------- control throws

    private static void ControlThrows()
    {
        const string g = "Control throws";
        Throw("aileron", "Aileron travel", 10, 40,
            "How far the ailerons move at full stick. More travel is a faster roll rate, and past a point a stall of the "
          + "down-going aileron that bites before the wing does. This is the stop, not the feel: it is what the real "
          + "aeroplane's rigging would be measured at.");
        Throw("elevator", "Elevator travel", 10, 45,
            "How far the elevator moves at full stick fore and aft. More travel means you can reach a higher angle of "
          + "attack and a tighter pitch, so the aeroplane stalls more readily on full aft stick and snaps more willingly.");
        Throw("rudder", "Rudder travel", 10, 45,
            "How far the rudder moves at full pedal. More travel is more yaw authority — a deeper slip, a better crosswind "
          + "landing, and more rudder to hold or to break a spin with.");
    }

    private static void Throw(string which, string label, double min, double max, string description)
    {
        TunableRegistry.Register(new Tunable
        {
            Id = "controls." + which + ".maxDeflRad",
            Group = "Control throws",
            Label = label,
            Units = "°",
            Min = min, Max = max, Step = 1, FineStep = 0.25, Decimals = 2,
            Description = description,
            Applies = c => Axis(c, which) != null && Axis(c, which)!.MaxDeflRad > 0.0,
            Get = c => Axis(c, which)!.MaxDeflRad * Deg,
            Set = (c, v) => Axis(c, which)!.MaxDeflRad = v / Deg,
        });
    }

    // ------------------------------------------------------ ground and tyres

    private static void GroundAndTyres()
    {
        const string g = "Ground and tyres";

        TunableRegistry.Register(new Tunable
        {
            Id = "gear.tireMu",
            Group = g,
            Label = "Tyre grip",
            Min = 0.1, Max = 1.5, Step = 0.05, FineStep = 0.01, Decimals = 2,
            Description = "How much the tyres hold before they slide. Bigger grips harder, so the aeroplane tracks straighter and "
                        + "the brakes bite more; smaller skids and slides, like a wet or a grass surface. It is the ceiling on every "
                        + "side force a wheel can make, so it is also what decides whether a swerve can be caught.",
            Applies = HasWheels,
            Get = GearGet(g2 => g2.TireMu),
            Set = GearSet((g2, v) => g2.TireMu = v),
        });

        TunableRegistry.Register(new Tunable
        {
            Id = "gear.corneringStiffnessN",
            Group = g,
            Label = "Tyre cornering stiffness",
            Units = " N/rad",
            Min = 500, Max = 20000, Step = 500, FineStep = 100, Decimals = 0,
            Description = "How quickly a tyre builds its side force as it is asked to roll at an angle. Bigger is a sharper, more "
                        + "darting response on the ground and a quicker swerve to catch; smaller is vaguer and softer. It works up to "
                        + "the grip ceiling above and no further.",
            Applies = HasWheels,
            Get = GearGet(g2 => g2.CorneringStiffnessN),
            Set = GearSet((g2, v) => g2.CorneringStiffnessN = v),
        });

        TunableRegistry.Register(new Tunable
        {
            Id = "gear.tirePressurePsi",
            Group = g,
            Label = "Tyre pressure",
            Units = " psi",
            Min = 4, Max = 60, Step = 1, FineStep = 0.2, Decimals = 1,
            Description = "What the tyres are pumped up to. Softer tyres spread their footprint, sink less into soft ground, soak up "
                        + "a rough strip and are kinder to a firm arrival; harder ones roll with less drag and sit more precisely. It "
                        + "also decides the speed at which the tyre will aquaplane on a wet surface.",
            Live = false,
            LiveNote = "The tyre is built when the aeroplane is: FLY again to feel it.",
            Applies = c => { foreach (GearConfig w in Wheels(c)) if (w.TirePressurePsi > 0.0) return true; return false; },
            Get = GearGet(g2 => g2.TirePressurePsi),
            Set = GearSet((g2, v) => g2.TirePressurePsi = v),
        });

        TunableRegistry.Register(new Tunable
        {
            Id = "gear.springN",
            Group = g,
            Label = "Undercarriage spring",
            Units = " N/m",
            Min = 5000, Max = 200000, Step = 5000, FineStep = 1000, Decimals = 0,
            Description = "How stiff the legs are. Stiffer legs bounce more off a firm landing and transmit every bump; softer ones "
                        + "soak the arrival up and sit lower under load. Too soft and the aeroplane wallows on the ground.",
            Applies = HasWheels,
            Get = GearGet(g2 => g2.SpringN),
            Set = GearSet((g2, v) => g2.SpringN = v),
        });

        TunableRegistry.Register(new Tunable
        {
            Id = "gear.dampNs",
            Group = g,
            Label = "Undercarriage damping",
            Units = " Ns/m",
            Min = 100, Max = 30000, Step = 250, FineStep = 50, Decimals = 0,
            Description = "How much the legs resist moving rather than how hard they push back. More damping kills the bounce after "
                        + "a firm landing; too little and the aeroplane pogoes down the runway, too much and the legs feel solid and "
                        + "the aeroplane skips off bumps.",
            Applies = HasWheels,
            Get = GearGet(g2 => g2.DampNs),
            Set = GearSet((g2, v) => g2.DampNs = v),
        });
    }

    // -------------------------------------------------------- stall dynamics

    private static void StallDynamics()
    {
        const string g = "Stall dynamics";

        TunableRegistry.Register(new Tunable
        {
            Id = "stallDynamics.stripSepTau",
            Group = g,
            Label = "How fast the flow lets go",
            Units = " s",
            Min = 0.02, Max = 1.0, Step = 0.05, FineStep = 0.01, Decimals = 3,
            Description = "The time the airflow takes to separate once a piece of wing passes its stalling angle. Shorter is a "
                        + "sharper, more sudden break; longer is a mushier one you can feel coming. It is the difference between a "
                        + "wing that drops and a wing that sinks.",
            Get = c => c.StallDynamics.StripSepTau,
            Set = (c, v) => c.StallDynamics.StripSepTau = v,
        });

        TunableRegistry.Register(new Tunable
        {
            Id = "stallDynamics.stripReattachTau",
            Group = g,
            Label = "How fast the flow comes back",
            Units = " s",
            Min = 0.05, Max = 3.0, Step = 0.05, FineStep = 0.01, Decimals = 3,
            Description = "The time the airflow takes to re-attach once you lower the nose. Longer means the stall hangs on after "
                        + "you have released the stick, so the recovery costs more height and a spin is harder to stop. This is the "
                        + "lag that makes a stall feel like something you have to fly out of rather than switch off.",
            Get = c => c.StallDynamics.StripReattachTau,
            Set = (c, v) => c.StallDynamics.StripReattachTau = v,
        });

        TunableRegistry.Register(new Tunable
        {
            Id = "stallDynamics.wakeGrowTau",
            Group = g,
            Label = "How fast the wake builds",
            Units = " s",
            Min = 0.05, Max = 2.0, Step = 0.05, FineStep = 0.01, Decimals = 3,
            Description = "The time the stalled wing's burbling wake takes to grow once it starts. Shorter means the tail is "
                        + "blanketed sooner after the break, so the buffet and the loss of elevator arrive together.",
            Get = c => c.StallDynamics.WakeGrowTau,
            Set = (c, v) => c.StallDynamics.WakeGrowTau = v,
        });

        TunableRegistry.Register(new Tunable
        {
            Id = "stallDynamics.wakeDecayTau",
            Group = g,
            Label = "How fast the wake clears",
            Units = " s",
            Min = 0.05, Max = 4.0, Step = 0.05, FineStep = 0.01, Decimals = 3,
            Description = "The time the wake takes to clear off the tail after the wing re-attaches. Longer keeps the elevator soft "
                        + "and the buffet going well into the recovery.",
            Get = c => c.StallDynamics.WakeDecayTau,
            Set = (c, v) => c.StallDynamics.WakeDecayTau = v,
        });
    }

    // -------------------------------------------------------------- derived

    private static void Derived()
    {
        string g = TunableRegistry.DerivedGroup;

        TunableRegistry.Register(new Tunable
        {
            Id = "rig.finOffsetRad",
            Group = g, Derived = true,
            Label = "Vertical tail offset",
            Units = "°",
            Min = -4, Max = 4, Step = 0.1, FineStep = 0.02, Decimals = 3,
            Rule = "Solved by RigTool so the aeroplane flies straight hands off at its rig condition.",
            Description = "How far the fin and rudder are set off the centreline, built into the airframe. It is what cancels the "
                        + "propeller's corkscrew in the cruise, so moving it means the aeroplane no longer flies straight hands off "
                        + "at cruise power — one way it will hold a little left rudder in, the other a little right. It also holds "
                        + "the Pitts' inverted spin: about 1.9° of fin is what that hold has been riding on, and whether to keep it "
                        + "is the open question this knob is here to settle. IF YOU SETTLE ON A VALUE: it cannot simply be typed into "
                        + "the config. The stored rig is a solved value and the tests hold the file to what the solver wants, so "
                        + "adopting a number means re-running RigTool, not editing it in.",
            Get = c => c.Rig.FinOffsetRad * Deg,
            Set = (c, v) => c.Rig.FinOffsetRad = v / Deg,
        });

        TunableRegistry.Register(new Tunable
        {
            Id = "propulsion.propInertia",
            Group = g, Derived = true,
            Label = "Propeller inertia",
            Units = " kg·m²",
            Min = 0.1, Max = 12, Step = 0.1, FineStep = 0.02, Decimals = 3,
            Rule = "Ty's blade rule: each blade 15 % of net prop weight, centre of mass at 25 % of DIAMETER, two-blade (+60 % on three).",
            Description = "How much the spinning propeller resists being sped up, slowed down, or — the part you feel — having its "
                        + "axis moved. Bigger is a stronger gyroscopic kick: more pitch when you yaw, more yaw when you pitch, which "
                        + "is what makes a snap roll and a spin entry bite. It also makes the engine slower to pick up and slower to "
                        + "come back. It is a term in the inverted-spin pitch budget, which is why it is on this page.",
            Applies = c => c.Propulsion != null && c.Propulsion.PropInertia > 0.0,
            Get = c => c.Propulsion!.PropInertia,
            Set = (c, v) => c.Propulsion!.PropInertia = v,
        });

        TunableRegistry.Register(new Tunable
        {
            Id = "propulsion.fineStopDeg",
            Group = g, Derived = true,
            Label = "Propeller fine pitch stop",
            Units = "°",
            Min = 5, Max = 25, Step = 0.5, FineStep = 0.1, Decimals = 2,
            Rule = "Derived so the engine makes full rpm at 26 inHg manifold pressure at sea level.",
            Description = "The flattest the blades can go. Flatter gives the engine a lighter load, so it reaches full rpm sooner — "
                        + "better acceleration and a shorter take-off run, at the cost of overspeeding more easily in a dive.",
            Applies = c => c.Propulsion != null && c.Propulsion.ConstantSpeed && c.Propulsion.FineStopDeg > 0.0,
            Get = c => c.Propulsion!.FineStopDeg,
            Set = (c, v) => c.Propulsion!.FineStopDeg = v,
        });

        TunableRegistry.Register(new Tunable
        {
            Id = "propulsion.coarseStopDeg",
            Group = g, Derived = true,
            Label = "Propeller coarse pitch stop",
            Units = "°",
            Min = 15, Max = 50, Step = 0.5, FineStep = 0.1, Decimals = 2,
            Rule = "Derived so the propeller cannot overspeed until Vd, which is 110 % of Vne, at full power.",
            Description = "The coarsest the blades can go. Coarser lets the propeller take a bigger bite, so it holds the rpm down "
                        + "in a dive and cruises more efficiently, but the engine is more loaded and slower to pick up.",
            Applies = c => c.Propulsion != null && c.Propulsion.ConstantSpeed && c.Propulsion.CoarseStopDeg > 0.0,
            Get = c => c.Propulsion!.CoarseStopDeg,
            Set = (c, v) => c.Propulsion!.CoarseStopDeg = v,
        });

        TunableRegistry.Register(new Tunable
        {
            Id = "limits.gMax",
            Group = g, Derived = true,
            Label = "Positive g limit",
            Units = " g",
            Min = 1.5, Max = 12, Step = 0.1, FineStep = 0.05, Decimals = 2,
            Rule = "Certificated: the type's published positive limit load factor.",
            Description = "How much positive g the airframe takes before it starts being damaged. It is the threshold of the damage "
                        + "model, not of the flying — lowering it lets you reach a bend without actually pulling the certificated "
                        + "number, which is the point of having it on a test page.",
            Live = false,
            LiveNote = "The structure is built when the aeroplane is: FLY again to feel it.",
            Get = c => c.Limits.GMax,
            Set = (c, v) => c.Limits.GMax = v,
        });

        TunableRegistry.Register(new Tunable
        {
            Id = "limits.gMin",
            Group = g, Derived = true,
            Label = "Negative g limit",
            Units = " g",
            Min = -12, Max = -0.5, Step = 0.1, FineStep = 0.05, Decimals = 2,
            Rule = "Certificated — except where Ty has already set it himself (the Pitts flies −5 because he asked for it).",
            Description = "How much negative g the airframe takes before it starts being damaged. Same as the positive limit: it is "
                        + "the damage threshold, so moving it is how you reach the failure without flying to the real number.",
            Live = false,
            LiveNote = "The structure is built when the aeroplane is: FLY again to feel it.",
            Get = c => c.Limits.GMin,
            Set = (c, v) => c.Limits.GMin = v,
        });
    }
}
