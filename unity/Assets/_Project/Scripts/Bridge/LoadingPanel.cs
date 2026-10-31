using FlyingGame.Core.DataContracts;
using FlyingGame.Core.Loading;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// THE LOAD SHEET. Owner, 2026-10-30: *"a true weight and balance system where you can load the airplane with
    /// occupants, luggage, or cargo and fuel, and the weight and balance is plotted and the flight characteristics are
    /// adjusted appropriately."*
    ///
    /// Put people in the seats, bags in the hold and fuel in the tanks, and watch the point move across the aeroplane's
    /// own certificated envelope. It is reached from the landing page's AIRCRAFT chapter, before you fly, because that is
    /// when a pilot does it.
    ///
    /// This is NOT the Aero Widget's weight-and-balance control (<see cref="Widget.WidgetLoading"/>), and both are
    /// wanted. That one is a teaching instrument: two abstract sliders, gross weight and CG, that will happily take the
    /// aeroplane outside its limits to show you why the limits are there. This one is the real thing — stations with
    /// arms, tanks with capacities, and a load that has to add up.
    ///
    /// **The envelope plot shows where the aeroplane is, and that is not a breach of the chart rule.** Charts in this
    /// project never show own-ship position (owner rule 2026-10-18), because finding out where you are is the game. A
    /// weight-and-balance envelope is not a navigation chart: it carries no geography, and without the loaded point on
    /// it there is nothing to read. The rule is about the map.
    ///
    /// A type with no `loading` block in its config cannot be loaded and says so. Most of the fleet is in that state;
    /// each one needs its published loading table (see docs/WEIGHT-AND-BALANCE.md).
    /// </summary>
    public sealed class LoadingPanel : MonoBehaviour
    {
        public FlightSimDriver Driver;

        public static bool IsOpen { get; private set; }
        public static void Open() => IsOpen = true;
        public static void Close() => IsOpen = false;

        private GUIStyle _btn, _btnOn, _head, _label, _small, _warn, _good, _tiny;
        private Texture2D _bg, _btnBg, _btnOnBg, _white, _plotBg;
        private int _fs;
        private Vector2 _scroll;
        private float _measured;

        private static Texture2D Solid(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }

        private void EnsureStyles()
        {
            int fs = Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) * 0.026f);
            if (_btn != null && fs == _fs) return;
            _fs = fs;
            _bg ??= Solid(new Color(0.05f, 0.08f, 0.12f, 0.95f));
            _plotBg ??= Solid(new Color(0.08f, 0.12f, 0.17f, 1f));
            _btnBg ??= Solid(new Color(0.18f, 0.24f, 0.32f, 0.95f));
            _btnOnBg ??= Solid(new Color(0.2f, 0.62f, 0.35f, 0.95f));
            _white ??= Solid(Color.white);
            Font f = UiFont.Get();
            _btn = new GUIStyle { font = f, fontSize = fs, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white, background = _btnBg }, active = { textColor = Color.white, background = _btnBg }, padding = new RectOffset(6, 6, 4, 4) };
            _btnOn = new GUIStyle(_btn) { fontStyle = FontStyle.Bold, normal = { textColor = Color.white, background = _btnOnBg }, active = { textColor = Color.white, background = _btnOnBg } };
            _head = new GUIStyle { font = f, fontSize = Mathf.RoundToInt(fs * 0.92f), fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerLeft, normal = { textColor = new Color(0.62f, 0.82f, 1f) } };
            _label = new GUIStyle { font = f, fontSize = fs, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            _small = new GUIStyle(_label) { fontSize = Mathf.RoundToInt(fs * 0.82f), normal = { textColor = new Color(0.78f, 0.84f, 0.90f) }, wordWrap = true, alignment = TextAnchor.UpperLeft };
            _tiny = new GUIStyle(_label) { fontSize = Mathf.RoundToInt(fs * 0.72f), normal = { textColor = new Color(0.72f, 0.80f, 0.88f) } };
            _warn = new GUIStyle(_label) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.72f, 0.25f) } };
            _good = new GUIStyle(_label) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.45f, 0.95f, 0.58f) } };
        }

        private void OnGUI()
        {
            if (!IsOpen) return;
            EnsureStyles();

            float s = Mathf.Min(Screen.width, Screen.height);
            float gap = s * 0.012f, lh = s * 0.052f, bh = s * 0.062f;
            float w = Mathf.Min(Screen.width - gap * 2f, s * 1.25f);
            float h = Mathf.Min(Screen.height - gap * 2f, s * 1.30f);
            var panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.DrawTexture(panel, _bg);

            // Title and Close go in BEFORE the scroll view, so the way out is always on screen however tall the
            // sheet gets (the same rule the OPTIONS panel keeps).
            float cw = w - gap * 2f;
            GUI.Label(new Rect(panel.x + gap, panel.y + gap, cw * 0.7f, lh), "LOAD SHEET", _head);
            if (UiLayout.Button(new Rect(panel.xMax - gap - cw * 0.18f, panel.y + gap, cw * 0.18f, bh), "Close", _btnOn)) Close();
            float top = panel.y + gap + bh + gap * 0.5f;

            LoadSheet sheet = SessionLoading.Current;
            AircraftConfig live = Driver != null && Driver.Sim != null ? Driver.Sim.Aircraft.Config : null;
            if (sheet == null)
            {
                string name = Driver != null ? Driver.AircraftName : "This aeroplane";
                bool envelopeOnly = live != null && LoadSheet.HasEnvelope(live);
                UiLayout.Label(new Rect(panel.x + gap, top, cw, lh),
                    envelopeOnly ? $"{name}: its limits are known, its loading table is not." : $"{name} has no weight-and-balance data yet.", _warn);
                float ey = top + lh + gap;
                GUI.Label(new Rect(panel.x + gap, ey, cw, lh * 3f), envelopeOnly
                    ? "Its certificated CG envelope is below, with where it is flying now. Putting people and fuel in it "
                      + "needs the other half of the data — the empty weight and the arms of its seats, holds and tanks — "
                      + "which comes from the type's loading table rather than its certificate."
                    : "A type can only be loaded once its published loading table is in its config: the empty weight, the "
                      + "arms of its seats, holds and tanks, and its certificated CG envelope. The Cessna 172 and the J-3 "
                      + "Cub have theirs. Until then this aeroplane flies at the weight and balance it was tuned at.", _small);
                ey += lh * 3f + gap;
                if (envelopeOnly)
                {
                    float ph = Mathf.Min(cw * 0.52f, panel.yMax - gap - ey);
                    DrawEnvelope(new Rect(panel.x + gap, ey, cw, ph), live.Loading,
                                 live.Mass.MassKg, WeightAndBalance.PercentMac(live, live.Mass.CgVec().X),
                                 WeightAndBalance.InEnvelope(live.Loading, WeightAndBalance.PercentMac(live, live.Mass.CgVec().X), live.Mass.MassKg));
                }
                return;
            }

            var view = new Rect(panel.x + gap, top, cw, panel.yMax - gap - top);
            float inner = Mathf.Max(_measured, view.height);
            _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0, 0, cw - s * 0.03f, inner));
            float x = 0, y = 0, colW = cw - s * 0.035f;
            Draw(sheet, x, ref y, colW, lh, bh, gap);
            _measured = y + gap;
            GUI.EndScrollView();
        }

        private void Draw(LoadSheet sheet, float x, ref float y, float colW, float lh, float bh, float gap)
        {
            AircraftConfig cfg = sheet.Config;
            WeightAndBalance.Result r = sheet.Last;
            double pct = WeightAndBalance.PercentMac(cfg, r.Cg.X);
            (double fwd, double aft) = WeightAndBalance.LimitsAt(sheet.Loading, r.MassKg);

            // ---- the plot
            float plotH = Mathf.Min(colW * 0.52f, Mathf.Min(Screen.width, Screen.height) * 0.42f);
            DrawEnvelope(new Rect(x, y, colW, plotH), sheet.Loading, r.MassKg, pct, sheet.InEnvelope);
            y += plotH + gap;

            // ---- the verdict
            string verdict = sheet.OverGross
                ? $"OVER GROSS by {(r.MassKg - sheet.Loading.MaxTakeoffMassKg) * 2.20462:0} lb"
                : !sheet.InEnvelope
                    ? (double.IsNaN(fwd) ? "OUTSIDE THE ENVELOPE" : pct < fwd ? "CG FORWARD OF THE LIMIT" : "CG AFT OF THE LIMIT")
                    : "WITHIN LIMITS";
            UiLayout.Label(new Rect(x, y, colW * 0.52f, lh), verdict, sheet.InEnvelope ? _good : _warn);
            UiLayout.Label(new Rect(x + colW * 0.52f, y, colW * 0.48f, lh),
                double.IsNaN(fwd) ? $"{pct:0.0} % MAC" : $"{pct:0.0} % MAC  (limits {fwd:0.0} – {aft:0.0})", _tiny);
            y += lh;
            UiLayout.Label(new Rect(x, y, colW, lh),
                $"{r.MassKg * 2.20462:#,0} lb / {r.MassKg:#,0} kg   ·   gross {sheet.Loading.MaxTakeoffMassKg * 2.20462:#,0} lb"
                + $"   ·   empty {sheet.Loading.EmptyMassKg * 2.20462:#,0} lb", _tiny);
            y += lh + gap * 0.5f;

            // ---- stations
            UiLayout.Label(new Rect(x, y, colW, lh), "OCCUPANTS, BAGGAGE AND CARGO", _head);
            y += lh;
            foreach (StationConfig st in sheet.Stations)
            {
                double have = sheet.StationKg(st.Id);
                UiLayout.Label(new Rect(x, y, colW * 0.40f, bh), st.DisplayName, _label);
                UiLayout.Label(new Rect(x + colW * 0.40f, y, colW * 0.16f, bh), $"{have * 2.20462:0} lb", _tiny);
                float v = GUI.HorizontalSlider(new Rect(x + colW * 0.57f, y + bh * 0.4f, colW * 0.43f, bh * 0.3f), (float)have, 0f, (float)st.MaxKg);
                if (Mathf.Abs(v - (float)have) > 0.05f) SessionLoading.SetStation(st.Id, v, Driver);
                y += bh + gap * 0.3f;
            }
            // The two FAA standard weights, because "how heavy is a person" should not need a conversion in your head.
            UiLayout.Label(new Rect(x, y, colW, lh * 0.9f), "A slider's full travel is what the station is placarded for. 170 lb is the standard adult weight.", _tiny);
            y += lh * 0.9f + gap * 0.5f;

            // ---- tanks
            UiLayout.Label(new Rect(x, y, colW, lh), "FUEL", _head);
            y += lh;
            foreach (TankConfig tk in sheet.Tanks)
            {
                double have = sheet.TankKg(tk.Id), cap = tk.CapacityL * tk.DensityKgPerL;
                double gal = tk.DensityKgPerL > 0 ? have / tk.DensityKgPerL / 3.785411784 : 0;
                UiLayout.Label(new Rect(x, y, colW * 0.40f, bh), tk.DisplayName, _label);
                UiLayout.Label(new Rect(x + colW * 0.40f, y, colW * 0.16f, bh), $"{gal:0.0} gal", _tiny);
                float v = GUI.HorizontalSlider(new Rect(x + colW * 0.57f, y + bh * 0.4f, colW * 0.43f, bh * 0.3f), (float)have, 0f, (float)cap);
                if (Mathf.Abs(v - (float)have) > 0.05f) SessionLoading.SetTank(tk.Id, v, Driver);
                y += bh + gap * 0.3f;
            }
            float third = (colW - gap) / 3f;
            if (UiLayout.Button(new Rect(x, y, third, bh), "Full fuel", _btn)) SessionLoading.SetFuelFraction(1.0, Driver);
            if (UiLayout.Button(new Rect(x + third + gap * 0.5f, y, third, bh), "Half fuel", _btn)) SessionLoading.SetFuelFraction(0.5, Driver);
            if (UiLayout.Button(new Rect(x + 2 * (third + gap * 0.5f), y, third, bh), "Type default", _btn)) SessionLoading.ResetToDefault(Driver);
            y += bh + gap * 0.5f;

            UiLayout.Label(new Rect(x, y, colW, lh * 0.9f),
                "The default load is the one this aeroplane has always flown at, so a fresh sheet changes nothing.", _tiny);
            y += lh * 0.9f;
        }

        /// <summary>
        /// The envelope: certificated CG limits as a filled polygon, weight up the side and percent MAC across, with
        /// the loaded point on it. Both axes are the aeroplane's own — the polygon's extent sets the scale, padded a
        /// little so a point outside the limits is still visible rather than clipped off the edge.
        /// </summary>
        private void DrawEnvelope(Rect box, LoadingConfig loading, double massKg, double pct, bool legal)
        {
            GUI.DrawTexture(box, _plotBg);
            var env = loading.Envelope;
            if (env == null || env.Count < 3) return;

            double pMin = double.MaxValue, pMax = double.MinValue, mMin = double.MaxValue, mMax = double.MinValue;
            foreach (double[] v in env)
            {
                pMin = System.Math.Min(pMin, v[0]); pMax = System.Math.Max(pMax, v[0]);
                mMin = System.Math.Min(mMin, v[1]); mMax = System.Math.Max(mMax, v[1]);
            }
            // Keep the loaded point on the chart even when it is illegal — being off the end is the thing to see.
            pMin = System.Math.Min(pMin, pct); pMax = System.Math.Max(pMax, pct);
            mMin = System.Math.Min(mMin, massKg); mMax = System.Math.Max(mMax, massKg);
            double pPad = System.Math.Max(2.0, (pMax - pMin) * 0.12), mPad = System.Math.Max(10.0, (mMax - mMin) * 0.10);
            pMin -= pPad; pMax += pPad; mMin -= mPad; mMax += mPad;

            float padL = box.width * 0.13f, padB = box.height * 0.13f, padT = box.height * 0.07f, padR = box.width * 0.04f;
            var plot = new Rect(box.x + padL, box.y + padT, box.width - padL - padR, box.height - padT - padB);

            Vector2 P(double p, double m) => new(
                plot.x + (float)((p - pMin) / (pMax - pMin)) * plot.width,
                plot.yMax - (float)((m - mMin) / (mMax - mMin)) * plot.height);

            // axes
            var axis = new Color(0.45f, 0.55f, 0.65f);
            Line(new Vector2(plot.x, plot.y), new Vector2(plot.x, plot.yMax), axis, 1.5f);
            Line(new Vector2(plot.x, plot.yMax), new Vector2(plot.xMax, plot.yMax), axis, 1.5f);

            // the envelope outline
            var limit = new Color(0.45f, 0.85f, 1f);
            for (int i = 0; i < env.Count; i++)
            {
                double[] a = env[i], b = env[(i + 1) % env.Count];
                Line(P(a[0], a[1]), P(b[0], b[1]), limit, 2f);
            }

            // max gross, where it is not already an edge
            double gross = loading.MaxTakeoffMassKg;
            if (gross > mMin && gross < mMax)
                Line(P(pMin, gross), P(pMax, gross), new Color(1f, 0.72f, 0.25f, 0.55f), 1f);

            // the loaded point
            Vector2 pt = P(pct, massKg);
            Color dot = legal ? new Color(0.3f, 1f, 0.45f) : new Color(1f, 0.45f, 0.3f);
            float rad = Mathf.Max(3f, box.height * 0.022f);
            Line(new Vector2(pt.x - rad * 2f, pt.y), new Vector2(pt.x + rad * 2f, pt.y), dot, 2f);
            Line(new Vector2(pt.x, pt.y - rad * 2f), new Vector2(pt.x, pt.y + rad * 2f), dot, 2f);
            Circle(pt, rad, dot, 2f);

            // labels: weight up the left in pounds, percent MAC across the bottom
            GUI.Label(new Rect(box.x, plot.y - _fs * 0.6f, padL * 1.5f, _fs * 1.4f), $"{mMax * 2.20462:#,0}", _tiny);
            GUI.Label(new Rect(box.x, plot.yMax - _fs * 0.7f, padL * 1.5f, _fs * 1.4f), $"{mMin * 2.20462:#,0}", _tiny);
            GUI.Label(new Rect(box.x, box.y, padL * 1.6f, _fs * 1.3f), "lb", _tiny);
            GUI.Label(new Rect(plot.x, box.yMax - _fs * 1.3f, plot.width * 0.5f, _fs * 1.3f), $"{pMin:0} % MAC", _tiny);
            var right = new GUIStyle(_tiny) { alignment = TextAnchor.MiddleRight };
            GUI.Label(new Rect(plot.xMax - plot.width * 0.5f, box.yMax - _fs * 1.3f, plot.width * 0.5f, _fs * 1.3f), $"{pMax:0} % MAC  (aft →)", right);
        }

        private void Line(Vector2 a, Vector2 b, Color c, float width)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.5f) return;
            Matrix4x4 m = GUI.matrix;
            Color old = GUI.color;
            GUI.color = c;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, len, width), _white);
            GUI.matrix = m;
            GUI.color = old;
        }

        private void Circle(Vector2 c, float rad, Color col, float width, int sides = 20)
        {
            Vector2 prev = c + new Vector2(rad, 0);
            for (int i = 1; i <= sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2f;
                Vector2 p = c + new Vector2(Mathf.Cos(a) * rad, Mathf.Sin(a) * rad);
                Line(prev, p, col, width);
                prev = p;
            }
        }
    }
}
