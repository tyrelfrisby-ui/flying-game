using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using UnityEngine;

namespace FlyingGame.Bridge.Widget
{
    /// <summary>
    /// THE "STICK AND RUDDER" CURRICULUM (protocol 8, owner 2026-10-09): every concept Langewiesche teaches, shown accurately
    /// and live, plus advanced spin physics — in our own words (no text from the book). Each lesson is a one-tap SCENE
    /// ({"cmd":"lesson","id"}): it sets up the aircraft, start state, wind, loading, view, display, vectors and insets, then
    /// runs a short scripted DEMONSTRATION in phases (an α hold, a bank hold, the autopilot…), writing what it measured on a
    /// caption line ("1 G: stalled at α 16.0°, 52 KIAS"). Review steps through it like anything else. Any pilot input ends the
    /// script (the scene stays). docs/STICK-AND-RUDDER.md is the coverage table.
    /// </summary>
    public sealed class WidgetLessons
    {
        private readonly AeroWidget _w;
        public WidgetLessons(AeroWidget w) { _w = w; }

        public sealed class Lesson
        {
            public string Id, Name, Concept;
            public System.Action<WidgetLessons> Setup;
            public List<Phase> Phases = new();
        }
        public sealed class Phase
        {
            public string Label; public float Seconds; public System.Action<WidgetLessons, float> Tick; public System.Func<WidgetLessons, bool> Done;
            public System.Action<WidgetLessons> Enter, Exit;
        }

        public Lesson Current { get; private set; }
        public int PhaseIdx { get; private set; } = -1;
        public float PhaseT { get; private set; }
        public bool Driving => Current != null && PhaseIdx >= 0 && PhaseIdx < Current.Phases.Count;
        public string Caption => Driving ? Current.Phases[PhaseIdx].Label : Current != null ? "DEMO COMPLETE" : null;
        public readonly List<string> Results = new();

        // ---- the scenes ----
        private static readonly List<Lesson> _all = new();
        public static IReadOnlyList<Lesson> All { get { if (_all.Count == 0) Build(); return _all; } }

        private AeroWidget W => _w;
        private WidgetControls C => _w.Controls;
        private AeroWidget.Readout R => _w.Read();

        // controller pieces
        private double _eI, _aI, _rI;
        /// <summary>Hold an angle of attack through the autopilot's pitch channel ("aoa" mode: slow servo, trim, pitch damping;
        /// the target ramps from the present α) — the ailerons stay the lesson's.</summary>
        public void HoldAlpha(double target, float dt)
        {
            var ap = _w.Autopilot;
            if (!ap.On || !ap.PitchOnly || ap.PitchMode != "aoa") { ap.Engage(null, null, "alt", null, null, null, false, false); ap.PitchOnly = true; ap.PitchMode = "aoa"; ap.AoaCmd = R.AlphaDeg; }
            ap.AoaTarget = target; ap.AutoThrottle = false; ap.AllowStall = true; ap.YawDamper = false;
            _levelUsed = true;
        }
        public void HoldBank(double target, float dt, bool coordinate = true)
        {
            var r = R; var s = _w.Ac.State;
            C.Aileron = System.Math.Clamp((target - r.RollDeg) * 0.025 - s.Rates.X * 0.25, -1, 1);
            if (coordinate) { double b = r.BetaDeg / 57.3; _rI = System.Math.Clamp(_rI + 1.2 * b * dt, -0.7, 0.7); C.Rudder = System.Math.Clamp(_rI + 2.0 * b - 0.2 * s.Rates.Z, -1, 1); }
        }
        /// <summary>Level flight through the AUTOPILOT's pitch channel (a tested, realistic loop): altitude held, power left
        /// to the script, ailerons left to the script; for the stall demos it may fly into the stall.</summary>
        public void HoldLevel(double altFt, float dt, bool allowStall = false)
        {
            var ap = _w.Autopilot;
            if (!ap.On || !ap.PitchOnly || ap.PitchMode == "aoa") { ap.Engage(altFt, null, "alt", null, null, null, false, false); ap.PitchOnly = true; ap.PitchMode = "alt"; }
            ap.AltFt = altFt; ap.AutoThrottle = false; ap.AllowStall = allowStall; ap.YawDamper = false;
            _levelUsed = true;
        }
        private bool _levelUsed;
        public void Seed() { _eI = C.Elevator; _aI = C.Aileron; _rI = C.Rudder; }
        public double Alt0;

        private static Phase P(string label, float s, System.Action<WidgetLessons, float> tick, System.Func<WidgetLessons, bool> done = null, System.Action<WidgetLessons> enter = null)
            => new() { Label = label, Seconds = s, Tick = tick, Done = done, Enter = enter };

        private static void Add(string id, string name, string concept, System.Action<WidgetLessons> setup, params Phase[] phases)
            => _all.Add(new Lesson { Id = id, Name = name, Concept = concept, Setup = setup, Phases = new List<Phase>(phases) });

        // shared setups
        private static void Cruise(WidgetLessons L, string ac = "c172-like", string view = "side")
        {
            L.W.StartCondition("cruise", ac, view, view == "locked" ? "side" : null);
            L.Alt0 = L.R.HeightFt; L.Seed();
        }
        private static void Show(WidgetLessons L, params string[] on) { foreach (var k in on) L.W.Show[k] = true; }
        private static void Inset(WidgetLessons L, params string[] names) { L.W.Insets.Clear(); foreach (var n in names) L.W.Insets.Add(n); }

