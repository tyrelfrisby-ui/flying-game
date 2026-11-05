using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlyingGame.Bridge;
using FlyingGame.Core;
using FlyingGame.Core.Vr;
using NUnit.Framework;

namespace FlyingGame.EditModeTests
{
    /// <summary>
    /// THE PITTS S-2B COCKPIT, AS TY LAID IT OUT (2026-11-04 19:22): "Front seat: airspeed, altimeter, g-meter and tach,
    /// nothing more, no switches; front has prop control and throttle along with stick and rudder. … Pitts rear has
    /// master, alternator, fuel pump and smoke, that's it."
    ///
    /// Read the way the game reads it: the rig table through <see cref="AirframeRig.Parse"/> (the parser the aeroplane is
    /// rigged with), the live dials from <see cref="CockpitLive.Pitts"/>, and what is DRAWN from each of the four liveries'
    /// GLBs (a node with vertices; retired controls are kept as empty nodes so no index moves). Expected lists are
    /// literals, copied by hand from Ty's words (CLAUDE.md: a test that reads the file it is checking proves nothing).
    ///
    /// The canopy latch, the canopy jettison and the harness are the airframe's, not the panel's, and stay in both seats.
    /// </summary>
    public sealed class PittsCockpitInventoryTests
    {
        private static readonly string[] Liveries =
        {
            "pitts-s2b-like-own.glb", "pitts-s2b-like-own-n1191.glb",
            "pitts-s2b-like-own-checker-unmarked.glb", "pitts-s2b-like-own-factory-unmarked.glb",
        };

        private static ModelRig Rig()
        {
            string json = File.ReadAllText(Path.Combine(GlbFile.Repo, "docs", "models", "pitts-s2b-own-rig.json"));
            ModelRig rig = AirframeRig.Parse(json);
            Assert.IsNotNull(rig, "the Pitts rig table does not parse");
            return rig;
        }

        private static IEnumerable<GlbFile> Glbs() =>
            Liveries.Select(f => new GlbFile(Path.Combine(GlbFile.Models, "pitts-s2b-like", f)));

        // ---- the front seat ----

        [Test]
        public void TheFrontPanelIsAirspeedAltimeterGMeterAndTachNothingMore()
        {
            foreach (GlbFile g in Glbs())
            {
                string[] faces = g.NodeNames.Where(n => n.StartsWith("FrontPanel_") && n.EndsWith("_Face") && g.Vertices(n) > 0)
                                            .OrderBy(n => n).ToArray();
                CollectionAssert.AreEqual(new[] { "FrontPanel_Airspeed_Face", "FrontPanel_Altimeter_Face", "FrontPanel_G_meter_Face",
                                                  "FrontPanel_Tachometer_Face" }, faces, Path.GetFileName(g.Path));
            }
        }

        [Test]
        public void TheFrontDialsAreLiveAndReadTheRightThings()
        {
            var front = CockpitLive.Pitts().Dials.Where(d => d.Face.StartsWith("FrontPanel_")).ToList();
            var want = new Dictionary<string, string[]>
            {
                ["FrontPanel_Airspeed_Face"] = new[] { "airspeed_mph" },
                ["FrontPanel_Altimeter_Face"] = new[] { "altitude_ft" },
                ["FrontPanel_G_meter_Face"] = new[] { "g_load", "g_max_seen", "g_min_seen" },
                ["FrontPanel_Tachometer_Face"] = new[] { "rpm" },
            };
            CollectionAssert.AreEquivalent(want.Keys, front.Select(d => d.Face));
            foreach (LiveDial d in front)
                CollectionAssert.AreEquivalent(want[d.Face], d.Needles.Select(n => n.Param).Distinct(), d.Face);
        }

        [Test]
        public void TheFrontSeatHasNoSwitchesAndNoPanelControlsAtAll()
        {
            ModelRig rig = Rig();
            CollectionAssert.IsEmpty(rig.Controls.Where(c => c.Seat == "Front").Select(c => c.Node));
            foreach (GlbFile g in Glbs())
            {
                string[] drawn = g.NodeNames.Where(n => n.StartsWith("Ctl_") && n.EndsWith("_Front") && g.Vertices(n) > 0).ToArray();
                CollectionAssert.IsEmpty(drawn, Path.GetFileName(g.Path) + " still draws front-seat panel controls");
            }
        }

