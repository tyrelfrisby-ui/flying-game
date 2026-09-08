using System.Collections.Generic;
using FlyingGame.Core.DataContracts;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Builds the visible airframe for the CURRENT aircraft type and rebuilds it whenever the driver
    /// switches types. First pass at type-accurate shapes (owner request, from 3-view drawings):
    ///  - wings (and their aileron rows) are LOFTED from the config's strip geometry — position, chord,
    ///    taper, sweep, dihedral — so the planform on screen is exactly the one the sim flies;
    ///  - tails come from a per-type 3-view table (most configs still carry a generic placeholder
    ///    tail; the glider's is real, so it lofts from its config);
    ///  - fuselage is a body of revolution from per-type station radii, plus canopy, propeller disc /
    ///    spinner or jet nose, engine nacelles (from the config's engine positions), wing struts,
    ///    biplane interplane struts, and landing gear (from the config's gear positions — the
    ///    glider's wing-tip wheels included).
    /// All meshes are generated in code (reviewable in git), unlit per the mobile budget, double-sided.
    /// Sim frame is x forward / y right / z DOWN; local Unity = (y, -z, x) — same map as CoordinateMap.
    /// </summary>
    [RequireComponent(typeof(FlightSimDriver))]
    public sealed class AirframeVisual : MonoBehaviour
    {
        private FlightSimDriver _driver;
        private readonly AirframeBuilder _builder = new();
        private string _builtId;

        private void Awake()
        {
            _driver = GetComponent<FlightSimDriver>();
        }

        private void Start()
        {
            _driver.AircraftChanged += OnAircraftChanged;
            Rebuild();
        }

        private void OnDestroy()
        {
            if (_driver != null) _driver.AircraftChanged -= OnAircraftChanged;
        }

        private void OnAircraftChanged()
        {
            if (_driver.AircraftId != _builtId) Rebuild();
        }

        private void LateUpdate()
        {
            if (_driver.Sim == null) return;
            var d = _driver.Sim.Aircraft.CurrentDeflections;
            _builder.SetDeflections((float)d.AileronRad, (float)d.ElevatorRad, (float)d.RudderRad, (float)d.SpoilerFraction);
        }

        private void Rebuild()
        {
            _builtId = _driver.AircraftId;
            float halfSpan = _builder.Build(transform, _driver.Sim.Aircraft.Config);
            GetComponent<GroundShadow>()?.Refresh();
            if (Camera.main != null && Camera.main.TryGetComponent(out ChaseCamera chase))
            {
                chase.FitTo(halfSpan * 2f);
            }
        }
    }

    /// <summary>Plain-C# airframe mesh builder (usable from the editor render tool as well as at runtime).</summary>
    public sealed class AirframeBuilder
    {
        private Transform _root;
        private readonly List<GameObject> _parts = new();
        private Material _glass, _propDisc;

        /// <summary>A hinged part: rotates about its hinge line by the live sim deflection × gain.</summary>
        private sealed class ControlPart
        {
            public Transform T; public string Surface; public float Gain; public Vector3 AxisUnity;
        }
        private readonly List<ControlPart> _controls = new();
        // Sim rotation "TE toward the thickness axis" maps to Unity through the (improper) sim→Unity axis
        // swap, which flips the rotation sense — hence the -1 (verified with the Render Airframes tool).
        private const float HingeSign = -1f;
        private const float SpoilerMaxRad = 1.05f; // 60° paddle rise at full deploy

        /// <summary>Move every hinged surface to the sim's current deflections (radians; spoiler 0..1).</summary>
        public void SetDeflections(float aileronRad, float elevatorRad, float rudderRad, float spoilerFrac)
        {
            foreach (ControlPart c in _controls)
            {
                if (c.T == null) continue;
                float d = c.Surface switch
                {
                    "aileron" => aileronRad,
                    "elevator" => elevatorRad,
                    "rudder" => rudderRad,
                    "spoiler" => -spoilerFrac * SpoilerMaxRad, // paddles rise (TE up)
                    _ => 0f,
                } * c.Gain;
                c.T.localRotation = Quaternion.AngleAxis(HingeSign * d * Mathf.Rad2Deg, c.AxisUnity);
            }
        }

        /// <summary>Destroy previously built parts.</summary>
        public void Clear()
        {
            foreach (GameObject p in _parts) if (p != null) Kill(p);
            _parts.Clear();
            _controls.Clear();
        }

        /// <summary>Build the airframe for `cfg` under `root`; returns the half-span (m) for camera fitting.</summary>
        public float Build(Transform root, AircraftConfig cfg)
        {
            Clear();
            _root = root;
            Style st = StyleFor(cfg.Id);

            _glass ??= new Material(Shader.Find("FlyingGame/UnlitTransparent") ?? Shader.Find("Unlit/Color")) { color = new Color(0.25f, 0.4f, 0.55f, 0.55f) };
            _propDisc ??= new Material(Shader.Find("FlyingGame/UnlitTransparent") ?? Shader.Find("Unlit/Color")) { color = new Color(0.15f, 0.15f, 0.15f, 0.35f) };

            float halfSpan = 0f;
            foreach (SurfaceConfig sf in cfg.Surfaces)
            {
                foreach (StripConfig s in sf.Strips) halfSpan = Mathf.Max(halfSpan, Mathf.Abs((float)s.Pos[1]));
            }

            BuildFuselage(st);
            BuildWings(cfg, st);
            BuildTail(cfg, st);
            BuildCanopy(st);
            BuildPropulsion(cfg, st);
            BuildStruts(cfg, st, halfSpan);
            BuildGear(cfg, st);
            BuildSpoilers(cfg, st, halfSpan);
            return halfSpan;
        }

        private static void Kill(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }

        // ---- fuselage: body of revolution through (x, r) stations ------------------------------

        private void BuildFuselage(Style st)
        {
            var mb = new MeshBuilder();
            const int seg = 14;
            var rings = new List<Vector3[]>();
            foreach ((float x, float r) in st.Body)
            {
                var ring = new Vector3[seg];
                for (int i = 0; i < seg; i++)
                {
                    float a = i * Mathf.PI * 2f / seg;
                    // slightly taller than wide (cabins), vertical centre a touch below the axis for jets/big bodies
                    ring[i] = U(x, Mathf.Sin(a) * r * st.BodyWidthScale, -Mathf.Cos(a) * r * st.BodyHeightScale + st.BodyAxisZ);
                }
                rings.Add(ring);
            }
            mb.AddLoft(rings);
            mb.AddCap(rings[0], U(st.Body[0].x + (st.BluntNose ? 0.05f : st.Body[0].r * 0.8f), 0f, st.BodyAxisZ));
            mb.AddCap(rings[^1], U(st.Body[^1].x - st.Body[^1].r * 0.5f, 0f, st.BodyAxisZ));
            Spawn("Fuselage", mb.ToMesh(Vector3.zero), st.Fuselage, Vector3.zero);

            if (st.BellyScoop)
            {
                Box("Scoop", new Vector3(-1.4f, 0f, 0.62f + st.BodyAxisZ), new Vector3(2.4f, 0.55f, 0.45f), st.Fuselage);
            }
        }

        // ---- wings from config strips ---------------------------------------------------------

        private void BuildWings(AircraftConfig cfg, Style st)
        {
            foreach (SurfaceConfig sf in cfg.Surfaces)
            {
                string id = sf.Id.ToLowerInvariant();
                if (!id.Contains("wing")) continue;
                bool control = id.Contains("aileron");
                Color col = control ? st.Control : st.Wing;
                LoftSurface(cfg, sf, isVertical: false, control ? 0.08f : 0.12f, cambered: !control, col, control ? sf.Id : "Wing:" + sf.Id, control ? "aileron" : null);
            }
        }

        // ---- tail: 3-view table if present, otherwise the config's own surfaces ----------------

        private void BuildTail(AircraftConfig cfg, Style st)
        {
            if (st.Tail == null)
            {
                foreach (SurfaceConfig sf in cfg.Surfaces)
                {
                    string id = sf.Id.ToLowerInvariant();
                    if (id.Contains("wing")) continue;
                    bool vertical = id.Contains("vstab") || id.Contains("rudder");
                    bool control = id.Contains("elevator") || id.Contains("rudder");
                    string ctrlSurface = id.Contains("elevator") ? "elevator" : id.Contains("rudder") ? "rudder" : null;
                    LoftSurface(cfg, sf, vertical, control ? 0.08f : 0.12f, cambered: false, control ? st.Control : st.TailColor, sf.Id, ctrlSurface);
                }
                return;
            }

            TailSpec t = st.Tail;
            float stabX = -100f, finX = -100f;
            foreach (SurfaceConfig sf in cfg.Surfaces)
            {
                string id = sf.Id.ToLowerInvariant();
                if (id == "hstab") stabX = (float)sf.Strips[0].Pos[0];
                if (id == "vstab") finX = (float)sf.Strips[0].Pos[0];
            }
            if (stabX < -99f) stabX = st.Body[^1].x + 1.5f;
            if (finX < -99f) finX = stabX;

            // Fin: root sits on the fuselage top at finX; trapezoid up to FinHeight, LE swept back.
            float rootZ = -BodyRadiusAt(st, finX) * st.BodyHeightScale + st.BodyAxisZ + 0.05f;
            float finRootLe = finX + 0.25f * t.FinRoot;
            float finTipLe = finRootLe - t.FinHeight * Mathf.Tan(t.FinSweepDeg * Mathf.Deg2Rad);
            {
                var stations = new List<Station>
                {
                    new(finRootLe, 0f, rootZ, t.FinRoot),
                    new(finTipLe, 0f, rootZ - t.FinHeight, t.FinTip),
                };
                LoftStations(stations, isVertical: true, 0.10f, false, 0.65f, st.TailColor, st.Control, "Fin", "Rudder", "rudder", 1f);
            }

            // Stabilizer: two halves from the centreline, trapezoid, dihedral; T-tail sits on the fin top.
            float stabZ = t.TTail ? rootZ - t.FinHeight + 0.05f : st.BodyAxisZ;
            float stabRootLe = (t.TTail ? finTipLe + 0.1f : stabX + 0.25f * t.StabRoot);
            float half = t.StabSpan * 0.5f;
            float tipLe = stabRootLe - half * Mathf.Tan(t.StabSweepDeg * Mathf.Deg2Rad);
            float tipZ = stabZ - half * Mathf.Tan(t.StabDihedralDeg * Mathf.Deg2Rad);
            {
                var stations = new List<Station>
                {
                    new(tipLe, -half, tipZ, t.StabTip),
                    new(stabRootLe, 0f, stabZ, t.StabRoot),
                    new(tipLe, half, tipZ, t.StabTip),
                };
                LoftStations(stations, isVertical: false, 0.11f, false, 0.68f, st.TailColor, st.Control, "Stab", "Elevator", "elevator", 1f);
            }
        }

        // ---- canopy / propulsion / struts / gear -----------------------------------------------

        private void BuildCanopy(Style st)
        {
            if (st.Canopy == null) return;
            (float x, float z, float len, float wid, float hgt) = st.Canopy.Value;
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Kill(go.GetComponent<Collider>());
            Attach(go, "Canopy");
            go.transform.localPosition = U(x, 0f, z + st.BodyAxisZ);
            go.transform.localScale = new Vector3(wid, hgt, len); // unity (right, up, fwd)
            go.GetComponent<MeshRenderer>().sharedMaterial = _glass;
        }

        private void BuildPropulsion(AircraftConfig cfg, Style st)
        {
            if (cfg.Engines != null && cfg.Engines.Count > 0)
            {
                foreach (EngineMount e in cfg.Engines)
                {
                    float ex = (float)e.Pos[0], ey = (float)e.Pos[1], ez = (float)e.Pos[2];
                    // Nacelle: capsule along x, sized off the style.
                    var nac = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    Kill(nac.GetComponent<Collider>());
                    Attach(nac, "Nacelle");
                    ez += st.NacelleDrop;
                    nac.transform.localPosition = U(ex - st.NacelleLength * 0.25f, ey, ez);
                    nac.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // capsule axis y → z (forward)
                    nac.transform.localScale = new Vector3(st.NacelleRadius * 2f, st.NacelleLength * 0.5f, st.NacelleRadius * 2f);
                    nac.GetComponent<MeshRenderer>().sharedMaterial = UnlitMat(st.Fuselage);
                    if (st.PropRadius > 0f)
                    {
                        Prop(ex + st.NacelleLength * 0.3f, ey, ez, st.PropRadius, st.RadialEngine);
                    }
                }
            }
            else if (st.PropRadius > 0f)
            {
                Prop(st.Body[0].x + 0.12f, 0f, st.BodyAxisZ, st.PropRadius, st.RadialEngine);
            }
        }

        private void Prop(float x, float y, float z, float radius, bool radial)
        {
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Kill(disc.GetComponent<Collider>());
            Attach(disc, "PropDisc");
            disc.transform.localPosition = U(x, y, z);
            disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // cylinder axis y → forward
            disc.transform.localScale = new Vector3(radius * 2f, 0.015f, radius * 2f);
            disc.GetComponent<MeshRenderer>().sharedMaterial = _propDisc;

            // Two blades (static) so the disc reads as a propeller, plus a spinner.
            for (int i = 0; i < 2; i++)
            {
                var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Kill(blade.GetComponent<Collider>());
                Attach(blade, "Blade");
                blade.transform.localPosition = U(x, y, z);
                blade.transform.localRotation = Quaternion.Euler(0f, 0f, 35f + i * 90f);
                blade.transform.localScale = new Vector3(radius * 2f * 0.98f, radius * 0.12f, 0.04f);
                blade.GetComponent<MeshRenderer>().sharedMaterial = UnlitMat(new Color(0.12f, 0.12f, 0.12f));
            }
            var spinner = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Kill(spinner.GetComponent<Collider>());
            Attach(spinner, "Spinner");
            spinner.transform.localPosition = U(x + radius * 0.08f, y, z);
            spinner.transform.localScale = new Vector3(radius * 0.28f, radius * 0.28f, radius * 0.5f);
            spinner.GetComponent<MeshRenderer>().sharedMaterial = UnlitMat(radial ? new Color(0.25f, 0.25f, 0.27f) : Color.white);
            if (radial)
            {
                var cowl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Kill(cowl.GetComponent<Collider>());
                Attach(cowl, "Radial");
                cowl.transform.localPosition = U(x - radius * 0.22f, y, z);
                cowl.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                cowl.transform.localScale = new Vector3(radius * 0.8f, radius * 0.16f, radius * 0.8f);
                cowl.GetComponent<MeshRenderer>().sharedMaterial = UnlitMat(new Color(0.2f, 0.2f, 0.22f));
            }
        }

        private void BuildStruts(AircraftConfig cfg, Style st, float halfSpan)
        {
            var strut = new Color(0.35f, 0.35f, 0.38f);
            if (st.HighWingStruts)
            {
                (float wx, float wz, float dih) = WingRoot(cfg, "wing");
                float r = BodyRadiusAt(st, wx);
                foreach (float side in new[] { -1f, 1f })
                {
                    float y = side * halfSpan * 0.45f;
                    Strut(new Vector3(wx - 0.25f, side * r * 0.85f, r * 0.85f + st.BodyAxisZ), new Vector3(wx, y, wz - Mathf.Abs(y) * Mathf.Tan(dih) + 0.04f), 0.07f, strut);
                }
            }
            if (st.BiplaneStruts)
            {
                (float ux, float uz, float udih) = WingRoot(cfg, "wing-upper");
                (float lx, float lz, float ldih) = WingRoot(cfg, "wing-lower");
                float chord = 1.0f;
                foreach (float side in new[] { -1f, 1f })
                {
                    float y = side * halfSpan * 0.6f;
                    float uzz = uz - Mathf.Abs(y) * Mathf.Tan(udih), lzz = lz - Mathf.Abs(y) * Mathf.Tan(ldih);
                    Strut(new Vector3(ux + chord * 0.2f, y, uzz), new Vector3(lx + chord * 0.2f, y, lzz), 0.06f, strut);
                    Strut(new Vector3(ux - chord * 0.4f, y, uzz), new Vector3(lx - chord * 0.4f, y, lzz), 0.06f, strut);
                    Strut(new Vector3(ux + chord * 0.2f, y, uzz), new Vector3(lx - chord * 0.4f, y, lzz), 0.05f, strut); // diagonal
                    // Cabane struts: fuselage top to the upper wing centre section.
                    float rb = BodyRadiusAt(st, ux);
                    Strut(new Vector3(ux + 0.2f, side * 0.35f, -rb * st.BodyHeightScale + st.BodyAxisZ), new Vector3(ux + 0.2f, side * 0.5f, uz), 0.05f, strut);
                    Strut(new Vector3(ux - 0.5f, side * 0.35f, -rb * st.BodyHeightScale + st.BodyAxisZ), new Vector3(ux - 0.5f, side * 0.5f, uz), 0.05f, strut);
                }
            }
        }

        private void BuildGear(AircraftConfig cfg, Style st)
        {
            if (cfg.Gear == null) return;
            float length = st.Body[0].x - st.Body[^1].x;
            float wheelR = Mathf.Clamp(length * 0.045f, 0.15f, 0.65f);
            var tire = new Color(0.1f, 0.1f, 0.1f);
            var leg = new Color(0.4f, 0.4f, 0.42f);
            foreach (GearConfig g in cfg.Gear)
            {
                float gx = (float)g.Pos[0], gy = (float)g.Pos[1], gz = (float)g.Pos[2];
                if (gz < 0f)
                {
                    // Above the CG = wing-tip wheel/skid (the glider): a small ball under the tip.
                    var tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Kill(tip.GetComponent<Collider>());
                    Attach(tip, "TipWheel");
                    tip.transform.localPosition = U(gx, gy, gz - 0.06f);
                    tip.transform.localScale = Vector3.one * 0.16f;
                    tip.GetComponent<MeshRenderer>().sharedMaterial = UnlitMat(tire);
                    continue;
                }

                float r = g.IsTailwheel ? wheelR * 0.45f : wheelR;
                var wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Kill(wheel.GetComponent<Collider>());
                Attach(wheel, "Wheel");
                wheel.transform.localPosition = U(gx, gy, gz - r);
                wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f); // cylinder axis y → x (sideways)
                wheel.transform.localScale = new Vector3(r * 2f, r * 0.3f, r * 2f);
                wheel.GetComponent<MeshRenderer>().sharedMaterial = UnlitMat(tire);

                // Leg from the wheel hub up to the body/wing.
                float rb = BodyRadiusAt(st, gx);
                float ay = Mathf.Sign(gy) * Mathf.Min(Mathf.Abs(gy), rb * 0.8f);
                float az = Mathf.Abs(gy) > rb * 1.2f && st.LowWingGear ? gz - r - 0.35f : rb * 0.9f * st.BodyHeightScale + st.BodyAxisZ;
                Strut(new Vector3(gx, gy, gz - r), new Vector3(gx, Mathf.Abs(gy) > rb * 1.2f && st.LowWingGear ? gy : ay, az), g.IsTailwheel ? 0.05f : 0.1f, leg);
            }
        }

        /// <summary>Speed-brake paddles (types with a spoiler axis): hinged plates on the upper wing that rise with deploy.</summary>
        private void BuildSpoilers(AircraftConfig cfg, Style st, float halfSpan)
        {
            if (cfg.Controls?.Spoiler == null || cfg.Controls.Spoiler.MaxDeflRad <= 0.0) return;
            (float wx, float wz, float dih) = WingRoot(cfg, "wing");
            float chord = 1.2f;
            foreach (SurfaceConfig sf in cfg.Surfaces) if (sf.Id.ToLowerInvariant() == "wing") chord = (float)sf.Strips[0].Chord;
            float len = halfSpan * 0.28f, height = chord * 0.16f, thick = 0.03f;
            foreach (float side in new[] { -1f, 1f })
            {
                float y = side * halfSpan * 0.42f;
                float z = wz - Mathf.Abs(y) * Mathf.Tan(dih) - chord * 0.12f * 0.5f; // on the upper surface
                float xHinge = wx - chord * 0.15f;                                    // ~40 % chord
                var pivot = new GameObject("Spoiler");
                Attach(pivot, "Spoiler");
                pivot.transform.localPosition = U(xHinge, y, z);
                var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Kill(plate.GetComponent<Collider>());
                plate.name = "SpoilerPlate";
                plate.transform.SetParent(pivot.transform, false);
                plate.transform.localPosition = new Vector3(0f, 0f, -height * 0.5f); // plate lies flat, trailing aft of the hinge
                plate.transform.localScale = new Vector3(len, thick, height);
                plate.GetComponent<MeshRenderer>().sharedMaterial = UnlitMat(st.Control);
                _parts.Add(plate);
                _controls.Add(new ControlPart { T = pivot.transform, Surface = "spoiler", Gain = 1f, AxisUnity = Vector3.right });
            }
        }

        // ---- lofting helpers -------------------------------------------------------------------

        private readonly struct Station
        {
            public readonly float Le, Y, Z, Chord;   // sim coords: LE x, span position, vertical, chord
            public Station(float le, float y, float z, float chord) { Le = le; Y = y; Z = z; Chord = chord; }
        }

        /// <summary>Loft a config surface from its strips; wing rows are trimmed at their control row's LE.</summary>
        private void LoftSurface(AircraftConfig cfg, SurfaceConfig sf, bool isVertical, float tRatio, bool cambered, Color col, string name, string ctrlSurface)
        {
            // Sort strips along the span axis.
            var strips = new List<StripConfig>(sf.Strips);
            strips.Sort((a, b) => (isVertical ? a.Pos[2].CompareTo(b.Pos[2]) : a.Pos[1].CompareTo(b.Pos[1])));

            // Control row belonging to this surface (to notch the parent's trailing edge).
            SurfaceConfig ctrl = null;
            string id = sf.Id.ToLowerInvariant();
            if (!(id.Contains("aileron") || id.Contains("elevator") || id.Contains("rudder")))
            {
                foreach (SurfaceConfig o in cfg.Surfaces)
                {
                    string oid = o.Id.ToLowerInvariant();
                    if (oid == id + "-aileron" || (id == "hstab" && oid == "elevator") || (id == "vstab" && oid.StartsWith("rudder")))
                    {
                        ctrl = o; break;
                    }
                }
            }

            // Control rows (and biplane halves) can be two separate spanwise groups (left/right ailerons
            // with the fuselage between) — loft each contiguous run on its own or the mesh bridges the gap.
            var groups = new List<List<StripConfig>>();
            // Split where the spanwise gap is far larger than the surface's typical strip spacing (the
            // fuselage between left/right ailerons); strip AREAS are not reliable widths in every config.
            float typical = 0f;
            if (strips.Count > 1)
            {
                var gaps = new List<float>();
                for (int i = 1; i < strips.Count; i++)
                {
                    float a = isVertical ? (float)strips[i - 1].Pos[2] : (float)strips[i - 1].Pos[1];
                    float b = isVertical ? (float)strips[i].Pos[2] : (float)strips[i].Pos[1];
                    gaps.Add(Mathf.Abs(b - a));
                }
                gaps.Sort();
                typical = gaps[gaps.Count / 2];
            }
            foreach (StripConfig s in strips)
            {
                float sp = isVertical ? (float)s.Pos[2] : (float)s.Pos[1];
                if (groups.Count > 0)
                {
                    StripConfig prev = groups[^1][^1];
                    float psp = isVertical ? (float)prev.Pos[2] : (float)prev.Pos[1];
                    if (Mathf.Abs(sp - psp) > 2.5f * Mathf.Max(typical, 0.05f)) groups.Add(new List<StripConfig>());
                }
                else groups.Add(new List<StripConfig>());
                groups[^1].Add(s);
            }
            int gi = 0;
            foreach (List<StripConfig> group in groups)
            {
                float gain = 0f;
                if (ctrlSurface != null)
                {
                    int n = 0;
                    foreach (StripConfig st in group) if (st.Control != null) { gain += (float)st.Control.Gain; n++; }
                    gain = n > 0 ? gain / n : 1f;
                }
                LoftGroup(group, ctrl, isVertical, tRatio, cambered, col, groups.Count > 1 ? $"{name}-{gi++}" : name, ctrlSurface, gain);
            }
        }

        private void LoftGroup(List<StripConfig> strips, SurfaceConfig ctrl, bool isVertical, float tRatio, bool cambered, Color col, string name, string ctrlSurface, float ctrlGain)
        {
            var stations = new List<Station>();
            foreach (StripConfig s in strips)
            {
                float x = (float)s.Pos[0], y = (float)s.Pos[1], z = (float)s.Pos[2], c = (float)s.Chord;
                float le = x + 0.25f * c;
                float te = x - 0.75f * c;
                if (ctrl != null)
                {
                    float ctrlLe = ControlLeAt(ctrl, isVertical ? z : y, isVertical);
                    if (!float.IsNaN(ctrlLe)) te = Mathf.Max(te, ctrlLe + 0.01f);
                }
                float dih = (float)s.DihedralRad;
                float zz = isVertical ? z : z - Mathf.Abs(y) * Mathf.Tan(dih);
                stations.Add(new Station(le, y, zz, le - te));
            }

            // Extend both ends by half a strip width so the tip reaches the real span.
            if (stations.Count >= 2)
            {
                StripConfig first = strips[0], last = strips[^1];
                float wFirst = first.Chord > 1e-6 ? (float)(first.Area / first.Chord) : 0.3f;
                float wLast = last.Chord > 1e-6 ? (float)(last.Area / last.Chord) : 0.3f;
                Station a = stations[0], b = stations[^1];
                bool spansCentre = !isVertical && a.Y < 0f && b.Y > 0f;
                float slopeA = isVertical ? 0f : -Mathf.Tan((float)first.DihedralRad);
                float slopeB = isVertical ? 0f : -Mathf.Tan((float)last.DihedralRad);
                if (isVertical)
                {
                    stations.Insert(0, new Station(a.Le, a.Y, a.Z + wFirst * 0.5f, a.Chord));
                    stations.Add(new Station(b.Le, b.Y, b.Z - wLast * 0.5f, b.Chord * 0.85f));
                }
                else
                {
                    stations.Insert(0, new Station(a.Le, a.Y - wFirst * 0.5f, a.Z + (spansCentre ? slopeA : 0f) * wFirst * 0.5f, a.Chord * 0.85f));
                    stations.Add(new Station(b.Le, b.Y + wLast * 0.5f, b.Z + slopeB * wLast * 0.5f, b.Chord * 0.85f));
                }
            }

            LoftStations(stations, isVertical, tRatio, cambered, 1f, col, col, name, null, ctrlSurface, ctrlGain);
        }

        private static float ControlLeAt(SurfaceConfig ctrl, float spanPos, bool isVertical)
        {
            float best = float.NaN, bestD = float.MaxValue;
            foreach (StripConfig s in ctrl.Strips)
            {
                float sp = isVertical ? (float)s.Pos[2] : (float)s.Pos[1];
                float w = s.Chord > 1e-6 ? (float)(s.Area / s.Chord) : 0.3f;
                float d = Mathf.Abs(sp - spanPos);
                if (d <= w * 0.75f && d < bestD)
                {
                    bestD = d;
                    best = (float)s.Pos[0] + 0.25f * (float)s.Chord;
                }
            }
            return best;
        }

        /// <summary>
        /// Loft an airfoil-section ring through the stations. mainFrac &lt; 1 splits the chord into a fixed
        /// part (first colour) and a trailing control part (second colour), e.g. stab + elevator.
        /// </summary>
        private void LoftStations(List<Station> stations, bool isVertical, float tRatio, bool cambered, float mainFrac, Color col, Color ctrlCol, string name, string ctrlName, string ctrlSurface = null, float ctrlGain = 1f)
        {
            if (stations.Count < 2) return;
            var main = new MeshBuilder();
            var ctrl = new MeshBuilder();
            var mainRings = new List<Vector3[]>();
            var ctrlRings = new List<Vector3[]>();
            var hinge = new List<Vector3>();   // hinge line points (local Unity) for the hinged part
            foreach (Station s in stations)
            {
                float cMain = s.Chord * mainFrac;
                mainRings.Add(Section(s.Le, s.Y, s.Z, cMain, cMain * tRatio, isVertical, cambered));
                if (mainFrac < 1f)
                {
                    float cCtrl = s.Chord - cMain;
                    ctrlRings.Add(Section(s.Le - cMain - 0.01f, s.Y, s.Z, cCtrl, cCtrl * tRatio * 0.8f, isVertical, false));
                    hinge.Add(U(s.Le - cMain - 0.01f, s.Y, s.Z));
                }
                else
                {
                    hinge.Add(U(s.Le, s.Y, s.Z));
                }
            }

            bool wholeIsControl = mainFrac >= 1f && ctrlSurface != null;
            main.AddLoft(mainRings);
            main.AddCapRing(mainRings[0]);
            main.AddCapRing(mainRings[^1]);
            if (wholeIsControl)
            {
                SpawnHinged(name, main, hinge, isVertical, col, ctrlSurface, ctrlGain);
            }
            else
            {
                Spawn(name, main.ToMesh(Vector3.zero), col, Vector3.zero);
            }

            if (mainFrac < 1f)
            {
                ctrl.AddLoft(ctrlRings);
                ctrl.AddCapRing(ctrlRings[0]);
                ctrl.AddCapRing(ctrlRings[^1]);
                SpawnHinged(ctrlName ?? name + "-ctrl", ctrl, hinge, isVertical, ctrlCol, ctrlSurface ?? "", ctrlGain);
            }
        }

        /// <summary>Spawn a loft whose pivot is the hinge-line midpoint and register it as a control part.</summary>
        private void SpawnHinged(string name, MeshBuilder mb, List<Vector3> hinge, bool isVertical, Color col, string surface, float gain)
        {
            Vector3 pivot = Vector3.zero;
            foreach (Vector3 h in hinge) pivot += h;
            pivot /= hinge.Count;
            // Hinge direction (local Unity): along the hinge line, oriented to sim +y (right) for horizontal
            // surfaces and sim -z (up) for vertical ones — the axis about which +deflection moves the TE
            // toward the thickness axis (down / right).
            Vector3 axis = hinge[^1] - hinge[0];
            if (axis.sqrMagnitude < 1e-6f) axis = isVertical ? Vector3.up : Vector3.right;
            axis.Normalize();
            if (isVertical ? axis.y < 0f : axis.x < 0f) axis = -axis;
            GameObject go = Spawn(name, mb.ToMesh(pivot), col, pivot);
            _controls.Add(new ControlPart { T = go.transform, Surface = surface, Gain = gain, AxisUnity = axis });
        }

        /// <summary>8-point airfoil-ish section at one station (sim coords → local Unity).</summary>
        private static Vector3[] Section(float le, float y, float z, float chord, float t, bool isVertical, bool cambered)
        {
            // (chord fraction from LE, thickness offset as fraction of t; negative = toward the upper surface)
            float[] f = { 0f, 0.1f, 0.35f, 0.65f, 1f, 0.65f, 0.35f, 0.1f };
            float[] up = cambered
                ? new[] { 0f, -0.45f, -0.5f, -0.3f, 0f, 0.15f, 0.28f, 0.22f }
                : new[] { 0f, -0.4f, -0.5f, -0.3f, 0f, 0.3f, 0.5f, 0.4f };
            var ring = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                float x = le - f[i] * chord;
                float d = up[i] * t;
                ring[i] = isVertical ? U(x, y + d, z) : U(x, y, z + d);
            }
            return ring;
        }

        private static (float x, float z, float dih) WingRoot(AircraftConfig cfg, string id)
        {
            foreach (SurfaceConfig sf in cfg.Surfaces)
            {
                if (sf.Id.ToLowerInvariant() != id) continue;
                StripConfig root = sf.Strips[0];
                foreach (StripConfig s in sf.Strips) if (System.Math.Abs(s.Pos[1]) < System.Math.Abs(root.Pos[1])) root = s;
                return ((float)root.Pos[0], (float)root.Pos[2], (float)root.DihedralRad);
            }
            return (0f, 0f, 0f);
        }

        private static float BodyRadiusAt(Style st, float x)
        {
            var b = st.Body;
            if (x >= b[0].x) return b[0].r;
            for (int i = 0; i < b.Length - 1; i++)
            {
                if (x <= b[i].x && x >= b[i + 1].x)
                {
                    float t = (b[i].x - x) / Mathf.Max(1e-4f, b[i].x - b[i + 1].x);
                    return Mathf.Lerp(b[i].r, b[i + 1].r, t);
                }
            }
            return b[^1].r;
        }

        // ---- primitives / spawning -------------------------------------------------------------

        private void Strut(Vector3 aSim, Vector3 bSim, float thick, Color col)
        {
            Vector3 a = U(aSim.x, aSim.y, aSim.z), b = U(bSim.x, bSim.y, bSim.z);
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Kill(go.GetComponent<Collider>());
            Attach(go, "Strut");
            go.transform.localPosition = (a + b) * 0.5f;
            Vector3 d = b - a;
            go.transform.localRotation = d.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(d) : Quaternion.identity;
            go.transform.localScale = new Vector3(thick, thick, d.magnitude);
            go.GetComponent<MeshRenderer>().sharedMaterial = UnlitMat(col);
        }

        private void Box(string name, Vector3 centreSim, Vector3 sizeSim, Color col)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Kill(go.GetComponent<Collider>());
            Attach(go, name);
            go.transform.localPosition = U(centreSim.x, centreSim.y, centreSim.z);
            go.transform.localScale = new Vector3(sizeSim.y, sizeSim.z, sizeSim.x);
            go.GetComponent<MeshRenderer>().sharedMaterial = UnlitMat(col);
        }

        private GameObject Spawn(string name, Mesh mesh, Color col, Vector3 pivot)
        {
            var go = new GameObject(name);
            Attach(go, name);
            go.transform.localPosition = pivot;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = UnlitMat(col);
            return go;
        }

        private void Attach(GameObject go, string name)
        {
            go.name = name;
            go.transform.SetParent(_root, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            _parts.Add(go);
        }

        private static readonly Dictionary<Color, Material> Mats = new();
        private static Material UnlitMat(Color c)
        {
            if (!Mats.TryGetValue(c, out Material m))
            {
                m = new Material(Shader.Find("FlyingGame/Lit") ?? Shader.Find("Unlit/Color")) { color = c };
                Mats[c] = m;
            }
            return m;
        }

        /// <summary>Sim (x fwd, y right, z down) → local Unity (right, up, fwd).</summary>
        private static Vector3 U(float x, float y, float z) => new(y, -z, x);

        private sealed class MeshBuilder
        {
            private readonly List<Vector3> _v = new();
            private readonly List<int> _t = new();

            public void AddLoft(List<Vector3[]> rings)
            {
                int n = rings[0].Length;
                int baseIdx = _v.Count;
                foreach (Vector3[] r in rings) _v.AddRange(r);
                for (int i = 0; i < rings.Count - 1; i++)
                for (int j = 0; j < n; j++)
                {
                    int a = baseIdx + i * n + j, b = baseIdx + i * n + (j + 1) % n;
                    int c = baseIdx + (i + 1) * n + j, d = baseIdx + (i + 1) * n + (j + 1) % n;
                    Quad(a, b, d, c);
                }
            }

            public void AddCap(Vector3[] ring, Vector3 apex)
            {
                int baseIdx = _v.Count;
                _v.AddRange(ring);
                _v.Add(apex);
                int n = ring.Length, ap = baseIdx + n;
                for (int j = 0; j < n; j++) Tri(baseIdx + j, baseIdx + (j + 1) % n, ap);
            }

            public void AddCapRing(Vector3[] ring)
            {
                Vector3 c = Vector3.zero;
                foreach (Vector3 p in ring) c += p;
                AddCap(ring, c / ring.Length);
            }

            private void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }

            // Single winding; the Lit shader is two-sided (Cull Off + VFACE normal flip).
            private void Tri(int a, int b, int c) { _t.Add(a); _t.Add(b); _t.Add(c); }

            /// <summary>Mesh with vertices expressed relative to `origin` (the part's pivot).</summary>
            public Mesh ToMesh(Vector3 origin)
            {
                var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                var verts = new List<Vector3>(_v.Count);
                foreach (Vector3 v in _v) verts.Add(v - origin);
                m.SetVertices(verts);
                m.SetTriangles(_t, 0);
                m.RecalculateNormals();
                m.RecalculateBounds();
                return m;
            }
        }

        // ------------------------------------------------------------------ 3-view style table

        private sealed class TailSpec
        {
            public float StabSpan, StabRoot, StabTip, StabSweepDeg, StabDihedralDeg;
            public float FinHeight, FinRoot, FinTip, FinSweepDeg;
            public bool TTail;
        }

        private sealed class Style
        {
            public (float x, float r)[] Body;               // fuselage stations nose→tail (sim x, radius)
            public float BodyWidthScale = 1f, BodyHeightScale = 1.1f, BodyAxisZ = 0f;
            public bool BluntNose, RadialEngine, BellyScoop, HighWingStruts, BiplaneStruts, LowWingGear;
            public (float x, float z, float len, float wid, float hgt)? Canopy;
            public float PropRadius, NacelleRadius = 0.4f, NacelleLength = 2f, NacelleDrop = 0f; // NacelleDrop: visual z offset below the config engine point
            public TailSpec Tail;
            public Color Fuselage, Wing, TailColor, Control;
        }

        private static Style StyleFor(string id)
        {
            Color white = new(0.93f, 0.93f, 0.9f), red = new(0.8f, 0.1f, 0.1f), silver = new(0.75f, 0.77f, 0.8f),
                  yellow = new(0.95f, 0.8f, 0.15f), blue = new(0.15f, 0.25f, 0.55f), navy = new(0.1f, 0.15f, 0.35f),
                  cream = new(0.92f, 0.9f, 0.85f), dark = new(0.3f, 0.3f, 0.32f), olive = new(0.35f, 0.4f, 0.28f);
            switch (id)
            {
                case "c172-like":
                    return new Style
                    {
                        BodyAxisZ = 0.0f, Body = new[] { (2.3f, 0.12f), (2.0f, 0.4f), (1.2f, 0.55f), (0.3f, 0.62f), (-0.7f, 0.6f), (-1.6f, 0.45f), (-3.0f, 0.28f), (-4.5f, 0.17f), (-5.9f, 0.1f) },
                        Canopy = (0.2f, -0.45f, 1.8f, 1.1f, 0.5f), PropRadius = 0.95f, HighWingStruts = true,
                        Tail = new TailSpec { StabSpan = 3.45f, StabRoot = 1.3f, StabTip = 0.85f, StabSweepDeg = 5, FinHeight = 1.55f, FinRoot = 1.9f, FinTip = 0.8f, FinSweepDeg = 35 },
                        Fuselage = white, Wing = white, TailColor = white, Control = new Color(0.2f, 0.35f, 0.7f),
                    };
                case "pa18-cub-like":
                    return new Style
                    {
                        BodyAxisZ = -0.25f, Body = new[] { (2.0f, 0.1f), (1.7f, 0.38f), (1.0f, 0.48f), (0.2f, 0.5f), (-0.8f, 0.45f), (-1.8f, 0.32f), (-3.2f, 0.2f), (-4.5f, 0.12f), (-5.0f, 0.08f) },
                        Canopy = (0.1f, -0.4f, 1.4f, 0.9f, 0.45f), PropRadius = 0.9f, HighWingStruts = true,
                        Tail = new TailSpec { StabSpan = 3.0f, StabRoot = 0.95f, StabTip = 0.7f, FinHeight = 1.3f, FinRoot = 1.4f, FinTip = 0.6f, FinSweepDeg = 25 },
                        Fuselage = yellow, Wing = yellow, TailColor = yellow, Control = dark,
                    };
                case "decathlon-8kcab-like":
                    return new Style
                    {
                        BodyAxisZ = -0.23f, Body = new[] { (2.0f, 0.1f), (1.7f, 0.4f), (1.0f, 0.5f), (0.2f, 0.52f), (-0.8f, 0.46f), (-1.8f, 0.33f), (-3.2f, 0.2f), (-4.6f, 0.12f), (-5.1f, 0.08f) },
                        Canopy = (0.1f, -0.42f, 1.5f, 0.95f, 0.45f), PropRadius = 0.95f, HighWingStruts = true,
                        Tail = new TailSpec { StabSpan = 3.1f, StabRoot = 1.0f, StabTip = 0.7f, FinHeight = 1.35f, FinRoot = 1.4f, FinTip = 0.6f, FinSweepDeg = 30 },
                        Fuselage = red, Wing = white, TailColor = white, Control = red,
                    };
                case "extra-300-like":
                    return new Style
                    {
                        BodyAxisZ = -0.2f, Body = new[] { (2.4f, 0.08f), (2.1f, 0.32f), (1.2f, 0.45f), (0.2f, 0.5f), (-0.8f, 0.42f), (-1.8f, 0.3f), (-3.0f, 0.18f), (-4.5f, 0.1f), (-4.9f, 0.06f) },
                        Canopy = (-0.3f, -0.45f, 1.4f, 0.7f, 0.5f), PropRadius = 1.0f, LowWingGear = true,
                        Tail = new TailSpec { StabSpan = 3.2f, StabRoot = 1.1f, StabTip = 0.75f, FinHeight = 1.3f, FinRoot = 1.5f, FinTip = 0.6f, FinSweepDeg = 30 },
                        Fuselage = red, Wing = red, TailColor = red, Control = white,
                    };
                case "pitts-s2b-like":
                    return new Style
                    {
                        BodyAxisZ = -0.05f, Body = new[] { (1.6f, 0.1f), (1.3f, 0.35f), (0.6f, 0.42f), (-0.2f, 0.42f), (-1.2f, 0.33f), (-2.3f, 0.22f), (-3.5f, 0.13f), (-4.1f, 0.08f) },
                        Canopy = (-0.6f, -0.4f, 0.9f, 0.6f, 0.35f), PropRadius = 0.95f, BiplaneStruts = true,
                        Tail = new TailSpec { StabSpan = 2.6f, StabRoot = 1.0f, StabTip = 0.7f, FinHeight = 1.1f, FinRoot = 1.3f, FinTip = 0.5f, FinSweepDeg = 35 },
                        Fuselage = red, Wing = red, TailColor = red, Control = white,
                    };
                case "stearman-pt17-like":
                    return new Style
                    {
                        BodyAxisZ = -0.2f, Body = new[] { (2.4f, 0.35f), (2.2f, 0.5f), (1.3f, 0.55f), (0.2f, 0.55f), (-1.0f, 0.45f), (-2.5f, 0.3f), (-4.2f, 0.17f), (-5.3f, 0.1f) },
                        BluntNose = true, RadialEngine = true, Canopy = (-0.7f, -0.5f, 0.8f, 0.6f, 0.3f), PropRadius = 1.35f, BiplaneStruts = true,
                        Tail = new TailSpec { StabSpan = 3.6f, StabRoot = 1.2f, StabTip = 0.8f, FinHeight = 1.3f, FinRoot = 1.6f, FinTip = 0.7f, FinSweepDeg = 40 },
                        Fuselage = blue, Wing = yellow, TailColor = yellow, Control = red,
                    };
                case "p51d-like":
                    return new Style
                    {
                        BodyAxisZ = -0.75f, Body = new[] { (3.1f, 0.1f), (2.7f, 0.35f), (1.8f, 0.5f), (0.5f, 0.55f), (-0.8f, 0.5f), (-2.2f, 0.38f), (-4.0f, 0.24f), (-6.0f, 0.14f), (-6.8f, 0.08f) },
                        Canopy = (-0.4f, -0.5f, 1.6f, 0.7f, 0.5f), PropRadius = 1.7f, BellyScoop = true, LowWingGear = true,
                        Tail = new TailSpec { StabSpan = 4.0f, StabRoot = 1.4f, StabTip = 0.8f, FinHeight = 1.8f, FinRoot = 2.0f, FinTip = 0.9f, FinSweepDeg = 30 },
                        Fuselage = silver, Wing = silver, TailColor = silver, Control = dark,
                    };
                case "f86-sabre-like":
                    return new Style
                    {
                        BodyAxisZ = -0.75f, Body = new[] { (3.6f, 0.45f), (3.0f, 0.6f), (1.5f, 0.75f), (0f, 0.78f), (-2f, 0.7f), (-4.5f, 0.5f), (-6.5f, 0.33f), (-7.9f, 0.22f) },
                        BluntNose = true, BodyHeightScale = 1.0f, Canopy = (1.8f, -0.7f, 1.8f, 0.7f, 0.5f), LowWingGear = true,
                        Tail = new TailSpec { StabSpan = 3.9f, StabRoot = 1.6f, StabTip = 0.8f, StabSweepDeg = 35, FinHeight = 2.4f, FinRoot = 2.6f, FinTip = 1.0f, FinSweepDeg = 40 },
                        Fuselage = silver, Wing = silver, TailColor = silver, Control = dark,
                    };
                case "seminole-like":
                    return new Style
                    {
                        BodyAxisZ = -0.85f, Body = new[] { (2.5f, 0.12f), (2.2f, 0.4f), (1.2f, 0.6f), (0.2f, 0.68f), (-1.0f, 0.62f), (-2.2f, 0.45f), (-3.8f, 0.28f), (-5.2f, 0.16f), (-5.9f, 0.1f) },
                        Canopy = (0.5f, -0.55f, 2.0f, 1.2f, 0.45f), PropRadius = 0.95f, NacelleRadius = 0.4f, NacelleLength = 2.2f, LowWingGear = true,
                        Tail = new TailSpec { StabSpan = 3.9f, StabRoot = 1.0f, StabTip = 0.7f, FinHeight = 1.9f, FinRoot = 2.0f, FinTip = 1.0f, FinSweepDeg = 35, TTail = true },
                        Fuselage = white, Wing = white, TailColor = white, Control = new Color(0.6f, 0.15f, 0.2f),
                    };
                case "dc3-like":
                    return new Style
                    {
                        BodyAxisZ = -1.85f, Body = new[] { (6.5f, 0.3f), (5.8f, 0.8f), (4.0f, 1.1f), (1.5f, 1.15f), (-2f, 1.15f), (-6f, 0.9f), (-9f, 0.6f), (-12f, 0.35f), (-13.1f, 0.2f) },
                        Canopy = (5.0f, -1.0f, 1.5f, 1.6f, 0.5f), PropRadius = 1.75f, RadialEngine = true, NacelleRadius = 0.75f, NacelleLength = 3.5f,
                        Tail = new TailSpec { StabSpan = 8.0f, StabRoot = 2.6f, StabTip = 1.3f, FinHeight = 3.3f, FinRoot = 3.6f, FinTip = 1.4f, FinSweepDeg = 30 },
                        Fuselage = silver, Wing = silver, TailColor = silver, Control = dark,
                    };
                case "boeing-737-like":
                    return new Style
                    {
                        BodyAxisZ = -3.0f, Body = new[] { (22f, 0.4f), (21f, 1.3f), (18.5f, 1.85f), (12f, 1.88f), (-10f, 1.88f), (-13f, 1.5f), (-15.5f, 1.0f), (-17.3f, 0.5f) },
                        BodyHeightScale = 1.0f, Canopy = (19.5f, -1.2f, 1.6f, 2.4f, 0.6f), NacelleRadius = 1.0f, NacelleLength = 4.0f, NacelleDrop = 1.0f, LowWingGear = true,
                        Tail = new TailSpec { StabSpan = 14.3f, StabRoot = 3.6f, StabTip = 1.2f, StabSweepDeg = 30, StabDihedralDeg = 7, FinHeight = 7.0f, FinRoot = 5.2f, FinTip = 2.0f, FinSweepDeg = 35 },
                        Fuselage = white, Wing = silver, TailColor = navy, Control = dark,
                    };
                default: // glider-2-33-like (and anything unknown): high-wing strut-braced tandem trainer
                    return new Style
                    {
                        BodyAxisZ = 0.15f, Body = new[] { (2.2f, 0.05f), (1.9f, 0.25f), (1.2f, 0.45f), (0.3f, 0.48f), (-0.8f, 0.38f), (-2.0f, 0.24f), (-3.5f, 0.14f), (-5.0f, 0.09f), (-5.7f, 0.06f) },
                        Canopy = (1.0f, -0.42f, 1.6f, 0.8f, 0.55f), HighWingStruts = true, Tail = null,
                        Fuselage = red, Wing = cream, TailColor = cream, Control = red,
                    };
            }
        }
    }
}
