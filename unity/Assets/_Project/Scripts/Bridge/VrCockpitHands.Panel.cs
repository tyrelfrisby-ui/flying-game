using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Vr;
using FlyingGame.Sim;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// PHASE 3: THE SWITCHES AND KNOBS (Pitts, Extra, Great Lakes), one data-driven system over Codex's `controls`
    /// table (docs/models/vr-cockpit-phase3a.md): the seat's own 22 <c>Ctl_*</c> nodes, each with its <c>Grip_</c> and
    /// <c>Poke_</c> empties. A toggle is flicked by a poke or grabbed and moved; a rotary is grabbed and TWISTED; a
    /// push-pull is grabbed and pulled (a momentary one also poked). The logic is Core's <see cref="PanelControl"/>.
    ///
    /// INTO THE SIM where the sim has the system, through the same state everything else uses: the trim knob is
    /// <see cref="TouchFlightControls.PitchTrimLever"/>, the smoke switch is <see cref="SmokeController"/>, the J-3 floats' WATER RUDDER handle is
    /// <see cref="CockpitWaterRudder"/> (held: the blade follows the hand; let go near the hook it hangs there, else the
    /// spring takes it down), the PROP lever is <see cref="Aircraft.PropLever01"/> (the governor's rpm), and MASTER,
    /// ALTERNATOR and FUEL PUMP are <see cref="Aircraft.Electrics"/> (Ty 2026-11-04: the Pitts' four rear switches).
    /// Because they live in the sim, the other seat's lever and switches follow. Everything else (magnetos, lights,
    /// mixture, alternate air / carb heat, fuel selector, radio, transponder) has no system in this sim yet: it moves,
    /// clicks, stays where it is left and says so in the log (`[VrCockpit] … visual only`). Drawn by this class after
    /// the rig, so the knob shows the hand's value.
    /// </summary>
    public sealed partial class VrCockpitHands
    {
        private sealed class PanelSlot
        {
            public PanelControl C;
            public RigHandle H;
            public Transform Grip, Poke;
            public HingedLever Lever;
            public SlideKnob Slide;
            public Grabbable Id;
        }

        private readonly List<PanelSlot> _panel = new();
        private SmokeController _smoke;
        private static readonly HashSet<string> SimParams = new() { "trim", "smoke", "water_rudder_up", "prop", "master", "alternator", "fuel_pump" };

        private void BindPanel(string seatFull)
        {
            _panel.Clear();
            ModelRig table = _visual?.Rig?.Table;
            if (table == null || table.Controls.Count == 0 || seatFull == null) return;
            _smoke = _air.GetComponent<SmokeController>();
            RigInputs now = RigInputs.Parked();   // the ramp: mags BOTH, master on, rich, fine pitch, fuel on, xpdr SBY
            int toggles = 0, rotaries = 0, pulls = 0, levers = 0, unbound = 0;
            var missing = new List<string>();
            foreach (PanelControlSpec spec in SeatControls(table, seatFull))
            {
                if (_panel.Count > Grabbable.PanelLast - Grabbable.PanelFirst) break;
                Transform grip = Node(spec.Grip), poke = Node(spec.Poke);
                RigHandle h = RigHandle.Find(_visual.Rig, spec.Node, grip);
                if (h == null) { unbound++; missing.Add(spec.Node); continue; }
                if (grip == null) missing.Add(spec.Grip);
                double travel = System.Math.Abs(h.V1 - h.V0);
                var slot = new PanelSlot
                {
                    C = new PanelControl(spec, Initial(spec, now), spec.Kind == PanelKind.Rotary ? travel : 0.0),
                    H = h, Grip = grip, Poke = poke,
                    Id = (Grabbable)((int)Grabbable.PanelFirst + _panel.Count),
                };
                if (spec.Kind == PanelKind.Toggle || spec.Kind == PanelKind.Lever)
                {
                    slot.Lever = new HingedLever(C(Local(h.PivotWorld)), C(Local(h.KnobWorld(h.V0))), C(Local(h.KnobWorld(h.V1))),
                                                 C(Quaternion.Inverse(_air.rotation) * h.HingeWorld));
                    if (spec.Kind == PanelKind.Toggle) toggles++; else levers++;
                }
                else if (spec.Kind == PanelKind.PushPull)
                {
                    slot.Slide = new SlideKnob(C(Local(h.KnobWorld(h.V0))), C(Local(h.KnobWorld(h.V1))));
                    pulls++;
                }
                else rotaries++;
                _panel.Add(slot);
            }
            Debug.Log($"[VrCockpit] {_driver.AircraftId} {seatFull} panel: {_panel.Count} controls ({toggles} toggles, {rotaries} knobs, {pulls} push-pull, {levers} levers)" +
                      (unbound > 0 || missing.Count > 0 ? $"; MISSING {string.Join(", ", missing)}" : "") +
                      "; into the sim: trim, smoke, water rudder, prop, master, alternator, fuel pump; the rest visual only (no system in the sim yet)");
        }

        /// <summary>The seat's rows of the table, and a PROP lever the rig draws but the table does not list: the Pitts'
        /// front seat has a prop lever beside its throttle and no panel controls at all (Ty 2026-11-04), so its
        /// <c>PropLever_Front</c> rig node becomes a <see cref="PanelKind.Lever"/> here, worked by the same hand code.</summary>
        private IEnumerable<PanelControlSpec> SeatControls(ModelRig table, string seatFull)
        {
            bool prop = false;
            foreach (PanelControlSpec spec in table.Controls)
            {
                if (spec.Seat != seatFull) continue;
                prop |= spec.Param == "prop";
                yield return spec;
            }
            string node = "PropLever_" + seatFull;
            if (prop || _visual.Rig.Find(node) == null) yield break;
            yield return new PanelControlSpec
            {
                Node = node, Param = "prop", Seat = seatFull, Kind = PanelKind.Lever, Lo = 0.0, Hi = 1.0,
                Grip = Node("Grip_" + node) != null ? "Grip_" + node : "",
            };
        }

        /// <summary>Where a control starts: the sim's own value for the trim and the smoke, the parked cockpit
        /// (<see cref="RigInputs.Parked"/>) for anything the rig inputs carry, and otherwise off, the fuel valve on.</summary>
        private double Initial(PanelControlSpec spec, in RigInputs now)
        {
            switch (spec.Param)
            {
                case "trim": return _touch != null ? _touch.PitchTrimLever : 0.0;
                case "smoke": return _smoke != null && _smoke.On ? 1.0 : 0.0;
                case "water_rudder_up": return _driver.Sim.Aircraft.WaterRudderUp01;
                case "prop": case "master": case "alternator": case "fuel_pump": return FromSim(spec.Param);
                case "fuel_valve": return 1.0;
                case "com_volume": return 0.5;
                case "xpdr_squawk": return spec.Short switch { "XpdrDigit1" => 1, "XpdrDigit2" => 2, _ => 0 };   // 1200
            }
            return now.TryGet(spec.Param, out double v) ? v : spec.Lo;
        }

        private void UpdatePanelPoints()
        {
            foreach (PanelSlot p in _panel)
            {
                if (!p.C.Held)
                {
                    if (p.C.Spec.Param == "trim" && _touch != null) p.C.SetFromSim(_touch.PitchTrimLever);
                    else if (p.C.Spec.Param == "smoke" && _smoke != null) p.C.SetFromSim(_smoke.On ? 1.0 : 0.0);
                    else if (p.C.Spec.Param == "water_rudder_up") p.C.SetFromSim(_driver.Sim.Aircraft.WaterRudderUp01);
                    else if (SimParams.Contains(p.C.Spec.Param) && p.C.Spec.Param != "trim" && p.C.Spec.Param != "smoke")
                        p.C.SetFromSim(FromSim(p.C.Spec.Param));
                }
                _grab.Set(p.Id, C(PanelPoint(p)), true);
            }
        }

        /// <summary>The sim's own value for a lever or switch that lives on the aeroplane (the other seat may have moved it).</summary>
        private double FromSim(string param)
        {
            Aircraft ac = _driver.Sim.Aircraft;
            switch (param)
            {
                case "prop": return ac.PropLever01;
                case "master": return ac.Electrics.Master ? 1.0 : 0.0;
                case "alternator": return ac.Electrics.Alternator ? 1.0 : 0.0;
                case "fuel_pump": return ac.Electrics.FuelPump ? 1.0 : 0.0;
                case "water_rudder_up": return ac.WaterRudderUp01;
                default: return 0.0;
            }
        }

        /// <summary>The control's grab point this frame: its Grip_ empty (it rides the node as drawn), else the knob.</summary>
        private Vector3 PanelPoint(PanelSlot p) =>
            p.Grip != null ? Local(p.Grip.position) : Local(p.H.KnobWorld(p.H.Node.Value(p.C.Value)));

        private PanelSlot Slot(Grabbable g)
        {
            int i = (int)g - (int)Grabbable.PanelFirst;
            return i >= 0 && i < _panel.Count ? _panel[i] : null;
        }

        private bool GrabPanel(HandSide side, Grabbable g, Vec3 hand)
        {
            PanelSlot p = Slot(g);
            if (p == null) return false;
            p.C.Grab(_hands[(int)side].Rotation);
            if (p.Lever != null) p.Lever.Grab(hand, p.C.T);
            if (p.Slide != null) p.Slide.Grab(hand, p.C.T);
            if (p.C.Spec.Param == "water_rudder_up") CockpitWaterRudder.Hold(_driver.Sim.Aircraft, p.C.Value);
            return true;
        }

        private void DrivePanel()
        {
            for (int i = 0; i < _panel.Count; i++)
            {
                PanelSlot p = _panel[i];
                PanelEvent ev;
                if (_grab.IsHeldBy(p.Id, out HandSide side))
                {
                    HandPose h = _hands[(int)side];
                    ev = p.Lever != null ? p.C.FollowTravel(p.Lever.Follow(h.Position))
                       : p.Slide != null ? p.C.FollowTravel(p.Slide.Follow(h.Position))
                       : p.C.FollowTwist(h.Rotation, C(Quaternion.Inverse(_air.rotation) * p.H.HingeWorld));
                    Feel(side, ev);
                }
                else
                {
                    bool was = p.C.Held;
                    ev = was ? p.C.Release() : PanelEvent.None;
                    if (was && p.C.Spec.Param == "water_rudder_up") ReleaseWaterRudder(p);
                    // A poke: the fingertip (a tracked index tip, or the controller) at the Poke_ point.
                    Vector3 at = p.Poke != null ? Local(p.Poke.position) : PanelPoint(p);
                    double d = System.Math.Min(Fingertip(0, at), Fingertip(1, at));
                    PanelEvent pe = p.C.Poke(d);
                    if (pe != PanelEvent.None) Feel(d == Fingertip(0, at) ? HandSide.Left : HandSide.Right, pe);
                    ev |= pe;
                }
                if ((ev & PanelEvent.Changed) != 0) Apply(p);
            }
        }

        /// <summary>How far a POINTING index fingertip is from <paramref name="at"/>; ∞ for anything else. Only a tracked
        /// hand pokes, and only with the index straight-ish (curl under 0.35) and thumb and index apart (over 4 cm):
        /// reaching in to pinch a switch must not flick it on the way. A controller takes switches by the grip.</summary>
        private double Fingertip(int s, Vector3 at)
        {
            if (!_hasJoints[s] || !_hands[s].Tracked || _det[s].Closed) return double.PositiveInfinity;
            if (_indexCurl[s] > PokeMaxIndexCurl || _gapM[s] < PokeMinGapM) return double.PositiveInfinity;
            return (_jointsLocal[s][HandJoint.IndexTip] - at).magnitude;
        }

        private const double PokeMaxIndexCurl = 0.35, PokeMinGapM = 0.04;

        private void Feel(HandSide side, PanelEvent ev)
        {
            if (ev == PanelEvent.None || _hands[(int)side].Source != HandSource.Controller) return;
            bool left = side == HandSide.Left;
            if ((ev & PanelEvent.Pressed) != 0) XrHandInput.Tick(left, 0.5f, 0.03f);
            else if ((ev & PanelEvent.Stop) != 0) XrHandInput.Tick(left, 0.6f, 0.03f);
            else if ((ev & PanelEvent.Detent) != 0) XrHandInput.Tick(left, 0.35f, 0.02f);
        }

        /// <summary>A control changed: into the sim where there is a system, and one log line either way.</summary>
        private void Apply(PanelSlot p)
        {
            PanelControlSpec s = p.C.Spec;
            double v = p.C.Value;
            switch (s.Param)
            {
                case "trim":
                    if (_touch != null) _touch.PitchTrimLever = (float)v;
                    return;   // continuous: no log line per click
                case "smoke":
                    if (_smoke != null && _smoke.On != v > 0.5) _smoke.Toggle();
                    Debug.Log($"[VrCockpit] smoke switch {(v > 0.5 ? "ON" : "OFF")} by hand → SmokeController {(_smoke != null ? (_smoke.On ? "on" : "off") : "MISSING")}");
                    return;
                case "water_rudder_up":
                    if (p.C.Held) CockpitWaterRudder.Hold(_driver.Sim.Aircraft, v);
                    return;   // continuous while held; the one log line is on the release
                case "prop":
                    _driver.Sim.Aircraft.PropLever01 = v;
                    return;   // continuous: no log line per click
                case "master": case "alternator": case "fuel_pump":
                {
                    FlyingGame.Core.Engine.ElectricalSystem el = _driver.Sim.Aircraft.Electrics;
                    bool on = v > 0.5;
                    if (s.Param == "master") el.Master = on;
                    else if (s.Param == "alternator") el.Alternator = on;
                    else el.FuelPump = on;
                    Debug.Log($"[VrCockpit] {s.Short} {(on ? "ON" : "OFF")} by hand → bus {el.BusVolts:0.0} V, {el.Amps:+0.0;-0.0} A");
                    return;
                }
                case "com_volume":
                    return;
            }
            string what = s.Param == "xpdr_squawk" ? $"squawk {Squawk()}" : $"{v:0.###}";
            Debug.Log($"[VrCockpit] {s.Short} → {what} by hand (visual only: the sim has no {s.Param} system yet)");
        }

        /// <summary>The hand opens on the WATER RUDDER handle: near the hook it hangs there (blade up), anywhere else
        /// the spring takes it back to the floor (blade down). The knob then follows the sim's travel.</summary>
        private void ReleaseWaterRudder(PanelSlot p)
        {
            Aircraft ac = _driver.Sim.Aircraft;
            double at = p.C.Value;
            CockpitWaterRudder.Release(ac);
            Debug.Log($"[WaterRudder] handle let go by hand at {at:0.00} of the travel → " +
                      (ac.HasWaterRudderRetract ? (ac.WaterRudderHooked ? "ON THE HOOK (blade up)" : "spring takes it DOWN (blade in the water)") : "no retract on this airframe"));
        }

        /// <summary>The four transponder digit knobs as one code (thousands, hundreds, tens, units).</summary>
        private string Squawk()
        {
            int[] d = new int[4];
            foreach (PanelSlot p in _panel)
            {
                string n = p.C.Spec.Short;
                if (n.StartsWith("XpdrDigit") && n.Length == 10 && n[9] >= '1' && n[9] <= '4') d[n[9] - '1'] = (int)System.Math.Round(p.C.Value);
            }
            return $"{d[0]}{d[1]}{d[2]}{d[3]}";
        }

        /// <summary>After the rig (execution order 10001): each panel control drawn at its own value, so a knob shows
        /// what the hand set even where the rig's param cannot (four transponder digits share one `xpdr_squawk`).</summary>
        private void DrawPanel()
        {
            if (!Active) return;
            foreach (PanelSlot p in _panel)
                if (p.H.T != null) p.H.Pose(p.H.Node.Value(p.C.Value));
        }

        private Vector3 HeldPanelPoint(Grabbable g)
        {
            PanelSlot p = Slot(g);
            return p != null ? PanelPoint(p) : _eye;
        }
    }
}
