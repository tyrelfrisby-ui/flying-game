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

        private GUIStyle _title, _text, _big, _small, _btn;
        private Texture2D _card, _bar, _line, _btnBg;
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
        }

        private void OnGUI()
        {
            if (Controller == null || !Controller.Active || Controller.Scenario == null || SessionSettings.MenuOpen) return;
            Init();
            PracticeScenario sc = Controller.Scenario;
            Rect view = ScreenLayout.Portrait ? new Rect(0f, 0f, Screen.width, Screen.height - ScreenLayout.TrayHeightPx) : new Rect(0f, 0f, Screen.width, Screen.height);
            float lh = _fs * 1.5f;

            if (sc.Phase == PracticePhase.Briefing)
            {
                if (sc.LessonPages.Length > 0 && Controller.LessonPage < sc.LessonPages.Length) DrawLessonPage(sc, view, lh);
                else DrawBriefing(sc, view, lh);
                return;
            }
            DrawLive(sc, view, lh);
            if (sc.Phase == PracticePhase.Finished) DrawResult(sc, view, lh);
        }

        /// <summary>An illustrated page: the picture rendered from the game, the written explanation, NEXT.</summary>
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

        private void DrawBriefing(PracticeScenario sc, Rect view, float lh)
        {
            float w = Mathf.Min(view.width * 0.9f, _fs * 26f), h = lh * 9.5f;
            var card = new Rect(view.x + (view.width - w) * 0.5f, view.y + view.height * 0.16f, w, h);
            GUI.DrawTexture(card, _card);
            float x = card.x + _fs, y = card.y + _fs * 0.6f, cw = card.width - 2 * _fs;
            GUI.Label(new Rect(x, y, cw, lh * 1.2f), sc.Title, _title); y += lh * 1.3f;
            GUI.Label(new Rect(x, y, cw, lh * 5.6f), sc.Instructions, _text); y += lh * 5.7f;
            GUI.Label(new Rect(x, y, cw, lh), $"The game is flying it.  {sc.HandoverLine.Replace("You have", "You get")} in {sc.BriefingLeft:F0} s", _small); y += lh;
            if (GUI.Button(new Rect(card.center.x - _fs * 4f, y, _fs * 8f, lh * 1.1f), "GO NOW", _btn)) Controller.Scenario.SkipBriefing();
        }

        private void DrawLive(PracticeScenario sc, Rect view, float lh)
        {
            if (sc.Airwork) { DrawAirwork(sc, view, lh); return; }
            // Alignment display: a runway bar across the top of the view with the fuselage line drawn over it, rotated by the
            // alignment error (exaggerated ×2 so a couple of degrees reads clearly). Parallel = the goal.
            float bw = Mathf.Min(view.width * 0.42f, _fs * 16f), bh = _fs * 0.7f;
            var bar = new Rect(view.x + (view.width - bw) * 0.5f, view.y + view.height * 0.115f, bw, bh);
            bool alignGood = Mathf.Abs((float)sc.AlignmentDeg) <= 3f, centreGood = Mathf.Abs((float)sc.OffCentreM) <= 3f;
            GUI.color = new Color(0.32f, 0.32f, 0.35f, 0.95f); GUI.DrawTexture(bar, _line);
            GUI.color = Color.white; GUI.DrawTexture(new Rect(bar.x, bar.center.y - 1f, bar.width, 2f), _line);   // centreline
            // Fuselage line: pivot at the bar centre, shifted sideways by the off-centre distance (1 m = 3 % of the bar).
            float shift = Mathf.Clamp((float)sc.OffCentreM, -12f, 12f) * bar.width * 0.03f;
            Vector2 pivot = new Vector2(bar.center.x + shift, bar.center.y);
            Matrix4x4 m = GUI.matrix;
            GUIUtility.RotateAroundPivot(-(float)sc.AlignmentDeg * 2f, pivot);
            GUI.color = alignGood ? new Color(0.45f, 1f, 0.5f) : new Color(1f, 0.6f, 0.3f);
            GUI.DrawTexture(new Rect(pivot.x - bw * 0.22f, pivot.y - bh * 0.28f, bw * 0.44f, bh * 0.56f), _line);
            GUI.DrawTexture(new Rect(pivot.x + bw * 0.22f - bh * 0.6f, pivot.y - bh * 0.6f, bh * 0.6f, bh * 1.2f), _line);   // nose
            GUI.matrix = m; GUI.color = Color.white;

            float y = bar.yMax + _fs * 0.3f;
            string align = alignGood ? "PARALLEL" : sc.AlignmentDeg > 0 ? $"NOSE RIGHT {sc.AlignmentDeg:F0}°" : $"NOSE LEFT {-sc.AlignmentDeg:F0}°";
            string centre = centreGood ? "ON CENTRE" : sc.OffCentreM > 0 ? $"{sc.OffCentreM:F0} m RIGHT" : $"{-sc.OffCentreM:F0} m LEFT";
            var a = new GUIStyle(_big) { normal = { textColor = alignGood ? new Color(0.45f, 1f, 0.5f) : new Color(1f, 0.6f, 0.3f) } };
            var c = new GUIStyle(_small) { normal = { textColor = centreGood ? new Color(0.45f, 1f, 0.5f) : new Color(1f, 0.6f, 0.3f) } };
            bool primaryAlign = sc.UserRudder || sc.FlareExercise || sc.Approach;
            GUI.Label(new Rect(view.x, y, view.width, lh), primaryAlign ? align : centre, a); y += lh * 0.95f;
            GUI.Label(new Rect(view.x, y, view.width, lh * 0.8f), primaryAlign ? centre : align, c); y += lh * 0.8f;

            if (sc.Approach)
            {
                string dev = Mathf.Abs((float)sc.GlideslopeDeviationDeg) < 0.3f ? "ON GLIDESLOPE" : sc.GlideslopeDeviationM > 0 ? $"HIGH {sc.GlideslopeDeviationM:F0} m" : $"LOW {-sc.GlideslopeDeviationM:F0} m";
                float vT = (float)(1.3 * sc.VsoMs), vNow = (float)sc.AirspeedMs;
                string spd = $"{vNow * 1.944f:F0} kt   (1.3 Vso = {vT * 1.944f:F0})";
                var g = new GUIStyle(_small) { normal = { textColor = Mathf.Abs((float)sc.GlideslopeDeviationDeg) < 0.3f ? new Color(0.45f, 1f, 0.5f) : new Color(1f, 0.8f, 0.3f) } };
                GUI.Label(new Rect(view.x, y, view.width, lh * 0.8f), dev + "   ·   " + spd, g); y += lh * 0.8f;
            }
            if (sc.SideView && Driver?.Sim != null) DrawWeightOnWheels(view, y, lh);

            if (sc.Phase == PracticePhase.Live)
            {
                string goal = sc.UserRudder ? "RUDDER: keep the fuselage parallel to the runway" : sc.UserAileron ? "AILERON: stay over the centreline" : sc.Approach ? "PITCH for the path, POWER for the speed" : "ELEVATOR: round out, hold it off";
                GUI.Label(new Rect(view.x, view.y + view.height * 0.06f, view.width, lh * 0.8f), goal + $"   ·   in band {sc.InBandFraction * 100:F0} %", _small);
            }
        }

        private void DrawAirwork(PracticeScenario sc, Rect view, float lh)
        {
            float y = view.y + view.height * 0.06f;
            var good = new Color(0.45f, 1f, 0.5f); var warn = new Color(1f, 0.6f, 0.3f);
            if (sc.Straight)
            {
                float bw = Mathf.Min(view.width * 0.42f, _fs * 16f), bh = _fs * 0.5f;
                var bar = new Rect(view.x + (view.width - bw) * 0.5f, view.y + view.height * 0.115f, bw, bh);
                Matrix4x4 m = GUI.matrix;
                GUIUtility.RotateAroundPivot(-(float)sc.BankDeg, bar.center);
                GUI.color = Mathf.Abs((float)sc.BankDeg) < 3f ? good : warn;
                GUI.DrawTexture(bar, _line);
                GUI.matrix = m; GUI.color = Color.white;
                string slip = Mathf.Abs((float)sc.BetaDeg) < 1f ? "no slip" : sc.BetaDeg > 0 ? $"slipping RIGHT {sc.BetaDeg:F0}°" : $"slipping LEFT {-sc.BetaDeg:F0}°";
                string drift = Mathf.Abs((float)sc.HeadingDriftDeg) < 2f ? "heading held" : sc.HeadingDriftDeg > 0 ? $"nose swung RIGHT {sc.HeadingDriftDeg:F0}°" : $"nose swung LEFT {-sc.HeadingDriftDeg:F0}°";
                var a = new GUIStyle(_big) { normal = { textColor = Mathf.Abs((float)sc.BankDeg) < 3f ? good : warn } };
                GUI.Label(new Rect(view.x, bar.yMax + _fs * 0.3f, view.width, lh), $"bank {sc.BankDeg:F0}°   ·   {slip}   ·   {drift}", a);
                GUI.Label(new Rect(view.x, y, view.width, lh * 0.8f), $"AILERON: keep the wings level (rudder is neutral)   ·   level {sc.InBandFraction * 100:F0} % of the time", _small);
                if (sc.Endless && sc.Phase == PracticePhase.Live)
                {
                    float bw2 = _fs * 5f;
                    if (GUI.Button(new Rect(view.xMax - bw2 - _fs, view.y + view.height * 0.16f, bw2, lh * 1.05f), "END", _btn)) { Controller.End(); Menu?.Open(); }
                }
                return;
            }
            if (sc.CLD || sc.Glide || sc.ClimbLesson)
            {
                string leg = sc.CLD ? (sc.Leg == 0 ? "CLIMB at Vy, full power" : sc.Leg == 1 ? "LEVEL, 75 % power" : sc.Leg == 2 ? "DESCEND at cruise speed, 50 % power" : "done")
                           : sc.Glide ? $"GLIDE — speed to fly {sc.SpeedToFlyMs * 1.944:F0} kt  (best L/D {sc.BestLdMs * 1.944:F0}, min sink {sc.MinSinkMs * 1.944:F0})"
                           : sc.Kind == PracticeKind.ClimbVyRear ? $"CLIMB at Vy {sc.VyMs * 1.944:F0} kt — best RATE" : $"CLIMB at Vx {sc.VxMs * 1.944:F0} kt — best ANGLE";
                bool onSpeed = Mathf.Abs((float)(sc.AirspeedMs - sc.TargetSpeedMs)) < 0.06f * (float)sc.TargetSpeedMs;
                var a = new GUIStyle(_big) { normal = { textColor = onSpeed ? good : warn } };
                GUI.Label(new Rect(view.x, view.y + view.height * 0.11f, view.width, lh), leg, _big);
                GUI.Label(new Rect(view.x, view.y + view.height * 0.11f + lh, view.width, lh), $"{sc.AirspeedMs * 1.944:F0} kt → target {sc.TargetSpeedMs * 1.944:F0}   ·   {(-sc.SinkMs * 196.85):+0;-0} fpm   ·   {(sc.AglM - sc.StartAglM) * 3.281:+0;-0} ft from start", a);
                string extra = sc.Glide ? $"over the ground {sc.AchievedRatio:F1}:1 (optimum {sc.SpeedToFlyGroundRatio:F1}:1)" : sc.ClimbLesson ? (sc.Kind == PracticeKind.ClimbVyRear ? $"{sc.ClimbTimeSec:F0} s" : $"{sc.ClimbDistanceM:F0} m of ground") : $"on target {sc.InBandFraction * 100:F0} % of the time";
                GUI.Label(new Rect(view.x, view.y + view.height * 0.11f + lh * 2f, view.width, lh * 0.8f), extra, _small);
                GUI.Label(new Rect(view.x, y, view.width, lh * 0.8f), $"YOU FLY {sc.UserAxesText.ToUpperInvariant()}   ·   the game has the rest", _small);
                return;
            }
            if (sc.STurn)
            {
                // Bank: a horizon bar tilted to the bank with the 45° targets marked; the cue says which way to roll next.
                float bw = Mathf.Min(view.width * 0.42f, _fs * 16f), bh = _fs * 0.5f;
                var bar = new Rect(view.x + (view.width - bw) * 0.5f, view.y + view.height * 0.115f, bw, bh);
                Matrix4x4 m = GUI.matrix;
                GUIUtility.RotateAroundPivot(-(float)sc.BankDeg, bar.center);
                GUI.color = Mathf.Abs((float)sc.BankDeg) >= 44f && Mathf.Abs((float)sc.BankDeg) <= 48f ? good : Color.white;
                GUI.DrawTexture(bar, _line);
                GUI.matrix = m; GUI.color = Color.white;
                string cue = sc.Phase != PracticePhase.Live ? "" : sc.TargetBankSign > 0 ? "ROLL RIGHT to 45°" : "ROLL LEFT to 45°";
                var a = new GUIStyle(_big) { normal = { textColor = Mathf.Abs((float)sc.RollRateDegS) >= 40f ? good : warn } };
                GUI.Label(new Rect(view.x, bar.yMax + _fs * 0.3f, view.width, lh), $"{cue}   bank {sc.BankDeg:F0}°   rate {Mathf.Abs((float)sc.RollRateDegS):F0}°/s", a);
                string spd = $"speed {sc.AirspeedMs * 1.944:F0} kt → target {sc.SpeedTargetMs * 1.944:F0} kt   ({(sc.CycleFraction < 0.5 ? "slowing to" : "back up toward")} {(sc.CycleFraction < 0.5 ? 1.15 * sc.VsoMs * 1.944 : sc.CruiseMs * 1.944):F0})   reversals {sc.Reversals}";
                GUI.Label(new Rect(view.x, bar.yMax + _fs * 0.3f + lh, view.width, lh * 0.8f), spd, _small);
                GUI.Label(new Rect(view.x, y, view.width, lh * 0.8f), "AILERON + RUDDER: 45° to 45°, at 45°/s or full aileron — reverse the moment you get there", _small);
                if (sc.Stalled) GUI.Label(new Rect(view.x, bar.yMax + _fs * 0.3f + lh * 1.8f, view.width, lh), "STALLED — the slow end of the cycle at 45° is past the stall in the turn", new GUIStyle(_big) { normal = { textColor = warn } });
            }
            else
            {
                var banner = new GUIStyle(_title) { normal = { textColor = sc.Stalled ? warn : good } };
                GUI.Label(new Rect(view.x, view.y + view.height * 0.11f, view.width, lh * 1.2f), sc.Stalled ? "STALLED" : "FLYING", banner);
                GUI.Label(new Rect(view.x, view.y + view.height * 0.11f + lh * 1.2f, view.width, lh * 0.8f),
                    $"AoA {sc.AlphaDeg:F0}°   {sc.AirspeedMs * 1.944:F0} kt   sink {sc.SinkMs * 196.85:F0} ft/min   bank {sc.BankDeg:F0}°   stalls {sc.StallCount}", _small);
                string goal = sc.Kind == PracticeKind.StallSideView ? "ELEVATOR: hold the nose up until it stalls — watch the tail's force bring it down"
                    : sc.Kind == PracticeKind.StallRudder ? "RUDDER: pick up the dropped wing — the game stalls it and breaks it, ≤10 % aileron"
                    : "ELEVATOR: stall it, then break the angle of attack — the game holds it with ≤10 % rudder and aileron";
                GUI.Label(new Rect(view.x, y, view.width, lh * 0.8f), goal, _small);
                if (sc.Kind == PracticeKind.StallSideView)
                    GUI.Label(new Rect(view.x, y + lh * 0.8f, view.width, lh * 0.8f), "green = wing lift · yellow = tail force (×3) · white = relative wind at the tail · grey = weight", _small);
            }
            if (sc.Endless && sc.Phase == PracticePhase.Live)
            {
                float bw2 = _fs * 5f;
                if (GUI.Button(new Rect(view.xMax - bw2 - _fs, view.y + view.height * 0.16f, bw2, lh * 1.05f), "END", _btn)) { Controller.End(); Menu?.Open(); }
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
            GUI.Label(new Rect(view.x, y, view.width, lh * 0.8f), s, _small);
        }

        private void DrawResult(PracticeScenario sc, Rect view, float lh)
        {
            float w = Mathf.Min(view.width * 0.85f, _fs * 22f), h = lh * 7f;
            var card = new Rect(view.x + (view.width - w) * 0.5f, view.y + view.height * 0.3f, w, h);
            GUI.DrawTexture(card, _card);
            float x = card.x + _fs, y = card.y + _fs * 0.6f, cw = card.width - 2 * _fs;
            GUI.Label(new Rect(x, y, cw, lh * 1.2f), sc.Title, _title); y += lh * 1.3f;
            GUI.Label(new Rect(x, y, cw, lh * 1.2f), $"{sc.Score:F0} %   {sc.Verdict}", _big); y += lh * 1.3f;
            string detail = sc.Glide ? $"{sc.AchievedRatio:F1}:1 over the ground against an optimum {sc.SpeedToFlyGroundRatio:F1}:1"
                : sc.ClimbLesson ? (sc.Kind == PracticeKind.ClimbVyRear ? $"{sc.ClimbTimeSec:F0} s for 1,000 ft (best possible {304.8 / Mathf.Max(0.1f, (float)sc.RocAtVyMs):F0} s)" : $"{sc.ClimbDistanceM:F0} m of ground for 1,000 ft")
                : sc.CLD ? $"on speed / on altitude {sc.InBandFraction * 100:F0} % of the run"
                : sc.STurn ? $"reversals {sc.Reversals} · score on rate and bank accuracy"
                : sc.Stall ? $"stalls {sc.StallCount} · max wing drop {sc.MaxWingDropDeg:F0}° · max sink {sc.MaxSinkMs * 196.85:F0} ft/min"
                : sc.Descending
                ? (sc.TouchedDown ? $"touchdown sink {sc.TouchdownSinkMs * 196.85:F0} ft/min · {Mathf.Abs((float)sc.TouchdownAlignDeg):F0}° off parallel · {Mathf.Abs((float)sc.TouchdownOffCentreM):F0} m off centre" : "no touchdown")
                : $"in band {sc.InBandFraction * 100:F0} % of the run · rms error {sc.RmsError:F1}";
            GUI.Label(new Rect(x, y, cw, lh), detail, _small); y += lh * 1.2f;
            float bw = _fs * 7f;
            if (GUI.Button(new Rect(card.center.x - bw - _fs * 0.5f, y, bw, lh * 1.1f), "AGAIN", _btn)) Controller.Restart();
            if (GUI.Button(new Rect(card.center.x + _fs * 0.5f, y, bw, lh * 1.1f), "MENU", _btn)) { Controller.End(); Menu?.Open(); }
        }
    }
}
