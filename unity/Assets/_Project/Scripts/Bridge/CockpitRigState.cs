using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
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

        /// <summary>A fresh aeroplane: the canopy is back on, the gauges are warm and the tell-tales are clear.</summary>
        public void Reset()
        {
            _energyHeightM = double.NaN; _varioMs = 0.0;
            _headingDeg = _headingRawDeg = float.NaN; _turnRateDps = 0f; _vsiFpm = 0f;
            _gMax = _gMin = 0.0;
            _oilTempF = 180.0; _chtF = 320.0;
            CanopyOpen01 = 0f; _canopyTarget = 0f; CanopyLocked = true;
            CanopyPull01 = 0f; _handOnRelease = 0f; CanopyGone = false;
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
            r.PropLever = 1.0;
            r.Mixture = engine && !ac.EngineStopped ? 1.0 : 0.0;
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

            // ---- avionics and canopy, as the player left them ----------------------------------------
            r.ComFreqMhz = ComActiveMhz; r.ComStandbyMhz = ComStandbyMhz;
            r.ComVolume = System.Math.Clamp(ComVolume, 0.0, 1.0);
            r.XpdrSquawk = Squawk; r.XpdrMode = XpdrMode; r.XpdrKnobClicks = XpdrKnobClicks;

            _iasKt = (float)r.AirspeedKias;
            StepCanopy(dt);
            r.CanopyOpen = CanopyOpen01;
            r.CanopyLock = CanopyLocked ? 1.0 : 0.0;
            r.CanopyJettison = CanopyPull01;
        }

        /// <summary>The four gauges with no sim behind them (P), from throttle, rpm and ambient pressure. The
        /// targets are the middles of the dial markings the model is drawn with (docs/models/ea300-own-RIG.md:
        /// oil 55-95 psi green, 140-210 F green, CHT green to 435 F, MP green 10-25) so the needles sit where a
        /// healthy engine's do, and the time constants are the minutes an air-cooled six takes to get there.</summary>
        private void FillEngineGauges(ref RigInputs r, FlyingGame.Sim.Aircraft ac, double altM, float dt)
        {
            double thr = r.Throttle, rpm = r.Rpm;
            bool running = rpm > 50.0;

            // Manifold pressure: ambient, less the throttle plate. Wide open takes about 95 % of ambient; a
            // closed throttle holds about a third of it. inHg = Pa / 3386.389.
            double ambientInHg = Atmosphere.PressureAtAltitude(altM) / 3386.389;
            r.ManifoldInHg = running ? ambientInHg * (0.33 + 0.62 * thr) : ambientInHg;

            // Oil and cylinder-head temperature: first-order, toward what the power setting holds.
            double oilTarget = running ? 150.0 + 60.0 * thr : 100.0;
            double chtTarget = running ? 260.0 + 160.0 * thr : 200.0;
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

            // No fuel quantity anywhere in the sim: the gauge reads full rather than lying about an engine
            // that cannot run dry.
            r.FuelCentreFrac = 1.0;
        }

        /// <summary>The canopy's own motion: it swings to where it was commanded, and the emergency handle
        /// pulls under a hand then springs back.</summary>
        private void StepCanopy(float dt)
        {
            if (dt <= 0f) return;
            // Speed shuts it: leave it open and accelerate, and it comes down and locks rather than flying off.
            if (_iasKt > CanopyMaxOpenKt && _canopyTarget > 0f) { _canopyTarget = 0f; CanopyLocked = true; }
            if (!CanopyGone)
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
