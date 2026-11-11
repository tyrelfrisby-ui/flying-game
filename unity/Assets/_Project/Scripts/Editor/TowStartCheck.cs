using System.Reflection;
using FlyingGame.Bridge;
using FlyingGame.Core;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// Ty 2026-11-06 (t905u): "Somehow starting a glider on the ground no longer provides a tow plane." Drives the REAL
    /// FlightSimDriver + TowController the way the game does (edit mode: Awake / Start invoked by hand, CLAUDE.md "An
    /// editor batch tool has to drive the component itself"): a runway start in each glider, the TOW condition the touch,
    /// controller and desk buttons all test, and the automatic hook-up a runway start now gets (no TOW tap needed; the
    /// Quest cannot show that button), then require a tug — the right type (the Great Lakes for the
    /// 2-33, t906u), drawn, on the runway ahead of the glider — and a rope on both hooks. Then the reset path: a Restart
    /// must drop the tug and offer TOW again, and a second StartTow must give a tug again.
    /// <c>-executeMethod FlyingGame.EditorTools.TowStartCheck.Run</c>; exit 0 = pass.
    /// </summary>
    public static class TowStartCheck
    {
        private static int _failures;
        private static void Require(bool ok, string message) { if (ok) return; _failures++; Debug.LogError("[TowStart] " + message); }

        private static void Invoke(object o, string method) =>
            o.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.Invoke(o, null);

        /// <summary>What TouchFlightControls tests before it shows TOW (landscape, portrait, controller and desk alike).</summary>
        private static bool TowOffered(FlightSimDriver d, TowController t) =>
            d.Sim?.Aircraft?.Config?.Propulsion == null && (t.Towing || (d.GroundStart && t.Tow == null));

        public static void Run()
        {
            _failures = 0;
            GameObject root = null;
            var start = SessionSettings.StartMode; string ac = SessionSettings.AircraftId, tug = SessionSettings.TugId, ch = SessionSettings.ChallengeId;
            try
            {
                WorldTerrain.Active ??= new WorldTerrain();
                foreach ((string glider, string chosenTug, string expectTug) in new[]
                {
                    ("glider-2-33-like", "pa25-pawnee-like", "great-lakes-like"),   // t906u: the picker does not apply to the 2-33
                    ("glider-2-33-like", "pa18-cub-like", "great-lakes-like"),
                    ("glider-swift-s1-like", "pa25-pawnee-like", "pa25-pawnee-like"),   // every other glider keeps the picker
                })
                {
                    SessionSettings.AircraftId = glider; SessionSettings.StartMode = SessionSettings.Start.OnTheRunway;
                    SessionSettings.ChallengeId = null; SessionSettings.TugId = chosenTug;
                    root = new GameObject("Aircraft");
                    FlightSimDriver driver = root.AddComponent<FlightSimDriver>();
                    TowController tow = root.AddComponent<TowController>();
                    Invoke(tow, "Awake"); Invoke(tow, "Start");
                    driver.ApplySession();   // the landing page's FLY
                    string tag = $"{glider} (picker {chosenTug})";
                    Require(driver.AircraftId == glider, $"{tag}: flying {driver.AircraftId}");
                    Require(driver.GroundStart, $"{tag}: a runway start is not a GroundStart");
                    Require(TowOffered(driver, tow), $"{tag}: TOW / RELEASE is not offered on the runway");
                    Require(tow.TugId == expectTug, $"{tag}: tug would be {tow.TugId}, want {expectTug}");
                    Require(tow.Towing, $"{tag}: a runway start is not on the rope without a TOW tap");

                    for (int pass = 0; pass < 2; pass++)
                    {
                        string p = pass == 0 ? "first FLY" : "after Restart";
                        if (pass == 1)
                        {
                            // The TOW button path too: let go, Restart has already hooked up again; tap TOW on a fresh rig.
                            tow.EndTow();
                            Require(TowOffered(driver, tow), $"{tag} {p}: TOW is not offered with no tug out");
                            try { tow.StartTow(); }
                            catch (System.Exception e) { Require(false, $"{tag} {p}: StartTow threw {e.GetType().Name}: {e.Message}\n{e.StackTrace}"); }
                        }
                        Require(tow.Towing, $"{tag} {p}: no rope on the hook after TOW");
                        Require(tow.TugAircraft != null && tow.TugAircraft.Config.Id == expectTug,
                            $"{tag} {p}: tug is {tow.TugAircraft?.Config?.Id ?? "none"}, want {expectTug}");
                        Require(tow.TugObject != null && tow.TugObject.activeSelf && tow.TugObject.GetComponentsInChildren<Renderer>().Length > 0,
                            $"{tag} {p}: the tug is not drawn");
                        Require(tow.Rope != null && tow.Rope.enabled, $"{tag} {p}: the rope is not drawn");
                        if (tow.TugAircraft != null && driver.Sim != null)
                        {
                            var g = driver.Sim.Aircraft.State.Position; var t = tow.TugAircraft.State.Position;
                            double ahead = (t - g).Length;
                            double agl = -t.Z - WorldTerrain.GroundHeightAt(t.X, t.Y);
                            Debug.Log($"[TowStart] {tag} {p}: tug {tow.TugAircraft.Config.Id} {ahead:F1} m ahead, {agl:F2} m above the ground, rope {(tow.Rope != null && tow.Rope.enabled ? "drawn" : "MISSING")}");
                            Require(ahead > 60 && ahead < 75, $"{tag} {p}: tug {ahead:F1} m from the glider (rope 61 m)");
                            Require(agl > -0.5 && agl < 3, $"{tag} {p}: tug {agl:F2} m above the ground");
                        }
                        // Restart (RESET, a new type, the menu's FLY again): the tug belongs to the last flight.
                        driver.ResetFlight();
                        Require(tow.Towing && tow.TugAircraft != null && tow.TugAircraft.State.Velocity.Length < 0.5,
                            $"{tag} {p}: after the Restart there is no fresh tug at rest on the rope");
                    }
                    Object.DestroyImmediate(root); root = null;
                }
                // A lesson / challenge sets up its own start: no tug of ours.
                Require(!TowController.WantsTowAtStart(true, true, false, "practice:groundroll"), "a lesson start would get a tug");
                Require(!TowController.WantsTowAtStart(true, false, false, null), "a powered aeroplane would get a tug");
                Require(!TowController.WantsTowAtStart(false, true, false, null), "an air start would get a tug");
                Require(TowController.WantsTowAtStart(true, true, false, null), "a glider runway start would get no tug");
            }
            catch (System.Exception e) { Require(false, e.ToString()); }
            finally
            {
                if (root != null) Object.DestroyImmediate(root);
                SessionSettings.StartMode = start; SessionSettings.AircraftId = ac; SessionSettings.TugId = tug; SessionSettings.ChallengeId = ch;
                Debug.Log(_failures == 0 ? "[TowStart] PASS" : $"[TowStart] FAIL ({_failures})");
                EditorApplication.Exit(_failures == 0 ? 0 : 1);
            }
        }
    }
}
