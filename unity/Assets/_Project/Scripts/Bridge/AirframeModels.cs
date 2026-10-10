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
            public readonly Vector3 ModelForward, ModelUp;   // which MODEL axes are the nose direction and up
            public readonly bool LargestIsSpan; public readonly float ScaleTrim;
            /// <summary>Model exported gear-UP (no wheels): its lowest point (belly / nacelles) sits this far above the
            /// wheel contact plane, and the procedural gear stays visible under it (owner 2026-09-14: the DC-3 sat on its belly).</summary>
            public readonly float GroundClearanceM;
            public bool ShowProceduralGear => GroundClearanceM > 0f;
            /// <summary>Model exported sitting on its tail (three-point attitude): pitch it nose-DOWN by this much so its fuselage
            /// datum lies along the sim's body x axis (owner 2026-10-10: the Extra was drawn ~11° nose-high).</summary>
            public readonly float PitchDownDeg;
            public Spec(Vector3 modelForward, Vector3 modelUp, bool largestIsSpan = true, float scaleTrim = 1f, float groundClearanceM = 0f, float pitchDownDeg = 0f) { ModelForward = modelForward; ModelUp = modelUp; LargestIsSpan = largestIsSpan; ScaleTrim = scaleTrim; GroundClearanceM = groundClearanceM; PitchDownDeg = pitchDownDeg; }
            /// <summary>Rotation that carries the model's forward/up axes onto Unity +z / +y (then the three-point fix).</summary>
            public Quaternion Rotation => Quaternion.AngleAxis(PitchDownDeg, Vector3.right) * Quaternion.Inverse(Quaternion.LookRotation(ModelForward, ModelUp));
        }

        // Confirmed from ModelRender views (build/models/*.png, camera at +x looks at the model's +x side): the Cub,
        // Gee Bee, Apache and Astir exports have the nose along +x with y up; the C-47 and 737 already point +z; the
        // Cessna 172 export lies on its side (height along x, length along y, span along z).
        public static readonly Dictionary<string, Spec> Specs = new()
        {
            { "c172-like", new Spec(new Vector3(0f, 1f, 0f), new Vector3(1f, 0f, 0f)) },
            { "pa18-cub-like", new Spec(Vector3.right, Vector3.up) },
            { "dc3-like", new Spec(Vector3.forward, Vector3.up, groundClearanceM: 0.7f) },     // gear-up export
            { "boeing-737-like", new Spec(Vector3.forward, Vector3.up, groundClearanceM: 1.2f) },   // gear-up export
            { "geebee-r2-like", new Spec(Vector3.right, Vector3.up) },
            { "seminole-like", new Spec(Vector3.right, Vector3.up) },
            // glider-2-33-like: model OFF (owner 2026-10-06: "the skin for the schweitzer 2-33 is way off — go back to your initial
            // drawing until we find a good skin"); the procedural trainer is drawn. Re-add { "glider-2-33-like", new Spec(Vector3.right, Vector3.up) }.
            // Sketchfab GLBs (owner 2026-10-02; licences + credits in each folder's LICENSE.txt), imported by glTFast.
            // Extra: exported in the three-point attitude (its tailwheel 0.11 m below its mains) — levelled 11° nose-down
            // (build/cg-check.csv: drawn wing, prop hub and gear then line up with the sim's).
            { "extra-300-like", new Spec(Vector3.left, Vector3.forward, pitchDownDeg: 11f) },        // nose -x, top +z (ModelRender)
            { "pa28-archer-like", new Spec(Vector3.right, Vector3.forward) },     // helijah exports: nose +x, top +z
            { "cirrus-sr22-like", new Spec(Vector3.right, Vector3.forward) },
            { "stearman-pt17-like", new Spec(Vector3.right, Vector3.forward) },
            { "p51d-like", new Spec(Vector3.down, Vector3.forward) },             // nose -y, top +z
            { "f86-sabre-like", new Spec(Vector3.up, Vector3.forward) },        // nose +y, top +z. Aidan6604 (the Spark_Customs file is a posed Sabre+MiG scene, one merged mesh)
            { "target-drone-like", new Spec(Vector3.right, Vector3.forward) },
            { "hughes-h4-like", new Spec(Vector3.right, Vector3.forward) },      // helijah export (same convention as his others)
            { "glider-eb29r-like", new Spec(new Vector3(0f, -1f, 0f), new Vector3(0f, 0f, 1f)) },   // export stands on its tail: nose along -y, height along z
        };
        public static IEnumerable<string> Ids => Specs.Keys;

        /// <summary>Types that wear another type's model (owner 2026-10-05: "the cub on floats and cub on bushwheels should
        /// both use the good skin from the PA-18 Super Cub"). They keep their OWN gear — the floats, the tundra tyres — so the
        /// model's wheels are taken off (<see cref="GearBoxMesh"/>) and the model is set by its wing, not its wheels.</summary>
        public static readonly Dictionary<string, string> Alias = new()
        {
            { "pa18-floats-like", "pa18-cub-like" },
            { "pa18-bush-like", "pa18-cub-like" },
        };
        public static string ModelId(string id) => id != null && Alias.TryGetValue(id, out string m) ? m : id;
        public static bool UsesOwnGear(string id) => id != null && Alias.ContainsKey(id);
        /// <summary>The model's own gear as boxes in the MESH's coordinates (the OBJ's units, its x mirrored by the importer): the
        /// Super Cub's main gear (Object_7: x −212…−125, y −135…−56, z ±96 in the file) and its tailwheel.</summary>
        public static readonly Dictionary<string, (Vector3 min, Vector3 max)[]> GearBoxMesh = new()
        {
            { "pa18-cub-like", new[]
                {
                    (new Vector3(118f, -137f, -100f), new Vector3(219f, -54f, 100f)),   // main gear: legs, axle, wheels
                    (new Vector3(-331f, -33f, -7f), new Vector3(-277f, 0.5f, 7f)),      // tailwheel and its spring (file x 278…330)
                } },
        };

        public static bool Has(string id) { string m = ModelId(id); return m != null && Specs.ContainsKey(m) && Resources.Load<GameObject>($"Models/{m}/{m}") != null; }

        /// <summary>Instantiate the model under <paramref name="parent"/>, oriented, scaled to the config span and grounded
        /// on the wheels. Returns the instance (null if none) and its local bounds after placement.</summary>
        public static GameObject Place(Transform parent, string id, AircraftConfig cfg, out Bounds bounds)
        {
            bounds = new Bounds();
            bool ownGear = UsesOwnGear(id);
            id = ModelId(id);
            var prefab = Resources.Load<GameObject>($"Models/{id}/{id}");
            if (prefab == null || !Specs.TryGetValue(id, out Spec spec)) return null;
            var inst = Object.Instantiate(prefab, parent, false);
            inst.name = "Model";
            foreach (Collider c in inst.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = spec.Rotation;
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
            // Flying boat (hull, no wheels): the hull keel is the "contact" — its depth below the CG, as the physics floats it.
            if (cfg.Floats != null && cfg.Floats.Count == 1 && cfg.Gear.Count == 0) gearDown = (float)cfg.Floats.KeelZ;
            // Along the length: line the model's OUTER WING up with the config's (owner 2026-10-04 — the length-centre
            // estimate put the 172's model 2 m behind its physics: it pivoted about the wrong point, and its hinges and prop
            // were nowhere near the sim's). The length centre is the fallback when the model's vertices can't be read.
            float zShift = -b.center.z + (float)LengthCentreOffset(cfg);
            if (OuterWingMid(inst, parent, HalfSpan(cfg), out float modelMid) && ConfigOuterWingMid(cfg, out float cfgMid))
            {
                Debug.Log($"[Airframe] {id}: aligned by the outer wing (shift {cfgMid - modelMid:F2} m; the length-centre estimate said {zShift:F2} m)");
                zShift = cfgMid - modelMid;
            }
            else Debug.Log($"[Airframe] {id}: outer wing not found in the model — placed by the length-centre estimate");
            float yShift = -gearDown - b.min.y + spec.GroundClearanceM;
            // A type wearing another's model sits by its WING (the model's wheels aren't its gear): the model's outer-wing
            // height on the config's.
            if (ownGear && OuterWingHeight(inst, parent, HalfSpan(cfg), out float mh) && ConfigOuterWingHeight(cfg, out float ch)) yShift = ch - mh;
            Vector3 shift = new Vector3(-b.center.x, yShift, zShift);
            inst.transform.localPosition = shift;
            bounds = LocalBounds(inst, parent);
            return inst;
        }

        /// <summary>The model's outer-wing chord midpoint along its length (parent frame, before the shift): the extent along z
        /// of the geometry between 65 % and 92 % of the half-span (wing only — the tailplane and struts don't reach out there).</summary>
        private static bool OuterWingMid(GameObject inst, Transform parent, float halfSpan, out float mid)
        {
            mid = 0f; float lo = float.MaxValue, hi = float.MinValue; int n = 0;
            float cx = LocalBounds(inst, parent).center.x;
            foreach (MeshFilter mf in inst.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh m = mf.sharedMesh;
                if (m == null || !m.isReadable) continue;
                Matrix4x4 to = parent.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                foreach (Vector3 v in m.vertices)
                {
                    Vector3 p = to.MultiplyPoint3x4(v);
                    float ax = Mathf.Abs(p.x - cx);
                    if (ax < 0.65f * halfSpan || ax > 0.92f * halfSpan) continue;
                    lo = Mathf.Min(lo, p.z); hi = Mathf.Max(hi, p.z); n++;
                }
            }
            if (n < 8 || hi - lo > 0.5f * halfSpan) return false;   // nothing out there, or not a wing
            mid = 0.5f * (lo + hi);
            return true;
        }

        /// <summary>The model's outer-wing mean height (parent frame, before the shift).</summary>
        private static bool OuterWingHeight(GameObject inst, Transform parent, float halfSpan, out float h)
        {
            h = 0f; double sum = 0; int n = 0;
            float cx = LocalBounds(inst, parent).center.x;
            foreach (MeshFilter mf in inst.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh m = mf.sharedMesh;
                if (m == null || !m.isReadable) continue;
                Matrix4x4 to = parent.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                foreach (Vector3 v in m.vertices)
                {
                    Vector3 p = to.MultiplyPoint3x4(v);
                    float ax = Mathf.Abs(p.x - cx);
                    if (ax < 0.65f * halfSpan || ax > 0.92f * halfSpan) continue;
                    sum += p.y; n++;
                }
            }
            if (n < 8) return false;
            h = (float)(sum / n);
            return true;
        }

        /// <summary>The config's outer-wing mean height relative to the CG (Unity up = −sim z).</summary>
        private static bool ConfigOuterWingHeight(AircraftConfig cfg, out float h)
        {
            h = 0f; float half = HalfSpan(cfg); double sum = 0; int n = 0;
            double cgZ = cfg.Mass.CgVec().Z;
            foreach (SurfaceConfig sf in cfg.Surfaces)
            {
                if (!sf.Id.ToLowerInvariant().Contains("wing")) continue;
                foreach (StripConfig st in sf.Strips)
                {
                    double ay = System.Math.Abs(st.Pos[1]);
                    if (ay < 0.65 * half || ay > 0.92 * half) continue;
                    sum += -(st.Pos[2] - cgZ); n++;
                }
            }
            if (n == 0) return false;
            h = (float)(sum / n);
            return true;
        }

        /// <summary>The config's outer-wing chord midpoint (sim x → Unity z): strips between 65 % and 92 % of the half-span.</summary>
        private static bool ConfigOuterWingMid(AircraftConfig cfg, out float mid)
        {
            mid = 0f; float half = HalfSpan(cfg); double lo = double.MaxValue, hi = double.MinValue;
            foreach (SurfaceConfig sf in cfg.Surfaces)
            {
                if (!sf.Id.ToLowerInvariant().Contains("wing")) continue;
                foreach (StripConfig st in sf.Strips)
                {
                    double ay = System.Math.Abs(st.Pos[1]);
                    if (ay < 0.65 * half || ay > 0.92 * half) continue;
                    double le = st.Pos[0] + 0.25 * st.Chord, te = st.Pos[0] - 0.75 * st.Chord;   // Pos = quarter chord
                    lo = System.Math.Min(lo, te); hi = System.Math.Max(hi, le);
                }
            }
            if (lo > hi) return false;
            mid = (float)(0.5 * (lo + hi));
            return true;
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

        /// <summary>Bounds of every mesh in <paramref name="frame"/>, from each mesh's own AABB corners — never from the
        /// vertex arrays, which are unreadable in a player build (that made the models 700× too big on the phone).</summary>
        public static Bounds LocalBounds(GameObject go, Transform frame)
        {
            // The real vertices when the mesh can be read (owner 2026-10-10: the Extra "rolls about a point below the
            // aircraft"): the eight corners of each mesh's own box, carried through a TILTED node (glTF exports keep parts
            // under rotated nodes), made a box up to 0.7 m too deep — the model was lifted off its wheels by that much and the
            // CG ended up under the belly. The corners stay as the fallback for an unreadable mesh.
            bool any = false; var b = new Bounds();
            void Add(Vector3 p) { if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p); }
            foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh m = mf.sharedMesh;
                if (m == null) continue;
                Matrix4x4 to = frame.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                if (m.isReadable)
                {
                    foreach (Vector3 v in m.vertices) Add(to.MultiplyPoint3x4(v));
                    continue;
                }
                Bounds mb = m.bounds;
                for (int i = 0; i < 8; i++)
                    Add(to.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? mb.min.x : mb.max.x, (i & 2) == 0 ? mb.min.y : mb.max.y, (i & 4) == 0 ? mb.min.z : mb.max.z)));
            }
            return b;
        }
    }
}
