using FlyingGame.Core;
using FlyingGame.Core.Nav;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The navigation radios and the two instruments you need to find your way without a moving map: a
    /// course deviation indicator per receiver, a heading card, and a clock.
    ///
    /// Owner, 2026-10-18: *"they have to navigate by ded reconing or vor only"*. The chart
    /// (<see cref="ChartPanel"/>) refuses to say where you are, so this is the half that lets you work it
    /// out. Dead reckoning needs a heading and a time; a cross fix needs two radials. Both are here and
    /// nothing else is.
    ///
    /// Unlike the chart, this DOES read the aeroplane's position — it has to, because that is what a radio
    /// receiver does. What it never does is turn that into a position on a map: it gives you a radial, a
    /// needle and a distance, and the triangulation is yours.
    ///
    /// Every receiver is line of sight through <see cref="NavReception"/>, so behind a ridge the needle
    /// flags, and the flag is the real one: OFF means the signal is not there, and the needle parks central
    /// rather than lying to you. Overhead a station the cone of confusion is shown as such rather than as a
    /// needle thrashing about, because a pilot reads station passage from that, and it is a fix.
    /// </summary>
    public sealed class NavRadios : MonoBehaviour
    {
        public FlightSimDriver Driver;

        /// <summary>The two receivers' frequencies and their course selectors. Static so OPTIONS can tune them.</summary>
        public static readonly double[] Freq = { 115.70, 113.40 };
        public static readonly double[] Obs = { 360, 360 };
        /// <summary>The ADF's frequency, kHz. One receiver: a light aeroplane has one.</summary>
        public static double AdfKhz = 353.0;
        /// <summary>Is the range receiver switched on? It is the oldest box in the panel and the loudest.</summary>
        public static bool RangeOn;
        public static bool Shown = true;

        /// <summary>The elapsed-time clock a dead-reckoning leg is timed on. Seconds.</summary>
        public static double LegSeconds;
        public static bool LegRunning;

        /// <summary>Full-scale needle deflection, degrees: a VOR is 10, a localizer 2.5, a glideslope 0.7.</summary>
        public const double VorFullScaleDeg = 10.0, LocFullScaleDeg = 2.5, GsFullScaleDeg = 0.7;

        private GUIStyle _label, _small, _flag, _sbtn;
        private Texture2D _bg, _white, _green, _amber;
        private int _fs;

        private static Texture2D Solid(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }

        /// <summary>What one receiver is reading this instant.</summary>
        public struct Reading
        {
            public bool Tuned;             // the frequency matches something
            public string Id;
            public bool Localizer;
            public bool Usable;            // line of sight says the signal is there
            public bool Cone;              // overhead the station: a fix, not a fault
            public double RadialFrom;      // the radial you are on
            public double Deviation;       // degrees off the selected course, + = course is to the right
            public bool ToStation;
            public bool HasDme;
            public double DmeNm;
            public bool HasGlideslope;
            public double GlideslopeDev;   // degrees above (+) or below (-) the path
        }

        /// <summary>Read a receiver. Pure given the world and a position, so the HUD and a test agree.</summary>
        public static Reading Read(WorldTerrain t, double freq, double obs, double x, double y, double altM)
        {
            var r = new Reading();
            if (t == null) return r;

            foreach (Vor v in Navaids.Vors)
            {
                if (System.Math.Abs(v.FreqMhz - freq) > 0.005) continue;
                r.Tuned = true; r.Id = v.Id; r.HasDme = v.HasDme;
                r.Cone = NavReception.InConeOfConfusion(v.X, v.Y, v.AntennaM, x, y, altM);
                r.Usable = r.Cone
                        || NavReception.PathSignal(t, v.X, v.Y, v.AntennaM, x, y, altM, v.FreqMhz) >= Airways.UsableSignal;
                r.RadialFrom = NavReception.RadialDeg(v.X, v.Y, x, y);
                r.DmeNm = NavReception.SlantRangeNm(v.X, v.Y, v.AntennaM, x, y, altM);
                r.Deviation = NavReception.CourseDeviationDeg(obs, r.RadialFrom, out bool to);
                r.ToStation = to;
                return r;
            }

            foreach (Approach a in Approaches.All)
            {
                if (!a.Localizer.HasValue) continue;
                Ils ils = a.Localizer.Value;
                if (System.Math.Abs(ils.FreqMhz - freq) > 0.005) continue;
                r.Tuned = true; r.Id = ils.Id; r.Localizer = true; r.HasGlideslope = ils.HasGlideslope;
                // The localizer antenna stands beyond the stop end, so its course line IS the centreline.
                double c = ils.CourseDeg * System.Math.PI / 180.0;
                double ux = System.Math.Cos(c), uy = System.Math.Sin(c);
                const double runwayAndBeyondM = 1125 + 300;
                double ax = ils.ThresholdX + ux * runwayAndBeyondM, ay = ils.ThresholdY + uy * runwayAndBeyondM;
                r.Usable = NavReception.PathSignal(t, ax, ay, ils.TdzeM + 5, x, y, altM, ils.FreqMhz) >= Airways.UsableSignal;
                // Deviation is the angle between the course and the bearing from the antenna, which is why a
                // localizer gets narrower as you close in rather than staying a fixed width.
                double brg = NavReception.RadialDeg(ax, ay, x, y);
                r.Deviation = NavReception.LocalizerDeviationDeg(ils.CourseDeg, brg);
                r.ToStation = true;
                r.RadialFrom = brg;
                double dx = x - ils.ThresholdX, dy = y - ils.ThresholdY;
                double outNm = -(dx * ux + dy * uy) / NavReception.MetresPerNm;   // back down the final
                r.DmeNm = System.Math.Abs(outNm);
                if (ils.HasGlideslope && outNm > 0.05)
                {
                    double onPathFt = Terps.GlidepathHeightFt(outNm, ils.GlideslopeDeg);
                    double hereFt = (altM - ils.TdzeM) * Terps.FtPerM;
                    // Angle off the path, which is what the needle shows.
                    double angleHere = System.Math.Atan2(hereFt / Terps.FtPerM, outNm * NavReception.MetresPerNm) * 180 / System.Math.PI;
                    r.GlideslopeDev = angleHere - ils.GlideslopeDeg;
                    _ = onPathFt;
                }
                return r;
            }
            return r;
        }

        private void Update()
        {
            if (LegRunning) LegSeconds += Time.unscaledDeltaTime;
        }

        private void EnsureStyles()
        {
            int fs = Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) * 0.019f);
            if (_label != null && fs == _fs) return;
            _fs = fs;
            _bg ??= Solid(new Color(0.03f, 0.05f, 0.07f, 0.8f));
            _white ??= Solid(Color.white);
            _green ??= Solid(new Color(0.3f, 1f, 0.4f));
            _amber ??= Solid(new Color(1f, 0.72f, 0.2f));
            Font f = UiFont.Get();
            _label = new GUIStyle { font = f, fontSize = fs, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.85f, 0.92f, 1f) } };
            _small = new GUIStyle(_label) { fontSize = Mathf.RoundToInt(fs * 0.82f) };
            _flag = new GUIStyle(_label) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.55f, 0.3f) } };
            var btnBg = Solid(new Color(0.18f, 0.24f, 0.32f, 0.95f));
            _sbtn = new GUIStyle(_small) { normal = { textColor = Color.white, background = btnBg }, active = { textColor = Color.white, background = btnBg } };
        }

        private void OnGUI()
        {
            if (!Shown || SessionSettings.MenuOpen || ChartPanel.IsOpen) return;
            if (Driver?.Sim?.Aircraft == null) return;
            EnsureStyles();
            var t = WorldTerrain.Active;
            if (t == null) return;

            var p = Driver.Sim.Aircraft.State.Position;
            double altM = -p.Z;
            double heading = Heading();

            // One block in the shared stack, so it can never land on a dial, a button or a lesson line.
            float h = _fs * 5.6f;
            Rect blk = UiLayout.NextBlock(h);
            float w = Mathf.Min(blk.width, _fs * 66f);
            var box = new Rect(blk.center.x - w * 0.5f, blk.y, w, h);
            GUI.DrawTexture(box, _bg);

            float cw = w / 6f;
            DrawReceiver(new Rect(box.x, box.y, cw, h), 0, t, p.X, p.Y, altM);
            DrawReceiver(new Rect(box.x + cw, box.y, cw, h), 1, t, p.X, p.Y, altM);
            DrawAdf(new Rect(box.x + cw * 2, box.y, cw, h), t, p.X, p.Y, altM, heading);
            DrawRange(new Rect(box.x + cw * 3, box.y, cw, h), t, p.X, p.Y, altM);
            DrawHeading(new Rect(box.x + cw * 4, box.y, cw, h), heading);
            DrawClock(new Rect(box.x + cw * 5, box.y, cw, h));
        }

        private double Heading()
        {
            var fwd = Driver.Sim.Aircraft.State.Attitude.Rotate(new FlyingGame.Core.MathTypes.Vec3(1, 0, 0));
            double deg = Mathf.Atan2((float)fwd.Y, (float)fwd.X) * Mathf.Rad2Deg;
            return deg < 0 ? deg + 360 : deg;
        }

        private void DrawReceiver(Rect r, int i, WorldTerrain t, double x, double y, double altM)
        {
            Reading rd = Read(t, Freq[i], Obs[i], x, y, altM);
            float pad = _fs * 0.3f;
            GUI.Label(new Rect(r.x, r.y + pad * 0.5f, r.width, _fs * 1.2f),
                      $"NAV {i + 1}   {Freq[i]:0.00}   {(rd.Tuned ? rd.Id : "—")}", _small);

            // The course window, and a tap either side of it to turn the OBS. A localizer has no OBS.
            float row = r.y + _fs * 1.5f;
            if (!rd.Localizer)
            {
                float bw = _fs * 1.6f;
                if (GUI.Button(new Rect(r.x + pad, row, bw, _fs * 1.3f), "←", _small)) Obs[i] = (Obs[i] + 359) % 360;
                if (GUI.Button(new Rect(r.xMax - pad - bw, row, bw, _fs * 1.3f), "→", _small)) Obs[i] = (Obs[i] + 1) % 360;
                GUI.Label(new Rect(r.x + bw + pad, row, r.width - 2 * (bw + pad), _fs * 1.3f),
                          $"CRS {Obs[i]:000}°", _label);
            }
            else GUI.Label(new Rect(r.x, row, r.width, _fs * 1.3f), "LOC — no OBS", _small);

            // The needle: a scale with five dots either side, and the bar where the course is.
            float barY = row + _fs * 1.8f;
            float half = r.width * 0.38f;
            Vector2 c = new(r.center.x, barY);
            for (int d = -5; d <= 5; d++)
            {
                if (d == 0) continue;
                GUI.DrawTexture(new Rect(c.x + half * d / 5f - 1, c.y - 1, 2, 2), _white);
            }
            if (!rd.Tuned || !rd.Usable)
            {
                GUI.Label(new Rect(r.x, barY - _fs * 0.7f, r.width, _fs * 1.4f), rd.Tuned ? "OFF" : "NO STATION", _flag);
            }
            else if (rd.Cone)
            {
                GUI.Label(new Rect(r.x, barY - _fs * 0.7f, r.width, _fs * 1.4f), "OVER THE STATION", _flag);
            }
            else
            {
                double full = rd.Localizer ? LocFullScaleDeg : VorFullScaleDeg;
                float f = Mathf.Clamp((float)(rd.Deviation / full), -1f, 1f);
                Color old = GUI.color;
                GUI.color = Mathf.Abs(f) < 0.2f ? new Color(0.3f, 1f, 0.4f) : Color.white;
                GUI.DrawTexture(new Rect(c.x + half * f - 1.5f, c.y - _fs * 0.8f, 3, _fs * 1.6f), _white);
                GUI.color = old;
                string tf = rd.ToStation ? "TO" : "FROM";
                string dme = rd.HasDme || rd.Localizer ? $"   {rd.DmeNm:0.0} NM" : "";
                GUI.Label(new Rect(r.x, barY + _fs * 0.9f, r.width, _fs * 1.2f),
                          $"R-{rd.RadialFrom:000}  {tf}{dme}", _small);
                if (rd.HasGlideslope)
                {
                    // The glideslope, vertical, on the right-hand edge.
                    float gx = r.xMax - _fs * 0.9f;
                    for (int d = -2; d <= 2; d++) GUI.DrawTexture(new Rect(gx - 1, c.y + _fs * 1.1f * d - 1, 2, 2), _white);
                    float gf = Mathf.Clamp((float)(rd.GlideslopeDev / GsFullScaleDeg), -1f, 1f);
                    GUI.DrawTexture(new Rect(gx - _fs * 0.5f, c.y - gf * _fs * 2.2f - 1.5f, _fs, 3), _green);
                }
            }
        }

        /// <summary>
        /// The ADF. A needle that points AT the beacon, relative to the nose, and nothing else — no course
        /// selector, no TO/FROM, no deviation bar. Working out where you are from a needle that points at a
        /// thing is the whole skill, and it is why an ADF approach is harder than a VOR one even though the
        /// instrument is simpler.
        ///
        /// It also tells you when it is lying: the terrain bends a low-frequency signal, so the needle leans
        /// toward high ground, and when that error is worth knowing about the panel says so rather than
        /// letting you trust it.
        /// </summary>
        private void DrawAdf(Rect r, WorldTerrain t, double x, double y, double altM, double heading)
        {
            float pad = _fs * 0.3f;
            // Tune: the two beacons and off, which is the whole band worth having here.
            GUI.Label(new Rect(r.x, r.y + pad * 0.5f, r.width, _fs * 1.2f), $"ADF   {AdfKhz:0} kHz", _small);
            float bw = (r.width - pad * 3) / 2f;
            var ndbs = Navaids.Ndbs;
            for (int i = 0; i < ndbs.Length && i < 2; i++)
                if (GUI.Button(new Rect(r.x + pad + i * (bw + pad), r.y + _fs * 1.4f, bw, _fs * 1.3f),
                               $"{ndbs[i].Id} {ndbs[i].FreqKhz:0}",
                               System.Math.Abs(AdfKhz - ndbs[i].FreqKhz) < 0.5 ? _sbtn : _small))
                    AdfKhz = ndbs[i].FreqKhz;

            Ndb tuned = default;
            bool found = false;
            foreach (Ndb n in ndbs) if (System.Math.Abs(n.FreqKhz - AdfKhz) < 0.5) { tuned = n; found = true; break; }

            Vector2 c = new(r.center.x, r.y + _fs * 3.9f);
            float rad = _fs * 1.3f;
            // The card: a circle with the nose at the top.
            for (int k = 0; k < 12; k++)
            {
                float a = k * 30f * Mathf.Deg2Rad;
                var p0 = c + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * rad;
                GUI.DrawTexture(new Rect(p0.x - 1, p0.y - 1, 2, 2), _white);
            }
            if (!found) { GUI.Label(new Rect(r.x, c.y - _fs * 0.7f, r.width, _fs * 1.4f), "NO BEACON", _flag); return; }
            double sig = NavReception.NdbSignal(tuned, x, y);
            if (sig < 0.2) { GUI.Label(new Rect(r.x, c.y - _fs * 0.7f, r.width, _fs * 1.4f), "OUT OF RANGE", _flag); return; }

            double trueBrg = NavReception.BearingToDeg(x, y, tuned.X, tuned.Y);
            double err = NavReception.NdbBearingErrorDeg(t, tuned, x, y, altM);
            double rb = NavReception.RelativeBearingDeg(trueBrg + err, heading);
            float ang = (float)rb * Mathf.Deg2Rad;
            Vector2 tip = c + new Vector2(Mathf.Sin(ang), -Mathf.Cos(ang)) * rad;
            Line(c - new Vector2(Mathf.Sin(ang), -Mathf.Cos(ang)) * rad * 0.45f, tip, new Color(1f, 0.85f, 0.3f), 2.5f);
            GUI.Label(new Rect(r.x, r.yMax - _fs * 1.15f, r.width, _fs),
                      System.Math.Abs(err) >= 2.0 ? $"RB {rb:000}  terrain error" : $"RB {rb:000}  MB {(trueBrg + err + 360) % 360:000}",
                      System.Math.Abs(err) >= 2.0 ? _flag : _small);
        }

        private void Line(Vector2 a, Vector2 b, Color col, float width)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.5f) return;
            Matrix4x4 m = GUI.matrix;
            Color old = GUI.color;
            GUI.color = col;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, len, width), _white);
            GUI.matrix = m;
            GUI.color = old;
        }

        private RangeAudio _audio;

        /// <summary>
        /// The range receiver. There is no needle to draw, because a range has none — so what is on the
        /// screen is only what you could also hear: the letter, whether the note is steady, and which
        /// course you are nearest. The sound is the instrument (see <see cref="RangeAudio"/>), and the
        /// writing on the glass is a courtesy for anybody flying with the volume down.
        /// </summary>
        private void DrawRange(Rect r, WorldTerrain t, double x, double y, double altM)
        {
            float pad = _fs * 0.3f;
            var ranges = Navaids.Ranges;
            AnRange rng = ranges.Length > 0 ? ranges[0] : default;
            GUI.Label(new Rect(r.x, r.y + pad * 0.5f, r.width, _fs * 1.2f),
                      ranges.Length > 0 ? $"RANGE  {rng.FreqKhz:0} kHz  {rng.Id}" : "RANGE", _small);
            if (GUI.Button(new Rect(r.x + pad, r.y + _fs * 1.4f, r.width - 2 * pad, _fs * 1.3f),
                           RangeOn ? "ON" : "OFF", RangeOn ? _sbtn : _small)) RangeOn = !RangeOn;

            if (ranges.Length == 0) return;
            RangeTone tone = AnRangeSignal.Read(t, rng, x, y, altM, out double off, out double near, out double sig);
            bool live = RangeOn && tone != RangeTone.Silent;

            // Hand it to the audio thread. The tone IS the instrument.
            if (_audio == null)
            {
                var go = new GameObject("RangeAudio");
                go.transform.SetParent(transform, false);
                _audio = go.AddComponent<RangeAudio>();
            }
            _audio.Set(live, off, AnRangeSignal.IsAQuadrant(tone), 1f);

            if (!RangeOn) { GUI.Label(new Rect(r.x, r.y + _fs * 3.1f, r.width, _fs * 1.3f), "receiver off", _small); return; }

            // The big letter, which is what a pilot would say out loud.
            string big = tone switch
            {
                RangeTone.OnCourse => "\u2014\u2014",      // one unbroken note
                RangeTone.A or RangeTone.BiSignalA => "A",
                RangeTone.N or RangeTone.BiSignalN => "N",
                _ => sig < 0.2 ? "—" : "CONE",
            };
            var style = new GUIStyle(_label)
            {
                fontSize = Mathf.RoundToInt(_fs * 1.8f),
                fontStyle = FontStyle.Bold,
                normal = { textColor = tone == RangeTone.OnCourse ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.85f, 0.35f) },
            };
            GUI.Label(new Rect(r.x, r.y + _fs * 2.9f, r.width, _fs * 1.9f), big, style);
            string line = tone == RangeTone.Silent
                ? (sig < 0.2 ? "out of range" : "CONE OF SILENCE — overhead")
                : $"crs {near:000}  {(System.Math.Abs(off) < AnRangeSignal.OnCourseHalfWidthDeg ? "on" : $"{(off > 0 ? "course right" : "course left")}")}";
            GUI.Label(new Rect(r.x, r.yMax - _fs * 1.15f, r.width, _fs), line, _small);
        }

        private void DrawHeading(Rect r, double heading)
        {
            GUI.Label(new Rect(r.x, r.y + _fs * 0.15f, r.width, _fs * 1.2f), "HEADING", _small);
            GUI.Label(new Rect(r.x, r.y + _fs * 1.4f, r.width, _fs * 1.8f), $"{heading:000}°",
                      new GUIStyle(_label) { fontSize = Mathf.RoundToInt(_fs * 1.7f) });
            // A card: the compass points sliding past a lubber line, which is what you steer on.
            float y = r.y + _fs * 3.6f, half = r.width * 0.42f;
            Vector2 c = new(r.center.x, y);
            GUI.DrawTexture(new Rect(c.x - 1, y - _fs * 0.7f, 2, _fs * 1.4f), _amber);
            for (int d = -60; d <= 60; d += 10)
            {
                double mark = (heading + d + 360) % 360;
                float px = c.x + half * d / 60f;
                bool cardinal = Mathf.Abs((float)(mark % 90)) < 0.5f;
                GUI.DrawTexture(new Rect(px - 0.5f, y + _fs * 0.3f, 1, cardinal ? _fs * 0.6f : _fs * 0.35f), _white);
                if (d % 30 == 0)
                    GUI.Label(new Rect(px - _fs * 1.2f, y - _fs * 0.9f, _fs * 2.4f, _fs), $"{mark / 10:00}", _small);
            }
            GUI.Label(new Rect(r.x, r.yMax - _fs * 1.1f, r.width, _fs), "mag = true here", _small);
        }

        private void DrawClock(Rect r)
        {
            GUI.Label(new Rect(r.x, r.y + _fs * 0.15f, r.width, _fs * 1.2f), "LEG TIME", _small);
            int mm = (int)(LegSeconds / 60), ss = (int)(LegSeconds % 60);
            GUI.Label(new Rect(r.x, r.y + _fs * 1.4f, r.width, _fs * 1.8f), $"{mm:00}:{ss:00}",
                      new GUIStyle(_label) { fontSize = Mathf.RoundToInt(_fs * 1.7f) });
            float bw = (r.width - _fs) / 2f, by = r.y + _fs * 3.5f;
            if (GUI.Button(new Rect(r.x + _fs * 0.3f, by, bw, _fs * 1.4f), LegRunning ? "STOP" : "START", _sbtn)) LegRunning = !LegRunning;
            if (GUI.Button(new Rect(r.x + _fs * 0.5f + bw, by, bw, _fs * 1.4f), "RESET", _sbtn)) { LegSeconds = 0; LegRunning = true; }
            // Ground speed is NOT shown: knowing it would make the time redundant and the whole exercise is
            // to work the leg out from the airspeed you are flying and the wind you were given.
        }
    }
}