        private double _mark, _mark2, _a0; private bool _flag;
        private readonly List<(double kias, double nz)> _stalls = new();
        private void StallRatio()
        {
            if (_stalls.Count < 2) return;
            var a = _stalls[0]; var b = _stalls[_stalls.Count - 1];
            Results.Add($"speed ratio {b.kias / a.kias:0.00} vs √(n ratio) {System.Math.Sqrt(b.nz / System.Math.Max(0.1, a.nz)):0.00}");
        }
        private void Note(string s) { Results.Add(s); }

        private static void Build()
        {
            // 1 — AoA is the key
            Add("aoa-attitude", "Angle of attack, not attitude", "The wing answers to its angle of attack — the same α gives the same lift coefficient climbing, level or diving.",
                L => { Cruise(L); Show(L, "vectors"); Inset(L, "clAlpha", "aoa"); },
                P("CLIMBING at α 6° (full power)", 14, (L, dt) => { L.C.Throttle01 = 1; L.HoldAlpha(6, dt); L.HoldBank(0, dt); }, null, L => L._mark = 0),
                P("LEVEL-ISH at α 6° (half power)", 14, (L, dt) => { L.C.Throttle01 = 0.45; L.HoldAlpha(6, dt); L.HoldBank(0, dt); }),
                P("DESCENDING at α 6° (idle)", 14, (L, dt) => { L.C.Throttle01 = 0; L.HoldAlpha(6, dt); L.HoldBank(0, dt); }));
            // 2 — the elevator controls AoA
            Add("elevator-is-aoa", "The elevator sets the angle of attack", "Stick back raises α; the airplane settles at a lower speed. Each stick position is a trimmed α, and each α a speed.",
                L => { Cruise(L); Show(L, "vectors"); Inset(L, "aoa", "clAlpha"); },
                P("α 2° — fast", 15, (L, dt) => { L.C.Throttle01 = 0.65; L.HoldAlpha(2, dt); L.HoldBank(0, dt); }, null, L => L.Results.Clear()),
                P("α 5°", 15, (L, dt) => { L.C.Throttle01 = 0.65; L.HoldAlpha(5, dt); L.HoldBank(0, dt); }),
                P("α 9° — slow", 18, (L, dt) => { L.C.Throttle01 = 0.65; L.HoldAlpha(9, dt); L.HoldBank(0, dt); }));
            // 3 — the throttle controls climb
            Add("throttle-is-climb", "The throttle sets the climb", "At one angle of attack, power changes the flight path, not the speed: watch the velocity vector against the attitude.",
                L => { Cruise(L); Show(L, "vectors", "wind"); Inset(L, "aoa"); },
                P("α 5° · FULL POWER — climbing", 14, (L, dt) => { L.C.Throttle01 = 1; L.HoldAlpha(5, dt); L.HoldBank(0, dt); }),
                P("α 5° · 55 % — about level", 14, (L, dt) => { L.C.Throttle01 = 0.55; L.HoldAlpha(5, dt); L.HoldBank(0, dt); }),
                P("α 5° · IDLE — descending", 14, (L, dt) => { L.C.Throttle01 = 0; L.HoldAlpha(5, dt); L.HoldBank(0, dt); }));
            // 4 — stall at any speed
            Add("stall-any-speed", "The stall is an angle, not a speed", "The wing stalls at its critical angle of attack every time — slowly at 1 g, or fast in a steep turn where the stall speed rises with √(load factor).",
                L => { Cruise(L, "c172-like", "behind"); Show(L, "vectors"); Inset(L, "aoa", "clAlpha"); L.Results.Clear(); },
                P("1 G STALL — idle, holding altitude as the speed decays", 70, (L, dt) => { L.C.Throttle01 = 0; L.HoldLevel(L.Alt0, dt, true); L.HoldBank(0, dt); },
                    L => L.StallCheck("1 G")),
                P("RECOVER — release back pressure, power", 6, (L, dt) => { L.C.Throttle01 = 1; L.HoldAlpha(4, dt); L.HoldBank(0, dt); }),
                P("BUILD SPEED", 90, (L, dt) => { L.C.Throttle01 = 1; L.HoldLevel(L.Alt0 - 200, dt); L.HoldBank(0, dt); }, L => L.R.Kias >= 108),
                P("ACCELERATED STALL — 60° bank, steady pull", 25, (L, dt) => { L.C.Throttle01 = 1; L._mark += dt * 1.5; L.HoldAlpha(L._a0 + L._mark, dt); L.HoldBank(60, dt); },
                    L => L.StallCheck("60° TURN"), L => { L._mark = 0; L._a0 = L.R.AlphaDeg; L.Seed(); }),
                P("RECOVER", 6, (L, dt) => { L.C.Throttle01 = 1; L.HoldAlpha(3, dt); L.HoldBank(0, dt); }, null, L => L.StallRatio()));
            Add("accelerated-stall", "The accelerated stall", "Pulling hard in a turn reaches the critical angle at a much higher speed: stall speed × √n.",
                L => { Cruise(L, "c172-like", "behind"); Show(L, "vectors"); Inset(L, "aoa"); L.Results.Clear(); },
                P("60° BANK — steady pull", 25, (L, dt) => { L.C.Throttle01 = 1; L._mark += dt * 1.5; L.HoldAlpha(L._a0 + L._mark, dt); L.HoldBank(60, dt); }, L => L.StallCheck("60° TURN"), L => { L._mark = 0; L._a0 = L.R.AlphaDeg; L.Seed(); }),
                P("RECOVER", 6, (L, dt) => { L.C.Throttle01 = 1; L.HoldAlpha(3, dt); L.HoldBank(0, dt); }));
            // 5 — glide
            Add("stretch-the-glide", "You can't stretch a glide", "The glide angle is set by L/D; best glide is the α of max L/D. Pulling back past it steepens the glide.",
                L => { Cruise(L); L.C.Throttle01 = 0; Show(L, "vectors", "wind"); Inset(L, "liftDrag", "aoa"); L.Results.Clear(); },
                P("BEST GLIDE α", 25, (L, dt) => { L.C.Throttle01 = 0; L.HoldAlpha(L.BestGlideAlpha(), dt); L.HoldBank(0, dt); }, null, L => L.GlideStart()),
                P("TOO SLOW (α +5°)", 25, (L, dt) => { L.C.Throttle01 = 0; L.HoldAlpha(L.BestGlideAlpha() + 5, dt); L.HoldBank(0, dt); }, null, L => { L.GlideEnd("BEST"); L.GlideStart(); }),
                P("TOO FAST (α −2.5°)", 25, (L, dt) => { L.C.Throttle01 = 0; L.HoldAlpha(L.BestGlideAlpha() - 2.5, dt); L.HoldBank(0, dt); }, null, L => { L.GlideEnd("SLOW"); L.GlideStart(); }),
                P("RESULTS", 3, (L, dt) => { }, null, L => L.GlideEnd("FAST")));
            // 6 — back side
            Add("back-side", "The back side of the power curve", "Below the minimum-power speed it takes MORE power to fly slower. The autopilot holds altitude while the speed comes down.",
                L => { Cruise(L); Inset(L, "powerRequired", "aoa"); L.W.Autopilot.Engage(null, 100, at: true, yd: true); L.Results.Clear(); },
                P("AUTOPILOT: altitude held, speed coming down", 80, (L, dt) => { L.W.Autopilot.Kias = System.Math.Max(52, L.W.Autopilot.Kias - dt * 0.7); L.PowerMark(); }));
            // 7 — air mass
            Add("air-mass", "The airplane flies in the air", "The wind only carries the whole air mass: airspeed and the turn are the same; only the track over the ground drifts.",
                L => { Cruise(L, "c172-like", "top"); L.W.SetWind(270, 20); Show(L, "vectors"); L.W.Show["groundTrack"] = true; },
                P("A STEADY 30° TURN IN A 20 KT WIND", 50, (L, dt) => { L.HoldBank(30, dt); L.HoldLevel(L.Alt0, dt); L.C.Throttle01 = 0.75; }));
            // 8 — how it turns
            Add("how-it-turns", "How an airplane turns", "Bank tilts the lift; its horizontal part turns the airplane. The vertical part must still carry the weight, so the elevator adds back pressure — load factor and stall speed rise.",
                L => { Cruise(L, "c172-like", "behind"); Show(L, "vectors", "lift"); L.W.Show["liftComponents"] = true; Inset(L, "aoa", "ball"); },
                P("30° BANK, level", 16, (L, dt) => { L.HoldBank(30, dt); L.HoldLevel(L.Alt0, dt); L.C.Throttle01 = 0.85; }),
                P("45° BANK, level", 16, (L, dt) => { L.HoldBank(45, dt); L.HoldLevel(L.Alt0, dt); L.C.Throttle01 = 0.95; }),
                P("60° BANK, level", 18, (L, dt) => { L.HoldBank(60, dt); L.HoldLevel(L.Alt0, dt); L.C.Throttle01 = 1; }));
            // 9 — adverse yaw
            Add("adverse-yaw", "Adverse yaw — what the rudder is for", "Aileron drag yaws the nose AWAY from the turn; the rudder coordinates it. Watch the ball and each wing's drag.",
                L => { Cruise(L, "c172-like", "behind"); L.W.Autopilot.Engage(null, 70, at: true, yd: false); Show(L, "vectors"); L.W.Show["wingDrag"] = true; Inset(L, "ball"); },
                P("SLOWING TO 70 KT", 40, (L, dt) => { }, L => L.R.Kias < 73),
                P("AILERON ONLY — roll left, feet still", 2.0f, (L, dt) => { L.C.Aileron = -0.3; L.C.Rudder = 0; L.HoldLevel(L.Alt0, dt); L.YawSample(); }, null, L => { L.W.Autopilot.Off(); L.YawStart(); }),
                P("WINGS LEVEL", 6, (L, dt) => { L.HoldBank(0, dt); L.HoldLevel(L.Alt0, dt); }, null, L => L.YawEnd("AILERON ONLY")),
                P("AILERON + RUDDER — coordinated", 2.0f, (L, dt) => { L.C.Aileron = -0.3; L.C.Rudder = -0.25; L.HoldLevel(L.Alt0, dt); L.YawSample(); }, null, L => L.YawStart()),
                P("WINGS LEVEL", 6, (L, dt) => { L.HoldBank(0, dt); L.HoldLevel(L.Alt0, dt); }, null, L => L.YawEnd("COORDINATED")));
            // 10 — slips and skids
            Add("forward-slip", "The forward slip", "Crossed controls: wing down one way, opposite rudder. The airplane flies sideways to the air (β), drag soars, the descent steepens at the same speed.",
                L => { Cruise(L, "c172-like", "front"); L.C.Throttle01 = 0; Show(L, "vectors", "wind"); Inset(L, "ball"); },
                P("GLIDE, controls neutral", 10, (L, dt) => { L.HoldAlpha(6, dt); L.HoldBank(0, dt); L.C.Throttle01 = 0; }),
                P("FORWARD SLIP — left wing down, right rudder", 16, (L, dt) => { L.HoldAlpha(6, dt); L.C.Rudder = 0.8; L.HoldBank(-12, dt, false); L.C.Throttle01 = 0; }));
            Add("skid", "The skid", "Too much rudder in a turn: the nose slews inside, the ball goes outside, the inside wing slows.",
                L => { Cruise(L, "c172-like", "behind"); Show(L, "vectors"); Inset(L, "ball"); },
                P("COORDINATED 20° TURN", 10, (L, dt) => { L.HoldBank(-20, dt); L.HoldLevel(L.Alt0, dt); }),
                P("SKID — extra left rudder", 12, (L, dt) => { L.HoldBank(-20, dt, false); L.C.Rudder = -0.6; L.HoldLevel(L.Alt0, dt); }));
            // 11 — base to final
            Add("base-to-final", "The skidding base-to-final turn", "Slow, banked, skidding with inside rudder and pulling: the inside wing flies slower at a higher α, stalls first and rolls the airplane INTO the turn — the classic stall-spin.",
                L => { Cruise(L, "c172-like", "behind"); L.W.Autopilot.Engage(null, 68, at: true); Show(L, "vectors"); Inset(L, "ball", "aoa"); L.Results.Clear(); },
                P("SLOWING TO 68 KT", 40, (L, dt) => { }, L => L.R.Kias < 71),
                P("BASE-TO-FINAL: 30° left bank, idle", 6, (L, dt) => { L.W.Autopilot.Off(); L.C.Throttle01 = 0; L.HoldBank(-30, dt); L.HoldLevel(L.Alt0, dt); }),
                P("OVERSHOOTING — more left rudder, pull", 12, (L, dt) => { L.C.Throttle01 = 0; L.C.Rudder = -0.9; L.HoldBank(-30, dt, false); L._mark += dt; L.HoldAlpha(6 + L._mark * 1.2, dt); },
                    L => L.WingStallCheck(), L => L._mark = 0),
                P("DEPARTURE — the inside wing stalled first", 3, (L, dt) => { }),
                P("RECOVER — PARE: opposite rudder, stick forward", 6, (L, dt) => { L.C.Throttle01 = 0; L.C.Aileron = 0; L.C.Rudder = L.R.YawRateDps < 0 ? 1 : -1; L.C.Elevator = 0.3; }, L => System.Math.Abs(L.R.YawRateDps) < 8 && L.PhaseT > 1.5f),
                P("ROTATION STOPPED — rudder neutral, ease out of the dive", 10, (L, dt) => { L.C.Throttle01 = 0; L.C.Rudder = 0; L.HoldBank(0, dt); L.HoldAlpha(4, dt); }, null, L => L.Seed()));
            // 12 — stability
            Add("longitudinal-stability", "Longitudinal stability", "With the CG ahead of the neutral point the airplane returns to its trimmed angle of attack after a disturbance; the tail's down-force is the lever.",
                L => { Cruise(L); Show(L, "vectors", "cgnp"); Inset(L, "aoa", "wb"); },
                P("TRIMMED, hands off", 6, (L, dt) => { }),
                P("A GUST: nose up for a second", 1.2f, (L, dt) => { L.C.Elevator = L.TrimStick - 0.4; }),
                P("HANDS OFF — it returns to its trimmed α", 25, (L, dt) => { L.C.Elevator = L.TrimStick; }));
            Add("weathervane", "Directional stability", "The fin weathervanes the airplane back into the relative wind after a yaw.",
                L => { Cruise(L, "c172-like", "top"); Show(L, "vectors", "wind"); Inset(L, "ball"); },
                P("A YAW KICK", 1.5f, (L, dt) => { L.C.Rudder = 0.7; }),
                P("FEET OFF — it swings back", 15, (L, dt) => { L.C.Rudder = L.TrimRud; }));
            Add("dihedral", "Lateral stability (dihedral)", "Sideslip makes the low wing meet the air at a higher angle and roll the wings back toward level.",
                L => { Cruise(L, "c172-like", "front"); Show(L, "vectors"); Inset(L, "ball"); },
                P("ROLL TO 15°, then let go", 2, (L, dt) => { L.HoldBank(15, dt, false); }),
                P("HANDS OFF — the slip rolls it back (or not)", 20, (L, dt) => { L.C.Aileron = L.TrimAil; L.C.Rudder = L.TrimRud; }));
            Add("spiral", "The spiral tendency", "Left alone in a bank, most airplanes slowly tighten into a descending spiral.",
                L => { Cruise(L, "c172-like", "behind"); Show(L, "vectors"); Inset(L, "ball", "aoa"); },
                P("BANK 20° AND LET GO", 3, (L, dt) => { L.HoldBank(20, dt); }),
                P("HANDS OFF", 40, (L, dt) => { L.C.Aileron = L.TrimAil; L.C.Rudder = L.TrimRud; L.C.Elevator = L.TrimStick; }));
            // 13 — trim
            Add("trim-is-speed", "Trim sets the speed", "Trim holds an angle of attack: re-trimmed nose-up, the hands-off airplane settles at a higher α and a lower speed.",
                L => { Cruise(L); Show(L, "vectors"); Inset(L, "aoa"); },
                P("TRIMMED FOR CRUISE", 10, (L, dt) => { L.C.Elevator = L.TrimStick; }),
                P("TRIM NOSE-UP — hands off", 30, (L, dt) => { L.C.Elevator = L.TrimStick - 0.12; }, null, L => L.Results.Add($"before: {L.R.Kias:0} KIAS")),
                P("RESULT", 2, (L, dt) => { L.C.Elevator = L.TrimStick - 0.12; }, null, L => L.Results.Add($"after: {L.R.Kias:0} KIAS, α {L.R.AlphaDeg:0.0}°")));
            // 14 — landing
            Add("aim-point", "The aim point", "On a steady glide the point you will reach does not move in the windshield; points beyond rise, points short sink.",
                L => { L.W.StartCondition("final", "c172-like", null, null); L.W.Show["aimPoint"] = true; Inset(L, "aoa"); }, P("STABILISED FINAL", 30, (L, dt) => { }));
            Add("flare", "The roundout and flare", "Near the ground the pilot trades the descent for a touchdown at low speed, nose up, in ground effect.",
                L => { L.W.StartCondition("final", "c172-like", null, null); Inset(L, "aoa"); L.W.Presets.Run("flare-demo"); }, P("THE FLARE", 40, (L, dt) => { }));
            Add("ground-effect", "Ground effect", "Within about a wingspan of the ground the downwash and induced drag fall: the airplane floats.",
                L => { L.W.StartCondition("final", "c172-like", null, null); Show(L, "vectors"); Inset(L, "liftDrag", "aoa"); L.W.Presets.Run("flare-demo"); }, P("FLOAT IN GROUND EFFECT", 40, (L, dt) => { }));
            // 15 — left-turning tendencies
            Add("left-turning", "Left-turning tendencies", "Full power, slow, high α: torque, P-factor, the spiral slipstream on the fin and gyroscopic precession all yaw a right-hand-prop airplane LEFT.",
                L => { Cruise(L, "c172-like", "behind"); Show(L, "vectors"); Inset(L, "ball"); L.W.Show["propEffects"] = true; },
                P("FULL POWER CLIMB, α 10°, rudder neutral", 18, (L, dt) => { L.C.Throttle01 = 1; L.HoldAlpha(10, dt); L.HoldBank(0, dt, false); L.C.Rudder = 0; }));
            // 16 — stalls and slow flight
            Add("power-off-stall", "The power-off stall", "Idle: the tail sits in the blanketed wake; as the wing stalls the downwash collapses and the nose drops (the pitch break).",
                L => { Cruise(L); Show(L, "vectors"); Inset(L, "aoa", "clAlpha"); L.Results.Clear(); },
                P("IDLE, holding altitude as the speed decays", 70, (L, dt) => { L.C.Throttle01 = 0; L.HoldLevel(L.Alt0, dt, true); L.HoldBank(0, dt); }, L => L.StallCheck("POWER-OFF")),
                P("THE BREAK — release back pressure", 6, (L, dt) => { L.C.Throttle01 = 1; L.HoldAlpha(4, dt); L.HoldBank(0, dt); }));
            Add("power-on-stall", "The power-on stall", "Full power: the slipstream keeps the tail working; the stall comes at a steep attitude and breaks harder, with a left yaw.",
                L => { Cruise(L); Show(L, "vectors"); Inset(L, "aoa", "ball"); L.Results.Clear(); },
                P("FULL POWER, raising α", 35, (L, dt) => { L.C.Throttle01 = 1; L._mark = System.Math.Min(L._mark + dt * 0.8, 22); L.HoldAlpha(5 + L._mark, dt); L.HoldBank(0, dt, false); }, L => L.StallCheck("POWER-ON"), L => L._mark = 0),
                P("RECOVER", 6, (L, dt) => { L.HoldAlpha(4, dt); L.HoldBank(0, dt); }));
            Add("slow-flight", "Slow flight", "Just above the stall: high α, high power, sluggish controls — the airplane on the back side, held level.",
                L => { Cruise(L); Show(L, "vectors"); Inset(L, "aoa", "powerRequired"); L.W.Autopilot.Engage(null, 52, at: true, yd: true); }, P("SLOW FLIGHT at 52 KIAS", 60, (L, dt) => { }));
            // 18 — spin basics + advanced
            Add("spin-basics", "Spin basics", "Stall + yaw = autorotation; the inside wing is deeper in the stall. Recovery: power idle, ailerons neutral, opposite rudder, stick forward.",
                L => { L.W.StartCondition("spin", "c172-like", null, null); Show(L, "vectors"); Inset(L, "aoa"); },
                P("DEVELOPED SPIN, pro-spin controls held", 8, (L, dt) => { }),
                P("RECOVERY — PARE", 10, (L, dt) => { }, null, L => { L.C.Hold = false; L.W.Presets.Run("spin-recovery"); }));
            Add("autorotation", "Autorotation", "Past the stall the roll damping reverses: the descending wing's extra α LOSES lift, so the roll drives itself.",
                L => { L.W.StartCondition("spin", "pitts-s2b-like", null, null); Show(L, "vectors", "strips"); Inset(L, "aoa"); }, P("AUTOROTATING", 20, (L, dt) => { }));
            Add("inertia-coupling", "Inertia coupling in the spin", "The spinning mass pitches the nose UP (−ω × Iω); the aerodynamic nose-down moment balances it in a steady spin.",
                L => { L.W.StartCondition("spin", "pitts-s2b-like", null, null); Show(L, "vectors", "moments"); Inset(L, "aoa"); }, P("THE MOMENTS IN THE SPIN", 20, (L, dt) => { }));
            Add("spin-modes", "Spin modes and the CG", "Moving the CG aft flattens the spin and slows the recovery; forward steepens it.",
                L => { L.W.StartCondition("spin", "pitts-s2b-like", null, null); Show(L, "vectors", "moments", "cgnp"); Inset(L, "wb", "aoa"); },
                P("CG FORWARD LIMIT", 12, (L, dt) => { }, null, L => L.W.Loading.Set(null, L.W.Loading.FwdLimitMac, false)),
                P("CG AFT LIMIT", 12, (L, dt) => { }, null, L => L.W.Loading.Set(null, L.W.Loading.AftLimitMac, false)),
                P("CG BEHIND THE LIMIT", 12, (L, dt) => { }, null, L => L.W.Loading.Set(null, L.W.Loading.AftLimitMac + 6, false)));
            Add("recovery-factors", "Recovery factors", "The horizontal tail's wake can blanket the rudder; forward stick unshields it; aileron helps or hurts depending on the mass distribution.",
                L => { L.W.StartCondition("spin", "pitts-s2b-like", null, null); Show(L, "vectors", "moments"); Inset(L, "aoa"); },
                P("DEVELOPED", 6, (L, dt) => { }),
                P("RUDDER ONLY (stick held aft)", 6, (L, dt) => { L.C.Hold = false; L.C.Rudder = L.R.YawRateDps < 0 ? 1 : -1; L.C.Elevator = -1; }),
                P("+ STICK FORWARD", 8, (L, dt) => { L.C.Elevator = 0.4; }));
            Add("incipient", "The incipient spin", "The first turn or two: the airplane is still deciding — rates and α oscillate before the spin settles.",
                L => { L.W.Load(AeroWidget.Scenario.Spin, "c172-like", 0); L.W.Presets.Run("spin-entry"); Show(L, "vectors"); Inset(L, "aoa"); }, P("ENTRY", 25, (L, dt) => { }));
            Add("power-in-spin", "Power in the spin", "Power in a spin usually flattens it (slipstream and gyroscopic moments) and makes the recovery harder.",
                L => { L.W.StartCondition("spin", "pitts-s2b-like", null, null); Show(L, "vectors", "moments"); Inset(L, "aoa"); },
                P("IDLE", 8, (L, dt) => { }), P("FULL POWER", 10, (L, dt) => { L.C.Throttle01 = 1; }), P("IDLE AGAIN", 8, (L, dt) => { L.C.Throttle01 = 0; }));
            Add("accelerated-spin", "The accelerated spin", "Entered from a steep turn or with aileron, the spin starts faster and steeper.",
                L => { Cruise(L, "pitts-s2b-like", "locked"); Show(L, "vectors"); Inset(L, "aoa"); },
                P("STEEP TURN, pull and full rudder", 8, (L, dt) => { L.C.Throttle01 = 0; L.HoldBank(-50, dt, false); L.C.Elevator = -1; L.C.Rudder = -1; }),
                P("SPINNING", 12, (L, dt) => { L.C.Elevator = -1; L.C.Rudder = -1; L.C.Aileron = 0; }));
            Add("spin-ailerons", "Ailerons in the spin", "With pro-spin elevator and rudder held, aileron WITH or AGAINST the rotation changes each wing's α and the spin's rate and attitude — which way depends on the mass distribution.",
                L => { L.W.StartCondition("spin", "pitts-s2b-like", null, null); Show(L, "vectors", "moments"); Inset(L, "aoa"); L.Results.Clear(); },
                P("NEUTRAL AILERON", 5, (L, dt) => { L.C.Aileron = 0; }, null, L => L.SpinNoteStart()),
                P("FULL AILERON WITH THE ROTATION", 5, (L, dt) => { L.C.Aileron = L._spinDir; }, null, L => L.SpinNote("NEUTRAL")),
                P("FULL AILERON AGAINST THE ROTATION", 5, (L, dt) => { L.C.Aileron = -L._spinDir; }, null, L => L.SpinNote("WITH")),
                P("NEUTRAL AGAIN", 4, (L, dt) => { L.C.Aileron = 0; }, null, L => L.SpinNote("AGAINST")));
        }

