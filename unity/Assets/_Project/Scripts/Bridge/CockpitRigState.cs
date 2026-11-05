using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.Instruments;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Everything a cockpit shows, gathered once a frame into the <see cref="RigInputs"/> a model's own rig is
    /// driven from (<see cref="AirframeRig"/>). The Extra's panel asks for 33 values where the 2-33's asked for
    /// 11, and they come from three different places:
    ///
    ///  - THE SIM, directly: airspeed, altitude, rpm, throttle, the toe brakes, load factor, heading, sideslip.
    ///    These are the aeroplane's own state and nothing here invents them.
    ///  - THE SIM, differentiated or lagged: vertical speed, rate of turn, the total-energy vario, the damped
    ///    compass card, the accelerometer's tell-tales. Still the sim's numbers, read the way an instrument
    ///    reads them.
    ///  - NOWHERE: the sim has no oil, no cylinder heads, no manifold and no fuel quantity. Those four gauges
    ///    are a PRESENTATION model built from throttle, rpm and altitude — a needle that moves plausibly rather
    ///    than a needle stuck on its peg. They feed the panel and nothing else; no value here ever reaches the
    ///    flight model. They are marked (P) below, and they are the first thing to delete if the engine ever
    ///    grows a real thermal model.
    ///
    /// Avionics and the canopy are STATE, not readings: the player sets them and they stay set, so they live
    /// here across frames and are reset with the aeroplane.
    /// </summary>
    public sealed class CockpitRigState
    {
        // ---- lagged / integrated readings --------------------------------------------------------------
        private double _energyHeightM = double.NaN, _varioMs;
        private float _headingDeg = float.NaN, _headingRawDeg = float.NaN, _turnRateDps, _vsiFpm;
        private double _gMax, _gMin;

        // ---- the presentation engine gauges (P) --------------------------------------------------------
        private double _oilTempF = 180.0, _chtF = 320.0;
        /// <summary>The fuel computer's count, US gallons burned since the aeroplane was reset.</summary>
        private double _fuelUsedGal;

        // ---- avionics, as the player left them ---------------------------------------------------------
        /// <summary>COM active / standby in MHz, and the volume. Defaults: 122.800 (unicom) on 121.500.</summary>
        public double ComActiveMhz = 122.800, ComStandbyMhz = 121.500, ComVolume = 0.6;
        /// <summary>Transponder code as a four-digit octal number, and the mode: 0 OFF, 1 SBY, 2 ON, 3 ALT.</summary>
        public int Squawk = 1200, XpdrMode = 1;
        /// <summary>How many clicks the transponder's knob has been turned (it free-runs; the rig extrapolates).</summary>
        public double XpdrKnobClicks;

        // ---- the canopy --------------------------------------------------------------------------------
        /// <summary>0 shut .. 1 fully open. Latched: it stays where the player left it.</summary>
        public float CanopyOpen01;
        /// <summary>true = the red levers are down and the canopy cannot be opened.</summary>
        public bool CanopyLocked = true;
        /// <summary>0..1 pull of the emergency release handle; the canopy goes at 1.</summary>
        public float CanopyPull01;
        /// <summary>Set once the canopy has left: the aeroplane stays canopy-less until it is reset.</summary>
        public bool CanopyGone;
        /// <summary>The latch a headset hand works (the Pitts' sliding ball, the Extra's squeeze handles), or null for
        /// a type with none. Set by VrCockpitHands; reset with the aeroplane; it drives the rig's `canopy_latch` and
        /// <see cref="CanopyLocked"/>. Phase 2's canopy opening asks <see cref="CanopyLocked"/>.</summary>
        public FlyingGame.Core.Vr.ICanopyLatch HandLatch;
        /// <summary>THE HANDS-ON BAIL-OUT (headset, docs/vr-cockpit.md phase 2), set by VrCockpitHands for a type that has
        /// them and reset with the aeroplane: the canopy on its hinge in the airload (while set it, not the menu's
        /// open/shut command, decides <see cref="CanopyOpen01"/>, and its drag goes into the flight model), the seat's
        /// belts, the headset cord and the climb out.</summary>
        public FlyingGame.Core.Vr.CanopyDynamics HandCanopy;
        /// <summary>The canopy jettison a headset hand pulls (the Pitts' red knob; the Extra's CanopyRelease where its model
        /// has one), or null. One-way; reset with the aeroplane. It drives the rig's `canopy_jettison` with the menu's
        /// red-handle pull, but NOT AirframeVisual's instant jettison: the canopy goes when CanopyDynamics says so.</summary>
        public FlyingGame.Core.Vr.JettisonPull HandJettison;
        public FlyingGame.Core.Vr.SeatHarness Harness;
        public FlyingGame.Core.Vr.HeadsetCord Cord;
        public readonly FlyingGame.Core.Vr.ClimbOut Climb = new();
        /// <summary>Where the open canopy's drag acts, the config's body frame (VrCockpitHands sets it).</summary>
        public FlyingGame.Core.MathTypes.Vec3 CanopyDragPointBody;

        /// <summary>How fast the canopy swings when the player asks for it, and how fast the release handle
        /// moves under a hand and springs back after.</summary>
        private const float CanopySec = 1.6f, PullSec = 0.12f, SpringBackSec = 0.35f;
        private float _canopyTarget;
        private float _handOnRelease;
        /// <summary>Airspeed last seen, so the canopy cannot be opened at a speed that would tear it off.</summary>
        private float _iasKt;
        /// <summary>Above this the canopy stays shut, and an open one is pulled shut: a one-piece acro canopy
        /// is a taxi-and-ramp item (E — the POH gives no figure, and this is the speed at which leaving it open
        /// stops looking like a choice).</summary>
        public const float CanopyMaxOpenKt = 60f;

        /// <summary>Open or shut the canopy, if the locks allow it and the aeroplane is slow enough. Ignored
        /// once it has been jettisoned. Asking for it OPEN unlocks it: the levers are part of the same action,
        /// the way a hand on the rail is.</summary>
        public void CommandCanopy(float open01)
        {
            if (CanopyGone) return;
            if (open01 > 0f && _iasKt > CanopyMaxOpenKt) return;
            if (open01 > 0f) CanopyLocked = false;
            _canopyTarget = Mathf.Clamp01(open01);
        }

        /// <summary>How open the cockpit is to the airflow, 0 sealed .. 1 no canopy (Core Vr.CockpitWind: the wind noise).</summary>
        public double CockpitExposure01 => HandCanopy != null ? HandCanopy.Exposure01 : CanopyGone ? 1.0 : 0.8 * CanopyOpen01;

        /// <summary>True where the canopy can be worked at all: there is one, and it is not jettisoned.</summary>
        public bool CanopyUsable => !CanopyGone;

        /// <summary>The red lock levers. Locking swings the canopy shut on its way; unlocking only frees it.</summary>
        public void SetCanopyLock(bool locked)
        {
            if (CanopyGone) return;
            CanopyLocked = locked;
            if (locked) _canopyTarget = 0f;
        }

        /// <summary>A hand on the emergency release: the handle pulls, and the canopy goes. Called by the
        /// bailout; the handle springs back on its own afterwards.</summary>
        public void PullCanopyRelease() => _handOnRelease = PullSec;

        // ---- the 172's own panel (FlyingGame.Core.Instruments; docs/C172-INSTRUMENTS.md) ------------------
        /// <summary>The config id whose panel is the 1970s 172's vacuum/gyro/radio stack.</summary>
        public const string C172Id = "c172-like";
        private C172Panel _c172;
        /// <summary>The frame the 172 panel last stepped on. AirframeVisual.LateUpdate fills the rig TWICE a frame
        /// (two lanes each added the SetRig call and the merge kept both — logged, not changed, because the
        /// other types' lagged needles are tuned on it); the gyros must integrate each frame's time once.</summary>
        private int _c172Frame = -1;
        /// <summary>The 172 panel being flown, or null for any other aeroplane. Static so the RADIOS and OPTIONS
        /// panels can reach the knobs and the failure hooks, the way <see cref="NavRadios.Freq"/> already is.</summary>
        public static C172Panel LiveC172 { get; private set; }
        /// <summary>Knob turns asked for by the UI since the last frame, degrees: the DG's (push and turn) and the ADF card's.</summary>
        public static double PendingDgKnobDeg, PendingAdfCardDeg;
        /// <summary>Set the DG to the compass on the next frame (the pilot's push-turn-until-they-agree).</summary>
        public static bool PendingDgToCompass;

        /// <summary>A fresh aeroplane: the canopy is back on, the gauges are warm and the tell-tales are clear.</summary>
        public void Reset()
        {
            if (LiveC172 == _c172) LiveC172 = null;
            _c172 = null;
            _energyHeightM = double.NaN; _varioMs = 0.0;
            _headingDeg = _headingRawDeg = float.NaN; _turnRateDps = 0f; _vsiFpm = 0f;
            _gMax = _gMin = 0.0;
            _oilTempF = 180.0; _chtF = 320.0; _fuelUsedGal = 0.0;
            CanopyOpen01 = 0f; _canopyTarget = 0f; CanopyLocked = true;
            CanopyPull01 = 0f; _handOnRelease = 0f; CanopyGone = false;
            HandLatch?.Reset();
            HandCanopy?.Reset();
            HandJettison?.Reset();
            Harness?.Reset();
            Cord?.Reset();
            Climb.Reset();
        }

        /// <summary>Clear the accelerometer's max / min tell-tales (the knob on a real one).</summary>
        public void ResetGTellTales() { _gMax = _gMin = 0.0; }

        /// <summary>Fill in everything the panel shows. <paramref name="r"/> arrives with the control positions
        /// already set from the aero model's own deflections (RigInputs.FromDeflections), which is the half that
        /// must not be guessed at.</summary>
        public void Fill(ref RigInputs r, FlightSimDriver driver, FlyingGame.Sim.Aircraft ac,
                         in ControlDeflections d, float dt)
        {
            if (driver == null || ac == null) return;
            AircraftConfig cfg = ac.Config;
            double alt = driver.AltitudeM, ias = driver.IasMs;

            // ---- the aeroplane's own state -----------------------------------------------------------
            r.AirspeedKias = ias * 1.9438445;
            r.AirspeedMph = ias * 2.2369363;
            r.AltitudeFt = alt * 3.2808399;
            r.GLoad = ac.LoadFactorZ;
            r.Throttle = Mathf.Clamp01((float)ac.Throttle01);
            r.Rpm = ac.EngineStopped ? 0.0 : ac.EngineRpm;
            r.SideslipDeg = driver.BetaDeg;

            // The toe brakes are the sim's own differential: left = brake x (1 - bias), right = x (1 + bias).
            double brake = System.Math.Clamp(ac.BrakeInput, 0.0, 1.0), bias = ac.BrakeBias;
            r.BrakeLeft = System.Math.Clamp(brake * (1.0 - bias), 0.0, 1.0);
            r.BrakeRight = System.Math.Clamp(brake * (1.0 + bias), 0.0, 1.0);

            // The verniers, the key and the switches. The sim flies with the engine running, the fuel on and
            // both magnetos live, so that is where the cockpit's levers sit; a type without an engine shows
            // the key off rather than pretending.
            bool engine = cfg != null && cfg.Propulsion != null && cfg.Propulsion.MaxPowerW > 0.0;
            // The prop and mixture levers are the sim's own now (Aircraft.PropLever / MixtureLever, Ty 2026-11-04): drawn
            // where the pilot left them, whatever the engine is doing.
            r.PropLever = ac.HasPropLever ? ac.PropLever : 1.0;
            r.Mixture = engine ? ac.MixtureLever : 0.0;
            r.FuelSelector = 1.0;
            r.Magneto = engine && !ac.EngineStopped ? 3.0 : 0.0;
            r.Master = 1.0;

            // ---- read the way an instrument reads ----------------------------------------------------
            // Vertical speed, lagged like the leak on a real VSI (2 s; a certificated one is nearer 6, which
            // on a cockpit needle reads as broken).
            float climbFpm = driver.WorldVelocityUnity.y * 196.8504f;
            _vsiFpm = dt > 1e-4f ? _vsiFpm + (climbFpm - _vsiFpm) * (1f - Mathf.Exp(-dt / 2f)) : climbFpm;
            r.VsiFpm = _vsiFpm;

            // Total-energy variometer (the gliders'): height plus kinetic height, lagged about a second, so a
            // pull-up reads as the trade it is rather than as lift.
            double he = alt + ias * ias / (2.0 * 9.80665);
            if (dt > 1e-4f && !double.IsNaN(_energyHeightM))
                _varioMs += ((he - _energyHeightM) / dt - _varioMs) * (1.0 - System.Math.Exp(-dt / 1.0));
            _energyHeightM = he;
            r.VarioKt = _varioMs * 1.9438445;

            // Magnetic heading. Variation in this world is zero (ChartPanel says so to the pilot), so the
            // compass reads the sim's own heading: x is north, y east. The card is damped, not dead.
            var fwd = ac.State.Attitude.Rotate(new FlyingGame.Core.MathTypes.Vec3(1, 0, 0));
            float hdg = Mathf.Repeat(Mathf.Atan2((float)fwd.Y, (float)fwd.X) * Mathf.Rad2Deg, 360f);
            if (float.IsNaN(_headingDeg)) { _headingDeg = _headingRawDeg = hdg; }
            else
            {
                // Rate of turn, from the heading the aeroplane actually has (the card's damping must not show
                // up in the turn needle). Lightly filtered: a raw per-frame difference is all noise.
                float turn = dt > 1e-4f ? Mathf.DeltaAngle(_headingRawDeg, hdg) / dt : _turnRateDps;
                _turnRateDps += (turn - _turnRateDps) * (1f - Mathf.Exp(-dt / 0.35f));
                _headingRawDeg = hdg;
                // The card itself: heavy, floating in fluid, and it overshoots a little before it settles.
                _headingDeg += Mathf.DeltaAngle(_headingDeg, hdg) * (1f - Mathf.Exp(-dt / 0.22f));
                _headingDeg = Mathf.Repeat(_headingDeg, 360f);
            }
            r.HeadingDeg = _headingDeg;
            r.TurnRateDps = _turnRateDps;

            // Bank and pitch, the ZYX Euler angles of the body in NED: bank from the body's right (y) and down (z)
            // axes, pitch from the nose's climb. Exact at any pitch (the cos θ cancels in the atan2).
            var rightAx = ac.State.Attitude.Rotate(new FlyingGame.Core.MathTypes.Vec3(0, 1, 0));
            var downAx = ac.State.Attitude.Rotate(new FlyingGame.Core.MathTypes.Vec3(0, 0, 1));
            r.BankDeg = System.Math.Atan2(rightAx.Z, downAx.Z) * (180.0 / System.Math.PI);
            r.PitchDeg = System.Math.Asin(System.Math.Clamp(-fwd.Z, -1.0, 1.0)) * (180.0 / System.Math.PI);

            // The inclinometer ball. In steady flight the ball IS a sideslip indicator: the side force opposes
            // β, and the ball hangs away from the force, so a relative wind from the right puts it to the
            // right. Full deflection at 10 deg of slip (E — a 2-1/4 in tube is about two ball widths).
            r.SlipBall = System.Math.Clamp(driver.BetaDeg / 10.0, -1.0, 1.0);

            // The accelerometer's tell-tales: they follow the needle out and stay.
            if (r.GLoad > _gMax) _gMax = r.GLoad;
            if (r.GLoad < _gMin) _gMin = r.GLoad;
            r.GMaxSeen = _gMax; r.GMinSeen = _gMin;

            // ---- the presentation gauges (P): no sim behind them -------------------------------------
            FillEngineGauges(ref r, ac, alt, dt);

            // ---- the 172's instruments: modelled, not presented ----------------------------------------
            if (cfg != null && cfg.Id == C172Id) FillC172(ref r, ac, dt);
            else if (_c172 != null) { if (LiveC172 == _c172) LiveC172 = null; _c172 = null; }

            // ---- avionics and canopy, as the player left them ----------------------------------------
            r.ComFreqMhz = ComActiveMhz; r.ComStandbyMhz = ComStandbyMhz;
            r.ComVolume = System.Math.Clamp(ComVolume, 0.0, 1.0);
            r.XpdrSquawk = Squawk; r.XpdrMode = XpdrMode; r.XpdrKnobClicks = XpdrKnobClicks;

            _iasKt = (float)r.AirspeedKias;
            StepCanopy(dt);
            r.CanopyOpen = CanopyOpen01;
            r.CanopyLock = CanopyLocked ? 1.0 : 0.0;
            r.CanopyJettison = System.Math.Max(CanopyPull01, HandJettison?.Pulled01 ?? 0.0);
            r.CanopyLatch = FlyingGame.Core.RigInputs.CanopyLatchFor(CanopyLocked, HandLatch != null ? HandLatch.Latch01 : null);
            r.HarnessLatch = Harness != null ? Harness.Harness01 : 0.0;
            // An open canopy's drag, or the open cockpit's once it has gone, into the flight model (Aircraft.ExtraDragAreaM2);
            // nothing unless the headset's hands-on canopy is in use.
            ac.ExtraDragAreaM2 = HandCanopy != null ? HandCanopy.DragAreaM2 : 0.0;
            ac.ExtraDragPointBody = CanopyDragPointBody;
        }

        /// <summary>Shared cockpit/HUD presentation pressure; the sim has no independent manifold state.</summary>
        public static double ManifoldPressureInHg(FlyingGame.Sim.Aircraft ac, double altM)
        {
            double ambient = Atmosphere.PressureAtAltitude(altM) / 3386.389;
            // A dead engine pumps nothing: the gauge reads the air outside, whatever the throttle says.
            return ac.EngineRunning && ac.EngineRpm > 50.0 ? ambient * (0.33 + 0.62 * ac.Throttle01) : ambient;
        }

        /// <summary>
        /// The 172's vacuum system, gyros, compass, turn coordinator and radios, stepped on the sim's own state.
        /// Tuning comes from the RADIOS panel (<see cref="NavRadios"/>); the bus is 14.2 V on the alternator with
        /// the engine turning and 12.4 V on the battery without it. The failure hooks live on
        /// <see cref="C172Panel.Failures"/>.
        /// </summary>
        private void FillC172(ref RigInputs r, FlyingGame.Sim.Aircraft ac, float dt)
        {
            var s = ac.State;
            bool turning = r.Rpm > 50.0;
            var x = new PanelInputs
            {
                Attitude = s.Attitude,
                RatesBody = s.Rates,
                SpecificForceBody = ac.SpecificForceBody,
                PositionWorld = s.Position,
                EngineRpm = r.Rpm,
                AmbientPressurePa = Atmosphere.PressureAt(s.Position),
                AmbientTempK = Atmosphere.TemperatureAtPosition(s.Position),
                BusVolts = r.Master > 0.5 ? (turning ? 14.2 : 12.4) : 0.0,
            };
            WorldTerrain t = WorldTerrain.Active;
            bool fresh = _c172 == null;
            _c172 ??= new C172Panel();
            LiveC172 = _c172;
            _c172.Nav1.FreqMhz = NavRadios.Freq[0]; _c172.Nav1.ObsDeg = NavRadios.Obs[0];
            _c172.Nav2.FreqMhz = NavRadios.Freq[1]; _c172.Nav2.ObsDeg = NavRadios.Obs[1];
            _c172.Adf.FreqKhz = NavRadios.AdfKhz;
            if (fresh)
            {
                // A flight starts with the engine running in every mode this game has, so the gyros have had
                // their five minutes and the DG has been set; a cold aeroplane starts with them stopped.
                _c172.Start(x, gyrosRunning: turning, t);
                PendingDgKnobDeg = PendingAdfCardDeg = 0; PendingDgToCompass = false;
            }
            else if (dt > 1e-4f && Time.frameCount != _c172Frame) _c172.Step(x, dt, t);
            _c172Frame = Time.frameCount;

            if (PendingDgToCompass) { _c172.Dg.SetTo(s.Attitude, _c172.Compass.ReadingDeg); PendingDgToCompass = false; }
            if (PendingDgKnobDeg != 0) { _c172.Dg.TurnKnob(s.Attitude, PendingDgKnobDeg); PendingDgKnobDeg = 0; }
            if (PendingAdfCardDeg != 0) { _c172.Adf.CardDeg = Mathf.Repeat((float)(_c172.Adf.CardDeg + PendingAdfCardDeg), 360f); PendingAdfCardDeg = 0; }
            _c172.WriteRig(ref r);
        }

        /// <summary>The four gauges with no sim behind them (P), from throttle, rpm and ambient pressure. The
        /// targets are the middles of the dial markings the model is drawn with (docs/models/ea300-own-RIG.md:
        /// oil 55-95 psi green, 140-210 F green, CHT green to 435 F, MP green 10-25) so the needles sit where a
        /// healthy engine's do, and the time constants are the minutes an air-cooled six takes to get there.</summary>
        private void FillEngineGauges(ref RigInputs r, FlyingGame.Sim.Aircraft ac, double altM, float dt)
        {
            // The engine's real power, not the lever: a dead engine with the throttle open cools (Ty 2026-11-04). Oil
            // pressure still follows rpm below, so a windmilling engine keeps a little.
            double thr = ac.ShaftPower01, rpm = r.Rpm;
            bool running = rpm > 50.0;
            bool firing = running && ac.EngineRunning;

            // Manifold pressure: ambient, less the throttle plate. Wide open takes about 95 % of ambient; a
            // closed throttle holds about a third of it. inHg = Pa / 3386.389.
            r.ManifoldInHg = ManifoldPressureInHg(ac, altM);

            // Oil and cylinder-head temperature: first-order, toward what the power setting holds.
            double oilTarget = firing ? 150.0 + 60.0 * thr : 100.0;
            double chtTarget = firing ? 260.0 + 160.0 * thr : 200.0;
            if (dt > 1e-4f)
            {
                _oilTempF += (oilTarget - _oilTempF) * (1.0 - System.Math.Exp(-dt / 150.0));
                _chtF += (chtTarget - _chtF) * (1.0 - System.Math.Exp(-dt / 45.0));
            }
            r.OilTempF = _oilTempF;
            r.ChtF = _chtF;

            // Oil pressure follows rpm and thins as the oil warms: idle about 35 psi, cruise about 80.
            double maxRpm = ac.Config?.Propulsion != null && ac.Config.Propulsion.MaxRpm > 100.0 ? ac.Config.Propulsion.MaxRpm : 2700.0;
            r.OilPressPsi = running
                ? System.Math.Clamp(20.0 + 65.0 * System.Math.Min(1.0, rpm / (0.85 * maxRpm)) - 0.08 * (_oilTempF - 180.0), 0.0, 115.0)
                : 0.0;

            // Fuel: the loading's own tanks where it has fuel in them (the loading page), by tank id. The reference
            // loading carries NO fuel on most types (the open W&B decision in AGENT-LOG) and the engine runs anyway,
            // so an empty loading reads FULL rather than showing empty tanks under a running engine.
            const double KgPerUsGal = RigInputs.KgPerUsGal;       // avgas, 6.0 lb per US gallon
            AircraftLoad load = ac.Load;
            bool loaded = load != null && load.FuelKg > 1e-6;
            double Gal(string id)
            {
                AircraftLoad.Slot s = load?.Find(id);
                if (s == null) return 0.0;
                return (loaded ? s.Kg : s.MaxKg) / KgPerUsGal;
            }
            r.FuelCentreFrac = loaded && load.FuelCapacityKg > 0.0 ? load.FuelKg / load.FuelCapacityKg : 1.0;
            r.FuelWingGal = Gal("wingLeft") + Gal("wingRight");
            r.FuelAkroGal = Gal("centre");
            // The fuel computer and the three-in-one's FUEL FLOW needle: the sim's own burn (BSFC x shaft power,
            // Aircraft.FuelFlowKgPerSec), and its total.
            r.FuelFlowGph = running ? RigInputs.GphFromKgPerSec(ac.FuelFlowKgPerSec) : 0.0;
            if (dt > 1e-4f) _fuelUsedGal += r.FuelFlowGph * dt / 3600.0;
            r.FuelUsedGal = _fuelUsedGal;

            // Presentation (P), no sim behind them, aimed at the middles of the markings the shared panel's dials are
            // drawn with (CODEX-SPEC, the ammeter; to be checked against Ty's own panel): the alternator's charge,
            // EGT with power.
            r.Amps = running ? 3.0 : r.Master > 0.5 ? -4.0 : 0.0;
            r.EgtF = running ? 1200.0 + 250.0 * thr : 100.0;
        }

        /// <summary>The canopy's own motion: it swings to where it was commanded, and the emergency handle
        /// pulls under a hand then springs back.</summary>
        private void StepCanopy(float dt)
        {
            if (dt <= 0f) return;
            // Speed shuts it: leave it open and accelerate, and it comes down and locks rather than flying off.
            if (_iasKt > CanopyMaxOpenKt && _canopyTarget > 0f) { _canopyTarget = 0f; CanopyLocked = true; }
            // A hand on a headset's canopy: its own physics set CanopyOpen01 (it flips open in the airload, which the
            // 60 kt rule above would contradict), so the menu's command stands down.
            if (HandCanopy != null) { if (!CanopyGone) CanopyOpen01 = (float)HandCanopy.Open01; }
            else if (!CanopyGone)
                CanopyOpen01 = Mathf.MoveTowards(CanopyOpen01, _canopyTarget, dt / CanopySec);
            if (_handOnRelease > 0f)
            {
                _handOnRelease -= dt;
                CanopyPull01 = Mathf.MoveTowards(CanopyPull01, 1f, dt / PullSec);
            }
            else CanopyPull01 = Mathf.MoveTowards(CanopyPull01, 0f, dt / SpringBackSec);
        }
    }
}
