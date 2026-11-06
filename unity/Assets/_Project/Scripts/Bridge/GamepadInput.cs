using FlyingGame.Core;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace FlyingGame.Bridge
{
    /// <summary>
    /// GAMEPAD — an MFi / Xbox / PlayStation / 8BitDo-style pad (Ty, 2026-11-02: an 8BitDo Pro 2 paired to the iPhone,
    /// "nothing works").
    ///
    /// Read through the Input System's <see cref="Gamepad"/>, not the old Input Manager. The old path saw a pad only
    /// as "joystick axes 1…n and buttons 0…19" whose order iOS decides, and the game flew from it only once a
    /// joystick PROFILE had been made in OPTIONS › JOYSTICK SETUP — with no profile <c>MobileJoystick</c> returned on
    /// its first line, which is why a freshly paired pad did nothing at all. The Input System names the controls
    /// (left stick, right trigger, Menu …) on every pad it knows, so there is nothing to calibrate and the mapping
    /// below is the same on every pad. The project runs BOTH input handlers (ProjectSettings activeInputHandler: 2),
    /// so every <c>Input.*</c> call elsewhere still works.
    ///
    /// The shaping is <see cref="GamepadShaping"/> (pure, tested in tools/FlightTests). This class only reads the pad
    /// once a frame and reports it. <see cref="TouchFlightControls"/> decides what it flies.
    /// </summary>
    public static class GamepadInput
    {
        /// <summary>One frame of the pad, shaped. Sticks and rudder are −1…1 in the PAD'S sense: right and pushed
        /// away from you are positive. <see cref="Throttle"/> is a RATE, −1…1 of the full rate (see
        /// <see cref="GamepadShaping.ThrottleRate"/>): a self-centring stick at rest leaves the power where it is.</summary>
        public struct Frame
        {
            public bool Connected;
            public float Roll, Pitch, Rudder, Throttle;
            public bool ThrottleUp, ThrottleDown, TrimNoseDown, TrimNoseUp, Brakes, Fire;
            public bool FlapsUpPressed, FlapsDownPressed, SmokePressed, ViewPressed, MenuPressed, RecordPressed;
            /// <summary>COCKPIT LOOK (Ty, 2026-11-02). No flight axis ever moves the view. The optional look is the
            /// d-pad while L3 (left stick click) is HELD -- trim and flaps are off for those frames -- and R3 (right
            /// stick click) recentres. Entering the cockpit view (Y) recentres too.</summary>
            public bool LookHeld, RecentrePressed;
            public int LookX, LookY;
        }

        // Inert outside the hudshots self-test; bypass physical input with a neutral connected frame.
        public static bool SimulateConnectedForTest;

        private static int _frame = -1;
        private static Frame _now;

        /// <summary>The pad's state this frame (read once, however many callers).</summary>
        public static Frame Now
        {
            get
            {
                if (SimulateConnectedForTest) return new Frame { Connected = true };
                if (_frame != Time.frameCount) { _frame = Time.frameCount; _now = LocalSplit.Active ? For(0) : Read(); }
                return _now;
            }
        }

        // ---- LOCAL 2-PLAYER (split screen): one pad per player, bound by device id ------------------------------------
        //
        // With split screen off nothing below runs and Now is the single-player read above, unchanged. With it on, Now is
        // player 1's pad and nobody else's (TouchFlightControls reads Now, so the first player's touch / pad path is the
        // same code as ever), and For(1) is the second player's. FlyingGame.Core.LocalMp.PadBinding decides who is who:
        // first pad P1, second pad P2, sticky while connected, a pad that reconnects gets its own seat back.

        private static readonly FlyingGame.Core.LocalMp.PadBinding _binding = new(2);
        private static readonly Frame[] _seatNow = new Frame[2];
        private static readonly System.Collections.Generic.List<int> _liveIds = new(4);
        private static int _seatFrame = -1;

        /// <summary>The pad of player <paramref name="player"/> (0 = P1) this frame; not connected when that seat has
        /// no pad. Without split screen, player 0 is <see cref="Now"/> and every other player has none.</summary>
        public static Frame For(int player)
        {
            if (!LocalSplit.Active) return player == 0 ? Now : default;
            if (SimulateConnectedForTest) return new Frame { Connected = true };
            if (player < 0 || player >= _seatNow.Length) return default;
            if (_seatFrame != Time.frameCount) { _seatFrame = Time.frameCount; ReadSeats(); }
            return _seatNow[player];
        }

        /// <summary>The name of player <paramref name="player"/>'s pad, or null.</summary>
        public static string NameFor(int player)
        {
#if ENABLE_INPUT_SYSTEM
            if (!LocalSplit.Active) return player == 0 ? ConnectedName : null;
            var p = PadById(_binding.PadFor(player));
            return p != null ? p.displayName ?? p.name : null;
#else
            return null;
#endif
        }

        private static void ReadSeats()
        {
#if ENABLE_INPUT_SYSTEM
            _liveIds.Clear();
            foreach (var g in Gamepad.all) if (g.added && g.enabled) _liveIds.Add(g.deviceId);
            if (_binding.Update(_liveIds))
                Debug.Log($"[Gamepad] split screen: P1 {NameOrNone(PadById(_binding.PadFor(0)))}, P2 {NameOrNone(PadById(_binding.PadFor(1)))}");
            for (int i = 0; i < _seatNow.Length; i++)
            {
                var p = PadById(_binding.PadFor(i));
                _seatNow[i] = p != null ? Read(p) : default;
            }
#endif
        }

        /// <summary>The pad's name, or null — for a status line.</summary>
        public static string ConnectedName
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                var p = Pad();
                return p != null ? p.displayName ?? p.name : null;
#else
                return null;
#endif
            }
        }

