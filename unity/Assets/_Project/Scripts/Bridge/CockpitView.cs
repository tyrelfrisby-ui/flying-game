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
    ///  • THE DEVICE ITSELF: its own attitude, zeroed wherever the pilot holds it, at a gain he sets. Off unless he
    ///    asks for it, because a phone lying on a desk reporting gravity is not a head. It runs ALONGSIDE the two
    ///    above (owner Ty, 2026-11-01) — CockpitLook keeps the hat's offset and the device's apart and sums them —
    ///    so a snap over the shoulder and a lean of the phone add up instead of cancelling.
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

        // THE SETTINGS ARE STATIC, and deliberately so. Options must be able to show and change them whether or
        // not a CockpitView exists right now -- on the menu, in an outside view, before an aeroplane is loaded
        // (owner Ty via Aero, 2026-11-01: "show them regardless of the current view, applying when cockpit view is
        // active"). The instance below follows them; it does not own them.

        private static bool _settingsLoaded, _deviceLook, _springCentre;
        private static float _deviceGain = (float)CockpitLook.MinDeviceGain;

        private static void LoadSettings()
        {
            if (_settingsLoaded) return;
            _settingsLoaded = true;
            _deviceLook = PlayerPrefs.GetInt(PrefDeviceLook, PlayerPrefs.GetInt(PrefGoggles, 0)) == 1;
            _deviceGain = Mathf.Clamp((float)PlayerPrefs.GetFloat(PrefGain, (float)CockpitLook.MinDeviceGain),
                                      (float)CockpitLook.MinDeviceGain, (float)CockpitLook.MaxDeviceGain);
            _springCentre = PlayerPrefs.GetInt(PrefSpring, 0) == 1;
        }

        /// <summary>Head tracking from the device's attitude: turn the phone, the view pans. Set in Options, read
        /// by whichever CockpitView happens to exist; it applies the moment the cockpit view is active.</summary>
        public static bool DeviceLookEnabled
        {
            get { LoadSettings(); return _deviceLook; }
            set
            {
                LoadSettings();
                if (_deviceLook == value) return;
                _deviceLook = value;
                PlayerPrefs.SetInt(PrefDeviceLook, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Degrees of view per degree of device, 1..10, continuous.</summary>
        public static float DeviceLookGain
        {
            get { LoadSettings(); return _deviceGain; }
            set
            {
                LoadSettings();
                float g = Mathf.Clamp(value, (float)CockpitLook.MinDeviceGain, (float)CockpitLook.MaxDeviceGain);
                if (Mathf.Approximately(_deviceGain, g)) return;
                _deviceGain = g;
                PlayerPrefs.SetFloat(PrefGain, g);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Let go of the look pad and the view returns to straight ahead.</summary>
        public static bool SpringCentreEnabled
        {
            get { LoadSettings(); return _springCentre; }
            set
            {
                LoadSettings();
                if (_springCentre == value) return;
                _springCentre = value;
                PlayerPrefs.SetInt(PrefSpring, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>This instance's view of the setting, for the view panel.</summary>
        public bool DeviceLook => DeviceLookEnabled;
        public float DeviceGain => DeviceLookGain;

        /// <summary>Set by the view panel each frame while an on-screen look button is held: right and up positive.</summary>
        public Vector2Int OnScreenHat;

        /// <summary>A hat push shorter than this is a tap — snap to that direction — rather than a pan.</summary>
        public const float TapSeconds = 0.22f;

        private const string PrefDeviceLook = "view.deviceLook";
        private const string PrefGoggles = "view.goggles";     // what it was called before 2026-11-01
        private const string PrefGain = "view.deviceGain";
        private const string PrefSpring = "view.springCentre";

        private float _hatHeld;
        private int _hatX, _hatY;
        private bool _hadHat;

        private void Start()
        {
            if (Driver == null) Driver = GetComponent<FlightSimDriver>();
            LoadSettings();
            if (Driver != null) Driver.AircraftChanged += OnAircraftChanged;
            ApplySettings();
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
            // Settings first, and even while the menu is open: the pilot is standing in Options moving the slider,
            // and the gyro has to come on there so the view is already following him when he closes it.
            ApplySettings();
            if (SessionSettings.MenuOpen) { _hadHat = false; _hatHeld = 0f; return; }

            // The device first, then the hat. They do NOT exclude each other: CockpitLook keeps the two offsets
            // apart and sums them, so holding the phone turned and tapping the pad compose the way a pilot's neck
            // and his seat do. (Before 2026-11-01 the device took the frame and returned here, which meant turning
            // the option on silently disabled the look pad.)
            if (DeviceLook && TryDeviceAttitude(out Quaternion q)) Look.FromDevice(ToCore(q));

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

        /// <summary>RECENTRE: straight ahead is here. With device look on that means re-zeroing against however the
        /// device is being held, not just dropping the hat's offset — otherwise the view would spring back to
        /// wherever the phone points and the button would look broken.</summary>
        public void Centre()
        {
            if (DeviceLook && TryDeviceAttitude(out Quaternion q)) Look.Recentre(ToCore(q));
            else Look.Centre();
        }

        public void SetDeviceLook(bool on) => DeviceLookEnabled = on;

        /// <summary>Carry whatever Options currently says into this instance: the gyro on or off, the gain, the
        /// spring. Called at Start and every frame, because the setting can be changed from a menu that has no
        /// idea whether a cockpit exists.</summary>
        private void ApplySettings()
        {
            Look.DeviceGain = DeviceLookGain;
            Look.SpringCentre = SpringCentreEnabled;
            bool want = DeviceLookEnabled;
            if (want == _deviceLookApplied) return;
            _deviceLookApplied = want;
            EnableDeviceAttitude(want);
            Look.Centre();
            if (want)
            {
                // Zero it wherever he is holding the device right now, so switching on does not swing the view.
                if (TryDeviceAttitude(out Quaternion q)) Look.Recentre(ToCore(q));
            }
            else
            {
                // Switching off must not leave the device's last offset holding the view off-centre with nothing
                // driving it any more.
                Look.ForgetDevice();
            }
        }

        private bool _deviceLookApplied;

        /// <summary>Degrees of view per degree of device, 1..10 (owner Ty, 2026-11-01). Continuous; CockpitLook
        /// clamps it, and this only has to persist it.</summary>
        public void SetDeviceGain(float gain) => DeviceLookGain = gain;

        public void SetSpringCentre(bool on) => SpringCentreEnabled = on;

        // ---- the device's attitude ----

        /// <summary>Whether this device can report its own attitude at all. A desktop cannot, so the option does
        /// not offer itself there.</summary>
        public static bool DeviceAttitudeAvailable => SystemInfo.supportsGyroscope;

        // NOTHING ELSE IN THE GAME READS THE DEVICE'S MOTION (checked 2026-11-01: Input.gyro, Input.acceleration and
        // the InputSystem sensors appear in this file and nowhere else), so tilting the phone moves the VIEW and
        // only the view — there is no tilt steering for it to double-count against. If one is ever added, it must
        // take its own reference and its own zero: feeding the same attitude to both would make looking out of the
        // left window also roll the aeroplane left, which is the one way this feature can go badly wrong.
        //
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

        /// <summary>
        /// Unity's quaternion into the sim's, component for component, deliberately NOT converted to Unity's
        /// left-handed frame.
        ///
        /// `Input.gyro.attitude` carries CoreMotion's own numbers: a RIGHT-handed frame whose identity is the
        /// phone lying flat with its screen up, +x across the screen, +y toward the top of the screen, +z out of
        /// the screen and therefore vertical. CockpitLook works in exactly that frame — it rotates the device's
        /// own -z (the back of the phone, where it is aimed) and resolves the result against +z as world up — and
        /// only ever returns two scalar angles, so the usual (x, y, -z, -w) swap and the 90 degree X rotation that
        /// an AR camera needs would be undone again immediately.
        ///
        /// If a platform ever reports a frame whose vertical is not +z, CockpitLook.BackOf and ViewDeltaOf are the
        /// two places that say so, and the symptom would be yaw and pitch trading places — not the mush that
        /// reading angles out of the raw delta produced.
        /// </summary>
        private static FlyingGame.Core.MathTypes.Quat ToCore(Quaternion q) =>
            new FlyingGame.Core.MathTypes.Quat(q.x, q.y, q.z, q.w);
    }
}
