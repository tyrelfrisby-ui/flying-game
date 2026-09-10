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
            // A scattering of thermals over the farm grid, various strengths — the same set on every plateau
            // (positions relative to that plateau's airport, tops relative to its elevation).
            (Vec3 pos, double r, double core, double top)[] valley =
            {
                // Bigger and stronger (owner 2026-09-09: the 2-33 could not stay up): cores 180–240 m, 4.5–7 m/s.
                (new Vec3(300, 300, 0), 180, 6.0, 2000),
                (new Vec3(-200, 500, 0), 140, 4.5, 1600),
                (new Vec3(800, -200, 0), 210, 7.0, 2400),
                (new Vec3(200, -600, 0), 150, 5.0, 1800),
                // The ploughed farmer's field north of the runway: dark earth, a strong, wide thermal.
                (new Vec3((CropField.Valley.X0 + CropField.Valley.X1) / 2, (CropField.Valley.Y0 + CropField.Valley.Y1) / 2, 0), 240, 6.5, 2400),
            };
            var set = new System.Collections.Generic.List<(Vec3 pos, double r, double core, double top)>();
            for (int p = 0; p < WorldTerrain.PlateauCount; p++)
            {
                double dy = WorldTerrain.PlateauDy(p), elev = WorldTerrain.Airports[p].ElevationM;
                foreach (var (pos, r, core, top) in valley) set.Add((new Vec3(pos.X, pos.Y + dy, -elev), r, core, top + elev));
            }
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
