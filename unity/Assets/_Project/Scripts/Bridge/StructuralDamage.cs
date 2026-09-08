using System;
using FlyingGame.Sim;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Bridge for the sim's REAL structural g limits (owner request 2026-09-08). The physics lives in
    /// FlyingGame.Sim.Aircraft.Structure: limit load = AircraftConfig.Limits.GMax / GMin, ultimate = 1.5 × limit
    /// (FAR 23/25). Over the limit the airframe groans/creaks (Severity 0..1 = how far from limit toward
    /// ultimate); over ultimate for a short dwell the WINGS BREAK OFF — the sim drops the wing surfaces and
    /// keeps flying fuselage/tail/engine, so elevator, rudder and throttle still work as it drops.
    ///
    /// This component polls that state every frame, raises Groan / WingsFailed for the other systems
    /// (FlightAudio, PilotEgress, NetSession, HUD), drives FlightAudio directly, and tells AirframeVisual to
    /// let the wings tumble away. A fresh sim Aircraft (ResetFlight / aircraft switch / challenge spawn) is
    /// intact, so this repairs whenever the driver's Aircraft instance changes.
    /// </summary>
    [RequireComponent(typeof(FlightSimDriver))]
    public sealed class StructuralDamage : MonoBehaviour
    {
        /// <summary>Raised every frame while over the LIMIT load: severity 0 (at limit) .. 1 (at ultimate).</summary>
        public event Action<float> Groan;
        /// <summary>Raised once when the ULTIMATE load is exceeded and the wings separate.</summary>
        public event Action WingsFailed;

        public bool WingsGone { get; private set; }
        public float Severity { get; private set; }     // 0 when under limit
        public float LoadFactorG { get; private set; }  // current n_z (g)
        public float LimitG { get; private set; }
        public float UltimateG { get; private set; }

        private FlightSimDriver _driver;
        private FlightAudio _audio;
        private AirframeVisual _visual;
        private Aircraft _watched;   // the sim Aircraft this state was read from (a new instance = intact airframe)

        private void Awake()
        {
            _driver = GetComponent<FlightSimDriver>();
        }

        private void Start()
        {
            _driver.AircraftChanged += Repair;
            _audio = GetComponent<FlightAudio>();
            _visual = GetComponent<AirframeVisual>();
            Repair();
        }

        private void OnDestroy()
        {
            if (_driver != null) _driver.AircraftChanged -= Repair;
        }

        /// <summary>Restore an intact airframe (ResetFlight / aircraft switch).</summary>
        public void Repair()
        {
            WingsGone = false;
            Severity = 0f;
            if (_watched != null) _watched.ComponentLost -= OnComponentLost;
            _watched = _driver != null && _driver.Sim != null ? _driver.Sim.Aircraft : null;
            if (_watched != null) _watched.ComponentLost += OnComponentLost;
            LostLine = null;
            ReadLimits();
        }

        /// <summary>HUD text naming what has broken off, null while intact.</summary>
        public string LostLine { get; private set; }

        private void OnComponentLost(FlyingGame.Core.AirframeComponent c)
        {
            if (_visual == null) _visual = GetComponent<AirframeVisual>();
            if (_visual != null) _visual.DetachComponent(c, _driver.WorldVelocityUnity);
            if (_audio == null) _audio = GetComponent<FlightAudio>();
            if (_audio != null) _audio.WingFailure();
            string name = c switch
            {
                FlyingGame.Core.AirframeComponent.WingLeft => "LEFT WING",
                FlyingGame.Core.AirframeComponent.WingRight => "RIGHT WING",
                FlyingGame.Core.AirframeComponent.TailHorizontal => "STABILISER",
                FlyingGame.Core.AirframeComponent.TailVertical => "FIN",
                FlyingGame.Core.AirframeComponent.Nose => "PROPELLER",
                _ => c.ToString().ToUpperInvariant(),
            };
            LostLine = LostLine == null ? "BROKE OFF: " + name : LostLine + ", " + name;
        }

        private void ReadLimits()
        {
            if (_watched == null) return;
            StructuralState st = _watched.Structure;
            LimitG = (float)st.LimitPosG;
            UltimateG = (float)st.UltimatePosG;
        }

        private void Update()
        {
            if (_driver == null || _driver.Sim == null) return;
            Aircraft ac = _driver.Sim.Aircraft;
            if (!ReferenceEquals(ac, _watched))
            {
                Repair();   // AdoptSim / spawn built a new Aircraft: intact again
            }

            StructuralState st = ac.Structure;
            LoadFactorG = (float)ac.LoadFactorZ;
            // Report the limit on the side currently being loaded (positive by default).
            bool negative = ac.LoadFactorZ < 0.0;
            LimitG = (float)(negative ? st.LimitNegG : st.LimitPosG);
            UltimateG = (float)(negative ? st.UltimateNegG : st.UltimatePosG);
            Severity = (float)st.OverLimitSeverity;

            if (Severity > 0f && !WingsGone)
            {
                Groan?.Invoke(Severity);
                if (_audio == null) _audio = GetComponent<FlightAudio>();
                if (_audio != null) _audio.StructuralGroan(Severity);
            }

            if (st.WingsFailed && !WingsGone)
            {
                WingsGone = true;
                Severity = 0f;
                if (_visual == null) _visual = GetComponent<AirframeVisual>();
                if (_visual != null) _visual.DetachWings(_driver.WorldVelocityUnity);
                if (_audio == null) _audio = GetComponent<FlightAudio>();
                if (_audio != null) _audio.WingFailure();
                WingsFailed?.Invoke();
            }
        }
    }
}
