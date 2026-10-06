using FlyingGame.Sim.Practice;
using UnityEngine;

namespace FlyingGame.Bridge.Practice
{
    /// <summary>
    /// Practice overlay: the briefing card (title, written instructions, countdown, GO), the live alignment display (a
    /// runway bar with the fuselage line over it — parallel is the goal), off-centre, glideslope deviation, weight on
    /// wheels in the side views, and the result card with AGAIN / MENU.
    /// </summary>
    public sealed class PracticeHud : MonoBehaviour
    {
        public PracticeController Controller;
        public StartMenu Menu;
        public FlightSimDriver Driver;
        public LessonDebrief Debrief;

        private GUIStyle _title, _text, _big, _small, _btn, _btnOff;
        private Texture2D _card, _bar, _line, _btnBg, _btnOffBg;
        private int _fs;

        private static Texture2D Solid(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }

        private void Init()
        {
            int fs = Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) * 0.028f);
            if (_title != null && fs == _fs) return;
            _fs = fs;
            Font f = UiFont.Get();
            _title = new GUIStyle { font = f, fontSize = Mathf.RoundToInt(fs * 1.4f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.6f, 0.95f, 1f) } };
            _text = new GUIStyle { font = f, fontSize = fs, alignment = TextAnchor.UpperLeft, wordWrap = true, normal = { textColor = Color.white } };
            _big = new GUIStyle { font = f, fontSize = Mathf.RoundToInt(fs * 1.25f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            _small = new GUIStyle { font = f, fontSize = Mathf.RoundToInt(fs * 0.85f), alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.85f, 0.9f, 0.95f) } };
            _card ??= Solid(new Color(0.04f, 0.07f, 0.11f, 0.88f));
            _bar ??= Solid(new Color(0.35f, 0.35f, 0.38f, 0.95f));
            _line ??= Solid(Color.white);
            _btnBg ??= Solid(new Color(0.2f, 0.62f, 0.35f, 0.95f));
            _btn = new GUIStyle { font = f, fontSize = fs, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white, background = _btnBg }, active = { textColor = Color.white, background = _btnBg } };
            _btnOffBg ??= Solid(new Color(0.18f, 0.24f, 0.32f, 0.95f));
            _btnOff = new GUIStyle(_btn) { normal = { textColor = new Color(0.8f, 0.85f, 0.9f), background = _btnOffBg }, active = { textColor = Color.white, background = _btnBg } };
        }

        private void OnGUI()
        {
            if (Controller == null || !Controller.Active || Controller.Scenario == null || SessionSettings.MenuOpen) return;
            Init();
            PracticeScenario sc = Controller.Scenario;
            Rect view = ScreenLayout.Portrait ? new Rect(0f, 0f, Screen.width, Screen.height - ScreenLayout.TrayHeightPx) : new Rect(0f, 0f, Screen.width, Screen.height);
            // A strips lesson in landscape: the cards and their backdrop keep clear of the elevator / throttle strips, which stay
            // up through the briefing so the thumb knows where to go (owner 2026-10-06).
            if (!ScreenLayout.Portrait && Controller.StripsShown)
            {
                float l = Mathf.Max(0f, UiLayout.BandMin), r = Mathf.Min(Screen.width, UiLayout.BandMax);
                if (r - l > Screen.width * 0.4f) view = new Rect(l, view.y, r - l, view.height);
            }
            float lh = _fs * 1.5f;

            if (sc.Phase == PracticePhase.Briefing || sc.Phase == PracticePhase.Finished) UiLayout.ModalShown();   // cards: the HUD steps aside
            if (sc.Phase == PracticePhase.Briefing)
            {
                if (Controller.Counting) { DrawCountdown(view, lh); return; }
                // Owner 2026-10-05: the setup page comes first and the world isn't shown until the aircraft has been placed
                // for the chosen setup — an opaque page under the toolbar.
                _backdrop ??= Solid(new Color(0.05f, 0.08f, 0.12f, 1f));
                float tb = Mathf.Max(view.y, UiLayout.ToolbarBottom);
                GUI.DrawTexture(new Rect(view.x, tb, view.width, view.yMax - tb), _backdrop);
                if (sc.LessonPages.Length > 0 && Controller.LessonPage < sc.LessonPages.Length) DrawLessonPage(sc, view, lh);
                else DrawBriefing(sc, view, lh);
                return;
            }
            // Live read-out: its own block in the shared text stack (no-overlap rule), lines laid out one after another.
            if (sc.Phase == PracticePhase.Finished) { DrawResult(sc, view, lh); return; }   // the debrief (or its replay)
            DrawOrb(sc, UiLayout.NextBlock(lh * 1.9f), lh);
            Rect block = UiLayout.NextBlock(lh * 4.2f);
            DrawLive(sc, block, lh);
        }

        /// <summary>An illustrated page: the picture rendered from the game, the written explanation, NEXT.</summary>
        private Texture2D _backdrop;

        private void DrawLessonPage(PracticeScenario sc, Rect view, float lh)
        {
            var page = sc.LessonPages[Controller.LessonPage];
            float w = Mathf.Min(view.width * 0.94f, _fs * 30f);
            float imgH = w * 0.625f;
            float h = lh * 1.4f + imgH + lh * 5.2f + lh * 1.4f;
            var card = new Rect(view.x + (view.width - w) * 0.5f, view.y + Mathf.Max(_fs * 2f, (view.height - h) * 0.35f), w, h);
            GUI.DrawTexture(card, _card);
            float x = card.x + _fs * 0.5f, y = card.y + _fs * 0.4f, cw = card.width - _fs;
            GUI.Label(new Rect(x, y, cw, lh * 1.2f), $"{page.title}   ({Controller.LessonPage + 1}/{sc.LessonPages.Length})", _title); y += lh * 1.3f;
            Texture2D pic = Controller.LessonPictures.Length > Controller.LessonPage ? Controller.LessonPictures[Controller.LessonPage] : null;
            var imgRect = new Rect(x, y, cw, imgH);
            if (pic != null) GUI.DrawTexture(imgRect, pic, ScaleMode.ScaleToFit); else GUI.Label(imgRect, "(picture)", _small);
            y += imgH + _fs * 0.3f;
            string legend = sc.Kind switch
            {
                PracticeKind.Straight => Controller.LessonPage == 0 ? "green = lift (tilted with the bank) · orange = its sideways part · grey = weight" : "white = relative wind of the slip · yellow = vertical tail force · orange = the nose swings",
                PracticeKind.ClimbLevelDescend => Controller.LessonPage == 0 ? "blue = slipstream spiral · yellow = its push on the fin · green = the descending blade's extra thrust · orange = nose yaws LEFT" : "yellow = the tail load at this speed · orange = the trim tab set to carry it (stick force zero)",
                PracticeKind.GlideRear or PracticeKind.GlideSide => Controller.LessonPage == 0 ? $"speed → · sink ↓ · yellow = min sink {sc.MinSinkMs * 1.944:F0} kt · green = best glide {sc.BestLdMs * 1.944:F0} kt ({sc.BestGlideRatio:F0}:1) · orange = speed to fly in this wind {sc.SpeedToFlyMs * 1.944:F0} kt" : "green = best glide · yellow = min sink (slower, steeper over the ground) · orange = speed to fly in this wind",
                _ => Controller.LessonPage == 0 ? $"speed → · climb rate ↑ · green = Vy {sc.VyMs * 1.944:F0} kt ({sc.RocAtVyMs * 196.85:F0} fpm) · orange = Vx {sc.VxMs * 1.944:F0} kt" : "orange = Vx, the steeper path over the obstacle · green = Vy, shallower but quicker to altitude",
            };
            GUI.Label(new Rect(x, y, cw, lh * 0.8f), legend, _small); y += lh * 0.9f;
            GUI.Label(new Rect(x, y, cw, lh * 4.0f), page.text, _text); y += lh * 4.1f;
            if (GUI.Button(new Rect(card.center.x - _fs * 4f, y, _fs * 8f, lh * 1.1f), "NEXT", _btn)) Controller.NextLessonPage();
        }

        private void DrawCountdown(Rect view, float lh)
        {
            int n = Mathf.CeilToInt(Controller.CountdownLeft - 0.2f);
            string txt = n >= 1 ? n.ToString() : "GO";
            var big = new GUIStyle(_title) { fontSize = Mathf.RoundToInt(_fs * 5f), normal = { textColor = n >= 1 ? Color.white : new Color(0.45f, 1f, 0.5f) } };
            UiLayout.Label(new Rect(view.x, view.y + view.height * 0.35f, view.width, lh * 5f), txt, big);
            UiLayout.Label(new Rect(view.x, view.y + view.height * 0.35f + lh * 5f, view.width, lh), Controller.Scenario.HandoverLine, _big);
        }

        // ---- points: the orb ---------------------------------------------------------------------------------------
        private static readonly Color[] GradeColours = { new(0.25f, 0.92f, 0.35f), new(1f, 0.88f, 0.2f), new(1f, 0.55f, 0.12f), new(0.95f, 0.16f, 0.12f) };
        private static readonly string[] GradeWords = { "GREEN", "YELLOW", "ORANGE", "RED" };
        private Texture2D _orbTex;
        private Texture2D OrbTex()
        {
            if (_orbTex != null) return _orbTex;
            const int n = 96; _orbTex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int i = 0; i < n; i++) for (int k = 0; k < n; k++)
            {
                float dx = (i + 0.5f) / n * 2 - 1, dy = (k + 0.5f) / n * 2 - 1, r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01((1f - r) * n * 0.25f);
                float shade = 0.75f + 0.25f * Mathf.Clamp01(1f - Mathf.Sqrt((dx + 0.35f) * (dx + 0.35f) + (dy - 0.35f) * (dy - 0.35f)) * 1.4f);   // a highlight up-left
                _orbTex.SetPixel(i, k, new Color(shade, shade, shade, a));
            }
            _orbTex.Apply();
            return _orbTex;
        }

        private void Orb(Rect r, Grade g) { GUI.color = GradeColours[(int)g]; GUI.DrawTexture(r, OrbTex()); GUI.color = Color.white; }

        /// <summary>The live orb (green/yellow/orange/red — never which way) and the running points.</summary>
        private void DrawOrb(PracticeScenario sc, Rect block, float lh)
        {
            LessonJudge j = sc.Judge;
            float d = block.height * 0.92f;
            float total = d + _fs * 12f;
            float x0 = block.center.x - total * 0.5f;
            Orb(new Rect(x0, block.y + (block.height - d) * 0.5f, d, d), j.Current);
            var big = new GUIStyle(_title) { alignment = TextAnchor.MiddleLeft, fontSize = Mathf.RoundToInt(_fs * 1.7f), normal = { textColor = Color.white } };
            UiLayout.Label(new Rect(x0 + d + _fs * 0.6f, block.y, _fs * 7f, block.height * 0.62f), $"{j.Points:F0} pts", big);
            double rate = LessonJudge.RatePerSec[(int)j.Current];
            var sm = new GUIStyle(_small) { alignment = TextAnchor.MiddleLeft, normal = { textColor = GradeColours[(int)j.Current] } };
            UiLayout.Label(new Rect(x0 + d + _fs * 0.6f, block.y + block.height * 0.6f, _fs * 11f, block.height * 0.4f), $"{GradeWords[(int)j.Current]}   {rate:+0;-0;0} per second", sm);
        }

        /// <summary>The briefing: the goal, how points are earned (the orb legend), what is judged and against what standard,
        /// and for the landings a side-view diagram of the ideal round-out and flare.</summary>
        private void DrawBriefing(PracticeScenario sc, Rect view, float lh)
        {
            LessonRules rules = sc.Judge.Rules;
            bool landing = rules.Moments.Exists(m => m.Name == LessonJudge.Std.TdSink.Name);
            int rows = rules.Live.Count + rules.Moments.Count;
            // SETUP (owner 2026-10-05): the flap setting is chosen HERE, before the aircraft is placed — each setting has its own
            // row of the idle-glide table (1.3 Vso, the power-off glide, the trim), and picking one re-places the aircraft on it.
            bool flapSetup = sc.FlareExercise && !sc.Airwork && GlideTable.HasFlaps(sc.Config);
            float setupRows = flapSetup ? 0.8f + 3f * 0.95f + 0.4f : 0f;
            float need = lh * (1.3f + 2.4f + setupRows + 1.5f + 0.8f + rows * 0.95f + (rules.Moments.Count > 0 ? 0.8f : 0f) + (landing ? 4.2f : 0f) + 1.0f + 1.3f) + _fs;
            float top0 = Mathf.Max(view.y, UiLayout.ToolbarBottom) + _fs * 0.4f, avail = view.yMax - top0 - _fs * 0.4f;
            float s = Mathf.Min(1f, avail / need);                                     // shrink to fit under the toolbar (no-overlap rule)
            float L = lh * s;
            float w = Mathf.Min(view.width * 0.94f, _fs * 34f), h = need * s;
            var card = new Rect(view.x + (view.width - w) * 0.5f, top0 + Mathf.Max(0f, (avail - h) * 0.4f), w, h);
            GUI.DrawTexture(card, _card);
            float x = card.x + _fs, y = card.y + _fs * 0.5f, cw = card.width - 2 * _fs;
            var txt = new GUIStyle(_text) { fontSize = Mathf.RoundToInt(_fs * s) };
            var sml = new GUIStyle(_small) { fontSize = Mathf.RoundToInt(_fs * 0.85f * s), alignment = TextAnchor.MiddleLeft };
            UiLayout.Label(new Rect(x, y, cw, L * 1.2f), sc.Title, _title); y += L * 1.3f;
            GUI.Label(new Rect(x, y, cw, L * 2.3f), rules.Goal, txt); y += L * 2.4f;
            if (flapSetup)
            {
                UiLayout.Label(new Rect(x, y, cw, L * 0.8f), "SETUP — FLAPS  (the lesson starts at idle on the power-off glide for this setting, in trim, aimed at the numbers from 100 ft)", sml); y += L * 0.8f;
                (double f, string n)[] set = { (0.0, "UP"), (0.5, "HALF"), (1.0, "FULL") };
                foreach (var (f, n) in set)
                {
                    bool on = System.Math.Abs(sc.Flaps - f) < 0.01;
                    var row = new Rect(x, y, cw, L * 0.9f);
                    if (GUI.Button(new Rect(x, y, _fs * 5f, L * 0.88f), n, on ? _btn : _btnOff)) { SessionSettings.LessonFlaps = (float)f; Controller.RestartAtCard(); }
                    GlideEntry e = GlideTable.Lookup(sc.Config.Id, f);
                    string info = e != null ? $"Vso {e.VsoKt:F0} kt · 1.3 Vso {e.SpeedKt:F0} kt · idle glide {e.GlideDeg:F1}° ({e.GlideRatio:F1}:1, {e.SinkFpm:F0} fpm) · trim {e.TrimStick:+0.00;-0.00}" : "computed for this airfield";
                    UiLayout.Label(new Rect(x + _fs * 5.6f, y, cw - _fs * 5.6f, L * 0.9f), info, sml);
                    y += L * 0.95f;
                }
                y += L * 0.4f;
            }
            // The orb legend: what each colour earns.
            float od = L * 0.9f, col = cw / 4f;
            for (int g = 0; g < 4; g++)
            {
                Orb(new Rect(x + g * col, y + (L * 1.3f - od) * 0.5f, od, od), (Grade)g);
                string lbl = g switch { 0 => "ideal  +10/s", 1 => "close  +4/s", 2 => "not good  0", _ => "bad  −6/s" };
                UiLayout.Label(new Rect(x + g * col + od + _fs * 0.3f, y, col - od - _fs * 0.4f, L * 1.3f), lbl, sml);
            }
            y += L * 1.5f;
            UiLayout.Label(new Rect(x, y, cw, L * 0.8f), "JUDGED ALL THE WAY  (the orb shows the worst of these; it never says which way you are off)", sml); y += L * 0.8f;
            foreach (Criterion c in rules.Live) { CriterionRow(c, x, y, cw, L, sml); y += L * 0.95f; }
            if (rules.Moments.Count > 0)
            {
                UiLayout.Label(new Rect(x, y, cw, L * 0.8f), "JUDGED MOMENTS  (green +100 · yellow +50 · orange 0 · red −50; a crash −300)", sml); y += L * 0.8f;
                foreach (Criterion c in rules.Moments) { CriterionRow(c, x, y, cw, L, sml); y += L * 0.95f; }
            }
            if (landing) { DrawFlareDiagram(new Rect(x, y, cw, L * 4f), sml); y += L * 4.2f; }
            UiLayout.Label(new Rect(x, y, cw, L), $"{sc.HandoverLine}   Press GO for a three-second countdown.", sml); y += L;
            if (GUI.Button(new Rect(card.center.x - _fs * 4f, y + L * 0.1f, _fs * 8f, L * 1.1f), "GO", _btn)) Controller.StartCountdown();
        }

        /// <summary>One table row: a criterion's name and ideal, then its green / yellow / orange limits as coloured chips.</summary>
        private void CriterionRow(Criterion c, float x, float y, float w, float L, GUIStyle st)
        {
            float nameW = w * 0.52f, chipW = (w - nameW) / 4f, od = L * 0.55f;
            UiLayout.Label(new Rect(x, y, nameW, L * 0.9f), $"{c.Name}  — ideal {c.Ideal}", st);
            string[] lim = { $"≤ {c.Green:0.##}", $"≤ {c.Yellow:0.##}", $"≤ {c.Orange:0.##}", $"> {c.Orange:0.##}" };
            for (int g = 0; g < 4; g++)
            {
                float cx = x + nameW + g * chipW;
                Orb(new Rect(cx, y + (L * 0.9f - od) * 0.5f, od, od), (Grade)g);
                UiLayout.Label(new Rect(cx + od + 2f, y, chipW - od - 4f, L * 0.9f), $"{lim[g]} {c.Unit}", st);
            }
        }

        /// <summary>Side view of the ideal landing: the glide path, the round-out beginning 10–20 ft up, the hold-off just
        /// over the runway as the speed decays, touchdown in the zone at the 1,000 ft markers.</summary>
        private void DrawFlareDiagram(Rect r, GUIStyle st)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.06f); GUI.DrawTexture(r, _line); GUI.color = Color.white;
            float gy = r.yMax - r.height * 0.22f;                                    // runway surface
            GUI.color = new Color(0.45f, 0.45f, 0.5f); GUI.DrawTexture(new Rect(r.x + r.width * 0.3f, gy, r.width * 0.68f, 3f), _line);
            GUI.color = new Color(0.25f, 0.92f, 0.35f, 0.5f); GUI.DrawTexture(new Rect(r.x + r.width * 0.62f, gy - 4f, r.width * 0.16f, 8f), _line);   // touchdown zone
            // Glide path → round-out curve → hold-off → touchdown (polyline of small dots).
            Vector2 P(float u)
            {
                float xx = r.x + r.width * (0.04f + 0.66f * u);
                float yy;
                if (u < 0.55f) yy = r.y + r.height * 0.08f + (gy - r.height * 0.42f - (r.y + r.height * 0.08f)) * (u / 0.55f);   // the path
                else { float k = (u - 0.55f) / 0.45f; yy = gy - r.height * 0.42f * Mathf.Pow(1f - k, 2.2f) - 2f; }                // round-out + hold-off
                return new Vector2(xx, yy);
            }
            GUI.color = new Color(0.6f, 0.95f, 1f);
            for (int i = 0; i <= 60; i++) { Vector2 p = P(i / 60f); GUI.DrawTexture(new Rect(p.x - 2f, p.y - 2f, 4f, 4f), _line); }
            GUI.color = Color.white;
            Vector2 ro = P(0.55f);
            GUI.DrawTexture(new Rect(ro.x - 1f, ro.y, 2f, gy - ro.y), _line);
            // Labels in the clear space around the path (no-overlap rule): the path runs top-left → bottom-right.
            var right = new GUIStyle(st) { alignment = TextAnchor.MiddleRight };
            UiLayout.Label(new Rect(r.x + r.width * 0.34f, r.y + r.height * 0.02f, r.width * 0.4f, r.height * 0.22f), "idle, on speed, in trim", st);
            UiLayout.Label(new Rect(r.x + r.width * 0.04f, gy - r.height * 0.34f, ro.x - r.x - r.width * 0.06f, r.height * 0.22f), "round out 10–20 ft", right);
            UiLayout.Label(new Rect(r.x + r.width * 0.72f, gy - r.height * 0.34f, r.width * 0.28f, r.height * 0.22f), "hold off — speed bleeds", st);
            UiLayout.Label(new Rect(r.x + r.width * 0.6f, gy + 4f, r.width * 0.4f, r.height * 0.2f), "aim: the numbers · touch down ~330 ft past", st);
        }

        private void DrawLive(PracticeScenario sc, Rect view, float lh)
        {
            if (sc.Airwork) { DrawAirwork(sc, view, lh); return; }
            // Owner 2026-10-03: no left/right display — the orb says how you are doing, not which way you are off.
            float y = view.y + lh * 0.9f;
            // (No live metrics — owner 2026-10-03: the numbers are in the debrief.)
            if (sc.Phase == PracticePhase.Live)
            {
                string goal = sc.UserRudder ? "RUDDER: keep the fuselage parallel to the runway" : sc.UserAileron ? "AILERON: stay over the centreline" : sc.Approach ? "PITCH for the path, POWER for the speed" : "ELEVATOR: round out, hold it off";
                UiLayout.Label(new Rect(view.x, view.y, view.width, lh * 0.8f), goal, _small);
            }
        }

        private void DrawAirwork(PracticeScenario sc, Rect view, float lh)
        {
            float y = view.y;
            var good = new Color(0.45f, 1f, 0.5f); var warn = new Color(1f, 0.6f, 0.3f);
            if (sc.Straight)
            {
                // (No bank bar or slip direction: the orb grades wings-level.)
                UiLayout.Label(new Rect(view.x, y, view.width, lh * 0.8f), "AILERON: keep the wings level (rudder is neutral)", _small);
                if (sc.Endless && sc.Phase == PracticePhase.Live)
                {
                    float bw2 = _fs * 5f;
                    // END lives in the shared toolbar (ViewPanel) — no-overlap rule.
                }
                return;
            }
            if (sc.CLD || sc.Glide || sc.ClimbLesson)
            {
                string leg = sc.CLD ? (sc.Leg == 0 ? "CLIMB at Vy, full power" : sc.Leg == 1 ? "LEVEL, 75 % power" : sc.Leg == 2 ? "DESCEND at cruise speed, 50 % power" : "done")
                           : sc.Glide ? $"GLIDE — speed to fly {sc.SpeedToFlyMs * 1.944:F0} kt  (best L/D {sc.BestLdMs * 1.944:F0}, min sink {sc.MinSinkMs * 1.944:F0})"
                           : sc.Kind == PracticeKind.ClimbVyRear ? $"CLIMB at Vy {sc.VyMs * 1.944:F0} kt — best RATE" : $"CLIMB at Vx {sc.VxMs * 1.944:F0} kt — best ANGLE";
                UiLayout.Label(new Rect(view.x, view.y + lh * 0.9f, view.width, lh), leg, _big);   // the task (its target); no live numbers
                UiLayout.Label(new Rect(view.x, y, view.width, lh * 0.8f), $"YOU FLY {sc.UserAxesText.ToUpperInvariant()}   ·   the game has the rest", _small);
                return;
            }
            if (sc.STurn)
            {
                // The task only: which way to roll next. (No bank / rate / speed numbers live — owner; they are in the debrief.)
                string cue = sc.Phase != PracticePhase.Live ? "" : sc.TargetBankSign > 0 ? "ROLL RIGHT to 45°" : "ROLL LEFT to 45°";
                UiLayout.Label(new Rect(view.x, view.y + lh * 0.9f, view.width, lh), cue, _big);
                UiLayout.Label(new Rect(view.x, y, view.width, lh * 0.8f), "AILERON + RUDDER: 45° to 45°, at 45°/s or full aileron — reverse the moment you get there", _small);
                if (sc.Stalled) UiLayout.Label(new Rect(view.x, view.y + lh * 1.9f, view.width, lh), "STALLED — the slow end of the cycle at 45° is past the stall in the turn", new GUIStyle(_big) { normal = { textColor = warn } });
            }
            else
            {
                var banner = new GUIStyle(_title) { normal = { textColor = sc.Stalled ? warn : good } };
                UiLayout.Label(new Rect(view.x, view.y + lh * 0.9f, view.width, lh * 1.2f), sc.Stalled ? "STALLED" : "FLYING", banner);
                // (AoA / speed / sink readout moved to the debrief.)
                string goal = sc.Kind == PracticeKind.StallSideView ? "ELEVATOR: hold the nose up until it stalls — watch the tail's force bring it down"
                    : sc.Kind == PracticeKind.StallRudder ? "RUDDER: pick up the dropped wing — the game stalls it and breaks it, ≤10 % aileron"
                    : "ELEVATOR: stall it, then break the angle of attack — the game holds it with ≤10 % rudder and aileron";
                UiLayout.Label(new Rect(view.x, y, view.width, lh * 0.8f), goal, _small);
                if (sc.Kind == PracticeKind.StallSideView)
                    UiLayout.Label(new Rect(view.x, y + lh * 0.8f, view.width, lh * 0.8f), "green = wing lift · yellow = tail force (×3) · white = relative wind at the tail · grey = weight", _small);
            }
            if (sc.Endless && sc.Phase == PracticePhase.Live)
            {
                float bw2 = _fs * 5f;
                    // END lives in the shared toolbar (ViewPanel) — no-overlap rule.
            }
        }

        private void DrawWeightOnWheels(Rect view, float y, float lh)
        {
            var ac = Driver.Sim.Aircraft;
            var samples = ac.LastForces;
            if (samples == null) return;
            float weight = (float)(ac.MassProperties.MassKg * 9.81);
            float left = 0, right = 0, tail = 0, brake = 0;
            foreach (var f in samples)
            {
                if (f.Kind != "gear") continue;
                float up = -(float)f.ForceBody.Z;   // body z is down
                float aft = -(float)f.ForceBody.X;
                if (f.PosBody.X < -2.0) tail += up; else if (f.PosBody.Y < -0.1) left += up; else if (f.PosBody.Y > 0.1) right += up; else tail += up;
                brake += Mathf.Max(0f, aft);
            }
            if (left + right + tail < 1f) return;
            string s = $"WEIGHT ON WHEELS   L {left / weight * 100:F0} %   R {right / weight * 100:F0} %   {(tail > 0.5f ? $"TAIL/NOSE {tail / weight * 100:F0} %" : "")}   {(brake > 5f ? $"BRAKING {brake / weight * 100:F0} % of weight" : "")}";
            UiLayout.Label(new Rect(view.x, y, view.width, lh * 0.8f), s, _small);
        }

        // ---- the debrief (owner 2026-10-03): charts, diagrams, screenshots, comments, replay with commentary ----------------
        private Vector2 _scroll; private Vector3 _lastPointer; private bool _dragging;

        private void DrawResult(PracticeScenario sc, Rect view, float lh)
        {
            if (Debrief != null && Debrief.Replay != null && Debrief.Replay.Active) return;   // the replay is showing
            LessonJudge j = sc.Judge;
            float w = Mathf.Min(view.width * 0.96f, _fs * 44f);
            float top = Mathf.Max(view.y, UiLayout.ToolbarBottom) + _fs * 0.4f;   // below the toolbar (no-overlap rule)
            var card = new Rect(view.x + (view.width - w) * 0.5f, top, w, view.yMax - top - _fs * 0.4f);
            GUI.DrawTexture(card, _card);
            float pad = _fs * 0.7f, cw = card.width - 2 * pad;

            // Fixed header: title, points, the four-colour bar, the buttons.
            float x = card.x + pad, y = card.y + pad * 0.6f;
            UiLayout.Label(new Rect(x, y, cw, lh * 1.1f), $"DEBRIEF — {sc.Title}", _title); y += lh * 1.15f;
            UiLayout.Label(new Rect(x, y, cw, lh * 1.2f), $"{j.Points:F0} points   ·   {sc.Verdict}", _big); y += lh * 1.25f;
            double tot = System.Math.Max(0.01, j.Seconds[0] + j.Seconds[1] + j.Seconds[2] + j.Seconds[3]);
            float bx = x;
            for (int g = 0; g < 4; g++)
            {
                float bw = (float)(cw * j.Seconds[g] / tot);
                GUI.color = GradeColours[g]; GUI.DrawTexture(new Rect(bx, y, bw, lh * 0.45f), _line); bx += bw;
            }
            GUI.color = Color.white; y += lh * 0.5f;
            UiLayout.Label(new Rect(x, y, cw, lh * 0.75f), $"green {j.Seconds[0] / tot * 100:F0} %  ·  yellow {j.Seconds[1] / tot * 100:F0} %  ·  orange {j.Seconds[2] / tot * 100:F0} %  ·  red {j.Seconds[3] / tot * 100:F0} %   of {tot:F0} s", _small); y += lh * 0.8f;
            float btw = (cw - 2 * pad) / 3f;
            if (GUI.Button(new Rect(x, y, btw, lh * 1.05f), "▶ REPLAY + COMMENTARY", _btn)) Debrief?.WatchReplay();
            if (GUI.Button(new Rect(x + btw + pad, y, btw, lh * 1.05f), "AGAIN", _btn)) Controller.Restart();
            if (GUI.Button(new Rect(x + 2 * (btw + pad), y, btw, lh * 1.05f), "MENU", _btn)) { Controller.End(); Menu?.Open(); }
            y += lh * 1.25f;

            // Scrolling body.
            var body = new Rect(card.x, y, card.width, card.yMax - y - pad * 0.4f);
            float chartH = Mathf.Max(lh * 4.5f, body.height * 0.34f);
            var rules = j.Rules;
            int nEvents = j.Events.Count;
            float shotH = cw * 0.5f * 0.56f;
            int shotRows = Debrief != null ? (Debrief.Shots.Count + 1) / 2 : 0;
            float content = rules.Live.Count * (chartH + lh * 1.2f) + (chartH + lh * 1.2f) + shotRows * (shotH + lh * 1.1f) + lh * 1.2f + nEvents * lh * 2.2f + lh;
            ScrollInput(body, content);
            GUI.BeginGroup(body);
            float cy = -_scroll.y, lx = pad;

            // Charts: each judged quantity against its bands, with the moments marked.
            for (int k = 0; k < rules.Live.Count; k++)
            {
                Criterion c = rules.Live[k];
                UiLayout.Label(new Rect(lx, cy, cw, lh), $"{c.Name} — ideal {c.Ideal}   (error in {c.Unit})", new GUIStyle(_small) { alignment = TextAnchor.MiddleLeft }); cy += lh;
                Chart(new Rect(lx, cy, cw, chartH), j, k, c); cy += chartH + lh * 0.2f;
            }
            // Path diagram.
            UiLayout.Label(new Rect(lx, cy, cw, lh), sc.Descending ? (sc.Kind is PracticeKind.LandingAileron or PracticeKind.CrosswindAileron or PracticeKind.CrosswindRudder or PracticeKind.LandingRudder ? "Your track down the runway (top view) — the centreline and its bands" : "Your path in side view — the ideal round-out and flare in blue") : "Your airspeed and height through the run", new GUIStyle(_small) { alignment = TextAnchor.MiddleLeft }); cy += lh;
            PathDiagram(new Rect(lx, cy, cw, chartH), sc, j); cy += chartH + lh * 0.2f;
            // Screenshots.
            if (Debrief != null && Debrief.Shots.Count > 0)
            {
                for (int i = 0; i < Debrief.Shots.Count; i++)
                {
                    var sh = Debrief.Shots[i];
                    float sx = lx + (i % 2) * (cw * 0.5f + 2f), sy = cy + (i / 2) * (shotH + lh * 1.1f);
                    var r = new Rect(sx, sy, cw * 0.5f - 4f, shotH);
                    if (sh.Tex != null) GUI.DrawTexture(r, sh.Tex, ScaleMode.ScaleToFit);
                    Orb(new Rect(sx, sy + shotH + lh * 0.15f, lh * 0.6f, lh * 0.6f), sh.Grade);
                    UiLayout.Label(new Rect(sx + lh * 0.7f, sy + shotH, r.width - lh * 0.7f, lh * 0.9f), $"{sh.LessonT:F0} s — {sh.Caption}", new GUIStyle(_small) { alignment = TextAnchor.MiddleLeft });
                }
                cy += shotRows * (shotH + lh * 1.1f);
            }
            // The instructor's comments, in order, each with a jump into the replay.
            UiLayout.Label(new Rect(lx, cy, cw, lh), "INSTRUCTOR'S DEBRIEF", _title); cy += lh * 1.2f;
            var wrap = new GUIStyle(_text) { fontSize = Mathf.RoundToInt(_fs * 0.85f) };
            for (int i = 0; i < nEvents; i++)
            {
                var e = j.Events[i];
                Orb(new Rect(lx, cy + lh * 0.2f, lh * 0.7f, lh * 0.7f), e.grade);
                GUI.Label(new Rect(lx + lh, cy, cw - lh * 4.2f, lh * 2.1f), $"{e.t:F0} s   {e.comment}", wrap);
                if (GUI.Button(new Rect(lx + cw - lh * 3f, cy + lh * 0.2f, lh * 3f, lh * 0.9f), "▶ watch", _btn)) Debrief?.WatchEvent(i);
                cy += lh * 2.2f;
            }
            GUI.EndGroup();
        }

        /// <summary>Drag / touch / wheel scrolling for the debrief body.</summary>
        private void ScrollInput(Rect body, float content)
        {
            float max = Mathf.Max(0f, content - body.height);
            Event ev = Event.current;
            if (ev.type == EventType.ScrollWheel && body.Contains(ev.mousePosition)) { _scroll.y += ev.delta.y * _fs; ev.Use(); }
            if (ev.type == EventType.MouseDown && body.Contains(ev.mousePosition)) { _dragging = true; _lastPointer = ev.mousePosition; }
            if (ev.type == EventType.MouseDrag && _dragging) { _scroll.y -= ev.mousePosition.y - _lastPointer.y; _lastPointer = ev.mousePosition; ev.Use(); }
            if (ev.type == EventType.MouseUp) _dragging = false;
            if (Input.touchCount == 1 && Input.GetTouch(0).phase == TouchPhase.Moved) _scroll.y += Input.GetTouch(0).deltaPosition.y * 0.5f;
            _scroll.y = Mathf.Clamp(_scroll.y, 0f, max);
        }

        /// <summary>Error vs time on the criterion's colour bands (green at the bottom), moments as coloured ticks.</summary>
        private void Chart(Rect r, LessonJudge j, int k, Criterion c)
        {
            var tl = j.Timeline;
            double t0 = tl.Count > 0 ? tl[0].T : 0, t1 = tl.Count > 1 ? tl[^1].T : t0 + 1;
            double top = c.Orange * 1.6;
            foreach (var s in tl) if (k < s.Err.Length) top = System.Math.Max(top, System.Math.Min(System.Math.Abs(s.Err[k]), c.Orange * 4));
            float Y(double e) => r.yMax - (float)(System.Math.Min(System.Math.Abs(e), top) / top) * r.height;
            float X(double t) => r.x + (float)((t - t0) / System.Math.Max(0.01, t1 - t0)) * r.width;
            // Bands.
            double[] lim = { 0, c.Green, c.Yellow, c.Orange, top };
            for (int g = 0; g < 4; g++)
            {
                float ya = Y(lim[g + 1]), yb = Y(lim[g]);
                GUI.color = new Color(GradeColours[g].r, GradeColours[g].g, GradeColours[g].b, 0.22f);
                GUI.DrawTexture(new Rect(r.x, ya, r.width, yb - ya), _line);
            }
            // The line (stepped: every sample joined to the next by a vertical run).
            GUI.color = Color.white;
            float px = float.NaN, py = 0;
            foreach (var s in tl)
            {
                if (k >= s.Err.Length) continue;
                float xx = X(s.T), yy = Y(s.Err[k]);
                if (!float.IsNaN(px)) { GUI.DrawTexture(new Rect(px, Mathf.Min(py, yy) - 1f, Mathf.Max(2f, xx - px), 2f), _line); GUI.DrawTexture(new Rect(xx - 1f, Mathf.Min(py, yy), 2f, Mathf.Abs(yy - py) + 1f), _line); }
                px = xx; py = yy;
            }
            // Moments.
            foreach (var e in j.Events)
            {
                if (e.t < t0 || e.t > t1 + 0.5) continue;
                GUI.color = GradeColours[(int)e.grade]; GUI.DrawTexture(new Rect(X(e.t) - 1.5f, r.y, 3f, r.height), _line);
            }
            GUI.color = Color.white;
            var st = new GUIStyle(_small) { alignment = TextAnchor.UpperLeft, fontSize = Mathf.RoundToInt(_fs * 0.7f) };
            GUI.Label(new Rect(r.x + 4f, r.y + 2f, r.width * 0.5f, _fs), $"{top:0.#} {c.Unit}", st);
            GUI.Label(new Rect(r.x + 4f, r.yMax - _fs * 1.1f, r.width * 0.5f, _fs), $"0 · {t0:F0} s", st);
            GUI.Label(new Rect(r.xMax - _fs * 5f, r.yMax - _fs * 1.1f, _fs * 5f, _fs), $"{t1:F0} s", st);
        }

        /// <summary>Landings: side profile (height vs distance) against the ideal; crosswind runs: the track (top view) on
        /// the centreline bands; airwork: airspeed and height through the run.</summary>
        private void PathDiagram(Rect r, PracticeScenario sc, LessonJudge j)
        {
            var tl = j.Timeline;
            GUI.color = new Color(1f, 1f, 1f, 0.05f); GUI.DrawTexture(r, _line); GUI.color = Color.white;
            if (tl.Count < 2) return;
            bool topView = sc.Kind is PracticeKind.LandingAileron or PracticeKind.CrosswindAileron or PracticeKind.CrosswindRudder or PracticeKind.LandingRudder;
            if (sc.Descending && !topView)
            {
                double a0 = tl[0].Along, a1 = tl[^1].Along, hmax = 1;
                foreach (var s in tl) hmax = System.Math.Max(hmax, s.Height);
                float X(double a) => r.x + (float)((a - a0) / System.Math.Max(1, a1 - a0)) * r.width;
                float Y(double h) => r.yMax - 6f - (float)(System.Math.Max(0, h) / (hmax * 1.1)) * (r.height - 12f);
                GUI.color = new Color(0.45f, 0.45f, 0.5f); GUI.DrawTexture(new Rect(r.x, Y(0), r.width, 3f), _line);
                double tdz = (sc.FlareExercise ? PracticeScenario.NumbersPastThresholdM : PracticeScenario.AimPastThresholdM) + PracticeScenario.TouchdownBeyondAimM;
                GUI.color = new Color(0.25f, 0.92f, 0.35f, 0.5f); GUI.DrawTexture(new Rect(X(tdz - 30), Y(0) - 4f, X(tdz + 61) - X(tdz - 30), 8f), _line);
                // Ideal: the glide down to 15 ft, then the height shrinking ~exponentially to the touchdown zone.
                GUI.color = new Color(0.5f, 0.85f, 1f, 0.9f);
                double gl = sc.Approach ? sc.GlideslopeRad : sc.FlareGlideRad, roH = 4.6;
                double roA = tdz - 120;                                           // round-out ~120 m before the touchdown point
                for (double a = a0; a < System.Math.Min(a1, tdz + 20); a += System.Math.Max(1, (a1 - a0) / 200))
                {
                    double h = a < roA ? roH + (roA - a) * System.Math.Tan(gl) : roH * System.Math.Exp(-(a - roA) / 40.0);
                    GUI.DrawTexture(new Rect(X(a) - 1f, Y(h) - 1f, 2f, 2f), _line);
                }
                // Actual, coloured by the grade at the time.
                foreach (var s in tl) { GUI.color = GradeColours[(int)s.G]; GUI.DrawTexture(new Rect(X(s.Along) - 1.5f, Y(s.Height) - 1.5f, 3f, 3f), _line); }
                GUI.color = Color.white;
                return;
            }
            if (topView)
            {
                double a0 = tl[0].Along, a1 = tl[^1].Along, span = 40 / 3.281;   // ±40 ft
                float X(double a) => r.x + (float)((a - a0) / System.Math.Max(1, a1 - a0)) * r.width;
                float Y(double c) => r.center.y - (float)(System.Math.Clamp(c, -span, span) / span) * r.height * 0.48f;
                double[] lim = { 5 / 3.281, 10 / 3.281, 20 / 3.281, span };
                for (int g = 3; g >= 0; g--) { GUI.color = new Color(GradeColours[g].r, GradeColours[g].g, GradeColours[g].b, 0.2f); GUI.DrawTexture(new Rect(r.x, Y(lim[g]), r.width, Y(-lim[g]) - Y(lim[g])), _line); }
                GUI.color = Color.white; GUI.DrawTexture(new Rect(r.x, r.center.y - 1f, r.width, 2f), _line);
                foreach (var s in tl) { GUI.color = GradeColours[(int)s.G]; GUI.DrawTexture(new Rect(X(s.Along) - 1.5f, Y(s.Cross) - 1.5f, 3f, 3f), _line); }
                GUI.color = Color.white;
                return;
            }
            {
                double t0 = tl[0].T, t1 = tl[^1].T, vmin = double.MaxValue, vmax = 0, hmin = double.MaxValue, hmax = double.MinValue;
                foreach (var s in tl) { vmin = System.Math.Min(vmin, s.Ias); vmax = System.Math.Max(vmax, s.Ias); hmin = System.Math.Min(hmin, s.Height); hmax = System.Math.Max(hmax, s.Height); }
                float X(double t) => r.x + (float)((t - t0) / System.Math.Max(0.1, t1 - t0)) * r.width;
                float Yv(double v) => r.yMax - 4f - (float)((v - vmin) / System.Math.Max(1, vmax - vmin)) * (r.height - 8f);
                float Yh(double h) => r.yMax - 4f - (float)((h - hmin) / System.Math.Max(1, hmax - hmin)) * (r.height - 8f);
                if (sc.TargetSpeedMs > 0) { GUI.color = new Color(0.5f, 0.85f, 1f, 0.6f); GUI.DrawTexture(new Rect(r.x, Yv(sc.TargetSpeedMs) - 1f, r.width, 2f), _line); }
                foreach (var s in tl)
                {
                    GUI.color = GradeColours[(int)s.G]; GUI.DrawTexture(new Rect(X(s.T) - 1.5f, Yv(s.Ias) - 1.5f, 3f, 3f), _line);
                    GUI.color = new Color(1f, 1f, 1f, 0.6f); GUI.DrawTexture(new Rect(X(s.T) - 1f, Yh(s.Height) - 1f, 2f, 2f), _line);
                }
                GUI.color = Color.white;
                var st = new GUIStyle(_small) { alignment = TextAnchor.UpperLeft, fontSize = Mathf.RoundToInt(_fs * 0.7f) };
                GUI.Label(new Rect(r.x + 4f, r.y + 2f, r.width, _fs), $"airspeed (coloured dots) {vmin * 1.944:F0}–{vmax * 1.944:F0} kt · height (white) {hmin * 3.281:F0}–{hmax * 3.281:F0} ft · target speed in blue", st);
            }
        }
    }
}
