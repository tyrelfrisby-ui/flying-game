using FlyingGame.Core;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// TV mode's decisions (docs/TV-COMPANION-PLAN.md §1.4–1.5). The resolutions are worked by hand: k = min(1, 1920 / long
/// side), each side × k rounded to the nearest even number and never above the TV's own (even) size.
/// </summary>
public sealed class TvLayoutTests
{
    // ---- resolution ----

    [Theory]
    [InlineData(3840, 2160, 1920, 1080)]   // 4K UHD: k = 0.5
    [InlineData(1920, 1080, 1920, 1080)]   // 1080p: as is
    [InlineData(1280, 720, 1280, 720)]     // 720p: never scaled UP
    [InlineData(2560, 1600, 1920, 1200)]   // 16:10, k = 0.75
    [InlineData(2048, 1536, 1920, 1440)]   // 4:3, k = 0.9375
    [InlineData(3440, 1440, 1920, 804)]    // 21:9: 1440 × 0.5581 = 803.7 → 804
    [InlineData(4096, 2160, 1920, 1012)]   // DCI 4K: 2160 × 0.46875 = 1012.5 → 1012
    [InlineData(1366, 768, 1366, 768)]     // an old 1366 × 768 set, as is
    [InlineData(1365, 767, 1364, 766)]     // odd native sizes round DOWN to even, never past the screen
    [InlineData(1080, 1920, 1080, 1920)]   // a portrait screen keeps its shape
    public void ChoosesTheTvShapeAtMost1080p(int w, int h, int ew, int eh)
    {
        var (rw, rh) = TvResolution.Choose(w, h);
        Assert.Equal(ew, rw);
        Assert.Equal(eh, rh);
    }

    [Theory]
    [InlineData(3840, 2160)]
    [InlineData(2560, 1600)]
    [InlineData(2048, 1536)]
    [InlineData(3440, 1440)]
    [InlineData(4096, 2160)]
    [InlineData(2622, 1206)]   // an iPhone 16 Pro's own shape, for a Mac-made AirPlay receiver that reports it
    public void TheAspectRatioIsTheTvsToAPixel(int w, int h)
    {
        var (rw, rh) = TvResolution.Choose(w, h);
        // The chosen height is within one pixel of the exact height for the chosen width (even rounding moves it ≤ 1 px).
        Assert.InRange(rh - rw * (double)h / w, -1.0, 1.0);
        Assert.True(rw <= w && rh <= h, "never larger than the TV");
        Assert.Equal(0, rw % 2);
        Assert.Equal(0, rh % 2);
        Assert.True(Math.Max(rw, rh) <= TvResolution.MaxLongSide);
    }

    [Theory]
    [InlineData(0, 1080)]
    [InlineData(1920, 0)]
    [InlineData(-5, -5)]
    public void ANonSizeMeansLeaveTheDisplayAlone(int w, int h) => Assert.Equal((0, 0), TvResolution.Choose(w, h));

    [Fact]
    public void TheCapIsHonoured() => Assert.Equal((1280, 720), TvResolution.Choose(3840, 2160, maxLong: 1280));

    // ---- mode ----

    [Theory]
    //          supported displays setting              widget  →  mode
    [InlineData(true, 1, TvSetting.GameOnTv, false, TvMode.Handheld)]   // no TV
    [InlineData(true, 2, TvSetting.GameOnTv, false, TvMode.GameOnTv)]   // TV connects: the game goes to it
    [InlineData(true, 3, TvSetting.GameOnTv, false, TvMode.GameOnTv)]   // more than one: still the first TV
    [InlineData(true, 2, TvSetting.Mirror, false, TvMode.Mirror)]       // MIRROR: leave it to iOS
    [InlineData(true, 1, TvSetting.Mirror, false, TvMode.Handheld)]
    [InlineData(false, 2, TvSetting.GameOnTv, false, TvMode.Handheld)]  // Mac / Quest / Editor: never
    [InlineData(true, 2, TvSetting.GameOnTv, true, TvMode.Handheld)]    // the Aero Widget app: never
    [InlineData(true, 0, TvSetting.GameOnTv, false, TvMode.Handheld)]
    public void ModeTable(bool supported, int displays, TvSetting setting, bool widget, TvMode expected)
        => Assert.Equal(expected, TvModeLogic.Decide(supported, displays, setting, widget));

    [Fact]
    public void GameOnTvIsTheFirstSettingValue()
    {
        // The saved pref is an int: 0 must stay GAME ON TV so a missing pref (GetInt default 0) is the default (Q1).
        Assert.Equal(0, (int)TvSetting.GameOnTv);
        Assert.Equal(1, (int)TvSetting.Mirror);
    }

    // ---- title-safe margin ----

    [Fact]
    public void TitleSafeIsTwoAndAHalfPercent()
    {
        Assert.Equal(0.025, TvSafeArea.InsetFraction);
        Assert.Equal(27.0, TvSafeArea.Inset(1080), 9);
        Assert.Equal(48.0, TvSafeArea.Inset(1920), 9);
        Assert.Equal(0.0, TvSafeArea.Inset(-10), 9);
    }

    // ---- TV + game controller: the button pad ----

    private static List<PadButton> Flat(List<List<PadButton>> g) => g.SelectMany(x => x).ToList();

