namespace FlyingGame.Core;

/// <summary>
/// Which aeroplane tows a glider off the runway. Ty 2026-11-06 (t906u): "Use the Great Lakes as a tow plane for the 2-33
/// until we get the super cub built." So the SGS 2-33 is towed by the Great Lakes whatever the TOW PLANE picker says,
/// and every other glider keeps the picker's choice (Pawnee / Super Cub).
///
/// INTERIM: when our own Super Cub model exists, the 2-33's tug goes back to the picker (or to the Cub) and this rule
/// is deleted. Nobody is building the Cub under this change.
///
/// Pure: no Unity. TowController asks it for the tug it spawns and draws; TowPlanesTests pins it.
/// </summary>
public static class TowPlanes
{
    public const string Sgs233 = "glider-2-33-like";

    /// <summary>The 2-33's tug until the Super Cub is built (t906u).</summary>
    public const string InterimTugFor233 = "great-lakes-like";

    /// <summary>The picker's default when nothing has been chosen (SessionSettings.TugId).</summary>
    public const string DefaultTug = "pa25-pawnee-like";

    /// <summary>True while the 2-33 ignores the picker and is towed by the Great Lakes.</summary>
    public static bool IsInterim(string? gliderId) => gliderId == Sgs233;

    /// <summary>The tug for <paramref name="gliderId"/>: the Great Lakes for the 2-33 (interim), else the player's
    /// <paramref name="chosenTugId"/>, else the Pawnee.</summary>
    public static string TugFor(string? gliderId, string? chosenTugId) =>
        IsInterim(gliderId) ? InterimTugFor233 : string.IsNullOrEmpty(chosenTugId) ? DefaultTug : chosenTugId!;
}
