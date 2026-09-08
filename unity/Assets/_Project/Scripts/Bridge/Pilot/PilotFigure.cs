using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The pilot's articulated figure (owner request 2026-09-08: "look more like a real person", arms up on
    /// the steering toggles once the canopy opens, the arm on the steered side pulling down). Built from
    /// iOS-safe primitives under the <see cref="PilotBody"/> root — no assets, no physics, 22 renderers.
    ///
    /// Frame = PilotBody's root: origin at the hips, +y up the spine, +z the way he faces, +x his RIGHT.
    /// Feet at y = −0.9 (PilotBody.HalfHeightM), helmet top ≈ +0.9 — a 1.8 m adult (head 0.24, torso 0.6,
    /// upper arm 0.32, forearm 0.28, thigh 0.45, shin 0.43 + boot).
    ///
    /// Joints are nested pivots (Shoulder → Elbow → hand, Hip → Knee → boot, Neck → helmet) so the limbs
    /// rotate about anatomical points. Arms are driven by HAND TARGETS through a 2-bone analytic IK with an
    /// elbow "pole" hint — the toggle pull is then exact: the hand target slides down the toggle line and the
    /// elbow follows. Legs and head use explicit joint rotations. A stance change cross-fades the limb set.
    ///
    /// Stances: Seated (strapped to the ejection seat) · FreeFall (spread skydiver box, slight arch, the root
    /// tumbles) · Hanging (upright in the harness, legs together and slightly bent, both hands up on the
    /// toggles at head height) · Landed (standing, arms down). Steering under canopy: the LEFT arm pulls
    /// its toggle down 0.4 m for full left stick (Steer < 0), the RIGHT arm for right stick — the other arm
    /// stays up; the pull lags the stick by 0.15 s.
    /// </summary>
    internal sealed class PilotFigure
    {
        public enum Stance { Seated, FreeFall, Hanging, Landed }

        // Segment lengths (m) and joint positions (root-local).
        public const float UpperArmM = 0.32f, ForearmM = 0.28f, ThighM = 0.45f, ShinM = 0.43f;
        public const float ShoulderX = 0.24f, ShoulderY = 0.55f, HipX = 0.11f, HipY = 0.02f;
        /// <summary>Toggle lines hang from this far above the harness confluence, this far either side of the hang axis.</summary>
        public const float ToggleAnchorAboveHarnessM = 1.2f, ToggleAnchorHalfSpanM = 0.42f;
        public const float TogglePullM = 0.40f;        // full pull: the hand drops from head height to shoulder level
        public const float PullLagSec = 0.15f;         // first-order lag of the arm behind the stick
        public const float HangBlendSec = 0.8f;        // free fall → hanging (line stretch snaps him upright)
        public const float BlendSec = 0.5f;            // other stance changes

        /// <summary>One stance: root-local hand targets + elbow bend hints, and the leg / neck joint rotations.</summary>
        private struct Limbs
        {
            public Vector3 HandL, HandR, PoleL, PoleR;
            public Quaternion HipL, HipR, KneeL, KneeR, Neck;
        }

        // Hand targets are given for the RIGHT side and mirrored. Hip: (swing-back °, abduction °), knee flex °,
        // neck pitch ° (negative = looks up).
        private static readonly Limbs SeatedPose   = Make(new Vector3(0.22f, 0.12f, 0.30f), new Vector3(0.7f, -0.5f, 0f), -85f, 4f, 85f, 0f);
        private static readonly Limbs FreeFallPose = Make(new Vector3(0.46f, 0.86f, 0.10f), new Vector3(1f, -0.3f, 0f), 20f, 25f, 55f, -20f);
        // Toggles up: upper arm up alongside the helmet (elbow ≈ (±0.28, 0.85, −0.11), 13 cm clear of it), forearm
        // out to the hand at (±0.50, 0.92) — head height (helmet top 0.885), 0.26 m outboard of the shoulder.
        // Both poles lean BACK (−z) so the lerped pole never crosses the shoulder→hand axis: the elbow sweeps
        // out-and-down behind the axis continuously during a pull instead of flipping sides.
        private static readonly Limbs HangingPose  = Make(new Vector3(0.50f, 0.92f, 0.05f), new Vector3(-0.8f, 0.2f, -0.8f), -15f, 3f, 30f, 0f);
        private static readonly Limbs LandedPose   = Make(new Vector3(0.27f, -0.03f, 0.03f), new Vector3(0f, 0f, -1f), 0f, 4f, 0f, 0f);
        // Full toggle pull: the hand comes straight down TogglePullM to shoulder level (y 0.52), the elbow drops to the ribs.
        private static readonly Vector3 PullOffsetR = new Vector3(0.02f, -TogglePullM, 0.05f);
        private static readonly Vector3 PullOffsetL = Mirror(PullOffsetR);
        private static readonly Vector3 PulledPoleR = new Vector3(0.5f, -0.7f, -0.9f);   // elbow at the ribs, behind
        private static readonly Vector3 PulledPoleL = Mirror(PulledPoleR);

        private readonly Transform _shoulderL, _shoulderR, _elbowL, _elbowR, _hipL, _hipR, _kneeL, _kneeR, _neck;
        private readonly GameObject _pack, _toggleL, _toggleR;
        private Limbs _from, _to, _cur;
        private float _blendT, _blendDur;
        private Stance _stance;
        private float _pullL, _pullR;
        private bool _togglesShown;

        public PilotFigure(Transform root, Stance stance)
        {
            Material suit = EgressAir.Unlit(new Color(0.35f, 0.42f, 0.28f));      // olive flight suit
            Material skin = EgressAir.Unlit(new Color(0.87f, 0.66f, 0.50f));
            Material helmet = EgressAir.Unlit(new Color(0.93f, 0.93f, 0.93f));
            Material visor = EgressAir.Unlit(new Color(0.07f, 0.08f, 0.10f));
            Material pack = EgressAir.Unlit(new Color(0.44f, 0.37f, 0.24f));      // tan parachute container
            Material webbing = EgressAir.Unlit(new Color(0.10f, 0.11f, 0.13f));   // harness straps + toggle lines
            Material boot = EgressAir.Unlit(new Color(0.09f, 0.08f, 0.07f));

            // Trunk: pelvis narrower than the chest (the taper), pack on the back, an X of chest straps.
            EgressAir.Prim(PrimitiveType.Cube, "Pelvis", root, new Vector3(0f, -0.02f, 0f), new Vector3(0.34f, 0.20f, 0.22f), suit);
            EgressAir.Prim(PrimitiveType.Cube, "Torso", root, new Vector3(0f, 0.31f, 0f), new Vector3(0.40f, 0.50f, 0.24f), suit);
            _pack = EgressAir.Prim(PrimitiveType.Cube, "Pack", root, new Vector3(0f, 0.30f, -0.20f), new Vector3(0.34f, 0.44f, 0.18f), pack);
            for (int s = 0; s < 2; s++)
            {
                GameObject strap = EgressAir.Prim(PrimitiveType.Cube, s == 0 ? "StrapL" : "StrapR", root,
                    new Vector3(0f, 0.31f, 0.128f), new Vector3(0.05f, 0.52f, 0.015f), webbing);
                strap.transform.localRotation = Quaternion.AngleAxis(s == 0 ? 22f : -22f, Vector3.forward);
            }

            // Head: helmet with a dark visor band and the skin-tone chin/face showing under it.
            _neck = Pivot("Neck", root, new Vector3(0f, 0.58f, 0f));
            EgressAir.Prim(PrimitiveType.Sphere, "Helmet", _neck, new Vector3(0f, 0.17f, -0.01f), Vector3.one * 0.27f, helmet);
            EgressAir.Prim(PrimitiveType.Cube, "Visor", _neck, new Vector3(0f, 0.18f, 0.12f), new Vector3(0.20f, 0.08f, 0.05f), visor);
            EgressAir.Prim(PrimitiveType.Cube, "Face", _neck, new Vector3(0f, 0.10f, 0.11f), new Vector3(0.14f, 0.09f, 0.06f), skin);

            BuildArm(root, false, suit, skin, out _shoulderL, out _elbowL);
            BuildArm(root, true, suit, skin, out _shoulderR, out _elbowR);
            BuildLeg(root, false, suit, boot, out _hipL, out _kneeL);
            BuildLeg(root, true, suit, boot, out _hipR, out _kneeR);

            // Steering toggle lines (riser anchor → hand), shown only while hanging under the open canopy.
            _toggleL = EgressAir.Prim(PrimitiveType.Cylinder, "ToggleL", root, Vector3.zero, Vector3.zero, webbing);
            _toggleR = EgressAir.Prim(PrimitiveType.Cylinder, "ToggleR", root, Vector3.zero, Vector3.zero, webbing);
            _toggleL.SetActive(false);
            _toggleR.SetActive(false);
            _togglesShown = false;

            _stance = stance;
            _cur = _from = _to = PoseFor(stance);
            _blendDur = BlendSec;
            _blendT = BlendSec;
            Tick(0f, stance, 0f, false, Vector3.zero, Vector3.zero);
        }

        /// <summary>The parachute container lives inside the ejection seat until separation.</summary>
        public void SetPackVisible(bool visible)
        {
            if (_pack != null && _pack.activeSelf != visible) _pack.SetActive(visible);
        }

        /// <summary>
        /// Pose the figure. `steer` −1..1 (negative = left, only acts while Hanging). Toggle anchors are ROOT-LOCAL
        /// points the two toggle lines hang from (used only when `togglesVisible`).
        /// </summary>
        public void Tick(float dt, Stance stance, float steer, bool togglesVisible, Vector3 toggleAnchorL, Vector3 toggleAnchorR)
        {
            if (stance != _stance)
            {
                _from = _cur;
                _to = PoseFor(stance);
                _blendT = 0f;
                _blendDur = stance == Stance.Hanging ? HangBlendSec : BlendSec;
                _stance = stance;
            }
            if (_blendT < _blendDur)
            {
                _blendT = Mathf.Min(_blendDur, _blendT + dt);
                float k = _blendT / _blendDur;
                k = k * k * (3f - 2f * k);
                _cur = Lerp(_from, _to, k);
            }

            // Toggle pull: left stick → left arm down, right stick → right arm down, first-order lag.
            bool hanging = stance == Stance.Hanging;
            float wantL = hanging ? Mathf.Clamp01(-steer) : 0f;
            float wantR = hanging ? Mathf.Clamp01(steer) : 0f;
            float a = dt > 0f ? 1f - Mathf.Exp(-dt / PullLagSec) : 0f;
            _pullL += (wantL - _pullL) * a;
            _pullR += (wantR - _pullR) * a;

            Vector3 handL = SolveArm(_shoulderL, _elbowL, new Vector3(-ShoulderX, ShoulderY, 0f),
                _cur.HandL + PullOffsetL * _pullL, Vector3.Lerp(_cur.PoleL, PulledPoleL, _pullL));
            Vector3 handR = SolveArm(_shoulderR, _elbowR, new Vector3(ShoulderX, ShoulderY, 0f),
                _cur.HandR + PullOffsetR * _pullR, Vector3.Lerp(_cur.PoleR, PulledPoleR, _pullR));

            _hipL.localRotation = _cur.HipL;
            _hipR.localRotation = _cur.HipR;
            _kneeL.localRotation = _cur.KneeL;
            _kneeR.localRotation = _cur.KneeR;
            _neck.localRotation = _cur.Neck;

            if (togglesVisible != _togglesShown)
            {
                _togglesShown = togglesVisible;
                _toggleL.SetActive(togglesVisible);
                _toggleR.SetActive(togglesVisible);
            }
            if (togglesVisible)
            {
                SetLocalLine(_toggleL, toggleAnchorL, handL, 0.02f);
                SetLocalLine(_toggleR, toggleAnchorR, handR, 0.02f);
            }
        }

        // ---- build ---------------------------------------------------------------------------------

        private static Transform Pivot(string name, Transform parent, Vector3 localPos)
        {
            Transform t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }

        private static void BuildArm(Transform root, bool right, Material suit, Material skin, out Transform shoulder, out Transform elbow)
        {
            string side = right ? "R" : "L";
            shoulder = Pivot("Shoulder" + side, root, new Vector3(right ? ShoulderX : -ShoulderX, ShoulderY, 0f));
            EgressAir.Prim(PrimitiveType.Capsule, "UpperArm" + side, shoulder, new Vector3(0f, -UpperArmM * 0.5f, 0f),
                new Vector3(0.10f, UpperArmM * 0.5f, 0.10f), suit);
            elbow = Pivot("Elbow" + side, shoulder, new Vector3(0f, -UpperArmM, 0f));
            EgressAir.Prim(PrimitiveType.Capsule, "Forearm" + side, elbow, new Vector3(0f, -ForearmM * 0.5f, 0f),
                new Vector3(0.085f, ForearmM * 0.5f, 0.085f), suit);
            EgressAir.Prim(PrimitiveType.Sphere, "Hand" + side, elbow, new Vector3(0f, -ForearmM - 0.02f, 0f), Vector3.one * 0.10f, skin);
        }

        private static void BuildLeg(Transform root, bool right, Material suit, Material boot, out Transform hip, out Transform knee)
        {
            string side = right ? "R" : "L";
            hip = Pivot("Hip" + side, root, new Vector3(right ? HipX : -HipX, HipY, 0f));
            EgressAir.Prim(PrimitiveType.Capsule, "Thigh" + side, hip, new Vector3(0f, -ThighM * 0.5f, 0f),
                new Vector3(0.13f, ThighM * 0.5f, 0.13f), suit);
            knee = Pivot("Knee" + side, hip, new Vector3(0f, -ThighM, 0f));
            EgressAir.Prim(PrimitiveType.Capsule, "Shin" + side, knee, new Vector3(0f, -ShinM * 0.5f, 0f),
                new Vector3(0.105f, ShinM * 0.5f, 0.105f), suit);
            // Boot: 0.08 tall centred on the ankle, toe forward → sole at root y = HipY − ThighM − ShinM − 0.04 = −0.90.
            EgressAir.Prim(PrimitiveType.Cube, "Boot" + side, knee, new Vector3(0f, -ShinM, 0.05f), new Vector3(0.12f, 0.08f, 0.26f), boot);
        }

        // ---- poses ---------------------------------------------------------------------------------

        private static Vector3 Mirror(Vector3 v) => new Vector3(-v.x, v.y, v.z);

        /// <summary>Hip: abduct about z (outboard for that side), then swing about x (+ = leg back, − = knee forward).</summary>
        private static Quaternion Hip(float swingBackDeg, float abductDeg, bool right) =>
            Quaternion.AngleAxis(swingBackDeg, Vector3.right) * Quaternion.AngleAxis(right ? abductDeg : -abductDeg, Vector3.forward);

        /// <summary>Knee: + flexes the shin back under the thigh.</summary>
        private static Quaternion Knee(float flexDeg) => Quaternion.AngleAxis(flexDeg, Vector3.right);

        private static Limbs Make(Vector3 handR, Vector3 poleR, float hipBackDeg, float hipAbductDeg, float kneeDeg, float neckPitchDeg) => new Limbs
        {
            HandL = Mirror(handR), HandR = handR,
            PoleL = Mirror(poleR), PoleR = poleR,
            HipL = Hip(hipBackDeg, hipAbductDeg, false), HipR = Hip(hipBackDeg, hipAbductDeg, true),
            KneeL = Knee(kneeDeg), KneeR = Knee(kneeDeg),
            Neck = Quaternion.AngleAxis(neckPitchDeg, Vector3.right),
        };

        private static Limbs PoseFor(Stance s)
        {
            switch (s)
            {
                case Stance.Seated: return SeatedPose;
                case Stance.Hanging: return HangingPose;
                case Stance.Landed: return LandedPose;
                default: return FreeFallPose;
            }
        }

        private static Limbs Lerp(Limbs a, Limbs b, float k) => new Limbs
        {
            HandL = Vector3.Lerp(a.HandL, b.HandL, k), HandR = Vector3.Lerp(a.HandR, b.HandR, k),
            PoleL = Vector3.Lerp(a.PoleL, b.PoleL, k), PoleR = Vector3.Lerp(a.PoleR, b.PoleR, k),
            HipL = Quaternion.Slerp(a.HipL, b.HipL, k), HipR = Quaternion.Slerp(a.HipR, b.HipR, k),
            KneeL = Quaternion.Slerp(a.KneeL, b.KneeL, k), KneeR = Quaternion.Slerp(a.KneeR, b.KneeR, k),
            Neck = Quaternion.Slerp(a.Neck, b.Neck, k),
        };

        // ---- kinematics ----------------------------------------------------------------------------

        /// <summary>
        /// Two-bone analytic IK in root space: put the hand at `target` (clamped to reach) with the elbow bent
        /// toward `pole`. Sets the shoulder and elbow pivot rotations; returns the hand position reached.
        /// </summary>
        private static Vector3 SolveArm(Transform shoulder, Transform elbow, Vector3 shoulderPos, Vector3 target, Vector3 pole)
        {
            Vector3 toHand = target - shoulderPos;
            float d = toHand.magnitude;
            Vector3 dir = d > 1e-4f ? toHand / d : Vector3.down;
            d = Mathf.Clamp(d, Mathf.Abs(UpperArmM - ForearmM) + 0.01f, UpperArmM + ForearmM - 0.01f);

            Vector3 side = pole - Vector3.Dot(pole, dir) * dir;          // bend direction, ⟂ to shoulder→hand
            if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(dir, Vector3.forward);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(dir, Vector3.up);
            side.Normalize();

            float cosA = Mathf.Clamp((UpperArmM * UpperArmM + d * d - ForearmM * ForearmM) / (2f * UpperArmM * d), -1f, 1f);
            float sinA = Mathf.Sqrt(1f - cosA * cosA);
            Vector3 upperDir = dir * cosA + side * sinA;                  // shoulder → elbow
            Vector3 elbowPos = shoulderPos + upperDir * UpperArmM;
            Vector3 foreDir = target - elbowPos;
            foreDir = foreDir.sqrMagnitude > 1e-8f ? foreDir.normalized : dir;

            Quaternion shoulderRot = Quaternion.FromToRotation(Vector3.down, upperDir);
            shoulder.localRotation = shoulderRot;
            elbow.localRotation = Quaternion.Inverse(shoulderRot) * Quaternion.FromToRotation(Vector3.down, foreDir);
            return elbowPos + foreDir * ForearmM;
        }

        /// <summary>A unit cylinder (2 units tall) stretched between two root-local points.</summary>
        private static void SetLocalLine(GameObject go, Vector3 a, Vector3 b, float thickness)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            Transform t = go.transform;
            if (len < 1e-3f) { t.localScale = Vector3.zero; return; }
            t.localPosition = (a + b) * 0.5f;
            t.localRotation = Quaternion.FromToRotation(Vector3.up, d / len);
            t.localScale = new Vector3(thickness, len * 0.5f, thickness);
        }
    }
}
