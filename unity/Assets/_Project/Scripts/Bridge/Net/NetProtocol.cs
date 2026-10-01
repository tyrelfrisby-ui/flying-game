using Newtonsoft.Json;
using UnityEngine.Scripting;

namespace FlyingGame.Bridge.Net
{
    /// <summary>
    /// Wire format shared with server/relay (compact JSON, 10 Hz). One flat message class covers every
    /// packet type; unused fields stay null and are omitted on send. Newtonsoft fills these by reflection,
    /// so the types are [Preserve]d (and listed in link.xml) against IL2CPP stripping.
    /// </summary>
    [Preserve]
    public sealed class NetFlags
    {
        public int w;   // 1 = wings gone (StructuralDamage.WingsGone)
        public int o;   // 1 = pilot out (PilotEgress.PilotOut)
    }

    /// <summary>A pilot's state: pose in Unity world axes (all clients share WorldTerrain), velocity,
    /// deflections [aileronRad, elevatorRad, rudderRad, spoilerFrac, flapFrac], flags.</summary>
    [Preserve]
    public sealed class NetState
    {
        public float[] p;   // position x,y,z
        public float[] q;   // rotation x,y,z,w
        public float[] v;   // world velocity m/s
        public float[] d;   // deflections
        public NetFlags f;
    }

    [Preserve]
    public sealed class NetPeer
    {
        public string id;
        public string name;
        public string ac;
        public NetState s;  // last known state (late-joiner roster), may be null
    }

    [Preserve]
    public sealed class NetMsg
    {
        public string t;        // "hello" | "peer" | "bye" | "s" | "join" | "tune" | "v"
        public string id;
        public string name;
        public string ac;
        public string room;
        public float[] p, q, v, d;
        public NetFlags f;
        public NetPeer[] peers;
        public string fq;       // radio frequency ("tune", and on received voice)
        public string a;        // voice: base64 8 kHz mu-law
        public int? e;          // voice: 1 = the sender unkeyed (end of transmission)

        public static readonly JsonSerializerSettings Settings = new()
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore,
        };

        public string ToJson() => JsonConvert.SerializeObject(this, Settings);
        public static NetMsg FromJson(string json) => JsonConvert.DeserializeObject<NetMsg>(json, Settings);

        public NetState AsState() => new() { p = p, q = q, v = v, d = d, f = f };
    }
}