        // ---- measurements written to the caption ----
        public double TrimStick, TrimAil, TrimRud;
        private bool StallCheck(string tag)
        {
            var r = R; double crit = _w.CriticalAlphaDeg(_w.Ac.FlapFraction);
            if (r.AlphaDeg < crit - 0.2) return false;
            double nz = r.LoadFactor;
            Results.Add($"{tag}: stalled at α {r.AlphaDeg:0.0}°, {r.Kias:0} KIAS, {nz:0.0} g");
            _stalls.Add((r.Kias, nz));
            return true;
        }
        private bool WingStallCheck()
        {
            var p = _w.Vectors.Current; if (p == null) return false;
            double crit = _w.CriticalAlphaDeg(_w.Ac.FlapFraction);
            if (p.WingAlphaL < crit && p.WingAlphaR < crit) return false;
            bool inside = R.RollDeg < 0 ? p.WingAlphaL >= p.WingAlphaR : p.WingAlphaR >= p.WingAlphaL;
            Results.Add($"{(p.WingAlphaL >= p.WingAlphaR ? "LEFT" : "RIGHT")} (= {(inside ? "INSIDE" : "OUTSIDE")}) wing stalled first: L α {p.WingAlphaL:0}° R α {p.WingAlphaR:0}°");
            return true;
        }
        private readonly double[] _pmarks = { 100, 85, 70, 60, 52 }; private int _pmIdx;
        private void PowerMark()
        {
            var r = R; if (_pmIdx >= _pmarks.Length) return;
            if (r.Kias <= _pmarks[_pmIdx] + 0.5 && System.Math.Abs(_w.Autopilot.Kias - r.Kias) < 2)
            { Results.Add($"{r.Kias:0} KIAS: power {_w.Controls.Throttle01 * 100:0} %, α {r.AlphaDeg:0.0}°"); _pmIdx++; if (Results.Count > 4) Results.RemoveAt(0); }
        }
        private double _gx, _gy, _gh;
        private void GlideStart() { var s = _w.Ac.State; _gx = s.Position.X; _gy = s.Position.Y; _gh = -s.Position.Z; }
        private void GlideEnd(string tag)
        {
            var s = _w.Ac.State; double dist = System.Math.Sqrt((s.Position.X - _gx) * (s.Position.X - _gx) + (s.Position.Y - _gy) * (s.Position.Y - _gy)), drop = _gh + s.Position.Z;
            Results.Add($"{tag}: {(drop > 1 ? dist / drop : 0):0.0}:1 at {R.Kias:0} KIAS");
        }
        private double _bestA = double.NaN; private string _bestKey;
        public double BestGlideAlpha()
        {
            string key = _w.AircraftId + _w.Ac.FlapFraction.ToString("0.00");
            if (_bestKey == key) return _bestA;
            double best = 0, ba = 6; foreach (var (a, ld) in _w.Curves.LdAlpha(_w)) if (ld > best) { best = ld; ba = a; }
            _bestKey = key; _bestA = ba; return ba;
        }
        // Adverse yaw: the yaw rate during the first second of the roll-in, and the most sideslip — + = yawing right.
        private double _yawSum, _betaMax; private int _yawN; private float _yawT0;
        private void YawStart() { _yawSum = 0; _yawN = 0; _betaMax = 0; _yawT0 = PhaseT; }
        private void YawSample() { var r = R; if (PhaseT < 1.2f) { _yawSum += r.YawRateDps; _yawN++; if (System.Math.Abs(r.BetaDeg) > System.Math.Abs(_betaMax)) _betaMax = r.BetaDeg; } }
        private void YawEnd(string tag)
        {
            double y = _yawN > 0 ? _yawSum / _yawN : 0;
            Results.Add($"{tag}: rolling LEFT, first 1.2 s: sideslip {_betaMax:+0.0;-0.0}° ({(_betaMax < -0.5 ? "nose yawed RIGHT — away: adverse" : _betaMax > 0.5 ? "nose LEFT of the path" : "ball centred")}), yaw rate {y:+0.0;-0.0}°/s");
        }
        private double _spinDir = -1, _yaw0, _roll0, _pitch0;
        private void SpinNoteStart() { _spinDir = R.YawRateDps < 0 ? -1 : 1; }
        private void SpinNote(string tag)
        {
            // averaged over the phase that just ended
            Results.Add($"{tag}: yaw {_yawAvg:0}°/s, roll {_rollAvg:0}°/s, pitch {_pitchAvg:0}°, α {_aAvg:0}°");
        }
        private double _yawAvg, _rollAvg, _pitchAvg, _aAvg; private int _avgN;

