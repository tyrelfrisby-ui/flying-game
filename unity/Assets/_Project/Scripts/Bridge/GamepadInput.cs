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
        /// away from you are positive.</summary>
        public struct Frame
        {
            public bool Connected;
            public float Roll, Pitch, Rudder;
            public bool ThrottleUp, ThrottleDown, TrimNoseDown, TrimNoseUp, Brakes, Fire;
            public bool FlapsUpPressed, FlapsDownPressed, SmokePressed, ViewPressed, MenuPressed, RecordPressed;
        }

        private static int _frame = -1;
        private static Frame _now;

        /// <summary>The pad's state this frame (read once, however many callers).</summary>
        public static Frame Now
        {
            get
            {
                if (_frame != Time.frameCount) { _frame = Time.frameCount; _now = Read(); }
                return _now;
            }
        }

        /// <summary>The pad's name, or null — for a status line.</summary>
        public static string ConnectedName
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                var p = Gamepad.current;
                return p != null && p.added ? p.displayName ?? p.name : null;
#else
                return null;
#endif
            }
        }

#if ENABLE_INPUT_SYSTEM
        private static Frame Read()
        {
            var f = new Frame();
            var p = Gamepad.current;
            if (p == null || !p.added) return f;   // never connected, or disconnected: everything idle
            f.Connected = true;
            Vector2 l = p.leftStick.ReadValue();
            var (roll, pitch) = GamepadShaping.Stick(l.x, l.y);
            f.Roll = (float)roll; f.Pitch = (float)pitch;
            f.Rudder = (float)GamepadShaping.Rudder(p.leftTrigger.ReadValue(), p.rightTrigger.ReadValue(), p.rightStick.ReadValue().x);
            f.ThrottleUp = p.rightShoulder.isPressed;
            f.ThrottleDown = p.leftShoulder.isPressed;
            f.TrimNoseDown = p.dpad.up.isPressed;
            f.TrimNoseUp = p.dpad.down.isPressed;
            f.FlapsUpPressed = p.dpad.left.wasPressedThisFrame;
            f.FlapsDownPressed = p.dpad.right.wasPressedThisFrame;
            f.Fire = p.buttonSouth.isPressed;
            f.SmokePressed = p.buttonEast.wasPressedThisFrame;
            f.Brakes = p.buttonWest.isPressed;
            f.ViewPressed = p.buttonNorth.wasPressedThisFrame;
            f.MenuPressed = p.startButton.wasPressedThisFrame;
            f.RecordPressed = p.selectButton.wasPressedThisFrame;
            return f;
        }

        // Connect / disconnect are logged so a "nothing works" report can be answered from the device log. Nothing
        // else needs the event: Gamepad.current follows the most recently used pad, and a pad that goes away is
        // read as idle on the next frame, which releases whatever it was holding.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Watch()
        {
            foreach (var g in Gamepad.all) Debug.Log($"[Gamepad] present: {g.displayName} ({g.layout})");
            InputSystem.onDeviceChange += (d, change) =>
            {
                if (d is not Gamepad) return;
                if (change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected)
                    Debug.Log($"[Gamepad] connected: {d.displayName} ({d.layout})");
                else if (change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected)
                    Debug.Log($"[Gamepad] disconnected: {d.displayName}");
            };
        }
#else
        private static Frame Read() => default;
#endif
    }
}
