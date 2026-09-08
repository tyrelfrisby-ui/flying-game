using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Builds the soaring scenery and registers its lift with the atmosphere: a ridge (long hill) for
    /// slope soaring and a few thermal columns. The bubble field already shows wind, so the ridge lift
    /// streams bubbles up the windward face and thermals show as rising bubble columns — the invisible
    /// air made visible. Press H to toggle a marker showing where the thermals are.
    /// </summary>
    public sealed class SoaringScenery : MonoBehaviour
    {
        private void Start()
        {
            // Ridge lift now lives on the first canyon wall (WorldBuilder); only thermals here.
            BuildThermals();
        }

        private void BuildThermals()
        {
            // A scattering of thermals over the farm grid, various strengths.
            (Vec3 pos, double r, double core, double top)[] set =
            {
                (new Vec3(300, 300, 0), 120, 4.5, 1800),
                (new Vec3(-200, 500, 0), 90, 3.0, 1400),
                (new Vec3(800, -200, 0), 140, 5.5, 2200),
                (new Vec3(200, -600, 0), 100, 3.5, 1600),
            };
            foreach (var (pos, r, core, top) in set)
            {
                var th = new Thermal(pos, r, core, top);
                th.LeanPerM = new Vec3(0.05, 0, 0); // slight downwind lean with height
                Atmosphere.Thermals.Add(th);

                // Faint translucent marker cylinder so the columns are findable (toggle with H).
                var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.Destroy(marker.GetComponent<Collider>());
                marker.name = "ThermalMarker";
                Vector3 baseU = CoordinateMap.ToUnity(pos);
                marker.transform.position = new Vector3(baseU.x, (float)top / 2f, baseU.z);
                marker.transform.localScale = new Vector3((float)r * 2f, (float)top / 2f, (float)r * 2f);
                var m = new Material(Shader.Find("Unlit/Color")) { color = new Color(1f, 0.8f, 0.3f, 1f) };
                marker.GetComponent<MeshRenderer>().material = m;
                marker.GetComponent<MeshRenderer>().enabled = false; // off by default (H toggles)
                marker.tag = "Untagged";
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.H))
            {
                foreach (var mr in FindThermalMarkers())
                {
                    mr.enabled = !mr.enabled;
                }
            }
        }

        private static System.Collections.Generic.List<MeshRenderer> FindThermalMarkers()
        {
            var list = new System.Collections.Generic.List<MeshRenderer>();
            foreach (var go in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (go.gameObject.name == "ThermalMarker")
                {
                    list.Add(go);
                }
            }
            return list;
        }

        /// <summary>Triangular-prism ridge: peaked cross-section swept along its axis (Unity z).</summary>
    }
}
