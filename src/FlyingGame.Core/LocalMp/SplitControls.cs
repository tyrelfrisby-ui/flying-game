namespace FlyingGame.Core.LocalMp;

/// <summary>
/// LOCAL 2-PLAYER, SPLIT SCREEN: who the on-screen controls belong to (Ty 2026-11-05 pt818u: the compact split HUD is
/// approved; the touch tray had to follow it).
///
/// The single-player touch tray (sticks, RESET, BAIL, FIRE, flaps, gear, ...) is laid out across the WHOLE screen. Under
/// a split screen that put P1's buttons on P2's half, and hiding only the drawing would have left invisible RESET / BAIL /
/// FIRE hit areas there that a P2 thumb could press for P1. So the tray is all or nothing: drawn AND hit-tested, or
/// neither. With the compact split HUD up and P1 on a pad, it is neither; P1 flies the pad (A fires, Menu opens OPTIONS),
/// and RESET and the handle move to the toolbar's FLIGHT panel, one column per player, each acting on its own aeroplane.
///
/// With no pad at all (Q114 is still open on what P2 should do then), P1 keeps the full tray exactly as single player:
/// otherwise nobody could fly P1.
/// </summary>
public static class SplitControls
{
    /// <summary>The touch tray is drawn and its hit areas are live. One flag for both, never one without the other.</summary>
    public static bool TouchTrayLive(bool compactSplit, bool p1HasPad) => !compactSplit || !p1HasPad;

    /// <summary>The toolbar row is replaced by the one FLIGHT entry (the per-player panel) while split compactly.</summary>
    public static bool CompactToolbar(bool compactSplit) => compactSplit;

    /// <summary>The actions the FLIGHT panel offers a player.</summary>
    public enum Action { Reset, Handle, View }

    /// <summary>Whether <paramref name="player"/>'s column offers <paramref name="action"/>. RESET and VIEW are every
    /// player's. The handle (bail out / eject / BRS) is P1's only: the second player's aeroplane has no egress yet, and
    /// offering P1's handle in P2's column is exactly the cross-player press this class exists to stop.</summary>
    public static bool Offers(int player, Action action, bool handleAvailable) => action switch
    {
        Action.Reset or Action.View => player is 0 or 1,
        Action.Handle => player == 0 && handleAvailable,
        _ => false,
    };

    /// <summary>Why P1's aeroplane was rebuilt (FlightSimDriver.AircraftChanged).</summary>
    public enum P1Spawn
    {
        /// <summary>FLY, a restart from the menus, a new start or aircraft: a new session, both players start again.</summary>
        Session,
        /// <summary>P1 pressed RESET (tray, keyboard R, joystick, FLIGHT panel): P1's own flight only.</summary>
        OwnReset,
    }

    /// <summary>Whether the second player's aeroplane goes back to its start slot. P1's own RESET used to restart P2 as
    /// well (every AircraftChanged of P1 restarted the pair); now it leaves P2 flying where it is.</summary>
    public static bool RestartsP2(P1Spawn cause, bool p2Flying) => cause == P1Spawn.Session || !p2Flying;
}
