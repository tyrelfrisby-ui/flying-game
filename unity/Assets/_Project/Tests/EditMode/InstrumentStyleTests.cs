using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FlyingGame.EditModeTests
{
    /// <summary>
    /// Ty 2026-11-04 19:22: "Make all instruments on all airplanes the same bold high-contrast style like the Extra;
    /// Cub keeps cream faces." The style is ONE generator, tools/own_models/instrument_face_style.py, and these hold
    /// every shipped face to it, by what the face IS (its dominant paint, decoded from the asset the game loads) as
    /// well as by the marker the generator leaves:
    ///
    ///  - every dial face material in every one of our own GLBs carries <c>instrumentFaceStyle: 1</c> and is painted
    ///    the style's black (8, 8, 8) — or its cream (245, 232, 195) on the J-3 and the PA-18;
    ///  - the runtime dial art under Resources/CockpitDials and the PA-18's downloaded dial maps are byte-for-byte
    ///    what the generator wrote (its manifest), the dial art black, the PA-18's cream;
    ///  - the floating gauges (AnalogGauges, every type without a modelled panel) read the same palette.
    ///
    /// The palette and the model list are literals, copied by hand (CLAUDE.md: a test that reads the file it is checking
    /// proves nothing). Run: Unity -batchmode -runTests -testPlatform EditMode.
    /// </summary>
    public sealed class InstrumentStyleTests
    {
        private static readonly Color32 Black = new(8, 8, 8, 255), Cream = new(245, 232, 195, 255), White = new(255, 255, 255, 255);
        private const int Tolerance = 12;

        /// <summary>The cream exception: the J-3s and the PA-18s, by model folder (AirframeModels' model ids).</summary>
        private static bool CreamModel(string folder) => folder.StartsWith("j3-") || folder.StartsWith("pa18-");

        /// <summary>The generator's own rule for a dial face material (instrument_face_style.is_face), copied.</summary>
        private static bool IsFace(string n) =>
            ((n.StartsWith("Dial_") || n.StartsWith("P3b_Dial_") || n.StartsWith("Compass_Card") || n.StartsWith("Compass_Face")
              || n.StartsWith("FrontPanel_")) && (n.Contains("Face") || !n.StartsWith("FrontPanel_")))
            || (n.StartsWith("RearPanel_") && n.EndsWith("_Face"))
            || n == "Cockpit_gauge_face" || n == "Gauge_face" || n == "Panel_gauge_faces";

        /// <summary>Every model with a modelled instrument panel, and the fewest faces each must show, so a model whose
        /// faces were renamed out of the rule fails here instead of passing with nothing checked.</summary>
        private static readonly (string File, int MinFaces)[] Panels =
        {
            ("pitts-s2b-like/pitts-s2b-like-own.glb", 8), ("pitts-s2b-like/pitts-s2b-like-own-n1191.glb", 8),
            ("pitts-s2b-like/pitts-s2b-like-own-checker-unmarked.glb", 8), ("pitts-s2b-like/pitts-s2b-like-own-factory-unmarked.glb", 8),
            ("extra-300-like/extra-300-like-own.glb", 30), ("extra-300-like/extra-300-like-own-n300uy.glb", 30),
            ("extra-300-like/extra-300-like-own-unmarked.glb", 30),
            ("great-lakes-like/great-lakes-like-own.glb", 15), ("great-lakes-like/great-lakes-like-own-red-sunburst.glb", 1),
            ("great-lakes-like/great-lakes-like-own-white-stripes.glb", 1),
            ("glider-2-33-like/glider-2-33-like-own.glb", 4), ("glider-2-33-like/glider-2-33-like-own-tf.glb", 4),
            ("j3-cub-like/j3-cub-like-own.glb", 2), ("j3-cub-floats-like/j3-cub-floats-like-own.glb", 2),
        };

        [Test]
        public void EveryModelledPanelFaceIsInTheSharedStyle()
        {
            var bad = new List<string>();
            foreach ((string file, int min) in Panels)
            {
                string folder = file.Split('/')[0];
                Color32 want = CreamModel(folder) ? Cream : Black;
                var g = new GlbFile(Path.Combine(GlbFile.Models, file));
                int faces = 0;
                foreach (JToken m in g.Materials)
                {
                    string name = (string)m["name"] ?? "";
                    if (!IsFace(name)) continue;
                    faces++;
                    if ((int?)m["extras"]?["instrumentFaceStyle"] != 1) { bad.Add($"{file}: {name} has no shared-style marker"); continue; }
                    Texture2D t = g.BaseColour(m);
                    Color32 paint = t != null ? GlbFile.Dominant(t) : Factor(m);
                    if (t != null) UnityEngine.Object.DestroyImmediate(t);
                    if (GlbFile.Distance(paint, want) > Tolerance) bad.Add($"{file}: {name} is painted {paint}, the style is {want}");
                }
                if (faces < min) bad.Add($"{file}: {faces} instrument faces found, expected at least {min}");
            }
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        /// <summary>The 172's panel is vector paint, not textures: its markings white and its faces black, both marked.</summary>
        [Test]
        public void TheC172PanelPaintIsTheSharedStyle()
        {
            var g = new GlbFile(Path.Combine(GlbFile.Models, "c172-like", "c172-like-own.glb"));
            var want = new Dictionary<string, Color32> { ["172_markings"] = White, ["172_instrument_black"] = Black };
            foreach (var kv in want)
            {
                JToken m = g.Materials.FirstOrDefault(x => (string)x["name"] == kv.Key);
                Assert.IsNotNull(m, kv.Key + " is gone from the 172");
                Assert.AreEqual(1, (int?)m["extras"]?["instrumentFaceStyle"], kv.Key + " has no shared-style marker");
                Assert.LessOrEqual(GlbFile.Distance(Factor(m), kv.Value), 2, $"{kv.Key} is {Factor(m)}");
            }
        }

        /// <summary>The live-dial art the game lays over the Pitts' and the Extra's faces (CockpitLive) and the PA-18's
        /// downloaded dial maps are exactly the files the generator wrote, and painted its black / its cream.</summary>
        [Test]
        public void TheRuntimeDialArtAndThePa18MapsAreTheGeneratorsOwn()
        {
            string manifestPath = Path.Combine(GlbFile.Repo, "tools", "own_models", "instrument-style-manifest.json");
            var manifest = JObject.Parse(File.ReadAllText(manifestPath));
            var files = new List<(string Key, string Path, Color32 Want)>();
            string dials = Path.Combine(Application.dataPath, "_Project", "Resources", "CockpitDials");
            foreach (string name in new[] { "ea-dial-alt", "ea-dial-asi", "ea-dial-fuel", "ea-dial-g", "ea-dial-mp", "ea-dial-oil",
                                            "ea-dial-tach", "gl-dial-asi", "live-compass-card" })
                files.Add((name + ".png", Path.Combine(dials, name + ".png"), Black));
            files.Add(("live-attitude.png", Path.Combine(dials, "live-attitude.png"), default));   // a horizon card: marker only
            foreach (string k in new[] { "alt", "asi", "hdg", "turn", "vsi" })
            {
                string name = $"DefaultWhite_{k}.png_baseColor.jpg";
                files.Add(("pa18-cub-like/" + name, Path.Combine(GlbFile.Models, "pa18-cub-like", name), Cream));
            }
            var bad = new List<string>();
            using var sha = SHA256.Create();
            foreach ((string key, string path, Color32 want) in files)
            {
                byte[] bytes = File.ReadAllBytes(path);
                string hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                if ((string)manifest[key] != hash) { bad.Add($"{key}: not the generator's output (re-run style_fleet_instruments.py)"); continue; }
                if (want.a == 0) continue;
                var t = new Texture2D(2, 2);
                Assert.IsTrue(t.LoadImage(bytes), key + " does not decode");
                Color32 paint = GlbFile.Dominant(t);
                UnityEngine.Object.DestroyImmediate(t);
                if (GlbFile.Distance(paint, want) > Tolerance) bad.Add($"{key} is painted {paint}, the style is {want}");
            }
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        /// <summary>The floating gauges (AnalogGauges) paint with the generator's palette, which it writes into Core.</summary>
        [Test]
        public void TheFloatingGaugesReadTheSamePalette()
        {
            Assert.AreEqual(0x080808u, FlyingGame.Core.InstrumentFaces.BlackPaint);
            Assert.AreEqual(0xFFFFFFu, FlyingGame.Core.InstrumentFaces.WhitePaint);
            Assert.AreEqual(0xF5E8C3u, FlyingGame.Core.InstrumentFaces.CreamPaint);
            Assert.AreEqual(0x050505u, FlyingGame.Core.InstrumentFaces.InkPaint);
        }

        private static Color32 Factor(JToken m)
        {
            JArray f = m["pbrMetallicRoughness"]?["baseColorFactor"] as JArray;
            if (f == null) return new Color32(255, 255, 255, 255);
            byte B(int i) => (byte)Mathf.Clamp(Mathf.RoundToInt((float)f[i] * 255f), 0, 255);
            return new Color32(B(0), B(1), B(2), 255);
        }
    }
}
