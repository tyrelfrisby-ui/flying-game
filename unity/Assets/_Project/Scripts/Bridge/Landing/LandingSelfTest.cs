using System.Collections;
using System.IO;
using System.Linq;
using FlyingGame.Core.Landing;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Bench runs of the landing flow in the player (docs/LANDING-V2.md), started from ClipSelfTest:
    ///
    ///  AERO_SELFTEST=landing — every page and every open menu at the iPhone-landscape, iPad-landscape and iPad-portrait
    ///    shapes, into $AERO_SHOT_DIR (default persistentDataPath/landing-shots). Never LAUNCHes (LAUNCH saves).
    ///  AERO_SELFTEST=landing-thumbs — the thumbnail pipeline's bench pass: every type in every configuration, then
    ///    every airport at every free start in the 2-33 (also kept as loc/any/…, the fallback every type can use),
    ///    through the same capture code a device uses. tools/landing-thumbs.sh copies the result into Resources.
    ///
    /// AERO_SELFTEST_QUIT=1 quits at the end.
    /// </summary>
    public static class LandingSelfTest
    {
        private static string ShotDir
        {
            get
            {
                string d = System.Environment.GetEnvironmentVariable("AERO_SHOT_DIR");
                if (string.IsNullOrEmpty(d)) d = Path.Combine(Application.persistentDataPath, "landing-shots");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        private static IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            string p = Path.Combine(ShotDir, name + ".png");
            ScreenCapture.CaptureScreenshot(p);
            Debug.Log($"[LandingSelfTest] {name} {Screen.width}x{Screen.height} -> {p}");
            yield return new WaitForSecondsRealtime(0.5f);
        }

        private static IEnumerator Settle()
        {
            yield return null;
            var p = LandingPreview.Instance;
            float until = Time.unscaledTime + 8f;
            while (p != null && p.Settling && Time.unscaledTime < until) yield return null;
            yield return new WaitForSecondsRealtime(0.8f);
        }

        public static IEnumerator Pages(StartMenu menu)
        {
            yield return new WaitForSecondsRealtime(3f);
            menu.Open(); menu.ShowLanding();
            var flow = LandingSession.Flow;
            var shapes = new (string name, int w, int h, bool iphone, bool ipad)[]
            {
                ("iphone-land", 2556, 1179, true, false), ("ipad-land", 1180, 820, true, true), ("ipad-port", 820, 1180, true, true),   // iPad Air in points: the Mac window cannot open at its pixels
            };
            foreach (var sh in shapes)
            {
                Screen.SetResolution(sh.w, sh.h, FullScreenMode.Windowed);
                UiLayout.IPhoneSafeAreaForTest = sh.iphone; UiLayout.IPadSafeAreaForTest = sh.ipad;
                yield return new WaitForSecondsRealtime(1.5f);
                // Page 1 as a first-timer sees it, then each menu open.
                flow.Pick(PickerKind.Lesson, "");
                flow.Pick(PickerKind.Aircraft, LandingCatalog.FirstTimeAircraftId);
                flow.GoTo(LandingPage.Choose);
                yield return Settle();
                yield return Shot($"{sh.name}-p1-choose");
                foreach (var k in new[] { PickerKind.Aircraft, PickerKind.Location, PickerKind.Start, PickerKind.CameraView, PickerKind.Challenge, PickerKind.Lesson })
                {
                    flow.Open(k);
                    yield return new WaitForSecondsRealtime(0.6f);
                    yield return Shot($"{sh.name}-p1-menu-{k.ToString().ToLowerInvariant()}");
                    flow.ClosePicker();
                }
                // The configuration menu on a type that has some: the Super Cub.
                flow.Pick(PickerKind.Aircraft, "pa18-cub-like");
                flow.Open(PickerKind.Configuration);
                yield return Settle();
                yield return Shot($"{sh.name}-p1-menu-configuration");
                // A card in a browsable menu PREVIEWS and the list stays up (Ty 2026-11-11): the floats are on the
                // hero above with the strip still open, and SELECT is what keeps them.
                flow.Pick(PickerKind.Configuration, "floats");
                yield return Settle();
                yield return Shot($"{sh.name}-p1-menu-configuration-previewing");
                flow.CommitBrowse();
                yield return Settle();
                yield return Shot($"{sh.name}-p1-supercub-floats");
                // A lesson restricts the lists.
                flow.Pick(PickerKind.Lesson, "practice:flare");
                yield return Settle();
                yield return Shot($"{sh.name}-p1-lesson");
                flow.Open(PickerKind.Aircraft);
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot($"{sh.name}-p1-lesson-aircraft-restricted");
                flow.ClosePicker();
                flow.Pick(PickerKind.Lesson, "");

                // Page 2: a J-3 on floats, a friend on the front controls, full tanks.
                flow.Pick(PickerKind.Aircraft, "j3-cub-like");
                flow.Pick(PickerKind.Configuration, "floats");
                flow.Next();
                var other = flow.Seats.FirstOrDefault(s => s.Occupant != Occupant.Me);
                if (other != null) flow.SetSeat(other.SeatId, Occupant.InvitedPlayer, SeatRole.DualControl);
                flow.SetFuelFraction(1.0);
                flow.SetTimeOfDay(TimeOfDay.Midday);
                flow.SetUnits(UnitSystem.Standard);   // the default (Ty 2026-11-05 10:56)
                yield return Settle();
                yield return Shot($"{sh.name}-p2-load");
                flow.SetUnits(UnitSystem.Metric);
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot($"{sh.name}-p2-load-metric");
                flow.SetUnits(UnitSystem.Standard);
                // Over gross: the warning, LAUNCH still there.
                if (other != null) flow.SetSeatKg(other.SeatId, 120);
                foreach (var b in flow.Stations.Where(s => s.Kind == FlyingGame.Core.AircraftLoad.StationKind.Baggage)) flow.SetStationKg(b.StationId, b.MaxKg);
                flow.SetTimeOfDay(TimeOfDay.Sunset);
                flow.SetWeather("gusty");
                yield return Settle();
                yield return Shot($"{sh.name}-p2-overgross-sunset");
                // Verified envelope and live points (wheel J-3 has certificate data; floats do not).
                flow.GoTo(LandingPage.Choose);
                flow.Pick(PickerKind.Aircraft, "j3-cub-like");
                flow.Pick(PickerKind.Configuration, "standard");
                flow.Next();
                flow.SetTimeOfDay(TimeOfDay.Midday);
                flow.SetWeather("calm");
                other = flow.Seats.FirstOrDefault(s => s.Occupant != Occupant.Me);
                if (other != null) flow.SetSeat(other.SeatId, Occupant.Empty, SeatRole.Passenger);
                flow.SetFuelFraction(0.5);
                foreach (var b in flow.Stations.Where(s => s.Kind == FlyingGame.Core.AircraftLoad.StationKind.Baggage)) flow.SetStationKg(b.StationId, 0);
                foreach (var units in new[] { UnitSystem.Standard, UnitSystem.Metric })
                {
                    flow.SetUnits(units);
                    yield return Settle();
                    yield return Shot($"{sh.name}-p2-envelope-{units.ToString().ToLowerInvariant()}");
                }
                // Deliberately over gross, verified before requesting the popup: never launch in this capture pass.
                if (other != null)
                {
                    flow.SetSeat(other.SeatId, Occupant.InvitedPlayer, SeatRole.DualControl);
                    flow.SetSeatKg(other.SeatId, 120);
                }
                flow.SetFuelFraction(1);
                foreach (var b in flow.Stations.Where(s => s.Kind == FlyingGame.Core.AircraftLoad.StationKind.Baggage)) flow.SetStationKg(b.StationId, b.MaxKg);
                foreach (var units in new[] { UnitSystem.Standard, UnitSystem.Metric })
                {
                    flow.SetUnits(units);
                    yield return Settle();
                    yield return Shot($"{sh.name}-p2-envelope-outside-{units.ToString().ToLowerInvariant()}");
                    if (flow.WeightBalance == null || flow.WeightBalance.InLimits)
                        throw new System.InvalidOperationException("W&B popup capture requires an out-of-limits load");
                    flow.RequestLaunch();
                    if (!flow.LaunchConfirmPending) throw new System.InvalidOperationException("W&B confirmation missing");
                    yield return Shot($"{sh.name}-p2-wb-popup-{units.ToString().ToLowerInvariant()}");
                    flow.ConfirmLaunch(fly: false);
                }
                flow.SetTimeOfDay(TimeOfDay.Midday);
                flow.GoTo(LandingPage.Choose);
            }
            UiLayout.IPhoneSafeAreaForTest = UiLayout.IPadSafeAreaForTest = false;
            Debug.Log("[LandingSelfTest] DONE pages");
            if (System.Environment.GetEnvironmentVariable("AERO_SELFTEST_QUIT") == "1") Application.Quit();
        }

        public static IEnumerator Thumbnails(StartMenu menu)
        {
            // Every card here is a picture of the aeroplane where it would START, and the launch-view captures below
            // happen a second after the settle rather than at it — by which time the page's display circle would have
            // it banked round somewhere else entirely (LandingPreview.FlyPose). Stand it still for the whole pass.
            LandingPreview.HoldAtStart = true;
            yield return new WaitForSecondsRealtime(3f);
            menu.Open(); menu.ShowLanding();
            Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(1.5f);
            var flow = LandingSession.Flow;
            flow.Pick(PickerKind.Lesson, "");
            flow.GoTo(LandingPage.Choose);
            int n = 0;
            foreach (var fam in flow.Catalog.Families.ToList())
            {
                foreach (var cfg in fam.Configs)
                {
                    flow.Pick(PickerKind.Aircraft, fam.Id);
                    flow.Pick(PickerKind.Configuration, cfg.Id);
                    flow.Pick(PickerKind.Start, "InTheAir");
                    yield return Settle();
                    yield return new WaitForSecondsRealtime(0.5f);   // CaptureMissing runs at the settle
                    n++;
                }
            }
            // Every airport, every free start, in the 2-33: kept as itself AND as the any-aircraft fallback.
            flow.Pick(PickerKind.Aircraft, LandingCatalog.FirstTimeAircraftId);
            foreach (var loc in flow.Catalog.Locations)
                foreach (var (v, _, _) in LandingCatalog.Viewpoints)
                {
                    if (v == Viewpoint.InAeroBox || v == Viewpoint.InCombatZone) continue;   // one box, one zone: not per airport
                    flow.Pick(PickerKind.Location, loc.Id);
                    flow.Pick(PickerKind.Start, v.ToString());
                    yield return Settle();
                    yield return new WaitForSecondsRealtime(0.5f);
                    LandingPreview.CaptureLaunchView($"loc/{ThumbnailKeys.Any}/{ThumbnailKeys.Safe(loc.Id)}/{v}");
                    n++;
                }
            LandingPreview.HoldAtStart = false;
            Debug.Log($"[LandingSelfTest] DONE thumbnails: {n} poses, folder {LandingThumbnails.Dir}");
            if (System.Environment.GetEnvironmentVariable("AERO_SELFTEST_QUIT") == "1") Application.Quit();
        }
    }
}
