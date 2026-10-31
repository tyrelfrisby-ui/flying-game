using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Which seat the pilot is in and where he is looking from it (owner 2026-10-31: "Codex is going to provide a
    /// view point for each seat in each airplane. Build the code necessary for looking around either by buttons like
    /// a hat control on a stick or by goggles").
    ///
    /// The arithmetic is <see cref="CockpitLook"/>, which is pure and tested without a device. This component is only
    /// the wiring: it picks the seat, collects input from three places, and hands <see cref="ChaseCamera"/> an eye
    /// point and a look direction.
    ///
    ///  • THE HAT on a real stick. JoystickInput already reports it as four buttons from
    ///    <see cref="JoystickInput.HatButton0"/> in the order up, right, down, left, and no action is bound to them,
    ///    so the hat does what a hat does. Held pans; a tap under <see cref="TapSeconds"/> snaps to that direction,
    ///    which is how a pilot actually looks over his shoulder on base.
    ///  • BUTTONS ON GLASS, for a device with no stick: <see cref="OnScreenHat"/> is set by the view panel.
    ///  • GOGGLES: the device's own attitude, zeroed wherever the pilot holds it. Off unless he asks for it, because
    ///    a phone lying on a desk reporting gravity is not a head.
    ///
    /// Seats come from the aircraft config's authored seats, so an aeroplane whose seats Codex has not authored yet
    /// falls back to one seat at the camera's own estimate of the cockpit and nothing breaks.
    /// </summary>
    public sealed class CockpitView : MonoBehaviour
    {
        public FlightSimDriver Driver;

        /// <summary>The look itself. The camera reads it; everything here writes it.</summary>
        public readonly CockpitLook Look = new();

        /// <summary>Which authored seat the pilot is in. Clamped to what the aeroplane has.</summary>
        public int SeatIndex { get; private set; }

        /// <summary>Head tracking from the device's attitude. The pilot turns this on.</summary>
        public bool Goggles { get; private set; }

        /// <summary>Set by the view panel each frame while an on-screen look button is held: right and up positive.</summary>
        public Vector2Int OnScreenHat;

        /// <summary>A hat push shorter than this is a tap — snap to that direction — rather than a pan.</summary>
        public const float TapSeconds = 0.22f;

        private const string PrefGoggles = "view.goggles";
        private const string PrefSpring = "view.springCentre";

        private float _hatHeld;
        private int _hatX, _hatY;
        private bool _hadHat;

        private void Start()
        {
            if (Driver == null) Driver = GetComponent<FlightSimDriver>();
            Goggles = PlayerPrefs.GetInt(PrefGoggles, 0) == 1;
            Look.SpringCentre = PlayerPrefs.GetInt(PrefSpring, 0) == 1;
            if (Driver != null) Driver.AircraftChanged += OnAircraftChanged;
            if (Goggles) EnableDeviceAttitude(true);
        }

        private void OnDestroy()
        {
            if (Driver != null) Driver.AircraftChanged -= OnAircraftChanged;
        }

        /// <summary>A different aeroplane has different seats: keep the index legal and look ahead again.</summary>
        private void OnAircraftChanged()
        {
            SeatIndex = Mathf.Clamp(SeatIndex, 0, Mathf.Max(0, SeatCount - 1));
            Look.Centre();
        }

        // ---- the seats ----

        private AircraftConfig Config => Driver?.Sim?.Aircraft?.Config;

        /// <summary>The authored seats, or an empty list for a type Codex has not authored yet.</summary>
        public System.Collections.Generic.List<StationConfig> Seats =>
            Config?.Loading?.Seats ?? new System.Collections.Generic.List<StationConfig>();

        public int SeatCount => Mathf.Max(1, Seats.Count);

        public string SeatLabel
        {
            get
            {
                var seats = Seats;
                if (seats.Count == 0) return "Cockpit";
                int i = Mathf.Clamp(SeatIndex, 0, seats.Count - 1);
                return string.IsNullOrEmpty(seats[i].Label) ? $"Seat {i + 1}" : seats[i].Label;
            }
        }

        public void SelectSeat(int index)
        {
            SeatIndex = Mathf.Clamp(index, 0, Mathf.Max(0, SeatCount - 1));
            Look.Centre();
        }

        public void NextSeat() => SelectSeat((SeatIndex + 1) % SeatCount);

        /// <summary>True when this aeroplane's seats carry authored view points, so the camera is sitting where Codex
        /// put the pilot's eye rather than where the camera guessed.</summary>
        public bool HasAuthoredViewPoint
        {
            get
            {
                var seats = Seats;
                if (seats.Count == 0) return false;
                return seats[Mathf.Clamp(SeatIndex, 0, seats.Count - 1)].HasEye;
            }
        }

        /// <summary>The eye, in the aeroplane's own local space, for <see cref="ChaseCamera"/>. Falls back to the
        /// camera's existing cockpit estimate for a type with no authored seats.</summary>
        public Vector3 EyeLocal
        {
            get
            {
                var seats = Seats;
                if (seats.Count == 0) return PilotEgress.CockpitLocal(Driver) + Vector3.up * 0.1f;
                StationConfig seat = seats[Mathf.Clamp(SeatIndex, 0, seats.Count - 1)];
                // The config's body frame is +x forward, +y right, +z DOWN; Unity local is the same swizzle the sim
                // uses for the world (CoordinateMap.ToUnity): x = y, y = -z, z = x.
                return CoordinateMap.ToUnity(seat.EyeVec());
            }
        }

        // ---- input ----

        private void Update()
        {
            float dt = Time.deltaTime;
            if (SessionSettings.MenuOpen) { _hadHat = false; _hatHeld = 0f; return; }

            if (Goggles)
            {
                // The device's attitude IS the head. Nothing else may write the look while goggles are on, or the
                // two fight each other every frame.
                if (TryDeviceAttitude(out Quaternion q)) Look.FromDevice(ToCore(q));
                return;
            }

            // The stick's hat, then the buttons on glass. Either may be the only one present.
            int x = 0, y = 0;
            if (JoystickInput.ButtonHeld(JoystickInput.HatButton0 + 0)) y += 1;   // up
            if (JoystickInput.ButtonHeld(JoystickInput.HatButton0 + 1)) x += 1;   // right
            if (JoystickInput.ButtonHeld(JoystickInput.HatButton0 + 2)) y -= 1;   // down
            if (JoystickInput.ButtonHeld(JoystickInput.HatButton0 + 3)) x -= 1;   // left
            if (x == 0 && y == 0) { x = OnScreenHat.x; y = OnScreenHat.y; }

            bool hat = x != 0 || y != 0;
            if (hat)
            {
                _hatHeld += dt;
                _hatX = x; _hatY = y;
                _hadHat = true;
                // Past the tap window it becomes a pan. Inside it, nothing moves yet — that is what lets a tap be a
                // tap rather than a very short pan.
                if (_hatHeld > TapSeconds) Look.Hat(x, y, dt);
            }
            else
            {
                if (_hadHat && _hatHeld <= TapSeconds) Look.Snap(DirectionOf(_hatX, _hatY));
                _hadHat = false;
                _hatHeld = 0f;
                Look.Hat(0, 0, dt);   // lets SpringCentre do its work
            }
        }

        /// <summary>Which named direction a hat push means. Pushed the way he is already looking, a pilot wants the
        /// furthest he can get: that is what makes one tap "over my shoulder".</summary>
        private CockpitLook.Direction DirectionOf(int x, int y)
        {
            if (x < 0 && y > 0) return CockpitLook.Direction.UpLeft;
            if (x > 0 && y > 0) return CockpitLook.Direction.UpRight;
            if (y > 0) return CockpitLook.Direction.Up;
            if (y < 0) return CockpitLook.Direction.Down;
            if (x < 0) return Look.YawDeg < -45.0 ? CockpitLook.Direction.OverLeftShoulder : CockpitLook.Direction.Left;
            if (x > 0) return Look.YawDeg > 45.0 ? CockpitLook.Direction.OverRightShoulder : CockpitLook.Direction.Right;
            return CockpitLook.Direction.Centre;
        }

        public void Centre()
        {
            if (Goggles && TryDeviceAttitude(out Quaternion q)) Look.Recentre(ToCore(q));
            else Look.Centre();
        }

        public void SetGoggles(bool on)
        {
            if (Goggles == on) return;
            Goggles = on;
            PlayerPrefs.SetInt(PrefGoggles, on ? 1 : 0);
            PlayerPrefs.Save();
            EnableDeviceAttitude(on);
            Look.Centre();
            // Zero them wherever he is holding the device right now, so switching on does not swing the view.
            if (on && TryDeviceAttitude(out Quaternion q)) Look.Recentre(ToCore(q));
        }

        public void SetSpringCentre(bool on)
        {
            Look.SpringCentre = on;
            PlayerPrefs.SetInt(PrefSpring, on ? 1 : 0);
            PlayerPrefs.Save();
        }

        // ---- the device's attitude ----

        /// <summary>Whether this device can report its own attitude at all. A desktop cannot, so the goggles option
        /// does not offer itself there.</summary>
        public static bool DeviceAttitudeAvailable => SystemInfo.supportsGyroscope;

        // The project is on the old input handler (ProjectSettings activeInputHandler: 0), so Input.gyro is the API.
        // Deliberately no InputSystem.AttitudeSensor branch: it would be dead code today and a compile error the day
        // someone switches the handler without the package installed.
        private static void EnableDeviceAttitude(bool on)
        {
            if (SystemInfo.supportsGyroscope) Input.gyro.enabled = on;
        }

        private static bool TryDeviceAttitude(out Quaternion q)
        {
            q = Quaternion.identity;
            if (!SystemInfo.supportsGyroscope || !Input.gyro.enabled) return false;
            q = Input.gyro.attitude;
            return true;
        }

        /// <summary>Unity's quaternion into the sim's. Only the CHANGE since the last recentre is ever used
        /// (CockpitLook.FromDevice), so the handedness of the absolute frame does not matter and is deliberately not
        /// converted — which is also why this needs no device to test.</summary>
        private static FlyingGame.Core.MathTypes.Quat ToCore(Quaternion q) =>
            new FlyingGame.Core.MathTypes.Quat(q.x, q.y, q.z, q.w);
    }
}
