#if !UNITY_5_3_OR_NEWER
using System.Text.Json;
#endif
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;

namespace FlyingGame.Sim.Practice;

/// <summary>One row of the idle-glide table: the power-off glide at 1.3 Vso for one aircraft in one flap setting.</summary>
public sealed class GlideEntry
{
    public string Id { get; set; } = "";
    public double Flaps { get; set; }
    public double VsoKt { get; set; }
    public double SpeedKt { get; set; }        // 1.3 Vso
    public double GlideDeg { get; set; }       // flight-path angle below the horizon
    public double GlideRatio { get; set; }
    public double SinkFpm { get; set; }
    public double AlphaDeg { get; set; }
    public double PitchDeg { get; set; }
    public double ElevatorDeg { get; set; }
    public double TrimStick { get; set; }      // stick position that holds it hands-off (−1 … 1)
}

/// <summary>
/// The idle-glide table (owner 2026-10-03: "determine the idle glide angle at 1.3 Vso for every airplane in every flap
/// configuration and create a table of the angle, speed and trim position, then start the lesson at idle, on speed and
/// with the trim set according to that table"). Computed by the trim solver with the engine at IDLE (the windmilling
/// prop's drag included), at the Valley field's elevation; gliders with half spoiler (their approach configuration).
/// The checked-in copy is configs/glide-table.json (regenerate: REGEN_GLIDE_TABLE=1 dotnet test --filter GlideTable);
/// GlideTableTests fails if any aircraft is missing or stale — part of the new-airplane protocol (docs/NEW-AIRPLANE-PROTOCOL.md).
/// </summary>
public static class GlideTable
{
    public const double AltitudeM = WorldTerrain.DatumM + 15;
    private static readonly Dictionary<string, GlideEntry> _entries = new();
    public static int Count => _entries.Count;

    private static string Key(string id, double flaps) => $"{id}@{Math.Round(flaps * 100):F0}";

    public static bool HasFlaps(AircraftConfig c)
    {
        foreach (var sf in c.Surfaces) foreach (var st in sf.Strips) if (st.Flap != null) return true;
        return false;
    }

    /// <summary>The flap settings the lessons offer: up / half / full, or just up.</summary>
    public static double[] FlapSettings(AircraftConfig c) => HasFlaps(c) ? new[] { 0.0, 0.5, 1.0 } : new[] { 0.0 };

    /// <summary>Compute one row exactly as the lesson does.</summary>
    public static GlideEntry Compute(AircraftConfig c, double flaps, double altitudeM = AltitudeM)
    {
        bool glider = c.Propulsion is null;
        double vso = PracticeScenario.EstimateVso(c, altitudeM - 15, flaps);
        double v = 1.3 * vso;
        TrimSolver.Result t = TrimSolver.SolveGliderTrim(c, v, altitudeM, flapFraction: flaps, spoilerFraction: glider ? 0.5 : 0.0, idleProp: true);
        double ratio = t.Converged && t.GlideRatio > 1 ? t.GlideRatio : 8.0;
        double gamma = Math.Atan(1.0 / ratio);
        return new GlideEntry
        {
            Id = c.Id, Flaps = flaps, VsoKt = vso * 1.943844, SpeedKt = v * 1.943844,
            GlideDeg = gamma * 180 / Math.PI, GlideRatio = ratio, SinkFpm = v * Math.Sin(gamma) * 196.85,
            AlphaDeg = t.AlphaRad * 180 / Math.PI, PitchDeg = (t.AlphaRad - gamma) * 180 / Math.PI,
            ElevatorDeg = t.ElevatorRad * 180 / Math.PI, TrimStick = Aircraft.StickForDeflection(t.ElevatorRad, c.Controls.Elevator),
        };
    }

    /// <summary>Load the table (Unity: deserialized with Newtonsoft in the Bridge; tests: <see cref="LoadJson"/>).</summary>
    public static void Load(IEnumerable<GlideEntry> list)
    {
        _entries.Clear();
        foreach (GlideEntry e in list) _entries[Key(e.Id, e.Flaps)] = e;
    }

#if !UNITY_5_3_OR_NEWER
    public static void LoadJson(string json) => Load(JsonSerializer.Deserialize<List<GlideEntry>>(json) ?? new List<GlideEntry>());

    public static string ToJson(IEnumerable<GlideEntry> entries) =>
        JsonSerializer.Serialize(entries.Select(e => Round(e)).ToList(), new JsonSerializerOptions { WriteIndented = true });
#endif

    public static GlideEntry Round(GlideEntry e) => new()
    {
        Id = e.Id, Flaps = e.Flaps, VsoKt = Math.Round(e.VsoKt, 2), SpeedKt = Math.Round(e.SpeedKt, 2), GlideDeg = Math.Round(e.GlideDeg, 3),
        GlideRatio = Math.Round(e.GlideRatio, 3), SinkFpm = Math.Round(e.SinkFpm, 1), AlphaDeg = Math.Round(e.AlphaDeg, 3),
        PitchDeg = Math.Round(e.PitchDeg, 3), ElevatorDeg = Math.Round(e.ElevatorDeg, 3), TrimStick = Math.Round(e.TrimStick, 4),
    };

    public static GlideEntry? Lookup(string id, double flaps) => _entries.TryGetValue(Key(id, flaps), out GlideEntry? e) ? e : null;
}
