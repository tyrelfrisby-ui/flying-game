using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The pilot once out of the aircraft: a point mass (90 kg pilot + 15 kg kit) drawn as an articulated
    /// figure (<see cref="PilotFigure"/> under this body's root transform, posed per phase),
    /// optionally strapped to an ejection seat (rocket along the seat's rail direction, then separation),
    /// free-falling with quadratic drag in ISA air + the sim's wind, then under the <see cref="Parachute"/>:
    /// canopy drag (deploying drag area), 3 m/s forward drive along the canopy heading, aileron steering
    /// (yaw rate ∝ input, up to 30°/s), a damped pendulum for the pilot swinging under the canopy, and a
    /// landing on terrain/water. All kinematics are integrated here in Update time (semi-implicit Euler,
    /// implicit drag) — no Unity physics, nothing in the sim.
    /// </summary>
    internal sealed class PilotBody
    {
        public const float MassKg = 105f;          // 90 kg pilot + 15 kg kit
        public const float SeatMassKg = 70f;
        public const float CdATumbling = 0.7f;     // m², pilot tumbling in free fall
        public const float CdAHanging = 0.5f;      // m², pilot upright under the canopy
        public const float CdASeat = 0.45f;        // m² extra while strapped to the seat
        public const float HalfHeightM = 0.9f;     // capsule half-height (1.8 m pilot)
        // Zero-zero seat rocket: 12 g for 0.25 s then 4 g to 0.5 s, along the aircraft's up axis at ejection.
        public const float RocketG1 = 12f, RocketBurn1Sec = 0.25f, RocketG2 = 4f, RocketBurn2Sec = 0.5f;

        public Vector3 Position;                   // Unity world, m
        public Vector3 Velocity;                   // Unity world, m/s
        public Vector3 AngularVelocityRad;         // body tumble, rad/s
        public float Steer;                        // −1..1 aileron (left/right) — only acts under canopy
        public float HeadingDeg;                   // canopy heading, Unity yaw (0 = north/+z, 90 = east/+x)
        public bool HasSeat { get; private set; }
        public bool Landed { get; private set; }
        public bool LandedOnWater { get; private set; }
        public float LandingSpeedMs { get; private set; }
        public Parachute Chute { get; private set; }
        public Transform Transform => _root.transform;
        public float AltitudeM => Position.y;
        public float HeightAglM => Position.y - HalfHeightM - EgressAir.SurfaceHeight(Position);
        public float SinkMs => -Velocity.y;
        public Vector3 HeadingDir => Quaternion.Euler(0f, HeadingDeg, 0f) * Vector3.forward;
        /// <summary>Wind-relative airspeed (m/s).</summary>
        public float AirspeedMs => (Velocity - EgressAir.Wind(Position)).magnitude;

        private readonly GameObject _root, _seat, _flame;
        private readonly PilotFigure _figure;      // the articulated body under _root (poses per phase, toggle arms)
        private Quaternion _rot;
        private float _rocketT = float.MaxValue;   // < RocketBurn2Sec while the seat rocket burns
        private Vector3 _seatUp = Vector3.up;      // rail direction frozen at ejection
        private Vector3 _hang = Vector3.up, _hangVel;   // pendulum: direction harness → canopy, and its rate
        private float _airspeedAtDeploy;
        private Vector3 _lastAero;                 // non-gravity acceleration last step (drives the pendulum)

        public PilotBody(Vector3 position, Vector3 velocity, Vector3 angularVelocityRad, float headingDeg,
            bool withSeat, Vector3 seatUp, Quaternion initialRotation)
        {
            Position = position;
            Velocity = velocity;
            AngularVelocityRad = angularVelocityRad;
            HeadingDeg = headingDeg;
            HasSeat = withSeat;
            _rot = initialRotation;

            _root = new GameObject("Pilot");
            _figure = new PilotFigure(_root.transform, withSeat ? PilotFigure.Stance.Seated : PilotFigure.Stance.FreeFall);
            _figure.SetPackVisible(!withSeat);      // the container rides inside the ejection seat until separation
            if (withSeat)
            {
                _seatUp = seatUp.sqrMagnitude > 0.5f ? seatUp.normalized : Vector3.up;
                _rocketT = 0f;
                _seat = EgressAir.Prim(PrimitiveType.Cube, "Seat", _root.transform, new Vector3(0f, -0.1f, -0.28f), new Vector3(0.55f, 0.95f, 0.35f),
                    EgressAir.Unlit(new Color(0.22f, 0.22f, 0.24f)));
                _flame = EgressAir.Prim(PrimitiveType.Sphere, "Rocket", _root.transform, new Vector3(0f, -1.1f, -0.28f), new Vector3(0.3f, 1.1f, 0.3f),
                    EgressAir.Unlit(new Color(1f, 0.6f, 0.15f)));
            }
            _root.transform.SetPositionAndRotation(Position, _rot);
        }

        /// <summary>Drop the seat: it becomes free debris pushed down/aft; the pilot continues alone.</summary>
        public void SeparateSeat()
        {
            if (!HasSeat) return;
            HasSeat = false;
            _rocketT = float.MaxValue;
            _figure.SetPackVisible(true);
            if (_flame != null) Object.Destroy(_flame);
            if (_seat != null)
            {
                Vector3 seatPos = _seat.transform.position;
                Quaternion seatRot = _seat.transform.rotation;
                Vector3 seatScale = _seat.transform.lossyScale;
                Object.Destroy(_seat);
                var go = EgressAir.Prim(PrimitiveType.Cube, "EjectionSeat", null, Vector3.zero, seatScale, EgressAir.Unlit(new Color(0.22f, 0.22f, 0.24f)));
                go.transform.SetPositionAndRotation(seatPos, seatRot);
                var body = go.AddComponent<DebrisBody>();
                body.MassKg = SeatMassKg;
                body.CdA = 0.6f;
                body.Velocity = Velocity + _rot * new Vector3(0f, -1.5f, -1.0f);
                body.AngularVelocityRad = new Vector3(2.5f, 0.8f, 1.5f);
                body.RestHeightM = 0.4f;
                body.LifeSec = 40f;
            }
            Velocity += _rot * new Vector3(0f, 0.5f, 0.3f);   // equal and opposite nudge on the pilot
        }

        /// <summary>Pilot chute out: line stretch → inflation begins (see <see cref="Parachute"/>).</summary>
        public void DeployChute()
        {
            if (Chute != null || Landed) return;
            Chute = new Parachute();
            Vector3 vRel = Velocity - EgressAir.Wind(Position);
            _airspeedAtDeploy = vRel.magnitude;
            // The bag streams straight downstream of the pilot; the pendulum then brings the canopy overhead
            // as the pilot decelerates and the flight path turns vertical.
            _hang = vRel.magnitude > 1f ? -vRel.normalized : Vector3.up;
            _hangVel = Vector3.zero;
            Vector3 vh = new Vector3(Velocity.x, 0f, Velocity.z);
            if (vh.magnitude > 2f) HeadingDeg = Mathf.Atan2(vh.x, vh.z) * Mathf.Rad2Deg;
            Chute.Place(Harness, _hang, HeadingDir, 0f);
        }

        private Vector3 Harness => Position + _hang * Parachute.RiserM;

        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            if (Landed)
            {
                Chute?.TickCollapse(dt, Position + Vector3.up * Parachute.RiserM);
                TickFigure(dt);
                return;
            }

            float rho = EgressAir.Density(Position.y);
            Vector3 wind = EgressAir.Wind(Position);
            float m = MassKg + (HasSeat ? SeatMassKg : 0f);
            Vector3 v0 = Velocity;

            // Gravity + seat rocket (frozen rail direction).
            Velocity += Vector3.down * (EgressAir.G * dt);
            if (_rocketT < RocketBurn2Sec)
            {
                float g = _rocketT < RocketBurn1Sec ? RocketG1 : RocketG2;
                Velocity += _seatUp * (g * EgressAir.G * dt);
                _rocketT += dt;
                if (_flame != null && _rocketT >= RocketBurn2Sec) _flame.SetActive(false);
            }

            // Body drag (tumbling → hanging as the canopy fills).
            float fill = Chute?.Fill ?? 0f;
            float cdA = HasSeat ? CdATumbling + CdASeat : Mathf.Lerp(CdATumbling, CdAHanging, fill);
            Velocity += EgressAir.ImplicitDragDelta(Velocity - wind, rho, cdA, m, dt);

            // Canopy: deployment model, steering, drive, drag on the drive-relative airflow.
            if (Chute != null)
            {
                Chute.Tick(dt, _airspeedAtDeploy);
                HeadingDeg += Steer * Parachute.TurnRateDegPerSec * fill * dt;
                Vector3 drive = HeadingDir * (Parachute.DriveMs * fill);
                Velocity += EgressAir.ImplicitDragDelta(Velocity - wind - drive, rho, Chute.CdS, m, dt);
            }

            Position += Velocity * dt;
            _lastAero = (Velocity - v0) / dt - Vector3.down * EgressAir.G;

            // Pendulum: the lines point along (g·up + aero accel); the pilot lags it as a damped pendulum of
            // length 7 m (ω = sqrt(g/L) = 1.18 rad/s, ζ ≈ 0.35 when full; stiff/well-damped while a streamer).
            if (Chute != null)
            {
                Vector3 target = Vector3.up * EgressAir.G + _lastAero;
                target = target.sqrMagnitude > 1e-3f ? target.normalized : Vector3.up;
                float omega = Mathf.Lerp(4f, Mathf.Sqrt(EgressAir.G / Parachute.LineLengthM), fill);
                float zeta = Mathf.Lerp(0.9f, 0.35f, fill);
                _hangVel += (omega * omega * (target - _hang) - 2f * zeta * omega * _hangVel) * dt;
                _hang = (_hang + _hangVel * dt).normalized;
            }

            // Ground / water.
            float surf = EgressAir.SurfaceHeight(Position);
            if (Position.y - HalfHeightM <= surf)
            {
                Land(surf, wind);
                return;
            }

            // Orientation: seat on its rail during the burn; tumble in free fall; hang under the canopy.
            if (_rocketT < RocketBurn2Sec)
            {
                Vector3 fwd = Vector3.ProjectOnPlane(_rot * Vector3.forward, _seatUp);
                if (fwd.sqrMagnitude > 1e-4f) _rot = Quaternion.LookRotation(fwd.normalized, _seatUp);
            }
            else if (Chute == null)
            {
                _rot *= Quaternion.Euler(AngularVelocityRad * (Mathf.Rad2Deg * dt));
            }
            else
            {
                Vector3 fwd = Vector3.ProjectOnPlane(HeadingDir, _hang);
                if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(Vector3.forward, _hang);
                Quaternion hangRot = Quaternion.LookRotation(fwd.normalized, _hang);
                float rate = 0.3f + 3f * fill;
                _rot *= Quaternion.Euler(AngularVelocityRad * (Mathf.Rad2Deg * dt));
                _rot = Quaternion.Slerp(_rot, hangRot, 1f - Mathf.Exp(-rate * dt));
                AngularVelocityRad *= Mathf.Exp(-(1f + 2f * fill) * dt);
            }

            _root.transform.SetPositionAndRotation(Position, _rot);
            Chute?.Place(Harness, _hang, HeadingDir, Steer * fill);
            TickFigure(dt);
        }

        /// <summary>
        /// Pose the figure for the phase: seated on the seat, spread in free fall, hanging with both hands on
        /// the toggles once the canopy is full (the steered side's arm pulls down), standing once landed. The
        /// toggle lines hang from a point 1.2 m up the risers, 0.42 m either side of the hang axis.
        /// </summary>
        private void TickFigure(float dt)
        {
            PilotFigure.Stance stance = Landed ? PilotFigure.Stance.Landed
                : HasSeat ? PilotFigure.Stance.Seated
                : Chute != null && Chute.Inflated ? PilotFigure.Stance.Hanging
                : PilotFigure.Stance.FreeFall;
            bool toggles = stance == PilotFigure.Stance.Hanging;
            Vector3 anchorL = Vector3.zero, anchorR = Vector3.zero;
            if (toggles)
            {
                Transform t = _root.transform;
                Vector3 top = Harness + _hang * PilotFigure.ToggleAnchorAboveHarnessM;
                Vector3 span = _rot * Vector3.right * PilotFigure.ToggleAnchorHalfSpanM;
                anchorL = t.InverseTransformPoint(top - span);
                anchorR = t.InverseTransformPoint(top + span);
            }
            _figure.Tick(dt, stance, Steer, toggles, anchorL, anchorR);
        }

        private void Land(float surf, Vector3 wind)
        {
            Landed = true;
            LandingSpeedMs = Velocity.magnitude;
            LandedOnWater = EgressAir.IsWater(Position);
            Position.y = surf + HalfHeightM;
            Velocity = Vector3.zero;
            AngularVelocityRad = Vector3.zero;
            _rot = Quaternion.Euler(0f, HeadingDeg, 0f);               // stand the capsule up
            _root.transform.SetPositionAndRotation(Position, _rot);
            if (_flame != null) _flame.SetActive(false);
            if (Chute != null)
            {
                Vector3 wh = new Vector3(wind.x, 0f, wind.z);
                Vector3 layDir = wh.magnitude > 0.5f ? wh.normalized : HeadingDir;
                Chute.BeginCollapse(Position - Vector3.up * HalfHeightM, layDir);
            }
        }

        public void Destroy()
        {
            Chute?.Destroy();
            Chute = null;
            if (_root != null) Object.Destroy(_root);
        }
    }
}
