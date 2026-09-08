using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Landing page (owner request): pick the aircraft, where to start (in the air, or on the runway at
    /// any of the four airports), a challenge or free flight, and the conditions (wind, turbulence,
    /// temperature, thermals). Shown at launch and from the MENU button; the sim is paused while open.
    /// OnGUI, DPI-scaled, styles built from scratch (iOS strips the default skin).
    /// </summary>
    public sealed class StartMenu : MonoBehaviour
    {
        public FlightSimDriver Driver;
        public WeatherController Weather;
        public ChallengeController Challenges;
        public TowController Tow;
        public RaceController Race;
        public StolController Stol;

        public bool IsOpen { get; private set; } = true;

        private GUIStyle _title, _head, _btn, _btnOn, _label, _small;
        private Texture2D _bg, _btnBg, _btnOnBg;
        private int _fs;
        private Vector2 _fleetScroll;

        private void Start()
        {
            Open();
        }

        public void Open()
        {
            IsOpen = true;
            SessionSettings.MenuOpen = true;
            Time.timeScale = 0f;
        }

        private void Fly()
        {
            IsOpen = false;
            SessionSettings.MenuOpen = false;
            Time.timeScale = 1f;
            SessionSettings.ApplyWeather();
            if (Weather != null) Weather.SyncFromSession();
            Driver.ApplySession();               // aircraft + start position (fires AircraftChanged)
            Race?.End(); Stol?.End();
            string ch = SessionSettings.ChallengeId;
            if (ch == "event:race" && Race != null) Race.Begin();
            else if (ch == "event:stol" && Stol != null) Stol.Begin(SessionSettings.Airport);
            else if (ch != null && !SessionSettings.IsEvent(ch) && Challenges != null) Challenges.StartById(ch);
        }

        private void EnsureStyles()
        {
            int fs = Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) * 0.026f);
            if (_btn != null && fs == _fs) return;
            _fs = fs;
            _bg ??= Solid(new Color(0.05f, 0.08f, 0.12f, 0.92f));
            _btnBg ??= Solid(new Color(0.18f, 0.24f, 0.32f, 0.95f));
            _btnOnBg ??= Solid(new Color(0.2f, 0.62f, 0.35f, 0.95f));
            Font f = UiFont.Get();
            _title = new GUIStyle { font = f, fontSize = Mathf.RoundToInt(fs * 1.6f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            _head = new GUIStyle { font = f, fontSize = Mathf.RoundToInt(fs * 1.05f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.55f, 0.95f, 1f) } };
            _btn = new GUIStyle { font = f, fontSize = fs, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white, background = _btnBg }, active = { textColor = Color.white, background = _btnBg }, padding = new RectOffset(6, 6, 4, 4) };
            _btnOn = new GUIStyle(_btn) { fontStyle = FontStyle.Bold, normal = { textColor = Color.white, background = _btnOnBg }, active = { textColor = Color.white, background = _btnOnBg } };
            _label = new GUIStyle { font = f, fontSize = fs, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            _small = new GUIStyle { font = f, fontSize = Mathf.RoundToInt(fs * 0.8f), alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.8f, 0.85f, 0.9f) } };
        }

        private void OnGUI()
        {
            EnsureStyles();
            float s = Mathf.Min(Screen.width, Screen.height);
            if (!IsOpen)
            {
                // Small MENU button top-left.
                float mbw = s * 0.11f, mbh = s * 0.055f;
                if (GUI.Button(new Rect(s * 0.02f, s * 0.02f, mbw, mbh), "MENU", _btn)) Open();
                return;
            }

            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _bg);
            float m = s * 0.03f, w = Screen.width - 2 * m;
            float lh = _fs * 1.7f, gap = _fs * 0.5f;
            GUI.Label(new Rect(m, m * 0.6f, w, lh * 1.3f), "FLIGHT SETUP", _title);
            float top = m * 0.6f + lh * 1.5f;
            float colW = (w - 3 * gap) / 4f;

            // ---- column 1: aircraft
            float x = m, y = top;
            GUI.Label(new Rect(x, y, colW, lh), "AIRCRAFT", _head); y += lh;
            float bh = lh * 1.05f;
            foreach ((string id, string name) in SessionSettings.Fleet)
            {
                bool on = SessionSettings.AircraftId == id;
                if (GUI.Button(new Rect(x, y, colW, bh), name, on ? _btnOn : _btn)) SessionSettings.AircraftId = id;
                y += bh + gap * 0.4f;
            }

            // ---- column 2: start
            x = m + colW + gap; y = top;
            GUI.Label(new Rect(x, y, colW, lh), "START", _head); y += lh;
            if (GUI.Button(new Rect(x, y, colW, bh), "In the air", SessionSettings.StartMode == SessionSettings.Start.InTheAir ? _btnOn : _btn)) SessionSettings.StartMode = SessionSettings.Start.InTheAir;
            y += bh + gap * 0.4f;
            if (GUI.Button(new Rect(x, y, colW, bh), "On the runway", SessionSettings.StartMode == SessionSettings.Start.OnTheRunway ? _btnOn : _btn)) SessionSettings.StartMode = SessionSettings.Start.OnTheRunway;
            y += bh + gap;
            GUI.Label(new Rect(x, y, colW, lh), "AIRPORT", _head); y += lh;
            for (int i = 0; i < FlyingGame.Core.WorldTerrain.Airports.Length; i++)
            {
                var a = FlyingGame.Core.WorldTerrain.Airports[i];
                string txt = $"{a.Name}  {a.ElevationM * 3.28084:N0} ft";
                if (GUI.Button(new Rect(x, y, colW, bh), txt, SessionSettings.AirportIndex == i ? _btnOn : _btn)) SessionSettings.AirportIndex = i;
                y += bh + gap * 0.4f;
            }
            if (SessionSettings.AircraftId == "glider-2-33-like")
            {
                y += gap;
                GUI.Label(new Rect(x, y, colW, lh), "TOW PLANE", _head); y += lh;
                float half = (colW - gap * 0.4f) / 2f;
                if (GUI.Button(new Rect(x, y, half, bh), "Pawnee", SessionSettings.TugId == "pa25-pawnee-like" ? _btnOn : _btn)) SessionSettings.TugId = "pa25-pawnee-like";
                if (GUI.Button(new Rect(x + half + gap * 0.4f, y, half, bh), "Super Cub", SessionSettings.TugId == "pa18-cub-like" ? _btnOn : _btn)) SessionSettings.TugId = "pa18-cub-like";
                y += bh + gap * 0.4f;
            }
            y += gap;
            GUI.Label(new Rect(x, y, colW, lh * 2f), SessionSettings.StartMode == SessionSettings.Start.InTheAir
                ? "Airborne 2,000 ft over the field, trimmed."
                : (SessionSettings.AircraftId == "glider-2-33-like" ? "At the threshold. Tap TOW for the aerotow."
                   : SessionSettings.AircraftId == "pa18-floats-like" ? "Afloat on the field's lake, engine idling." : "At the threshold, engine idling."), _small);

            // ---- column 3: challenge
            x = m + 2 * (colW + gap); y = top;
            GUI.Label(new Rect(x, y, colW, lh), "CHALLENGE", _head); y += lh;
            foreach ((string id, string name) in SessionSettings.Challenges)
            {
                bool on = SessionSettings.ChallengeId == id;
                if (GUI.Button(new Rect(x, y, colW, bh), name, on ? _btnOn : _btn)) SessionSettings.ChallengeId = id;
                y += bh + gap * 0.4f;
            }

            // ---- column 4: conditions
            x = m + 3 * (colW + gap); y = top;
            GUI.Label(new Rect(x, y, colW, lh), "CONDITIONS", _head); y += lh;
            y = Slider(x, y, colW, "Wind from", $"{SessionSettings.WindFromDeg:000}°", ref SessionSettings.WindFromDeg, 0f, 359f, 15f);
            y = Slider(x, y, colW, "Wind speed", $"{SessionSettings.WindSpeedMs * 1.944f:F0} kt", ref SessionSettings.WindSpeedMs, 0f, 20f, 1f);
            float turb = SessionSettings.TurbulenceLevel;
            y = Slider(x, y, colW, "Turbulence", new[] { "Calm", "Light", "Moderate", "Severe" }[SessionSettings.TurbulenceLevel], ref turb, 0f, 3f, 1f);
            SessionSettings.TurbulenceLevel = Mathf.RoundToInt(turb);
            float surfF = (15f + SessionSettings.IsaDeviationC) * 1.8f + 32f;
            y = Slider(x, y, colW, "Temperature", $"{surfF:F0}°F at sea level", ref SessionSettings.IsaDeviationC, -30f, 30f, 1f);
            y = Slider(x, y, colW, "Thermals", SessionSettings.ThermalScale <= 0.01f ? "Off" : $"{SessionSettings.ThermalScale:F1}×", ref SessionSettings.ThermalScale, 0f, 2f, 0.25f);

            // ---- FLY
            float fw = s * 0.26f, fh = s * 0.09f;
            if (GUI.Button(new Rect(Screen.width - m - fw, Screen.height - m - fh, fw, fh), "FLY", _btnOn)) Fly();
        }

        /// <summary>A labelled -/+ stepper (touch-friendly; IMGUI sliders are fiddly on a phone).</summary>
        private float Slider(float x, float y, float w, string label, string value, ref float v, float min, float max, float step)
        {
            float lh = _fs * 1.7f, bw = lh * 1.2f;
            GUI.Label(new Rect(x, y, w, lh), label, _label);
            y += lh;
            if (GUI.Button(new Rect(x, y, bw, lh), "−", _btn)) v = Mathf.Clamp(v - step, min, max);
            GUI.Label(new Rect(x + bw + _fs * 0.4f, y, w - 2 * bw - _fs * 0.8f, lh), value, new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter });
            if (GUI.Button(new Rect(x + w - bw, y, bw, lh), "+", _btn)) v = Mathf.Clamp(v + step, min, max);
            if (label == "Wind from" && v >= 359f) v = 0f;
            return y + lh + _fs * 0.5f;
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            t.SetPixels(new[] { c, c, c, c }); t.Apply();
            return t;
        }
    }
}
