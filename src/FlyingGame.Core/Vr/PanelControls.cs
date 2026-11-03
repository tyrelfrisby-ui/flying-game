using System;
using System.Collections.Generic;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Vr
{
    public enum PanelKind { Toggle, Rotary, PushPull }

    /// <summary>
    /// ONE COCKPIT SWITCH OR KNOB as Codex's rig table describes it (`controls` in docs/models/*-own-rig.json,
    /// docs/models/vr-cockpit-phase3a.md): the moving node <c>Ctl_&lt;Kind&gt;_&lt;Name&gt;[_Front]</c>, its param, its ends
    /// in the sim's units, its detents, an optional spring return, and the <c>Grip_</c> / <c>Poke_</c> empties a hand
    /// uses. Engine-free; the Bridge parses the JSON into this.
    /// </summary>
    public sealed class PanelControlSpec
    {
        public string Node = "", Param = "", Seat = "Rear", Semantics = "absolute", Grip = "", Poke = "";
        public PanelKind Kind;
        public double Lo, Hi = 1.0;
        public double[] Detents = Array.Empty<double>();
        /// <summary>Let go at <see cref="SpringFrom"/>, it goes to <see cref="SpringTo"/> (the magneto's START → BOTH, a
        /// momentary push → out). NaN = latched.</summary>
        public double SpringFrom = double.NaN, SpringTo = double.NaN;

        public bool Momentary => !double.IsNaN(SpringFrom);
        /// <summary>The control's name without kind or seat: "Magneto", "Smoke".</summary>
        public string Short
        {
            get
            {
                string n = Node.StartsWith("Ctl_", StringComparison.Ordinal) ? Node.Substring(4) : Node;
                int u = n.IndexOf('_');
                if (u >= 0) n = n.Substring(u + 1);
                return n.EndsWith("_Front", StringComparison.Ordinal) ? n.Substring(0, n.Length - 6) : n;
            }
        }
    }

    /// <summary>What a control did this frame, for haptics and the log.</summary>
    [Flags]
    public enum PanelEvent { None = 0, Detent = 1, Stop = 2, Changed = 4, Pressed = 8 }

    /// <summary>
    /// A SWITCH OR KNOB UNDER A HAND. The value is in the sim's own units (a toggle 0 / 1, the magneto 0 OFF .. 4
    /// START, a push-pull 0 .. 1), always one of the detents where the control has them.
    ///
    ///   Toggle    grab (pinch or fist) and move the bat: it follows on its arc (<see cref="FollowTravel"/>) and flips
    ///             as it passes half way; or POKE it (<see cref="Poke"/>): a fingertip coming within
    ///             <see cref="PokeInM"/> (1.5 cm) of its Poke point flips it once, and it re-arms when the finger has
    ///             gone <see cref="PokeOutM"/> (3 cm) away. A finger resting on it does nothing more.
    ///   Rotary    grab and TWIST: the knob turns by the hand's own roll about the knob's shaft since the grab
    ///             (swing-twist: twist = 2·atan2(q⃗·â, q_w), the same sense the rig turns it by), over the knob's full
    ///             travel; with detents it clicks from one to the next.
    ///   PushPull  grab and pull along its shaft (<see cref="FollowTravel"/>); a momentary one (COM swap, IDENT) is also
    ///             pushed by a poke and springs back out when the finger leaves.
    ///
    /// Let go: a latched control stays at the nearest detent; a spring-loaded one goes from its spring position to
    /// where it springs to (START → BOTH).
    /// </summary>
    public sealed class PanelControl
    {
        public const double PokeInM = 0.015, PokeOutM = 0.030;

        public readonly PanelControlSpec Spec;
        public double Value { get; private set; }
        public bool Held { get; private set; }

        private double _grabValue;
        private Quat _grabRot;
        private double _travelDeg;
        private bool _pokeArmed = true, _poking;

        public PanelControl(PanelControlSpec spec, double initial, double rotaryTravelDeg = 0.0)
        {
            Spec = spec;
            _travelDeg = rotaryTravelDeg;
            Value = Snap(Math.Clamp(initial, Math.Min(spec.Lo, spec.Hi), Math.Max(spec.Lo, spec.Hi)));
        }

        /// <summary>0..1 along the travel, Lo → Hi.</summary>
        public double T => Math.Abs(Spec.Hi - Spec.Lo) < 1e-12 ? 0.0 : (Value - Spec.Lo) / (Spec.Hi - Spec.Lo);
        public double ValueAt(double t) => Spec.Lo + (Spec.Hi - Spec.Lo) * Math.Clamp(t, 0.0, 1.0);

        /// <summary>The nearest detent (unchanged where there are none).</summary>
        public double Snap(double v)
        {
            double[] d = Spec.Detents;
            if (d == null || d.Length == 0) return v;
            double best = d[0], bd = Math.Abs(v - d[0]);
            for (int i = 1; i < d.Length; i++) { double e = Math.Abs(v - d[i]); if (e < bd) { bd = e; best = d[i]; } }
            return best;
        }

        /// <summary>The sim (or a reset) moved it while no hand has it: the smoke switch follows the smoke, the trim
        /// knob the trim.</summary>
        public void SetFromSim(double v)
        {
            if (Held) return;
            Value = Snap(Math.Clamp(v, Math.Min(Spec.Lo, Spec.Hi), Math.Max(Spec.Lo, Spec.Hi)));
        }

        public void Grab(Quat handRotation)
        {
            Held = true;
            _grabValue = Value;
            _grabRot = handRotation;
        }

        /// <summary>A toggle's bat or a push-pull's shaft under the hand: <paramref name="t"/> 0..1 along its travel, from
        /// the lever / slide geometry. A toggle flips as it passes half way; detents snap.</summary>
        public PanelEvent FollowTravel(double t)
        {
            double v = ValueAt(t);
            if (Spec.Kind == PanelKind.Toggle) v = t >= 0.5 ? Spec.Hi : Spec.Lo;
            return Set(Snap(v));
        }

        /// <summary>A rotary under a twisting hand: the hand's roll about <paramref name="shaftAxis"/> since the grab, over
        /// the knob's travel. Unclamped twist past a stop stays at the stop.</summary>
        public PanelEvent FollowTwist(Quat handRotation, Vec3 shaftAxis)
        {
            double twistDeg = Twist(Quat.Multiply(handRotation, _grabRot.Conjugate()), shaftAxis);
            double span = Spec.Hi - Spec.Lo;
            double v = _travelDeg > 1e-6 ? _grabValue + twistDeg / _travelDeg * span : _grabValue;
            v = Math.Clamp(v, Math.Min(Spec.Lo, Spec.Hi), Math.Max(Spec.Lo, Spec.Hi));
            return Set(Snap(v));
        }

        /// <summary>Let go: a spring-loaded position springs; the rest stay in their detent.</summary>
        public PanelEvent Release()
        {
            Held = false;
            _pokeArmed = false;   // the fingers that just let go are still on it: no poke until they have left
            if (Spec.Momentary && Math.Abs(Value - Spec.SpringFrom) < 1e-9) return Set(Spec.SpringTo);
            return PanelEvent.None;
        }

        /// <summary>
        /// A fingertip <paramref name="distM"/> from the control's Poke point, this frame (∞ when no finger). A toggle
        /// flips once per poke; a momentary push-button is IN while the finger is in and springs out after.
        /// </summary>
        public PanelEvent Poke(double distM)
        {
            if (Held) return PanelEvent.None;
            bool inside = distM <= PokeInM;
            if (distM >= PokeOutM) _pokeArmed = true;
            PanelEvent ev = PanelEvent.None;
            if (Spec.Kind == PanelKind.Toggle)
            {
                if (inside && _pokeArmed)
                {
                    _pokeArmed = false;
                    ev = Set(Math.Abs(Value - Spec.Hi) < 1e-9 ? Spec.Lo : Spec.Hi);
                }
                return ev;
            }
            if (Spec.Kind == PanelKind.PushPull && Spec.Momentary)
            {
                if (inside && _pokeArmed && !_poking) { _poking = true; _pokeArmed = false; ev = Set(Spec.SpringFrom) | PanelEvent.Pressed; }
                else if (_poking && distM >= PokeOutM) { _poking = false; ev = Set(Spec.SpringTo); }
            }
            return ev;
        }

        private PanelEvent Set(double v)
        {
            if (Math.Abs(v - Value) < 1e-12) return PanelEvent.None;
            double lo = Math.Min(Spec.Lo, Spec.Hi), hi = Math.Max(Spec.Lo, Spec.Hi);
            PanelEvent ev = PanelEvent.Changed;
            if (Spec.Detents.Length > 0) ev |= PanelEvent.Detent;
            if ((v <= lo + 1e-9 || v >= hi - 1e-9) && Spec.Detents.Length == 0) ev |= PanelEvent.Stop;
            if (Spec.Momentary && Math.Abs(v - Spec.SpringFrom) < 1e-9 && Spec.Kind == PanelKind.PushPull) ev |= PanelEvent.Pressed;
            Value = v;
            return ev;
        }

        /// <summary>The twist of a rotation about an axis, degrees, in the sense the rotation itself turns (swing-twist
        /// decomposition: 2·atan2(q⃗·â, w)), wrapped to −180..180.</summary>
        public static double Twist(Quat q, Vec3 axis)
        {
            double l = axis.Length;
            if (l < 1e-12) return 0.0;
            double p = (q.X * axis.X + q.Y * axis.Y + q.Z * axis.Z) / l;
            double a = 2.0 * Math.Atan2(p, q.W) * 180.0 / Math.PI;
            while (a > 180.0) a -= 360.0;
            while (a < -180.0) a += 360.0;
            return a;
        }
    }

    /// <summary>
    /// A RIG TABLE'S ROWS, ONCE EACH. Codex's ea300-own-rig.json carries 414 rows for 126 nodes (and the Great Lakes'
    /// 347 for 91): the same Ctl_* row repeated up to ten times. Bound as they stand, every duplicate is a second
    /// writer of the same transform every frame. The first row of each node is kept; the count of the rest, and of
    /// any that differ from the one kept, is returned for the log. The file itself is Codex's and is not edited.
    /// </summary>
    public static class RigRows
    {
        public static List<T> FirstOfEach<T>(IReadOnlyList<T> rows, Func<T, string> key, Func<T, T, bool> same,
                                             out int dropped, out List<string> differing)
        {
            var seen = new Dictionary<string, T>();
            var kept = new List<T>(rows.Count);
            differing = new List<string>();
            dropped = 0;
            foreach (T r in rows)
            {
                string k = key(r) ?? "";
                if (seen.TryGetValue(k, out T first))
                {
                    dropped++;
                    if (!same(first, r) && !differing.Contains(k)) differing.Add(k);
                    continue;
                }
                seen[k] = r;
                kept.Add(r);
            }
            return kept;
        }
    }
}
