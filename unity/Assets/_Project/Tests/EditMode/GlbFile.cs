using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FlyingGame.EditModeTests
{
    /// <summary>
    /// A shipped GLB read the way the tests need it: its JSON, which nodes really draw something (a mesh with
    /// vertices on the node or under it — the Pitts keeps retired controls as empty nodes so no index moves), and
    /// a material's base-colour texture decoded by Unity's own PNG/JPEG reader.
    /// </summary>
    internal sealed class GlbFile
    {
        public readonly string Path;
        public readonly JObject Json;
        private readonly byte[] _bin;
        private readonly Dictionary<string, int> _byName = new();

        public static string Repo => System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", ".."));
        public static string Models => System.IO.Path.Combine(Application.dataPath, "_Project", "Resources", "Models");

        public GlbFile(string path)
        {
            Path = path;
            byte[] d = File.ReadAllBytes(path);
            Assert.AreEqual(0x46546C67u, BitConverter.ToUInt32(d, 0), path + ": not a GLB");
            int n = BitConverter.ToInt32(d, 12);
            Json = JObject.Parse(System.Text.Encoding.UTF8.GetString(d, 20, n));
            int off = 20 + n;
            _bin = off + 8 <= d.Length ? new ArraySegment<byte>(d, off + 8, BitConverter.ToInt32(d, off)).ToArray() : Array.Empty<byte>();
            var nodes = (JArray)Json["nodes"];
            for (int i = 0; i < nodes.Count; i++) _byName[(string)nodes[i]["name"] ?? ""] = i;
        }

        public JArray Nodes => (JArray)Json["nodes"];
        public JArray Materials => (JArray)Json["materials"] ?? new JArray();
        public bool Has(string node) => _byName.ContainsKey(node);
        public IEnumerable<string> NodeNames => _byName.Keys;

        /// <summary>Vertices drawn by the node and everything under it.</summary>
        public int Vertices(string node) => _byName.TryGetValue(node, out int i) ? Vertices(i) : 0;

        private int Vertices(int i)
        {
            JToken n = Nodes[i];
            int v = 0;
            if (n["mesh"] != null)
                foreach (JToken p in (JArray)Json["meshes"][(int)n["mesh"]]["primitives"])
                    v += (int)Json["accessors"][(int)p["attributes"]["POSITION"]]["count"];
            if (n["children"] is JArray kids) foreach (JToken c in kids) v += Vertices((int)c);
            return v;
        }

        /// <summary>The material's base-colour texture, decoded, or null when it has none.</summary>
        public Texture2D BaseColour(JToken material)
        {
            JToken tex = material["pbrMetallicRoughness"]?["baseColorTexture"]?["index"];
            if (tex == null) return null;
            JToken image = Json["images"][(int)Json["textures"][(int)tex]["source"]];
            JToken view = Json["bufferViews"][(int)image["bufferView"]];
            int start = (int?)view["byteOffset"] ?? 0, len = (int)view["byteLength"];
            var bytes = new byte[len];
            Buffer.BlockCopy(_bin, start, bytes, 0, len);
            var t = new Texture2D(2, 2);
            Assert.IsTrue(t.LoadImage(bytes), $"{System.IO.Path.GetFileName(Path)}: texture of {material["name"]} does not decode");
            return t;
        }

        /// <summary>The commonest colour of the opaque pixels, each channel quantised to 8 (antialiasing and JPEG noise
        /// spread a flat face over neighbouring values).</summary>
        public static Color32 Dominant(Texture2D t)
        {
            var count = new Dictionary<int, int>();
            int best = 0, bestKey = 0;
            foreach (Color32 c in t.GetPixels32())
            {
                if (c.a < 128) continue;
                int key = (c.r >> 3) << 10 | (c.g >> 3) << 5 | (c.b >> 3);
                count.TryGetValue(key, out int k);
                count[key] = ++k;
                if (k > best) { best = k; bestKey = key; }
            }
            return new Color32((byte)((bestKey >> 10) << 3), (byte)(((bestKey >> 5) & 31) << 3), (byte)((bestKey & 31) << 3), 255);
        }

        public static int Distance(Color32 a, Color32 b) =>
            Math.Max(Math.Abs(a.r - b.r), Math.Max(Math.Abs(a.g - b.g), Math.Abs(a.b - b.b)));
    }
}
