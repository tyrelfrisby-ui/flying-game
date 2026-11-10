using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;

namespace FlyingGame.FlightTests;

/// <summary>The sim's process-wide world, in one place (CLAUDE.md: "this suite is globally coupled"). A test that
/// installs any of it takes it down again in Dispose; a test that needs an empty, calm, flat world says so with
/// <see cref="Calm"/> rather than trusting whoever ran before it.</summary>
internal static class SimGlobals
{
    /// <summary>No terrain, no surface or water override, still air, no solids and no decks.</summary>
    public static void Calm()
    {
        WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null; FloatHydro.FlatWaterOverride = null;
        Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null; Atmosphere.Field = null;
        ClearSolids();
    }

    /// <summary>The registered solids and decks (Landmarks.RegisterSolids and friends add to these and never remove).</summary>
    public static void ClearSolids()
    {
        WorldSolids.Shapes.Clear(); WorldSolids.Boxes.Clear(); WorldDecks.All.Clear();
    }

    /// <summary>For an assertion message: what of the world was installed when it failed.</summary>
    public static string Describe() =>
        $"terrain {WorldTerrain.Active?.GetType().Name ?? "none"}, surface {WorldTerrain.SurfaceOverride?.ToString() ?? "none"}, " +
        $"flat water {FloatHydro.FlatWaterOverride?.ToString() ?? "none"}, wind {Atmosphere.SteadyWind.Length:F1} m/s, " +
        $"turbulence {(Atmosphere.ActiveTurbulence != null ? "on" : "off")}, field {(Atmosphere.Field != null ? "on" : "off")}, " +
        $"decks {WorldDecks.All.Count}, solids {WorldSolids.Shapes.Count}/{WorldSolids.Boxes.Count}";
}
