namespace FlyingGame.Core;

/// <summary>
/// THE HINGE LINES of a model's own flight-control surfaces, where neither the model nor its rig carries them (Ty t917u
/// 2026-11-06: "The elevator on the pitts is not visibly moving").
///
/// Why this exists. A model with its own rig (docs/models/&lt;model&gt;-rig.json) skips the hinge carve, because a rigged
/// model's moving parts are already separate, pivoted nodes. The Pitts' rig (VR cockpit work, 2026-11-02 on) drives the
/// cockpit only: sticks, pedals, throttles, canopy, switches. Its Elevator_L/R, four ailerons and rudder ARE separate
/// meshes, but no rig row moves them, so from the day the rig arrived every flight surface on the Pitts stayed still.
/// They cannot simply be given rig rows: a rig node turns about its own origin, and the Pitts' surface nodes have their
/// origins at the model origin, with no "_HingeAxis" marker either (the Extra has both).
///
/// So the lines come from the model's own build script, copied by hand, and the Bridge (AirframeVisual) turns each named
/// node about its line on the same deflections as everything else. tools/FlightTests/SurfaceHingeTests holds the numbers
/// against the drawn meshes in every livery's GLB (the elevator's face 4 mm aft of its hinge, the rudder's round nose
/// centred on its hinge, the aileron faces 3 mm aft of theirs).
///
/// When a model is rebuilt with its surface origins ON their hinges and a "_HingeAxis" marker (as build_ea300.py does),
/// or its rig gains rows for the surfaces, its entry here should go: those win over this table.
///
/// Points are in the surface NODE's own glTF frame (metres, as the file stores them), which for the Pitts is the model's
/// frame: its surface nodes carry no transform (the test holds that). P0 → P1 runs along the hinge, either sense.
///
/// THE SLAVE STRUTS. The Pitts' upper and lower ailerons are joined by a strut on each side; the rig's strut follower
/// (AirframeRig) keeps one between its two pins, Strut_Slave_&lt;s&gt;_PinUpper / _PinLower, which the GLB does not carry.
/// <see cref="PinsFor"/> gives them (measured on the strut mesh's two ends), and the Bridge hangs them on the ailerons
/// before the rig binds, so the struts ride with the ailerons instead of standing still between moving horns.
/// </summary>
public static class SurfaceHinges
{
    public readonly struct Line
    {
        /// <summary>The GLB node's name ("Elevator_L").</summary>
        public readonly string Node;
        /// <summary>"aileron", "elevator" or "rudder": which deflection drives it.</summary>
        public readonly string Surface;
        public readonly double X0, Y0, Z0, X1, Y1, Z1;
        public Line(string node, string surface, double x0, double y0, double z0, double x1, double y1, double z1)
        {
            Node = node; Surface = surface; X0 = x0; Y0 = y0; Z0 = z0; X1 = x1; Y1 = y1; Z1 = z1;
        }
    }

    // tools/own_models/build_pitts_s2b.py, glTF = (-x, z, y) of the script's frame (+x forward, +y left, +z up):
    //  - horizontal tail: "straight hinge x -3.30", the stab's chord plane z 0.071 (Surf(SLE, STE, 0.071, 0.09), symmetric),
    //    elevators from the root cut E_IN 0.082 to B_S 1.1404.
    //  - fin / rudder: hinge at H_T -3.30, vertical (rudder_sec: x_from = H_T at every height), z -0.320 .. Z_FT 0.8465.
    //  - upper wing: HU = LEU0 - HF * CH = 0.664 - 0.0948|y| - 0.75 * 1.016 (swept), chord line at HF:
    //    0.613 - 0.762 sin 1 deg + yc(0.75, 0.010) * 1.016 = 0.6064; ailerons AU_IN 1.20 .. AU_OUT 2.79.
    //  - lower wing: HL = -0.762 (unswept), 3 deg dihedral: -0.551 + tan 3 deg |y| - 0.762 sin 1 deg (m = 0);
    //    ailerons AL_IN 1.15 .. AL_OUT 2.638.
    private static readonly Line[] Pitts =
    {
        new("Elevator_L", "elevator", 3.300, 0.0710, 0.0820, 3.300, 0.0710, 1.1404),
        new("Elevator_R", "elevator", 3.300, 0.0710, -0.0820, 3.300, 0.0710, -1.1404),
        new("Rudder", "rudder", 3.300, -0.3200, 0.0, 3.300, 0.8465, 0.0),
        new("Aileron_UL", "aileron", 0.21176, 0.6064, 1.200, 0.36249, 0.6064, 2.790),
        new("Aileron_UR", "aileron", 0.21176, 0.6064, -1.200, 0.36249, 0.6064, -2.790),
        new("Aileron_LL", "aileron", 0.762, -0.50403, 1.150, 0.762, -0.42605, 2.638),
        new("Aileron_LR", "aileron", 0.762, -0.50403, -1.150, 0.762, -0.42605, -2.638),
    };

    /// <summary>A slave strut's pin, an empty to hang under the surface node <see cref="On"/>, in that node's frame.</summary>
    public readonly struct Pin
    {
        public readonly string Node, Strut, On;
        public readonly double X, Y, Z;
        public Pin(string node, string strut, string on, double x, double y, double z) { Node = node; Strut = strut; On = on; X = x; Y = y; Z = z; }
    }

    // The centre of each end cap of Strut_Slave_L in pitts-s2b-like-own.glb (top 2.0265 m out on the upper aileron's horn,
    // bottom 1.9255 m on the lower's); R mirrored.
    private static readonly Pin[] PittsPins =
    {
        new("Strut_Slave_L_PinUpper", "Strut_Slave_L", "Aileron_UL", 0.2601, 0.5466, 2.0265),
        new("Strut_Slave_L_PinLower", "Strut_Slave_L", "Aileron_LL", 0.7116, -0.4017, 1.9255),
        new("Strut_Slave_R_PinUpper", "Strut_Slave_R", "Aileron_UR", 0.2601, 0.5466, -2.0265),
        new("Strut_Slave_R_PinLower", "Strut_Slave_R", "Aileron_LR", 0.7116, -0.4017, -1.9255),
    };

    private static readonly Dictionary<string, Pin[]> PinsByType = new()
    {
        ["pitts-s2b-like"] = PittsPins,
    };

    /// <summary>The slave-strut pins to add to a type's own model, or an empty list.</summary>
    public static IReadOnlyList<Pin> PinsFor(string? aircraftId) =>
        aircraftId != null && PinsByType.TryGetValue(aircraftId, out Pin[]? l) ? l : System.Array.Empty<Pin>();

    private static readonly Dictionary<string, Line[]> ByType = new()
    {
        ["pitts-s2b-like"] = Pitts,
    };

    /// <summary>The hinge lines for a type's own model (every livery's GLB shares them), or an empty list.</summary>
    public static IReadOnlyList<Line> For(string? aircraftId) =>
        aircraftId != null && ByType.TryGetValue(aircraftId, out Line[]? l) ? l : System.Array.Empty<Line>();
}