    [Fact]
    public void AFullyEquippedAeroplaneGetsEveryButton()
    {
        var g = TvPadLayout.Groups(flaps: true, gear: true, smoke: true, inCockpit: true);
        Assert.Equal(3, g.Count);
        Assert.Equal(new[] { PadButton.ViewChase, PadButton.ViewCockpit, PadButton.ViewTail, PadButton.ViewSide, PadButton.ViewFlyby, PadButton.ViewLock }, g[0]);
        Assert.Equal(new[] { PadButton.Smoke, PadButton.FlapsUp, PadButton.FlapsDown, PadButton.Gear, PadButton.CamRef, PadButton.LookCentre }, g[1]);
        Assert.Equal(new[] { PadButton.Pause, PadButton.Reset, PadButton.Aircraft, PadButton.Clip, PadButton.Rec, PadButton.Aero }, g[2]);
    }

    [Fact]
    public void OnlyButtonsThatDoSomethingAreListed()
    {
        // A glider with no smoke, no flaps, fixed gear, outside: views, CAM, and the actions.
        var flat = Flat(TvPadLayout.Groups(flaps: false, gear: false, smoke: false, inCockpit: false));
        Assert.DoesNotContain(PadButton.Smoke, flat);
        Assert.DoesNotContain(PadButton.FlapsUp, flat);
        Assert.DoesNotContain(PadButton.FlapsDown, flat);
        Assert.DoesNotContain(PadButton.Gear, flat);
        Assert.DoesNotContain(PadButton.LookCentre, flat);   // looking around is a cockpit thing
        Assert.Contains(PadButton.CamRef, flat);
        Assert.Equal(13, flat.Count);   // 6 views + CAM + 6 actions
    }

    [Theory]
    //          screen W  H      portrait  (iPhone 16 Pro at 3x; iPad Air 11" at 2x)
    [InlineData(2622, 1206, false)]
    [InlineData(1206, 2622, true)]
    [InlineData(2360, 1640, false)]
    [InlineData(1640, 2360, true)]
    public void TheGridFitsHasBigTargetsAndNothingOverlaps(int sw, int sh, bool portrait)
    {
        // The box the Bridge gives it: inside a 4 % margin, under a toolbar + three text lines (≈ 22 % of the height in
        // landscape, 12 % in portrait). The targets must be at least 44 pt at 3x = 132 px.
        double m = 0.04 * Math.Min(sw, sh), top = (portrait ? 0.12 : 0.22) * sh;
        double x = m, y = top, w = sw - 2 * m, h = sh - top - m, gap = 0.012 * Math.Min(sw, sh);
        foreach (bool full in new[] { true, false })
        {
            var groups = TvPadLayout.Groups(full, full, full, full);
            var cells = TvPadLayout.Layout(groups, portrait, x, y, w, h, gap);
            Assert.Equal(Flat(groups).Count, cells.Count);
            foreach (var c in cells)
            {
                Assert.True(c.X >= x - 1e-6 && c.Y >= y - 1e-6 && c.X + c.W <= x + w + 1e-6 && c.Y + c.H <= y + h + 1e-6, $"{c.Button} outside the box");
                Assert.True(c.W >= 132 && c.H >= 132, $"{c.Button} is {c.W:F0} × {c.H:F0} px");
                Assert.True(c.H <= c.W + 1e-6, "never taller than wide");
            }
            for (int i = 0; i < cells.Count; i++)
                for (int j = i + 1; j < cells.Count; j++)
                {
                    var a = cells[i]; var b = cells[j];
                    bool apart = a.X + a.W <= b.X + 1e-6 || b.X + b.W <= a.X + 1e-6 || a.Y + a.H <= b.Y + 1e-6 || b.Y + b.H <= a.Y + 1e-6;
                    Assert.True(apart, $"{a.Button} overlaps {b.Button}");
                }
        }
    }

    [Fact]
    public void LandscapeIsSixAcrossAndEachGroupStartsARow()
    {
        var groups = TvPadLayout.Groups(true, true, true, true);
        var cells = TvPadLayout.Layout(groups, portrait: false, 0, 0, 1200, 900, 0);
        // 6 columns of 200; three rows of 200 (cells are never taller than wide), the groups 0 apart with a zero gap.
        Assert.Equal(6, TvPadLayout.Columns(false));
        Assert.Equal(3, TvPadLayout.Columns(true));
        Assert.Equal((0.0, 0.0, 200.0, 200.0), (cells[0].X, cells[0].Y, cells[0].W, cells[0].H));   // CHASE
        Assert.Equal((1000.0, 0.0), (cells[5].X, cells[5].Y));                                        // LOCK
        Assert.Equal((0.0, 200.0), (cells[6].X, cells[6].Y));                                         // SMOKE: row 2
        Assert.Equal((0.0, 400.0), (cells[12].X, cells[12].Y));                                       // PAUSE: row 3
    }

    [Fact]
    public void PortraitWrapsAGroupOntoMoreRows()
    {
        var groups = TvPadLayout.Groups(false, false, false, false);   // 6 views, 1 switch, 6 actions
        var cells = TvPadLayout.Layout(groups, portrait: true, 0, 0, 900, 3000, 0);
        // 3 columns of 300: views take rows 0–1, CAM row 2, actions rows 3–4 (300 px each).
        Assert.Equal((0.0, 300.0), (cells[3].X, cells[3].Y));      // the 4th view, SIDE, starts the views' second row
        Assert.Equal(PadButton.ViewSide, cells[3].Button);
        Assert.Equal((0.0, 600.0), (cells[6].X, cells[6].Y));      // CAM
        Assert.Equal((0.0, 900.0), (cells[7].X, cells[7].Y));      // PAUSE
        Assert.Equal((600.0, 1200.0), (cells[12].X, cells[12].Y)); // AERO, last
    }

    [Fact]
    public void ANothingBoxGivesNoCells()
        => Assert.Empty(TvPadLayout.Layout(TvPadLayout.Groups(true, true, true, true), false, 0, 0, 0, 500, 4));
}
