using System.Text.RegularExpressions;
using FlyingGame.Core.LocalMp;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// LOCAL 2-PLAYER, split screen: the touch tray and the per-player RESET / BAIL / FIRE (Ty 2026-11-05 pt818u: the compact
/// split HUD is approved; the tray is what remained). Codex's HUD lane: "TouchFlightControls must suppress its
/// full-screen tray AND its raw touch hit regions together ... Hiding only OnGUI would leave invisible RESET/BAIL/FIRE
/// hit areas."
///
/// The policy is Core (SplitControls) and is tested with literal truth tables. The Unity half (TouchFlightControls,
/// ViewPanel, LocalCoop, CockpitTaps) is not compiled by this suite, so its wiring is held by reading the source, the
/// same way FuselageFollowCameraTests holds the camera.
/// </summary>
public class LocalSplitControlsTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent;
        return d!.FullName;
    }
    private static string Src(string file) => File.ReadAllText(Path.Combine(Root(), "unity", "Assets", "_Project", "Scripts", "Bridge", file));

    /// <summary>The body of the method that starts at <paramref name="signature"/> (to its matching brace).</summary>
    private static string Body(string src, string signature)
    {
        int at = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, $"not found: {signature}");
        int open = src.IndexOf('{', at), depth = 0;
        for (int i = open; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return src.Substring(open, i - open + 1);
        }
        throw new InvalidOperationException("unbalanced: " + signature);
    }

    // ---- the policy ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(false, false, true)]   // single player, touch: the tray as always
    [InlineData(false, true, true)]    // single player, a pad: the tray as always (its buttons, sticks hidden)
    [InlineData(true, true, false)]    // compact split, P1 on a pad: no tray, no hit areas
    [InlineData(true, false, true)]    // compact split, no pad at all: P1 keeps the tray, or nobody flies P1 (Q114 open)
    public void TheTrayIsAllOrNothing(bool compact, bool p1Pad, bool live)
        => Assert.Equal(live, SplitControls.TouchTrayLive(compact, p1Pad));

    [Fact]
    public void TheCompactToolbarIsTheSplitHudsFlag()
    {
        Assert.True(SplitControls.CompactToolbar(true));
        Assert.False(SplitControls.CompactToolbar(false));
    }

    [Fact]
    public void EachPlayerResetsAndChangesViewOnTheirOwnColumn()
    {
        foreach (int p in new[] { 0, 1 })
        {
            Assert.True(SplitControls.Offers(p, SplitControls.Action.Reset, handleAvailable: false));
            Assert.True(SplitControls.Offers(p, SplitControls.Action.View, handleAvailable: false));
        }
        Assert.False(SplitControls.Offers(2, SplitControls.Action.Reset, false));
    }

    /// <summary>The handle is P1's: P2's column never offers it, even while P1's is available — that would be P1's
    /// bail-out under P2's thumb.</summary>
    [Fact]
    public void OnlyP1HasAHandle()
    {
        Assert.True(SplitControls.Offers(0, SplitControls.Action.Handle, handleAvailable: true));
        Assert.False(SplitControls.Offers(0, SplitControls.Action.Handle, handleAvailable: false));
        Assert.False(SplitControls.Offers(1, SplitControls.Action.Handle, handleAvailable: true));
    }

    [Fact]
    public void P1sOwnResetLeavesP2Flying()
    {
        Assert.False(SplitControls.RestartsP2(SplitControls.P1Spawn.OwnReset, p2Flying: true));
        Assert.True(SplitControls.RestartsP2(SplitControls.P1Spawn.OwnReset, p2Flying: false));   // nothing to keep: start it
        Assert.True(SplitControls.RestartsP2(SplitControls.P1Spawn.Session, p2Flying: true));     // FLY / restart: both start again
        Assert.True(SplitControls.RestartsP2(SplitControls.P1Spawn.Session, p2Flying: false));
    }

    // ---- the Unity wiring -----------------------------------------------------------------------------------------

    /// <summary>One flag gates the drawing, the touch loop and the hit test, so a button is never live where it is not
    /// drawn.</summary>
    [Fact]
    public void TheTrayDrawsAndHitTestsOnTheSameFlag()
    {
        string t = Src("TouchFlightControls.cs");
        Assert.Contains("public static bool TrayLive => FlyingGame.Core.LocalMp.SplitControls.TouchTrayLive(LocalPlayerHud.CompactActive, GamepadInput.Now.Connected);", t);
        Assert.Matches(new Regex(@"else if \(TrayLive\) ReadPointers\(\);\s*else PadOnly\(\);"), Body(t, "private void Update()"));
        Assert.Contains("if (!TrayLive) return;", Body(t, "private void OnGUI()"));
        Assert.Contains("if (!TrayLive) return false;", Body(t, "public bool IsControlAt(Vector2 p)"));
        // ReadPointers is the only reader of the raw touches, and it runs only under TrayLive.
        Assert.Single(Regex.Matches(t, @"\bReadPointers\(\);"));
    }

    /// <summary>With the tray off no finger owns a control, but the pad's FIRE (A) and the queued taps still run.</summary>
    [Fact]
    public void PadOnlyLetsGoOfEveryFingerAndStillFires()
    {
        string t = Src("TouchFlightControls.cs");
        string pad = Body(t, "private void PadOnly()");
        foreach (string f in new[] { "_trimFinger", "_ailTrimFinger", "_rudTrimFinger", "_fireFinger", "_ejectFinger", "_lookFinger" })
            Assert.Contains(f, pad);
        Assert.Contains("FireEjectAndTaps();", pad);
        Assert.DoesNotContain("Input.touches", pad);
        string fire = Body(t, "private void FireEjectAndTaps()");
        Assert.Contains("_combat.Firing = _fireFinger != int.MinValue", fire);
        Assert.Contains("gp.Fire", fire);
        Assert.Contains("ActOnTaps();", fire);
    }

    [Fact]
    public void P1sResetIsMarkedAsItsOwnBeforeTheFlightRestarts()
    {
        string reset = Body(Src("TouchFlightControls.cs"), "private void DoReset()");
        int mark = reset.IndexOf("LocalCoop.MarkP1OwnReset()", StringComparison.Ordinal);
        int restart = reset.IndexOf("_driver.ResetFlight()", StringComparison.Ordinal);
        Assert.True(mark >= 0 && restart > mark, "MarkP1OwnReset must come before ResetFlight");

        string coop = Src("LocalCoop.cs");
        string spawned = Body(coop, "private void OnP1Spawned()");
        Assert.Contains("SplitControls.RestartsP2(cause, p2Flying)", spawned);
        Assert.Contains("_p1OwnReset = false;", spawned);   // read once
        string p2 = Body(coop, "public void ResetP2()");
        Assert.Contains("_p2.ResetFlight();", p2);
        Assert.DoesNotContain("P1", Regex.Replace(p2, "\"[^\"]*\"", ""));   // P2's reset never touches the first player
    }

    /// <summary>The FLIGHT panel: each column acts on its own aeroplane, through the policy.</summary>
    [Fact]
    public void TheFlightPanelActsOnEachColumnsOwnAeroplane()
    {
        string v = Src("ViewPanel.cs");
        Assert.Contains("if (FlyingGame.Core.LocalMp.SplitControls.CompactToolbar(LocalPlayerHud.CompactActive)) { _viewMenu = false; DrawFlightEntry(); return; }",
            Body(v, "private void DrawLiveColumn()"));
        string panel = Body(v, "private void DrawFlightEntry()");
        Assert.Contains("SplitControls.Action.Reset", panel);
        Assert.Contains("SplitControls.Action.Handle", panel);
        Assert.Contains("var touch = i == 0 && v.Driver != null ? v.Driver.GetComponent<TouchFlightControls>() : null;", panel);
        Assert.Contains("v.Chase.NextView()", panel);
        Assert.DoesNotContain("FindFirstObjectByType<ChaseCamera>", panel);
        string reset = Body(v, "private void ResetPlayer(int player, LocalPlayerView v)");
        Assert.Contains("MenuReset()", reset);
        Assert.Contains("ResetP2()", reset);
    }

    /// <summary>Glass taps on a cockpit instrument go through that aeroplane's own camera and only on its own half.</summary>
    [Fact]
    public void CockpitTapsAreEachPlayersOwn()
    {
        string c = Src("CockpitTaps.cs");
        Assert.Contains("LocalPlayerView view = LocalSplit.Active ? LocalPlayerView.ForDriver(_driver) : null;", Body(c, "private void Taps()"));
        Assert.Contains("if (LocalSplit.Active && !cam.pixelRect.Contains(down)) return;", Body(c, "private void TryTap("));
        Assert.Contains("if (_driver == null || _driver.PlayerIndex == 0) Current = this;", c);
    }
}
