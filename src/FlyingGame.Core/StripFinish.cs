namespace FlyingGame.Core;

/// <summary>
/// What an island landing surface (island-spec.json "surface") looks like and what it rolls like, decided in one place
/// so the paint, the colour and the wheels cannot disagree. Ty (2026-11-03): the Frank Church strips were drawn as
/// paved runways, centreline dashes, threshold bars, numbers and all. Real backcountry strips are dirt and turf, and
/// nothing unpaved carries runway paint: pavement gets the markings, everything else is just its own ground.
/// </summary>
public static class StripFinish
{
    public enum Kind { Paved, Gravel, Dirt, Turf, Sand, Water }

    public static Kind Of(string surface) => surface switch
    {
        "asphalt" or "paved" or "concrete" => Kind.Paved,
        "water" => Kind.Water,
        "hard sand" or "sand" => Kind.Sand,
        "dirt" => Kind.Dirt,
        "turf" => Kind.Turf,
        _ when surface.Contains("grass") => Kind.Turf,
        _ => Kind.Gravel,
    };

    /// <summary>Centreline, threshold bars, runway numbers, edge paint and the painted field ident: pavement only.</summary>
    public static bool Painted(string surface) => Of(surface) == Kind.Paved;

    /// <summary>Grass and turf lie the way a field lies (NaturalShapeParams); everything else is a plane.</summary>
    public static bool LiesNaturally(string surface) => Of(surface) == Kind.Turf;

    /// <summary>What the wheels find. The ground model has no separate dirt: a dirt strip rolls like gravel (hard,
    /// unpaved), turf like grass. Sand and the land under a water lane keep the gravel they always had.</summary>
    public static WorldTerrain.Surface Rolls(string surface) => Of(surface) switch
    {
        Kind.Paved => WorldTerrain.Surface.Paved,
        Kind.Turf => WorldTerrain.Surface.Grass,
        _ => WorldTerrain.Surface.Gravel,
    };
}
