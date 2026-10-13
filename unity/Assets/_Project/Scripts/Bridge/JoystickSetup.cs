using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// JOYSTICK — any stick, HOTAS, pedals or RC-radio dongle (owner 2026-10-11: "a guided joystick calibration and learning
    /// session plus button assignment that will work with any joystick"). Shared by the game (Mac) and the Aero Widget.
    /// The raw axes are the project's WJoy1…8 (all joysticks); buttons are JoystickButton0…19. A PROFILE per joystick name
    /// (PlayerPrefs) says which axis is which control, its centre and both endpoints, its dead zone, and which button does what.
    /// </summary>
    public static class JoystickInput
    {
        public const int AxisCount = 8, ButtonCount = 32, HatButton0 = 28;   // (the Mac: 28…31 = the hat's up / right / down / left)
        /// <summary>The assignable actions, in the order the setup asks for them.</summary>
        public static readonly string[] Actions = { "brakes", "guns", "flapsDown", "flapsUp", "flapsHalf", "gear", "trimUp", "trimDown", "apDisconnect", "pause", "reset", "view" };
        public static readonly string[] ActionNames = { "BRAKES (held)", "GUNS (held)", "FLAPS DOWN", "FLAPS UP", "FLAPS 50 %", "GEAR UP / DOWN", "TRIM NOSE UP", "TRIM NOSE DOWN", "AUTOPILOT DISCONNECT", "PAUSE / REVIEW", "RESET", "CHANGE VIEW" };

        // The Mac reads joysticks straight from macOS's HID manager (AeroNative HJ_*): Unity's Input Manager only reads them
        // while its app is in front, and the widget runs behind Glass Overlay (owner 2026-10-11: the Hotas X did nothing in
        // Glass). Elsewhere (the editor, iOS) Unity's axes WJoy1…8 and JoystickButton0…19.
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
        private const bool Native = true;
        [System.Runtime.InteropServices.DllImport("AeroNative")] private static extern void HJ_Start();
        [System.Runtime.InteropServices.DllImport("AeroNative")] private static extern int HJ_Count();
        [System.Runtime.InteropServices.DllImport("AeroNative")] private static extern int HJ_Name(int dev, byte[] buf, int max);
        [System.Runtime.InteropServices.DllImport("AeroNative")] private static extern float HJ_Axis(int dev, int i);
        [System.Runtime.InteropServices.DllImport("AeroNative")] private static extern int HJ_Button(int dev, int b);
#else
        private const bool Native = false;
        private static void HJ_Start() { }
        private static int HJ_Count() => 0;
        private static int HJ_Name(int dev, byte[] buf, int max) => -1;
        private static float HJ_Axis(int dev, int i) => 0f;
        private static int HJ_Button(int dev, int b) => 0;
#endif
        private static bool _started;
        /// <summary>No joystick at all (the widget's capture instance: the presenter's stick must not fly the render).</summary>
        private static readonly bool Off = System.Environment.GetEnvironmentVariable("AERO_NO_JOYSTICK") == "1" || System.Environment.GetEnvironmentVariable("AERO_WIDGET_CAPTURE") == "1";
        private static int _frame = -1;
        private static readonly bool[] _held = new bool[ButtonCount], _was = new bool[ButtonCount];
        private static string _nameCache; private static float _nameAt = -10f;
        private static readonly byte[] _nameBuf = new byte[256];

        /// <summary>The first connected joystick's buttons this frame (and last frame's, for presses).</summary>
        private static void Poll()
        {
            if (_frame == Time.frameCount) return;
            bool link = UseLink;
            if (!Native && !link) { System.Array.Clear(_held, 0, ButtonCount); System.Array.Clear(_was, 0, ButtonCount); return; }   // (the link dropped: nothing stays held)
            if (Native && !_started) { _started = true; HJ_Start(); }
            _frame = Time.frameCount;
            bool any = Native && HJ_Count() > 0;
            for (int b = 0; b < ButtonCount; b++) { _was[b] = _held[b]; _held[b] = link ? JoyLink.Button(b) : any && HJ_Button(0, b) != 0; }
        }

        /// <summary>JOYSTICK LINK (2026-10-12): no joystick of this device's own, and the Mac's arriving over the network.</summary>
        private static bool UseLink { get { if (Off) return false; JoyLink.Tick(); return JoyLink.Connected && LocalName == null; } }

        /// <summary>Raw axis i (1…8), −1…1 — this device's joystick, or the Mac's over the link.</summary>
        public static float Raw(int i) => UseLink ? JoyLink.Axis(i) : LocalRaw(i);
        public static string ConnectedName => Off ? null : LocalName ?? (UseLink ? JoyLink.JoystickName : null);

        /// <summary>This device's own joystick (what the Mac shares over the link).</summary>
        public static float LocalRaw(int i)
        {
            if (Off) return 0f;
            if (Native) { if (!_started) { _started = true; HJ_Start(); } return HJ_Count() > 0 ? HJ_Axis(0, i - 1) : 0f; }
            try { return Input.GetAxisRaw("WJoy" + i); } catch (System.ArgumentException) { return 0f; }
        }
        public static bool LocalButton(int b) => !Off && Native && b >= 0 && b < ButtonCount && HJ_Count() > 0 && HJ_Button(0, b) != 0;
        public static string LocalName
        {
            get
            {
                if (Off) return null;
                if (Native)
                {
                    if (!_started) { _started = true; HJ_Start(); }
                    if (Time.unscaledTime - _nameAt > 0.5f)
                    {
                        _nameAt = Time.unscaledTime;
                        int n = HJ_Count() > 0 ? HJ_Name(0, _nameBuf, _nameBuf.Length) : -1;
                        _nameCache = n > 0 ? System.Text.Encoding.UTF8.GetString(_nameBuf, 0, n) : null;
                    }
                    return _nameCache;
                }
                foreach (var nm in Input.GetJoystickNames()) if (!string.IsNullOrEmpty(nm)) return nm;
                return null;
            }
        }
        public static bool ButtonHeld(int b)
        {
            if (Off) return false;
            if (b < 0 || b >= ButtonCount) return false;
            if (Native || UseLink) { Poll(); return _held[b]; }
            return b < 20 && Input.GetKey(KeyCode.JoystickButton0 + b);   // (Unity names 20 buttons)
        }
        public static bool ButtonDown(int b)
        {
            if (Off) return false;
            if (b < 0 || b >= ButtonCount) return false;
            if (Native || UseLink) { Poll(); return _held[b] && !_was[b]; }
            return b < 20 && Input.GetKeyDown(KeyCode.JoystickButton0 + b);
        }
        /// <summary>A button's name for the setup: "BUTTON 3", or the hat's "HAT UP" etc.</summary>
        public static string ButtonName(int b) => Native && b >= HatButton0 && b < HatButton0 + 4 ? "HAT " + new[] { "UP", "RIGHT", "DOWN", "LEFT" }[b - HatButton0] : $"BUTTON {b + 1}";
        public static int AnyButtonDown() { for (int b = 0; b < ButtonCount; b++) if (ButtonDown(b)) return b; return -1; }
        public static bool AnyButtonHeld() { for (int b = 0; b < ButtonCount; b++) if (ButtonHeld(b)) return true; return false; }

        /// <summary>The setup running now (it owns the stick while it runs: nothing flies the airplane).</summary>
        public static JoystickWizard Wizard;
        public static bool SetupActive => Wizard != null && Wizard.Active;

        private static JoystickProfile _profile; private static string _profileName; private static float _checkedAt = -10f;
        /// <summary>The profile of the joystick connected now (null: none connected, or not set up yet).</summary>
        public static JoystickProfile Profile
        {
            get
            {
                if (Time.unscaledTime - _checkedAt > 1f)
                {
                    _checkedAt = Time.unscaledTime;
                    string n = ConnectedName;
                    if (n != _profileName) { _profileName = n; _profile = n == null ? null : Load(n); }
                }
                return _profile;
            }
        }
        private static string Key(string name) => "joy.profile." + name;
        public static JoystickProfile Load(string name)
        {
            string j = PlayerPrefs.GetString(Key(name), "");
            if (string.IsNullOrEmpty(j)) return null;
            try { return JsonConvert.DeserializeObject<JoystickProfile>(j); } catch { return null; }
        }
        public static void Save(JoystickProfile p)
        {
            PlayerPrefs.SetString(Key(p.Name), JsonConvert.SerializeObject(p)); PlayerPrefs.Save();
            _profileName = null; _checkedAt = -10f;
        }
        public static void Forget(string name) { PlayerPrefs.DeleteKey(Key(name)); PlayerPrefs.Save(); _profileName = null; _checkedAt = -10f; }

        /// <summary>The action's button (or switch channel) is held now / was pressed this frame.</summary>
        public static bool Held(string action)
        {
            var p = Profile; if (p == null || SetupActive) return false;
            if (p.Buttons.TryGetValue(action, out int b)) return ButtonHeld(b);
            return p.AxisButtons.TryGetValue(action, out var sw) && AxisSw(action, sw).now;
        }
        public static bool Pressed(string action)
        {
            var p = Profile; if (p == null || SetupActive) return false;
            if (p.Buttons.TryGetValue(action, out int b)) return ButtonDown(b);
            if (!p.AxisButtons.TryGetValue(action, out var sw)) return false;
            var e = AxisSw(action, sw); return e.now && !e.prev;
        }
        // A switch channel's state this frame and last (its edge = a press); a frame it wasn't asked about has no edge.
        private static readonly Dictionary<string, (int frame, bool now, bool prev)> _axSw = new();
        private static (bool now, bool prev) AxisSw(string action, AxisSwitch sw)
        {
            int f = Time.frameCount;
            if (!_axSw.TryGetValue(action, out var e) || e.frame != f)
            {
                bool h = sw.On(); e = (f, h, _axSw.TryGetValue(action, out var o) && o.frame == f - 1 ? o.now : h); _axSw[action] = e;
            }
            return (e.now, e.prev);
        }
    }

    /// <summary>One calibrated axis: which raw axis, its centre and the raw values at both ends.</summary>
    public sealed class JoyAxis
    {
        public int Index;                  // 1…8 (0 = not assigned)
        public float Center, PosEnd, NegEnd, Dead = 0.03f;
        public bool Centering = true;      // false = a throttle (no centre; NegEnd = idle, PosEnd = full)
        [JsonIgnore] public bool Set => Index > 0;
        /// <summary>A centring axis: −1…1 (+ = right / push / right rudder), with the dead zone taken out.</summary>
        public float Value()
        {
            if (!Set) return 0f;
            float raw = JoystickInput.Raw(Index), d = raw - Center, pos = PosEnd - Center, neg = NegEnd - Center;
            float n = d * pos > 0 ? d / pos : d * neg > 0 ? -d / neg : 0f;
            n = Mathf.Clamp(n, -1f, 1f);
            float a = Mathf.Abs(n);
            return a < Dead ? 0f : Mathf.Sign(n) * (a - Dead) / (1f - Dead);
        }
        /// <summary>A throttle: 0 (idle) … 1 (full).</summary>
        public float Value01() => !Set ? 0f : Mathf.Clamp01(Mathf.InverseLerp(NegEnd, PosEnd, JoystickInput.Raw(Index)));
    }

    /// <summary>A button that arrives as a channel (an RC radio's dongle sends its buttons and switches as axes, owner
    /// 2026-10-12): on when the raw value is nearer the pressed value than the released one.</summary>
    public sealed class AxisSwitch
    {
        public int Axis; public float OnValue, OffValue;
        public bool On() { float v = JoystickInput.Raw(Axis); return Mathf.Abs(v - OnValue) < Mathf.Abs(v - OffValue); }
    }

    public sealed class JoystickProfile
    {
        public string Name;
        public JoyAxis Aileron = new(), Elevator = new(), Rudder = new(), Throttle = new() { Centering = false };
        /// <summary>RUDDER TRIM (owner 2026-10-11: "change one to rudder trim … the one on the throttle"): a spring-centred rocker
        /// RUNS the trim (−1…1 = rate, left / right) and the trim stays where it's left — like a trim switch.</summary>
        public JoyAxis RudderTrim = new();
        public Dictionary<string, int> Buttons = new();
        /// <summary>Actions on a switch CHANNEL instead of a button (an RC radio's buttons).</summary>
        public Dictionary<string, AxisSwitch> AxisButtons = new();
        /// <summary>FLAP SWITCH (owner 2026-10-12: "the 3 position switch to be flaps"): a 2- or 3-position switch channel,
        /// NegEnd = flaps UP, PosEnd = FULL; the middle = half.</summary>
        public JoyAxis FlapSwitch = new() { Centering = false };
        /// <summary>The flap switch's position: 0 up, 1 half, 2 full; −1 = none.</summary>
        public int FlapSwitchPos() { if (!FlapSwitch.Set) return -1; float t = FlapSwitch.Value01(); return t < 0.25f ? 0 : t < 0.75f ? 1 : 2; }
        /// <summary>Calibrated controls: aileron + right, elevator + PUSH (nose down), rudder + right; throttle 0…1 when it has one.</summary>
        public (float ail, float ele, float rud, float thr01, bool hasThr) Read() =>
            (Aileron.Value(), Elevator.Value(), Rudder.Value(), Throttle.Value01(), Throttle.Set);
    }

    /// <summary>
    /// The guided setup: centre → each control by moving it (the axis that moves is the one) → a learning sweep (true
    /// endpoints, dead zone from the measured noise) → each button by pressing it → saved. Every step can be skipped
    /// (no rudder, no throttle, no button for that); BACK redoes the last step.
    /// </summary>
    public sealed class JoystickWizard
    {
        private enum Kind { Center, AxisFirst, AxisSecond, ThrIdle, ThrFull, Learn, Button, Done }
        private readonly List<(Kind kind, string ctl, bool positive, string text)> _steps = new();
        private JoystickProfile _p;
        private int _i;
        private readonly float[] _rest = new float[JoystickInput.AxisCount + 1], _noise = new float[JoystickInput.AxisCount + 1], _snap = new float[JoystickInput.AxisCount + 1];
        private readonly float[] _last = new float[JoystickInput.AxisCount + 1], _min = new float[JoystickInput.AxisCount + 1], _max = new float[JoystickInput.AxisCount + 1];
        private float _stillFor, _stepTime, _learnMoving, _cooldown;
        private bool _movedSinceStep, _buttonsClear;
        private int _candidate;

        public bool Active { get; private set; }
        public string DeviceName => _p?.Name;
        public int StepNumber => _i + 1;
        public int StepCount => _steps.Count;
        public string Instruction => Active ? _steps[_i].text : "";
        public string Status { get; private set; } = "";
        public float Progress { get; private set; }
        /// <summary>The raw axis this step is looking at (for the meters' highlight); 0 = none.</summary>
        public int Focus { get; private set; }
        public bool Finished { get; private set; }
        public JoystickProfile Result => _p;

        public bool Start()
        {
            string name = JoystickInput.ConnectedName;
            if (name == null) { Status = "NO JOYSTICK CONNECTED"; Active = false; return false; }
            _p = new JoystickProfile { Name = name };
            _steps.Clear();
            _steps.Add((Kind.Center, null, false, "Let go of everything — stick and rudder centred. (Leave the throttle anywhere.)"));
            _steps.Add((Kind.AxisFirst, "aileron", true, "Hold the stick fully RIGHT."));
            _steps.Add((Kind.AxisSecond, "aileron", false, "Now hold it fully LEFT."));
            _steps.Add((Kind.AxisFirst, "elevator", false, "PULL the stick fully BACK (nose up) and hold."));
            _steps.Add((Kind.AxisSecond, "elevator", true, "Now PUSH it fully FORWARD and hold."));
            _steps.Add((Kind.AxisFirst, "rudder", true, "Full RIGHT rudder (pedal, twist or rudder stick) and hold — or SKIP if there's none."));
            _steps.Add((Kind.AxisSecond, "rudder", false, "Now full LEFT rudder."));
            _steps.Add((Kind.ThrIdle, "throttle", false, "Throttle to IDLE (all the way back) and hold — or SKIP if there's none."));
            _steps.Add((Kind.ThrFull, "throttle", true, "Now throttle FULL (all the way forward) and hold."));
            _steps.Add((Kind.ThrIdle, "flapSwitch", false, "FLAP SWITCH (a 2- or 3-position switch): set it to FLAPS UP and leave it — or SKIP to use buttons."));
            _steps.Add((Kind.ThrFull, "flapSwitch", true, "Now flip it all the way to FULL FLAPS."));
            _steps.Add((Kind.AxisFirst, "rudderTrim", true, "RUDDER TRIM: hold its control (e.g. the rocker on the throttle) fully RIGHT — or SKIP if you don't want one."));
            _steps.Add((Kind.AxisSecond, "rudderTrim", false, "Now rudder trim fully LEFT."));
            _steps.Add((Kind.Learn, null, false, "Learning: stir everything — every control through its full travel, several times."));
            for (int k = 0; k < JoystickInput.Actions.Length; k++)
                _steps.Add((Kind.Button, JoystickInput.Actions[k], false, $"Press the button for {JoystickInput.ActionNames[k]} — or SKIP."));
            _steps.Add((Kind.Done, null, false, "Done. Move the controls to check them, then FINISH to save."));
            Active = true; Finished = false; Enter(0);
            JoystickInput.Wizard = this;
            return true;
        }
        public void Cancel() { Active = false; Status = "CANCELLED"; }
        public void Skip()
        {
            if (!Active) return;
            var s = _steps[_i];
            if (s.kind == Kind.AxisFirst || s.kind == Kind.ThrIdle)
            {
                Axis(s.ctl).Index = 0;
                Enter(_i + 2);   // the pair goes together
                return;
            }
            if (s.kind == Kind.Done) { Finish(); return; }
            Enter(_i + 1);
        }
        public void Back()
        {
            if (!Active || _i == 0) return;
            int to = _i - 1;
            var prev = _steps[to];
            if (prev.kind == Kind.AxisSecond || prev.kind == Kind.ThrFull) { to--; Axis(prev.ctl).Index = 0; }   // redo the pair
            if (prev.kind == Kind.AxisFirst || prev.kind == Kind.ThrIdle) Axis(prev.ctl).Index = 0;
            if (prev.kind == Kind.Button) { _p.Buttons.Remove(prev.ctl); _p.AxisButtons.Remove(prev.ctl); }
            Enter(Mathf.Max(0, to));
        }
        public void Finish()
        {
            if (!Active) return;
            JoystickInput.Save(_p);
            Active = false; Finished = true; Status = "SAVED";
        }

        private JoyAxis Axis(string ctl) => ctl switch { "aileron" => _p.Aileron, "elevator" => _p.Elevator, "rudder" => _p.Rudder, "rudderTrim" => _p.RudderTrim, "flapSwitch" => _p.FlapSwitch, _ => _p.Throttle };
        private bool SwitchTaken(int ax) { foreach (var sw in _p.AxisButtons.Values) if (sw.Axis == ax) return true; return false; }
        private bool Used(int ax) => _p.Aileron.Index == ax || _p.Elevator.Index == ax || _p.Rudder.Index == ax || _p.Throttle.Index == ax || _p.RudderTrim.Index == ax || _p.FlapSwitch.Index == ax;

        private void Enter(int i)
        {
            _i = Mathf.Clamp(i, 0, _steps.Count - 1);
            _stillFor = 0; _stepTime = 0; _learnMoving = 0; _movedSinceStep = false; _candidate = 0; _cooldown = 0.35f;
            _buttonsClear = !JoystickInput.AnyButtonHeld();
            Progress = 0; Focus = 0; Status = "";
            for (int a = 1; a <= JoystickInput.AxisCount; a++) { _last[a] = JoystickInput.Raw(a); _snap[a] = _last[a]; _min[a] = _max[a] = _last[a]; }
            var s = _steps[_i];
            if (s.kind == Kind.AxisSecond || s.kind == Kind.ThrFull) Focus = Axis(s.ctl).Index;
            if (s.kind == Kind.AxisSecond && !Axis(s.ctl).Set) Enter(_i + 1);   // its first half was skipped
        }

        /// <summary>One frame (real time).</summary>
        public void Tick(float dt)
        {
            if (!Active) return;
            if (JoystickInput.ConnectedName == null) { Status = "JOYSTICK DISCONNECTED"; return; }
            _stepTime += dt;
            if (_cooldown > 0) { _cooldown -= dt; for (int a = 1; a <= JoystickInput.AxisCount; a++) _last[a] = JoystickInput.Raw(a); return; }
            float maxStep = 0;
            for (int a = 1; a <= JoystickInput.AxisCount; a++) { float v = JoystickInput.Raw(a); maxStep = Mathf.Max(maxStep, Mathf.Abs(v - _last[a])); _last[a] = v; }
            bool still = maxStep < 0.012f;
            _stillFor = still ? _stillFor + dt : 0f;
            var s = _steps[_i];
            switch (s.kind)
            {
                case Kind.Center:
                {
                    // 1.5 s of stillness: the rest values and their noise.
                    if (_stillFor < 0.05f) { for (int a = 1; a <= JoystickInput.AxisCount; a++) { _rest[a] = _last[a]; _noise[a] = 0; } }
                    for (int a = 1; a <= JoystickInput.AxisCount; a++) { _rest[a] = Mathf.Lerp(_rest[a], _last[a], 0.1f); _noise[a] = Mathf.Max(_noise[a], Mathf.Abs(_last[a] - _rest[a])); }
                    Progress = Mathf.Clamp01(_stillFor / 1.5f);
                    Status = still ? "HOLD STILL…" : "WAITING FOR STILLNESS";
                    if (_stillFor >= 1.5f) Enter(_i + 1);
                    break;
                }
                case Kind.AxisFirst:
                {
                    // The unassigned axis that moved furthest from rest, held there.
                    int best = 0; float bestD = 0.45f;
                    for (int a = 1; a <= JoystickInput.AxisCount; a++) { if (Used(a)) continue; float d = Mathf.Abs(_last[a] - _rest[a]); if (d > bestD) { bestD = d; best = a; } }
                    if (best != _candidate) { _candidate = best; _stillFor = 0; }
                    Focus = best;
                    Progress = best == 0 ? 0 : Mathf.Clamp01(_stillFor / 0.6f);
                    Status = best == 0 ? "MOVE IT ALL THE WAY" : $"AXIS {best} — HOLD…";
                    if (best != 0 && _stillFor >= 0.6f)
                    {
                        var ax = Axis(s.ctl); ax.Index = best; ax.Center = _rest[best]; ax.Centering = true;
                        if (s.positive) ax.PosEnd = _last[best]; else ax.NegEnd = _last[best];
                        Enter(_i + 1);
                    }
                    break;
                }
                case Kind.AxisSecond:
                {
                    var ax = Axis(s.ctl); int a = ax.Index; Focus = a;
                    float first = s.positive ? ax.NegEnd : ax.PosEnd, span = Mathf.Abs(first - ax.Center);
                    float d = _last[a] - ax.Center;
                    bool other = d * (first - ax.Center) < 0 && Mathf.Abs(d) > Mathf.Max(0.3f, 0.45f * span);
                    if (!other) _stillFor = 0;
                    Progress = other ? Mathf.Clamp01(_stillFor / 0.6f) : 0;
                    Status = other ? $"AXIS {a} — HOLD…" : "THE OTHER WAY, ALL THE WAY";
                    if (other && _stillFor >= 0.6f) { if (s.positive) ax.PosEnd = _last[a]; else ax.NegEnd = _last[a]; Enter(_i + 1); }
                    break;
                }
                case Kind.ThrIdle:
                {
                    // Idle: still after some movement, or 3 s still if it was already there.
                    if (!still) _movedSinceStep = true;
                    float need = _movedSinceStep ? 0.8f : 3f;
                    Progress = Mathf.Clamp01(_stillFor / need);
                    Status = still ? "HOLD…" : "MOVING";
                    if (_stillFor >= need) { for (int a = 1; a <= JoystickInput.AxisCount; a++) _snap[a] = _last[a]; Enter(_i + 1); for (int a = 1; a <= JoystickInput.AxisCount; a++) _snap[a] = _last[a]; }
                    break;
                }
                case Kind.ThrFull:
                {
                    int best = 0; float bestD = 0.5f;
                    for (int a = 1; a <= JoystickInput.AxisCount; a++) { if (Used(a)) continue; float d = Mathf.Abs(_last[a] - _snap[a]); if (d > bestD) { bestD = d; best = a; } }
                    if (best != _candidate) { _candidate = best; _stillFor = 0; }
                    Focus = best;
                    Progress = best == 0 ? 0 : Mathf.Clamp01(_stillFor / 0.6f);
                    Status = best == 0 ? (s.ctl == "throttle" ? "ALL THE WAY FORWARD" : "ALL THE WAY") : $"AXIS {best} — HOLD…";
                    if (best != 0 && _stillFor >= 0.6f)
                    {
                        var ax = Axis(s.ctl); ax.Index = best; ax.Centering = false; ax.NegEnd = _snap[best]; ax.PosEnd = _last[best]; ax.Center = _snap[best];
                        Enter(_i + 1);
                    }
                    break;
                }
                case Kind.Learn:
                {
                    // The true endpoints: the extremes seen on each assigned axis while it's stirred (6 s of movement).
                    foreach (var ax in new[] { _p.Aileron, _p.Elevator, _p.Rudder, _p.Throttle, _p.RudderTrim })
                    {
                        if (!ax.Set) continue;
                        float v = _last[ax.Index];
                        if (ax.PosEnd > ax.NegEnd) { ax.PosEnd = Mathf.Max(ax.PosEnd, v); ax.NegEnd = Mathf.Min(ax.NegEnd, v); }
                        else { ax.PosEnd = Mathf.Min(ax.PosEnd, v); ax.NegEnd = Mathf.Max(ax.NegEnd, v); }
                    }
                    if (!still) _learnMoving += dt;
                    Progress = Mathf.Clamp01(_learnMoving / 6f);
                    Status = still ? "KEEP MOVING EVERYTHING" : "LEARNING…";
                    if (_learnMoving >= 6f)
                    {
                        // Dead zones from the rest noise (2.5×, as a fraction of each half-travel), 2 … 12 %.
                        foreach (var ax in new[] { _p.Aileron, _p.Elevator, _p.Rudder, _p.RudderTrim })
                            if (ax.Set) ax.Dead = Mathf.Clamp(2.5f * _noise[ax.Index] / Mathf.Max(0.05f, Mathf.Min(Mathf.Abs(ax.PosEnd - ax.Center), Mathf.Abs(ax.NegEnd - ax.Center))), 0.02f, 0.12f);
                        Enter(_i + 1);
                    }
                    break;
                }
                case Kind.Button:
                {
                    if (!_buttonsClear) { _buttonsClear = !JoystickInput.AnyButtonHeld(); Status = "LET GO OF THE BUTTONS"; break; }
                    int b = JoystickInput.AnyButtonDown();
                    Status = "WAITING FOR A BUTTON";
                    if (b >= 0)
                    {
                        // One button, one action: taking a button from another action frees it there.
                        foreach (var kv in new List<KeyValuePair<string, int>>(_p.Buttons)) if (kv.Value == b) _p.Buttons.Remove(kv.Key);
                        _p.Buttons[s.ctl] = b; _p.AxisButtons.Remove(s.ctl);
                        Status = JoystickInput.ButtonName(b);
                        Enter(_i + 1);
                        break;
                    }
                    // An RC radio's button or switch: a free channel that jumps (one channel, one action).
                    for (int a = 1; a <= JoystickInput.AxisCount; a++)
                    {
                        if (Used(a) || SwitchTaken(a)) continue;
                        if (Mathf.Abs(_last[a] - _snap[a]) > 0.5f)
                        {
                            _p.Buttons.Remove(s.ctl); _p.AxisButtons[s.ctl] = new AxisSwitch { Axis = a, OnValue = _last[a], OffValue = _snap[a] };
                            Status = $"CHANNEL {a}";
                            Enter(_i + 1);
                            break;
                        }
                    }
                    break;
                }
                case Kind.Done:
                {
                    var r = _p.Read();
                    Status = $"AIL {r.ail * 100:+0;-0;0}  ELE {r.ele * 100:+0;-0;0}  RUD {r.rud * 100:+0;-0;0}" + (r.hasThr ? $"  THR {r.thr01 * 100:0}" : "");
                    Progress = 1;
                    break;
                }
            }
        }

        /// <summary>What's been learnt so far, one line per control (for the panels).</summary>
        public string Summary()
        {
            if (_p == null) return "";
            string A(string n, JoyAxis a) => a.Set ? $"{n} AXIS {a.Index}" : $"{n} —";
            string s = $"{A("AIL", _p.Aileron)} · {A("ELE", _p.Elevator)} · {A("RUD", _p.Rudder)} · {A("THR", _p.Throttle)}" + (_p.RudderTrim.Set ? $" · {A("R TRIM", _p.RudderTrim)}" : "") + (_p.FlapSwitch.Set ? $" · {A("FLAP SW", _p.FlapSwitch)}" : "");
            int nb = _p.Buttons.Count + _p.AxisButtons.Count;
            if (nb > 0) s += $" · {nb} BUTTONS";
            return s;
        }
    }
}
