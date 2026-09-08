using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Round personal canopy (28 ft / 8.5 m C-9 style) — the DEPLOYMENT MODEL and the visual dome.
    ///
    /// Deployment timeline (t from the pilot chute leaving the pack):
    ///   0 – 0.6 s   LINE STRETCH: pilot chute + bag pay the lines out; drag area rises 0 → 1.5 m².
    ///   0.6 s → +tf INFLATION: drag area grows on an S-curve (smoothstep: ~t² early, easing at the end)
    ///               from 1.5 m² to the full CdS. tf = n·D0/v (Knacke, n ≈ 8 for a solid flat circular
    ///               canopy) clamped to 1.8–2.5 s, so a fast opening at 60 m/s fills in 1.8 s and a
    ///               glider-speed one in 2.5 s.
    ///   end of fill OPENING SHOCK: the canopy over-inflates — CdS peaks at 1.3× steady for 0.3 s
    ///               (visually the dome overshoots to 108 % diameter and settles).
    ///
    /// Steady CdS = 0.75 · π(8.5/2)² = 42.6 m² (Cd referred to the nominal flat area). Sea-level sink for
    /// 105 kg under it (plus ≈0.5 m² for the hanging pilot):
    ///   v = sqrt(2·m·g / (ρ·CdS)) = sqrt(2·105·9.81 / (1.225·43.1)) = 6.2 m/s
    /// — a C-9 is quoted at ~20 ft/s (6.1 m/s) at 250 lb, so the number checks out; at 2000 m (ρ = 1.007)
    /// it is 6.9 m/s. Forward drive 3 m/s (steering slots), yaw rate up to 30°/s on aileron.
    ///
    /// Pure C#; owns its GameObjects (dome, pilot chute, 8 suspension lines + bridle) and is ticked/placed
    /// by <see cref="PilotBody"/>.
    /// </summary>
    internal sealed class Parachute
    {
        public const float NominalDiameterM = 8.5f;
        public const float CanopyCd = 0.75f;
        public static readonly float CdSFull = CanopyCd * Mathf.PI * NominalDiameterM * NominalDiameterM * 0.25f; // ≈ 42.6 m²
        public const float LineStretchSec = 0.6f;
        public const float LineStretchCdS = 1.5f;
        public const float FillConstant = 8f;
        public const float FillTimeMinSec = 1.8f, FillTimeMaxSec = 2.5f;
        public const float ShockPeak = 1.3f;
        public const float ShockSec = 0.3f;
        public const float LineLengthM = 7f;          // skirt → confluence
        public const float RiserM = 0.6f;             // confluence above the pilot's centre
        public const float ProjectedRadiusM = 2.9f;   // inflated projected diameter ≈ 0.68·D0
        public const float DomeHeightM = 2.2f;
        public const float DriveMs = 3f;
        public const float TurnRateDegPerSec = 30f;
        public const float CollapseSec = 2f;
        private const int Lines = 8;

        public float Fill { get; private set; }         // 0 (streamer) .. 1 (full canopy)
        public float CdS { get; private set; }          // current drag area, m² (includes the shock)
        public bool Inflated { get; private set; }
        public bool LinesStretched => _t >= LineStretchSec;
        public float FillTimeSec => _fillTime;
        public float TimeSec => _t;

        private static Mesh _dome;
        private readonly GameObject _root, _canopy, _pilotChute, _bridle;
        private readonly GameObject[] _lines = new GameObject[Lines];
        private float _t, _fillTime, _overshoot = 1f;
        // collapse (after landing)
        private bool _collapsing;
        private float _collapseT;
        private Vector3 _cStartPos, _cEndPos, _cStartScale, _cEndScale, _pcStart, _pcEnd;
        private Quaternion _cStartRot, _cEndRot;

        public Parachute()
        {
            _dome ??= EgressAir.Dome(28, 8);
            _root = new GameObject("Parachute");
            _canopy = EgressAir.MeshObject("Canopy", _dome, EgressAir.Unlit(new Color(0.98f, 0.55f, 0.15f)), _root.transform);
            _pilotChute = EgressAir.MeshObject("PilotChute", _dome, EgressAir.Unlit(new Color(0.95f, 0.95f, 0.92f)), _root.transform);
            _pilotChute.transform.localScale = new Vector3(0.45f, 0.35f, 0.45f);
            Material line = EgressAir.Unlit(new Color(0.12f, 0.12f, 0.14f));
            for (int i = 0; i < Lines; i++)
            {
                _lines[i] = EgressAir.Prim(PrimitiveType.Cylinder, $"Line{i}", _root.transform, Vector3.zero, Vector3.one, line);
            }
            _bridle = EgressAir.Prim(PrimitiveType.Cylinder, "Bridle", _root.transform, Vector3.zero, Vector3.one, line);
        }

        /// <summary>Advance the deployment model. `airspeedAtDeploy` sets the fill time (Knacke).</summary>
        public void Tick(float dt, float airspeedAtDeploy)
        {
            if (_collapsing) return;
            _t += dt;
            if (_t < LineStretchSec)
            {
                Fill = 0f;
                CdS = LineStretchCdS * (_t / LineStretchSec);
                return;
            }
            if (_fillTime <= 0f)
            {
                _fillTime = Mathf.Clamp(FillConstant * NominalDiameterM / Mathf.Max(airspeedAtDeploy, 5f), FillTimeMinSec, FillTimeMaxSec);
            }
            float s = Mathf.Clamp01((_t - LineStretchSec) / _fillTime);
            Fill = s * s * (3f - 2f * s);                                   // S-curve: ~s² early, eases into full
            float steady = LineStretchCdS + (CdSFull - LineStretchCdS) * Fill;
            // Opening shock: a half-sine bump centred on the moment of full inflation.
            float tau = _t - LineStretchSec - _fillTime + ShockSec * 0.5f;   // 0 .. ShockSec across the bump
            float shock = tau > 0f && tau < ShockSec ? 1f + (ShockPeak - 1f) * Mathf.Sin(Mathf.PI * tau / ShockSec) : 1f;
            CdS = steady * shock;
            _overshoot = 1f + 0.08f * (shock - 1f) / (ShockPeak - 1f);       // dome visibly over-inflates then settles
            if (!Inflated && s >= 1f) Inflated = true;
        }

        /// <summary>
        /// Place the visual: `harness` = confluence point, `hangDir` = unit direction from the harness up the
        /// lines (the line-tension direction, pendulum-lagged by the pilot body), `heading` = canopy heading
        /// (horizontal unit), `bank` = −1..1 visual roll into the turn.
        /// </summary>
        public void Place(Vector3 harness, Vector3 hangDir, Vector3 heading, float bank)
        {
            if (_collapsing) return;
            float stretch = Mathf.Clamp01(_t / LineStretchSec);
            float r = Mathf.Lerp(0.15f, ProjectedRadiusM, Fill) * _overshoot;     // streamer → dome
            float h = Mathf.Lerp(2.6f, DomeHeightM, Fill);
            float baseDist = stretch * Mathf.Sqrt(Mathf.Max(1f, LineLengthM * LineLengthM - r * r));

            Vector3 fwd = Vector3.ProjectOnPlane(heading, hangDir);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(Vector3.forward, hangDir);
            Quaternion rot = Quaternion.LookRotation(fwd.normalized, hangDir) * Quaternion.AngleAxis(bank * 10f, Vector3.forward);

            Vector3 basePos = harness + hangDir * baseDist;
            _canopy.transform.SetPositionAndRotation(basePos, rot);
            _canopy.transform.localScale = new Vector3(r, h, r);

            Vector3 apex = basePos + rot * Vector3.up * h;
            Vector3 pc = apex + hangDir * 1.3f;
            _pilotChute.transform.SetPositionAndRotation(pc, rot);
            SetLine(_bridle, apex, pc, 0.02f);

            for (int i = 0; i < Lines; i++)
            {
                float a = i / (float)Lines * Mathf.PI * 2f;
                Vector3 skirt = basePos + rot * new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                SetLine(_lines[i], harness, skirt, 0.03f);
            }
        }

        /// <summary>Landed: deflate and lay the canopy on the ground over <see cref="CollapseSec"/>.</summary>
        public void BeginCollapse(Vector3 feet, Vector3 layDir)
        {
            if (_collapsing) return;
            _collapsing = true;
            _collapseT = 0f;
            float r = Mathf.Max(0.3f, _canopy.transform.localScale.x);
            _cStartPos = _canopy.transform.position;
            _cStartRot = _canopy.transform.rotation;
            _cStartScale = _canopy.transform.localScale;
            _cEndPos = feet + layDir * (r + 2f) + Vector3.up * 0.12f;
            _cEndRot = Quaternion.LookRotation(layDir, Vector3.up);
            _cEndScale = new Vector3(r * 0.9f, 0.12f, r * 0.9f);
            _pcStart = _pilotChute.transform.position;
            _pcEnd = _cEndPos + layDir * (r + 1.5f) + Vector3.up * 0.1f;
        }

        public void TickCollapse(float dt, Vector3 harness)
        {
            if (!_collapsing) return;
            _collapseT = Mathf.Min(CollapseSec, _collapseT + dt);
            float k = _collapseT / CollapseSec;
            k = k * k * (3f - 2f * k);
            Vector3 pos = Vector3.Lerp(_cStartPos, _cEndPos, k);
            Quaternion rot = Quaternion.Slerp(_cStartRot, _cEndRot, k);
            Vector3 scale = Vector3.Lerp(_cStartScale, _cEndScale, k);
            _canopy.transform.SetPositionAndRotation(pos, rot);
            _canopy.transform.localScale = scale;
            Vector3 pc = Vector3.Lerp(_pcStart, _pcEnd, k);
            _pilotChute.transform.position = pc;
            Vector3 apex = pos + rot * Vector3.up * scale.y;
            SetLine(_bridle, apex, pc, 0.02f);
            for (int i = 0; i < Lines; i++)
            {
                float a = i / (float)Lines * Mathf.PI * 2f;
                Vector3 skirt = pos + rot * new Vector3(Mathf.Cos(a) * scale.x, 0f, Mathf.Sin(a) * scale.z);
                SetLine(_lines[i], harness, skirt, 0.03f);
            }
        }

        public void Destroy()
        {
            if (_root != null) Object.Destroy(_root);
        }

        private static void SetLine(GameObject go, Vector3 a, Vector3 b, float thickness)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-3f) { go.transform.localScale = Vector3.zero; return; }
            go.transform.SetPositionAndRotation((a + b) * 0.5f, Quaternion.FromToRotation(Vector3.up, d / len));
            go.transform.localScale = new Vector3(thickness, len * 0.5f, thickness);   // cylinder primitive is 2 units tall
        }
    }
}
