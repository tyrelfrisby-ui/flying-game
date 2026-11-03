using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Default-off, per-camera visibility for the GLB's floating Sight_ bars. Only AirframeVisual
    /// binds a local driver; remote, lesson, widget and editor models remain unbound. Built-in
    /// camera callbacks bracket even manual Render/CopyFrom cameras. The stack restores the
    /// outer camera after a nested water reflection, then returns to hidden after the main render.
    /// </summary>
    [ExecuteAlways]
    public sealed class CockpitSights : MonoBehaviour
    {
        private Renderer[] _bars = System.Array.Empty<Renderer>();
        private FlightSimDriver _owner;
        private readonly Stack<Camera> _cameras = new();

        public static bool IsSight(Transform node)
        {
            for (var t = node; t != null; t = t.parent)
                if (t.name.StartsWith("Sight_", System.StringComparison.Ordinal)
                    || t.name.StartsWith("Model:Sight_", System.StringComparison.Ordinal)) return true;
            return false;
        }

        public static void Install(GameObject model)
        {
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                if (IsSight(r.transform)) { model.AddComponent<CockpitSights>(); return; }
        }

        private void Collect()
        {
            var bars = new List<Renderer>();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (!IsSight(r.transform)) continue;
                bars.Add(r);
                r.enabled = false;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            _bars = bars.ToArray();
        }

        public void BindLocal(FlightSimDriver owner)
        {
            _owner = owner;
            SetVisible(false);
        }

        private void OnEnable()
        {
            if (_bars.Length == 0) Collect();   // also re-collect after an editor domain reload
            SetVisible(false);
            Camera.onPreCull += BeforeCamera;
            Camera.onPostRender += AfterCamera;
        }

        private void OnDisable()
        {
            Camera.onPreCull -= BeforeCamera;
            Camera.onPostRender -= AfterCamera;
            _cameras.Clear();
            SetVisible(false);
        }

        /// <summary>A camera that shows exactly what another one does: the Quest clip recorder's mono camera at the
        /// head (<see cref="QuestRecorder"/>). It is not Camera.main, so without this the sight was switched off for
        /// every recorded frame and never appeared in a clip (Ty, 2026-11-02). Asked as the camera it mirrors.</summary>
        public static Camera MirrorCamera, MirrorOf;

        private bool Allows(Camera camera)
        {
            if (camera != null && camera == MirrorCamera) camera = MirrorOf;
            if (_owner == null || !transform.IsChildOf(_owner.transform) || camera == null || camera != Camera.main) return false;
            var chase = camera.GetComponent<ChaseCamera>();
            return chase != null && chase.CockpitSightsAllowed(_owner);
        }

        private void BeforeCamera(Camera camera)
        {
            _cameras.Push(camera);
            SetVisible(Allows(camera));
        }

        private void AfterCamera(Camera camera)
        {
            // A mismatched callback must fail closed, rather than leave an enabled renderer behind.
            if (_cameras.Count == 0 || _cameras.Pop() != camera) _cameras.Clear();
            SetVisible(_cameras.Count > 0 && Allows(_cameras.Peek()));
        }

        private void SetVisible(bool visible)
        {
            foreach (var r in _bars) if (r != null) r.enabled = visible;
        }
    }
}
