using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// THE DRAWN WHEELS FOLLOW THE SUSPENSION. The gear flexes and the legs hinge on the fuselage (commit 4805726a,
    /// Core LandingGear: <see cref="LandingGear.ContactPointM"/> swings each contact up AND outboard on an arc about
    /// its fuselage pivot as the leg compresses), but a model is drawn at ONE gear state and nothing moved it — so a
    /// sitting aeroplane was drawn with its tyres BURIED by the static squash and its track narrower than it stands.
    /// Measured on the models the game loads, with the aeroplane settled on three points (GearVisualTests): the J-3's
    /// legs squash 40.9 mm and its drawn wheels are 45.2 mm inboard of where it stands (90 mm of track) with 3.76 deg
    /// of hinge to make up; the Great Lakes 45.4 mm, 27.4 mm inboard (55 mm of track), 2.50 deg. Ty, shown that:
    /// <i>"Wire it — two types are already rigged"</i>.
    ///
    /// WHAT IT DOES. Per rigged side: turn the model's own <c>Gear_Pivot_&lt;s&gt;</c> node about the line from it to
    /// <c>Gear_HingeAxis_&lt;s&gt;</c>, by the angle that lifts the DRAWN tyre's contact by the leg's current stroke
    /// (<see cref="WheelState.LegStrokeM"/>). The node's origin IS the hinge and the leg tubes, the wheel and the lower
    /// shock hang off it, so turning it is the one thing you can do to a drawn leg that keeps it on its fittings —
    /// which is the whole reason the Blender scripts rigged it this way (tools/own_models/build_j3_cub.py,
    /// build_great_lakes.py).
    ///
    /// SCOPE — TWO MODELS, AND NOTHING ELSE MAY MOVE. A side is driven only when the model brings all three of
    /// <c>Gear_Pivot_&lt;s&gt;</c>, <c>Gear_HingeAxis_&lt;s&gt;</c> and a <c>Wheel_&lt;s&gt;</c> with geometry, and the
    /// config has a main leg with a pivot authored on that side. From the GLBs (not the build scripts) that is exactly
    /// j3-cub-like-own and great-lakes-like-own in its three liveries. j3-cub-skis-like-own carries the pivot and the
    /// axis but NO wheel — it is on skis, and a ski's contact is its own underside, not a tyre's — so it has nothing
    /// to measure and is left alone. There is no stand-in pivot and no guessed axis for anything else.
    ///
    /// EVERY NUMBER IS MEASURED ON THE MODEL, nothing is assumed from the build script's wording, and that matters
    /// here: the Great Lakes' pivot node says <c>hinge_deg_as_exported 15.79</c> (it was exported in the STATIC pose,
    /// solved to land its 3-point height on the published 7 ft 4 in), so a reader would subtract 15.79 deg from every
    /// angle. They must not — the config's gear point was then taken off that exported pose, so model and config agree
    /// and the drawn offset is zero for it too. <see cref="Leg.DrawnStrokeM"/> is that offset, measured, so neither
    /// convention has to be believed.
    ///
    /// AND THE ANGLE IS NOT THE SIM'S. <see cref="LandingGear.LegHingeRad"/> is the angle of a leg whose hinge line is
    /// square to the arc. The J-3's is, to 1.97 deg, and that angle lifts its drawn contact to 99.9 % of the stroke.
    /// The Great Lakes' axle hinges at the fuselage CENTRELINE with its compression strut running aft: its line is
    /// canted 35.8 deg in plan, the same angle lifts the wheel only 87 % as far, and it needs x1.14. So the angle is
    /// SOLVED here against the axis measured on this model, for the rise Core asks for. REJECTED: taking LegHingeRad
    /// and applying it (one line, no solve) — right for the J-3 to a tenth of a millimetre, 12 mm short on the Great
    /// Lakes at a 100 mm stroke, and the next rigged model's cant is nobody's guess.
    ///
    /// FRAMES, and why there is no sim→Unity axis swap anywhere in here. Everything is measured in, solved in and
    /// applied in ONE frame: the pivot node's PARENT. The rotation is <c>localRotation</c> about the pivot's own
    /// origin, which is a rotation in that frame; so the solve is done there too, and the handedness question that
    /// <c>AirframeVisual.HingeSign</c> exists for never arises. The one thing that frame does not know is which way is
    /// UP and how long a metre is — both come from <see cref="Leg.UpHat"/> / <see cref="Leg.PerMetre"/>, taken from the
    /// parent→aeroplane map built out of <see cref="Pt"/> at bind, which carries the model's fit scale and the
    /// importer's axis conversion with it whatever they are.
    ///
    /// Positions are composed from LOCAL transforms only (<see cref="Pt"/>), never through world space: the aeroplane
    /// is routinely kilometres from the origin, where a float world position rounds to centimetres — the same rule
    /// AirframeRig's slave struts are built on.
    ///
    /// AND THE SHOCK STRUTS HAVE TO FOLLOW, or the fix looks worse than what it replaces. A leg's strut spans a pin
    /// that MOVES with the hinge (the axle end) and one that does not (the J-3's bungee apex under the belly, the
    /// Great Lakes' oleo top on the longeron). Turning the leg alone slides the drawn tube's far end about as far as
    /// the axle moves — 40-45 mm at the static stance — a visible break in the gear of a Cub. So each strut is
    /// re-aimed from one pin at the other,
    /// which is exactly what the Great Lakes' pivot node says to do in so many words: <c>hinge_reference</c> =
    /// <i>"rotate the pivot by (target - hinge_deg_as_exported) from its rest rotation, then re-aim Oleo_Cylinder at
    /// Gear_OleoTop and Oleo_Shaft at the cylinder origin"</i>. It is the same job AirframeRig's slave struts do for
    /// the Great Lakes' interplane struts, by the same shortest-arc rotation.
    ///
    /// WHAT THIS IS NOT. It is not the retractable-gear visual (<c>AirframeBuilder.SetGearExtension</c>, which scales
    /// legs into their wells); neither rigged type retracts. It is not driven for a remote player's aeroplane, a
    /// replay or the widget: the recording and the network packet carry the gear EXTENSION fraction and the control
    /// deflections, and no per-leg compression, so there is nothing to drive them from and they keep the model's own
    /// pose. Recording it would be a new field in FlightTrack.PackedFrame and is Ty's call, not this lane's.
    /// </summary>
    public sealed class GearFlexVisual
    {
        /// <summary>The most hinge a drawn leg is ever turned by. The two rigged models publish their own full travel
        /// on the node — the J-3's <c>hinge_deg_full</c> is 9.019 deg (0.10 m at the wheel) and the Great Lakes'
        /// 22.916 deg — so 25 deg clears both and nothing can be swung through the fuselage by a stroke the sim allows
        /// past the leg's stop (a bungee leg's is 0.18 m; an oleo has none authored, so its stroke is unbounded).</summary>
        public const float MaxHingeDeg = 25f;

        /// <summary>Below this the pose is left alone: a tenth of a millimetre of wheel travel is nothing to see, and a
        /// parked aeroplane then costs no transform writes at all.</summary>
        private const float DeadBandDeg = 0.01f;

        private sealed class Leg
        {
            /// <summary>Gear_Pivot_&lt;s&gt;: its origin is the hinge, and the leg hangs off it.</summary>
            public Transform Pivot;
            /// <summary>The pose the model was exported in — every angle is measured from here.</summary>
            public Quaternion RestRot;
            /// <summary>The hinge line, unit, in the pivot's PARENT frame (what the rotation turns about).</summary>
            public Vector3 Axis;
            /// <summary>Pivot → the DRAWN tyre's contact, in the parent frame (the arm the solve lifts).</summary>
            public Vector3 Arm;
            /// <summary>The aeroplane's own up, unit, in the parent frame; and how many parent units a metre of it is.</summary>
            public Vector3 UpHat;
            public float PerMetre;
            /// <summary>+1 or −1: which way round <see cref="Axis"/> raises THIS wheel. MEASURED on the model at bind,
            /// the way build_j3_cub.py measures its own (<c>_s = 1.0 if g_rot(G_A, 0.05)[2] &gt; G_A[2] else -1.0</c>) —
            /// so neither the exporter's axis conversion nor the model's left/right mirroring has to be reasoned about.</summary>
            public float Sign;
            /// <summary>The leg stroke the model was DRAWN at (m): the angle applied is the current stroke less this.</summary>
            public float DrawnStrokeM;
            /// <summary>Index into Config.Gear and Aircraft.Wheels — matched by which SIDE the drawn wheel is on.</summary>
            public int Gear;
            /// <summary>The last angle written, so a still aeroplane writes no transforms.</summary>
            public float Applied = float.NaN;
        }

        /// <summary>A drawn strut spanning one pin that moves with the hinge and one that does not: its own origin sits
        /// on one of them (true of all four in our two models) and its long axis points at the other, so putting it
        /// back is one shortest-arc rotation about its own origin and its localPosition never changes.</summary>
        private sealed class Strut
        {
            public Transform T;            // the strut; its own origin is one pin
            public Transform Other;        // the node whose origin is the other pin
            public Transform Frame;        // an ancestor of BOTH (the pivot's parent), where the two pins are compared
            public Quaternion RestRot;
            public Vector3 RestDir;        // pin-to-pin at rest, in the STRUT'S OWN parent frame, unit
        }

        /// <summary>Which drawn pieces span a moving pin and a fixed one, by node name and per side. A pair where
        /// either node is missing is skipped, so a model that has only some of them still gets the rest.
        ///  - the J-3's bungee: the lower tube rides on the axle pin and reaches up to the belly apex, and the boot
        ///    (with the upper tube inside it) hangs on that apex pin and reaches back down to the axle;
        ///  - the Great Lakes' oleo: the cylinder rides on the bottom pin and reaches up to Gear_OleoTop, and the shaft
        ///    hangs on Gear_OleoTop and reaches down to the cylinder's own origin.
        /// Both halves of each pair are listed because each has to AIM at the other; neither reads the other's
        /// rotation, only its origin, so the order they are done in does not matter.</summary>
        private static readonly (string Strut, string Other)[] StrutPairs =
        {
            ("Shock_Lower_", "Shock_Boot_"),
            ("Shock_Boot_", "Shock_Lower_"),
            ("Oleo_Cylinder_", "Gear_OleoTop_"),
            ("Oleo_Shaft_", "Oleo_Cylinder_"),
        };

        private readonly List<Leg> _legs = new();
        private readonly List<Strut> _struts = new();
        private Transform _boundTo;
        private bool _tried;

        /// <summary>True once a model has been looked at, whether or not anything on it could be driven.</summary>
        public bool Bound => _tried && _boundTo != null;
        /// <summary>How many sides are being driven (0 for every type whose model is not rigged for this).</summary>
        public int Count => _legs.Count;

        /// <summary>Forget the binding (a rebuild, a new model, a new livery).</summary>
        public void Clear() { _legs.Clear(); _struts.Clear(); _boundTo = null; _tried = false; }

        // ---- reading the model ---------------------------------------------------------------------------------

        /// <summary>A local point of <paramref name="t"/> in <paramref name="frame"/>'s local space, composed from
        /// LOCAL transforms because the aeroplane is routinely kilometres from the origin (AirframeRig does the same).
        /// False when <paramref name="frame"/> is not an ancestor, in which case nothing is driven.</summary>
        private static bool Pt(Transform t, Transform frame, Vector3 local, out Vector3 p)
        {
            p = local;
            Transform x = t;
            for (; x != null && x != frame; x = x.parent) p = x.localPosition + x.localRotation * Vector3.Scale(x.localScale, p);
            return x == frame;
        }

        /// <summary>The node the GLB calls <paramref name="node"/>, inside this model only. The importer prefixes every
        /// MESH object with "Model:" and appends "_1"/"_2" per extra primitive, so both are allowed for — the same
        /// normalising AirframeVisual's hinge markers needed before they would match at all. Empties keep their name.</summary>
        private static Transform Find(Transform modelRoot, string node)
        {
            foreach (Transform t in modelRoot.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                int c = n.LastIndexOf(':');
                if (c >= 0) n = n.Substring(c + 1);
                if (n == node) return t;
            }
            return null;
        }

        /// <summary>The DRAWN tyre: the lowest point of its mesh along <paramref name="upHat"/> (its contact) with the
        /// fore-and-aft and lateral middle of the wheel (where that contact sits), in <paramref name="frame"/>'s space.
        ///
        /// From the mesh BOUNDS' eight corners, not its vertices: bounds are metadata and need no readable mesh, and a
        /// wheel is a disc whose lowest bound corner IS the bottom of its tyre. The node's own mesh and anything the
        /// importer split off it are both taken, because a multi-material wheel (ours has tyre and hub) arrives as
        /// Wheel_L plus a child Wheel_L_1 and either may be the tyre.</summary>
        private static bool DrawnContact(Transform wheel, Transform frame, Vector3 upHat, out Vector3 contact)
        {
            contact = Vector3.zero;
            bool any = false;
            float lo = 0f;
            Vector3 min = Vector3.zero, max = Vector3.zero, lowest = Vector3.zero;
            foreach (MeshFilter mf in wheel.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh m = mf.sharedMesh;
                if (m == null) continue;
                Bounds b = m.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var v = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    if (!Pt(mf.transform, frame, v, out Vector3 p)) return false;
                    float d = Vector3.Dot(p, upHat);
                    if (!any) { min = max = p; lo = d; lowest = p; any = true; continue; }
                    min = Vector3.Min(min, p); max = Vector3.Max(max, p);
                    if (d < lo) { lo = d; lowest = p; }
                }
            }
            if (!any) return false;
            // The contact's height is the tyre's lowest point; where it sits is the middle of the wheel. Taken as the
            // lowest corner plus the middle MINUS its own height, so the point is on the tyre's bottom, not inside it.
            Vector3 mid = 0.5f * (min + max);
            contact = mid + upHat * (Vector3.Dot(lowest, upHat) - Vector3.Dot(mid, upHat));
            return true;
        }

        // ---- bind ----------------------------------------------------------------------------------------------

        /// <summary>
        /// Look the model over once and keep whichever sides are rigged for this. <paramref name="root"/> is the
        /// AEROPLANE's own transform — the frame the config's metres and its "up" live in — and
        /// <paramref name="modelRoot"/> the placed model inside it. Safe to call every frame; it does the work once per
        /// model and logs one line either way, so a model that was expected to flex and does not says so.
        /// </summary>
        public void Bind(Transform root, Transform modelRoot, AircraftConfig cfg, string modelId)
        {
            if (root == null || modelRoot == null || cfg == null) return;
            if (_tried && _boundTo == modelRoot) return;
            _legs.Clear();
            _struts.Clear();
            _boundTo = modelRoot;
            _tried = true;

            var missing = new List<string>();
            foreach (string side in new[] { "L", "R" })
            {
                Transform pivot = Find(modelRoot, "Gear_Pivot_" + side);
                Transform axisEnd = Find(modelRoot, "Gear_HingeAxis_" + side);
                Transform wheel = Find(modelRoot, "Wheel_" + side);
                if (pivot == null) continue;                                  // not rigged for this at all: silence
                if (axisEnd == null || wheel == null)
                {
                    // j3-cub-skis-like-own is exactly this: pivot and axis, no wheel. It is on skis and is left alone.
                    missing.Add($"Gear_Pivot_{side} has no {(axisEnd == null ? "Gear_HingeAxis_" + side : "Wheel_" + side)}");
                    continue;
                }
                Transform frame = pivot.parent;
                if (frame == null) { missing.Add($"Gear_Pivot_{side} has no parent frame"); continue; }

                // The parent frame → aeroplane map, columns measured with the same local-transform walk as everything
                // else, so the model's fit scale and the importer's axis conversion are carried whatever they are.
                if (!Pt(frame, root, Vector3.zero, out Vector3 o)
                    || !Pt(frame, root, Vector3.right, out Vector3 cx)
                    || !Pt(frame, root, Vector3.up, out Vector3 cy)
                    || !Pt(frame, root, Vector3.forward, out Vector3 cz))
                { missing.Add($"Gear_Pivot_{side} is not inside the aeroplane's own transform"); continue; }
                var toRoot = new Matrix4x4();
                toRoot.SetColumn(0, cx - o); toRoot.SetColumn(1, cy - o); toRoot.SetColumn(2, cz - o);
                toRoot.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));
                if (Mathf.Abs(toRoot.determinant) < 1e-9f) { missing.Add($"Gear_Pivot_{side} sits in a degenerate frame"); continue; }
                Matrix4x4 toFrame = toRoot.inverse;
                // One metre of the aeroplane's UP, in this frame: the direction and the unit of length in one vector.
                // (In the aeroplane's own frame up is +y: the config's z is DOWN and AirframeBuilder places every part
                // through U(x, y, z) => (y, -z, x).)
                Vector3 up = toFrame.MultiplyVector(Vector3.up);
                if (up.sqrMagnitude < 1e-12f) { missing.Add($"Gear_Pivot_{side}: no scale to its frame"); continue; }

                if (!Pt(axisEnd, frame, Vector3.zero, out Vector3 axisP)) { missing.Add($"Gear_HingeAxis_{side} is outside the pivot's frame"); continue; }
                Vector3 pivotP = pivot.localPosition;
                Vector3 axis = axisP - pivotP;
                if (axis.sqrMagnitude < 1e-8f) { missing.Add($"Gear_HingeAxis_{side} sits on the pivot: no hinge line"); continue; }

                var leg = new Leg
                {
                    Pivot = pivot, RestRot = pivot.localRotation,
                    Axis = axis.normalized, UpHat = up.normalized, PerMetre = up.magnitude,
                };
                if (!DrawnContact(wheel, frame, leg.UpHat, out Vector3 contactP)) { missing.Add($"Wheel_{side} has no mesh to measure"); continue; }
                leg.Arm = contactP - pivotP;
                if (leg.Arm.sqrMagnitude < 1e-8f) { missing.Add($"Wheel_{side} is drawn on the pivot: no arm"); continue; }

                // WHICH WAY ROUND RAISES IT: measured, never reasoned about.
                leg.Sign = RiseM(leg, 1f) >= 0f ? 1f : -1f;

                // WHICH LEG IT IS: by the side the drawn wheel is actually on, not by the node's letter. The
                // aeroplane's own frame has Unity x = the config's y (U above), so the arm's x says port or starboard.
                float drawnY = toRoot.MultiplyVector(leg.Arm).x;
                int gi = -1;
                for (int i = 0; i < cfg.Gear.Count; i++)
                {
                    GearConfig g = cfg.Gear[i];
                    if (g.IsTailwheel || Mathf.Abs((float)g.Pos[1]) < 0.3f) continue;
                    if (LandingGear.SplayPerM(g) <= 0) continue;              // no pivot authored: not ours to move
                    if (Mathf.Sign((float)g.Pos[1]) != Mathf.Sign(drawnY)) continue;
                    gi = i; break;
                }
                if (gi < 0) { missing.Add($"Gear_Pivot_{side}: no main leg with a pivot authored on that side of {cfg.Id}"); continue; }
                // Two drawn legs on ONE sim leg would rise and fall together — an aeroplane whose wheels never take
                // weight independently, which is worse than not moving them at all. It can only happen if the side
                // came out wrong, so refuse it and say so rather than draw it.
                bool taken = false;
                foreach (Leg had in _legs) if (had.Gear == gi) taken = true;
                if (taken)
                {
                    Debug.LogWarning($"[GearFlex] {modelId}: Gear_Pivot_{side}'s drawn wheel reads as the same side as the other one"
                                     + $" (arm x {drawnY:F3} m) — both would follow leg {gi}. Neither side of this model is driven.");
                    _legs.Clear(); _struts.Clear();
                    return;
                }
                leg.Gear = gi;

                // THE POSE THE MODEL WAS DRAWN AT, as a stroke: how far the drawn contact already is above the config's
                // authored (free-hanging) gear point, along the aeroplane's up. Measured, so neither the J-3's
                // "exported extended" nor the Great Lakes' "exported static" has to be taken on trust.
                Vector3 authored = new Vector3((float)cfg.Gear[gi].Pos[1], -(float)cfg.Gear[gi].Pos[2], (float)cfg.Gear[gi].Pos[0]);
                if (!Pt(frame, root, contactP, out Vector3 contactRoot)) { missing.Add($"Wheel_{side} is outside the aeroplane's transform"); continue; }
                leg.DrawnStrokeM = contactRoot.y - authored.y;                // + = drawn ABOVE the authored point
                _legs.Add(leg);
                BindStruts(modelRoot, frame, side, modelId);                  // still at the rest pose: see BindStruts
                Debug.Log($"[GearFlex] {modelId} {side}: hinge about {leg.Axis} (sign {leg.Sign:+0;-0}), arm {leg.Arm.magnitude / leg.PerMetre:F3} m,"
                          + $" drawn at a stroke of {leg.DrawnStrokeM * 1000f:+0.0;-0.0;0.0} mm, driven by leg {gi} (y {cfg.Gear[gi].Pos[1]:+0.00;-0.00})");
            }
            if (_legs.Count > 0)
                Debug.Log($"[GearFlex] {modelId}: {_legs.Count} drawn leg(s) now follow the suspension"
                          + (missing.Count > 0 ? $"; NOT driven: {string.Join("; ", missing)}" : ""));
            else if (missing.Count > 0)
                Debug.Log($"[GearFlex] {modelId}: nothing driven — {string.Join("; ", missing)}");
        }

        /// <summary>Keep the drawn shock struts of one side between their pins. Called at bind, with the leg still at
        /// its rest pose, so each strut's rest direction is the one its draughtsman drew.</summary>
        private void BindStruts(Transform modelRoot, Transform frame, string side, string modelId)
        {
            foreach ((string a, string b) in StrutPairs)
            {
                Transform t = Find(modelRoot, a + side), other = Find(modelRoot, b + side);
                if (t == null || other == null || t.parent == null) continue;
                var s = new Strut { T = t, Other = other, Frame = frame, RestRot = t.localRotation };
                if (!PinDir(s, out Vector3 dir))
                {
                    Debug.LogWarning($"[GearFlex] {modelId}: {a}{side} and {b}{side} are not both under the gear's own frame"
                                     + " — that strut will stay at its rest pose while the leg moves.");
                    continue;
                }
                s.RestDir = dir;
                _struts.Add(s);
            }
        }

        /// <summary>Pin to pin, as the STRUT'S OWN parent frame sees it, unit: the two origins are compared in a frame
        /// that is an ancestor of both (one of them usually hangs off the pivot and the other off the fuselage, so
        /// neither frame contains both), and only the DIRECTION is carried across — a direction transform is rotation
        /// alone, so nothing here is a world position and there is nothing to round at 20 km from the origin.</summary>
        private static bool PinDir(Strut s, out Vector3 dir)
        {
            dir = Vector3.zero;
            if (s.Frame == null || s.T == null || s.T.parent == null || s.Other == null) return false;
            if (!Pt(s.T, s.Frame, Vector3.zero, out Vector3 mine)) return false;
            if (!Pt(s.Other, s.Frame, Vector3.zero, out Vector3 theirs)) return false;
            Vector3 inFrame = theirs - mine;
            if (inFrame.sqrMagnitude < 1e-8f) return false;              // the two pins coincide: no direction to hold
            Vector3 d = s.T.parent.InverseTransformDirection(s.Frame.TransformDirection(inFrame));
            if (d.sqrMagnitude < 1e-12f) return false;
            dir = d.normalized;
            return true;
        }

        // ---- per frame -----------------------------------------------------------------------------------------

        /// <summary>How far this leg's drawn contact RISES, in metres of the aeroplane, when the leg is turned
        /// <paramref name="deg"/> about its own hinge line.</summary>
        private static float RiseM(Leg l, float deg)
            => Vector3.Dot(Quaternion.AngleAxis(deg, l.Axis) * l.Arm - l.Arm, l.UpHat) / l.PerMetre;

        /// <summary>The angle that lifts the drawn contact by <paramref name="riseM"/> metres — bisected on the arc the
        /// MODEL actually has, which is why a canted hinge needs no correction factor anywhere. Monotone over the whole
        /// search: the rise is a·(cos θ − 1) + b·sin θ and it only turns over where the arm comes up to horizontal,
        /// which is 40 deg of hinge on the J-3 (its contact hangs 49.7 deg below the pivot) and 58 deg on the Great
        /// Lakes (32.2 deg below) — both well outside <see cref="MaxHingeDeg"/>. A stroke that 25 deg cannot deliver
        /// comes back clamped to it rather than inverted.</summary>
        private static float Solve(Leg l, float riseM)
        {
            if (Mathf.Abs(riseM) < 1e-5f) return 0f;
            float lo = -MaxHingeDeg, hi = MaxHingeDeg;                 // in units of sign * degrees: increasing in rise
            if (RiseM(l, l.Sign * hi) <= riseM) return l.Sign * hi;
            if (RiseM(l, l.Sign * lo) >= riseM) return l.Sign * lo;
            for (int i = 0; i < 24; i++)
            {
                float mid = 0.5f * (lo + hi);
                if (RiseM(l, l.Sign * mid) < riseM) lo = mid; else hi = mid;
            }
            return l.Sign * 0.5f * (lo + hi);
        }

        /// <summary>Put every driven leg where the aeroplane is standing on it. <paramref name="wheels"/> is
        /// <c>Aircraft.Wheels</c> — one <see cref="WheelState"/> per config leg, in the config's own order — and the
        /// only thing read off it is <see cref="WheelState.LegStrokeM"/>, which is 0 for a leg in the air, so an
        /// aeroplane off the ground hangs its gear exactly where the model draws it.</summary>
        public void Apply(IReadOnlyList<WheelState> wheels)
        {
            if (_legs.Count == 0 || wheels == null) return;
            bool moved = false;
            foreach (Leg l in _legs)
            {
                if (l.Pivot == null) continue;
                if (l.Gear >= wheels.Count) continue;                  // a rebuild mid-frame: leave the pose alone
                float deg = Solve(l, (float)wheels[l.Gear].LegStrokeM - l.DrawnStrokeM);
                if (!float.IsNaN(l.Applied) && Mathf.Abs(deg - l.Applied) < DeadBandDeg) continue;
                l.Pivot.localRotation = Quaternion.AngleAxis(deg, l.Axis) * l.RestRot;
                l.Applied = deg;
                moved = true;
            }
            if (moved) FollowStruts();
        }

        /// <summary>
        /// Hang every driven leg free — the pose its own model was exported in, which is what every unrigged type in
        /// the fleet is drawn at and what these two were drawn at until now.
        ///
        /// This is what a REPLAY gets, and why: a replay does not step the sim (the world is frozen at
        /// <c>Time.timeScale</c> 0 and the live flight is parked), and <c>Aircraft.SetReplayPose</c> writes the pose,
        /// the deflections, the throttle, the flap, the gear EXTENSION and the load factor — not the wheels. Nor is
        /// there anything to write: <c>FlightTrack.TrackSample</c> and its <c>PackedFrame</c> carry one byte of gear
        /// extension and no per-leg compression at all, so a recording simply does not know how far each leg was
        /// squashed. Read live during a playback, <see cref="WheelState.LegStrokeM"/> is whatever it happened to be
        /// when the replay was entered — frozen, and wrong as soon as the moment being watched is a different one.
        /// A replay therefore looks exactly as it did before any of this, rather than carrying a stale number: the
        /// same choice, for the same reason, that the propeller's blade damage had to be given a timeline for
        /// (Ty 2026-11-10: "Replay shows damaged propeller that is not turning and bent wings prior to the crash").
        /// Recording it is a field in PackedFrame and a timeline to go with it — Ty's call, not this lane's.
        /// </summary>
        public void Rest()
        {
            bool moved = false;
            foreach (Leg l in _legs)
            {
                if (l.Pivot == null) continue;
                if (!float.IsNaN(l.Applied) && Mathf.Abs(l.Applied) < DeadBandDeg) continue;
                l.Pivot.localRotation = l.RestRot;
                l.Applied = 0f;
                moved = true;
            }
            if (moved) FollowStruts();
        }

        /// <summary>Put every drawn strut back between its pins: a shortest-arc rotation from the direction it was
        /// drawn at onto the direction its two pins stand at now, about its own origin — so its localPosition, which
        /// is its pin, is never touched. Only the ORIGINS of the far nodes are read, so no strut depends on another
        /// strut having been done first.</summary>
        private void FollowStruts()
        {
            foreach (Strut s in _struts)
                if (PinDir(s, out Vector3 dir))
                    s.T.localRotation = Quaternion.FromToRotation(s.RestDir, dir) * s.RestRot;
        }

        /// <summary>The angle last written to each driven leg, for a check or a report (degrees, + = as the model's own
        /// hinge sense has it; <see cref="Leg.Sign"/> is already in it).</summary>
        public IEnumerable<(int Gear, float Deg)> Applied()
        {
            foreach (Leg l in _legs) yield return (l.Gear, float.IsNaN(l.Applied) ? 0f : l.Applied);
        }
    }
}
