using System.Collections.Generic;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// THE PILOT'S VISIBLE BODY in the headset: arms, gloved hands, thighs, shins, boots. No head — the camera is the
    /// head. Posed every frame from joint positions the IK has already solved (VrCockpitHands), in WORLD space.
    ///
    /// Two ways to draw it, chosen once:
    ///  - Codex's skinned <c>Resources/Models/pilot-body/pilot-body-own.glb</c> (CONTRACT.md), bones by the contract's
    ///    names (L/R_UpperArm, L/R_LowerArm, L/R_Hand, L/R_UpperLeg, L/R_LowerLeg, L/R_Foot, finger bones L/R_Index1..3
    ///    …). Each limb bone is AIMED at the next joint (its rest direction to its child, turned onto the solved one),
    ///    so the asset's own bone axes do not matter. Finger curl turns each finger bone about its local +X (E: the
    ///    usual Blender export; HANDOFF asks Codex to confirm).
    ///  - Until then, a STAND-IN from primitives: a capsule per long bone, a palm box with four finger capsules and a
    ///    thumb per hand, a box per boot. 22 renderers, one shared material, no colliders, no shadows.
    ///
    /// Swapping in the asset is only loading it: the IK and everything that drives it are the same.
    /// </summary>
    public sealed class VrPilotBody
    {
        /// <summary>One frame of solved joints, world space.</summary>
        public struct Pose
        {
            public Vector3 ShoulderL, ElbowL, WristL, GripL, ShoulderR, ElbowR, WristR, GripR;
            public Quaternion HandRotL, HandRotR;
            /// <summary>0 open .. 1 closed round a control.</summary>
            public float CurlL, CurlR;
            public Vector3 HipL, KneeL, AnkleL, BallL, HipR, KneeR, AnkleR, BallR;
            /// <summary>The aeroplane's up, for the boots' soles and the palms' roll.</summary>
            public Vector3 Up;
        }

        public const string AssetPath = "Models/pilot-body/pilot-body-own";

        private GameObject _root;
        private bool _visible;
        private bool _skinned;

        // ---- stand-in ----
        private Transform _upperL, _foreL, _upperR, _foreR, _thighL, _shinL, _thighR, _shinR, _bootL, _bootR;
        private Hand _handL, _handR;
        private Material _suit, _glove, _boot;

        // ---- skinned ----
        private readonly Dictionary<string, Transform> _bones = new();
        private readonly Dictionary<Transform, Quaternion> _rest = new();

        private sealed class Hand
        {
            public Transform Palm;
            public readonly Transform[] Fingers = new Transform[4];
            public Transform Thumb;
        }

        public bool Skinned => _skinned;

        public void SetVisible(bool on)
        {
            if (on && _root == null) Build();
            if (_root == null || _visible == on) return;
            _visible = on;
            _root.SetActive(on);
        }

        public void Destroy()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null;
        }

        private void Build()
        {
            var prefab = Resources.Load<GameObject>(AssetPath);
            if (prefab != null && BuildSkinned(prefab)) { _skinned = true; return; }
            BuildStandIn();
            Debug.Log($"[VrCockpit] pilot body: {AssetPath}.glb {(prefab == null ? "not in the build" : "has none of the contract's bones")} — drawing the primitive stand-in (22 parts).");
        }

        // ---- the skinned asset ----

        private static readonly string[] Bones =
        {
            "Hips", "L_UpperArm", "L_LowerArm", "L_Hand", "R_UpperArm", "R_LowerArm", "R_Hand",
            "L_UpperLeg", "L_LowerLeg", "L_Foot", "L_Toes", "R_UpperLeg", "R_LowerLeg", "R_Foot", "R_Toes",
        };
        private static readonly string[] Fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };

        private bool BuildSkinned(GameObject prefab)
        {
            _root = Object.Instantiate(prefab);
            _root.name = "VR Pilot Body (skinned)";
            foreach (Transform t in _root.GetComponentsInChildren<Transform>(true))
                if (!_bones.ContainsKey(t.name)) _bones[t.name] = t;
            int found = 0;
            var missing = new List<string>();
            foreach (string b in Bones) { if (_bones.ContainsKey(b)) found++; else missing.Add(b); }
            if (found < 8)
            {
                Object.Destroy(_root); _root = null; _bones.Clear();
                return false;
            }
            foreach (Transform t in _bones.Values) _rest[t] = t.localRotation;
            foreach (var r in _root.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                if (r is SkinnedMeshRenderer s) s.updateWhenOffscreen = true;   // it is never "offscreen": it is under the camera
            }
            foreach (var c in _root.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
            Debug.Log($"[VrCockpit] pilot body: {AssetPath}.glb bound, {found} of {Bones.Length} limb bones" +
                      (missing.Count > 0 ? $"; missing {string.Join(", ", missing)}" : ""));
            _root.SetActive(false);
            return true;
        }

        /// <summary>Turn <paramref name="bone"/> so the direction to <paramref name="child"/> lies along the solved
        /// <paramref name="from"/> → <paramref name="to"/>. The asset's own bone axes never enter into it.</summary>
        private static void Aim(Transform bone, Transform child, Vector3 from, Vector3 to)
        {
            if (bone == null || child == null) return;
            Vector3 have = child.position - bone.position, want = to - from;
            if (have.sqrMagnitude < 1e-10f || want.sqrMagnitude < 1e-10f) return;
            bone.rotation = Quaternion.FromToRotation(have, want) * bone.rotation;
        }

        private Transform B(string n) => _bones.TryGetValue(n, out Transform t) ? t : null;

        /// <summary>SKINNED ONLY, before the IK: back to the rest pose (no drift), the asset's hips on the seat's hip
        /// midpoint, facing the nose (the asset faces glTF +Z, as every model here does).</summary>
        public void PlaceRoot(Vector3 hipMid, Quaternion aeroplane)
        {
            if (!_skinned || _root == null) return;
            foreach (var kv in _rest) kv.Key.localRotation = kv.Value;
            _root.transform.rotation = aeroplane;
            Transform hips = B("Hips");
            if (hips != null) _root.transform.position += hipMid - hips.position;
        }

        /// <summary>SKINNED ONLY: a limb's root joint (world, after <see cref="PlaceRoot"/>) and its own two bone
        /// lengths, so the IK reaches with the asset's arm and not the stand-in's numbers.</summary>
        public bool TryChain(string upper, string lower, string end, out Vector3 root, out float upperLen, out float lowerLen)
        {
            Transform u = B(upper), l = B(lower), e = B(end);
            root = default; upperLen = lowerLen = 0f;
            if (!_skinned || u == null || l == null || e == null) return false;
            root = u.position;
            upperLen = Vector3.Distance(u.position, l.position);
            lowerLen = Vector3.Distance(l.position, e.position);
            return upperLen > 0.05f && lowerLen > 0.05f;
        }

        private void PoseSkinned(in Pose p)
        {
            Limb("L_UpperArm", "L_LowerArm", "L_Hand", p.ShoulderL, p.ElbowL, p.WristL);
            Limb("R_UpperArm", "R_LowerArm", "R_Hand", p.ShoulderR, p.ElbowR, p.WristR);
            Limb("L_UpperLeg", "L_LowerLeg", "L_Foot", p.HipL, p.KneeL, p.AnkleL);
            Limb("R_UpperLeg", "R_LowerLeg", "R_Foot", p.HipR, p.KneeR, p.AnkleR);
            Aim(B("L_Hand"), B("L_Middle1"), p.WristL, p.GripL);
            Aim(B("R_Hand"), B("R_Middle1"), p.WristR, p.GripR);
            Aim(B("L_Foot"), B("L_Toes"), p.AnkleL, p.BallL);
            Aim(B("R_Foot"), B("R_Toes"), p.AnkleR, p.BallR);
            Curl("L_", p.CurlL);
            Curl("R_", p.CurlR);
        }

        private void Limb(string upper, string lower, string end, Vector3 a, Vector3 b, Vector3 c)
        {
            Transform u = B(upper), l = B(lower), e = B(end);
            if (u == null || l == null) return;
            // The shoulder / hip of the asset is where the asset put it; the chain is solved from OUR joint, so move
            // the solved directions onto the asset's own root joint.
            Aim(u, l, a, b);
            Aim(l, e, b, c);
        }

        private void Curl(string side, float curl)
        {
            foreach (string f in Fingers)
                for (int i = 1; i <= 3; i++)
                {
                    Transform t = B(side + f + i);
                    if (t == null) continue;
                    float deg = curl * (f == "Thumb" ? 35f : i == 1 ? 70f : 80f);
                    t.localRotation = _rest[t] * Quaternion.AngleAxis(deg, Vector3.right);
                }
        }

        // ---- the stand-in ----

        private void BuildStandIn()
        {
            _root = new GameObject("VR Pilot Body (stand-in)");
            Shader sh = Shader.Find("FlyingGame/Lit") ?? Shader.Find("Unlit/Color");
            _suit = new Material(sh) { color = new Color(0.36f, 0.40f, 0.30f), enableInstancing = true };   // sage flight suit
            _glove = new Material(sh) { color = new Color(0.55f, 0.49f, 0.38f), enableInstancing = true };  // tan Nomex
            _boot = new Material(sh) { color = new Color(0.08f, 0.07f, 0.07f), enableInstancing = true };   // black boots
            _upperL = Part(PrimitiveType.Capsule, "UpperArm_L", _suit); _foreL = Part(PrimitiveType.Capsule, "Forearm_L", _suit);
            _upperR = Part(PrimitiveType.Capsule, "UpperArm_R", _suit); _foreR = Part(PrimitiveType.Capsule, "Forearm_R", _suit);
            _thighL = Part(PrimitiveType.Capsule, "Thigh_L", _suit); _shinL = Part(PrimitiveType.Capsule, "Shin_L", _suit);
            _thighR = Part(PrimitiveType.Capsule, "Thigh_R", _suit); _shinR = Part(PrimitiveType.Capsule, "Shin_R", _suit);
            _bootL = Part(PrimitiveType.Cube, "Boot_L", _boot); _bootR = Part(PrimitiveType.Cube, "Boot_R", _boot);
            _handL = MakeHand("L"); _handR = MakeHand("R");
            _root.SetActive(false);
        }

        private Hand MakeHand(string side)
        {
            var h = new Hand { Palm = Part(PrimitiveType.Cube, "Palm_" + side, _glove) };
            for (int i = 0; i < 4; i++) h.Fingers[i] = Part(PrimitiveType.Capsule, $"Finger{i}_{side}", _glove);
            h.Thumb = Part(PrimitiveType.Capsule, "Thumb_" + side, _glove);
            return h;
        }

        private Transform Part(PrimitiveType type, string name, Material m)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());   // nothing on the pilot may collide with the aeroplane
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.transform.SetParent(_root.transform, false);
            return go.transform;
        }

        /// <summary>A capsule from a to b, <paramref name="radius"/> thick (Unity's capsule is 2 tall and 1 across).</summary>
        private static void Bone(Transform t, Vector3 a, Vector3 b, float radius)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            t.SetPositionAndRotation(0.5f * (a + b), len > 1e-5f ? Quaternion.FromToRotation(Vector3.up, d / len) : Quaternion.identity);
            t.localScale = new Vector3(2f * radius, Mathf.Max(len, 2f * radius) * 0.5f, 2f * radius);
        }

        private static void PoseHand(Hand h, Vector3 wrist, Vector3 grip, Quaternion handRot, float curl, bool left)
        {
            // The palm runs from the wrist to the knuckles, past the centre of the fist; its back faces the
            // controller's up, so turning the wrist turns the hand.
            Vector3 fwd = grip - wrist;
            if (fwd.sqrMagnitude < 1e-8f) fwd = handRot * Vector3.forward;
            fwd.Normalize();
            Quaternion palm = Quaternion.LookRotation(fwd, handRot * Vector3.up);
            Vector3 knuckles = wrist + fwd * 0.10f;
            h.Palm.SetPositionAndRotation(wrist + fwd * 0.05f, palm);
            h.Palm.localScale = new Vector3(0.085f, 0.028f, 0.10f);
            // Four fingers from the knuckles, curled DOWN (toward the palm) by up to 100 degrees about the palm's
            // lateral axis — round a stick grip at curl 1.
            Vector3 side = palm * Vector3.right, down = palm * Vector3.down;
            for (int i = 0; i < 4; i++)
            {
                float across = (i - 1.5f) * 0.021f * (left ? -1f : 1f);
                float len = i == 3 ? 0.065f : i == 0 ? 0.075f : 0.085f;
                Vector3 baseP = knuckles + side * across;
                Vector3 dir = Quaternion.AngleAxis(curl * 100f, side) * fwd;
                Bone(h.Fingers[i], baseP, baseP + dir * len, 0.0095f);
            }
            // The thumb from the inner side of the palm, folding across it as the hand closes.
            Vector3 thumbBase = wrist + fwd * 0.04f + side * (left ? 0.04f : -0.04f) + down * 0.01f;
            Vector3 thumbDir = Vector3.Slerp((fwd + side * (left ? 0.6f : -0.6f)).normalized, (fwd * 0.4f - side * (left ? 0.6f : -0.6f) + down * 0.6f).normalized, curl);
            Bone(h.Thumb, thumbBase, thumbBase + thumbDir * 0.065f, 0.011f);
        }

        private static void PoseBoot(Transform boot, Vector3 ankle, Vector3 ball, Vector3 up)
        {
            Vector3 heelToToe = ball - ankle;
            Vector3 fwd = Vector3.ProjectOnPlane(heelToToe, up);
            if (fwd.sqrMagnitude < 1e-8f) fwd = heelToToe;
            boot.SetPositionAndRotation(ankle + heelToToe * 0.55f, Quaternion.LookRotation(fwd.normalized, up));
            boot.localScale = new Vector3(0.10f, 0.09f, 0.29f);
        }

        /// <summary>Draw this frame's pose.</summary>
        public void Apply(in Pose p)
        {
            if (_root == null || !_visible) return;
            if (_skinned) { PoseSkinned(p); return; }
            Bone(_upperL, p.ShoulderL, p.ElbowL, 0.050f); Bone(_foreL, p.ElbowL, p.WristL, 0.040f);
            Bone(_upperR, p.ShoulderR, p.ElbowR, 0.050f); Bone(_foreR, p.ElbowR, p.WristR, 0.040f);
            Bone(_thighL, p.HipL, p.KneeL, 0.075f); Bone(_shinL, p.KneeL, p.AnkleL, 0.055f);
            Bone(_thighR, p.HipR, p.KneeR, 0.075f); Bone(_shinR, p.KneeR, p.AnkleR, 0.055f);
            PoseBoot(_bootL, p.AnkleL, p.BallL, p.Up); PoseBoot(_bootR, p.AnkleR, p.BallR, p.Up);
            PoseHand(_handL, p.WristL, p.GripL, p.HandRotL, p.CurlL, true);
            PoseHand(_handR, p.WristR, p.GripR, p.HandRotR, p.CurlR, false);
        }
    }
}