#if ENABLE_INPUT_SYSTEM
        /// <summary>
        /// The pad to read. A pad connected WHILE THE GAME RUNS needs nothing from us: the player's own GameController
        /// observer (GCControllerDidConnectNotification, compiled into UnityFramework) hands it to the Input System as
        /// an Added device, and an added device is made <see cref="Gamepad.current"/> on the spot
        /// (InputManager.AddDevice, Input System 1.20). What <c>current</c> alone can miss: it is only re-pointed when
        /// a pad SENDS INPUT or is added / removed, so it can be left on a pad that is disabled (the Input System
        /// switches pads off while the app is in the background, e.g. while pairing in Settings, and back on when it
        /// returns) while another live pad sits idle in <see cref="Gamepad.all"/>. So: <c>current</c> if it is live,
        /// else the live pad that spoke most recently.
        /// </summary>
        private static Gamepad Pad()
        {
            var c = Gamepad.current;
            if (c != null && c.added && c.enabled) return c;
            Gamepad best = null;
            foreach (var g in Gamepad.all)
                if (g.added && g.enabled && (best == null || g.lastUpdateTime > best.lastUpdateTime)) best = g;
            return best;
        }

        // THE MAPPING (Ty, 2026-11-02: "the right hand on the stick"), on every platform:
        //   RIGHT stick         roll and pitch (expo)
        //   LEFT stick X        rudder (expo), plus RT − LT, summed and clamped
        //   LEFT stick Y        throttle as a RATE (linear, no expo): up = more power, down = less, let go = holds
        //   RB / LB (held)      throttle up / down too, at a fixed rate
        //   d-pad up / down     pitch trim nose down / nose up (held)    d-pad right / left   flaps down / up a step
        //   A guns (held) · B smoke · X wheel brakes (held) · Y change view · Menu OPTIONS · View REC
        //   L3 held + d-pad     cockpit look (pan held, snap on a tap)   R3   recentre the cockpit view
        private static Frame Read() => Read(Pad());

        private static Gamepad PadById(int id)
        {
            if (id == FlyingGame.Core.LocalMp.PadBinding.NoPad) return null;
            foreach (var g in Gamepad.all) if (g.deviceId == id && g.added && g.enabled) return g;
            return null;
        }

        private static string NameOrNone(Gamepad p) => p != null ? $"{p.displayName} (#{p.deviceId})" : "none (parked)";

        private static Frame Read(Gamepad p)
        {
            var f = new Frame();
            if (p == null) return f;   // never connected, or gone: everything idle, which lets go of the controls
            f.Connected = true;
            Vector2 r = p.rightStick.ReadValue(), l = p.leftStick.ReadValue();
            var (roll, pitch) = GamepadShaping.Stick(r.x, r.y);
            f.Roll = (float)roll; f.Pitch = (float)pitch;
            f.Rudder = (float)GamepadShaping.Rudder(l.x, p.leftTrigger.ReadValue(), p.rightTrigger.ReadValue());
            f.Throttle = (float)GamepadShaping.ThrottleRate(l.y);
            f.ThrottleUp = p.rightShoulder.isPressed;
            f.ThrottleDown = p.leftShoulder.isPressed;
            f.LookHeld = p.leftStickButton.isPressed;
            f.RecentrePressed = p.rightStickButton.wasPressedThisFrame;
            if (f.LookHeld)
            {
                // L3 held: the d-pad looks instead of trimming / flapping (right and up positive, like the hat).
                f.LookX = (p.dpad.right.isPressed ? 1 : 0) - (p.dpad.left.isPressed ? 1 : 0);
                f.LookY = (p.dpad.up.isPressed ? 1 : 0) - (p.dpad.down.isPressed ? 1 : 0);
            }
            else
            {
                f.TrimNoseDown = p.dpad.up.isPressed;
                f.TrimNoseUp = p.dpad.down.isPressed;
                f.FlapsUpPressed = p.dpad.left.wasPressedThisFrame;
                f.FlapsDownPressed = p.dpad.right.wasPressedThisFrame;
            }
            f.Fire = p.buttonSouth.isPressed;
            f.SmokePressed = p.buttonEast.wasPressedThisFrame;
            f.Brakes = p.buttonWest.isPressed;
            f.ViewPressed = p.buttonNorth.wasPressedThisFrame;
            f.MenuPressed = p.startButton.wasPressedThisFrame;
            f.RecordPressed = p.selectButton.wasPressedThisFrame;
            return f;
        }

        // Connect / disconnect / enable are logged so a "nothing works" report can be answered from the device log.
        // Nothing else needs the event: Pad() is asked again every frame, so a pad that arrives is flown from on its
        // first frame, and one that goes away is read as idle, which releases whatever it was holding.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Watch()
        {
            foreach (var g in Gamepad.all) Debug.Log($"[Gamepad] present: {g.displayName} ({g.layout})");
            InputSystem.onDeviceChange += (d, change) =>
            {
                if (d is not Gamepad) return;
                if (change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected)
                    Debug.Log($"[Gamepad] connected: {d.displayName} ({d.layout}) · {Gamepad.all.Count} pad(s)");
                else if (change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected)
                    Debug.Log($"[Gamepad] disconnected: {d.displayName} · {Gamepad.all.Count} pad(s) left");
                else if (change == InputDeviceChange.Enabled || change == InputDeviceChange.Disabled)
                    Debug.Log($"[Gamepad] {d.displayName} {(change == InputDeviceChange.Enabled ? "enabled" : "disabled")}");
            };
        }
#else
        private static Frame Read() => default;
#endif
    }
}