        // ---- running it ----
        public bool Start(string id)
        {
            Lesson l = null; foreach (var x in All) if (x.Id == id) l = x;
            if (l == null) return false;
            Current = l; PhaseIdx = -1; Results.Clear(); _stalls.Clear(); _pmIdx = 0;
            _w.Autopilot.Off(); _w.Presets.Stop();
            foreach (var k in new[] { "vectors", "lift", "drag", "wind", "strips", "cgnp", "liftComponents", "wingDrag", "groundTrack", "aimPoint", "propEffects" }) _w.Show[k] = false;
            _w.Show["vectors"] = true;
            _w.SetWind(0, 0);
            l.Setup(this);
            TrimStick = C.Elevator; TrimAil = C.Aileron; TrimRud = C.Rudder;
            Seed();
            _w.Lesson = id;
            Next();
            return true;
        }
        public void Stop() { Current = null; PhaseIdx = -1; _w.Lesson = null; }
        /// <summary>Pilot input ends the script (the scene stays).</summary>
        public void Interrupt() { if (Driving) PhaseIdx = Current.Phases.Count; }

        private void Next()
        {
            if (PhaseIdx >= 0 && PhaseIdx < Current.Phases.Count) Current.Phases[PhaseIdx].Exit?.Invoke(this);
            PhaseIdx++; PhaseT = 0; _avgN = 0; _yawAvg = _rollAvg = _pitchAvg = _aAvg = 0;
            if (PhaseIdx < Current.Phases.Count) Current.Phases[PhaseIdx].Enter?.Invoke(this);
        }

