using System.Collections.Generic;
using FlyingGame.Core;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Makes the painted parts of a cockpit move (<see cref="FlyingGame.Core.CockpitLive"/> says which, per model):
    ///
    ///  - LIVE DIALS. Each painted rear-panel face gets dial art with a real scale and no needle on it, and needles
    ///    of our own just proud of the face, turned from the same <see cref="RigInputs"/> the model's rig is fed.
    ///    A compass or an attitude indicator turns the face itself.
    ///  - STICKS AND PEDALS cut out of a static cockpit mesh (the Pitts draws them into its one <c>Cockpit</c>
    ///    mesh): the triangles inside each volume leave the mesh for a part of their own, pivoted at the floor.
    ///  - A THROTTLE LEVER where the model has none (the Pitts).
    ///
    /// Why this exists: on an iPhone the flat dials (<see cref="AnalogGauges"/>) carried the readings, so a panel of
    /// pictures went unnoticed; in a headset every flat overlay stands down and the panel is all the pilot has
    /// (Ty, first Quest flight 2026-11-02: "none of the cockpit gauges move").
    ///
    /// Everything is placed in the PARENT's local frame from local transforms, never through world space: the
    /// aeroplane is often kilometres from the origin, where a world position carries a millimetre of float error —
    /// as big as the gaps that keep a needle in front of its dial.
    /// </summary>
    public sealed class CockpitLiveParts
    {
        private sealed class Dial
        {
            public LiveDial Spec;
            public Transform Face, NeedleRoot;
            public Material FaceMat;
            public Vector3 RestPos, CentreP, NormalP;
            public Quaternion RestRot;
            public readonly List<(DialNeedle N, Transform T)> Needles = new();
        }

        private enum Kind { Stick, Pedal, Lever }

        private sealed class Part
        {
            public Kind Kind; public bool Right; public Transform Pivot; public Quaternion Rest;
        }

        private readonly List<Dial> _dials = new();
        private readonly List<Part> _parts = new();
        private readonly List<Object> _owned = new();
        private bool _looked;

        /// <summary>True once this aeroplane has been looked at (whether or not it had anything to make live).</summary>
        public bool Bound => _looked;

        /// <summary>True once anything on this aeroplane is being driven.</summary>
        public bool Active => _dials.Count > 0 || _parts.Count > 0;

        /// <summary>Gap between a face and the first needle, and between stacked needles, metres. Far more than
        /// the depth buffer resolves at arm's length with the cockpit's 5 cm near plane.</summary>
        private const float NeedleProudM = 0.002f, NeedleStepM = 0.0005f;
        /// <summary>The painted face sits 1 mm in front of its bezel's cap; this takes it to 1.5 mm.</summary>
        private const float FaceProudM = 0.0005f;

        /// <summary>Once per aeroplane: find what <paramref name="ownModelName"/>'s table names and make it live.</summary>
        public void Bind(AirframeVisual visual, string ownModelName)
        {
            if (_looked || visual == null || visual.ModelRoot == null) return;
            _looked = true;
            CockpitLiveSpec spec = FlyingGame.Core.CockpitLive.For(ownModelName);
            if (spec.IsEmpty) return;
            Transform air = visual.transform;
            foreach (string n in spec.Hide)
            {
                Transform t = visual.FindInModel(n);
                if (t == null) continue;
                foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            }
            int dials = 0;
            foreach (LiveDial d in spec.Dials) if (BindDial(visual, air, d)) dials++;
            string cuts = CutParts(visual, air, spec);
            AddThrottles(visual, air, spec);
            Debug.Log($"[Cockpit] {ownModelName}: {dials} of {spec.Dials.Count} dials live, {cuts}, " +
                      $"{spec.Throttles.Count} throttle lever(s) added, {spec.Hide.Count} orphaned rig needles hidden");
        }

        /// <summary>The hierarchy was rebuilt: everything made here went with it, except the materials.</summary>
        public void Forget()
        {
            _looked = false;
            _dials.Clear(); _parts.Clear();
            foreach (Object o in _owned) if (o != null) Object.Destroy(o);
            _owned.Clear();
        }

        /// <summary>Move every needle, card, stick, pedal and lever to this frame's state.</summary>
        public void Update(in RigInputs r)
        {
            foreach (Dial d in _dials)
            {
                if (d.Face == null) continue;
                if (d.Spec.CardParam.Length > 0 && r.TryGet(d.Spec.CardParam, out double cv))
                {
                    Quaternion q = Quaternion.AngleAxis((float)d.Spec.CardDeg(cv), d.NormalP);
                    d.Face.localRotation = q * d.RestRot;
                    d.Face.localPosition = q * (d.RestPos - d.CentreP) + d.CentreP;
                }
                foreach (var (n, t) in d.Needles)
                {
                    if (t == null || n.Param.Length == 0 || !r.TryGet(n.Param, out double v)) continue;
                    // Clockwise as the pilot sees it, about the needle root's +z (into the panel).
                    t.localRotation = Quaternion.AngleAxis(-(float)n.AngleDeg(v), Vector3.forward);
                }
            }
            float stickFwd = (float)FlyingGame.Core.CockpitLive.StickPitchFwdDeg(r);
            float stickRight = (float)FlyingGame.Core.CockpitLive.StickRightDeg(r);
            float throttle = (float)FlyingGame.Core.CockpitLive.ThrottleFwdDeg(r);
            foreach (Part p in _parts)
            {
                if (p.Pivot == null) continue;
                // The pivot's axes are the aeroplane's (x right, y up, z forward): +about x tips the top forward,
                // −about z leans it right.
                Quaternion q = p.Kind switch
                {
                    Kind.Stick => Quaternion.AngleAxis(-stickRight, Vector3.forward) * Quaternion.AngleAxis(stickFwd, Vector3.right),
                    Kind.Pedal => Quaternion.AngleAxis((float)FlyingGame.Core.CockpitLive.PedalFwdDeg(r, p.Right), Vector3.right),
                    _ => Quaternion.AngleAxis(throttle, Vector3.right),
                };
                p.Pivot.localRotation = p.Rest * q;
            }
        }

        // ---- dials ----

        private bool BindDial(AirframeVisual visual, Transform air, LiveDial spec)
        {
            Transform face = visual.FindInModel(spec.Face);
            if (face == null || face.parent == null || !face.TryGetComponent(out MeshFilter mf) || mf.sharedMesh == null
                || !face.TryGetComponent(out Renderer rend)) return false;
            var art = Resources.Load<Texture2D>("CockpitDials/" + spec.Art);
            if (art == null) { Debug.LogWarning($"[Cockpit] dial art CockpitDials/{spec.Art} missing: {spec.Face} stays painted"); return false; }

            Transform p = face.parent;
            float metre = 1f / Mathf.Max(1e-4f, p.lossyScale.x);           // metres -> parent units
            Vector3 aftP = p.InverseTransformDirection(-air.forward).normalized;
            Vector3 upP = p.InverseTransformDirection(air.up).normalized;

            // The disc, in the parent's frame: centre, radius and the normal that faces the seats (aft).
            Matrix4x4 toP = Matrix4x4.TRS(face.localPosition, face.localRotation, face.localScale);
            Mesh mesh = mf.sharedMesh;
            Bounds b = mesh.bounds;
            Vector3 n = Vector3.zero;
            if (mesh.isReadable && mesh.vertexCount >= 3)
            {
                Vector3[] v = mesh.vertices; int[] tri = mesh.GetTriangles(0);
                if (tri.Length >= 3) n = Vector3.Cross(v[tri[1]] - v[tri[0]], v[tri[2]] - v[tri[0]]);
            }
            Vector3 normalP = n.sqrMagnitude > 1e-12f ? toP.MultiplyVector(n).normalized : aftP;
            if (Vector3.Dot(normalP, aftP) < 0f) normalP = -normalP;
            float radiusP = Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z)) * toP.MultiplyVector(Vector3.right).magnitude;

            face.localPosition += normalP * (FaceProudM * metre);
            toP = Matrix4x4.TRS(face.localPosition, face.localRotation, face.localScale);
            Vector3 centreP = toP.MultiplyPoint3x4(b.center);

            var dial = new Dial { Spec = spec, Face = face, RestPos = face.localPosition, RestRot = face.localRotation, CentreP = centreP, NormalP = normalP };
            dial.FaceMat = rend.material;                     // an instance: this aeroplane's face only
            _owned.Add(dial.FaceMat);
            SequenceCard.SetMainTexture(dial.FaceMat, art);

            // 12 o'clock is the aeroplane's up laid into the dial's plane.
            Vector3 twelve = Vector3.ProjectOnPlane(upP, normalP);
            if (twelve.sqrMagnitude < 1e-8f) twelve = Vector3.ProjectOnPlane(p.InverseTransformDirection(air.forward), normalP);
            var root = new GameObject("Live:" + spec.Face).transform;
            root.SetParent(p, false);
            root.localPosition = centreP + normalP * (NeedleProudM * metre);
            root.localRotation = Quaternion.LookRotation(-normalP, twelve.normalized);
            dial.NeedleRoot = root;
            for (int i = 0; i < spec.Needles.Count; i++)
            {
                DialNeedle dn = spec.Needles[i];
                var go = new GameObject(dn.Param.Length > 0 ? dn.Param : dn.Shape.ToString());
                go.transform.SetParent(root, false);
                go.transform.localPosition = new Vector3(0f, 0f, -i * NeedleStepM * metre);   // later needles nearer the eye
                go.transform.localRotation = Quaternion.AngleAxis(-(float)dn.AngleDeg(double.NaN), Vector3.forward);
                Mesh m = NeedleMesh(dn.Shape, radiusP);
                _owned.Add(m);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = Flat(new Color(dn.R, dn.G, dn.B));
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                dial.Needles.Add((dn, go.transform));
            }
            _dials.Add(dial);
            return true;
        }

        /// <summary>A needle in the XY plane pointing +y from the hub, sized to a dial of radius <paramref name="r"/>,
        /// wound clockwise as seen from −z (the pilot's side). FlyingGame/Lit draws both sides anyway.</summary>
        private static Mesh NeedleMesh(NeedleShape shape, float r)
        {
            Vector2[] poly = shape switch
            {
                NeedleShape.Hand => new[] { new Vector2(-0.05f, -0.12f), new Vector2(-0.05f, 0.40f), new Vector2(0f, 0.55f), new Vector2(0.05f, 0.40f), new Vector2(0.05f, -0.12f) },
                NeedleShape.TellTale => new[] { new Vector2(-0.015f, 0.55f), new Vector2(-0.015f, 0.86f), new Vector2(0.015f, 0.86f), new Vector2(0.015f, 0.55f) },
                NeedleShape.Lubber => new[] { new Vector2(-0.025f, 0.70f), new Vector2(0f, 0.98f), new Vector2(0.025f, 0.70f) },
                NeedleShape.Bar => new[] { new Vector2(-0.03f, -0.55f), new Vector2(-0.03f, 0.55f), new Vector2(0.03f, 0.55f), new Vector2(0.03f, -0.55f) },
                _ => new[] { new Vector2(-0.035f, -0.20f), new Vector2(-0.035f, 0.05f), new Vector2(-0.012f, 0.72f), new Vector2(0f, 0.84f), new Vector2(0.012f, 0.72f), new Vector2(0.035f, 0.05f), new Vector2(0.035f, -0.20f) },
            };
            var v = new Vector3[poly.Length];
            for (int i = 0; i < poly.Length; i++) v[i] = new Vector3(poly[i].x * r, poly[i].y * r, 0f);
            var t = new int[(poly.Length - 2) * 3];
            for (int i = 0; i < poly.Length - 2; i++) { t[i * 3] = 0; t[i * 3 + 1] = i + 1; t[i * 3 + 2] = i + 2; }
            var m = new Mesh { name = "Needle-" + shape, vertices = v, triangles = t };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // ---- sticks and pedals out of a static mesh ----

        private sealed class Cut
        {
            public string Name; public Kind Kind; public bool Right;
            public System.Func<double, double, double, bool> Contains;
            public Vector3 PivotLocal;                     // in the cockpit mesh's frame
            public int Expected;
            public readonly List<int>[] Tris;
            public Cut(int subMeshes) { Tris = new List<int>[subMeshes]; for (int i = 0; i < subMeshes; i++) Tris[i] = new List<int>(); }
            public int Count { get { int c = 0; foreach (var l in Tris) c += l.Count / 3; return c; } }
        }

        /// <summary>glTF (right-handed, the table's frame) to the mesh as glTFast imported it: x mirrored.</summary>
        private static Vector3 FromGltf(double x, double y, double z) => new((float)-x, (float)y, (float)z);

        private string CutParts(AirframeVisual visual, Transform air, CockpitLiveSpec spec)
        {
            if (spec.Sticks.Count == 0 && spec.Pedals.Count == 0) return "no parts to cut";
            Transform cockpit = visual.FindInModel("Cockpit");
            if (cockpit == null || !cockpit.TryGetComponent(out MeshFilter mf) || mf.sharedMesh == null
                || !cockpit.TryGetComponent(out MeshRenderer mr)) return "NO Cockpit mesh to cut sticks and pedals from";
            Mesh src = mf.sharedMesh;
            if (!src.isReadable) return "Cockpit mesh not readable: sticks and pedals stay put";

            int subs = src.subMeshCount;
            var cuts = new List<Cut>();
            foreach (StickCut s in spec.Sticks)
                cuts.Add(new Cut(subs) { Name = s.Name, Kind = Kind.Stick, Contains = s.Contains, PivotLocal = FromGltf(s.Px, s.Py, s.Pz), Expected = spec.StickTris });
            foreach (PedalCut s in spec.Pedals)
                cuts.Add(new Cut(subs) { Name = s.Name, Kind = Kind.Pedal, Right = s.Right, Contains = s.Contains, PivotLocal = FromGltf(s.Px, s.Py, s.Pz), Expected = spec.PedalTris });

            Vector3[] v = src.vertices;
            Vector3[] nrm = src.normals;
            bool hasN = nrm != null && nrm.Length == v.Length;
            var keep = new List<int>[subs];
            for (int sm = 0; sm < subs; sm++)
            {
                int[] t = src.GetTriangles(sm);
                keep[sm] = new List<int>(t.Length);
                for (int k = 0; k + 2 < t.Length; k += 3)
                {
                    Cut owner = null;
                    foreach (Cut c in cuts)
                    {
                        if (In(c, v[t[k]]) && In(c, v[t[k + 1]]) && In(c, v[t[k + 2]])) { owner = c; break; }
                    }
                    var into = owner != null ? owner.Tris[sm] : keep[sm];
                    into.Add(t[k]); into.Add(t[k + 1]); into.Add(t[k + 2]);
                }
            }

            // A cut that took nothing, or not what the table expects, means the model changed under the table:
            // say so, and leave that part in the static mesh rather than carving something else out of it.
            var report = new List<string>();
            var made = new List<Cut>();
            foreach (Cut c in cuts)
            {
                int n = c.Count;
                bool ok = n > 0 && (c.Expected == 0 || n == c.Expected);
                report.Add($"{c.Name} {n}{(ok ? "" : $" (expected {c.Expected}: LEFT STATIC)")}");
                if (ok) { made.Add(c); continue; }
                for (int sm = 0; sm < subs; sm++) keep[sm].AddRange(c.Tris[sm]);
            }
            if (made.Count == 0) return "cut " + string.Join(", ", report);

            Mesh rest = Object.Instantiate(src);
            rest.name = src.name + "-live";
            for (int sm = 0; sm < subs; sm++) rest.SetTriangles(keep[sm], sm);
            _owned.Add(rest);
            mf.sharedMesh = rest;

            // Each part on a pivot whose axes are the aeroplane's (x right, y up, z forward).
            Quaternion axes = Quaternion.Inverse(cockpit.rotation) * air.rotation;
            foreach (Cut c in made)
            {
                var pivot = new GameObject("Live:" + c.Name).transform;
                pivot.SetParent(cockpit, false);
                pivot.localPosition = c.PivotLocal;
                pivot.localRotation = axes;
                Quaternion inv = Quaternion.Inverse(axes);
                var map = new Dictionary<int, int>();
                var pv = new List<Vector3>(); var pn = new List<Vector3>();
                var part = new Mesh { name = c.Name };
                part.subMeshCount = subs;
                var subTris = new List<int>[subs];
                for (int sm = 0; sm < subs; sm++)
                {
                    subTris[sm] = new List<int>(c.Tris[sm].Count);
                    foreach (int i in c.Tris[sm])
                    {
                        if (!map.TryGetValue(i, out int j))
                        {
                            j = pv.Count; map[i] = j;
                            pv.Add(inv * (v[i] - c.PivotLocal));
                            pn.Add(hasN ? inv * nrm[i] : Vector3.up);
                        }
                        subTris[sm].Add(j);
                    }
                }
                part.SetVertices(pv);
                part.SetNormals(pn);
                for (int sm = 0; sm < subs; sm++) part.SetTriangles(subTris[sm], sm);
                part.RecalculateBounds();
                _owned.Add(part);
                pivot.gameObject.AddComponent<MeshFilter>().sharedMesh = part;
                var r = pivot.gameObject.AddComponent<MeshRenderer>();
                r.sharedMaterials = mr.sharedMaterials;
                r.shadowCastingMode = mr.shadowCastingMode;
                r.receiveShadows = mr.receiveShadows;
                _parts.Add(new Part { Kind = c.Kind, Right = c.Right, Pivot = pivot, Rest = pivot.localRotation });
            }
            return "cut " + string.Join(", ", report);
        }

        private static bool In(Cut c, Vector3 meshLocal) => c.Contains(-meshLocal.x, meshLocal.y, meshLocal.z);

        // ---- a throttle lever the model does not have ----

        private void AddThrottles(AirframeVisual visual, Transform air, CockpitLiveSpec spec)
        {
            if (spec.Throttles.Count == 0) return;
            Transform cockpit = visual.FindInModel("Cockpit");
            if (cockpit == null) return;
            float metre = 1f / Mathf.Max(1e-4f, cockpit.lossyScale.x);
            Quaternion axes = Quaternion.Inverse(cockpit.rotation) * air.rotation;
            foreach (LeverAdd l in spec.Throttles)
            {
                Vector3 at = FromGltf(l.Px, l.Py, l.Pz);
                // The quadrant: a dark slotted plate on the wall that the lever swings along. It does not move.
                var plate = new GameObject("Live:" + l.Name + "_Quadrant").transform;
                plate.SetParent(cockpit, false);
                plate.localPosition = at; plate.localRotation = axes;
                AddBox(plate, new Vector3(0f, 0.01f, 0f), new Vector3(0.012f, 0.05f, 0.17f) * metre, new Color(0.10f, 0.10f, 0.11f));

                var pivot = new GameObject("Live:" + l.Name).transform;
                pivot.SetParent(cockpit, false);
                pivot.localPosition = at + axes * Vector3.right * (0.012f * metre);   // inboard of the plate
                pivot.localRotation = axes;
                float len = (float)l.LengthM * metre, knob = (float)l.KnobM * metre;
                AddBox(pivot, new Vector3(0f, len * 0.5f, 0f), new Vector3(0.010f * metre, len, 0.010f * metre), new Color(0.55f, 0.56f, 0.58f));
                AddBox(pivot, new Vector3(0f, len, 0f), new Vector3(knob, knob * 0.9f, knob), new Color(0.06f, 0.06f, 0.07f));
                _parts.Add(new Part { Kind = Kind.Lever, Pivot = pivot, Rest = pivot.localRotation });
            }
        }

        private void AddBox(Transform parent, Vector3 centre, Vector3 size, Color c)
        {
            var go = new GameObject("Box");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = centre;
            Mesh m = BoxMesh(size);
            _owned.Add(m);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Flat(c);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static Mesh BoxMesh(Vector3 s)
        {
            Vector3 h = s * 0.5f;
            var v = new List<Vector3>(); var t = new List<int>();
            void Face(Vector3 n, Vector3 u, Vector3 w)
            {
                int b = v.Count;
                Vector3 c = Vector3.Scale(n, h), du = Vector3.Scale(u, h), dw = Vector3.Scale(w, h);
                v.Add(c - du - dw); v.Add(c - du + dw); v.Add(c + du + dw); v.Add(c + du - dw);
                t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3);
            }
            Face(Vector3.right, Vector3.up, Vector3.forward); Face(Vector3.left, Vector3.forward, Vector3.up);
            Face(Vector3.up, Vector3.forward, Vector3.right); Face(Vector3.down, Vector3.right, Vector3.forward);
            Face(Vector3.forward, Vector3.right, Vector3.up); Face(Vector3.back, Vector3.up, Vector3.right);
            var m = new Mesh { name = "LiveBox" };
            m.SetVertices(v); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        private static readonly Dictionary<Color, Material> Mats = new();

        /// <summary>Flat colour, two-sided: FlyingGame/Lit is force-included in every build (BuildScript).</summary>
        private static Material Flat(Color c)
        {
            if (!Mats.TryGetValue(c, out Material m) || m == null)
            {
                Shader sh = Shader.Find("FlyingGame/Lit") ?? Shader.Find("Unlit/Color");
                m = new Material(sh) { color = c, name = "CockpitLive" };
                Mats[c] = m;
            }
            return m;
        }
    }
}
