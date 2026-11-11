using System.Collections.Generic;
using Aero.Wake;
using FlyingGame.Bridge;
using FlyingGame.Core;
using FlyingGame.Core.Weather;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlyingGame.Clouds
{
    /// <summary>
    /// Thin ground fog over grass strips, lakes and valley floors (Ty 2026-11-05 08:39; sites: <see cref="CloudSites.Fog"/>;
    /// docs/CLOUDS-WAKE-VISUALS.md "Ground fog"). WHETHER a site has fog, and how thick, is the island weather's call
    /// (Ty 08:41; <see cref="IslandWeather.FogAt"/>, docs/weather-system.md "Fog"), re-read every
    /// <see cref="WeatherPollSec"/>: valleys on a calm, clear, humid morning, gone once the sun or the wind has had it.
    /// Every site is one box drawn by FlyingGame/GroundFog. The site nearest
    /// the eye carries a 384 m wake-stirred patch: eight slices of fog density advected (MacCormack) through the real wake
    /// (<see cref="WakeField.WriteVolumeToTexture"/>) by Hidden/FlyingGame/GroundFogAdvect at 15 Hz, the wake re-baked
    /// at 7.5 Hz. The fog is only a tracer; what it shows is what the aeroplanes' lift and thrust did to the air.
    /// iOS, the Mac and the Quest run exactly this.
    /// </summary>
    public sealed class GroundFog : MonoBehaviour
    {
        public const int Resolution = 256, WakeTile = 128, Slices = 8;
        public const float PatchSize = 384f, StepSec = 1f / 15f, RelaxSec = 150f;
        /// <summary>Sim box height over the fog's own depth: the fog's top sits near half the box, so the vortices can
        /// lift it into the clear air above.</summary>
        public const float BoxOverDepth = 2.2f;
        /// <summary>The patch runs only while the eye is this near a site (m, beyond its half-length) and this low.</summary>
        public const float ActiveRangeM = 1500f, ActiveHeightM = 700f;
        /// <summary>Whole sites are not drawn past this (m). The one scale owns it (GraphicsQuality.Knobs
        /// .GroundFogDrawRangeM): 14 km at High, as it always was, and 0 - no fog at all - at Minimum.</summary>
        public static float DrawRangeM => (float)GraphicsQualityDirector.Knobs.GroundFogDrawRangeM;
        /// <summary>How far ahead of the eye, along its horizontal look, the patch is centred (m).</summary>
        public const float LookAheadM = 80f;
        /// <summary>How often the weather's fog amount per site is re-read (s), and the least amount drawn.</summary>
        public const float WeatherPollSec = 5f, MinAmount = 0.02f;
        /// <summary>Editor tools: a fixed fog amount for every site (negative = the island weather decides).</summary>
        public static float AmountOverride = -1f;

        public Camera viewCamera;
        readonly List<Object> owned = new List<Object>();
        readonly List<Renderer> boxes = new List<Renderer>();
        readonly List<Material> mats = new List<Material>();
        readonly List<CloudSites.FogSite> sites = new List<CloudSites.FogSite>();
        readonly List<float> amounts = new List<float>();
        float nextPoll = float.MinValue;
        RenderTexture[] fogA = new RenderTexture[2], fogB = new RenderTexture[2];
        RenderTexture wake, hatA, hatB;
        Material advect;
        int active = -1, tick;
        bool initialized, built;
        Vector2 origin, drift;
        float nextStep;

        /// <summary>Fog sites drawn, and the one carrying the stirred patch (-1 none).</summary>
        public int SiteCount => sites.Count;
        public int ActiveSite => active;
        public CloudSites.FogSite Site(int i) => sites[i];
        /// <summary>The fog amount (0..1) the weather gave site i at the last poll.</summary>
        public float Amount(int i) => amounts[i];
        public RenderTexture WakeAtlasForTesting => wake;
        /// <summary>The active patch: origin X/Z, size, box height (the render material's _FogPatch).</summary>
        public Vector4 PatchForTesting => active >= 0 ? mats[active].GetVector("_FogPatch") : Vector4.zero;
        public RenderTexture SlicesForTesting(int half) => half == 0 ? fogA[0] : fogB[0];

        public static GroundFog Attach(Transform parent)
        {
            var go = new GameObject("GroundFog");
            go.transform.SetParent(parent, false);
            var f = go.AddComponent<GroundFog>();
            f.Build();
            return f;
        }

        T Own<T>(T item) where T : Object { owned.Add(item); return item; }
        RenderTexture Target(int w, int h, string label)
        {
            var rt = Own(new RenderTexture(w, h, 0, RenderTextureFormat.ARGBHalf) {
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
                useMipMap = false, autoGenerateMips = false, name = label });
            rt.Create(); return rt;
        }

        static float BoxHeight(CloudSites.FogSite s) => (float)s.DepthM * BoxOverDepth;

        public void Build()
        {
            if (built) return;
            Shader draw = Shader.Find("FlyingGame/GroundFog"), adv = Shader.Find("Hidden/FlyingGame/GroundFogAdvect");
            if (draw == null || adv == null) { Debug.LogError("[Fog] FlyingGame/GroundFog or its advection shader is missing from the build: no ground fog"); return; }
            built = true;
            advect = Own(new Material(adv));
            var cube = Own(CubeMesh());
            foreach (CloudSites.FogSite s in CloudSites.Fog)
            {
                sites.Add(s);
                amounts.Add(0f);
                var m = Own(new Material(draw));
                SetSite(m, s);
                m.SetVector("_FogPatch", new Vector4(0, 0, 0, BoxHeight(s)));
                mats.Add(m);
                var go = new GameObject("Ground fog " + s.Id);
                go.transform.SetParent(transform, false);
                float h = BoxHeight(s);
                // The box: the site's extent plus the edge noise's reach, from 1 m under the surface to the box top.
                go.transform.SetPositionAndRotation(new Vector3((float)s.X, (float)s.SurfaceM + h * .5f - .5f, (float)s.Z),
                    Quaternion.Euler(0, (float)s.HeadingDeg, 0));
                go.transform.localScale = new Vector3((float)s.HalfWidth * 2 + 80, h + 1, (float)s.HalfLength * 2 + 80);
                go.AddComponent<MeshFilter>().sharedMesh = cube;
                var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m;
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                boxes.Add(r);
            }
            for (int i = 0; i < 2; i++)
            {
                fogA[i] = Target(Resolution, Resolution, "Ground fog slices 0-3");
                fogB[i] = Target(Resolution, Resolution, "Ground fog slices 4-7");
            }
            hatA = Target(Resolution, Resolution, "Ground fog predicted 0-3");
            hatB = Target(Resolution, Resolution, "Ground fog predicted 4-7");
            wake = Target(WakeTile * 4, WakeTile * 2, "Ground fog wake 4x2");
            Debug.Log($"[Fog] {sites.Count} places fog can lie: {string.Join(", ", sites.ConvertAll(s => s.Id))}");
        }

        /// <summary>Re-read every site's fog amount from the island weather (at the sim clock).</summary>
        void PollWeather()
        {
            IslandWeather w = IslandWeather.Current;
            var foggy = new List<string>();
            for (int i = 0; i < sites.Count; i++)
            {
                CloudSites.FogSite s = sites[i];
                float a = AmountOverride >= 0 ? AmountOverride : (float)w.FogAt(s.Z, s.X, Atmosphere.SimTimeSec).density;   // sim (north, east)
                if (a < MinAmount) a = 0;
                if ((a > 0) != (amounts[i] > 0) && a > 0) foggy.Add(s.Id);
                amounts[i] = a;
                mats[i].SetFloat("_FogAmount", a);
            }
            if (active >= 0) advect.SetFloat("_FogAmount", amounts[active]);
            if (foggy.Count > 0) Debug.Log($"[Fog] fog formed at {string.Join(", ", foggy)} (local {w.LocalHour(Atmosphere.SimTimeSec):F1} h)");
        }

        static void SetSite(Material m, CloudSites.FogSite s)
        {
            float h = (float)s.HeadingDeg * Mathf.Deg2Rad;
            m.SetVector("_FogSite", new Vector4((float)s.X, (float)s.Z, Mathf.Sin(h), Mathf.Cos(h)));
            m.SetVector("_FogHalf", new Vector4((float)s.HalfLength, (float)s.HalfWidth, s.Lake ? 1 : 0, 0));
            m.SetFloat("_FogSurface", (float)s.SurfaceM);
        }

        static Mesh CubeMesh()
        {
            var m = new Mesh { name = "Ground fog box" };
            var v = new List<Vector3>();
            for (int i = 0; i < 8; i++) v.Add(new Vector3((i & 1) - .5f, ((i >> 1) & 1) - .5f, ((i >> 2) & 1) - .5f));
            m.SetVertices(v);
            // Outward faces; the shader draws the back ones (Cull Front).
            m.SetTriangles(new[] {
                0,2,1, 1,2,3,   4,5,6, 5,7,6,   0,1,4, 1,5,4,   2,6,3, 3,6,7,   0,4,2, 2,4,6,   1,3,5, 3,7,5 }, 0);
            m.RecalculateBounds();
            return m;
        }

        void LateUpdate()
        {
            if (!viewCamera) viewCamera = Camera.main;
            Step(viewCamera, Time.time, Time.deltaTime);
        }

        /// <summary>The frame, for an editor tool (LateUpdate does not run in edit mode).</summary>
        public void StepForTesting(Camera cam, float now, float dt) { viewCamera = cam; Step(cam, now, dt); }

        void Step(Camera cam, float now, float dt)
        {
            if (!built || !cam) return;
            if (now >= nextPoll || now < nextPoll - WeatherPollSec * 2) { nextPoll = now + WeatherPollSec; PollWeather(); }
            Vector3 eye = cam.transform.position;
            // The fog drifts with the surface wind (half the wind aloft: it is the slow air at the bottom of the layer).
            Vector3 w = CoordinateMap.ToUnity(Atmosphere.MeanWindAtPosition(CoordinateMap.ToSim(new Vector3(eye.x, 10f, eye.z))));
            Vector2 wind = new Vector2(w.x, w.z) * .5f;
            drift += wind * dt;
            Shader.SetGlobalVector("_FogDrift", new Vector4(drift.x, drift.y, 0, 0));

            int nearest = -1; float best = float.MaxValue;
            for (int i = 0; i < sites.Count; i++)
            {
                CloudSites.FogSite s = sites[i];
                float d = new Vector2(eye.x - (float)s.X, eye.z - (float)s.Z).magnitude;
                boxes[i].enabled = d < DrawRangeM && amounts[i] > 0;
                float reach = d - (float)s.HalfLength;
                if (amounts[i] > 0 && reach < ActiveRangeM && eye.y - (float)s.SurfaceM < ActiveHeightM && reach < best) { best = reach; nearest = i; }
            }
            if (nearest != active)
            {
                if (active >= 0) mats[active].SetVector("_FogPatch", new Vector4(0, 0, 0, BoxHeight(sites[active])));
                active = nearest; initialized = false;
                if (active >= 0) { SetSite(advect, sites[active]); advect.SetFloat("_FogAmount", amounts[active]); }
            }
            if (active < 0 || now < nextStep) return;
            nextStep = now + StepSec;      // at most one step per tick; never catch up after a stalled frame
            CloudSites.FogSite site = sites[active];
            float hBox = BoxHeight(site);
            // The patch follows the eye, a little ahead of where it looks (what is in view), held over the site,
            // snapped to 8 texels.
            Vector3 look = cam.transform.forward; look.y = 0;
            Vector3 at = eye + (look.sqrMagnitude > 1e-4f ? look.normalized * LookAheadM : Vector3.zero);
            float snap = PatchSize / Resolution * 8;
            float ext = (float)System.Math.Max(site.HalfLength, site.HalfWidth) + 60;
            float cx = Mathf.Clamp(at.x, (float)site.X - ext, (float)site.X + ext), cz = Mathf.Clamp(at.z, (float)site.Z - ext, (float)site.Z + ext);
            Vector2 o = new Vector2(Mathf.Floor(cx / snap) * snap - PatchSize / 2, Mathf.Floor(cz / snap) * snap - PatchSize / 2);
            Vector2 shift = initialized ? (o - origin) / PatchSize : Vector2.zero;
            if ((tick++ & 1) == 0 || !initialized)
                WakeField.WriteVolumeToTexture(wake, new Bounds(new Vector3(o.x + PatchSize / 2, (float)site.SurfaceM + hBox / 2, o.y + PatchSize / 2),
                    new Vector3(PatchSize, hBox, PatchSize)), 4, 2);
            advect.SetTexture("_FogA", fogA[0]); advect.SetTexture("_FogB", fogB[0]); advect.SetTexture("_WakeAtlas", wake);
            advect.SetVector("_FogPatch", new Vector4(o.x, o.y, PatchSize, hBox));
            advect.SetVector("_FogShift", new Vector4(shift.x, shift.y, StepSec, initialized ? 0 : 1));
            advect.SetVector("_FogWind", new Vector4(wind.x, wind.y, .5f / WakeTile, RelaxSec));
            advect.SetVector("_FogTexel", new Vector4(1f / Resolution, 1f / Resolution, 0, 0));
            // MacCormack (GroundFogAdvect): predict, then correct by half the round trip's error.
            Graphics.Blit(null, hatA, advect, 0);
            Graphics.Blit(null, hatB, advect, 1);
            advect.SetTexture("_HatA", hatA); advect.SetTexture("_HatB", hatB);
            Graphics.Blit(null, fogA[1], advect, 2);
            Graphics.Blit(null, fogB[1], advect, 3);
            (fogA[0], fogA[1]) = (fogA[1], fogA[0]);
            (fogB[0], fogB[1]) = (fogB[1], fogB[0]);
            origin = o; initialized = true;
            Material m = mats[active];
            m.SetTexture("_FogA", fogA[0]); m.SetTexture("_FogB", fogB[0]);
            m.SetVector("_FogPatch", new Vector4(o.x, o.y, PatchSize, hBox));
        }

        void OnDestroy()
        {
            foreach (var item in owned) { if (item is RenderTexture rt) rt.Release(); if (item) { if (Application.isPlaying) Destroy(item); else DestroyImmediate(item); } }
            owned.Clear();
        }
    }
}
