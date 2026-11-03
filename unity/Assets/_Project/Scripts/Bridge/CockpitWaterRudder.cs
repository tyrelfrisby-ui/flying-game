using FlyingGame.Sim;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// THE J-3 FLOATPLANE'S WATER-RUDDER RETRACT, in the cockpit (Ty 2026-11-02, t278u: "the water rudder is springloaded
    /// down so when it is down the female side of the cable in the cockpit is simple let go of and sits on the floor
    /// vertically left of the stick. make it red and label it water rudder. picking it up and hanging it on the hook
    /// raises the water rudder").
    ///
    /// The model (build_j3_cub.py, floats) carries three nodes: <c>WaterRudder_Handle</c>, a rig SLIDE on
    /// <c>water_rudder_up</c> from the floor fairlead to the hook on the front seat back (AirframeRig moves it from
    /// <see cref="Aircraft.WaterRudderUp01"/>, like every other rigged part); <c>WaterRudder_Cable</c>, a steel tube of
    /// unit length up its own local Y (glTF +Y) from the fairlead, which this aims at the handle's ferrule and stretches
    /// to length every frame; and the blade (<c>WaterRudder_Blade_L</c>, the rig turns it on the same param).
    ///
    /// The hand: <see cref="Hold"/> while a hand has the handle (the fraction of the way from the floor to the hook, 0..1,
    /// which the sim's cable takes directly, Aircraft.WaterRudderHeldUp01), <see cref="Release"/> when it lets go: within
    /// <see cref="HookSnapFraction"/> of the hook it hangs there (UP), anywhere else the spring takes the handle back to
    /// the floor (DOWN). The Quest hands (lane/claude-vr-hands, not merged into this lane yet) see the handle as a rig
    /// slide (RigHandle.Slide) and need only route the param "water_rudder_up" here: Hold on the drag, Release on the
    /// open hand. Phone and controller: the GEAR slot toggles it (TouchFlightControls, Aircraft.ToggleWaterRudder).
    /// </summary>
    public sealed class CockpitWaterRudder
    {
        /// <summary>Let go within this fraction of the travel from the hook and the ring drops onto it (6 cm of 0.258 m:
        /// the model's hook_snap_m over its rig_map_m).</summary>
        public const double HookSnapFraction = 0.06 / 0.2585;

        private Transform _cable, _handle;
        private Vector3 _cableScale0;
        private bool _looked;

        public bool Bound => _looked;
        public bool Active => _cable != null && _handle != null;

        /// <summary>Find the nodes once per model. glTFast names a node's GameObject after the node; AirframeVisual
        /// renames MESH objects "Model:&lt;node&gt;", so both spellings are accepted.</summary>
        public void Bind(Transform modelRoot)
        {
            _looked = true; _cable = _handle = null;
            if (modelRoot == null) return;
            foreach (Transform t in modelRoot.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.StartsWith("Model:") ? t.name.Substring(6) : t.name;
                if (n == "WaterRudder_Cable") _cable ??= t;
                else if (n == "WaterRudder_Handle") _handle ??= t;
            }
            if (_cable != null) _cableScale0 = _cable.localScale;
            if (Active) Debug.Log("[WaterRudder] cockpit handle and cable bound");
        }

        public void Forget() { _looked = false; _cable = _handle = null; }

        /// <summary>After the rig has placed the handle this frame: the cable from the fairlead to the ferrule (the
        /// handle node's origin), straight, because the spring keeps it taut.</summary>
        public void LateUpdate()
        {
            if (!Active) return;
            Transform parent = _cable.parent;
            Vector3 from = _cable.position, to = _handle.position;
            Vector3 d = to - from;
            float len = d.magnitude;
            float lossy = parent != null ? parent.lossyScale.y : 1f;
            if (len < 1e-4f) { _cable.localScale = new Vector3(_cableScale0.x, 1e-4f, _cableScale0.z); return; }
            _cable.rotation = Quaternion.FromToRotation(Vector3.up, d / len);
            _cable.localScale = new Vector3(_cableScale0.x, len / Mathf.Max(1e-6f, lossy), _cableScale0.z);
        }

        /// <summary>A hand has the handle at this fraction of the way to the hook (0 floor .. 1 hook).</summary>
        public static void Hold(Aircraft ac, double up01)
        {
            if (ac == null || !ac.HasWaterRudderRetract) return;
            ac.WaterRudderHeldUp01 = System.Math.Clamp(up01, 0.0, 1.0);
        }

        /// <summary>The hand opens: on the hook if it was let go close enough to it, else back to the floor.</summary>
        public static void Release(Aircraft ac)
        {
            if (ac == null || !ac.HasWaterRudderRetract) return;
            double at = ac.WaterRudderHeldUp01 ?? ac.WaterRudderUp01;
            ac.WaterRudderHeldUp01 = null;
            ac.SetWaterRudderHooked(at >= 1.0 - HookSnapFraction);
        }
    }
}
