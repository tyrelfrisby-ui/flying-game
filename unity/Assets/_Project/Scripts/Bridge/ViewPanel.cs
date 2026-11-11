using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Camera / replay / clips controls (owner 2026-10-01).
    ///   In flight: a column under OPTIONS (top right) — VIEW (cycles the camera views), REPLAY, CLIP (the last
    ///   15 s – 3 min to Photos, length in OPTIONS), REC (everything until the next tap). Clips never show these buttons
    ///   (<see cref="ClipRecorder"/> films a separate camera). It steps sideways out of the aircraft's on-screen box.
    ///   In a replay: a bar in the control tray (portrait) or along the bottom (landscape) — scrub track, −10 s,
    ///   play/pause, +10 s, speed, VIEW, CLIP, REC, EXIT (back to the live flight where it was paused).
    /// </summary>
    public sealed class ViewPanel : MonoBehaviour
    {
        public ChaseCamera Chase;
        public FlightReplay Replay;
        public OptionsPanel Options;
        /// <summary>The debrief, opened by the DEBRIEF button on the replay bar (Ty 2026-11-10). Null = no debrief
        /// button, which is what a build without the panel gets.</summary>
        public DebriefPanel Debrief;
        private FlyingGame.Bridge.Practice.PracticeController _practice;
        private FlyingGame.Bridge.Net.NetSession _net;
        private StartMenu _menu;

        private GUIStyle _btn, _btnOn, _btnRec, _btnArm, _label, _small;
        private Texture2D _bg, _btnBg, _btnOnBg, _recBg, _armBg, _track, _fill, _knob;
        private int _fs;
        private bool _scrubbing;
        private Rect _talkRect;   // GUI coords (top-left origin); empty when hidden
        private string _toast;
        private float _toastUntil;

        private static Texture2D Solid(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }

        private void EnsureStyles()
        {
            int fs = Mathf.RoundToInt(UiLayout.Unit * 0.026f);
            if (_btn != null && fs == _fs) return;
            _fs = fs;
            _bg ??= Solid(new Color(0.05f, 0.08f, 0.12f, 0.92f));
            _btnBg ??= Solid(new Color(0.18f, 0.24f, 0.32f, 0.9f));
            _btnOnBg ??= Solid(new Color(0.2f, 0.62f, 0.35f, 0.95f));
            _recBg ??= Solid(new Color(0.8f, 0.12f, 0.12f, 0.95f));
            // VOX armed but not open yet: amber, so it is neither the dull "off" nor the red "live" (DrawVox).
            _armBg ??= Solid(new Color(0.85f, 0.55f, 0.08f, 0.95f));
            _track ??= Solid(new Color(0.3f, 0.34f, 0.4f, 1f));
            _fill ??= Solid(new Color(0.25f, 0.75f, 1f, 1f));
            _knob ??= Solid(Color.white);
            Font f = UiFont.Get();
            _btn = new GUIStyle { font = f, fontSize = fs, alignment = TextAnchor.MiddleCenter, wordWrap = true, normal = { textColor = Color.white, background = _btnBg }, active = { textColor = Color.white, background = _btnBg }, padding = new RectOffset(4, 4, 2, 2) };
            _btnOn = new GUIStyle(_btn) { fontStyle = FontStyle.Bold, normal = { textColor = Color.white, background = _btnOnBg }, active = { textColor = Color.white, background = _btnOnBg } };
            _btnRec = new GUIStyle(_btn) { fontStyle = FontStyle.Bold, normal = { textColor = Color.white, background = _recBg }, active = { textColor = Color.white, background = _recBg } };
            _btnArm = new GUIStyle(_btn) { fontStyle = FontStyle.Bold, normal = { textColor = Color.black, background = _armBg }, active = { textColor = Color.black, background = _armBg } };
            _label = new GUIStyle { font = f, fontSize = fs, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            _small = new GUIStyle(_label) { fontStyle = FontStyle.Normal, fontSize = Mathf.RoundToInt(fs * 0.85f), normal = { textColor = new Color(0.85f, 0.9f, 0.95f) } };
        }

        private void Update()
        {
            // TALK is push-to-talk: transmitting while a finger (or the mouse) is held on it.
            bool held = false;
            if (_talkRect.width > 0f && !SessionSettings.MenuOpen)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    Touch t = Input.GetTouch(i);
                    if (t.phase != TouchPhase.Ended && t.phase != TouchPhase.Canceled && _talkRect.Contains(new Vector2(t.position.x, Screen.height - t.position.y))) held = true;
                }
                if (Input.touchCount == 0 && Input.GetMouseButton(0) && _talkRect.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y))) held = true;
            }
            VoiceComms.PttHeld = held;

            string m = ClipRecorder.PollMessage();
            if (!string.IsNullOrEmpty(m)) { _toast = m; _toastUntil = Time.realtimeSinceStartup + 3.5f; Debug.Log("[Clips] " + m); }
        }

        private static string Clock(double sec)
        {
            sec = System.Math.Max(0, sec);
            int m = (int)(sec / 60); double s = sec - m * 60;
            return $"{m}:{s:00.0}";
        }

        private void OnGUI()
        {
            if (SessionSettings.MenuOpen || Chase == null) return;
            // A Quest never shows OnGUI (the VR menu replaces this toolbar), and building its labels on every Layout
            // and Repaint event was garbage for the headset's collector every frame of a flight.
            if (XrHeadset.Active) return;
            if (OpenViewMenuRequest) { _viewMenu = true; OpenViewMenuRequest = false; }
            EnsureStyles();
            _talkRect = default;
            // The debrief takes the whole screen, and the replay bar steps aside while it is up — the same rule the
            // live column already follows for OPTIONS and the Aero panel. Two IMGUI layers both answer the same tap,
            // so leaving the bar drawn under the debrief would let one press work a button on each. CLOSE on the
            // debrief brings the bar back.
            if (Replay != null && Replay.Active) { if (!DebriefPanel.IsOpen) DrawReplayBar(); }
            else if ((Options == null || !Options.IsOpen) && !Widget.GameAeroPanel.IsOpen) DrawLiveColumn();
            DrawToast();
            DrawGraphicsWarning();
        }

        /// <summary>
        /// THE GRAPHICS WARNING (Ty 2026-11-11): "a warning when graphics would compromise the flight model or frame
        /// rate". It names what was given up and why — "clouds drawn closer in ...: the simulator was not keeping up
        /// — the flight model lost 0.4 s of flying" — and it is DISMISSIBLE: OK clears it, and it clears itself after
        /// <see cref="GraphicsWarningSeconds"/> so it cannot sit over the instruments for the rest of the flight.
        /// It cannot nag: <see cref="FlyingGame.Core.AutoQuality"/> raises at most one, rate-limited, per downgrade.
        /// </summary>
        public const float GraphicsWarningSeconds = 12f;
        private string _gfxWarning;
        private float _gfxUntil;

        private void DrawGraphicsWarning()
        {
            var pending = GraphicsQualityDirector.Warning;
            if (pending.HasValue && pending.Value.Message != _gfxWarning)
            {
                _gfxWarning = pending.Value.Message;
                _gfxUntil = Time.realtimeSinceStartup + GraphicsWarningSeconds;
            }
            if (string.IsNullOrEmpty(_gfxWarning)) return;
            if (Time.realtimeSinceStartup > _gfxUntil) { _gfxWarning = null; GraphicsQualityDirector.DismissWarning(); return; }

            float s = Mathf.Min(Screen.width, Screen.height);
            float w = Mathf.Min(UiSurface.Width * 0.82f, s * 1.1f);
            float textW = w - _fs * 2f;
            float h = UiLayout.WrappedHeight(_small, _gfxWarning, textW) + _fs * 1.2f + UiLayout.ButtonH;
            Rect line = UiLayout.NextLine(h);
            var r = new Rect(line.center.x - w * 0.5f, line.y, w, h);
            GUI.DrawTexture(r, _bg);
            UiLayout.LabelWrapped(new Rect(r.x + _fs, r.y + _fs * 0.5f, textW, h - _fs * 1.2f - UiLayout.ButtonH), _gfxWarning, _small);
            var ok = new Rect(r.center.x - w * 0.18f, r.yMax - UiLayout.ButtonH - _fs * 0.3f, w * 0.36f, UiLayout.ButtonH);
            if (UiLayout.Button(ok, "OK", _btn)) { _gfxWarning = null; GraphicsQualityDirector.DismissWarning(); }
        }

        private void DrawToast()
        {
            if (string.IsNullOrEmpty(_toast) || Time.realtimeSinceStartup > _toastUntil) return;
            float s = Mathf.Min(Screen.width, Screen.height);
            Rect line = UiLayout.NextLine(_fs * 1.9f);
            var r = new Rect(line.center.x - Mathf.Min(line.width, s * 0.7f) * 0.5f, line.y, Mathf.Min(line.width, s * 0.7f), line.height);
            GUI.DrawTexture(r, _bg);
            UiLayout.Label(r, _toast, _label);
        }

        private void DrawLiveColumn()
        {
            // The shared toolbar row (owner rule 2026-10-03: no overlap): VIEW · REPLAY · CLIP · REC between MENU and
            // OPTIONS; a lesson's END takes a fifth slot. The current view's name shows on the VIEW button when it isn't the
            // default.
            if (FlyingGame.Core.LocalMp.SplitControls.CompactToolbar(LocalPlayerHud.CompactActive)) { _viewMenu = false; DrawFlightEntry(); return; }
            _flightOpen = false;
            _practice ??= Object.FindFirstObjectByType<FlyingGame.Bridge.Practice.PracticeController>();
            bool lesson = _practice != null && _practice.Active;
            int n = lesson ? 8 : 7;        // + CHART
            bool other = Chase.CurrentView != ChaseCamera.View.RelativeWind;
            if (UiLayout.Button(UiLayout.ToolbarSlot(0, n), other ? "VIEW: " + ChaseCamera.ViewNames[(int)Chase.CurrentView] : "VIEW", other || _viewMenu ? _btnOn : _btn)) _viewMenu = !_viewMenu;
            if (_viewMenu) DrawViewMenu(UiLayout.ToolbarBottom, Screen.height);
            DrawLookPad();
            if (Replay != null && UiLayout.Button(UiLayout.ToolbarSlot(1, n), "REPLAY", _btn)) Replay.Enter();
            if (UiLayout.Button(UiLayout.ToolbarSlot(2, n), ClipRecorder.Busy ? "SAVING..." : "CLIP " + ClipRecorder.Length(ClipRecorder.ClipSeconds), _btn)) ClipRecorder.SaveClip();
            bool rec = ClipRecorder.Recording;
            if (UiLayout.Button(UiLayout.ToolbarSlot(3, n), rec ? "STOP REC" : "REC", rec ? _btnRec : _btn)) ClipRecorder.ToggleRecording();
            // AERO (owner 2026-10-09, parity with the Aero Widget): forces, insets, autopilot, weight & balance.
            if (UiLayout.Button(UiLayout.ToolbarSlot(4, n), "AERO", _btn)) { Widget.GameAeroPanel.Toggle(); Options?.Close(); }
            // SLOW MOTION (owner 2026-10-11): 100 → 50 → 25 → 10 %; the controls stay real time. Not in multiplayer.
            _net ??= Object.FindFirstObjectByType<FlyingGame.Bridge.Net.NetSession>();
            bool mp = _net != null && _net.Active;
            if (mp && SessionSettings.SimSpeed < 1f) { SessionSettings.SimSpeed = 1f; if (Time.timeScale > 0f) Time.timeScale = 1f; }
            if (UiLayout.Button(UiLayout.ToolbarSlot(5, n), mp ? "TIME 100%" : SessionSettings.SimSpeedLabel, SessionSettings.SimSpeed < 1f ? _btnOn : _btn) && !mp)
            {
                int i = System.Array.IndexOf(SessionSettings.SimSpeeds, SessionSettings.SimSpeed);
                SessionSettings.SimSpeed = SessionSettings.SimSpeeds[(i + 1) % SessionSettings.SimSpeeds.Length];
                if (Time.timeScale > 0f) Time.timeScale = SessionSettings.SimSpeed;
            }
            // CHART (owner 2026-10-18): the VFR sectional, the IFR low chart and the satellite image, any
            // time. It shows no own-ship position, deliberately — see ChartPanel.
            if (UiLayout.Button(UiLayout.ToolbarSlot(6, n), "CHART", ChartPanel.IsOpen ? _btnOn : _btn))
            { ChartPanel.Toggle(); Options?.Close(); Widget.GameAeroPanel.Close(); }
            if (lesson && UiLayout.Button(UiLayout.ToolbarSlot(7, n), "END", _btn)) { _practice.End(); (_menu ??= Object.FindFirstObjectByType<StartMenu>())?.Open(); }
            // Radio push-to-talk (multiplayer), bottom left (Ty 2026-11-08: "The radio is annoying just move the PTT
            // button to the bottom left of the screen and label it PTT"). It was a centred text-stack block, which put
            // a two-line HOLD TO TALK sign straight across the aeroplane. It sits just ABOVE the control tray rather
            // than in the corner proper: the corner is the rudder/throttle pad, and a transmit button over the rudder
            // is worse than one that is slightly higher up.
            float pw = UiLayout.S * 0.15f, ph = UiLayout.ButtonH * 1.2f;
            float trayTop = Mathf.Min(UiLayout.BottomLimit, Screen.height - UiLayout.SafeBottom);
            float voiceX = UiLayout.SafeLeft + UiLayout.Margin, voiceY = trayTop - ph - UiLayout.Gap;
            if (VoiceComms.RadioAvailable)
            {
                _talkRect = new Rect(voiceX, voiceY, pw, ph);
                GUI.Box(_talkRect, "PTT", VoiceComms.PttHeld ? _btnRec : _btn);
            }
            else _talkRect = default;
            // VOX goes BESIDE PTT, on the same row, and it is drawn whether or not the radio exists: the radio only
            // exists in a room, but a hot mic does not need one — narration alone can hold the microphone open on a
            // solo flight, which is the case that cost Ty an evening, so the indicator has to be there then too.
            // Same row as PTT on purpose: that row's clearance above the control tray is the one Ty already approved
            // (2026-11-08), so this adds no new way to crowd the pads or the dials. PTT keeps the corner slot it was
            // given and never moves; VOX takes it only when there is no PTT to sit next to.
            DrawVox(new Rect(VoiceComms.RadioAvailable ? voiceX + pw + UiLayout.Gap : voiceX, voiceY, pw, ph));
            string radio = VoiceComms.MicProblem ?? (VoiceComms.Receiving != null ? $"RADIO: {VoiceComms.Receiving}" : null);
            if (radio != null) UiLayout.Label(UiLayout.NextLine(_fs * 1.4f), radio, _label);
        }

        /// <summary>
        /// THE VOX BUTTON, and the only thing on screen that says whether the microphone is open (Ty 2026-11-10:
        /// "Add a vox button on the screen", after an evening in which a persisted "Record my voice in clips" held
        /// the mic from launch with nothing to show it — "Force quit and restart did not fix it").
        ///
        /// It does two jobs, and the second matters more. Tapping it arms or disarms VOX: armed, the microphone is
        /// held open, the voice gate opens on voice activity, and he hears himself in his own headset; disarmed, the
        /// microphone opens only while PTT is held. PTT is untouched either way.
        ///
        /// VOX IS THE INTERCOM, NOT THE RADIO. Ty 2026-11-10: "No vox so intercom between pilots in the same
        /// airplane or solo hearing yourself talk which would allow me to do my narration tutorial videos." So this
        /// is the ONLY intercom control there is — the Options checkbox said the same thing and has gone — and it
        /// never keys the transmitter. Hearing himself is the feature he films with, not a side effect. The scope
        /// widens later to the other seat and then to a flight of aeroplanes; VoiceIO.mm's VOX bus note carries his
        /// specification of why that path can never be the radio's.
        ///
        /// AND IT SHOWS THE REAL STATE, not its own. The colour follows <see cref="VoiceComms.MicOpen"/>, which is
        /// read back out of the native engine (VO_State) rather than worked out from the settings here, so it cannot
        /// say "shut" while VoiceIO.mm has the mic. That is why it goes RED and reads MIC when the microphone is open
        /// with VOX off — the PTT unkey linger — rather than only when this button did it. Red is already this
        /// toolbar's "live" colour (REC, PTT held).
        ///
        /// Three states, so "armed" and "open" are never confused:
        ///   dull   VOX     — the microphone is shut. Nothing is listening.
        ///   amber  VOX ..  — armed, but the mic is not open yet: starting up, or permission refused (MicProblem
        ///                    says which, on its own line). A state that should flicker past, and sticks if denied.
        ///   red    VOX / MIC, with a level bar — THE MICROPHONE IS OPEN. The bar is what it is hearing, so a glance
        ///                    separates "open and quiet" from "open and picking me up".
        /// </summary>
        private void DrawVox(Rect r)
        {
            bool open = VoiceComms.MicOpen, armed = VoiceComms.VoxOn;
            GUIStyle st = open ? _btnRec : armed ? _btnArm : _btn;
            // With VOX off and the mic open anyway, the word VOX would be a lie about why: say MIC instead.
            string label = open ? (armed ? "VOX ●" : "MIC ●") : armed ? "VOX .." : "VOX";
            if (UiLayout.Button(r, label, st)) VoiceComms.VoxOn = !VoiceComms.VoxOn;
            if (!open) return;
            // Level bar across the bottom of the button: a dark channel with a white fill, inside the button's own
            // edge so it reads as part of it rather than as a separate widget.
            float inset = Mathf.Max(2f, r.width * 0.06f), h = Mathf.Max(2f, r.height * 0.12f);
            var channel = new Rect(r.x + inset, r.yMax - inset - h, r.width - 2f * inset, h);
            GUI.DrawTexture(channel, _track);
            float lvl = Mathf.Clamp01(VoiceComms.MicLevel01);
            if (lvl > 0.01f) GUI.DrawTexture(new Rect(channel.x, channel.y, channel.width * lvl, channel.height), _knob);
        }

        // ---- LOCAL 2-PLAYER, compact split (SplitControls) -------------------------------------------------------------
        //
        // The full toolbar row runs across both halves and the touch tray is off (TouchFlightControls.TrayLive), so RESET
        // and the handle would be out of reach. One FLIGHT entry takes the row's place, on the seam between the two views
        // (landscape: the middle of the seam; portrait: the left edge of P1's half, level with its middle), clear of the
        // compact HUD's top and bottom rows. It opens one column per player, and every button acts on that column's
        // aeroplane only: P1's RESET resets P1, P2's resets P2 (LocalCoop.ResetP2), and the handle is P1's alone (the
        // second aeroplane has no egress yet). MENU and OPTIONS stay where they are (OPTIONS pauses both players).
        private bool _flightOpen;
        private LocalCoop _coop;

        private void DrawFlightEntry()
        {
            var views = LocalPlayerView.All;
            if (views.Count < 2) { _flightOpen = false; return; }
            float s = UiLayout.S, bw = s * 0.17f, bh = UiLayout.ButtonH;
            Rect entry = ScreenLayout.Portrait
                ? new Rect(UiLayout.SafeLeft + UiLayout.Margin, views[0].GuiRect.center.y - bh * 0.5f, bw, bh)
                : new Rect(Screen.width * 0.5f - bw * 0.5f, Screen.height * 0.5f - bh * 0.5f, bw, bh);
            if (!_flightOpen)
            {
                if (UiLayout.Button(entry, "FLIGHT", _btn)) _flightOpen = true;
                BlockRect = entry; _blockFrame = Time.frameCount;
                return;
            }
            UiLayout.ModalShown();
            float gap = s * 0.015f, colW = Mathf.Min(s * 0.42f, (Screen.width * 0.92f - 3 * gap) / 2f), rowH = UiLayout.ButtonH * 1.1f;
            const int rows = 4;   // header, RESET, handle, VIEW
            float w = 2 * colW + 3 * gap, h = rows * (rowH + gap) + rowH + 2 * gap;
            var panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            BlockRect = panel; _blockFrame = Time.frameCount;
            GUI.DrawTexture(panel, _bg);
            for (int i = 0; i < 2; i++)
            {
                var v = views[i];
                float x = panel.x + gap + i * (colW + gap), y = panel.y + gap;
                Rect Row(int k) => new Rect(x, y + k * (rowH + gap), colW, rowH);
                string pad = GamepadInput.NameFor(i);
                UiLayout.Label(Row(0), $"P{i + 1}" + (pad != null ? "" : "  (no controller)"), _label);
                if (FlyingGame.Core.LocalMp.SplitControls.Offers(i, FlyingGame.Core.LocalMp.SplitControls.Action.Reset, false)
                    && UiLayout.Button(Row(1), $"P{i + 1} RESET", _btn))
                { ResetPlayer(i, v); _flightOpen = false; }
                var touch = i == 0 && v.Driver != null ? v.Driver.GetComponent<TouchFlightControls>() : null;
                bool handle = touch != null && touch.MenuHandleAvailable;
                if (FlyingGame.Core.LocalMp.SplitControls.Offers(i, FlyingGame.Core.LocalMp.SplitControls.Action.Handle, handle)
                    && UiLayout.Button(Row(2), $"P{i + 1} " + touch.MenuHandleLabel.ToUpperInvariant(), _btnRec))
                { touch.MenuPullHandle(); _flightOpen = false; }
                if (FlyingGame.Core.LocalMp.SplitControls.Offers(i, FlyingGame.Core.LocalMp.SplitControls.Action.View, false)
                    && v.Chase != null && UiLayout.Button(Row(3), "VIEW: " + ChaseCamera.ViewNames[(int)v.Chase.CurrentView], _btn))
                    v.Chase.NextView();
            }
            if (UiLayout.Button(new Rect(panel.x + gap, panel.yMax - gap - rowH, panel.width - 2 * gap, rowH), "CLOSE", _btn)) _flightOpen = false;
        }

        private void ResetPlayer(int player, LocalPlayerView v)
        {
            if (player == 0) { v.Driver?.GetComponent<TouchFlightControls>()?.MenuReset(); return; }   // marks it P1's own reset
            (_coop != null ? _coop : _coop = Object.FindFirstObjectByType<LocalCoop>())?.ResetP2();
        }

        /// <summary>The view picker (owner 2026-10-05: "tapping the view button should bring up a menu of the views to choose
        /// from, not a list that cycles through with each press"): every view as a button, the current one lit; tap one to fly
        /// it (the panel closes), or VIEW / CLOSE to dismiss. Laid out between <paramref name="top"/> and <paramref name="bottom"/>
        /// (under the toolbar; above the replay bar), the HUD stepping aside while it is up.</summary>
        /// <summary>
        /// LOOKING AROUND on glass (owner 2026-10-31), for a device with no stick: a four-way pad bottom-left, a seat
        /// button and CENTRE. Held pans, tapped snaps — the same two gestures a real hat gives, decided in
        /// <see cref="CockpitView"/> so the stick and the glass behave identically. Only drawn in the cockpit, where
        /// the pilot has a head to turn; the outside views already have their own camera.
        ///
        /// The pad stays even while the device is driving the view (owner Ty, 2026-11-01). The two no longer fight:
        /// CockpitLook keeps the hat's offset and the device's apart and sums them, so the pad still works as a
        /// coarse adjustment on top of where the phone is pointing, and RECENTRE joins the row below.
        /// </summary>
        private void DrawLookPad()
        {
            _cockpit ??= Object.FindFirstObjectByType<CockpitView>();
            if (_cockpit == null) { return; }
            if (Chase == null || Chase.CurrentView != ChaseCamera.View.Cockpit) { _cockpit.OnScreenHat = Vector2Int.zero; return; }
            float b = UiLayout.ButtonH, m = UiLayout.Margin;
            float cell = b * 1.05f;
            // Under the toolbar on the LEFT. Not along the bottom: that is where the throttle strip and the stick
            // live, and a look pad on top of the controls would be read as both.
            float x0 = m, y0 = UiLayout.ToolbarBottom + m;
            LookRect = new Rect(x0, y0 - m * 0.4f, cell * 3f, cell * 3f + b + m);
            _lookFrame = Time.frameCount;

            int hx = 0, hy = 0;
            if (Pad(new Rect(x0 + cell, y0, cell, cell), "▲")) hy += 1;
            if (Pad(new Rect(x0, y0 + cell, cell, cell), "◄")) hx -= 1;
            if (Pad(new Rect(x0 + cell * 2f, y0 + cell, cell, cell), "►")) hx += 1;
            if (Pad(new Rect(x0 + cell, y0 + cell * 2f, cell, cell), "▼")) hy -= 1;
            _cockpit.OnScreenHat = new Vector2Int(hx, hy);

            // The middle of the pad centres the view; it is the one place a thumb expects it.
            if (UiLayout.Button(new Rect(x0 + cell, y0 + cell, cell, cell),
                                _cockpit.Look.LookingAway ? "●" : "", _cockpit.Look.LookingAway ? _btnOn : _btn))
                _cockpit.Centre();

            // RECENTRE, only while the device is driving the view. The pad's own middle key centres the hat's
            // offset; this one also re-zeroes against however the device is being held, which is the thing a pilot
            // actually needs after shifting in his seat.
            float row = y0 + cell * 3f + m * 0.3f;
            if (_cockpit.DeviceLook)
            {
                if (UiLayout.Button(new Rect(x0, row, cell * 3f, b), "RECENTRE", _btn)) _cockpit.Centre();
                row += b + m * 0.3f;
            }
            else if (TiltSteering.Enabled)
            {
                // Tilt has the phone, so the view is looked around with the pad above — which is always drawn — and
                // this takes the place of the device RECENTRE: it re-zeroes the STICK against however the phone is
                // being held now, which is what a pilot needs after shifting his grip (Ty 2026-11-08).
                if (UiLayout.Button(new Rect(x0, row, cell * 3f, b), "CENTRE TILT", _btn)) TiltSteering.Centre();
                row += b + m * 0.3f;
            }

            // The seat, when the aeroplane has more than one authored.
            if (_cockpit.SeatCount > 1 &&
                UiLayout.Button(new Rect(x0, row, cell * 3f, b), "SEAT: " + _cockpit.SeatLabel, _btn))
                _cockpit.NextSeat();
            LookRect = new Rect(x0, y0 - m * 0.4f, cell * 3f, row + b + m - (y0 - m * 0.4f));
        }

        /// <summary>A pad key reports HELD, not clicked: panning needs the held state every frame.</summary>
        private bool Pad(Rect r, string glyph)
        {
            bool held = Input.GetMouseButton(0) && r.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
            for (int i = 0; i < Input.touchCount && !held; i++)
            {
                Touch t = Input.GetTouch(i);
                if (t.phase != TouchPhase.Ended && t.phase != TouchPhase.Canceled &&
                    r.Contains(new Vector2(t.position.x, Screen.height - t.position.y))) held = true;
            }
            UiLayout.Button(r, glyph, held ? _btnOn : _btn);
            return held;
        }

        private CockpitView _cockpit;

        private void DrawViewMenu(float top, float bottom)
        {
            UiLayout.ModalShown();
            float s = UiLayout.S, gap = s * 0.015f;
            int count = ChaseCamera.ViewNames.Length + 1;   // + CLOSE
            int cols = ScreenLayout.Portrait ? 2 : 3, rows = (count + cols - 1) / cols;
            float bw = Mathf.Min(s * 0.42f, (Screen.width * 0.92f - (cols - 1) * gap) / cols);
            float avail = bottom - top - 4 * gap;
            float bh = Mathf.Min(UiLayout.ButtonH * 1.1f, (avail - (rows - 1) * gap - _fs * 2f) / rows);
            float w = cols * bw + (cols - 1) * gap, h = _fs * 2f + rows * bh + (rows - 1) * gap;
            var panel = new Rect((Screen.width - w) * 0.5f - gap, top + gap, w + 2 * gap, h + 2 * gap);
            BlockRect = panel; _blockFrame = Time.frameCount;
            GUI.DrawTexture(panel, _bg);
            UiLayout.Label(new Rect(panel.x, panel.y + gap * 0.5f, panel.width, _fs * 1.8f), "CHOOSE A VIEW", _label);
            float y0 = panel.y + gap + _fs * 2f;
            for (int k = 0; k < count; k++)
            {
                int r = k / cols, c = k % cols;
                var b = new Rect(panel.x + gap + c * (bw + gap), y0 + r * (bh + gap), bw, bh);
                if (k == count - 1) { if (UiLayout.Button(b, "CLOSE", _btn)) _viewMenu = false; continue; }
                bool cur = (int)Chase.CurrentView == k;
                if (UiLayout.Button(b, ChaseCamera.ViewNames[k], cur ? _btnOn : _btn)) { Chase.SetView((ChaseCamera.View)k); _viewMenu = false; }
            }
        }
        private bool _viewMenu;
        /// <summary>Self-test: open the view menu.</summary>
        public static bool OpenViewMenuRequest;
        private static Rect BlockRect; private static int _blockFrame = -10;
        private static Rect LookRect; private static int _lookFrame = -10;
        /// <summary>The open view menu (SCREEN coordinates, bottom-left origin) — the flight pads ignore touches that start on it.</summary>
        public static bool BlocksTouch(Vector2 screenPos)
        {
            if (Widget.GameAeroPanel.Blocks(screenPos)) return true;   // the AERO panel's switches are not the stick
            var p = new Vector2(screenPos.x, Screen.height - screenPos.y);
            // The look pad is its own rect: it can be up at the same time as the view menu, and a thumb panning the
            // view must not also be flying the aeroplane.
            if (Time.frameCount - _lookFrame <= 2 && LookRect.Contains(p)) return true;
            if (Time.frameCount - _blockFrame > 2) return false;
            return BlockRect.Contains(p);
        }

        private void DrawReplayBar()
        {
            float s = Mathf.Min(Screen.width, Screen.height);
            bool portrait = ScreenLayout.Portrait;
            float gap = s * 0.015f;
            // 12 slots: portrait keeps its two rows of six, and landscape went from 11 to 12 when DEBRIEF arrived
            // (Ty 2026-11-10). Portrait's second row had one slot spare, so nothing in the flight-tested layout moved.
            int perRow = portrait ? 6 : 12;   // (the manual SMOKE switch keeps its own slot, so nothing moves when it appears)
            int rows = portrait ? 2 : 1;
            float bh = s * (portrait ? 0.085f : 0.07f);
            float trackH = s * 0.05f, labelH = _fs * 1.6f;
            float barH = portrait ? ScreenLayout.TrayHeightPx : labelH + trackH + rows * (bh + gap) + 3 * gap;
            var bar = new Rect(0f, Screen.height - barH, Screen.width, barH);
            GUI.DrawTexture(bar, _bg);   // portrait: also covers the tray the pads normally paint

            var rec = Replay.Recorder;
            double start = rec.StartTime, end = rec.EndTime;
            float x0 = gap * 2f, w = Screen.width - gap * 4f;
            float y = bar.y + gap;
            GUI.Label(new Rect(x0, y, w, labelH),
                $"REPLAY   {Clock(Replay.Head - start)} / {Clock(end - start)}    ({Clock(end - Replay.Head)} before you paused)   ×{Replay.Speed:0.##}", _small);
            y += labelH + gap * 0.5f;

            // Scrub track: tap or drag anywhere on it.
            var track = new Rect(x0, y + trackH * 0.35f, w, trackH * 0.3f);
            var hit = new Rect(x0 - gap, y - gap, w + 2 * gap, trackH + 2 * gap);
            float frac = end > start ? (float)((Replay.Head - start) / (end - start)) : 1f;
            GUI.DrawTexture(track, _track);
            GUI.DrawTexture(new Rect(track.x, track.y, track.width * frac, track.height), _fill);
            GUI.DrawTexture(new Rect(track.x + track.width * frac - trackH * 0.2f, y, trackH * 0.4f, trackH), _knob);
            Event e = Event.current;
            if (e.type == EventType.MouseDown && hit.Contains(e.mousePosition)) _scrubbing = true;
            if (_scrubbing && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag))
            {
                float u = Mathf.Clamp01((e.mousePosition.x - track.x) / track.width);
                Replay.Seek(start + u * (end - start));
                e.Use();
            }
            if (e.type == EventType.MouseUp) _scrubbing = false;
            y += trackH + gap;

            float bw = (w - (perRow - 1) * gap) / perRow;
            // (UiLayout.Button: each label shrinks to fit its button — 10 slots a row made SMOKE / VIEW spill on an iPad,
            // as SMOKE AS FLOWN and VIEW already did at 9)
            int i = 0;
            Rect Next() { int r = i / perRow, c = i % perRow; i++; return new Rect(x0 + c * (bw + gap), y + r * (bh + gap), bw, bh); }
            if (UiLayout.Button(Next(), "-10 s", _btn)) Replay.Seek(Replay.Head - 10.0);
            if (UiLayout.Button(Next(), Replay.Playing ? "PAUSE" : "PLAY", _btnOn)) Replay.TogglePlay();
            if (UiLayout.Button(Next(), "+10 s", _btn)) Replay.Seek(Replay.Head + 10.0);
            // (portrait has 5 a row: SPEED goes to the second row there, so the two smoke buttons stay side by side)
            if (!portrait && UiLayout.Button(Next(), $"SPEED ×{Replay.Speed:0.##}", _btn)) Replay.CycleSpeed();
            // Smoke in the replay (Ty t912u, t982u): the mode — AS FLOWN, the recorded timeline, every replay opens on it;
            // a press takes MANUAL control, keeping what is on screen, and a press on MANUAL returns to AS FLOWN at once —
            // and beside it, in manual only, the SMOKE switch, on / off at each press from the play head on, drawn as the
            // live one is (lit "SMOKE ON"). A clip made from the replay records the same smoke.
            bool manual = Replay.SmokeControl.Manual;
            if (UiLayout.Button(Next(), manual ? "SMOKE\nMANUAL" : "SMOKE\nAS FLOWN" + (Replay.SmokeUnrecorded ? " (none)" : ""), manual ? _btnOn : _btn))
                Replay.ToggleSmokeManual();
            Rect sw = Next();
            if (manual)
            {
                bool on = Replay.SmokeSwitchOn;
                GUI.color = on ? new Color(1f, 0.85f, 0.3f, 1f) : Color.white;
                if (UiLayout.Button(sw, on ? "SMOKE ON" : "SMOKE", _btn)) Replay.ToggleSmokeSwitch();
                GUI.color = Color.white;
            }
            if (portrait && UiLayout.Button(Next(), $"SPEED ×{Replay.Speed:0.##}", _btn)) Replay.CycleSpeed();
            if (UiLayout.Button(Next(), "VIEW\n" + ChaseCamera.ViewNames[(int)Chase.CurrentView], _viewMenu ? _btnOn : _btn)) _viewMenu = !_viewMenu;
            // OPTIONS, from inside the replay (Ty 2026-11-08: "i don't see a way to deselect it either … we really need
            // every option available and functioning correctly in replay so we can turn on/off anything"). The replay bar
            // used to REPLACE the live column, which is where OPTIONS lives, so a flight path vector or a cloud of
            // bubbles could not be turned off without leaving the replay first. Opening it does not disturb the replay:
            // ApplyTimeScale leaves the clock alone while one is running, and the play head runs on unscaled time.
            if (UiLayout.Button(Next(), "OPTIONS", Options != null && Options.IsOpen ? _btnOn : _btn)) Options?.Toggle();
            if (UiLayout.Button(Next(), ClipRecorder.Busy ? "SAVING..." : "CLIP " + ClipRecorder.Length(ClipRecorder.ClipSeconds), _btn)) ClipRecorder.SaveClip();
            bool recOn = ClipRecorder.Recording;
            if (UiLayout.Button(Next(), recOn ? "STOP REC" : "REC", recOn ? _btnRec : _btn)) ClipRecorder.ToggleRecording();
            if (UiLayout.Button(Next(), "EXIT\nREPLAY", _btnOn)) Replay.Exit();
            // THE DEBRIEF (Ty 2026-11-10): what failed, when, what you did, how long you took to notice. It lives
            // here and not on the live toolbar because that row is already seven buttons wide and his standing
            // complaint is menus — "we need a shortcut button so I don't have to several menus deep". From flying:
            // REPLAY, DEBRIEF, and a tap on a line puts the play head four seconds before the moment it is about.
            Debrief ??= GetComponent<DebriefPanel>();
            if (Debrief != null && UiLayout.Button(Next(), "DEBRIEF", DebriefPanel.IsOpen ? _btnOn : _btn))
                Debrief.Toggle();
            if (_viewMenu) DrawViewMenu(UiLayout.ToolbarBottom, bar.y);
        }
    }
}
