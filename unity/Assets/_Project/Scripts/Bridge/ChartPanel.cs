using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.Nav;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// THE CHART. Owner, 2026-10-18: *"create a map of the playground that the user can call up at anytime.
    /// don't show the user their location on the map they have to navigate by ded reconing or vor only …
    /// create an ifr low chart to go along with the vfr chart and a 'satalite image' so the user can see all
    /// three but no gps type positions ever."*
    ///
    /// Three charts of the same world — a sectional, a low-altitude en-route chart, and a photograph — that
    /// you can open at any time from the toolbar or the pause menu, pan and zoom, and that **never show you
    /// where you are.** There is no own-ship symbol, no track line, no breadcrumb, and no "centre on
    /// aircraft" button. That is the feature. You find out where you are the way a pilot without a moving
    /// map does: by the clock and the compass, or by crossing two radials.
    ///
    /// It is enforced structurally rather than by being careful. Nothing in this class reads the aeroplane's
    /// position: there is no reference to <see cref="FlightSimDriver"/>, no <c>Sim</c>, no transform. The
    /// drawing takes a rectangle of the world and the published chart data, and that is all it is given. If
    /// somebody ever wants to add a little triangle, they will have to add a dependency first, and that is
    /// the point at which they should stop.
    ///
    /// COST. The background is one terrain sample per pixel — about 650,000 of them — so it is built ONCE on
    /// a worker thread with a progress bar, and then cached to disk under the build number so that every
    /// later launch of the same build opens instantly. The symbology is drawn in IMGUI over the top, which
    /// keeps the lines and the numbers sharp at any zoom, the way the printing on a chart is sharp and the
    /// terrain tint is not.
    /// </summary>
    public sealed class ChartPanel : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }
        public static void Toggle() => IsOpen = !IsOpen;
        public static void Close() => IsOpen = false;

        // 1,024 px across 26.6 km of world is 26 m a pixel, which is about what a sectional resolves.
        private const int TexW = 1024;
        private static int TexH => Mathf.RoundToInt(TexW * (float)((ChartRaster.XMax - ChartRaster.XMin)
                                                                  / (ChartRaster.YMax - ChartRaster.YMin)));

        private ChartLayer _layer = ChartLayer.Vfr;
        private readonly Texture2D[] _tex = new Texture2D[3];
        private ChartRaster.Grid _grid;
        private List<AirwayLeg> _legs = new();
        private List<ChartFeature> _features = new();
        private System.Threading.Tasks.Task _build;
        private volatile int _rows;
        private byte[][] _pixels;
        private string _error;

        // The view: which world point is in the middle, and how many metres to a screen pixel.
        private Vector2 _centre = new(400, -5000);
        private float _mPerPx = 30f;
        private bool _dragging;
        private Vector2 _lastDrag;
        private bool _showPlates = true;

        private GUIStyle _btn, _btnOn, _head, _label, _tiny, _box;
        private Texture2D _bg, _btnBg, _btnOnBg, _white;
        private int _fs;

        private static Texture2D Solid(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }

        private string CachePath(ChartLayer l)
            => System.IO.Path.Combine(Application.persistentDataPath, $"chart-{Application.version}-{l}.raw");

        private void EnsureStyles()
        {
            int fs = Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) * 0.022f);
            if (_btn != null && fs == _fs) return;
            _fs = fs;
            _bg ??= Solid(new Color(0.05f, 0.08f, 0.12f, 0.97f));
            _btnBg ??= Solid(new Color(0.18f, 0.24f, 0.32f, 0.95f));
            _btnOnBg ??= Solid(new Color(0.2f, 0.62f, 0.35f, 0.95f));
            _white ??= Solid(Color.white);
            Font f = UiFont.Get();
            _btn = new GUIStyle { font = f, fontSize = fs, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white, background = _btnBg }, active = { textColor = Color.white, background = _btnBg }, padding = new RectOffset(6, 6, 4, 4) };
            _btnOn = new GUIStyle(_btn) { fontStyle = FontStyle.Bold, normal = { textColor = Color.white, background = _btnOnBg }, active = { textColor = Color.white, background = _btnOnBg } };
            _head = new GUIStyle { font = f, fontSize = Mathf.RoundToInt(fs * 1.1f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.55f, 0.95f, 1f) } };
            _label = new GUIStyle { font = f, fontSize = fs, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            _tiny = new GUIStyle { font = f, fontSize = Mathf.RoundToInt(fs * 0.78f), alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.black } };
            _box = new GUIStyle(_tiny) { alignment = TextAnchor.MiddleLeft };
        }

        // ---------------------------------------------------------------- building

        private void StartBuild()
        {
            if (_build != null) return;
            var t = WorldTerrain.Active;
            if (t == null) { _error = "the world is not built yet"; return; }
            // Warm every lazily-built table inside the terrain on THIS thread before a worker touches it.
            // WorldTerrain builds its river profile on first use, and the region classes have statics of
            // their own; two threads racing to build one of those is the kind of bug that happens once on a
            // phone and never on a Mac. Touching all three entry points here settles it.
            for (int k = 0; k < 4; k++)
            {
                double wx = 400 + k * 1500, wy = -k * 3000;
                t.HeightAt(wx, wy, false, false);
                t.WaterSurfaceAt(wx, wy);
                WorldTerrain.SurfaceAt(wx, wy);
            }

            int w = TexW, h = TexH;
            _pixels = new byte[3][];
            if (LoadCache(w, h)) { _rows = h; _build = System.Threading.Tasks.Task.CompletedTask; return; }

            _build = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    _grid = ChartRaster.Heights(t, w, h, rowDone: r => _rows = r + 1);
                    for (int i = 0; i < 3; i++)
                    {
                        _pixels[i] = new byte[w * h * 4];
                        ChartRaster.Paint(_grid, (ChartLayer)i, _pixels[i]);
                    }
                    _legs = Airways.Legs(t);
                    _features = ChartFeatures.All(t);
                    SaveCache(w, h);
                }
                catch (System.Exception e) { _error = e.Message; }
            });
        }

        private bool LoadCache(int w, int h)
        {
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    string p = CachePath((ChartLayer)i);
                    if (!System.IO.File.Exists(p)) return false;
                    byte[] b = System.IO.File.ReadAllBytes(p);
                    if (b.Length != w * h * 4) return false;
                    _pixels[i] = b;
                }
            }
            catch (System.Exception) { return false; }
            // The airway minimums are quick next to the raster, but not free; off the main thread anyway.
            var t = WorldTerrain.Active;
            System.Threading.Tasks.Task.Run(() =>
            {
                try { _legs = Airways.Legs(t); _features = ChartFeatures.All(t); } catch (System.Exception) { }
            });
            return true;
        }

        private void SaveCache(int w, int h)
        {
            try
            {
                for (int i = 0; i < 3; i++) System.IO.File.WriteAllBytes(CachePath((ChartLayer)i), _pixels[i]);
            }
            catch (System.Exception) { }   // a chart that cannot be cached still draws
        }

        /// <summary>Raster row 0 is the north edge; a Texture2D's row 0 is the bottom. Flip on the way in.</summary>
        private void Upload()
        {
            int w = TexW, h = TexH;
            for (int i = 0; i < 3; i++)
            {
                if (_tex[i] != null || _pixels?[i] == null) continue;
                var flipped = new byte[w * h * 4];
                for (int row = 0; row < h; row++)
                    System.Array.Copy(_pixels[i], row * w * 4, flipped, (h - 1 - row) * w * 4, w * 4);
                var tx = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                tx.LoadRawTextureData(flipped);
                tx.Apply(false, false);
                _tex[i] = tx;
            }
            _pixels = null;
        }

        // ---------------------------------------------------------------- view maths

        private Rect _view;

        private Vector2 ToScreen(double worldX, double worldY)
            => new(_view.center.x + (float)(worldY - _centre.y) / _mPerPx,
                   _view.center.y - (float)(worldX - _centre.x) / _mPerPx);

        private bool OnScreen(Vector2 p, float pad = 40f)
            => p.x > _view.x - pad && p.x < _view.xMax + pad && p.y > _view.y - pad && p.y < _view.yMax + pad;

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

        private void Circle(Vector2 c, float r, Color col, float width, int sides = 24)
        {
            Vector2 prev = c + new Vector2(r, 0);
            for (int i = 1; i <= sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                Vector2 p = c + new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
                Line(prev, p, col, width);
                prev = p;
            }
        }

        /// <summary>
        /// A label on a chart: dark text on a pale box, so it reads over tan terrain and over a photograph
        /// alike. Sized line by line, because <c>GUIStyle.CalcSize</c> measures a multi-line string as if it
        /// were one line and every two-line tag came out half as tall as its text.
        /// </summary>
        private void Tag(Vector2 at, string text, Color fg, Color bg, float dx = 0, float dy = 0)
        {
            string[] lines = text.Split('\n');
            float wMax = 0, lh = _tiny.lineHeight > 0 ? _tiny.lineHeight : _fs * 1.1f;
            foreach (string l in lines) wMax = Mathf.Max(wMax, _tiny.CalcSize(new GUIContent(l)).x);
            var r = new Rect(at.x + dx, at.y + dy, wMax + 7, lh * lines.Length + 3);
            Color old = GUI.color;
            GUI.color = bg; GUI.DrawTexture(r, _white); GUI.color = old;
            var st = new GUIStyle(_box) { normal = { textColor = fg } };
            for (int i = 0; i < lines.Length; i++)
                GUI.Label(new Rect(r.x + 3, r.y + 1 + lh * i, r.width - 6, lh), lines[i], st);
        }

        // ---------------------------------------------------------------- the panel

        private void OnGUI()
        {
            if (!IsOpen) return;
            EnsureStyles();
            StartBuild();

            float s = Mathf.Min(Screen.width, Screen.height);
            float m = s * 0.015f;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _bg);

            float barH = s * 0.065f;
            // Landscape puts the notes beside the chart; portrait has no width to spare, so there they cover
            // the chart and the INFO button swaps between the two.
            bool wide = Screen.width > Screen.height * 1.25f;
            float sideW = wide ? Mathf.Min(Screen.width * 0.3f, s * 0.46f) : 0f;
            _view = new Rect(m, m + barH + m * 0.5f, Screen.width - 2 * m - (wide ? sideW + m : 0f),
                             Screen.height - 2 * m - barH - m * 0.5f);

            DrawTopBar(m, barH, wide);

            if (_error != null) { GUI.Label(_view, "the chart could not be drawn: " + _error, _label); return; }
            if (_build == null || !_build.IsCompleted)
            {
                float frac = TexH > 0 ? (float)_rows / TexH : 0f;
                var r = new Rect(_view.center.x - _view.width * 0.3f, _view.center.y - barH * 0.5f, _view.width * 0.6f, barH);
                GUI.Label(new Rect(r.x, r.y - barH, r.width, barH),
                          $"Drawing the chart from the ground up — {frac * 100f:F0} %. Once only; after this it opens straight away.", _label);
                GUI.DrawTexture(r, _btnBg);
                GUI.DrawTexture(new Rect(r.x, r.y, r.width * frac, r.height), _btnOnBg);
                return;
            }
            Upload();

            Pan();
            GUI.BeginGroup(_view);
            var inner = new Rect(0, 0, _view.width, _view.height);
            var saveView = _view;
            _view = inner;
            DrawBackground(inner);
            DrawSymbology();
            _view = saveView;
            GUI.EndGroup();
            // The frame, and the scale bar.
            Line(new Vector2(_view.x, _view.y), new Vector2(_view.xMax, _view.y), new Color(1, 1, 1, 0.3f), 1);
            Line(new Vector2(_view.x, _view.yMax), new Vector2(_view.xMax, _view.yMax), new Color(1, 1, 1, 0.3f), 1);
            DrawScaleBar();
            if (wide) DrawSidePanel(Screen.width - m - sideW, m + barH + m * 0.5f, sideW, Screen.height - 2 * m - barH - m * 0.5f);
            else if (_infoOpen) DrawSidePanel(_view.x, _view.y, _view.width, _view.height);
        }

        private bool _infoOpen;

        /// <summary>
        /// One row, laid out on a BUDGET rather than on a guessed button width: CLOSE is placed first and
        /// everything else divides what is left in proportion. The first version sized the buttons from the
        /// screen width and ran the FIT button ninety pixels UNDER the CLOSE button on an iPad, which is
        /// exactly the overlap the owner's rule of 2026-10-03 forbids.
        /// </summary>
        private void DrawTopBar(float m, float barH, bool wide)
        {
            float gap = m * 0.5f;
            float cw = s_() * 0.16f;
            if (UiLayout.Button(new Rect(Screen.width - m - cw, m, cw, barH), "CLOSE", _btnOn)) IsOpen = false;

            // Three layer tabs, the plates switch, zoom out, zoom in, fit — and INFO as well in portrait,
            // where there is no room beside the chart for the notes.
            float[] wt = wide ? new[] { 1f, 1f, 1f, 0.85f, 0.4f, 0.4f, 0.6f }
                              : new[] { 1f, 1f, 1f, 0.85f, 0.4f, 0.4f, 0.6f, 0.7f };
            float total = 0;
            foreach (float f in wt) total += f;
            float avail = (Screen.width - m - cw - gap) - m - gap * (wt.Length - 1);
            float unit = Mathf.Max(barH * 0.55f, avail / total);
            float x = m;
            Rect Next(int i) { var r = new Rect(x, m, unit * wt[i], barH); x += r.width + gap; return r; }

            var names = new[] { ("VFR", ChartLayer.Vfr), ("IFR LOW", ChartLayer.IfrLow), ("SATELLITE", ChartLayer.Satellite) };
            for (int i = 0; i < 3; i++)
                if (UiLayout.Button(Next(i), names[i].Item1, _layer == names[i].Item2 ? _btnOn : _btn)) _layer = names[i].Item2;
            if (UiLayout.Button(Next(3), _showPlates ? "PLATES ON" : "PLATES OFF", _showPlates ? _btnOn : _btn)) _showPlates = !_showPlates;
            if (UiLayout.Button(Next(4), "\u2212", _btn)) _mPerPx = Mathf.Min(60f, _mPerPx * 1.5f);
            if (UiLayout.Button(Next(5), "+", _btn)) _mPerPx = Mathf.Max(6f, _mPerPx / 1.5f);
            if (UiLayout.Button(Next(6), "FIT", _btn)) { _centre = new Vector2(400, -5000); _mPerPx = 30f; }
            if (!wide && UiLayout.Button(Next(7), _infoOpen ? "MAP" : "INFO", _infoOpen ? _btnOn : _btn)) _infoOpen = !_infoOpen;
        }

        private static float s_() => Mathf.Min(Screen.width, Screen.height);

        private void Pan()
        {
            Event e = Event.current;
            if (e == null) return;
            if (e.type == EventType.MouseDown && _view.Contains(e.mousePosition)) { _dragging = true; _lastDrag = e.mousePosition; }
            else if (e.type == EventType.MouseUp) _dragging = false;
            else if (e.type == EventType.MouseDrag && _dragging)
            {
                Vector2 d = e.mousePosition - _lastDrag;
                _lastDrag = e.mousePosition;
                _centre = new Vector2(_centre.x + d.y * _mPerPx, _centre.y - d.x * _mPerPx);
            }
            else if (e.type == EventType.ScrollWheel && _view.Contains(e.mousePosition))
                _mPerPx = Mathf.Clamp(_mPerPx * (1f + e.delta.y * 0.06f), 6f, 60f);
            // Two fingers pinch (iOS): the distance between them sets the zoom.
            if (Input.touchCount == 2)
            {
                float d = (Input.GetTouch(0).position - Input.GetTouch(1).position).magnitude;
                if (_pinch > 0 && d > 1) _mPerPx = Mathf.Clamp(_mPerPx * (_pinch / d), 6f, 60f);
                _pinch = d;
            }
            else _pinch = 0;
            _centre = new Vector2(Mathf.Clamp(_centre.x, (float)ChartRaster.XMin, (float)ChartRaster.XMax),
                                  Mathf.Clamp(_centre.y, (float)ChartRaster.YMin, (float)ChartRaster.YMax));
        }
        private float _pinch;

        private void DrawBackground(Rect inner)
        {
            Texture2D tx = _tex[(int)_layer];
            if (tx == null) return;
            // Where the whole chart would land on screen, then draw it clipped to the view.
            Vector2 topLeft = ToScreen(ChartRaster.XMax, ChartRaster.YMin);
            float pxPerMetre = 1f / _mPerPx;
            float wPx = (float)(ChartRaster.YMax - ChartRaster.YMin) * pxPerMetre;
            float hPx = (float)(ChartRaster.XMax - ChartRaster.XMin) * pxPerMetre;
            GUI.DrawTexture(new Rect(topLeft.x, topLeft.y, wPx, hPx), tx);
        }

        private void DrawScaleBar()
        {
            // A chart without a scale is a picture. Pick a round number of miles that fits the bar.
            float want = _view.width * 0.22f;
            double nm = want * _mPerPx / NavReception.MetresPerNm;
            double[] steps = { 0.5, 1, 2, 5, 10 };
            double pick = steps[0];
            foreach (double st in steps) if (st <= nm) pick = st;
            float px = (float)(pick * NavReception.MetresPerNm) / _mPerPx;
            float y = _view.yMax - _fs * 2.2f, x = _view.x + _fs;
            Line(new Vector2(x, y), new Vector2(x + px, y), Color.white, 2);
            Line(new Vector2(x, y - 4), new Vector2(x, y + 4), Color.white, 2);
            Line(new Vector2(x + px, y - 4), new Vector2(x + px, y + 4), Color.white, 2);
            GUI.Label(new Rect(x, y + 4, px + _fs * 6, _fs * 1.4f), $"{pick:0.#} NM", _label);
        }

        // ---------------------------------------------------------------- symbology

        private void DrawSymbology()
        {
            if (_layer == ChartLayer.Vfr) DrawLandmarks();
            if (_layer == ChartLayer.IfrLow) DrawAirways();
            DrawAirports();
            if (_layer != ChartLayer.Satellite) { DrawNavaids(); DrawBeacons(); }
            if (_layer == ChartLayer.IfrLow) DrawFixes();
        }

        /// <summary>
        /// The things you look out of the window for. Dead reckoning on its own is not how anybody navigates
        /// — you fly a heading and a time and then find the thing you expected to see — so the sectional
        /// carries the towns, the bridges, the arch, the dam, the three falls and the two tall towers, with
        /// their heights, the way a real one prints obstructions.
        /// </summary>
        private void DrawLandmarks()
        {
            foreach (ChartFeature f in _features)
            {
                if (f.Kind == ChartFeatureKind.Town)
                {
                    // A built-up area is yellow on a sectional.
                    Vector2 a = ToScreen(f.X1, f.Y0), b = ToScreen(f.X0, f.Y1);
                    var r = new Rect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
                    if (!OnScreen(r.center, Mathf.Max(r.width, r.height))) continue;
                    Color old = GUI.color;
                    GUI.color = new Color(0.98f, 0.88f, 0.42f, 0.55f);
                    GUI.DrawTexture(r, _white);
                    GUI.color = old;
                    if (r.width > 24) Tag(new Vector2(r.center.x, r.yMax), f.Name, new Color(0.3f, 0.2f, 0f), new Color(1f, 1f, 1f, 0.6f), -r.width * 0.3f, 2f);
                    continue;
                }

                Vector2 c = ToScreen(f.X, f.Y);
                if (!OnScreen(c, 60)) continue;
                var ink = new Color(0.25f, 0.15f, 0.08f);
                float sz = Mathf.Max(5f, 140f / _mPerPx);
                switch (f.Kind)
                {
                    case ChartFeatureKind.Obstruction:
                        // The sectional obstruction symbol: a spike with legs, MSL in bold over AGL in brackets.
                        Line(c + new Vector2(-sz * 0.6f, sz * 0.5f), c, ink, 2f);
                        Line(c + new Vector2(sz * 0.6f, sz * 0.5f), c, ink, 2f);
                        Line(c + new Vector2(-sz * 0.6f, sz * 0.5f), c + new Vector2(sz * 0.6f, sz * 0.5f), ink, 2f);
                        Tag(c, $"{f.MslFt:0}\n({f.AglFt:0})", ink, new Color(1f, 1f, 1f, 0.78f), sz * 0.7f, -sz);
                        break;
                    case ChartFeatureKind.Waterfall:
                        Line(c + new Vector2(-sz, 0), c + new Vector2(sz, 0), new Color(0.2f, 0.4f, 0.7f), 2f);
                        Line(c, c + new Vector2(0, sz * 1.2f), new Color(0.2f, 0.4f, 0.7f), 2f);
                        Tag(c, $"{f.Name}  {f.AglFt:0} ft", new Color(0.1f, 0.25f, 0.5f), new Color(1f, 1f, 1f, 0.78f), sz + 3f, -_fs * 0.6f);
                        break;
                    case ChartFeatureKind.Bridge:
                    case ChartFeatureKind.Dam:
                    case ChartFeatureKind.Arch:
                        Circle(c, sz * 0.7f, ink, 2f, 8);
                        Tag(c, f.Kind == ChartFeatureKind.Arch ? $"{f.Name}  {f.AglFt:0} ft" : f.Name,
                            ink, new Color(1f, 1f, 1f, 0.78f), sz + 3f, -_fs * 0.6f);
                        break;
                }
            }
        }

        private void DrawAirports()
        {
            foreach (WorldTerrain.Airport a in WorldTerrain.Airports)
            {
                Vector2 c = ToScreen(a.X, a.Y);
                if (!OnScreen(c, 80)) continue;
                var col = _layer == ChartLayer.Satellite ? Color.white : new Color(0.1f, 0.25f, 0.6f);
                // The runways, to scale, which is what an airport symbol is on a real chart once it is big
                // enough to draw: two strips crossing, along x and along y.
                foreach (WorldTerrain.Strip st in WorldTerrain.AirportStrips)
                {
                    if (!st.Kind.StartsWith("paved")) continue;
                    double h = st.HeadingDeg * Mathf.Deg2Rad;
                    double cx = a.X + st.Dx, cy = a.Y + st.Dy;
                    double ex = Mathf.Cos((float)h) * st.Length / 2, ey = Mathf.Sin((float)h) * st.Length / 2;
                    Line(ToScreen(cx - ex, cy - ey), ToScreen(cx + ex, cy + ey), col, Mathf.Max(2f, (float)(st.Width / _mPerPx)));
                }
                Circle(c, Mathf.Max(6f, 900f / _mPerPx), col, 2f);
                int elevFt = Mathf.RoundToInt((float)(a.ElevationM * Terps.FtPerM));
                Tag(c, $"{a.Name.ToUpper()}  {elevFt}", Color.black, new Color(1f, 1f, 1f, 0.82f),
                    Mathf.Max(8f, 900f / _mPerPx) + 4f, -_fs * 0.6f);
            }
        }

        private void DrawNavaids()
        {
            foreach (Vor v in Navaids.Vors)
            {
                Vector2 c = ToScreen(v.X, v.Y);
                if (!OnScreen(c, 120)) continue;
                var col = new Color(0.08f, 0.35f, 0.45f);
                // The compass rose: a VOR's symbol is a hexagon, and when there is room, a rose round it
                // oriented to north — which is also the reminder that north here is magnetic AND true.
                float r = Mathf.Max(7f, 600f / _mPerPx);
                Circle(c, r, col, 2f, 6);
                if (r > 26f)
                {
                    Circle(c, r * 2.6f, new Color(col.r, col.g, col.b, 0.5f), 1f, 48);
                    for (int t = 0; t < 36; t++)
                    {
                        float a = t * 10f * Mathf.Deg2Rad;
                        float inner = r * 2.6f * (t % 3 == 0 ? 0.88f : 0.94f);
                        Line(c + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * inner,
                             c + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * r * 2.6f,
                             new Color(col.r, col.g, col.b, 0.6f), 1f);
                    }
                }
                string dme = v.HasDme ? " DME" : "";
                Tag(c, $"{v.Name.ToUpper()}  {v.Id}\n{v.FreqMhz:0.00}{dme}", Color.black,
                    new Color(0.86f, 0.95f, 0.98f, 0.92f), r + 4f, r + 2f);
            }
        }

        /// <summary>
        /// A beacon: the sectional symbol is a dotted circle, and on an IFR chart it carries its frequency
        /// and two-letter identifier in a box.
        /// </summary>
        private void DrawBeacons()
        {
            foreach (Ndb n in Navaids.Ndbs)
            {
                Vector2 c = ToScreen(n.X, n.Y);
                if (!OnScreen(c, 90)) continue;
                var col = new Color(0.45f, 0.25f, 0.5f);
                float r = Mathf.Max(6f, 420f / _mPerPx);
                // Dotted, which is what distinguishes it from a VOR at a glance.
                for (int k = 0; k < 20; k++)
                {
                    float a = k * 18f * Mathf.Deg2Rad;
                    Vector2 p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    GUI.DrawTexture(new Rect(p.x - 1f, p.y - 1f, 2.5f, 2.5f), _white);
                }
                Circle(c, r * 0.18f, col, 2f, 8);
                Tag(c, $"{n.Name.ToUpper()}  {n.Id}\n{n.FreqKhz:0} kHz", Color.black,
                    new Color(0.95f, 0.88f, 0.98f, 0.92f), r + 4f, r + 2f);
            }
        }

        private void DrawAirways()
        {
            var col = new Color(0.15f, 0.3f, 0.75f);
            foreach (Airway a in Airways.All)
                for (int i = 0; i + 1 < a.Points.Length; i++)
                {
                    var (x0, y0) = Airways.PointAt(a.Points[i]);
                    var (x1, y1) = Airways.PointAt(a.Points[i + 1]);
                    Vector2 p0 = ToScreen(x0, y0), p1 = ToScreen(x1, y1);
                    if (!OnScreen(p0, 400) && !OnScreen(p1, 400)) continue;
                    Line(p0, p1, col, 3f);
                    AirwayLeg? leg = null;
                    foreach (AirwayLeg l in _legs)
                        if (l.FromId == a.Points[i] && l.ToId == a.Points[i + 1]) { leg = l; break; }
                    Vector2 mid = (p0 + p1) * 0.5f;
                    string label = leg.HasValue
                        ? $"{a.Designator}   {leg.Value.CourseDeg:000}°   {leg.Value.DistanceNm:0.0} NM\nMEA {leg.Value.MeaFt / 100:0}00   MOCA {leg.Value.MocaFt / 100:0}00"
                        : a.Designator;
                    if (OnScreen(mid, 80)) Tag(mid, label, new Color(0.08f, 0.15f, 0.5f), new Color(1f, 1f, 1f, 0.88f), 6f, -_fs);
                }
        }

        private void DrawFixes()
        {
            foreach (ProcedureFix f in Airways.EnRouteFixes)
            {
                Vector2 c = ToScreen(f.X, f.Y);
                if (!OnScreen(c, 80)) continue;
                var col = new Color(0.15f, 0.3f, 0.75f);
                float r = Mathf.Max(5f, 180f / _mPerPx);
                // A fix is a triangle.
                Vector2 a = c + new Vector2(0, -r), b = c + new Vector2(r * 0.9f, r * 0.6f), d = c + new Vector2(-r * 0.9f, r * 0.6f);
                Line(a, b, col, 2f); Line(b, d, col, 2f); Line(d, a, col, 2f);
                Tag(c, $"{f.Name}\n{f.Identified}", new Color(0.08f, 0.15f, 0.5f), new Color(1f, 1f, 1f, 0.85f), r + 3f, r);
            }
        }

        // ---------------------------------------------------------------- the side panel

        private Vector2 _sideScroll;

        private void DrawSidePanel(float x, float y, float w, float h)
        {
            GUI.DrawTexture(new Rect(x, y, w, h), _btnBg);
            float pad = _fs * 0.6f;
            var inner = new Rect(x + pad, y + pad, w - 2 * pad, h - 2 * pad);
            GUI.BeginGroup(inner);
            float cy = 0, cw = inner.width - _fs;
            float lh = _fs * 1.5f;

            void Head(string t) { GUI.Label(new Rect(0, cy, cw, lh), t, _head); cy += lh; }
            void Text(string t, float lines = 1f)
            {
                var st = new GUIStyle(_label) { wordWrap = true, fontSize = Mathf.RoundToInt(_fs * 0.86f), alignment = TextAnchor.UpperLeft };
                float hh = st.CalcHeight(new GUIContent(t), cw);
                GUI.Label(new Rect(0, cy, cw, hh), t, st);
                cy += hh + _fs * 0.2f;
            }

            cy -= _sideScroll.y;

            Head("NO POSITION, EVER");
            Text("This chart does not show where you are and never will. Fly a heading, time the leg, and "
                 + "check yourself against a radial. Magnetic variation here is zero, so every course on "
                 + "every chart is both magnetic and true.");

            Head("VORs");
            foreach (Vor v in Navaids.Vors)
                Text($"{v.Id}  {v.Name}   {v.FreqMhz:0.00}{(v.HasDme ? "  DME" : "")}\n"
                     + $"    {v.GroundM * Terps.FtPerM:0} ft,  {(v.Class == VorClass.Terminal ? "terminal" : "low")}");
            Text("All four are line of sight. Behind a ridge the needle flags; climb and it comes back.");

            Head("BEACONS (ADF)");
            foreach (Ndb n in Navaids.Ndbs)
                Text($"{n.Id}  {n.Name}   {n.FreqKhz:0} kHz   {n.RangeNm:0} NM\n    {n.GroundM * Terps.FtPerM:0} ft");
            Text("A beacon's signal follows the ground, so a ridge does NOT put it in shadow — which is why "
                 + "these two sit low, in the valleys the VORs cannot see into. The price is accuracy: high "
                 + "ground beside the path bends the needle toward itself, and the ADF says so when it does.");

            if (_layer == ChartLayer.IfrLow || _showPlates)
            {
                Head("APPROACHES");
                foreach (Approach a in Approaches.All)
                {
                    Text($"{a.Airport.ToUpper()}  {a.Title}"
                         + (a.Localizer.HasValue ? $"   {a.Localizer.Value.FreqMhz:0.00}  {a.Localizer.Value.Id}" : $"   {Navaids.ById(a.VorId).FreqMhz:0.00}  {a.VorId}")
                         + $"\n    final {a.FinalCourseDeg:000}°   "
                         + (a.Kind == ApproachKind.Ils ? "DA" : "MDA")
                         + $" {a.MinimumsFt:0} ft ({a.HatFt:0} HAT)"
                         + (a.ProcedureTurn != TurnSide.None
                            ? $"\n    PROCEDURE TURN at {a.ProcedureTurnFix}, remain within {a.ProcedureTurnRemainWithinNm:0.#} NM, {a.ProcedureTurnAltitudeFt:0} ft"
                            : "\n    no course reversal published"));
                    foreach (ProcedureFix f in a.Fixes)
                    {
                        if (f.Role == FixRole.MissedApproachHold) continue;
                        Text($"      {f.Role switch { FixRole.Iaf => "IAF", FixRole.Faf => "FAF", FixRole.MissedApproachPoint => "MAP", _ => "fix" }}"
                             + $" {f.Name,-6} {f.MinAltitudeFt:0} ft   {f.Identified}");
                    }
                    Text("      missed: " + a.MissedApproachText);
                }
            }

            Head("AIRWAYS");
            foreach (Airway a in Airways.All) Text($"{a.Designator}:  {string.Join(" – ", a.Points)}");
            if (_legs.Count == 0) Text("(working out the minimum altitudes…)");
            foreach (AirwayLeg l in _legs)
                Text($"    {l.FromId}–{l.ToId}  {l.CourseDeg:000}°  {l.DistanceNm:0.0} NM   "
                     + $"MEA {l.MeaFt:0}   MOCA {l.MocaFt:0}");

            GUI.EndGroup();
            float content = cy + _sideScroll.y;
            if (content > inner.height)
            {
                Event e = Event.current;
                if (e != null && e.type == EventType.ScrollWheel && new Rect(x, y, w, h).Contains(e.mousePosition))
                    _sideScroll.y = Mathf.Clamp(_sideScroll.y + e.delta.y * _fs, 0, content - inner.height);
                if (e != null && e.type == EventType.MouseDrag && new Rect(x, y, w, h).Contains(e.mousePosition))
                    _sideScroll.y = Mathf.Clamp(_sideScroll.y - e.delta.y, 0, content - inner.height);
                float frac = inner.height / content;
                GUI.DrawTexture(new Rect(x + w - 4, y + (h - h * frac) * (_sideScroll.y / (content - inner.height)), 3, h * frac), _btnOnBg);
            }
        }
    }
}
