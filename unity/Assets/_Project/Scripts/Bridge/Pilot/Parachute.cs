using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The pilot's round emergency canopy — the visual dome, lines and pilot chute, around the DEPLOYMENT MODEL in Core
    /// (<see cref="FlyingGame.Core.Vr.CanopyOpening"/>, docs/vr-cockpit.md phase 2), which both ways out share (the
    /// menu's timed BAIL OUT and the headset's hands-on exit):
    ///
    ///   pins clear → pilot chute (spring 8 m/s, CdS 0.41 m²) pays out bridle + lines (8.5 m) → LINE STRETCH at speed
    ///   V_ls → INFLATION over t_f = n·D0 / V_ls (Knacke, n ≈ 8), drag area growing as (t/t_f)² to the full
    ///   Cd0·S0 = 0.80 · π · 7.925² / 4 = 39.5 m² (a 26 ft round: the Strong Para-Cushion's).
    ///
    /// The OPENING SHOCK is no longer a 1.3× bump put on the drag area (it was, before 2026-11-02): it comes out of
    /// PilotBody's integration with a drag area that grows while the speed is still high — ≈ 6.7 g from 120 mph at sea
    /// level. Steady sink for 105 kg: √(2·105·9.81 / (1.225·(39.5 + 0.5))) = 6.5 m/s at sea level, 7.2 m/s at 2000 m.
    /// Forward drive 2.7 m/s (the Para-Cushion's "forward speed of about 6 MPH", its steering vents), yaw up to 30°/s.
    ///
    /// The dome is a GORED canopy (owner request 2026-09-08): <see cref="Gores"/> panels from apex to skirt,
    /// alternately WHITE and RED — two sub-meshes on one lofted mesh, one Unlit/Color material each (iOS-safe;
    /// Unlit/Color has no vertex colour). The skirt is pulled in 6 % at every seam so the gores read as
    /// separate panels in silhouette too. Inflation scales the mesh (r, h, r) and the landing collapse lerps
    /// the same transform, so both work unchanged on the new mesh. Codex's pilot-gear Parachute_Canopy, where the
    /// build has it, is driven by the same <see cref="Fill"/> (its "Inflate" shape key; VrCockpitHands).
    /// </summary>
    internal sealed class Parachute
    {
        public const int Gores = 28;                  // EVEN so the colours alternate all the way round
        public const float NominalDiameterM = (float)FlyingGame.Core.Vr.CanopyOpening.NominalDiameterM;
        public static readonly float CdSFull = (float)FlyingGame.Core.Vr.CanopyOpening.FullCdS;   // ≈ 39.5 m²
        public const float LineLengthM = (float)FlyingGame.Core.Vr.CanopyOpening.LinesM;   // skirt → confluence
        public const float RiserM = 0.6f;             // confluence above the pilot's centre
        public const float ProjectedRadiusM = 2.7f;   // inflated projected diameter ≈ 0.68·D0
        public const float DomeHeightM = 2.1f;
        public const float DriveMs = 2.7f;            // "a forward speed of about 6 MPH" (Para-Cushion manual)
        public const float TurnRateDegPerSec = 30f;
        public const float CollapseSec = 2f;
        private const int Lines = 8;
        private const float GoreSeamRadius = 0.94f;   // skirt radius at a seam, as a fraction of the gore centre's

        public float Fill { get; private set; }         // 0 (streamer) .. 1 (full canopy)
        public float CdS { get; private set; }          // current drag area, m² (includes the shock)
        public bool Inflated { get; private set; }
        public bool LinesStretched => _open.Phase >= FlyingGame.Core.Vr.OpeningPhase.Inflating;
        public float FillTimeSec => (float)_open.FillTimeSec;
        public float TimeSec => (float)_open.TimeSec;
        private readonly FlyingGame.Core.Vr.CanopyOpening _open = new();

        private static Mesh _dome, _goredDome;         // plain dome for the pilot chute; gored (2 sub-meshes) for the canopy
        private readonly GameObject _root, _canopy, _pilotChute, _bridle;
        private readonly GameObject[] _lines = new GameObject[Lines];
        // collapse (after landing)
        private bool _collapsing;
        private float _collapseT, _collapseSec = CollapseSec, _sinkM;
        private Vector3 _cStartPos, _cEndPos, _cStartScale, _cEndScale, _pcStart, _pcEnd;
        private Quaternion _cStartRot, _cEndRot;

        public Parachute()
        {
            _dome ??= EgressAir.Dome(16, 6);
            _goredDome ??= GoredDome(Gores, 8);
            _root = new GameObject("Parachute");
            ReplayActors.Register(_root);   // inflation and steering show in the replay
            Material white = EgressAir.Unlit(new Color(0.95f, 0.95f, 0.95f));
            Material red = EgressAir.Unlit(new Color(0.85f, 0.10f, 0.10f));
            _canopy = EgressAir.MeshObject("Canopy", _goredDome, white, _root.transform);
            _canopy.GetComponent<MeshRenderer>().sharedMaterials = new[] { white, red };   // sub-mesh 0 = white gores, 1 = red
            _pilotChute = EgressAir.MeshObject("PilotChute", _dome, EgressAir.Unlit(new Color(0.95f, 0.95f, 0.92f)), _root.transform);
            _pilotChute.transform.localScale = new Vector3(0.45f, 0.35f, 0.45f);
            Material line = EgressAir.Unlit(new Color(0.12f, 0.12f, 0.14f));
            for (int i = 0; i < Lines; i++)
            {
                _lines[i] = EgressAir.Prim(PrimitiveType.Cylinder, $"Line{i}", _root.transform, Vector3.zero, Vector3.one, line);
            }
            _bridle = EgressAir.Prim(PrimitiveType.Cylinder, "Bridle", _root.transform, Vector3.zero, Vector3.one, line);
        }

        /// <summary>Advance the deployment model at the pilot's CURRENT airspeed and the air's density.</summary>
        public void Tick(float dt, float airspeedMs, float rho)
        {
            if (_collapsing) return;
            if (_open.Phase == FlyingGame.Core.Vr.OpeningPhase.Packed) _open.Deploy(airspeedMs);
            _open.Step(dt, airspeedMs, rho);
            Fill = (float)_open.Inflate01;
            CdS = (float)_open.CdS;
            if (!Inflated && _open.Phase == FlyingGame.Core.Vr.OpeningPhase.Open) Inflated = true;
        }

        /// <summary>
        /// Place the visual: `harness` = confluence point, `hangDir` = unit direction from the harness up the
        /// lines (the line-tension direction, pendulum-lagged by the pilot body), `heading` = canopy heading
        /// (horizontal unit), `bank` = −1..1 visual roll into the turn.
        /// </summary>
        public void Place(Vector3 harness, Vector3 hangDir, Vector3 heading, float bank)
        {
            if (_collapsing) return;
            float stretch = Mathf.Clamp01((float)(_open.SeparationM / (FlyingGame.Core.Vr.CanopyOpening.BridleM + FlyingGame.Core.Vr.CanopyOpening.LinesM)));
            if (LinesStretched) stretch = 1f;
            float r = Mathf.Lerp(0.15f, ProjectedRadiusM, Fill);                  // streamer → dome
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
                float ra = r * GoreRadiusFactor(a);
                Vector3 skirt = basePos + rot * new Vector3(Mathf.Cos(a) * ra, 0f, Mathf.Sin(a) * ra);
                SetLine(_lines[i], harness, skirt, 0.03f);
            }
        }

        /// <summary>Landed: deflate and lay the canopy on the ground over <see cref="CollapseSec"/>. On the water
        /// (Core CanopyOnWater): <paramref name="centre"/> is where its centre comes to rest, over <paramref name="sec"/>.</summary>
        public void BeginCollapse(Vector3 feet, Vector3 layDir, Vector3? centre = null, float sec = CollapseSec)
        {
            if (_collapsing) return;
            _collapsing = true;
            _collapseT = 0f;
            _collapseSec = Mathf.Max(0.1f, sec);
            float r = Mathf.Max(0.3f, _canopy.transform.localScale.x);
            _cStartPos = _canopy.transform.position;
            _cStartRot = _canopy.transform.rotation;
            _cStartScale = _canopy.transform.localScale;
            _cEndPos = centre.HasValue ? centre.Value + Vector3.up * 0.05f : feet + layDir * (r + 2f) + Vector3.up * 0.12f;
            _cEndRot = Quaternion.LookRotation(layDir, Vector3.up);
            _cEndScale = new Vector3(r * 0.9f, 0.12f, r * 0.9f);
            _pcStart = _pilotChute.transform.position;
            _pcEnd = _cEndPos + layDir * (r + 1.5f) + Vector3.up * 0.1f;
        }

        public void TickCollapse(float dt, Vector3 harness)
        {
            if (!_collapsing) return;
            _collapseT = Mathf.Min(_collapseSec, _collapseT + dt);
            float k = _collapseT / _collapseSec;
            k = k * k * (3f - 2f * k);
            Vector3 pos = Vector3.Lerp(_cStartPos, _cEndPos, k) + Vector3.down * _sinkM;
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
                float f = GoreRadiusFactor(a);
                Vector3 skirt = pos + rot * new Vector3(Mathf.Cos(a) * scale.x * f, 0f, Mathf.Sin(a) * scale.z * f);
                SetLine(_lines[i], harness, skirt, 0.03f);
            }
        }

        /// <summary>On the water: how far the soaked canopy has sunk below the surface (Core CanopyOnWater.DepthM).</summary>
        public void SinkTo(float depthM) => _sinkM = Mathf.Max(0f, depthM);

        public void Destroy()
        {
            if (_root != null) Object.Destroy(_root);
        }

        /// <summary>The ghost's canopy (PilotBody.Ghost): translucent like him.</summary>
        public void SetGhost() => PilotBody.GhostRenderers(_root);

        /// <summary>Hide the primitive dome and lines (Codex's own Parachute_Deployed is drawn instead).</summary>
        public void SetVisible(bool on)
        {
            if (_root != null && _root.activeSelf != on) _root.SetActive(on);
        }

        private static void SetLine(GameObject go, Vector3 a, Vector3 b, float thickness)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-3f) { go.transform.localScale = Vector3.zero; return; }
            go.transform.SetPositionAndRotation((a + b) * 0.5f, Quaternion.FromToRotation(Vector3.up, d / len));
            go.transform.localScale = new Vector3(thickness, len * 0.5f, thickness);   // cylinder primitive is 2 units tall
        }

        /// <summary>
        /// Skirt radius at an angle round the canopy as a fraction of the nominal: 1 at a gore's centre,
        /// <see cref="GoreSeamRadius"/> at the seams, linear between — exactly the lofted mesh's profile, so the
        /// suspension lines meet the skirt where the mesh actually is.
        /// </summary>
        private static float GoreRadiusFactor(float angleRad)
        {
            float u = angleRad / (Mathf.PI * 2f) * Gores;
            u -= Mathf.Floor(u);                                         // 0 = seam, 0.5 = gore centre, 1 = next seam
            return GoreSeamRadius + (1f - GoreSeamRadius) * (1f - Mathf.Abs(2f * u - 1f));
        }

        /// <summary>
        /// Unit gored dome: upper hemisphere (skirt ring at y = 0, apex at y = 1), `gores` panels × `rings`
        /// latitude rings, two vertex columns per gore (seam pulled in to <see cref="GoreSeamRadius"/>, centre
        /// at full radius) so each gore is a fluted triangular-to-trapezoid panel from apex to skirt. Gore g's
        /// triangles go to sub-mesh g % 2 (0 = white, 1 = red). DOUBLE-SIDED like <see cref="EgressAir.Dome"/>
        /// (the inside is what the chase camera sees from below; Unlit/Color back-face culls).
        /// </summary>
        private static Mesh GoredDome(int gores, int rings)
        {
            int segments = gores * 2;
            var verts = new Vector3[(segments + 1) * (rings + 1)];
            var uvs = new Vector2[verts.Length];
            for (int j = 0; j <= rings; j++)
            {
                float phi = j / (float)rings * Mathf.PI * 0.5f;
                float r = Mathf.Cos(phi), y = Mathf.Sin(phi);
                for (int i = 0; i <= segments; i++)
                {
                    float a = i / (float)segments * Mathf.PI * 2f;
                    float f = (i & 1) == 0 ? GoreSeamRadius : 1f;        // even columns are seams, odd are gore centres
                    int k = j * (segments + 1) + i;
                    verts[k] = new Vector3(Mathf.Cos(a) * r * f, y, Mathf.Sin(a) * r * f);
                    uvs[k] = new Vector2(i / (float)segments, j / (float)rings);
                }
            }
            // Per gore: 2 quads × rings × (2 outside + 2 inside triangles) × 3 indices = rings × 24.
            var white = new int[((gores + 1) / 2) * rings * 24];
            var red = new int[(gores / 2) * rings * 24];
            int tw = 0, tr = 0;
            for (int g = 0; g < gores; g++)
            {
                bool isWhite = (g & 1) == 0;
                int[] tris = isWhite ? white : red;
                int t = isWhite ? tw : tr;
                for (int q = 0; q < 2; q++)
                {
                    int i = g * 2 + q;
                    for (int j = 0; j < rings; j++)
                    {
                        int a = j * (segments + 1) + i, b = a + 1, c = a + segments + 1, d = c + 1;
                        // outside
                        tris[t++] = a; tris[t++] = c; tris[t++] = b;
                        tris[t++] = b; tris[t++] = c; tris[t++] = d;
                        // inside (reverse winding)
                        tris[t++] = a; tris[t++] = b; tris[t++] = c;
                        tris[t++] = b; tris[t++] = d; tris[t++] = c;
                    }
                }
                if (isWhite) tw = t; else tr = t;
            }
            var m = new Mesh { name = "GoredDome" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.subMeshCount = 2;
            m.SetTriangles(white, 0);
            m.SetTriangles(red, 1);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
