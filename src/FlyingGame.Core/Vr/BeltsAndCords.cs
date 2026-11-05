using System;

namespace FlyingGame.Core.Vr
{
    /// <summary>
    /// NO SEAT BELTS AND NO HEADSET CORDS, IN ANY AEROPLANE (Ty 2026-11-05: "Remove seat belts and headset wires completely
    /// (may revisit). Bailout is just a button press and it happens.").
    ///
    /// Two halves. What the pilot WEARS: <see cref="SeatHarness.ForCockpit"/> gives every seat an empty harness while
    /// <see cref="BeltsWorn"/> is false, so nothing holds him in and the climb-out is never "still strapped in". What the
    /// models DRAW: the GLBs shipped with webbing, buckles and release levers (Pitts, Extra, 2-33, Grob as nodes; the Great
    /// Lakes and the Pitts also as a material inside their one big cockpit mesh). The Blender sources no longer make them,
    /// but a GLB already built still has them, so the game strips them when the model is placed: by node name, and by
    /// material inside a mesh that carries other things too. The rules are here, in one place, so a test can hold them.
    ///
    /// Kept on purpose: the headset JACKS (Headset_Jack_*; a socket in the sidewall is part of the aeroplane), the
    /// parachute's own harness (Parachute_Harness_*, on the pilot gear, never on an airframe) and every control cable
    /// (canopy release, tow, trim, water rudder). The material "Harness_Steel" is NOT a belt rule: the Pitts' and the
    /// Extra's jacks are drawn in it; their buckles go by name.
    /// </summary>
    public static class BeltsAndCords
    {
        /// <summary>Off: the seats have no belts and the headset no cord. Set true to bring both back (the Core models,
        /// Harness and HeadsetCord, are untouched).</summary>
        public const bool BeltsWorn = false;

        /// <summary>True for a model node that is a seat belt, a buckle or release, or the headset's cord or plug.</summary>
        public static bool IsBeltOrCordNode(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            if (n.Contains("parachute")) return false;
            if (n.Contains("headset_jack")) return false;
            if (n.Contains("harness")) return true;                       // Harness_*, Grip_Harness_*, Lap_/Shoulder_Harness_*
            if (n == "buckle" || n.StartsWith("buckle_") || n.Contains("_buckle")) return true;   // Grob Buckle_F/_R, Harness_Rear_Buckle, Belt_buckle_*
            if (n.Contains("lap_belt") || n.Contains("seatbelt") || n.Contains("seat_belt") || n.StartsWith("belt_")) return true;
            if (n.Contains("headset_cable") || n.Contains("headsetcord") || n.Contains("headset_cord") || n.Contains("headset_plug")) return true;
            return false;
        }

        /// <summary>Materials that are belts and nothing else, for meshes that also carry the rest of the cockpit (the Great
        /// Lakes' Cockpit_Interior, the Pitts' Cockpit). Exact names, lower case.</summary>
        private static readonly string[] BeltMaterials =
        {
            "cockpit_harness", "cockpit_lap_belt", "harness_webbing", "harness_release", "int_webbing", "belt", "headsetcord",
        };

        /// <summary>True for a material that draws only webbing / belt hardware. glTFast may suffix a duplicate (" (Instance)",
        /// ".001"), so the base name is compared.</summary>
        public static bool IsBeltMaterial(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            int cut = n.IndexOf(" (", StringComparison.Ordinal);
            if (cut > 0) n = n.Substring(0, cut);
            int dot = n.LastIndexOf('.');
            if (dot > 0 && dot < n.Length - 1 && char.IsDigit(n[dot + 1])) n = n.Substring(0, dot);
            foreach (string m in BeltMaterials) if (n == m) return true;
            return false;
        }
    }
}
