using System;
using System.Collections.Generic;
using FlyingGame.Core.Wreck;
using FlyingGame.Sim;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Bridge for the sim's REAL structural g limits (owner request 2026-09-08, thresholds revised 2026-10-31). The
    /// physics lives in FlyingGame.Sim.Aircraft.Structure. THREE thresholds: the rated LIMIT load
    /// (AircraftConfig.Limits.GMax / GMin), which the syllabus grades against and which makes no noise; the GROAN
    /// threshold at 1.5 × limit, where the airframe starts to creak and pop (Severity 0..1 = how far from groan
    /// toward ultimate); and ULTIMATE at 2.0 × limit, where for a short dwell the WINGS BREAK OFF — the sim drops the wing surfaces and
    /// keeps flying fuselage/tail/engine, so elevator, rudder and throttle still work as it drops.
    ///
    /// This component polls that state every frame, raises Groan / WingsFailed for the other systems
    /// (FlightAudio, PilotEgress, NetSession, HUD), drives FlightAudio directly, and tells AirframeVisual to
    /// let the wings tumble away. A fresh sim Aircraft (ResetFlight / aircraft switch / challenge spawn) is
    /// intact, so this repairs whenever the driver's Aircraft instance changes.
    ///
    /// CRASH IMPACTS (2026-11-02, lane/claude-crash-water): this no longer plays the crash sound itself. A hard hit
    /// and every piece that breaks off go through FlyingGame.Sim.CrashRelay, which raises FlyingGame.Core.CrashEvents
    /// with the surface struck (Land / Water); FlightAudio subscribes and plays the crunch or the splash. The relay
    /// also runs the wreck (WreckSim): each debris object is bound to its sim piece and only shows where it is.
    /// </summary>
    [RequireComponent(typeof(FlightSimDriver))]
    public sealed class StructuralDamage : MonoBehaviour
    {
        /// <summary>Raised every frame while past the GROAN threshold: severity 0 (there) .. 1 (at ultimate).</summary>
        public event Action<float> Groan;
        /// <summary>Raised once per structural pop, with the severity at that moment.</summary>
        public event Action<float> Pop;
        /// <summary>Raised once when the ULTIMATE load is exceeded and the wings separate.</summary>
        public event Action WingsFailed;

        public bool WingsGone { get; private set; }
        public float Severity { get; private set; }     // 0 when under limit
        public float LoadFactorG { get; private set; }  // current n_z (g)
        public float LimitG { get; private set; }
        /// <summary>Where the complaining starts: 1.5 x the limit.</summary>
        public float GroanG { get; private set; }
        public float UltimateG { get; private set; }
        /// <summary>Pops heard so far, mirrored from the sim so a changed count means a new pop.</summary>
        public int Pops { get; private set; }

        private FlightSimDriver _driver;
        private FlightAudio _audio;
        private AirframeVisual _visual;
        private Aircraft _watched;   // the sim Aircraft this state was read from (a new instance = intact airframe)
        private CrashRelay _relay;   // crash events + wreck pieces for _watched
        private readonly List<GameObject> _wreckVisuals = new();
        /// <summary>The crash relay for the current aircraft (null before the sim exists).</summary>
        public CrashRelay Relay => _relay;

        private void Awake()
        {
            _driver = GetComponent<FlightSimDriver>();
        }

        private void Start()
        {
            _driver.AircraftChanged += Repair;
            _driver.AirframeReplaced += Repair;   // the one reset (FlightSimDriver.AirframeReplaced): before the new aeroplane steps
            _audio = GetComponent<FlightAudio>();
            _visual = GetComponent<AirframeVisual>();
            Repair();
        }

        private void OnDestroy()
        {
            if (_driver != null) { _driver.AircraftChanged -= Repair; _driver.AirframeReplaced -= Repair; }
        }

        /// <summary>Restore an intact airframe (ResetFlight / aircraft switch).</summary>
        public void Repair()
        {
            WingsGone = false;
            Severity = 0f;
            if (_watched != null) { _watched.ComponentLost -= OnComponentLost; _watched.HardImpact -= OnHardImpact; _watched.Damage.Changed -= OnDamage; _watched.Contact -= OnContact; }
            _watched = _driver != null && _driver.Sim != null ? _driver.Sim.Aircraft : null;
            if (_watched != null) { _watched.ComponentLost += OnComponentLost; _watched.HardImpact += OnHardImpact; _watched.Damage.Changed += OnDamage; _watched.Contact += OnContact; }
            // The old wreck goes with the old aeroplane, and the trees it snapped stand again (FlyingGame.Core.TreeStructure).
            FlyingGame.Core.Forest.ClearBroken();
            foreach (GameObject g in _wreckVisuals) if (g != null) Destroy(g);
            _wreckVisuals.Clear();
            _relay = _watched != null ? new CrashRelay(_watched, subscribe: false) : null;
            if (GetComponent<FuelSlickVisual>() == null) gameObject.AddComponent<FuelSlickVisual>();
            if (GetComponent<ContactEffects>() == null) gameObject.AddComponent<ContactEffects>();   // placeholder scrape sound + dust
            if (GetComponent<PropDebris>() == null) gameObject.AddComponent<PropDebris>();           // a broken blade's pieces
            if (GetComponent<TyreSmoke>() == null) gameObject.AddComponent<TyreSmoke>();             // rubber the tyres lose (Core TyreWear)
            LostLine = null;
            ReadLimits();
        }

        /// <summary>HUD text naming what has broken off, null while intact.</summary>
        public string LostLine { get; private set; }

        /// <summary>A hard point or wheel just hit. A wheel on land is not a crash: its chirp comes from the tyre's own
        /// spin-up slip (Core WheelSpin → GroundSoundMix → TyreBrakeSynth), for as long as the tyre really slips, and a
        /// leg that breaks adds its own crash via ComponentLost. Anything else is a crash impact, raised by the relay as a
        /// CrashEvents impact with the surface it struck (FlightAudio plays crunch or splash).</summary>
        private void OnHardImpact(double closingMs, string point)
        {
            if (_audio == null) _audio = GetComponent<FlightAudio>();
            if (point.StartsWith("Gear") && (_watched == null || !_watched.LastImpact.OnWater)) return;
            _relay?.OnHardImpact(closingMs, point);
        }

        /// <summary>Hand a debris object to its sim piece: from here the wreck physics moves it.</summary>
        private void Bind(GameObject go, WreckPiece piece)
        {
            if (go == null) return;
            _wreckVisuals.Add(go);
            if (piece == null || _watched == null) return;
            WingDebris d = go.GetComponent<WingDebris>();
            if (d == null) return;
            d.Piece = piece;
            d.PieceOffsetBody = piece.CgBody - _watched.Config.Mass.CgVec();
        }

        /// <summary>Contact damage (Ty 2026-11-03): a medium wingtip strike bends the panel — the crunch, raised as a land
        /// impact so FlightAudio plays it, and the panel turned up on the model. Breaks arrive as ComponentLost.</summary>
        private void OnContact(FlyingGame.Core.ContactEvent e)
        {
            if (_watched == null) return;
            if (e.Kind == FlyingGame.Core.ContactEventKind.FuselageCrushed || e.Kind == FlyingGame.Core.ContactEventKind.BranchDamage)
            {
                // A crushed fuselage section (it bends instead of breaking) or branches doing harm (Ty 2026-11-04: "it made a
                // sound with no damage"): the crunch, as a land impact, its severity from the energy the crush or the wood took
                // against the energy of an arrival at the cabin's break speed.
                double eBreak = 0.5 * _watched.MassProperties.MassKg * FlyingGame.Core.AirframeContact.CabinBreakMs * FlyingGame.Core.AirframeContact.CabinBreakMs;
                double sev = System.Math.Clamp(e.Intensity / System.Math.Max(1.0, eBreak), e.Kind == FlyingGame.Core.ContactEventKind.BranchDamage ? 0.1 : 0.25, 1.0);
                FlyingGame.Core.CrashEvents.RaiseImpact(new FlyingGame.Core.CrashImpact(FlyingGame.Core.ImpactSurface.Land, e.PositionWorld, e.VelocityWorld,
                    e.VelocityWorld.Length, _watched.MassProperties.MassKg, sev, -1));
                string what = e.Kind == FlyingGame.Core.ContactEventKind.BranchDamage
                    ? (e.Component == FlyingGame.Core.AirframeComponent.Propeller ? "PROP DAMAGED BY BRANCHES" : "BRANCH DAMAGE")
                    : e.Component == FlyingGame.Core.AirframeComponent.TailBoom ? "TAIL BENT"
                    : e.Component == FlyingGame.Core.AirframeComponent.Nose ? "ENGINE MOUNT BENT" : "FUSELAGE CRUSHED";
                if (LostLine == null || !LostLine.Contains(what)) LostLine = LostLine == null ? what : LostLine + ", " + what;
                return;
            }
            if (e.Kind != FlyingGame.Core.ContactEventKind.PanelBent) return;
            FlyingGame.Core.CrashEvents.RaiseImpact(new FlyingGame.Core.CrashImpact(FlyingGame.Core.ImpactSurface.Land, e.PositionWorld, e.VelocityWorld,
                e.VelocityWorld.Length, _watched.MassProperties.MassKg, System.Math.Clamp(e.Intensity, 0.2, 1.0), -1));
            string name = PanelName(e.Component);
            LostLine = LostLine == null ? "BENT: " + name : LostLine + ", BENT " + name;
        }

        private static string PanelName(FlyingGame.Core.AirframeComponent c) => c switch
        {
            FlyingGame.Core.AirframeComponent.WingTipLeft => "LEFT WINGTIP PANEL",
            FlyingGame.Core.AirframeComponent.WingTipRight => "RIGHT WINGTIP PANEL",
            FlyingGame.Core.AirframeComponent.WingTipLowerLeft => "LEFT LOWER WINGTIP PANEL",
            FlyingGame.Core.AirframeComponent.WingTipLowerRight => "RIGHT LOWER WINGTIP PANEL",
            _ => c.ToString().ToUpperInvariant(),
        };

        /// <summary>Bullet damage that is not a part loss: fuel streaming, fire.</summary>
        private void OnDamage(FlyingGame.Core.AirframeComponent c, FlyingGame.Core.Combat.DamageEvent e)
        {
            if (e == FlyingGame.Core.Combat.DamageEvent.PartLost) return;   // ComponentLost handles it
            if (_audio == null) _audio = GetComponent<FlightAudio>();
            _audio?.BulletHit(1f);
            string side = c == FlyingGame.Core.AirframeComponent.WingLeftInner ? "LEFT" : "RIGHT";
            string name = e == FlyingGame.Core.Combat.DamageEvent.FuelLeak ? $"{side} TANK HOLED — streaming fuel" : $"{side} WING ON FIRE";
            LostLine = LostLine == null ? name : LostLine + ", " + name;
        }

        private void OnComponentLost(FlyingGame.Core.AirframeComponent c)
        {
            if (_visual == null) _visual = GetComponent<AirframeVisual>();
            GameObject debris = _visual != null ? _visual.DetachComponent(c, _driver.WorldVelocityUnity) : null;
            // The break's sound is the relay's: the piece's first touch of land or water raises its own CrashEvents
            // impact (and a prop strike raises one for the airframe). The g-limit wing failure keeps its own sound.
            WreckPiece piece = _relay?.OnComponentLost(c);
            Bind(debris, piece);
            string name = c switch
            {
                FlyingGame.Core.AirframeComponent.WingLeft => "LEFT WING",
                FlyingGame.Core.AirframeComponent.WingRight => "RIGHT WING",
                FlyingGame.Core.AirframeComponent.TailHorizontal => "STABILISER",
                FlyingGame.Core.AirframeComponent.TailVertical => "FIN",
                FlyingGame.Core.AirframeComponent.Nose => "NOSE",
                FlyingGame.Core.AirframeComponent.TailBoom => "TAIL",
                FlyingGame.Core.AirframeComponent.WingLeftOuter => "LEFT OUTER WING",
                FlyingGame.Core.AirframeComponent.WingRightOuter => "RIGHT OUTER WING",
                FlyingGame.Core.AirframeComponent.WingLeftInner => "LEFT INNER WING",
                FlyingGame.Core.AirframeComponent.WingRightInner => "RIGHT INNER WING",
                FlyingGame.Core.AirframeComponent.AileronLeft => "LEFT AILERON",
                FlyingGame.Core.AirframeComponent.AileronRight => "RIGHT AILERON",
                FlyingGame.Core.AirframeComponent.ElevatorLeft => "LEFT ELEVATOR",
                FlyingGame.Core.AirframeComponent.ElevatorRight => "RIGHT ELEVATOR",
                FlyingGame.Core.AirframeComponent.Rudder => "RUDDER",
                FlyingGame.Core.AirframeComponent.Cabin => "FUSELAGE BROKE UP",
                FlyingGame.Core.AirframeComponent.GearLeft => "LEFT GEAR",
                FlyingGame.Core.AirframeComponent.GearRight => "RIGHT GEAR",
                FlyingGame.Core.AirframeComponent.GearNose => "NOSE GEAR",
                FlyingGame.Core.AirframeComponent.GearTail => "TAILWHEEL",
                FlyingGame.Core.AirframeComponent.Propeller => "PROP STRIKE — engine stopped",
                FlyingGame.Core.AirframeComponent.WingTipLeft or FlyingGame.Core.AirframeComponent.WingTipRight
                    or FlyingGame.Core.AirframeComponent.WingTipLowerLeft or FlyingGame.Core.AirframeComponent.WingTipLowerRight => PanelName(c),
                _ => c.ToString().ToUpperInvariant(),
            };
            LostLine = c == FlyingGame.Core.AirframeComponent.Propeller ? (LostLine == null ? name : LostLine + ", " + name)
                : LostLine == null ? "BROKE OFF: " + name : LostLine + ", " + name;
        }

        private void ReadLimits()
        {
            if (_watched == null) return;
            StructuralState st = _watched.Structure;
            LimitG = (float)st.LimitPosG;
            GroanG = (float)st.GroanPosG;
            UltimateG = (float)st.UltimatePosG;
            Pops = st.Pops;
        }

        private void Update()
        {
            if (_driver == null || _driver.Sim == null) return;
            Aircraft ac = _driver.Sim.Aircraft;
            if (!ReferenceEquals(ac, _watched))
            {
                Repair();   // AdoptSim / spawn built a new Aircraft: intact again
            }

            // The wreck and the airframe's flooding, in step with the game clock.
            _relay?.Step(Time.deltaTime);
            // Bent wing panels follow their deflection (it grows with the load factor until they snap).
            if (_visual == null) _visual = GetComponent<AirframeVisual>();
            if (_visual != null)
                foreach (FlyingGame.Core.WingPanel p in ac.Panels)
                    if (p.State == FlyingGame.Core.PanelState.Bent || System.Math.Abs(p.ElasticRad) > 0.003) _visual.ShowPanelBend(p, ac.Config);
            // A crushed tail boom or engine mount bends on the model too.
            if (_visual != null && (ac.Crush.TailCrushM > 0 || ac.Crush.NoseCrushM > 0)) _visual.ShowFuselageBend(ac.Crush, ac.Config);

            StructuralState st = ac.Structure;
            LoadFactorG = (float)ac.LoadFactorZ;
            // Report the limit on the side currently being loaded (positive by default).
            bool negative = ac.LoadFactorZ < 0.0;
            LimitG = (float)(negative ? st.LimitNegG : st.LimitPosG);
            GroanG = (float)(negative ? st.GroanNegG : st.GroanPosG);
            UltimateG = (float)(negative ? st.UltimateNegG : st.UltimatePosG);
            Severity = (float)st.OverLimitSeverity;

            if (Severity > 0f && !WingsGone)
            {
                Groan?.Invoke(Severity);
                if (_audio == null) _audio = GetComponent<FlightAudio>();
                if (_audio != null) _audio.StructuralGroan(Severity);
            }

            // One sound per pop the sim counted, however many frames it took to get here. Capped so a pathological
            // frame cannot fire a hundred voices at once.
            if (!WingsGone && st.Pops != Pops)
            {
                int owed = Mathf.Clamp(st.Pops - Pops, 0, 3);
                if (_audio == null) _audio = GetComponent<FlightAudio>();
                for (int i = 0; i < owed; i++) { Pop?.Invoke(Severity); if (_audio != null) _audio.StructuralPop(Severity); }
            }
            Pops = st.Pops;

            if (st.WingsFailed && !WingsGone)
            {
                WingsGone = true;
                Severity = 0f;
                if (_visual == null) _visual = GetComponent<AirframeVisual>();
                (GameObject leftWing, GameObject rightWing) = _visual != null ? _visual.DetachWings(_driver.WorldVelocityUnity) : (null, null);
                IReadOnlyList<WreckPiece> wings = _relay != null ? _relay.OnWingsFailed() : Array.Empty<WreckPiece>();
                WreckPiece leftPiece = null, rightPiece = null;
                foreach (WreckPiece wp in wings)
                {
                    if (wp.Component == FlyingGame.Core.AirframeComponent.WingLeft) leftPiece = wp;
                    else if (wp.Component == FlyingGame.Core.AirframeComponent.WingRight) rightPiece = wp;
                }
                Bind(leftWing, leftPiece);
                Bind(rightWing, rightPiece);
                if (_audio == null) _audio = GetComponent<FlightAudio>();
                if (_audio != null) _audio.WingFailure();
                WingsFailed?.Invoke();
            }
        }
    }
}
