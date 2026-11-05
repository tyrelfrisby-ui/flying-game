using System.Linq;
using System.Reflection;
using FlyingGame.Bridge;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// Ty 2026-11-05 06:35 / 06:37, on the REAL components in edit mode (CLAUDE.md "An editor batch tool has to drive the
    /// component itself": Awake/Start invoked by hand, the egress ticked through <see cref="PilotEgress.TickForTesting"/>):
    ///  1. Every type, flying at 40 m/s and at rest: BAIL OUT; the canopy unlatches, its pins come out and the canopy's own
    ///     physics (Core Vr.CanopyBailout / CanopyDynamics) takes it off; then exactly the parts in <see cref="Leaves"/> (copied
    ///     by hand) are gone from the aeroplane, flown by CanopyFlight, nothing else left or was spawned, and he got out. An
    ///     open cockpit (Great Lakes, Cub, Jenny, Stearman, Aircam) loses nothing.
    ///  2. A fatal crash (the 172 into the ground at 40 m/s, 15 m/s down, nose 20° low, flown by the sim), then BAIL OUT:
    ///     he leaves as a ghost (translucent), his canopy opens and he RISES, the farewell line is up, the HUD stage is off.
    ///  3. A gentle crash (25 m/s, 4 m/s down, the gear goes): no ghost, the crash's grade carried into the landing's.
    ///  4. A bail-out from a flying aeroplane: the crash grade is Unhurt and the verdict is the landing's alone.
    /// <c>-executeMethod FlyingGame.EditorTools.BailoutHeavenCheck.Run</c>; exit 0 = pass.
    /// </summary>
    public static class BailoutHeavenCheck
    {
        private static int _failures;
        private static void Require(bool ok, string message) { if (ok) return; _failures++; Debug.LogError("[BailoutHeaven] " + message); }

        private static void Invoke(object o, string method) =>
            o.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.Invoke(o, null);

        private static void SetId(FlightSimDriver d, string id) => typeof(FlightSimDriver).GetProperty("AircraftId").GetSetMethod(true).Invoke(d, new object[] { id });

        private static FlyingGame.Sim.SimLoop Sim(FlyingGame.Sim.Aircraft ac) => new(ac);

        private static FlyingGame.Sim.Aircraft Flown(AircraftConfig cfg, double v, double sink, double pitchDeg, double h, double seconds)
        {
            double th = pitchDeg * System.Math.PI / 180;
            var q = new Quat(0, System.Math.Sin(th / 2), 0, System.Math.Cos(th / 2));
            var ac = new FlyingGame.Sim.Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -h), q, q.Conjugate().Rotate(new Vec3(v, 0, sink)), Vec3.Zero));
            for (double t = 0; t < seconds; t += 0.005) ac.Step(new FlyingGame.Sim.ControlInputs(0, 0, 0, 1), 0.005);
            return ac;
        }

        private static (GameObject root, FlightSimDriver driver, AirframeVisual visual, PilotEgress egress) Build(string id, FlyingGame.Sim.Aircraft ac)
        {
            var root = new GameObject("Aircraft");
            FlightSimDriver driver = root.AddComponent<FlightSimDriver>();
            SetId(driver, id);
            driver.AdoptSim(Sim(ac));
            AirframeVisual visual = root.AddComponent<AirframeVisual>();
            Invoke(visual, "Awake"); Invoke(visual, "Start");
            PilotEgress egress = root.AddComponent<PilotEgress>();
            Invoke(egress, "Awake"); Invoke(egress, "Start");
            return (root, driver, visual, egress);
        }

        private static void Tick(PilotEgress e, float seconds, System.Func<bool> until = null)
        {
            for (float t = 0; t < seconds; t += 0.02f)
            {
                e.TickForTesting(0.02f);
                if (until != null && until()) return;
            }
        }

        private static void Cleanup(GameObject root)
        {
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t != null && t.parent == null && (t.name == "JettisonedCanopy" || t.name == "Pilot" || t.name == "Parachute" || t.name == "EjectionSeat" || t.name.StartsWith("Debris-")))
                    Object.DestroyImmediate(t.gameObject);
            if (root != null) Object.DestroyImmediate(root);
        }

        private static string[] ModelNodes(AirframeVisual v) =>
            v.ModelRoot == null ? new string[0] : v.ModelRoot.GetComponentsInChildren<Transform>(true).Select(t => CanopyEgress.Bare(t.name)).ToArray();

        // ---- 1. the real canopy / door, every type --------------------------------------------------------------------

        /// <summary>What leaves each type, copied by hand from the GLBs' node lists and the shells' styles (2026-11-05). Any
        /// type not listed loses NOTHING: the open cockpits (Great Lakes, Cub, Jenny, Stearman, Aircam), the fold-out doors
        /// (J-3, PA-18), the cabins whose doors are not drawn apart, and the downloaded models with no separate canopy.</summary>
        private static readonly System.Collections.Generic.Dictionary<string, string[]> Leaves = new()
        {
            ["c172-like"] = new[] { "C172_Door_R", "C172_Door_latch_R", "Glass.Door.R" },
            ["extra-300-like"] = new[] { "Canopy_Detachable" },
            ["pitts-s2b-like"] = new[] { "Canopy_Detachable" },
            ["glider-2-33-like"] = new[] { "Canopy_Frame", "Canopy_Glass", "Canopy_Hardware" },
            ["glider-grob103-acro-like"] = new[] { "Canopy_Frame", "Canopy_Glass" },
            ["cassutt-f1-like"] = new[] { "Canopy" },
            ["glasair3-like"] = new[] { "Canopy" },
            ["glider-swift-s1-like"] = new[] { "Canopy" },
        };

        private static string[] Nodes(GameObject root, AirframeVisual v) => v.ModelRoot != null ? ModelNodes(v)
            : root.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToArray();

        private static void CanopyCheck()
        {
            string dir = System.IO.Path.Combine(Application.streamingAssetsPath, "aircraft");
            int withParts = 0, types = 0;
            foreach (string f in System.IO.Directory.GetFiles(dir, "*.json").OrderBy(x => x))
            {
                string id = System.IO.Path.GetFileNameWithoutExtension(f);
                AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(id);
                if (cfg == null) continue;
                types++;
                string[] want = Leaves.TryGetValue(id, out string[] w) ? w : new string[0];
                if (want.Length > 0) withParts++;
                // Flying (it goes at once) and at rest (his hand shoves it off): both through the canopy's own physics.
                foreach (float speed in new[] { 40f, 0f })
                {
                    GameObject root = null;
                    string what = $"{id} at {speed:F0} m/s";
                    try
                    {
                        var b = Build(id, new FlyingGame.Sim.Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -800), Quat.Identity, new Vec3(speed, 0, 0), Vec3.Zero)));
                        root = b.root;
                        string[] before = Nodes(root, b.visual);
                        foreach (string n in want) Require(before.Contains(n), $"{what}: the model has no '{n}' to lose (has the GLB changed?)");
                        var parts = b.visual.CanopyParts();
                        var named = parts.Select(t => CanopyEgress.Bare(t.name)).ToArray();
                        var leaving = new System.Collections.Generic.HashSet<string>(parts.SelectMany(t => t.GetComponentsInChildren<Transform>(true)).Select(t => CanopyEgress.Bare(t.name)));
                        // A part under another goes with it: the named list is the top of what leaves.
                        Require(named.All(n => want.Contains(n)) && (want.Length == 0) == (named.Length == 0),
                            $"{what}: the airframe would lose [{string.Join(", ", named)}], wanted [{string.Join(", ", want)}]");
                        var rootsBefore = new System.Collections.Generic.HashSet<GameObject>(root.scene.GetRootGameObjects());
                        b.egress.BailOut();
                        Tick(b.egress, PilotEgress.BailJumpSec + 1.5f, () => b.egress.PilotOut);
                        Require(b.egress.PilotOut, $"{what}: he never got out (canopy still on: {!b.visual.Cockpit.CanopyGone})");
                        string[] after = Nodes(root, b.visual);
                        foreach (string n in want) Require(!after.Contains(n), $"{what}: '{n}' is still on the aeroplane after the canopy went");
                        string[] extra = before.Except(after).Where(n => !leaving.Contains(n)).Take(5).ToArray();
                        Require(extra.Length == 0, $"{what}: parts other than its canopy/door left the aeroplane: [{string.Join(", ", extra)}]");
                        var gone = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                            .Where(t => t.parent == null && t.name == "JettisonedCanopy").ToArray();
                        Require(gone.Length == (want.Length > 0 ? 1 : 0), $"{what}: {gone.Length} jettisoned canopies for {want.Length} parts");
                        foreach (Transform g in gone)
                        {
                            Require(g.GetComponent<Renderer>() == null && g.childCount > 0, $"{what}: a stand-in canopy was spawned (a JettisonedCanopy with a mesh of its own)");
                            Require(g.GetComponent<CanopyFlight>() != null && g.GetComponent<DebrisBody>() == null, $"{what}: the canopy is not flown by the canopy's own physics (CanopyFlight)");
                            foreach (Transform c in g) Require(want.Contains(CanopyEgress.Bare(c.name)), $"{what}: '{c.name}' left the aeroplane but is not its canopy or door");
                        }
                        string[] spawned = root.scene.GetRootGameObjects().Where(g => !rootsBefore.Contains(g)).Select(g => g.name)
                            .Where(n => n != "JettisonedCanopy" && n != "Pilot" && n != "Parachute" && n != "EjectionSeat" && n != "PilotRemark" && !n.StartsWith("Debris-")).ToArray();
                        Require(spawned.Length == 0, $"{what}: spawned beside the real parts: [{string.Join(", ", spawned)}]");
                        Debug.Log($"[BailoutHeaven] {what}: {(b.visual.ModelRoot != null ? "model" : "procedural shell")}, " +
                                  $"{(want.Length > 0 ? "jettisoned " + string.Join(" + ", want) : "nothing to jettison")}; he is out ({b.egress.Current})");
                    }
                    finally { Cleanup(root); }
                }
            }
            Debug.Log($"[BailoutHeaven] canopy check: {types} types, {withParts} lose a canopy or door");
            Require(types >= 20, $"only {types} types ran");
        }

        // ---- 2-4. crash, then bail out ------------------------------------------------------------------------------

        private static void CrashThenBailOut(string what, FlyingGame.Sim.Aircraft ac, bool wantGhost, LandingOutcome wantCrash)
        {
            GameObject root = null;
            try
            {
                var b = Build("c172-like", ac);
                root = b.root;
                Debug.Log($"[BailoutHeaven] {what}: the seat took {ac.Occupant.Describe()}");
                b.egress.BailOut();
                Tick(b.egress, PilotEgress.BailJumpSec + 0.1f, () => b.egress.PilotOut);
                Require(b.egress.PilotOut, $"{what}: he never left the aeroplane");
                Require(b.egress.CrashOutcome == wantCrash, $"{what}: crash verdict {b.egress.CrashOutcome}, wanted {wantCrash}");
                Require(b.egress.IsGhost == wantGhost, $"{what}: ghost {b.egress.IsGhost}, wanted {wantGhost}");
                Transform pilot = b.egress.PilotTransform;
                float y0 = pilot.position.y;
                if (wantGhost)
                {
                    foreach (Renderer r in pilot.GetComponentsInChildren<Renderer>(true))
                        foreach (Material m in r.sharedMaterials)
                            Require(m != null && m.shader.name == "FlyingGame/UnlitTransparent" && Mathf.Abs(m.color.a - (float)PilotOutRules.GhostAlpha) < 1e-3f,
                                $"{what}: the ghost's '{r.name}' is drawn with {(m != null ? m.shader.name + " a " + m.color.a : "null")}, not translucent");
                    Tick(b.egress, 30f);
                    float rose = pilot.position.y - y0;
                    Debug.Log($"[BailoutHeaven] {what}: phase {b.egress.Current}, rose {rose:F0} m in 30 s, rising {b.egress.PilotVelocity.y:F1} m/s, " +
                              $"canopy {b.egress.ChuteInflate01 * 100:F0} %, “{b.egress.LastLine}”");
                    Require(b.egress.Current == PilotEgress.Phase.Ascending, $"{what}: phase {b.egress.Current}, wanted Ascending (canopy full, going up)");
                    Require(b.egress.Stage == PilotOutRules.Stage.Ascending && !PilotOutRules.ShowHud(b.egress.Stage), $"{what}: stage {b.egress.Stage}");
                    Require(rose > 40f && b.egress.PilotVelocity.y > 2f, $"{what}: the ghost rose only {rose:F0} m ({b.egress.PilotVelocity.y:F1} m/s)");
                    Require(PilotOutRules.HeavenLines.Contains(b.egress.LastLine), $"{what}: line '{b.egress.LastLine}' is not a farewell");
                    Require(b.egress.Outcome == LandingOutcome.Dead && !b.egress.PilotLanded, $"{what}: outcome {b.egress.Outcome}, landed {b.egress.PilotLanded}");
                }
                else
                {
                    Tick(b.egress, 120f, () => b.egress.Current == PilotEgress.Phase.Landed);
                    Debug.Log($"[BailoutHeaven] {what}: phase {b.egress.Current}, outcome {b.egress.Outcome} (crash {b.egress.CrashOutcome}), “{b.egress.LastLine}”");
                    Require(b.egress.Current == PilotEgress.Phase.Landed, $"{what}: phase {b.egress.Current}, wanted Landed");
                    Require(b.egress.Outcome != LandingOutcome.Dead || wantCrash == LandingOutcome.Dead, $"{what}: he died of a {wantCrash} crash");
                    Require((int)b.egress.Outcome >= (int)b.egress.CrashOutcome, $"{what}: the outcome {b.egress.Outcome} forgot the crash's {b.egress.CrashOutcome}");
                }
            }
            finally { Cleanup(root); }
        }

        public static void Run()
        {
            _failures = 0;
            WorldTerrain terrain = WorldTerrain.Active;
            try
            {
                WorldTerrain.Active = null;   // flat ground at 0, as in the suite's OccupantInjuryTests
                CrashEvents.ClearForTests();
                CanopyCheck();
                AircraftConfig c172 = UnityAircraftConfigLoader.LoadFromStreamingAssets("c172-like");
                CrashThenBailOut("high-g crash", Flown(c172, 40, 15, -20, 3, 8), wantGhost: true, wantCrash: LandingOutcome.Dead);
                CrashThenBailOut("gentle crash", Flown(c172, 25, 4, 0, 3, 8), wantGhost: false, wantCrash: LandingOutcome.Injured);
                CrashThenBailOut("in-flight bail-out", Flown(c172, 55, 0, 0, 300, 2), wantGhost: false, wantCrash: LandingOutcome.Unhurt);
            }
            catch (System.Exception e) { _failures++; Debug.LogError("[BailoutHeaven] threw: " + e); }
            finally { WorldTerrain.Active = terrain; CrashEvents.ClearForTests(); }
            Debug.Log(_failures == 0 ? "[BailoutHeaven] PASS" : $"[BailoutHeaven] FAIL ({_failures})");
            if (Application.isBatchMode) EditorApplication.Exit(_failures == 0 ? 0 : 1);
        }
    }
}
