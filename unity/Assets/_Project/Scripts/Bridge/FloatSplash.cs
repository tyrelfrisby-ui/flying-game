using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Draws the existing physics spray without feeding anything back to the simulation.
    /// SprayField emits/flies real hull-root parcels; this component presents them as fine white
    /// specks, with short independently rooted chine sheets. Visible cloud lifetime, footprint,
    /// sheet bounds and wake containment are visual choices (Ty t948/t950), not force corrections.
    /// Actual per-float support/ventilation/heel contact selects broad plow vs narrow step drawing.
    /// Surface foam and stationary prop mist retain their existing materials/behaviour.
    /// </summary>
    public sealed class FloatSplash : MonoBehaviour
    {
        public FlightSimDriver Driver;

        private ParticleSystem _spray, _foam;
        private ParticleSystem.Particle[] _buf = new ParticleSystem.Particle[SprayField.MaxParcels], _foamBuf = new ParticleSystem.Particle[SprayField.MaxFoam];
        private readonly SprayField _field = new SprayField();
        private Material _mat, _mistMat, _foamMat;
        private ParticleSystem[] _prop = new ParticleSystem[0];
        private Transform[] _discs = new Transform[0];
        private string _propAircraft, _fieldAircraft;
        private readonly System.Collections.Generic.List<ParticleSystem> _owned = new System.Collections.Generic.List<ParticleSystem>();
        private bool _built;
        private Mesh _sheets;
        private GameObject _sheetObject;
        private readonly float[] _sheetTime = new float[FloatSprayVisual.MaxArms];
        private readonly Vec3[] _sheetPoints = new Vec3[FloatSprayVisual.Samples];
        private readonly int[] _sheetRoots = new int[FloatSprayVisual.MaxArms];
        private readonly Vector3[] _sheetVertices = new Vector3[FloatSprayVisual.MaxArms * FloatSprayVisual.Samples * 2];
        private readonly Vector2[] _sheetUv = new Vector2[FloatSprayVisual.MaxArms * FloatSprayVisual.Samples * 2];
        private readonly Color[] _sheetColors = new Color[FloatSprayVisual.MaxArms * FloatSprayVisual.Samples * 2];

        private void Build()
        {
            _mat = new Material(Shader.Find("FlyingGame/Spray") ?? Shader.Find("FlyingGame/UnlitTransparent")) { color = new Color(1f, 1f, 1f, 1f) };
            _mat.SetFloat("_FineSpray", 1f);
            _mistMat = new Material(_mat); _mistMat.SetFloat("_FineSpray", 0f); _mistMat.SetFloat("_Mist", 1f);
            _foamMat = new Material(_mistMat); _foamMat.SetFloat("_Foam", 1f);
            _foamMat.color = new Color(.92f, .97f, 1f, .75f); // Preserve approved surface foam.
            BuildSheets();
            _spray = MakeDriven("FloatSpray", SprayField.MaxParcels, _mat, ParticleSystemRenderMode.Billboard);
            _foam = MakeDriven("KelvinFoam", SprayField.MaxFoam, _foamMat, ParticleSystemRenderMode.HorizontalBillboard);
            _built = true;
        }

        /// <summary>A particle system that simulates nothing itself: SetParticles drives every particle.</summary>
        private ParticleSystem MakeDriven(string name, int max, Material mat, ParticleSystemRenderMode mode)
        {
            var go = new GameObject(name);
            go.transform.SetParent(null, true);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _owned.Add(ps);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max; main.loop = false; main.playOnAwake = false;
            main.startSpeed = 0f; main.gravityModifier = 0f; main.startLifetime = 1000f;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat; r.renderMode = mode;
            if (mode == ParticleSystemRenderMode.Stretch) { r.velocityScale = 0.04f; r.lengthScale = 1.2f; }
            ps.Play();
            ps.Pause();   // SetParticles each frame; the system itself never advances
            return ps;
        }

        private void LateUpdate() => Step(Mathf.Min(Time.deltaTime, 1f / 20f));

        // Explicit time step also lets editor captures exercise the real parcel field.
        private void Step(float dt)
        {
            if (Driver?.Sim == null || Driver.Sim.Aircraft.Config.Floats == null)
            {
                if (_sheetObject) _sheetObject.SetActive(false);
                System.Array.Clear(_sheetTime, 0, _sheetTime.Length);
                if (_built) { foreach (var p in _owned) { if (p) SetRate(p, 0); } if (_spray) _spray.SetParticles(_buf, 0); if (_foam) _foam.SetParticles(_foamBuf, 0); }
                return;
            }
            if (!_built) Build();
            var ac = Driver.Sim.Aircraft;
            if (_fieldAircraft != ac.Config.Id) { _field.Clear(); System.Array.Clear(_sheetTime, 0, _sheetTime.Length); _fieldAircraft = ac.Config.Id; }
            if (dt > 0f) _field.Step(ac.Config, ac.State, dt);
            UpdatePropSpray();
            DrawSheets(ac.State, dt);

            Vec3 forward = ac.State.Attitude.Rotate(new Vec3(1, 0, 0));
            var floatStates = new System.ReadOnlySpan<FloatHydro.FloatState>(FloatHydro.Floats,
                0, ac.Config.Floats.TipFloats != null ? 4 : 2);
            int n = 0;
            foreach (SprayField.Parcel p in _field.Parcels)
            {
                if (!p.Alive || p.Age > FloatSprayVisual.VisibleLife) continue;
                // A parcel is a simulated cloud, never a beach-ball-sized individual drop.
                // Hide distant/outside clouds; do not redirect the physics to fit the picture.
                float fade = 1f - (float)(p.Age / FloatSprayVisual.VisibleLife);
                // Resolve a parcel cloud as several fine specks within a few centimetres,
                // reusing the same 600-sprite drawing budget. The simulated parcel is untouched.
                for (int speck = 0; speck < 4 && n < _buf.Length; speck++)
                {
                    double phase = n * 2.399963 + speck * 1.7;
                    Vec3 at = p.Pos + new Vec3(System.Math.Sin(phase) * .028,
                        System.Math.Cos(phase) * .028, -speck * .009);
                    float visible = (float)FloatSprayVisual.CloudVisibility(at, forward,
                        ac.Config.Floats.BeamM, floatStates);
                    if (visible <= 0) continue;
                    ref ParticleSystem.Particle q = ref _buf[n++];
                    q.position = CoordinateMap.ToUnity(at);
                    q.velocity = CoordinateMap.ToUnity(p.Vel);
                    q.startSize = .014f + .010f * (float)(p.Diameter / SprayField.DropMaxM);
                    q.startColor = new Color(1f, 1f, 1f, visible * Mathf.Min(1f, fade * 5f));
                    q.startLifetime = 1000f; q.remainingLifetime = 999f; q.rotation = 0f;
                }
            }
            _spray.SetParticles(_buf, n);
            int m = 0;
            foreach (SprayField.Foam f in _field.Foams)
            {
                if (!f.Alive) continue;
                ref ParticleSystem.Particle q = ref _foamBuf[m++];
                q.position = CoordinateMap.ToUnity(f.Pos) + Vector3.up * 0.02f;
                q.velocity = Vector3.zero;
                float age = (float)(f.Age / f.Life);
                q.startSize = .42f + .38f * age;
                q.rotation = (m * 137.5f) % 360;
                q.startColor = new Color(1f, 1f, 1f, 0.35f * (1f - age));
                q.startLifetime = 1000f; q.remainingLifetime = 999f;
            }
            _foam.SetParticles(_foamBuf, m);
        }

        // Short broken sheets make the contact-root geometry legible before the airborne parcels disperse.
        // All four arms of a twin-float V start at real roots; there is no centreline float emitter.
        private void BuildSheets()
        {
            _sheetObject = new GameObject("Per-float contact spray sheets");
            _sheets = new Mesh { name = "Float chine spray Vs" }; _sheets.MarkDynamic();
            _sheetObject.AddComponent<MeshFilter>().sharedMesh = _sheets;
            var renderer = _sheetObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial = _mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var triangles = new int[FloatSprayVisual.MaxArms * (FloatSprayVisual.Samples - 1) * 6];
            int k = 0;
            for (int arm = 0; arm < FloatSprayVisual.MaxArms; arm++)
                for (int j = 0; j < FloatSprayVisual.Samples - 1; j++)
                {
                    int v = (arm * FloatSprayVisual.Samples + j) * 2;
                    triangles[k++] = v; triangles[k++] = v + 1; triangles[k++] = v + 2;
                    triangles[k++] = v + 1; triangles[k++] = v + 3; triangles[k++] = v + 2;
                }
            _sheets.vertices = _sheetVertices; _sheets.triangles = triangles;
        }

        private void DrawSheets(RigidBodyState state, float dt)
        {
            _sheetObject.SetActive(true);
            var roots = _field.LastRoots;
            FloatSprayVisual.SelectRoots(roots, state.Attitude, _sheetRoots);
            Vec3 travel = state.Attitude.Rotate(state.Velocity);
            for (int arm = 0; arm < FloatSprayVisual.MaxArms; arm++)
            {
                int index = _sheetRoots[arm];
                _sheetTime[arm] = index < 0 ? 0 : Mathf.Min((float)FloatSprayVisual.SheetLife, _sheetTime[arm] + dt);
                if (index >= 0) FloatSprayVisual.SampleSheet(roots[index], travel, Atmosphere.SteadyWind, _sheetPoints, _sheetTime[arm]);
                for (int j = 0; j < FloatSprayVisual.Samples; j++)
                {
                    int v = (arm * FloatSprayVisual.Samples + j) * 2;
                    if (index < 0)
                    {
                        _sheetVertices[v] = _sheetVertices[v + 1] = Driver.transform.position;
                        _sheetColors[v] = _sheetColors[v + 1] = Color.clear; continue;
                    }
                    var root = roots[index];
                    float t = j / (float)(FloatSprayVisual.Samples - 1);
                    float step = (float)FloatSprayVisual.StepFraction(FloatHydro.Floats[root.Slot]);
                    Vec3 raw = _sheetPoints[j];
                    Vec3 bounded = FloatSprayVisual.BoundSheet(raw, root.Position, state.Attitude.Rotate(new Vec3(1, 0, 0)));
                    Vec3 point = raw + (bounded - raw) * step;
                    Vector3 at = CoordinateMap.ToUnity(point);
                    Vector3 along = CoordinateMap.ToUnity(root.JetVelocity - travel);
                    // Plow retains the actual bow jet's up/out trajectory and a broad fine sheet.
                    // Step narrows/lowers the drawing only as the actual heel releases.
                    float width = Mathf.Lerp(.10f + .40f * Mathf.Sin(t * Mathf.PI),
                        .055f + .12f * Mathf.Sin(t * Mathf.PI), step);
                    Vector3 across = Vector3.Cross(along, Vector3.up).normalized * width;
                    Vector3 flutter = Vector3.Cross(along, Vector3.up).normalized
                        * (.025f * Mathf.Sin(j * 2.1f + arm * 1.7f) * Mathf.Sin(t * Mathf.PI)
                            * Mathf.Clamp01(_sheetTime[arm] / (float)FloatSprayVisual.SheetLife));
                    _sheetVertices[v] = at + flutter - across; _sheetVertices[v + 1] = at + flutter + across;
                    // Keep the shader's round end-mask inside the UV bounds at the contact,
                    // otherwise the first visible sheet begins half a metre behind its real root.
                    _sheetUv[v] = new Vector2(0, .1f + .8f * t); _sheetUv[v + 1] = new Vector2(1, .1f + .8f * t);
                    // No sheet below the water. The actual parcels still handle their own impacts.
                    double? water = FloatHydro.WaterSurfaceAt(point.X, point.Y, -point.Z);
                    float alpha = water.HasValue && -point.Z >= water.Value - .01 ? Mathf.Min(1f, (1 - t) * 5f) : 0;
                    _sheetColors[v] = _sheetColors[v + 1] = new Color(1, 1, 1, alpha);
                }
            }
            _sheets.vertices = _sheetVertices; _sheets.uv = _sheetUv; _sheets.colors = _sheetColors;
            _sheets.RecalculateBounds();
        }

        private ParticleSystem Make(string name, float size, float life, int maxParticles)
        {
            var go = new GameObject(name);
            go.transform.SetParent(null, true);
            var ps = go.AddComponent<ParticleSystem>();
            _owned.Add(ps);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = life;
            main.startSize = new ParticleSystem.MinMaxCurve(size * .25f, size);
            main.startSpeed = 0f;
            main.gravityModifier = 0.9f;
            main.maxParticles = maxParticles * 2;
            main.startColor = new Color(1f, 1f, 1f, 0.7f);
            main.loop = true;
            main.playOnAwake = true;
            var em = ps.emission; em.rateOverTime = 0f;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = _mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            var sh = ps.shape; sh.enabled = false;
            return ps;
        }

        // Read the existing drawn discs, including twin pushers; never duplicate StyleFor hub dimensions.
        private void UpdatePropSpray()
        {
            var ac = Driver.Sim.Aircraft;
            if (_propAircraft != ac.Config.Id || _discs.Length == 0 || !_discs[0])
            {
                foreach (var p in _prop) if (p) { _owned.Remove(p); Destroy(p.gameObject); }
                var discs = new System.Collections.Generic.List<Transform>();
                foreach (var mf in Driver.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.name == "PropDisc") discs.Add(mf.transform);
                _discs = discs.ToArray(); _prop = new ParticleSystem[_discs.Length];
                for (int i = 0; i < _prop.Length; i++) _prop[i] = Make("PropSpray" + i, .35f, .85f, 100);
                _propAircraft = ac.Config.Id;
            }
            float rpm = ac.EngineStopped ? 0f : (float)ac.EngineRpm;
            float power = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(650, 2200, rpm));
            for (int i = 0; i < _discs.Length; i++)
            {
                var disc = _discs[i];
                if (!disc || !disc.gameObject.activeInHierarchy) { SetRate(_prop[i], 0); continue; }
                var mesh = disc.GetComponent<MeshFilter>().sharedMesh;
                float radius = mesh.bounds.extents.x * Mathf.Abs(disc.lossyScale.x);
                Vector3 hub = disc.position;
                double? water = WorldTerrain.Active?.WaterSurfaceAt(hub.z, hub.x);
                // Project the disc's vertical radius, so pitch/roll and pusher installations count.
                float verticalRadius = radius * Mathf.Sqrt(Mathf.Clamp01(1f - disc.forward.y * disc.forward.y));
                float clearance = water.HasValue ? hub.y - verticalRadius - (float)water.Value : float.PositiveInfinity;
                float proximity = 1f - Mathf.SmoothStep(0f, Mathf.Max(.3f, radius * .85f), Mathf.Max(0, clearance));
                // Once the hull throws real spray, its two float contacts own the visible spray.
                // The old central slipstream mist obscured those Vs with a third plume behind the fuselage.
                // Keep prop mist at rest, without changing prop forces, float spray or splash audio.
                float strength = _field.LastRootCount == 0 && water.HasValue && hub.y > water.Value ? power * proximity : 0f;
                // Slipstream entrains water at the bottom of the disc, then carries it aft.
                var ps = _prop[i];
                SetRate(ps, strength * 220);
                ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = _mistMat;
                if (strength <= 0) continue;
                ps.transform.position = new Vector3(hub.x, (float)water.Value + .06f, hub.z);
                var main = ps.main; main.startSize = new ParticleSystem.MinMaxCurve(.04f, .2f + strength * .35f);
                var shape = ps.shape; shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = radius * .35f;
                ps.transform.rotation = Quaternion.Euler(90, 0, 0);
                var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
                Vector3 aft = -Driver.transform.forward * (3 + strength * 9);
                vel.x = new ParticleSystem.MinMaxCurve(aft.x - 1, aft.x + 1);
                vel.y = new ParticleSystem.MinMaxCurve(1f, 2f + strength * 3);
                vel.z = new ParticleSystem.MinMaxCurve(aft.z - 1, aft.z + 1);
            }
        }

        private void OnDisable()
        {
            if (_sheetObject) _sheetObject.SetActive(false);
            foreach (var p in _owned) if (p) p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void OnDestroy()
        {
            if (_sheetObject) Destroy(_sheetObject);
            if (_sheets) Destroy(_sheets);
            foreach (var p in _owned) if (p) Destroy(p.gameObject);
            if (_mat) Destroy(_mat);
            if (_mistMat) Destroy(_mistMat);
            if (_foamMat) Destroy(_foamMat);
        }

        private static void SetRate(ParticleSystem ps, float rate)
        {
            var em = ps.emission; em.rateOverTime = rate;
            if (rate > 0 && !ps.isPlaying) ps.Play();
        }

    }
}
