using System.Collections.Generic;
using FlyingGame.Core.Vr;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Takes the seat belts and headset cords out of a placed model (Ty 2026-11-05; the rules are Core's
    /// <see cref="BeltsAndCords"/>). Runs once per placed model, before anything cuts or copies its meshes.
    ///
    /// A node whose NAME is a belt has its renderers switched off (the node stays: nothing that looks a node up breaks).
    /// A mesh that draws a belt material among others (the Great Lakes' Cockpit_Interior, the Pitts' Cockpit) gets a copy
    /// with that sub-mesh's triangles emptied; a mesh that is ONLY belt is switched off. A mesh that cannot be read is
    /// reported, not guessed at. The copies are handed back in <c>made</c> so the owner can destroy them with the model.
    /// </summary>
    public static class CockpitBeltStrip
    {
        public static int Strip(Transform root, List<Mesh> made = null)
        {
            if (root == null || BeltsAndCords.BeltsWorn) return 0;
            int nodes = 0, subs = 0, unreadable = 0;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!BeltsAndCords.IsBeltOrCordNode(t.name)) continue;
                foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true)) { if (r.enabled) { r.enabled = false; nodes++; } }
            }
            var copied = new Dictionary<Mesh, Mesh>();
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled) continue;
                Material[] mats = r.sharedMaterials;
                int belt = 0;
                for (int i = 0; i < mats.Length; i++) if (mats[i] != null && BeltsAndCords.IsBeltMaterial(mats[i].name)) belt++;
                if (belt == 0) continue;
                if (belt == mats.Length) { r.enabled = false; subs += belt; continue; }
                Mesh src = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.TryGetComponent(out MeshFilter mf) ? mf.sharedMesh : null;
                if (src == null) continue;
                if (!src.isReadable) { unreadable++; continue; }
                if (!copied.TryGetValue(src, out Mesh m))
                {
                    m = Object.Instantiate(src);
                    m.name = src.name + " (no belts)";
                    for (int i = 0; i < mats.Length && i < m.subMeshCount; i++)
                        if (mats[i] != null && BeltsAndCords.IsBeltMaterial(mats[i].name)) { m.SetTriangles(System.Array.Empty<int>(), i, false); subs++; }
                    copied[src] = m;
                    made?.Add(m);
                }
                if (r is SkinnedMeshRenderer s2) s2.sharedMesh = m;
                else r.GetComponent<MeshFilter>().sharedMesh = m;
            }
            Debug.Log($"[Belts] {root.name}: no belts or headset cords — {nodes} belt renderer(s) off, {subs} belt sub-mesh(es) emptied" +
                      (unreadable > 0 ? $", {unreadable} mesh(es) UNREADABLE (belt left drawn)" : ""));
            return nodes + subs;
        }
    }
}
