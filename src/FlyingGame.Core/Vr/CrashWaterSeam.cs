// THE INTEGRATION BRANCH FILLS THIS IN (CONTRACT.md "Egress ↔ crash-water seam").
//
// lane/claude-crash-water (a different lineage, off lane/claude-island-nav) owns the water: AirframeFlooding,
// HydroElement, WreckSim (WreckPhase, WreckPiece, IWreckWorld), WaterPhysics. This lane reads the cockpit's water only
// through ICockpitWaterState / IPilotWater (EgressWater.cs), filled here by LineageCockpitWater (a stated stub) and
// LivePilotWater. Once both lanes are on one branch, define FLYINGGAME_CRASH_WATER (Core's csc.rsp and the test project)
// and this adapter replaces the stub: FlightSimDriver hands VrCockpitHands a CrashWaterCockpit instead of the
// LineageCockpitWater. Nothing in it is compiled until then, so it cannot break either build today.
//
// THE MAPPING (crash-water member → seam member), read off lane/claude-crash-water at 43f89f6:
//   Phase            the wreck piece that carries WreckAtom.Cabin (WreckSim.Pieces), or the intact airframe:
//                      WreckPhase.InAir, OnLand              → CockpitWaterPhase.Dry
//                      WreckPhase.Plunging, Floating         → Floating
//                      WreckPhase.Sinking                    → Sinking
//                      WreckPhase.OnBottom                   → OnBottom
//                    intact airframe (no wreck yet): AirframeFlooding.Active ? (cabin under → Sinking else Floating) : Dry
//   FloodFraction    cabin HydroElement: WaterM3 / VoidM3 (0 when VoidM3 is 0)
//   InteriorWaterUp  cabin box floor + FloodFraction × its vertical extent: floor = −(world z of the box's lowest point),
//                    extent = 2 · HydroElement.VerticalHalf(attitude) — the box is in the airframe body frame (x fwd,
//                    y right, z down) at HydroElement.Centre ± Half
//   SurfaceUpAt      IWreckWorld.WaterUpAt(simX = z, simY = x, up = y)   (sim x north = Unity z, sim y east = Unity x)
//   WaterDensity     WaterPhysics.RhoWater
//   IPilotWater      LiveWreckWorld: SurfaceUpAt as above; GroundUpAt = IWreckWorld.GroundUpAt(z, x); Density as above;
//                    CurrentAt: zero until crash-water carries a current
// AUDIO: lane/codex-crash-audio plays its gurgle from CrashEvents.Flood (FloodState.AirEscapeM3s). It must multiply that
// gain by PilotWaterAudioGate.BubbleGainForListener while the listener is the pilot in the water (Ty, 14:31: no bubbles
// or gurgles for the pilot, ever).
// RENDER: crash-water's Editor/WaterRender.cs is an editor fixture using FlyingGame/Water as it is; this lane changes that
// shader (see-through by depth, the underside, _WaterSeeThrough / _UwOn uniforms) — re-render that fixture after the merge.

#if FLYINGGAME_CRASH_WATER
using FlyingGame.Core.Wreck;

namespace FlyingGame.Core.Vr
{
    public sealed class CrashWaterCockpit : ICockpitWaterState
    {
        private readonly WreckSim _wreck;
        private readonly AirframeFlooding _flooding;
        private readonly System.Func<FlyingGame.Core.MathTypes.RigidBodyState> _state;

        public CrashWaterCockpit(WreckSim wreck, AirframeFlooding flooding, System.Func<FlyingGame.Core.MathTypes.RigidBodyState> state)
        { _wreck = wreck; _flooding = flooding; _state = state; }

        private HydroElement Cabin
        {
            get
            {
                foreach (HydroElement e in _flooding.Attached) if (e.Atom == WreckAtom.Cabin) return e;
                return null;
            }
        }

        public CockpitWaterPhase Phase
        {
            get
            {
                // TO FILL: find the piece carrying the cabin in _wreck and map its WreckPhase as tabled above.
                return _flooding.Active ? CockpitWaterPhase.Floating : CockpitWaterPhase.Dry;
            }
        }

        public double FloodFraction { get { HydroElement c = Cabin; return c == null || c.VoidM3 <= 0 ? 0.0 : c.WaterM3 / c.VoidM3; } }

        public double InteriorWaterUp
        {
            get
            {
                HydroElement c = Cabin;
                if (c == null || c.WaterM3 <= 0) return double.NaN;
                var s = _state();
                double half = c.VerticalHalf(s.Attitude);
                var centre = s.Position + s.Attitude.Rotate(c.Centre - default(FlyingGame.Core.MathTypes.Vec3));   // TO FILL: minus the CG
                double floorUp = -centre.Z - half;
                return floorUp + FloodFraction * 2.0 * half;
            }
        }

        public double? SurfaceUpAt(double x, double z, double y) => _wreck.World.WaterUpAt(z, x, y);
        public double WaterDensity => WaterPhysics.RhoWater;
    }
}
#endif
