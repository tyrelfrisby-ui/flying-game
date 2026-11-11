using System.Linq;
using FlyingGame.Bridge;
using FlyingGame.Core.Landing;
using NUnit.Framework;
using UnityEngine;

namespace FlyingGame.EditModeTests
{
    /// <summary>
    /// Landing page v2 (Ty 2026-11-05, docs/LANDING-V2.md), the Unity side: what the .NET suite cannot see because it
    /// never compiles the Bridge. The flow's own rules are in tools/FlightTests/LandingFlowTests. Literals are copied by
    /// hand. Run: Unity -batchmode -runTests -testPlatform EditMode -testFilter LandingV2Tests.
    /// </summary>
    public sealed class LandingV2Tests
    {
        /// <summary>Viewpoint and SessionSettings.Start convert BY VALUE (LandingSession.ApplyPreview): same names, same order.</summary>
        [Test]
        public void ViewpointsAreTheSessionStartsInOrder()
        {
            Assert.AreEqual(System.Enum.GetNames(typeof(SessionSettings.Start)), System.Enum.GetNames(typeof(Viewpoint)));
            Assert.AreEqual(new[] { "InTheAir", "OnTheRunway", "OnFinal", "InAeroBox", "InCombatZone", "InThermal" }, System.Enum.GetNames(typeof(Viewpoint)));
        }

        private static LandingCatalog RealFleet() =>
            new LandingCatalog(SessionSettings.Fleet, new[] { ("KMEV", "KMEV", 4700.0) }, SessionSettings.Challenges.Select(c => ((string)c.id, c.name)),
                               SessionSettings.Lessons, lessonNeedsEngine: SessionSettings.LessonNeedsEngine);

        /// <summary>Every aeroplane the game offers is reachable in the new menus, as a card or as a configuration, and no
        /// configuration names a file the fleet does not have.</summary>
        [Test]
        public void EveryFleetAircraftIsACardOrAConfiguration()
        {
            var cat = RealFleet();
            var reachable = cat.Families.SelectMany(f => f.Configs.Select(c => c.AircraftId)).Distinct().OrderBy(x => x).ToArray();
            var fleet = SessionSettings.Fleet.Select(f => f.id).OrderBy(x => x).ToArray();
            Assert.AreEqual(fleet, reachable);
            foreach (var v in LandingCatalog.Variants)
            {
                Assert.IsTrue(fleet.Contains(v.Key), v.Key);
                Assert.IsTrue(fleet.Contains(v.Value.family), v.Value.family);
            }
            // The types with floats today (copied from SessionSettings.Fleet by hand).
            Assert.AreEqual(new[] { "dhc2-beaver-like", "j3-cub-like", "pa18-cub-like" },
                            cat.Families.Where(f => f.Configs.Any(c => c.Kind == GearKind.Floats)).Select(f => f.Id).OrderBy(x => x).ToArray());
        }

        /// <summary>The first-time aeroplane is the yellow SGS 2-33 (CLAUDE.md: the red one is banned): one config, its own file.</summary>
        [Test]
        public void TheFirstTimeAeroplaneIsThe233OnItsOwnFile()
        {
            var cat = RealFleet();
            var fam = cat.FamilyOf(LandingCatalog.FirstTimeAircraftId);
            Assert.IsNotNull(fam);
            Assert.AreEqual(new[] { "standard:glider-2-33-like" }, fam.Configs.Select(c => $"{c.Id}:{c.AircraftId}").ToArray());
        }

        /// <summary>The restricted lists name aeroplanes the fleet really has, and every event has rules of its own.</summary>
        [Test]
        public void TheRestrictedListsNameRealAeroplanes()
        {
            var fleet = SessionSettings.Fleet.Select(f => f.id).ToArray();
            foreach (string id in LandingCatalog.LessonTrainers.Concat(LandingCatalog.Aerobats)) Assert.IsTrue(fleet.Contains(id), id);
            var cat = RealFleet();
            foreach (var c in SessionSettings.Challenges.Where(c => c.id != null && SessionSettings.IsEvent(c.id)))
                Assert.AreNotSame(ActivityRules.FreeFlight, cat.RulesFor(c.id), c.id);
            foreach (var l in SessionSettings.Lessons)
                Assert.IsTrue(cat.Families.Any(cat.RulesFor(l.id).Allows), l.id + " has no aeroplane");
        }

        /// <summary>Midday is the scene as built; another time moves the sun; midday again puts it back exactly.</summary>
        [Test]
        public void MiddayIsTheSceneAsBuiltAndComesBack()
        {
            var go = new GameObject("Sun");
            try
            {
                var sun = go.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.intensity = 1.1f;
                go.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
                RenderSettings.sun = sun;
                Quaternion built = go.transform.rotation;
                TimeOfDayLighting.Apply(TimeOfDay.Midday);
                Assert.AreEqual(0f, Quaternion.Angle(built, go.transform.rotation), 1e-3f);
                TimeOfDayLighting.Apply(TimeOfDay.Morning);
                Assert.Greater(Quaternion.Angle(built, go.transform.rotation), 10f);
                Assert.AreEqual(22f, go.transform.eulerAngles.x, 0.01f);
                TimeOfDayLighting.Apply(TimeOfDay.Midday);
                Assert.AreEqual(0f, Quaternion.Angle(built, go.transform.rotation), 1e-3f);
                Assert.AreEqual(1.1f, sun.intensity, 1e-5f);
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>UNITS (Ty 2026-11-05 10:56): Standard when nothing is saved, Metric remembered through the store at once,
        /// and the Bridge's one reader (<see cref="LandingSession.SavedUnits"/>) reads the very key the page writes. The
        /// editor's own pref is put back.</summary>
        [Test]
        public void UnitsDefaultToStandardAndRoundTripThroughTheStore()
        {
            var store = new MemoryLandingStore();
            var flow = new LandingFlow(RealFleet(), store, _ => null);
            Assert.AreEqual(UnitSystem.Standard, flow.Units);
            flow.SetUnits(UnitSystem.Metric);
            Assert.AreEqual("metric", store.Get("landing.units"));
            Assert.AreEqual(UnitSystem.Metric, new LandingFlow(RealFleet(), store, _ => null).Units);

            string saved = PlayerPrefs.HasKey("landing.units") ? PlayerPrefs.GetString("landing.units") : null;
            try
            {
                LandingSession.Reset();
                PlayerPrefs.DeleteKey("landing.units");
                Assert.AreEqual(UnitSystem.Standard, LandingSession.SavedUnits);
                PlayerPrefs.SetString("landing.units", "metric");
                Assert.AreEqual(UnitSystem.Metric, LandingSession.SavedUnits);
            }
            finally
            {
                if (saved == null) PlayerPrefs.DeleteKey("landing.units"); else PlayerPrefs.SetString("landing.units", saved);
                LandingSession.Reset();
            }
        }

        /// <summary>A key is a relative path under the device's capture folder; a key nobody has is a miss, not a throw.</summary>
        [Test]
        public void ThumbnailKeysAreFilesUnderTheCaptureFolder()
        {
            string p = LandingThumbnails.PathFor("loc/pitts-s2b-like/standard/kmev/OnFinal");
            StringAssert.EndsWith(System.IO.Path.Combine("landing-thumbs", "loc", "pitts-s2b-like", "standard", "kmev", "OnFinal.png"), p);
            Assert.IsNull(LandingThumbnails.Get("loc/no-such-aircraft/standard/nowhere/InTheAir"));
            Assert.IsNull(LandingThumbnails.Get((PickerItem)null));
        }
    }
}
