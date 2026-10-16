using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The two hazards inside the maze behind the waterfall: the falling rock on the E branch, and the sheet
    /// of water on the way home.
    ///
    /// Both follow the owner's rule that a hazard is deterministic or telegraphed — a hazard that kills at
    /// random is a dice roll, and this game is about flying. The rockfall runs on a fixed cycle off the clock,
    /// with dust and a rumble a second before every fall, so a pilot who watches it once can time it for
    /// good. The sheet of water does no damage at all: it takes your sight for a second, and it sits before a
    /// bend so you have to remember the turn rather than see it.
    ///
    /// The rocks are real solids while they fall, which means the airframe meets them through the same
    /// contact physics as the ground. Nothing special happens to an aeroplane that hits one — it is hit by a
    /// rock, and the structure answers for it.
    /// </summary>
    public sealed class TunnelHazards : MonoBehaviour
    {
        /// <summary>Nothing is simulated or drawn beyond this: the hazards cost nothing when you are elsewhere.</summary>
        public const float ActiveRangeM = 600f;

        public FlightSimDriver Driver;

        /// <summary>0..1 of the screen lost to the sheet of water, for the overlay. Decays after the pass.</summary>
        public float Blind { get; private set; }

        private WaterfallTunnel.Leg _rockLeg, _sheetLeg;
        private bool _found;
        private Transform _root;
        private readonly List<GameObject> _rocks = new();
        /// <summary>Each rock in SIM space while it falls: centre, half-sizes, and whether it is in the air.
        /// The solid reads this, so nothing writes into the shared box list and nothing can dangle.</summary>
        private readonly List<(double x, double y, double up, double hx, double hy, double hz, bool live)> _rockState = new();
        private GameObject _sheet, _dust;
        private Texture2D _white;
        private float _lastCyclePhase;

        private void Start()
        {
            if (WorldTerrain.Active == null) { enabled = false; return; }
            WorldTerrain.Waterfall f = WaterfallTunnel.Fall(WorldTerrain.Active);
            foreach (WaterfallTunnel.Leg l in WaterfallTunnel.Legs(f))
            {
                if (l.Kind == WaterfallTunnel.Kind.Rockfall) _rockLeg = l;
                if (l.Kind == WaterfallTunnel.Kind.WaterSheet) _sheetLeg = l;
            }
            _found = _rockLeg.Length > 0 && _sheetLeg.Length > 0;
            if (!_found) { enabled = false; return; }

            _root = new GameObject("TunnelHazards").transform;
            _root.SetParent(transform.parent, false);
            BuildRocks();
            BuildSheet();
            SetVisible(false);
        }

        private static Vector3 U(double x, double y, double up) => WorldBuilder.U(x, y, up);

        private static Vector3 Mid(in WaterfallTunnel.Leg l, double up) =>
            U((l.X0 + l.X1) / 2, (l.Y0 + l.Y1) / 2, up);

        private void BuildRocks()
        {
            var rock = WorldBuilder.Mat("FlyingGame/Lit", new Color(0.3f, 0.27f, 0.24f));
            var dustMat = WorldBuilder.Mat("FlyingGame/UnlitTransparent", new Color(0.55f, 0.5f, 0.45f, 0.5f));
            for (int i = 0; i < WaterfallTunnel.Rockfall.Count; i++)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(g.GetComponent<Collider>());      // the sim's own contact model does this
                g.name = $"Rock{i}";
                g.transform.SetParent(_root, false);
                float size = 2.4f + i * 0.7f;
                g.transform.localScale = new Vector3(size, size * 0.8f, size * 1.1f);
                g.GetComponent<MeshRenderer>().sharedMaterial = rock;
                _rocks.Add(g);
                _rockState.Add((0, 0, 0, 0, 0, 0, false));
            }
            _dust = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(_dust.GetComponent<Collider>());
            _dust.name = "RockDust";
            _dust.transform.SetParent(_root, false);
            _dust.GetComponent<MeshRenderer>().sharedMaterial = dustMat;
            WorldSolids.Shapes.Add(new RockSolid(this));
        }

        private void BuildSheet()
        {
            // A curtain across the bore, from the arch to the floor.
            double len = _sheetLeg.Length;
            double dx = (_sheetLeg.X1 - _sheetLeg.X0) / len, dy = (_sheetLeg.Y1 - _sheetLeg.Y0) / len;
            double rx = -dy, ry = dx;
            double cxm = (_sheetLeg.X0 + _sheetLeg.X1) / 2, cym = (_sheetLeg.Y0 + _sheetLeg.Y1) / 2;
            double floor = _sheetLeg.FloorAt(0.5), hw = _sheetLeg.HalfWidth;
            var mesh = new Mesh { name = "WaterSheet" };
            var v = new List<Vector3>(); var tris = new List<int>(); var uvs = new List<Vector2>();
            const int n = 10;
            for (int i = 0; i <= n; i++)
            {
                double u = -hw + 2 * hw * i / n;
                double top = floor + _sheetLeg.ClearHeight(System.Math.Abs(u)) - 0.5;
                v.Add(U(cxm + rx * u, cym + ry * u, floor));
                v.Add(U(cxm + rx * u, cym + ry * u, top));
                uvs.Add(new Vector2((float)i / n, 0)); uvs.Add(new Vector2((float)i / n, 1));
                if (i == 0) continue;
                int b = (i - 1) * 2;
                tris.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
            }
            mesh.SetVertices(v); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            _sheet = new GameObject("WaterSheet");
            _sheet.transform.SetParent(_root, false);
            _sheet.AddComponent<MeshFilter>().sharedMesh = mesh;
            _sheet.AddComponent<MeshRenderer>().sharedMaterial =
                WorldBuilder.Mat("FlyingGame/Waterfall", new Color(0.8f, 0.9f, 1f, 0.7f));
        }

        private void SetVisible(bool on)
        {
            if (_root != null && _root.gameObject.activeSelf != on) _root.gameObject.SetActive(on);
        }

        private void Update()
        {
            if (!_found || Driver == null || Driver.Sim == null) return;
            Vector3 p = Driver.transform.position;
            Vector3 rockMid = Mid(_rockLeg, _rockLeg.FloorAt(0.5));
            Vector3 sheetMid = Mid(_sheetLeg, _sheetLeg.FloorAt(0.5));
            bool near = (p - rockMid).sqrMagnitude < ActiveRangeM * ActiveRangeM
                        || (p - sheetMid).sqrMagnitude < ActiveRangeM * ActiveRangeM;
            SetVisible(near);
            if (!near) { ClearRockSolids(); Blind = Mathf.Max(0, Blind - Time.deltaTime); return; }

            StepRockfall(rockMid);
            StepSheet(p);
        }

        /// <summary>Draws what <see cref="WaterfallTunnel.Rockfall"/> says: the cycle, the warning and the
        /// positions are the Core rule, and this is the scene following it. The lane the rocks never fall in
        /// is part of that rule, which is why it can be tested rather than eyeballed.</summary>
        private void StepRockfall(Vector3 mid)
        {
            double t = Time.time;
            double floor = _rockLeg.FloorAt(0.5), crown = floor + _rockLeg.Crown;
            double len = _rockLeg.Length;
            double dx = (_rockLeg.X1 - _rockLeg.X0) / len, dy = (_rockLeg.Y1 - _rockLeg.Y0) / len;
            double rx = -dy, ry = dx;
            double cxm = (_rockLeg.X0 + _rockLeg.X1) / 2, cym = (_rockLeg.Y0 + _rockLeg.Y1) / 2;

            // The telegraph: dust at the roof over the rocks' own side, growing, for the last second.
            bool telegraph = WaterfallTunnel.Rockfall.Telegraphing(t);
            _dust.SetActive(telegraph);
            if (telegraph)
            {
                float k = (float)(1 - WaterfallTunnel.Rockfall.ToFall(t) / WaterfallTunnel.Rockfall.TelegraphS);
                double du = WaterfallTunnel.Rockfall.LateralOf(WaterfallTunnel.Rockfall.Count / 2, _rockLeg);
                _dust.transform.position = U(cxm + rx * du, cym + ry * du, crown - 3);
                _dust.transform.localScale = new Vector3((float)_rockLeg.HalfWidth * 0.8f, 6f * k, 14f);
            }

            for (int i = 0; i < _rocks.Count; i++)
            {
                GameObject g = _rocks[i];
                if (!WaterfallTunnel.Rockfall.TryRock(i, t, _rockLeg, out double up, out double u))
                {
                    g.SetActive(false);
                    _rockState[i] = (0, 0, 0, 0, 0, 0, false);
                    continue;
                }
                double x = cxm + rx * u, y = cym + ry * u;
                g.SetActive(true);
                g.transform.position = U(x, y, up);
                g.transform.Rotate(37f * Time.deltaTime, 53f * Time.deltaTime, 11f * Time.deltaTime);
                Vector3 s3 = g.transform.localScale;
                // Unity (east, up, north) back to sim (north, east, up): the box's own half-sizes.
                _rockState[i] = (x, y, up, s3.z * 0.5, s3.x * 0.5, s3.y * 0.5, true);
            }
        }

        private void ClearRockSolids()
        {
            for (int i = 0; i < _rockState.Count; i++) _rockState[i] = (0, 0, 0, 0, 0, 0, false);
        }

        /// <summary>
        /// The falling rocks, as a solid. A rock in the air is a box the airframe can hit, so hitting one goes
        /// through exactly the same contact model as hitting the ground — the structure answers for it and
        /// nothing in the sim has to know what a rock is.
        /// </summary>
        private sealed class RockSolid : WorldSolids.IShape
        {
            private readonly TunnelHazards _h;
            public RockSolid(TunnelHazards h) { _h = h; }

            public bool Penetrate(double x, double y, double up, out Vec3 normalNed, out double depth)
            {
                normalNed = Vec3.Zero; depth = 0;
                foreach (var r in _h._rockState)
                {
                    if (!r.live) continue;
                    double dx = x - r.x, dy = y - r.y, dz = up - r.up;
                    if (System.Math.Abs(dx) > r.hx || System.Math.Abs(dy) > r.hy || System.Math.Abs(dz) > r.hz) continue;
                    double px = r.hx - System.Math.Abs(dx), py = r.hy - System.Math.Abs(dy), pz = r.hz - System.Math.Abs(dz);
                    double m = System.Math.Min(px, System.Math.Min(py, pz));
                    if (m == pz) normalNed = new Vec3(0, 0, dz >= 0 ? -1 : 1);
                    else if (m == px) normalNed = new Vec3(System.Math.Sign(dx), 0, 0);
                    else normalNed = new Vec3(0, System.Math.Sign(dy), 0);
                    depth = m;
                    return true;
                }
                return false;
            }
        }

        private void StepSheet(Vector3 p)
        {
            // Through the sheet: a second of white, then it clears. No damage, ever.
            double len = _sheetLeg.Length;
            double dx = (_sheetLeg.X1 - _sheetLeg.X0) / len, dy = (_sheetLeg.Y1 - _sheetLeg.Y0) / len;
            double cxm = (_sheetLeg.X0 + _sheetLeg.X1) / 2, cym = (_sheetLeg.Y0 + _sheetLeg.Y1) / 2;
            var sp = Driver.Sim.Aircraft.State.Position;
            double along = (sp.X - cxm) * dx + (sp.Y - cym) * dy;
            double lateral = System.Math.Abs((sp.X - cxm) * -dy + (sp.Y - cym) * dx);
            bool inside = System.Math.Abs(along) < 12 && lateral < _sheetLeg.HalfWidth
                          && -sp.Z > _sheetLeg.FloorAt(0.5) - 2 && -sp.Z < _sheetLeg.FloorAt(0.5) + _sheetLeg.Crown;
            if (inside) Blind = 1f;
            else Blind = Mathf.Max(0f, Blind - Time.deltaTime / 1.1f);
        }

        private void OnGUI()
        {
            if (Blind <= 0.01f || SessionSettings.MenuOpen || Event.current.type != EventType.Repaint) return;
            if (_white == null) { _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply(); }
            GUI.depth = 100;   // under the HUD, over the world
            GUI.color = new Color(0.86f, 0.93f, 1f, 0.95f * Blind);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _white);
            GUI.color = Color.white;
        }

        private void OnDestroy() => ClearRockSolids();
    }
}