        public void Tick(float dt)
        {
            if (!Driving || dt <= 0) return;
            var ph = Current.Phases[PhaseIdx];
            PhaseT += dt;
            _levelUsed = false;
            ph.Tick?.Invoke(this, dt);
            if (!_levelUsed && _w.Autopilot.On && _w.Autopilot.PitchOnly) _w.Autopilot.Off();   // the phase isn't holding level any more
            var r = R; _avgN++; _yawAvg += (r.YawRateDps - _yawAvg) / _avgN; _rollAvg += (r.RollRateDps - _rollAvg) / _avgN; _pitchAvg += (r.PitchDeg - _pitchAvg) / _avgN; _aAvg += (r.AlphaDeg - _aAvg) / _avgN;
            if ((ph.Done != null && ph.Done(this)) || PhaseT >= ph.Seconds) Next();
        }

        /// <summary>Lesson-specific vectors: the lift's vertical / horizontal parts, each wing's drag, the ground track and the
        /// wind, the aim point, the propeller's yawing effects.</summary>
        public void DrawExtras(WidgetControlsDisplay ui, WidgetVectors v, System.Func<Vec3, Vector3> world, System.Func<Vec3, Vector3> dir,
                               WidgetVectors.ScrFn scr, float pxM, float gPx, float lw)
        {
            var w = _w; var s = w.ShownState; Vec3 cg = w.Config.Mass.CgVec(); var pic = v.Current; if (pic == null) return;
            double W = w.Ac.MassProperties.MassKg * 9.81;
            Vector3 cgU = world(cg);
            if (w.Show.TryGetValue("liftComponents", out bool lc) && lc && v.Filtered.TryGetValue("lift", out var lift))
            {
                Vector3 L = dir(lift.vec) * (gPx * pxM);
                Vector3 vert = new(0, L.y, 0), horiz = new(L.x, 0, L.z);
                if (scr(cgU, out var a) && scr(cgU + vert, out var b)) ui.PxArrow(a, b, lw * 0.8f, new Color(0.4f, 1f, 0.5f, 0.85f));
                if (scr(cgU, out var a2) && scr(cgU + horiz, out var b2)) ui.PxArrow(a2, b2, lw * 0.8f, new Color(1f, 0.6f, 0.2f, 0.95f));
                var rd = w.Read(); double vs = w.StallKias(w.Ac.FlapFraction);
                ui.PxText(new Vector2(30, w.Display.SceneRect.yMin + 26), $"LOAD {rd.LoadFactor:0.00} g · STALL SPEED {vs:0} × √{rd.LoadFactor:0.00} = {vs * System.Math.Sqrt(System.Math.Max(0, rd.LoadFactor)):0} KIAS", 17f, Color.white, TextAnchor.MiddleLeft);
            }
            if (w.Show.TryGetValue("wingDrag", out bool wd) && wd && w.Ac.LastForces != null && w.Shown == null)
            {
                Vec3 dl = Vec3.Zero, dr = Vec3.Zero;
                foreach (var f in w.Ac.LastForces) if (f.Kind == "drag" && f.PosBody.X > cg.X - 1.5 && System.Math.Abs(f.PosBody.Y) > 0.6) { if (f.PosBody.Y < 0) dl += f.ForceBody; else dr += f.ForceBody; }
                foreach (var (d, side) in new[] { (dl, -1), (dr, 1) })
                {
                    Vector3 o = world(w.WingMidSpan(side) + new Vec3(0, side * w.SpanM * 0.2, 0)), dd = dir(d * (1.0 / W)) * (gPx * 8f * pxM);
                    if (scr(o, out var a) && scr(o + dd, out var b)) ui.PxArrow(a, b, lw * 0.8f, new Color(1f, 0.3f, 0.25f));
                }
                ui.PxText(new Vector2(30, w.Display.SceneRect.yMin + 26), $"WING DRAG  L {dl.Length * 0.2248:0} lb · R {dr.Length * 0.2248:0} lb (×8)", 17f, Color.white, TextAnchor.MiddleLeft);
            }
            if (w.Show.TryGetValue("groundTrack", out bool gt) && gt)
            {
                Vector3 vAirW = dir(s.Velocity) , wind = new Vector3((float)Atmosphere.SteadyWind.Y, 0, (float)Atmosphere.SteadyWind.X);
                Vector3 gv = vAirW + wind; float k = gPx * pxM / 60f;
                if (scr(cgU, out var a0)) { if (scr(cgU + new Vector3(vAirW.x, 0, vAirW.z) * k, out var b0)) ui.PxArrow(a0, b0, lw, WidgetVectors.Wind); if (scr(cgU + new Vector3(gv.x, 0, gv.z) * k, out var b1)) ui.PxArrow(a0, b1, lw, new Color(0.95f, 0.8f, 0.55f)); if (scr(cgU + wind * k * 2, out var b2)) ui.PxArrow(a0, b2, lw * 0.7f, Color.white); }
                ui.PxText(new Vector2(30, w.Display.SceneRect.yMin + 26), $"AIR VELOCITY (cyan) · GROUND TRACK (tan) · WIND {Atmosphere.SteadyWind.Length * 1.9438:0} KT (white)", 16f, Color.white, TextAnchor.MiddleLeft);
            }
        }

        /// <summary>The caption line: lesson · phase · results.</summary>
        public void DrawCaption(WidgetControlsDisplay ui, Rect scene)
        {
            if (Current == null) return;
            float y = scene.yMax - 58;
            ui.PxText(new Vector2(22, y), $"LESSON · {Current.Name.ToUpperInvariant()}", 17f, new Color(0.55f, 0.85f, 1f), TextAnchor.MiddleLeft);
            if (Caption != null) ui.PxText(new Vector2(22, y - 22), Caption, 16f, Color.white, TextAnchor.MiddleLeft);
            for (int i = 0; i < Results.Count && i < 4; i++) ui.PxText(new Vector2(22, y - 44 - i * 20), Results[i], 15f, new Color(1f, 0.85f, 0.4f), TextAnchor.MiddleLeft);
        }
    }
}