        [Test]
        public void TheFrontSeatHasThrottlePropStickAndRudder()
        {
            ModelRig rig = Rig();
            var frontParams = rig.Nodes.Where(n => n.Node.EndsWith("_Front")).Select(n => n.Param).Distinct().ToList();
            // What the pilot flies with, by Ty's list; and nothing else but the airframe's canopy and harness.
            CollectionAssert.IsSubsetOf(new[] { "throttle", "prop", "aileron", "elevator", "rudder" }, frontParams);
            var airframe = new[] { "canopy_latch", "canopy_jettison", "harness_latch" };
            CollectionAssert.IsEmpty(frontParams.Except(new[] { "throttle", "prop", "aileron", "elevator", "rudder" }).Except(airframe),
                                     "the front seat has rig parts beyond Ty's list");
            Assert.AreEqual("throttle", rig.Nodes.Single(n => n.Node == "Throttle_Front").Param);
            Assert.AreEqual("prop", rig.Nodes.Single(n => n.Node == "PropLever_Front").Param);
            foreach (GlbFile g in Glbs())
                foreach (string n in new[] { "Throttle_Front", "PropLever_Front", "Stick_Front", "StickRoll_Front", "Pedal_L_Front", "Pedal_R_Front" })
                    Assert.Greater(g.Vertices(n), 0, $"{Path.GetFileName(g.Path)}: {n} draws nothing");
        }

        [Test]
        public void FrontAndRearLeversReadTheSameSimState()
        {
            // Linked by construction: both seats' levers are rig nodes on ONE param, which the sim owns
            // (Aircraft.Throttle01, Aircraft.PropLever01), so moving either seat's moves the other's.
            ModelRig rig = Rig();
            Assert.AreEqual("throttle", rig.Nodes.Single(n => n.Node == "Throttle_Rear").Param);
            Assert.AreEqual("prop", rig.Controls.Single(c => c.Seat == "Rear" && c.Kind == PanelKind.PushPull && c.Node == "Ctl_PushPull_Prop").Param);
            Assert.AreEqual("prop", rig.Nodes.Single(n => n.Node == "PropLever_Front").Param);
        }

        // ---- the rear switches ----

        [Test]
        public void TheRearSwitchesAreMasterAlternatorFuelPumpAndSmokeLabelled()
        {
            ModelRig rig = Rig();
            var switches = rig.Controls.Where(c => c.Kind == PanelKind.Toggle).ToList();
            Assert.IsTrue(switches.All(c => c.Seat == "Rear"));
            CollectionAssert.AreEqual(new[] { "master", "alternator", "fuel_pump", "smoke" }, switches.Select(c => c.Param));
            CollectionAssert.AreEqual(new[] { "MASTER", "ALTERNATOR", "FUEL PUMP", "SMOKE" }, switches.Select(c => c.Label));
            foreach (GlbFile g in Glbs())
            {
                foreach (PanelControlSpec c in switches)
                    Assert.Greater(g.Vertices(c.Node), 0, $"{Path.GetFileName(g.Path)}: {c.Label} ({c.Node}) draws nothing");
                // Their placards: one legend strip carries all four names.
                Assert.Greater(g.Vertices("Pitts_V4_Rear_Legends") + g.Vertices("Pitts_Reference_Switch_Legends"), 0,
                               Path.GetFileName(g.Path) + ": the switch legends are gone");
            }
        }

        [Test]
        public void EverySwitchAndKnobDrawnInTheCockpitWorks()
        {
            // "Remove the nonsense knobs": anything drawn as a Ctl_ control must be in the table a hand works, and
            // every toggle in the table must be one of the sim's systems (VrCockpitHands.Panel SimParams).
            ModelRig rig = Rig();
            var table = new HashSet<string>(rig.Controls.Select(c => c.Node));
            foreach (GlbFile g in Glbs())
            {
                string[] orphans = g.NodeNames.Where(n => n.StartsWith("Ctl_") && g.Vertices(n) > 0 && !table.Contains(n)).ToArray();
                CollectionAssert.IsEmpty(orphans, Path.GetFileName(g.Path) + " draws controls nothing can work");
            }
            CollectionAssert.AreEquivalent(new[] { "Ctl_Toggle_Master", "Ctl_Toggle_Alternator", "Ctl_Toggle_FuelPump", "Ctl_Toggle_Smoke",
                                                   "Ctl_PushPull_Mixture", "Ctl_PushPull_Prop", "Ctl_Rotary_FuelSelector" }, table);
        }
    }
}
