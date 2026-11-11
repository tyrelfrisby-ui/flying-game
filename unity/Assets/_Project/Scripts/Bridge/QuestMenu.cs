using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.Vr;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// THE MENU INSIDE THE HEADSET. Every menu in this game is OnGUI, and OnGUI draws to the phone's / Mac's
    /// screen — on a Quest that is a buffer nobody sees. Without this the app would open on a start page the
    /// pilot cannot see, paused, with no way out. So a small world-space panel, built from the game's own font,
    /// carries what a pilot needs from inside the cockpit:
    ///
    ///   FLY / RESUME · RESTART FLIGHT · Aircraft · Start · Seat · View · HUD in cockpit · Display 72 / 90 Hz ·
    ///   Rudder pedals (a page of its own) · Record video · Recentre · Reset flight · Bail out / Deploy BRS /
    ///   Eject (asks twice)
    ///
    /// THE RUDDER PEDALS PAGE is the same mapping and calibration as Options → Controls on the flat screens (Core's
    /// PedalSession; docs/RUDDER-PEDALS.md): device, on / off, each role's axis (&lt; &gt;) and invert (select), dead
    /// zones, and the guided calibration with the axes' live values.
    ///
    /// It is deliberately NOT the whole landing page: lessons, challenges, weather and multiplayer have OnGUI
    /// cards of their own that would be just as invisible, so a flight started from here is FREE FLIGHT
    /// (<see cref="SessionSettings.ChallengeId"/> = null). Bringing those across is Codex's VR-UI work.
    ///
    /// Point with either hand (controller or tracked hand) and pull the trigger / press A or X / pinch; or move
    /// the highlight with a thumbstick and push it left / right to change a value. B, Y or the menu button
    /// closes it in flight. It opens by itself whenever the landing page is up, and pauses the sim while it is
    /// open in flight, exactly as the Options panel does. In flight it opens from the LEFT controller's menu
    /// button (☰), or the left stick click held for a second (QuestControlMapper.MenuHoldSec); the first few
    /// flights show a small tip in the cockpit saying so (owner Ty, 2026-11-02: "i couldn't figure out how to
    /// get to options").
    ///
    /// THE SEAT ROW chooses for the aeroplane that will FLY (on the landing page that is the Aircraft row's, not
    /// the glider the driver happens to have loaded) and keeps the choice by seat ID (SessionSettings.ChooseSeat):
    /// see SeatChoice for how an index once put a "rear" pilot in the Pitts' front seat.
    ///
    /// Drawn with FlyingGame/XrOverlay (force-included), which ignores depth, so the panel shows through the
    /// instrument panel it is floating in front of.
    ///
    /// IT RIDES THE SEAT, NOT THE AEROPLANE (<see cref="QuestRig.HudAnchor"/>). Until 2026-11-02 the panel was
    /// parented to the aeroplane's transform, and every airframe rebuild — every FLY — destroys any child with a
    /// renderer it did not build itself (AirframeVisual.Clear, "stray child cleared on rebuild"). So the menu
    /// worked on the landing page and was a destroyed object from the first flight on: ☰ and the held stick click
    /// both threw a MissingReferenceException inside Open, the menu never appeared, and the only way to another
    /// aeroplane was to restart the app (Ty, build a222bda). The anchor is a root object of the rig's own, and if
    /// the panel is ever destroyed anyway it is built again rather than failing.
    ///
    /// CHANGE AIRCRAFT in flight goes back to the landing page's rows (FLY, Aircraft, Start, Seat …) without
    /// restarting anything; FLY there starts the new choice.
    /// </summary>
    public sealed class QuestMenu
    {
        private sealed class Row
        {
            public System.Func<string> Label;
            public System.Action Activate;
            public System.Action<int> Cycle;
            public System.Func<bool> Visible;
            /// <summary>On the Rudder pedals page rather than the main one.</summary>
            public bool PedalPage;
            /// <summary>A name to open the menu ON this row (<see cref="OpenAt"/>), or null.</summary>
            public string Key;
            public TextMesh Text;
        }

        public const float Distance = 0.8f, RowHeight = 0.042f, Width = 0.62f, CharHeight = 0.024f;

        private readonly List<Row> _rows = new();
        private readonly List<Row> _shown = new();
        private GameObject _root;
        private Transform _highlight;
        private LineRenderer _laser;
        private TextMesh _title, _hint;
        private bool _open, _built, _pedalPage;
        private int _sel, _openedFrame;
        private string _pendingKey;
        private string _cardsNote = "";
        private float _navHeld, _cycleHeld;
        private bool _prevTrig, _prevA, _prevX, _prevB, _prevY, _prevPinch;
        private float _confirmUntil;
        private Material _textMat, _panelMat, _barMat, _laserMat;
        private StartMenu _start;
        private QuestRig _rig;
        private readonly Dictionary<string, List<StationConfig>> _seatsByType = new();

        // The "press MENU" tip: a small card low in the cockpit for the first flights (QuestCockpit.ShowTip).
        private Transform _tip;
        private TextMesh _tipText;
        private Material _tipPanelMat;
        private float _tipShownAt = -1f;

        // The power readout: "POWER 65 %" low on the left, by the throttle hand, while the lever moves
        // (QuestCockpit.PowerOpacity), and with the how-to line on the first flights.
        private Transform _power;
        private TextMesh _powerText;
        private Material _powerPanelMat;
        private float _powerMovedAt = -100f, _powerHintUntil = -1f;
        private double _powerLast = double.NaN;
        private string _powerShown = "";

        public bool IsOpen => _open;

        private ChaseCamera Chase => _rig != null ? _rig.GetComponent<ChaseCamera>() : null;
        private CockpitView Cockpit => Chase != null ? Chase.Cockpit : null;
        private FlightSimDriver Driver => Chase != null ? Chase.Driver : null;
        private TouchFlightControls Controls => Driver != null ? Driver.GetComponent<TouchFlightControls>() : null;
        private StartMenu Landing => _start != null ? _start : _start = Object.FindFirstObjectByType<StartMenu>();
        private bool OnLanding => SessionSettings.MenuOpen && Landing != null && Landing.IsOpen;

        private static readonly (SessionSettings.Start mode, string name)[] Starts =
        {
            (SessionSettings.Start.InTheAir, "In the air"), (SessionSettings.Start.OnTheRunway, "On the runway"),
            (SessionSettings.Start.OnFinal, "On final, 300 ft"), (SessionSettings.Start.InAeroBox, "Aerobatic box"),
            (SessionSettings.Start.InCombatZone, "Combat zone"), (SessionSettings.Start.InThermal, "In a thermal"),
        };

        // ---- open / close ----

        /// <summary>The menu button, the held stick click, a held pinch or the MENU tab (<paramref name="via"/> says
        /// which, for the log). Opening it in flight means he has found it: the tip is retired for good.</summary>
        public void Toggle(QuestRig rig, string via = "")
        {
            if (_open) { Close(force: false, via); return; }
            Open(rig, via);
            if (_open && !OnLanding) XrHeadset.MenuFound = true;
        }

        public void Open(QuestRig rig, string via = "")
        {
            _rig = rig;
            EnsureBuilt();
            _open = true;
            _openedFrame = Time.frameCount;
            _root.SetActive(true);
            XrHeadset.MenuOpen = true;
            bool landing = OnLanding;
            if (!landing) { SessionSettings.PanelPause = true; SessionSettings.ApplyTimeScale(); }
            _sel = 0;
            _pedalPage = false;
            _confirmUntil = 0f;
            EndTip();
            PlaceInFrontOfHead();
            // Whatever is already held (the trigger that just opened it from the MENU tab) is not a press in here.
            QuestControllerFrame f = XrControllers.Read(out _);
            _prevTrig = f.RightTrigger > 0.6 || f.LeftTrigger > 0.6;
            _prevA = f.A; _prevX = f.X; _prevB = f.B; _prevY = f.Y;
            _prevPinch = f.HandsOnly && (f.RightPinch > 0.8 || f.LeftPinch > 0.8);
            Debug.Log($"[QuestInput] menu OPEN{(via.Length > 0 ? $" by {via}" : "")} ({(landing ? "landing page" : "in flight, paused")})");
        }

        /// <summary>Close it. On the landing page it cannot be closed except by flying — there is nothing behind it
        /// the pilot could see.</summary>
        public void Close(bool force, string via = "")
        {
            if (!_open) return;
            if (!force && OnLanding) return;
            _open = false;
            XrHeadset.MenuOpen = false;
            if (_root != null) _root.SetActive(false);
            if (!OnLanding) { SessionSettings.PanelPause = false; SessionSettings.ApplyTimeScale(); }
            Debug.Log($"[QuestInput] menu closed{(via.Length > 0 ? $" by {via}" : "")}");
        }

        /// <summary>Build the panel, or build it again if something destroyed it (see the class notes: it used to be
        /// destroyed with the aeroplane). A menu that cannot open is worse than one rebuilt.</summary>
        private void EnsureBuilt()
        {
            if (_built && _root != null) return;
            if (_built)
            {
                Debug.LogWarning("[QuestInput] the VR menu's objects had been destroyed: building it again");
                _rows.Clear();
                _built = false;
            }
            Build();
        }

        /// <summary>Editor shot tool (QuestMenuShots): build, open and lay the menu out with no headset and no Update.</summary>
        public GameObject OpenForTesting(QuestRig rig)
        {
            Open(rig);
            Refresh();
            return _root;
        }

        /// <summary>Every frame from <see cref="QuestRig"/>.</summary>
        public void Tick(QuestRig rig)
        {
            _rig = rig;
            if (!_open && OnLanding) Open(rig);
            if (_open && _pedalPage) { RudderPedals.CalibrationShown(); RudderPedals.Tick(); }   // live values and the calibration, with the sim paused
            TickTip();
            TickPower();
            if (!_open) return;
            Refresh();
            if (Time.frameCount != _openedFrame) HandleInput();
        }

        private void PlaceInFrontOfHead()
        {
            Transform head = _rig.transform;
            Vector3 f = head.forward;
            Vector3 flat = new Vector3(f.x, 0f, f.z);
            flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
            Vector3 pos = head.position + flat * Distance - Vector3.up * 0.05f;
            // Ride the SEAT (the rig's anchor, a root object that follows the aeroplane), never the aeroplane's own
            // transform: an airframe rebuild destroys every drawable child it finds there (see the class notes).
            Transform anchor = _rig.HudAnchor;
            _root.transform.SetParent(anchor, false);
            _root.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(flat, Vector3.up));
        }

        // ---- the rows ----

        private void Build()
        {
            _built = true;
            Font font = UiFont.Get();
            Shader sh = Shader.Find("FlyingGame/XrOverlay");   // ours: ZTest Always, force-included (BuildScript)
            if (sh == null) { Debug.LogError("[Quest] FlyingGame/XrOverlay shader missing from the build: no VR menu"); sh = Shader.Find("Unlit/Color"); }
            _textMat = new Material(sh) { mainTexture = font.material.mainTexture, renderQueue = 4002 };
            Font.textureRebuilt += f => { if (f == font && _textMat != null) _textMat.mainTexture = font.material.mainTexture; };
            _panelMat = Solid(sh, new Color(0.05f, 0.08f, 0.12f, 0.92f), 4000);
            _barMat = Solid(sh, new Color(0.16f, 0.34f, 0.46f, 0.98f), 4001);
            _laserMat = Solid(sh, new Color(0.5f, 0.95f, 1f, 0.8f), 4003);

            _root = new GameObject("VR Menu");
            if (Application.isPlaying) Object.DontDestroyOnLoad(_root);

            AddRow(() => OnLanding ? "FLY" : "RESUME", () => { if (OnLanding) FlyNow(); else Close(force: false, "RESUME"); });
            // In flight: the aircraft row's choice, if it differs from the one flying, starts right here.
            AddRow(() => !OnLanding && Driver != null && Driver.AircraftId != SessionSettings.AircraftId
                    ? $"FLY THE {AircraftName(SessionSettings.AircraftId).ToUpperInvariant()} NOW" : "RESTART FLIGHT", FlyNow, visible: () => !OnLanding);
            AddRow(() => "CHANGE AIRCRAFT  (main menu)   >", GoToLanding, visible: () => !OnLanding && Landing != null);
            AddRow(() => $"Aircraft:  {AircraftName(SessionSettings.AircraftId)}" + (!OnLanding && Driver != null && Driver.AircraftId != SessionSettings.AircraftId ? "   (restart to fly it)" : ""),
                () => CycleAircraft(1), CycleAircraft);
            AddRow(() => $"Start:  {StartName()}", () => CycleStart(1), CycleStart);
            // MULTIPLAYER (Ty 2026-11-05: two Quest users on the same Wi-Fi, each in their own headset): Solo or the
            // "Same network" room, the one the phones, iPads and Macs join from their landing page (LanMesh: no
            // server, no internet, nothing billed). Each headset flies its own aeroplane with its own full view; the
            // others are drawn as they fly. Takes effect on FLY / restart. In flight the next row says who is there.
            AddRow(() => $"Multiplayer:  {MultiplayerName()}" + (!OnLanding && Session != null && Session.Active != (SessionSettings.Multiplayer != SessionSettings.MultiplayerMode.Solo) ? "   (restart to join)" : ""),
                () => CycleMultiplayer(1), CycleMultiplayer);
            AddRow(() => $"   {Session?.StatusLine ?? "Same network: starting"}", null,
                visible: () => !OnLanding && Session != null && Session.Active);
            // Gear option (the landing page's Bushwheels / Wheel-skis buttons, Ty 2026-11-05: wheel-skis on every
            // aeroplane): wheels, bushwheels or wheel-skis; with skis fitted the cockpit gets the WHEELS / SKIS selector.
            AddRow(() => $"Gear:  {GearName()}", () => CycleGear(1), CycleGear);
            AddRow(() => $"Seat:  {SeatChoice.Label(SeatsOf(SeatAircraftId), SessionSettings.SeatIdFor(SeatAircraftId))}",
                () => CycleSeat(1), CycleSeat, () => SeatsOf(SeatAircraftId).Count > 1);
            // VIEWS (Ty 2026-11-04: "no views are available except 'outside' which is actually cockpit"). The row used to
            // be a two-way toggle labelled from ChaseCamera.InCockpit; now it cycles the views a headset can stand
            // (QuestViews) and says the one selected, whatever the camera did this frame.
            AddRow(() => $"View:  {QuestViews.Label(Chase != null ? Chase.CurrentView : ChaseCamera.View.Cockpit)}   < >", () => CycleView(1), CycleView);
            // FLY BY HAND (Ty 2026-11-03): the pilot's body and his hands on the stick, throttle and handles. Off,
            // the controllers fly as they always have and no body is drawn.
            AddRow(() => $"Fly by hand:  {(XrHeadset.FlyByHand ? "On  (body + hands)" : "Off")}", ToggleFlyByHand, _ => ToggleFlyByHand());
            AddRow(() => $"HUD in cockpit:  {(XrHeadset.HudInCockpit ? "On" : "Off")}", ToggleHud, _ => ToggleHud());
            AddRow(() => $"Display:  {(XrHeadset.Prefer90Hz ? "90 Hz" : "72 Hz")}" + (XrHeadset.CurrentRefreshHz > 0f ? $"   (running {XrHeadset.CurrentRefreshHz:F0})" : ""),
                () => XrHeadset.Prefer90Hz = !XrHeadset.Prefer90Hz, _ => XrHeadset.Prefer90Hz = !XrHeadset.Prefer90Hz);
            AddRow(() => $"Rudder pedals:  {PedalSummary()}   >", OpenPedals);
            // STICK FEEL (Ty 2026-11-02: "far too sensitive, especially around neutral"). The Options → Controls expo,
            // all three axes at once; unset means the headset's own default (QuestStickFeel). Select puts it back.
            AddRow(() => $"Stick expo:  {StickExpo() * 100:0} %" + (SessionSettings.FeelExpo[0] < 0f ? "  (default)" : "") + "   < >",
                () => SetStickExpo(-1f), d => SetStickExpo(Mathf.Clamp(Mathf.Round(StickExpo() * 20f + d) / 20f, 0f, 0.9f)));
            AddRow(() => $"Gamepad:  {XrControllers.GamepadLine}", null);
            // THE COCKPIT'S OWN (Ty 2026-11-05): the IAC card (poke the card itself to go to the next), the cards copied into
            // the headset's inbox, and the altimeter's Kollsman setting (< > a hundredth; select = field elevation / zero).
            AddRow(() => $"IAC card:  {Short(SequenceCardLibrary.Current.LabelAscii, 30)}   < >", () => SequenceCardLibrary.Next(),
                d => { if (d > 0) SequenceCardLibrary.Next(); else SequenceCardLibrary.Previous(); }, key: "card");
            AddRow(() => "Import cards from the inbox" + (_cardsNote.Length > 0 ? $"   ({Short(_cardsNote, 26)})" : ""),
                () => { SequenceCardLibrary.ImportInbox(out _cardsNote); Debug.Log("[SequenceCard] inbox: " + _cardsNote); });
            AddRow(() => $"Remove this card ({Short(SequenceCardLibrary.Current.Title, 22)})", () => SequenceCardLibrary.Remove(SequenceCardLibrary.Current.Id),
                visible: () => SequenceCardLibrary.Current.IsUser);
            AddRow(AltimeterLabel, AltimeterSelect, d => FlyingCockpit?.Altimeter.Step(d), visible: () => !OnLanding && FlyingCockpit != null, key: "altimeter");
            // Starting a recording from the menu in flight closes it, so the film starts on the flying, not on
            // the menu; stopping leaves it open. A + B held does the same from the air (QuestRecordChord).
            AddRow(() => QuestRecorder.MenuLabel, () =>
            {
                bool starting = !QuestRecorder.Recording;
                QuestRecorder.Toggle();
                if (starting && QuestRecorder.Recording && !OnLanding) Close(force: false);
            }, visible: () => QuestRecorder.Instance != null);
            AddRow(() => "Recentre the view", () => { XrHeadset.Recentre(); });
            AddRow(() => "Reset flight", () => { Controls?.MenuReset(); Close(force: false, "Reset flight"); }, visible: () => !OnLanding && Controls != null);
            AddRow(() => Time.unscaledTime < _confirmUntil ? $"PRESS AGAIN: {HandleName()}" : HandleName(), PullHandle,
                visible: () => !OnLanding && Controls != null && Controls.MenuHandleAvailable);

            BuildPedalRows();

            var panel = Quad("Panel", _panelMat);
            panel.SetParent(_root.transform, false);
            _highlight = Quad("Highlight", _barMat);
            _highlight.SetParent(_root.transform, false);
            _title = Text("Title", "AERO PLAYGROUND", CharHeight * 1.2f);
            _hint = Text("Hint", "", CharHeight * 0.7f);
            foreach (Row r in _rows) r.Text = Text("Row", "", CharHeight);

            _laser = new GameObject("Pointer").AddComponent<LineRenderer>();
            _laser.transform.SetParent(_root.transform, false);
            _laser.useWorldSpace = true;
            _laser.positionCount = 2;
            _laser.startWidth = _laser.endWidth = 0.003f;
            _laser.sharedMaterial = _laserMat;
            _laser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _laser.receiveShadows = false;
        }

        private void AddRow(System.Func<string> label, System.Action activate, System.Action<int> cycle = null, System.Func<bool> visible = null, bool pedalPage = false, string key = null) =>
            _rows.Add(new Row { Label = label, Activate = activate, Cycle = cycle, Visible = visible ?? (() => true), PedalPage = pedalPage, Key = key });

        /// <summary>Open the menu with the row named <paramref name="key"/> selected: a poke on the altimeter opens it on the
        /// altimeter row (CockpitTaps), the headset's stand-in for the glass's Kollsman popup.</summary>
        public void OpenAt(QuestRig rig, string key)
        {
            if (!_open) Open(rig, "poke " + key);
            _pedalPage = false;
            _pendingKey = key;
        }

        private CockpitRigState FlyingCockpit => Driver != null ? Driver.GetComponent<AirframeVisual>()?.Cockpit : null;

        private string AltimeterLabel()
        {
            CockpitRigState c = FlyingCockpit;
            if (c == null) return "Altimeter:  -";
            var a = c.Altimeter;
            return $"Altimeter:  {FlyingGame.Core.Instruments.KollsmanAltimeter.Window(a.InHg)} inHg   reads {a.IndicatedFt(c.StaticPa):N0} ft   < >";
        }

        /// <summary>Select: field elevation, then zero, then field elevation again (each press).</summary>
        private void AltimeterSelect()
        {
            CockpitRigState c = FlyingCockpit;
            if (c == null) return;
            bool atField = System.Math.Abs(c.Altimeter.IndicatedFt(c.StaticPa) - c.TrueAltitudeFt) < 15.0;
            if (atField) c.SetAltimeterToRead(0.0); else c.SetAltimeterToRead(c.TrueAltitudeFt);
        }
        private static string GearName() => SessionSettings.Skis ? "Wheel-skis  (selector in the cockpit)" : SessionSettings.Bushwheels ? "Bushwheels" : "Wheels";

        /// <summary>Wheels → bushwheels → wheel-skis (one or the other, as on the landing page), and the aeroplane re-loaded.</summary>
        private void CycleGear(int d)
        {
            int now = SessionSettings.Skis ? 2 : SessionSettings.Bushwheels ? 1 : 0;
            int next = ((now + d) % 3 + 3) % 3;
            SessionSettings.Bushwheels = next == 1;
            SessionSettings.Skis = next == 2;
            Driver?.ApplySession();
            Debug.Log($"[QuestMenu] gear: {GearName()}");
        }


        // ---- the rudder pedals page ----

        private static FlyingGame.Core.PedalSession Pedals => RudderPedals.Session;
        private static bool Calibrating => Pedals.Calibration != null;
        private static bool Mapping => Pedals.Connected && !Calibrating;

        private static string PedalSummary()
        {
            var s = Pedals;
            if (!s.Connected) return "none";
            return !s.Profile.Enabled ? "connected, OFF" : Calibrating ? "calibrating" : "ON";
        }

        private void OpenPedals() { _pedalPage = true; _sel = 1; RudderPedals.Tick(); }

        private static string Short(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 1) + "~";

        private void BuildPedalRows()
        {
            var roles = new[] { (FlyingGame.Core.PedalRole.Rudder, "Rudder"), (FlyingGame.Core.PedalRole.LeftBrake, "L toe"), (FlyingGame.Core.PedalRole.RightBrake, "R toe") };
            AddRow(() => "<  BACK", () => { _pedalPage = false; _sel = 0; }, pedalPage: true);
            AddRow(() => $"Device:  {Short(Pedals.DeviceLine(), 34)}", () => Pedals.CycleDevice(1), d => Pedals.CycleDevice(d),
                visible: () => !Calibrating, pedalPage: true);
            AddRow(() => "Plug pedals in (USB-C OTG adapter)", null, visible: () => !Pedals.Connected, pedalPage: true);
            AddRow(() => "Without them: rudder + brakes on the controllers", null, visible: () => !Pedals.Connected, pedalPage: true);
            AddRow(() => $"Use pedals:  {(Pedals.Profile.Enabled ? "On" : "Off")}", () => Pedals.SetEnabled(!Pedals.Profile.Enabled),
                _ => Pedals.SetEnabled(!Pedals.Profile.Enabled), visible: () => Mapping, pedalPage: true);
            foreach (var (role, name) in roles)
            {
                var r = role;
                // < > picks the axis; select flips it.
                AddRow(() => $"{name}:  {Pedals.RoleCompact(r)}", () => Pedals.ToggleInvert(r), d => Pedals.CycleAxis(r, d),
                    visible: () => Mapping, pedalPage: true);
            }
            AddRow(() => $"Dead zone  rud {Pedals.Profile.Rudder.Dead * 100:0}%", null, d => Pedals.AdjustDead(FlyingGame.Core.PedalRole.Rudder, 0.01 * d),
                visible: () => Mapping, pedalPage: true);
            AddRow(() => $"Dead zone  toes {Pedals.Profile.LeftBrake.Dead * 100:0}%", null, d =>
            {
                Pedals.AdjustDead(FlyingGame.Core.PedalRole.LeftBrake, 0.01 * d);
                Pedals.SetDead(FlyingGame.Core.PedalRole.RightBrake, Pedals.Profile.LeftBrake.Dead);
            }, visible: () => Mapping, pedalPage: true);
            AddRow(() => "CALIBRATE (guided)", () => Pedals.StartCalibration(), visible: () => Mapping, pedalPage: true);
            AddRow(() => "Reset to detected", () => Pedals.ResetToDefaults(), visible: () => Mapping, pedalPage: true);
            AddRow(() => "select = invert   < > = axis", null, visible: () => Mapping, pedalPage: true);

            // While calibrating: what to do, how far, each axis live with the ends seen, Skip / FINISH, Cancel.
            AddRow(() => Pedals.Calibration?.ShortInstruction ?? "", null, visible: () => Calibrating, pedalPage: true);
            AddRow(() => Pedals.Calibration == null ? "" : $"   {Pedals.Calibration.Status}  {Pedals.Calibration.Progress * 100:0}%", null, visible: () => Calibrating, pedalPage: true);
            for (int i = 0; i < 6; i++)
            {
                int a = i;
                AddRow(() => CalAxisLine(a), null, visible: () => Calibrating && a < Pedals.Device.Axes.Length, pedalPage: true);
            }
            AddRow(() => Pedals.Calibration?.Current == FlyingGame.Core.PedalCalibrator.Step.Sweep ? "FINISH" : "Skip this step",
                () => { if (Pedals.Calibration?.Current == FlyingGame.Core.PedalCalibrator.Step.Sweep) Pedals.FinishCalibration(); else Pedals.SkipCalibrationStep(); },
                visible: () => Calibrating, pedalPage: true);
            AddRow(() => "Cancel calibration", () => Pedals.CancelCalibration(), visible: () => Calibrating, pedalPage: true);
        }

        private static string CalAxisLine(int a)
        {
            var s = Pedals;
            if (s.Calibration == null || s.Device == null || a >= s.Device.Axes.Length) return "";
            (double lo, double hi) = s.Calibration.Seen(a);
            string name = s.Device.AxisNames[a].ToUpperInvariant();
            return $"{(a == s.Calibration.Focus ? ">" : " ")} {name,-7} {s.Device.Axes[a]:+0.00;-0.00}  {lo:+0.00;-0.00}..{hi:+0.00;-0.00}";
        }

        /// <summary>Lay the visible rows out top to bottom and refresh their text (only when it changes).</summary>
        private void Refresh()
        {
            _shown.Clear();
            foreach (Row r in _rows)
            {
                bool v = r.PedalPage == _pedalPage && r.Visible();
                r.Text.gameObject.SetActive(v);
                if (v) _shown.Add(r);
            }
            if (_pendingKey != null)
            {
                int k = _shown.FindIndex(r => r.Key == _pendingKey);
                if (k >= 0) _sel = k;
                _pendingKey = null;
            }
            _sel = Mathf.Clamp(_sel, 0, Mathf.Max(0, _shown.Count - 1));
            float h = (_shown.Count + 2.6f) * RowHeight;
            Transform panel = _root.transform.Find("Panel");
            panel.localPosition = new Vector3(0f, 0f, 0.002f);
            panel.localScale = new Vector3(Width, h, 1f);
            float top = h * 0.5f;
            string title = _pedalPage ? "RUDDER PEDALS" + (Calibrating ? "  -  CALIBRATING" : "")
                         : OnLanding ? "AERO PLAYGROUND" : "AERO PLAYGROUND  -  PAUSED";
            if (_title.text != title) _title.text = title;
            Place(_title, top - RowHeight * 0.75f);
            for (int i = 0; i < _shown.Count; i++)
            {
                Row r = _shown[i];
                string s = (r.Cycle != null ? "<  " : "    ") + r.Label() + (r.Cycle != null ? "  >" : "");
                if (r.Text.text != s) r.Text.text = s;
                Place(r.Text, top - RowHeight * (i + 1.75f));
            }
            // The font has no ☰, so the button is named in words. On the landing page the line says how to get
            // back here from the air, which is the thing Ty could not find on his first flight.
            string hint = XrHeadset.HandsOnly ? "point and pinch  -  hold a left pinch to close"
                        : OnLanding ? "trigger / A selects   < > changes   in flight: MENU tab or left MENU button"
                        : "point + trigger / A   stick < > changes   B or MENU closes";
            if (_hint.text != hint) _hint.text = hint;
            Place(_hint, -top + RowHeight * 0.55f);
            _highlight.localPosition = new Vector3(0f, top - RowHeight * (_sel + 1.75f), 0.001f);
            _highlight.localScale = new Vector3(Width - 0.02f, RowHeight * 0.9f, 1f);
        }

        private void Place(TextMesh t, float y) => t.transform.localPosition = new Vector3(-Width * 0.5f + 0.03f, y, 0f);

        // ---- input ----

        private void HandleInput()
        {
            QuestControllerFrame f = XrControllers.Read(out _);
            float dt = Time.unscaledDeltaTime;

            // Pointing: the right hand if it is there, else the left.
            int pointed = -1;
            bool aimed = Aim(false, out Vector3 o, out Vector3 d) || Aim(true, out o, out d);
            if (aimed && HitRow(o, d, out pointed, out Vector3 hit))
            {
                _laser.enabled = true;
                _laser.SetPosition(0, o); _laser.SetPosition(1, hit);
                if (pointed >= 0) _sel = pointed;
            }
            else if (aimed)
            {
                _laser.enabled = true;
                _laser.SetPosition(0, o); _laser.SetPosition(1, o + d * 1.5f);
            }
            else _laser.enabled = false;

            // Thumbsticks: up / down moves the highlight, left / right changes a value (with key repeat).
            double sy = System.Math.Abs(f.RightY) > System.Math.Abs(f.LeftY) ? f.RightY : f.LeftY;
            double sx = System.Math.Abs(f.RightX) > System.Math.Abs(f.LeftX) ? f.RightX : f.LeftX;
            if (Repeat(System.Math.Abs(sy) > 0.6 && System.Math.Abs(sy) > System.Math.Abs(sx), ref _navHeld, dt))
                _sel = Mathf.Clamp(_sel + (sy > 0 ? -1 : 1), 0, _shown.Count - 1);
            if (Repeat(System.Math.Abs(sx) > 0.6 && System.Math.Abs(sx) >= System.Math.Abs(sy), ref _cycleHeld, dt) && _sel < _shown.Count)
                _shown[_sel].Cycle?.Invoke(sx > 0 ? 1 : -1);

            bool trig = f.RightTrigger > 0.6 || f.LeftTrigger > 0.6;
            bool pinch = f.HandsOnly && (f.RightPinch > 0.8 || f.LeftPinch > 0.8);
            bool select = (trig && !_prevTrig) || (f.A && !_prevA) || (f.X && !_prevX) || (pinch && !_prevPinch);
            bool back = (f.B && !_prevB) || (f.Y && !_prevY);
            _prevTrig = trig; _prevA = f.A; _prevX = f.X; _prevB = f.B; _prevY = f.Y; _prevPinch = pinch;

            if (select && _sel < _shown.Count) _shown[_sel].Activate?.Invoke();
            else if (back && _pedalPage) { _pedalPage = false; _sel = 0; }   // B / Y: up a page first
            else if (back) Close(force: false, f.B && !f.Y ? "B" : "Y");
        }

        private static bool Repeat(bool held, ref float t, float dt)
        {
            if (!held) { t = 0f; return false; }
            bool fire = t == 0f;
            t += dt;
            if (t > 0.45f) { t -= 0.18f; fire = true; }
            return fire;
        }

        private bool Aim(bool left, out Vector3 origin, out Vector3 dir)
        {
            origin = default; dir = Vector3.forward;
            if (!XrControllers.TryPointer(left, out Vector3 p, out Quaternion q)) return false;
            if (!_rig.TrackingToWorld(p, q, out origin, out Quaternion wq)) return false;
            dir = wq * Vector3.forward;
            return true;
        }

        private bool HitRow(Vector3 o, Vector3 d, out int row, out Vector3 hit)
        {
            row = -1; hit = default;
            Transform t = _root.transform;
            Vector3 n = t.forward;
            float den = Vector3.Dot(d, n);
            if (Mathf.Abs(den) < 1e-4f) return false;
            float dist = Vector3.Dot(t.position - o, n) / den;
            if (dist <= 0f || dist > 5f) return false;
            hit = o + d * dist;
            Vector3 local = t.InverseTransformPoint(hit);
            float h = (_shown.Count + 2.6f) * RowHeight;
            if (Mathf.Abs(local.x) > Width * 0.5f || Mathf.Abs(local.y) > h * 0.5f) return false;
            float fromTop = h * 0.5f - local.y;
            int i = Mathf.FloorToInt(fromTop / RowHeight - 1.25f);
            row = i >= 0 && i < _shown.Count ? i : -1;
            return true;
        }

        // ---- actions ----

        /// <summary>Back to the landing page's rows, in the headset: the flight stops (the landing page pauses it, as on
        /// every other screen), the panel shows FLY / Aircraft / Start / Seat …, and FLY starts the new choice. No
        /// restart of the app (Ty, build a222bda: "has to quit and relaunch to change aircraft").</summary>
        private void GoToLanding()
        {
            if (Landing == null) return;
            Landing.Open();
            _pedalPage = false;
            _sel = 0;
            _confirmUntil = 0f;
            Debug.Log("[QuestInput] menu: CHANGE AIRCRAFT - back to the landing page");
        }

        private void FlyNow()
        {
            SessionSettings.ChallengeId = null;     // free flight: a lesson's OnGUI cards could not be seen in here
            KeepToHeadsetFleet("FLY");
            // A start that throws must not leave the pilot behind a half-closed menu with no cockpit: say why, and
            // still put him in the seat (the 2026-11-02 Quest build threw here on every FLY — StreamingFiles).
            try { if (Landing != null) Landing.Fly(); }
            catch (System.Exception e) { Debug.LogError($"[Quest] FLY: the flight did not start: {e}"); }
            Close(force: true, "FLY");
            Debug.Log($"[QuestInput] FLY: {SessionSettings.AircraftId}, {StartName()}");
            if (Chase != null) Chase.SetView(ChaseCamera.View.Cockpit);
            XrHeadset.Recentre();
            _rig?.ReportAfterFly();
            StartTipIfDue();
            if (XrHeadset.PowerHintFlights < QuestCockpit.PowerHintFlights)
            {
                XrHeadset.PowerHintFlights = XrHeadset.PowerHintFlights + 1;
                _powerHintUntil = Time.unscaledTime + (float)QuestCockpit.PowerHintSec;
            }
        }

        // ---- the power readout ----

        /// <summary>Every frame: show the lever's setting while it moves (and the how-to line on the first flights),
        /// riding the cockpit like the tip, low on the left where the throttle hand is. In the cockpit view only, and
        /// not with the menu open.</summary>
        private void TickPower()
        {
            Transform anchor = _rig != null ? _rig.HudAnchor : null;
            FlyingGame.Sim.Aircraft ac = Driver != null && Driver.Sim != null ? Driver.Sim.Aircraft : null;
            bool cockpit = !_open && !SessionSettings.MenuOpen && anchor != null && ac != null && ChaseCamera.InCockpit;
            if (!cockpit)
            {
                if (_power != null && _power.gameObject.activeSelf) _power.gameObject.SetActive(false);
                _powerLast = double.NaN;
                return;
            }
            bool glider = ac.Config.Propulsion == null;
            double lever = glider ? ac.CurrentDeflections.SpoilerFraction : ac.Throttle01;
            float now = Time.unscaledTime;
            if (!double.IsNaN(_powerLast) && QuestCockpit.LeverMoved(_powerLast, lever)) _powerMovedAt = now;
            _powerLast = lever;
            bool hint = now < _powerHintUntil;
            float op = hint ? 1f : (float)QuestCockpit.PowerOpacity(now - _powerMovedAt);
            if (op <= 0f)
            {
                if (_power != null && _power.gameObject.activeSelf) _power.gameObject.SetActive(false);
                return;
            }
            if (_power == null) BuildPower();
            if (!_power.gameObject.activeSelf) _power.gameObject.SetActive(true);
            if (_power.parent != anchor) _power.SetParent(anchor, false);
            // Low and to the left, 0.55 m out: a glance down at the left hand, never over the horizon.
            var at = new Vector3(-0.2f, -0.3f, 0.55f);
            _power.localPosition = at;
            _power.localRotation = Quaternion.LookRotation(at.normalized, Vector3.up);
            // A new string only when the whole-percent figure changes.
            string text = QuestCockpit.PowerLabel(lever, glider);
            if (hint) text += "\n" + QuestCockpit.PowerHint;
            if (text != _powerShown) { _powerShown = text; _powerText.text = text; }
            Transform panel = _power.GetChild(0);
            panel.localScale = new Vector3(hint ? 0.62f : 0.2f, hint ? 0.11f : 0.045f, 1f);
            Color tc = _powerText.color; tc.a = op; _powerText.color = tc;
            Color pc = _powerPanelMat.color; pc.a = 0.85f * op; _powerPanelMat.color = pc;
        }

        private void BuildPower()
        {
            if (!_built) { Build(); if (!_open) _root.SetActive(false); }
            var root = new GameObject("VR Power Readout");
            _power = root.transform;
            _powerPanelMat = Solid(_panelMat.shader, new Color(0.05f, 0.08f, 0.12f, 0.85f), 4000);
            Transform panel = Quad("Panel", _powerPanelMat);
            panel.SetParent(_power, false);
            panel.localPosition = new Vector3(0f, 0f, 0.002f);
            var go = new GameObject("Text");
            go.transform.SetParent(_power, false);
            _powerText = go.AddComponent<TextMesh>();
            _powerText.font = UiFont.Get();
            _powerText.fontSize = 64;
            _powerText.characterSize = CharHeight * 0.75f * 10f / _powerText.fontSize;
            _powerText.anchor = TextAnchor.MiddleCenter;
            _powerText.alignment = TextAlignment.Center;
            _powerText.color = new Color(0.55f, 1f, 0.6f, 1f);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = _textMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            _powerShown = "";
        }

        // ---- the "press MENU" tip ----

        /// <summary>On the first <see cref="QuestCockpit.TipFlights"/> flights, until he has opened the menu
        /// himself: a small card low in the cockpit for <see cref="QuestCockpit.TipSeconds"/>.</summary>
        private void StartTipIfDue()
        {
            if (XrControllers.Read(out _).HandsOnly) return;   // hands have their own gesture, shown on the menu
            if (!QuestCockpit.ShowTip(XrHeadset.MenuTipFlights, XrHeadset.MenuFound)) return;
            XrHeadset.MenuTipFlights = XrHeadset.MenuTipFlights + 1;
            _tipShownAt = Time.unscaledTime;
        }

        private void EndTip()
        {
            _tipShownAt = -1f;
            if (_tip != null) _tip.gameObject.SetActive(false);
        }

        /// <summary>Ride the cockpit: parented to the rig's HUD anchor (the seat's eye, the aeroplane's axes), 0.7 m
        /// ahead and 0.2 m down — below the windscreen line, over the panel, so it is read with a glance down and
        /// never covers the horizon. Only in the cockpit view; outside it would float at the aeroplane.</summary>
        private void TickTip()
        {
            if (_tipShownAt < 0f) return;
            float op = (float)QuestCockpit.TipOpacity(Time.unscaledTime - _tipShownAt);
            if (op <= 0f) { EndTip(); return; }
            Transform anchor = _rig != null ? _rig.HudAnchor : null;
            bool show = !_open && anchor != null && ChaseCamera.InCockpit;
            if (show && _tip == null) BuildTip();
            if (_tip == null) return;
            _tip.gameObject.SetActive(show);
            if (!show) return;
            if (_tip.parent != anchor) _tip.SetParent(anchor, false);
            // A little right of centre, so it never covers the MENU tab it points to (QuestMenuTabView.At, low left).
            var at = new Vector3(0.10f, -0.2f, 0.7f);
            _tip.localPosition = at;
            _tip.localRotation = Quaternion.LookRotation(at.normalized, Vector3.up);   // square on to the eye
            Color tc = _tipText.color; tc.a = op; _tipText.color = tc;
            Color pc = _tipPanelMat.color; pc.a = 0.85f * op; _tipPanelMat.color = pc;
        }

        private void BuildTip()
        {
            if (!_built) { Build(); if (!_open) _root.SetActive(false); }
            var root = new GameObject("VR Menu Tip");
            _tip = root.transform;
            _tipPanelMat = Solid(_panelMat.shader, new Color(0.05f, 0.08f, 0.12f, 0.85f), 4000);
            Transform panel = Quad("Panel", _tipPanelMat);
            panel.SetParent(_tip, false);
            panel.localPosition = new Vector3(0f, 0f, 0.002f);
            panel.localScale = new Vector3(0.50f, 0.10f, 1f);
            var go = new GameObject("Text");
            go.transform.SetParent(_tip, false);
            _tipText = go.AddComponent<TextMesh>();
            _tipText.font = UiFont.Get();
            _tipText.fontSize = 64;
            _tipText.characterSize = CharHeight * 0.8f * 10f / _tipText.fontSize;
            _tipText.anchor = TextAnchor.MiddleCenter;
            _tipText.alignment = TextAlignment.Center;
            _tipText.color = new Color(0.92f, 0.95f, 1f, 1f);
            _tipText.text = QuestCockpit.MenuTip;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = _textMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        private static string AircraftName(string id)
        {
            foreach (var f in SessionSettings.Fleet) if (f.id == id) return f.name;
            return id;
        }

        /// <summary>The headset's fleet: only the types with our own cockpit and a checked eye (FlyingGame.Core.Vr.QuestFleet).</summary>
        private static readonly (string id, string name)[] HeadsetFleet = FlyingGame.Core.Vr.QuestFleet.Filter(SessionSettings.Fleet);

        /// <summary>Put the choice on a type the headset lists (a saved J-3 from the phone, say), and say so.</summary>
        internal static void KeepToHeadsetFleet(string why)
        {
            string was = SessionSettings.AircraftId, now = FlyingGame.Core.Vr.QuestFleet.Resolve(was);
            if (now == was) return;
            FlyingGame.Core.Vr.QuestFleet.HiddenBecause.TryGetValue(was ?? "", out string reason);
            Debug.Log($"[Quest] aircraft '{was}' is not offered in a headset ({reason ?? "no cockpit of our own with a checked eye"}): {now} instead ({why})");
            SessionSettings.AircraftId = now;
        }

        /// <summary>The expo the three axes fly with now: the pilot's setting, else the headset default.</summary>
        private static float StickExpo() => SessionSettings.FeelExpo[0] >= 0f ? SessionSettings.FeelExpo[0] : (float)FlyingGame.Core.Vr.QuestStickFeel.DefaultExpo;

        /// <summary>Set aileron, elevator and rudder expo together (&lt; 0 = back to the default) and fly it at once.</summary>
        private void SetStickExpo(float expo)
        {
            for (int i = 0; i < 3; i++) SessionSettings.FeelExpo[i] = expo;
            SessionSettings.SaveFeel();
            SessionSettings.ReapplyFeel(Driver?.Sim?.Aircraft?.Config);
            Debug.Log($"[QuestInput] stick expo {(expo < 0f ? $"default ({FlyingGame.Core.Vr.QuestStickFeel.DefaultExpo * 100:0} %)" : $"{expo * 100:0} %")}");
        }

        private static void CycleAircraft(int step)
        {
            var fleet = HeadsetFleet;
            int i = System.Array.FindIndex(fleet, f => f.id == SessionSettings.AircraftId);
            SessionSettings.AircraftId = fleet[((i < 0 ? 0 : i) + step + fleet.Length) % fleet.Length].id;
        }

        private FlyingGame.Bridge.Net.NetSession _net;
        private FlyingGame.Bridge.Net.NetSession Session => _net != null ? _net : Driver != null ? (_net = Driver.GetComponent<FlyingGame.Bridge.Net.NetSession>()) : null;

        /// <summary>The headset offers Solo and the LAN room only: the online rooms are the relay's, chosen on a flat screen.</summary>
        private static string MultiplayerName() => SessionSettings.Multiplayer switch
        {
            SessionSettings.MultiplayerMode.Solo => "Solo",
            SessionSettings.MultiplayerMode.Lan => "Same network (Wi-Fi)",
            SessionSettings.MultiplayerMode.FreeForAll => "Free-for-all (online)",
            _ => "Room by code (online)",
        };

        private static void CycleMultiplayer(int step)
        {
            SessionSettings.Multiplayer = SessionSettings.Multiplayer == SessionSettings.MultiplayerMode.Lan
                ? SessionSettings.MultiplayerMode.Solo : SessionSettings.MultiplayerMode.Lan;
            Debug.Log($"[QuestInput] menu: Multiplayer {MultiplayerName()}");
        }

        private static string StartName()
        {
            foreach (var s in Starts) if (s.mode == SessionSettings.StartMode) return s.name;
            return SessionSettings.StartMode.ToString();
        }

        private static void CycleStart(int step)
        {
            int i = System.Array.FindIndex(Starts, s => s.mode == SessionSettings.StartMode);
            SessionSettings.StartMode = Starts[((i < 0 ? 0 : i) + step + Starts.Length) % Starts.Length].mode;
        }

        /// <summary>The aeroplane the Seat row is about: on the landing page the one FLY will spawn (the driver
        /// still holds whatever it loaded at start, the 2-33), in flight the one being flown.</summary>
        private string SeatAircraftId => OnLanding || Driver == null ? SessionSettings.AircraftId : Driver.AircraftId;

        /// <summary>That aeroplane's authored seats a headset may use (QuestFleet.SeatsOnly: the J-3 is the rear
        /// only): the loaded config's when it is the one flying, else read once from its config and kept.</summary>
        private IReadOnlyList<StationConfig> SeatsOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return System.Array.Empty<StationConfig>();
            if (Driver != null && Driver.AircraftId == id && Driver.Sim != null && Cockpit != null)
                return FlyingGame.Core.Vr.QuestFleet.Seats(id, Cockpit.Seats, s => s.Id);
            if (!_seatsByType.TryGetValue(id, out List<StationConfig> seats))
            {
                try { seats = UnityAircraftConfigLoader.LoadFromStreamingAssets(id)?.Loading?.Seats; }
                catch (System.Exception e) { Debug.LogWarning($"[Quest] seats of {id} not read: {e.Message}"); }
                _seatsByType[id] = seats = FlyingGame.Core.Vr.QuestFleet.Seats(id, seats, s => s.Id);
            }
            return seats;
        }

        /// <summary>Choose the next / previous seat FOR THAT TYPE, by ID. CockpitView follows the choice (now if
        /// that type is flying, on FLY otherwise) and QuestRig recentres the head at the new eye.</summary>
        private void CycleSeat(int step)
        {
            string id = SeatAircraftId;
            var seats = SeatsOf(id);
            if (seats.Count < 2) return;
            SessionSettings.ChooseSeat(id, SeatChoice.Cycle(seats, SessionSettings.SeatIdFor(id), step));
        }

        /// <summary>The green HUD in the cockpit view (off by default in a headset). Turning it on also selects the
        /// HUD as the instrument display, since the dials are a flat overlay a headset cannot show.</summary>
        private static void ToggleHud()
        {
            XrHeadset.HudInCockpit = !XrHeadset.HudInCockpit;
            if (XrHeadset.HudInCockpit && SessionSettings.Instruments != SessionSettings.InstrumentMode.Hud)
            {
                SessionSettings.Instruments = SessionSettings.InstrumentMode.Hud;
                SessionSettings.SavePrefs();
            }
        }

        private static void ToggleFlyByHand()
        {
            XrHeadset.FlyByHand = !XrHeadset.FlyByHand;
            Debug.Log($"[QuestMenu] fly by hand {(XrHeadset.FlyByHand ? "ON" : "OFF")}");
        }

        private void CycleView(int d)
        {
            if (Chase == null) return;
            ChaseCamera.View next = QuestViews.Next(Chase.CurrentView, d);
            Chase.SetView(next);
            Debug.Log($"[QuestInput] menu: view → {QuestViews.Label(next)}");
        }

        private string HandleName() => Controls == null ? "" : Controls.MenuHandleLabel;

        /// <summary>Bail out / BRS / eject asks twice: the first press arms it for 3 s.</summary>
        private void PullHandle()
        {
            if (Time.unscaledTime >= _confirmUntil) { _confirmUntil = Time.unscaledTime + 3f; return; }
            _confirmUntil = 0f;
            Controls?.MenuPullHandle();
            Close(force: false);
        }

        // ---- building blocks ----

        private static Material Solid(Shader sh, Color c, int queue) =>
            new Material(sh) { mainTexture = Texture2D.whiteTexture, color = c, renderQueue = queue };

        private static Transform Quad(string name, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            if (Application.isPlaying) Object.Destroy(go.GetComponent<Collider>());
            else Object.DestroyImmediate(go.GetComponent<Collider>());
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            // A quad is seen from its -z side, and the pilot sits on the panel's -z side looking along its forward:
            // no turn needed (and the text shader does not cull, so it would show either way).
            return go.transform;
        }

        private TextMesh Text(string name, string s, float charHeight)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            // Like the quad: a TextMesh reads left to right from its -z side, which is where the pilot is.
            var t = go.AddComponent<TextMesh>();
            t.font = UiFont.Get();
            t.fontSize = 64;
            // A TextMesh line is about fontSize x characterSize / 10 world units tall.
            t.characterSize = charHeight * 10f / t.fontSize;
            t.anchor = TextAnchor.MiddleLeft;
            t.alignment = TextAlignment.Left;
            t.color = new Color(0.92f, 0.95f, 1f, 1f);
            t.text = s;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = _textMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return t;
        }
    }
}
