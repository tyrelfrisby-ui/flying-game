using System.Collections.Generic;
using FlyingGame.Core.DataContracts;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Real 3-D airframe models (owner 2026-09-12: the USDZ files on the Desktop, converted to OBJ under
    /// Resources/Models/&lt;id&gt;/). A model replaces the procedural body, wings, tail, canopy, gear and prop of its
    /// type; the procedural build still runs hidden underneath so hinges, debris splitting, gear retraction and the
    /// prop keep their bookkeeping. Each model is rotated so its nose points along sim +x (Unity +z), scaled so its
    /// span matches the config, and dropped so its lowest point sits at the wheels' contact height.
    /// </summary>
    public static class AirframeModels
    {
        /// <summary>Per-type placement: yaw (deg) that turns the model's nose to Unity +z, an extra pitch/roll fix, and
        /// whether the model's largest horizontal extent is its span (true) or its length (false).</summary>
        public readonly struct Spec
        {
            public readonly float Yaw, Pitch, Roll; public readonly bool LargestIsSpan; public readonly float ScaleTrim;
            public Spec(float yaw, float pitch = 0f, float roll = 0f, bool largestIsSpan = true, float scaleTrim = 1f) { Yaw = yaw; Pitch = pitch; Roll = roll; LargestIsSpan = largestIsSpan; ScaleTrim = scaleTrim; }
        }

        // Filled in from ModelRender views (build/models/*.png): every one of these USDZ exports is Y-up; the yaw is
        // what turns each model's nose to +z.
        public static readonly Dictionary<string, Spec> Specs = new()
        {
            { "c172-like", new Spec(0f) },
            { "pa18-cub-like", new Spec(0f) },
            { "dc3-like", new Spec(0f) },
            { "boeing-737-like", new Spec(0f) },
            { "geebee-r2-like", new Spec(0f) },
            { "seminole-like", new Spec(0f) },
            { "glider-2-33-like", new Spec(0f) },
        };
        public static IEnumerable<string> Ids => Specs.Keys;

        public static bool Has(string id) => id != null && Specs.ContainsKey(id) && Resources.Load<GameObject>($"Models/{id}/{id}") != null;

        /// <summary>Instantiate the model under <paramref name="parent"/>, oriented, scaled to the config span and grounded
        /// on the wheels. Returns the instance (null if none) and its local bounds after placement.</summary>
        public static GameObject Place(Transform parent, string id, AircraftConfig cfg, out Bounds bounds)
        {
            bounds = new Bounds();
            var prefab = Resources.Load<GameObject>($"Models/{id}/{id}");
            if (prefab == null || !Specs.TryGetValue(id, out Spec spec)) return null;
            var inst = Object.Instantiate(prefab, parent, false);
            inst.name = "Model";
            foreach (Collider c in inst.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.Euler(spec.Pitch, spec.Yaw, spec.Roll);
            inst.transform.localScale = Vector3.one;
            // Bounds in the parent's frame (after rotation, unit scale).
            Bounds b = LocalBounds(inst, parent);
            float span = 2f * HalfSpan(cfg);
            float extent = spec.LargestIsSpan ? Mathf.Max(b.size.x, 0.01f) : Mathf.Max(b.size.z, 0.01f);
            float s = span / extent * spec.ScaleTrim;
            inst.transform.localScale = Vector3.one * s;
            b = LocalBounds(inst, parent);
            // Centre laterally and along the length on the config's CG station (x = 0), then sit on the wheels.
            float gearDown = 0f; foreach (GearConfig g in cfg.Gear) gearDown = Mathf.Max(gearDown, (float)g.Pos[2]);
            Vector3 shift = new Vector3(-b.center.x, -gearDown - b.min.y, -b.center.z + (float)LengthCentreOffset(cfg));
            inst.transform.localPosition = shift;
            bounds = LocalBounds(inst, parent);
            return inst;
        }

        private static float HalfSpan(AircraftConfig cfg)
        {
            float s = 0f;
            foreach (SurfaceConfig sf in cfg.Surfaces) { if (!sf.Id.ToLowerInvariant().Contains("wing")) continue; foreach (StripConfig st in sf.Strips) s = Mathf.Max(s, Mathf.Abs((float)st.Pos[1])); }
            return s > 0f ? s : 5f;
        }

        /// <summary>Where the config's fuselage midpoint sits relative to the CG along x (so the model's length centre lands there).</summary>
        private static double LengthCentreOffset(AircraftConfig c)
        {
            double minTe = double.MaxValue;
            foreach (SurfaceConfig sf in c.Surfaces) foreach (StripConfig st in sf.Strips) minTe = System.Math.Min(minTe, st.Pos[0] - 0.75 * st.Chord);
            double len = c.Fuselage.Crossflow?.LengthM > 0 ? c.Fuselage.Crossflow.LengthM : 7.0;
            double tailX = minTe - 0.2, noseX = tailX + len;
            return (tailX + noseX) / 2;
        }

        public static Bounds LocalBounds(GameObject go, Transform frame)
        {
            bool any = false; var b = new Bounds();
            foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                foreach (Vector3 v in mf.sharedMesh.vertices)
                {
                    Vector3 p = frame.InverseTransformPoint(mf.transform.TransformPoint(v));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }
    }
}
