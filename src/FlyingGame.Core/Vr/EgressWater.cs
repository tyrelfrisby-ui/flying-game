using System;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Vr
{
    // THE EGRESS <-> CRASH-WATER SEAM (CONTRACT.md "Egress ↔ crash-water seam"). The pilot's way out needs four facts
    // about the water: where its surface is, how dense it is, which way it flows, and how far the cockpit has filled.
    // This lane does NOT model water: lane/claude-crash-water does (AirframeFlooding, HydroElement, WreckSim). So the
    // egress code reads these two narrow interfaces, this lineage fills them with what it already has (FloatHydro's
    // surface and a stated stub for the filling), and the integration branch replaces the stub with crash-water's
    // own numbers (CrashWaterSeam.cs carries the exact member mapping, compiled out until then).
    //
    // FRAME. Everything here is the Unity world frame (x east, y UP, z north), the frame the headset code places the
    // pilot in. The sim frame is x north, y east, z down: sim (x, y, up) = (z, x, y) here.

    /// <summary>Where the aeroplane is with respect to the water — crash-water's WreckPhase, folded to what the pilot's
    /// exit needs to know (CrashWaterSeam.cs: InAir/OnLand → Dry, Plunging/Floating → Floating, Sinking, OnBottom).</summary>
    public enum CockpitWaterPhase { Dry = 0, Floating = 1, Sinking = 2, OnBottom = 3 }

    /// <summary>The cockpit's water, as the exit needs it.</summary>
    public interface ICockpitWaterState
    {
        CockpitWaterPhase Phase { get; }
        /// <summary>Water inside the cockpit / the cockpit's floodable volume, 0..1.</summary>
        double FloodFraction { get; }
        /// <summary>Height (m, up) of the water standing INSIDE the cockpit; NaN when it is dry.</summary>
        double InteriorWaterUp { get; }
        /// <summary>The outside water surface over (x, z) for a point at height <paramref name="y"/> (m, up), or null.</summary>
        double? SurfaceUpAt(double x, double z, double y);
        /// <summary>kg/m³.</summary>
        double WaterDensity { get; }
    }

    /// <summary>The water the pilot's own body is in once he has left (one body, Archimedes + drag: PilotSwim).</summary>
    public interface IPilotWater
    {
        /// <summary>Surface height (m, up) over (x, z) for a body at height <paramref name="y"/>, or null (no water).</summary>
        double? SurfaceUpAt(double x, double z, double y);
        /// <summary>Bed or ground height under (x, z), m up.</summary>
        double GroundUpAt(double x, double z);
        double Density { get; }
        /// <summary>Water velocity at a point (m/s, world): a river's flow; zero on a lake.</summary>
        Vec3 CurrentAt(double x, double z, double y);
    }

    /// <summary>Flat water at a fixed height over a flat bed (tests, and anything that has no world).</summary>
    public sealed class FlatPilotWater : IPilotWater
    {
        public double? Surface;
        public double Bed;
        public Vec3 Current;
        public double Density { get; set; } = FloatHydro.WaterDensity;
        public FlatPilotWater(double? surfaceUp, double bedUp = -100.0) { Surface = surfaceUp; Bed = bedUp; }
        public double? SurfaceUpAt(double x, double z, double y) => Surface.HasValue && Surface.Value > Bed ? Surface : null;
        public double GroundUpAt(double x, double z) => Bed;
        public Vec3 CurrentAt(double x, double z, double y) => Current;
    }

    /// <summary>The game world as this lineage has it: FloatHydro's surface (which honours its test override) over
    /// WorldTerrain's ground; still water (this lineage has no river current a body can read).</summary>
    public sealed class LivePilotWater : IPilotWater
    {
        public static readonly LivePilotWater Instance = new();
        public double Density => FloatHydro.WaterDensity;
        public double? SurfaceUpAt(double x, double z, double y)
        {
            double? w = FloatHydro.WaterSurfaceAt(z, x, y);
            return w.HasValue && w.Value >= WorldTerrain.GroundHeightAt(z, x) - 0.01 ? w : null;
        }
        public double GroundUpAt(double x, double z) => WorldTerrain.GroundHeightAt(z, x);
        public Vec3 CurrentAt(double x, double z, double y) => Vec3.Zero;
    }

    /// <summary>
    /// THIS LINEAGE'S COCKPIT WATER — A STUB, NOT A WATER MODEL. The aeroplane here never floods (the old ditching contact
    /// holds it at a fixed depth), so the cockpit's filling is stood in for by one first-order lag: once the sill is
    /// under the outside surface, the water inside rises toward the outside level with time constant
    /// <see cref="FillTauSec"/> (20 s, E — a light aeroplane's cabin is commonly said to fill in about a minute; crash-water's
    /// orifice flow through the door seals and vents, HydroElement, gives the real figure). crash-water's AirframeFlooding replaces every number here
    /// (CrashWaterSeam.cs). What the door gate needs from it is only the two levels, inside and out.
    /// </summary>
    public sealed class LineageCockpitWater : ICockpitWaterState
    {
        public const double FillTauSec = 20.0;

        private readonly IPilotWater _water;
        public CockpitWaterPhase Phase { get; private set; } = CockpitWaterPhase.Dry;
        public double FloodFraction { get; private set; }
        public double InteriorWaterUp { get; private set; } = double.NaN;
        public double WaterDensity => _water.Density;
        /// <summary>The cockpit's floor and its top (canopy / cabin roof), m up, the last <see cref="Step"/>.</summary>
        public double FloorUp { get; private set; }
        public double TopUp { get; private set; }

        public LineageCockpitWater(IPilotWater water) { _water = water ?? LivePilotWater.Instance; }

        public double? SurfaceUpAt(double x, double z, double y) => _water.SurfaceUpAt(x, z, y);

        public void Reset() { Phase = CockpitWaterPhase.Dry; FloodFraction = 0.0; InteriorWaterUp = double.NaN; }

        /// <summary>One frame: the cockpit's floor and top heights at the eye's (x, z) this frame.</summary>
        public void Step(double dt, double x, double z, double floorUp, double topUp)
        {
            FloorUp = floorUp; TopUp = Math.Max(topUp, floorUp + 0.1);
            double? s = _water.SurfaceUpAt(x, z, floorUp);
            if (!s.HasValue || s.Value <= floorUp)
            {
                // Out of the water (or never in it): whatever got in drains out of the same gaps; stub: at once.
                Phase = CockpitWaterPhase.Dry; FloodFraction = 0.0; InteriorWaterUp = double.NaN;
                return;
            }
            double outside = Math.Min(s.Value, TopUp);
            double inside = double.IsNaN(InteriorWaterUp) ? floorUp : InteriorWaterUp;
            if (dt > 0.0) inside += (outside - inside) * (1.0 - Math.Exp(-dt / FillTauSec));
            InteriorWaterUp = Math.Clamp(inside, floorUp, TopUp);
            FloodFraction = (InteriorWaterUp - floorUp) / (TopUp - floorUp);
            Phase = s.Value < TopUp ? CockpitWaterPhase.Floating : FloodFraction > 0.999 ? CockpitWaterPhase.OnBottom : CockpitWaterPhase.Sinking;
        }

        /// <summary>Tests: set the state directly.</summary>
        public void SetForTesting(CockpitWaterPhase phase, double floorUp, double topUp, double interiorUp)
        {
            Phase = phase; FloorUp = floorUp; TopUp = topUp; InteriorWaterUp = interiorUp;
            FloodFraction = double.IsNaN(interiorUp) ? 0.0 : Math.Clamp((interiorUp - floorUp) / (topUp - floorUp), 0.0, 1.0);
        }
    }

    /// <summary>
    /// THE PILOT-IN-WATER AUDIO GATE (Ty, 2026-11-02 14:31: "NO gurgling or bubbling sounds for the pilot in the water").
    /// Set by the headset egress while the pilot's body is in the water. Anything that plays bubble or gurgle sounds
    /// near the listener — today nothing in this lineage; lane/codex-crash-audio subscribes to CrashEvents.Flood for its
    /// gurgle — multiplies its gain by <see cref="BubbleGainForListener"/>. That lane is not edited from here: the gate
    /// is the seam, named in CONTRACT.md.
    /// </summary>
    public static class PilotWaterAudioGate
    {
        public static bool PilotInWater { get; set; }
        public static double BubbleGainForListener => PilotInWater ? 0.0 : 1.0;
    }
}
