using FlyingGame.Core.Vr;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// THE "MENU" TAB in the headset's cockpit: a small tab low on the left of the panel, below the windscreen line,
    /// that opens the VR menu when it is poked with either hand or pointed at and the trigger pulled. The third way
    /// in beside ☰ and the held left stick click, and the one that needs no button to be found (owner Ty,
    /// 2026-11-02, build a222bda: "cannot open the menu or options at all" in flight).
    ///
    /// The rule that keeps it from opening by accident in a loop is Core's <see cref="QuestMenuTab"/> (a poke has
    /// to come in from outside and stay a fifth of a second; a pointed trigger needs a hover first and a fresh pull).
    /// This class only finds the hands and draws the tab.
    ///
    /// It rides the seat (<see cref="QuestRig.HudAnchor"/>: the eye, the aeroplane's axes), so it sits still on the
    /// panel whatever the aeroplane does. Drawn with FlyingGame/XrOverlay, like the menu: it shows through the panel
    /// it sits on. Cockpit view only, menu closed.
    /// </summary>
    public sealed class QuestMenuTabView
    {
        /// <summary>Where the tab sits, from the seat's eye: x right, y up, z forward, metres. Low on the left of the
        /// panel — a glance down, a short reach of the left hand, never over the horizon and well above where the
        /// hands rest on the stick and throttle.</summary>
        public static readonly Vector3 At = new(-0.18f, -0.19f, 0.50f);
        public const float Width = 0.10f, Height = 0.045f;
        /// <summary>The poke volume around the tab, half extents, metres (x, y in its plane, z through it).</summary>
        public static readonly Vector3 PokeHalf = new(0.06f, 0.035f, 0.05f);

        private readonly QuestMenuTab _logic = new();
        private Transform _root;
        private Material _panelMat, _textMat;
        private TextMesh _label;
        private bool _shown;
        private Transform _anchor;

        private static readonly Color Dim = new(0.05f, 0.12f, 0.16f, 0.88f), Lit = new(0.10f, 0.55f, 0.70f, 0.95f);

        /// <summary>Once a frame from <see cref="QuestRig"/>. Returns which gesture opened the menu, if one did.</summary>
        public QuestMenuTrigger Tick(QuestRig rig, in QuestControllerFrame f, bool show, float dt)
        {
            Transform anchor = rig != null ? rig.HudAnchor : null;
            show &= anchor != null;
            if (!show)
            {
                if (_shown) { if (_root != null) _root.gameObject.SetActive(false); _shown = false; }
                _logic.Reset();
                return QuestMenuTrigger.None;
            }
            if (_root == null) Build();
            if (_anchor != anchor || _root.parent != anchor)
            {
                _anchor = anchor;
                _root.SetParent(anchor, false);
                _root.localPosition = At;
                _root.localRotation = Quaternion.LookRotation(At.normalized, Vector3.up);   // square on to the eye
            }
            if (!_shown) { _root.gameObject.SetActive(true); _shown = true; _logic.Reset(); }

            // Both hands: is a tip inside the tab, is a ray on it (right hand first), and that hand's trigger.
            bool poke = false, ray = false;
            double trig = 0.0;
            for (int h = 0; h < 2; h++)
            {
                bool left = h == 1;
                if (!XrControllers.TryPointer(left, out Vector3 p, out Quaternion q)) continue;
                if (!rig.TrackingToWorld(p, q, out Vector3 wp, out Quaternion wq)) continue;
                Vector3 tip = _root.InverseTransformPoint(wp);
                if (Mathf.Abs(tip.x) <= PokeHalf.x && Mathf.Abs(tip.y) <= PokeHalf.y && Mathf.Abs(tip.z) <= PokeHalf.z) poke = true;
                if (!ray && RayHits(_root, wp, wq * Vector3.forward))
                {
                    ray = true;
                    trig = f.HandsOnly ? (left ? f.LeftPinch : f.RightPinch) : (left ? f.LeftTrigger : f.RightTrigger);
                }
            }
            QuestMenuTrigger got = _logic.Step(dt, poke, ray, trig);
            _panelMat.color = Color.Lerp(Dim, Lit, (float)_logic.Glow01);
            return got;
        }

        /// <summary>The ray meets the tab's plane, in front of the hand, inside the tab (a little margin).</summary>
        private static bool RayHits(Transform tab, Vector3 o, Vector3 d)
        {
            Vector3 lo = tab.InverseTransformPoint(o), ld = tab.InverseTransformDirection(d);
            if (Mathf.Abs(ld.z) < 1e-4f) return false;
            float t = -lo.z / ld.z;
            if (t <= 0f || t > 3f) return false;
            Vector3 hit = lo + ld * t;
            return Mathf.Abs(hit.x) <= Width * 0.5f + 0.01f && Mathf.Abs(hit.y) <= Height * 0.5f + 0.01f;
        }

        private void Build()
        {
            Shader sh = Shader.Find("FlyingGame/XrOverlay");   // ours: ZTest Always, force-included (BuildScript)
            if (sh == null) { Debug.LogError("[QuestInput] FlyingGame/XrOverlay shader missing: the MENU tab is drawn unlit"); sh = Shader.Find("Unlit/Color"); }
            Font font = UiFont.Get();
            _panelMat = new Material(sh) { mainTexture = Texture2D.whiteTexture, color = Dim, renderQueue = 4000 };
            _textMat = new Material(sh) { mainTexture = font.material.mainTexture, renderQueue = 4002 };
            Font.textureRebuilt += fnt => { if (fnt == font && _textMat != null) _textMat.mainTexture = font.material.mainTexture; };

            _root = new GameObject("VR Menu Tab").transform;
            var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
            panel.name = "Panel";
            Object.Destroy(panel.GetComponent<Collider>());
            panel.transform.SetParent(_root, false);
            panel.transform.localPosition = new Vector3(0f, 0f, 0.002f);
            panel.transform.localScale = new Vector3(Width, Height, 1f);
            var pr = panel.GetComponent<MeshRenderer>();
            pr.sharedMaterial = _panelMat;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pr.receiveShadows = false;

            var go = new GameObject("Text");
            go.transform.SetParent(_root, false);
            _label = go.AddComponent<TextMesh>();
            _label.font = font;
            _label.fontSize = 64;
            _label.characterSize = 0.022f * 10f / _label.fontSize;
            _label.anchor = TextAnchor.MiddleCenter;
            _label.alignment = TextAlignment.Center;
            _label.color = new Color(0.92f, 0.97f, 1f, 1f);
            _label.text = "MENU";
            var tr = go.GetComponent<MeshRenderer>();
            tr.sharedMaterial = _textMat;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.receiveShadows = false;
            _root.gameObject.SetActive(false);
            Debug.Log($"[QuestInput] MENU tab built at the seat eye + ({At.x:F2}, {At.y:F2}, {At.z:F2}) m: poke it, or point and pull the trigger");
        }
    }
}
