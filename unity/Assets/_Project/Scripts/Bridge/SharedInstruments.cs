using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.Instruments;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The shared instrument library in the cockpit (Ty 2026-11-05: one turn coordinator, used six times). For every mount
    /// <see cref="InstrumentLibrary.MountsFor"/> lists on the placed model, the library's GLB
    /// (Resources/Models/instruments/&lt;design&gt;, built by tools/own_models/instruments) is put on the panel at the old
    /// instrument's centre, sized by its face radius, and driven from the same <see cref="RigInputs"/> as the model's own
    /// rig; the old model's parts of that instrument stand down (renderers off: the rig may still turn them, nobody sees).
    /// GLBs built before the library therefore get the library instrument too, the way CockpitBeltStrip takes the belts out
    /// of old models.
    ///
    /// Placement is in the MODEL ROOT's frame from the mount's glTF numbers (the importer mirrors x), never through world
    /// positions (CLAUDE.md: a millimetre in world space is not a millimetre). The instance hangs under the model root,
    /// which the airframe rebuild destroys with it; <see cref="Forget"/> drops the references and the next Bind builds anew.
    /// One path for iOS/Mac and the Quest: CockpitLiveParts binds and drives this, and both builds and the editor shot
    /// tools go through it.
    /// </summary>
    public sealed class SharedInstruments
    {
        private sealed class Driven
        {
            public InstrumentPart Part; public Transform T; public Quaternion RestRot; public Vector3 RestPos;
        }

        private readonly List<Driven> _parts = new();
        private readonly List<Object> _owned = new();
        private static readonly Dictionary<string, GameObject> Prefabs = new();

        public int Placed { get; private set; }
        public bool Active => _parts.Count > 0;

        /// <summary>Once per placed model.</summary>
        public void Bind(Transform air, System.Func<string, Transform> find, string ownModelName)
        {
            IReadOnlyList<InstrumentMount> mounts = InstrumentLibrary.MountsFor(ownModelName);
            if (mounts.Count == 0 || find == null) return;
            Transform root = ModelRoot(find, mounts);
            if (root == null)
            {
                Debug.LogWarning($"[Instruments] {ownModelName}: none of the mounts' nodes found: no library instruments placed");
                return;
            }
            var sb = new System.Text.StringBuilder();
            foreach (InstrumentMount m in mounts)
            {
                string why = Place(root, find, m);
                if (why == null) Placed++;
                else sb.Append($" {m.Slot}: {why};");
            }
            Debug.Log($"[Instruments] {ownModelName}: {Placed} of {mounts.Count} library instruments placed, {_parts.Count} moving parts" +
                      (sb.Length > 0 ? " — not placed:" + sb : ""));
        }

        public void Forget()
        {
            _parts.Clear();
            foreach (Object o in _owned) if (o != null) { if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o); }
            _owned.Clear();
            Placed = 0;
        }

        public void Update(in RigInputs r)
        {
            foreach (Driven d in _parts)
            {
                if (d.T == null || !r.TryGet(d.Part.Param, out double v)) continue;
                float x = (float)d.Part.Value(v);
                // The instance's +z faces the pilot, so a positive turn about +z is CLOCKWISE as he sees it.
                if (d.Part.Slide) d.T.localPosition = d.RestPos + Vector3.up * x;
                else d.T.localRotation = d.RestRot * Quaternion.AngleAxis(x, Vector3.forward);
            }
        }

        /// <summary>The placed model's root ("Model", AirframeModels.Place), from any node a mount names.</summary>
        private static Transform ModelRoot(System.Func<string, Transform> find, IReadOnlyList<InstrumentMount> mounts)
        {
            foreach (InstrumentMount m in mounts)
                foreach (string n in m.Hide)
                {
                    Transform t = find(n);
                    for (; t != null; t = t.parent) if (t.name == "Model") return t;
                }
            return null;
        }

        private string Place(Transform root, System.Func<string, Transform> find, InstrumentMount m)
        {
            InstrumentDesign d = InstrumentLibrary.Design(m.Kind);
            if (!Prefabs.TryGetValue(d.Resource, out GameObject prefab) || prefab == null)
                Prefabs[d.Resource] = prefab = Resources.Load<GameObject>("Models/instruments/" + d.Resource);
            if (prefab == null) return $"Resources/Models/instruments/{d.Resource} missing";

            // glTF (x aft-or-forward, y up, z left) -> the model root's frame: the importer mirrors x.
            var n = new Vector3(-(float)m.Nx, (float)m.Ny, (float)m.Nz);
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, n);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.forward;
            var centre = new Vector3(-(float)m.Cx, (float)m.Cy, (float)m.Cz);

            GameObject go = Object.Instantiate(prefab, root, false);
            go.name = "Live:Library_" + m.Slot;
            Transform t = go.transform;
            t.localPosition = centre + n * (float)m.FaceProudM;
            t.localRotation = Quaternion.LookRotation(n, up.normalized);
            t.localScale = Vector3.one * (float)m.RadiusM;
            _owned.Add(go);

            var byName = new Dictionary<string, Transform>();
            foreach (Transform c in go.GetComponentsInChildren<Transform>(true)) byName[c.name] = c;

            // The old model's parts of this instrument stand down. A model built with the library has none: then the
            // panel has no bezel of its own there either, and the library's goes on whatever the mount says.
            int hidden = 0;
            foreach (string h in m.Hide)
            {
                Transform ht = find(h);
                if (ht == null) continue;
                hidden++;
                foreach (Renderer r in ht.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            }
            if (hidden == 0) Debug.Log($"[Instruments] {m.Slot}: none of its {m.Hide.Count} old nodes in this model (built with the library?): bezel on");
            if (!m.Bezel && hidden > 0 && byName.TryGetValue(d.BezelNode, out Transform bz))
                foreach (Renderer r in bz.GetComponentsInChildren<Renderer>(true)) r.enabled = false;

            // Lit like every instrument in the cockpit, THEN this aeroplane's own face art on a copy of the lit face: the
            // lighting caches one lit copy per source material, which a per-aeroplane source would grow on every respawn.
            InstrumentLighting.Apply(go);
            if (byName.TryGetValue(d.FaceNode, out Transform face))
            {
                // The altimeter's and the g-meter's faces carry the seat (CockpitTaps classifies by name: Kollsman popup, reset).
                if (m.Kind == InstrumentKind.Altimeter) face.name = "Library_Altimeter_Face_" + (m.Front ? "F" : "R");
                if (m.Kind == InstrumentKind.GMeter) face.name = "Library_G_meter_Face_" + (m.Front ? "F" : "R");
                if (m.FaceArt.Length > 0 && face.TryGetComponent(out Renderer fr) && fr.sharedMaterial != null)
                {
                    var art = Resources.Load<Texture2D>(m.FaceArt);
                    if (art == null) Debug.LogWarning($"[Instruments] {m.Slot}: face art {m.FaceArt} missing: the generic face stays");
                    else
                    {
                        var inst = new Material(fr.sharedMaterial) { name = fr.sharedMaterial.name };   // this aeroplane's face only
                        SequenceCard.SetMainTexture(inst, art);
                        fr.sharedMaterial = inst;
                        _owned.Add(inst);
                    }
                }
            }

            foreach (InstrumentPart p in d.Parts)
            {
                if (!byName.TryGetValue(p.Node, out Transform pt)) { Debug.LogWarning($"[Instruments] {m.Slot}: part {p.Node} not in {d.Resource}"); continue; }
                _parts.Add(new Driven { Part = m.PartFor(p), T = pt, RestRot = pt.localRotation, RestPos = pt.localPosition });
            }

            // Knobs that stay: just in front of the library face, moved in their parent's frame.
            foreach (string l in m.Lift)
            {
                Transform lt = find(l);
                if (lt == null || lt.parent == null) continue;
                Vector3 shift = root.TransformVector(n * (float)(m.FaceProudM + 0.0025));
                lt.localPosition += lt.parent.InverseTransformVector(shift);
            }
            // The check that the table still matches the model: the first hidden node is the old needle on its pivot.
            Transform pivot = m.Hide.Count > 0 ? find(m.Hide[0]) : null;
            if (pivot != null && pivot.name.IndexOf("Face", System.StringComparison.Ordinal) < 0 && pivot.name.IndexOf("Bezel", System.StringComparison.Ordinal) < 0)
            {
                // Across the face only (the 172's AI and DG pivots sit behind its common dial plane). Root units are glTF metres.
                float mm = Vector3.ProjectOnPlane(root.InverseTransformPoint(pivot.position) - centre, n).magnitude * 1000f;
                if (mm > 2f) Debug.LogWarning($"[Instruments] {m.Slot}: the table's centre is {mm:F1} mm from {m.Hide[0]}'s pivot: the model moved; re-measure (measure_mounts.py)");
            }
            return null;
        }
    }
}
