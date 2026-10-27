using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The regional air made visible (debug, J toggles): every region's outline on the ground in its own colour, the home
    /// field's CALM ZONE (stadium and blend edge on the ground, and its ceiling drawn in the air), wind arrows on a grid
    /// round the aircraft at its own height (white = horizontal, green/red tick = rising/sinking), the lift/sink map on the
    /// ground (green crosses lift, red sink, at 300 m above the ground) and the authored thermal columns, plus a readout of
    /// the air the aircraft is in. Draws nothing without a regional field (uniform air).
    /// </summary>
    public sealed class AtmosphereOverlay : MonoBehaviour
    {
        public FlightSimDriver Driver;
        public const KeyCode Toggle = KeyCode.J;
        public float ArrowSpacingM = 300f, ArrowSeconds = 8f, MapSpacingM = 150f, RefreshS = 0.25f;
        public int ArrowHalf = 5, MapHalf = 20;

        private Material _mat;
        private AtmosphereField _builtFor;
        private readonly List<(Vector3 a, Vector3 b, Color c)> _static = new();
        private readonly List<(Vector3 a, Vector3 b, Color c)> _dynamic = new();
        private float _nextRefresh;
        private GUIStyle _style;
        private AtmosphereSample _here;

        private void Awake()
        {
            Shader sh = Shader.Find("FlyingGame/HudLine");
            if (sh != null) _mat = new Material(sh) { color = Color.white };
        }

        private void Update()
        {
            if (Input.GetKeyDown(Toggle)) SessionSettings.ShowAtmosphereMap = !SessionSettings.ShowAtmosphereMap;
            AtmosphereField f = Atmosphere.Field;
            if (!SessionSettings.ShowAtmosphereMap || f == null || Driver == null || Driver.Sim == null) return;
            if (!ReferenceEquals(f, _builtFor)) BuildStatic(f);
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + RefreshS;
            BuildDynamic(f, Driver.Sim.Aircraft.State.Position);
        }

        private static Color RegionColour(string id)
        {
            uint h = 2166136261;
            foreach (char ch in id) h = (h ^ ch) * 16777619;
            return Color.HSVToRGB((h % 360) / 360f, 0.6f, 1f);
        }

        private void Line(List<(Vector3, Vector3, Color)> to, Vec3 a, Vec3 b, Color c) => to.Add((CoordinateMap.ToUnity(a), CoordinateMap.ToUnity(b), c));

        /// <summary>An outline on the ground (+ lift above it), given as a closed list of (x, y).</summary>
        private void GroundLoop(AtmosphereField f, IList<(double x, double y)> pts, double lift, Color c)
        {
            for (int i = 0; i < pts.Count; i++)
            {
                var (x0, y0) = pts[i]; var (x1, y1) = pts[(i + 1) % pts.Count];
                Line(_static, new Vec3(x0, y0, -(f.GroundAt(x0, y0) + lift)), new Vec3(x1, y1, -(f.GroundAt(x1, y1) + lift)), c);
            }
        }

        private static List<(double, double)> Stadium(double cx, double cy, double heading, double halfLen, double radius)
        {
            var pts = new List<(double, double)>();
            double c = System.Math.Cos(heading), s = System.Math.Sin(heading);
            for (int end = 0; end < 2; end++)
                for (int k = 0; k <= 24; k++)
                {
                    double a = (end == 0 ? -0.5 : 0.5) * System.Math.PI + System.Math.PI * k / 24;
                    double along = (end == 0 ? halfLen : -halfLen) + radius * System.Math.Cos(a);
                    double across = radius * System.Math.Sin(a);
                    pts.Add((cx + along * c - across * s, cy + along * s + across * c));
                }
            return pts;
        }

        private void BuildStatic(AtmosphereField f)
        {
            _builtFor = f;
            _static.Clear();
            foreach (AtmosphereField.Region r in f.Regions)
            {
                Color col = RegionColour(r.Id);
                var cfg = r.Cfg;
                List<(double, double)> core;
                if (r.Shape == "box")
                    core = new List<(double, double)> { (r.CenterX - cfg.HalfX, r.CenterY - cfg.HalfY), (r.CenterX + cfg.HalfX, r.CenterY - cfg.HalfY), (r.CenterX + cfg.HalfX, r.CenterY + cfg.HalfY), (r.CenterX - cfg.HalfX, r.CenterY + cfg.HalfY) };
                else if (r.Shape == "capsule") core = Stadium(r.CenterX, r.CenterY, r.HeadingRad, cfg.HalfLengthM, cfg.RadiusM);
                else core = Stadium(r.CenterX, r.CenterY, 0, 0, cfg.RadiusM);
                GroundLoop(f, core, 20, col);
                if (r.Shape != "box") GroundLoop(f, r.Shape == "capsule" ? Stadium(r.CenterX, r.CenterY, r.HeadingRad, cfg.HalfLengthM, cfg.RadiusM + cfg.BlendM) : Stadium(r.CenterX, r.CenterY, 0, 0, cfg.RadiusM + cfg.BlendM), 20, col * 0.5f);
            }
            AtmosphereField.CalmZone z = f.Calm;
            if (z == null) return;
            var calm = new Color(0.3f, 0.9f, 1f);
            var inner = Stadium(z.CentreX, z.CentreY, z.HeadingRad, z.HalfLengthM, z.RadiusM);
            GroundLoop(f, inner, 15, calm);
            GroundLoop(f, Stadium(z.CentreX, z.CentreY, z.HeadingRad, z.HalfLengthM, z.RadiusM + z.BlendWidthM), 15, calm * 0.55f);
            // The ceiling, flat, and a post every so often down to the ground.
            for (int i = 0; i < inner.Count; i++)
            {
                var (x0, y0) = inner[i]; var (x1, y1) = inner[(i + 1) % inner.Count];
                Line(_static, new Vec3(x0, y0, -z.CeilingMslM), new Vec3(x1, y1, -z.CeilingMslM), calm);
                if (i % 6 == 0) Line(_static, new Vec3(x0, y0, -f.GroundAt(x0, y0)), new Vec3(x0, y0, -z.CeilingMslM), calm * 0.7f);
            }
        }

        private void BuildDynamic(AtmosphereField f, Vec3 at)
        {
            _dynamic.Clear();
            double t = Atmosphere.SimTimeSec;
            _here = f.Sample(at, t);
            // Wind arrows at the aircraft's height.
            for (int i = -ArrowHalf; i <= ArrowHalf; i++)
                for (int j = -ArrowHalf; j <= ArrowHalf; j++)
                {
                    double x = System.Math.Round(at.X / ArrowSpacingM) * ArrowSpacingM + i * ArrowSpacingM;
                    double y = System.Math.Round(at.Y / ArrowSpacingM) * ArrowSpacingM + j * ArrowSpacingM;
                    var p = new Vec3(x, y, at.Z);
                    if (-p.Z < f.GroundAt(x, y) + 5) continue;
                    Vec3 w = Atmosphere.WindFromSample(p, f.Sample(p, t), false);
                    Vec3 h = new Vec3(w.X, w.Y, 0) * ArrowSeconds;
                    Line(_dynamic, p, p + h, Color.white);
                    if (h.LengthSquared > 1)
                    {
                        Vec3 back = h * -0.25, side = new Vec3(-h.Y, h.X, 0) * 0.12;
                        Line(_dynamic, p + h, p + h + back + side, Color.white);
                        Line(_dynamic, p + h, p + h + back - side, Color.white);
                    }
                    Line(_dynamic, p, p + new Vec3(0, 0, w.Z * ArrowSeconds), w.Z < 0 ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.3f, 0.25f));
                }
            // The lift/sink map 300 m above the ground.
            for (int i = -MapHalf; i <= MapHalf; i++)
                for (int j = -MapHalf; j <= MapHalf; j++)
                {
                    double x = System.Math.Round(at.X / MapSpacingM) * MapSpacingM + i * MapSpacingM;
                    double y = System.Math.Round(at.Y / MapSpacingM) * MapSpacingM + j * MapSpacingM;
                    double g = f.GroundAt(x, y);
                    double wUp = -Atmosphere.WindFromSample(new Vec3(x, y, -(g + 300)), f.Sample(new Vec3(x, y, -(g + 300)), t), false).Z;
                    if (System.Math.Abs(wUp) < 0.25) continue;
                    float a = Mathf.Clamp01((float)System.Math.Abs(wUp) / 3f);
                    Color c = wUp > 0 ? new Color(0.3f, 1f, 0.4f, a) : new Color(1f, 0.3f, 0.25f, a);
                    double s = MapSpacingM * 0.3 * (0.4 + 0.6 * a);
                    Line(_dynamic, new Vec3(x - s, y, -(g + 3)), new Vec3(x + s, y, -(g + 3)), c);
                    Line(_dynamic, new Vec3(x, y - s, -(g + 3)), new Vec3(x, y + s, -(g + 3)), c);
                }
            // The authored thermal columns, as rings at their core radius at the aircraft's height.
            foreach (Thermal th in Atmosphere.Thermals)
            {
                Vec3 core = th.CoreAt(-at.Z);
                for (int k = 0; k < 24; k++)
                {
                    double a0 = 2 * System.Math.PI * k / 24, a1 = 2 * System.Math.PI * (k + 1) / 24;
                    Line(_dynamic, new Vec3(core.X + th.CoreRadiusM * System.Math.Cos(a0), core.Y + th.CoreRadiusM * System.Math.Sin(a0), at.Z),
                                   new Vec3(core.X + th.CoreRadiusM * System.Math.Cos(a1), core.Y + th.CoreRadiusM * System.Math.Sin(a1), at.Z), new Color(1f, 0.8f, 0.3f));
                }
            }
        }

        private void OnPostRender()
        {
            if (!SessionSettings.ShowAtmosphereMap || Atmosphere.Field == null || _mat == null || SessionSettings.MenuOpen) return;
            _mat.SetPass(0);
            GL.PushMatrix();
            GL.Begin(GL.LINES);
            foreach (var (a, b, c) in _static) { GL.Color(c); GL.Vertex(a); GL.Vertex(b); }
            foreach (var (a, b, c) in _dynamic) { GL.Color(c); GL.Vertex(a); GL.Vertex(b); }
            GL.End();
            GL.PopMatrix();
        }

        private void OnGUI()
        {
            if (!SessionSettings.ShowAtmosphereMap || Atmosphere.Field == null || SessionSettings.MenuOpen) return;
            _style ??= new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height / 55f), normal = { textColor = Color.white } };
            AtmosphereSample s = _here;
            double kt = 1 / AtmosphereField.KtToMs;
            double spd = System.Math.Sqrt(s.MeanWind.X * s.MeanWind.X + s.MeanWind.Y * s.MeanWind.Y);
            double from = (System.Math.Atan2(-s.MeanWind.Y, -s.MeanWind.X) * 180 / System.Math.PI + 360) % 360;
            string text = $"AIR [{Toggle}]  {s.RegionId}{(s.CalmWeight > 0 ? $"  calm {s.CalmWeight:P0}" : "")}\n"
                + $"wind {from:000}° {spd * kt:F0} kt  w {-s.MeanWind.Z:+0.0;-0.0} m/s  gust σ {s.SigmaHorizMs:F1}/{s.SigmaVertMs:F1} m/s  L {s.LengthHorizM:F0} m\n"
                + $"OAT {s.TemperatureK - 273.15:F0}°C (ISA{s.IsaDeviationK:+0;-0})  QNH {s.QnhPa / 100:F0} hPa  DA {s.DensityAltitudeM * 3.28084:F0} ft\n"
                + $"slope {s.SlopeLiftMs:+0.0;-0.0}  lee {s.LeeMs:+0.0;-0.0}  wave {s.WaveMs:+0.0;-0.0}  conv {s.ConvectiveMs:+0.0;-0.0}  speed-up {s.SpeedUp:P0}";
            GUI.Label(new Rect(Screen.width * 0.02f, Screen.height * 0.12f, Screen.width * 0.6f, Screen.height * 0.2f), text, _style);
        }
    }
}
