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

        /// <summary>Thermal tops = cumulus base, metres above each plateau's field (~6,500 ft: a good summer day).</summary>
        public const double CloudBaseAglM = 2000;

        private void BuildThermals()
        {
            // A scattering of thermals over the farm grid, various strengths — the same set on every plateau
            // (positions relative to that plateau's airport, tops relative to its elevation).
            (Vec3 pos, double r, double core, double top)[] valley =
            {
                // Bigger and stronger (owner 2026-09-09: the 2-33 could not stay up): cores 180–240 m, 4.5–7 m/s.
                // ... and again twice as wide and twice as strong (owner 2026-09-10: "for easier thermalling").
                // All tops at ONE height (owner 2026-10-03, cumulus): every thermal of the day stops at the same
                // condensation level, so the cloud bases line up — strengths and widths still vary.
                // CLEAR of the runways and their approach paths (owner 2026-10-06: "they still float farther than i think they should
                // … the training glider hit a thermal right over the runway and climbed way up"): two of these sat ON the main
                // runway (12 and 14 m/s cores), so every landing flew through rising air. Now each edge stays ≥ 700 m off the
                // main runway's line (x along, y = 0) and ≥ 600 m off the cross runway's (x = 450): still close by for a glider.
                (new Vec3(-1000, 1300, 0), 360, 12.0, CloudBaseAglM),
                (new Vec3(-1100, -1250, 0), 280, 9.0, CloudBaseAglM),
                (new Vec3(1900, -1500, 0), 420, 14.0, CloudBaseAglM),
                (new Vec3(2000, 1500, 0), 300, 10.0, CloudBaseAglM),
                // The ploughed farmer's field east of the runway: dark earth, a strong thermal — narrowed and set to the field's
                // south side so the cross runway's eastern approach stays in still air.
                (new Vec3((CropField.Valley.X0 + CropField.Valley.X1) / 2 - 150, (CropField.Valley.Y0 + CropField.Valley.Y1) / 2, 0), 300, 13.0, CloudBaseAglM),
            };
            var set = new System.Collections.Generic.List<(Vec3 pos, double r, double core, double top)>();
            for (int p = 0; p < WorldTerrain.PlateauCount; p++)
            {
                double dy = WorldTerrain.PlateauDy(p), elev = WorldTerrain.Airports[p].ElevationM;
                foreach (var (pos, r, core, top) in valley) set.Add((new Vec3(pos.X, pos.Y + dy, -elev), r, core, top + elev));
            }
            int seed = 0;
            foreach (var (pos, r, core, top) in set)
            {
                var th = new Thermal(pos, r, core, top);
                // Lean comes from the wind itself now (Thermal.CoreAt), so a circling glider stays in the column.
                Atmosphere.Thermals.Add(th);
                Cumulus.ForThermal(th, seed++);   // its cumulus, base at the thermal's top

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
